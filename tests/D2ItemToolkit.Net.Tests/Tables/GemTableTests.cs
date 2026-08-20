using System.Linq;
using Xunit;

namespace D2ItemToolkit.Tests
{
    /// <summary>
    /// The gem-table half of the socket-filler path — the counterpart of GemTable.test.ts, which
    /// had no C# equivalent: SocketFillerTests only reaches GemTable through a rendered
    /// description, so the row lookups and the quadruple reader were never asserted directly.
    ///
    /// gems row 0 is a real row. TXT_Gems_GetLine 0x6372c0 rejects only `>= recordCount`
    /// (0x6372cc) and exactly -1 (0x6372d1); the `jle` that also drops 0 is at 0x4866e9 and
    /// belongs to INV_FormatRunewordName, behind an IsOfType(rune) test at 0x4866d6.
    /// </summary>
    public class GemTableTests
    {
        private static readonly D2DataFiles Data = D2DataFiles.LoadEmbedded();

        private static readonly ItemTable Items = new ItemTable(
            Data.Weapons, Data.Armor, Data.Misc);

        private static GemTable Gems()
        {
            return new GemTable(Data.Gems, Items);
        }

        [Fact]
        public void The_first_gems_row_is_a_real_gem()
        {
            // TXT_AllocTxt_gems 0x637279 writes the row index into items +0xF0 and writes a
            // literal 0 on its first iteration, so gcv's offset genuinely is 0.
            Assert.Equal("gcv", Data.Gems.GetString(0, "code").Trim());
        }

        [Fact]
        public void A_rune_letter_still_ignores_row_zero()
        {
            // RowForRuneClassId keeps the 0x4866e9 `jle`. No rune occupies row 0 (it is gcv), so
            // this is faithful and unobservable, but the two lookups must stay distinct.
            GemTable gems = Gems();

            Assert.Equal(0, gems.RowForFillerClassId(Items.ClassIdForCode("gcv")));
            Assert.Equal(-1, gems.RowForRuneClassId(Items.ClassIdForCode("gcv")));
        }

        [Fact]
        public void A_non_filler_resolves_to_no_gems_row_at_all()
        {
            GemTable gems = Gems();

            Assert.Equal(68, gems.RowCount);
            Assert.Equal(-1, gems.RowForFillerClassId(Items.ClassIdForCode("lrg")));
            Assert.Equal(-1, gems.RowForFillerClassId(-1));
            Assert.Equal(-1, gems.RowForRuneClassId(Items.ClassIdForCode("lrg")));
        }

        [Fact]
        public void Reads_the_rune_letter_off_the_record_and_leaves_gems_letterless()
        {
            GemTable gems = Gems();

            int ral = gems.RowForRuneClassId(Items.ClassIdForCode("r08"));
            Assert.Equal(42, ral);
            Assert.Equal("Ral", gems.Letter(ral));

            Assert.Null(gems.Letter(0));
            Assert.Null(gems.Letter(-1));
            Assert.Null(gems.Letter(68));
        }

        [Fact]
        public void Reports_the_gems_txt_code_for_a_row()
        {
            GemTable gems = Gems();

            Assert.Equal("gcv", gems.Code(0));
            Assert.Null(gems.Code(-1));
            Assert.Null(gems.Code(68));
        }

        [Fact]
        public void Reads_the_three_quadruples_of_each_destination_slot()
        {
            // pProperties[3][3] at gems row +0x30: slot 0 is the weapon mods, 1 the helm mods and
            // 2 the shield mods. Perfect Ruby is weapon 15-20 fire damage, helm/armor +38 life,
            // shield +40% fire resist.
            GemTable gems = Gems();
            int row = gems.RowForFillerClassId(Items.ClassIdForCode("gpr"));
            Assert.Equal(19, row);

            Assert.Equal(
                new[] { "0/15/15", "0/20/20", "0/0/0" }, Quadruples(gems, row, 0));
            Assert.Equal(
                new[] { "0/38/38", "0/0/0", "0/0/0" }, Quadruples(gems, row, 1));
            Assert.Equal(
                new[] { "0/40/40", "0/0/0", "0/0/0" }, Quadruples(gems, row, 2));

            Assert.Empty(gems.Properties(row, -1));
            Assert.Empty(gems.Properties(row, 3));
            Assert.Empty(gems.Properties(-1, 0));
            Assert.Empty(gems.Properties(68, 0));
        }

        private static string[] Quadruples(GemTable gems, int row, int slot)
        {
            return gems.Properties(row, slot)
                .Select(p => p.Param + "/" + p.Min + "/" + p.Max)
                .ToArray();
        }

        [Fact]
        public void Leaves_every_property_id_negative_until_a_resolver_is_injected()
        {
            // The mod code columns hold property NAMES; without pPropertiesLinker there is nothing
            // to resolve them against, and the appliers treat a negative id as "stop".
            GemTable gems = Gems();
            int row = gems.RowForFillerClassId(Items.ClassIdForCode("gpr"));

            Assert.Equal(
                new[] { -1, -1, -1 },
                gems.Properties(row, 0).Select(p => p.PropertyId).ToArray());

            var properties = new PropertiesTable(Data.Properties, Data.ItemStatCost);
            gems.ResolvePropertyCodesWith(code => properties.RowForCode(code));

            Assert.Equal(
                new[] { properties.RowForCode("fire-min"), properties.RowForCode("fire-max"), -1 },
                gems.Properties(row, 0).Select(p => p.PropertyId).ToArray());

            Assert.Equal(20, properties.RowForCode("fire-min"));
            Assert.Equal(21, properties.RowForCode("fire-max"));
        }

        [Fact]
        public void Is_empty_when_either_file_is_missing()
        {
            Assert.Equal(0, new GemTable(null, Items).RowCount);
            Assert.Equal(-1, new GemTable(null, Items).RowForFillerClassId(0));
            Assert.Equal(
                -1,
                new GemTable(Data.Gems, null).RowForFillerClassId(Items.ClassIdForCode("gcv")));
        }
    }
}
