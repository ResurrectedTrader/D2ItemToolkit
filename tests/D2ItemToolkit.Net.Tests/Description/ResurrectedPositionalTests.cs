using System.Collections.Generic;
using Xunit;

namespace D2ItemToolkit.Tests
{
    /// <summary>
    /// The second verification round's findings. The positional descfunc wrappers
    /// (sub_14060cb00 / cea0 / de20 / e430). The localised RuneQuote at both ends of the rune
    /// letters (0x1401d2f44 / 0x1401d3034). And ITEMDESC_GetMinMaxStats' MAX(min, max)
    /// (0x1401d0987) on the unclamped damage lines.
    /// </summary>
    public class ResurrectedPositionalTests
    {
        private static readonly Dictionary<string, TooltipEngine> Engines = new Dictionary<string, TooltipEngine>();

        private static TooltipEngine Engine(string language = "enUS", bool legacy = false)
        {
            string key = language + legacy;
            TooltipEngine engine;
            if (!Engines.TryGetValue(key, out engine))
            {
                engine = TooltipEngine.ForVariant(
                    GameVariant.ReignOfTheWarlock,
                    new ResurrectedTextOptions { Language = language, LegacyGraphics = legacy });
                Engines[key] = engine;
            }

            return engine;
        }

        private static Unit Viewer(int classId)
        {
            var viewer = new Unit();
            viewer.UnitType = 0;
            viewer.ClassId = classId;
            viewer.StatsLists.Add(new UnitStatList(0, 0).Add(0, 200).Add(2, 200).Add(12, 90));
            return viewer;
        }

        private static string[] Lines(TooltipEngine engine, Unit item, int viewerClass = 1)
        {
            return engine.Render(item, Viewer(viewerClass)).Text.Split('\n');
        }

        private static string[] Modifier(string language, int stat, int layer, int value)
        {
            TooltipEngine engine = Engine(language);
            var ring = new Unit();
            ring.UnitType = 4;
            ring.ClassId = engine.Items.ClassIdForCode("rin");
            ring.Quality = ItemQualityNo.Rare;
            ring.ItemFlags = ItemRecordFlags.Identified;
            ring.StatsLists.Add(new UnitStatList(0, ItemStatListFlags.Magic).Add(stat, value, layer));
            return Lines(engine, ring);
        }

        private static int Skill(string name)
        {
            return Engine().Data.SkillRows.FindRow("skill", name);
        }

        // ---- the wrapper itself ------------------------------------------------------------------

        [Theory]
        [InlineData("%+0 to %1", "+3 to Fire Ball")]              // a flag keeps the conversion open
        [InlineData("%1 gets %0", "Fire Ball gets 3")]           // arguments follow marker order
        [InlineData("%0 only", "")]                              // %0 without %1 writes nothing
        [InlineData("only %1", "")]                              // %1 without %0 writes nothing
        [InlineData("plain %d %s", "plain 3 Fire Ball")]         // no markers: plain printf
        [InlineData("%0%% %1 %0", "3% Fire Ball ")]             // only the first copy is rewritten; the`n        //   second is a "%0" flag still open at the NUL, which writes nothing (0x140b477d5)
        public void The_two_argument_wrapper(string format, string expected)
        {
            Assert.Equal(expected, CFormat.PositionalWrapper(format, "ds", false, 3, "Fire Ball"));
        }

        [Theory]
        [InlineData("%1 %0 (%2/%3)", "Teleport 1 (20/30)")]     // only %0 and %1 may swap
        [InlineData("%0 %2 %1 %3", "")]                          // %2 must follow %0 and %1
        [InlineData("%0 %1 %3 %2", "")]                          // %3 must follow %2
        public void The_charges_wrapper_insists_on_its_order(string format, string expected)
        {
            Assert.Equal(expected, CFormat.PositionalWrapper(format, "dsdd", true, 1, "Teleport", 20, 30));
        }

        // ---- the descfuncs that take it ----------------------------------------------------------

        [Fact]
        public void An_aura_in_spanish()
        {
            Assert.Contains(
                "Aura Meditación de nivel 12 si está equipado",
                Modifier("esES", 151, Skill("Meditation"), 12));
        }

        [Fact]
        public void Charges_in_spanish_and_english()
        {
            int layer = (Skill("Teleport") << 6) | 1;
            int charges = 20 | (20 << 8);
            Assert.Contains("Teletransporte de nivel 1 (20/20 cargas)", Modifier("esES", 204, layer, charges));
            Assert.Contains("Level 1 Teleport (20/20 Charges)", Modifier("enUS", 204, layer, charges));
        }

        [Fact]
        public void An_oskill_and_a_class_skill_in_japanese()
        {
            Assert.Contains("〈テレポート〉向上（+1）", Modifier("jaJP", 97, Skill("Teleport"), 1));
            Assert.Contains(
                "〈ファイアボール〉スキル向上（+3）（ソーサレス専用）",
                Modifier("jaJP", 107, Skill("Fire Ball"), 3));
        }

        [Fact]
        public void Self_repair_in_chinese()
        {
            // 2500 / 10 = 250 frames > 30, so (1, (250 + 12) / 25) through "每 %1 秒修复 %0 点耐久度".
            Assert.Contains("每 10 秒修复 1 点耐久度", Modifier("zhCN", 252, 0, 10));
            Assert.Contains("Repairs 1 durability in 10 seconds", Modifier("enUS", 252, 0, 10));
        }

        // ---- RuneQuote ---------------------------------------------------------------------------

        private static string RuneLetters(string language, bool legacy)
        {
            TooltipEngine engine = Engine(language, legacy);
            var helm = new Unit();
            helm.UnitType = 4;
            helm.ClassId = engine.Items.ClassIdForCode("cap");
            helm.ItemFlags = ItemRecordFlags.Identified | ItemRecordFlags.Socketed;
            helm.StatsLists.Add(new UnitStatList(0, ItemStatListFlags.Extended).Add(72, 30).Add(73, 30).Add(194, 1));

            var ber = new Unit();
            ber.UnitType = 4;
            ber.ClassId = engine.Items.ClassIdForCode("r30");
            ber.ItemFlags = ItemRecordFlags.Identified;
            helm.Items.Add(ber);

            return engine.Render(helm, Viewer(1)).Text.Split('\n')[1];
        }

        [Theory]
        [InlineData("enUS", false, "'Ber'")]
        [InlineData("ptBR", false, "\"Ber\"")]
        [InlineData("frFR", false, "Ber")]
        [InlineData("zhTW", true, "貝")]              // legacy zhTW: empty quotes, localised letters
        public void The_rune_letters_close_with_the_same_localised_quote(
            string language, bool legacy, string expected)
        {
            Assert.Equal(expected, RuneLetters(language, legacy));
        }

        // ---- MAX(min, max) -----------------------------------------------------------------------

        private static Unit Weapon(string code, params int[] statValue)
        {
            var weapon = new Unit();
            weapon.UnitType = 4;
            weapon.ClassId = Engine().Items.ClassIdForCode(code);
            weapon.ItemFlags = ItemRecordFlags.Identified;
            var list = new UnitStatList(0, ItemStatListFlags.Extended).Add(72, 30).Add(73, 30);
            for (int at = 0; at < statValue.Length; at += 2)
            {
                list.Add(statValue[at], statValue[at + 1]);
            }

            weapon.StatsLists.Add(list);
            return weapon;
        }

        [Fact]
        public void A_throw_minimum_above_its_maximum_shows_the_minimum_twice()
        {
            // `dmg-min` raised the throw minimum to 12 past the base maximum of 9.
            string[] lines = Lines(Engine(), Weapon("tkf", 21, 10, 22, 11, 159, 12, 160, 9));
            Assert.Contains("ÿc0Throw Damage: 12 to 12", lines);
            Assert.Contains("One-Hand Damage: 10 to 11", lines);
        }

        [Fact]
        public void A_barbarians_one_hand_line_takes_the_maximum_too()
        {
            string[] lines = Lines(Engine(), Weapon("2hs", 21, 10, 22, 9, 23, 14, 24, 20), 4);
            Assert.Contains("ÿc0One-Hand Damage: 10 to 10", lines);
            Assert.Contains("ÿc0Two-Hand Damage: 14 to 20", lines);
        }

        [Fact]
        public void The_damage_api_reports_the_same_maximum()
        {
            Unit knife = Weapon("tkf", 21, 10, 22, 11, 159, 12, 160, 9);
            ItemDamageRange thrown = Assert.Single(
                Engine().Damage(knife, Viewer(1)).Lines, d => d.Kind == ItemDamageKind.Throw);
            Assert.Equal(12, thrown.Max);
        }

        // ---- sub_140b47700: a conversion still open at the NUL writes nothing -------------------

        private static Unit DamageResistUnique(TooltipEngine engine, string code, string name, int value)
        {
            var item = new Unit();
            item.UnitType = 4;
            item.ClassId = engine.Items.ClassIdForCode(code);
            item.Quality = ItemQualityNo.Unique;
            item.FileIndex = engine.Data.UniqueItems.FindRow("index", name);
            item.ItemFlags = ItemRecordFlags.Identified;
            item.StatsLists.Add(new UnitStatList(0, ItemStatListFlags.Extended).Add(31, 1000).Add(72, 60).Add(73, 60));
            item.StatsLists.Add(new UnitStatList(0, ItemStatListFlags.Magic).Add(36, value));
            return item;
        }

        [Fact]
        public void A_trailing_percent_left_open_is_dropped_in_french()
        {
            // frFR "…réduits de %d% %": "% " + 0xC2 prints 0xC2, 0xA0 follows, and the final "%" is
            // still open at the NUL (0x140b477d5), so nothing of it is written.
            TooltipEngine engine = Engine("frFR");
            string[] lines = Lines(engine, DamageResistUnique(engine, "uar", "Shaftstop", 30));
            Assert.Contains("Dégâts physiques subis réduits de 30\u00A0", lines);
            Assert.DoesNotContain("Dégâts physiques subis réduits de 30\u00A0%", lines);
        }

        [Fact]
        public void The_negative_french_text_drops_it_in_legacy_too()
        {
            // strings-legacy has no ...Negative, so Bone Break falls back to the HD text.
            TooltipEngine engine = Engine("frFR", true);
            Assert.Contains(
                "Dégâts physiques subis augmentés de 15\u00A0",
                Lines(engine, DamageResistUnique(engine, "cm3", "Bone Break", -15)));
        }

        [Theory]
        [InlineData("abc %", "abc ")]
        [InlineData("abc %-5", "abc ")]
        [InlineData("%d%\u00A0%", "7\u00A0")]
        public void An_open_conversion_at_the_end_writes_nothing(string format, string expected)
        {
            Assert.Equal(expected, CFormat.Sprintf(format, 7));
        }
    }
}
