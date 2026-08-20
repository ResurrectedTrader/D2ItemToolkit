using Xunit;

namespace D2ItemToolkit.Tests
{
    /// <summary>
    /// TXT_AllocTxt_items compiles weapons (0x633351), then armor (0x63336d), then misc (0x63338c)
    /// and sums the three counts at 0x6333ab — the counterpart of ItemTable.test.ts, which had no
    /// C# equivalent. Getting the concatenation order wrong shifts every class id in the game.
    /// </summary>
    public class ItemTableTests
    {
        private static readonly D2DataFiles Data = D2DataFiles.LoadEmbedded();

        private static readonly ItemTable Items = new ItemTable(
            Data.Weapons, Data.Armor, Data.Misc);

        private static TxtFile File(int classId)
        {
            TxtFile file;
            Assert.True(Items.TryResolve(classId, out file, out _), "unresolved " + classId);
            return file;
        }

        private static int Row(int classId)
        {
            int row;
            Assert.True(Items.TryResolve(classId, out _, out row), "unresolved " + classId);
            return row;
        }

        [Fact]
        public void Counts_the_three_files_as_one_table()
        {
            Assert.Equal(306, Data.Weapons.RowCount);
            Assert.Equal(202, Data.Armor.RowCount);
            Assert.Equal(151, Data.Misc.RowCount);
            Assert.Equal(659, Items.RowCount);
        }

        [Fact]
        public void Indexes_the_concatenation_weapons_armor_misc_not_armor_first()
        {
            Assert.Same(Data.Weapons, File(0));
            Assert.Equal(0, Row(0));
            Assert.Equal("hax", Items.Code(0));

            Assert.Same(Data.Weapons, File(305));
            Assert.Equal(305, Row(305));
            Assert.Equal("amf", Items.Code(305));

            Assert.Same(Data.Armor, File(306));
            Assert.Equal(0, Row(306));
            Assert.Equal("cap", Items.Code(306));

            Assert.Same(Data.Misc, File(508));
            Assert.Equal(0, Row(508));
            Assert.Equal("elx", Items.Code(508));

            Assert.Same(Data.Misc, File(658));
            Assert.Equal(150, Row(658));
            Assert.Equal("std", Items.Code(658));
        }

        [Fact]
        public void Returns_nothing_out_of_range_rather_than_clamping()
        {
            // 0x6335fc.
            Assert.False(Items.TryResolve(-1, out _, out _));
            Assert.False(Items.TryResolve(659, out _, out _));

            Assert.Equal(string.Empty, Items.GetString(659, "code"));
            Assert.Equal(0, Items.GetInt(659, "levelreq"));
            Assert.Equal(string.Empty, Items.Code(-1));
            Assert.Equal(0, Items.RequiredLevel(-1));
        }

        [Fact]
        public void Reads_by_column_name_so_the_three_schemas_do_not_shift_each_other()
        {
            // misc.txt has no `type2` values for a potion, and weapons.txt has no `spelldesc`; an
            // absent or blank column yields the loader's default rather than a neighbour's cell.
            Assert.Equal("axe", Items.PrimaryTypeCode(0));
            Assert.Equal(string.Empty, Items.SecondaryTypeCode(0));
            Assert.Equal("elix", Items.PrimaryTypeCode(508));

            Assert.Equal(Data.Weapons.GetInt(305, "levelreq"), Items.RequiredLevel(305));
            Assert.Equal(Data.Armor.GetInt(0, "levelreq"), Items.RequiredLevel(306));
            Assert.Equal(Data.Misc.GetInt(0, "levelreq"), Items.RequiredLevel(508));
        }

        [Fact]
        public void Resolves_a_class_id_from_a_code_case_insensitively()
        {
            Assert.Equal(557, Items.ClassIdForCode("gcv"));
            Assert.Equal(557, Items.ClassIdForCode("GCV"));
            Assert.Equal(330, Items.ClassIdForCode("lrg"));
            Assert.Equal(0, Items.ClassIdForCode("hax"));

            Assert.Equal(-1, Items.ClassIdForCode(string.Empty));
            Assert.Equal(-1, Items.ClassIdForCode(null));
            Assert.Equal(-1, Items.ClassIdForCode("nosuchcode"));
        }

        [Fact]
        public void Tolerates_a_missing_file()
        {
            var partial = new ItemTable(Data.Weapons, null, Data.Misc);

            Assert.Equal(457, partial.RowCount);

            TxtFile file;
            int row;
            Assert.True(partial.TryResolve(306, out file, out row));
            Assert.Same(Data.Misc, file);
            Assert.Equal(0, row);
        }
    }
}
