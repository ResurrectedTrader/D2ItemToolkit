using System.Linq;
using Xunit;

namespace D2ItemToolkit.Tests
{
    /// <summary>
    /// D2R's worn mask, ITEMS_GetSetItemsMask 0x14022eb70: grid type 3, quality 5, flags 0x4000 and
    /// 0x100 clear (0x14022ec4e-0x14022ec75) — and no identified test, unlike the piece list's
    /// ownership walk (0x1401d3889). An unidentified worn sibling raises a tier but is not owned.
    /// </summary>
    public class ResurrectedSetStateTests
    {
        private static readonly TooltipEngine Engine =
            TooltipEngine.ForVariant(GameVariant.ReignOfTheWarlock);

        private const int SigonsGage = 35;     // hgl, add func 2, aprop1a swing3 30
        private const int SigonsVisor = 36;    // ghm

        private const int BodyHead = 1;
        private const int BodyGloves = 10;

        private static Unit Piece(int setItemRow, int x, bool identified = true)
        {
            var unit = new Unit();
            unit.UnitType = 4;
            unit.Quality = ItemQualityNo.Set;
            unit.ItemFlags = identified ? ItemRecordFlags.Identified : 0;
            unit.FileIndex = setItemRow;
            unit.ClassId = Engine.Items.ClassIdForCode(Engine.Data.SetItems.GetString(setItemRow, "item").Trim());
            unit.Location = 1;
            unit.X = x;
            unit.ItemLevel = 50;
            unit.StatsLists.Add(new UnitStatList(0, ItemStatListFlags.Extended).Add(31, 10).Add(72, 20).Add(73, 20));
            return unit;
        }

        private static Unit Wearer(params Unit[] carried)
        {
            var player = new Unit();
            player.UnitType = 0;
            player.ClassId = 0;
            player.StatsLists.Add(new UnitStatList(0, 0).Add(0, 100).Add(2, 100).Add(12, 60));
            player.Items.AddRange(carried);
            return player;
        }

        [Fact]
        public void An_unidentified_worn_sibling_lights_its_bit_but_is_not_owned()
        {
            Unit gage = Piece(SigonsGage, BodyGloves);
            SetItemTooltipInput state = Engine.SetStateOf(gage, Wearer(gage, Piece(SigonsVisor, BodyHead, false)));

            Assert.Equal(new[] { SigonsGage }, state.OwnedSetItemIds.ToArray());
            Assert.Equal(
                (1 << Engine.Sets.PieceAt(SigonsGage).Slot) | (1 << Engine.Sets.PieceAt(SigonsVisor).Slot),
                state.WornMaskIncludingSelf);
        }

        [Fact]
        public void An_unidentified_worn_sibling_raises_the_first_tier()
        {
            // Tier 0 is state 165; swing3 is stat 93. The record carries the enabled list, and the
            // mask decides whether the writer reaches it.
            Unit gage = Piece(SigonsGage, BodyGloves);
            gage.StatsLists.Add(new UnitStatList(165, ItemStatListFlags.Magic).Add(93, 30));

            string[] worn = Engine.Render(gage, Wearer(gage, Piece(SigonsVisor, BodyHead, false)))
                .ColoredText.Split('\n');
            string[] alone = Engine.Render(gage, Wearer(gage)).ColoredText.Split('\n');

            Assert.Contains("ÿc2+30% Increased Attack Speed", worn);
            Assert.Contains("ÿc1Sigon's Visor", worn);
            Assert.DoesNotContain("ÿc2+30% Increased Attack Speed", alone);
        }

        [Fact]
        public void Lod_keeps_the_identified_test()
        {
            var lod = TooltipEngine.Embedded;
            var gage = new Unit
            {
                UnitType = 4,
                Quality = ItemQualityNo.Set,
                ItemFlags = ItemRecordFlags.Identified,
                FileIndex = SigonsGage,
                Location = 1,
                X = BodyGloves,
                ClassId = lod.Items.ClassIdForCode("hgl"),
            };
            var visor = new Unit
            {
                UnitType = 4,
                Quality = ItemQualityNo.Set,
                FileIndex = SigonsVisor,
                Location = 1,
                X = BodyHead,
                ClassId = lod.Items.ClassIdForCode("ghm"),
            };

            Assert.Equal(1 << lod.Sets.PieceAt(SigonsGage).Slot, lod.SetStateOf(gage, Wearer(visor)).WornMaskIncludingSelf);
        }
    }
}
