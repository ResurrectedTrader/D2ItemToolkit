using System.Linq;
using Xunit;

namespace D2ItemToolkit.Tests
{
    /// <summary>
    /// `ShowItemLevel` appends ` [ilvl N]` after the item's name. The game draws no such line, so
    /// this is one of the options that deliberately departs from it.
    /// </summary>
    public class ItemLevelSuffixTests
    {
        private static readonly D2DataFiles Data = D2DataFiles.LoadEmbedded();
        private static readonly TooltipEngine Engine = TooltipEngine.Embedded;
        private static readonly ItemTable Items = new ItemTable(Data.Weapons, Data.Armor, Data.Misc);

        /// <summary>UniqueItems.txt post-splice, 0-based. `xea`, a Serpentskin Armor.</summary>
        private const int SkinOfTheVipermagi = 210;

        private static Unit Vipermagi(int itemLevel)
        {
            var armor = new Unit();
            armor.UnitType = 4;
            armor.ClassId = Items.ClassIdForCode("xea");
            armor.Quality = ItemQualityNo.Unique;
            armor.FileIndex = SkinOfTheVipermagi;
            armor.ItemFlags = ItemRecordFlags.Identified;
            armor.ItemLevel = itemLevel;

            armor.StatsLists.Add(
                new UnitStatList(0, ItemStatListFlags.Extended).Add(31, 127));
            armor.StatsLists.Add(
                new UnitStatList(0, ItemStatListFlags.Magic).Add(16, 120));

            return armor;
        }

        private static TooltipOptions Showing()
        {
            var options = new TooltipOptions();
            options.ShowItemLevel = true;
            return options;
        }

        private static string[] Names(Tooltip tip)
        {
            return tip.Lines
                .Where(l => l.Section == ItemTooltipSection.ItemName)
                .Select(l => System.Text.RegularExpressions.Regex
                    .Replace(l.Text ?? string.Empty, "ÿc.", string.Empty).TrimEnd('\n'))
                .ToArray();
        }

        [Fact]
        public void The_level_follows_the_name_and_not_the_base_name()
        {
            // A unique's name section is two lines. The suffix belongs on the item's own name, not
            // on "Serpentskin Armor" below it.
            Assert.Equal(
                new[] { "Skin of the Vipermagi [ilvl 67]", "Serpentskin Armor" },
                Names(Engine.Render(Vipermagi(67), null, Showing())));
        }

        [Fact]
        public void Nothing_is_appended_when_the_record_carries_no_level()
        {
            // -1 is the documented absent sentinel, and the option must not invent an "ilvl -1".
            Assert.Equal(
                new[] { "Skin of the Vipermagi", "Serpentskin Armor" },
                Names(Engine.Render(Vipermagi(-1), null, Showing())));
        }

        [Fact]
        public void Level_zero_is_shown_because_only_minus_one_is_the_sentinel()
        {
            // The game floors item level at 1, so a 0 is a producer that defaulted rather than a
            // real level - but -1 is the documented sentinel, so 0 is treated as a real level.
            // Pinned so which of the two this is stays a decision rather than a discovery.
            Assert.Contains("[ilvl 0]", Names(Engine.Render(Vipermagi(0), null, Showing()))[0]);
        }

        [Fact]
        public void The_option_is_inert_when_off()
        {
            // Off is the default, and off has to be byte-identical to what the game draws.
            Assert.Equal(
                Engine.Render(Vipermagi(67)).Text,
                Engine.Render(Vipermagi(67), null, new TooltipOptions()).Text);

            Assert.DoesNotContain("ilvl", Engine.Render(Vipermagi(67)).Text);
        }

        [Fact]
        public void The_suffix_is_grey_and_restores_the_lines_colour()
        {
            // Same grey the range annotation uses, and a marker restoring the name's own colour
            // follows it so nothing after is repainted.
            string colored = Engine.Render(Vipermagi(67), null, Showing()).ColoredText;

            Assert.Contains(
                "Skin of the Vipermagiÿc5 [ilvl 67]ÿc4", colored);
        }

        [Fact]
        public void A_padded_name_does_not_get_a_double_space()
        {
            // The game pads a magic or rare name with a trailing space. 57 of the 60 corpus cases
            // that carry a suffix are padded, so a separator of our own is the common case, not the
            // edge one.
            var shield = new Unit();
            shield.UnitType = 4;
            shield.ClassId = Items.ClassIdForCode("lrg");
            shield.Quality = ItemQualityNo.Magic;
            shield.ItemFlags = ItemRecordFlags.Identified;
            shield.ItemLevel = 42;
            shield.StatsLists.Add(new UnitStatList(0, ItemStatListFlags.Extended).Add(31, 15));

            string name = Names(Engine.Render(shield, null, Showing()))[0];

            Assert.DoesNotContain("  [ilvl", name);
            Assert.Contains(" [ilvl 42]", name);
        }

        [Fact]
        public void A_set_item_gets_it_too()
        {
            // The set-item builder is a separate compose path; both have to carry the suffix or the
            // option is silently half-implemented.
            var crest = new Unit();
            crest.UnitType = 4;
            crest.ClassId = Items.ClassIdForCode("xsk");
            crest.Quality = ItemQualityNo.Set;
            crest.FileIndex = 80;
            crest.ItemFlags = ItemRecordFlags.Identified;
            crest.ItemLevel = 84;
            crest.StatsLists.Add(new UnitStatList(0, ItemStatListFlags.Extended).Add(31, 76));

            Tooltip tip = Engine.Render(crest, null, Showing());

            Assert.Equal(ItemTooltipKind.IdentifiedSetItem, tip.Kind);
            Assert.Contains("Tal Rasha's Horadric Crest [ilvl 84]", Names(tip));
        }
    }
}
