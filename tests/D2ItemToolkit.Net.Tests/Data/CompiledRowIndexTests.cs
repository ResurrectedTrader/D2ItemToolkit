using Xunit;

namespace D2ItemToolkit.Tests
{
    /// <summary>
    /// Row index IS the record id: the C++ producer emits the game's classId, so a single extra or
    /// missing row silently renames every item after it. The expected counts are the record counts
    /// in the shipped .bin files the game actually loads (DATATBLS_LoadFromBin), which are one less
    /// than the .txt data row count because 0x6bd742 splices out the "Expansion" divider.
    /// </summary>
    public class CompiledRowIndexTests
    {
        private static readonly D2DataFiles Data = D2DataFiles.LoadEmbedded();

        private static readonly ItemTable Items = new ItemTable(
            Data.Weapons, Data.Armor, Data.Misc);

        [Theory]
        [InlineData("itemtypes", 103)]
        [InlineData("weapons", 306)]
        [InlineData("armor", 202)]
        [InlineData("misc", 151)]
        [InlineData("uniqueitems", 402)]
        [InlineData("setitems", 127)]
        public void Table_row_counts_match_the_compiled_bin(string table, int expected)
        {
            Assert.Equal(expected, Table(table).RowCount);
        }

        [Theory]
        [InlineData(13, "char")]
        [InlineData(20, "gem")]
        [InlineData(45, "weap")]
        [InlineData(53, "sock")]
        [InlineData(58, "jewl")]
        [InlineData(74, "rune")]
        public void ItemTypes_rows_land_where_the_binary_indexes_them(int row, string code)
        {
            // These are the literal constants pushed at IsOfType call sites: 13 at 0x48e5c6,
            // 20/53/74 at 0x4e68bd / 0x4865e2 / 0x4e6a6c. 58 is the first row past the divider.
            Assert.Equal(code, Data.ItemTypes.GetString(row, "Code").Trim());
            Assert.Equal(row, Types.Row(code));
        }

        [Theory]
        [InlineData(174, "qf2")]
        [InlineData(175, "ktr")]
        [InlineData(176, "wrb")]
        public void Weapon_class_ids_skip_the_divider(int classId, string code)
        {
            // weapons.txt puts "Expansion" at data row 175, so Katar compiles to 175, not 176.
            Assert.Equal(code, Items.Code(classId));
        }

        private static readonly ItemTypeTree Types = new ItemTypeTree(Data.ItemTypes);

        private static TxtFile Table(string name)
        {
            switch (name)
            {
                case "itemtypes": return Data.ItemTypes;
                case "weapons": return Data.Weapons;
                case "armor": return Data.Armor;
                case "misc": return Data.Misc;
                case "uniqueitems": return Data.UniqueItems;
                case "setitems": return Data.SetItems;
                default: return null;
            }
        }
    }
}
