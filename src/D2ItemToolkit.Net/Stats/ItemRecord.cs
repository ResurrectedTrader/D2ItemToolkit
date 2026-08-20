using System;
using System.Collections.Generic;

namespace D2ItemToolkit
{
    [Flags]
    public enum ItemRecordFlags : uint
    {
        None = 0,
        Identified = 0x00000010,
        Broken = 0x00000100,
        Socketed = 0x00000800,
        Named = 0x00008000,
        Personalized = 0x01000000,
        Ethereal = 0x00400000,
        Runeword = 0x04000000,
    }

    // What the item IS, as opposed to what stats it carries. These sit at the TOP of the unit
    // document, beside its stat lists.
    internal sealed class ItemIdentity
    {
        public int ClassId = -1;
        public string Code = string.Empty;
        public int Quality;
        public ItemRecordFlags Flags;
        public int FileIndex = -1;
        public int RarePrefix;
        public int RareSuffix;
        public int AutoAffix;

        // wItemFormat (+0x30). 0 is a classic item; ITEM_CalcRequiredLevel hides a classic unique's
        // level requirement from a non-expansion viewer (0x62b877).
        public int Format;

        public const int MaxAffixSlots = 3;
        public readonly int[] MagicPrefix = new int[MaxAffixSlots];
        public readonly int[] MagicSuffix = new int[MaxAffixSlots];
        public int EarLevel;
        public string PlayerName = string.Empty;

        /// <summary>bInvGfxIdx — see <see cref="IUnit.GfxIndex"/>.</summary>
        public int GfxIndex;

        public bool Has(ItemRecordFlags flag)
        {
            return (Flags & flag) != 0;
        }
    }

    /// <summary>
    /// A whole item unit — identity, its OWN stats, and its own sockets. The unit document is
    /// self-similar, so a socket filler has exactly this shape too, which is what
    /// ITEM_CalcRequiredLevel's recursion at 0x62b901 walks.
    /// </summary>
    internal sealed class ItemUnit
    {
        public readonly ItemIdentity Identity;
        public readonly IDictionary<int, int> Stats;
        public readonly IList<ItemUnit> Sockets;

        public ItemUnit(
            ItemIdentity identity,
            IDictionary<int, int> stats = null,
            IList<ItemUnit> sockets = null)
        {
            Identity = identity;
            Stats = stats ?? new Dictionary<int, int>();
            Sockets = sockets;
        }
    }

    internal sealed class ItemViewer
    {
        public int UnitType = -1;
        public int ClassId = -1;

        // Derived from the viewer's own stat lists, not stated: level is stat 12, strength 0,
        // dexterity 2 — exactly what STATLIST_UnitGetStatValue reads.
        public int Level;
        public int Strength;
        public int Dexterity;

        /// <summary>
        /// dwFlagEx verbatim. UNITFLAGEX_ISEXPANSION (0x2000000) is the only bit the description
        /// engine reads (0x62b877); an absent field defaults to having it, because an expansion
        /// character is the normal case and a missing flag should not silently hide unique level
        /// requirements.
        /// </summary>
        public uint FlagsEx = UnitFlagExpansion;

        public const uint UnitFlagExpansion = 0x02000000;

        public bool IsExpansion { get { return (FlagsEx & UnitFlagExpansion) != 0; } }

        /// <summary>The unit's own skills and their bonused levels, by skill id.</summary>
        public readonly Dictionary<int, int> Skills = new Dictionary<int, int>();

        /// <summary>
        /// The viewer's merged stats, packed layer-major. The op 2-5 scaling reads the PLAYER, not
        /// the item: SKILLDESC_CalcStatGroupValue 0x4e4c50 calls
        /// GetStatUnsignedValue(GetPlayerUnit(), opBase, 0) at 0x4e4c93/0x4e4c99. `opBase` is 12
        /// (level) on every shipped row, but it is a column, so the lookup has to be by stat id.
        /// </summary>
        public readonly Dictionary<int, int> Stats = new Dictionary<int, int>();

        /// <summary>
        /// Layer 0 of the named stat, or 0 when absent. GetStatUnsignedValue 0x625483 returns 0 for
        /// a null unit rather than halting, so a viewer-less tooltip scales by zero and still emits
        /// the line — the zero filter at 0x4e628b tests the STORED value, ahead of the scaling call.
        /// </summary>
        public int Stat(int statId)
        {
            int value;
            return Stats.TryGetValue(ItemStatReader.PackStatKey(0, statId), out value) ? value : 0;
        }

        public int SkillLevel(int skillId)
        {
            int level;
            return Skills.TryGetValue(skillId, out level) ? level : 0;
        }

        /// <summary>
        /// A state is a stat list carrying its own dwStateNo, so this is read off the stat lists
        /// rather than stated (0x485dda tests state 101 for Holy Shield).
        /// </summary>
        public readonly HashSet<int> ActiveStates = new HashSet<int>();

        // LoadItemDesc gates Smite and Kick on dwClassId alone (0x48e75c / 0x48e7c7) without
        // checking dwUnitType, so a monster with class id 3 or 6 false-positives on a mercenary
        // tooltip. Consumers should require IsPlayer rather than reproduce that.
        public bool IsPlayer { get { return UnitType == 0; } }
    }


    internal static class ItemRecordReader
    {


        public static ItemIdentity ReadIdentity(IUnit record)
        {
            if (record == null) throw new ArgumentNullException("record");

            var identity = new ItemIdentity();
            identity.ClassId = record.ClassId;
            identity.Code = record.Code ?? string.Empty;
            identity.Quality = record.Quality;
            identity.Flags = record.ItemFlags;
            identity.FileIndex = record.FileIndex;
            identity.RarePrefix = record.RarePrefix;
            identity.RareSuffix = record.RareSuffix;
            identity.AutoAffix = record.AutoAffix;
            identity.Format = record.Format;
            identity.EarLevel = record.EarLevel;
            identity.PlayerName = record.PlayerName ?? string.Empty;
            identity.GfxIndex = record.GfxIndex;

            // Copied element-wise rather than Array.Copy: IUnit exposes the triples as
            // IReadOnlyList so an implementation need not back them with an array at all.
            for (int i = 0; i < ItemIdentity.MaxAffixSlots; ++i)
            {
                identity.MagicPrefix[i] = i < record.MagicPrefix.Count ? record.MagicPrefix[i] : 0;
                identity.MagicSuffix[i] = i < record.MagicSuffix.Count ? record.MagicSuffix[i] : 0;
            }

            return identity;
        }

        /// <summary>
        /// The record's socket fillers as whole units, recursively. Array position is the socket
        /// index; each filler's stats are its OWN lists only, which is what GetStatUnsignedValue
        /// reads when ITEM_CalcRequiredLevel recurses into it (0x62b901).
        /// </summary>
        public static List<ItemUnit> ReadSocketUnits(IUnit record)
        {
            var units = new List<ItemUnit>();

            foreach (IUnit socket in ItemStatReader.EnumerateSockets(record))
            {
                units.Add(
                    new ItemUnit(
                        ReadIdentity(socket),
                        ItemStatReader.ReconstructView(socket, ItemStatView.ItemOnly()),
                        ReadSocketUnits(socket)));
            }

            return units;
        }

        /// <summary>
        /// A player is a unit document of the same shape as an item, so its attributes are not
        /// special fields — they are ordinary stats on its own stat lists, exactly as
        /// STATLIST_UnitGetStatValue reads them. Whether Holy Shield is up falls out the same way: a
        /// state is a stat list carrying its own dwStateNo (0x485dda tests state 101).
        /// </summary>
        public static ItemViewer ReadViewer(IUnit player)
        {
            if (player == null) throw new ArgumentNullException("player");

            var viewer = new ItemViewer();
            viewer.UnitType = player.UnitType;
            viewer.ClassId = player.ClassId;

            var stats = new Dictionary<int, int>();
            foreach (ItemStatGroup group in ItemStatReader.EnumerateGroups(player))
            {
                // On pMyStats rather than pMyLastList, so it is not contributing.
                if ((group.Flags & ItemStatListFlags.Set) != 0)
                {
                    continue;
                }

                viewer.ActiveStates.Add(group.StateNo);

                foreach (KeyValuePair<int, int> stat in group.EnumerateStats())
                {
                    int existing;
                    stats[stat.Key] = stats.TryGetValue(stat.Key, out existing)
                        ? existing + stat.Value
                        : stat.Value;
                }
            }

            // The merged values land LAST and by assignment. A wearer's chain is structural: it
            // says which states are active, but its attribute values are pre-gear, because
            // STATLIST_CalcFullStatFromChildren does the folding and the capture cannot re-send
            // every equipped piece inside the player document. GetStat reads the folded result,
            // and that is what the requirement checks compare against.
            //
            // Assignment, not accumulation: these are already totals. Adding them to the chain
            // sum would count the kit twice. Absent, the chain values stand, which is what a
            // hand-built viewer or a producer without merged stats gives.
            foreach (IUnitStat stat in player.Stats)
            {
                stats[ItemStatReader.PackStatKey(stat.Layer, stat.Id)] = stat.Value;
            }

            foreach (KeyValuePair<int, int> stat in stats)
            {
                viewer.Stats[stat.Key] = stat.Value;
            }

            viewer.Level = ViewerStat(stats, StatLevel);
            viewer.Strength = ViewerStat(stats, StatStrength);
            viewer.Dexterity = ViewerStat(stats, StatDexterity);

            // A skill LEVEL is the one thing a stat capture cannot reach: SKILLS_GetSkillLevel reads
            // pSkill->nSkillLevel off the SKILL list.
            foreach (IUnitSkill skill in player.Skills)
            {
                viewer.Skills[skill.Skill] = skill.Level;
            }

            viewer.FlagsEx = player.FlagsEx;

            return viewer;
        }


        private const int StatStrength = 0;
        private const int StatDexterity = 2;
        private const int StatLevel = 12;

        private static int ViewerStat(IDictionary<int, int> stats, int statId)
        {
            int value;
            return stats.TryGetValue(ItemStatReader.PackStatKey(0, statId), out value) ? value : 0;
        }
    }

}
