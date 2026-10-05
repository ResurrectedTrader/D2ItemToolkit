using System.Linq;
using Xunit;

namespace D2ItemToolkit.Tests
{
    /// <summary>
    /// D2R's mastery terms: stat 203 on the strength/dexterity requirement
    /// (SKILLS_GetWeaponMasteryBonus 0x14024d610, its throwing arm 0x14024d380, the dual-melee test
    /// 0x140239c30) and stat 209 on the level requirement (0x140228913-0x140228aa0).
    ///
    /// Levitate is the one shipped stat-203 source, at layer `weap | 1 &lt;&lt; 14`; the dual-melee
    /// and stat-209 cases are hand-built, because no shipped viewer reaches them.
    /// </summary>
    public class WeaponMasteryTests
    {
        private static readonly TooltipEngine Engine =
            TooltipEngine.ForVariant(GameVariant.ReignOfTheWarlock);

        private const int Levitate = 0x402D;          // weap (45), condition 1: not dual-wielding
        private const int WhileDualMelee = 0x802D;    // weap, condition 2: only dual-wielding

        private static string[] Lines(IUnit item, IUnit viewer)
        {
            return Engine.Render(item, viewer).Text.Split('\n');
        }

        private static Unit Item(string code, int stat92 = 0)
        {
            var item = new Unit();
            item.UnitType = 4;
            item.ClassId = Engine.Items.ClassIdForCode(code);
            item.ItemFlags = ItemRecordFlags.Identified;
            item.ItemLevel = 50;
            Assert.True(item.ClassId >= 0, code);

            var stats = new UnitStatList(0, ItemStatListFlags.Extended).Add(72, 30).Add(73, 30);
            if (stat92 != 0)
            {
                stats.Add(92, stat92);
            }

            item.StatsLists.Add(stats);
            return item;
        }

        private static Unit Player(int classId, int statId, int value, int layer, int lastUsedSkill = -1)
        {
            var player = new Unit();
            player.UnitType = 0;
            player.ClassId = classId;
            player.LastUsedSkill = lastUsedSkill;
            player.StatsLists.Add(
                new UnitStatList(0, 0).Add(0, 100).Add(2, 100).Add(12, 90).Add(statId, value, layer));
            return player;
        }

        private static Unit Warlock(int lastUsedSkill = -1)
        {
            return Player(7, WeaponMastery.StatItemRequirementPercent, -20, Levitate, lastUsedSkill);
        }

        private static void Wield(Unit player, string code, int bodyLocation, ItemRecordFlags extra = 0)
        {
            Unit weapon = Item(code);
            weapon.ItemFlags |= extra;
            weapon.Location = 1;
            weapon.X = bodyLocation;
            player.Items.Add(weapon);
        }

        [Theory]
        [InlineData(-1, "Required Dexterity: 17")]   // no last-used skill: the generic arm, -20%
        [InlineData(0, "Required Dexterity: 17")]    // Attack: itypea1 is not a throwing type
        [InlineData(2, "Required Dexterity: 21")]    // Throw engages the arm; 16429 matches nothing
        [InlineData(15, "Required Dexterity: 21")]   // Poison Javelin
        public void A_throwing_skill_takes_the_throwing_arm_which_returns_even_zero(
            int lastUsedSkill, string expected)
        {
            Assert.Contains(expected, Lines(Item("tkf"), Warlock(lastUsedSkill)));
        }

        [Theory]
        [InlineData(-1, 20)]
        [InlineData(2, 25)]
        public void A_javelin_takes_the_arm_on_both_requirements(int lastUsedSkill, int expected)
        {
            string[] lines = Lines(Item("9ja"), Warlock(lastUsedSkill));
            Assert.Contains("Required Strength: " + expected, lines);
            Assert.Contains("Required Dexterity: " + expected, lines);
        }

        [Fact]
        public void A_weapon_that_is_not_throwable_never_takes_the_arm()
        {
            string[] lines = Lines(Item("sbr"), Warlock(2));
            Assert.Contains("Required Strength: 20", lines);
            Assert.Contains("Required Dexterity: 20", lines);
        }

        [Fact]
        public void The_met_flag_follows_the_arm()
        {
            Unit warlock = Player(7, WeaponMastery.StatItemRequirementPercent, -20, Levitate, 2);
            warlock.StatsLists[0].Stats.First(s => s.Id == 2).Value = 18;

            Assert.Contains(
                "ÿc1Required Dexterity: 21",
                Engine.Render(Item("tkf"), warlock).ColoredText.Split('\n'));
            warlock.LastUsedSkill = -1;
            Assert.Contains(
                "ÿc0Required Dexterity: 17",
                Engine.Render(Item("tkf"), warlock).ColoredText.Split('\n'));
        }

        public static TheoryData<int, string, int, ItemRecordFlags, string> DualMeleeCases()
        {
            return new TheoryData<int, string, int, ItemRecordFlags, string>
            {
                { Levitate, "scm", 5, 0, "Required Strength: 25" },
                { Levitate, "scm", 5, ItemRecordFlags.Broken, "Required Strength: 20" },
                { Levitate, "scm", 12, 0, "Required Strength: 20" },
                { WhileDualMelee, "scm", 5, 0, "Required Strength: 20" },
                { WhileDualMelee, null, 5, 0, "Required Strength: 25" },
                { Levitate, "lrg", 5, 0, "Required Strength: 20" },
            };
        }

        [Theory]
        [MemberData(nameof(DualMeleeCases))]
        public void Dual_melee_is_two_clean_melee_weapons_in_the_active_hands(
            int layer, string offHand, int offHandLocation, ItemRecordFlags offHandFlags, string expected)
        {
            Unit barbarian = Player(4, WeaponMastery.StatItemRequirementPercent, -20, layer);
            Wield(barbarian, "sbr", 4);
            if (offHand != null)
            {
                Wield(barbarian, offHand, offHandLocation, offHandFlags);
            }

            Assert.Contains(expected, Lines(Item("sbr"), barbarian));
        }

        [Theory]
        [InlineData(0, -25, "Required Level: 30")]
        [InlineData(45, -25, "Required Level: 40")]   // weap, and a ring is not one
        public void Stat_209_scales_the_finished_level(int layer, int percent, string expected)
        {
            Unit viewer = Player(1, WeaponMastery.StatItemLevelRequirementPercent, percent, layer);
            Assert.Contains(expected, Lines(Item("rin", 40), viewer));
        }

        [Fact]
        public void Stat_209_is_not_clamped_so_a_full_discount_hides_the_line()
        {
            Unit viewer = Player(1, WeaponMastery.StatItemLevelRequirementPercent, -100, 0);
            Assert.DoesNotContain(Lines(Item("rin", 40), viewer), l => l.StartsWith("Required Level"));
        }

        [Fact]
        public void Stat_209_compounds_through_a_socket_filler()
        {
            // Ber's levelreq 63 becomes 63 + (63 * -50) / 100 = 32 inside the recursion, the cap
            // takes it as its own and applies the term again: 32 - 16 = 16. Applying it once would
            // give 32.
            Unit helm = Item("cap");
            helm.ItemFlags |= ItemRecordFlags.Socketed;
            helm.Items.Add(Item("r30"));

            Unit viewer = Player(1, WeaponMastery.StatItemLevelRequirementPercent, -50, 0);
            Assert.Contains("Required Level: 16", Lines(helm, viewer));
        }

        [Fact]
        public void Reads_lastUsedSkill_off_the_wire_and_defaults_it_to_minus_one()
        {
            Assert.Equal(2, UnitJson.Read("{ \"lastUsedSkill\": 2 }").LastUsedSkill);
            Assert.Equal(-1, UnitJson.Read("{}").LastUsedSkill);
        }

        [Fact]
        public void Lod_ignores_both_stats()
        {
            Unit viewer = Player(1, WeaponMastery.StatItemLevelRequirementPercent, -25, 0);
            viewer.StatsLists[0].Add(WeaponMastery.StatItemRequirementPercent, -20);
            Unit ring = Item("rin", 40);
            ring.ClassId = TooltipEngine.Embedded.Items.ClassIdForCode("rin");

            Assert.Contains("Required Level: 40", TooltipEngine.Embedded.Render(ring, viewer).Text.Split('\n'));
        }
    }
}
