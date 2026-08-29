using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace D2ItemToolkit.Tests
{
    /// <summary>
    /// <see cref="TooltipEngine.Damage"/>, which returns the numbers the WeaponDamage section
    /// writes rather than the string it writes them into.
    ///
    /// The load-bearing test here is the last one: the API's numbers must be the numbers in the
    /// rendered line, for every shape. The two share <c>DamageValues</c>, but they route to it
    /// separately, and a routing that drifts is exactly the kind of fault a pair of hand-written
    /// tests on each side would each pass while disagreeing with one another.
    /// </summary>
    public class WeaponDamageApiTests
    {
        private static readonly D2DataFiles Data = D2DataFiles.LoadEmbedded();
        private static readonly TooltipEngine Engine = TooltipEngine.Embedded;
        private static readonly ItemTable Items = new ItemTable(Data.Weapons, Data.Armor, Data.Misc);

        /// <summary>Bastard Sword: both `1or2handed` and `2handed`, so it reaches the Barbarian arm.</summary>
        private const string Versatile = "bsw";

        private const int BarbarianClass = 4;
        private const int PaladinClass = 3;

        private static Unit Weapon(string code, params int[] statValue)
        {
            var item = new Unit();
            item.UnitType = 4;
            item.ClassId = Items.ClassIdForCode(code);
            item.ItemFlags = ItemRecordFlags.Identified;
            Assert.True(item.ClassId >= 0, code);

            var list = new UnitStatList(0, ItemStatListFlags.Extended);
            for (int i = 0; i + 1 < statValue.Length; i += 2)
            {
                list.Add(statValue[i], statValue[i + 1]);
            }

            item.StatsLists.Add(list);
            return item;
        }

        private static Unit Player(int classId)
        {
            var player = new Unit();
            player.UnitType = 0;
            player.ClassId = classId;
            return player;
        }

        private static ItemDamageRange Single(ItemDamage damage)
        {
            return Assert.Single(damage.Lines);
        }

        [Fact]
        public void A_one_handed_weapon_yields_one_one_hand_line()
        {
            // Short Sword is 1-handed, so stats 21/22 are the pair and there is nothing else.
            ItemDamageRange line = Single(Engine.Damage(Weapon("ssd", 21, 7, 22, 14)));

            Assert.Equal(ItemDamageKind.OneHand, line.Kind);
            Assert.Equal(7, line.Min);
            Assert.Equal(14, line.Max);
        }

        [Fact]
        public void A_two_handed_weapon_reads_the_secondary_pair()
        {
            // 0x4858f1 picks the pair from items.txt `2handed`. Maul is two-handed only.
            ItemDamageRange line = Single(Engine.Damage(Weapon("mau", 23, 30, 24, 60)));

            Assert.Equal(ItemDamageKind.TwoHand, line.Kind);
            Assert.Equal(30, line.Min);
            Assert.Equal(60, line.Max);
        }

        [Fact]
        public void A_barbarian_gets_both_lines_one_hand_on_top()
        {
            ItemDamage damage = Engine.Damage(
                Weapon(Versatile, 23, 20, 24, 40, 21, 10, 22, 25), Player(BarbarianClass));

            Assert.Equal(
                new[] { ItemDamageKind.OneHand, ItemDamageKind.TwoHand },
                damage.Lines.Select(l => l.Kind).ToArray());

            Assert.Equal(10, damage.Lines[0].Min);
            Assert.Equal(25, damage.Lines[0].Max);
            Assert.Equal(20, damage.Lines[1].Min);
            Assert.Equal(40, damage.Lines[1].Max);
        }

        [Fact]
        public void Anyone_else_holding_the_same_weapon_gets_one_line()
        {
            // The arm is BARBARIAN_CheckItemData_b1or2Handed_isTrue 0x62a1e0 — class 4 alone.
            ItemDamage damage = Engine.Damage(
                Weapon(Versatile, 23, 20, 24, 40, 21, 10, 22, 25), Player(PaladinClass));

            Assert.Equal(ItemDamageKind.TwoHand, Single(damage).Kind);
        }

        [Fact]
        public void The_clamp_applies_to_the_single_line_and_not_to_the_dual_pair()
        {
            // 0x485931 forces max above min; the Barbarian arm at 0x485669 has no such step.
            Assert.Equal(41, Single(Engine.Damage(Weapon(Versatile, 23, 40, 24, 40))).Max);

            ItemDamage dual = Engine.Damage(
                Weapon(Versatile, 23, 40, 24, 40, 21, 15, 22, 15), Player(BarbarianClass));

            Assert.Equal(15, dual.Lines[0].Max);
            Assert.Equal(40, dual.Lines[1].Max);
        }

        [Fact]
        public void A_throwable_weapon_gets_a_throw_line_above_its_own()
        {
            // Throwing Knife. The throw pair is 159/160, appended last and emitted reversed, so it
            // is the TOP row and therefore first here.
            ItemDamage damage = Engine.Damage(Weapon("tkf", 21, 6, 22, 12, 159, 8, 160, 16));

            Assert.Equal(
                new[] { ItemDamageKind.Throw, ItemDamageKind.OneHand },
                damage.Lines.Select(l => l.Kind).ToArray());

            Assert.Equal(8, damage.Lines[0].Min);
            Assert.Equal(16, damage.Lines[0].Max);
        }

        [Fact]
        public void A_throwing_potion_reads_missiles_txt_and_nothing_else()
        {
            // 0x485459 takes the tpot arm outright: no ordinary line and no throw line, and the
            // numbers are the missile's rather than any stat's. Fulminating Potion is missile 44.
            ItemDamageRange line = Single(Engine.Damage(Weapon("opl", 21, 99, 22, 99)));

            Assert.Equal(ItemDamageKind.ThrowingPotion, line.Kind);
            // 5 to 15 in the fire colour. The end-to-end assertion for this item reads
            // `Marker + "15 to " + Marker + "115"`, where the leading 1 of each is the COLOUR
            // digit glued to the number — the concatenation invites exactly that misreading.
            Assert.Equal(5, line.Min);
            Assert.Equal(15, line.Max);
            Assert.False(line.Modified);
        }

        [Fact]
        public void A_non_weapon_has_no_damage_at_all()
        {
            Assert.Empty(Engine.Damage(Weapon("lrg", 21, 10, 22, 20)).Lines);
        }

        [Fact]
        public void A_negative_damage_stat_skips_the_section_but_zero_does_not()
        {
            // 0x48e704 / 0x48e716 gate on >= 0.
            // The gate reads stats 21 and 22, NOT the two-hand pair, even for a two-handed weapon.
            Assert.Empty(Engine.Damage(Weapon("mau", 21, -1)).Lines);

            // Zero passes, and the clamp turns it into 0 to 1.
            ItemDamageRange line = Single(Engine.Damage(Weapon("mau")));
            Assert.Equal(0, line.Min);
            Assert.Equal(1, line.Max);
        }

        [Fact]
        public void Modified_tracks_the_colour_the_line_is_painted()
        {
            // All on the base list, so base == merged and pModified stays clear.
            Assert.False(Single(Engine.Damage(Weapon("mau", 23, 30, 24, 60))).Modified);

            // A magic list puts the merged value above the base one (0x485300).
            Unit enhanced = Weapon("mau", 23, 30, 24, 60);
            enhanced.StatsLists.Add(new UnitStatList(0, ItemStatListFlags.Magic).Add(23, 5));

            Assert.True(Single(Engine.Damage(enhanced)).Modified);
        }

        /// <summary>
        /// The drift guard. Every shape above is rendered as well as measured, and the numbers in
        /// the string have to be the numbers in the API — otherwise one of the two routings has
        /// grown a case the other has not.
        /// </summary>
        [Fact]
        public void The_numbers_are_the_ones_the_rendered_line_shows()
        {
            var cases = new List<KeyValuePair<Unit, Unit>>
            {
                new KeyValuePair<Unit, Unit>(Weapon("ssd", 21, 7, 22, 14), null),
                new KeyValuePair<Unit, Unit>(Weapon("mau", 23, 30, 24, 60), null),
                new KeyValuePair<Unit, Unit>(Weapon("mau"), null),
                new KeyValuePair<Unit, Unit>(Weapon(Versatile, 23, 40, 24, 40), null),
                new KeyValuePair<Unit, Unit>(
                    Weapon(Versatile, 23, 20, 24, 40, 21, 10, 22, 25), Player(BarbarianClass)),
                new KeyValuePair<Unit, Unit>(
                    Weapon(Versatile, 23, 40, 24, 40, 21, 15, 22, 15), Player(BarbarianClass)),
                new KeyValuePair<Unit, Unit>(Weapon("tkf", 21, 6, 22, 12, 159, 8, 160, 16), null),
                new KeyValuePair<Unit, Unit>(Weapon("opl"), null),
            };

            foreach (KeyValuePair<Unit, Unit> testCase in cases)
            {
                int[] fromApi = Engine.Damage(testCase.Key, testCase.Value).Lines
                    .SelectMany(l => new[] { l.Min, l.Max })
                    .ToArray();

                int[] fromText = RenderedDamageNumbers(testCase.Key, testCase.Value);

                Assert.Equal(fromText, fromApi);
            }
        }

        /// <summary>
        /// The damage numbers as the tooltip actually draws them, in display order. A line whose
        /// min equals its max drops the "to max" half (0x4855bd), which is why a missing second
        /// number is read as a repeat rather than as a mismatch.
        /// </summary>
        private static int[] RenderedDamageNumbers(Unit item, Unit viewer)
        {
            var numbers = new List<int>();

            foreach (ItemTooltipLine line in Engine.Render(item, viewer).Lines)
            {
                if (line.Section != ItemTooltipSection.WeaponDamage)
                {
                    continue;
                }

                string text = Regex.Replace(line.Text ?? string.Empty, "ÿc.", string.Empty);

                MatchCollection matched = Regex.Matches(text, @"-?\d+");
                if (matched.Count == 0)
                {
                    continue;
                }

                numbers.Add(int.Parse(matched[0].Value));
                numbers.Add(int.Parse(matched[matched.Count - 1].Value));
            }

            return numbers.ToArray();
        }
    }
}
