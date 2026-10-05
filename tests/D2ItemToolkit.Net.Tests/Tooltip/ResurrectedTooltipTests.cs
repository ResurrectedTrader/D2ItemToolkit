using Xunit;

namespace D2ItemToolkit.Tests
{
    /// <summary>
    /// ITEMS_GetFullDescription 0x1401d5200 and its writers, on the Reign of the Warlock tables.
    /// Every expected string is the traced D2R behaviour written out by hand — "~" stands for the
    /// colour escape so the markers stay readable.
    /// </summary>
    public class ResurrectedTooltipTests
    {
        private static readonly TooltipEngine Engine =
            TooltipEngine.ForVariant(GameVariant.ReignOfTheWarlock);

        private static string Colored(IUnit item, IUnit viewer = null, TooltipOptions options = null)
        {
            return Engine.Render(item, viewer, options).ColoredText.Replace("ÿc", "~");
        }

        private static Unit Item(string code, int quality = ItemQualityNo.Normal)
        {
            var item = new Unit();
            item.UnitType = 4;
            item.ClassId = Engine.Items.ClassIdForCode(code);
            item.Quality = quality;
            item.ItemFlags = ItemRecordFlags.Identified;
            item.ItemLevel = 50;
            Assert.True(item.ClassId >= 0, code);
            return item;
        }

        private static Unit Player(int classId, int strength = 100, int dexterity = 100, int level = 60)
        {
            var player = new Unit();
            player.UnitType = 0;
            player.ClassId = classId;
            player.StatsLists.Add(
                new UnitStatList(0, 0).Add(0, strength).Add(2, dexterity).Add(12, level));
            return player;
        }

        private static int MagicPrefix(string name)
        {
            TxtFile prefixes = Engine.Data.MagicPrefix;
            for (int row = 0; row < prefixes.RowCount; ++row)
            {
                if (prefixes.GetString(row, "Name") == name)
                {
                    // 1-based over [MagicSuffix][MagicPrefix][automagic].
                    return Engine.Data.MagicSuffix.RowCount + row + 1;
                }
            }

            return -1;
        }

        [Fact]
        public void The_variants_are_distinct_engines()
        {
            Assert.Equal(GameVariant.Lod114d, TooltipEngine.Embedded.Variant);
            Assert.Equal(GameVariant.Resurrected, TooltipEngine.ForVariant(GameVariant.Resurrected).Variant);
            Assert.Equal(GameVariant.ReignOfTheWarlock, Engine.Variant);
        }

        [Fact]
        public void A_one_prefix_magic_name_has_no_trailing_space_and_durability_no_marker()
        {
            // ItemNameMagicFormatPrefixOnly (0x1401587ff); ITEMDESC_Durability formats (cur, max)
            // with no colour-3 on an enhanced max (0x1401d051b).
            Unit shield = Item("lrg", ItemQualityNo.Magic);
            shield.MagicPrefix[0] = MagicPrefix("Sturdy");
            shield.StatsLists.Add(new UnitStatList(0, ItemStatListFlags.Extended)
                .Add(31, 120).Add(72, 40).Add(73, 62));
            shield.StatsLists.Add(new UnitStatList(0, ItemStatListFlags.Magic).Add(16, 30).Add(75, 20));

            Assert.Equal(
                "~3Sturdy Large Shield\n" +
                "~0Defense: ~3156\n" +
                "~0Chance to Block: ~330%\n" +
                "~0Smite Damage: 2 to 4\n" +
                "~0Durability: 40 of 74\n" +
                "~0Required Strength: 34\n" +
                "~0Required Level: 3\n" +
                "~3+30% Enhanced Defense\n" +
                "~3Increase Maximum Durability 20%",
                Colored(shield, Player(3)));
        }

        [Fact]
        public void A_belt_shows_its_extra_slots_over_the_default_belt()
        {
            // belts row 4 (light belt, 8) less row 2 (default, 4), "%+d".
            Unit belt = Item("vbl");
            belt.StatsLists.Add(new UnitStatList(0, ItemStatListFlags.Extended)
                .Add(31, 4).Add(72, 12).Add(73, 12));

            Assert.Equal(
                "~0Light Belt\n~0Defense: 4\n~0Belt Size: +4 Slots\n~0Durability: 12 of 12",
                Colored(belt, Player(0)));
        }

        [Fact]
        public void A_grimoire_names_its_class_and_reddens_for_another()
        {
            Unit grimoire = Item("wa1");
            grimoire.StatsLists.Add(new UnitStatList(0, ItemStatListFlags.Extended)
                .Add(31, 10).Add(72, 20).Add(73, 20));

            Assert.Contains("~1(Warlock Only)\n", Colored(grimoire, Player(1)));
            Assert.Contains("~0(Warlock Only)\n", Colored(grimoire, Player(7)));
        }

        [Fact]
        public void Quantity_carries_the_stack_size_in_the_hd_text_only()
        {
            Unit arrows = Item("aqv");
            arrows.StatsLists.Add(new UnitStatList(0, ItemStatListFlags.Extended).Add(70, 250));

            Assert.Equal("~0Arrows\n~0Quantity: 250 of 500", Colored(arrows, Player(0)));

            TooltipEngine legacy = TooltipEngine.ForVariant(
                GameVariant.ReignOfTheWarlock, new ResurrectedTextOptions { LegacyGraphics = true });
            Assert.Equal(
                "~0Arrows\n~0Quantity: 250",
                legacy.Render(arrows, Player(0)).ColoredText.Replace("ÿc", "~"));
        }

        [Fact]
        public void Runes_and_event_items_take_the_new_name_colours()
        {
            Assert.StartsWith("~JEl Rune\n", Colored(Item("r01"), Player(0)));
            Assert.Equal("~LKey of Terror", Colored(Item("pk1"), Player(0)));
        }

        [Fact]
        public void The_cube_speaks_through_its_spelldesc_with_quest_colour_14()
        {
            // No QuestUsage section: the usage line is mode-1 spelldesc with spelldesccolor 4.
            Assert.Equal(
                "~0~>Horadric Cube\n~0~4Right Click to Open",
                Colored(Item("box"), Player(0)));
        }

        [Fact]
        public void Potions_scale_by_the_charstats_percent_and_rejuvenation_uses_mode_4()
        {
            Assert.Equal("~0Super Healing Potion\n~0Points: 320", Colored(Item("hp5"), Player(7)));
            Assert.Equal("~0Super Healing Potion\n~0Points: 640", Colored(Item("hp5"), Player(4)));
            Assert.Equal(
                "~0Full Rejuvenation Potion\n~0Heals 100% Life and Mana",
                Colored(Item("rvl"), Player(7)));
        }

        [Fact]
        public void A_throw_line_marks_only_the_first_number_and_only_when_modified()
        {
            Unit javelin = Item("jav");
            javelin.StatsLists.Add(new UnitStatList(0, ItemStatListFlags.Extended)
                .Add(21, 6).Add(22, 14).Add(159, 6).Add(160, 14).Add(70, 40).Add(72, 0).Add(73, 0));
            javelin.StatsLists.Add(new UnitStatList(0, ItemStatListFlags.Magic).Add(160, 5));

            // 1.14d marked both numbers ("~0Throw Damage: ~36 to ~319"); D2R splices one marker.
            // The line carries its own ~0, so the display needs no re-anchor in front of it.
            Assert.Contains("\n~0Throw Damage: ~36 to 19\n", Colored(javelin, Player(0)));
        }

        [Fact]
        public void A_worldstone_shard_is_red_unless_desecrated_zones_run_in_hell()
        {
            Unit shard = Item("xa1");

            // EventItem gives the section colour 28; the failed condition prepends red inside it.
            Assert.StartsWith("~L~1", Colored(shard, Player(0)));

            var hell = new TooltipOptions { Difficulty = 2, DesecratedZonesEnabled = true };
            string text = Colored(shard, Player(0), hell);
            Assert.StartsWith("~L", text);
            Assert.False(text.StartsWith("~L~1", System.StringComparison.Ordinal));
        }

        [Fact]
        public void Legacy_text_keeps_the_hd_ids_the_tables_were_loaded_with()
        {
            // DATATBLS_LoadAllTxts runs at startup (0x140063844), before the panel manager's update
            // first writes g_IsRunningLecgacyGfx (only writer, 0x14061d604), so spelldescstr resolves
            // through the HD table (0x1401f88f1) to 27574. The legacy table refused that HD entry
            // because legacy holds the key under 27446, so LANG_SpellDesc (0x1401d2da9) misses and
            // gets g_pStringTable — the HD table's strMissingString, loaded last (0x140477ac5).
            Unit shard = Item("xa1");

            TooltipEngine english = TooltipEngine.ForVariant(
                GameVariant.ReignOfTheWarlock, new ResurrectedTextOptions { LegacyGraphics = true });
            Assert.Equal(
                "~L~1Western Worldstone Shard\n~0~8Missing string",
                english.Render(shard, Player(0)).ColoredText.Replace("ÿc", "~"));

            TooltipEngine french = TooltipEngine.ForVariant(
                GameVariant.ReignOfTheWarlock,
                new ResurrectedTextOptions { LegacyGraphics = true, Language = "frFR" });
            Assert.Equal(
                "~L~1Fragment occidental de la pierre-monde\n~0~8Ligne manquante",
                french.Render(shard, Player(0)).ColoredText.Replace("ÿc", "~"));

            TooltipEngine russian = TooltipEngine.ForVariant(
                GameVariant.ReignOfTheWarlock,
                new ResurrectedTextOptions { LegacyGraphics = true, Language = "ruRU" });
            Assert.Equal(
                "~L~1Western Worldstone Shard\n~0~8Отсутствует строка",
                russian.Render(shard, Player(0)).ColoredText.Replace("ÿc", "~"));

            // HD is unaffected.
            Assert.Equal(
                "~L~1Western Worldstone Shard\n~0~8Right Click to terrorize Act 1",
                Colored(shard, Player(0)));
        }

        [Theory]
        [InlineData("enUS", "Missing string")]
        [InlineData("deDE", "Missing string")]
        [InlineData("esES", "Missing string")]
        [InlineData("frFR", "Ligne manquante")]
        [InlineData("itIT", "Stringa mancante")]
        [InlineData("koKR", "없는 문자열")]
        [InlineData("plPL", "Missing string")]
        [InlineData("ruRU", "Отсутствует строка")]
        [InlineData("zhCN", "丢失字符串")]
        [InlineData("zhTW", "Missing string")]
        [InlineData("esMX", "Texto faltante")]
        [InlineData("jaJP", "文字列が見つかりません")]
        [InlineData("ptBR", "String ausente")]
        public void Every_legacy_locale_misses_with_the_hd_tables_text(string language, string missing)
        {
            // The HD load never forces enUS (only the legacy one does, 0x140476dbe), so the
            // fallback is the HD strMissingString in the locale's own column.
            TooltipEngine legacy = TooltipEngine.ForVariant(
                GameVariant.ReignOfTheWarlock,
                new ResurrectedTextOptions { LegacyGraphics = true, Language = language });
            foreach (string code in new[] { "xa1", "xa2", "xa3", "xa4", "xa5" })
            {
                string text = legacy.Render(Item(code), Player(0)).ColoredText.Replace("ÿc", "~");
                Assert.EndsWith("\n~0~8" + missing, text);
            }
        }

        [Fact]
        public void Legacy_skill_names_resolve_by_their_hd_id()
        {
            // skilldesc `str name` BaneHexName is HD id 27448; legacy holds 27448 as
            // UseTerrorTokenAct2, so the legacy table refused BaneHexName (0x140476f85).
            TooltipEngine legacy = TooltipEngine.ForVariant(
                GameVariant.ReignOfTheWarlock, new ResurrectedTextOptions { LegacyGraphics = true });
            Unit ring = Item("rin", ItemQualityNo.Magic);
            ring.StatsLists.Add(new UnitStatList(0, ItemStatListFlags.Magic).Add(107, 3, 385));

            Assert.Contains(
                "~3+3 to Right Click to terrorize Act 2 (Warlock Only)",
                legacy.Render(ring, Player(7)).ColoredText.Replace("ÿc", "~"));
        }

        [Fact]
        public void Defense_and_damage_include_the_viewers_level_scaled_stats()
        {
            // ITEMDESC_Defense 0x1401d1d00 and ITEMDESC_GetMinMaxStats 0x1401d07f0 attach the item
            // to the viewer before reading the total; ops 4/5 then add level * value >> 3 to the
            // target (0x14020c57d), and the base-vs-total test marks the number.
            Unit armor = Item("qui", ItemQualityNo.Magic);
            armor.MagicPrefix[0] = MagicPrefix("Paleocene");
            armor.StatsLists.Add(new UnitStatList(0, ItemStatListFlags.Extended)
                .Add(31, 10).Add(72, 20).Add(73, 20));
            armor.StatsLists.Add(new UnitStatList(0, ItemStatListFlags.Magic).Add(214, 24));

            Assert.Equal(
                "~3Faithful Quilted Armor\n" +
                "~0Defense: ~3130\n" +
                "~0Durability: 20 of 20\n" +
                "~0Required Strength: 12\n" +
                "~0Required Level: 22\n" +
                "~3+120 Defense (Based on Character Level)",
                Colored(armor, Player(0, level: 40)));

            Unit axe = Item("hax", ItemQualityNo.Magic);
            axe.MagicPrefix[0] = MagicPrefix("Gritty");
            axe.StatsLists.Add(new UnitStatList(0, ItemStatListFlags.Extended)
                .Add(21, 3).Add(22, 6).Add(72, 28).Add(73, 28));
            axe.StatsLists.Add(new UnitStatList(0, ItemStatListFlags.Magic).Add(218, 6));

            Assert.Equal(
                "~3Grinding Hand Axe\n" +
                "~0One-Hand Damage: ~33 to 36\n" +
                "~0Durability: 28 of 28\n" +
                "~0Required Level: 37\n" +
                "~0Axe Class - Fast Attack Speed\n" +
                "~3+30 to Maximum Damage (Based on Character Level)",
                Colored(axe, Player(0, level: 40)));
        }

        [Fact]
        public void Op_5_takes_a_percent_of_the_pre_op_defense()
        {
            // ac%/lvl 12 at level 40: (40 * 12) >> 3 = 60%, of the pre-op 10 = 6.
            Unit armor = Item("qui");
            armor.StatsLists.Add(new UnitStatList(0, ItemStatListFlags.Extended)
                .Add(31, 10).Add(72, 20).Add(73, 20));
            armor.StatsLists.Add(new UnitStatList(0, ItemStatListFlags.Magic).Add(215, 12));

            Assert.Contains("~0Defense: ~316\n", Colored(armor, Player(0, level: 40)));
        }
    }
}
