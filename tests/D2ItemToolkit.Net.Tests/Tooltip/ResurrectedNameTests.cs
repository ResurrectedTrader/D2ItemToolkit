using System.Collections.Concurrent;
using Xunit;

namespace D2ItemToolkit.Tests
{
    /// <summary>
    /// ITEMS_GetName 0x140157740 per locale: the grammar header of sub_140251970 and the possessive
    /// of sub_140478a70. Several expectations are the game's own odd output (raw gender tags, the
    /// Polish trailing space) — reproduced, not fixed.
    /// </summary>
    public class ResurrectedNameTests
    {
        private static readonly ConcurrentDictionary<string, TooltipEngine> Engines =
            new ConcurrentDictionary<string, TooltipEngine>();

        private static TooltipEngine Engine(string language)
        {
            return Engines.GetOrAdd(language, l => TooltipEngine.ForVariant(
                GameVariant.ReignOfTheWarlock, new ResurrectedTextOptions { Language = l }));
        }

        private static string Name(
            string language, string code, int quality,
            string prefix = null, string suffix = null, string owner = null,
            string rarePrefix = null, string rareSuffix = null)
        {
            TooltipEngine engine = Engine(language);
            D2DataFiles data = engine.Data;

            var item = new Unit();
            item.UnitType = 4;
            item.ClassId = engine.Items.ClassIdForCode(code);
            item.Quality = quality;
            item.ItemFlags = ItemRecordFlags.Identified
                             | (owner != null ? ItemRecordFlags.Personalized : 0);
            item.PlayerName = owner ?? string.Empty;
            if (prefix != null)
            {
                item.MagicPrefix[0] = data.MagicSuffix.RowCount + 1 + data.MagicPrefix.FindRow("Name", prefix);
            }

            if (suffix != null)
            {
                item.MagicSuffix[0] = 1 + data.MagicSuffix.FindRow("Name", suffix);
            }

            if (rarePrefix != null)
            {
                item.RarePrefix = data.RareSuffix.RowCount + 1 + data.RarePrefix.FindRow("name", rarePrefix);
                item.RareSuffix = 1 + data.RareSuffix.FindRow("name", rareSuffix);
            }

            string name = string.Empty;
            foreach (ItemTooltipLine line in engine.Render(item).Lines)
            {
                if (line.Section == ItemTooltipSection.ItemName)
                {
                    string text = line.Text.Replace("\n", string.Empty);
                    name = name.Length == 0 ? text : text + " / " + name;
                }
            }

            return name;
        }

        [Theory]
        [InlineData("deDE", "gezacktes Kurzschwert der Dornen")]
        [InlineData("frFR", "Épée courte irrégulière d’épines")]
        [InlineData("esES", "Espada corta dentellada de espinas")]
        [InlineData("plPL", "Zębaty Krótki Miecz Cierni")]
        [InlineData("ruRU", "зазубренный короткий меч с шипами")]
        [InlineData("koKR", "바늘의 톱날의 숏소드")]
        [InlineData("jaJP", "鋸刃もつ荊棘のショートソード")]
        public void Two_affix_magic_names_agree_with_the_gendered_adjective(string language, string expected)
        {
            Assert.Equal(expected, Name(language, "ssd", ItemQualityNo.Magic, "Jagged", "of Thorns"));
        }

        [Fact]
        public void The_noun_tag_picks_the_adjective_variant()
        {
            Assert.Equal("robuste Krone", Name("deDE", "crn", ItemQualityNo.Magic, "Sturdy"));
            Assert.Equal("robuster Helm", Name("deDE", "hlm", ItemQualityNo.Magic, "Sturdy"));
            Assert.Equal("Masywna Korona", Name("plPL", "crn", ItemQualityNo.Magic, "Sturdy"));
        }

        [Fact]
        public void Spanish_suffix_only_names_print_the_header_defect_literally()
        {
            // esES 26776 is "a0n1:%1 %0": the BASE becomes the adjective, so the suffix leads and a
            // non-[ms] base keeps its tag.
            Assert.Equal("de la ballena [fs]Corona", Name("esES", "crn", ItemQualityNo.Magic, suffix: "of the Whale"));
            Assert.Equal("de la ballena Gran yelmo", Name("esES", "ghm", ItemQualityNo.Magic, suffix: "of the Whale"));
            Assert.Equal("Corona de la ballena", Name("esMX", "crn", ItemQualityNo.Magic, suffix: "of the Whale"));
        }

        [Fact]
        public void An_adjective_without_the_nouns_tag_prints_whole()
        {
            Assert.Equal(
                "[ms]Zakażający[fs]Zakażająca[ns]Zakażające[p]Zakażające Berło",
                Name("plPL", "scp", ItemQualityNo.Magic, "Virulent"));
        }

        [Fact]
        public void A_trailing_space_in_an_argument_collapses_the_next_one()
        {
            Assert.Equal("Botas estridentes de espinas", Name("esMX", "lbt", ItemQualityNo.Magic, "Screaming", "of Thorns"));
            Assert.Equal("Botas estridentes ", Name("esMX", "lbt", ItemQualityNo.Magic, "Screaming"));
        }

        [Theory]
        [InlineData("enUS", "Hans", "Hans' Crown")]
        [InlineData("enUS", "Anna", "Anna's Crown")]
        [InlineData("deDE", "Hans", "Hans' Krone")]
        [InlineData("deDE", "Anna", "Annas Krone")]
        [InlineData("frFR", "Hans", "Couronne de Hans")]
        [InlineData("frFR", "anna", "Couronne d'anna")]
        [InlineData("esES", "Hans", "Corona de Hans")]
        [InlineData("itIT", "Hans", "Corona di Hans")]
        [InlineData("plPL", "Hans", "Korona(Hans) ")]
        [InlineData("ruRU", "Hans", "(Hans) корона")]
        [InlineData("jaJP", "Hans", "Hans冠")]
        public void The_possessive_follows_the_language(string language, string owner, string expected)
        {
            Assert.Equal(expected, Name(language, "crn", ItemQualityNo.Normal, owner: owner));
        }

        [Fact]
        public void A_rare_affix_line_is_personalised_alone()
        {
            Assert.Equal(
                "Krone / Annas Bestien - Biss",
                Name("deDE", "crn", ItemQualityNo.Rare, owner: "Anna", rarePrefix: "Beast", rareSuffix: "bite"));
            Assert.Equal(
                "Korona / Bestialskie Ukąszenie(Anna) ",
                Name("plPL", "crn", ItemQualityNo.Rare, owner: "Anna", rarePrefix: "Beast", rareSuffix: "bite"));
        }

        [Fact]
        public void English_has_no_grammar_header_and_no_stray_space()
        {
            Assert.Equal("Sturdy Crown", Name("enUS", "crn", ItemQualityNo.Magic, "Sturdy"));
            Assert.Equal("Crown of the Whale", Name("enUS", "crn", ItemQualityNo.Magic, suffix: "of the Whale"));
            Assert.Equal("Superior Gauntlets", Name("enUS", "hgl", ItemQualityNo.Superior));
        }

        [Theory]
        [InlineData("Corosive", "clw", " когти", "коррозийные")]
        [InlineData("Spiritual", "dr3", " бараньи рога", "возвышенные")]
        public void A_russian_plural_prefix_that_ends_in_a_newline_draws_the_name_on_two_rows(
            string prefix, string code, string top, string bottom)
        {
            // ruRU HD `[pl]коррозийные\n`: the a0n1 header takes [pl] to the end, newline included,
            // and the collapse rule only eats a trailing SPACE, so the base becomes its own row. Rows
            // draw in reverse, so it lands ABOVE the prefix, with the leading space kept.
            TooltipEngine engine = Engine("ruRU");
            var item = new Unit();
            item.UnitType = 4;
            item.ClassId = engine.Items.ClassIdForCode(code);
            item.Quality = ItemQualityNo.Magic;
            item.ItemFlags = ItemRecordFlags.Identified;
            item.MagicPrefix[0] = engine.Data.MagicSuffix.RowCount + 1 + engine.Data.MagicPrefix.FindRow("Name", prefix);

            string[] lines = engine.Render(item).Text.Split('\n');
            Assert.Equal(top, lines[0]);
            Assert.Equal(bottom, lines[1]);
        }
    }
}
