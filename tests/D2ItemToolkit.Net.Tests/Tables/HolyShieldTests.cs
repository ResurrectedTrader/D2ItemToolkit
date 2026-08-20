using System.Collections.Generic;
using Xunit;

namespace D2ItemToolkit.Tests
{
    /// <summary>
    /// Holy Shield's contribution to smite damage (0x485df1) and block chance (0x485c58), computed
    /// from skills.txt rather than supplied by the producer.
    /// </summary>
    public class HolyShieldTests
    {
        private static readonly D2DataFiles Data = D2DataFiles.LoadEmbedded();

        private static readonly ItemTable Items = new ItemTable(
            Data.Weapons, Data.Armor, Data.Misc);

        private static readonly ItemTypeTree Types = new ItemTypeTree(Data.ItemTypes);

        private static readonly SkillDamage Skills = new SkillDamage(Data.SkillRows);

        [Fact]
        public void Skill_one_hundred_and_seventeen_is_holy_shield()
        {
            Assert.Equal(
                "Holy Shield", Data.SkillRows.GetString(SkillDamage.HolyShieldSkillId, "skill"));
        }

        [Theory]
        // slope [2,3,4,4,4] from MinLevDam1..5. Level 1 contributes nothing at all.
        [InlineData(1, 0)]
        [InlineData(5, 8)]      // 2 * (5 - 1)
        [InlineData(8, 14)]     // 2 * 7
        [InlineData(10, 20)]    // 7*2 + 3*(10-8)
        [InlineData(20, 54)]    // 7*2 + 4*(20-16) + 8*3
        [InlineData(25, 74)]    // 7*2 + 4*(25-22) + 6*4 + 8*3 = 14+12+24+24
        [InlineData(30, 94)]    // 7*2 + 4*(30-28) + 6*(4+4) + 8*3 = 14+8+48+24
        public void The_level_breakpoint_ramp_matches_the_five_slopes(int level, int expected)
        {
            Assert.Equal(
                expected, SkillDamage.LevelBreakpoints(level, new[] { 2, 3, 4, 4, 4 }));
        }

        [Fact]
        public void Holy_shield_damage_is_table_arithmetic_with_no_calc_string()
        {
            int min;
            int max;
            Assert.True(Skills.TryCalcDamage(SkillDamage.HolyShieldSkillId, 10, out min, out max));

            // MinDam 3 and MaxDam 6, plus the ramp, all shifted left by HitShift 8. The smite writer
            // shifts back down by 8, so the visible bonus is 3 + 20 and 6 + 20.
            Assert.Equal((3 + 20) << 8, min);
            Assert.Equal((6 + 20) << 8, max);
        }

        [Theory]
        // param5 10, param6 40. ramp = level*110/(level+6); v = 10 + ramp*30/100, capped at 40.
        [InlineData(1, 14)]
        [InlineData(10, 30)]
        [InlineData(20, 35)]
        [InlineData(60, 40)]
        public void The_block_bonus_blends_param_five_into_param_six(int level, int expected)
        {
            Assert.Equal(
                expected, Skills.ParamWithDiminishing(SkillDamage.HolyShieldSkillId, level));
        }

        [Fact]
        public void Level_zero_yields_no_block_bonus()
        {
            Assert.Equal(0, Skills.ParamWithDiminishing(SkillDamage.HolyShieldSkillId, 0));
        }

        [Fact]
        public void A_skill_that_needs_a_calc_string_is_refused_rather_than_approximated()
        {
            var refused = new List<int>();

            for (int skill = 0; skill < Data.SkillRows.RowCount; ++skill)
            {
                if (!Skills.TryCalcDamage(skill, 10, out _, out _))
                {
                    refused.Add(skill);
                }
            }

            // Plenty of skills DO use SrcDam or a calc; the point is that they are refused, and that
            // Holy Shield is not among them.
            Assert.NotEmpty(refused);
            Assert.DoesNotContain(SkillDamage.HolyShieldSkillId, refused);
        }

        [Fact]
        public void An_active_holy_shield_raises_smite_damage_and_block_chance()
        {
            string bare = Describe(0, false);
            string buffed = Describe(10, true);

            // Bone Shield: shields have mindam/maxdam in armor.txt for the smite line.
            Assert.NotEqual(bare, buffed);
            Assert.Contains("Smite Damage", buffed, System.StringComparison.Ordinal);
        }

        [Fact]
        public void An_inactive_holy_shield_contributes_nothing_even_at_high_level()
        {
            Assert.Equal(Describe(0, false), Describe(40, false));
        }

        private static string Describe(int holyShieldLevel, bool active)
        {
            var item = new ItemIdentity();
            item.ClassId = Items.ClassIdForCode("lrg");
            item.Code = "lrg";
            item.Flags = ItemRecordFlags.Identified;

            var viewer = new ItemViewer();
            viewer.UnitType = 0;
            viewer.ClassId = 3;
            viewer.Level = 40;
            viewer.Skills[SkillDamage.HolyShieldSkillId] = holyShieldLevel;
            if (active)
            {
                viewer.ActiveStates.Add(SkillDamage.HolyShieldState);
            }

            var stats = new Dictionary<int, int>();
            stats[ItemStatReader.PackStatKey(0, 20)] = 25;

            var sections = new RecordSections(
                Data, Items, Types, item, viewer, stats, null, null, null);

            return sections.GetSection(ItemTooltipSection.SmiteOrKickDamage)
                   + "|" + sections.GetSection(ItemTooltipSection.BlockChance);
        }

        // =================================================================
        // 0x48e768/0x48e778: a class-restricted shield whose Class is not Paladin never gets the
        // smite line. `head` (Voodoo Heads) is Equiv1=shld with Class=nec.
        // =================================================================

        private static string SmiteFor(string code)
        {
            var item = new ItemIdentity();
            item.ClassId = Items.ClassIdForCode(code);
            item.Code = code;
            item.Flags = ItemRecordFlags.Identified;
            Assert.True(item.ClassId >= 0, "no items row for " + code);

            var viewer = new ItemViewer();
            viewer.UnitType = 0;
            viewer.ClassId = 3;      // Paladin

            var sections = new RecordSections(
                Data, Items, Types, item, viewer, new Dictionary<int, int>(), null, null, null);

            return sections.GetSection(ItemTooltipSection.SmiteOrKickDamage);
        }

        [Theory]
        [InlineData("ne1")]
        [InlineData("ne9")]
        [InlineData("nef")]
        public void A_paladin_gets_no_smite_line_on_a_necromancer_head(string code)
        {
            Assert.Null(SmiteFor(code));
        }

        [Fact]
        public void An_unrestricted_shield_still_smites()
        {
            Assert.NotNull(SmiteFor("lrg"));
        }

        [Fact]
        public void A_paladin_restricted_shield_still_smites()
        {
            // ashd (Auric Shields, pa1..paf) is Class=pal, so the restriction matches and the
            // line stays — the gate drops the line only when the class is present and NOT Paladin.
            Assert.NotNull(SmiteFor("pa1"));
            Assert.NotNull(SmiteFor("paf"));
        }

        [Fact]
        public void Every_voodoo_head_is_refused()
        {
            var offenders = new List<string>();

            foreach (string code in new[]
            {
                "ne1", "ne2", "ne3", "ne4", "ne5", "ne6", "ne7", "ne8",
                "ne9", "nea", "neb", "neg", "ned", "nee", "nef",
            })
            {
                if (SmiteFor(code) != null)
                {
                    offenders.Add(code);
                }
            }

            Assert.Empty(offenders);
        }
    }
}
