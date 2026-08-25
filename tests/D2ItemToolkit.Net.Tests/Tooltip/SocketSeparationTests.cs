using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace D2ItemToolkit.Tests
{
    /// <summary>
    /// SeparateSocketContributions. The game always merges a filler's mods into the item's own
    /// block, so this mode has no original to be checked against — what it must guarantee is that
    /// nothing is lost or double-counted, and that each block is attributed to the right filler.
    /// </summary>
    public class SocketSeparationTests
    {
        private static readonly D2DataFiles Data = D2DataFiles.LoadEmbedded();
        private static readonly TooltipEngine Engine = TooltipEngine.Embedded;

        private static readonly ItemTable Items = new ItemTable(
            Data.Weapons, Data.Armor, Data.Misc);

        private static Unit Filler(string code)
        {
            var unit = new Unit();
            unit.UnitType = 4;
            unit.Quality = 2;
            unit.ClassId = Items.ClassIdForCode(code);
            unit.Code = code;
            unit.ItemFlags = ItemRecordFlags.Identified;
            return unit;
        }

        /// <summary>A jewel with its OWN rolled affix, which is the case gems and runes are not.</summary>
        private static Unit Jewel()
        {
            Unit jewel = Filler("jew");
            jewel.Quality = 4;
            jewel.MagicPrefix[0] = RangedDamageAffix();

            var stats = new List<UnitStat>();
            stats.Add(new UnitStat { Id = 17, Value = 20 });
            stats.Add(new UnitStat { Id = 18, Value = 20 });

            var list = new UnitStatList();
            list.StateNo = 0;
            list.Flags = 64;
            list.Stats = stats;
            jewel.StatsLists = new List<UnitStatList> { list };
            return jewel;
        }

        private static int RangedDamageAffix()
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
                    if (table.GetString(row, "mod" + mod + "code").Trim() == "dmg%"
                        && table.GetInt(row, "mod" + mod + "min")
                            != table.GetInt(row, "mod" + mod + "max"))
                    {
                        return id;
                    }
                }
            }

            Assert.Fail("no ranged dmg% affix in shipped data");
            return 0;
        }

        private static Unit SocketedSword(params Unit[] fillers)
        {
            var host = new Unit();
            host.UnitType = 4;
            host.Quality = 2;
            host.ClassId = Items.ClassIdForCode("crs");
            host.ItemFlags = ItemRecordFlags.Identified | ItemRecordFlags.Socketed;
            host.ItemLevel = 60;

            var baseStats = new List<UnitStat>();
            baseStats.Add(new UnitStat { Id = 21, Value = 5 });
            baseStats.Add(new UnitStat { Id = 22, Value = 15 });
            baseStats.Add(new UnitStat { Id = 194, Value = fillers.Length });

            var list = new UnitStatList();
            list.StateNo = 0;
            list.Flags = 2147483648;
            list.Stats = baseStats;

            host.StatsLists = new List<UnitStatList> { list };
            host.Items = new List<Unit>(fillers);
            return host;
        }

        /// <summary>
        /// Colour is switched off with -1 rather than left at its grey default: these tests assert
        /// on the annotation's TEXT, and the default wraps every span in two colour markers.
        /// RangeAnnotationTests pins the default itself.
        /// </summary>
        private static TooltipOptions Separated(bool withRanges = false)
        {
            var options = new TooltipOptions();
            options.Sockets = SocketMode.Separated;

            if (withRanges)
            {
                options.Ranges = new RangeDisplay();
                options.Ranges.Color = -1;
            }
            return options;
        }

        private static string[] Section(Tooltip tooltip, ItemTooltipSection section)
        {
            return tooltip.Lines
                .Where(l => l.Section == section)
                .Select(l => (l.Text ?? string.Empty).TrimEnd('\n'))
                .ToArray();
        }

        [Fact]
        public void The_default_render_still_merges_them()
        {
            // The mode is opt-in, and off means the game's own behaviour: the fillers' mods appear
            // in the item's own block and the name gains "Gemmed".
            Unit host = SocketedSword(Filler("r08"), Filler("gpr"));
            Tooltip merged = Engine.Render(host);

            Assert.Empty(Section(merged, ItemTooltipSection.SocketContribution));
            Assert.Contains(
                Section(merged, ItemTooltipSection.Modifiers),
                l => l.Contains("fire damage"));
        }

        [Fact]
        public void Separating_moves_the_fillers_out_of_the_items_own_block()
        {
            Unit host = SocketedSword(Filler("r08"), Filler("gpr"));
            Tooltip separated = Engine.Render(host, null, Separated());

            // Gone from the item's own modifiers...
            Assert.DoesNotContain(
                Section(separated, ItemTooltipSection.Modifiers),
                l => l.Contains("fire damage"));

            // ...and present below it, one block per filler, each headed by the filler's name.
            string[] blocks = Section(separated, ItemTooltipSection.SocketContribution);

            Assert.Contains("Ral Rune", blocks);
            Assert.Contains("Perfect Ruby", blocks);
            Assert.Equal(2, blocks.Count(l => l.Contains("fire damage")));
        }

        [Fact]
        public void The_blocks_sit_below_the_item()
        {
            // Lines are in display order, so every socket block must come after every line of the
            // item's own tooltip — otherwise they read as part of it.
            Unit host = SocketedSword(Filler("r08"));
            Tooltip separated = Engine.Render(host, null, Separated());

            int lastOwn = -1;
            int firstBlock = int.MaxValue;

            for (int at = 0; at < separated.Lines.Count; ++at)
            {
                if (separated.Lines[at].Section == ItemTooltipSection.SocketContribution)
                {
                    firstBlock = System.Math.Min(firstBlock, at);
                }
                else
                {
                    lastOwn = at;
                }
            }

            Assert.True(firstBlock > lastOwn, "a socket block appeared above the item");
        }

        [Fact]
        public void Nothing_is_lost_by_separating_them()
        {
            // The union of the item's own modifiers and every socket block has to describe the same
            // stats the merged render describes. A filler whose mods vanished would be invisible.
            Unit host = SocketedSword(Filler("r08"), Filler("gpr"));

            string mergedFire = Section(Engine.Render(host), ItemTooltipSection.Modifiers)
                .Single(l => l.Contains("fire damage"));

            // Merged, the two fillers' fire damage adds up; separated, each shows its own half.
            Assert.Contains("20", mergedFire);

            string[] blocks = Section(
                Engine.Render(host, null, Separated()), ItemTooltipSection.SocketContribution);

            Assert.Contains(blocks, l => l.Contains("5-30 fire damage"));
            Assert.Contains(blocks, l => l.Contains("15-20 fire damage"));
        }

        [Fact]
        public void A_jewel_is_ranged_from_its_own_affixes()
        {
            // The case the mode exists for. A gem or rune has no stats of its own and no gems.txt
            // cell rolls, so its block carries no span — but a jewel's affixes DO roll, and its
            // block is where that span belongs.
            Unit host = SocketedSword(Filler("gpr"), Jewel());

            string[] blocks = Section(
                Engine.Render(host, null, Separated(withRanges: true)),
                ItemTooltipSection.SocketContribution);

            Assert.Contains(blocks, l => l.Contains("Enhanced Damage [10-20]"));

            // The gem's own line is present and deliberately unannotated.
            string gem = blocks.Single(l => l.Contains("15-20 fire damage"));
            Assert.DoesNotContain("[", gem);
        }

        [Fact]
        public void A_gem_block_carries_no_span_because_no_gem_cell_rolls()
        {
            // Ral, Ort and Thul are the only gems.txt rows whose min differs from their max, and
            // that pair is funcs 15/16 — the two ENDS of a damage range, both fixed. So a rune block
            // shows the damage and no span, which is the correct answer rather than a missing one.
            Unit host = SocketedSword(Filler("r08"));

            string[] blocks = Section(
                Engine.Render(host, null, Separated(withRanges: true)),
                ItemTooltipSection.SocketContribution);

            Assert.Contains(blocks, l => l.Contains("5-30 fire damage"));
            Assert.DoesNotContain(blocks, l => l.Contains("["));
        }

        [Fact]
        public void A_blank_row_separates_the_blocks()
        {
            // Three gems in a row read as one list without it.
            Unit host = SocketedSword(Filler("r08"), Filler("gpr"));
            string[] blocks = Section(
                Engine.Render(host, null, Separated()), ItemTooltipSection.SocketContribution);

            // TrimEnd in Section strips the terminator, so a gap row is the empty string.
            Assert.Equal(2, blocks.Count(l => l.Length == 0));
            Assert.Equal(string.Empty, blocks[0]);
        }

        /// <summary>
        /// A rare item with its own fire resist, socketed with a jewel that also gives fire resist.
        /// The three views have to disagree in exactly the right way, because each shows a different
        /// VALUE and the span must match the value beside it.
        /// </summary>
        private static Unit FireResCase(out int itemLow, out int itemHigh,
            out int jewelLow, out int jewelHigh)
        {
            var affixes = new MagicAffixTable(Data);
            var found = new List<int[]>();

            for (int id = 1; id <= affixes.RowCount; ++id)
            {
                TxtFile table;
                int row;
                if (!affixes.TryResolve(id, out table, out row)) { continue; }

                for (int mod = 1; mod <= 3; ++mod)
                {
                    if (table.GetString(row, "mod" + mod + "code").Trim() != "res-fire") { continue; }

                    int lo = table.GetInt(row, "mod" + mod + "min");
                    int hi = table.GetInt(row, "mod" + mod + "max");
                    if (lo != hi) { found.Add(new[] { id, lo, hi }); }
                }
            }

            Assert.True(found.Count >= 3, "need two distinct ranged res-fire affixes");

            int[] itemAffix = found[2];
            int[] jewelAffix = found[0];

            itemLow = itemAffix[1];
            itemHigh = itemAffix[2];
            jewelLow = jewelAffix[1];
            jewelHigh = jewelAffix[2];

            var jewel = new Unit();
            jewel.UnitType = 4;
            jewel.Quality = 4;
            jewel.ClassId = Items.ClassIdForCode("jew");
            jewel.Code = "jew";
            jewel.ItemFlags = ItemRecordFlags.Identified;
            jewel.MagicPrefix[0] = jewelAffix[0];

            var jl = new UnitStatList();
            jl.StateNo = 0;
            jl.Flags = 64;
            jl.Stats = new List<UnitStat> { new UnitStat { Id = 39, Value = jewelLow } };
            jewel.StatsLists = new List<UnitStatList> { jl };

            var host = new Unit();
            host.UnitType = 4;
            host.Quality = 6;
            host.ClassId = Items.ClassIdForCode("xhn");
            host.ItemFlags = ItemRecordFlags.Identified | ItemRecordFlags.Socketed;
            host.ItemLevel = 70;
            host.MagicPrefix[0] = itemAffix[0];

            var bl = new UnitStatList();
            bl.StateNo = 0;
            bl.Flags = 2147483648;
            bl.Stats = new List<UnitStat> { new UnitStat { Id = 194, Value = 1 } };

            var ml = new UnitStatList();
            ml.StateNo = 0;
            ml.Flags = 64;
            ml.Stats = new List<UnitStat> { new UnitStat { Id = 39, Value = itemLow } };

            host.StatsLists = new List<UnitStatList> { bl, ml };
            host.Items = new List<Unit> { jewel };
            return host;
        }

        [Fact]
        public void A_merged_line_gets_the_SUM_of_both_spans()
        {
            // The merged render draws ONE Fire Resist line holding item plus jewel, so its span has
            // to be the sum of the two. Annotating it with the item's span alone read as
            // "Fire Resist +28% [11-20]" — a number outside its own range.
            int itemLow, itemHigh, jewelLow, jewelHigh;
            Unit host = FireResCase(out itemLow, out itemHigh, out jewelLow, out jewelHigh);

            var options = new TooltipOptions();
            options.Ranges = new RangeDisplay();

            string line = Engine.Render(host, null, options).Lines
                .Select(l => l.Text ?? string.Empty)
                .Single(l => l.Contains("Fire Resist"));

            string expected = "[" + (itemLow + jewelLow) + "-" + (itemHigh + jewelHigh) + "]";
            Assert.Contains(expected, line);
        }

        [Fact]
        public void A_separated_line_gets_only_its_own_span()
        {
            // The mirror image: with the fillers moved out, the item's line shows its own value, so
            // its span must be its own too — and the jewel's block gets the jewel's.
            int itemLow, itemHigh, jewelLow, jewelHigh;
            Unit host = FireResCase(out itemLow, out itemHigh, out jewelLow, out jewelHigh);

            Tooltip tooltip = Engine.Render(host, null, Separated(withRanges: true));

            string own = Section(tooltip, ItemTooltipSection.Modifiers)
                .Single(l => l.Contains("Fire Resist"));

            string filler = Section(tooltip, ItemTooltipSection.SocketContribution)
                .Single(l => l.Contains("Fire Resist"));

            Assert.Contains("[" + itemLow + "-" + itemHigh + "]", own);
            Assert.Contains("[" + jewelLow + "-" + jewelHigh + "]", filler);
        }

        [Fact]
        public void A_breakdown_splits_the_spans_the_same_way()
        {
            // Breakdown had no ranges at all until now, and once wired the socket bucket showed the
            // jewel's VALUE against the item's SPAN — because reconstructing "just the fillers"
            // silently folded in the host's own sources.
            int itemLow, itemHigh, jewelLow, jewelHigh;
            Unit host = FireResCase(out itemLow, out itemHigh, out jewelLow, out jewelHigh);

            var options = new TooltipOptions();
            options.Ranges = new RangeDisplay();

            TooltipBreakdown b = Engine.Breakdown(host, null, options);

            string magic = b.Magic.Select(l => l.Text ?? string.Empty)
                .Single(l => l.Contains("Fire Resist"));

            string sockets = b.Sockets.Select(l => l.Text ?? string.Empty)
                .Single(l => l.Contains("Fire Resist"));

            Assert.Contains("[" + itemLow + "-" + itemHigh + "]", magic);
            Assert.Contains("[" + jewelLow + "-" + jewelHigh + "]", sockets);
        }

        [Fact]
        public void An_empty_socket_produces_no_block()
        {
            Unit host = SocketedSword();

            Assert.Empty(Section(
                Engine.Render(host, null, Separated()), ItemTooltipSection.SocketContribution));
        }
    }
}
