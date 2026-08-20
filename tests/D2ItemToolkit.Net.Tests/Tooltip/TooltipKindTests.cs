using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace D2ItemToolkit.Tests
{
    /// <summary>
    /// LoadItemDesc 0x48dd90 diverts four item kinds away from the generic tooltip before building
    /// anything. The gates that are pure item data have to be set from the item tables, or Compose
    /// silently builds a generic tooltip where the game builds a different one.
    /// </summary>
    public class TooltipKindTests
    {
        private static readonly D2DataFiles Data = D2DataFiles.LoadEmbedded();

        private static readonly ItemTable Items = new ItemTable(
            Data.Weapons, Data.Armor, Data.Misc);

        private static readonly ItemTypeTree Types = new ItemTypeTree(Data.ItemTypes);

        private static ItemTooltipContext Context(string code)
        {
            var item = new ItemIdentity();
            item.ClassId = Items.ClassIdForCode(code);
            item.Code = code;
            item.Flags = ItemRecordFlags.Identified;
            Assert.True(item.ClassId >= 0, "no items row for " + code);

            return new RecordSections(
                    Data, Items, Types, item, null, new Dictionary<int, int>(), null, null, null)
                .CreateContext();
        }

        [Theory]
        // 0x48e44c compares the items row's own wType (+0x11E) against 18 exactly — not an
        // IsOfType walk — then 0x48e451 diverts to INV_ShowBookTooltip and 0x48e45c returns.
        [InlineData("tbk")]   // Tome of Town Portal
        [InlineData("ibk")]   // Tome of Identify
        public void A_tome_is_classified_as_a_book(string code)
        {
            ItemTooltipContext context = Context(code);

            Assert.True(context.IsBook);
            Assert.Equal(ItemTooltipKind.Book, ItemTooltipComposer.Classify(context));
        }

        [Theory]
        // The scrolls are NOT books: they are their own itemtypes row and take the generic path.
        [InlineData("tsc")]
        [InlineData("isc")]
        [InlineData("gpr")]
        [InlineData("lrg")]
        [InlineData("ssd")]
        public void A_non_tome_is_not_a_book(string code)
        {
            ItemTooltipContext context = Context(code);

            Assert.False(context.IsBook);
            Assert.Equal(ItemTooltipKind.Generic, ItemTooltipComposer.Classify(context));
        }

        [Fact]
        public void Exactly_two_shipped_codes_are_books()
        {
            var books = new List<string>();

            for (int classId = 0; classId < Items.RowCount; ++classId)
            {
                string code = Items.Code(classId);
                if (string.IsNullOrEmpty(code))
                {
                    continue;
                }

                if (Context(code.Trim()).IsBook)
                {
                    books.Add(code.Trim());
                }
            }

            books.Sort(StringComparer.Ordinal);
            Assert.Equal(new[] { "ibk", "tbk" }, books);
        }

        [Fact]
        public void A_book_is_refused_by_the_generic_compose_path()
        {
            // The point of the gate: the game never builds a generic tooltip for a tome, so
            // Compose must refuse rather than produce a plausible wrong one.
            var item = new ItemIdentity();
            item.ClassId = Items.ClassIdForCode("tbk");
            item.Code = "tbk";
            item.Flags = ItemRecordFlags.Identified;

            var sections = new RecordSections(
                Data, Items, Types, item, null, new Dictionary<int, int>(), null, null, null);

            var composer = new ItemTooltipComposer(
                sections, sections.CreateModifierGenerator(new Dictionary<int, int>()));

            Assert.Throws<NotSupportedException>(
                () => composer.Compose(sections.CreateContext(), new Dictionary<int, int>()));
        }

        // =================================================================
        // 0x48ec3f: the quest-usage line. Prepended to the FINISHED buffer, so it renders as the
        // bottom row and is appended ahead of AppendOrder[0].
        // =================================================================

        private static RecordSections Sections(string code)
        {
            var item = new ItemIdentity();
            item.ClassId = Items.ClassIdForCode(code);
            item.Code = code;
            item.Flags = ItemRecordFlags.Identified;
            Assert.True(item.ClassId >= 0, "no items row for " + code);

            return new RecordSections(
                Data, Items, Types, item, null, new Dictionary<int, int>(), null, null, null);
        }

        [Fact]
        public void The_horadric_cube_says_right_click_to_open()
        {
            Assert.Equal(
                "Right Click to Open\n",
                Sections("box").GetSection(ItemTooltipSection.QuestUsage));
        }

        [Fact]
        public void The_cairn_stones_key_says_right_click_to_read()
        {
            Assert.Equal(
                "Right Click to Read\n",
                Sections("bkd").GetSection(ItemTooltipSection.QuestUsage));
        }

        [Fact]
        public void Wirts_leg_is_excluded_from_the_quest_usage_line()
        {
            Assert.Null(Sections("leg").GetSection(ItemTooltipSection.QuestUsage));
        }

        [Fact]
        public void A_quest_item_without_a_usage_line_writes_nothing()
        {
            // 24 other quest items pass the outer gate and fall to the colour-only branch
            // at 0x48ece5, emitting no line at all.
            Assert.Null(Sections("hdm").GetSection(ItemTooltipSection.QuestUsage));
        }

        [Fact]
        public void A_non_quest_item_writes_nothing()
        {
            Assert.Null(Sections("lrg").GetSection(ItemTooltipSection.QuestUsage));
            Assert.Null(Sections("gpr").GetSection(ItemTooltipSection.QuestUsage));
        }

        [Fact]
        public void The_quest_usage_line_renders_as_the_bottom_row()
        {
            // D2WINFONT_DrawWideString 0x501a80 does y += lineHeight / -10 at 0x501c17, so
            // position 0 is the bottom row. Appended first => drawn last => lowest.
            RecordSections sections = Sections("box");

            var composer = new ItemTooltipComposer(
                sections, sections.CreateModifierGenerator(new Dictionary<int, int>()));

            string rendered = composer.Render(
                composer.Compose(sections.CreateContext(), new Dictionary<int, int>()));

            // The gold marker is INSIDE the name buffer (GetItemName prepends it at 0x48ce6d), so
            // it is part of the section's text and survives the marker-free Render — the same way
            // every other writer-embedded marker does.
            Assert.Equal("ÿc4Horadric Cube\nRight Click to Open", rendered);

            // And with markers: v105 is 0 for a normal-quality cube, and 0x48ecf2's colour 4 lands
            // on the bottom row. Character for character what a real capture holds.
            Assert.Equal(
                "ÿc0ÿc4Horadric Cube\nÿc4Right Click to Open",
                composer.RenderWithColorCodes(
                    composer.Compose(sections.CreateContext(), new Dictionary<int, int>())));
        }

        // =================================================================
        // op 2..5 scale by the VIEWER's stat, not the item's.
        // SKILLDESC_CalcStatGroupValue 0x4e4c50: GetStatUnsignedValue(GetPlayerUnit(), opBase, 0)
        // >> opBase.nValShift, imul stored value, >> op param, >> own nValShift.
        // =================================================================

        private static string PerLevelLine(int statId, int storedValue, int viewerLevel)
        {
            var item = new ItemIdentity();
            item.ClassId = Items.ClassIdForCode("lrg");
            item.Code = "lrg";
            item.Flags = ItemRecordFlags.Identified;

            ItemViewer viewer = null;
            if (viewerLevel >= 0)
            {
                viewer = new ItemViewer();
                viewer.UnitType = 0;
                viewer.ClassId = 3;
                viewer.Level = viewerLevel;
                viewer.Stats[ItemStatReader.PackStatKey(0, 12)] = viewerLevel;
            }

            var stats = new Dictionary<int, int>();
            stats[ItemStatReader.PackStatKey(0, statId)] = storedValue;

            var sections = new RecordSections(
                Data, Items, Types, item, viewer, stats, null, null, null);

            var composer = new ItemTooltipComposer(
                sections, sections.CreateModifierGenerator(stats));

            return composer.Render(composer.Compose(sections.CreateContext(), stats));
        }

        [Fact]
        public void A_per_level_stat_scales_by_the_viewer_level()
        {
            // ItemStatCost 214 item_armor_percent_perlevel, op 2, op base level, op param 8.
            // Before the fix this rendered "0 Defense (Based on Character Level)".
            Assert.Contains(
                "+100 Defense (Based on Character Level)",
                PerLevelLine(214, 16, 50),
                StringComparison.Ordinal);
        }

        [Fact]
        public void The_same_stat_scales_differently_at_a_different_level()
        {
            Assert.NotEqual(PerLevelLine(214, 16, 20), PerLevelLine(214, 16, 60));
        }

        [Fact]
        public void A_viewerless_tooltip_scales_to_zero_but_still_emits_the_line()
        {
            // GetStatUnsignedValue 0x625483 returns 0 for a null unit rather than halting, and the
            // zero filter at 0x4e628b tests the STORED value, ahead of the scaling call at
            // 0x4e62c4 — so the line survives carrying 0.
            string none = PerLevelLine(214, 16, -1);

            Assert.Contains("(Based on Character Level)", none, StringComparison.Ordinal);
        }

        [Fact]
        public void The_viewer_stat_lookup_is_by_stat_id_not_hardwired_to_level()
        {
            // op base is a column; on shipped data it is always 12, but the lookup must honour it.
            var viewer = new ItemViewer();
            viewer.Stats[ItemStatReader.PackStatKey(0, 12)] = 42;
            viewer.Stats[ItemStatReader.PackStatKey(0, 0)] = 7;

            Assert.Equal(42, viewer.Stat(12));
            Assert.Equal(7, viewer.Stat(0));
            Assert.Equal(0, viewer.Stat(99));
        }

        // =================================================================
        // INV_ShowBookTooltip 0x48d060. Append order is quantity (0x48d07d), then locale 2203 and
        // 2206 each with a 3998 terminator when ShopMode is EXACTLY zero (0x48d082), then
        // GetItemName with no terminator (0x48d0ed). Position 0 renders at the bottom.
        // =================================================================

        private static string RenderBook(string code, int quantity, int shopMode, int spell = 0)
        {
            var item = new ItemIdentity();
            item.ClassId = Items.ClassIdForCode(code);
            item.Code = code;
            item.Flags = ItemRecordFlags.Identified;
            Assert.True(item.ClassId >= 0, "no items row for " + code);

            // GetItemName's tome/scroll arm at 0x48c542 picks the spell from MagicSuffix[0]
            // (2199/2201 for a tome), not from the item code.
            item.MagicSuffix[0] = spell;

            var stats = new Dictionary<int, int>();
            if (quantity > 0)
            {
                stats[ItemStatReader.PackStatKey(0, 70)] = quantity;
            }

            var sections = new RecordSections(
                Data, Items, Types, item, null, stats, null, null, null);

            var composer = new ItemTooltipComposer(
                sections, sections.CreateModifierGenerator(stats));

            ItemTooltipContext context = sections.CreateContext();
            context.ShopMode = shopMode;

            return composer.Render(composer.ComposeBook(context));
        }

        [Fact]
        public void A_tome_of_town_portal_renders_its_whole_tooltip()
        {
            Assert.Equal(
                "Tome of Town Portal\nInsert Scrolls\nRight Click to Use\nQuantity: 20",
                RenderBook("tbk", 20, 0));
        }

        [Fact]
        public void A_tome_of_identify_renders_its_whole_tooltip()
        {
            // Same shape as the Town Portal tome: the two usage lines are locale 2203/2206 and are
            // not spell-dependent, so only the name differs.
            Assert.Equal(
                "Tome of Identify\nInsert Scrolls\nRight Click to Use\nQuantity: 20",
                RenderBook("ibk", 20, 0, spell: 1));
        }

        [Fact]
        public void An_identify_tome_in_a_shop_loses_the_usage_lines_too()
        {
            Assert.Equal(
                "Tome of Identify\nQuantity: 20",
                RenderBook("ibk", 20, 1, spell: 1));
        }

        [Fact]
        public void Both_tomes_differ_only_in_their_name_row()
        {
            string portal = RenderBook("tbk", 20, 0);
            string identify = RenderBook("ibk", 20, 0, spell: 1);

            Assert.NotEqual(portal, identify);
            Assert.Equal(
                string.Join("\n", portal.Split('\n').Skip(1)),
                string.Join("\n", identify.Split('\n').Skip(1)));
        }

        [Fact]
        public void The_spell_comes_from_the_suffix_not_the_item_code()
        {
            // Both tome codes take the same arm; suffix 1 is Identify. Asserting on the code alone
            // would pass for the wrong reason.
            Assert.StartsWith(
                "Tome of Identify\n", RenderBook("ibk", 20, 0, spell: 1), StringComparison.Ordinal);
            Assert.StartsWith(
                "Tome of Identify\n", RenderBook("tbk", 20, 0, spell: 1), StringComparison.Ordinal);
            Assert.StartsWith(
                "Tome of Town Portal\n", RenderBook("ibk", 20, 0), StringComparison.Ordinal);
        }

        [Theory]
        // 0x48d082 is `cmp dword_7BCBF0, 0` / `jnz` — exactly zero, so every shop mode drops both.
        [InlineData(1)]
        [InlineData(5)]
        [InlineData(9)]
        [InlineData(10)]
        public void A_shop_mode_drops_both_usage_lines(int shopMode)
        {
            string text = RenderBook("tbk", 20, shopMode);

            Assert.DoesNotContain("Insert Scrolls", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Right Click to Use", text, StringComparison.Ordinal);
            Assert.Equal("Tome of Town Portal\nQuantity: 20", text);
        }

        [Fact]
        public void The_book_quantity_is_not_gated_like_the_generic_one()
        {
            // 0x48d07d has no identified / not-socketed test, unlike 0x48e8ef / 0x48e90d.
            var item = new ItemIdentity();
            item.ClassId = Items.ClassIdForCode("tbk");
            item.Code = "tbk";
            item.Flags = ItemRecordFlags.None;          // unidentified

            var stats = new Dictionary<int, int>();
            stats[ItemStatReader.PackStatKey(0, 70)] = 7;

            var sections = new RecordSections(
                Data, Items, Types, item, null, stats, null, null, null);

            Assert.Equal(
                "Quantity: 7\n", sections.GetSection(ItemTooltipSection.BookQuantity));
            Assert.Null(sections.GetSection(ItemTooltipSection.QuantityAndSpellDescription));
        }

        [Fact]
        public void A_book_tooltip_carries_no_colour_markers()
        {
            // No AppendAsWideChar anywhere in 0x48d060, and GetItemName's colour tail is skipped
            // for quest == 0 (0x48cb0b).
            Assert.DoesNotContain(
                ItemTooltipColor.Marker, RenderBook("tbk", 20, 0), StringComparison.Ordinal);
        }

        [Fact]
        public void The_generic_path_still_refuses_a_book()
        {
            var item = new ItemIdentity();
            item.ClassId = Items.ClassIdForCode("tbk");
            item.Code = "tbk";
            item.Flags = ItemRecordFlags.Identified;

            var sections = new RecordSections(
                Data, Items, Types, item, null, new Dictionary<int, int>(), null, null, null);

            var composer = new ItemTooltipComposer(
                sections, sections.CreateModifierGenerator(new Dictionary<int, int>()));

            Assert.Throws<NotSupportedException>(
                () => composer.Compose(sections.CreateContext(), new Dictionary<int, int>()));
        }

        [Fact]
        public void The_book_path_refuses_a_non_book()
        {
            var item = new ItemIdentity();
            item.ClassId = Items.ClassIdForCode("lrg");
            item.Code = "lrg";
            item.Flags = ItemRecordFlags.Identified;

            var sections = new RecordSections(
                Data, Items, Types, item, null, new Dictionary<int, int>(), null, null, null);

            var composer = new ItemTooltipComposer(
                sections, sections.CreateModifierGenerator(new Dictionary<int, int>()));

            Assert.Throws<NotSupportedException>(
                () => composer.ComposeBook(sections.CreateContext()));
        }
    }
}
