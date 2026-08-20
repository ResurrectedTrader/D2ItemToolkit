using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace D2ItemToolkit.Tests
{
    /// <summary>
    /// SKILLDESC_BuildStatBuffDesc 0x4e60dc short-circuits into SKILLDESC_BuildChargeSkillDesc
    /// 0x4e5e90 for an elixir, so the elixir line REPLACES the whole modifiers block.
    /// </summary>
    public class ElixirTests
    {
        private static readonly D2DataFiles Data = D2DataFiles.LoadEmbedded();

        private static readonly ItemTable Items = new ItemTable(
            Data.Weapons, Data.Armor, Data.Misc);

        private static readonly ItemTypeTree Types = new ItemTypeTree(Data.ItemTypes);

        private const int StatValue = 71;

        private static RecordSections Sections(string code, int fileIndex, int value)
        {
            var item = new ItemIdentity();
            item.ClassId = Items.ClassIdForCode(code);
            item.Code = code;
            item.Quality = ItemQualityNo.Normal;
            item.Flags = ItemRecordFlags.Identified;
            item.FileIndex = fileIndex;
            Assert.True(item.ClassId >= 0, code);

            var stats = new Dictionary<int, int>();
            if (value != 0)
            {
                stats[ItemStatReader.PackStatKey(0, StatValue)] = value;
            }

            return new RecordSections(
                Data, Items, Types, item, null, stats, null, null, null);
        }

        private static string Describe(string code, int fileIndex, int value)
        {
            return Sections(code, fileIndex, value)
                .GetSection(ItemTooltipSection.Modifiers);
        }

        [Theory]
        // unk_72D6C0: fileIndex picks the attribute. 0 strength, 1 energy, 2 dexterity, 3 vitality.
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void An_attribute_elixir_names_what_it_raises(int fileIndex)
        {
            string text = Describe("elx", fileIndex, 5);

            Assert.False(string.IsNullOrEmpty(text), "fileIndex " + fileIndex);
            Assert.Contains("5", text, System.StringComparison.Ordinal);
            Assert.EndsWith("\n", text, System.StringComparison.Ordinal);
        }

        [Fact]
        public void The_four_attribute_elixirs_all_name_something_different()
        {
            string[] names = new[] { 0, 1, 2, 3 }
                .Select(f => Describe("elx", f, 5))
                .ToArray();

            Assert.Equal(4, names.Distinct().Count());
        }

        [Theory]
        // 0x4e5f41 / 0x4e5f4e / 0x4e5f5b: stat ids 6..11 are 8-bit fixed point. 7 is maxhp, 9 maxmana.
        [InlineData(7)]
        [InlineData(9)]
        public void A_life_or_mana_elixir_shifts_the_value_down_by_eight(int fileIndex)
        {
            // 20 << 8 renders as 20; the four attribute entries would show the raw number.
            Assert.Contains("20", Describe("elx", fileIndex, 20 << 8), System.StringComparison.Ordinal);
            Assert.DoesNotContain(
                (20 << 8).ToString(), Describe("elx", fileIndex, 20 << 8),
                System.StringComparison.Ordinal);
        }

        [Fact]
        public void An_attribute_elixir_does_not_shift()
        {
            Assert.Contains("5", Describe("elx", 0, 5), System.StringComparison.Ordinal);
        }

        [Fact]
        public void A_zero_value_writes_nothing()
        {
            // 0x4e5f7d skips the whole emission when the value is zero.
            Assert.Null(Describe("elx", 0, 0));
        }

        [Fact]
        public void A_negative_value_omits_the_plus()
        {
            string positive = Describe("elx", 0, 5);
            string negative = Describe("elx", 0, -5);

            Assert.False(string.IsNullOrEmpty(negative));

            // 0x4e5fe5 prefixes locale 4002 only on the positive branch.
            Assert.Contains("-5", negative, System.StringComparison.Ordinal);
            Assert.NotEqual(positive.Replace("5", "-5"), negative);
        }

        [Fact]
        public void A_file_index_outside_the_table_writes_nothing()
        {
            // Only 0, 1, 2, 3, 9 and 7 appear in the six entries.
            Assert.Null(Describe("elx", 4, 5));
            Assert.Null(Describe("elx", 42, 5));
        }

        [Theory]
        // The whole line, pinned. Locale 3498..3503 are the six "Elixir of ..." strings and 4002 is
        // the "+"; note 3502 is Mana against fileIndex 9 and 3503 Life against 7.
        [InlineData(0, 5, "Elixir of Strength +5\n")]
        [InlineData(1, 5, "Elixir of Energy +5\n")]
        [InlineData(2, 5, "Elixir of Dexterity +5\n")]
        [InlineData(3, 5, "Elixir of Vitality +5\n")]
        [InlineData(7, 20 << 8, "Elixir of Life +20\n")]
        [InlineData(9, 20 << 8, "Elixir of Mana +20\n")]
        [InlineData(0, -5, "Elixir of Strength -5\n")]
        public void The_elixir_line_renders_exactly(int fileIndex, int value, string expected)
        {
            Assert.Equal(expected, Describe("elx", fileIndex, value));
        }

        [Fact]
        public void A_non_elixir_never_takes_this_path()
        {
            Assert.Null(Describe("lrg", 0, 5));
            Assert.Null(Describe("gpr", 0, 5));
        }

        [Fact]
        public void The_elixir_line_replaces_the_generated_modifier_block()
        {
            // Give the item a stat the normal engine WOULD describe, and prove it does not appear.
            var item = new ItemIdentity();
            item.ClassId = Items.ClassIdForCode("elx");
            item.Quality = ItemQualityNo.Normal;
            item.Flags = ItemRecordFlags.Identified;
            item.FileIndex = 0;

            var stats = new Dictionary<int, int>();
            stats[ItemStatReader.PackStatKey(0, StatValue)] = 5;
            stats[ItemStatReader.PackStatKey(0, 39)] = 25;   // fire resist

            var sections = new RecordSections(
                Data, Items, Types, item, null, stats, null, null, null);
            var composer = new ItemTooltipComposer(sections, Data.CreateGenerator());

            var context = new ItemTooltipContext();
            context.Quality = ItemQuality.Normal;
            context.Flags = ItemTooltipFlags.Identified;

            IReadOnlyList<ItemTooltipLine> lines = composer.Compose(context, stats);
            string text = composer.Render(lines);

            Assert.DoesNotContain("Fire Resist", text, System.StringComparison.Ordinal);
            Assert.Contains(
                lines, l => l.Section == ItemTooltipSection.Modifiers);
        }
    }
}
