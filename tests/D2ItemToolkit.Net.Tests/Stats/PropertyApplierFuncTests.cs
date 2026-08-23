using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace D2ItemToolkit.Tests
{
    /// <summary>
    /// The property handlers behind dword_7462F8 (0x65eb30..0x65fae0), one func at a time.
    ///
    /// SocketFillerTests only sweeps the funcs that gems.txt happens to reach, which leaves the
    /// damage handlers and their weapon-shape arms unexercised. Every property below is a REAL
    /// properties.txt row, so the func/stat/set triples come from shipped data rather than being
    /// made up; only the {param, min, max} quadruple is supplied, exactly as a gems.txt mod would.
    /// </summary>
    public class PropertyApplierFuncTests
    {
        private static readonly D2DataFiles Data = D2DataFiles.LoadEmbedded();

        private static readonly ItemTable Items = new ItemTable(
            Data.Weapons, Data.Armor, Data.Misc);

        private static readonly ItemTypeTree Types = new ItemTypeTree(Data.ItemTypes);

        private const int StatMinDamage = 21;
        private const int StatMaxDamage = 22;
        private const int StatSecondaryMinDamage = 23;
        private const int StatSecondaryMaxDamage = 24;
        private const int StatMaxDamagePercent = 17;
        private const int StatMinDamagePercent = 18;
        private const int StatThrowMinDamage = 159;
        private const int StatThrowMaxDamage = 160;
        private const int StatPoisonMaxDamage = 58;
        private const int StatPoisonCount = 326;
        private const int StatIndestructible = 152;

        private static PropertyApplier Applier()
        {
            return new PropertyApplier(Data, Items, Types);
        }

        private static ItemIdentity Item(string code)
        {
            var item = new ItemIdentity();
            item.ClassId = Items.ClassIdForCode(code);
            item.Code = code;
            Assert.True(item.ClassId >= 0, "no items row for " + code);
            return item;
        }

        private static ItemProperty Property(
            PropertyApplier applier, string code, int param, int min, int max)
        {
            var property = new ItemProperty();
            property.PropertyId = applier.Properties.RowForCode(code);
            Assert.True(property.PropertyId >= 0, "no properties row for " + code);
            property.Param = param;
            property.Min = min;
            property.Max = max;
            return property;
        }

        /// <summary>Applies one property to a bare item and returns the resulting stats.</summary>
        private static Dictionary<int, int> Apply(
            string code, string itemCode, int min, int max, int param = 0)
        {
            var into = new Dictionary<int, int>();
            PropertyApplier applier = Applier();
            applier.Apply(
                PropertyApplier.PropModeGem, Item(itemCode),
                Property(applier, code, param, min, max), into);

            return into;
        }

        private static int? Stat(Dictionary<int, int> stats, int statId)
        {
            int value;
            return stats.TryGetValue(ItemStatReader.PackStatKey(0, statId), out value)
                ? (int?)value
                : null;
        }

        /// <summary>The stat ids present, ascending — the assertion that nothing EXTRA was written.</summary>
        private static int[] Ids(Dictionary<int, int> stats)
        {
            return stats.Keys.Select(ItemStatReader.StatFromKey).OrderBy(id => id).ToArray();
        }

        private static int[] Expected(string ids)
        {
            return ids.Split(',').Select(int.Parse).OrderBy(id => id).ToArray();
        }

        [Fact]
        public void Func_one_rolls_once_and_writes_the_row_stat()
        {
            // "ac" is a lone func 1 onto stat 31.
            Dictionary<int, int> stats = Apply("ac", "cap", 12, 12);

            Assert.Equal(new[] { 31 }, Ids(stats));
            Assert.Equal(12, Stat(stats, 31));
        }

        [Fact]
        public void Func_three_reuses_the_value_func_one_already_rolled()
        {
            // "res-all" is func [1,3,3,3] onto the four single resistances. The whole point of
            // func 3 is that it does NOT roll again — all four must come out identical.
            Dictionary<int, int> stats = Apply("res-all", "cap", 30, 30);

            Assert.Equal(new[] { 39, 41, 43, 45 }, Ids(stats));
            foreach (int id in new[] { 39, 41, 43, 45 })
            {
                Assert.Equal(30, Stat(stats, id));
            }
        }

        [Fact]
        public void Func_two_writes_the_percentage_stat()
        {
            Dictionary<int, int> stats = Apply("ac%", "cap", 15, 15);

            Assert.Equal(new[] { 16 }, Ids(stats));
            Assert.Equal(15, Stat(stats, 16));
        }

        [Fact]
        public void Func_eight_writes_its_row_stat_like_func_one()
        {
            // "swing1" is func 8 onto stat 93 (increased attack speed).
            Dictionary<int, int> stats = Apply("swing1", "cap", 20, 20);

            Assert.Equal(new[] { 93 }, Ids(stats));
            Assert.Equal(20, Stat(stats, 93));
        }

        [Fact]
        public void Func_twenty_writes_indestructible_unshifted_at_one()
        {
            Dictionary<int, int> stats = Apply("indestruct", "cap", 0, 0);

            Assert.Equal(new[] { StatIndestructible }, Ids(stats));
            Assert.Equal(1, Stat(stats, StatIndestructible));
        }

        [Fact]
        public void Func_seventeen_prefers_the_param_over_the_range()
        {
            // "ac/lvl" is func 17 onto stat 214. Param wins outright — the range is never read.
            Dictionary<int, int> stats = Apply("ac/lvl", "cap", 3, 3, 7);

            Assert.Equal(new[] { 214 }, Ids(stats));
            Assert.Equal(7, Stat(stats, 214));
        }

        [Fact]
        public void Func_seventeen_falls_back_to_the_range_when_the_param_is_zero()
        {
            Assert.Equal(4, Stat(Apply("ac/lvl", "cap", 4, 4), 214));
        }

        [Fact]
        public void A_stat_with_a_nonzero_valshift_is_stored_shifted_left()
        {
            // stat 216 (hp/lvl) carries ValShift 8. The description engine shifts back down, so an
            // unshifted store would render as 1/256th of the real value.
            StatDescriptor descriptor;
            Assert.True(Data.ItemStatCost.TryGetStat(216, out descriptor));
            Assert.Equal(8, descriptor.ValShift);

            Assert.Equal(4 << 8, Stat(Apply("hp/lvl", "cap", 0, 0, 4), 216));
        }

        [Fact]
        public void A_zero_value_writes_no_stat_at_all()
        {
            // 0x65ea50 returns before touching the list when the value is zero, so a property that
            // rolls nothing leaves no trace rather than an explicit zero.
            Assert.Empty(Apply("ac", "cap", 0, 0));
        }

        [Fact]
        public void An_equal_range_needs_no_seed()
        {
            var into = new Dictionary<int, int>();
            PropertyApplier applier = Applier();
            applier.Apply(
                PropertyApplier.PropModeGem, Item("cap"),
                Property(applier, "ac", 0, 9, 9), into);

            Assert.Equal(9, Stat(into, 31));
            Assert.Empty(applier.RolledRanges);
        }

        [Theory]
        // A genuine range resolves to its low end; an inverted one is swapped first (0x65eb6a),
        // so the low end is the SMALLER of the two either way.
        [InlineData(5, 40)]
        [InlineData(40, 5)]
        public void A_genuine_range_resolves_to_its_low_end_and_is_reported(int min, int max)
        {
            var into = new Dictionary<int, int>();
            PropertyApplier applier = Applier();
            ItemProperty property = Property(applier, "ac", 0, min, max);
            applier.Apply(PropertyApplier.PropModeGem, Item("cap"), property, into);

            Assert.Equal(5, Stat(into, 31));
            Assert.Equal(new[] { property.PropertyId }, applier.RolledRanges.ToArray());
        }

        // Funcs 5 and 6 pick which of the three damage stats to write from the item's own damage
        // columns, so the same property lands differently on a one-hander, a pure two-hander, a
        // versatile weapon, a throwable and a non-weapon. This matrix separates the six arms.
        [Theory]
        // A non-weapon fails every weapon test, so all three destinations are written.
        [InlineData("cap", "21,23,159")]
        // One-handed only: mindam is set, 2handmindam is not.
        [InlineData("axe", "21")]
        // Two-handed only: mindam is zero, so the primary arm is skipped entirely.
        [InlineData("bax", "23")]
        // Versatile: both columns are set, so both arms fire.
        [InlineData("2hs", "21,23")]
        // Throwable: the missile arm joins the one-handed arm.
        [InlineData("tkf", "21,159")]
        public void Func_five_writes_only_the_damage_stats_that_item_can_carry(
            string code, string expected)
        {
            Dictionary<int, int> stats = Apply("dmg-min", code, 6, 6);

            Assert.Equal(Expected(expected), Ids(stats));
            foreach (int id in Expected(expected))
            {
                Assert.Equal(6, Stat(stats, id));
            }
        }

        [Theory]
        [InlineData("cap", "22,24,160")]
        [InlineData("axe", "22")]
        [InlineData("bax", "24")]
        [InlineData("2hs", "22,24")]
        [InlineData("tkf", "22,160")]
        public void Func_six_mirrors_func_five_across_the_max_stats(string code, string expected)
        {
            Dictionary<int, int> stats = Apply("dmg-max", code, 9, 9);

            Assert.Equal(Expected(expected), Ids(stats));
            foreach (int id in Expected(expected))
            {
                Assert.Equal(9, Stat(stats, id));
            }
        }

        [Fact]
        public void Func_five_floors_the_total_at_one_rather_than_at_zero()
        {
            // axe has mindam 4. A -10 would take it to -6, so the value is clamped to 1 - 4 = -3,
            // leaving a displayed minimum of exactly 1.
            Assert.Equal(4, Items.GetInt(Items.ClassIdForCode("axe"), "mindam"));

            Assert.Equal(-3, Stat(Apply("dmg-min", "axe", -10, -10), StatMinDamage));
        }

        [Fact]
        public void Func_six_floors_the_total_at_zero_not_at_one()
        {
            // The one place funcs 5 and 6 are not mirror images: axe has maxdam 11, and -20 clamps
            // to 0 - 11 rather than 1 - 11.
            Assert.Equal(11, Items.GetInt(Items.ClassIdForCode("axe"), "maxdam"));

            Assert.Equal(-11, Stat(Apply("dmg-max", "axe", -20, -20), StatMaxDamage));
        }

        [Fact]
        public void A_clamp_never_engages_without_a_base_damage_column()
        {
            // cap has no damage columns at all, so baseDamage is zero and the raw negative is
            // written straight through.
            Assert.Equal(-20, Stat(Apply("dmg-max", "cap", -20, -20), StatMaxDamage));
        }

        [Fact]
        public void Enhanced_damage_writes_the_percentage_pair_when_the_bonus_survives()
        {
            // axe maxdam 11, so 50% is 5 — a real increase, and the percentages stand.
            Dictionary<int, int> stats = Apply("dmg%", "axe", 50, 50);

            Assert.Equal(new[] { StatMaxDamagePercent, StatMinDamagePercent }, Ids(stats));
            Assert.Equal(50, Stat(stats, StatMinDamagePercent));
            Assert.Equal(50, Stat(stats, StatMaxDamagePercent));
        }

        [Fact]
        public void Enhanced_damage_degrades_to_plus_one_max_when_the_percentage_rounds_away()
        {
            // 5% of 11 truncates to 0, so on a WEAPON the percentage pair would be worthless and
            // the handler substitutes func 6 with a value of 1 instead.
            Dictionary<int, int> stats = Apply("dmg%", "axe", 5, 5);

            Assert.Equal(new[] { StatMaxDamage }, Ids(stats));
            Assert.Equal(1, Stat(stats, StatMaxDamage));
        }

        [Fact]
        public void Enhanced_damage_never_degrades_on_a_non_weapon()
        {
            // cap has maxdam 0, so the bonus is 0 — but the weapon test fails first, so the
            // percentages are written as-is.
            Dictionary<int, int> stats = Apply("dmg%", "cap", 5, 5);

            Assert.Equal(new[] { StatMaxDamagePercent, StatMinDamagePercent }, Ids(stats));
            Assert.Equal(5, Stat(stats, StatMinDamagePercent));
        }

        [Fact]
        public void Enhanced_damage_scales_off_the_larger_of_the_two_damage_columns()
        {
            // 2hs has maxdam 9 and 2handmaxdam 17. 6% of 17 is 1, but 6% of 9 is 0 — so reading
            // the one-handed column alone would wrongly degrade this to +1 max damage.
            Assert.Equal(9, Items.GetInt(Items.ClassIdForCode("2hs"), "maxdam"));
            Assert.Equal(17, Items.GetInt(Items.ClassIdForCode("2hs"), "2handmaxdam"));

            Assert.Equal(6, Stat(Apply("dmg%", "2hs", 6, 6), StatMinDamagePercent));
        }

        [Fact]
        public void Func_fifteen_and_sixteen_write_the_elemental_pair_directly()
        {
            // "dmg-fire" is func [15,16] onto stats 48 and 49. Neither is a physical damage stat,
            // so both go straight to the stat list with no weapon-shape routing.
            Dictionary<int, int> stats = Apply("dmg-fire", "cap", 3, 14);

            Assert.Equal(new[] { 48, 49 }, Ids(stats));
            Assert.Equal(3, Stat(stats, 48));
            // Func 16 takes nMax, not nMin — the one asymmetry between the two.
            Assert.Equal(14, Stat(stats, 49));
        }

        [Fact]
        public void Physical_damage_routes_back_through_the_weapon_shape_handlers()
        {
            // "dmg-norm" is func [15,16] onto stats 21 and 22, which ARE the damage stats — so on
            // a one-hander the secondary and throwing arms must stay empty.
            Dictionary<int, int> stats = Apply("dmg-norm", "axe", 3, 14);

            Assert.Equal(new[] { StatMinDamage, StatMaxDamage }, Ids(stats));
            Assert.Equal(3, Stat(stats, StatMinDamage));
            Assert.Equal(14, Stat(stats, StatMaxDamage));
        }

        [Fact]
        public void Physical_damage_fans_across_every_arm_on_a_non_weapon()
        {
            Dictionary<int, int> stats = Apply("dmg-norm", "cap", 3, 14);

            Assert.Equal(Expected("21,22,23,24,159,160"), Ids(stats));
            Assert.Equal(3, Stat(stats, StatSecondaryMinDamage));
            Assert.Equal(14, Stat(stats, StatSecondaryMaxDamage));
            Assert.Equal(14, Stat(stats, StatThrowMaxDamage));
        }

        [Fact]
        public void Throwing_damage_is_written_without_routing()
        {
            // "dmg-throw" targets stats 159 and 160 by name. Func 15 only reroutes on stat 21, so
            // these are written directly even though they are damage stats.
            Dictionary<int, int> stats = Apply("dmg-throw", "axe", 2, 8);

            Assert.Equal(new[] { StatThrowMinDamage, StatThrowMaxDamage }, Ids(stats));
            Assert.Equal(2, Stat(stats, StatThrowMinDamage));
            Assert.Equal(8, Stat(stats, StatThrowMaxDamage));
        }

        [Fact]
        public void Poison_damage_drags_a_duration_along_with_it()
        {
            // "dmg-pois" is func [15,16,17] onto stats 57, 58 and 59. Writing stat 58 pulls stat
            // 326 with it, or the description reads "over 0 seconds".
            Dictionary<int, int> stats = Apply("dmg-pois", "cap", 10, 20, 75);

            Assert.Equal(10, Stat(stats, 57));
            Assert.Equal(20, Stat(stats, StatPoisonMaxDamage));
            Assert.Equal(1, Stat(stats, StatPoisonCount));
            // Func 17 on stat 59 takes the param — the poison's length.
            Assert.Equal(75, Stat(stats, 59));
        }

        [Fact]
        public void The_poison_duration_accumulates_once_per_application()
        {
            // Two poison gems in two sockets each add their own count, matching the ADD arm
            // at 0x65eb0a.
            var into = new Dictionary<int, int>();
            PropertyApplier applier = Applier();
            ItemIdentity item = Item("cap");
            ItemProperty property = Property(applier, "dmg-pois", 75, 10, 20);

            applier.Apply(PropertyApplier.PropModeGem, item, property, into);
            applier.Apply(PropertyApplier.PropModeGem, item, property, into);

            Assert.Equal(40, Stat(into, StatPoisonMaxDamage));
            Assert.Equal(2, Stat(into, StatPoisonCount));
        }

        [Fact]
        public void No_duration_is_written_when_there_is_no_poison_damage()
        {
            Assert.Null(Stat(Apply("dmg-fire", "cap", 3, 14), StatPoisonCount));
        }

        // ---- the seven funcs the affix, unique, set, runeword and cube tables reach -------------

        private static int? Layered(Dictionary<int, int> stats, int layer, int statId)
        {
            int value;
            return stats.TryGetValue(ItemStatReader.PackStatKey(layer, statId), out value)
                ? (int?)value
                : null;
        }

        [Theory]
        // Amazon's three tabs are params 0..2, so class 0 packs flat.
        [InlineData(0, 0)]
        [InlineData(1, 1)]
        [InlineData(2, 2)]
        // Param 3 is the next class's first tab, and the stride jumps to 8 rather than 3.
        [InlineData(3, 8)]
        [InlineData(5, 10)]
        // 20 is the highest param shipped — assassin's third tab.
        [InlineData(20, 50)]
        public void Func_ten_packs_the_skill_tab_as_class_times_eight_plus_tab(int param, int layer)
        {
            // `skilltab` is a lone func 10 onto stat 188, and 148 cells across MagicPrefix,
            // automagic, UniqueItems, SetItems and Runes reach it.
            Dictionary<int, int> stats = Apply("skilltab", "cap", 2, 2, param);

            Assert.Equal(new[] { 188 }, Ids(stats));
            Assert.Equal(2, Layered(stats, layer, 188));
        }

        [Fact]
        public void Func_twelve_rolls_the_layer_and_takes_the_value_from_the_param()
        {
            // Ormus' Robes, the only shipped `skill-rand`: par=3, min=36, max=60. The +3 is the
            // VALUE and the rolled skill id is the LAYER — 36..60 being the sorceress skills — so
            // the low end of the roll lands on 36.
            Dictionary<int, int> stats = Apply("skill-rand", "uui", 36, 60, 3);

            Assert.Equal(new[] { 107 }, Ids(stats));
            Assert.Equal(3, Layered(stats, 36, 107));

            // Not the other way round: a value of 36 at layer 3 is the bug this pins.
            Assert.Null(Layered(stats, 3, 107));
        }

        [Fact]
        public void Func_thirty_six_rolls_the_layer_and_takes_the_value_from_val()
        {
            // Hellfire Torch, the only shipped `randclassskill`: min=0, max=6 over the seven
            // classes, and the value comes from Properties.txt val1, which is 3.
            Dictionary<int, int> stats = Apply("randclassskill", "cm2", 0, 6);

            Assert.Equal(new[] { 83 }, Ids(stats));
            Assert.Equal(3, Layered(stats, 0, 83));
        }

        [Fact]
        public void Func_twenty_three_writes_no_stat_at_all()
        {
            // `ethereal` has a blank stat1: the handler flips a flag and applies the ethereal
            // bonus, so nothing reaches a stat list. Ethereal Edge is one of the five users.
            Assert.Empty(Apply("ethereal", "7ba", 0, 0));
        }

        [Fact]
        public void Func_eighteen_packs_both_ends_into_one_value()
        {
            // "of Dawn" is `ac/time` 10..40 onto stat 268. Both ends are biased by +256 and packed
            // as param + 4 * ((max << 10) + min), so the stat carries its own range.
            Dictionary<int, int> stats = Apply("ac/time", "cap", 10, 40);

            Assert.Equal(new[] { 268 }, Ids(stats));

            const int Low = 10 + 256;
            const int High = 40 + 256;
            Assert.Equal(4 * ((High << 10) + Low), Stat(stats, 268));

            // And the packing is recoverable, which is the whole point of the encoding.
            int? written = Stat(stats, 268);
            Assert.NotNull(written);
            int packed = written.GetValueOrDefault();
            Assert.Equal(0, packed & 3);
            Assert.Equal(10, ((packed >> 2) & 0x3FF) - 256);
            Assert.Equal(40, ((packed >> 12) & 0x3FF) - 256);
        }

        [Fact]
        public void Func_eighteen_biases_a_negative_end_rather_than_dropping_it()
        {
            // "of Sunlight" is the other shipped user and its min is NEGATIVE (-10..60), which the
            // +256 bias is there to carry.
            Dictionary<int, int> stats = Apply("ac/time", "cap", -10, 60);

            int? written = Stat(stats, 268);
            Assert.NotNull(written);
            int packed = written.GetValueOrDefault();
            Assert.Equal(-10, ((packed >> 2) & 0x3FF) - 256);
            Assert.Equal(60, ((packed >> 12) & 0x3FF) - 256);
        }

        [Fact]
        public void Func_nineteen_packs_the_charge_pair_and_the_skill_level()
        {
            // Hellfire Torch's `charged`: param is the skill (Hydra, 62), min the charge count and
            // max the level. Level 30 comes straight from max, the value packs
            // (maxCharges << 8) + current, and the layer packs (skill << 6) + level.
            Dictionary<int, int> stats = Apply("charged", "cm2", 10, 30, 62);

            Assert.Equal(new[] { 204 }, Ids(stats));

            const int Skill = 62;
            const int Level = 30;
            const int MaxCharges = 10;

            // Low end of the seed-drawn current count is maxCharges / 8 + 1.
            Assert.Equal(
                (MaxCharges << 8) + (MaxCharges / 8 + 1),
                Layered(stats, (Skill << 6) + Level, 204));
        }

        [Fact]
        public void Func_nineteen_resolves_the_current_charges_to_the_requested_end()
        {
            // The current count is the only seed-drawn part, so the two ends bracket it while the
            // high byte — the max charges — stays put.
            var into = new Dictionary<int, int>();
            var high = new PropertyApplier(Data, Items, Types, RollEnd.High);
            high.Apply(
                PropertyApplier.PropModeGem, Item("cm2"),
                Property(high, "charged", 62, 10, 30), into);

            Assert.Equal((10 << 8) + 10, Layered(into, (62 << 6) + 30, 204));
        }

        [Fact]
        public void Func_nineteen_reports_the_arms_that_need_an_item_level()
        {
            // A non-positive max derives the level from the item's own level (0x65f70a / 0x65f75b),
            // which a record does not carry — so it takes the game's floor of 1 and is reported.
            var into = new Dictionary<int, int>();
            PropertyApplier applier = Applier();
            ItemProperty property = Property(applier, "charged", 62, 10, 0);
            applier.Apply(PropertyApplier.PropModeGem, Item("cm2"), property, into);

            Assert.Contains(property.PropertyId, applier.ItemLevelDependent);
            Assert.Equal((10 << 8) + 2, Layered(into, (62 << 6) + 1, 204));
        }

        [Fact]
        public void Func_fourteen_caps_the_socket_count_by_the_items_own_limits()
        {
            // A cap is 2x2, so the footprint allows 4, but items.txt gemsockets is 2 — so a roll
            // of 3 clamps to 2 and lands on stat 194 as a SET.
            Dictionary<int, int> stats = Apply("sock", "cap", 3, 3);

            Assert.Equal(new[] { 194 }, Ids(stats));
            Assert.Equal(2, Stat(stats, 194));
        }

        [Fact]
        public void Func_fourteen_writes_nothing_on_a_base_that_takes_no_sockets()
        {
            // mbl (heavy boots) has gemsockets 0, so ITEM_GetMaxSockCount returns min(0, tier) = 0
            // whatever the item level, the cap collapses to 0 and 0x65f679 falls through to write
            // nothing. Getting this wrong puts sockets on every pair of boots.
            Assert.Empty(Apply("sock", "mbl", 3, 3));
        }

        [Fact]
        public void Func_fourteen_reports_that_the_tier_cap_needs_an_item_level()
        {
            // ITEM_GetMaxSockCount 0x62bc20 picks MaxSock1/25/40 by item level, which a record does
            // not carry, so that half of the cap is reported rather than guessed.
            var into = new Dictionary<int, int>();
            PropertyApplier applier = Applier();
            ItemProperty property = Property(applier, "sock", 0, 3, 3);
            applier.Apply(PropertyApplier.PropModeGem, Item("cap"), property, into);

            Assert.Contains(property.PropertyId, applier.ItemLevelDependent);
        }

        [Fact]
        public void None_of_the_seven_reports_itself_unsupported_any_more()
        {
            foreach (var probe in new[]
            {
                new { Code = "skilltab", Item = "cap", Min = 1, Max = 1, Param = 0 },
                new { Code = "skill-rand", Item = "uui", Min = 36, Max = 60, Param = 3 },
                new { Code = "randclassskill", Item = "cm2", Min = 0, Max = 6, Param = 0 },
                new { Code = "ethereal", Item = "7ba", Min = 0, Max = 0, Param = 0 },
                new { Code = "ac/time", Item = "cap", Min = 10, Max = 40, Param = 0 },
                new { Code = "charged", Item = "cm2", Min = 10, Max = 30, Param = 62 },
                new { Code = "sock", Item = "cap", Min = 3, Max = 3, Param = 0 },
            })
            {
                var into = new Dictionary<int, int>();
                PropertyApplier applier = Applier();
                applier.Apply(
                    PropertyApplier.PropModeGem, Item(probe.Item),
                    Property(applier, probe.Code, probe.Param, probe.Min, probe.Max), into);

                Assert.Empty(applier.UnsupportedFunc);
            }
        }
    }
}
