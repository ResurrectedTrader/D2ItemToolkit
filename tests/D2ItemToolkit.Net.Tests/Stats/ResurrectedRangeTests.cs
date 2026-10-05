using System.Linq;
using Xunit;

namespace D2ItemToolkit.Tests
{
    /// <summary>
    /// The D2R spawn-order facts the roll-range reconstruction depends on, each found by the ranges
    /// bug hunt and traced in d2r.exe.
    /// </summary>
    public class ResurrectedRangeTests
    {
        private static readonly TooltipEngine Rotw = TooltipEngine.ForVariant(GameVariant.ReignOfTheWarlock);

        private static ItemRollRanges Ranges(string json)
        {
            return Rotw.Ranges(UnitJson.Read(json));
        }

        private static string Spans(ItemRollRanges ranges)
        {
            return string.Join("|", ranges.Stats.Select(r =>
                r.StatId + (r.Layer == 0 ? string.Empty : "@" + r.Layer) + " " + r.Low + ".." + r.High));
        }

        private static string Defense(ItemRollRanges ranges)
        {
            RolledStatRange defense = ranges.Stats.Single(r => r.StatId == 31 && r.Layer == 0);
            return defense.Low + ".." + defense.High;
        }

        internal const string EtherealVipermagi =
            "{\"unitType\":4,\"classId\":360,\"quality\":7,\"itemFlags\":4194320,\"format\":100,\"fileIndex\":210,\"itemLevel\":60,"
            + "\"statsLists\":[{\"stateNo\":0,\"flags\":2147483648,\"stats\":[{\"id\":31,\"value\":190},{\"id\":73,\"value\":19},{\"id\":72,\"value\":19}]},"
            + "{\"stateNo\":0,\"flags\":64,\"stats\":[{\"id\":16,\"value\":120},{\"id\":39,\"value\":30},{\"id\":41,\"value\":30},{\"id\":43,\"value\":30},"
            + "{\"id\":45,\"value\":30},{\"id\":105,\"value\":30},{\"id\":35,\"value\":10},{\"id\":127,\"value\":1}]}]}";

        internal const string EtherealSuperiorAncientArmor =
            "{\"unitType\":4,\"classId\":326,\"quality\":3,\"itemFlags\":4194320,\"format\":100,\"fileIndex\":2,\"itemLevel\":60,"
            + "\"statsLists\":[{\"stateNo\":0,\"flags\":2147483648,\"stats\":[{\"id\":31,\"value\":351},{\"id\":73,\"value\":31},{\"id\":72,\"value\":31}]},"
            + "{\"stateNo\":0,\"flags\":64,\"stats\":[{\"id\":16,\"value\":10}]}]}";

        internal static string Fortitude(int flags, int baseDefense)
        {
            return "{\"unitType\":4,\"classId\":443,\"quality\":2,\"itemFlags\":" + flags
                + ",\"format\":100,\"fileIndex\":-1,\"itemLevel\":60,\"magicPrefix\":[20547,0,0],"
                + "\"statsLists\":[{\"stateNo\":0,\"flags\":2147483648,\"stats\":[{\"id\":31,\"value\":" + baseDefense
                + "},{\"id\":73,\"value\":60},{\"id\":72,\"value\":60},{\"id\":194,\"value\":4}]},"
                + "{\"stateNo\":171,\"flags\":64,\"stats\":[{\"id\":16,\"value\":200}]}]}";
        }

        internal const string PulInGrandCrown =
            "{\"unitType\":4,\"classId\":357,\"quality\":2,\"itemFlags\":2064,\"format\":100,\"fileIndex\":-1,\"itemLevel\":60,"
            + "\"statsLists\":[{\"stateNo\":0,\"flags\":2147483648,\"stats\":[{\"id\":31,\"value\":90},{\"id\":194,\"value\":1}]}],"
            + "\"items\":[{\"unitType\":4,\"classId\":645,\"quality\":2,\"itemFlags\":16,\"format\":100,\"statsLists\":[]}]}";

        internal const string SuperiorAncientArmorDurability =
            "{\"unitType\":4,\"classId\":326,\"quality\":3,\"itemFlags\":16,\"format\":100,\"fileIndex\":4,\"itemLevel\":60,"
            + "\"statsLists\":[{\"stateNo\":0,\"flags\":2147483648,\"stats\":[{\"id\":31,\"value\":220},{\"id\":73,\"value\":60},{\"id\":72,\"value\":60}]},"
            + "{\"stateNo\":0,\"flags\":64,\"stats\":[{\"id\":75,\"value\":12}]}]}";

        internal const string SuperiorJavelinAttackRating =
            "{\"unitType\":4,\"classId\":47,\"quality\":3,\"itemFlags\":16,\"format\":100,\"fileIndex\":0,\"itemLevel\":60,"
            + "\"statsLists\":[{\"stateNo\":0,\"flags\":2147483648,\"stats\":[]},{\"stateNo\":0,\"flags\":64,\"stats\":[{\"id\":19,\"value\":2}]}]}";

        internal const string IrathasCollar =
            "{\"unitType\":4,\"classId\":535,\"quality\":5,\"itemFlags\":16,\"format\":100,\"fileIndex\":9,\"itemLevel\":60,"
            + "\"statsLists\":[{\"stateNo\":0,\"flags\":2147483648,\"stats\":[]},"
            + "{\"stateNo\":0,\"flags\":64,\"stats\":[{\"id\":45,\"value\":30},{\"id\":110,\"value\":75}]},"
            + "{\"stateNo\":165,\"flags\":8256,\"stats\":[{\"id\":39,\"value\":15},{\"id\":41,\"value\":15},{\"id\":43,\"value\":15},{\"id\":45,\"value\":15}]}]}";

        internal const string ImmortalKingsForge =
            "{\"unitType\":4,\"classId\":384,\"quality\":5,\"itemFlags\":16,\"format\":100,\"fileIndex\":73,\"itemLevel\":60,"
            + "\"statsLists\":[{\"stateNo\":0,\"flags\":2147483648,\"stats\":[{\"id\":31,\"value\":50},{\"id\":73,\"value\":24},{\"id\":72,\"value\":24}]},"
            + "{\"stateNo\":0,\"flags\":64,\"stats\":[{\"id\":31,\"value\":65},{\"id\":0,\"value\":20},{\"id\":2,\"value\":20},{\"id\":201,\"layer\":2436,\"value\":12}]},"
            + "{\"stateNo\":165,\"flags\":8256,\"stats\":[{\"id\":93,\"value\":25}]},"
            + "{\"stateNo\":166,\"flags\":8256,\"stats\":[{\"id\":31,\"value\":120}]},"
            + "{\"stateNo\":167,\"flags\":8256,\"stats\":[{\"id\":60,\"value\":10}]},"
            + "{\"stateNo\":168,\"flags\":8256,\"stats\":[{\"id\":62,\"value\":10}]},"
            + "{\"stateNo\":169,\"flags\":8256,\"stats\":[{\"id\":134,\"value\":2}]}]}";

        [Fact]
        public void An_ethereal_armour_with_spawn_ac_percent_scales_the_maximised_base()
        {
            // The unique's props run first (0x1402e0fc3); func 2 sets the base to maxac+1
            // (0x140283f88), and only then ITEMS_MakeEthereal (0x1402e14c0) scales it by 3/2:
            // 127 * 3 / 2 = 190, plus 120% = 418.
            ItemRollRanges ranges = Ranges(EtherealVipermagi);
            Assert.Equal("418..418", Defense(ranges));
            Assert.Empty(ranges.OutOfRange);
        }

        [Fact]
        public void An_ethereal_superior_armour_scales_its_maximised_base_too()
        {
            // Row 2 `ac%` 5..15 over trunc(234 * 3 / 2) = 351.
            ItemRollRanges ranges = Ranges(EtherealSuperiorAncientArmor);
            Assert.Equal("368..403", Defense(ranges));
            Assert.Empty(ranges.OutOfRange);
        }

        [Theory]
        [InlineData(67110928, 500, "1230..1572")]   // Fortitude, Archon Plate
        [InlineData(71305232, 750, "1845..2358")]   // Fortitude, ethereal Archon Plate
        public void A_runewords_ac_percent_does_not_maximise_the_base(int flags, int baseDefense, string expected)
        {
            // ITEMMODS_UpdateRuneword assigns with pItem = the rune (0x1402d39ec, 0x140286f5c), so
            // ITEMS_SetBaseStatValue (0x140287afd) tests the rune, not the armour.
            ItemRollRanges ranges = Ranges(Fortitude(flags, baseDefense));
            Assert.Equal(expected, Defense(ranges));
            Assert.DoesNotContain(31, ranges.OutOfRange);
        }

        [Fact]
        public void A_socketed_pul_does_not_maximise_the_hosts_base()
        {
            // ITEMS_ApplyGemOrRuneAndRefreshSets passes the filler as pItem (0x1400a6772).
            Assert.Equal("101..146", Defense(Ranges(PulInGrandCrown)));
        }

        [Theory]
        [InlineData(SuperiorAncientArmorDurability, "31 218..233|75 10..15")]
        [InlineData(SuperiorJavelinAttackRating, "19 1..3")]
        public void A_superior_item_rolls_only_the_qualityitems_row_its_file_index_names(string json, string expected)
        {
            // LOD114d_sub_5C2970 stores the drawn row with ITEMS_SetFileIndex (0x140381dc3) and
            // applies only that row's mods (0x140381dd2-0x140381e23).
            ItemRollRanges ranges = Ranges(json);
            Assert.Equal(expected, Spans(ranges));
            Assert.Empty(ranges.OutOfRange);
        }

        [Fact]
        public void A_set_items_tiered_aprops_are_not_summed_into_its_own_stats()
        {
            // sub_1402866E0 case 4 puts aprops in states 165..169 when `add func` is set
            // (0x140286a18-0x140286a33).
            ItemRollRanges ranges = Ranges(IrathasCollar);
            Assert.Equal("45 30..30|110 75..75", Spans(ranges));
            Assert.Empty(ranges.OutOfRange);
        }

        [Fact]
        public void An_inactive_tier_ac_does_not_join_the_defense_span()
        {
            ItemRollRanges ranges = Ranges(ImmortalKingsForge);
            Assert.Equal("0 20..20|2 20..20|31 108..118|201@2436 12..12", Spans(ranges));
            Assert.Empty(ranges.OutOfRange);
        }

        internal const string ImmortalKingsForgeEarned =
            "{\"unitType\":4,\"classId\":384,\"quality\":5,\"itemFlags\":16,\"format\":100,\"fileIndex\":73,\"itemLevel\":60,"
            + "\"statsLists\":[{\"stateNo\":0,\"flags\":2147483648,\"stats\":[{\"id\":31,\"value\":50},{\"id\":73,\"value\":24},{\"id\":72,\"value\":24}]},"
            + "{\"stateNo\":0,\"flags\":64,\"stats\":[{\"id\":31,\"value\":65},{\"id\":0,\"value\":20},{\"id\":2,\"value\":20},{\"id\":201,\"layer\":2436,\"value\":12}]},"
            + "{\"stateNo\":165,\"flags\":64,\"stats\":[{\"id\":93,\"value\":25}]},"
            + "{\"stateNo\":166,\"flags\":64,\"stats\":[{\"id\":31,\"value\":120}]},"
            + "{\"stateNo\":167,\"flags\":8256,\"stats\":[{\"id\":60,\"value\":10}]},"
            + "{\"stateNo\":168,\"flags\":8256,\"stats\":[{\"id\":62,\"value\":10}]},"
            + "{\"stateNo\":169,\"flags\":8256,\"stats\":[{\"id\":134,\"value\":2}]}]}";

        [Fact]
        public void An_earned_tiers_ac_joins_the_defense_span()
        {
            // sub_1402866E0 puts aprop2a `ac` 120 in state 166 (0x140286a33); once earned the list
            // drops STATLIST_SET (0x14028ab90) and the Defense line draws 50 + 65 + 120.
            ItemRollRanges ranges = Ranges(ImmortalKingsForgeEarned);
            Assert.Equal("228..238", Defense(ranges));
            Assert.Empty(ranges.OutOfRange);

            Tooltip tooltip = Rotw.Render(UnitJson.Read(ImmortalKingsForgeEarned), null,
                new TooltipOptions { Ranges = new RangeDisplay { Color = -1 } });
            Assert.Equal("Defense: ÿc3235 [228-238]\n",
                tooltip.Lines.Single(l => l.Section == ItemTooltipSection.ArmorClass).Text);
        }

        internal const string Stormshield =
            "{\"unitType\":4,\"classId\":447,\"quality\":7,\"itemFlags\":16,\"format\":100,\"fileIndex\":253,\"itemLevel\":80,"
            + "\"statsLists\":[{\"stateNo\":0,\"flags\":2147483648,\"stats\":[{\"id\":31,\"value\":140},{\"id\":73,\"value\":86},{\"id\":72,\"value\":86}]},"
            + "{\"stateNo\":0,\"flags\":64,\"stats\":[{\"id\":214,\"value\":30},{\"id\":36,\"value\":35},{\"id\":0,\"value\":30},{\"id\":152,\"value\":1},"
            + "{\"id\":20,\"value\":25},{\"id\":41,\"value\":25},{\"id\":43,\"value\":60},{\"id\":128,\"value\":10}]}]}";

        internal const string Level80 =
            "{\"unitType\":0,\"classId\":1,\"statsLists\":[{\"stateNo\":0,\"flags\":2147483648,\"stats\":"
            + "[{\"id\":12,\"value\":80},{\"id\":0,\"value\":200},{\"id\":2,\"value\":100}]}]}";

        [Fact]
        public void The_defense_span_includes_the_viewers_level_scaled_defense()
        {
            // ITEMDESC_Defense attaches the item to the viewer (0x1401d1df1), re-running op 4 for
            // `ac/lvl`: (30 * 80) >> 3 = 300 on top of the 133..148 roll.
            Tooltip tooltip = Rotw.Render(UnitJson.Read(Stormshield), UnitJson.Read(Level80),
                new TooltipOptions { Ranges = new RangeDisplay { Color = -1 } });
            Assert.Equal("Defense: ÿc3440 [433-448]\n",
                tooltip.Lines.Single(l => l.Section == ItemTooltipSection.ArmorClass).Text);
            Assert.Equal("133..148", Defense(Ranges(Stormshield)));

            ItemRollRanges forViewer = Rotw.RangesForViewer(UnitJson.Read(Stormshield), UnitJson.Read(Level80));
            Assert.Equal("433..448", Defense(forViewer));
            Assert.Empty(forViewer.OutOfRange);
        }

        private static string VipermagiWithUm(string runeLists)
        {
            return "{\"unitType\":4,\"classId\":360,\"quality\":7,\"itemFlags\":2064,\"format\":100,\"fileIndex\":210,\"itemLevel\":60,"
                + "\"statsLists\":[{\"stateNo\":0,\"flags\":2147483648,\"stats\":[{\"id\":31,\"value\":127},{\"id\":73,\"value\":38},{\"id\":72,\"value\":38},{\"id\":194,\"value\":1}]},"
                + "{\"stateNo\":0,\"flags\":64,\"stats\":[{\"id\":16,\"value\":120},{\"id\":39,\"value\":30},{\"id\":41,\"value\":30},{\"id\":43,\"value\":30},"
                + "{\"id\":45,\"value\":30},{\"id\":105,\"value\":30},{\"id\":35,\"value\":10},{\"id\":127,\"value\":1}]}],"
                + "\"items\":[{\"unitType\":4,\"classId\":646,\"quality\":2,\"itemFlags\":16,\"format\":100,\"statsLists\":" + runeLists + "}]}";
        }

        internal static readonly string VipermagiWithUmClient = VipermagiWithUm("[]");

        internal static readonly string VipermagiWithUmServer = VipermagiWithUm(
            "[{\"stateNo\":0,\"flags\":64,\"stats\":[{\"id\":39,\"value\":15},{\"id\":41,\"value\":15},{\"id\":43,\"value\":15},{\"id\":45,\"value\":15}]}]");

        [Fact]
        public void A_fillers_contribution_is_in_the_comparand()
        {
            // The span counts the Um (gems.txt helm/armor res-all 15); so must what it is checked against.
            Assert.Equal(646, Rotw.Items.ClassIdForCode("r22"));
            Assert.Empty(Ranges(VipermagiWithUmClient).OutOfRange);
            Assert.Empty(Ranges(PulInGrandCrown).OutOfRange);
        }

        [Fact]
        public void A_server_captured_rune_is_ranged_from_gems_txt()
        {
            // ITEMS_ApplyGemOrRuneAndRefreshSets assigns to the filler (0x1400a6772), so a server
            // capture carries the rune's list; its roll is still gems.txt's.
            ItemRollRanges ranges = Ranges(VipermagiWithUmServer);
            Assert.Contains("39 35..50", Spans(ranges));
            Assert.Empty(ranges.OutOfRange);

            Tooltip tooltip = Rotw.Render(UnitJson.Read(VipermagiWithUmServer), null,
                new TooltipOptions { Ranges = new RangeDisplay { Color = -1 } });
            Assert.Contains(tooltip.Lines, l => l.Text == "All Resistances +45 [35-50]\n");
        }

        internal const string ClassicMilabregasRobe =
            "{\"unitType\":4,\"classId\":326,\"quality\":5,\"itemFlags\":16,\"format\":0,\"fileIndex\":24,\"itemLevel\":30,"
            + "\"statsLists\":[{\"stateNo\":0,\"flags\":2147483648,\"stats\":[{\"id\":31,\"value\":225},{\"id\":73,\"value\":60},{\"id\":72,\"value\":60}]},"
            + "{\"stateNo\":0,\"flags\":64,\"stats\":[{\"id\":78,\"value\":3},{\"id\":34,\"value\":2}]}]}";

        [Fact]
        public void A_classic_set_item_gets_no_aprops_and_so_no_maximised_base()
        {
            // sub_1402866E0 case 4: format 0 applies props 1..2 only (0x140286911); the aprop `ac%`
            // that would maximise the base is never assigned.
            ItemRollRanges ranges = Ranges(ClassicMilabregasRobe);
            Assert.Equal("31 218..233|34 2..2|78 3..3", Spans(ranges));
            Assert.Empty(ranges.OutOfRange);
        }

        internal static string UpgradedVipermagi(int flags, int baseDefense)
        {
            return "{\"unitType\":4,\"classId\":430,\"quality\":7,\"itemFlags\":" + flags
                + ",\"format\":100,\"fileIndex\":210,\"itemLevel\":60,\"statsLists\":[{\"stateNo\":0,\"flags\":2147483648,"
                + "\"stats\":[{\"id\":31,\"value\":" + baseDefense + "},{\"id\":73,\"value\":36},{\"id\":72,\"value\":36}]},"
                + "{\"stateNo\":0,\"flags\":64,\"stats\":[{\"id\":16,\"value\":120},{\"id\":39,\"value\":30},{\"id\":41,\"value\":30},"
                + "{\"id\":43,\"value\":30},{\"id\":45,\"value\":30},{\"id\":105,\"value\":30},{\"id\":35,\"value\":10},{\"id\":127,\"value\":1}]}]}";
        }

        [Theory]
        [InlineData(16, 400, "800..1034")]
        [InlineData(4194320, 600, "1201..1551")]
        public void A_cube_upgraded_unique_rerolls_its_base_unmaximised(int flags, int baseDefense, string expected)
        {
            // PLRTRADE_CreateCubeOutputs keeps the item for a `mod` output and re-runs InitItemStats
            // (0x1403c0251), whose armour roll is a plain minac..maxac store (0x1402de731); only
            // ApplyEthereality follows (0x1403c027b).
            ItemRollRanges ranges = Ranges(UpgradedVipermagi(flags, baseDefense));
            Assert.Equal(expected, Defense(ranges));
            Assert.Empty(ranges.OutOfRange);
        }

        // Wyrmhide (uea 364..470, normcode lea) with magicprefix row 6, Holy `ac%` 81..100.
        internal const string RareWyrmhide =
            "{\"unitType\":4,\"classId\":430,\"quality\":6,\"itemFlags\":16,\"format\":100,\"fileIndex\":-1,\"itemLevel\":60,"
            + "\"magicPrefix\":[792,0,0],\"statsLists\":[{\"stateNo\":0,\"flags\":2147483648,"
            + "\"stats\":[{\"id\":31,\"value\":471},{\"id\":73,\"value\":36},{\"id\":72,\"value\":36}]},"
            + "{\"stateNo\":0,\"flags\":64,\"stats\":[{\"id\":16,\"value\":90}]}]}";

        [Fact]
        public void A_rare_on_an_upgradable_base_spans_both_the_maximised_and_the_rerolled_base()
        {
            // Native: maxac + 1 = 471; upgraded: 364..470. 364 + 81% = 658, 471 + 100% = 942.
            Assert.Equal(785, Rotw.Data.MagicSuffix.RowCount);
            ItemRollRanges ranges = Ranges(RareWyrmhide);
            Assert.Equal("16 81..100|31 658..942", Spans(ranges));
            Assert.Empty(ranges.OutOfRange);
        }

        [Fact]
        public void Set_bonus_codes_do_not_depend_on_a_set_item_having_been_rendered()
        {
            TooltipEngine fresh = TooltipEngine.FromData(Rotw.Data);
            Unit collar = UnitJson.Read(IrathasCollar);
            string before = Spans(fresh.Ranges(collar, new[] { 3 }));
            fresh.Render(collar);

            Assert.Equal(before, Spans(fresh.Ranges(collar, new[] { 3 })));
        }
    }
}
