using System.Collections.Generic;
using Xunit;

namespace D2ItemToolkit.Tests
{
    /// <summary>
    /// The C# peer of tests/D2ItemToolkit.Ts.Tests/Stats/ItemStatOps.test.ts. Both pin the same rule:
    /// an op-13 percent stat is folded onto its targets and then REMOVED from an item's merged
    /// view, because STATLIST_ApplyComplexStatFormula refuses to store it in FullStats when the
    /// owner is an item (0x626821 / 0x626847).
    /// </summary>
    public class ItemStatOpsTests
    {
        /// <summary>
        /// The reverse index ItemStatCost.txt's five op-13 rows compile to — 16 drives 31,
        /// 17 drives 22/24/160, 18 drives 21/23/159, 75 drives 73 and 94 drives 92.
        /// </summary>
        private static readonly ItemStatOpEntry[] ShippedEntries =
        {
            new ItemStatOpEntry(16, 31),
            new ItemStatOpEntry(17, 22),
            new ItemStatOpEntry(17, 24),
            new ItemStatOpEntry(17, 160),
            new ItemStatOpEntry(18, 21),
            new ItemStatOpEntry(18, 23),
            new ItemStatOpEntry(18, 159),
            new ItemStatOpEntry(75, 73),
            new ItemStatOpEntry(94, 92),
        };

        private sealed class FakeTable : IItemStatOpTable
        {
            private readonly IReadOnlyList<ItemStatOpEntry> _entries;

            public FakeTable(IReadOnlyList<ItemStatOpEntry> entries)
            {
                _entries = entries;
            }

            public IReadOnlyList<ItemStatOpEntry> PercentOfBaseEntries
            {
                get { return _entries; }
            }
        }

        private static SortedDictionary<int, int> Stats(params int[] statsAndValues)
        {
            var map = new SortedDictionary<int, int>();
            for (int i = 0; i < statsAndValues.Length; i += 2)
            {
                map[ItemStatReader.PackStatKey(0, statsAndValues[i])] = statsAndValues[i + 1];
            }

            return map;
        }

        private static int? At(IDictionary<int, int> merged, int stat)
        {
            int value;
            return merged.TryGetValue(ItemStatReader.PackStatKey(0, stat), out value)
                ? value
                : (int?)null;
        }

        [Fact]
        public void A_percent_that_landed_on_a_target_is_dropped_from_the_merged_view()
        {
            // 75 folds onto 73, then 0x626821 clears the update flag because the owner is an item
            // and 0x626847 skips the write that would have stored 75 itself.
            SortedDictionary<int, int> merged = Stats(73, 62, 75, 25);

            ItemStatOps.Resolve(merged, Stats(73, 62), new FakeTable(ShippedEntries));

            Assert.Equal(77, At(merged, 73)); // 62 + trunc(62 * 25 / 100)
            Assert.Null(At(merged, 75));
        }

        [Fact]
        public void A_percent_whose_target_computed_zero_survives()
        {
            // 0x62678d/0x626790: a target that computes ZERO skips the switch entirely, so the flag
            // is never cleared and the percent stat IS stored. Stat 94's target 92 is absent here,
            // so it never lands and nothing drops it.
            SortedDictionary<int, int> merged = Stats(94, 40);

            ItemStatOps.Resolve(merged, Stats(), new FakeTable(ShippedEntries));

            Assert.Equal(40, At(merged, 94));
        }

        [Fact]
        public void One_non_zero_target_is_enough_to_drop_a_percent_with_three()
        {
            // The flag is per PERCENT stat, not per target: 18 drives 21, 23 and 159, and only 21
            // exists here. 0x626847 tests the single flag after all three targets.
            SortedDictionary<int, int> merged = Stats(21, 4, 18, 150);

            ItemStatOps.Resolve(merged, Stats(21, 4), new FakeTable(ShippedEntries));

            Assert.Equal(10, At(merged, 21)); // 4 + trunc(4 * 150 / 100)
            Assert.Null(At(merged, 18));
        }

        [Fact]
        public void A_percent_still_drops_when_its_product_truncated_to_nothing()
        {
            // Throwing Knife: trunc(9 * 10 / 100) == 0, so the NUMBER does not move — but the gate
            // is on the target's computed value, which is 9, not on whether the percent changed it.
            // This is why the throw line comes out unmarked.
            SortedDictionary<int, int> merged = Stats(159, 4, 160, 9, 17, 10, 18, 10);

            ItemStatOps.Resolve(merged, Stats(159, 4, 160, 9), new FakeTable(ShippedEntries));

            Assert.Equal(4, At(merged, 159));
            Assert.Equal(9, At(merged, 160));
            Assert.Null(At(merged, 17));
            Assert.Null(At(merged, 18));
        }

        [Fact]
        public void The_base_view_is_never_touched()
        {
            // BaseOnly IS this pass's input (0x624ed4 always reads Stats), and Bonus is
            // merged-minus-base — so the drop must not reach it or the subtraction changes meaning.
            SortedDictionary<int, int> baseStats = Stats(73, 62, 75, 25);
            SortedDictionary<int, int> merged = Stats(73, 62, 75, 25);

            ItemStatOps.Resolve(merged, baseStats, new FakeTable(ShippedEntries));

            Assert.Equal(62, At(baseStats, 73));
            Assert.Equal(25, At(baseStats, 75));
        }
    }
}
