using System.Collections.Generic;
using Xunit;

namespace D2ItemToolkit.Tests
{
    /// <summary>
    /// AppendQuanity 0x486100 and INV_FormatItemStatCostText 0x486370, which share one buffer.
    /// </summary>
    public class SpellDescriptionTests
    {
        private static readonly D2DataFiles Data = D2DataFiles.LoadEmbedded();

        private static readonly ItemTable Items = new ItemTable(
            Data.Weapons, Data.Armor, Data.Misc);

        private static readonly ItemTypeTree Types = new ItemTypeTree(Data.ItemTypes);

        private const int Amazon = 0;
        private const int Sorceress = 1;
        private const int Paladin = 3;
        private const int Barbarian = 4;

        private static string Describe(string code, int? classId, int quantity = 0)
        {
            var item = new ItemIdentity();
            item.ClassId = Items.ClassIdForCode(code);
            item.Code = code;
            item.Flags = ItemRecordFlags.Identified;
            Assert.True(item.ClassId >= 0, code);

            ItemViewer viewer = null;
            if (classId.HasValue)
            {
                viewer = new ItemViewer();
                viewer.UnitType = 0;
                viewer.ClassId = classId.Value;
            }

            var stats = new Dictionary<int, int>();
            if (quantity != 0)
            {
                stats[ItemStatReader.PackStatKey(0, 70)] = quantity;
            }

            var sections = new RecordSections(
                Data, Items, Types, item, viewer, stats, null, null, null);
            return sections.GetSection(ItemTooltipSection.QuantityAndSpellDescription);
        }

        [Fact]
        public void A_rejuv_potion_uses_mode_one_and_shows_the_string_alone()
        {
            // rvs has spelldesc 1: the locale string with no value appended.
            string text = Describe("rvs", Paladin);

            Assert.False(string.IsNullOrEmpty(text));
            Assert.DoesNotContain("Quantity", text, System.StringComparison.Ordinal);
        }

        [Theory]
        // hp3 is the Healing Potion: spelldesc 2, stat1 hpregen (74), calc1 100. The healing family
        // multiplier is 1.5x for Amazon/Paladin/Assassin, 2x for Barbarian, 1x for the casters.
        [InlineData("hp3", Amazon, 150)]
        [InlineData("hp3", Paladin, 150)]
        [InlineData("hp3", Barbarian, 200)]
        [InlineData("hp3", Sorceress, 100)]
        public void A_healing_potion_scales_per_class(string code, int classId, int expected)
        {
            Assert.Contains(
                " " + expected, Describe(code, classId), System.StringComparison.Ordinal);
        }

        [Theory]
        // mp3 is the Mana Potion: stat1 manarecovery (26), calc1 80. The mana family is the mirror —
        // casters get 2x and the Barbarian gets 1x.
        [InlineData("mp3", Amazon, 120)]
        [InlineData("mp3", Paladin, 120)]
        [InlineData("mp3", Barbarian, 80)]
        [InlineData("mp3", Sorceress, 160)]
        public void A_mana_potion_scales_the_other_way(string code, int classId, int expected)
        {
            Assert.Contains(
                " " + expected, Describe(code, classId), System.StringComparison.Ordinal);
        }

        [Fact]
        public void No_viewer_means_no_spell_description_at_all()
        {
            // 0x4863a2 bails before every arm when there is no player unit, so the quantity line
            // survives instead.
            string text = Describe("hp3", null);

            Assert.DoesNotContain("100", text ?? string.Empty, System.StringComparison.Ordinal);
        }

        [Fact]
        public void A_spell_description_replaces_the_quantity_line()
        {
            // Both write to var_1434 and every spelldesc arm uses STRING_CopyWideString.
            string text = Describe("hp3", Paladin, 3);

            Assert.DoesNotContain("Quantity", text, System.StringComparison.Ordinal);
        }

        [Fact]
        public void A_stackable_item_shows_its_quantity_even_at_zero()
        {
            // 0x486160 gates on `stat 70 > 0 OR maxstack > 0`, and throwing knives are stackable.
            string text = Describe("tkf", Paladin);

            Assert.Contains("Quantity: 0", text, System.StringComparison.Ordinal);
        }

        [Fact]
        public void A_non_stackable_item_with_no_spelldesc_writes_nothing()
        {
            Assert.Null(Describe("lrg", Paladin));
        }

        [Fact]
        public void Only_modes_one_and_two_appear_in_the_shipped_tables()
        {
            var modes = new SortedSet<int>();

            for (int classId = 0; classId < Items.RowCount; ++classId)
            {
                int mode = Items.GetInt(classId, "spelldesc");
                if (mode > 0)
                {
                    modes.Add(mode);
                }
            }

            // Modes 3 and 4 are unreachable from shipped data, which is why they are left
            // unimplemented rather than guessed at.
            Assert.Equal(new[] { 1, 2 }, modes);
        }
    }
}
