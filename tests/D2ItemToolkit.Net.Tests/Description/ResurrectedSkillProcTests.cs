using System.Collections.Generic;
using Xunit;

namespace D2ItemToolkit.Tests
{
    /// <summary>
    /// D2R descfunc 15 (ITEMSTATDESC_Build 0x1401ec332-0x1401ec8de): the positional
    /// (value, level, name) wrapper sub_14060d840, and the `item proc text` arm that only
    /// Metamorphosis's Mark of the Bear and Mark of the Wolf reach.
    /// </summary>
    public class ResurrectedSkillProcTests
    {
        private const int SkillOnAttack = 195;
        private const int SkillOnKill = 196;
        private const int SkillOnDeath = 197;
        private const int SkillOnHit = 198;
        private const int SkillOnLevelUp = 199;
        private const int SkillOnGetHit = 201;

        private const int MarkOfTheBear = 371;
        private const int MarkOfTheWolf = 372;

        private static readonly Dictionary<string, TooltipEngine> Engines =
            new Dictionary<string, TooltipEngine>();

        private static TooltipEngine Engine(GameVariant variant, string language, bool legacy)
        {
            string key = variant + "/" + language + "/" + legacy;
            lock (Engines)
            {
                TooltipEngine engine;
                if (!Engines.TryGetValue(key, out engine))
                {
                    var options = new ResurrectedTextOptions();
                    options.Language = language;
                    options.LegacyGraphics = legacy;
                    engine = TooltipEngine.ForVariant(variant, options);
                    Engines[key] = engine;
                }

                return engine;
            }
        }

        private static string Line(
            string language, int stat, int skill, int level, int value,
            GameVariant variant = GameVariant.ReignOfTheWarlock, bool legacy = false)
        {
            ItemDescriptionGenerator generator = Engine(variant, language, legacy).Data.CreateGenerator();
            int layer = (skill << 6) | level;
            IReadOnlyList<ItemDescriptionLine> lines = generator.Describe(new[]
            {
                new KeyValuePair<int, int>(ItemStatReader.PackStatKey(layer, stat), value),
            });

            Assert.Single(lines);
            return lines[0].Text;
        }

        [Theory]
        [InlineData(GameVariant.ReignOfTheWarlock)]
        [InlineData(GameVariant.Resurrected)]
        public void Metamorphosis_marks_render_their_proc_text_in_english(GameVariant variant)
        {
            Assert.Equal(
                "\nPhysical Damage Received Reduced by 20%\n+25% Attack Speed\nMark of the Bear:\n"
                + "Werebear strikes grant Mark for 180 seconds",
                Line("enUS", SkillOnHit, MarkOfTheBear, 1, 100, variant));
            Assert.Equal(
                "\nIncrease Maximum Life 40%\n30% Bonus to Attack Rating\nMark of the Wolf:\n"
                + "Werewolf strikes grant Mark for 180 seconds",
                Line("enUS", SkillOnHit, MarkOfTheWolf, 1, 100, variant));
        }

        [Fact]
        public void Metamorphosis_marks_render_their_proc_text_in_german()
        {
            Assert.Equal(
                "\nErlittener physischer Schaden um 20% verringert\n+25% Angriffsgeschwindigkeit\n"
                + "Mal des Bären:\nWerbärangriffe gewähren 180 Sek. lang das Mal.",
                Line("deDE", SkillOnHit, MarkOfTheBear, 1, 100));
            Assert.Equal(
                "\nErhöht max. Leben 40% \n30% Bonus zu Angriffswert\nMal des Wolfs:\n"
                + "Werwolfangriffe gewähren 180 Sek. lang das Mal.",
                Line("deDE", SkillOnHit, MarkOfTheWolf, 1, 100));
        }

        [Fact]
        public void Metamorphosis_marks_take_the_legacy_strings_where_they_exist()
        {
            Assert.Equal(
                "\nDamage Reduced by 20%\n+25% Attack Speed\nMark of the Bear:\n"
                + "Werebear strikes grant Mark for 180 seconds",
                Line("enUS", SkillOnHit, MarkOfTheBear, 1, 100, legacy: true));
            Assert.Equal(
                "\nIncrease Maximum Life 40%\n30% Bonus to Attack Rating\nMark of the Wolf:\n"
                + "Werewolf strikes grant Mark for 180 seconds",
                Line("enUS", SkillOnHit, MarkOfTheWolf, 1, 100, legacy: true));
            Assert.Equal(
                "\nSchaden reduziert um 20%\n+25% Angriffsgeschwindigkeit\nMal des Bären:\n"
                + "Werbärangriffe gewähren 180 Sekunden lang das Mal.",
                Line("deDE", SkillOnHit, MarkOfTheBear, 1, 100, legacy: true));
            Assert.Equal(
                "\nErhöht max. Leben 40% \n30% Bonus zu Angriffswert\nMal des Wolfs:\n"
                + "Werwolfangriffe gewähren 180 Sekunden lang das Mal.",
                Line("deDE", SkillOnHit, MarkOfTheWolf, 1, 100, legacy: true));
        }

        [Fact]
        public void The_proc_text_drops_the_chance_and_level()
        {
            // Neither shipped row's calcs read the level, and the chance is never printed.
            Assert.Equal(
                Line("enUS", SkillOnHit, MarkOfTheBear, 1, 100),
                Line("enUS", SkillOnGetHit, MarkOfTheBear, 20, 5));
        }

        [Fact]
        public void A_skill_without_proc_text_keeps_the_chance_line()
        {
            // Coldkill: hit-skill Ice Blast (45) 10% level 10.
            Assert.Equal(
                "10% Chance to cast level 10 Ice Blast on striking",
                Line("enUS", SkillOnHit, 45, 10, 10));
        }

        [Theory]
        // Coldkill: hit-skill Ice Blast (45) 10% level 10.
        [InlineData("esES", SkillOnHit, 45, 10, 10,
            "10% de probabilidad de lanzar Explosión de hielo de nivel 10 al golpear")]
        [InlineData("frFR", SkillOnHit, 45, 10, 10,
            "10% de chances de lancer Décharge de glace - niv. 10 en touchant")]
        [InlineData("esMX", SkillOnHit, 45, 10, 10,
            "10% de probabilidad de lanzar Resplandor gélido nivel 10 al golpear")]
        [InlineData("jaJP", SkillOnHit, 45, 10, 10, "命中時に10%の確率で〈アイスブラスト〉（レベル10）を発動")]
        [InlineData("ptBR", SkillOnHit, 45, 10, 10,
            "10% de chance de lançar Impacto Gélido de nível 10 ao atingir um inimigo")]
        [InlineData("ruRU", SkillOnHit, 45, 10, 10,
            "Вероятность 10% применить умение «Ледяной удар» 10-го уровня при ударе")]
        // Arm of King Leoric: gethit-skill Bone Prison (88) 10% level 2, Bone Spirit (93) 5% level 10.
        [InlineData("ruRU", SkillOnGetHit, 88, 2, 10,
            "Вероятность 10% применить умение «Костяная клетка» 2-го уровня при получении урона")]
        [InlineData("ruRU", SkillOnGetHit, 93, 10, 5,
            "Вероятность 5% применить умение «Костяной дух» 10-го уровня при получении урона")]
        [InlineData("esES", SkillOnGetHit, 88, 2, 10,
            "10% de probabilidad de lanzar Prisión de huesos de nivel 2 al recibir un golpe")]
        // Todesfaelle Flamme: att-skill Fire Ball (47) 10% level 6.
        [InlineData("frFR", SkillOnAttack, 47, 6, 10,
            "10% de chances de lancer Boule de feu - niv. 6 en attaquant")]
        [InlineData("jaJP", SkillOnAttack, 47, 6, 10, "攻撃時に10%の確率で〈ファイアボール〉（レベル6）を発動")]
        // Executioner's Justice: kill-skill Decrepify (87) 50% level 6.
        [InlineData("ptBR", SkillOnKill, 87, 6, 50,
            "50% de chance de lançar Decrepitar de nível 6 quando você abate um inimigo")]
        // Medusa's Gaze: death-skill Nova (48) 100% level 44.
        [InlineData("esMX", SkillOnDeath, 48, 44, 100,
            "100% de probabilidad de lanzar Nova nivel 44 cuando mueres")]
        // Rainbow Facet: levelup-skill Nova (48) 100% level 41.
        [InlineData("ruRU", SkillOnLevelUp, 48, 41, 100,
            "Вероятность 100% применить умение «Кольцо молний» 41-го уровня при достижении нового уровня")]
        public void Chance_to_cast_formats_positionally(
            string language, int stat, int skill, int level, int value, string expected)
        {
            Assert.Equal(expected, Line(language, stat, skill, level, value));
        }

        [Fact]
        public void The_positional_wrapper_passes_arguments_in_marker_order()
        {
            Assert.Equal("7 3 x", CFormat.PositionalValueLevelName("%0 %1 %2", 7, 3, "x"));
            Assert.Equal("x 3 7", CFormat.PositionalValueLevelName("%2 %1 %0", 7, 3, "x"));
            Assert.Equal("3 x 7", CFormat.PositionalValueLevelName("%1 %2 %0", 7, 3, "x"));
            Assert.Equal("7% x 3", CFormat.PositionalValueLevelName("%0%% %2 %1", 7, 3, "x"));
        }

        [Fact]
        public void The_positional_wrapper_rewrites_only_the_first_marker_after_flags()
        {
            // A flag keeps the conversion open (0x14060d8f3), so "%+0" is the value marker.
            Assert.Equal("+7 3 x", CFormat.PositionalValueLevelName("%+0 %1 %2", 7, 3, "x"));
            // "%%0" is a literal percent then a digit, not a marker: no markers at all means
            // plain printf with (value, level, name).
            Assert.Equal("%0 7", CFormat.PositionalValueLevelName("%%0 %d", 7, 3, "x"));
        }

        [Fact]
        public void The_positional_wrapper_writes_nothing_for_a_partial_set()
        {
            Assert.Equal(string.Empty, CFormat.PositionalValueLevelName("%0 %1", 7, 3, "x"));
            Assert.Equal(string.Empty, CFormat.PositionalValueLevelName("%d %2", 7, 3, "x"));
        }

        private static SkillItemProc Proc(int count, params SkillDescLineRow[] lines)
        {
            var skill = new SkillItemProc();
            skill.TextId = 900;
            skill.LineCount = count;
            skill.Params = new[] { 25, 20, 0, 0, 0, 0, 0, 0 };
            skill.AuraLenCalc = "4500";
            skill.Lines = new SkillDescLineRow[SkillItemProc.MaxLines];
            for (int at = 0; at < skill.Lines.Length; ++at)
            {
                skill.Lines[at] = at < lines.Length ? lines[at] : new SkillDescLineRow();
            }

            return skill;
        }

        private static SkillDescLineRow Row(int func, int textA, string calcA, int textB = 5382)
        {
            var row = new SkillDescLineRow();
            row.Func = func;
            row.TextA = textA;
            row.TextB = textB;
            row.CalcA = calcA;
            row.CalcB = string.Empty;
            return row;
        }

        private static FakeStringTable ProcStrings(string proc)
        {
            return new FakeStringTable()
                .Add(900, proc)
                .Add(901, "%d%% A")
                .Add(902, "B ")
                .Add(4252, string.Empty)
                .Add(4267, " second")
                .Add(4268, " seconds")
                .Add(DescStringIds.Newline, "\n");
        }

        [Fact]
        public void A_line_that_writes_nothing_empties_the_whole_proc_line()
        {
            // calcA 0 on line 74 skips the argument (0x1401ec50c); the count check then fails.
            SkillItemProc skill = Proc(2, Row(74, 901, "par1"), Row(74, 901, "0"));
            Assert.Equal(string.Empty, SkillDescCalc.FormatItemProc(skill, 1, ProcStrings("%s|%s")));
            Assert.Equal("25% A", SkillDescCalc.FormatItemProc(
                Proc(1, Row(74, 901, "par1")), 1, ProcStrings("%s")));
        }

        [Fact]
        public void An_unsupported_calc_is_a_line_not_produced()
        {
            SkillItemProc skill = Proc(1, Row(74, 901, "edmn"));
            Assert.Equal(string.Empty, SkillDescCalc.FormatItemProc(skill, 1, ProcStrings("%s")));
        }

        [Fact]
        public void A_zero_count_appends_the_proc_text_verbatim()
        {
            Assert.Equal("100%% %s", SkillDescCalc.FormatItemProc(Proc(0), 1, ProcStrings("100%% %s")));
        }

        [Fact]
        public void Line_12_writes_seconds_with_tenths_and_its_text_b_first()
        {
            Assert.Equal("B 1 second", SkillDescCalc.FormatItemProc(
                Proc(1, Row(12, 4252, "25", 902)), 1, ProcStrings("%s")));
            Assert.Equal("2.4 seconds", SkillDescCalc.FormatItemProc(
                Proc(1, Row(12, 4252, "60")), 1, ProcStrings("%s")));
            Assert.Equal(string.Empty, SkillDescCalc.FormatItemProc(
                Proc(1, Row(12, 4252, "2")), 1, ProcStrings("%s")));
        }

        [Fact]
        public void The_length_guard_counts_the_newline_before_it_is_stripped()
        {
            // proc 250 bytes + "25% A\n" (6) = 256: the argument is refused, so the line is empty.
            string proc = new string('x', 248) + "%s";
            Assert.Equal(string.Empty, SkillDescCalc.FormatItemProc(
                Proc(1, Row(74, 901, "par1")), 1, ProcStrings(proc)));
            string shorter = new string('x', 247) + "%s";
            Assert.Equal(new string('x', 247) + "25% A", SkillDescCalc.FormatItemProc(
                Proc(1, Row(74, 901, "par1")), 1, ProcStrings(shorter)));
        }

        [Fact]
        public void The_calc_subset_reads_the_skills_row()
        {
            var skill = new SkillItemProc();
            skill.Params = new[] { 10, 3, 0, 0, 0, 0, 0, 0 };
            skill.AuraLenCalc = "4500";

            int value;
            Assert.True(SkillDescCalc.TryEvaluate("ln12", skill, 5, out value));
            Assert.Equal(22, value);
            Assert.True(SkillDescCalc.TryEvaluate("ln12", skill, 0, out value));
            Assert.Equal(0, value);
            Assert.True(SkillDescCalc.TryEvaluate("lvl", skill, 5, out value));
            Assert.Equal(5, value);
            Assert.True(SkillDescCalc.TryEvaluate("len", skill, 5, out value));
            Assert.Equal(4500, value);
            Assert.True(SkillDescCalc.TryEvaluate(string.Empty, skill, 5, out value));
            Assert.Equal(0, value);
            Assert.False(SkillDescCalc.TryEvaluate("par1+1", skill, 5, out value));
            Assert.False(SkillDescCalc.TryEvaluate("edmn", skill, 5, out value));
        }
    }
}
