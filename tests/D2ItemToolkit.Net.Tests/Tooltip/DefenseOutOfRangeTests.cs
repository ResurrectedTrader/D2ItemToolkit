using System.Linq;
using Xunit;

namespace D2ItemToolkit.Tests
{
    /// <summary>
    /// REPORTED, from a screenshot: a Skin of the Vipermagi reading `Defense: 279 [244-277]` — a
    /// value outside the span offered for it. The capture that produced it stores the GAME's own
    /// string, `ÿc0Defense: ÿc3279`, and a base list holding stat 31 = 127.
    ///
    /// 127 is `maxac + 1` for a Serpentskin Armor (111..126), which the roll alone cannot produce:
    /// ITEM_RollBaseArmorClass 0x556360 draws `minac + rand(maxac - minac + 1)` and HALTS the game
    /// if the result exceeds maxac (0x5563b2). The `ac%` property is what puts it there —
    /// see ITEMMOD_MaximizeStatForEnhanced, cited in <see cref="RolledRangeReconstructor"/>.
    ///
    /// So the base does not roll on such an item, and the correct annotation is NONE.
    /// </summary>
    public class DefenseOutOfRangeTests
    {
        private static readonly D2DataFiles Data = D2DataFiles.LoadEmbedded();
        private static readonly TooltipEngine Engine = TooltipEngine.Embedded;
        private static readonly ItemTable Items = new ItemTable(Data.Weapons, Data.Armor, Data.Misc);

        /// <summary>UniqueItems.txt post-splice, 0-based.</summary>
        private const int SkinOfTheVipermagi = 210;

        private const int StatDefense = 31;
        private const int StatArmorPercent = 16;

        /// <summary>armor.txt `xea`: minac 111, maxac 126. The capture's base is 127.</summary>
        private const int SerpentskinMaxAc = 126;

        private static Unit Vipermagi(int baseDefense)
        {
            var armor = new Unit();
            armor.UnitType = 4;
            armor.ClassId = Items.ClassIdForCode("xea");
            armor.Quality = ItemQualityNo.Unique;
            armor.FileIndex = SkinOfTheVipermagi;
            armor.ItemFlags = ItemRecordFlags.Identified;

            armor.StatsLists.Add(
                new UnitStatList(0, ItemStatListFlags.Extended)
                    .Add(StatDefense, baseDefense).Add(72, 22).Add(73, 24));

            // The unique's own `ac% 120..120`, as the record carries it.
            armor.StatsLists.Add(
                new UnitStatList(0, ItemStatListFlags.Magic).Add(StatArmorPercent, 120));

            return armor;
        }

        private static string DefenseLine(Unit item)
        {
            var options = new TooltipOptions();
            options.ShowRolledRanges = true;
            options.RangeColor = -1;

            return Engine.Render(item, null, options).Lines
                .Select(l => System.Text.RegularExpressions.Regex
                    .Replace(l.Text ?? string.Empty, "ÿc.", string.Empty).TrimEnd('\n'))
                .Single(t => t.StartsWith("Defense:"));
        }

        [Fact]
        public void An_enhanced_defense_item_has_a_fixed_base_and_so_no_span()
        {
            // The captured record, rendered: the game's own number, and no annotation because
            // nothing about this Defense could have rolled differently.
            Unit captured = Vipermagi(SerpentskinMaxAc + 1);

            Assert.Equal("Defense: 279", DefenseLine(captured));

            RolledStatRange defense = Engine.Ranges(captured).Stats
                .Single(r => r.StatId == StatDefense && r.Layer == 0);

            Assert.False(defense.IsRange, "an `ac%` item's Defense does not roll");
            Assert.Equal(279, defense.Low);
            Assert.Equal(279, defense.High);
            Assert.Empty(Engine.Ranges(captured).OutOfRange);
        }

        [Fact]
        public void A_base_the_maximise_could_not_have_left_behind_is_reported()
        {
            // maxac itself is now OUT of range, because the maximise always lands one above it.
            // Before this was traced the span was the raw 111..126 roll, so this record looked
            // perfectly ordinary and the real one — 127 — looked broken. Exactly backwards.
            Unit impossible = Vipermagi(SerpentskinMaxAc);

            Assert.Equal("Defense: 277", DefenseLine(impossible));
            Assert.Contains(StatDefense, Engine.Ranges(impossible).OutOfRange);
        }

        [Fact]
        public void Without_an_armour_percent_the_base_still_rolls()
        {
            // The maximise is reached only through `ac%`. Strip it and the span is the ordinary
            // armor.txt roll again, so this is the control that stops the fix over-applying.
            Unit plain = Vipermagi(120);
            plain.StatsLists.RemoveAt(1);
            plain.FileIndex = -1;
            plain.Quality = ItemQualityNo.Normal;

            RolledStatRange defense = Engine.Ranges(plain).Stats
                .Single(r => r.StatId == StatDefense && r.Layer == 0);

            Assert.True(defense.IsRange);
            Assert.Equal(111, defense.Low);
            Assert.Equal(126, defense.High);
        }

        /// <summary>
        /// KNOWN FAILURE, kept so the gap is not forgotten.
        ///
        /// Magefist is a Battle Gauntlets (39..47) carrying `ac 10..10` at prop5 and `ac% 20..30`
        /// at prop6. Everything traced says the `ac%` must maximise it to 48: the dispatch table at
        /// 0x745b58 gives row 5 `ac%` PropertyFunc_SimpleStatWrapper2, which passes the enhanced
        /// flag as 1 (0x65d2be), and ITEMMOD_ApplyRandomStatValue then maximises unconditionally
        /// (0x65cf52). A War Traveler is a `boot` with the IDENTICAL 39..47 range and one `ac%`,
        /// and it IS 48 in the same capture.
        ///
        /// But the captured Magefist's base is 45, and the game's own string agrees —
        /// `Defense: 68` is 45 + 10 + 45 * 29 / 100. Magefist is the only one of the five with a
        /// flat `ac` alongside its `ac%`, which is a correlation and not a mechanism, so nothing is
        /// coded for it. Un-skip once the discriminator is traced.
        /// </summary>
        [Fact(Skip = "Untraced: an `ac%` that provably maximises elsewhere does not here.")]
        public void Magefist_keeps_its_rolled_base_despite_carrying_an_armour_percent()
        {
            var glove = new Unit();
            glove.UnitType = 4;
            glove.ClassId = Items.ClassIdForCode("xtg");
            glove.Quality = ItemQualityNo.Unique;
            glove.FileIndex = 105;
            glove.ItemFlags = ItemRecordFlags.Identified;

            glove.StatsLists.Add(
                new UnitStatList(0, ItemStatListFlags.Extended)
                    .Add(StatDefense, 45).Add(72, 18).Add(73, 18));
            glove.StatsLists.Add(
                new UnitStatList(0, ItemStatListFlags.Magic)
                    .Add(StatArmorPercent, 29).Add(StatDefense, 10));

            Assert.Equal("Defense: 68", DefenseLine(glove));
            Assert.Empty(Engine.Ranges(glove).OutOfRange);
        }
    }
}
