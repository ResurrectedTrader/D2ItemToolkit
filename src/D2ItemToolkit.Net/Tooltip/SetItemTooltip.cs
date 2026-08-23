using System;
using System.Collections.Generic;

namespace D2ItemToolkit
{
    /// <summary>
    /// The parts of an identified set item's tooltip that the item's own record cannot supply.
    ///
    /// Everything else — the piece names, their order, the count, the set name, `add func`, this
    /// piece's slot and the partial-bonus stats — is derived from setitems.txt and the record, and
    /// is deliberately NOT settable here.
    ///
    /// Both masks and the owned set carry the same fact from two different game functions, and the
    /// writer really does call both: GetSetItem 0x486770 for each piece's colour and
    /// ITEMS_GetEquippedSetItemsMask 0x62a370 for the tier arithmetic. They do not agree in
    /// general — GetSetItem accepts inventory pages 0/3/4/0xFF and grid types 1/3/4 (0x4867b3),
    /// the mask accepts grid type 3 alone (0x62a3f0) — so a set piece in the backpack, or on
    /// weapon swap, colours green while contributing no bit.
    /// </summary>
    public sealed class SetItemTooltipInput
    {
        /// <summary>
        /// The <c>setitems.txt</c> row indices GetSetItem 0x486770 would return non-null for, i.e.
        /// the pieces of this set the viewer is carrying somewhere it counts. A piece not listed
        /// here is painted red (0x48d902).
        ///
        /// Grid types 1, 3 AND 4 all qualify (0x4867d4), so a piece on WEAPON SWAP belongs in this
        /// list even though it contributes no mask bit — see the masks below.
        /// </summary>
        public IEnumerable<int> OwnedSetItemIds;

        /// <summary>
        /// ITEMS_GetEquippedSetItemsMask(viewer, item, 1) — bit `slot` per WORN sibling, the
        /// hovered item included. Feeds `add func == 2` (0x4e65b2) and, through its popcount, the
        /// derived set-bonus block.
        ///
        /// EXCLUDE BODY LOCATIONS 11 AND 12. INVENTORY_PlaceItemInGrid 0x63afd0 stamps the grid
        /// type from the body slot at 0x63b1e2 — `cmp [ebp+arg_0], 0Bh / setnl cl / add cl, 3 /
        /// mov [edx+0Dh], cl`, i.e. `(bodyLoc >= 11) ? 4 : 3` written to pItemData+105 — and 11/12
        /// are the swap pair (ITEMMODE_GetAlternateBodyLoc 0x55f240). The mask requires grid type 3
        /// alone (0x62a3f0), so a set piece on weapon swap lights NO bit and does not raise the
        /// piece count. A caller that counted it would light one tier too many.
        /// </summary>
        public int WornMaskIncludingSelf;

        /// <summary>
        /// ITEMS_GetEquippedSetItemsMask(viewer, item, 0) — the same mask with the hovered item
        /// excluded. Feeds `add func == 1` (0x4e6618). The weapon-swap exclusion above applies
        /// here too.
        /// </summary>
        public int WornMaskExcludingSelf;

        /// <summary>
        /// dwAnimMode == 1, tested at 0x48d870. False suppresses the full-set block outright, and
        /// the game suppresses it for the same reason.
        /// </summary>
        public bool IsEquipped;

        /// <summary>
        /// OPTIONAL OVERRIDE, and the FIRST of three sources. Leave null and the block comes from
        /// the VIEWER's own record, which is where the game reads it:
        /// SKILLDESC_AppendItemBuffTextAlt 0x4e6680 walks GetStatsByState(wearer, STATE_ITEMSET k)
        /// for k 0..5 (0x4e66c9) and takes the list whose stat 71 equals this set's id (0x4e66d7).
        ///
        /// Failing that it is DERIVED from sets.txt plus the equipped-piece count, by replaying
        /// ITEMMOD_ApplySetBonuses 0x660120 — the function that filled the wearer's list to begin
        /// with. That is exact for 217 of the 220 shipped property slots; the three genuine ranges
        /// (Vidala's Rig FMin1/FMax1 15..20, Cathan's Traps PMin2a/PMax2a 15..20, Cow King's
        /// Leathers FMin5/FMax5 25/5, inverted) resolve to the low end, as every other
        /// seed-dependent range in this port does.
        ///
        /// Set this only to override both.
        /// </summary>
        public IEnumerable<KeyValuePair<int, int>> FullSetStats;
    }

    /// <summary>One row of the piece list, in setitems.txt order.</summary>
    internal sealed class SetPieceLine
    {
        public string Text;

        /// <summary>GetSetItem returned non-null: green (0x48d8fb) rather than red (0x48d902).</summary>
        public bool Owned;
    }

    /// <summary>
    /// The four set-specific buffers of ITEM_BuildSetItemTooltip, already built. The composer only
    /// orders and colours them; deriving them is <see cref="SetItemTooltipBuilder"/>'s job.
    /// </summary>
    internal sealed class SetItemTooltipContent
    {
        /// <summary>var_4790, built at 0x48d88e-0x48d92a.</summary>
        public IReadOnlyList<SetPieceLine> Pieces = new SetPieceLine[0];

        /// <summary>var_1538 — `str(sets[+0x02]) + str(3998)`, built at 0x48d3b5-0x48d3d0.</summary>
        public string SetName = string.Empty;

        /// <summary>var_3390, SKILLDESC_AppendItemBuffTextAlt 0x4e6680.</summary>
        public string FullSetText = string.Empty;

        /// <summary>var_2F90, SKILLDESC_AppendItemBuffText 0x4e6560.</summary>
        public string PartialText = string.Empty;

        /// <summary>
        /// var_138 — locale 3333 plus its terminator, written at 0x48dab1-0x48dac3 when
        /// NPCMENU_CalculateItemTransactionCost refuses and ShopMode is not 4.
        /// </summary>
        public string TransactionRefusedText = string.Empty;
    }

    /// <summary>
    /// Which STATE_ITEMSET n tiers SKILLDESC_AppendItemBuffText 0x4e6560 asks for. Pure arithmetic
    /// over `add func` and the worn mask — no data lookup, which is why it is testable on its own.
    /// </summary>
    internal static class SetBonusTiers
    {
        /// <summary>dword_6DBD70 = { 165, 166, 167, 168, 169, 170 }, read directly.</summary>
        public static readonly int[] ItemSetStates = { 165, 166, 167, 168, 169, 170 };

        /// <summary>
        /// dword_6DBD90, a 64-entry popcount table indexed by the mask. 0x4e65ba refuses anything
        /// at 0x40 or above and substitutes a count of zero rather than reading past the table.
        /// ITEMMOD_ApplySetBonuses reads the same table under a second name, dword_6EDA40, behind
        /// the identical guard (`cmp eax, 40h / jb` at 0x660190).
        /// </summary>
        public static int PopCount(int mask)
        {
            if (mask < 0 || mask >= 64)
            {
                return 0;
            }

            int count = 0;
            for (int bit = 0; bit < 6; ++bit)
            {
                if ((mask & (1 << bit)) != 0)
                {
                    ++count;
                }
            }

            return count;
        }

        public static IReadOnlyList<int> Select(
            int addFunc, int selfSlot, int wornMaskIncludingSelf, int wornMaskExcludingSelf)
        {
            var states = new List<int>();

            // 0x4e659f loads the byte and subtracts one, so 0 falls through both arms.
            switch (addFunc)
            {
                case 1:
                    // 0x4e6622-0x4e665c. WHICH sibling is worn picks WHICH aprop pair, and the
                    // index collapses over the gap this piece leaves (0x4e662f).
                    for (int j = 0; j < 6; ++j)
                    {
                        if (j == selfSlot)
                        {
                            continue;
                        }

                        if ((wornMaskExcludingSelf & (1 << j)) == 0)
                        {
                            continue;
                        }

                        states.Add(ItemSetStates[j > selfSlot ? j - 1 : j]);
                    }

                    break;

                case 2:
                    // 0x4e65b2-0x4e65f9: N worn pieces light tiers 0 .. N-2.
                    int tiers = PopCount(wornMaskIncludingSelf) - 1;
                    for (int i = 0; i < tiers; ++i)
                    {
                        states.Add(ItemSetStates[i]);
                    }

                    break;
            }

            return states;
        }
    }

    /// <summary>
    /// Derives <see cref="SetItemTooltipContent"/> from the captured record plus the caller's
    /// <see cref="SetItemTooltipInput"/>.
    /// </summary>
    internal sealed class SetItemTooltipBuilder
    {
        /// <summary>The `%0` the piece list is written through (0x48d8d0 pushes 2769h).</summary>
        private const int SetPieceFormat = 10089;

        /// <summary>Locale 3333, "Item cannot be traded here." (0x48dab6).</summary>
        private const int TransactionRefusedStringId = 3333;

        /// <summary>
        /// The nPropMode ITEMMOD_ApplySetBonuses pushes for both blocks (`push 4` at 0x6601df and
        /// 0x66021e). It is recorded rather than left as a bare literal because it is inert here:
        /// the only handler in the table that reads the mode is func 1, whose "enhanced" reset
        /// needs mode 1 (`cmp ecx, 1`, 0x65eb59), so 4 selects nothing.
        /// </summary>
        private const int SetBonusPropMode = 4;

        private readonly D2DataFiles _data;
        private readonly SetTable _sets;
        private readonly ItemTable _items;
        private readonly ItemTypeTree _types;
        private readonly PropertyApplier _applier;

        public SetItemTooltipBuilder(
            D2DataFiles data, SetTable sets, ItemTable items, ItemTypeTree types)
        {
            if (data == null) throw new ArgumentNullException("data");
            if (sets == null) throw new ArgumentNullException("sets");

            _data = data;
            _sets = sets;
            _items = items;
            _types = types;

            _applier = new PropertyApplier(data, items, types);
            _sets.ResolvePropertyCodesWith(_applier.Properties.RowForCode);
        }

        /// <summary>
        /// Null when the writer would draw nothing at all: GetSetItemsLine returning null returns
        /// at 0x48d397 and GetSetsLine at 0x48d3ab, both before a single buffer is appended.
        /// </summary>
        public SetItemTooltipContent Build(
            IUnit record,
            ItemIdentity item,
            ItemViewer viewer,
            IDictionary<int, int> stats,
            SetItemTooltipInput input,
            IUnit wearer = null)
        {
            if (item == null) throw new ArgumentNullException("item");
            if (input == null) throw new ArgumentNullException("input");

            SetItemRecord piece = _sets.PieceAt(item.FileIndex);
            if (piece == null)
            {
                return null;
            }

            SetRecord set = _sets.SetAt(piece.SetId);
            if (set == null)
            {
                return null;
            }

            var content = new SetItemTooltipContent();
            content.SetName = Str(set.NameStringId) + Terminator;
            content.TransactionRefusedText = Str(TransactionRefusedStringId) + Terminator;
            content.Pieces = BuildPieces(set, input);
            content.PartialText = BuildPartial(record, item, viewer, stats, piece, input);
            content.FullSetText =
                BuildFullSet(item, viewer, wearer, piece.SetId, stats, input);
            return content;
        }

        private IReadOnlyList<SetPieceLine> BuildPieces(SetRecord set, SetItemTooltipInput input)
        {
            var owned = new HashSet<int>();
            if (input.OwnedSetItemIds != null)
            {
                foreach (int id in input.OwnedSetItemIds)
                {
                    owned.Add(id);
                }
            }

            var pieces = new List<SetPieceLine>();

            // The loop is bounded by sets[+0x0C], the RUNTIME member count, and breaks on the
            // first null pointer (0x48d8a7) — both of which SetRecord.Pieces already models.
            foreach (SetItemRecord member in set.Pieces)
            {
                var line = new SetPieceLine();

                // wsprintf 0x48be80 is Blizzard's positional templater, not the Win32 one, and
                // with ENG data the format is bare "%0" — the name verbatim (0x48d8dd).
                line.Text = ItemNameBuilder.Positional(
                    Str(SetPieceFormat), Str(member.NameStringId), null, null) + Terminator;
                line.Owned = owned.Contains(member.SetItemId);
                pieces.Add(line);
            }

            return pieces;
        }

        /// <summary>
        /// SKILLDESC_AppendItemBuffText 0x4e6560, one BuildStatBuffDesc per selected tier.
        ///
        /// A selected tier still renders nothing unless its list is ENABLED. BuildStatBuffDesc
        /// reaches it through GetStatList(item, state, 0) (0x4e60ff), whose zero mask sends it down
        /// the pMyLastList chain at +0x3C (0x6257ef); STATLIST_ToggleStateDisabled parks a disabled
        /// tier on the OTHER chain by setting STATLIST_SET (0x6279e7) and re-attaching, and
        /// STATLIST_AttachStatListToUnit files a 0x2000 list under +0x40 (0x626e67). So a tier
        /// carrying STATLIST_SET is unreachable from here.
        /// </summary>
        private string BuildPartial(
            IUnit record,
            ItemIdentity item,
            ItemViewer viewer,
            IDictionary<int, int> stats,
            SetItemRecord piece,
            SetItemTooltipInput input)
        {
            IReadOnlyList<int> states = SetBonusTiers.Select(
                piece.AddFunc, piece.Slot,
                input.WornMaskIncludingSelf, input.WornMaskExcludingSelf);

            if (states.Count == 0 || record == null)
            {
                return string.Empty;
            }

            var text = new System.Text.StringBuilder();

            foreach (int state in states)
            {
                SortedDictionary<int, int> tier = EnabledTier(record, state);
                if (tier.Count == 0)
                {
                    continue;
                }

                text.Append(Describe(tier, item, viewer, stats, describedUnitIsItem: true));
            }

            return text.ToString();
        }

        /// <summary>
        /// SKILLDESC_AppendItemBuffTextAlt 0x4e6680. The block lives on the PLAYER's statlist, so
        /// the described unit is the player (0x4e670c passes a1) and the never-breaks tail at
        /// 0x4e63a4 — which needs `*v8 == 4` — cannot fire.
        /// </summary>
        private string BuildFullSet(
            ItemIdentity item,
            ItemViewer viewer,
            IUnit wearer,
            int setId,
            IDictionary<int, int> stats,
            SetItemTooltipInput input)
        {
            if (!input.IsEquipped)
            {
                return string.Empty;
            }

            IEnumerable<KeyValuePair<int, int>> source =
                input.FullSetStats
                ?? FullSetStatsOfWearer(wearer, setId)
                ?? DeriveSetBonuses(item, setId, input.WornMaskIncludingSelf);

            if (source == null)
            {
                return string.Empty;
            }

            var full = new SortedDictionary<int, int>();
            foreach (KeyValuePair<int, int> entry in source)
            {
                int existing;
                full[entry.Key] = full.TryGetValue(entry.Key, out existing)
                    ? existing + entry.Value
                    : entry.Value;
            }

            if (full.Count == 0)
            {
                return string.Empty;
            }

            return Describe(full, item, viewer, stats, describedUnitIsItem: false);
        }

        /// <summary>
        /// The wearer's STATE_ITEMSET list for THIS set — 0x4e66c9 walks states 165..170 and
        /// 0x4e66d7 keeps the one whose stat 71 (`value`) is the set id, which is how the engine
        /// tells one worn set's block from another's when a character wears two.
        ///
        /// Null when the wearer carries no statlist chain at all, which is not the same as an empty
        /// one: a producer that only records merged attributes has nothing to offer here, and the
        /// caller has to supply <see cref="SetItemTooltipInput.FullSetStats"/> instead.
        /// </summary>
        private static IEnumerable<KeyValuePair<int, int>> FullSetStatsOfWearer(
            IUnit wearer, int setId)
        {
            if (wearer == null || wearer.StatsLists == null || wearer.StatsLists.Count == 0)
            {
                return null;
            }

            foreach (IUnitStatList group in wearer.StatsLists)
            {
                if (group == null
                    || group.StateNo < ItemStatListStates.ItemSet1
                    || group.StateNo > ItemStatListStates.ItemSet6
                    || group.Stats == null)
                {
                    continue;
                }

                var packed = new SortedDictionary<int, int>();
                bool isThisSet = false;

                foreach (IUnitStat stat in group.Stats)
                {
                    if (stat == null)
                    {
                        continue;
                    }

                    if (stat.Id == StatSetValue && stat.Layer == 0 && stat.Value == setId)
                    {
                        isThisSet = true;
                    }

                    packed[ItemStatReader.PackStatKey(stat.Layer, stat.Id)] = stat.Value;
                }

                if (isThisSet)
                {
                    return packed;
                }
            }

            return null;
        }

        /// <summary>
        /// ITEMMOD_ApplySetBonuses 0x660120, replayed against sets.txt. This is what PUT the block
        /// on the wearer's chain in the first place — ITEM_ManageSetBonusStatList 0x663c9e opens a
        /// STATE_ITEMSET list on the wearer, stamps stat 71 with the set id (0x663c93) and then
        /// calls it (0x663c9e) — so replaying it reconstructs exactly the list
        /// SKILLDESC_AppendItemBuffTextAlt would have read, without needing the wearer's chain.
        ///
        /// Note that the block is NOT just the FCode properties: the same list receives the
        /// PARTIAL PCode tiers (0x6601c4), which is why a four-of-five set still shows a gold
        /// block. The rebuild is exact for shipped data because 217 of the 220 property slots have
        /// FMin == FMax; the three that do not resolve to the low end here, the same way
        /// <see cref="PropertyApplier"/> handles every other seed-dependent range.
        /// </summary>
        private SortedDictionary<int, int> DeriveSetBonuses(
            ItemIdentity item, int setId, int wornMaskIncludingSelf)
        {
            var stats = new SortedDictionary<int, int>();

            SetRecord set = _sets.SetAt(setId);
            if (set == null)
            {
                return stats;
            }

            int count = SetBonusTiers.PopCount(wornMaskIncludingSelf);

            // sets +0x0C is the RUNTIME member count the link loop built (0x6366ff), which is
            // Pieces.Count. n = min(count, nSetItems - 1) at 0x6601ae-0x6601b5, then
            // `lea eax, [eax+eax-2]` at 0x6601b7.
            int members = set.Pieces.Count;
            int capped = count < members - 1 ? count : members - 1;
            int limit = 2 * capped - 2;

            int slot = 0;
            foreach (ItemProperty property in _sets.PartialProperties(setId))
            {
                // `test eax,eax / jle` at 0x6601c2 skips the block outright for a non-positive
                // limit, and the tail test is `cmp ebx, eax / jl` at 0x6601ef.
                if (slot >= limit)
                {
                    break;
                }

                // 0x6601ca skips a blank slot and carries on. It does NOT break — that asymmetry
                // with the full block below is the whole reason both walks exist separately.
                if (property.PropertyId >= 0)
                {
                    _applier.Apply(SetBonusPropMode, item, property, stats);
                }

                ++slot;
            }

            // 0x6601f9: the full block needs the WHOLE set, not one short of it.
            if (count < members)
            {
                return stats;
            }

            foreach (ItemProperty property in _sets.FullProperties(setId))
            {
                // 0x660209 ends the walk rather than skipping.
                if (property.PropertyId < 0)
                {
                    break;
                }

                _applier.Apply(SetBonusPropMode, item, property, stats);
            }

            return stats;
        }

        /// <summary>itemstatcost `value`, post-splice row 71 — the set id STATLIST_GetBaseStatValue
        /// reads at 0x4e66d7.</summary>
        private const int StatSetValue = 71;

        /// <summary>
        /// The item's own STATE_ITEMSET n list, dropped when STATLIST_SET marks it disabled. The
        /// stats are NOT merged across sockets: BuildStatBuffDesc's filler walk (0x4e6162) only
        /// ever finds state-0 lists on a gem.
        /// </summary>
        private static SortedDictionary<int, int> EnabledTier(IUnit record, int state)
        {
            var tier = new SortedDictionary<int, int>();

            // The wearer's OWN chain: EnumerateGroups would descend into the gear it carries.
            foreach (ItemStatGroup group in ItemStatReader.EnumerateOwnGroups(record))
            {
                if (group.StateNo != state
                    || (group.Flags & ItemStatListFlags.Set) != 0)
                {
                    continue;
                }

                foreach (KeyValuePair<int, int> stat in group.EnumerateStats())
                {
                    int existing;
                    tier[stat.Key] = tier.TryGetValue(stat.Key, out existing)
                        ? existing + stat.Value
                        : stat.Value;
                }
            }

            return tier;
        }

        /// <summary>
        /// Both set blocks pass isMainStatBlock = 0 (0x4e65f9 / 0x4e670c), which costs them the
        /// inherent damage-to-undead line (0x4e61ea), and a8 = 1, which terminates every line.
        /// </summary>
        private string Describe(
            SortedDictionary<int, int> tier,
            ItemIdentity item,
            ItemViewer viewer,
            IDictionary<int, int> stats,
            bool describedUnitIsItem)
        {
            var values = new SynthesisedStatValues(
                tier, item, viewer, _items, _types, stats, describedUnitIsItem);

            ItemDescriptionGenerator generator =
                _data.CreateGenerator(values, isMainStatBlock: false);

            return generator.Join(generator.Describe(tier));
        }

        private string Str(int id)
        {
            return _data.Strings.GetByIndex(id) ?? string.Empty;
        }

        private string Terminator
        {
            get { return Str(DescStringIds.Newline); }
        }
    }
}
