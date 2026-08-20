using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace D2ItemToolkit.Tests
{
    /// <summary>
    /// The order of two stats that share a descpriority.
    ///
    /// SORT_ItemDescPriority 0x6379d0 compares the priority word alone and returns 0 for a tie, so
    /// the game's order within a tie group is decided entirely by the CRT qsort permutation at
    /// 0x638571. This was a known divergence until a capture of Call to Arms discriminated it: the
    /// game prints the three oskills ABOVE Prevent Monster Heal, all four at priority 81.
    /// </summary>
    public class DescPriorityOrderTests
    {
        private static readonly D2DataFiles Data = D2DataFiles.LoadEmbedded();

        // Battle Cry, Battle Orders and Battle Command — skills.txt rows 146, 149 and 155.
        private const int BattleCry = 146;
        private const int BattleOrders = 149;
        private const int BattleCommand = 155;

        private const int PreventMonsterHeal = 117; // item_preventheal
        private const int NonClassSkill = 97; // item_nonclassskill, the oskill stat

        [Fact]
        public void The_described_stats_come_out_in_the_crt_qsort_permutation()
        {
            // Every tie group, in the order 0x638571 leaves them. Eight of the twelve differ from
            // a plain ascending-stat-id tie-break, so this is the pin that stops one creeping back
            // in: a stable sort would leave every group in ascending id order.
            var expected = new Dictionary<int, int[]>
            {
                { 1, new[] { 252, 204 } },
                { 8, new[] { 87, 80 } }, // Gheed's Fortune
                { 11, new[] { 114, 85 } },
                { 16, new[] { 86, 138 } },
                { 22, new[] { 34, 36 } },
                { 33, new[] { 233, 147 } },
                { 81, new[] { 117, 107, 108, 97 } }, // Call to Arms
                {
                    88,
                    new[] { 306, 305, 335, 308, 329, 330, 60, 336, 307, 331, 332, 333, 334 }
                },
                { 106, new[] { 124, 180 } },
                { 108, new[] { 122, 179 } },
                { 160, new[] { 195, 197, 198, 199, 201, 152, 196 } },
                {
                    180,
                    new[]
                    {
                        280, 281, 282, 283, 284, 285, 286, 272, 288, 289, 290, 279, 293, 294, 295,
                        296, 297, 298, 299, 300, 301, 302, 303, 278, 271, 277, 276, 275, 274, 292,
                        270, 273, 269, 268, 287,
                    }
                },
            };

            IReadOnlyList<int> order = Data.ItemStatCost.StatIdsByDescPriority;

            var grouped = new Dictionary<int, List<int>>();
            foreach (int statId in order)
            {
                StatDescriptor descriptor;
                Assert.True(Data.ItemStatCost.TryGetStat(statId, out descriptor));

                List<int> bucket;
                if (!grouped.TryGetValue(descriptor.DescPriority, out bucket))
                {
                    bucket = new List<int>();
                    grouped.Add(descriptor.DescPriority, bucket);
                }

                bucket.Add(statId);
            }

            // 12 groups covering 75 of the 207 described stats — counted against the shipped file,
            // so a data change that adds or drops a tie fails here rather than silently.
            Assert.Equal(207, order.Count);
            Assert.Equal(12, grouped.Count(g => g.Value.Count > 1));
            Assert.Equal(75, grouped.Where(g => g.Value.Count > 1).Sum(g => g.Value.Count));

            foreach (KeyValuePair<int, int[]> group in expected)
            {
                Assert.Equal(group.Value, grouped[group.Key].ToArray());
            }
        }

        [Fact]
        public void The_priorities_are_still_ascending()
        {
            // The permutation reorders ties only; the fold in SKILLDESC_BuildStatBuffDesc walks
            // forward and depends on the array being ordered.
            int previous = int.MinValue;

            foreach (int statId in Data.ItemStatCost.StatIdsByDescPriority)
            {
                StatDescriptor descriptor;
                Assert.True(Data.ItemStatCost.TryGetStat(statId, out descriptor));
                Assert.True(descriptor.DescPriority >= previous);
                previous = descriptor.DescPriority;
            }
        }

        [Fact]
        public void Call_to_arms_prints_its_oskills_above_prevent_monster_heal()
        {
            // The captured game tooltip, which is what re-opened this. Lines come back in APPEND
            // order and the renderer draws them bottom-up, so Prevent Monster Heal appearing FIRST
            // here is what puts it BELOW the three oskills on screen.
            IReadOnlyList<ItemDescriptionLine> lines =
                new ItemDescriptionGenerator(
                        Data.ItemStatCost, Data.Strings, null, Data.Skills, Data.Classes)
                    .Describe(
                        new[]
                        {
                            Build.Entry(NonClassSkill, 6, BattleOrders),
                            Build.Entry(PreventMonsterHeal, 1),
                            Build.Entry(NonClassSkill, 1, BattleCry),
                            Build.Entry(NonClassSkill, 4, BattleCommand),
                        });

            Assert.Equal(
                new[]
                {
                    "Prevent Monster Heal",
                    "+1 to Battle Cry",
                    "+6 to Battle Orders",
                    "+4 to Battle Command",
                },
                lines.Select(l => l.Text).ToArray());
        }

        [Fact]
        public void The_short_sort_rotates_equal_elements()
        {
            // _shortsort 0x685ac0 is a selection sort. With every element equal the maximum stays
            // at lo, so each pass swaps lo with the shrinking hi and the run comes out rotated
            // left by one — not reversed, and not stable. Eight or fewer elements never reach the
            // partition path at all (0x685bfe), which is why every two-element tie group moved.
            var items = new[] { 0, 1, 2, 3, 4 };
            CrtQsort.Sort(items, (a, b) => 0);

            Assert.Equal(new[] { 1, 2, 3, 4, 0 }, items);
        }

        [Fact]
        public void A_single_element_is_left_alone()
        {
            // 0x685bd1: cmp esi, 2 / jb — fewer than two elements returns before touching memory.
            var items = new[] { 7 };
            CrtQsort.Sort(items, (a, b) => 0);

            Assert.Equal(new[] { 7 }, items);
        }
    }
}
