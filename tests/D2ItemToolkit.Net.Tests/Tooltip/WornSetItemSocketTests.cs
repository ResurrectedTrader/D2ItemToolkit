using System.Linq;
using Xunit;

namespace D2ItemToolkit.Tests
{
    /// <summary>
    /// The separated-socket mode MOVES a filler's stats out of the item's block. It must never ADD
    /// stats the merged render would not have shown, and a worn set item is where the two come
    /// apart: ITEM_RecalcAllEquippedItems 0x4c1350 discards its fillers, so there is nothing to
    /// move — and a block listing them anyway claimed the item granted what it does not.
    ///
    /// Tal Rasha's Horadric Crest with an Um is the case a real capture pinned this on, and the one
    /// reported. Its own set properties include `res-all 15`, which is EXACTLY what an Um gives a
    /// helm — so the two are indistinguishable by eye, and the collision is the whole reason the
    /// report read as "the socket stats are still there".
    /// </summary>
    public class WornSetItemSocketTests
    {
        private static readonly D2DataFiles Data = D2DataFiles.LoadEmbedded();
        private static readonly TooltipEngine Engine = TooltipEngine.Embedded;
        private static readonly ItemTable Items = new ItemTable(Data.Weapons, Data.Armor, Data.Misc);

        /// <summary>setitems.txt post-splice, 0-based. `xsk`, a Death Mask.</summary>
        private const int TalRashasHoradricCrest = 80;

        private const int LocationEquipped = 1;
        private const int LocationStash = 3;

        private const int StatDefense = 31;
        private const int StatNumSockets = 194;

        private static Unit CrestWithUm(int location)
        {
            var um = new Unit();
            um.UnitType = 4;
            um.ClassId = Items.ClassIdForCode("r22");
            um.ItemFlags = ItemRecordFlags.Identified;

            var helm = new Unit();
            helm.UnitType = 4;
            helm.ClassId = Items.ClassIdForCode("xsk");
            helm.Quality = ItemQualityNo.Set;
            helm.FileIndex = TalRashasHoradricCrest;
            helm.ItemFlags = ItemRecordFlags.Identified | ItemRecordFlags.Socketed;
            helm.Location = location;
            helm.X = 1;

            helm.StatsLists.Add(
                new UnitStatList(0, ItemStatListFlags.Extended)
                    .Add(StatDefense, 121).Add(StatNumSockets, 1));

            helm.Items.Add(um);
            return helm;
        }

        private static TooltipOptions Separated()
        {
            var options = new TooltipOptions();
            options.Sockets = SocketMode.Separated;
            return options;
        }

        private static string[] Sectioned(Tooltip tip, ItemTooltipSection section)
        {
            return tip.Lines
                .Where(l => l.Section == section)
                .Select(l => System.Text.RegularExpressions.Regex
                    .Replace(l.Text ?? string.Empty, "ÿc.", string.Empty).TrimEnd('\n'))
                .Where(t => t.Length != 0)
                .ToArray();
        }

        [Fact]
        public void A_carried_set_item_moves_its_filler_into_a_block()
        {
            // The mode working as documented: the Um's line leaves the item's own block and appears
            // below it, under the rune's name.
            Unit carried = CrestWithUm(LocationStash);

            Assert.Contains("All Resistances +15", Sectioned(Engine.Render(carried), ItemTooltipSection.Modifiers));

            Tooltip split = Engine.Render(carried, null, Separated());

            Assert.DoesNotContain(
                "All Resistances +15", Sectioned(split, ItemTooltipSection.Modifiers));
            Assert.Equal(
                new[] { "Um Rune", "All Resistances +15" },
                Sectioned(split, ItemTooltipSection.SocketContribution));
        }

        [Fact]
        public void A_worn_set_item_has_no_filler_to_move_and_grows_no_block()
        {
            // Worn, the recalc has already thrown the Um away, so the merged render never showed
            // its line — and the separated render must not invent one.
            Unit worn = CrestWithUm(LocationEquipped);

            Assert.DoesNotContain(
                "All Resistances +15", Sectioned(Engine.Render(worn), ItemTooltipSection.Modifiers));

            Assert.Empty(
                Sectioned(Engine.Render(worn, null, Separated()),
                    ItemTooltipSection.SocketContribution));
        }

        /// <summary>
        /// The Crest's OWN `res-all 15` and an Um's `res-all 15` are the same four stats, so if the
        /// rune applied they would MERGE rather than appear twice. That makes the single number the
        /// decisive evidence about whether the filler counts: +15 means it does not, +30 means it
        /// does. A reported tooltip read +15 with all six of the Crest's own properties present,
        /// which is the worn case — the line is the set item's, not the rune's.
        /// </summary>
        [Fact]
        public void The_resistance_number_says_whether_the_rune_counted()
        {
            int[] resists = { 39, 41, 43, 45 };

            Unit worn = CrestWithUm(LocationEquipped);
            Unit carried = CrestWithUm(LocationStash);

            // setitems.txt row 82 `res-all 15`, which the record carries as its four members.
            foreach (Unit crest in new[] { worn, carried })
            {
                var own = new UnitStatList(0, ItemStatListFlags.Magic);
                foreach (int resist in resists)
                {
                    own.Add(resist, 15);
                }

                crest.StatsLists.Add(own);
            }

            Assert.Contains(
                "All Resistances +15", Sectioned(Engine.Render(worn), ItemTooltipSection.Modifiers));

            Assert.Contains(
                "All Resistances +30",
                Sectioned(Engine.Render(carried), ItemTooltipSection.Modifiers));
        }

        /// <summary>
        /// REPORTED, from a screenshot: `Defense: 121 [99-131]` beside `+45 Defense [99-131]`.
        ///
        /// A Death Mask rolls 54..86 (armor.txt) and the Crest adds a FIXED 45, so 99..131 is the
        /// span of what the DEFENSE SECTION draws and the only line it belongs on. The `+45
        /// Defense` modifier draws the set property alone, which could never have rolled — it took
        /// the section's span because both lines report stat 31 and one lookup served both.
        /// </summary>
        [Fact]
        public void The_defense_modifier_line_does_not_borrow_the_sections_span()
        {
            Unit crest = CrestWithUm(LocationEquipped);

            // The base array holds the ROLL, 76 of 54..86; the set's fixed 45 is a modifier on top,
            // and 121 is what the section adds up to. CrestWithUm's 121 is the rendered total, so
            // it is replaced here rather than added to.
            crest.StatsLists.Clear();
            crest.StatsLists.Add(
                new UnitStatList(0, ItemStatListFlags.Extended)
                    .Add(StatDefense, 76).Add(StatNumSockets, 1));
            crest.StatsLists.Add(new UnitStatList(0, ItemStatListFlags.Magic).Add(StatDefense, 45));

            var options = new TooltipOptions();
            options.Ranges = new RangeDisplay();
            options.Ranges.Color = -1;

            Tooltip tip = Engine.Render(crest, null, options);

            string section = Sectioned(tip, ItemTooltipSection.ArmorClass).Single();
            string modifier = Sectioned(tip, ItemTooltipSection.Modifiers)
                .Single(t => t.Contains("Defense"));

            // 54..86 base plus the fixed 45.
            Assert.Equal("Defense: 121 [99-131]", section);

            // Fixed, so no span at all rather than the section's.
            Assert.Equal("+45 Defense", modifier);
        }

        [Fact]
        public void Separating_never_adds_a_line_the_merged_render_lacks()
        {
            // The contract stated as one assertion, over both states: the two renders hold the same
            // set of stat lines, only distributed differently.
            foreach (int location in new[] { LocationEquipped, LocationStash })
            {
                Unit item = CrestWithUm(location);

                string[] merged = Sectioned(Engine.Render(item), ItemTooltipSection.Modifiers);

                Tooltip split = Engine.Render(item, null, Separated());
                string[] own = Sectioned(split, ItemTooltipSection.Modifiers);
                string[] fillers = Sectioned(split, ItemTooltipSection.SocketContribution);

                foreach (string line in own.Concat(fillers))
                {
                    // The filler block's heading is the rune's NAME, which is not a stat line and is
                    // the one thing the merged render has no counterpart for.
                    if (line == "Um Rune")
                    {
                        continue;
                    }

                    Assert.Contains(line, merged);
                }
            }
        }
    }
}
