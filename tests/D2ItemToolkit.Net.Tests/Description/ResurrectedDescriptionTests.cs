using Xunit;

namespace D2ItemToolkit.Tests
{
    /// <summary>
    /// ITEMSTATDESC_Build 0x1401eba60 on D2R data, where the line text lives in printf formats.
    /// </summary>
    public class ResurrectedDescriptionTests
    {
        private static readonly TooltipEngine Engine =
            TooltipEngine.ForVariant(GameVariant.ReignOfTheWarlock);

        private static string Modifiers(int classId, params int[] statLayerValue)
        {
            var ring = new Unit();
            ring.UnitType = 4;
            ring.ClassId = Engine.Items.ClassIdForCode("rin");
            ring.Quality = ItemQualityNo.Rare;
            ring.ItemFlags = ItemRecordFlags.Identified;

            var list = new UnitStatList(0, ItemStatListFlags.Magic);
            for (int at = 0; at < statLayerValue.Length; at += 3)
            {
                list.Add(statLayerValue[at], statLayerValue[at + 2], statLayerValue[at + 1]);
            }

            ring.StatsLists.Add(list);

            var viewer = new Unit();
            viewer.UnitType = 0;
            viewer.ClassId = classId;
            viewer.StatsLists.Add(new UnitStatList(0, 0).Add(0, 100).Add(2, 100).Add(12, 60));

            return Engine.Render(ring, viewer).Text;
        }

        private static int Skill(string name)
        {
            TxtFile skills = Engine.Data.SkillRows;
            for (int row = 0; row < skills.RowCount; ++row)
            {
                if (skills.GetString(row, "skill") == name)
                {
                    return row;
                }
            }

            return -1;
        }

        [Fact]
        public void Per_level_func_19_appends_its_descstr2()
        {
            // (8 * level 60) >> 3, then " " + increaseswithplaylevelX (0x1401ecb3a).
            Assert.Contains("+60 Defense (Based on Character Level)", Modifiers(0, 214, 0, 8));
        }

        [Fact]
        public void Func_29_picks_the_string_by_sign_then_prints_the_magnitude()
        {
            Assert.Contains("Physical Damage Received Reduced by 10%", Modifiers(0, 36, 0, 10));
            Assert.Contains("Physical Damage Received Increased by 15%", Modifiers(0, 36, 0, -15));
        }

        [Fact]
        public void The_own_class_oskill_clamp_is_dead()
        {
            int battleOrders = Skill("Battle Orders");
            Assert.Contains("+6 to Battle Orders", Modifiers(4, 97, battleOrders, 6));
            Assert.Contains("+6 to Battle Orders", Modifiers(0, 97, battleOrders, 6));
        }

        [Fact]
        public void A_warlock_class_skill_keeps_its_class_line()
        {
            Assert.Contains(
                "+2 to Levitation Mastery (Warlock Only)", Modifiers(7, 107, Skill("Levitate"), 2));
        }

        [Fact]
        public void Class_skill_levels_format_the_charstats_string()
        {
            Assert.Contains("+1 to Warlock Skills", Modifiers(7, 83, 7, 1));
            Assert.Contains("+2 to Barbarian Skill Levels", Modifiers(4, 83, 4, 2));
        }

        [Fact]
        public void Howl_formats_its_own_string()
        {
            Assert.Contains("Hit Causes Monster to Flee +50%", Modifiers(0, 112, 0, 64));
        }

        [Fact]
        public void The_damage_aggregate_uses_the_d2r_keys()
        {
            // Each aggregate line ends with the `newline` key the game appends (0x1401ea91e); the
            // rare name above it is " " (1718 with two empty affixes) over the base.
            Assert.Equal(" \nRing\nAdds 5-10 fire damage", Modifiers(0, 48, 0, 5, 49, 0, 10));
            Assert.Equal(" \nRing\n+7 fire damage", Modifiers(0, 48, 0, 7, 49, 0, 7));
            Assert.Equal(
                " \nRing\nAdds 75-150 poison damage over 3 seconds",
                Modifiers(0, 57, 0, 256, 58, 0, 512, 59, 0, 75));
            Assert.Contains("+5 to Minimum Fire Damage", Modifiers(0, 48, 0, 5));
        }
    }
}
