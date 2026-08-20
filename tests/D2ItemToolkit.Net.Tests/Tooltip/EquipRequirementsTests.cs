using System.Collections.Generic;
using Xunit;

namespace D2ItemToolkit.Tests
{
    /// <summary>
    /// ITEM_CheckEquipRequirements 0x62eaf0 — the counterpart of EquipRequirements.test.ts. The
    /// class was previously reached only through RecordSections, so the requirement arithmetic and
    /// the three met flags were never driven directly.
    /// </summary>
    public class EquipRequirementsTests
    {
        private static readonly D2DataFiles Data = D2DataFiles.LoadEmbedded();

        private static readonly ItemTable Items = new ItemTable(
            Data.Weapons, Data.Armor, Data.Misc);

        private static readonly EquipRequirements Requirements =
            new EquipRequirements(Data, Items);

        private static ItemIdentity Item(string code)
        {
            return Item(code, ItemRecordFlags.Identified);
        }

        private static ItemIdentity Item(string code, ItemRecordFlags flags)
        {
            var item = new ItemIdentity();
            item.ClassId = Items.ClassIdForCode(code);
            item.Code = code;
            item.Flags = flags;
            return item;
        }

        private static ItemViewer Player(int classId, int strength)
        {
            return Player(classId, strength, 40);
        }

        private static ItemViewer Player(int classId, int strength, int level)
        {
            var viewer = new ItemViewer();
            viewer.UnitType = 0;
            viewer.ClassId = classId;
            viewer.Strength = strength;
            viewer.Level = level;
            return viewer;
        }

        private static Dictionary<int, int> Percent(int value)
        {
            var stats = new Dictionary<int, int>();
            stats[ItemStatReader.PackStatKey(0, 91)] = value;
            return stats;
        }

        [Fact]
        public void The_displayed_requirement_is_the_items_txt_value()
        {
            // Large Shield: reqstr 34 in armor.txt. armor.txt carries no reqdex column at all, so
            // the absent column reads as the loader's 0 rather than a shifted value.
            Assert.Equal(34, Requirements.Requirement(Item("lrg"), "reqstr", null));
            Assert.Equal(0, Requirements.Requirement(Item("lrg"), "reqdex", null));
        }

        [Fact]
        public void Stat_ninety_one_is_applied_as_a_percentage_on_top()
        {
            // 34 + D2ApplyPercent(34, 50, 100) = 34 + 17.
            Assert.Equal(51, Requirements.Requirement(Item("lrg"), "reqstr", Percent(50)));

            // Both sites skip D2ApplyPercent entirely when the percent is zero (0x48e651).
            Assert.Equal(34, Requirements.Requirement(Item("lrg"), "reqstr", Percent(0)));

            // The percentage truncates toward zero: 34 * 33 / 100 = 11.22.
            Assert.Equal(45, Requirements.Requirement(Item("lrg"), "reqstr", Percent(33)));
        }

        [Fact]
        public void An_ethereal_item_discounts_ten()
        {
            ItemIdentity ethereal = Item(
                "lrg", ItemRecordFlags.Identified | ItemRecordFlags.Ethereal);

            Assert.Equal(24, Requirements.Requirement(ethereal, "reqstr", null));
            Assert.Equal(41, Requirements.Requirement(ethereal, "reqstr", Percent(50)));

            // The discount applies to a requirement, not to an absent one: a zero base returns
            // before it and never goes negative.
            Assert.Equal(0, Requirements.Requirement(ethereal, "reqdex", null));
        }

        [Fact]
        public void A_viewer_with_no_strength_at_all_fails()
        {
            // 0x62ebcf: `available > 0` comes first, so a zero-strength viewer fails even a
            // requirement of zero.
            Assert.False(Requirements.MetStrength(Item("lrg"), Player(3, 0), null));
            Assert.False(Requirements.MetStrength(Item("lrg"), null, null));
            Assert.False(Requirements.MetDexterity(Item("lrg"), Player(3, 0), null));
        }

        [Fact]
        public void The_strength_check_is_a_plain_greater_or_equal_against_the_displayed_total()
        {
            Assert.True(Requirements.MetStrength(Item("lrg"), Player(3, 34), null));
            Assert.False(Requirements.MetStrength(Item("lrg"), Player(3, 33), null));

            // Ethereal moves the line and the check together.
            ItemIdentity ethereal = Item(
                "lrg", ItemRecordFlags.Identified | ItemRecordFlags.Ethereal);
            Assert.True(Requirements.MetStrength(ethereal, Player(3, 24), null));
            Assert.False(Requirements.MetStrength(ethereal, Player(3, 23), null));
        }

        [Fact]
        public void The_level_check_compares_the_viewer_level_against_the_calculated_requirement()
        {
            // Large Shield's own items.txt levelreq is 0, so a bare one is met by anyone —
            // including a null viewer, which 0x62ec88 reads as level 0.
            Assert.True(Requirements.MetLevel(Item("lrg"), null, null, null, null));

            // Stat 92 is item_levelreq, which ITEM_CalcRequiredLevel adds on top (0x62ba27).
            var required = new Dictionary<int, int>();
            required[ItemStatReader.PackStatKey(0, 92)] = 25;

            Assert.True(
                Requirements.MetLevel(Item("lrg"), Player(3, 100, 25), required, null, null));
            Assert.False(
                Requirements.MetLevel(Item("lrg"), Player(3, 100, 24), required, null, null));
            Assert.False(Requirements.MetLevel(Item("lrg"), null, required, null, null));
        }

        [Fact]
        public void The_class_restriction_is_the_primary_type_rows_class_column()
        {
            // ItemTypes: shie has a blank Class, head is nec and ashd is pal.
            Assert.Equal(
                EquipRequirements.NoClassRestriction, Requirements.ClassRestriction(Item("lrg")));
            Assert.Equal(2, Requirements.ClassRestriction(Item("ne1")));
            Assert.Equal(3, Requirements.ClassRestriction(Item("pa1")));
        }

        [Fact]
        public void An_unrestricted_item_is_met_by_everyone_including_no_viewer_at_all()
        {
            Assert.True(Requirements.MetClass(Item("lrg"), null));
            Assert.True(Requirements.MetClass(Item("lrg"), Player(6, 0)));
        }

        [Fact]
        public void A_restricted_item_compares_the_class_id_with_no_unit_type_test()
        {
            // 0x48e4a6 compares the player unit's class id straight against the restriction, so a
            // non-player viewer whose class id happens to match reads as met.
            Assert.True(Requirements.MetClass(Item("ne1"), Player(2, 0)));
            Assert.False(Requirements.MetClass(Item("ne1"), Player(3, 0)));
            Assert.False(Requirements.MetClass(Item("ne1"), null));

            var monster = new ItemViewer();
            monster.UnitType = 1;
            monster.ClassId = 2;
            Assert.True(Requirements.MetClass(Item("ne1"), monster));
        }
    }
}
