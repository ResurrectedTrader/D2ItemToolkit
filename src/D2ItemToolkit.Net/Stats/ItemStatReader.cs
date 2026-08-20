using System;
using System.Collections.Generic;

namespace D2ItemToolkit
{
    internal static class ItemStatReader
    {
        // A unit document is self-similar: identity fields, `statsLists` and `sockets`,
        // where each socket entry is another unit document and its POSITION is the socket index. An
        // item and a player are both D2UnitStrc, so both serialise to this same shape.
        //
        // There is no per-socket view, and no socket index anywhere: to describe one filler, take
        // EnumerateSockets().ElementAt(n) and view THAT record with ItemOnly(). Self-similarity
        // means the whole reader already works on it.

        // (layer << 16) | stat — LAYER-major, which is the MIRROR of the engine's own packing.
        // D2SLayerStatIdStrc is { uint16 nLayer @0x00; uint16 nStat @0x02 }, so a captured
        // nPackedValue is (stat << 16) | layer. A key from here is NOT comparable with one of those;
        // convert with ((p & 0xFFFF) << 16) | (p >> 16). Layer-major is kept because it sorts by
        // layer, which is the order the description engine consumes entries in.
        public static int PackStatKey(int layer, int stat)
        {
            unchecked
            {
                return (int)(((uint)(layer & 0xFFFF) << 16) | (uint)(stat & 0xFFFF));
            }
        }

        public static int StatFromKey(int key)
        {
            return key & 0xFFFF;
        }

        /// <summary>
        /// Both halves of a packed key. Not a Try- method: every 32-bit key unpacks, so the bool it
        /// used to return was always true and its caller carried a disjunct that could never fire.
        /// The TypeScript peer returns { layer, stat } for the same reason.
        /// </summary>
        public static void UnpackStatKey(int key, out int layer, out int stat)
        {
            layer = LayerFromKey(key);
            stat = StatFromKey(key);
        }

        public static int LayerFromKey(int key)
        {
            unchecked
            {
                return (int)((uint)key >> 16);
            }
        }


        public static SortedDictionary<int, int> ReconstructView(IUnit record, ItemStatView view)
        {
            if (record == null) throw new ArgumentNullException("record");


            var merged = new SortedDictionary<int, int>();

            foreach (ItemStatGroup group in EnumerateGroups(record))
            {
                if (view.ExcludedFlags != 0 && (group.Flags & view.ExcludedFlags) != 0)
                {
                    continue;
                }

                if (group.FromSocket && !view.IncludeSockets)
                {
                    continue;
                }

                if (view.RequiredFlags != 0 && (group.Flags & view.RequiredFlags) == 0)
                {
                    continue;
                }

                if (view.AllowedStates != null
                    && Array.IndexOf(view.AllowedStates, group.StateNo) < 0)
                {
                    continue;
                }

                if (view.ExcludedStates != null
                    && Array.IndexOf(view.ExcludedStates, group.StateNo) >= 0)
                {
                    continue;
                }

                foreach (IUnitStat stat in group.Stats)
                {
                    int key = PackStatKey(stat.Layer, stat.Id);

                    int existing;
                    merged[key] = merged.TryGetValue(key, out existing)
                        ? existing + stat.Value
                        : stat.Value;
                }
            }

            return merged;
        }

        /// <summary>
        /// This record's own groups followed by its sockets', each tagged with whether it was
        /// reached through a socket so a view can drop the fillers.
        /// </summary>
        public static IEnumerable<ItemStatGroup> EnumerateGroups(IUnit record)
        {
            if (record == null) throw new ArgumentNullException("record");

            return EnumerateGroups(record, false);
        }

        private static IEnumerable<ItemStatGroup> EnumerateGroups(IUnit record, bool fromSocket)
        {
            foreach (IUnitStatList group in record.StatsLists)
            {
                yield return new ItemStatGroup(group, fromSocket);
            }

            foreach (IUnit socket in EnumerateSockets(record))
            {
                foreach (ItemStatGroup group in EnumerateGroups(socket, true))
                {
                    yield return group;
                }
            }
        }

        /// <summary>
        /// The socket records in index order. Position IS the index: the producer sorts by the
        /// ordinal INVENTORY_PlaceItemInSocket assigned, which is contiguous from 0.
        /// </summary>
        public static IEnumerable<IUnit> EnumerateSockets(IUnit record)
        {
            if (record == null) throw new ArgumentNullException("record");

            return record.Sockets;
        }

        /// <summary>Socket index to the filler's classId, for the writers that only need that.</summary>
        public static SortedDictionary<int, uint> ReadSockets(IUnit record)
        {
            var sockets = new SortedDictionary<int, uint>();
            int index = 0;
            foreach (IUnit socket in EnumerateSockets(record))
            {
                // The document's two fallbacks for a missing classId differ: identity wants -1
                // ("no such row"), this map wants 0. Keep the 0 — a negative would widen to
                // 0xFFFFFFFF and index nothing.
                sockets[index] = socket.ClassId < 0 ? 0u : (uint)socket.ClassId;
                ++index;
            }

            return sockets;
        }
    }

    // The three wire names UnitJson reads by hand. The other six the producer emits — statsLists,
    // stats, sockets, stateNo, flags, classId — are matched by System.Text.Json's camelCase policy
    // against the DTO's property names, so a constant for them checked nothing and none was
    // referenced. They also claimed to mirror producer/ItemStatStorage.h and had already drifted
    // from it in both directions, which is worse than not claiming it.
    internal static class ItemStatKeys
    {
        public const string StatId = "id";
        public const string StatLayer = "layer";
        public const string StatValue = "value";
    }

    /// <summary>D2C_StatlistFlags (D2StatList.h). MOO's names, not ours.</summary>
    public static class ItemStatListFlags
    {
        /// <summary>
        /// The bit GetStatList is asked for when the description engine collects a unit's mods
        /// (0x4e6438). A StatListEx header carries STATLIST_EXTENDED instead (0x6257dd), which is
        /// how the base array is distinguishable from the chain nodes.
        /// </summary>
        public const uint Magic = 0x40;

        /// <summary>
        /// Despite the name this does not mean "is a set bonus". It says the node is posted to
        /// the pMyStats chain rather than pMyLastList, where it contributes nothing
        /// (D2StatList.cpp:1083). D2Common_10574 (#10574) flips the bit and re-posts, so the bit
        /// and the chain never disagree — which is why the record stores no separate field for
        /// which chain a node was on.
        ///
        /// Set tiers are the bit's main user rather than its meaning. ItemMods.cpp:2335 creates
        /// STATE_ITEMSET1..6 as MAGIC|SET, so a tier starts out not contributing and the bit is
        /// cleared once the equipped count reaches it. An EARNED tier is therefore MAGIC-only and
        /// indistinguishable by flags from any other item mod: only its stateNo says it is a tier.
        /// </summary>
        public const uint Set = 0x2000;

        /// <summary>Marks the StatListEx header carrying the base array.</summary>
        public const uint Extended = 0x80000000;
    }

    /// <summary>D2C_States (D2States.h), which is sequential from STATE_NONE = 0.</summary>
    public static class ItemStatListStates
    {
        public const int None = 0;

        public const int ItemSet1 = 165;

        public const int ItemSet6 = 170;

        public const int Runeword = 171;
    }

    internal struct ItemStatView
    {
        /// <summary>False drops every group reached through a socket filler.</summary>
        public bool IncludeSockets;

        /// <summary>Any of these bits must be present. Zero means "do not filter on flags".</summary>
        public uint RequiredFlags;

        /// <summary>None of these bits may be present. Zero means "exclude nothing".</summary>
        public uint ExcludedFlags;

        /// <summary>Null means "do not filter on stateNo".</summary>
        public int[] AllowedStates;

        /// <summary>Null means "exclude no state".</summary>
        public int[] ExcludedStates;

        private static readonly int[] ModifierBlockStates =
        {
            ItemStatListStates.None,
            ItemStatListStates.Runeword,
        };

        // Since an earned tier drops STATLIST_SET, stateNo is the ONLY thing that still identifies
        // a set tier — a flag test cannot do it.
        private static readonly int[] SetTierStates = CreateSetTierStates();

        private static int[] CreateSetTierStates()
        {
            var states = new int[ItemStatListStates.ItemSet6 - ItemStatListStates.ItemSet1 + 1];
            for (int i = 0; i < states.Length; ++i)
            {
                states[i] = ItemStatListStates.ItemSet1 + i;
            }

            return states;
        }

        // A node the item itself grants, in either sense: STATLIST_EXTENDED is the header carrying
        // the base array, STATLIST_MAGIC is every affix / unique / setitems / runeword node.
        private const uint ItemOwn =
            ItemStatListFlags.Extended | ItemStatListFlags.Magic;

        /// <summary>
        /// What the blue modifier block is actually built from. SKILLDESC_AppendStatBuffText
        /// 0x4e6438 passes mask 0x40 and states 0 and 171, and the temp list receives exactly
        /// three kinds of node (0x4e6137 / 0x4e6154 / 0x4e61a0): the item's state-0 node, its
        /// runeword node, and one per socket filler.
        ///
        /// GetStatList 0x6257d0 walks the pMyLastList chain at +0x3C and keeps a node only when
        /// its stateNo matches AND `node->dwFlags &amp; mask` is non-zero (0x62580d). The base stat
        /// array lives at +0x24, is not in that chain, and carries STATLIST_EXTENDED rather than
        /// MAGIC, so base stats can never be described here. Set bonuses DO carry the 0x40 bit —
        /// what keeps them out is that they sit on states 165-170 and neither query asks for those.
        /// </summary>
        public static ItemStatView Modifiers()
        {
            ItemStatView view = Everything();
            view.RequiredFlags = ItemStatListFlags.Magic;
            view.ExcludedFlags = ItemStatListFlags.Set;
            view.AllowedStates = ModifierBlockStates;
            return view;
        }

        /// <summary>What the item is worth on its own: its base array and its own mods, no set tiers.</summary>
        public static ItemStatView ForSale()
        {
            ItemStatView view = Everything();
            view.RequiredFlags = ItemOwn;
            view.ExcludedFlags = ItemStatListFlags.Set;
            view.ExcludedStates = SetTierStates;
            return view;
        }

        /// <summary>What it is currently giving its wearer, so earned set tiers count too.</summary>
        public static ItemStatView Equipped()
        {
            ItemStatView view = ForSale();
            view.ExcludedStates = null;
            return view;
        }

        /// <summary>
        /// The set tiers on the item itself. An unearned tier still carries STATLIST_SET, an
        /// earned one has had it cleared, so the flag is exactly the earned/unearned test.
        /// </summary>
        public static ItemStatView SetBonuses(bool includeUnearned)
        {
            ItemStatView view = Everything();
            view.IncludeSockets = false;
            view.RequiredFlags = ItemStatListFlags.Magic;
            view.AllowedStates = SetTierStates;
            if (!includeUnearned)
            {
                view.ExcludedFlags = ItemStatListFlags.Set;
            }

            return view;
        }

        public static ItemStatView ItemOnly()
        {
            ItemStatView view = ForSale();
            view.IncludeSockets = false;
            return view;
        }

        /// <summary>
        /// The base array on the item itself and nothing else — what SERVER_GetUnitStat reads.
        /// INV_CalcWeaponDamageRange compares this against the merged value to decide whether a
        /// damage number has been modified (0x485300).
        /// </summary>
        public static ItemStatView BaseOnly()
        {
            ItemStatView view = Everything();
            view.IncludeSockets = false;
            view.RequiredFlags = ItemStatListFlags.Extended;
            view.ExcludedFlags = ItemStatListFlags.Set;
            return view;
        }

        public static ItemStatView Everything()
        {
            ItemStatView view;
            view.IncludeSockets = true;
            view.RequiredFlags = 0;
            view.ExcludedFlags = 0;
            view.AllowedStates = null;
            view.ExcludedStates = null;
            return view;
        }
    }

    internal struct ItemStatGroup
    {
        private readonly IUnitStatList _list;

        internal ItemStatGroup(IUnitStatList list, bool fromSocket)
        {
            _list = list;

            StateNo = list.StateNo;
            Flags = list.Flags;
            // Not stored on the group: it comes from which record we reached it through.
            FromSocket = fromSocket;
        }

        public int StateNo { get; private set; }

        public uint Flags { get; private set; }

        /// <summary>True when this group belongs to a socket filler rather than the item itself.</summary>
        public bool FromSocket { get; private set; }

        public IReadOnlyList<IUnitStat> Stats
        {
            get { return _list.Stats; }
        }

        public IEnumerable<KeyValuePair<int, int>> EnumerateStats()
        {
            foreach (IUnitStat stat in _list.Stats)
            {
                yield return new KeyValuePair<int, int>(
                    ItemStatReader.PackStatKey(stat.Layer, stat.Id), stat.Value);
            }
        }
    }
}
