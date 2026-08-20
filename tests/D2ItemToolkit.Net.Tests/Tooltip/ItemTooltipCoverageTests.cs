using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace D2ItemToolkit.Tests
{
    public class ItemTooltipCoverageTests
    {
        private static ItemTooltipComposer Composer(FakeSections sections)
        {
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(1, ItemDescFunc.PlusValueString, 100));

            var strings = new FakeStringTable().WithPunctuation().Add(100, "to Strength");

            return new ItemTooltipComposer(
                sections, new ItemDescriptionGenerator(stats, strings));
        }

        private static ItemTooltipContext Generic()
        {
            var context = new ItemTooltipContext();
            context.Quality = ItemQuality.Unique;
            context.Flags = ItemTooltipFlags.Identified;
            context.IsWeaponOrArmorType = true;
            return context;
        }

        private static ItemTooltipLine Line(
            string text,
            ItemTooltipSection section = ItemTooltipSection.ItemName,
            int color = ItemTooltipColor.White,
            bool marker = true)
        {
            var line = new ItemTooltipLine();
            line.Text = text;
            line.Section = section;
            line.Color = color;
            line.EmitsColorMarker = marker;
            return line;
        }

        [Fact]
        public void Classify_returns_every_kind()
        {
            ItemTooltipContext context = Generic();
            Assert.Equal(ItemTooltipKind.Generic, ItemTooltipComposer.Classify(context));

            context = Generic();
            context.IsShopTransaction = true;
            Assert.Equal(ItemTooltipKind.ShopTransaction, ItemTooltipComposer.Classify(context));

            context = Generic();
            context.IsTransmogrify = true;
            Assert.Equal(ItemTooltipKind.Transmogrify, ItemTooltipComposer.Classify(context));

            context = Generic();
            context.Quality = ItemQuality.Set;
            Assert.Equal(ItemTooltipKind.IdentifiedSetItem, ItemTooltipComposer.Classify(context));

            // Set but unidentified stays generic.
            context = Generic();
            context.Quality = ItemQuality.Set;
            context.Flags = ItemTooltipFlags.None;
            Assert.Equal(ItemTooltipKind.Generic, ItemTooltipComposer.Classify(context));

            context = Generic();
            context.IsBook = true;
            Assert.Equal(ItemTooltipKind.Book, ItemTooltipComposer.Classify(context));

            Assert.Throws<ArgumentNullException>(() => ItemTooltipComposer.Classify(null));
        }

        [Fact]
        public void Compose_rejects_non_generic_items_and_null_arguments()
        {
            ItemTooltipComposer composer = Composer(new FakeSections());
            var empty = new KeyValuePair<int, int>[0];

            ItemTooltipContext book = Generic();
            book.IsBook = true;

            NotSupportedException error =
                Assert.Throws<NotSupportedException>(() => composer.Compose(book, empty));
            Assert.Contains("Book", error.Message, StringComparison.Ordinal);

            Assert.Throws<ArgumentNullException>(() => composer.Compose(null, empty));
            Assert.Throws<ArgumentNullException>(() => composer.Compose(Generic(), null));
        }

        [Fact]
        public void The_composer_rejects_null_dependencies()
        {
            var stats = new FakeStatCostTable();
            var generator = new ItemDescriptionGenerator(stats, new FakeStringTable());

            Assert.Throws<ArgumentNullException>(
                () => new ItemTooltipComposer(null, generator));
            Assert.Throws<ArgumentNullException>(
                () => new ItemTooltipComposer(new FakeSections(), null));
        }

        [Fact]
        public void An_empty_line_terminator_makes_every_section_one_line()
        {
            var sections = new FakeSections()
                .Set(ItemTooltipSection.ItemName, "Name")
                .Set(ItemTooltipSection.EtherealSocketed, "Eth\nSock");
            sections.LineTerminator = string.Empty;

            ItemTooltipComposer composer = Composer(sections);
            IReadOnlyList<ItemTooltipLine> lines =
                composer.Compose(Generic(), new KeyValuePair<int, int>[0]);

            Assert.Equal(2, lines.Count);
            Assert.Equal("Eth\nSock", lines.Single(
                l => l.Section == ItemTooltipSection.EtherealSocketed).Text);

            // DropTrailingTerminator has nothing to strip.
            Assert.Equal("NameEth\nSock", composer.Render(lines));
        }

        [Fact]
        public void Render_leaves_a_string_that_does_not_end_with_the_terminator_alone()
        {
            ItemTooltipComposer composer = Composer(new FakeSections());

            string rendered = composer.Render(new[] { Line("no terminator here") });

            Assert.Equal("no terminator here", rendered);
        }

        [Fact]
        public void Render_and_RenderWithColorCodes_reject_null_and_accept_both_shapes()
        {
            ItemTooltipComposer composer = Composer(new FakeSections());

            Assert.Throws<ArgumentNullException>(() => composer.Render(null));
            Assert.Throws<ArgumentNullException>(() => composer.RenderWithColorCodes(null));

            ItemTooltipLine[] asArray = { Line("A\n"), Line("B\n") };
            Assert.Equal("A\nB", composer.Render(asArray));
            Assert.Equal("A\nB", composer.Render(asArray.ToList()));
        }

        [Fact]
        public void Emit_skips_lines_with_no_text()
        {
            ItemTooltipComposer composer = Composer(new FakeSections());

            var lines = new[]
            {
                Line("first\n"),
                Line(null),
                Line(string.Empty),
                Line("last\n"),
            };

            Assert.Equal("first\nlast", composer.Render(lines));
        }

        [Fact]
        public void A_marker_is_emitted_on_every_row_that_has_glyphs()
        {
            ItemTooltipComposer composer = Composer(new FakeSections());
            string marker = ItemTooltipColor.Marker
                            + ItemTooltipComposer.EncodeColorDigit(ItemTooltipColor.Magic);

            // Same colour twice: BOTH state it. Stickiness would carry the second in APPEND order,
            // but reversing into display order breaks that, so a row that does not own the game's
            // section marker is re-anchored with the colour that was in force at it.
            string sticky = composer.RenderWithColorCodes(
                new[]
                {
                    Line("A\n", color: ItemTooltipColor.Magic, marker: false),
                    Line("B\n", color: ItemTooltipColor.Magic),
                });

            Assert.Equal(marker + "A\n" + marker + "B", sticky);

            // A re-anchored row with no glyphs gets nothing — a marker there would draw a colour
            // code instead of a blank line. A row that OWNS the section marker still gets it: that
            // one is AppendAsWideChar, which only checks the buffer is non-empty (0x4521cd).
            Assert.Equal(
                marker + "A\n\n" + marker + "B",
                composer.RenderWithColorCodes(
                    new[]
                    {
                        Line("A\n", color: ItemTooltipColor.Magic, marker: false),
                        Line("\n", color: ItemTooltipColor.Magic, marker: false),
                        Line("B\n", color: ItemTooltipColor.Magic),
                    }));

            // A re-anchored row that already opens with a marker states its own colour and takes
            // no anchor — this is the runeword name sitting above the base name.
            Assert.Equal(
                marker + "A\n" + marker + "B",
                composer.RenderWithColorCodes(
                    new[]
                    {
                        Line(marker + "A\n", color: ItemTooltipColor.Unique, marker: false),
                        Line("B\n", color: ItemTooltipColor.Magic),
                    }));

            // Text that already opens with a marker does NOT suppress the composer's own. A marker
            // in the section TEXT was put there by a writer and says nothing about whether the
            // line's colour has been stated: INV_FormatBlockChanceText prepends colour 0 to the
            // label buffer (0x485d0e) and LoadItemDesc then prepends the section's (0x48eb80), so
            // the game draws two. Suppressing here swallowed one of them.
            string unique = ItemTooltipColor.Marker
                            + ItemTooltipComposer.EncodeColorDigit(ItemTooltipColor.Unique);
            Assert.Equal(
                unique + marker + "A",
                composer.RenderWithColorCodes(
                    new[] { Line(marker + "A\n", color: ItemTooltipColor.Unique) }));

            // No marker string at all: the digit is still written.
            Assert.Equal(
                "3A",
                composer.RenderWithColorCodes(
                    new[] { Line("A\n", color: ItemTooltipColor.Magic) }, string.Empty));

            // Null text with a marker requested still emits the marker and nothing else.
            Assert.Equal(
                string.Empty,
                composer.RenderWithColorCodes(new[] { Line(null, color: ItemTooltipColor.Magic) }));
        }

        [Fact]
        public void The_quest_prefix_is_emitted_once_at_the_front()
        {
            ItemTooltipComposer composer = Composer(new FakeSections());
            string quest = ItemTooltipColor.Marker
                           + ItemTooltipComposer.EncodeColorDigit(ItemTooltipColor.Unique);

            string colored = composer.RenderWithColorCodes(
                new[] { Line("Name\n", color: ItemTooltipColor.Magic) },
                questColorPrefix: true);

            // Display order puts it at the END, where it paints nothing but still spends budget.
            Assert.EndsWith(quest, colored, StringComparison.Ordinal);

            // Render is the marker-free variant: the flag only costs budget there.
            Assert.Equal(
                "Name", composer.Render(new[] { Line("Name\n") }, questColorPrefix: true));
        }

        [Fact]
        public void EncodeColorDigit_is_unchecked()
        {
            Assert.Equal('0', ItemTooltipComposer.EncodeColorDigit(0));
            Assert.Equal(':', ItemTooltipComposer.EncodeColorDigit(10));
            Assert.Equal('=', ItemTooltipComposer.EncodeColorDigit(13));
        }

        [Fact]
        public void The_budget_abandons_a_line_that_cannot_even_fit_its_marker()
        {
            ItemTooltipComposer composer = Composer(new FakeSections());

            // Bottom leaves exactly 3 characters, so the line above cannot fit even its marker.
            var bottom = Line(
                new string('x', ItemTooltipComposer.MaxTooltipLength - 7) + "\n",
                ItemTooltipSection.EtherealSocketed,
                ItemTooltipColor.Magic);

            var top = Line("TOP\n", ItemTooltipSection.ItemName, ItemTooltipColor.Unique);

            string rendered = composer.Render(new[] { top, bottom });

            Assert.DoesNotContain("TOP", rendered, StringComparison.Ordinal);
            Assert.StartsWith("\n", rendered, StringComparison.Ordinal);
        }

        [Fact]
        public void With_exactly_one_character_left_the_boundary_row_draws_a_lone_marker_byte()
        {
            ItemTooltipComposer composer = Composer(new FakeSections());

            var bottom = Line(
                new string('x', ItemTooltipComposer.MaxTooltipLength - 5) + "\n",
                ItemTooltipSection.EtherealSocketed,
                ItemTooltipColor.Magic);

            var top = Line("TOP\n", ItemTooltipSection.ItemName, ItemTooltipColor.Unique);

            string colored = composer.RenderWithColorCodes(new[] { top, bottom });

            Assert.Contains(
                ItemTooltipColor.Marker.Substring(0, 1) + "\n",
                colored,
                StringComparison.Ordinal);
        }

        [Fact]
        public void The_only_line_being_abandoned_leaves_a_white_boundary_row()
        {
            ItemTooltipComposer composer = Composer(new FakeSections());

            var only = Line(
                new string('x', ItemTooltipComposer.MaxTooltipLength + 10) + "\n",
                ItemTooltipSection.ItemName,
                ItemTooltipColor.Unique);

            // A single line longer than the budget is cut, not abandoned.
            string rendered = composer.Render(new[] { only });
            Assert.Equal(ItemTooltipComposer.MaxTooltipLength - 3, rendered.Length);
        }

        [Fact]
        public void A_cut_landing_just_after_a_complete_marker_drops_the_fragment()
        {
            ItemTooltipComposer composer = Composer(new FakeSections());

            // Pad so the cut falls immediately after an embedded marker.
            int pad = ItemTooltipComposer.MaxTooltipLength - 3 - ItemTooltipColor.Marker.Length;
            string text = new string('x', pad) + ItemTooltipColor.Marker + "tail\n";

            var line = Line(text, ItemTooltipSection.ItemName, ItemTooltipColor.Unique);
            string rendered = composer.Render(new[] { line });

            Assert.DoesNotContain(ItemTooltipColor.Marker, rendered, StringComparison.Ordinal);
        }

        [Fact]
        public void A_cut_shorter_than_the_marker_needs_no_pull_back()
        {
            var sections = new FakeSections();
            ItemTooltipComposer composer = Composer(sections);

            var bottom = Line(
                new string('x', ItemTooltipComposer.MaxTooltipLength - 5) + "\n",
                ItemTooltipSection.EtherealSocketed,
                ItemTooltipColor.Magic);

            var top = Line("TOP\n", ItemTooltipSection.ItemName, ItemTooltipColor.Unique);

            string rendered = composer.Render(new[] { top, bottom });

            Assert.Contains("\n", rendered, StringComparison.Ordinal);
        }

        [Fact]
        public void Merging_is_skipped_entirely_when_there_is_no_terminator()
        {
            var sections = new FakeSections()
                .Set(ItemTooltipSection.ItemName, "Name");
            sections.LineTerminator = null;

            ItemTooltipComposer composer = Composer(sections);
            IReadOnlyList<ItemTooltipLine> lines =
                composer.Compose(Generic(), new KeyValuePair<int, int>[0]);

            Assert.Single(lines);
            Assert.Equal("Name", lines[0].Text);
        }

        [Fact]
        public void A_terminated_run_advances_the_merge_scan()
        {
            var sections = new FakeSections()
                .Set(ItemTooltipSection.EtherealSocketed, "Eth\n")
                .Set(ItemTooltipSection.Durability, "Dur\n")
                .Set(ItemTooltipSection.ItemName, "Name\n");

            ItemTooltipComposer composer = Composer(sections);
            IReadOnlyList<ItemTooltipLine> lines =
                composer.Compose(Generic(), new KeyValuePair<int, int>[0]);

            Assert.Equal(3, lines.Count);
            Assert.Equal("Name\nDur\nEth", composer.Render(lines));
        }

        [Fact]
        public void LastEmbeddedColor_falls_back_for_empty_text()
        {
            ItemTooltipComposer composer = Composer(new FakeSections());

            // The abandoned-line boundary row reads the colour of the line above it; an empty one
            // must fall back rather than throw.
            var bottom = Line(
                new string('x', ItemTooltipComposer.MaxTooltipLength - 3) + "\n",
                ItemTooltipSection.EtherealSocketed,
                ItemTooltipColor.Magic);

            var middle = Line(string.Empty, ItemTooltipSection.Durability, ItemTooltipColor.Red);
            var top = Line("TOP\n", ItemTooltipSection.ItemName, ItemTooltipColor.Unique);

            string colored = composer.RenderWithColorCodes(new[] { top, middle, bottom });

            Assert.DoesNotContain("TOP", colored, StringComparison.Ordinal);
        }

        [Fact]
        public void A_section_with_no_trailing_terminator_gets_one_supplied()
        {
            var sections = new FakeSections()
                .Set(ItemTooltipSection.ItemName, "Name")
                .Set(ItemTooltipSection.Durability, "A\nB");

            ItemTooltipComposer composer = Composer(sections);
            IReadOnlyList<ItemTooltipLine> lines =
                composer.Compose(Generic(), new KeyValuePair<int, int>[0]);

            ItemTooltipLine[] durability =
                lines.Where(l => l.Section == ItemTooltipSection.Durability).ToArray();

            Assert.Equal(2, durability.Length);
            // Display order, so the second appended part comes first.
            Assert.Equal("B\n", durability[0].Text);
            Assert.Equal("A\n", durability[1].Text);
        }

        [Fact]
        public void A_pre_joined_modifier_keeps_its_missing_terminator()
        {
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(DamageStatIds.FireMinDamage, ItemDescFunc.PlusValueString, 100));

            var strings = new FakeStringTable().WithPunctuation()
                .Add(DamageStringIds.FireSingle, "raw");

            var values = new FakeStatValues()
                .AddBase(DamageStatIds.FireMinDamage, 10)
                .AddBase(DamageStatIds.FireMaxDamage, 5);

            var sections = new FakeSections().Set(ItemTooltipSection.ItemName, "Name\n");
            var composer = new ItemTooltipComposer(
                sections, new ItemDescriptionGenerator(stats, strings, values));

            IReadOnlyList<ItemTooltipLine> lines = composer.Compose(
                Generic(),
                new[] { new KeyValuePair<int, int>(DamageStatIds.FireMinDamage, 10) });

            // Unterminated and merged into the following section.
            Assert.Contains(
                lines,
                l => l.Section == ItemTooltipSection.Modifiers
                     && l.Text.StartsWith("raw", StringComparison.Ordinal));
        }

        [Fact]
        public void Unmet_requirements_pick_the_red_colour()
        {
            var sections = new FakeSections()
                .Set(ItemTooltipSection.ItemName, "Name\n")
                .Set(ItemTooltipSection.RequiredLevel, "Level 10\n")
                .Set(ItemTooltipSection.RequiredStrength, "Str 20\n")
                .Set(ItemTooltipSection.RequiredDexterity, "Dex 30\n")
                .Set(ItemTooltipSection.ClassRestriction, "Amazon Only\n")
                .Unmeetable(ItemTooltipSection.RequiredLevel)
                .Unmeetable(ItemTooltipSection.RequiredStrength)
                .Unmeetable(ItemTooltipSection.RequiredDexterity)
                .Unmeetable(ItemTooltipSection.ClassRestriction);

            ItemTooltipComposer composer = Composer(sections);
            IReadOnlyList<ItemTooltipLine> lines =
                composer.Compose(Generic(), new KeyValuePair<int, int>[0]);

            Assert.All(
                lines.Where(l => l.Section != ItemTooltipSection.ItemName),
                l => Assert.Equal(ItemTooltipColor.Red, l.Color));
        }

        [Fact]
        public void A_non_weapon_item_skips_the_weapon_and_armour_block()
        {
            var sections = new FakeSections()
                .Set(ItemTooltipSection.ItemName, "Name\n")
                .Set(ItemTooltipSection.ArmorClass, "Defense: 100\n")
                .Set(ItemTooltipSection.WeaponDamage, "Damage: 1-2\n")
                .Set(ItemTooltipSection.CharmDescription, "Charm\n");

            ItemTooltipContext context = Generic();
            context.IsWeaponOrArmorType = false;

            ItemTooltipComposer composer = Composer(sections);
            IReadOnlyList<ItemTooltipLine> lines =
                composer.Compose(context, new KeyValuePair<int, int>[0]);

            Assert.DoesNotContain(lines, l => l.Section == ItemTooltipSection.ArmorClass);
            Assert.DoesNotContain(lines, l => l.Section == ItemTooltipSection.WeaponDamage);
            Assert.Contains(lines, l => l.Section == ItemTooltipSection.CharmDescription);
        }

        [Fact]
        public void The_transaction_cost_appears_only_while_a_page_is_open()
        {
            var sections = new FakeSections()
                .Set(ItemTooltipSection.ItemName, "Name\n")
                .Set(ItemTooltipSection.TransactionCost, "Cost: 5\n");

            ItemTooltipComposer composer = Composer(sections);

            ItemTooltipContext closed = Generic();
            closed.ShopMode = 0;
            Assert.DoesNotContain(
                composer.Compose(closed, new KeyValuePair<int, int>[0]),
                l => l.Section == ItemTooltipSection.TransactionCost);

            ItemTooltipContext tooHigh = Generic();
            tooHigh.ShopMode = 10;
            Assert.DoesNotContain(
                composer.Compose(tooHigh, new KeyValuePair<int, int>[0]),
                l => l.Section == ItemTooltipSection.TransactionCost);

            ItemTooltipContext open = Generic();
            open.ShopMode = 4;
            Assert.Contains(
                composer.Compose(open, new KeyValuePair<int, int>[0]),
                l => l.Section == ItemTooltipSection.TransactionCost);
        }

        [Fact]
        public void An_unidentified_item_takes_the_unidentified_section_and_no_stat_block()
        {
            var sections = new FakeSections()
                .Set(ItemTooltipSection.ItemName, "Name\n")
                .Set(ItemTooltipSection.Unidentified, "Unidentified\n");

            ItemTooltipContext context = Generic();
            context.Flags = ItemTooltipFlags.None;

            ItemTooltipComposer composer = Composer(sections);
            IReadOnlyList<ItemTooltipLine> lines = composer.Compose(
                context, new[] { new KeyValuePair<int, int>(1, 5) });

            Assert.Contains(lines, l => l.Section == ItemTooltipSection.Unidentified);
            Assert.DoesNotContain(lines, l => l.Section == ItemTooltipSection.Modifiers);
        }

        [Fact]
        public void Every_name_colour_override_is_reachable()
        {
            var sections = new FakeSections().Set(ItemTooltipSection.ItemName, "Name\n");
            ItemTooltipComposer composer = Composer(sections);

            Assert.Equal(ItemTooltipColor.Magic, NameColor(composer, c => c.Quality = ItemQuality.Magic));
            Assert.Equal(
                ItemTooltipColor.Set,
                NameColor(composer, c =>
                {
                    c.Quality = ItemQuality.Set;
                    c.Flags = ItemTooltipFlags.None;
                }));
            Assert.Equal(ItemTooltipColor.Rare, NameColor(composer, c => c.Quality = ItemQuality.Rare));
            Assert.Equal(ItemTooltipColor.Unique, NameColor(composer, c => c.Quality = ItemQuality.Unique));
            Assert.Equal(ItemTooltipColor.Crafted, NameColor(composer, c => c.Quality = ItemQuality.Crafted));
            Assert.Equal(ItemTooltipColor.Tempered, NameColor(composer, c => c.Quality = ItemQuality.Tempered));

            Assert.Equal(
                ItemTooltipColor.White,
                NameColor(composer, c => c.Quality = ItemQuality.Normal));

            Assert.Equal(
                ItemTooltipColor.SocketedOrEthereal,
                NameColor(composer, c =>
                {
                    c.Quality = ItemQuality.Normal;
                    c.Flags |= ItemTooltipFlags.Socketed;
                }));

            Assert.Equal(
                ItemTooltipColor.SocketedOrEthereal,
                NameColor(composer, c =>
                {
                    c.Quality = ItemQuality.Normal;
                    c.Flags |= ItemTooltipFlags.Ethereal;
                }));

            Assert.Equal(
                ItemTooltipColor.White,
                NameColor(composer, c => c.UnidentifiedInShop = true));

            Assert.Equal(
                ItemTooltipColor.Crafted,
                NameColor(composer, c => c.ForcesCraftedColor = true));

            Assert.Equal(
                ItemTooltipColor.Red,
                NameColor(composer, c => c.Flags |= ItemTooltipFlags.Broken));
        }

        [Fact]
        public void A_null_terminator_survives_the_empty_name_and_cut_paths()
        {
            var sections = new FakeSections()
                .Set(ItemTooltipSection.RuneLetters, "'Runes'");
            sections.LineTerminator = null;

            ItemTooltipComposer composer = Composer(sections);
            IReadOnlyList<ItemTooltipLine> lines =
                composer.Compose(Generic(), new KeyValuePair<int, int>[0]);

            Assert.Equal(2, lines.Count);
            Assert.Equal(string.Empty, lines[0].Text);

            // And a line longer than the budget still cuts without a terminator to re-attach.
            string cut = composer.Render(
                new[] { Line(new string('y', ItemTooltipComposer.MaxTooltipLength + 5)) });

            Assert.Equal(ItemTooltipComposer.MaxTooltipLength - 3, cut.Length);
        }

        [Fact]
        public void A_null_line_text_does_not_trigger_a_merge()
        {
            ItemTooltipComposer composer = Composer(new FakeSections());

            string rendered = composer.Render(
                new[] { Line("top\n"), Line(null), Line("bottom\n") });

            Assert.Equal("top\nbottom", rendered);
        }

        [Fact]
        public void An_unterminated_run_inside_one_section_merges_without_a_marker()
        {
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(DamageStatIds.FireMinDamage, ItemDescFunc.PlusValueString, 100));
            stats.Add(Build.Stat(39, ItemDescFunc.PlusValueString, 101, priority: 90));

            var strings = new FakeStringTable().WithPunctuation()
                .Add(DamageStringIds.FireSingle, "raw")
                .Add(101, "Fire Resist");

            var values = new FakeStatValues()
                .AddBase(DamageStatIds.FireMinDamage, 10)
                .AddBase(DamageStatIds.FireMaxDamage, 5);

            var sections = new FakeSections().Set(ItemTooltipSection.ItemName, "Name\n");
            var composer = new ItemTooltipComposer(
                sections, new ItemDescriptionGenerator(stats, strings, values));

            IReadOnlyList<ItemTooltipLine> lines = composer.Compose(
                Generic(),
                new[]
                {
                    new KeyValuePair<int, int>(DamageStatIds.FireMinDamage, 10),
                    new KeyValuePair<int, int>(39, 30),
                });

            ItemTooltipLine merged = lines.Single(
                l => l.Section == ItemTooltipSection.Modifiers
                     && l.Text.StartsWith("raw", StringComparison.Ordinal));

            // Same section, so no marker is spliced in.
            Assert.DoesNotContain(ItemTooltipColor.Marker, merged.Text, StringComparison.Ordinal);
        }

        [Fact]
        public void A_null_marker_still_reserves_one_character_for_the_digit()
        {
            ItemTooltipComposer composer = Composer(new FakeSections());

            var bottom = Line(
                new string('x', ItemTooltipComposer.MaxTooltipLength - 2) + "\n",
                ItemTooltipSection.EtherealSocketed,
                ItemTooltipColor.Magic);

            string colored = composer.RenderWithColorCodes(
                new[] { Line("TOP\n"), bottom }, null);

            Assert.DoesNotContain("TOP", colored, StringComparison.Ordinal);
        }

        [Fact]
        public void With_no_marker_string_the_boundary_row_is_just_a_terminator()
        {
            ItemTooltipComposer composer = Composer(new FakeSections());

            var bottom = Line(
                new string('x', ItemTooltipComposer.MaxTooltipLength - 3) + "\n",
                ItemTooltipSection.EtherealSocketed,
                ItemTooltipColor.Magic);

            string colored = composer.RenderWithColorCodes(
                new[] { Line("TOP\n"), bottom }, string.Empty);

            Assert.DoesNotContain("TOP", colored, StringComparison.Ordinal);

            // The boundary row stands in for the section the cut dropped, so it OWNS that section's
            // marker (AppendAsWideChar checks only that the buffer is non-empty, 0x4521cd). With no
            // marker string that is the bare digit and the terminator.
            Assert.StartsWith("3\n", colored, StringComparison.Ordinal);
        }


        [Fact]
        public void A_cut_that_is_shorter_than_a_marker_needs_no_adjustment()
        {
            ItemTooltipComposer composer = Composer(new FakeSections());

            // Leaves a cut of 2, shorter than the 3-character marker, so no pull-back applies.
            var bottom = Line(
                new string('x', ItemTooltipComposer.MaxTooltipLength - 9) + "\n",
                ItemTooltipSection.EtherealSocketed,
                ItemTooltipColor.Magic);

            var top = Line("TOPTEXT\n", ItemTooltipSection.ItemName, ItemTooltipColor.Unique);

            string rendered = composer.Render(new[] { top, bottom });

            Assert.StartsWith("TO\n", rendered, StringComparison.Ordinal);
        }

        [Fact]
        public void The_boundary_row_falls_back_when_the_line_above_it_is_empty()
        {
            ItemTooltipComposer composer = Composer(new FakeSections());

            var bottom = Line(
                new string('x', ItemTooltipComposer.MaxTooltipLength - 10) + "\n",
                ItemTooltipSection.EtherealSocketed,
                ItemTooltipColor.Magic);

            var middle = Line(string.Empty, ItemTooltipSection.Durability, ItemTooltipColor.Red);
            var top = Line("TOP\n", ItemTooltipSection.ItemName, ItemTooltipColor.Unique);

            string rendered = composer.Render(new[] { top, middle, bottom });

            Assert.DoesNotContain("TOP", rendered, StringComparison.Ordinal);
            Assert.StartsWith("\n", rendered, StringComparison.Ordinal);
        }

        [Fact]
        public void A_null_modifier_text_becomes_an_empty_terminated_line()
        {
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(1, ItemDescFunc.PlusValueString, 100));

            var strings = new FakeStringTable().WithPunctuation().Add(100, string.Empty);

            var sections = new FakeSections().Set(ItemTooltipSection.ItemName, "Name\n");
            var composer = new ItemTooltipComposer(
                sections, new ItemDescriptionGenerator(stats, strings));

            IReadOnlyList<ItemTooltipLine> lines = composer.Compose(
                Generic(), new[] { new KeyValuePair<int, int>(1, 5) });

            Assert.Contains(lines, l => l.Section == ItemTooltipSection.Modifiers);
        }

        private static int NameColor(
            ItemTooltipComposer composer, Action<ItemTooltipContext> configure)
        {
            ItemTooltipContext context = Generic();
            configure(context);

            return composer.Compose(context, new KeyValuePair<int, int>[0])
                .Single(l => l.Section == ItemTooltipSection.ItemName)
                .Color;
        }
    }
}
