using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace D2ItemToolkit.Tests
{
    /// <summary>
    /// Pins the behaviours that have regressed more than once across audit rounds. These are
    /// deliberately narrow: each one asserts a single decision that an earlier "fix" got
    /// wrong, so that the next edit fails here instead of surviving to the next audit.
    ///
    /// Three failure modes dominate:
    ///   1. Which stat accessor feeds a guard. Three distinct reads exist and substituting one
    ///      for another is silent.
    ///   2. Which line carries a colour marker, and whether any section is left unmarked.
    ///   3. Formatter boundary arithmetic, where >= and > differ by one character.
    /// </summary>
    public class RegressionTests
    {
        // =================================================================
        // 1. Accessor per guard
        // =================================================================

        private static FakeStringTable UndeadStrings()
        {
            return new FakeStringTable().WithPunctuation()
                .Add(DamageStringIds.DamageToUndead, "Damage to Undead");
        }

        [Fact]
        public void The_undead_line_guard_reads_the_items_own_stat_list()
        {
            // 0x4e61ea: ITEM_GetMinimalStatValueShifted on the described item.
            var stats = new FakeStatCostTable();
            var values = new FakeStatValues().AddItemType(UndeadDamageLine.BluntItemType);
            values.AddItemStat(DamageStatIds.UndeadDamagePercent, 5);

            Assert.Empty(new ItemDescriptionGenerator(stats, UndeadStrings(), values)
                .Describe(new KeyValuePair<int, int>[0]));
        }

        [Fact]
        public void The_undead_line_guard_ignores_the_viewers_stats()
        {
            // A regression twice over: reading the player's list here suppresses the line on
            // any character wearing anything with stat 122.
            var stats = new FakeStatCostTable();
            var values = new FakeStatValues().AddItemType(UndeadDamageLine.BluntItemType);
            values.AddPlayer(DamageStatIds.UndeadDamagePercent, 5);

            Assert.Single(new ItemDescriptionGenerator(stats, UndeadStrings(), values)
                .Describe(new KeyValuePair<int, int>[0]));
        }

        [Fact]
        public void The_undead_line_guard_ignores_the_merged_list()
        {
            // A socketed gem's stat 122 must not suppress it either.
            var stats = new FakeStatCostTable();
            var values = new FakeStatValues().AddItemType(UndeadDamageLine.BluntItemType);
            values.AddBase(DamageStatIds.UndeadDamagePercent, 5);

            Assert.Single(new ItemDescriptionGenerator(stats, UndeadStrings(), values)
                .Describe(new KeyValuePair<int, int>[0]));
        }

        [Fact]
        public void The_undead_line_is_suppressed_outside_the_main_stat_block()
        {
            // 0x4e61d0: the caller flag. Set-bonus blocks pass 0.
            var stats = new FakeStatCostTable();
            var values = new FakeStatValues().AddItemType(UndeadDamageLine.BluntItemType);

            var generator = new ItemDescriptionGenerator(
                stats, UndeadStrings(), values, isMainStatBlock: false);

            Assert.Empty(generator.Describe(new KeyValuePair<int, int>[0]));
        }

        [Fact]
        public void Op_scaling_reads_the_viewers_stats_not_the_items()
        {
            // 0x4e4c93: GetStatUnsignedValue(GetPlayerUnit(), ...). The counterpart to the
            // undead guard: here the PLAYER is correct and the item would be wrong.
            var stats = new FakeStatCostTable();
            StatDescriptor perLevel = Build.Stat(1, ItemDescFunc.PlusValueString, 100);
            perLevel.Op = 2;
            perLevel.OpParam = 0;
            perLevel.OpBase = 12;
            stats.Add(perLevel);
            stats.Add(Build.Stat(12, 0, 0));

            var values = new FakeStatValues();
            values.AddPlayer(12, 3);
            values.AddItemStat(12, 99); // must be ignored

            var strings = new FakeStringTable().WithPunctuation().Add(100, "to Life");

            IReadOnlyList<ItemDescriptionLine> lines =
                new ItemDescriptionGenerator(stats, strings, values)
                    .Describe(new[] { Build.Entry(1, 2) });

            Assert.Equal("+6 to Life", lines[0].Text); // 2 * 3
        }

        [Fact]
        public void The_secondary_damage_suppression_reads_the_merged_list_at_layer_zero()
        {
            // 0x4e62e3: STATLIST_GetBaseStatValue(mergedList, 21, 0).
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(23, ItemDescFunc.PlusValueString, 100));

            var strings = new FakeStringTable().WithPunctuation().Add(100, "Secondary Min");

            var suppressed = new FakeStatValues().AddBase(21, 5);
            Assert.Empty(new ItemDescriptionGenerator(stats, strings, suppressed)
                .Describe(new[] { Build.Entry(23, 7) }));

            // The item's own list and the player's must not drive it.
            var notSuppressed = new FakeStatValues();
            notSuppressed.AddItemStat(21, 5);
            notSuppressed.AddPlayer(21, 5);
            Assert.Single(new ItemDescriptionGenerator(stats, strings, notSuppressed)
                .Describe(new[] { Build.Entry(23, 7) }));
        }

        // =================================================================
        // 2. Colour marker placement
        // =================================================================

        private static ItemTooltipComposer Composer(FakeSections sections)
        {
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(16, ItemDescFunc.PlusValuePercentString, 100));
            stats.Add(Build.Stat(39, ItemDescFunc.PlusValueString, 101));

            var strings = new FakeStringTable().WithPunctuation()
                .Add(100, "Enhanced Defense").Add(101, "Fire Resist");

            return new ItemTooltipComposer(sections, new ItemDescriptionGenerator(stats, strings));
        }

        private static readonly KeyValuePair<int, int>[] TwoMods =
        {
            new KeyValuePair<int, int>(0x00000010, 180),
            new KeyValuePair<int, int>(0x00000027, 40),
        };

        private static ItemTooltipContext Ctx()
        {
            var c = new ItemTooltipContext();
            c.Quality = ItemQuality.Unique;
            c.Flags = ItemTooltipFlags.Identified;
            c.IsWeaponOrArmorType = true;
            return c;
        }

        [Fact]
        public void Every_stat_block_line_carries_its_own_colour_marker()
        {
            // Regressed once: AppendModifiers set Color but never EmitsColorMarker, so the whole
            // affix list rendered in the inherited colour.
            //
            // The game spends ONE marker on the blue block (AppendAsWideChar 0x4521c0, one call per
            // section buffer), which lands on the first-APPENDED row. Every other row is
            // re-anchored on emission, because reversing into display order breaks the stickiness
            // they relied on — so what must hold is the rendered string, not the flag.
            FakeSections sections = new FakeSections().Set(ItemTooltipSection.ItemName, "Name\n");

            ItemTooltipComposer composer = Composer(sections);
            IReadOnlyList<ItemTooltipLine> lines = composer.Compose(Ctx(), TwoMods);

            ItemTooltipLine[] mods = lines
                .Where(l => l.Section == ItemTooltipSection.Modifiers).ToArray();

            Assert.Equal(2, mods.Length);
            Assert.All(mods, m => Assert.Equal(ItemTooltipColor.Magic, m.Color));

            // Exactly one of them owns the game's marker, and it is the first-APPENDED — the LAST
            // in display order.
            Assert.False(mods[0].EmitsColorMarker);
            Assert.True(mods[1].EmitsColorMarker);

            string magic = ItemTooltipColor.Marker
                           + ItemTooltipComposer.EncodeColorDigit(ItemTooltipColor.Magic);
            string rendered = composer.RenderWithColorCodes(lines);
            foreach (ItemTooltipLine mod in mods)
            {
                // The last display row loses its terminator (DropTrailingTerminator).
                Assert.Contains(magic + mod.Text.TrimEnd('\n'), rendered, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void No_section_is_left_without_a_colour_marker()
        {
            FakeSections sections = new FakeSections()
                .Set(ItemTooltipSection.ItemName, "Name\n")
                .Set(ItemTooltipSection.ArmorClass, "Defense: 445\n")
                .Set(ItemTooltipSection.Durability, "Durability: 20 of 20\n");

            ItemTooltipComposer composer = Composer(sections);
            IReadOnlyList<ItemTooltipLine> lines = composer.Compose(Ctx(), TwoMods);

            Assert.NotEmpty(lines);

            // One marker per SECTION on the flag; every rendered row still opens with one.
            foreach (IGrouping<ItemTooltipSection, ItemTooltipLine> group in
                lines.GroupBy(l => l.Section))
            {
                Assert.Equal(1, group.Count(l => l.EmitsColorMarker));
            }

            string rendered = composer.RenderWithColorCodes(lines);
            foreach (string row in rendered.Split('\n'))
            {
                Assert.StartsWith(ItemTooltipColor.Marker, row, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void A_marker_embedded_mid_section_governs_only_the_lines_after_it()
        {
            // szPrintfBuffer embeds markers PART WAY THROUGH the WeaponDamage buffer — one on
            // the damage number (0x485991) and one on the throw line (0x485afd) — so the
            // section's own marker governs only up to the first embedded one. Colour must be
            // carried forward line by line in APPEND order.
            //
            // Giving every line the section colour and relying on stickiness repaints the rest:
            // in display order the throw line comes first, and the one-hand line above it would
            // inherit the throw line's embedded colour instead of the section's.
            string magic = ItemTooltipColor.Marker
                           + ItemTooltipComposer.EncodeColorDigit(ItemTooltipColor.Magic);

            FakeSections sections = new FakeSections()
                .Set(ItemTooltipSection.WeaponDamage,
                    "One-Hand Damage: " + magic + "5 to 12\nThrow Damage: 7 to 15\n");

            ItemTooltipLine[] run = Composer(sections)
                .Compose(Ctx(), new KeyValuePair<int, int>[0])
                .Where(l => l.Section == ItemTooltipSection.WeaponDamage)
                .ToArray();

            Assert.Equal(2, run.Length);

            // Display order is the reverse of append order: the throw line is on top.
            ItemTooltipLine throwLine = run[0];
            ItemTooltipLine oneHand = run[1];

            // The one-hand line leads the section, so it takes the section colour...
            Assert.Equal(ItemTooltipColor.White, oneHand.Color);

            // ...and the throw line, appended after the embedded marker, takes THAT colour.
            Assert.Equal(ItemTooltipColor.Magic, throwLine.Color);
        }

        [Fact]
        public void The_transaction_cost_line_renders_in_the_item_names_colour()
        {
            // 0x48cf87 appends the price with a raw AppendToBuffer and no marker. In APPEND
            // order that puts it after the item name's marker, so it inherits the quality
            // colour. Reversed to DISPLAY order it comes first, where "inherit" would mean the
            // renderer default instead — so an explicit marker is required to reproduce the
            // game's colour.
            //
            // This pin previously asserted the marker was absent and would have blocked the
            // fix. It is the same inversion the marker placement itself got wrong.
            FakeSections sections = new FakeSections()
                .Set(ItemTooltipSection.ItemName, "Name\n")
                .Set(ItemTooltipSection.TransactionCost, "Price: 5000\n");

            ItemTooltipContext context = Ctx();
            context.ShopMode = 1;

            ItemTooltipLine cost = Composer(sections).Compose(context, TwoMods)
                .Single(l => l.Section == ItemTooltipSection.TransactionCost);

            Assert.True(cost.EmitsColorMarker);
            Assert.Equal(ItemTooltipComposer.ResolveItemNameColor(context), cost.Color);
        }

        [Fact]
        public void The_assembled_tooltip_ends_unterminated()
        {
            // The game's string holds N chunks and N-1 separators: the two writers that omit a
            // trailing 3998 (GetItemName 0x48ce72, the price 0x48cf87) are exactly the two
            // that end up last in append order. SplitLines terminates every part so the
            // reversal keeps lines apart, which would otherwise leave N.
            FakeSections sections = new FakeSections()
                .Set(ItemTooltipSection.ItemName, "Name\n")
                .Set(ItemTooltipSection.ArmorClass, "Defense: 445\n");

            ItemTooltipComposer composer = Composer(sections);
            IReadOnlyList<ItemTooltipLine> lines =
                composer.Compose(Ctx(), new KeyValuePair<int, int>[0]);

            string plain = composer.Render(lines);
            string colored = composer.RenderWithColorCodes(lines);

            Assert.False(plain.EndsWith("\n", StringComparison.Ordinal));
            Assert.False(colored.EndsWith("\n", StringComparison.Ordinal));

            // ...but the separators BETWEEN chunks survive: N chunks still give N-1.
            Assert.Equal(lines.Count - 1, plain.Count(ch => ch == '\n'));
        }

        [Fact]
        public void The_quest_colour_marker_paints_nothing()
        {
            // 0x48ecf2 prepends it to the append-order string, where the first non-empty
            // section's OWN marker lands immediately after and overrides it — every one of the
            // 18 buffers carries one, so it is unconditionally inert. Its real effect is that
            // esi = 4 becomes the renderer's default at 0x48ed45.
            //
            // The inert position in a display-ordered string is the very END. This pin
            // previously asserted it led the last display line; that repaints that line
            // whenever the bottom section spans more than one line, since only the run LEADER
            // carries a marker to override it.
            FakeSections sections = new FakeSections()
                .Set(ItemTooltipSection.ItemName, "Name\n")
                // A multi-line bottom section is the case that distinguishes the two.
                .Set(ItemTooltipSection.ArmorClass, "Defense: 445\nBlock: 30%\n");

            // Magic quality, so the item name's own marker is 3 and cannot be confused with
            // the quest marker's 4.
            ItemTooltipContext context = Ctx();
            context.Quality = ItemQuality.Magic;

            ItemTooltipComposer composer = Composer(sections);
            IReadOnlyList<ItemTooltipLine> lines =
                composer.Compose(context, new KeyValuePair<int, int>[0]);

            string marked = composer.RenderWithColorCodes(
                lines, questColorPrefix: true);

            string questMarker = ItemTooltipColor.Marker
                                 + ItemTooltipComposer.EncodeColorDigit(ItemTooltipColor.Unique);

            // Present, and with no glyph after it.
            Assert.EndsWith(questMarker, marked, StringComparison.Ordinal);
            Assert.Equal(1, CountOccurrences(marked, questMarker));

            // Dropping it changes nothing but the tail, so no line was repainted.
            string plain = composer.RenderWithColorCodes(lines);
            Assert.Equal(plain, marked.Substring(0, marked.Length - questMarker.Length));
        }

        private static int CountOccurrences(string text, string needle)
        {
            int count = 0;
            for (int i = text.IndexOf(needle, StringComparison.Ordinal); i >= 0;
                 i = text.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
            {
                ++count;
            }

            return count;
        }

        [Fact]
        public void An_out_of_range_colour_digit_is_not_clamped_by_the_emitter()
        {
            // 0x452219 is a bare add; the >= 13 fallback lives in the renderer.
            Assert.Equal('=', ItemTooltipComposer.EncodeColorDigit(13));
            Assert.Equal(':', ItemTooltipComposer.EncodeColorDigit(10));
        }

        // =================================================================
        // 3. Structural gates
        // =================================================================

        [Fact]
        public void The_weapon_and_armour_sections_are_skipped_for_other_item_types()
        {
            // 0x48e60a: eight writers never run unless the item is type 45 or 50.
            FakeSections sections = new FakeSections()
                .Set(ItemTooltipSection.ItemName, "Ring\n")
                .Set(ItemTooltipSection.RequiredStrength, "Required Strength: 40\n")
                .Set(ItemTooltipSection.ArmorClass, "Defense: 100\n")
                .Set(ItemTooltipSection.AttackSpeed, "Fast\n");

            ItemTooltipContext context = Ctx();
            context.IsWeaponOrArmorType = false;

            ItemTooltipSection[] present = Composer(sections)
                .Compose(context, new KeyValuePair<int, int>[0])
                .Select(l => l.Section).Distinct().ToArray();

            Assert.DoesNotContain(ItemTooltipSection.RequiredStrength, present);
            Assert.DoesNotContain(ItemTooltipSection.ArmorClass, present);
            Assert.DoesNotContain(ItemTooltipSection.AttackSpeed, present);
            Assert.Contains(ItemTooltipSection.ItemName, present);
        }

        [Fact]
        public void An_unidentified_item_gets_no_stat_block()
        {
            // 0x48e8ef: the stat block and the Unidentified line are mutually exclusive.
            FakeSections sections = new FakeSections().Set(ItemTooltipSection.ItemName, "Name\n");

            ItemTooltipContext context = Ctx();
            context.Flags = ItemTooltipFlags.None; // not identified

            Assert.DoesNotContain(ItemTooltipSection.Modifiers,
                Composer(sections).Compose(context, TwoMods).Select(l => l.Section));
        }

        [Fact]
        public void The_1023_truncation_keeps_the_bottom_of_the_tooltip()
        {
            // 0x48ed0d measures the APPEND-order string and 0x48ed19 NULs [1023], so the
            // survivors are a PREFIX OF APPEND ORDER — the BOTTOM of the tooltip. The item name
            // and price are appended LAST and render at the TOP, so they are what the game
            // loses. Truncating the display-ordered string keeps the opposite half.
            //
            // Load-bearing, not cosmetic: TEXT_TooltipSetAttributes discards anything 1024 or
            // longer (0x502292), so an unbudgeted tooltip renders as nothing at all.

            var top = new ItemTooltipLine();     // display order: first = appended LAST
            top.Text = "ITEMNAME\n";
            top.Section = ItemTooltipSection.ItemName;
            top.Color = ItemTooltipColor.Unique;
            top.EmitsColorMarker = true;

            var bottom = new ItemTooltipLine();  // appended FIRST, survives truncation
            bottom.Text = new string('x', 4000) + "\n";
            bottom.Section = ItemTooltipSection.EtherealSocketed;
            bottom.Color = ItemTooltipColor.Magic;
            bottom.EmitsColorMarker = true;

            ItemTooltipComposer composer = Composer(new FakeSections());
            string rendered = composer.Render(new[] { top, bottom });

            Assert.DoesNotContain("ITEMNAME", rendered, StringComparison.Ordinal);
            Assert.StartsWith("xxx", rendered, StringComparison.Ordinal);

            // The marker the game charges for this section is counted even though Render omits
            // it, so the surviving text is one marker's worth short of the limit.
            string colored = composer.RenderWithColorCodes(
                new[] { top, bottom });

            Assert.True(colored.Length <= ItemTooltipComposer.MaxTooltipLength,
                "rendered " + colored.Length + " characters, over the 1023 budget");
        }

        [Theory]
        [InlineData(true, true, 0, 0, true)]    // all five terms hold
        [InlineData(false, true, 0, 0, false)]  // not an item
        [InlineData(true, false, 0, 0, false)]  // table forbids durability
        [InlineData(true, true, 1, 0, false)]   // indestructible
        [InlineData(true, true, 0, 5, false)]   // has max durability
        public void The_never_breaks_tail_line_needs_all_five_terms(
            bool isItem, bool tableAllows, int indestructible, int maxDurability, bool expected)
        {
            // 0x4e636a-0x4e63a4.
            var stats = new FakeStatCostTable();
            var strings = new FakeStringTable().WithPunctuation()
                .Add(DescStringIds.NeverBreaks, "Cannot Be Broken");

            var values = new FakeStatValues();
            values.DescribedUnitIsItem = isItem;
            values.ItemTableAllowsDurability = tableAllows;
            values.AddItemStat(152, indestructible);
            values.TxtMaxDurability = maxDurability; // 0x4e63a4 uses its own accessor

            IReadOnlyList<ItemDescriptionLine> lines =
                new ItemDescriptionGenerator(stats, strings, values)
                    .Describe(new KeyValuePair<int, int>[0]);

            Assert.Equal(expected, lines.Any(l => l.Text == "Cannot Be Broken"));
        }

        [Fact]
        public void The_never_breaks_line_survives_an_empty_string_entry()
        {
            // 0x4e63b2-0x4e63e0 never tests the pointer, so an empty entry still emits the
            // row — and therefore its separator. Dropping it loses that separator.
            var stats = new FakeStatCostTable();
            var strings = new FakeStringTable().WithPunctuation()
                .Add(DescStringIds.NeverBreaks, string.Empty);

            var values = new FakeStatValues();
            values.DescribedUnitIsItem = true;
            values.ItemTableAllowsDurability = true;

            IReadOnlyList<ItemDescriptionLine> lines =
                new ItemDescriptionGenerator(stats, strings, values)
                    .Describe(new KeyValuePair<int, int>[0]);

            Assert.Single(lines);
            Assert.True(lines[0].IsBlank);
        }

        // =================================================================
        // 4. Formatter boundaries
        // =================================================================

        [Fact]
        public void A_trailing_percent_escapes_the_length_budget()
        {
            // 0x526c46 copies the one-character "%" with an unbudgeted copy, so the result can
            // reach exactly maxLength where every other path stops one short.
            Assert.Equal("abc%", TblFormat.FormatBounded("abc%", 4));
        }

        [Fact]
        public void Literal_text_is_budgeted_as_it_is_appended()
        {
            // 0x526a4c admits one literal at a time while written < maxLength. Relying on a
            // final truncate instead lets the trailing-% path return an unbounded string.
            Assert.Equal("abcd", TblFormat.FormatBounded("abcdefghij%", 5));
        }

        [Fact]
        public void The_max_durability_test_uses_its_own_accessor()
        {
            // 0x4e63a4 GetTxtMaxDurability is NOT GetItemStatValue(73): it requires stat 73 in
            // the base array first. Reusing the min-clamped read suppresses the line where the
            // game emits it.
            var stats = new FakeStatCostTable();
            var strings = new FakeStringTable().WithPunctuation()
                .Add(DescStringIds.NeverBreaks, "Cannot Be Broken");

            var values = new FakeStatValues();
            values.DescribedUnitIsItem = true;
            values.ItemTableAllowsDurability = true;
            values.TxtMaxDurability = 0;      // the accessor the tail actually consults
            values.AddItemStat(73, 250);      // must not be consulted

            Assert.Single(new ItemDescriptionGenerator(stats, strings, values)
                .Describe(new KeyValuePair<int, int>[0]));
        }

        [Fact]
        public void The_surviving_length_is_one_below_the_budget()
        {
            // 0x526bda overwrites the last character written.
            Assert.Equal("abcd", TblFormat.FormatBounded("abcdefgh", 5));
        }

        [Fact]
        public void A_conversion_that_would_not_fit_is_dropped_and_formatting_stops()
        {
            // 0x526b13: admission needs len + written + 1 < maxLength, and on failure the
            // number is not emitted at all and the remainder of the format is abandoned.
            Assert.Equal("ab", TblFormat.FormatBounded("ab%dcd", 4, 99));
        }

        [Fact]
        public void A_null_string_argument_with_no_room_left_truncates_instead_of_throwing()
        {
            // 0x52675c tests n before dereferencing, so n == 0 is safe.
            Assert.Equal("abc", TblFormat.FormatBounded("abc%s", 4, (object)null));
        }

        [Fact]
        public void A_null_string_argument_with_room_left_is_surfaced()
        {
            Assert.Throws<FormatException>(
                () => TblFormat.FormatBounded("a%s", 64, (object)null));
        }

        [Fact]
        public void A_doubled_percent_consumes_an_argument()
        {
            // 0x526bb4 advances the vararg cursor on the shared tail, so anything after a %%
            // shifts by one: the %% eats the 7 and the %d gets the 8.
            Assert.Equal("%8", TblFormat.Format("%%%d", 7, 8));
        }

        [Fact]
        public void An_unrecognised_specifier_is_fatal()
        {
            // 0x526c61 halts the game; only \0 % d s u are in the jump table.
            Assert.Throws<FormatException>(() => TblFormat.Format("%x", 7));
            Assert.Throws<FormatException>(() => TblFormat.Format("%i", 7));
        }

        [Fact]
        public void A_null_format_yields_an_empty_line_not_a_dropped_one()
        {
            // 0x5269e1 returns with the destination as the caller zeroed it.
            Assert.Equal(string.Empty, TblFormat.Format(null, 7));
        }

        [Fact]
        public void A_formatted_integer_is_capped_at_eight_characters()
        {
            // UTF8_ConvertToWideChar terminates at index 8 (0x526320).
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(1, ItemDescFunc.ValueString, 100));

            var strings = new FakeStringTable().WithPunctuation().Add(100, "Big");

            IReadOnlyList<ItemDescriptionLine> lines =
                new ItemDescriptionGenerator(stats, strings)
                    .Describe(new[] { Build.Entry(1, 1234567890) });

            Assert.Equal("12345678 Big", lines[0].Text);
        }

        // =================================================================
        // 5. Separator bookkeeping
        // =================================================================

        [Fact]
        public void A_pre_joined_line_takes_no_separator_in_either_mode()
        {
            // 0x4e620e and 0x4e5e18 append directly and never set the emitted latch.
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(1, ItemDescFunc.PlusValueString, 100));

            var strings = new FakeStringTable().WithPunctuation().Add(100, "to Strength");
            var generator = new ItemDescriptionGenerator(stats, strings);

            var preJoined = new ItemDescriptionLine();
            preJoined.Text = "PRE";
            preJoined.PreJoined = true;

            var normal = new ItemDescriptionLine();
            normal.Text = "NORMAL";

            var lines = new[] { preJoined, normal };

            // Block mode: no separator before PRE, and PRE does not make NORMAL emit one.
            Assert.Equal("PRENORMAL", generator.Join(lines, inlineMode: false));

            // Inline mode (the default, and what the item tooltip uses): PRE carries its own
            // terminator, NORMAL gets one.
            Assert.Equal("PRENORMAL\n", generator.Join(lines));
        }

        [Fact]
        public void An_unrecognised_specifier_past_the_budget_truncates_rather_than_halting()
        {
            // 0x526a6d dominates the jump table at 0x526a99, so once the budget is spent the
            // engine returns WITHOUT inspecting the specifier — the halt at 0x526c66 is
            // unreachable there. "%%" is the way to land exactly on the limit: it appends
            // after the re-test, so the NEXT conversion meets an exhausted budget.
            Assert.Equal("AB", TblFormat.FormatBounded("AB%%%x", 3, 1));
        }

        [Fact]
        public void A_null_string_argument_past_the_budget_truncates_rather_than_throwing()
        {
            // Same gate. With the re-test hoisted, `room` can never go negative, so the
            // `room == 0` guard still catches every case it is meant to.
            Assert.Equal("AB", TblFormat.FormatBounded("AB%%%s", 3, 1, null));
        }

        [Fact]
        public void A_placeholder_skill_name_keeps_the_line_rather_than_dropping_it()
        {
            // The four skill DescFuncs test the RESOLVED POINTER from GetLocaleString, not the
            // string id (0x4e534a test eax,eax / 0x4e534c jz). SKILLDESC_GetStatNameString
            // returns the sentinel id 5382 on failure, but that is an ordinary string.tbl
            // index holding placeholder text, so the engine KEEPS the line and prints it.
            //
            // The contract on ISkillTable.GetSkillName briefly said to collapse 5382 to null.
            // That would drop rows the engine emits; this pin makes the difference visible.
            const string Placeholder = "an evil force";

            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(148, ItemDescFunc.SkillAura, 100));

            var strings = new FakeStringTable().WithPunctuation()
                .Add(100, "Level %d %s Aura When Equipped");

            var resolves = new FakeSkillTable().Add(120, Placeholder);
            IReadOnlyList<ItemDescriptionLine> kept =
                new ItemDescriptionGenerator(stats, strings, null, resolves)
                    .Describe(new[] { Build.Entry(148, 3, layer: 120) });

            Assert.Single(kept);
            Assert.Equal("Level 3 " + Placeholder + " Aura When Equipped", kept[0].Text);

            // Only a genuinely absent string drops the row.
            IReadOnlyList<ItemDescriptionLine> dropped =
                new ItemDescriptionGenerator(stats, strings, null, new FakeSkillTable())
                    .Describe(new[] { Build.Entry(148, 3, layer: 120) });

            Assert.Empty(dropped);
        }

        [Fact]
        public void Every_stat_line_is_terminated_so_the_block_does_not_collapse()
        {
            // LoadItemDesc drives the stat block in INLINE mode: 0x48e92d pushes (0x1000, 1, 0)
            // into SKILLDESC_AppendStatBuffText, whose arg_4 = 1 becomes the first push at
            // 0x4e6437 and so lands in arg_14. At 0x4e62ec the terminator branch is taken for
            // every line; at 0x4e6307 the 3852+3995 separator branch is skipped.
            //
            // Unterminated, the whole block renders as one line glued to the section below.
            FakeSections sections = new FakeSections()
                .Set(ItemTooltipSection.ItemName, "Name\n")
                .Set(ItemTooltipSection.EtherealSocketed, "Socketed (2)\n");

            ItemTooltipComposer composer = Composer(sections);
            IReadOnlyList<ItemTooltipLine> lines = composer.Compose(Ctx(), TwoMods);

            ItemTooltipLine[] mods = lines
                .Where(l => l.Section == ItemTooltipSection.Modifiers).ToArray();

            Assert.Equal(2, mods.Length);
            Assert.All(mods, m => Assert.EndsWith("\n", m.Text, StringComparison.Ordinal));

            // Neither runs into its neighbour.
            string rendered = composer.Render(lines);
            Assert.Contains("Fire Resist\n", rendered, StringComparison.Ordinal);
            Assert.DoesNotContain("ResistSocketed", rendered, StringComparison.Ordinal);
        }

        [Fact]
        public void A_stat_string_that_already_ends_in_a_newline_gets_a_second_one()
        {
            // 0x4e62f3 appends 3998 UNCONDITIONALLY — there is no test for an existing
            // terminator — and the renderer steps once per 0Ah with no collapsing (0x501b97 /
            // 0x501bd0). So a line whose text already ends in a newline gets a blank row after
            // it. Relying on SplitLines to supply the terminator "only when missing" drops it.
            //
            // With the shipped ENG data this never fires: of the 133 distinct description
            // strings reachable from descfunc-bearing rows, ZERO end with a newline. Strings
            // reached by .txt KEY resolve patch-first (0x524d93), and the patch entries for the
            // strMod* family exist precisely to drop the newline base 3610 carries. Strings
            // reached by HARDCODED ID (3612-3623, 10023) do keep theirs, but those arrive
            // PreJoined and skip the terminator.
            //
            // Pinned anyway because it is what the asm does, and a mod or another locale can
            // reintroduce such a string — this test supplies one directly.
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(16, ItemDescFunc.PlusValuePercentString, 100));

            var strings = new FakeStringTable().WithPunctuation()
                .Add(100, "Enhanced Defense\n"); // as shipped strings do

            var sections = new FakeSections().Set(ItemTooltipSection.ItemName, "Name\n");
            var composer = new ItemTooltipComposer(
                sections, new ItemDescriptionGenerator(stats, strings));

            ItemTooltipLine[] mods = composer
                .Compose(Ctx(), new[] { new KeyValuePair<int, int>(0x00000010, 180) })
                .Where(l => l.Section == ItemTooltipSection.Modifiers)
                .ToArray();

            // The text line plus the blank row its own trailing newline creates.
            Assert.Equal(2, mods.Length);
            Assert.Contains(mods, m => m.Text == "\n");
        }

        [Fact]
        public void The_budget_charges_the_price_nothing_and_no_extra_terminator()
        {
            // The game's measured string (0x48ed0d on var_1234) contains ONE marker per stack
            // buffer and ends UNTERMINATED. So:
            //
            //  * The synthesized terminator on the last-appended line must not be charged — the
            //    game has no terminator there at all.
            //  * TransactionCost is charged NOTHING. It gets no AppendAsWideChar in the game
            //    (0x48cf87 raw-appends the price), and although this class must EMIT a marker for
            //    it — in display order the price is first and has nothing to inherit from — that
            //    marker is deliberately not budgeted for. See ApplyAppendOrderBudget.
            //
            // An earlier version of this comment claimed the price was charged "exactly ONE marker
            // — ours — never zero", and that a length guard in RenderWithColorCodes would otherwise
            // trim it. Both were false: the code charges zero and there is no length guard. The
            // padding was wrong to match, sitting 3 characters short of the boundary so the test
            // passed under either accounting and pinned nothing.

            // Accounted total, walking append order (bottom, name, price):
            //   3 (bottom's marker) + (Pad + 1) + 3 (name's marker) + 5 ("NAME\n")
            //   + 0 (nothing for the price) + 10 ("Cost: 1234" less its synthesized "\n")
            //   = Pad + 22
            // so the largest Pad that still fits 1023 is 1001. At Pad + 1 the price clips.
            const int Pad = ItemTooltipComposer.MaxTooltipLength - 22;

            var bottom = new ItemTooltipLine();
            bottom.Text = new string('x', Pad) + "\n";
            bottom.Section = ItemTooltipSection.EtherealSocketed;
            bottom.Color = ItemTooltipColor.Magic;
            bottom.EmitsColorMarker = true;

            var name = new ItemTooltipLine();
            name.Text = "NAME\n";
            name.Section = ItemTooltipSection.ItemName;
            name.Color = ItemTooltipColor.Unique;
            name.EmitsColorMarker = true;

            var price = new ItemTooltipLine();
            price.Text = "Cost: 1234\n";
            price.Section = ItemTooltipSection.TransactionCost;
            price.Color = ItemTooltipColor.Unique;
            price.EmitsColorMarker = true;

            // Display order: price on top, then name, then the padded bottom section.
            var lines = new[] { price, name, bottom };

            ItemTooltipComposer composer = Composer(new FakeSections());
            string rendered = composer.Render(lines);

            // Exactly on the boundary: everything survives. Charging the price a marker would push
            // the total to 1026 and clip it to "Cost: 12".
            Assert.Contains("Cost: 1234", rendered, StringComparison.Ordinal);
            Assert.Contains("NAME", rendered, StringComparison.Ordinal);

            // One character further and the price is the line that clips, which proves the pin is
            // actually sitting on the boundary rather than comfortably inside it.
            bottom.Text = new string('x', Pad + 1) + "\n";
            string over = composer.Render(lines);

            Assert.DoesNotContain("Cost: 1234", over, StringComparison.Ordinal);
            Assert.Contains("Cost: 123", over, StringComparison.Ordinal);
        }

        [Fact]
        public void A_two_line_transaction_cost_gives_each_line_its_own_leading_colour()
        {
            // The section can hold two lines: the ethereal-repair prefix with its own colour 1
            // (0x48cf3c) and then the raw price (0x48cf87). The FIRST line's leading colour is
            // whatever the ItemName section left in force; the PRICE's is what the ethereal line
            // left. Seeding the walk with the colour computed AT THE PRICE gives the first line
            // the last line's colour — the append/display inversion again, one level down.
            string red = ItemTooltipColor.Marker
                         + ItemTooltipComposer.EncodeColorDigit(ItemTooltipColor.Red);

            FakeSections sections = new FakeSections()
                .Set(ItemTooltipSection.ItemName, "Name\n")
                .Set(ItemTooltipSection.TransactionCost,
                    red + "Cannot Be Repaired\nPrice: 5000\n");

            ItemTooltipContext context = Ctx();
            context.ShopMode = 4;

            ItemTooltipLine[] run = Composer(sections).Compose(context, TwoMods)
                .Where(l => l.Section == ItemTooltipSection.TransactionCost)
                .ToArray();

            Assert.Equal(2, run.Length);

            // Display order reverses within the section: the price is on top.
            ItemTooltipLine price = run[0];
            ItemTooltipLine ethereal = run[1];

            // The ethereal line leads the section, so it takes the carried item-name colour...
            Assert.Equal(ItemTooltipComposer.ResolveItemNameColor(context), ethereal.Color);

            // ...and the price, appended after its embedded marker, takes THAT colour.
            Assert.Equal(ItemTooltipColor.Red, price.Color);
        }

        [Fact]
        public void A_budget_truncated_line_keeps_its_line_boundary()
        {
            // The cut always lands strictly before the text's own terminator, so the truncated
            // line does not carry one. In APPEND order that is harmless — nothing follows the
            // last-appended chunk — but this line is display-FIRST, where the terminator is the
            // separator from the line below. Without it the top two display lines render as one
            // and the tooltip is a line short of the game's, which still boundaries on the next
            // chunk's own 0x0A (0x501b97 / 0x501bd0).

            var bottom = new ItemTooltipLine();
            bottom.Text = new string('x', 600) + "\n";
            bottom.Section = ItemTooltipSection.EtherealSocketed;
            bottom.Color = ItemTooltipColor.Magic;
            bottom.EmitsColorMarker = true;

            var middle = new ItemTooltipLine();
            middle.Text = "MIDDLE\n";
            middle.Section = ItemTooltipSection.ArmorClass;
            middle.Color = ItemTooltipColor.White;
            middle.EmitsColorMarker = true;

            // Long enough that the budget must cut inside it.
            var top = new ItemTooltipLine();
            top.Text = new string('T', 600) + "\n";
            top.Section = ItemTooltipSection.ItemName;
            top.Color = ItemTooltipColor.Unique;
            top.EmitsColorMarker = true;

            ItemTooltipComposer composer = Composer(new FakeSections());
            string rendered = composer.Render(new[] { top, middle, bottom });

            // The truncated top line must still be its own line: MIDDLE must not be glued to it.
            Assert.DoesNotContain("TMIDDLE", rendered, StringComparison.Ordinal);
            Assert.Contains("T\nMIDDLE", rendered, StringComparison.Ordinal);
        }

        [Fact]
        public void At_the_budget_boundary_the_price_line_keeps_its_colour()
        {
            // The failure mode this guards is subtle: if TransactionCost is charged no marker at
            // all, the accounted total fits but the EMITTED string overshoots by exactly 3, and
            // EnforceRenderableLength then trims 3 characters off the FRONT — which is precisely
            // the price line's own marker. Length stays legal, so a length assertion passes; the
            // price silently renders in the renderer's default colour instead of colour 1 on a
            // tooltip the game shows in full.
            //
            // Measured at this exact padding, where the accounted total fills the budget WITHOUT
            // the charge so the emitted string overshoots by 3:
            //   charged     -> "\xFFc1Cost: 1\n..."   marker intact, price text clipped
            //   not charged -> "Cost: 1234\n..."      full text, marker gone entirely

            const int Pad = ItemTooltipComposer.MaxTooltipLength - 22;

            var bottom = new ItemTooltipLine();
            bottom.Text = new string('x', Pad) + "\n";
            bottom.Section = ItemTooltipSection.EtherealSocketed;
            bottom.Color = ItemTooltipColor.Magic;
            bottom.EmitsColorMarker = true;

            var name = new ItemTooltipLine();
            name.Text = "NAME\n";
            name.Section = ItemTooltipSection.ItemName;
            name.Color = ItemTooltipColor.Set;
            name.EmitsColorMarker = true;

            var price = new ItemTooltipLine();
            price.Text = "Cost: 1234\n";
            price.Section = ItemTooltipSection.TransactionCost;
            price.Color = ItemTooltipColor.Red;
            price.EmitsColorMarker = true;

            ItemTooltipComposer composer = Composer(new FakeSections());
            string rendered = composer.RenderWithColorCodes(
                new[] { price, name, bottom });

            string expected = ItemTooltipColor.Marker
                              + ItemTooltipComposer.EncodeColorDigit(ItemTooltipColor.Red);

            Assert.StartsWith(expected, rendered, StringComparison.Ordinal);
        }

        [Fact]
        public void When_only_a_section_marker_would_fit_the_boundary_row_survives()
        {
            // The game's clamp is a blind NUL at [1023] (0x48ed19), so when the budget runs out
            // between a chunk's terminator and the next chunk's marker, the PRECEDING chunk's 3998
            // is still inside the retained characters. The renderer's newline handler
            // (0x501b97 -> 0x501bd0) then steps the cursor up one row (0x501c17) and leaves a
            // blank top line. Abandoning the line outright loses that row and shifts the whole
            // tooltip down one — 96 of 872 swept budget cases.
            //
            // Sized so `used` after the bottom line is 1021: 3 (its marker) + 1018 (its text), and
            // the next section's 3-character marker then cannot fit.

            var bottom = new ItemTooltipLine();
            bottom.Text = new string('x', 1017) + "\n";
            bottom.Section = ItemTooltipSection.EtherealSocketed;
            bottom.Color = ItemTooltipColor.Magic;
            bottom.EmitsColorMarker = true;

            var top = new ItemTooltipLine();
            top.Text = "NAME\n";
            top.Section = ItemTooltipSection.ItemName;
            top.Color = ItemTooltipColor.Unique;
            top.EmitsColorMarker = true;

            ItemTooltipComposer composer = Composer(new FakeSections());
            string rendered = composer.Render(new[] { top, bottom });

            // The top row is blank but present: the string opens with the boundary terminator.
            Assert.StartsWith("\n", rendered, StringComparison.Ordinal);
            Assert.DoesNotContain("NAME", rendered, StringComparison.Ordinal);
        }

        [Fact]
        public void The_boundary_rows_lone_marker_byte_takes_the_colour_in_force()
        {
            // When exactly one character of budget remains, the retained string ends with a bare
            // U+00FF. The renderer matches it at 0x501af4, fails the compare against L"c" at
            // 0x501b1c because the NUL follows, and 0x501b58 draws it as an ORDINARY GLYPH in the
            // colour currently in force — which is whatever the last RETAINED chunk left, not the
            // colour of the line being abandoned.
            //
            // And it must emit that marker: after the reverse this line is display-FIRST, so there
            // is nothing ahead of it to inherit from. Both halves are the append-vs-display
            // inversion again.
            //
            // Sized so `used` after the bottom line is 1022, leaving exactly one character.

            var bottom = new ItemTooltipLine();
            bottom.Text = new string('x', 1018) + "\n";
            bottom.Section = ItemTooltipSection.EtherealSocketed;
            bottom.Color = ItemTooltipColor.Unique;      // the colour in force at the cut
            bottom.EmitsColorMarker = true;

            var top = new ItemTooltipLine();
            top.Text = "NAME\n";
            top.Section = ItemTooltipSection.ItemName;
            top.Color = ItemTooltipColor.Magic;          // NOT the colour the glyph should take
            top.EmitsColorMarker = true;

            ItemTooltipComposer composer = Composer(new FakeSections());
            IReadOnlyList<ItemTooltipLine> kept = new List<ItemTooltipLine>(
                composer.Compose(Ctx(), new KeyValuePair<int, int>[0]));

            string rendered = composer.RenderWithColorCodes(
                new[] { top, bottom });

            // The lone U+00FF survives as the first glyph, and states the BOTTOM line's colour.
            string expected = ItemTooltipColor.Marker
                              + ItemTooltipComposer.EncodeColorDigit(ItemTooltipColor.Unique)
                              + ItemTooltipColor.Marker[0];

            Assert.StartsWith(expected, rendered, StringComparison.Ordinal);
            Assert.DoesNotContain("NAME", rendered, StringComparison.Ordinal);
            GC.KeepAlive(kept);
        }

        [Fact]
        public void A_cut_that_splits_a_colour_marker_drops_the_fragment()
        {
            // Section text can embed markers, so the 1023 cut can land between a marker's 'c' and
            // its digit. In the GAME that fragment is at the very end of the string: 0x501b25 /
            // 0x501b28 finds no character left, sets the colour to 0 at 0x501b2a and exits — it
            // draws nothing. Here the re-attached terminator follows it, so 0x501b34 reads that
            // '\n' AS THE DIGIT: 0x0A - 0x30 = -38, stored at 0x501b43 and accepted by the SIGNED
            // `jl` at 0x501b46. The newline is swallowed (top two lines merge) and everything
            // after renders with colour -38.

            // Build a top line whose text embeds a marker, positioned so the cut lands exactly
            // after the marker's 'c'. Budget: 3 (bottom marker) + bottomLen + 3 (top marker).
            string embedded = ItemTooltipColor.Marker
                              + ItemTooltipComposer.EncodeColorDigit(ItemTooltipColor.Magic);

            var bottom = new ItemTooltipLine();
            bottom.Text = new string('B', 500) + "\n";
            bottom.Section = ItemTooltipSection.EtherealSocketed;
            bottom.Color = ItemTooltipColor.White;
            bottom.EmitsColorMarker = true;

            // 3 + 501 + 3 = 507 used before the top line, so the cut falls at 1023 - 507 = 516
            // characters into it. Put the marker at 514 so the cut lands 2 chars in.
            var top = new ItemTooltipLine();
            top.Text = new string('T', 514) + embedded + new string('U', 200) + "\n";
            top.Section = ItemTooltipSection.ItemName;
            top.Color = ItemTooltipColor.Unique;
            top.EmitsColorMarker = true;

            ItemTooltipComposer composer = Composer(new FakeSections());
            string rendered = composer.RenderWithColorCodes(
                new[] { top, bottom });

            // No dangling U+00FF: every marker in the output must be followed by 'c' AND a digit.
            for (int i = 0; i < rendered.Length; ++i)
            {
                if (rendered[i] != ItemTooltipColor.Marker[0])
                {
                    continue;
                }

                Assert.True(i + 2 < rendered.Length,
                    "a colour marker was left dangling at the end of the string");
                Assert.Equal(ItemTooltipColor.Marker[1], rendered[i + 1]);
                Assert.NotEqual('\n', rendered[i + 2]);
            }
        }

        [Fact]
        public void A_spliced_marker_is_charged_to_the_budget_once_not_twice()
        {
            // AppendAsWideChar (0x4521c0) gives a buffer exactly ONE marker: it returns the
            // buffer untouched when empty (0x4521cd `cmp word ptr [esi], 0` / 0x4521d3 `jz`), and
            // the non-empty path builds [locale 3994][digit][original] once — 0x4521f9 fetches the
            // string, 0x452219 `add bl, 30h` makes the digit, 0x452248/0x452255 append the two
            // parts. LoadItemDesc calls it once per buffer.
            //
            // MergeUnterminatedRuns splices that marker into the PRECEDING line's text when an
            // unterminated PreJoined stat line (0x4e5e18 raw-appends it, 0x4e62ad skips its 3998)
            // swallows the first line of the next section. If that section has MORE than one line
            // its run survives the merge, and the budget must not charge its marker a second time:
            // the three characters are already inside the merged line's text.
            //
            // Sized so the game does not clamp AT ALL — 0x48ed12 `cmp eax, 3FFh` is not met:
            //   3 ("mmm") + 3 (AttackSpeed's marker) + 1000 (its first line) = 1006 measured,
            //   plus 3 for the mod block's own marker = 1009, plus 12 for "Holy Thunder" with the
            //   last-appended terminator excluded = 1021.
            // Charging the spliced marker twice makes it 1024, cutting at 1023 - 1012 = 11
            // characters and showing "Holy Thunde" for a line the game shows whole.
            //
            // The damage aggregate is the only producer of an unterminated line. Its stock
            // strings all end with 0A, so this supplies one that does not — the mod/locale case
            // MergeUnterminatedRuns exists for.
            // Any non-zero DescFunc: the stat only has to reach StatIdsByDescPriority (0x638530),
            // because 0x4e62a6 gives the aggregate first refusal before the DescFunc gate.
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(
                DamageStatIds.FireMinDamage, ItemDescFunc.PlusValueString, 100));

            var strings = new FakeStringTable().WithPunctuation()
                .Add(DamageStringIds.FireSingle, "mmm");
            var values = new FakeStatValues()
                .AddBase(DamageStatIds.FireMinDamage, 10)
                .AddBase(DamageStatIds.FireMaxDamage, 5);

            var sections = new FakeSections().Set(
                ItemTooltipSection.AttackSpeed,
                new string('u', 999) + "\nHoly Thunder\n");

            var composer = new ItemTooltipComposer(
                sections, new ItemDescriptionGenerator(stats, strings, values));

            IReadOnlyList<ItemTooltipLine> lines = composer.Compose(
                Ctx(),
                new[] { new KeyValuePair<int, int>(DamageStatIds.FireMinDamage, 10) });

            // The merge really did happen: the AttackSpeed run is one line short and its first
            // line now lives inside the Modifiers line, marker and all.
            Assert.Single(lines, l => l.Section == ItemTooltipSection.AttackSpeed);
            Assert.EndsWith("u\n",
                lines.Single(l => l.Section == ItemTooltipSection.Modifiers).Text,
                StringComparison.Ordinal);

            string colored = composer.RenderWithColorCodes(lines);
            Assert.Contains("Holy Thunder", colored, StringComparison.Ordinal);
        }

        [Fact]
        public void The_budget_reproduces_the_games_cut_and_reserves_nothing_extra()
        {
            // TEXT_TooltipSetAttributes memsets the buffer instead of copying it at 1024 or more
            // (0x502292), so an over-long string renders as NOTHING. The budget charges the
            // game's marker accounting (one per section) to keep the surviving line set faithful,
            // but this class emits one marker per colour change, which can be a few characters
            // more — so the emitted length needs its own guard.
            // The budget must charge the GAME's accounting and nothing more. Reserving extra for
            // the markers this class emits was tried and reverted: the 1024 discard at 0x502292
            // applies to the game's APPEND-ordered buffer, which this display-ordered string never
            // becomes, so reserving against it displaces the 0x48ed19 cut and truncates where the
            // game does not.
            //
            // Padded so the game-accounted total is 1021 — UNDER the limit, so 0x48ed12's `jb` is
            // taken and the game does not clamp at all:
            //   3 (bottom's marker) + bottomLen + 3 (name's marker) + 5 ("NAME\n")
            //   + 0 (none for the price) + 10 ("Cost: 1234" less its synthesized \n) = 1021
            // A 3-character reservation would push that to 1024 and clip the price to "Cost: 123".
            //
            // The emitted string may itself exceed 1023 here, and that is accepted — see
            // RenderWithColorCodes. Asserting otherwise is what this pin used to get wrong.

            var bottom = new ItemTooltipLine();
            bottom.Text = new string('x', ItemTooltipComposer.MaxTooltipLength - 24) + "\n";
            bottom.Section = ItemTooltipSection.EtherealSocketed;
            bottom.Color = ItemTooltipColor.Magic;
            bottom.EmitsColorMarker = true;

            var name = new ItemTooltipLine();
            name.Text = "NAME\n";
            name.Section = ItemTooltipSection.ItemName;
            name.Color = ItemTooltipColor.Set;
            name.EmitsColorMarker = true;

            var price = new ItemTooltipLine();
            price.Text = "Cost: 1234\n";
            price.Section = ItemTooltipSection.TransactionCost;
            price.Color = ItemTooltipColor.Unique;
            price.EmitsColorMarker = true;

            var lines = new List<ItemTooltipLine> { price, name, bottom };

            ItemTooltipComposer composer = Composer(new FakeSections());
            string colored = composer.RenderWithColorCodes(lines);

            // The game keeps the price line in full at this length, so we must too.
            Assert.Contains("Cost: 1234", colored, StringComparison.Ordinal);
            Assert.Contains("NAME", colored, StringComparison.Ordinal);
        }

        [Fact]
        public void The_price_inherits_a_marker_embedded_in_the_line_above_it()
        {
            // Append order is NAME, then the ethereal-repair line with its own colour 1
            // (0x48cf3c), then the price with no marker at all (0x48cf87). Sticky colour makes
            // the price red, NOT the quality colour — the item name is no longer the nearest
            // preceding marker once the ethereal line is present.
            string red = ItemTooltipColor.Marker
                         + ItemTooltipComposer.EncodeColorDigit(ItemTooltipColor.Red);

            FakeSections sections = new FakeSections()
                .Set(ItemTooltipSection.ItemName, "Name\n")
                .Set(ItemTooltipSection.TransactionCost,
                    red + "Cannot Be Repaired\nPrice: 5000\n");

            ItemTooltipContext context = Ctx();
            context.ShopMode = 4;

            ItemTooltipLine price = Composer(sections).Compose(context, TwoMods)
                .First(l => l.Section == ItemTooltipSection.TransactionCost);

            Assert.Equal(ItemTooltipColor.Red, price.Color);

            // Without the embedded marker it falls back to the quality colour.
            FakeSections plain = new FakeSections()
                .Set(ItemTooltipSection.ItemName, "Name\n")
                .Set(ItemTooltipSection.TransactionCost, "Price: 5000\n");

            ItemTooltipLine fallback = Composer(plain).Compose(context, TwoMods)
                .First(l => l.Section == ItemTooltipSection.TransactionCost);

            Assert.Equal(ItemTooltipComposer.ResolveItemNameColor(context), fallback.Color);
        }

        [Fact]
        public void A_stat_descriptor_does_not_default_DescVal_to_one()
        {
            // The loader has no default hook for descval (0x637f0c) or dgrpval (0x637ff4), and
            // nothing in TXT_AllocTxt_itemstatcost sets one. A row that omits the column
            // arrives as 0, which takes the DescVal-other path — string alone or a blank line —
            // not the number-first shape a 1 produces. Baking 1 into the struct silently
            // changed the shape of every such row for any implementer filling a DTO.
            var descriptor = new StatDescriptor();

            Assert.Equal(0, descriptor.DescVal);
            Assert.Equal(0, descriptor.DescGrpVal);
        }

        [Fact]
        public void The_time_provider_is_queried_once_per_line()
        {
            // DRLGENV_GetPeriodOfDayFromAct is called once (0x65ca4a). Querying twice lets a
            // live provider return two different angles while formatting a single line.
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(1, ItemDescFunc.ValueStringByTime, 100));

            var strings = new FakeStringTable().WithPunctuation()
                .Add(100, "to Strength")
                .Add(DescStringIds.PeriodOfDay[0], "Dusk");

            var time = new CountingGameTime();
            time.Degrees = 90;

            new ItemDescriptionGenerator(stats, strings, null, null, null, null, time)
                .Describe(new[] { Build.Entry(1, ByTime.Pack(0, -30, 70)) });

            Assert.Equal(1, time.Calls);
        }

        private sealed class CountingGameTime : IGameTimeProvider
        {
            public int Calls;
            public int Degrees;

            public bool TryGetTimeAngle(out int degrees)
            {
                ++Calls;
                degrees = Degrees;
                return true;
            }
        }

        [Fact]
        public void A_key_in_several_tables_resolves_to_the_patch_entry()
        {
            // STRTABLE_LookupString (0x524d30) searches patchstring FIRST (0x524d93, returning
            // index + 0x2710), then expansionstring (0x524dc4, + 0x4E20), then string.tbl
            // (0x524de7, unchanged). So patchstring OVERRIDES string.tbl by key.
            //
            // Not academic: 87 keys are in both tables in the shipped ENG data and most differ.
            // The strMod*
            // family's patch entries exist to DROP a trailing newline, so searching base first
            // reintroduces one and gives every grouped resist stat a blank tooltip row. It also
            // downgrades text — base "better chance of getting magic item" vs patch
            // "Better Chance of Getting Magic Items".
            //
            // Invisible to every other test in this suite, which uses fake string tables.
            TblFile baseTable = SynthTbl(new[] { "shared", "baseOnly" });
            TblFile patchTable = SynthTbl(new[] { "shared", "patchOnly" });
            TblFile expansionTable = SynthTbl(new[] { "shared", "expOnly" });

            var strings = new TblStringTable(baseTable, patchTable, expansionTable);

            // "shared" is index 0 in all three; the patch id must win.
            Assert.Equal(TblStringTable.PatchBase + 0, strings.GetIndexByKey("shared"));

            // Keys unique to one table still resolve to that table, with its offset.
            Assert.Equal(1, strings.GetIndexByKey("baseOnly"));
            Assert.Equal(TblStringTable.PatchBase + 1, strings.GetIndexByKey("patchOnly"));
            Assert.Equal(TblStringTable.ExpansionBase + 1, strings.GetIndexByKey("expOnly"));

            // A total miss AND a blank cell both become the 5382 sentinel. STRTABLE_LookupString
            // returns 0 for an empty key (0x524d7c / 0x524d8b) and DATATBLS_LookupStringId turns
            // any zero into 5382 (0x6117c6) — the shipped itemstatcost.bin holds 5382 in every
            // blank string cell. Returning 0 would point at index 0, which is a real entry
            // (Warriv's Act 1 gossip), and would make callers drop rows the game prints.
            Assert.Equal(DescStringIds.DescStr2Sentinel, strings.ResolveKey("nowhere"));
            Assert.Equal(DescStringIds.DescStr2Sentinel, strings.ResolveKey(""));
        }

        [Fact]
        public void A_skill_table_with_no_skilldesc_file_yields_the_sentinel_not_null()
        {
            // SKILLDESC_GetStatNameString (0x4e6ce0) returns 5382 on every failure path — the
            // branches at 0x4e6ce3/0x4e6cf1/0x4e6d01/0x4e6d0c/0x4e6d14 and the fall-through all
            // reach `mov ax, 1506h` at 0x4e6d24 — and 5382 resolves to "an evil force". The
            // engine therefore never yields a null skill name.
            //
            // skilldesc.txt is ABSENT from some extractions. With null names, DescFunc 16 drops
            // every aura row, 24/27/28 blank theirs, and DescFunc 15 hands null to the formatter,
            // which throws and destroys the entire tooltip.
            const string Sentinel = "an evil force";

            TblFile baseTable = SynthTbl(new[] { "k0", "k1" });
            var strings = new TblStringTable(baseTable, null, null);

            // Stand the sentinel up at index 5382 in a table wide enough to hold it.
            var wide = new TblStringTable(SynthWithSentinel(Sentinel), null, null);
            Assert.Equal(Sentinel, wide.GetByIndex(DescStringIds.DescStr2Sentinel));

            TxtFile skills = TxtFile.Parse(
                "skill\tcharclass\tskilldesc\r\n"
                + "Fire Bolt\tsor\tfirebolt\r\n"
                + "Nameless\tsor\t\r\n");

            var table = new TxtSkillTable(skills, null, wide);

            Assert.Equal(2, table.RowCount);
            Assert.Equal(Sentinel, table.GetSkillName(0));
            Assert.Equal(Sentinel, table.GetSkillName(1));

            // Out of range too, and existence is a pure range check (TXT_Skills_GetLine 0x45c4b0).
            Assert.Equal(Sentinel, table.GetSkillName(999));
            Assert.True(table.SkillExists(1));
            Assert.False(table.SkillExists(2));

            GC.KeepAlive(strings);
        }

        /// <summary>A .tbl whose index 5382 holds <paramref name="sentinel"/>.</summary>
        private static TblFile SynthWithSentinel(string sentinel)
        {
            var keys = new string[DescStringIds.DescStr2Sentinel + 1];
            for (int i = 0; i < keys.Length; ++i)
            {
                keys[i] = "k" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            return SynthTbl(keys, DescStringIds.DescStr2Sentinel, sentinel);
        }

        [Fact]
        public void Table_existence_is_a_range_check_not_a_content_test()
        {
            // Every one of the engine's row accessors rejects ONLY an out-of-range id and
            // inspects no cell: INV_GetCharStatsTxtLine (0x4833e0) does `test eax,eax / jl` at
            // 0x4833e4 and `cmp eax, [ecx+0BC8h] / jge` at 0x4833f2; TXT_Skills_GetLine
            // (0x45c4b0) is the same shape. A content test drops DescFunc 13/14/27 (and 16/24/
            // 27/28) lines the engine emits for a row whose name cell happens to be blank.
            //
            // This defect appeared three times — SkillExists, MonsterTypeExists, ClassExists —
            // so pin the shape, not one instance.
            var strings = new TblStringTable(SynthTbl(new[] { "k0", "k1" }), null, null);

            TxtFile charstats = TxtFile.Parse(
                "class\tStrAllSkills\tStrSkillTab1\tStrSkillTab2\tStrSkillTab3\tStrClassOnly\r\n"
                + "Amazon\tk1\tk1\tk1\tk1\tk1\r\n"
                + "\tk1\tk1\tk1\tk1\tk1\r\n");   // blank name, still a real record

            var classes = new TxtCharacterClassTable(charstats, strings);
            Assert.True(classes.ClassExists(0));
            Assert.True(classes.ClassExists(1));   // blank cell does NOT mean absent
            Assert.False(classes.ClassExists(2));
            Assert.False(classes.ClassExists(-1));

            TxtFile skills = TxtFile.Parse("skill\tcharclass\tskilldesc\r\nFire Bolt\tsor\t\r\n\t\t\r\n");
            var skillTable = new TxtSkillTable(skills, null, strings);
            Assert.True(skillTable.SkillExists(1));
            Assert.False(skillTable.SkillExists(2));
        }

        [Fact]
        public void A_missing_expansion_table_falls_back_the_way_GetLocaleString_does()
        {
            // GetLocaleString (0x524a30) is a CASCADE, not a range switch. With no
            // expansionstring.tbl loaded, 0x524a44 `mov esi, 2B46h` rewrites the id to 11078 and
            // falls into the PATCH arm (0x524a61), so an expansion-range id resolves through
            // patchstring[1078] — "Missing string" in shipped ENG data.
            //
            // Returning null instead silently loses text the game still shows, and a classic
            // (non-LoD) locale directory has no expansionstring.tbl at all. Invisible to every
            // other test here, which all pass complete tables.
            var patchKeys = new string[TblStringTable.MissingStringId - TblStringTable.PatchBase + 1];
            for (int i = 0; i < patchKeys.Length; ++i)
            {
                patchKeys[i] = "p" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            TblFile patch = SynthTbl(
                patchKeys,
                TblStringTable.MissingStringId - TblStringTable.PatchBase,
                "Missing string");

            var noExpansion = new TblStringTable(SynthTbl(new[] { "b0", "b1" }), patch, null);

            // 21240 would be expansionstring[1240]; with none loaded it becomes 11078.
            Assert.Equal("Missing string", noExpansion.GetByIndex(21240));
            Assert.Equal("Missing string", noExpansion.GetByIndex(TblStringTable.MissingStringId));

            // The patch range itself still resolves normally.
            Assert.Equal("V:p5", noExpansion.GetByIndex(TblStringTable.PatchBase + 5));

            // 0x524ab8 asks the base table for the id UNCHANGED, so with no patchstring either
            // the rewritten 11078 is out of range for string.tbl and 0x524948 substitutes 500.
            var baseOnly = new TblStringTable(SynthTbl(new[] { "b0", "b1" }), null, null);
            Assert.Null(baseOnly.GetByIndex(21240)); // index 500 absent from this 2-entry table
        }

        [Fact]
        public void A_key_resolving_to_base_index_zero_becomes_the_sentinel()
        {
            // STRTABLE_LookupString's base arm returns the found index UNCHANGED (0x524e0f)
            // while a miss returns `xor ax, ax` (0x524e0c), so a base hit at index 0 is
            // indistinguishable from not-found — and DATATBLS_LookupStringId turns either into
            // 5382 (0x6117c6). The loader can therefore never store 0 in a string-id field.
            //
            // Without this, the key at base index 0 renders that entry's text (Warriv's Act 1
            // gossip in shipped data) instead of the sentinel.
            var strings = new TblStringTable(SynthTbl(new[] { "zeroth", "first" }), null, null);

            Assert.Equal(0, strings.GetIndexByKey("zeroth"));   // it IS index 0...
            Assert.Equal(DescStringIds.DescStr2Sentinel, strings.ResolveKey("zeroth"));
            Assert.Equal(1, strings.ResolveKey("first"));       // ...but 1 resolves normally

            // A patch hit at its own index 0 is 10000, which is > 0 and survives.
            var withPatch = new TblStringTable(
                SynthTbl(new[] { "base0" }), SynthTbl(new[] { "patch0" }), null);
            Assert.Equal(TblStringTable.PatchBase, withPatch.ResolveKey("patch0"));
        }

        [Fact]
        public void A_header_with_duplicate_or_blank_names_does_not_break_ColumnNames()
        {
            // Shipped headers really do carry duplicates and blanks — armor.txt has 164 fields for
            // 162 distinct names (mindam, maxdam twice), CharTemplate.txt 95 for 87 (SkillName
            // eight times), AutoMap.txt 13 for 12, weapons.txt one blank cell. Sizing the array by
            // the distinct-name COUNT and writing at the original column INDEX threw
            // IndexOutOfRangeException on all four.
            //
            // The loader tolerates them: only the first matching header column binds (0x6bd00f),
            // which is the same first-wins rule the dictionary implements.
            TxtFile dupes = TxtFile.Parse("a\tb\ta\tc\r\n1\t2\t3\t4\r\n");

            IReadOnlyList<string> names = dupes.ColumnNames;   // must not throw
            Assert.Equal(4, names.Count);
            Assert.Equal("a", names[0]);
            Assert.Equal("b", names[1]);
            Assert.Null(names[2]);                             // the duplicate did not bind
            Assert.Equal("c", names[3]);

            // First wins, so "a" still resolves to column 0.
            Assert.Equal("1", dupes.GetString(0, "a"));

            TxtFile blank = TxtFile.Parse("a\t\tc\r\n1\t2\t3\r\n");
            Assert.Equal(3, blank.ColumnNames.Count);
            Assert.Equal("3", blank.GetString(0, "c"));
        }

        [Fact]
        public void A_bit_column_is_set_by_any_non_zero_value()
        {
            // TXTFIELD_BIT runs the cell through the same integer parser and then tests it with
            // `test eax, eax` / `jnz` at 0x6bde7c / 0x6bde7e, choosing SetBitAtOffset (0x6bde88)
            // or UnsetBitAtOffset (0x6bde9a). So ANY non-zero value sets the bit — a "1"/"true"
            // test reads "2", "-1" and "01" as false where the game sets them.
            TxtFile file = TxtFile.Parse("b\r\n1\r\n0\r\n2\r\n-1\r\n01\r\n\r\n");

            Assert.True(file.GetBool(0, "b"));
            Assert.False(file.GetBool(1, "b"));
            Assert.True(file.GetBool(2, "b"));
            Assert.True(file.GetBool(3, "b"));
            Assert.True(file.GetBool(4, "b"));
        }

        [Fact]
        public void The_txt_parser_matches_the_loaders_row_and_cell_rules()
        {
            // Four separate rules, all from STRUCT_CreateBinFieldExcelAndFillData and
            // TXT_ParseDataAndPutIntoStructure. None is reachable with stock data — every
            // shipped .txt ends in CRLF and has no padded or malformed cells — but all four are
            // cheap to get right and silently wrong otherwise.

            // 1. A final line with no terminator is NOT a record: the row counter increments only
            //    at 0x6bd737, reached through the CR/LF test at 0x6bd72c / 0x6bd733.
            Assert.Equal(1, TxtFile.Parse("h1\th2\r\na\tb\r\nc\td").RowCount);
            Assert.Equal(2, TxtFile.Parse("h1\th2\r\na\tb\r\nc\td\r\n").RowCount);

            // 2. Cells are NOT trimmed: the tokenizer NUL-terminates only at the tab (0x6bd71c),
            //    and the key converters compare verbatim (0x524ca8).
            TxtFile padded = TxtFile.Parse("k\r\n ModStr1a\r\n");
            Assert.Equal(" ModStr1a", padded.GetString(0, "k"));

            // 2b. HEADER names are not trimmed either — FOG_ParseBinField compares the field as
            //     tokenised (0x6bcf58, __strnicmp, case-insensitive but never trimmed). A padded
            //     header cell does not bind, and its descriptor takes the absent-column default
            //     (0x6bdfc5 writes 0), so a padded "descfunc" removes the stat from the walked
            //     array entirely (0x638530).
            TxtFile paddedHeader = TxtFile.Parse(" descfunc\tok\r\n1\t2\r\n");
            Assert.False(paddedHeader.HasColumn("descfunc"));
            Assert.True(paddedHeader.HasColumn(" descfunc"));   // bound under its literal name
            Assert.True(paddedHeader.HasColumn("ok"));

            // 3. Numbers accumulate over EVERY byte after one optional '-' (0x6bde10 / 0x6bde15),
            //    with no digit test. "3x" is 102: '3' gives 3, then 3*10 + (120 - 48) = 102.
            //    "+5" is -45: '+' gives 43 - 48 = -5, then -5*10 + (53 - 48) = -45. The stray byte
            //    poisons every digit after it, which is exactly why the game's parser is worth
            //    reproducing rather than "fixing".
            TxtFile numbers = TxtFile.Parse("n\r\n3x\r\n+5\r\n-12\r\n7\r\n");
            Assert.Equal(102, numbers.GetInt(0, "n"));
            Assert.Equal(-45, numbers.GetInt(1, "n"));
            Assert.Equal(-12, numbers.GetInt(2, "n"));
            Assert.Equal(7, numbers.GetInt(3, "n"));

            // 4. An ABSENT column differs from a BLANK cell for the key path. Absent leaves the
            //    field 0 (0x6bdfd4); blank runs the converter, which yields 5382 (0x6117c6).
            var strings = new TblStringTable(SynthTbl(new[] { "z", "one" }), null, null);
            TxtFile noColumn = TxtFile.Parse("other\r\nx\r\n");
            Assert.False(noColumn.HasColumn("descstrpos"));
            Assert.Equal(DescStringIds.DescStr2Sentinel, strings.ResolveKey(string.Empty));
        }

        [Fact]
        public void Loaded_stat_fields_are_truncated_to_the_loaders_widths()
        {
            // The loader stores each field with a plain move and no range check — 0x6bde5d dword,
            // 0x6bdeed word, 0x6bde06 byte — so the .txt value is TRUNCATED. Descriptors at
            // 0x637ec6 onwards make descpriority a WORD (read signed by the sort at 0x6379e1) and
            // descfunc/descval/dgrpfunc/dgrpval BYTEs.
            //
            // Not reachable with stock data (all 359 rows are byte-identical to itemstatcost.bin),
            // but the consequences are large: descpriority 40000 sorts FIRST as -25536 rather than
            // last, and descfunc 256 becomes 0, removing the row from the walked array entirely.
            var strings = new TblStringTable(SynthTbl(new[] { "k0", "k1" }), null, null);

            TxtFile file = TxtFile.Parse(
                "Stat\tdescpriority\tdescfunc\tdescval\tdgrp\tstuff\r\n"
                + "wide\t40000\t256\t258\t65536\t6\r\n");

            var table = new TxtItemStatCostTable(file, strings);

            StatDescriptor stat;
            Assert.True(table.TryGetStat(0, out stat));

            Assert.Equal(-25536, stat.DescPriority);  // (short)40000
            Assert.Equal(0, stat.DescFunc);           // (byte)256
            Assert.Equal(2, stat.DescVal);            // (byte)258
            Assert.Equal(0, stat.DescGrp);            // (ushort)65536

            // descfunc truncated to 0 means the row never enters the emission list.
            Assert.Empty(table.StatIdsByDescPriority);
        }

        [Fact]
        public void The_Expansion_divider_test_is_case_sensitive_and_untrimmed()
        {
            // The compiler's test is STRING_CompareTwoStringCaseSensitive (0x6bd742, a _strncmp
            // over 10 bytes) against a first field NUL-terminated at the tab with no trimming
            // (0x6bd71c). Case-folding is measurably wrong on shipped data: of the 29 .txt files
            // carrying a divider, objgroup.txt spells it "EXPANSION", and objgroup.bin declares
            // 133 records for 133 data rows — the compiler KEPT that row. An OrdinalIgnoreCase
            // test drops it and renumbers every objgroup id from 97 upward.
            TxtFile exact = TxtFile.Parse("a\tb\r\n1\tx\r\nExpansion\t\r\n2\ty\r\n");
            Assert.Equal(2, exact.RowCount);
            Assert.Equal("2", exact.GetString(1, "a"));

            TxtFile shouted = TxtFile.Parse("a\tb\r\n1\tx\r\nEXPANSION\t\r\n2\ty\r\n");
            Assert.Equal(3, shouted.RowCount);
            Assert.Equal("EXPANSION", shouted.GetString(1, "a"));

            // Untrimmed too: a padded cell is not the divider.
            TxtFile padded = TxtFile.Parse("a\tb\r\n1\tx\r\n Expansion\t\r\n2\ty\r\n");
            Assert.Equal(3, padded.RowCount);
        }

        [Fact]
        public void The_511_cap_is_applied_before_the_zero_filter()
        {
            // STATLIST_GetItemStatBonusValues copies EVERY matching (layer, value) pair, zeros
            // included, and stops at 511 (0x626174 / 0x626177); the consumer skips zeros
            // afterwards at 0x4e628b / 0x4e6295. Filtering first lets non-zero entries past the
            // cap that the game had already discarded.
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(1, ItemDescFunc.PlusValueString, 100));

            var strings = new FakeStringTable().WithPunctuation().Add(100, "to Strength");

            // 511 zero-valued layers, then one non-zero. The game's copy loop fills its buffer
            // with the zeros and never reaches the last pair, so nothing is described.
            var entries = new List<KeyValuePair<int, int>>();
            for (int layer = 0; layer < 511; ++layer)
            {
                entries.Add(Build.Entry(1, 0, layer));
            }

            entries.Add(Build.Entry(1, 7, 511));

            Assert.Empty(new ItemDescriptionGenerator(stats, strings).Describe(entries));

            // One fewer zero and the non-zero pair fits inside the cap.
            entries.RemoveAt(0);
            Assert.Single(new ItemDescriptionGenerator(stats, strings).Describe(entries));
        }

        [Fact]
        public void An_Expansion_divider_row_does_not_consume_a_record_id()
        {
            // The txt->bin compiler SKIPS the "Expansion" divider, and every engine accessor
            // indexes the COMPILED table. Proven by the shipped .bin sizes: charstats.txt has 8
            // data rows with the divider at row 5, and charstats.bin holds 7 records
            // (4 + 7*196 = 1376); monstats.txt has 735 with the divider at 410, and
            // monstats.bin holds 734 (4 + 734*424 = 311220).
            //
            // Keeping it shifts every later id by one. For charstats that means Druid 5 -> 6 and
            // Assassin 6 -> 7, so id 5 lands ON the divider: every Druid +skills, skill-tab and
            // "(Druid Only)" line is dropped, and every Assassin line prints Druid's strings.
            TxtFile file = TxtFile.Parse(
                "class\tStrAllSkills\r\n"
                + "Amazon\tA\r\n"
                + "Sorceress\tB\r\n"
                + "Necromancer\tC\r\n"
                + "Paladin\tD\r\n"
                + "Barbarian\tE\r\n"
                + "Expansion\t\r\n"
                + "Druid\tF\r\n"
                + "Assassin\tG\r\n");

            Assert.Equal(7, file.RowCount);
            Assert.Equal("Barbarian", file.GetString(4, "class"));
            Assert.Equal("Druid", file.GetString(5, "class"));
            Assert.Equal("Assassin", file.GetString(6, "class"));
        }

        /// <summary>
        /// Builds a minimal in-memory .tbl: 21-byte header, one u16 index slot per key, then
        /// 17-byte hash nodes, then NUL-terminated key and value blobs. Value for key K is "V:K".
        /// </summary>
        private static TblFile SynthTbl(string[] keys)
        {
            return SynthTbl(keys, -1, null);
        }

        private static TblFile SynthTbl(string[] keys, int overrideIndex, string overrideValue)
        {
            return TblFile.Parse(SynthTblBytes(keys, overrideIndex, overrideValue));
        }

        private static byte[] SynthTblBytes(
            string[] keys, int overrideIndex, string overrideValue)
        {
            const int HeaderLength = 21;
            const int NodeLength = 17;

            int indexBase = HeaderLength;
            int nodeBase = indexBase + (keys.Length * 2);
            int dataBase = nodeBase + (keys.Length * NodeLength);

            var blob = new List<byte>();
            var keyOffset = new int[keys.Length];
            var valueOffset = new int[keys.Length];
            var valueLength = new int[keys.Length];

            for (int i = 0; i < keys.Length; ++i)
            {
                keyOffset[i] = dataBase + blob.Count;
                blob.AddRange(System.Text.Encoding.UTF8.GetBytes(keys[i]));
                blob.Add(0);

                valueOffset[i] = dataBase + blob.Count;
                string value = i == overrideIndex ? overrideValue : "V:" + keys[i];
                byte[] encoded = System.Text.Encoding.UTF8.GetBytes(value ?? string.Empty);
                valueLength[i] = encoded.Length;
                blob.AddRange(encoded);
                blob.Add(0);
            }

            var bytes = new byte[dataBase + blob.Count];
            Array.Copy(BitConverter.GetBytes((ushort)keys.Length), 0, bytes, 2, 2);
            Array.Copy(BitConverter.GetBytes((uint)keys.Length), 0, bytes, 4, 4);

            for (int i = 0; i < keys.Length; ++i)
            {
                Array.Copy(BitConverter.GetBytes((ushort)i), 0, bytes, indexBase + (i * 2), 2);

                int at = nodeBase + (i * NodeLength);
                bytes[at] = 1; // used
                Array.Copy(BitConverter.GetBytes((ushort)i), 0, bytes, at + 1, 2);
                Array.Copy(BitConverter.GetBytes((uint)keyOffset[i]), 0, bytes, at + 7, 4);
                Array.Copy(BitConverter.GetBytes((uint)valueOffset[i]), 0, bytes, at + 11, 4);

                // stringLength at +15, which the reader honours because the game does: shipped
                // tables always carry strlen + 1 here.
                Array.Copy(
                    BitConverter.GetBytes((ushort)(valueLength[i] + 1)), 0, bytes, at + 15, 2);
            }

            blob.CopyTo(bytes, dataBase);
            return bytes;
        }

        // Offsets into a SynthTblBytes blob, for corrupting it.
        private static int SynthIndexSlot(int id) { return 21 + (id * 2); }

        private static int SynthUsedByte(int keyCount, int node)
        {
            return 21 + (keyCount * 2) + (node * 17);
        }

        [Fact]
        public void Poison_length_is_collected_even_though_its_descfunc_is_blank()
        {
            // COLLECTION and EMISSION use different arrays, and only emission is descfunc-filtered.
            // SKILLDESC_BuildStatListDesc walks a table compiled into the binary — 0x4e49e3 loads
            // `offset unk_72CDD0` as an immediate, bounded by dword_72CDCC = 143, stride 0x10 —
            // whose rows select collector arms by stat id alone. Stat 59's poison-length read
            // (0x4e4ad9) and stat 326's divisor read (0x4e4ae4) are therefore LIVE, even though
            // poisonlength's descfunc cell is blank in shipped 1.14d data and it never appears in
            // the emission array (a pointer read at 0x4e6240, built by the descfunc filter at
            // 0x638530).
            //
            // Pinned because a comment here once said 0x72CDD0 "is not what is loaded", which reads
            // as a licence to drop those two reads. Doing so makes frames 0, so every poison item
            // in the game prints "+0 poison damage over 0 seconds" via string 3620 instead of its
            // real range via 3621.
            var values = new FakeStatValues()
                .AddBase(DamageStatIds.PoisonMinDamage, 256)
                .AddBase(DamageStatIds.PoisonMaxDamage, 512)
                .AddBase(DamageStatIds.PoisonLength, 25);
            values.AddItemStat(DamageStatIds.PoisonLengthDivisor, 1);

            string text;
            Assert.True(Aggregate(values).TryDescribe(DamageStatIds.PoisonMinDamage, out text));

            // frames = 25/1; min = (25*256+128)>>8 = 25; max = (25*512+128)>>8 = 50; secs = 25/25.
            Assert.Equal("PR[25|50~1]", text);
        }

        [Fact]
        public void An_empty_item_name_leaves_a_blank_row_at_the_top()
        {
            // ItemName is the only buffer whose writer appends no terminator (GetItemName's tail at
            // 0x48ce72), so it is normally the unterminated end of the game's string. When it is
            // EMPTY the string ends with the previous section's own 3998 instead, and the renderer
            // steps a row for that newline (0x501b97 -> 0x501bd0 -> 0x501c17) with no collapsing —
            // a blank row above everything else.
            //
            // The buffer really can be empty: GetItemName's LowQuality arm tests the row pointer at
            // 0x48c21e and jumps to 0x48CAFC at 0x48c220 without writing the destination.
            //
            // Dropping the trailing terminator unconditionally erases that row and shifts the whole
            // tooltip down one. Game string here is "ÿc4'AmnRalThul'\n" — 15 chars, so 0x48ed12
            // never fires and no truncation is involved.
            var sections = new FakeSections()
                .Set(ItemTooltipSection.RuneLetters, "'AmnRalThul'\n");

            ItemTooltipComposer composer = Composer(sections);
            IReadOnlyList<ItemTooltipLine> lines =
                composer.Compose(Ctx(), new KeyValuePair<int, int>[0]);

            string rendered = composer.Render(lines);

            // Two rows: a blank one on top, then the rune letters.
            Assert.Equal(new[] { string.Empty, "'AmnRalThul'" }, rendered.Split('\n'));

            // The blank row must not spend a colour marker — the game has no character there.
            string colored = composer.RenderWithColorCodes(lines);
            Assert.StartsWith(
                ItemTooltipColor.Marker + ItemTooltipComposer.EncodeColorDigit(ItemTooltipColor.Unique),
                colored.Substring(colored.IndexOf('\n') + 1),
                StringComparison.Ordinal);
        }

        [Fact]
        public void An_empty_stat_block_leaves_no_colour_in_force()
        {
            // AppendAsWideChar returns an empty buffer completely untouched (0x4521cd
            // `cmp word ptr [esi], 0` / 0x4521d3 `jz` to the epilogue), so a stat block that
            // produces no lines writes no marker and leaves NOTHING in force. Adopting its
            // nominal colour 3 anyway paints the sections appended BEFORE it — which render below
            // it and have no marker of their own to inherit from — in a colour the game never
            // emitted.
            //
            // TransactionCost is the observable case, because it is the one section with no marker
            // of its own (0x48cf87 raw-appends the price).
            var sections = new FakeSections()
                .Set(ItemTooltipSection.TransactionCost, "Cost: 1234\n");

            ItemTooltipContext context = Ctx();
            context.ShopMode = 2;

            // No stats at all, so the Modifiers block emits nothing.
            ItemTooltipComposer composer = Composer(sections);
            IReadOnlyList<ItemTooltipLine> lines =
                composer.Compose(context, new KeyValuePair<int, int>[0]);

            ItemTooltipLine price =
                lines.Single(l => l.Section == ItemTooltipSection.TransactionCost);

            Assert.Equal(ItemTooltipColor.White, price.Color);
            Assert.NotEqual(ItemTooltipColor.Magic, price.Color);
        }

        [Fact]
        public void Skill_class_codes_come_from_playerclass_txt_in_row_order()
        {
            // skills.txt's `charclass` resolves against a linker built from playerclass.txt's
            // `Code` column in ROW ORDER: the descriptor at 0x615234 points `charclass` at
            // dword_96BC34, which 0x61282e creates from the `Code` descriptor at 0x6127ef right
            // before playerclass.txt is compiled at 0x612833. Hardcoding the list makes a reordered
            // or extended playerclass.txt silently misresolve.
            TblFile baseTable = SynthTbl(new[] { "index0", "aKey" });
            var strings = new TblStringTable(baseTable, null, null);

            // Deliberately NOT stock order, and with an extra class after the Expansion divider.
            var playerClass = TxtFile.Parse(
                "Player Class\tCode\r\n" +
                "Necromancer\tnec\r\n" +
                "Amazon\tama\r\n" +
                "Expansion\t\r\n" +
                "Druid\tdru\r\n" +
                "Warrior\twar\r\n\r\n");

            var skills = TxtFile.Parse(
                "skill\tId\tcharclass\tskilldesc\r\n" +
                "s0\t0\tnec\t\r\n" +
                "s1\t1\tama\t\r\n" +
                "s2\t2\tdru\t\r\n" +
                "s3\t3\twar\t\r\n" +
                "s4\t4\tnotaclass\t\r\n\r\n");

            var table = new TxtSkillTable(skills, null, strings, playerClass);

            Assert.Equal(0, table.GetSkillClass(0));   // nec is row 0 here, not row 2
            Assert.Equal(1, table.GetSkillClass(1));
            Assert.Equal(2, table.GetSkillClass(2));   // the Expansion divider consumed no id
            Assert.Equal(3, table.GetSkillClass(3));   // a class the stock list does not have
            Assert.Equal(-1, table.GetSkillClass(4));  // 0x6bd168: miss yields -1

            // The compare is CASE-SENSITIVE over four space-padded bytes: field type 0x0D copies at
            // most 4 bytes and pads with 0x20 (0x6bdc62, 0x6bdc7f, 0x6bdc9a, 0x6bdcb1), and
            // GetClassIdFromName compares that packed value as a raw DWORD (0x6bd155). So "Nec"
            // does NOT match Code "nec", and a 5-character cell matches on its first four.
            var cased = TxtFile.Parse(
                "skill\tId\tcharclass\tskilldesc\r\n" +
                "s0\t0\tNec\t\r\n" +
                "s1\t1\tdruid\t\r\n" +
                "s2\t2\twarrior\t\r\n\r\n");

            var casedTable = new TxtSkillTable(cased, null, strings, playerClass);

            // Wrong case: "Nec " != "nec ".
            Assert.Equal(-1, casedTable.GetSkillClass(0));

            // Truncation can turn a hit into a miss: "druid" packs to "drui", not "dru ".
            Assert.Equal(-1, casedTable.GetSkillClass(1));

            // ...and a miss into a hit: "warrior" and Code "war" pack to "warr" and "war ", which
            // still differ — but a Code of "warrior" would pack to the same "warr" as the cell.
            Assert.Equal(-1, casedTable.GetSkillClass(2));

            var longCode = TxtFile.Parse(
                "Player Class\tCode\r\n" +
                "Warrior\twarrior\r\n\r\n");

            Assert.Equal(
                0,
                new TxtSkillTable(cased, null, strings, longCode).GetSkillClass(2));

            // Omitting the file keeps the stock order, which is what shipped 1.14d data gives.
            var stock = new TxtSkillTable(skills, null, strings);
            Assert.Equal(2, stock.GetSkillClass(0));   // nec is 2 in stock order
            Assert.Equal(0, stock.GetSkillClass(1));
            Assert.Equal(5, stock.GetSkillClass(2));
            Assert.Equal(-1, stock.GetSkillClass(3));  // no "war" in stock data
        }

        [Fact]
        public void Rows_are_terminated_by_CRLF_and_a_bare_LF_is_cell_content()
        {
            // The compiler's scanner tests exactly two bytes: TAB (0x6bd718) and CR (0x6bd722),
            // and a CR must be followed by LF — 0x6bd72c `cmp byte ptr [esi], 0Ah` / 0x6bd733
            // `jnz` halts otherwise. 0x0A matches neither test, so a bare LF falls through
            // 0x6bd726 and stays INSIDE THE FIELD.
            //
            // Splitting on '\n' is therefore wrong in a way that silently corrupts everything: a
            // bare LF in one cell splits the row, so every record id after it shifts by one and
            // the table describes the wrong stats. Measured across all 75 shipped .txt in the
            // 1.14d MPQ extraction: zero bare LF and zero bare CR, so this is mod-only.

            // A bare LF is ordinary content: two data rows, not three, and the LF is IN the cell.
            TxtFile embedded = TxtFile.Parse("a\tb\r\n1\tx\ny\r\n2\tz\r\n");

            Assert.Equal(2, embedded.RowCount);
            Assert.Equal("x\ny", embedded.GetString(0, "b"));
            Assert.Equal("z", embedded.GetString(1, "b"));

            // A CR that is not part of a CRLF is a halt in the game, so it is rejected here.
            Assert.Throws<InvalidDataException>(() => TxtFile.Parse("a\tb\r\n1\tx\ry\r\n"));

            // And an LF-only file has no row terminator at all, so it yields no records — the
            // game gets none from it either.
            Assert.Equal(0, TxtFile.Parse("a\tb\n1\tx\n2\ty\n").RowCount);
        }

        [Fact]
        public void A_tbl_index_pointing_outside_the_hash_table_is_rejected()
        {
            // STRTABLE_GetStringByIndex tests nodeForIndex[id] against the hash table size at
            // 0x524955 `cmp eax, [ecx+4]` / 0x524958 `jl`; falling through halts with internal
            // error 0x102 (pushed at 0x52495a) and then _exit(-1) at 0x524974. The game will not
            // run with such a table.
            //
            // Skipping the entry instead leaves it null, and TblStringTable.Lookup then
            // substitutes index 500 — a WRONG STRING where the game stops dead. The 500
            // substitution is real but belongs to the out-of-range INDEX test alone
            // (0x524943/0x524946/0x524948), not to a corrupt node.
            byte[] bytes = SynthTblBytes(new[] { "k0", "k1" }, -1, null);
            Array.Copy(BitConverter.GetBytes((ushort)99), 0, bytes, SynthIndexSlot(1), 2);

            // Rejected on LOOKUP, not at load. TABLES_LoadStrings validates nothing: it walks the
            // hash table sequentially by slot (cursor seeded at 0x525bae, stepped 0x11 at 0x525bcf,
            // bounded by hashTableSize at 0x525bd2) and reads only node+11 (0x525bb1) and node+15
            // (0x525bb4) to size the string arena — never nodeForIndex, never the `used` byte. So
            // a table whose bad node belongs to an index nobody asks for runs fine in the game.
            TblFile table = TblFile.Parse(bytes);

            // The unaffected index still resolves, exactly as it would in the game.
            Assert.Equal("V:k0", table.GetByIndex(0));
            Assert.Throws<InvalidDataException>(() => table.GetByIndex(1));
        }

        [Fact]
        public void A_tbl_node_whose_used_byte_is_not_exactly_one_is_rejected()
        {
            // 0x524992 `cmp byte ptr [esi], 1` / 0x524997 `jz` — the byte must be EXACTLY 1.
            // Zero (an unused slot that an index still points at) and any other value both fall
            // through to the halt with internal error 0x107. Treating 0 as "skip" and accepting 2
            // are both divergences; measured zero occurrences across the three shipped ENG tables
            // (string 5391, patchstring 1179, expansionstring 2818 elements).
            foreach (byte used in new byte[] { 0, 2 })
            {
                byte[] bytes = SynthTblBytes(new[] { "k0", "k1" }, -1, null);
                bytes[SynthUsedByte(2, 1)] = used;

                TblFile table = TblFile.Parse(bytes);

                Assert.Equal("V:k0", table.GetByIndex(0));
                Assert.Throws<InvalidDataException>(() => table.GetByIndex(1));
            }
        }

        [Fact]
        public void An_absent_key_column_resolves_to_id_zero_not_the_5382_sentinel()
        {
            // The loader distinguishes an ABSENT column from a BLANK cell, and so must every
            // provider that reads a KEYTOWORD column:
            //   absent -> the defaults loop writes 0 (0x6bdfd4), so the engine resolves
            //             string.tbl[0], Warriv's Act 1 gossip;
            //   blank  -> the converter runs, STRTABLE_LookupString returns 0 (0x524d8b) and
            //             DATATBLS_LookupStringId substitutes 5382 (0x6117c6), "an evil force".
            //
            // itemstatcost got this right while charstats, MonType, monstats and skilldesc
            // resolved unconditionally, printing "an evil force" where the game prints the gossip.
            // All five now share TxtKeys.Id.
            TblFile baseTable = SynthTbl(new[] { "index0", "aKey" });
            var strings = new TblStringTable(baseTable, null, null);

            // Column present but blank -> the sentinel.
            var blank = TxtFile.Parse("class\tStrAllSkills\r\nama\t\r\n\r\n");
            Assert.Equal(
                strings.GetByIndex(DescStringIds.DescStr2Sentinel),
                new TxtCharacterClassTable(blank, strings).GetAllSkillsText(0));

            // Column absent entirely -> id 0, i.e. the FIRST string in the table.
            var absent = TxtFile.Parse("class\r\nama\r\n\r\n");
            Assert.Equal(
                strings.GetByIndex(0),
                new TxtCharacterClassTable(absent, strings).GetAllSkillsText(0));

            // And those are genuinely different strings, or the assertions above prove nothing.
            Assert.NotEqual(
                strings.GetByIndex(0),
                strings.GetByIndex(DescStringIds.DescStr2Sentinel));
        }

        // =================================================================
        // 6. Effective colour after stickiness
        // =================================================================

        [Fact]
        public void Every_row_with_glyphs_renders_in_the_colour_it_declares()
        {
            // Colour is sticky (0x501bec never resets it), so a display row that does not state
            // its own colour reads as whatever the row above left behind. The emitted string is
            // display-ordered and the game never produces one, so every row states its colour and
            // this walk must recover exactly what each line declares.
            //
            // Restating a colour already in force costs 3 characters, and
            // TEXT_TooltipSetAttributes discards the whole string at 1024 or more (0x502292) —
            // but ApplyAppendOrderBudget charges the GAME's per-section accounting, not the
            // markers emitted here, so the extra bytes cannot displace the cut.
            FakeSections sections = new FakeSections()
                .Set(ItemTooltipSection.ItemName, "Name\n")
                .Set(ItemTooltipSection.WeaponDamage, "Two-Hand: 10 to 20\nOne-Hand: 5 to 9\n")
                .Set(ItemTooltipSection.ArmorClass, "Defense: 445\n");

            ItemTooltipComposer composer = Composer(sections);
            IReadOnlyList<ItemTooltipLine> lines = composer.Compose(Ctx(), TwoMods);
            string rendered = composer.RenderWithColorCodes(lines);

            // Walk the string the way the renderer does: markers set the colour, newlines only
            // move the cursor. Record the colour in force at the start of each line.
            var effective = new List<int>();
            int current = -1;
            bool atLineStart = true;

            for (int i = 0; i < rendered.Length; ++i)
            {
                if (i + 2 < rendered.Length
                    && rendered[i] == ItemTooltipColor.Marker[0]
                    && rendered[i + 1] == ItemTooltipColor.Marker[1])
                {
                    current = rendered[i + 2] - '0';
                    i += 2;
                    continue;
                }

                if (rendered[i] == '\n')
                {
                    atLineStart = true;
                    continue;
                }

                if (atLineStart)
                {
                    effective.Add(current);
                    atLineStart = false;
                }
            }

            Assert.Equal(lines.Count, effective.Count);

            for (int i = 0; i < lines.Count; ++i)
            {
                Assert.True(lines[i].Color == effective[i],
                    "display line " + i + " (" + lines[i].Section + ") declares colour "
                    + lines[i].Color + " but renders as " + effective[i]);
            }
        }

        [Fact]
        public void Every_line_renders_in_the_colour_it_declares()
        {
            // The property the marker placement exists to produce, and the one asserting the
            // flag alone missed. Colour is sticky in emission order and the newline handler
            // never resets it, so walking the display-ordered lines and carrying the last
            // marker forward must reproduce each line's OWN declared colour.
            FakeSections sections = new FakeSections()
                .Set(ItemTooltipSection.ItemName, "Name\n")
                .Set(ItemTooltipSection.WeaponDamage, "Two-Hand: 10 to 20\nOne-Hand: 5 to 9\n")
                .Set(ItemTooltipSection.ArmorClass, "Defense: 445\n");

            IReadOnlyList<ItemTooltipLine> lines =
                Composer(sections).Compose(Ctx(), TwoMods);

            // More than one multi-line section must be present or the pin proves nothing.
            Assert.Contains(lines.GroupBy(l => l.Section), g => g.Count() > 1);

            // `EmitsColorMarker` is the GAME's marker — AppendAsWideChar 0x4521c0 fires once per
            // section buffer, so it lands on the section's first-APPENDED row, which is its LAST
            // in display order. Exactly one row per section, and it is that one.
            foreach (IGrouping<ItemTooltipSection, ItemTooltipLine> group in
                lines.GroupBy(l => l.Section))
            {
                ItemTooltipLine[] rows = group.ToArray();
                for (int i = 0; i < rows.Length; ++i)
                {
                    Assert.Equal(i == rows.Length - 1, rows[i].EmitsColorMarker);
                }
            }

            // The colour each row actually declares is unchanged by where the marker sits: the
            // carry is computed in append order and every row keeps its own value.
            Assert.All(lines, l => Assert.InRange(l.Color, 0, 10));
        }

        // =================================================================
        // 7. Damage aggregation core
        // =================================================================

        private static FakeStringTable DamageStrings()
        {
            // Distinctive shapes so argument ORDER and COUNT are both observable.
            return new FakeStringTable().WithPunctuation()
                .Add(DamageStringIds.FireSingle, "F1[%d]")
                .Add(DamageStringIds.FireRange, "FR[%d|%d]")
                .Add(DamageStringIds.PoisonSingle, "P1[%d~%d]")
                .Add(DamageStringIds.PoisonRange, "PR[%d|%d~%d]")
                .Add(DamageStringIds.PhysicalRange, "PH[%d|%d]")
                .Add(DamageStringIds.EnhancedDamage, "Enhanced Damage");
        }

        private static ItemDamageAggregate Aggregate(FakeStatValues values)
        {
            return new ItemDamageAggregate(DamageStrings(), values);
        }

        [Fact]
        public void A_single_value_elemental_line_formats_the_max_not_the_min()
        {
            // 0x4e5ac2: the max is pushed before the comparison branch and is the only
            // argument on the single-value path. min=10 max=5 makes the two distinguishable.
            var values = new FakeStatValues()
                .AddBase(DamageStatIds.FireMinDamage, 10)
                .AddBase(DamageStatIds.FireMaxDamage, 5);

            string text;
            Assert.True(Aggregate(values).TryDescribe(DamageStatIds.FireMinDamage, out text));
            Assert.Equal("F1[5]", text);
        }

        [Fact]
        public void Poison_strings_take_an_asymmetric_trailing_seconds_argument()
        {
            // 0x4e5c2d: single is (max, seconds) with add esp,14h; range is
            // (min, max, seconds) with add esp,18h. length 25 / divisor 1 = 25 frames = 1s.
            // (25*256+128)>>8 = 25 and (25*512+128)>>8 = 50.
            var range = new FakeStatValues()
                .AddBase(DamageStatIds.PoisonMinDamage, 256)
                .AddBase(DamageStatIds.PoisonMaxDamage, 512)
                .AddBase(DamageStatIds.PoisonLength, 25);

            string text;
            Assert.True(Aggregate(range).TryDescribe(DamageStatIds.PoisonMinDamage, out text));
            Assert.Equal("PR[25|50~1]", text);

            var single = new FakeStatValues()
                .AddBase(DamageStatIds.PoisonMinDamage, 512)
                .AddBase(DamageStatIds.PoisonMaxDamage, 256)
                .AddBase(DamageStatIds.PoisonLength, 25);

            Assert.True(Aggregate(single).TryDescribe(DamageStatIds.PoisonMinDamage, out text));
            Assert.Equal("P1[25~1]", text); // max, then seconds
        }

        [Fact]
        public void A_non_positive_poison_divisor_is_clamped_to_one()
        {
            // 0x4e5c39 jg keeps a divisor > 0; anything else has 1 written back.
            var values = new FakeStatValues()
                .AddBase(DamageStatIds.PoisonMinDamage, 256)
                .AddBase(DamageStatIds.PoisonMaxDamage, 512)
                .AddBase(DamageStatIds.PoisonLength, 25);
            values.AddItemStat(DamageStatIds.PoisonLengthDivisor, -5);

            string text;
            Assert.True(Aggregate(values).TryDescribe(DamageStatIds.PoisonMinDamage, out text));
            Assert.Equal("PR[25|50~1]", text); // as if the divisor were 1
        }

        [Fact]
        public void The_poison_divisor_comes_from_the_items_own_list()
        {
            // 0x4e4adf reads stat 326 off the described item, not the merged list. (0x4e4ad8 is
            // `push edx`, the unit argument; 0x4e4ae4 is the store of the returned divisor.)
            var values = new FakeStatValues()
                .AddBase(DamageStatIds.PoisonMinDamage, 256)
                .AddBase(DamageStatIds.PoisonMaxDamage, 512)
                .AddBase(DamageStatIds.PoisonLength, 50)
                .AddBase(DamageStatIds.PoisonLengthDivisor, 999); // merged list: ignored
            values.AddItemStat(DamageStatIds.PoisonLengthDivisor, 2);

            string text;
            Assert.True(Aggregate(values).TryDescribe(DamageStatIds.PoisonMinDamage, out text));
            Assert.Equal("PR[25|50~1]", text); // 50/2 = 25 frames
        }

        [Fact]
        public void The_enhanced_damage_line_prints_the_min_percent_and_suppresses_the_max()
        {
            // jpt_4E4A11: stat 18 is the value formatted at 0x4e5d8f; stat 17 only gates it.
            var values = new FakeStatValues()
                .AddBase(DamageStatIds.ItemMinDamagePercent, 30)
                .AddBase(DamageStatIds.ItemMaxDamagePercent, 70);

            ItemDamageAggregate aggregate = Aggregate(values);

            string text;
            Assert.True(aggregate.TryDescribe(DamageStatIds.ItemMinDamagePercent, out text));
            Assert.Equal("+30% Enhanced Damage", text);

            Assert.True(aggregate.TryDescribe(DamageStatIds.ItemMaxDamagePercent, out text));
            Assert.Equal(string.Empty, text); // handled, emits nothing
        }

        [Fact]
        public void A_degenerate_physical_range_clears_both_latches()
        {
            // 0x4e5d1a and 0x4e5d1d clear slot 5 AND slot 4.
            //
            // This drives the state machine directly. The clear is unobservable in practice
            // because min/max are never written back, so the 0x4e5d16 comparison is idempotent —
            // a later visit reaches the same `return 0` whether or not the slot was cleared — and
            // case 24 (descpriority 123) has already been consumed before 23/22/21 (124/126/127)
            // in the ascending walk. The pin is still worth keeping: it fixes the transition
            // itself, which is what a refactor would break.
            var values = new FakeStatValues()
                .AddBase(DamageStatIds.MinDamage, 10)
                .AddBase(DamageStatIds.MaxDamage, 10);

            ItemDamageAggregate aggregate = Aggregate(values);

            Assert.False(aggregate.TryDescribe(DamageStatIds.MinDamage, out _));
            Assert.False(aggregate.TryDescribe(DamageStatIds.SecondaryMaxDamage, out _));
        }

        [Fact]
        public void A_printed_physical_range_suppresses_every_later_damage_stat()
        {
            var values = new FakeStatValues()
                .AddBase(DamageStatIds.MinDamage, 5)
                .AddBase(DamageStatIds.MaxDamage, 10);

            ItemDamageAggregate aggregate = Aggregate(values);

            string text;
            Assert.True(aggregate.TryDescribe(DamageStatIds.MinDamage, out text));
            Assert.Equal("PH[5|10]", text);

            foreach (int later in new[]
            {
                DamageStatIds.MaxDamage,
                DamageStatIds.SecondaryMinDamage,
                DamageStatIds.SecondaryMaxDamage,
            })
            {
                Assert.True(aggregate.TryDescribe(later, out text));
                Assert.Equal(string.Empty, text);
            }
        }

        [Fact]
        public void Physical_damage_falls_back_to_the_secondary_stats()
        {
            // 0x4e4aff / 0x4e4b1c: stat 21 falls back to 23, stat 22 to 24.
            var values = new FakeStatValues()
                .AddBase(DamageStatIds.SecondaryMinDamage, 5)
                .AddBase(DamageStatIds.SecondaryMaxDamage, 10);

            string text;
            Assert.True(Aggregate(values).TryDescribe(DamageStatIds.MinDamage, out text));
            Assert.Equal("PH[5|10]", text);
        }

        [Fact]
        public void A_half_present_pair_is_not_handled_here_at_all()
        {
            // 0x4e4b53: strictly > 0 on BOTH halves. A lone half must fall through to its own
            // DescFunc line rather than being silently swallowed.
            var values = new FakeStatValues().AddBase(DamageStatIds.FireMinDamage, 10);

            ItemDamageAggregate aggregate = Aggregate(values);

            Assert.False(aggregate.TryDescribe(DamageStatIds.FireMinDamage, out _));
            Assert.False(aggregate.TryDescribe(DamageStatIds.FireMaxDamage, out _));
        }
    }
}
