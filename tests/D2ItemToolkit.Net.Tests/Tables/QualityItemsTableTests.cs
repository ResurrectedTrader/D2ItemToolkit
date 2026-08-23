using Xunit;

namespace D2ItemToolkit.Tests
{
    public class QualityItemsTableTests
    {
        private static readonly D2DataFiles Data = D2DataFiles.LoadEmbedded();

        [Fact]
        public void QualityItems_txt_is_loaded_from_the_embedded_resources()
        {
            Assert.NotNull(Data.QualityItems);
            Assert.Equal(8, Data.QualityItems.RowCount);
        }

        [Fact]
        public void Each_row_carries_the_ranges_its_modifiers_roll_within()
        {
            // lowqualityitems.txt names the inferior prefixes and nothing else; this file is the
            // superior counterpart and does carry ranges, which is what makes a superior item's
            // modifiers attributable to a row.
            Assert.Equal(1, Data.QualityItems.GetInt(0, "nummods"));
            Assert.Equal("att", Data.QualityItems.GetString(0, "mod1code").Trim());
            Assert.Equal(1, Data.QualityItems.GetInt(0, "mod1min"));
            Assert.Equal(3, Data.QualityItems.GetInt(0, "mod1max"));

            // The aggregate columns restate the same range per damage kind, and stay 0 where the
            // row's mods do not touch that kind.
            Assert.Equal(1, Data.QualityItems.GetInt(0, "ToHitMin"));
            Assert.Equal(3, Data.QualityItems.GetInt(0, "ToHitMax"));
            Assert.Equal(0, Data.QualityItems.GetInt(0, "Dam%Max"));
            Assert.Equal(0, Data.QualityItems.GetInt(0, "AC%Max"));
        }

        [Fact]
        public void Rows_are_gated_by_item_type()
        {
            // Row 0 is attack rating, which is a weapon-only roll: armour and the armour-shaped
            // slots are 0, so picking a row for a superior item means honouring these columns.
            Assert.Equal(1, Data.QualityItems.GetInt(0, "weapon"));
            Assert.Equal(0, Data.QualityItems.GetInt(0, "armor"));
            Assert.Equal(0, Data.QualityItems.GetInt(0, "boots"));

            // Row 2 is the armour-class roll, gated the other way round.
            Assert.Equal("ac%", Data.QualityItems.GetString(2, "mod1code").Trim());
            Assert.Equal(0, Data.QualityItems.GetInt(2, "weapon"));
            Assert.Equal(1, Data.QualityItems.GetInt(2, "armor"));
            Assert.Equal(1, Data.QualityItems.GetInt(2, "shield"));
        }

        [Fact]
        public void Two_mod_rows_exist_and_fill_both_slots()
        {
            // Row 3 is the attack-rating-and-damage superior, so a superior item can carry two
            // modifiers from a single row rather than two rows.
            Assert.Equal(2, Data.QualityItems.GetInt(3, "nummods"));
            Assert.Equal("att", Data.QualityItems.GetString(3, "mod1code").Trim());
            Assert.Equal("dmg%", Data.QualityItems.GetString(3, "mod2code").Trim());
            Assert.Equal(5, Data.QualityItems.GetInt(3, "mod2min"));
            Assert.Equal(15, Data.QualityItems.GetInt(3, "mod2max"));
        }
    }
}
