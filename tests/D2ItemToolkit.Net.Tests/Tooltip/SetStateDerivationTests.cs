using System.Linq;
using Xunit;

namespace D2ItemToolkit.Tests
{
    /// <summary>
    /// Deriving a set item's tooltip state from what the VIEWER carries, instead of asking the
    /// caller for two bit masks it cannot compute without the disassembly.
    ///
    /// The distinction these tests exist for: GetSetItem 0x486770 accepts inventory grid types 1, 3
    /// AND 4 (0x4867d4), so a piece on the alternate weapon set is OWNED and colours green — but
    /// ITEMS_GetEquippedSetItemsMask requires type 3 alone (0x62a3f0), so it lights no bit and
    /// raises no bonus tier. A caller that treated "equipped" as one thing would light one tier too
    /// many, which is the failure this API removes.
    /// </summary>
    public class SetStateDerivationTests
    {
        private static readonly D2DataFiles Data = D2DataFiles.LoadEmbedded();
        private static readonly TooltipEngine Engine = TooltipEngine.Embedded;

        private static readonly ItemTable Items = new ItemTable(
            Data.Weapons, Data.Armor, Data.Misc);

        // The Angelic Raiment pieces, setitems.txt rows. Four pieces over four body slots, which is
        // what lets a worn/swapped distinction be built at all.
        private const int AngelicSickle = 50;   // sbr, a sword
        private const int AngelicMantle = 51;   // rng, body armour
        private const int AngelicHalo = 52;     // rin, a ring
        private const int AngelicWings = 53;    // amu, an amulet

        // A piece of a DIFFERENT set, to prove the scan filters by set id. Arctic HORN and not
        // Arctic Binding: Binding is slot 2 inside Arctic Gear, the same slot Angelic Halo holds,
        // so a stray bit would OR into one already set and the test could not see the filter go.
        private const int ArcticHorn = 54;      // swb, a bow — slot 0

        private const int LocationEquipped = 1;
        private const int LocationInventory = 3;

        /// <summary>Body locations. 11 and 12 are the alternate weapon set (0x55f240).</summary>
        private const int BodyRightArm = 4;
        private const int BodyTorso = 3;
        private const int BodyRightArmSwap = 11;
        private const int BodyLeftArmSwap = 12;

        private static Unit Piece(int setItemRow, int location, int x = 0)
        {
            string code = Data.SetItems.GetString(setItemRow, "item").Trim();

            var unit = new Unit();
            unit.UnitType = 4;
            unit.Quality = ItemQualityNo.Set;
            unit.ItemFlags = ItemRecordFlags.Identified;
            unit.FileIndex = setItemRow;
            unit.ClassId = Items.ClassIdForCode(code);
            unit.Location = location;
            unit.X = x;

            Assert.True(unit.ClassId >= 0, "no item row for " + code);
            return unit;
        }

        private static Unit Wearer(params Unit[] carried)
        {
            var player = new Unit();
            player.UnitType = 0;
            player.Items.AddRange(carried);
            return player;
        }

        /// <summary>The bit a piece contributes, which is its slot INSIDE the set (0x62a474).</summary>
        private static int Bit(int setItemRow)
        {
            return 1 << Engine.Sets.PieceAt(setItemRow).Slot;
        }

        [Fact]
        public void The_hovered_piece_counts_itself_even_when_the_viewer_lists_nothing()
        {
            // Nothing obliges a caller to repeat the hovered item inside the viewer's items — "what
            // else the player is carrying" is the natural reading of a list passed alongside it. So
            // the hovered piece's own state comes from the ITEM. Here it is identified and in the
            // inventory, which is where GetSetItem looks, so its own row is owned; it is not worn,
            // so no bit lights.
            SetItemTooltipInput state = Engine.SetStateOf(
                Piece(AngelicHalo, LocationInventory), Wearer());

            Assert.Equal(new[] { AngelicHalo }, state.OwnedSetItemIds.ToArray());
            Assert.Equal(0, state.WornMaskIncludingSelf);
            Assert.Equal(0, state.WornMaskExcludingSelf);
            Assert.False(state.IsEquipped);
        }

        [Fact]
        public void A_worn_hovered_piece_lights_its_bit_without_being_listed()
        {
            // The regression this guards: taking the mask only from the viewer's list dropped the
            // hovered piece's own tier whenever the caller did not repeat it. Two worn pieces, one
            // of them the hovered item and absent from the list — both bits must be set.
            Unit halo = Piece(AngelicHalo, LocationEquipped, 6);

            SetItemTooltipInput listed = Engine.SetStateOf(halo, Wearer(
                halo, Piece(AngelicMantle, LocationEquipped, BodyTorso)));

            SetItemTooltipInput unlisted = Engine.SetStateOf(halo, Wearer(
                Piece(AngelicMantle, LocationEquipped, BodyTorso)));

            Assert.Equal(Bit(AngelicHalo) | Bit(AngelicMantle), unlisted.WornMaskIncludingSelf);
            Assert.Equal(listed.WornMaskIncludingSelf, unlisted.WornMaskIncludingSelf);
            Assert.Equal(listed.WornMaskExcludingSelf, unlisted.WornMaskExcludingSelf);
        }

        [Fact]
        public void Carried_siblings_are_owned_whether_or_not_they_are_worn()
        {
            Unit halo = Piece(AngelicHalo, LocationEquipped, 6);

            SetItemTooltipInput state = Engine.SetStateOf(halo, Wearer(
                halo,
                Piece(AngelicWings, LocationInventory),
                Piece(AngelicMantle, LocationEquipped, BodyTorso)));

            Assert.Equal(
                new[] { AngelicMantle, AngelicHalo, AngelicWings },
                state.OwnedSetItemIds.OrderBy(id => id).ToArray());
        }

        [Fact]
        public void A_piece_of_another_set_contributes_nothing()
        {
            Unit halo = Piece(AngelicHalo, LocationEquipped, 6);

            SetItemTooltipInput state = Engine.SetStateOf(halo, Wearer(
                halo, Piece(ArcticHorn, LocationEquipped, 8)));

            Assert.Equal(new[] { AngelicHalo }, state.OwnedSetItemIds.ToArray());
            Assert.Equal(Bit(AngelicHalo), state.WornMaskIncludingSelf);
        }

        [Fact]
        public void A_worn_sibling_lights_its_slot_bit()
        {
            Unit halo = Piece(AngelicHalo, LocationEquipped, 6);

            SetItemTooltipInput state = Engine.SetStateOf(halo, Wearer(
                halo, Piece(AngelicMantle, LocationEquipped, BodyTorso)));

            Assert.Equal(Bit(AngelicHalo) | Bit(AngelicMantle), state.WornMaskIncludingSelf);

            // Excluding-self differs in exactly the hovered piece's bit, never in any other.
            Assert.Equal(Bit(AngelicMantle), state.WornMaskExcludingSelf);
        }

        [Fact]
        public void A_piece_on_the_alternate_weapon_set_is_owned_but_lights_no_bit()
        {
            // THE case this API exists for. The sickle sits on body location 11, so
            // INVENTORY_PlaceItemInGrid stamps grid type 4 (0x63b1e2): GetSetItem still takes it, so
            // it colours green, but the worn mask does not, so it raises no tier.
            Unit halo = Piece(AngelicHalo, LocationEquipped, 6);

            SetItemTooltipInput state = Engine.SetStateOf(halo, Wearer(
                halo, Piece(AngelicSickle, LocationEquipped, BodyRightArmSwap)));

            Assert.Contains(AngelicSickle, state.OwnedSetItemIds);
            Assert.Equal(Bit(AngelicHalo), state.WornMaskIncludingSelf);

            // The same sickle in the ACTIVE weapon slot does light its bit — so the difference is
            // the body location and nothing else about the record.
            SetItemTooltipInput active = Engine.SetStateOf(halo, Wearer(
                halo, Piece(AngelicSickle, LocationEquipped, BodyRightArm)));

            Assert.Equal(Bit(AngelicHalo) | Bit(AngelicSickle), active.WornMaskIncludingSelf);
        }

        [Fact]
        public void Both_swap_slots_are_owned_but_light_no_bit()
        {
            // The boundary is `bodyLoc >= 11`, so BOTH 11 and 12 are the swap pair. Testing 11 alone
            // left `>= 11` narrowable to `== 11` — which makes a set shield or off-hand on the swap
            // bar light a bit and raise a tier — and to `>= 10`, which stops a worn GLOVE granting
            // its own. Both survived the whole suite until this case existed.
            Unit halo = Piece(AngelicHalo, LocationEquipped, 6);

            foreach (int swap in new[] { BodyRightArmSwap, BodyLeftArmSwap })
            {
                SetItemTooltipInput state = Engine.SetStateOf(halo, Wearer(
                    halo, Piece(AngelicSickle, LocationEquipped, swap)));

                Assert.Contains(AngelicSickle, state.OwnedSetItemIds);
                Assert.Equal(Bit(AngelicHalo), state.WornMaskIncludingSelf);
            }

            // ...and the slot BELOW the boundary is worn, which is the other half of `>= 11`.
            const int BodyGloves = 10;

            Assert.Equal(
                Bit(AngelicHalo) | Bit(AngelicSickle),
                Engine.SetStateOf(halo, Wearer(
                    halo, Piece(AngelicSickle, LocationEquipped, BodyGloves)))
                    .WornMaskIncludingSelf);
        }

        [Fact]
        public void An_unequippable_piece_grants_no_bonus()
        {
            // The mask's OTHER refusal, flag 0x4000 (0x62a446). Dropping it from the mask left every
            // test green, because only the Broken half was ever exercised.
            const uint CannotEquip = 0x4000;

            Unit halo = Piece(AngelicHalo, LocationEquipped, 6);

            Unit blocked = Piece(AngelicMantle, LocationEquipped, BodyTorso);
            blocked.ItemFlags = (ItemRecordFlags)((uint)blocked.ItemFlags | CannotEquip);

            SetItemTooltipInput state = Engine.SetStateOf(halo, Wearer(halo, blocked));

            Assert.Contains(AngelicMantle, state.OwnedSetItemIds);
            Assert.Equal(Bit(AngelicHalo), state.WornMaskIncludingSelf);
        }

        [Fact]
        public void An_inventory_piece_is_owned_but_lights_no_bit()
        {
            Unit halo = Piece(AngelicHalo, LocationEquipped, 6);

            SetItemTooltipInput state = Engine.SetStateOf(halo, Wearer(
                halo, Piece(AngelicWings, LocationInventory)));

            Assert.Contains(AngelicWings, state.OwnedSetItemIds);
            Assert.Equal(Bit(AngelicHalo), state.WornMaskIncludingSelf);
        }

        [Fact]
        public void An_unidentified_sibling_is_not_owned()
        {
            // GetSetItem requires CheckItemFlag 0x10 (0x4867a2). Every set item drops unidentified,
            // so a sibling just picked up is the normal case — the game paints its row red.
            Unit halo = Piece(AngelicHalo, LocationEquipped, 6);

            Unit unidentified = Piece(AngelicWings, LocationInventory);
            unidentified.ItemFlags = 0;

            SetItemTooltipInput state = Engine.SetStateOf(halo, Wearer(halo, unidentified));

            Assert.DoesNotContain(AngelicWings, state.OwnedSetItemIds);
        }

        [Fact]
        public void A_broken_piece_is_owned_but_grants_no_bonus()
        {
            // The mask refuses flag 0x100 and flag 0x4000 (0x62a446). A shield at zero durability is
            // still carried — and still drawn red by name — but contributes no tier.
            Unit halo = Piece(AngelicHalo, LocationEquipped, 6);

            Unit broken = Piece(AngelicMantle, LocationEquipped, BodyTorso);
            broken.ItemFlags |= ItemRecordFlags.Broken;

            SetItemTooltipInput state = Engine.SetStateOf(halo, Wearer(halo, broken));

            Assert.Contains(AngelicMantle, state.OwnedSetItemIds);
            Assert.Equal(Bit(AngelicHalo), state.WornMaskIncludingSelf);
            Assert.Empty(Engine.EarnedSetIdsOf(Wearer(halo, broken)));
        }

        [Fact]
        public void A_piece_on_the_trade_page_is_not_owned()
        {
            // GetSetItem walks pages 0 / 3 / 4 / 0xFF (0x4867b3-0x4867bf), which excludes trade.
            // Ground and store are not in the viewer's chain at all.
            Unit halo = Piece(AngelicHalo, LocationEquipped, 6);

            foreach (int location in new[] { 0, 4, 5 })
            {
                SetItemTooltipInput state = Engine.SetStateOf(halo, Wearer(
                    halo, Piece(AngelicWings, location)));

                Assert.DoesNotContain(AngelicWings, state.OwnedSetItemIds);
            }
        }

        [Fact]
        public void Two_copies_of_one_piece_count_once()
        {
            // The game ORs `1 << slot` (0x62a474), so a second Angelic Halo in the other ring slot
            // lights no new bit. Counting units instead earned a tier off one duplicated piece and
            // put set-bonus spans on an item that has none.
            Unit halo = Piece(AngelicHalo, LocationEquipped, 6);
            Unit second = Piece(AngelicHalo, LocationEquipped, 7);

            SetItemTooltipInput state = Engine.SetStateOf(halo, Wearer(halo, second));

            Assert.Equal(Bit(AngelicHalo), state.WornMaskIncludingSelf);
            Assert.Empty(Engine.EarnedSetIdsOf(Wearer(halo, second)));
        }

        [Fact]
        public void A_viewers_carried_gear_never_becomes_the_viewers_own_stats()
        {
            // `Items` means socket fillers on an item and carried gear on a wearer, and the stat
            // reader RECURSES for the former. Reading a wearer through that recursion folded every
            // carried item's stats into the viewer's attributes — a +10 strength charm in the
            // backpack met a requirement the character did not.
            Unit halo = Piece(AngelicHalo, LocationEquipped, 6);

            Unit carried = Piece(AngelicWings, LocationInventory);
            carried.StatsLists.Add(
                new UnitStatList(0, ItemStatListFlags.Magic).Add(0, 100));

            var player = new Unit();
            player.UnitType = 0;
            player.StatsLists.Add(
                new UnitStatList(0, ItemStatListFlags.Extended).Add(0, 20).Add(12, 40));
            player.Items.Add(halo);
            player.Items.Add(carried);

            ItemViewer viewer = ItemRecordReader.ReadViewer(player);

            Assert.Equal(20, viewer.Strength);
        }

        [Fact]
        public void IsEquipped_follows_the_hovered_items_own_location()
        {
            Assert.True(
                Engine.SetStateOf(Piece(AngelicHalo, LocationEquipped, 6), Wearer()).IsEquipped);

            Assert.False(
                Engine.SetStateOf(Piece(AngelicHalo, LocationInventory), Wearer()).IsEquipped);
        }

        // ---- what the derivation changes about the rendered tooltip -----------------------------

        [Fact]
        public void The_piece_list_colours_owned_pieces_differently_from_the_rest()
        {
            // The payoff: Render alone used to pass an empty input, so every sibling painted red.
            Unit halo = Piece(AngelicHalo, LocationEquipped, 6);
            Unit wearer = Wearer(halo, Piece(AngelicMantle, LocationEquipped, BodyTorso));

            var pieces = Engine.Render(halo, wearer).Lines
                .Where(l => l.Section == ItemTooltipSection.SetPieceList)
                .ToArray();

            Assert.Equal(4, pieces.Length);

            // Two owned, two not — and they are told apart by COLOUR, which is the whole point of
            // OwnedSetItemIds. Green is 2 and red is 1 (0x48d8fb / 0x48d902).
            Assert.Equal(2, pieces.Count(l => l.Color == ItemTooltipColor.Set));
            Assert.Equal(2, pieces.Count(l => l.Color == ItemTooltipColor.Red));

            // With no wearer at all every piece is unowned, which is what the old default gave for
            // everyone.
            Assert.Equal(
                4,
                Engine.Render(halo).Lines
                    .Count(l => l.Section == ItemTooltipSection.SetPieceList
                        && l.Color == ItemTooltipColor.Red));
        }

        [Fact]
        public void The_full_set_block_reaches_the_plain_render()
        {
            // It could not before: Render passed IsEquipped = false, and BuildFullSet returns
            // immediately on that, so the block was dead on the convenience path no matter what the
            // wearer's record carried.
            Unit halo = Piece(AngelicHalo, LocationEquipped, 6);

            Unit wearer = Wearer(
                halo,
                Piece(AngelicMantle, LocationEquipped, BodyTorso),
                Piece(AngelicWings, LocationEquipped, 5),
                Piece(AngelicSickle, LocationEquipped, BodyRightArm));

            Assert.Contains(
                Engine.Render(halo, wearer).Lines,
                l => l.Section == ItemTooltipSection.FullSetBonus);

            // Same item, same wearer, but the piece itself is in the inventory: the game suppresses
            // the block outright, and so does this.
            Unit loose = Piece(AngelicHalo, LocationInventory);
            Assert.DoesNotContain(
                Engine.Render(loose, wearer).Lines,
                l => l.Section == ItemTooltipSection.FullSetBonus);
        }

        // ---- the same derivation behind Ranges --------------------------------------------------

        [Fact]
        public void Earned_sets_need_two_worn_pieces()
        {
            Unit halo = Piece(AngelicHalo, LocationEquipped, 6);

            Assert.Empty(Engine.EarnedSetIdsOf(Wearer(halo)));

            int setId = Engine.Sets.PieceAt(AngelicHalo).SetId;

            Assert.Equal(
                new[] { setId },
                Engine.EarnedSetIdsOf(Wearer(
                    halo, Piece(AngelicMantle, LocationEquipped, BodyTorso))).ToArray());
        }

        [Fact]
        public void Earned_sets_use_the_worn_predicate_and_not_the_owned_one()
        {
            // A second piece carried rather than worn does not earn the tier, and neither does one
            // on weapon swap — the same distinction the masks make, which is why both derivations
            // share one walk.
            Unit halo = Piece(AngelicHalo, LocationEquipped, 6);

            Assert.Empty(Engine.EarnedSetIdsOf(Wearer(
                halo, Piece(AngelicWings, LocationInventory))));

            Assert.Empty(Engine.EarnedSetIdsOf(Wearer(
                halo, Piece(AngelicSickle, LocationEquipped, BodyRightArmSwap))));
        }

        [Fact]
        public void Ranges_folds_in_the_tiers_the_viewer_has_earned()
        {
            Unit halo = Piece(AngelicHalo, LocationEquipped, 6);
            Unit wearer = Wearer(halo, Piece(AngelicMantle, LocationEquipped, BodyTorso));

            ItemRollRanges alone = Engine.RangesForViewer(halo, null);
            ItemRollRanges earned = Engine.RangesForViewer(halo, wearer);

            Assert.True(
                earned.Stats.Count > alone.Stats.Count,
                "an earned tier should contribute spans the item alone does not");

            Assert.Contains(earned.Stats, r => r.Sources.HasFlag(RollSources.SetBonus));
            Assert.DoesNotContain(alone.Stats, r => r.Sources.HasFlag(RollSources.SetBonus));
        }
    }
}
