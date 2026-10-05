using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Xunit;

namespace D2ItemToolkit.Tests
{
    /// <summary>
    /// PropertyGroups.txt choices and func-25 stat picks in the roll-range reconstruction. Every
    /// shipped user is pinned: Wraithstep, Opalvein, the six crafted charms and the six group
    /// prefixes. The TypeScript suite asserts the same strings.
    /// </summary>
    public class PropertyGroupTests
    {
        private static readonly TooltipEngine Engine =
            TooltipEngine.ForVariant(GameVariant.ReignOfTheWarlock);

        private static readonly D2DataFiles Data = Engine.Data;

        private static RolledRangeReconstructor Reconstructor(D2DataFiles data = null)
        {
            D2DataFiles source = data ?? Data;
            return new RolledRangeReconstructor(
                source,
                Engine.Items,
                Engine.Types,
                new MagicAffixTable(source),
                new SetTable(source.Sets, source.SetItems, source.Strings));
        }

        private static int Key(int statId, int layer = 0)
        {
            return ItemStatReader.PackStatKey(layer, statId);
        }

        private static ItemIdentity Unique(string index, int expectedRow)
        {
            int row = Data.UniqueItems.FindRow("index", index);
            Assert.Equal(expectedRow, row);

            var unit = new Unit();
            unit.UnitType = 4;
            unit.Quality = 7;
            unit.FileIndex = row;
            unit.ClassId = Engine.Items.ClassIdForCode(
                Data.UniqueItems.GetString(row, "code").Trim());
            unit.ItemFlags = ItemRecordFlags.Identified;
            return ItemRecordReader.ReadIdentity(unit);
        }

        private static ItemIdentity Magic(int prefixId, string code)
        {
            var unit = new Unit();
            unit.UnitType = 4;
            unit.Quality = 4;
            unit.ClassId = Engine.Items.ClassIdForCode(code);
            unit.ItemFlags = ItemRecordFlags.Identified;
            unit.MagicPrefix[0] = prefixId;
            return ItemRecordReader.ReadIdentity(unit);
        }

        private static ItemRollRanges Ranges(
            ItemIdentity item, Dictionary<int, int> recorded, D2DataFiles data = null)
        {
            return Reconstructor(data).Reconstruct(item, recorded, null, null);
        }

        private static string Describe(RolledStatRange range)
        {
            return range.StatId + "@" + range.Layer + " " + range.Low + ".." + range.High
                + " (" + (int)range.Sources + ")";
        }

        private static string Describe(RolledChoice choice)
        {
            return choice.GroupRow + " " + choice.Code + " " + (int)choice.Sources + " "
                + choice.PickMode + " " + choice.CountLow + ".." + choice.CountHigh + " "
                + choice.Resolution + " " + Outcomes(choice.Consistent);
        }

        private static string Outcomes(IReadOnlyList<IReadOnlyList<RolledChoicePick>> outcomes)
        {
            return "[" + string.Join(",", outcomes.Select(
                outcome => "[" + string.Join(",", outcome.Select(
                    pick => pick.Option + ":" + pick.Param)) + "]")) + "]";
        }

        private static string Describe(RolledChoiceOption option)
        {
            return option.Entry + " " + option.PropertyId + " " + option.Code + " w" + option.Weight
                + " p" + option.ParamLow + ".." + option.ParamHigh + " m" + option.Min + ".."
                + option.Max + " | " + string.Join("; ", option.Variants.Select(
                    variant => variant.Param + ": " + string.Join(", ",
                        variant.Stats.Select(Describe))));
        }

        private static string[] Choices(ItemRollRanges ranges)
        {
            return ranges.Choices.Select(Describe).ToArray();
        }

        private static string[] Stats(ItemRollRanges ranges)
        {
            return ranges.Stats.Select(Describe).ToArray();
        }

        // ---- Wraithstep ------------------------------------------------------------------------

        private const string WraithstepOption =
            "1 124 skilltab w1 p21..23 m1..1 | 21: 188@56 1..1 (516); 22: 188@57 1..1 (516); "
            + "23: 188@58 1..1 (516)";

        [Theory]
        [InlineData(21, 56)]
        [InlineData(22, 57)]
        [InlineData(23, 58)]
        public void Wraithstep_resolves_the_tab_the_record_carries(int param, int layer)
        {
            ItemRollRanges ranges = Ranges(
                Unique("Wraithstep", 413),
                new Dictionary<int, int> { { Key(188, layer), 1 } });

            Assert.Equal(
                new[] { "0 skilltab-war 516 Exactly 1..1 Resolved [[0:" + param + "]]" },
                Choices(ranges));
            Assert.Equal(new[] { WraithstepOption }, ranges.Choices[0].Options.Select(Describe));
            Assert.Contains("188@" + layer + " 1..1 (516)", Stats(ranges));
            Assert.Single(ranges.Stats, r => r.StatId == 188);
            Assert.Empty(ranges.Unattributed);
            Assert.Empty(ranges.OutOfRange);
        }

        [Fact]
        public void Wraithstep_with_no_record_lists_every_tab_and_claims_none()
        {
            ItemRollRanges ranges = Ranges(Unique("Wraithstep", 413), null);

            Assert.Equal(
                new[] { "0 skilltab-war 516 Exactly 1..1 NoRecord [[0:21],[0:22],[0:23]]" },
                Choices(ranges));
            Assert.DoesNotContain(ranges.Stats, r => r.StatId == 188);
        }

        [Fact]
        public void Wraithstep_with_two_tabs_recorded_is_contradicted()
        {
            ItemRollRanges ranges = Ranges(
                Unique("Wraithstep", 413),
                new Dictionary<int, int> { { Key(188, 56), 1 }, { Key(188, 57), 1 } });

            Assert.Equal(
                new[] { "0 skilltab-war 516 Exactly 1..1 Contradicted [[0:21],[0:22],[0:23]]" },
                Choices(ranges));
            Assert.Equal(new[] { 188 }, ranges.OutOfRange);
        }

        [Fact]
        public void Wraithstep_with_no_tab_recorded_is_contradicted()
        {
            ItemRollRanges ranges = Ranges(
                Unique("Wraithstep", 413),
                new Dictionary<int, int> { { Key(96), 30 } });

            Assert.Equal(
                new[] { "0 skilltab-war 516 Exactly 1..1 Contradicted [[0:21],[0:22],[0:23]]" },
                Choices(ranges));
            Assert.Empty(ranges.OutOfRange);
        }

        // ---- Opalvein --------------------------------------------------------------------------

        [Fact]
        public void Opalvein_offers_the_six_magdam_rand_options()
        {
            ItemRollRanges ranges = Ranges(Unique("Opalvein", 416), null);

            Assert.Equal(
                new[] { "1 magdam-rand 516 Exactly 1..1 NoRecord [[0:0],[1:0],[2:0],[3:0],[4:0],[5:0]]" },
                Choices(ranges));
            Assert.Equal(
                new[]
                {
                    "1 283 extra-mag w1 p0..0 m3..5 | 0: 357@0 3..5 (516)",
                    "2 29 dmg% w1 p0..0 m20..40 | 0: 17@0 20..40 (516), 18@0 20..40 (516)",
                    "3 244 extra-fire w1 p0..0 m3..5 | 0: 329@0 3..5 (516)",
                    "4 246 extra-cold w1 p0..0 m3..5 | 0: 331@0 3..5 (516)",
                    "5 245 extra-ltng w1 p0..0 m3..5 | 0: 330@0 3..5 (516)",
                    "6 247 extra-pois w1 p0..0 m3..5 | 0: 332@0 3..5 (516)",
                },
                ranges.Choices[0].Options.Select(Describe));
            Assert.All(ranges.Choices[0].Options, option => Assert.Null(option.Nested));
        }

        [Theory]
        [InlineData(0, new[] { 357 }, 4, "357@0 3..5 (516)")]
        [InlineData(1, new[] { 17, 18 }, 33, "17@0 20..40 (516)|18@0 20..40 (516)")]
        [InlineData(2, new[] { 329 }, 4, "329@0 3..5 (516)")]
        [InlineData(3, new[] { 331 }, 4, "331@0 3..5 (516)")]
        [InlineData(4, new[] { 330 }, 4, "330@0 3..5 (516)")]
        [InlineData(5, new[] { 332 }, 4, "332@0 3..5 (516)")]
        public void Opalvein_resolves_each_option(int option, int[] stats, int value, string expected)
        {
            var recorded = new Dictionary<int, int>();
            foreach (int stat in stats)
            {
                recorded[Key(stat)] = value;
            }

            ItemRollRanges ranges = Ranges(Unique("Opalvein", 416), recorded);

            Assert.Equal(
                new[] { "1 magdam-rand 516 Exactly 1..1 Resolved [[" + option + ":0]]" },
                Choices(ranges));
            foreach (string line in expected.Split('|'))
            {
                Assert.Contains(line, Stats(ranges));
            }

            Assert.Empty(ranges.Unattributed);
            Assert.Empty(ranges.OutOfRange);
        }

        [Fact]
        public void Opalvein_with_two_options_recorded_is_contradicted()
        {
            ItemRollRanges ranges = Ranges(
                Unique("Opalvein", 416),
                new Dictionary<int, int> { { Key(329), 4 }, { Key(331), 4 } });

            Assert.Equal(
                new[] { "1 magdam-rand 516 Exactly 1..1 Contradicted [[0:0],[1:0],[2:0],[3:0],[4:0],[5:0]]" },
                Choices(ranges));
            Assert.Equal(new[] { 329, 331 }, ranges.OutOfRange);
        }

        // ---- the six crafted charms ------------------------------------------------------------

        [Fact]
        public void Crafted_Cold_Rupture_worked_example()
        {
            ItemRollRanges ranges = Ranges(
                Unique("Crafted Cold Rupture", 427),
                new Dictionary<int, int>
                {
                    { Key(187), 300 },
                    { Key(43), -70 },
                    { Key(335), 7 },
                    { Key(9), 40 << 8 },
                    { Key(80), 20 },
                    { Key(99), 18 },
                    { Key(34), 6 },
                });

            Assert.Equal(
                new[]
                {
                    "3 Gelid-Affix1 516 UpTo 1..1 Resolved [[1:0]]",
                    "9 Gelid-Affix2 516 UpTo 1..1 Resolved [[0:0]]",
                    "15 Gelid-Affix3 516 UpTo 1..1 Resolved [[1:0]]",
                    "21 Gelid-Affix4 516 UpTo 1..1 Resolved [[1:0]]",
                    "27 Gelid-Affix6 516 UpTo 1..1 Resolved [[1:0]]",
                },
                Choices(ranges));
            Assert.Equal(
                new[]
                {
                    "9@0 2560..19200 (516)",
                    "34@0 5..10 (516)",
                    "43@0 -70..-70 (4)",
                    "80@0 14..25 (516)",
                    "99@0 12..24 (516)",
                    "187@0 300..300 (4)",
                    "335@0 5..10 (516)",
                },
                Stats(ranges));
            Assert.Empty(ranges.Unattributed);
            Assert.Empty(ranges.OutOfRange);
        }

        [Fact]
        public void A_crafted_charms_options_are_the_groups_columns()
        {
            ItemRollRanges ranges = Ranges(Unique("Crafted Cold Rupture", 427), null);

            Assert.Equal(
                new[]
                {
                    "1 246 extra-cold w1 p0..0 m5..15 | 0: 331@0 5..15 (516)",
                    "2 234 pierce-cold w1 p0..0 m5..10 | 0: 335@0 5..10 (516)",
                    "1 59 mag% w1 p0..0 m14..25 | 0: 80@0 14..25 (516)",
                    "2 58 gold% w1 p0..0 m20..55 | 0: 79@0 20..55 (516)",
                    "1 13 hp w1 p0..0 m10..65 | 0: 7@0 2560..16640 (516)",
                    "2 11 mana w1 p0..0 m10..75 | 0: 9@0 2560..19200 (516)",
                    "1 76 move1 w1 p0..0 m5..10 | 0: 96@0 5..10 (516)",
                    "2 79 balance1 w3 p0..0 m12..24 | 0: 99@0 12..24 (516)",
                    "3 251 all-stats w1 p0..0 m3..8 | 0: 0@0 3..8 (516), 1@0 3..8 (516), "
                        + "2@0 3..8 (516), 3@0 3..8 (516)",
                    "1 6 red-mag w1 p0..0 m5..10 | 0: 35@0 5..10 (516)",
                    "2 3 red-dmg w1 p0..0 m5..10 | 0: 34@0 5..10 (516)",
                },
                ranges.Choices.SelectMany(c => c.Options).Select(Describe));
        }

        public static IEnumerable<object[]> Charms()
        {
            // index, row, group rows, pierce-immunity stat, fixed stat, fixed value, Affix1 option 0
            yield return new object[] { "Crafted Cold Rupture", 427, new[] { 3, 9, 15, 21, 27 }, 187, 43, -70, new[] { 331 } };
            yield return new object[] { "Crafted Flame Rift", 433, new[] { 5, 11, 17, 23, 29 }, 189, 39, -70, new[] { 329 } };
            yield return new object[] { "Crafted Crack of the Heavens", 434, new[] { 4, 10, 16, 22, 28 }, 190, 41, -70, new[] { 330 } };
            yield return new object[] { "Crafted Rotting Fissure", 435, new[] { 2, 8, 14, 20, 26 }, 191, 45, -70, new[] { 332 } };
            yield return new object[] { "Crafted Bone Break", 436, new[] { 6, 12, 18, 24, 30 }, 192, 36, -10, new[] { 17, 18 } };
            yield return new object[] { "Crafted Black Cleft", 437, new[] { 7, 13, 19, 25, 31 }, 193, 37, -45, new[] { 357 } };
        }

        [Theory]
        [MemberData(nameof(Charms))]
        public void Every_crafted_charm_resolves_one_fixed_pick_set(
            string index, int row, int[] groupRows, int pierceImmunity, int fixedStat,
            int fixedValue, int[] affix1)
        {
            // Affix1 option 0, Affix2 option 1 (gold%), Affix3 option 0 (hp), Affix4 option 2
            // (all-stats), Affix6 option 0 (red-mag).
            var recorded = new Dictionary<int, int>
            {
                { Key(pierceImmunity), 300 },
                { Key(fixedStat), fixedValue },
                { Key(79), 30 },
                { Key(7), 20 << 8 },
                { Key(0), 5 },
                { Key(1), 5 },
                { Key(2), 5 },
                { Key(3), 5 },
                { Key(35), 7 },
            };
            foreach (int stat in affix1)
            {
                // dmg% (17, 18) rolls 75..100; every other Affix1 option 0 rolls inside 5..15.
                recorded[Key(stat)] = affix1.Length == 2 ? 80 : 12;
            }

            ItemRollRanges ranges = Ranges(Unique(index, row), recorded);

            string[] picks = { "[[0:0]]", "[[1:0]]", "[[0:0]]", "[[2:0]]", "[[0:0]]" };
            Assert.Equal(
                groupRows.Select((group, i) => group + " "
                    + Data.PropertyGroups.GetString(group, "code") + " 516 UpTo 1..1 Resolved "
                    + picks[i]),
                Choices(ranges));
            Assert.Empty(ranges.Unattributed);
            Assert.Empty(ranges.OutOfRange);
        }

        [Fact]
        public void A_crafted_charm_annotates_its_picked_lines()
        {
            int row = Data.UniqueItems.FindRow("index", "Crafted Cold Rupture");

            var charm = new Unit();
            charm.UnitType = 4;
            charm.Quality = 7;
            charm.FileIndex = row;
            charm.ClassId = Engine.Items.ClassIdForCode("cs2");
            charm.ItemFlags = ItemRecordFlags.Identified;
            charm.StatsLists.Add(
                new UnitStatList(0, ItemStatListFlags.Magic)
                    .Add(187, 300)
                    .Add(43, -70)
                    .Add(335, 7)
                    .Add(9, 40 << 8)
                    .Add(80, 20)
                    .Add(99, 18)
                    .Add(34, 6));

            var options = new TooltipOptions();
            options.Ranges = new RangeDisplay();
            options.Ranges.Color = -1;

            string[] lines = Engine.Render(charm, null, options).Lines
                .Select(l => (l.Text ?? string.Empty).TrimEnd('\n'))
                .ToArray();

            Assert.Equal(
                new[]
                {
                    "Renewed Cold Rupture",
                    "Grand Charm",
                    "Keep in Inventory to Gain Bonus",
                    "Required Level: 75",
                    "Monster Cold Immunity is Sundered",
                    "+18% Faster Hit Recovery [12-24]",
                    "-7% to Enemy Cold Resistance [5-10]",
                    "+40 to Mana [10-75]",
                    "Cold Resist -70%",
                    "Damage Reduced by 6 [5-10]",
                    "20% Better Chance of Getting Magic Items [14-25]",
                },
                lines);
        }

        // ---- the six group prefixes ------------------------------------------------------------

        private const string VirulentA = "2 Virulent-Affix1 514 UpTo 0..1 ";
        private const string VirulentB = "8 Virulent-Affix2 514 UpTo 0..1 ";

        [Fact]
        public void Virulent_resolves_both_choices_when_both_picked()
        {
            ItemRollRanges ranges = Ranges(
                Magic(1501, "cm2"),
                new Dictionary<int, int> { { Key(336), 12 }, { Key(79), 30 } });

            Assert.Equal(
                new[] { VirulentA + "Resolved [[1:0]]", VirulentB + "Resolved [[1:0]]" },
                Choices(ranges));
            Assert.Contains("336@0 7..18 (514)", Stats(ranges));
            Assert.Contains("79@0 20..55 (514)", Stats(ranges));
            Assert.Empty(ranges.OutOfRange);
        }

        [Fact]
        public void Virulent_below_the_group_floor_resolves_to_nothing_picked()
        {
            ItemRollRanges ranges = Ranges(
                Magic(1501, "cm2"), new Dictionary<int, int> { { Key(336), 5 } });

            Assert.Equal(
                new[] { VirulentA + "Resolved [[]]", VirulentB + "Resolved [[]]" },
                Choices(ranges));
            Assert.Contains("336@0 2..8 (2)", Stats(ranges));
        }

        [Fact]
        public void Virulent_inside_both_spans_is_ambiguous()
        {
            ItemRollRanges ranges = Ranges(
                Magic(1501, "cm2"), new Dictionary<int, int> { { Key(336), 7 } });

            Assert.Equal(
                new[] { VirulentA + "Ambiguous [[],[1:0]]", VirulentB + "Resolved [[]]" },
                Choices(ranges));
            Assert.Contains("336@0 2..18 (514)", Stats(ranges));
            Assert.Empty(ranges.Unattributed);
            Assert.Empty(ranges.OutOfRange);
        }

        [Fact]
        public void Virulent_resolves_the_mastery_option()
        {
            ItemRollRanges ranges = Ranges(
                Magic(1501, "cm2"),
                new Dictionary<int, int> { { Key(336), 4 }, { Key(332), 9 } });

            Assert.Equal(
                new[] { VirulentA + "Resolved [[0:0]]", VirulentB + "Resolved [[]]" },
                Choices(ranges));
            Assert.Contains("336@0 2..8 (2)", Stats(ranges));
            Assert.Contains("332@0 5..15 (514)", Stats(ranges));
        }

        [Fact]
        public void Virulent_above_every_span_is_contradicted()
        {
            ItemRollRanges ranges = Ranges(
                Magic(1501, "cm2"), new Dictionary<int, int> { { Key(336), 20 } });

            Assert.Equal(
                new[]
                {
                    VirulentA + "Contradicted [[],[0:0],[1:0]]",
                    VirulentB + "Contradicted [[],[0:0],[1:0]]",
                },
                Choices(ranges));
            Assert.Equal(new[] { 336 }, ranges.OutOfRange);
        }

        [Theory]
        // prefix id, base, mod1 stat, A group row, A option 0 stats, B group row
        [InlineData(1502, "cm2", 333, 3, new[] { 331 }, 9)]
        [InlineData(1503, "qui", 335, 4, new[] { 330 }, 10)]
        [InlineData(1504, "qui", 334, 5, new[] { 329 }, 11)]
        [InlineData(1505, "qui", 358, 6, new[] { 17, 18 }, 12)]
        [InlineData(1506, "qui", 366, 7, new[] { 357 }, 13)]
        public void The_cross_wired_prefixes_resolve_by_the_group_their_cells_name(
            int prefixId, string code, int mod1, int groupA, int[] optionStats, int groupB)
        {
            var recorded = new Dictionary<int, int> { { Key(mod1), 3 } };
            foreach (int stat in optionStats)
            {
                recorded[Key(stat)] = optionStats.Length == 2 ? 80 : 11;
            }

            ItemRollRanges ranges = Ranges(Magic(prefixId, code), recorded);

            Assert.Equal(
                new[]
                {
                    groupA + " " + Data.PropertyGroups.GetString(groupA, "code")
                        + " 514 UpTo 0..1 Resolved [[0:0]]",
                    groupB + " " + Data.PropertyGroups.GetString(groupB, "code")
                        + " 514 UpTo 0..1 Resolved [[]]",
                },
                Choices(ranges));
            Assert.Contains(mod1 + "@0 2..8 (2)", Stats(ranges));
            Assert.Empty(ranges.Unattributed);
            Assert.Empty(ranges.OutOfRange);
        }

        [Fact]
        public void Incendiary_names_the_Gelid_groups()
        {
            ItemRollRanges ranges = Ranges(
                Magic(1502, "cm2"),
                new Dictionary<int, int> { { Key(333), 3 }, { Key(331), 11 } });

            Assert.Equal(
                new[]
                {
                    "3 Gelid-Affix1 514 UpTo 0..1 Resolved [[0:0]]",
                    "9 Gelid-Affix2 514 UpTo 0..1 Resolved [[]]",
                },
                Choices(ranges));
            Assert.Contains("333@0 2..8 (2)", Stats(ranges));
            Assert.Contains("331@0 5..15 (514)", Stats(ranges));
        }

        // ---- 1.14d and the table itself --------------------------------------------------------

        [Fact]
        public void A_1_14d_item_has_no_choices()
        {
            D2DataFiles lod = D2DataFiles.LoadEmbedded();
            Assert.Null(lod.PropertyGroups);

            var unit = new Unit();
            unit.UnitType = 4;
            unit.Quality = 7;
            unit.FileIndex = lod.UniqueItems.FindRow("index", "The Eye of Etlich");
            unit.ClassId = TooltipEngine.Embedded.Items.ClassIdForCode(
                lod.UniqueItems.GetString(unit.FileIndex, "code").Trim());
            unit.ItemFlags = ItemRecordFlags.Identified;

            Assert.Empty(TooltipEngine.Embedded.Ranges(unit).Choices);
        }

        [Fact]
        public void The_shipped_tables_have_32_and_20_groups()
        {
            var rotw = new PropertyGroupsTable(
                Data.PropertyGroups, new PropertiesTable(Data.Properties, Data.ItemStatCost));
            Assert.Equal(32, rotw.RowCount);
            Assert.Equal(1, rotw[0].PickMode);
            Assert.Equal(124, rotw[0].Entries[0].Prop.Row);
            Assert.Equal(PropertyRef.KindProperty, rotw[0].Entries[0].Prop.Kind);
            Assert.Equal(21, rotw[0].Entries[0].ParMin);
            Assert.Equal(23, rotw[0].Entries[0].ParMax);
            Assert.Equal(-1, rotw[0].Entries[1].Prop.Row);
            Assert.Equal(3, rotw[20].Entries[1].Weight);
            Assert.Equal(26, rotw.RowForCode("virulent-affix6"));

            D2DataFiles lod = D2DataFiles.LoadEmbedded(GameVariant.Resurrected);
            Assert.Equal(
                20,
                new PropertyGroupsTable(
                    lod.PropertyGroups, new PropertiesTable(lod.Properties, lod.ItemStatCost))
                    .RowCount);
        }

        [Fact]
        public void A_group_entry_may_name_only_an_earlier_group()
        {
            var text = new StringBuilder("code\tpickmode\tprop1\tprop2\tprop3\r\n");
            for (int row = 0; row < 6; ++row)
            {
                string props = row == 3 ? "g3\tg5\tg1" : "\t\t";
                text.Append("g" + row + "\t1\t" + props + "\r\n");
            }

            var groups = new PropertyGroupsTable(
                TxtFile.Parse(text.ToString(), GameVariant.ReignOfTheWarlock),
                new PropertiesTable(Data.Properties, Data.ItemStatCost));

            PropertyGroupsTable.Entry[] entries = groups[3].Entries;
            Assert.Equal(-1, entries[0].Prop.Row);
            Assert.Equal(PropertyRef.KindGroup, entries[0].Prop.Kind);
            Assert.Equal(-1, entries[1].Prop.Row);
            Assert.Equal(1, entries[2].Prop.Row);
            Assert.Equal(PropertyRef.KindGroup, entries[2].Prop.Kind);
            Assert.Equal(PropertyRef.KindUnresolved, entries[3].Prop.Kind);
        }

        [Fact]
        public void Properties_win_over_a_group_of_the_same_code()
        {
            var groups = new PropertyGroupsTable(
                TxtFile.Parse("code\tpickmode\r\nstr\t0\r\nmine\t0\r\n", GameVariant.ReignOfTheWarlock),
                new PropertiesTable(Data.Properties, Data.ItemStatCost));
            var properties = new PropertiesTable(Data.Properties, Data.ItemStatCost);

            PropertyRef str = PropertyGroupsTable.Link("STR", properties, groups);
            Assert.Equal(properties.RowForCode("str"), str.Row);
            Assert.Equal(PropertyRef.KindProperty, str.Kind);

            PropertyRef mine = PropertyGroupsTable.Link("MINE", properties, groups);
            Assert.Equal(1, mine.Row);
            Assert.Equal(PropertyRef.KindGroup, mine.Kind);
        }

        // ---- synthetic: pickmode 0 and func 25 -------------------------------------------------

        [Fact]
        public void A_pickmode_0_group_and_a_func_25_stat_pick()
        {
            D2DataFiles data = Synthetic();
            int target = new PropertiesTable(data.Properties, data.ItemStatCost)
                .RowForCode("test-target");

            ItemRollRanges ranges = Reconstructor(data).Reconstruct(
                Unique("Wraithstep", 413),
                new Dictionary<int, int>
                {
                    { Key(39), 4 },
                    { Key(41), 7 },
                    { Key(2), 16 },
                },
                null,
                null);

            Assert.Equal(
                new[]
                {
                    "32 test-all 516 All 2..2 Resolved [[0:0,1:0]]",
                    "-1 test-statpick 516 StatPick 1..1 Resolved [[0:" + target + "]]",
                },
                Choices(ranges));
            Assert.Equal(
                new[]
                {
                    "1 31 res-fire w1 p0..0 m3..5 | 0: 39@0 3..5 (516)",
                    "2 33 res-ltng w1 p0..0 m7..7 | 0: 41@0 7..7 (516)",
                },
                ranges.Choices[0].Options.Select(Describe));

            // Strength (stat 0) is never a candidate, and an unresolved stat name links to -1.
            Assert.Equal(
                new[]
                {
                    "2 " + target + " test-target w1 p" + target + ".." + target + " m4..6 | "
                        + target + ": 2@0 4..6 (516)",
                    "3 " + target + " test-target w1 p" + target + ".." + target + " m4..6 | "
                        + target + ": 7@0 1024..1536 (516)",
                },
                ranges.Choices[1].Options.Select(Describe));

            Assert.Contains("39@0 3..5 (516)", Stats(ranges));
            Assert.Contains("41@0 7..7 (516)", Stats(ranges));
            Assert.Contains("2@0 14..21 (516)", Stats(ranges));
            Assert.Empty(ranges.UnsupportedFuncs);
            Assert.Empty(ranges.OutOfRange);
        }

        /// <summary>
        /// The embedded RotW tables with two properties and three groups appended, and Wraithstep's
        /// first two cells pointed at them.
        /// </summary>
        private static D2DataFiles Synthetic()
        {
            string root = Path.Combine(
                Path.GetTempPath(), "d2tooltip-groups-" + Guid.NewGuid().ToString("N"));
            string excel = Path.Combine(root, "excel");
            string strings = Path.Combine(root, "strings");
            Directory.CreateDirectory(excel);
            Directory.CreateDirectory(strings);

            try
            {
                Assembly assembly = typeof(D2DataFiles).Assembly;
                foreach (string name in D2DataFiles.EmbeddedResourceNames)
                {
                    string directory = name.StartsWith("d2r.excel.", StringComparison.Ordinal)
                        && !name.StartsWith("d2r.excel.base.", StringComparison.Ordinal)
                            ? excel
                            : name.StartsWith("d2r.strings.", StringComparison.Ordinal)
                                ? strings
                                : null;
                    if (directory == null)
                    {
                        continue;
                    }

                    string file = name.Substring(directory == excel ? 10 : 12);
                    using (Stream stream = assembly.GetManifestResourceStream("D2ItemToolkit.Data." + name))
                    using (FileStream output = File.Create(Path.Combine(directory, file)))
                    {
                        Assert.NotNull(stream);
                        stream.CopyTo(output);
                    }
                }

                Edit(Path.Combine(excel, "properties.txt"), (header, rows) =>
                {
                    rows.Add(Row(header, new Dictionary<string, string>
                    {
                        { "code", "test-statpick" }, { "func1", "25" },
                    }));
                    rows.Add(Row(header, new Dictionary<string, string>
                    {
                        { "code", "test-target" }, { "stat1", "strength" }, { "stat2", "dexterity" },
                        { "stat3", "maxhp" }, { "stat4", "no-such-stat" },
                    }));
                });

                Edit(Path.Combine(excel, "propertygroups.txt"), (header, rows) =>
                {
                    rows.Add(Row(header, new Dictionary<string, string>
                    {
                        { "code", "test-all" }, { "PickMode", "0" },
                        { "Prop1", "res-fire" }, { "ModMin1", "3" }, { "ModMax1", "5" },
                        { "Prop2", "res-ltng" }, { "ModMin2", "7" }, { "ModMax2", "7" },
                    }));
                });

                Edit(Path.Combine(excel, "uniqueitems.txt"), (header, rows) =>
                {
                    int index = Array.IndexOf(header, "index");
                    int at = rows.FindIndex(r => r.Length > index && r[index] == "Wraithstep");
                    string[] row = rows[at];
                    row[Array.IndexOf(header, "prop1")] = "test-all";
                    row[Array.IndexOf(header, "prop2")] = "test-statpick";
                    row[Array.IndexOf(header, "par2")] = "test-target-row";
                    row[Array.IndexOf(header, "min2")] = "4";
                    row[Array.IndexOf(header, "max2")] = "6";
                });

                // The param is a row number, known only once properties.txt is extended.
                int target = TxtFile.Load(File.ReadAllBytes(Path.Combine(excel, "properties.txt")),
                    GameVariant.ReignOfTheWarlock).FindRow("code", "test-target");
                string uniques = File.ReadAllText(Path.Combine(excel, "uniqueitems.txt"), Latin1);
                File.WriteAllText(
                    Path.Combine(excel, "uniqueitems.txt"),
                    uniques.Replace("test-target-row", target.ToString()),
                    Latin1);

                return D2DataFiles.LoadResurrected(GameVariant.ReignOfTheWarlock, excel, strings);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        // The tables are bytes, not UTF-8: a round trip through the default encoding would
        // rewrite any high byte.
        private static readonly Encoding Latin1 = Encoding.GetEncoding(28591);

        private static string[] Row(string[] header, Dictionary<string, string> cells)
        {
            return header.Select(column =>
            {
                string value;
                return cells.TryGetValue(column, out value) ? value : string.Empty;
            }).ToArray();
        }

        private static void Edit(string path, Action<string[], List<string[]>> edit)
        {
            string[] lines = File.ReadAllText(path, Latin1).Split(new[] { "\r\n" }, StringSplitOptions.None);
            string[] header = lines[0].Split('\t');
            // The final CRLF leaves one empty element; interior blank lines are rows and stay.
            var rows = lines.Skip(1).Take(lines.Length - 2).Select(l => l.Split('\t')).ToList();
            edit(header, rows);
            File.WriteAllText(
                path,
                string.Join("\r\n", new[] { lines[0] }.Concat(rows.Select(r => string.Join("\t", r))))
                    + "\r\n",
                Latin1);
        }
    }
}
