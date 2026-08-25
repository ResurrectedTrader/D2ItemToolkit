using System;
using System.Collections.Generic;
using System.Globalization;
using Xunit;

namespace D2ItemToolkit.Tests
{
    /// <summary>
    /// A client capture hands over gems and runes with an EMPTY stat chain: every caller of the
    /// assignment (ITEM_ApplySocketableAndEquipStats 0x4c0cf0) lives in D2Common/D2Game, and the
    /// client is handed the host's already-merged stats in the item packet. So the host's blue
    /// block has to be rebuilt from gems.txt, which is what SocketStatSynthesis does.
    ///
    /// The two shields below are the exact items from a real capture that first exposed this, and
    /// the expected lines are the ones the GAME drew for them.
    /// </summary>
    public class SocketStatSynthesisTests
    {
        private static int ClassId(string code)
        {
            return TooltipEngine.Embedded.Items.ClassIdForCode(code);
        }

        /// <summary>An item with fillers that carry no statlist at all — the captured shape.</summary>
        private static Unit Host(string hostCode, params string[] fillerCodes)
        {
            var sockets = new List<string>();
            foreach (string code in fillerCodes)
            {
                sockets.Add(
                    "{ \"unitType\": 4, \"classId\": "
                    + ClassId(code).ToString(CultureInfo.InvariantCulture) + " }");
            }

            return Unit.FromJson(
                "{ \"unitType\": 4, \"classId\": "
                + ClassId(hostCode).ToString(CultureInfo.InvariantCulture)
                + ", \"quality\": 2, \"itemFlags\": 2064, \"statsLists\": [], \"items\": ["
                + string.Join(", ", sockets.ToArray()) + "] }");
        }

        private static string Render(Unit item)
        {
            return TooltipEngine.Embedded.Render(item).Text;
        }

        [Fact]
        public void A_shield_gains_the_shield_slot_mods_of_its_runes()
        {
            // Hyperion, gemapplytype 2 -> the `shield` array. Ko is dex 10 (twice) and Mal is
            // red-mag 7, which is character for character what the game drew for this item.
            string text = Render(Host("urg", "r18", "r18", "r23"));

            Assert.Contains("+20 to Dexterity", text);
            Assert.Contains("Magic Damage Reduced by 7", text);
        }

        [Fact]
        public void Body_armor_gains_the_helm_slot_mods_of_its_runes()
        {
            // Wire Fleece, gemapplytype 1 -> the `helm` array, which is what body armor uses too.
            // Shael balance2 20, Thul res-cold 30, Lem gold% 50.
            string text = Render(Host("utu", "r13", "r10", "r20"));

            Assert.Contains("+20% Faster Hit Recovery", text);
            Assert.Contains("Cold Resist +30%", text);
            Assert.Contains("50% Extra Gold from Monsters", text);
        }

        [Fact]
        public void The_slot_comes_from_the_HOST_not_the_filler()
        {
            // Ko is dex 10 in all three arrays, so it proves nothing on its own. Thul does: cold
            // damage in a weapon (gemapplytype 0), cold RESIST in armour. Same rune, same document,
            // two different lines — that is ITEM_GetItemsTxt_bGemApplyType(host) at 0x4c0dee.
            string weapon = Render(Host("ssd", "r10"));
            string armor = Render(Host("utu", "r10"));

            Assert.Contains("Adds 3-14 cold damage", weapon);
            Assert.DoesNotContain("Cold Resist", weapon);

            Assert.Contains("Cold Resist +30%", armor);
            Assert.DoesNotContain("cold damage", armor);
        }

        [Fact]
        public void A_filler_that_already_carries_stats_is_left_alone()
        {
            // A server-side producer records the mods the engine assigned. Synthesising on top of
            // those would count the gem twice, so a filler with a chain of its own is not touched.
            Unit item = Unit.FromJson(
                "{ \"unitType\": 4, \"classId\": " + ClassId("urg")
                + ", \"quality\": 2, \"itemFlags\": 2064, \"statsLists\": [], \"items\": ["
                + "{ \"unitType\": 4, \"classId\": " + ClassId("r18")
                + ", \"statsLists\": [ { \"stateNo\": 0, \"flags\": 64, "
                + "\"stats\": [ { \"id\": 2, \"value\": 10 } ] } ] } ] }");

            string text = TooltipEngine.Embedded.Render(item).Text;

            Assert.Contains("+10 to Dexterity", text);
            Assert.DoesNotContain("+20 to Dexterity", text);
        }

        [Fact]
        public void An_equipped_set_items_fillers_are_discarded_by_the_recalc()
        {
            // ITEM_RecalcAllEquippedItems 0x4c1350 ends with a loop over the eleven body slots that
            // fires only for quality 5 (0x4c15ec-0x4c162b). It calls
            // STATLIST_RemoveFromOwnerAndRecalc (0x4c1658), which detaches the item's whole stat
            // list (0x6277fa -> STATLIST_DetachAndRecalc), then rebuilds with
            // ITEM_ApplySocketableAndEquipStats(wearer, THE SET ITEM, 0) at 0x4c1661 — a2 is the set
            // item, not a filler, so both IsOfType gates fail (0x4c0d30 / 0x4c0da3) and it lands on
            // ITEM_ProcessSetItemEquip. The fillers are never re-applied.
            //
            // A real capture is what caught it: Tal Rasha's Horadric Crest with an Um in it draws
            // `All Resistances +15` — its own set property alone — while a runeword shield in the
            // same snapshot draws all three of its runes' mods.
            Unit worn = Unit.FromJson(
                "{ \"unitType\": 4, \"classId\": " + ClassId("xsk")
                + ", \"quality\": 5, \"itemFlags\": 2064, \"fileIndex\": 80, "
                + "\"statsLists\": [], \"items\": ["
                + "{ \"unitType\": 4, \"classId\": " + ClassId("r22") + " } ] }");

            var equipped = new SetItemTooltipInput();
            equipped.IsEquipped = true;

            var carried = new SetItemTooltipInput();
            carried.IsEquipped = false;

            Assert.DoesNotContain(
                "All Resistances",
                TooltipEngine.Embedded.RenderSetItem(worn, equipped).Text,
                StringComparison.Ordinal);

            // Not equipped, so the loop never ran and Um's helm mod is still on it.
            Assert.Contains(
                "All Resistances +15",
                TooltipEngine.Embedded.RenderSetItem(worn, carried).Text,
                StringComparison.Ordinal);
        }

        [Fact]
        public void Only_a_SET_item_loses_its_fillers_that_way()
        {
            // 0x4c1614 gates the loop on GetItemQuality == 5. A normal-quality host keeps its
            // fillers however it is carried — which is why the two runeword items in the same
            // capture render their runes and the set item does not.
            Assert.False(
                SocketStatSynthesis.FillersAreDiscardedByRecalc(
                    Host("urg", "r18"), equipped: true));

            Assert.True(
                SocketStatSynthesis.FillersAreDiscardedByRecalc(
                    Unit.FromJson("{ \"unitType\": 4, \"quality\": 5, \"itemFlags\": 16 }"),
                    equipped: true));

            // 0x4c1618 / 0x4c1628 exclude a broken item and flag 0x4000.
            Assert.False(
                SocketStatSynthesis.FillersAreDiscardedByRecalc(
                    Unit.FromJson("{ \"unitType\": 4, \"quality\": 5, \"itemFlags\": 272 }"),
                    equipped: true));
        }

        [Fact]
        public void A_jewel_is_never_synthesised()
        {
            // 0x4c0da3 tests type 74 `rune` and 0x4c0d30 type 20 `gem`; a jewel matches neither and
            // falls through to ITEM_ProcessSetItemEquip (0x4c0e06). gems.txt has no row for it
            // either way, so there is nothing to synthesise even if the gate let it through.
            string text = Render(Host("lrg", "jew"));

            Assert.DoesNotContain("Dexterity", text);
            Assert.DoesNotContain("Defense: ÿc3", text);
        }

        [Fact]
        public void Excluding_sockets_excludes_the_synthesised_stats_too()
        {
            var options = new TooltipOptions();
            options.Sockets = SocketMode.Excluded;

            string text = TooltipEngine.Embedded.Render(
                Host("urg", "r18", "r18", "r23"), null, options).Text;

            Assert.DoesNotContain("Dexterity", text);
            Assert.DoesNotContain("Magic Damage Reduced", text);
        }

        [Fact]
        public void The_breakdown_attributes_them_to_the_sockets()
        {
            TooltipBreakdown breakdown =
                TooltipEngine.Embedded.Breakdown(Host("urg", "r18", "r18", "r23"));

            var socketText = new List<string>();
            foreach (ItemTooltipLine line in breakdown.Sockets)
            {
                socketText.Add(line.Text.Replace("\n", string.Empty));
            }

            Assert.Contains("+20 to Dexterity", socketText);
            Assert.Contains("Magic Damage Reduced by 7", socketText);
        }
    }
}
