using Xunit;

namespace D2ItemToolkit.Tests
{
    /// <summary>
    /// DATATBLS_ItemParamLinker 0x140214e40: atoi for a leading `-` or digit, else skills `skill`,
    /// montype `type`, states `state`, else 0 — through FOG_GetRowFromTxt's ASCII case fold.
    /// </summary>
    public class ItemParamLinkerTests
    {
        private static readonly TooltipEngine Engine =
            TooltipEngine.ForVariant(GameVariant.ReignOfTheWarlock);

        [Theory]
        [InlineData("12", 12)]
        [InlineData("-5", -5)]
        [InlineData("12abc", 12)]              // atoi stops at the first non-digit
        [InlineData(" 5", 0)]                  // not a digit first, so a name, and no name matches
        [InlineData("Flame Wave", 398)]        // skills
        [InlineData("feral rage", 232)]        // the case fold; uniqueitems ships it lower-case
        [InlineData("undead", 1)]              // montype
        [InlineData("fullsetgeneric", 175)]    // states
        [InlineData("no such name", 0)]
        [InlineData("", 0)]
        public void Resolves_a_cell_as_the_game_does(string cell, int expected)
        {
            Assert.Equal(expected, Engine.Data.ParamLinker.Resolve(cell));
        }

        [Theory]
        [InlineData("12", 12)]
        [InlineData("12abc", 0)]
        [InlineData("Teleport", 0)]            // 1.14d's linker is untraced: names stay 0
        public void Lod_keeps_the_whole_cell_reading(string cell, int expected)
        {
            Assert.Equal(expected, TooltipEngine.Embedded.Data.ParamLinker.Resolve(cell));
        }

        [Fact]
        public void A_named_skill_param_lands_on_that_skills_layer()
        {
            // Opalvein's prop1 is `att-skill` "Flame Wave" 2..15: func 11 writes stat 195 at
            // layer (398 << 6) | 15 with the chance, 2. Parsed as 0 it went to layer 15.
            var unit = new Unit();
            unit.UnitType = 4;
            unit.Quality = 7;
            unit.FileIndex = Engine.Data.UniqueItems.FindRow("index", "Opalvein");
            unit.ClassId = Engine.Items.ClassIdForCode("rin");
            unit.ItemFlags = ItemRecordFlags.Identified;

            int layer = (398 << 6) | 15;
            unit.StatsLists.Add(new UnitStatList(0, ItemStatListFlags.Magic).Add(195, 2, layer));
            ItemRollRanges ranges = Engine.Ranges(unit);

            RolledStatRange range = Assert.Single(ranges.Stats, r => r.StatId == 195);
            Assert.Equal(layer, range.Layer);
            Assert.Equal(2, range.Low);
            Assert.Equal(2, range.High);
            Assert.DoesNotContain(195, ranges.Unattributed);
        }
    }
}
