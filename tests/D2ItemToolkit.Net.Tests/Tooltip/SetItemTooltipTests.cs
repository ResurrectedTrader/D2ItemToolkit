using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace D2ItemToolkit.Tests
{
    /// <summary>
    /// ITEM_BuildSetItemTooltip 0x48d1d0. The order-and-colour half runs against a fake sections
    /// provider so the assertions are about the WRITER, not about the shipped tables; the
    /// real-data half pins the one thing a fake cannot, which is that the tables agree.
    /// </summary>
    public class SetItemTooltipTests
    {
        private static readonly D2DataFiles Data = D2DataFiles.LoadEmbedded();

        private const string Marker = ItemTooltipColor.Marker;

        // ---------------------------------------------------------------- fakes

        private static ItemTooltipContext SetContext(
            ItemTooltipFlags flags = ItemTooltipFlags.None,
            bool weaponOrArmor = false,
            bool shield = false,
            int shopMode = 0)
        {
            var context = new ItemTooltipContext();
            context.Quality = ItemQuality.Set;
            context.Flags = flags | ItemTooltipFlags.Identified;
            context.IsWeaponOrArmorType = weaponOrArmor;
            context.IsShieldType = shield;
            context.ShopMode = shopMode;
            return context;
        }

        private static ItemTooltipComposer Composer(FakeSections sections)
        {
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(39, ItemDescFunc.PlusValueString, 101, priority: 50));

            var strings = new FakeStringTable().WithPunctuation().Add(101, "Fire Resist");

            return new ItemTooltipComposer(
                sections, new ItemDescriptionGenerator(stats, strings));
        }

        private static SetItemTooltipContent Content(
            string[] pieces = null,
            bool[] owned = null,
            string setName = "Angelic Raiment\n",
            string full = "",
            string partial = "")
        {
            var content = new SetItemTooltipContent();
            content.SetName = setName;
            content.FullSetText = full;
            content.PartialText = partial;
            content.TransactionRefusedText = "Item cannot be traded here.\n";

            var lines = new List<SetPieceLine>();
            for (int i = 0; pieces != null && i < pieces.Length; ++i)
            {
                var piece = new SetPieceLine();
                piece.Text = pieces[i] + "\n";
                piece.Owned = owned != null && i < owned.Length && owned[i];
                lines.Add(piece);
            }

            content.Pieces = lines;
            return content;
        }

        private static string[] Rows(string rendered)
        {
            return rendered.Split('\n');
        }

        private static readonly KeyValuePair<int, int>[] NoStats = new KeyValuePair<int, int>[0];

        // ---------------------------------------------------------------- 1-12

        /// <summary>
        /// Test 1. The piece list is appended in setitems.txt row order (0x48d88e-0x48d92a) and
        /// D2WINFONT_DrawWideString steps the cursor UPWARDS (0x501c17), so the LAST row of
        /// setitems.txt is the highest of the four on screen.
        /// </summary>
        [Fact]
        public void The_piece_list_renders_in_reverse_setitems_order()
        {
            var sections = new FakeSections().Set(ItemTooltipSection.ItemName, "Angelic Halo");

            IReadOnlyList<ItemTooltipLine> lines = Composer(sections).ComposeSetItem(
                SetContext(),
                Content(
                    new[] { "Angelic Sickle", "Angelic Mantle", "Angelic Halo", "Angelic Wings" },
                    new[] { false, false, true, true }),
                NoStats);

            string[] rows = Rows(Composer(sections).Render(lines));

            Assert.Equal(
                new[] { "Angelic Wings", "Angelic Halo", "Angelic Mantle", "Angelic Sickle" },
                rows.Skip(rows.Length - 4).ToArray());
        }

        /// <summary>
        /// Test 2. The set name is appended AFTER the list (0x48d958 versus 0x48d93b), so it sits
        /// one row above it, gold.
        /// </summary>
        [Fact]
        public void The_set_name_sits_directly_above_the_piece_list_in_gold()
        {
            var sections = new FakeSections().Set(ItemTooltipSection.ItemName, "Angelic Halo");

            IReadOnlyList<ItemTooltipLine> lines = Composer(sections).ComposeSetItem(
                SetContext(), Content(new[] { "Angelic Sickle", "Angelic Wings" }), NoStats);

            ItemTooltipLine[] display = lines.ToArray();

            Assert.Equal(ItemTooltipSection.SetName, display[display.Length - 3].Section);
            Assert.Equal(ItemTooltipColor.Unique, display[display.Length - 3].Color);
            Assert.Equal("Angelic Raiment\n", display[display.Length - 3].Text);

            Assert.Equal(ItemTooltipSection.SetPieceList, display[display.Length - 2].Section);
            Assert.Equal(ItemTooltipSection.SetPieceList, display[display.Length - 1].Section);
        }

        /// <summary>
        /// Test 3. 0x48d9a9 appends str(3998) with no test in front of it, so there is always one
        /// blank row above the set name — even with both bonus blocks empty.
        /// </summary>
        [Fact]
        public void One_blank_row_always_separates_the_set_name_from_what_is_above_it()
        {
            var sections = new FakeSections().Set(ItemTooltipSection.ItemName, "Angelic Halo");

            IReadOnlyList<ItemTooltipLine> lines = Composer(sections).ComposeSetItem(
                SetContext(), Content(new[] { "Angelic Sickle" }), NoStats);

            Assert.Equal(
                "Angelic Halo\n\nAngelic Raiment\nAngelic Sickle",
                Composer(sections).Render(lines));
        }

        /// <summary>
        /// Test 4. The SECOND blank comes from 0x48d97f, which sits inside the `var_3390 is
        /// non-empty` test at 0x48d96a.
        /// </summary>
        [Fact]
        public void The_second_blank_row_appears_only_with_a_full_set_block()
        {
            var sections = new FakeSections().Set(ItemTooltipSection.ItemName, "Halo");

            IReadOnlyList<ItemTooltipLine> without = Composer(sections).ComposeSetItem(
                SetContext(), Content(new[] { "Sickle" }), NoStats);

            Assert.Equal(1, Rows(Composer(sections).Render(without)).Count(r => r.Length == 0));

            IReadOnlyList<ItemTooltipLine> with = Composer(sections).ComposeSetItem(
                SetContext(),
                Content(new[] { "Sickle" }, full: "+10 to All Attributes\n"),
                NoStats);

            Assert.Equal(
                "Halo\n\n+10 to All Attributes\n\nAngelic Raiment\nSickle",
                Composer(sections).Render(with));
        }

        /// <summary>
        /// Test 5. `dwAnimMode == 1` at 0x48d870 is the whole gate on the full-set block, and the
        /// builder folds it into an empty FullSetText.
        /// </summary>
        [Fact]
        public void The_full_set_block_is_absent_when_the_piece_is_not_equipped()
        {
            var builder = new SetItemTooltipBuilder(
                Data, new SetTable(Data.Sets, Data.SetItems, Data.Strings),
                new ItemTable(Data.Weapons, Data.Armor, Data.Misc),
                new ItemTypeTree(Data.ItemTypes));

            var input = new SetItemTooltipInput();
            input.FullSetStats = new[]
            {
                new KeyValuePair<int, int>(ItemStatReader.PackStatKey(0, 39), 25),
            };

            ItemIdentity halo = AngelicHaloIdentity();

            input.IsEquipped = false;
            Assert.Equal(
                string.Empty,
                builder.Build(null, halo, null, new Dictionary<int, int>(), input).FullSetText);

            input.IsEquipped = true;
            Assert.NotEqual(
                string.Empty,
                builder.Build(null, halo, null, new Dictionary<int, int>(), input).FullSetText);
        }

        /// <summary>
        /// Test 6. var_4F90 holds the ethereal/socketed text AND the modifier block, so 0x48d9e0
        /// gives the pair ONE AppendAsWideChar where the generic path spends two.
        /// </summary>
        [Fact]
        public void The_ethereal_text_and_the_modifier_block_share_one_marker()
        {
            var sections = new FakeSections()
                .Set(ItemTooltipSection.ItemName, "Halo")
                .Set(ItemTooltipSection.EtherealSocketed, "Socketed (2)\n");

            IReadOnlyList<ItemTooltipLine> lines = Composer(sections).ComposeSetItem(
                SetContext(ItemTooltipFlags.Socketed),
                Content(new[] { "Sickle" }),
                new[] { new KeyValuePair<int, int>(ItemStatReader.PackStatKey(0, 39), 25) });

            int game = lines.Count(l => l.EmitsColorMarker
                                        && (l.Section == ItemTooltipSection.Modifiers
                                            || l.Section == ItemTooltipSection.EtherealSocketed));

            Assert.Equal(1, game);
            Assert.Contains("Socketed (2)", Composer(sections).Render(lines));
            Assert.Contains("Fire Resist", Composer(sections).Render(lines));
        }

        /// <summary>
        /// Test 6b. The buffer's gate is the SOCKETED flag alone (0x48d7e6), not the
        /// ethereal-or-socketed test INV_FormatEtherealSocketedText itself makes.
        /// </summary>
        [Fact]
        public void An_ethereal_but_unsocketed_set_item_gets_no_ethereal_line()
        {
            var sections = new FakeSections()
                .Set(ItemTooltipSection.ItemName, "Halo")
                .Set(ItemTooltipSection.EtherealSocketed, "Ethereal (Cannot Be Repaired)\n");

            IReadOnlyList<ItemTooltipLine> lines = Composer(sections).ComposeSetItem(
                SetContext(ItemTooltipFlags.Ethereal), Content(new[] { "Sickle" }), NoStats);

            Assert.DoesNotContain("Ethereal", Composer(sections).Render(lines));
        }

        /// <summary>
        /// Test 7. 0x48d93b prepends a `ÿc2` to the whole assembled list, in front of the first
        /// piece's own marker. AppendAsWideChar no-ops on an empty buffer (0x4521cd).
        /// </summary>
        [Fact]
        public void The_piece_list_carries_a_redundant_leading_marker_unless_it_is_empty()
        {
            var sections = new FakeSections().Set(ItemTooltipSection.ItemName, "Halo");

            string withPieces = Composer(sections).RenderWithColorCodes(
                Composer(sections).ComposeSetItem(
                    SetContext(), Content(new[] { "Sickle" }, new[] { false }), NoStats));

            Assert.EndsWith(Marker + "2" + Marker + "1Sickle", withPieces, StringComparison.Ordinal);

            string empty = Composer(sections).RenderWithColorCodes(
                Composer(sections).ComposeSetItem(SetContext(), Content(), NoStats));

            Assert.EndsWith(Marker + "4Angelic Raiment", empty, StringComparison.Ordinal);
            Assert.DoesNotContain(Marker + "2" + Marker, empty);
        }

        /// <summary>
        /// Test 8. 0x48d79a-0x48d7ae: flag 0x100 alone, and nothing else, reddens the name here.
        /// </summary>
        [Theory]
        [InlineData(0u, ItemTooltipColor.Set)]
        [InlineData(0x100u, ItemTooltipColor.Red)]
        public void The_item_name_is_green_unless_the_item_is_broken(
            uint flags, int expected)
        {
            var sections = new FakeSections().Set(ItemTooltipSection.ItemName, "Halo");

            IReadOnlyList<ItemTooltipLine> lines = Composer(sections).ComposeSetItem(
                SetContext((ItemTooltipFlags)flags), Content(new[] { "Sickle" }), NoStats);

            Assert.Equal(
                expected,
                lines.First(l => l.Section == ItemTooltipSection.ItemName).Color);
        }

        /// <summary>
        /// Test 9. 0x48d595-0x48d5ab reddens the class line only for a PLAYER of the wrong class;
        /// var_28 is zeroed at 0x48d2eb and never written again, so everything else is white. The
        /// composer takes that from IsRequirementUnmet, which is the same predicate.
        /// </summary>
        [Theory]
        [InlineData(true, ItemTooltipColor.Red)]
        [InlineData(false, ItemTooltipColor.White)]
        public void The_class_restriction_reddens_only_for_a_mismatched_player(
            bool unmet, int expected)
        {
            var sections = new FakeSections()
                .Set(ItemTooltipSection.ItemName, "Halo")
                .Set(ItemTooltipSection.ClassRestriction, "(Paladin Only)\n");

            if (unmet)
            {
                sections.Unmeetable(ItemTooltipSection.ClassRestriction);
            }

            IReadOnlyList<ItemTooltipLine> lines = Composer(sections).ComposeSetItem(
                SetContext(), Content(new[] { "Sickle" }), NoStats);

            Assert.Equal(
                expected,
                lines.First(l => l.Section == ItemTooltipSection.ClassRestriction).Color);
        }

        /// <summary>
        /// Test 10. LoadItemDesc truncates at 0x48ed12; ITEM_BuildSetItemTooltip runs from
        /// MoveArgumentToEAX 0x48db0b straight to TEXT_CalcTextDimensions 0x48db1d over a
        /// 2048-WCHAR buffer with no guard.
        /// </summary>
        [Fact]
        public void Nothing_is_cut_at_1023_characters()
        {
            var pieces = new string[6];
            for (int i = 0; i < pieces.Length; ++i)
            {
                pieces[i] = new string((char)('a' + i), 300);
            }

            var sections = new FakeSections().Set(ItemTooltipSection.ItemName, "Halo");

            IReadOnlyList<ItemTooltipLine> lines = Composer(sections).ComposeSetItem(
                SetContext(), Content(pieces), NoStats);

            string rendered = Composer(sections).Render(lines, false,
                ItemTooltipComposer.UnlimitedTooltipLength);

            Assert.True(rendered.Length > 1800, "expected no cut, got " + rendered.Length);
            foreach (string piece in pieces)
            {
                Assert.Contains(piece, rendered);
            }

            // The budget is a knob, not a removal: the generic default still cuts.
            Assert.True(
                Composer(sections).Render(lines).Length <= ItemTooltipComposer.MaxTooltipLength);
        }

        /// <summary>
        /// Test 11. INV_FormatDefenseRangeText is reached only inside `IsOfType(item, 51)`
        /// (0x48d68a), so a set BOOT never gets the Kick Damage line the generic path gives an
        /// Assassin.
        /// </summary>
        [Fact]
        public void A_set_boot_gets_no_kick_damage_line()
        {
            var sections = new FakeSections()
                .Set(ItemTooltipSection.ItemName, "Boots")
                .Set(ItemTooltipSection.SmiteOrKickDamage, "Kick Damage: 3 to 8\n");

            IReadOnlyList<ItemTooltipLine> boots = Composer(sections).ComposeSetItem(
                SetContext(weaponOrArmor: true), Content(new[] { "Sickle" }), NoStats);

            Assert.DoesNotContain("Kick Damage", Composer(sections).Render(boots));

            // The same buffer on a SHIELD is emitted, so the gate is the shield test and not a
            // blanket suppression.
            IReadOnlyList<ItemTooltipLine> shield = Composer(sections).ComposeSetItem(
                SetContext(weaponOrArmor: true, shield: true),
                Content(new[] { "Sickle" }), NoStats);

            Assert.Contains("Kick Damage", Composer(sections).Render(shield));
        }

        /// <summary>
        /// Test 12. There is no call site for any of these in the writer's 638 instructions, so a
        /// provider that offers them must be ignored.
        /// </summary>
        [Fact]
        public void The_sections_the_writer_never_calls_are_never_emitted()
        {
            var sections = new FakeSections()
                .Set(ItemTooltipSection.ItemName, "Halo")
                .Set(ItemTooltipSection.QuestUsage, "Right click to open\n")
                .Set(ItemTooltipSection.Unidentified, "Unidentified\n")
                .Set(ItemTooltipSection.SocketFillerDescription, "Weapons: +1\n")
                .Set(ItemTooltipSection.CharmDescription, "Keep in inventory\n")
                .Set(ItemTooltipSection.QuantityAndSpellDescription, "Quantity: 20\n")
                .Set(ItemTooltipSection.RuneLetters, "'RalOrt'\n");

            string rendered = Composer(sections).Render(
                Composer(sections).ComposeSetItem(
                    SetContext(weaponOrArmor: true, shield: true),
                    Content(new[] { "Sickle" }), NoStats));

            Assert.Equal("Halo\n\nAngelic Raiment\nSickle", rendered);
        }

        // ---------------------------------------------------------------- 13-16

        /// <summary>Test 13. `add func` 0 leaves v7 at -1 and neither arm runs (0x4e65a3).</summary>
        [Fact]
        public void Add_func_zero_selects_no_tier()
        {
            Assert.Empty(SetBonusTiers.Select(0, 0, 0x3F, 0x3F));
        }

        /// <summary>
        /// Test 14. `add func` 2 counts the mask through dword_6DBD90 and lights tiers 0..N-2
        /// (0x4e65c7 / 0x4e65ce), so six worn pieces still never reach STATE_ITEMSET6.
        /// </summary>
        [Theory]
        [InlineData(0x00, new int[0])]
        [InlineData(0x01, new int[0])]
        [InlineData(0x03, new[] { 165 })]
        [InlineData(0x07, new[] { 165, 166 })]
        [InlineData(0x3F, new[] { 165, 166, 167, 168, 169 })]
        public void Add_func_two_lights_one_tier_fewer_than_the_pieces_worn(
            int mask, int[] expected)
        {
            Assert.Equal(expected, SetBonusTiers.Select(2, 0, mask, 0).ToArray());
        }

        /// <summary>
        /// Test 15. `add func` 1 maps the WORN slot to a tier, collapsing over the gap this piece
        /// leaves (0x4e662f).
        /// </summary>
        [Fact]
        public void Add_func_one_maps_each_worn_sibling_to_its_own_tier()
        {
            Assert.Equal(
                new[] { 165, 168 },
                SetBonusTiers.Select(1, 2, 0, (1 << 0) | (1 << 4)).ToArray());

            // Self is skipped outright even when its own bit is set.
            Assert.Equal(
                new[] { 165 },
                SetBonusTiers.Select(1, 2, 0, (1 << 0) | (1 << 2)).ToArray());
        }

        /// <summary>
        /// Test 16. THE SPEC HAD THIS BACKWARDS. `docs/set-item-tooltip.md` §11 claimed an
        /// unequipped set item with worn siblings still shows a green tier, and that
        /// `includeUnearned: true` is therefore the right view. It is not:
        /// SKILLDESC_BuildStatBuffDesc reaches the tier through GetStatList(item, state, 0)
        /// (0x4e60ff) and a zero mask sends that down the pMyLastList chain at +0x3C (0x6257ef),
        /// while STATLIST_ToggleStateDisabled parks a disabled tier on +0x40 by setting
        /// STATLIST_SET (0x6279e7) and re-attaching (0x626e67). A tier carrying the bit is
        /// unreachable, so the writer emits nothing.
        /// </summary>
        [Fact]
        public void A_tier_still_carrying_STATLIST_SET_renders_nothing()
        {
            var builder = new SetItemTooltipBuilder(
                Data, new SetTable(Data.Sets, Data.SetItems, Data.Strings),
                new ItemTable(Data.Weapons, Data.Armor, Data.Misc),
                new ItemTypeTree(Data.ItemTypes));

            var input = new SetItemTooltipInput();
            input.WornMaskIncludingSelf = (1 << 2) | (1 << 3);
            input.WornMaskExcludingSelf = 1 << 3;

            // Tier 0 IS selected by the arithmetic...
            Assert.Equal(new[] { 165 }, SetBonusTiers.Select(2, 2, input.WornMaskIncludingSelf, 0)
                .ToArray());

            Unit disabled = AngelicHaloRecord(
                ItemStatListFlags.Magic | ItemStatListFlags.Set);
            Assert.Equal(
                string.Empty,
                builder.Build(disabled, AngelicHaloIdentity(), Player(30), Merged(disabled), input)
                    .PartialText);

            Unit enabled = AngelicHaloRecord(ItemStatListFlags.Magic);
            Assert.NotEqual(
                string.Empty,
                builder.Build(enabled, AngelicHaloIdentity(), Player(30), Merged(enabled), input)
                    .PartialText);
        }

        // ---------------------------------------------------------------- 17-20

        private static SetTable RealSets()
        {
            return new SetTable(Data.Sets, Data.SetItems, Data.Strings);
        }

        /// <summary>
        /// Test 17. Row counts are post-splice — sets.txt loses its `Expansion` divider at
        /// pre-splice body index 16 and setitems.txt at 62 — and the link at 0x63668d walks
        /// setitems.txt ascending.
        /// </summary>
        [Fact]
        public void The_shipped_tables_link_in_ascending_setitems_order()
        {
            SetTable sets = RealSets();

            Assert.Equal(32, sets.SetCount);
            Assert.Equal(127, sets.PieceCount);

            int linked = 0;
            for (int setId = 0; setId < sets.SetCount; ++setId)
            {
                SetRecord set = sets.SetAt(setId);
                Assert.Equal(setId, set.SetId);
                Assert.True(set.Pieces.Count <= SetTable.MaxPiecesPerSet);

                for (int i = 0; i < set.Pieces.Count; ++i)
                {
                    Assert.Equal(setId, set.Pieces[i].SetId);
                    Assert.Equal(i, set.Pieces[i].Slot);

                    if (i > 0)
                    {
                        Assert.True(set.Pieces[i - 1].SetItemId < set.Pieces[i].SetItemId);
                    }
                }

                linked += set.Pieces.Count;
            }

            // Every shipped piece names a set that exists and fits, so nothing is dropped.
            Assert.Equal(127, linked);
        }

        /// <summary>
        /// Test 18. sets.txt `name` is a KEY. Three shipped sets resolve to a different display
        /// name, which is what makes resolving by key rather than by value load-bearing.
        /// </summary>
        [Theory]
        [InlineData(13, "Angelical Raiment", "Angelic Raiment")]
        [InlineData(11, "Berserker's Garb", "Berserker's Arsenal")]
        [InlineData(31, "McAuley's Folly", "Sander's Folly")]
        public void A_sets_display_name_is_not_its_key(int setId, string key, string display)
        {
            SetRecord set = RealSets().SetAt(setId);

            Assert.Equal(key, set.Key);
            Assert.Equal(display, set.Name);
        }

        /// <summary>Test 19. wsprintf 0x48d8dd with locale 10089, which is bare `%0`.</summary>
        [Fact]
        public void The_piece_line_is_the_name_verbatim()
        {
            Assert.Equal("%0", Data.Strings.GetByIndex(10089));

            // setitems.txt +0x24 is the `index` cell resolved through the string table, and for a
            // piece the key and the display name happen to agree — unlike a SET, where they do not.
            SetItemRecord sickle = RealSets().PieceAt(AngelicSickleRow);
            Assert.Equal("Angelic Sickle", sickle.Key);
            Assert.Equal("Angelic Sickle", sickle.Name);
            Assert.Equal(2, sickle.AddFunc);
            Assert.Equal(0, sickle.Slot);

            SetItemTooltipContent content = RealBuilder().Build(
                AngelicHaloRecord(ItemStatListFlags.Magic), AngelicHaloIdentity(), Player(30),
                new Dictionary<int, int>(), new SetItemTooltipInput());

            Assert.Equal(
                new[]
                {
                    "Angelic Sickle\n", "Angelic Mantle\n", "Angelic Halo\n", "Angelic Wings\n",
                },
                content.Pieces.Select(p => p.Text).ToArray());
        }

        /// <summary>
        /// Test 20. The whole thing, against the shipped extraction: a level-30 character wearing
        /// Angelic Halo and Angelic Wings, ShopMode 0.
        ///
        /// docs/set-item-tooltip.md §9 omits the `Ring` row. GetItemName's set arm builds
        /// `base + 3998 + str(setitems[+0x24])` (0x48ca1c), so the base type is a row of its own
        /// directly under the set-item name.
        ///
        /// §9 also predates the derived block. Two of Angelical Raiment's four pieces are worn, so
        /// ITEMMOD_ApplySetBonuses takes limit = 2 * min(2, 3) - 2 = 2 and applies PCode2a
        /// (`dex 10`) and PCode2b (blank, skipped at 0x6601ca) — the gold `+10 to Dexterity` row
        /// and the second blank that 0x48d96a gates on it.
        /// </summary>
        [Fact]
        public void Angelic_Halo_renders_character_for_character()
        {
            Unit record = AngelicHaloRecord(ItemStatListFlags.Magic);

            var input = new SetItemTooltipInput();
            input.OwnedSetItemIds = new[] { AngelicHaloRow, AngelicWingsRow };
            input.WornMaskIncludingSelf = (1 << 2) | (1 << 3);
            input.WornMaskExcludingSelf = 1 << 3;
            input.IsEquipped = true;

            Tooltip tooltip = TooltipEngine.Embedded.RenderSetItem(
                record, input, PlayerRecord(30));

            Assert.Equal(ItemTooltipKind.IdentifiedSetItem, tooltip.Kind);

            Assert.Equal(
                "Angelic Halo\n"
                + "Ring\n"
                + "Required Level: 12\n"
                + "+20 to Life\n"
                + "Replenish Life +6\n"
                + "+360 to Attack Rating (Based on Character Level)\n"
                + "\n"
                + "+10 to Dexterity\n"
                + "\n"
                + "Angelic Raiment\n"
                + "Angelic Wings\n"
                + "Angelic Halo\n"
                + "Angelic Mantle\n"
                + "Angelic Sickle",
                tooltip.Text);

            Assert.Equal(
                Marker + "2Angelic Halo\n"
                + Marker + "2Ring\n"
                + Marker + "0Required Level: 12\n"
                + Marker + "3+20 to Life\n"
                + Marker + "3Replenish Life +6\n"
                + Marker + "2+360 to Attack Rating (Based on Character Level)\n"
                + "\n"
                + Marker + "4+10 to Dexterity\n"
                + "\n"
                + Marker + "4Angelic Raiment\n"
                + Marker + "2Angelic Wings\n"
                + Marker + "2Angelic Halo\n"
                + Marker + "1Angelic Mantle\n"
                + Marker + "2" + Marker + "1Angelic Sickle",
                tooltip.ColoredText);
        }

        /// <summary>
        /// Render classifies and routes on its own, so a set item is drawn rather than refused.
        /// </summary>
        [Fact]
        public void Render_routes_a_set_item_to_the_set_writer()
        {
            Tooltip tooltip = TooltipEngine.Embedded.Render(
                AngelicHaloRecord(ItemStatListFlags.Magic), PlayerRecord(30));

            Assert.Equal(ItemTooltipKind.IdentifiedSetItem, tooltip.Kind);

            // No siblings supplied, so every piece is red and no tier is selected.
            Assert.Equal(
                "Angelic Halo\n"
                + "Ring\n"
                + "Required Level: 12\n"
                + "+20 to Life\n"
                + "Replenish Life +6\n"
                + "\n"
                + "Angelic Raiment\n"
                + "Angelic Wings\n"
                + "Angelic Halo\n"
                + "Angelic Mantle\n"
                + "Angelic Sickle",
                tooltip.Text);
        }

        // ---------------------------------------------------------------- fixtures

        private const int AngelicSickleRow = 50;
        private const int AngelicHaloRow = 52;
        private const int AngelicWingsRow = 53;

        private const int StatMaxHp = 7;
        private const int StatHpRegen = 74;
        private const int StatToHitPerLevel = 224;
        private const int StatLevel = 12;

        private static SetItemTooltipBuilder RealBuilder()
        {
            return new SetItemTooltipBuilder(
                Data, RealSets(), new ItemTable(Data.Weapons, Data.Armor, Data.Misc),
                new ItemTypeTree(Data.ItemTypes));
        }

        private static ItemIdentity AngelicHaloIdentity()
        {
            return ItemRecordReader.ReadIdentity(AngelicHaloRecord(ItemStatListFlags.Magic));
        }

        /// <summary>
        /// setitems.txt post-splice row 52: `Angelic Halo`, item `rin`, add func 2, prop1 regen 6,
        /// prop2 hp 20, aprop1a att/lvl 24. maxhp carries ValShift 8, so +20 is stored as 5120.
        /// </summary>
        private static Unit AngelicHaloRecord(uint tierFlags)
        {
            var items = new ItemTable(Data.Weapons, Data.Armor, Data.Misc);

            var record = new Unit();
            record.UnitType = 4;
            record.ClassId = items.ClassIdForCode("rin");
            record.Quality = ItemQualityNo.Set;
            record.ItemFlags = ItemRecordFlags.Identified;
            record.FileIndex = AngelicHaloRow;

            record.StatsLists.Add(
                new UnitStatList(0, ItemStatListFlags.Magic)
                    .Add(StatHpRegen, 6)
                    .Add(StatMaxHp, 20 << 8));

            record.StatsLists.Add(
                new UnitStatList(ItemStatListStates.ItemSet1, tierFlags)
                    .Add(StatToHitPerLevel, 24));

            return record;
        }

        [Fact]
        public void The_full_set_block_is_derived_from_the_WEARERS_own_statlist()
        {
            // SKILLDESC_AppendItemBuffTextAlt 0x4e6680 walks GetStatsByState(wearer, 165+k) for
            // k 0..5 (0x4e66c9) and keeps the list whose stat 71 is this set's id (0x4e66d7). So it
            // is NOT a caller input: the rolled values are already on the wearer's chain, and a
            // viewer record that carries the chain needs no SetItemTooltipInput.FullSetStats.
            //
            // Angelic Raiment is sets.txt row 13, which is what stat 71 has to hold.
            Unit wearer = PlayerRecord(30);
            wearer.StatsLists.Add(
                new UnitStatList(ItemStatListStates.ItemSet1 + 1, ItemStatListFlags.Magic)
                    .Add(StatSetValue, AngelicRaimentSetId)
                    .Add(StatLifeReplenish, 20));

            var input = new SetItemTooltipInput();
            input.OwnedSetItemIds = new[] { AngelicHaloRow, AngelicWingsRow };
            input.WornMaskIncludingSelf = (1 << 2) | (1 << 3);
            input.WornMaskExcludingSelf = 1 << 3;
            input.IsEquipped = true;

            Tooltip tooltip = TooltipEngine.Embedded.RenderSetItem(
                AngelicHaloRecord(ItemStatListFlags.Magic), input, wearer);

            Assert.Contains("Replenish Life +20", tooltip.Text, StringComparison.Ordinal);

            // A node for a DIFFERENT set on the same chain is skipped — that is what stat 71 is for
            // when a character wears two sets at once.
            Unit other = PlayerRecord(30);
            other.StatsLists.Add(
                new UnitStatList(ItemStatListStates.ItemSet1 + 1, ItemStatListFlags.Magic)
                    .Add(StatSetValue, AngelicRaimentSetId + 1)
                    .Add(StatLifeReplenish, 20));

            Assert.DoesNotContain(
                "Replenish Life +20",
                TooltipEngine.Embedded.RenderSetItem(
                    AngelicHaloRecord(ItemStatListFlags.Magic), input, other).Text,
                StringComparison.Ordinal);
        }

        // ------------------------------------------- the derived set-bonus block, 0x660120

        /// <summary>setitems.txt post-splice row 80 — Tal Rasha's Horadric Crest, `xsk`, add func
        /// blank, slot 4 of five.</summary>
        private const int TalRashasCrestRow = 80;

        /// <summary>sets.txt post-splice row 19 — `Tal Rasha's Wrappings`, five members.</summary>
        private const int TalRashasSetId = 19;

        /// <summary>
        /// The mask for the Crest plus three siblings: bits 0, 1, 2 and 4, its own slot included
        /// (ITEMS_GetEquippedSetItemsMask is asked with includeSelf = 1 at 0x66018b).
        /// </summary>
        private const int FourOfFiveMask = (1 << 0) | (1 << 1) | (1 << 2) | (1 << 4);

        private static Unit TalRashasCrestRecord()
        {
            var items = new ItemTable(Data.Weapons, Data.Armor, Data.Misc);

            var record = new Unit();
            record.UnitType = 4;
            record.ClassId = items.ClassIdForCode("xsk");
            record.Quality = ItemQualityNo.Set;
            record.ItemFlags = ItemRecordFlags.Identified;
            record.FileIndex = TalRashasCrestRow;

            record.StatsLists.Add(new UnitStatList(0, ItemStatListFlags.Extended).Add(31, 100));

            return record;
        }

        private static SetItemTooltipInput WornInput(int mask)
        {
            var input = new SetItemTooltipInput();
            input.WornMaskIncludingSelf = mask;
            input.WornMaskExcludingSelf = mask & ~(1 << 4);
            input.IsEquipped = true;
            return input;
        }

        private static string DerivedFullSet(int mask)
        {
            return RealBuilder().Build(
                TalRashasCrestRecord(),
                ItemRecordReader.ReadIdentity(TalRashasCrestRecord()),
                Player(50),
                new Dictionary<int, int>(),
                WornInput(mask)).FullSetText;
        }

        /// <summary>
        /// The headline case. ITEMMOD_ApplySetBonuses 0x660120 with four of five worn takes
        /// n = min(4, nSetItems - 1) = 4 (0x6601ae-0x6601b5) and limit = 2n - 2 = 6 (0x6601b7), so
        /// the walk at 0x6601c4 covers PCode2a..PCode4b — the 2-, 3- and 4-piece pairs — and
        /// 0x6601fc withholds the FCode block because four is short of five.
        ///
        /// The buffer is APPEND order, which the description engine emits lowest-DescPriority
        /// first: item_magicbonus 8, hpregen 56, item_fastergethitrate 139.
        /// </summary>
        [Fact]
        public void Four_of_five_Tal_Rashas_derives_the_three_partial_bonuses()
        {
            Assert.Equal(
                "65% Better Chance of Getting Magic Items\n"
                + "Replenish Life +10\n"
                + "+25% Faster Hit Recovery\n",
                DerivedFullSet(FourOfFiveMask));

            // And on screen, reversed, gold.
            Tooltip tooltip = TooltipEngine.Embedded.RenderSetItem(
                TalRashasCrestRecord(), WornInput(FourOfFiveMask), PlayerRecord(50));

            Assert.Contains(
                Marker + "4+25% Faster Hit Recovery\n"
                + Marker + "4Replenish Life +10\n"
                + Marker + "465% Better Chance of Getting Magic Items\n",
                tooltip.ColoredText,
                StringComparison.Ordinal);
        }

        /// <summary>
        /// One piece worn gives limit = 2 * 1 - 2 = 0, and `test eax,eax / jle` at 0x6601c2 skips
        /// the partial walk outright. Two pieces is the first mask that draws anything.
        /// </summary>
        [Fact]
        public void One_worn_piece_derives_nothing()
        {
            Assert.Equal(string.Empty, DerivedFullSet(1 << 4));

            Assert.Equal("Replenish Life +10\n", DerivedFullSet((1 << 0) | (1 << 4)));
        }

        /// <summary>
        /// The partial walk SKIPS a blank slot (0x6601ca) where the full walk BREAKS at one
        /// (0x660209). Tal Rasha's has PCode2b, 3b and 4b blank, so three worn pieces must still
        /// reach PCode3a — a walk that stopped at the first blank would show only PCode2a.
        /// </summary>
        [Fact]
        public void A_blank_partial_slot_is_skipped_rather_than_ending_the_walk()
        {
            Assert.Equal(
                "65% Better Chance of Getting Magic Items\nReplenish Life +10\n",
                DerivedFullSet((1 << 0) | (1 << 1) | (1 << 4)));
        }

        /// <summary>
        /// 0x6601fc compares the worn count against sets[+0x0C] itself, not against one less, so
        /// the FCode block waits for the whole set. `state` (FCode6, func 24) writes stat 98, which
        /// ItemStatCost.txt gives no `descfunc` — it renders nothing in either engine.
        /// </summary>
        [Fact]
        public void The_full_code_block_appears_only_when_every_piece_is_worn()
        {
            Assert.DoesNotContain("Sorceress", DerivedFullSet(FourOfFiveMask));

            Assert.Equal(
                "65% Better Chance of Getting Magic Items\n"
                + "All Resistances +50\n"
                + "Replenish Life +10\n"
                + "+150 to Life\n"
                + "+50 Defense vs. Missile\n"
                + "+150 Defense\n"
                + "+25% Faster Hit Recovery\n"
                + "+3 to Sorceress Skill Levels\n",
                DerivedFullSet(0x1F));
        }

        /// <summary>
        /// Precedence is supplied input, then the wearer's chain, then the derivation — the first
        /// two are what the game itself reads (0x4e66c9), the third only reconstructs them.
        /// </summary>
        [Fact]
        public void A_supplied_full_set_block_wins_over_the_derivation()
        {
            SetItemTooltipInput input = WornInput(FourOfFiveMask);
            input.FullSetStats = new[]
            {
                new KeyValuePair<int, int>(ItemStatReader.PackStatKey(0, 39), 25),
            };

            string text = RealBuilder().Build(
                TalRashasCrestRecord(),
                ItemRecordReader.ReadIdentity(TalRashasCrestRecord()),
                Player(50),
                new Dictionary<int, int>(),
                input).FullSetText;

            Assert.Equal("Fire Resist +25%\n", text);

            // And so does the wearer's own STATE_ITEMSET list, which sits between the two.
            Unit wearer = PlayerRecord(50);
            wearer.StatsLists.Add(
                new UnitStatList(ItemStatListStates.ItemSet1, ItemStatListFlags.Magic)
                    .Add(StatSetValue, TalRashasSetId)
                    .Add(StatLifeReplenish, 7));

            Assert.Equal(
                "Replenish Life +7\n",
                RealBuilder().Build(
                    TalRashasCrestRecord(),
                    ItemRecordReader.ReadIdentity(TalRashasCrestRecord()),
                    Player(50),
                    new Dictionary<int, int>(),
                    WornInput(FourOfFiveMask),
                    wearer).FullSetText);
        }

        /// <summary>
        /// The guard that stops a missing property func being SILENT. An unhandled func applies
        /// nothing, so the stat never exists and the line simply is not drawn — nothing fails.
        /// SocketFillerTests has had this for gems.txt since the gem path was written; sets.txt did
        /// not, which is why funcs 21 and 22 were found by reading the data rather than by a red
        /// test, after 9 of 32 sets had been silently dropping lines like `+3 to Sorceress Skill
        /// Levels`.
        ///
        /// Walk every property of every set, apply it, and require that the applier reports nothing
        /// it could not do.
        /// </summary>
        [Fact]
        public void Every_shipped_set_property_reaches_an_implemented_func()
        {
            var applier = new PropertyApplier(
                Data,
                new ItemTable(Data.Weapons, Data.Armor, Data.Misc),
                new ItemTypeTree(Data.ItemTypes));

            SetTable sets = RealSets();
            sets.ResolvePropertyCodesWith(applier.Properties.RowForCode);

            var item = new ItemIdentity();
            var stats = new SortedDictionary<int, int>();
            int applied = 0;

            for (int setId = 0; setId < sets.SetCount; ++setId)
            {
                foreach (ItemProperty property in
                         sets.PartialProperties(setId).Concat(sets.FullProperties(setId)))
                {
                    if (property.PropertyId < 0)
                    {
                        continue;
                    }

                    ++applied;
                    applier.Apply(SetBonusPropMode, item, property, stats);
                }
            }

            // The walk really reached the applier: a resolver that failed would leave every
            // PropertyId at -1 and skip the body, and an empty UnsupportedFunc would then prove
            // nothing at all.
            Assert.Equal(220, applied);
            Assert.NotEmpty(stats);

            Assert.Empty(applier.UnsupportedFunc);

            // And nothing shipped takes func 11's item-level arms — Cow King's `gethit-skill` has
            // max 5, so its level is verbatim.
            Assert.Empty(applier.ItemLevelDependent);
        }

        /// <summary>`push 4` at 0x6601df and 0x66021e — PROPMODE for a set bonus.</summary>
        private const int SetBonusPropMode = 4;

        /// <summary>
        /// Counted over the 32 post-splice sets.txt rows and all sixteen property slots each —
        /// eight partial at +0x10 and eight full at +0x90 — 220 slots carry a code, and only three
        /// have Min != Max.
        /// </summary>
        [Fact]
        public void No_shipped_set_property_is_actually_rolled()
        {
            // `Min != Max` is NOT evidence of a roll. It only means that where the two columns are
            // a range, and Properties.txt decides that per func:
            //
            //   Vidala's Rig    FCode1 dmg-cold     15..20  func 15 coldmindam + 16 coldmaxdam
            //                                               -> "adds 15-20 cold damage", both ends real
            //   Cathan's Traps  PCode2a dmg-fire    15..20  func 15 firemindam + 16 firemaxdam, same
            //   Cow King's      FCode5 gethit-skill 25..5   func 11 item_skillongethit, where Min is
            //                                               the % chance and Max the skill LEVEL —
            //                                               "25% chance to cast level 5 when struck",
            //                                               not an inverted range
            //
            // So all 220 slots are deterministic and the derivation is exact for every one whose
            // func is implemented. This test asserted "3 rolled" while that heuristic was believed;
            // it now pins the three exceptions as the non-ranges they are.
            SetTable sets = RealSets();
            sets.ResolvePropertyCodesWith(code => string.IsNullOrEmpty(code) ? -1 : 0);

            int total = 0;
            var rolled = new List<string>();

            for (int setId = 0; setId < sets.SetCount; ++setId)
            {
                foreach (ItemProperty property in
                         sets.PartialProperties(setId).Concat(sets.FullProperties(setId)))
                {
                    if (property.PropertyId < 0)
                    {
                        continue;
                    }

                    ++total;
                    if (property.Min != property.Max)
                    {
                        rolled.Add(sets.SetAt(setId).Key);
                    }
                }
            }

            Assert.Equal(220, total);

            // Exactly three slots carry Min != Max, and every one is a two-parameter property
            // rather than a range to roll — so all 220 are deterministic.
            Assert.Equal(
                new[] { "Vidala's Rig", "Cathan's Traps", "Cow King's Leathers" },
                rolled.ToArray());
        }

        /// <summary>
        /// sets[+0x0C] is the count the link loop built (`inc` at 0x6366ff, capped at six by
        /// 0x6366df), and the derivation feeds it straight into `min(count, nSetItems - 1)`. It is
        /// Pieces.Count and nothing else — there is no separate column.
        /// </summary>
        [Fact]
        public void The_member_count_the_arithmetic_uses_is_the_linked_piece_count()
        {
            Assert.Equal(5, RealSets().SetAt(TalRashasSetId).Pieces.Count);
            Assert.Equal(TalRashasSetId, RealSets().PieceAt(TalRashasCrestRow).SetId);
            Assert.Equal(4, RealSets().PieceAt(TalRashasCrestRow).Slot);
        }

        /// <summary>itemstatcost `value`, post-splice row 71.</summary>
        private const int StatSetValue = 71;

        /// <summary>item_hpregen, post-splice row 74 — "Replenish Life".</summary>
        private const int StatLifeReplenish = 74;

        /// <summary>sets.txt row for "Angelical Raiment" (docs/set-item-tooltip.md §9).</summary>
        private const int AngelicRaimentSetId = 13;

        private static Unit PlayerRecord(int level)
        {
            var player = new Unit();
            player.UnitType = 0;
            player.ClassId = 0;
            player.StatsLists.Add(
                new UnitStatList(0, ItemStatListFlags.Extended).Add(StatLevel, level));
            return player;
        }

        private static ItemViewer Player(int level)
        {
            return ItemRecordReader.ReadViewer(PlayerRecord(level));
        }

        private static IDictionary<int, int> Merged(Unit record)
        {
            return ItemStatReader.ReconstructView(record, ItemStatView.Equipped());
        }
    }
}
