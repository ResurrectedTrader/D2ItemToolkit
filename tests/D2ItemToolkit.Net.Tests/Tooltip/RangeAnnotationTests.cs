using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace D2ItemToolkit.Tests
{
    /// <summary>
    /// The ShowRolledRanges render flag. The game has no such mode, so the only things worth
    /// asserting are that it is INERT when off, that it annotates exactly the lines where one span
    /// is unambiguous, and that it stays silent on the lines where it would not be.
    /// </summary>
    public class RangeAnnotationTests
    {
        private static readonly D2DataFiles Data = D2DataFiles.LoadEmbedded();
        private static readonly TooltipEngine Engine = TooltipEngine.Embedded;

        private static readonly ItemTable Items = new ItemTable(
            Data.Weapons, Data.Armor, Data.Misc);

        /// <summary>
        /// A record whose stats sit at the midpoint of every reconstructed span, so the rendered
        /// lines are ones a real item could carry and every annotation has something to attach to.
        /// </summary>
        private static Unit AtMidRoll(Unit shell)
        {
            ItemRollRanges ranges = Engine.Ranges(shell);

            var baseStats = new List<UnitStat>();
            var modStats = new List<UnitStat>();

            foreach (RolledStatRange range in ranges.Stats)
            {
                var stat = new UnitStat();
                stat.Id = range.StatId;
                stat.Layer = range.Layer;
                stat.Value = range.Low + (range.High - range.Low) / 2;
                (range.StatId == 31 ? baseStats : modStats).Add(stat);
            }

            // A layer-rolling property contributes a stat too — at one of its possible layers, the
            // low one here. Without it the record has no skill line to inspect.
            foreach (RolledLayerRange range in ranges.LayerVaries)
            {
                var stat = new UnitStat();
                stat.Id = range.StatId;
                stat.Layer = range.LayerLow;
                stat.Value = range.Value;
                modStats.Add(stat);
            }

            var lists = new List<UnitStatList>();

            var baseList = new UnitStatList();
            baseList.StateNo = 0;
            baseList.Flags = 2147483648;
            baseList.Stats = baseStats;
            lists.Add(baseList);

            var modList = new UnitStatList();
            modList.StateNo = 0;
            modList.Flags = 64;
            modList.Stats = modStats;
            lists.Add(modList);

            shell.StatsLists = lists;
            return shell;
        }

        private static Unit Unique(string index)
        {
            int row = Data.UniqueItems.FindRow("index", index);
            Assert.True(row >= 0, index);

            var unit = new Unit();
            unit.UnitType = 4;
            unit.Quality = 7;
            unit.FileIndex = row;
            unit.ClassId = Items.ClassIdForCode(Data.UniqueItems.GetString(row, "code").Trim());
            unit.ItemFlags = ItemRecordFlags.Identified;
            unit.ItemLevel = 80;
            return AtMidRoll(unit);
        }

        /// <summary>
        /// Ranges on, colour off. The DEFAULT paints them grey, which embeds two markers into every
        /// annotated line — so the tests below, which are about the TEXT, opt out of it. The default
        /// itself is pinned by <see cref="The_annotation_is_grey_unless_asked_otherwise"/>.
        /// </summary>
        private static TooltipOptions Annotating()
        {
            var options = new TooltipOptions();
            options.ShowRolledRanges = true;
            options.RangeColor = -1;
            return options;
        }

        private static string[] Texts(Tooltip tooltip)
        {
            return tooltip.Lines.Select(l => l.Text ?? string.Empty).ToArray();
        }

        [Fact]
        public void The_flag_is_inert_when_off()
        {
            // The whole point: an un-annotated render has to stay what the game draws, which is what
            // the corpus differential also holds.
            Unit item = Unique("The Eye of Etlich");

            Assert.Equal(
                Engine.Render(item).Text,
                Engine.Render(item, null, new TooltipOptions()).Text);
        }

        [Fact]
        public void A_modifier_line_carries_its_span()
        {
            // lifesteal is 3..7 on this row, and the line shows one stat, so one span fits.
            string[] lines = Texts(Engine.Render(Unique("The Eye of Etlich"), null, Annotating()));

            Assert.Contains(lines, l => l.Contains("Life stolen per hit [3-7]"));
            Assert.Contains(lines, l => l.Contains("Defense vs. Missile [10-40]"));
            Assert.Contains(lines, l => l.Contains("Light Radius [1-5]"));
        }

        [Fact]
        public void The_defense_line_carries_the_base_armour_roll()
        {
            // The one SECTION that gets annotated: Defense shows a single stat whose base rolls.
            Unit shako = Unique("Harlequin Crest");
            string[] lines = Texts(Engine.Render(shako, null, Annotating()));

            string expected = " [" + Items.GetInt(shako.ClassId, "minac")
                + "-" + Items.GetInt(shako.ClassId, "maxac") + "]";

            Assert.Contains(lines, l => l.StartsWith("Defense:") && l.Contains(expected));
        }

        [Fact]
        public void A_two_valued_damage_line_gets_both_spans()
        {
            // "Adds 1-4 cold damage" prints coldmindam AND coldmaxdam, whose spans differ (1..2 and
            // 3..5). One number would belong to neither half, so both are written positionally.
            string[] lines = Texts(Engine.Render(Unique("The Eye of Etlich"), null, Annotating()));

            string adds = lines.Single(l => l.Contains("cold damage"));
            Assert.Contains("[(1-2)-(3-5)]", adds);
        }

        [Fact]
        public void A_group_line_collapses_its_members_to_one_span()
        {
            // Every stat a DescGrp line covers shares the single number it prints, so their spans
            // agree — repeating them would give "[(2-5)-(2-5)-(2-5)-(2-5)]".
            var options = Annotating();
            string[] lines = Texts(Engine.Render(Unique("The Eye of Etlich"), null, options));

            // Not a range on this item, so nothing at all rather than a four-way degenerate.
            string allSkills = lines.Single(l => l.Contains("to All Skills"));
            Assert.DoesNotContain("[", allSkills);
        }

        [Fact]
        public void The_single_valued_enhanced_damage_line_is_annotated()
        {
            // The counterpart: Enhanced Damage is also an aggregated line, but it prints the MIN
            // half alone, so its span is unambiguous and DOES appear. Getting this wrong silences
            // the most-wanted range on the item.
            string[] lines = Texts(Engine.Render(Unique("Titan's Revenge"), null, Annotating()));

            Assert.Contains(lines, l => l.Contains("Enhanced Damage [150-200]"));
        }

        [Fact]
        public void A_packed_value_is_decoded_rather_than_printed_raw()
        {
            // Stat 204 packs (maxCharges << 8) + current. Printed raw the span reads "[2306-2313]";
            // decoded it is the CURRENT charge count, which is the number the line shows first and
            // the only part the seed varies.
            int row = Data.Runes.FindRow("Name", "Runeword88");
            Assert.True(row >= 0);

            var unit = new Unit();
            unit.UnitType = 4;
            unit.Quality = 2;
            unit.ClassId = Items.ClassIdForCode("crs");
            unit.ItemFlags = ItemRecordFlags.Identified | ItemRecordFlags.Runeword;
            unit.ItemLevel = 70;
            unit.MagicPrefix[0] = Data.Strings.ResolveKey(
                Data.Runes.GetString(row, "Name").Trim());

            string[] lines = Texts(Engine.Render(AtMidRoll(unit), null, Annotating()));

            string charges = lines.Single(l => l.Contains("Charges"));

            RolledStatRange packed = Engine.Ranges(unit).Stats.Single(r => r.StatId == 204);
            Assert.True(packed.IsPackedEncoding);
            Assert.True(packed.IsRange);

            // The raw span must NOT appear; the decoded charge count must.
            Assert.DoesNotContain("[" + packed.Low + "-" + packed.High + "]", charges);
            Assert.Contains("[" + packed.DisplayLow + "-" + packed.DisplayHigh + "]", charges);

            // And the decoded end really is the low byte, which is the "5" in "(5/9 Charges)".
            Assert.Equal(packed.Low & 0xFF, packed.DisplayLow);
            Assert.Equal(packed.High & 0xFF, packed.DisplayHigh);
        }

        [Fact]
        public void A_by_time_stat_never_has_a_span_to_show()
        {
            // Func 18 packs property.Min and property.Max straight in and never rolls (0x65f870 has
            // no RollRandomValue call), so both ends produce the identical word. There is nothing to
            // unpack for a range because there is no range — which is why by-time needs no decoding
            // even though it is a packed encoding.
            int affix = -1;
            var affixes = new MagicAffixTable(Data);

            for (int id = 1; id <= affixes.RowCount && affix < 0; ++id)
            {
                TxtFile table;
                int row;
                if (!affixes.TryResolve(id, out table, out row))
                {
                    continue;
                }

                for (int mod = 1; mod <= 3; ++mod)
                {
                    if (table.GetString(row, "mod" + mod + "code").Trim() == "ac/time")
                    {
                        affix = id;
                    }
                }
            }

            Assert.True(affix > 0);

            var unit = new Unit();
            unit.UnitType = 4;
            unit.Quality = 4;
            unit.ClassId = Items.ClassIdForCode("cap");
            unit.ItemFlags = ItemRecordFlags.Identified;
            unit.MagicPrefix[0] = affix;

            RolledStatRange byTime = Engine.Ranges(unit).Stats.Single(r => r.StatId == 268);

            Assert.True(byTime.IsPackedEncoding);
            Assert.False(byTime.IsRange);
        }

        [Fact]
        public void A_stat_that_could_not_have_varied_is_left_alone()
        {
            // Harlequin Crest's own props are all fixed, so only the base defense may be annotated.
            string[] lines = Texts(Engine.Render(Unique("Harlequin Crest"), null, Annotating()));

            Assert.Equal(1, lines.Count(l => l.Contains("[")));
            Assert.Contains(lines, l => l.StartsWith("Defense:") && l.Contains("["));
        }

        [Fact]
        public void The_format_is_the_callers_to_choose()
        {
            TooltipOptions options = Annotating();
            options.RangeAnnotation = ranges =>
                ranges[0].StatId == 89
                    ? " (" + ranges[0].Low + ".." + ranges[0].High + ")"
                    : null;

            string[] lines = Texts(Engine.Render(Unique("The Eye of Etlich"), null, options));

            Assert.Contains(lines, l => l.Contains("Light Radius (1..5)"));

            // Returning null for every other stat suppresses them, so exactly one line is marked.
            Assert.Equal(1, lines.Count(l => l.Contains("(1..5)")));
            Assert.DoesNotContain(lines, l => l.Contains("[3-7]"));
        }

        [Fact]
        public void The_annotation_is_grey_unless_asked_otherwise()
        {
            // A range is text the game never draws, so inheriting the stat line's blue made it read
            // as part of the line. The default is the game's own grey — asserted here rather than
            // left implicit, because every other test in this file opts out of it.
            var options = new TooltipOptions();
            options.ShowRolledRanges = true;

            Assert.Equal(ItemTooltipColor.SocketedOrEthereal, options.RangeColor);

            string[] lines = Texts(Engine.Render(Unique("The Eye of Etlich"), null, options));
            string light = lines.Single(l => l.Contains("Light Radius"));

            Assert.Contains(
                ItemTooltipColor.Marker + "5 [1-5]" + ItemTooltipColor.Marker + "3",
                light);
        }

        [Fact]
        public void The_annotation_can_be_painted_its_own_colour()
        {
            TooltipOptions options = Annotating();
            options.RangeColor = ItemTooltipColor.White;

            string[] lines = Texts(Engine.Render(Unique("The Eye of Etlich"), null, options));
            string light = lines.Single(l => l.Contains("Light Radius"));

            // The annotation is wrapped whole — its leading space included, which has no glyph — by
            // a marker for the range colour and then one restoring the line's own, so nothing after
            // it is repainted.
            Assert.Contains(
                ItemTooltipColor.Marker + "0 [1-5]" + ItemTooltipColor.Marker + "3",
                light);
        }

        [Fact]
        public void A_coloured_annotation_does_not_leak_into_the_next_line()
        {
            // The running colour is tracked from the UN-annotated text, so the marker the annotation
            // embeds cannot change what the following line inherits.
            Unit item = Unique("The Eye of Etlich");

            TooltipOptions options = Annotating();
            options.RangeColor = ItemTooltipColor.White;

            IReadOnlyList<ItemTooltipLine> plain = Engine.Render(item).Lines;
            IReadOnlyList<ItemTooltipLine> painted = Engine.Render(item, null, options).Lines;

            Assert.Equal(plain.Count, painted.Count);
            for (int at = 0; at < plain.Count; ++at)
            {
                Assert.Equal(plain[at].Color, painted[at].Color);
            }
        }

        [Fact]
        public void Every_stat_line_reports_which_stat_it_shows()
        {
            // Independent of the flag: the line-to-stat mapping is what lets a caller build its own
            // display instead of re-deriving which stat a line came from.
            Tooltip tooltip = Engine.Render(Unique("The Eye of Etlich"));

            ItemTooltipLine light = tooltip.Lines.Single(
                l => (l.Text ?? string.Empty).Contains("Light Radius"));

            Assert.Equal(89, light.StatId);
            Assert.Equal(0, light.Layer);

            // A line that shows no stat says so rather than claiming stat 0.
            Assert.All(
                tooltip.Lines.Where(l => l.Section == ItemTooltipSection.RequiredLevel),
                line => Assert.Equal(-1, line.StatId));
        }

        [Fact]
        public void A_skill_line_reports_its_layer()
        {
            // Ormus' Robes grants a rolled sorceress skill, so the line's identity is the LAYER —
            // a stat id alone would not say which skill.
            Tooltip tooltip = Engine.Render(Unique("Ormus' Robes"));

            ItemTooltipLine skill = tooltip.Lines.FirstOrDefault(l => l.StatId == 107);

            Assert.NotNull(skill);
            Assert.True(skill.Layer >= 36 && skill.Layer <= 60, "layer was " + skill.Layer);
        }
    }
}
