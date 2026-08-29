using System.Collections.Generic;

namespace D2ItemToolkit
{
    /// <summary>
    /// What a socketed gem or rune gives its host, rebuilt from gems.txt.
    ///
    /// ITEM_ApplySocketableAndEquipStats 0x4c0cf0 is the whole rule. For a filler of type 20
    /// (`gem`) it calls ApplyRuneAndGemStats(2, NULL, filler, gemsRow, applyType, 0) at 0x4c0d99;
    /// for type 74 (`rune`), ApplyRuneAndGemStats(5, host, filler, gemsRow, applyType, 0) at
    /// 0x4c0df9. Anything else — a jewel — falls through to ITEM_ProcessSetItemEquip and gets no
    /// gems.txt properties at all (0x4c0e06).
    ///
    /// `applyType` is the HOST's items.txt `gemapplytype` (ITEM_GetItemsTxt_bGemApplyType 0x629a40
    /// → TXT_Items_GetGemApplyType 0x629a00). It selects which of the three property arrays on the
    /// gems record is read: ITEMMOD_GetMaxLevelAtIndex 0x65c6d0 builds {+0x30, +0x60, +0x90} and
    /// indexes it, i.e. 0 weapon, 1 helm, 2 shield — the slot argument
    /// <see cref="GemTable.Properties"/> already takes. Three or above halts the game (0x65c6f0).
    ///
    /// The walk takes at most three properties and STOPS at the first with no property rather than
    /// skipping it (0x66004f), and the item threaded into the property funcs is the FILLER, not the
    /// host — ApplyRuneAndGemStats loads `esi` from its pItem argument at 0x660057. That is the same
    /// call shape <see cref="RecordSections"/> already uses for a loose filler's own description;
    /// the only difference here is that one slot is chosen instead of all four blocks being walked.
    ///
    /// WHY THIS EXISTS. Every caller of the assignment lives in D2Common/D2Game. A client-side
    /// capture reads D2Client's unit tables, and the client is handed the HOST's already-computed
    /// stats in the item packet — it never instantiates the filler's mods. So a gem or rune arrives
    /// with an empty stat chain and its contribution has to be synthesised. A jewel does not: it is
    /// a magic item with rolled affixes of its own, which the capture carries, and gems.txt has no
    /// row for it either way.
    /// </summary>
    internal sealed class SocketStatSynthesis
    {
        private readonly ItemTable _items;
        private readonly ItemTypeTree _types;
        private readonly GemTable _gems;
        private readonly PropertyApplier _applier;

        private readonly int _gemTypeRow;
        private readonly int _runeTypeRow;

        public SocketStatSynthesis(D2DataFiles data, ItemTable items, ItemTypeTree types)
        {
            _items = items;
            _types = types;
            _gems = new GemTable(data.Gems, items);
            _applier = new PropertyApplier(data, items, types);
            _gems.ResolvePropertyCodesWith(_applier.Properties.RowForCode);

            _gemTypeRow = types == null ? -1 : types.Row("gem");
            _runeTypeRow = types == null ? -1 : types.Row("rune");
        }

        /// <summary>
        /// The union over every filler that carries no captured stats of its own. Fillers that DO
        /// carry them are left alone: a server-side producer records the mods the engine already
        /// assigned, and synthesising on top would count them twice.
        /// </summary>
        public SortedDictionary<int, int> Contributions(IUnit host)
        {
            var merged = new SortedDictionary<int, int>();

            if (host == null)
            {
                return merged;
            }

            int slot = _items.GetInt(host.ClassId, "gemapplytype");

            // 0x65c6f0 halts above two. Shipped data never does, but a caller's own tables might.
            if (slot < 0 || slot > 2)
            {
                return merged;
            }

            foreach (IUnit filler in host.Items)
            {
                // One level only: a jewel cannot itself hold sockets, so vanilla never nests
                // further, and the game applies the filler to its immediate host.
                Add(merged, Contribution(filler, slot));
            }

            return merged;
        }

        /// <summary>
        /// One filler's contribution to a host with this gemapplytype, or empty when the filler
        /// already carries stats, is not a gem or rune, or has no gems.txt row.
        /// </summary>
        public SortedDictionary<int, int> Contribution(IUnit filler, int slot)
        {
            var stats = new SortedDictionary<int, int>();

            if (filler == null || slot < 0 || slot > 2)
            {
                return stats;
            }

            if (ItemStatReader.ReconstructView(filler, ItemStatView.Modifiers()).Count != 0)
            {
                return stats;
            }

            ItemIdentity identity = ItemRecordReader.ReadIdentity(filler);

            int primary = _types.Row(_items.PrimaryTypeCode(identity.ClassId));
            int secondary = _types.Row(_items.SecondaryTypeCode(identity.ClassId));

            bool gem = _gemTypeRow >= 0 && _types.IsOfType(primary, secondary, _gemTypeRow);
            bool rune = !gem && _runeTypeRow >= 0
                             && _types.IsOfType(primary, secondary, _runeTypeRow);

            if (!gem && !rune)
            {
                return stats;
            }

            int row = _gems.RowForFillerClassId(identity.ClassId);
            if (row < 0)
            {
                return stats;
            }

            int propMode = gem ? PropertyApplier.PropModeGem : PropertyApplier.PropModeRune;

            foreach (ItemProperty property in _gems.Properties(row, slot))
            {
                if (property.PropertyId < 0)
                {
                    break;
                }

                _applier.Apply(propMode, identity, property, stats);
            }

            return stats;
        }

        /// <summary>
        /// The gem/rune properties every filler would apply, before any of them is rolled — the same
        /// selection <see cref="Contributions"/> applies, exposed so a range reconstruction can run
        /// them at both ends instead of once.
        ///
        /// **No gems.txt cell actually rolls.** The three whose min differs from their max are
        /// `dmg-fire`, `dmg-ltng` and `dmg-cold` on the Ral, Ort and Thul runes, and those are funcs
        /// 15 and 16 — the two ENDS of a damage range, both fixed, read as separate parameters
        /// exactly as funcs 11 and 19 read theirs. So a gem or rune contributes no span at all; a
        /// socketed JEWEL does, but from its own affixes rather than from here.
        /// </summary>
        public IEnumerable<ItemProperty> FillerProperties(IUnit host)
        {
            if (host == null)
            {
                yield break;
            }

            int slot = _items.GetInt(host.ClassId, "gemapplytype");
            if (slot < 0 || slot > 2)
            {
                yield break;
            }

            foreach (IUnit filler in host.Items)
            {
                int row = FillerRow(filler);
                if (row < 0)
                {
                    continue;
                }

                foreach (ItemProperty property in _gems.Properties(row, slot))
                {
                    if (property.PropertyId < 0)
                    {
                        break;
                    }

                    yield return property;
                }
            }
        }

        /// <summary>
        /// items.txt `gemapplytype` for this host — which of the three gems.txt mod columns applies
        /// (0x65c6f0 halts above two). -1 when the host cannot take fillers at all.
        /// </summary>
        public int SlotFor(IUnit host)
        {
            if (host == null)
            {
                return -1;
            }

            int slot = _items.GetInt(host.ClassId, "gemapplytype");
            return slot >= 0 && slot <= 2 ? slot : -1;
        }

        /// <summary>
        /// ONE filler's properties, so a caller can range or describe each socket separately rather
        /// than as the union <see cref="Contributions"/> returns.
        /// </summary>
        public IEnumerable<ItemProperty> FillerProperties(IUnit filler, int slot)
        {
            if (slot < 0 || slot > 2)
            {
                yield break;
            }

            int row = FillerRow(filler);
            if (row < 0)
            {
                yield break;
            }

            foreach (ItemProperty property in _gems.Properties(row, slot))
            {
                if (property.PropertyId < 0)
                {
                    break;
                }

                yield return property;
            }
        }

        /// <summary>
        /// The gems.txt row a filler applies from, or -1 when it carries its own stats, is not a gem
        /// or rune, or has no row. The same three gates <see cref="Contribution"/> applies.
        /// </summary>
        private int FillerRow(IUnit filler)
        {
            if (filler == null
                || ItemStatReader.ReconstructView(filler, ItemStatView.Modifiers()).Count != 0)
            {
                return -1;
            }

            ItemIdentity identity = ItemRecordReader.ReadIdentity(filler);

            int primary = _types.Row(_items.PrimaryTypeCode(identity.ClassId));
            int secondary = _types.Row(_items.SecondaryTypeCode(identity.ClassId));

            bool gem = _gemTypeRow >= 0 && _types.IsOfType(primary, secondary, _gemTypeRow);
            bool rune = !gem && _runeTypeRow >= 0
                             && _types.IsOfType(primary, secondary, _runeTypeRow);

            return gem || rune ? _gems.RowForFillerClassId(identity.ClassId) : -1;
        }

        private static void Add(
            IDictionary<int, int> into, IEnumerable<KeyValuePair<int, int>> from)
        {
            foreach (KeyValuePair<int, int> stat in from)
            {
                int existing;
                into[stat.Key] = into.TryGetValue(stat.Key, out existing)
                    ? existing + stat.Value
                    : stat.Value;
            }
        }
    }
}
