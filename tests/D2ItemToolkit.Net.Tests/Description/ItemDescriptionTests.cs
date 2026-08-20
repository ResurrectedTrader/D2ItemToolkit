using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace D2ItemToolkit.Tests
{
    /// <summary>
    /// Behaviour is asserted against the disassembly rather
    /// than from community DescFunc tables. Where a test looks surprising it is usually
    /// because the community table is wrong; those cases name the address.
    /// </summary>
    public class ItemDescriptionTests
    {
        private static ItemDescriptionGenerator Gen(
            FakeStatCostTable stats,
            FakeStringTable strings,
            FakeStatValues values = null,
            FakeSkillTable skills = null,
            FakeClassTable classes = null,
            FakeMonsterTable monsters = null,
            FakeGameTime byTime = null)
        {
            return new ItemDescriptionGenerator(stats, strings, values, skills, classes, monsters, byTime);
        }

        private static string One(ItemDescriptionGenerator generator, params KeyValuePair<int, int>[] entries)
        {
            IReadOnlyList<ItemDescriptionLine> lines = generator.Describe(entries);
            Assert.Single(lines);
            return lines[0].Text;
        }

        /// <summary>
        /// The engine returned success but produced no text: the caller appends the empty
        /// buffer, so this is a blank tooltip row, not an absent line.
        /// </summary>
        private static void AssertBlank(
            ItemDescriptionGenerator generator, params KeyValuePair<int, int>[] entries)
        {
            IReadOnlyList<ItemDescriptionLine> lines = generator.Describe(entries);
            Assert.Single(lines);
            Assert.True(lines[0].IsBlank);
        }

        private static IReadOnlyList<ItemDescriptionLine> All(
            ItemDescriptionGenerator generator, params KeyValuePair<int, int>[] entries)
        {
            return generator.Describe(entries);
        }

        // =================================================================
        // Guard clauses
        // =================================================================

        [Fact]
        public void Ctor_rejects_a_null_stat_table()
        {
            Assert.Throws<ArgumentNullException>(
                () => new ItemDescriptionGenerator(null, new FakeStringTable()));
        }

        [Fact]
        public void Ctor_rejects_a_null_string_table()
        {
            Assert.Throws<ArgumentNullException>(
                () => new ItemDescriptionGenerator(new FakeStatCostTable(), null));
        }

        [Fact]
        public void Describe_rejects_null_stats()
        {
            Assert.Throws<ArgumentNullException>(
                () => Gen(new FakeStatCostTable(), new FakeStringTable()).Describe(null));
        }

        [Fact]
        public void Join_rejects_null_lines()
        {
            Assert.Throws<ArgumentNullException>(
                () => Gen(new FakeStatCostTable(), new FakeStringTable()).Join(null));
        }

        // =================================================================
        // Selection, suppression and ordering
        // =================================================================

        [Fact]
        public void Stats_print_in_the_order_the_table_hands_them_back()
        {
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(1, ItemDescFunc.PlusValueString, 100, priority: 10));
            stats.Add(Build.Stat(2, ItemDescFunc.PlusValueString, 101, priority: 90));

            var strings = new FakeStringTable().WithPunctuation()
                .Add(100, "to Strength").Add(101, "to Energy");

            IReadOnlyList<ItemDescriptionLine> lines = All(Gen(stats, strings),
                Build.Entry(2, 5), Build.Entry(1, 10));

            Assert.Equal(new[] { "+10 to Strength", "+5 to Energy" }, lines.Select(l => l.Text).ToArray());
            Assert.Equal(10, lines[0].DescPriority);
        }

        [Fact]
        public void A_zero_valued_stat_is_skipped()
        {
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(1, ItemDescFunc.PlusValueString, 100));

            Assert.Empty(All(Gen(stats, new FakeStringTable().WithPunctuation().Add(100, "to Strength")),
                Build.Entry(1, 0)));
        }

        [Fact]
        public void A_stat_absent_from_the_item_is_skipped()
        {
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(1, ItemDescFunc.PlusValueString, 100));
            stats.Add(Build.Stat(2, ItemDescFunc.PlusValueString, 101));

            var strings = new FakeStringTable().WithPunctuation()
                .Add(100, "to Strength").Add(101, "to Energy");

            Assert.Equal("+10 to Strength", One(Gen(stats, strings), Build.Entry(1, 10)));
        }

        [Fact]
        public void A_stat_with_no_table_row_is_skipped()
        {
            var stats = new FakeStatCostTable();
            stats.AddMissing(7);

            Assert.Empty(All(Gen(stats, new FakeStringTable()), Build.Entry(7, 10)));
        }

        [Fact]
        public void A_stat_with_desc_func_zero_is_skipped()
        {
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(72, 0, 100)); // durability: the tooltip prints it elsewhere

            Assert.Empty(All(Gen(stats, new FakeStringTable().Add(100, "Durability")),
                Build.Entry(72, 40)));
        }

        [Fact]
        public void Secondary_min_damage_is_suppressed_when_min_damage_is_present()
        {
            // SKILLDESC_BuildStatBuffDesc 0x4e62d2.
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(21, ItemDescFunc.PlusValueString, 100));
            stats.Add(Build.Stat(23, ItemDescFunc.PlusValueString, 101));

            var strings = new FakeStringTable().WithPunctuation()
                .Add(100, "Min Damage").Add(101, "Secondary Min Damage");

            var values = new FakeStatValues().AddBase(21, 5);

            IReadOnlyList<ItemDescriptionLine> lines = All(Gen(stats, strings, values),
                Build.Entry(21, 5), Build.Entry(23, 7));

            Assert.Equal(new[] { "+5 Min Damage" }, lines.Select(l => l.Text).ToArray());
        }

        [Fact]
        public void Secondary_max_damage_is_suppressed_when_max_damage_is_present()
        {
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(22, ItemDescFunc.PlusValueString, 100));
            stats.Add(Build.Stat(24, ItemDescFunc.PlusValueString, 101));

            var strings = new FakeStringTable().WithPunctuation()
                .Add(100, "Max Damage").Add(101, "Secondary Max Damage");

            var values = new FakeStatValues().AddBase(22, 5);

            Assert.Single(All(Gen(stats, strings, values), Build.Entry(22, 5), Build.Entry(24, 7)));
        }

        [Fact]
        public void Secondary_damage_prints_when_the_primary_is_absent()
        {
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(23, ItemDescFunc.PlusValueString, 101));

            var strings = new FakeStringTable().WithPunctuation().Add(101, "Secondary Min Damage");

            Assert.Equal("+7 Secondary Min Damage", One(Gen(stats, strings), Build.Entry(23, 7)));
        }

        [Fact]
        public void A_stat_present_at_several_layers_prints_once_per_layer_in_layer_order()
        {
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(107, ItemDescFunc.Skill, 0));

            var strings = new FakeStringTable().WithPunctuation();
            var skills = new FakeSkillTable().Add(1, "Fire Bolt").Add(2, "Teleport");

            IReadOnlyList<ItemDescriptionLine> lines = All(Gen(stats, strings, null, skills),
                Build.Entry(107, 3, layer: 2), Build.Entry(107, 1, layer: 1));

            Assert.Equal(new[] { "+1 to Fire Bolt", "+3 to Teleport" }, lines.Select(l => l.Text).ToArray());
        }

        [Fact]
        public void An_empty_stat_set_yields_no_lines()
        {
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(1, ItemDescFunc.PlusValueString, 100));

            Assert.Empty(Gen(stats, new FakeStringTable()).Describe(new KeyValuePair<int, int>[0]));
        }

        [Fact]
        public void Join_separates_lines_the_way_the_game_does()
        {
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(1, ItemDescFunc.PlusValueString, 100));
            stats.Add(Build.Stat(2, ItemDescFunc.PlusValueString, 101));

            var strings = new FakeStringTable().WithPunctuation()
                .Add(100, "to Strength").Add(101, "to Energy");

            ItemDescriptionGenerator generator = Gen(stats, strings);
            IReadOnlyList<ItemDescriptionLine> lines = All(generator,
                Build.Entry(1, 10), Build.Entry(2, 5));

            // Inline mode is the default and what the item tooltip uses: 3998 after every
            // line, no separator.
            Assert.Equal("+10 to Strength\n+5 to Energy\n", generator.Join(lines));

            // Block mode is the other shape, for callers that pass arg_14 == 0: 3852 + 3995
            // before each line after the first, nothing terminating the last.
            Assert.Equal("+10 to Strength\n +5 to Energy",
                generator.Join(lines, inlineMode: false));
        }

        [Fact]
        public void Join_of_nothing_is_empty()
        {
            Assert.Equal(string.Empty,
                Gen(new FakeStatCostTable(), new FakeStringTable().WithPunctuation())
                    .Join(new ItemDescriptionLine[0]));
        }

        // =================================================================
        // DescVal placement
        // =================================================================

        [Theory]
        [InlineData(1, "+10 to Strength")]
        [InlineData(2, "to Strength +10")]
        public void DescVal_decides_where_the_number_goes(int descVal, string expected)
        {
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(1, ItemDescFunc.PlusValueString, 100, descVal: descVal));

            Assert.Equal(expected,
                One(Gen(stats, new FakeStringTable().WithPunctuation().Add(100, "to Strength")),
                    Build.Entry(1, 10)));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(7)]
        public void DescFunc_1_with_an_unusual_desc_val_yields_a_blank_row(int descVal)
        {
            // 0x4e4f5d: eight arms leave the buffer empty rather than copying the string.
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(1, ItemDescFunc.PlusValueString, 100, descVal: descVal));

            AssertBlank(Gen(stats, new FakeStringTable().WithPunctuation().Add(100, "to Strength")),
                Build.Entry(1, 10));
        }

        // =================================================================
        // Strings, signs and value computation
        // =================================================================

        [Fact]
        public void A_negative_value_uses_the_negative_string()
        {
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(1, ItemDescFunc.ValueString, 100, strNeg: 101));

            var strings = new FakeStringTable().WithPunctuation()
                .Add(100, "Faster Cast Rate").Add(101, "Slower Cast Rate");

            ItemDescriptionGenerator generator = Gen(stats, strings);
            Assert.Equal("-10 Slower Cast Rate", One(generator, Build.Entry(1, -10)));
            Assert.Equal("10 Faster Cast Rate", One(generator, Build.Entry(1, 10)));
        }

        [Fact]
        public void A_negative_value_does_not_fall_back_to_the_positive_string()
        {
            // 0x4e4e43 selects DescStrNeg unconditionally. A blank DescStrNeg means a blank
            // text part, not a reuse of DescStrPos.
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(1, ItemDescFunc.ValueString, 100));

            Assert.Equal("-10 ",
                One(Gen(stats, new FakeStringTable().WithPunctuation().Add(100, "to Strength")),
                    Build.Entry(1, -10)));
        }

        [Fact]
        public void The_plus_sign_appears_only_for_a_strictly_positive_value()
        {
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(1, ItemDescFunc.PlusValueString, 100));

            ItemDescriptionGenerator generator =
                Gen(stats, new FakeStringTable().WithPunctuation().Add(100, "to Strength"));

            Assert.Equal("+10 to Strength", One(generator, Build.Entry(1, 10)));
            Assert.Equal("-10 ", One(generator, Build.Entry(1, -10)));
        }

        [Fact]
        public void A_stat_with_no_string_index_still_prints_its_number()
        {
            // Str(0) resolves to an empty entry, not a missing one, and the engine emits the
            // number and separator regardless.
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(1, ItemDescFunc.PlusValueString, 0));

            Assert.Equal("+10 ",
                One(Gen(stats, new FakeStringTable().WithPunctuation()), Build.Entry(1, 10)));
        }

        [Fact]
        public void A_string_index_the_table_does_not_have_still_prints_its_number()
        {
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(1, ItemDescFunc.PlusValueString, 999));

            Assert.Equal("+10 ",
                One(Gen(stats, new FakeStringTable().WithPunctuation()), Build.Entry(1, 10)));
        }

        [Fact]
        public void ValShift_scales_the_displayed_value()
        {
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(7, ItemDescFunc.PlusValueString, 100, valShift: 8));

            IReadOnlyList<ItemDescriptionLine> lines = All(
                Gen(stats, new FakeStringTable().WithPunctuation().Add(100, "to Life")),
                Build.Entry(7, 40 << 8));

            Assert.Equal("+40 to Life", lines[0].Text);
            Assert.Equal(40, lines[0].Value);
        }

        [Theory]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        public void Ops_2_to_5_scale_the_value_against_a_player_stat(int op)
        {
            // SKILLDESC_CalcStatGroupValue 0x4e4cad.
            var stats = new FakeStatCostTable();
            StatDescriptor perLevel = Build.Stat(1, ItemDescFunc.PlusValueString, 100);
            perLevel.Op = op;
            perLevel.OpParam = 3;   // >> 3
            perLevel.OpBase = 12;   // character level
            stats.Add(perLevel);
            stats.Add(Build.Stat(12, 0, 0)); // the op base row, ValShift 0

            var values = new FakeStatValues().AddPlayer(12, 40);
            var strings = new FakeStringTable().WithPunctuation().Add(100, "to Life");

            // (2 * 40) >> 3 = 10
            Assert.Equal("+10 to Life",
                One(Gen(stats, strings, values), Build.Entry(1, 2)));
        }

        [Fact]
        public void Op_scaling_honours_the_op_base_val_shift()
        {
            var stats = new FakeStatCostTable();
            StatDescriptor perLevel = Build.Stat(1, ItemDescFunc.PlusValueString, 100);
            perLevel.Op = 2;
            perLevel.OpParam = 0;
            perLevel.OpBase = 12;
            stats.Add(perLevel);
            stats.Add(Build.Stat(12, 0, 0, valShift: 2)); // player stat is in quarters

            var values = new FakeStatValues().AddPlayer(12, 40);

            // 2 * (40 >> 2) = 20
            Assert.Equal("+20 to Life",
                One(Gen(stats, new FakeStringTable().WithPunctuation().Add(100, "to Life"), values),
                    Build.Entry(1, 2)));
        }

        [Fact]
        public void Op_scaling_yields_zero_when_there_is_no_value_source()
        {
            var stats = new FakeStatCostTable();
            StatDescriptor perLevel = Build.Stat(1, ItemDescFunc.PlusValueString, 100);
            perLevel.Op = 2;
            perLevel.OpBase = 12;
            stats.Add(perLevel);
            stats.Add(Build.Stat(12, 0, 0));

            // GetStatUnsignedValue returns 0 for a null unit, so the multiply still happens
            // and yields 0. DescFunc 1 uses the strict sign test, so zero gets no plus.
            Assert.Equal("0 to Life",
                One(Gen(stats, new FakeStringTable().WithPunctuation().Add(100, "to Life")),
                    Build.Entry(1, 2)));
        }

        [Fact]
        public void Op_scaling_yields_zero_when_the_op_base_row_is_missing()
        {
            var stats = new FakeStatCostTable();
            StatDescriptor perLevel = Build.Stat(1, ItemDescFunc.PlusValueString, 100);
            perLevel.Op = 2;
            perLevel.OpBase = 999;
            stats.Add(perLevel);

            var values = new FakeStatValues().AddPlayer(999, 40);

            // 0x4e4c88 returns 0 outright for an out-of-range op base.
            Assert.Equal("0 to Life",
                One(Gen(stats, new FakeStringTable().WithPunctuation().Add(100, "to Life"), values),
                    Build.Entry(1, 2)));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(6)]
        [InlineData(13)]
        public void Ops_outside_2_to_5_do_not_scale(int op)
        {
            var stats = new FakeStatCostTable();
            StatDescriptor descriptor = Build.Stat(1, ItemDescFunc.PlusValueString, 100);
            descriptor.Op = op;
            descriptor.OpParam = 3;
            descriptor.OpBase = 12;
            stats.Add(descriptor);
            stats.Add(Build.Stat(12, 0, 0));

            var values = new FakeStatValues().AddPlayer(12, 40);

            Assert.Equal("+2 to Life",
                One(Gen(stats, new FakeStringTable().WithPunctuation().Add(100, "to Life"), values),
                    Build.Entry(1, 2)));
        }

        [Fact]
        public void Missing_punctuation_strings_degrade_to_unseparated_text()
        {
            // The sign and separator come from the .tbl, so an incomplete table runs the
            // pieces together rather than throwing. A real MPQ always has 3995/4001/4002.
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(1, ItemDescFunc.PlusValueString, 100));

            Assert.Equal("10to Strength",
                One(Gen(stats, new FakeStringTable().Add(100, "to Strength")), Build.Entry(1, 10)));
        }

        // =================================================================
        // Every DescFunc
        // =================================================================

        [Theory]
        [InlineData(ItemDescFunc.PlusValueString, 10, "+10 String")]
        [InlineData(ItemDescFunc.PlusValueString, -10, "-10 ")]
        [InlineData(ItemDescFunc.ValuePercentString, 10, "10% String")]
        [InlineData(ItemDescFunc.ValueString, 10, "10 String")]
        [InlineData(ItemDescFunc.PlusValuePercentString, 10, "+10% String")]
        [InlineData(ItemDescFunc.PlusValuePercentString, -10, "-10% ")]
        [InlineData(ItemDescFunc.ValueFramesPercentString, 128, "100% String")]
        [InlineData(ItemDescFunc.ValueFramesPercentString, 64, "50% String")]
        [InlineData(ItemDescFunc.StaleNegated25, 10, "10 String")]
        [InlineData(ItemDescFunc.StaleNegated26, 10, "10 String")]
        public void Simple_desc_funcs(int func, int value, string expected)
        {
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(1, func, 100));

            Assert.Equal(expected,
                One(Gen(stats, new FakeStringTable().WithPunctuation().Add(100, "String")),
                    Build.Entry(1, value)));
        }

        [Theory]
        [InlineData(ItemDescFunc.NegatedValuePercentString, -25, "+25% ")]
        [InlineData(ItemDescFunc.NegatedValuePercentString, 25, "-25% String")]
        public void DescFunc_20_negates_and_keeps_the_percent(int func, int value, string expected)
        {
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(1, func, 100));

            Assert.Equal(expected,
                One(Gen(stats, new FakeStringTable().WithPunctuation().Add(100, "String")),
                    Build.Entry(1, value)));
        }

        [Fact]
        public void DescFunc_21_also_emits_a_percent_despite_what_the_community_table_says()
        {
            // 20 and 21 both fall into the 4/8 path at 0x4e5031.
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(1, ItemDescFunc.NegatedValuePercentStringString2, 100));

            // The string is selected from the ORIGINAL negative value, so DescStrNeg (blank
            // here) applies; and 0x4e5948 emits the DescStr2 separator with no zero check.
            Assert.Equal("+25%  ",
                One(Gen(stats, new FakeStringTable().WithPunctuation().Add(100, "String")),
                    Build.Entry(1, -25)));
        }

        [Fact]
        public void DescFunc_12_prints_the_string_alone_when_the_value_is_one()
        {
            // 0x4e4f05: DescFunc 12 with a value of exactly 1 omits the number.
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(1, ItemDescFunc.PlusValueStringSuppressOne, 100));

            ItemDescriptionGenerator generator =
                Gen(stats, new FakeStringTable().WithPunctuation().Add(100, "Indestructible"));

            Assert.Equal(" Indestructible", One(generator, Build.Entry(1, 1)));
            Assert.Equal("+2 Indestructible", One(generator, Build.Entry(1, 2)));
        }

        [Theory]
        [InlineData(ItemDescFunc.PlusValueStringString2, 10, "+10 String Second")]
        [InlineData(ItemDescFunc.ValuePercentStringString2, 10, "10% String Second")]
        [InlineData(ItemDescFunc.PlusValuePercentStringString2, 10, "+10% String Second")]
        [InlineData(ItemDescFunc.ValueStringString2, 10, "10 String Second")]
        [InlineData(ItemDescFunc.ValueFramesPercentStringString2, 128, "100% String Second")]
        [InlineData(ItemDescFunc.NegatedValuePercentStringString2, -10, "+10%  Second")]
        public void The_desc_funcs_that_take_a_second_string(int func, int value, string expected)
        {
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(1, func, 100, str2: 101));

            var strings = new FakeStringTable().WithPunctuation().Add(100, "String").Add(101, "Second");

            Assert.Equal(expected, One(Gen(stats, strings), Build.Entry(1, value)));
        }

        [Theory]
        [InlineData(ItemDescFunc.PlusValueString)]
        [InlineData(ItemDescFunc.ValuePercentString)]
        [InlineData(ItemDescFunc.RawFormat)]
        public void A_desc_func_outside_6_to_10_and_21_ignores_its_second_string(int func)
        {
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(1, func, 100, str2: 101));

            var strings = new FakeStringTable().WithPunctuation().Add(100, "String").Add(101, "Second");

            Assert.DoesNotContain("Second", One(Gen(stats, strings), Build.Entry(1, 10)));
        }

        [Fact]
        public void A_second_string_of_5382_is_replaced_by_string_11091()
        {
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(1, ItemDescFunc.ValueStringString2, 100,
                str2: DescStringIds.DescStr2Sentinel));

            var strings = new FakeStringTable().WithPunctuation()
                .Add(100, "String").Add(DescStringIds.DescStr2Override, "Replacement");

            Assert.Equal("10 String Replacement", One(Gen(stats, strings), Build.Entry(1, 10)));
        }

        [Fact]
        public void A_second_string_index_of_zero_is_omitted()
        {
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(1, ItemDescFunc.ValueStringString2, 100, str2: 0));

            Assert.Equal("10 String ",
                One(Gen(stats, new FakeStringTable().WithPunctuation().Add(100, "String")),
                    Build.Entry(1, 10)));
        }

        [Fact]
        public void A_second_string_the_table_does_not_have_is_omitted()
        {
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(1, ItemDescFunc.ValueStringString2, 100, str2: 999));

            Assert.Equal("10 String ",
                One(Gen(stats, new FakeStringTable().WithPunctuation().Add(100, "String")),
                    Build.Entry(1, 10)));
        }

        [Theory]
        [InlineData(-5)]    // value <= 0
        [InlineData(4)]     // 2500/4 = 625 > 30 -> per second string
        [InlineData(100)]   // 2500/100 = 25, not > 30
        public void DescFunc_11_always_produces_a_repair_line(int value)
        {
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(252, ItemDescFunc.RepairDurability, 100));

            var strings = new FakeStringTable().WithPunctuation()
                .Add(DescStringIds.RepairSingleCount, "Repairs %d Durability in 25 Seconds")
                .Add(DescStringIds.RepairCountAndSeconds, "Repairs %d Durability per Second");

            Assert.StartsWith("Repairs ", One(Gen(stats, strings), Build.Entry(252, value)));
        }

        [Fact]
        public void DescFunc_11_uses_25_for_a_non_positive_rate()
        {
            // A stored zero never reaches the formatter, so the non-positive arm is only
            // reachable via a negative value or one that shifts down to zero.
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(252, ItemDescFunc.RepairDurability, 100, valShift: 4));

            var strings = new FakeStringTable().WithPunctuation()
                .Add(DescStringIds.RepairSingleCount, "Repairs %d Durability");

            Assert.Equal("Repairs 25 Durability", One(Gen(stats, strings), Build.Entry(252, 1)));
        }

        [Fact]
        public void DescFunc_11_switches_string_when_the_rate_exceeds_thirty_seconds()
        {
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(252, ItemDescFunc.RepairDurability, 100));

            var strings = new FakeStringTable().WithPunctuation()
                .Add(DescStringIds.RepairSingleCount, "SLOW %d")
                .Add(DescStringIds.RepairCountAndSeconds, "FAST %d");

            // 2500/4 = 625 > 30
            Assert.Equal("FAST 1", One(Gen(stats, strings), Build.Entry(252, 4)));
            // 2500/100 = 25, not > 30
            Assert.Equal("SLOW 1", One(Gen(stats, strings), Build.Entry(252, 100)));
        }

        [Fact]
        public void DescFunc_13_reads_charstats_and_ignores_desc_str_pos()
        {
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(83, ItemDescFunc.ClassAllSkills, 100));

            var strings = new FakeStringTable().WithPunctuation().Add(100, "IGNORED");
            var classes = new FakeClassTable().AddAllSkills(3, "to Paladin Skill Levels");

            Assert.Equal("+2 to Paladin Skill Levels",
                One(Gen(stats, strings, null, null, classes), Build.Entry(83, 2, layer: 3)));
        }

        [Fact]
        public void DescFunc_13_honours_desc_val_2()
        {
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(83, ItemDescFunc.ClassAllSkills, 100, descVal: 2));

            var classes = new FakeClassTable().AddAllSkills(3, "Paladin Skill Levels");

            Assert.Equal("Paladin Skill Levels +2",
                One(Gen(stats, new FakeStringTable().WithPunctuation(), null, null, classes),
                    Build.Entry(83, 2, layer: 3)));
        }

        [Fact]
        public void DescFunc_13_drops_a_zero_value()
        {
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(83, ItemDescFunc.ClassAllSkills, 100));

            var classes = new FakeClassTable().AddAllSkills(3, "to Paladin Skill Levels");

            // A zero entry never reaches the formatter, so drive it through a shift instead.
            StatDescriptor shifted = Build.Stat(84, ItemDescFunc.ClassAllSkills, 100, valShift: 8);
            stats.Add(shifted);

            Assert.Empty(All(Gen(stats, new FakeStringTable().WithPunctuation(), null, null, classes),
                Build.Entry(84, 1, layer: 3)));
        }

        [Fact]
        public void DescFunc_13_drops_the_line_with_no_class_table()
        {
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(83, ItemDescFunc.ClassAllSkills, 100));

            Assert.Empty(All(Gen(stats, new FakeStringTable().WithPunctuation()),
                Build.Entry(83, 2, layer: 3)));
        }

        [Fact]
        public void DescFunc_13_drops_the_line_for_an_unknown_class()
        {
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(83, ItemDescFunc.ClassAllSkills, 100));

            var classes = new FakeClassTable().AddAllSkills(3, "to Paladin Skill Levels");

            Assert.Empty(All(Gen(stats, new FakeStringTable().WithPunctuation(), null, null, classes),
                Build.Entry(83, 2, layer: 5)));
        }

        [Fact]
        public void DescFunc_14_unpacks_the_class_from_the_layer_not_a_flat_tab_id()
        {
            // 0x4e5280: tab = layer & 7, class = layer >> 3.
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(188, ItemDescFunc.SkillTab, 100));

            var classes = new FakeClassTable()
                .AddTab(3, 1, "+%d to Combat Skills")
                .AddClassOnly(3, "(Paladin Only)");

            int layer = (3 << 3) | 1;

            Assert.Equal("+2 to Combat Skills (Paladin Only)",
                One(Gen(stats, new FakeStringTable().WithPunctuation(), null, null, classes),
                    Build.Entry(188, 2, layer)));
        }

        [Fact]
        public void DescFunc_14_rejects_a_tab_index_above_two()
        {
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(188, ItemDescFunc.SkillTab, 100));

            var classes = new FakeClassTable().AddTab(3, 3, "+%d to Nothing");

            Assert.Empty(All(Gen(stats, new FakeStringTable().WithPunctuation(), null, null, classes),
                Build.Entry(188, 2, (3 << 3) | 3)));
        }

        [Fact]
        public void DescFunc_14_drops_the_line_with_no_class_table()
        {
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(188, ItemDescFunc.SkillTab, 100));

            Assert.Empty(All(Gen(stats, new FakeStringTable().WithPunctuation()),
                Build.Entry(188, 2, (3 << 3) | 1)));
        }

        [Fact]
        public void DescFunc_14_still_prints_when_the_tab_text_is_missing()
        {
            // 0x4e528d tests the charstats ROW and the tab index only; the tab text itself is
            // never pointer-checked.
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(188, ItemDescFunc.SkillTab, 100));

            var classes = new FakeClassTable().AddClassOnly(3, "(Paladin Only)");

            Assert.Equal(" (Paladin Only)",
                One(Gen(stats, new FakeStringTable().WithPunctuation(), null, null, classes),
                    Build.Entry(188, 2, (3 << 3) | 1)));
        }

        [Fact]
        public void DescFunc_14_omits_a_missing_class_only_suffix()
        {
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(188, ItemDescFunc.SkillTab, 100));

            var classes = new FakeClassTable().AddTab(3, 1, "+%d to Combat Skills");

            Assert.Equal("+2 to Combat Skills ",
                One(Gen(stats, new FakeStringTable().WithPunctuation(), null, null, classes),
                    Build.Entry(188, 2, (3 << 3) | 1)));
        }

        [Fact]
        public void DescFunc_15_unpacks_the_skill_and_level_from_the_layer()
        {
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(198, ItemDescFunc.SkillOnEvent, 100));

            var strings = new FakeStringTable().WithPunctuation()
                .Add(100, "%d%% Chance to cast level %d %s on striking");
            var skills = new FakeSkillTable().Add(56, "Frost Nova");

            Assert.Equal("5% Chance to cast level 3 Frost Nova on striking",
                One(Gen(stats, strings, null, skills), Build.Entry(198, 5, (56 << 6) | 3)));
        }

        [Fact]
        public void DescFunc_15_honours_a_non_default_skill_id_shift()
        {
            var stats = new FakeStatCostTable();
            stats.SkillIdShift = 8;
            stats.Add(Build.Stat(198, ItemDescFunc.SkillOnEvent, 100));

            var strings = new FakeStringTable().WithPunctuation().Add(100, "%d%% cast level %d %s");
            var skills = new FakeSkillTable().Add(56, "Frost Nova");

            Assert.Equal("5% cast level 3 Frost Nova",
                One(Gen(stats, strings, null, skills), Build.Entry(198, 5, (56 << 8) | 3)));
        }

        [Theory]
        [InlineData(0)]     // skill id 0 is rejected
        [InlineData(500)]   // beyond RowCount
        public void DescFunc_15_rejects_an_out_of_range_skill_id(int skillId)
        {
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(198, ItemDescFunc.SkillOnEvent, 100));

            var strings = new FakeStringTable().WithPunctuation().Add(100, "%d %d %s");
            var skills = new FakeSkillTable();
            skills.RowCount = 400;

            Assert.Empty(All(Gen(stats, strings, null, skills),
                Build.Entry(198, 5, (skillId << 6) | 3)));
        }

        [Fact]
        public void DescFunc_15_drops_the_line_with_no_skill_table()
        {
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(198, ItemDescFunc.SkillOnEvent, 100));

            Assert.Empty(All(Gen(stats, new FakeStringTable().WithPunctuation().Add(100, "%d %d %s")),
                Build.Entry(198, 5, (56 << 6) | 3)));
        }

        [Fact]
        public void DescFunc_16_treats_the_layer_as_a_bare_skill_id()
        {
            // 0x4e533e: unlike 15 and 24, there is no shift here.
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(148, ItemDescFunc.SkillAura, 100));

            var strings = new FakeStringTable().WithPunctuation()
                .Add(100, "Level %d %s Aura When Equipped");
            var skills = new FakeSkillTable().Add(120, "Holy Freeze");

            Assert.Equal("Level 3 Holy Freeze Aura When Equipped",
                One(Gen(stats, strings, null, skills), Build.Entry(148, 3, layer: 120)));
        }

        [Fact]
        public void DescFunc_16_drops_the_line_for_an_unknown_skill()
        {
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(148, ItemDescFunc.SkillAura, 100));

            var strings = new FakeStringTable().WithPunctuation().Add(100, "Level %d %s");

            Assert.Empty(All(Gen(stats, strings, null, new FakeSkillTable()),
                Build.Entry(148, 3, layer: 120)));
        }

        [Fact]
        public void DescFunc_16_drops_the_line_with_no_skill_table()
        {
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(148, ItemDescFunc.SkillAura, 100));

            Assert.Empty(All(Gen(stats, new FakeStringTable().WithPunctuation().Add(100, "Level %d %s")),
                Build.Entry(148, 3, layer: 120)));
        }

        [Fact]
        public void DescFunc_19_formats_the_string_with_the_value()
        {
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(1, ItemDescFunc.RawFormat, 100));

            var strings = new FakeStringTable().WithPunctuation()
                .Add(100, "Adds %d poison damage over 3 seconds");

            Assert.Equal("Adds 7 poison damage over 3 seconds",
                One(Gen(stats, strings), Build.Entry(1, 7)));
        }

        [Fact]
        public void DescFunc_22_appends_the_monster_type()
        {
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(179, ItemDescFunc.MonsterTypeDamage, 100));

            var strings = new FakeStringTable().WithPunctuation().Add(100, "Damage");
            var monsters = new FakeMonsterTable().AddType(4, "Undead");

            Assert.Equal("+50% Damage to Undead",
                One(Gen(stats, strings, null, null, null, monsters), Build.Entry(179, 50, layer: 4)));
        }

        [Fact]
        public void DescFunc_22_still_prints_when_the_monster_type_is_unknown()
        {
            // GetMonTypeLine returning 0 skips the suffix but keeps the line (0x4e5578).
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(179, ItemDescFunc.MonsterTypeDamage, 100));

            var strings = new FakeStringTable().WithPunctuation().Add(100, "Damage");

            Assert.Equal("+50% Damage",
                One(Gen(stats, strings, null, null, null, new FakeMonsterTable()),
                    Build.Entry(179, 50, layer: 4)));
        }

        [Fact]
        public void DescFunc_22_still_prints_when_its_string_is_missing()
        {
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(179, ItemDescFunc.MonsterTypeDamage, 0));

            var monsters = new FakeMonsterTable().AddType(4, "Undead");

            Assert.Equal("+50%  to Undead",
                One(Gen(stats, new FakeStringTable().WithPunctuation(), null, null, null, monsters),
                    Build.Entry(179, 50, layer: 4)));
        }

        [Fact]
        public void DescFunc_23_names_a_single_monster()
        {
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(341, ItemDescFunc.MonsterDamage, 100));

            var strings = new FakeStringTable().WithPunctuation().Add(100, "Damage to");
            var monsters = new FakeMonsterTable().AddMonster(9, "Fallen");

            Assert.Equal("50% Damage to Fallen",
                One(Gen(stats, strings, null, null, null, monsters), Build.Entry(341, 50, layer: 9)));
        }

        [Fact]
        public void DescFunc_23_drops_the_line_for_an_unknown_monster()
        {
            // TXT_MonStats_GetLine returning 0 drops the whole line (0x4e55c0).
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(341, ItemDescFunc.MonsterDamage, 100));

            var strings = new FakeStringTable().WithPunctuation().Add(100, "Damage to");

            Assert.Empty(All(Gen(stats, strings, null, null, null, new FakeMonsterTable()),
                Build.Entry(341, 50, layer: 9)));
        }

        [Fact]
        public void DescFunc_23_drops_the_line_with_no_monster_table()
        {
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(341, ItemDescFunc.MonsterDamage, 100));

            Assert.Empty(All(Gen(stats, new FakeStringTable().WithPunctuation().Add(100, "Damage to")),
                Build.Entry(341, 50, layer: 9)));
        }

        [Fact]
        public void DescFunc_23_still_prints_when_its_string_is_missing()
        {
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(341, ItemDescFunc.MonsterDamage, 0));

            var monsters = new FakeMonsterTable().AddMonster(9, "Fallen");

            Assert.Equal("50%  Fallen",
                One(Gen(stats, new FakeStringTable().WithPunctuation(), null, null, null, monsters),
                    Build.Entry(341, 50, layer: 9)));
        }

        [Fact]
        public void DescFunc_24_builds_the_charge_line_from_locale_strings()
        {
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(204, ItemDescFunc.Charges, 100));

            var strings = new FakeStringTable().WithPunctuation()
                .Add(DescStringIds.Level, "Level")
                .Add(100, "(%d/%d Charges)");
            var skills = new FakeSkillTable().Add(54, "Teleport");

            int value = (20 << 8) | 13;

            Assert.Equal("Level 3 Teleport (13/20 Charges)",
                One(Gen(stats, strings, null, skills), Build.Entry(204, value, (54 << 6) | 3)));
        }

        [Fact]
        public void DescFunc_24_drops_the_line_for_an_unknown_skill()
        {
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(204, ItemDescFunc.Charges, 100));

            var strings = new FakeStringTable().WithPunctuation()
                .Add(DescStringIds.Level, "Level").Add(100, "(%d/%d Charges)");

            AssertBlank(Gen(stats, strings, null, new FakeSkillTable()),
                Build.Entry(204, (20 << 8) | 13, (54 << 6) | 3));
        }

        [Fact]
        public void DescFunc_24_drops_the_line_with_no_skill_table()
        {
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(204, ItemDescFunc.Charges, 100));

            var strings = new FakeStringTable().WithPunctuation().Add(100, "(%d/%d Charges)");

            AssertBlank(Gen(stats, strings), Build.Entry(204, (20 << 8) | 13, (54 << 6) | 3));
        }

        [Fact]
        public void DescFunc_27_composes_the_class_only_skill_line()
        {
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(107, ItemDescFunc.SkillClassOnly, 100));

            var strings = new FakeStringTable().WithPunctuation();
            var skills = new FakeSkillTable().Add(54, "Teleport", classId: 1);
            var classes = new FakeClassTable().AddClassOnly(1, "(Sorceress Only)");

            Assert.Equal("+2 to Teleport (Sorceress Only)",
                One(Gen(stats, strings, null, skills, classes), Build.Entry(107, 2, layer: 54)));
        }

        [Fact]
        public void DescFunc_27_keeps_a_partial_line_for_a_class_less_skill()
        {
            // SKILLS_GetCharClassFromSkillId_Validated failing drops the line (0x4e580b).
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(107, ItemDescFunc.SkillClassOnly, 100));

            var skills = new FakeSkillTable().Add(54, "Teleport");
            var classes = new FakeClassTable().AddClassOnly(1, "(Sorceress Only)");

            Assert.Equal("+2 to Teleport ",
                One(Gen(stats, new FakeStringTable().WithPunctuation(), null, skills, classes),
                    Build.Entry(107, 2, layer: 54)));
        }

        [Fact]
        public void DescFunc_27_keeps_a_partial_line_when_the_class_id_is_above_six()
        {
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(107, ItemDescFunc.SkillClassOnly, 100));

            var skills = new FakeSkillTable().Add(54, "Teleport", classId: 7);
            var classes = new FakeClassTable().AddClassOnly(7, "(Nobody Only)");

            // 0x4e5812: the line survives with its trailing separator.
            Assert.Equal("+2 to Teleport ",
                One(Gen(stats, new FakeStringTable().WithPunctuation(), null, skills, classes),
                    Build.Entry(107, 2, layer: 54)));
        }

        [Fact]
        public void DescFunc_27_omits_a_missing_class_only_suffix()
        {
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(107, ItemDescFunc.SkillClassOnly, 100));

            var skills = new FakeSkillTable().Add(54, "Teleport", classId: 1);

            Assert.Equal("+2 to Teleport ",
                One(Gen(stats, new FakeStringTable().WithPunctuation(), null, skills, new FakeClassTable()),
                    Build.Entry(107, 2, layer: 54)));
        }

        [Fact]
        public void DescFunc_27_drops_an_unknown_skill()
        {
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(107, ItemDescFunc.SkillClassOnly, 100));

            AssertBlank(Gen(stats, new FakeStringTable().WithPunctuation(), null,
                    new FakeSkillTable(), new FakeClassTable()),
                Build.Entry(107, 2, layer: 54));
        }

        [Fact]
        public void DescFunc_27_drops_the_line_without_a_skill_or_class_table()
        {
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(107, ItemDescFunc.SkillClassOnly, 100));

            AssertBlank(Gen(stats, new FakeStringTable().WithPunctuation()),
                Build.Entry(107, 2, layer: 54));
        }

        [Fact]
        public void DescFunc_27_drops_the_line_when_the_to_string_is_missing()
        {
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(107, ItemDescFunc.SkillClassOnly, 100));

            var strings = new FakeStringTable().Add(DescStringIds.Space, " ").Add(DescStringIds.Plus, "+");
            var skills = new FakeSkillTable().Add(54, "Teleport", classId: 1);
            var classes = new FakeClassTable().AddClassOnly(1, "(Sorceress Only)");

            AssertBlank(Gen(stats, strings, null, skills, classes), Build.Entry(107, 2, layer: 54));
        }

        [Fact]
        public void DescFunc_28_names_the_skill()
        {
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(97, ItemDescFunc.Skill, 100));

            var skills = new FakeSkillTable().Add(54, "Teleport");

            Assert.Equal("+2 to Teleport",
                One(Gen(stats, new FakeStringTable().WithPunctuation(), null, skills),
                    Build.Entry(97, 2, layer: 54)));
        }

        [Fact]
        public void DescFunc_28_clamps_to_three_for_the_viewers_own_class()
        {
            // 0x4e5889: the famous +skills cap on class-specific items.
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(97, ItemDescFunc.Skill, 100));

            var skills = new FakeSkillTable().Add(54, "Teleport", classId: 1);
            var values = new FakeStatValues();
            values.PlayerClass = 1;

            IReadOnlyList<ItemDescriptionLine> lines = All(
                Gen(stats, new FakeStringTable().WithPunctuation(), values, skills),
                Build.Entry(97, 6, layer: 54));

            Assert.Equal("+3 to Teleport", lines[0].Text);
            Assert.Equal(3, lines[0].Value);
        }

        [Fact]
        public void DescFunc_28_does_not_clamp_for_another_class()
        {
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(97, ItemDescFunc.Skill, 100));

            var skills = new FakeSkillTable().Add(54, "Teleport", classId: 1);
            var values = new FakeStatValues();
            values.PlayerClass = 3;

            Assert.Equal("+6 to Teleport",
                One(Gen(stats, new FakeStringTable().WithPunctuation(), values, skills),
                    Build.Entry(97, 6, layer: 54)));
        }

        [Fact]
        public void DescFunc_28_does_not_clamp_at_or_below_three()
        {
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(97, ItemDescFunc.Skill, 100));

            var skills = new FakeSkillTable().Add(54, "Teleport", classId: 1);
            var values = new FakeStatValues();
            values.PlayerClass = 1;

            Assert.Equal("+3 to Teleport",
                One(Gen(stats, new FakeStringTable().WithPunctuation(), values, skills),
                    Build.Entry(97, 3, layer: 54)));
        }

        [Fact]
        public void DescFunc_28_drops_an_unknown_skill()
        {
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(97, ItemDescFunc.Skill, 100));

            AssertBlank(Gen(stats, new FakeStringTable().WithPunctuation(), null, new FakeSkillTable()),
                Build.Entry(97, 2, layer: 54));
        }

        [Fact]
        public void DescFunc_28_drops_the_line_with_no_skill_table()
        {
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(97, ItemDescFunc.Skill, 100));

            AssertBlank(Gen(stats, new FakeStringTable().WithPunctuation()),
                Build.Entry(97, 2, layer: 54));
        }

        [Fact]
        public void DescFunc_28_drops_the_line_when_the_to_string_is_missing()
        {
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(97, ItemDescFunc.Skill, 100));

            var strings = new FakeStringTable().Add(DescStringIds.Space, " ").Add(DescStringIds.Plus, "+");
            var skills = new FakeSkillTable().Add(54, "Teleport");

            AssertBlank(Gen(stats, strings, null, skills), Build.Entry(97, 2, layer: 54));
        }

        [Fact]
        public void DescFunc_28_keeps_a_row_that_exists_with_an_empty_name()
        {
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(97, ItemDescFunc.Skill, 100));

            var skills = new FakeSkillTable().Add(54, string.Empty);

            // 0x4e58ba tests the pointer, so an empty entry is not a missing one.
            Assert.Equal("+2 to ",
                One(Gen(stats, new FakeStringTable().WithPunctuation(), null, skills),
                    Build.Entry(97, 2, layer: 54)));
        }

        [Fact]
        public void DescFunc_22_prints_without_a_monster_table_at_all()
        {
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(179, ItemDescFunc.MonsterTypeDamage, 100));

            var strings = new FakeStringTable().WithPunctuation().Add(100, "Damage");

            Assert.Equal("+50% Damage",
                One(Gen(stats, strings), Build.Entry(179, 50, layer: 4)));
        }

        [Fact]
        public void A_line_stringifies_to_its_text()
        {
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(1, ItemDescFunc.PlusValueString, 100));

            IReadOnlyList<ItemDescriptionLine> lines = All(
                Gen(stats, new FakeStringTable().WithPunctuation().Add(100, "to Strength")),
                Build.Entry(1, 10));

            Assert.Equal("+10 to Strength", lines[0].ToString());
        }

        [Fact]
        public void An_unknown_desc_func_prints_nothing()
        {
            // The engine's default arm returns 0.
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(1, 99, 100));

            Assert.Empty(All(Gen(stats, new FakeStringTable().WithPunctuation().Add(100, "Mystery")),
                Build.Entry(1, 10)));
        }

        // =================================================================
        // DescFunc 17 and 18, by time
        // =================================================================

        [Fact]
        public void A_by_time_value_unpacks_into_a_period_and_two_bounds()
        {
            ByTimeValue v = ByTimeValue.Unpack(ByTime.Pack(2, -30, 70));

            Assert.Equal(2, v.Period);
            Assert.Equal(-30, v.Low);
            Assert.Equal(70, v.High);
        }

        [Theory]
        [InlineData(0, 0, 70)]      // at the peak: the high bound
        [InlineData(0, 180, -30)]   // opposite the peak: the low bound
        [InlineData(0, 90, 20)]     // quarter turn: midway
        public void A_by_time_value_interpolates_across_the_day(int period, int degrees, int expected)
        {
            ByTimeValue v = ByTimeValue.Unpack(ByTime.Pack(period, -30, 70));
            Assert.Equal(expected, v.Interpolate(degrees));
        }

        [Fact]
        public void A_by_time_angle_beyond_half_a_turn_folds_back()
        {
            // 0x65ca7b: distance > 180 becomes 360 - distance.
            ByTimeValue v = ByTimeValue.Unpack(ByTime.Pack(0, -30, 70));
            Assert.Equal(v.Interpolate(90), v.Interpolate(270));
        }

        [Fact]
        public void DescFunc_17_prefixes_the_period_name_and_the_interpolated_value()
        {
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(1, ItemDescFunc.ValueStringByTime, 100));

            // The name is indexed by the packed period through the permuted table at
            // 0x6DBD88, so period 1 resolves to string 21237.
            var strings = new FakeStringTable().WithPunctuation()
                .Add(100, "to Strength")
                .Add(DescStringIds.PeriodOfDay[1], "Dawn");

            var time = new FakeGameTime();
            time.Degrees = 90; // the peak for period 1

            Assert.Equal("Dawn\n+70 to Strength",
                One(Gen(stats, strings, null, null, null, null, time),
                    Build.Entry(1, ByTime.Pack(1, -30, 70))));
        }

        [Fact]
        public void DescFunc_18_adds_a_percent()
        {
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(1, ItemDescFunc.ValuePercentStringByTime, 100));

            var strings = new FakeStringTable().WithPunctuation()
                .Add(100, "Enhanced Defense")
                .Add(DescStringIds.PeriodOfDay[0], "Dusk");

            var time = new FakeGameTime();

            Assert.Equal("Dusk\n+70% Enhanced Defense",
                One(Gen(stats, strings, null, null, null, null, time),
                    Build.Entry(1, ByTime.Pack(0, -30, 70))));
        }

        [Fact]
        public void DescFunc_17_shows_the_low_bound_when_there_is_no_current_act()
        {
            // 0x4e53c0: with no act the interpolation is bypassed entirely.
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(1, ItemDescFunc.ValueStringByTime, 100));

            var strings = new FakeStringTable().WithPunctuation()
                .Add(100, "to Strength")
                .Add(DescStringIds.PeriodOfDay[0], "Dusk");

            var time = new FakeGameTime();
            time.HasTime = false;
            time.Degrees = 0;

            Assert.Equal("Dusk\n+40 to Strength",
                One(Gen(stats, strings, null, null, null, null, time),
                    Build.Entry(1, ByTime.Pack(0, 40, 70))));
        }

        [Fact]
        public void DescFunc_17_works_without_a_time_provider()
        {
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(1, ItemDescFunc.ValueStringByTime, 100));

            var strings = new FakeStringTable().WithPunctuation()
                .Add(100, "to Strength")
                .Add(DescStringIds.PeriodOfDay[0], "Dusk");

            Assert.Equal("Dusk\n+40 to Strength",
                One(Gen(stats, strings), Build.Entry(1, ByTime.Pack(0, 40, 70))));
        }

        [Fact]
        public void DescFunc_17_omits_a_missing_period_name()
        {
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(1, ItemDescFunc.ValueStringByTime, 100));

            Assert.Equal("\n+40 to Strength",
                One(Gen(stats, new FakeStringTable().WithPunctuation().Add(100, "to Strength")),
                    Build.Entry(1, ByTime.Pack(0, 40, 70))));
        }

        [Fact]
        public void DescFunc_17_leaves_the_number_out_when_only_the_adjusted_value_is_negative()
        {
            // 0x4e5436: adjusted < 0 while the raw stat is >= 0 leaves the digits empty.
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(1, ItemDescFunc.ValueStringByTime, 100));

            var strings = new FakeStringTable().WithPunctuation().Add(100, "to Strength");

            var time = new FakeGameTime();
            time.Degrees = 180; // opposite the peak, so the low bound applies

            Assert.Equal("\n to Strength",
                One(Gen(stats, strings, null, null, null, null, time),
                    Build.Entry(1, ByTime.Pack(0, -30, 70))));
        }

        [Fact]
        public void DescFunc_17_with_an_unusual_desc_val_keeps_only_the_period_name()
        {
            // 0x4e54a3: DescVal other than 1 or 2 leaves the value part empty, but the
            // period name and separator have already been written.
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(1, ItemDescFunc.ValueStringByTime, 100, descVal: 0));

            var strings = new FakeStringTable().WithPunctuation()
                .Add(100, "to Strength")
                .Add(DescStringIds.PeriodOfDay[0], "Dusk");

            Assert.Equal("Dusk\n", One(Gen(stats, strings), Build.Entry(1, ByTime.Pack(0, 40, 70))));
        }
        // =================================================================
        // DescGrp
        // =================================================================

        private static FakeStatCostTable ResistTable(int descGrpFunc = ItemDescFunc.PlusValueString,
            int descGrpVal = 1, int grpStrPos = 200)
        {
            var stats = new FakeStatCostTable();

            foreach (int statId in new[] { 39, 41, 43, 45 })
            {
                StatDescriptor descriptor = Build.Stat(statId, ItemDescFunc.PlusValueString, 100 + statId);
                descriptor.DescGrp = 1;
                descriptor.DescGrpFunc = descGrpFunc;
                descriptor.DescGrpVal = descGrpVal;
                descriptor.DescGrpStrPos = grpStrPos;
                stats.Add(descriptor);
            }

            stats.AddGroup(1, 39, 41, 43, 45);
            return stats;
        }

        private static FakeStringTable ResistStrings()
        {
            return new FakeStringTable().WithPunctuation()
                .Add(139, "Fire Resist")
                .Add(141, "Lightning Resist")
                .Add(143, "Cold Resist")
                .Add(145, "Poison Resist")
                .Add(200, "to All Resistances");
        }

        private static FakeStatValues ResistValues(params int[] values)
        {
            var source = new FakeStatValues();
            int[] ids = { 39, 41, 43, 45 };
            for (int i = 0; i < values.Length; ++i)
            {
                source.AddBase(ids[i], values[i]);
            }

            return source;
        }

        [Fact]
        public void A_complete_group_at_one_value_prints_once_from_its_lowest_member()
        {
            IReadOnlyList<ItemDescriptionLine> lines = All(
                Gen(ResistTable(), ResistStrings(), ResistValues(30, 30, 30, 30)),
                Build.Entry(39, 30), Build.Entry(41, 30), Build.Entry(43, 30), Build.Entry(45, 30));

            Assert.Single(lines);
            Assert.Equal("+30 to All Resistances", lines[0].Text);
            Assert.True(lines[0].IsGroup);
            Assert.Equal(39, lines[0].StatId); // the lowest id in the group emits
        }

        [Fact]
        public void A_group_with_a_member_at_a_different_value_prints_individually()
        {
            IReadOnlyList<ItemDescriptionLine> lines = All(
                Gen(ResistTable(), ResistStrings(), ResistValues(30, 30, 30, 15)),
                Build.Entry(39, 30), Build.Entry(41, 30), Build.Entry(43, 30), Build.Entry(45, 15));

            Assert.Equal(4, lines.Count);
            Assert.All(lines, line => Assert.False(line.IsGroup));
        }

        [Fact]
        public void A_group_with_an_absent_member_prints_individually()
        {
            // GetBaseStatValue returns 0 for the absent member, which breaks the equality.
            IReadOnlyList<ItemDescriptionLine> lines = All(
                Gen(ResistTable(), ResistStrings(), ResistValues(30, 30, 30)),
                Build.Entry(39, 30), Build.Entry(41, 30), Build.Entry(43, 30));

            Assert.Equal(3, lines.Count);
            Assert.All(lines, line => Assert.False(line.IsGroup));
        }

        [Fact]
        public void A_group_falls_apart_without_a_value_source()
        {
            IReadOnlyList<ItemDescriptionLine> lines = All(
                Gen(ResistTable(), ResistStrings()),
                Build.Entry(39, 30), Build.Entry(41, 30), Build.Entry(43, 30), Build.Entry(45, 30));

            Assert.Equal(4, lines.Count);
        }

        [Fact]
        public void A_group_the_table_does_not_know_prints_individually()
        {
            FakeStatCostTable stats = ResistTable();
            stats.Groups.Clear();

            IReadOnlyList<ItemDescriptionLine> lines = All(
                Gen(stats, ResistStrings(), ResistValues(30, 30, 30, 30)),
                Build.Entry(39, 30), Build.Entry(41, 30), Build.Entry(43, 30), Build.Entry(45, 30));

            Assert.Equal(4, lines.Count);
        }

        [Fact]
        public void An_empty_group_prints_individually()
        {
            FakeStatCostTable stats = ResistTable();
            stats.AddGroup(1);

            IReadOnlyList<ItemDescriptionLine> lines = All(
                Gen(stats, ResistStrings(), ResistValues(30, 30, 30, 30)),
                Build.Entry(39, 30));

            Assert.Single(lines);
            Assert.False(lines[0].IsGroup);
        }

        [Fact]
        public void A_group_naming_a_member_with_no_row_prints_individually()
        {
            FakeStatCostTable stats = ResistTable();
            stats.AddGroup(1, 39, 41, 43, 45, 999);

            IReadOnlyList<ItemDescriptionLine> lines = All(
                Gen(stats, ResistStrings(), ResistValues(30, 30, 30, 30)),
                Build.Entry(39, 30));

            Assert.Single(lines);
            Assert.False(lines[0].IsGroup);
        }

        [Fact]
        public void A_group_with_no_group_desc_func_prints_nothing_for_its_primary()
        {
            // The engine reads DescGrpFunc once grouped; a zero there yields no line.
            IReadOnlyList<ItemDescriptionLine> lines = All(
                Gen(ResistTable(descGrpFunc: 0), ResistStrings(), ResistValues(30, 30, 30, 30)),
                Build.Entry(39, 30), Build.Entry(41, 30), Build.Entry(43, 30), Build.Entry(45, 30));

            Assert.Empty(lines);
        }

        [Fact]
        public void A_group_whose_string_is_missing_still_prints_its_number()
        {
            IReadOnlyList<ItemDescriptionLine> lines = All(
                Gen(ResistTable(grpStrPos: 0), ResistStrings(), ResistValues(30, 30, 30, 30)),
                Build.Entry(39, 30), Build.Entry(41, 30), Build.Entry(43, 30), Build.Entry(45, 30));

            Assert.Single(lines);
            Assert.Equal("+30 ", lines[0].Text);
        }

        [Fact]
        public void A_group_honours_its_own_desc_val()
        {
            IReadOnlyList<ItemDescriptionLine> lines = All(
                Gen(ResistTable(descGrpVal: 2), ResistStrings(), ResistValues(30, 30, 30, 30)),
                Build.Entry(39, 30), Build.Entry(41, 30), Build.Entry(43, 30), Build.Entry(45, 30));

            Assert.Single(lines);
            Assert.Equal("to All Resistances +30", lines[0].Text);
        }

        [Fact]
        public void A_group_uses_its_own_second_string()
        {
            FakeStatCostTable stats = ResistTable(descGrpFunc: ItemDescFunc.ValueStringString2);
            foreach (StatDescriptor descriptor in stats.Stats.Values)
            {
                descriptor.DescGrpStr2 = 201;
            }

            FakeStringTable strings = ResistStrings().Add(201, "(group)");

            IReadOnlyList<ItemDescriptionLine> lines = All(
                Gen(stats, strings, ResistValues(30, 30, 30, 30)),
                Build.Entry(39, 30), Build.Entry(41, 30), Build.Entry(43, 30), Build.Entry(45, 30));

            Assert.Equal("30 to All Resistances (group)", lines[0].Text);
        }

        [Fact]
        public void A_grouped_negative_value_uses_the_group_negative_string()
        {
            FakeStatCostTable stats = ResistTable();
            foreach (StatDescriptor descriptor in stats.Stats.Values)
            {
                descriptor.DescGrpStrNeg = 202;
            }

            FakeStringTable strings = ResistStrings().Add(202, "from All Resistances");

            IReadOnlyList<ItemDescriptionLine> lines = All(
                Gen(stats, strings, ResistValues(-30, -30, -30, -30)),
                Build.Entry(39, -30), Build.Entry(41, -30), Build.Entry(43, -30), Build.Entry(45, -30));

            Assert.Equal("-30 from All Resistances", lines[0].Text);
        }
    }
}


