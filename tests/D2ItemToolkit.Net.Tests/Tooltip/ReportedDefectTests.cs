using System;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace D2ItemToolkit.Tests
{
    /// <summary>
    /// Three defects reported from a live consumer. Each asserts the behaviour the caller expected,
    /// so all of them FAIL until the engine is fixed, and each dumps what it actually got so the
    /// difference is readable rather than inferred from an assertion message.
    /// </summary>
    public class ReportedDefectTests
    {
        private readonly ITestOutputHelper _out;

        public ReportedDefectTests(ITestOutputHelper output)
        {
            _out = output;
        }

        private static readonly D2DataFiles Data = D2DataFiles.LoadEmbedded();
        private static readonly TooltipEngine Engine = TooltipEngine.Embedded;

        private static readonly ItemTable Items = new ItemTable(
            Data.Weapons, Data.Armor, Data.Misc);

        /// <summary>
        /// Tal Rasha's Horadric Crest, `xsk`, a Death Mask — the piece the report named. Post-splice
        /// setitems row, 0-based. (81 is Griswold's Valor, which is a Corona; an earlier revision
        /// paired that index with this item code and the two disagreed.)
        /// </summary>
        private const int SocketableSetHelm = 80;

        /// <summary>
        /// Carried, NOT worn. A worn set item's fillers are discarded by the recalc, so there is
        /// nothing for the separated mode to move and nothing for a range to describe —
        /// <see cref="WornSetItemSocketTests"/> pins that case. The bug these tests cover is that
        /// the set path ignored both options outright, which needs a piece the fillers still apply
        /// to.
        /// </summary>
        private const int LocationStash = 3;

        private const int StatDefense = 31;
        private const int StatArmorPercent = 16;
        private const int StatNumSockets = 194;

        private static Unit SocketedSetHelm()
        {
            var rune = new Unit();
            rune.UnitType = 4;
            rune.ClassId = Items.ClassIdForCode("r22");   // Um Rune
            rune.ItemFlags = ItemRecordFlags.Identified;

            var helm = new Unit();
            helm.UnitType = 4;
            helm.ClassId = Items.ClassIdForCode("xsk");
            helm.Quality = ItemQualityNo.Set;
            helm.FileIndex = SocketableSetHelm;
            helm.ItemFlags = ItemRecordFlags.Identified | ItemRecordFlags.Socketed;
            helm.Location = LocationStash;
            helm.X = 1;

            helm.StatsLists.Add(
                new UnitStatList(0, ItemStatListFlags.Extended)
                    .Add(StatDefense, 100)
                    .Add(StatNumSockets, 1));

            helm.Items.Add(rune);
            return helm;
        }

        private void Dump(string what, Tooltip tip)
        {
            _out.WriteLine("--- " + what + " (kind " + tip.Kind + ") ---");
            foreach (ItemTooltipLine line in tip.Lines)
            {
                _out.WriteLine(
                    "  [" + line.Section + "] " + (line.Text ?? string.Empty).TrimEnd('\n'));
            }
        }

        [Fact]
        public void A_socketed_set_item_honours_SeparateSocketContributions()
        {
            // REPORTED: "socketed talrasha helm with um rune, when I press ctrl, it just removes
            // some line breaks, but does not break the um rune apart, as a separate item."
            Unit helm = SocketedSetHelm();

            var options = new TooltipOptions();
            options.Sockets = SocketMode.Separated;

            Tooltip tip = Engine.Render(helm, null, options);
            Dump("set item, SeparateSocketContributions = true", tip);

            Assert.Contains(
                tip.Lines,
                l => l.Section == ItemTooltipSection.SocketContribution);
        }

        [Fact]
        public void A_set_item_honours_ShowRolledRanges()
        {
            // The same defect's other half: Render returns through the set-item builder before the
            // annotation is installed, so ctrl does nothing on any of the 127 set pieces.
            Unit helm = SocketedSetHelm();

            var options = new TooltipOptions();
            options.Ranges = new RangeDisplay();
            options.Ranges.Color = -1;

            Tooltip tip = Engine.Render(helm, null, options);
            Dump("set item, ShowRolledRanges = true", tip);

            Assert.Contains(tip.Lines, l => (l.Text ?? string.Empty).Contains("["));
        }

        [Fact]
        public void The_defense_span_brackets_the_defense_the_line_shows()
        {
            // REPORTED: "items that have enhanced defence, control show the range of the base
            // defence, but the actual number is enhanced, so it's not clear where it rolled in the
            // base range."
            //
            // A Large Shield rolls 12..14 base. Under an enhanced-defence prefix a base 13 shows
            // as 13 + 13 * pct / 100, so the annotation must bracket THAT — not report the 12..14
            // the base rolled within, which is a span the printed number can never fall inside.
            //
            // The prefix is a real one, because the span is rebuilt from the affix the record
            // names. A hand-authored `ac%` on the stat list alone gives the reconstruction nothing
            // to roll and the defect hides.
            int defAffix = FirstAffixGranting("ac%");
            Assert.True(defAffix > 0, "no magic affix grants `ac%`");

            int pct = MidRollOf(defAffix, "ac%");

            var shield = new Unit();
            shield.UnitType = 4;
            shield.ClassId = Items.ClassIdForCode("lrg");
            shield.Quality = ItemQualityNo.Magic;
            shield.ItemFlags = ItemRecordFlags.Identified;
            shield.MagicPrefix[0] = defAffix;

            // maxac + 1, because the `ac%` affix maximises the base — see
            // DefenseOutOfRangeTests. A hand-authored roll inside minac..maxac is a record the
            // game cannot produce, and the span then correctly refuses to contain it.
            int baseDefense = Engine.Items.GetInt(shield.ClassId, "maxac") + 1;

            shield.StatsLists.Add(
                new UnitStatList(0, ItemStatListFlags.Extended).Add(StatDefense, baseDefense));
            shield.StatsLists.Add(
                new UnitStatList(0, ItemStatListFlags.Magic).Add(StatArmorPercent, pct));

            var options = new TooltipOptions();
            options.Ranges = new RangeDisplay();
            options.Ranges.Color = -1;

            Tooltip tip = Engine.Render(shield, null, options);
            Dump("enhanced-defence shield, ShowRolledRanges = true", tip);

            // Markers stripped first: the Defense line carries an embedded marker, and reading
            // digits straight off it yields "332" for a value of 32.
            string defense = tip.Lines
                .Select(Plain)
                .Single(t => t.StartsWith("Defense:", StringComparison.Ordinal));

            int shown = int.Parse(
                new string(defense.SkipWhile(c => !char.IsDigit(c)).TakeWhile(char.IsDigit)
                    .ToArray()));

            _out.WriteLine("shown defense = " + shown);
            _out.WriteLine("annotated line = " + defense);

            int low, high;
            Assert.True(TryReadSpan(defense, out low, out high), "no [low-high] on the Defense line");

            _out.WriteLine("span = " + low + ".." + high);

            Assert.True(
                shown >= low && shown <= high,
                "Defense reads " + shown + " but the span offered is " + low + ".." + high);
        }

        [Fact]
        public void A_shifted_stats_span_is_written_in_the_units_the_line_prints()
        {
            // REPORTED: "+11 to Life [2816-3840]" — 2816 is 11 << 8. Life, mana and stamina carry
            // ValShift 8, so the stat is stored 8.8 fixed point and the writer shifts it down
            // before printing. The span is not shifted, so it is 256x the number beside it.
            //
            // The annotation already reads DisplayLow/DisplayHigh; what those do NOT do is undo
            // ValShift — Display() decodes stat 204's charge byte and returns everything else
            // untouched. Only shifted stats are affected, which is why resists look right.
            int hpAffix = FirstAffixGranting("hp");
            Assert.True(hpAffix > 0, "no magic affix grants `hp`");

            var charm = new Unit();
            charm.UnitType = 4;
            charm.ClassId = Items.ClassIdForCode("cm1");
            charm.Quality = ItemQualityNo.Magic;
            charm.ItemFlags = ItemRecordFlags.Identified;
            charm.MagicPrefix[0] = hpAffix;

            charm.StatsLists.Add(
                new UnitStatList(0, ItemStatListFlags.Magic).Add(7, 11 << 8));

            var options = new TooltipOptions();
            options.Ranges = new RangeDisplay();
            options.Ranges.Color = -1;

            Tooltip tip = Engine.Render(charm, null, options);
            Dump("charm with +Life, ShowRolledRanges = true", tip);

            string life = tip.Lines
                .Select(l => (l.Text ?? string.Empty).TrimEnd('\n'))
                .Single(t => t.Contains("Life"));

            int low, high;
            Assert.True(TryReadSpan(life, out low, out high), "no [low-high] on the Life line");

            _out.WriteLine("line = " + life);
            _out.WriteLine("span = " + low + ".." + high);

            // The line prints 11. A span in the same units brackets it; one in raw storage units
            // is 256x too large.
            Assert.True(
                low >= 11 - 100 && high <= 11 + 100,
                "Life prints 11 but the span offered is " + low + ".." + high
                + " — that is storage units, not display units");
        }

        /// <summary>1-based id of the first magic affix whose mod grants <paramref name="code"/>.</summary>
        private static int FirstAffixGranting(string code)
        {
            var affixes = new MagicAffixTable(Data);

            for (int id = 1; id <= affixes.RowCount; ++id)
            {
                TxtFile table;
                int row;
                if (!affixes.TryResolve(id, out table, out row))
                {
                    continue;
                }

                for (int mod = 1; mod <= 3; ++mod)
                {
                    if (table.GetString(row, "mod" + mod + "code").Trim() == code
                        && table.GetInt(row, "mod" + mod + "min")
                            != table.GetInt(row, "mod" + mod + "max"))
                    {
                        return id;
                    }
                }
            }

            return -1;
        }

        [Fact]
        public void An_aggregated_line_names_every_stat_it_shows()
        {
            // REQUESTED by a consumer: Tooltip.Lines gave no way to say which stat a line is about
            // for a line that speaks for more than one. ItemDescriptionLine already carried
            // ShownStats and Aggregated; they were dropped on the way up to ItemTooltipLine.
            //
            // firemindam(48) and firemaxdam(49) are written as ONE line, "Adds 1-4 fire damage".
            const int FireMin = 48;
            const int FireMax = 49;

            var sword = new Unit();
            sword.UnitType = 4;
            sword.ClassId = Items.ClassIdForCode("crs");
            sword.Quality = ItemQualityNo.Magic;
            sword.ItemFlags = ItemRecordFlags.Identified;

            sword.StatsLists.Add(
                new UnitStatList(0, ItemStatListFlags.Magic)
                    .Add(FireMin, 1)
                    .Add(FireMax, 4));

            Tooltip tip = Engine.Render(sword);
            Dump("fire damage, aggregated line", tip);

            ItemTooltipLine fire = tip.Lines.Single(l => l.StatId == FireMin);

            Assert.Equal("Adds 1-4 fire damage", Plain(fire));
            Assert.True(fire.Aggregated, "the fire line speaks for two stats");
            Assert.Equal(new[] { FireMin, FireMax }, fire.ShownStats);

            // A single-stat line reports itself as one: null ShownStats means "just StatId", which
            // is what lets a caller treat the two uniformly without a special case.
            ItemTooltipLine single = tip.Lines.First(
                l => l.Section == ItemTooltipSection.Modifiers && l.StatId >= 0 && !l.Aggregated);

            Assert.Null(single.ShownStats);
        }

        /// <summary>The midpoint of the roll <paramref name="affix"/> gives <paramref name="code"/>.</summary>
        private static int MidRollOf(int affix, string code)
        {
            var affixes = new MagicAffixTable(Data);

            TxtFile table;
            int row;
            Assert.True(affixes.TryResolve(affix, out table, out row));

            for (int mod = 1; mod <= 3; ++mod)
            {
                if (table.GetString(row, "mod" + mod + "code").Trim() != code)
                {
                    continue;
                }

                return (table.GetInt(row, "mod" + mod + "min")
                        + table.GetInt(row, "mod" + mod + "max")) / 2;
            }

            throw new InvalidOperationException("affix " + affix + " does not grant " + code);
        }

        /// <summary>The line without its embedded colour markers or trailing terminator.</summary>
        private static string Plain(ItemTooltipLine line)
        {
            return Plain(line.Text);
        }

        private static string Plain(string text)
        {
            return System.Text.RegularExpressions.Regex
                .Replace(text ?? string.Empty, "ÿc.", string.Empty)
                .TrimEnd('\n');
        }

        private static bool TryReadSpan(string text, out int low, out int high)
        {
            low = 0;
            high = 0;

            int open = text.IndexOf('[');
            int dash = open < 0 ? -1 : text.IndexOf('-', open);
            int close = open < 0 ? -1 : text.IndexOf(']', open);

            return open >= 0 && dash > open && close > dash
                && int.TryParse(text.Substring(open + 1, dash - open - 1), out low)
                && int.TryParse(text.Substring(dash + 1, close - dash - 1), out high);
        }
    }
}
