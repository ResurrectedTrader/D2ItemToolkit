using System.Collections.Generic;
using Xunit;

namespace D2ItemToolkit.Tests
{
    /// <summary>
    /// ITEM_CalcRequiredLevel 0x62b5b0, driven from the embedded tables.
    /// </summary>
    public class RequiredLevelTests
    {
        private static readonly D2DataFiles Data = D2DataFiles.LoadEmbedded();

        private static readonly ItemTable Items = new ItemTable(
            Data.Weapons, Data.Armor, Data.Misc);

        private static readonly RequiredLevelCalculator Calculator =
            new RequiredLevelCalculator(Data, Items);

        private static ItemIdentity Item(string code, int quality, int fileIndex = -1)
        {
            var item = new ItemIdentity();
            item.ClassId = Items.ClassIdForCode(code);
            item.Code = code;
            item.Quality = quality;
            item.FileIndex = fileIndex;
            item.Flags = ItemRecordFlags.Identified;
            return item;
        }

        private static int Level(
            ItemIdentity item,
            ItemViewer viewer = null,
            IDictionary<int, int> stats = null,
            IDictionary<int, uint> sockets = null,
            IList<ItemUnit> socketUnits = null)
        {
            return Calculator.Calculate(item, viewer, stats, socketUnits, sockets);
        }

        private static Dictionary<int, int> Stats(params int[] layerStatValue)
        {
            var stats = new Dictionary<int, int>();
            for (int i = 0; i + 2 < layerStatValue.Length + 1; i += 3)
            {
                stats[ItemStatReader.PackStatKey(layerStatValue[i], layerStatValue[i + 1])] =
                    layerStatValue[i + 2];
            }

            return stats;
        }

        [Fact]
        public void A_unique_takes_its_uniqueitems_level()
        {
            // UniqueItems row 0 is The Gnasher, "lvl req" 5, on a Hand Axe whose levelreq is 0.
            Assert.Equal(5, Level(Item("hax", ItemQualityNo.Unique, 0)));
        }

        [Fact]
        public void A_set_item_takes_its_setitems_level()
        {
            // SetItems row 0 is Civerb's Ward, "lvl req" 9, on a Large Shield.
            Assert.Equal(9, Level(Item("lrg", ItemQualityNo.Set, 0)));
        }

        [Fact]
        public void A_classic_unique_hides_its_level_from_a_non_expansion_viewer()
        {
            ItemIdentity item = Item("hax", ItemQualityNo.Unique, 0);
            item.Format = 0;

            var viewer = new ItemViewer();
            viewer.UnitType = 0;
            viewer.ClassId = 3;
            viewer.FlagsEx = 0;

            Assert.Equal(0, Level(item, viewer));

            viewer.FlagsEx = ItemViewer.UnitFlagExpansion;
            Assert.Equal(5, Level(item, viewer));
        }

        [Fact]
        public void A_magic_item_takes_the_highest_of_its_two_affixes()
        {
            // The magic array is 1-based over [MagicSuffix][MagicPrefix][automagic], so id 66 is
            // suffix row 65 — "of Regeneration", levelreq 52 and no class restriction.
            ItemIdentity item = Item("lrg", ItemQualityNo.Magic);
            item.MagicSuffix[0] = 66;

            Assert.Equal(52, Level(item));
        }

        [Fact]
        public void A_magic_item_ignores_affix_slots_one_and_two()
        {
            // GetMagicPrefix/GetMagicSuffix are called with index 0 only (0x62b5f2).
            ItemIdentity item = Item("lrg", ItemQualityNo.Magic);
            item.MagicSuffix[1] = 66;
            item.MagicSuffix[2] = 66;

            Assert.Equal(0, Level(item));
        }

        [Fact]
        public void A_rare_item_reads_every_affix_slot()
        {
            ItemIdentity item = Item("lrg", ItemQualityNo.Rare);
            item.MagicSuffix[2] = 66;

            Assert.Equal(52, Level(item));
        }

        [Fact]
        public void A_crafted_item_adds_ten_plus_three_for_each_affix()
        {
            ItemIdentity item = Item("lrg", ItemQualityNo.Craft);
            item.MagicSuffix[0] = 66;

            // 52 + 10 + 3 for the one affix that resolves.
            Assert.Equal(65, Level(item));
        }

        [Fact]
        public void A_crafted_item_is_capped_one_below_the_maximum_character_level()
        {
            ItemIdentity item = Item("lrg", ItemQualityNo.Craft);

            // Suffix row 339 is "of Vita", levelreq 97 — the highest in the table. Six affixes take
            // the raw total to 97 + 10 + 18 = 125, well past the ceiling.
            for (int slot = 0; slot < ItemIdentity.MaxAffixSlots; ++slot)
            {
                item.MagicSuffix[slot] = 340;
                item.MagicPrefix[slot] = 340;
            }

            // experience.txt MaxLvl is 99 and the cap is one below it (0x62b848).
            Assert.Equal(98, Level(item));

            // The cap applies to the crafted subtotal only; stat 92 is added afterwards at 0x62ba27.
            Assert.Equal(98 + 5, Level(item, null, Stats(0, 92, 5)));
        }

        [Fact]
        public void The_items_table_level_is_a_floor()
        {
            // Find any item whose own levelreq is non-zero and check it shows through.
            for (int classId = 0; classId < Items.RowCount; ++classId)
            {
                int required = Items.RequiredLevel(classId);
                if (required <= 1)
                {
                    continue;
                }

                var item = new ItemIdentity();
                item.ClassId = classId;
                item.Quality = ItemQualityNo.Normal;
                Assert.Equal(required, Level(item));
                return;
            }

            Assert.Fail("no items row carries a level requirement");
        }

        [Fact]
        public void Stat_ninety_two_is_added_on_top()
        {
            ItemIdentity item = Item("hax", ItemQualityNo.Unique, 0);

            Assert.Equal(5 + 7, Level(item, null, Stats(0, 92, 7)));
        }

        [Fact]
        public void A_negative_total_clamps_to_zero()
        {
            ItemIdentity item = Item("hax", ItemQualityNo.Unique, 0);

            Assert.Equal(0, Level(item, null, Stats(0, 92, -50)));
        }

        [Fact]
        public void A_socketed_filler_raises_the_host_requirement()
        {
            ItemIdentity item = Item("lrg", ItemQualityNo.Normal);

            int filler = -1;
            int fillerLevel = 0;
            for (int classId = 0; classId < Items.RowCount; ++classId)
            {
                if (Items.RequiredLevel(classId) > 1)
                {
                    filler = classId;
                    fillerLevel = Items.RequiredLevel(classId);
                    break;
                }
            }

            Assert.True(filler >= 0);

            var sockets = new SortedDictionary<int, uint> { { 0, (uint)filler } };
            Assert.Equal(fillerLevel, Level(item, null, null, sockets));
        }

        [Fact]
        public void An_off_class_granted_skill_costs_six_extra_levels()
        {
            int skill = FirstClassSkill();
            int skillClass = Data.Skills.GetSkillClass(skill);
            int reqLevel = Data.Skills.RequiredLevel(skill);

            ItemIdentity item = Item("lrg", ItemQualityNo.Normal);

            var stranger = new ItemViewer();
            stranger.UnitType = 0;
            stranger.ClassId = skillClass == 0 ? 1 : 0;

            var owner = new ItemViewer();
            owner.UnitType = 0;
            owner.ClassId = skillClass;

            // Stat 97 is item_nonclassskill; the LAYER carries the skill id.
            Dictionary<int, int> stats = Stats(skill, 97, 1);

            Assert.Equal(reqLevel + 6, Level(item, stranger, stats));
            Assert.Equal(reqLevel, Level(item, owner, stats));
        }

        [Fact]
        public void A_single_skill_never_takes_the_off_class_penalty()
        {
            int skill = FirstClassSkill();
            int reqLevel = Data.Skills.RequiredLevel(skill);

            ItemIdentity item = Item("lrg", ItemQualityNo.Normal);

            // Stat 107 is item_singleskill, read at 0x62b927 with no class comparison at all.
            Assert.Equal(reqLevel, Level(item, null, Stats(skill, 107, 1)));
        }

        [Fact]
        public void A_magic_jewel_in_a_socket_raises_the_hosts_requirement()
        {
            // 0x62b901 recurses the WHOLE calculation into every filler, so the jewel's own
            // quality affixes count. The concatenated magic array is 1-based over
            // [MagicSuffix][MagicPrefix][automagic], so a suffix row's id is its index plus one.
            int suffix = Data.MagicSuffix.FindRow("Name", "of Transcendence");
            Assert.True(suffix >= 0);
            Assert.Equal(68, Data.MagicSuffix.GetInt(suffix, "levelreq"));

            ItemIdentity host = Item("lrg", ItemQualityNo.Normal);

            var jewel = new ItemIdentity();
            jewel.ClassId = Items.ClassIdForCode("jew");
            jewel.Quality = ItemQualityNo.Magic;
            jewel.MagicSuffix[0] = suffix + 1;

            var fillers = new List<ItemUnit> { new ItemUnit(jewel) };

            Assert.Equal(68, Level(host, socketUnits: fillers));

            // The classId-only view cannot see the affix, which is exactly the degradation the
            // richer overload exists to avoid.
            var byClassId = new SortedDictionary<int, uint> { { 0, (uint)jewel.ClassId } };
            Assert.Equal(
                Items.RequiredLevel(jewel.ClassId), Level(host, sockets: byClassId));
        }

        [Fact]
        public void A_socketed_gem_still_contributes_its_items_txt_level()
        {
            ItemIdentity host = Item("lrg", ItemQualityNo.Normal);

            var gem = new ItemIdentity();
            gem.ClassId = Items.ClassIdForCode("gpv");   // perfect amethyst

            var fillers = new List<ItemUnit> { new ItemUnit(gem) };

            Assert.Equal(Items.RequiredLevel(gem.ClassId), Level(host, socketUnits: fillers));
            Assert.True(Items.RequiredLevel(gem.ClassId) > 1);
        }

        [Fact]
        public void A_fillers_stat_92_reaches_the_host()
        {
            // The recursion adds the filler's OWN stat 92 (0x62ba27) before the max is taken.
            ItemIdentity host = Item("lrg", ItemQualityNo.Normal);

            var filler = new ItemIdentity();
            filler.ClassId = Items.ClassIdForCode("jew");

            var stats = new Dictionary<int, int>();
            stats[ItemStatReader.PackStatKey(0, 92)] = 55;

            Assert.Equal(
                55, Level(host, socketUnits: new List<ItemUnit> { new ItemUnit(filler, stats) }));
        }

        private static int FirstClassSkill()
        {
            for (int skill = 0; skill < Data.Skills.RowCount; ++skill)
            {
                int skillClass = Data.Skills.GetSkillClass(skill);
                if (skillClass >= 0 && skillClass <= 6 && Data.Skills.RequiredLevel(skill) > 1)
                {
                    return skill;
                }
            }

            Assert.Fail("no class skill with a level requirement");
            return -1;
        }
    }
}
