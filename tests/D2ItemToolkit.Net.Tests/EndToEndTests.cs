using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace D2ItemToolkit.Tests
{
    /// <summary>
    /// Reader to description, end to end: a stored record becomes a view, and the view
    /// becomes tooltip lines.
    ///
    /// The stat-to-DescFunc mapping below is an illustration, not a transcription of
    /// vanilla itemstatcost.txt. Real data comes from the IItemStatCostTable
    /// implementation you plug in.
    /// </summary>
    public class EndToEndTests
    {
        private const string Record = @"{
            
            ""statsLists"": [
              { ""source"": ""base"", ""stateNo"": 0, ""flags"": 2147483648,
                ""stats"": [ { ""id"": 31, ""value"": 445 }, { ""id"": 72, ""value"": 60 },
                             { ""id"": 73, ""value"": 60 }, { ""id"": 194, ""value"": 2 } ] },
              { ""source"": ""quality"", ""stateNo"": 0, ""flags"": 64,
                ""stats"": [ { ""id"": 16, ""value"": 180 }, { ""id"": 39, ""value"": 40 } ] },
              { ""source"": ""setBonus"", ""stateNo"": 165, ""flags"": 8256,
                ""stats"": [ { ""id"": 0, ""value"": 20 } ] }
            ],
            ""items"": [
              { ""classId"": 620,
                ""statsLists"": [ { ""source"": ""quality"", ""stateNo"": 0, ""flags"": 64,
                    ""stats"": [ { ""id"": 17, ""value"": 15 },
                                 { ""id"": 97, ""layer"": 2, ""value"": 1 } ] } ] },
              { ""classId"": 604,
                ""statsLists"": [ { ""source"": ""quality"", ""stateNo"": 0, ""flags"": 64,
                    ""stats"": [ { ""id"": 39, ""value"": 38 } ] } ] }
            ]
        }";

        private static ItemDescriptionGenerator BuildGenerator()
        {
            var stats = new FakeStatCostTable();

            // Ordered high priority first, as IItemStatCostTable requires.
            stats.Add(Build.Stat(97, ItemDescFunc.Skill, 300, priority: 120));
            stats.Add(Build.Stat(17, ItemDescFunc.PlusValuePercentString, 301, priority: 110));
            stats.Add(Build.Stat(16, ItemDescFunc.PlusValuePercentString, 302, priority: 100));
            stats.Add(Build.Stat(39, ItemDescFunc.PlusValueString, 303, descVal: 2, priority: 50));
            stats.Add(Build.Stat(0, ItemDescFunc.PlusValueString, 304, priority: 40));

            // Printed by dedicated client code ahead of the DescFunc loop, so DescFunc 0.
            stats.Add(Build.Stat(31, 0, 0));
            stats.Add(Build.Stat(72, 0, 0));
            stats.Add(Build.Stat(73, 0, 0));
            stats.Add(Build.Stat(194, 0, 0));

            var strings = new FakeStringTable().WithPunctuation()
                .Add(301, "Enhanced Damage")
                .Add(302, "Enhanced Defense")
                .Add(303, "Fire Resist")
                .Add(304, "to Strength");

            var skills = new FakeSkillTable().Add(2, "Charged Bolt");

            return new ItemDescriptionGenerator(stats, strings, null, skills);
        }

        [Fact]
        public void An_item_for_sale_describes_its_own_mods_and_its_sockets()
        {
            Unit doc = Unit.FromJson(Record);
            {
                SortedDictionary<int, int> view =
                    ItemStatReader.ReconstructView(doc, ItemStatView.ForSale());

                string[] lines = BuildGenerator().Describe(view).Select(l => l.Text).ToArray();

                Assert.Equal(new[]
                {
                    "+1 to Charged Bolt",       // socket 0, a jewel
                    "+15% Enhanced Damage",     // socket 0, same jewel
                    "+180% Enhanced Defense",   // the item's own affix
                    "Fire Resist +78",          // 40 on the item plus 38 from the ruby
                }, lines);
            }
        }

        [Fact]
        public void An_unearned_set_bonus_is_absent_from_the_for_sale_description()
        {
            Unit doc = Unit.FromJson(Record);
            {
                SortedDictionary<int, int> view =
                    ItemStatReader.ReconstructView(doc, ItemStatView.ForSale());

                Assert.DoesNotContain("to Strength",
                    string.Join("\n", BuildGenerator().Describe(view).Select(l => l.Text)));
            }
        }

        [Fact]
        public void The_set_bonus_can_be_described_separately_for_an_equipped_view()
        {
            Unit doc = Unit.FromJson(Record);
            {
                SortedDictionary<int, int> view =
                    ItemStatReader.ReconstructView(doc, ItemStatView.SetBonuses(true));

                Assert.Equal(new[] { "+20 to Strength" },
                    BuildGenerator().Describe(view).Select(l => l.Text).ToArray());
            }
        }

        [Fact]
        public void Stats_the_client_prints_itself_are_left_out_of_the_desc_func_lines()
        {
            Unit doc = Unit.FromJson(Record);
            {
                SortedDictionary<int, int> view =
                    ItemStatReader.ReconstructView(doc, ItemStatView.ForSale());

                IReadOnlyList<ItemDescriptionLine> lines = BuildGenerator().Describe(view);

                // Defence, durability and sockets are present in the view but carry no
                // DescFunc, so they never reach the tooltip through this path.
                Assert.Contains(ItemStatReader.PackStatKey(0, 31), view.Keys);
                Assert.DoesNotContain(31, lines.Select(l => l.StatId));
                Assert.DoesNotContain(194, lines.Select(l => l.StatId));
            }
        }

        [Fact]
        public void A_socket_can_be_described_on_its_own()
        {
            Unit doc = Unit.FromJson(Record);
            {
                // A filler is a record of the same shape, so it describes through the same reader.
                IUnit filler = doc.Items.ElementAt(1);

                SortedDictionary<int, int> view =
                    ItemStatReader.ReconstructView(filler, ItemStatView.ItemOnly());

                Assert.Equal(new[] { "Fire Resist +38" },
                    BuildGenerator().Describe(view).Select(l => l.Text).ToArray());
            }
        }
    }
}
