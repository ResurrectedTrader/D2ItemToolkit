using System;
using System.Collections.Generic;

namespace D2ItemToolkit
{
    /// <summary>
    /// One op-13 relationship: <see cref="PercentStat"/>'s value is a percentage applied to
    /// <see cref="TargetStat"/>'s BASE value, and the product is added to the target's merged value.
    /// </summary>
    public struct ItemStatOpEntry
    {
        public int PercentStat;
        public int TargetStat;

        public ItemStatOpEntry(int percentStat, int targetStat)
        {
            PercentStat = percentStat;
            TargetStat = targetStat;
        }
    }

    /// <summary>One op-4 or op-5 relationship: a per-level stat that scales a target by a unit stat.</summary>
    internal struct ItemStatLevelOpEntry
    {
        public readonly int SourceStat;
        public readonly int TargetStat;
        public readonly int Op;
        public readonly int OpParam;
        public readonly int OpBase;
        public readonly int OpBaseShift;

        public ItemStatLevelOpEntry(int sourceStat, int targetStat, int op, int opParam, int opBase, int opBaseShift)
        {
            SourceStat = sourceStat;
            TargetStat = targetStat;
            Op = op;
            OpParam = opParam;
            OpBase = opBase;
            OpBaseShift = opBaseShift;
        }
    }

    /// <summary>
    /// Deliberately separate from IItemStatCostTable: the description engine's fakes implement that
    /// one, and op resolution is a merge-time concern rather than a describe-time one.
    /// </summary>
    internal interface IItemStatOpTable
    {
        IReadOnlyList<ItemStatOpEntry> PercentOfBaseEntries { get; }
    }

    /// <summary>
    /// Re-applies the op-13 stats the engine folds into FullStats.
    ///
    /// ItemStatCost's op stats are a REVERSE index: the loader writes an entry into the record of
    /// each of a row's `op stat1..3` TARGETS, so op 13 declared on stat 18 modifies 21, 23 and 159
    /// — not the other way round (walked at 0x626259 / 0x626273 / 0x62666b, +0xDE, 6-byte stride,
    /// 16 slots).
    ///
    /// `STATLIST_CalcCombinedStatValue` 0x626200 case 13 (0x626626-0x626662) accumulates
    /// `D2ApplyPercent(base[target], combined[percent], 100)`. Both inputs are forced to layer 0
    /// (`push 0` at 0x626635, `shl edx, 10h` at 0x626648) and no nValShift is applied anywhere in
    /// that arm. A zero on either side skips the entry; only op 0 ends the walk.
    ///
    /// The base value comes from STATLIST_LookupBaseStatWithMinAccr 0x624ed0, which always reads
    /// `Stats` (+0x24) — the captured `base` group — while the result lands in FullStats (0x625158),
    /// which is what GetStatUnsignedValue reads. So the pass is not self-referential: it consumes
    /// the base view and produces the merged one.
    /// </summary>
    internal static class ItemStatOps
    {
        /// <summary>
        /// Applies every op-13 entry to <paramref name="merged"/> in place. Only the Equipped and
        /// ForSale views may be passed here.
        ///
        /// NOT BaseOnly: that view IS this pass's input, and DamageIsModified/Bonus are
        /// merged-minus-base, so moving base would silently strip every colour marker.
        /// NOT Modifiers: those are individual chain nodes, and the stats described there are
        /// 16/17/18 themselves.
        /// </summary>
        public static void Resolve(
            IDictionary<int, int> merged,
            IDictionary<int, int> baseStats,
            IItemStatOpTable table,
            bool dropPercents = true)
        {
            if (merged == null || baseStats == null || table == null)
            {
                return;
            }

            foreach (ItemStatOpEntry entry in table.PercentOfBaseEntries)
            {
                int percent;
                if (!merged.TryGetValue(
                        ItemStatReader.PackStatKey(0, entry.PercentStat), out percent)
                    || percent == 0)
                {
                    continue;
                }

                int baseValue;
                if (!baseStats.TryGetValue(
                        ItemStatReader.PackStatKey(0, entry.TargetStat), out baseValue)
                    || baseValue == 0)
                {
                    continue;
                }

                int key = ItemStatReader.PackStatKey(0, entry.TargetStat);
                int existing;
                merged[key] = (merged.TryGetValue(key, out existing) ? existing : 0)
                              + ApplyPercent(baseValue, percent);
            }

            // The game does not STORE the percent on an item — 0x626821 tests
            // `dwOwnerType == UNIT_ITEM` and clears the update flag, and 0x626847 then skips the
            // store — and the render depends on that absence, which is what stops
            // INV_FormatDurabilityText colouring a Superior weapon's max (see
            // <see cref="DropResolvedPercents"/>).
            //
            // A merged-stat consumer wants it anyway: the tooltip draws `+150% Enhanced Defense` as
            // its own line, so a caller indexing what an item grants has to be able to find it.
            if (dropPercents)
            {
                DropResolvedPercents(merged, table);
            }
        }

        /// <summary>
        /// An op-13 percent stat is NOT stored in an item's FullStats once it has landed on a target.
        ///
        /// STATLIST_ApplyComplexStatFormula 0x626770 writes each TARGET to FullStats (0x626786),
        /// then — when that target computed non-zero (0x62678d/0x626790) — switches on the op.
        /// Case 13 is 0x626821: `cmp dword ptr [esi+8], 4`, i.e. dwOwnerType == UNIT_ITEM, and only
        /// then does 0x626827 clear the update flag. After the three-target loop, 0x626847 skips the
        /// STATLIST_SetFullStatValue at 0x626868 that would have stored the PERCENT stat itself.
        /// D2MOO states it plainly: `case STAT_OP_ADD_ITEM_STAT_PCT: if (dwOwnerType == UNIT_ITEM)
        /// bUpdate = FALSE;`.
        ///
        /// So on an item, `GetStatUnsignedValue(item, 75, 0)` reads 0 however large the durability
        /// bonus is, and STATLIST_GetStatBonusFromLists — merged minus base, 0x625570 — is 0 too.
        /// That is what stops INV_FormatDurabilityText colouring the max on a Superior weapon
        /// (0x484f14 gates the marker on exactly that difference).
        ///
        /// The gate is per-percent-stat, not per-target: one target computing non-zero drops it.
        /// A target that computed ZERO leaves the flag alone, which is why the removal is
        /// conditional rather than unconditional.
        ///
        /// This applies to an ITEM's list only. On a player's list dwOwnerType is not 4, the flag
        /// survives, and the percent stat IS stored — which is why <see cref="Resolve"/> documents
        /// that only item views may be passed to it.
        /// </summary>
        private static void DropResolvedPercents(
            IDictionary<int, int> merged, IItemStatOpTable table)
        {
            var resolved = new List<int>();

            foreach (ItemStatOpEntry entry in table.PercentOfBaseEntries)
            {
                int target;
                if (!merged.TryGetValue(
                        ItemStatReader.PackStatKey(0, entry.TargetStat), out target)
                    || target == 0)
                {
                    continue;
                }

                int key = ItemStatReader.PackStatKey(0, entry.PercentStat);
                if (merged.ContainsKey(key) && !resolved.Contains(key))
                {
                    resolved.Add(key);
                }
            }

            // Removed after the walk: a percent stat has up to three targets, and dropping it while
            // the enumeration is still reading those entries would cut the later ones short.
            foreach (int key in resolved)
            {
                merged.Remove(key);
            }
        }

        /// <summary>
        /// D2R's ops 4 and 5, which fire once the item's list is attached to a player or monster:
        /// ITEMDESC_Defense (0x1401d1df1) and ITEMDESC_GetMinMaxStats (0x1401d08a2) attach it to the
        /// viewer before reading the total, and STATLIST_PostStatToStatList re-runs every op-4/5 stat
        /// against that unit (0x14020e0f0 / 0x14020e1c5). Per entry, the evaluator 0x14020bff0 takes
        /// `level = stat(unit, opbase) &gt;&gt; valshift(opbase)` and skips at &lt;= 0, takes the
        /// source's combined value on the item and skips at 0, then adds
        /// `(source * level) &gt;&gt; opparam` (op 4, 0x14020c34e) or that as a percent of the target's
        /// pre-op value (op 5, 0x14020c690 onward). <paramref name="preOp"/> is the merged view before
        /// op 13; the base view is left alone, so the colour marker still sees base != total.
        /// </summary>
        public static void ResolveLevelScaled(
            IDictionary<int, int> merged,
            IDictionary<int, int> preOp,
            IReadOnlyList<ItemStatLevelOpEntry> entries,
            Func<int, int> unitStat)
        {
            if (merged == null || preOp == null || entries == null || unitStat == null)
            {
                return;
            }

            foreach (ItemStatLevelOpEntry entry in entries)
            {
                int level = unitStat(entry.OpBase) >> entry.OpBaseShift;
                if (level <= 0)
                {
                    continue;
                }

                int source;
                if (!preOp.TryGetValue(ItemStatReader.PackStatKey(0, entry.SourceStat), out source) || source == 0)
                {
                    continue;
                }

                int scaled = unchecked(source * level) >> entry.OpParam;
                int key = ItemStatReader.PackStatKey(0, entry.TargetStat);

                int added;
                if (entry.Op == 4)
                {
                    added = scaled;
                }
                else
                {
                    int target;
                    preOp.TryGetValue(key, out target);
                    added = ApplyPercentOverflowSafe(target, scaled);
                }

                int existing;
                merged[key] = unchecked((merged.TryGetValue(key, out existing) ? existing : 0) + added);
            }
        }

        /// <summary>
        /// D2R's overflow-safe percent (the stat-209 tail at 0x140228a0c and op 5 at 0x14020c697 use
        /// the same three forms).
        /// </summary>
        internal static int ApplyPercentOverflowSafe(int value, int percent)
        {
            if (value > 0x100000)
            {
                return (value & ~0xF) < 0x640 ? (int)((long)value * percent / 100) : (value / 100) * percent;
            }

            if (percent > 0x10000)
            {
                return (percent & ~0xF) < 0x640 ? (int)((long)value * percent / 100) : (percent / 100) * value;
            }

            return value * percent / 100;
        }

        /// <summary>
        /// D2ApplyPercent with a 100 divisor. 1.14d does this in integers — the truncation is
        /// load-bearing: a +10% prefix on a Throwing Knife (max throw damage 9) produces 0 and the
        /// item renders identically to an unmodified one.
        /// </summary>
        private static int ApplyPercent(int value, int percent)
        {
            return (int)((long)value * percent / 100);
        }
    }
}
