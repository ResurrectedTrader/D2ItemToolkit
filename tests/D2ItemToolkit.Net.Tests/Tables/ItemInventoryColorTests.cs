using System.Collections.Generic;
using Xunit;

namespace D2ItemToolkit.Tests
{
    /// <summary>
    /// colors.txt and the inventory palette shift, against the shipped tables.
    ///
    /// The point of most of these is the .txt/.bin distinction: every column feeding this holds a
    /// 4-char CODE in the files we embed and a resolved row index in the compiled table a live
    /// consumer reads. A test that only checked "some number came back" would pass while reading
    /// the wrong column entirely.
    /// </summary>
    public class ItemInventoryColorTests
    {
        private static readonly D2DataFiles Data = D2DataFiles.LoadEmbedded();

        private static readonly ItemTable Items = new ItemTable(
            Data.Weapons, Data.Armor, Data.Misc);

        private static readonly ItemTypeTree Types = new ItemTypeTree(Data.ItemTypes);

        private static ItemInventoryColor Colors()
        {
            return new ItemInventoryColor(Data, Items, Types);
        }

        private static ColorTable Table()
        {
            return new ColorTable(Data.Colors);
        }

        // =================================================================
        // colors.txt itself
        // =================================================================

        [Fact]
        public void The_table_is_twenty_one_rows_and_is_not_spliced()
        {
            // No `Expansion` first cell, so STRUCT_CreateBinFieldExcelAndFillData removes nothing
            // and the row index is the literal 0-based position. ItemTypes.txt, by contrast, IS
            // spliced — which is why nothing here uses a literal itemtypes row.
            Assert.Equal(21, Table().RowCount);
        }

        [Theory]
        [InlineData(0, "whit")]
        [InlineData(3, "blac")]
        [InlineData(9, "cred")]
        [InlineData(15, "lgld")]
        [InlineData(20, "bwht")]
        public void A_code_maps_to_its_row_index(int row, string code)
        {
            ColorTable table = Table();

            Assert.Equal(row, table.RowForCode(code));
            Assert.Equal(code, table.CodeAt(row));
        }

        [Fact]
        public void The_last_row_is_the_ceiling_every_lookup_clamps_to()
        {
            Assert.Equal(ColorTable.MaxPaletteIndex, Table().RowCount - 1);
            Assert.Equal(ColorTable.None, ColorTable.Clamp(ColorTable.MaxPaletteIndex + 1));
            Assert.Equal(ColorTable.None, ColorTable.Clamp(-1));
            Assert.Equal(20, ColorTable.Clamp(20));
        }

        [Fact]
        public void An_unknown_or_empty_code_is_no_shift_rather_than_row_zero()
        {
            // Row 0 is `whit`. Falling back to it would silently paint every unmatched item white.
            Assert.Equal(ColorTable.None, Table().RowForCode("zzzz"));
            Assert.Equal(ColorTable.None, Table().RowForCode(string.Empty));
            Assert.Equal(ColorTable.None, Table().RowForCode(null));
        }

        // =================================================================
        // Every colour column in the shipped data resolves
        // =================================================================

        [Theory]
        [InlineData("MagicPrefix")]
        [InlineData("MagicSuffix")]
        [InlineData("AutoMagic")]
        public void Every_affix_transformcolor_names_a_real_row(string which)
        {
            TxtFile table = which == "MagicPrefix" ? Data.MagicPrefix
                : which == "MagicSuffix" ? Data.MagicSuffix
                : Data.AutoMagic;

            ColorTable colors = Table();
            var orphans = new List<string>();
            int populated = 0;

            for (int row = 0; row < table.RowCount; ++row)
            {
                string code = (table.GetString(row, "transformcolor") ?? string.Empty).Trim();
                if (code.Length == 0)
                {
                    continue;
                }

                ++populated;
                if (colors.RowForCode(code) < 0)
                {
                    orphans.Add(row + ":" + code);
                }
            }

            Assert.True(populated > 0, which + " has no transformcolor cells at all");
            Assert.Empty(orphans);
        }

        [Theory]
        [InlineData("UniqueItems")]
        [InlineData("SetItems")]
        public void Every_invtransform_names_a_real_row(string which)
        {
            TxtFile table = which == "UniqueItems" ? Data.UniqueItems : Data.SetItems;

            ColorTable colors = Table();
            var orphans = new List<string>();
            int populated = 0;

            for (int row = 0; row < table.RowCount; ++row)
            {
                string code = (table.GetString(row, "invtransform") ?? string.Empty).Trim();
                if (code.Length == 0)
                {
                    continue;
                }

                ++populated;
                if (colors.RowForCode(code) < 0)
                {
                    orphans.Add(row + ":" + code);
                }
            }

            Assert.True(populated > 0, which + " has no invtransform cells at all");
            Assert.Empty(orphans);
        }

        [Fact]
        public void The_gems_transform_column_is_numeric_and_in_range()
        {
            // The one column that is ALREADY an index in the .txt, which is why the gem arm does
            // not go through ColorTable.
            int populated = 0;
            for (int row = 0; row < Data.Gems.RowCount; ++row)
            {
                int transform = Data.Gems.GetInt(row, "transform", -1);
                if (transform < 0)
                {
                    continue;
                }

                ++populated;
                Assert.InRange(transform, 0, ColorTable.MaxPaletteIndex);
            }

            Assert.True(populated > 0);
        }

        // =================================================================
        // Resolution
        // =================================================================

        private static ItemIdentity Item(string code, int quality, ItemRecordFlags flags)
        {
            var item = new ItemIdentity();
            item.ClassId = Items.ClassIdForCode(code);
            item.Quality = quality;
            item.Flags = flags;
            return item;
        }

        [Fact]
        public void A_set_item_takes_its_rows_invtransform()
        {
            // SetItems row 3 is Hsarus' Iron Heel, invtransform `dred` = 8.
            ItemIdentity item = Item("lbt", ItemQualityNo.Set, ItemRecordFlags.Identified);
            item.FileIndex = 3;

            Assert.Equal(Table().RowForCode("dred"), Colors().Resolve(item));
        }

        [Fact]
        public void An_unidentified_set_or_unique_has_no_shift()
        {
            // dwFileIndex is not carried by the client until identified, so the game returns no
            // shift rather than reading a row it does not have.
            ItemIdentity item = Item("lbt", ItemQualityNo.Set, ItemRecordFlags.None);
            item.FileIndex = 3;

            Assert.Equal(ColorTable.None, Colors().Resolve(item));
        }

        [Fact]
        public void A_set_or_unique_never_falls_through_to_the_affix_path()
        {
            // Even with a coloured affix set, the set arm returns directly.
            ItemIdentity item = Item("lbt", ItemQualityNo.Set, ItemRecordFlags.Identified);
            item.FileIndex = -1;
            item.MagicSuffix[0] = FirstAffixWithAColour();

            Assert.Equal(ColorTable.None, Colors().Resolve(item));
        }

        /// <summary>The first id in the concatenated affix array whose row carries a colour.</summary>
        private static int FirstAffixWithAColour()
        {
            ColorTable colors = Table();
            for (int row = 0; row < Data.MagicSuffix.RowCount; ++row)
            {
                string code = (Data.MagicSuffix.GetString(row, "transformcolor") ?? string.Empty).Trim();
                if (code.Length != 0 && colors.RowForCode(code) >= 0)
                {
                    return row + 1; // 1-based into [magicsuffix][magicprefix][automagic]
                }
            }

            return 0;
        }

        [Fact]
        public void A_magic_item_takes_its_suffixs_transformcolor()
        {
            int affixId = FirstAffixWithAColour();
            Assert.True(affixId > 0, "no shipped magic suffix carries a transformcolor");

            string expected =
                Data.MagicSuffix.GetString(affixId - 1, "transformcolor").Trim();

            ItemIdentity item = Item("rin", ItemQualityNo.Magic, ItemRecordFlags.Identified);
            item.MagicSuffix[0] = affixId;

            Assert.Equal(Table().RowForCode(expected), Colors().Resolve(item));
        }

        [Fact]
        public void A_magic_item_with_no_coloured_affix_has_no_shift()
        {
            ItemIdentity item = Item("rin", ItemQualityNo.Magic, ItemRecordFlags.Identified);

            Assert.Equal(ColorTable.None, Colors().Resolve(item));
        }

        [Fact]
        public void A_normal_item_is_tinted_by_a_gem_in_the_first_socket()
        {
            // Perfect Amethyst: gems row 4, transform 17.
            ItemIdentity shield = Item("lrg", ItemQualityNo.Normal, ItemRecordFlags.Identified);
            ItemIdentity gem = Item("gpv", ItemQualityNo.Normal, ItemRecordFlags.Identified);

            var gems = new GemTable(Data.Gems, Items);
            int row = gems.RowForFillerClassId(gem.ClassId);
            Assert.True(row >= 0, "gpv did not resolve to a gems row");

            int expected = Data.Gems.GetInt(row, "transform", -1);
            Assert.InRange(expected, 0, ColorTable.MaxPaletteIndex);

            Assert.Equal(expected, Colors().Resolve(shield, gem));
        }

        [Fact]
        public void A_rune_in_the_first_socket_does_not_tint()
        {
            // Runes share gems.txt — el carries a real transform — but they are itemtype `rune`
            // under `sock`, not `gem`, so IsOfType(gem) excludes them.
            ItemIdentity shield = Item("lrg", ItemQualityNo.Normal, ItemRecordFlags.Identified);
            ItemIdentity rune = Item("r01", ItemQualityNo.Normal, ItemRecordFlags.Identified);

            Assert.Equal(ColorTable.None, Colors().Resolve(shield, rune));
        }

        [Fact]
        public void An_empty_socket_leaves_a_normal_item_untinted()
        {
            ItemIdentity shield = Item("lrg", ItemQualityNo.Normal, ItemRecordFlags.Identified);

            Assert.Equal(ColorTable.None, Colors().Resolve(shield));
        }

        // =================================================================
        // The inventory sprite name
        // =================================================================

        private static ItemInventoryGraphics Graphics()
        {
            return new ItemInventoryGraphics(Data, Items, Types);
        }

        [Fact]
        public void A_self_named_graphic_keeps_the_items_own_code()
        {
            // lrg's invfile is `invlrg`, which IS "inv" + code, so the item has its own art.
            Assert.Equal(
                "lrg",
                Graphics().Resolve(Item("lrg", ItemQualityNo.Normal, ItemRecordFlags.Identified)));
        }

        [Theory]
        [InlineData("xap", "cap")]
        [InlineData("xkp", "skp")]
        [InlineData("xlm", "hlm")]
        public void An_exceptional_tier_collapses_to_its_normal_code(string code, string expected)
        {
            // xap is the exceptional Cap and its invfile is `invcap` — a SHARED graphic — so the
            // sprite is named by the normal tier, not by the item.
            Assert.Equal(
                expected,
                Graphics().Resolve(Item(code, ItemQualityNo.Normal, ItemRecordFlags.Identified)));
        }

        [Theory]
        [InlineData(0, "rin1")]
        [InlineData(4, "rin5")]
        public void A_ring_appends_its_one_based_graphics_variant(int gfxIndex, string expected)
        {
            // itemtypes `ring` has VarInvGfx 5. gfxIndex is 0-based and the suffix is 1-based, so
            // the only thing separating rin1 from rin5 is the field the producer now emits.
            ItemIdentity ring = Item("rin", ItemQualityNo.Magic, ItemRecordFlags.Identified);
            ring.GfxIndex = gfxIndex;

            Assert.Equal(expected, Graphics().Resolve(ring));
        }

        [Fact]
        public void An_item_type_without_variants_gets_no_suffix()
        {
            // `shie` has no VarInvGfx, so a non-zero index must NOT leak into the name.
            ItemIdentity shield = Item("lrg", ItemQualityNo.Normal, ItemRecordFlags.Identified);
            shield.GfxIndex = 3;

            Assert.Equal("lrg", Graphics().Resolve(shield));
        }

        [Fact]
        public void An_identified_unique_takes_its_own_row_graphic()
        {
            // UniqueItems row 0 is The Gnasher, invfile `invhaxu`.
            ItemIdentity axe = Item("hax", ItemQualityNo.Unique, ItemRecordFlags.Identified);
            axe.FileIndex = 0;

            Assert.Equal("invhaxu", Graphics().Resolve(axe));
        }

        [Fact]
        public void An_unidentified_unique_keeps_the_plain_sprite()
        {
            // dwFileIndex is not carried until identified, so the special graphic cannot apply.
            ItemIdentity axe = Item("hax", ItemQualityNo.Unique, ItemRecordFlags.None);
            axe.FileIndex = 0;

            Assert.Equal("hax", Graphics().Resolve(axe));
        }

        [Fact]
        public void A_unique_with_no_row_graphic_falls_back_to_uniqueinvfile()
        {
            // The Amulet of the Viper: the one misc row carrying uniqueinvfile.
            ItemIdentity amulet = Item("vip", ItemQualityNo.Unique, ItemRecordFlags.Identified);
            amulet.FileIndex = -1;

            Assert.Equal("invvip", Graphics().Resolve(amulet));
        }

        [Fact]
        public void No_shipped_set_row_carries_its_own_graphic()
        {
            // Reachability, not decoration: SetItems.invfile is empty on EVERY row, so the set arm
            // ALWAYS reaches the items.txt setinvfile fallback. A test that only exercised the
            // populated path would be testing nothing a player can see.
            int populated = 0;
            for (int row = 0; row < Data.SetItems.RowCount; ++row)
            {
                if (!string.IsNullOrEmpty(
                        (Data.SetItems.GetString(row, "invfile") ?? string.Empty).Trim()))
                {
                    ++populated;
                }
            }

            Assert.Equal(0, populated);

            // And the counterpart IS populated, so the unique arm really does use it.
            int uniques = 0;
            for (int row = 0; row < Data.UniqueItems.RowCount; ++row)
            {
                if (!string.IsNullOrEmpty(
                        (Data.UniqueItems.GetString(row, "invfile") ?? string.Empty).Trim()))
                {
                    ++uniques;
                }
            }

            Assert.Equal(140, uniques);
        }

        [Fact]
        public void Appearance_gives_the_sprite_the_colour_and_the_gate_together()
        {
            Unit ring = Unit.FromJson(
                @"{ ""unitType"": 4, ""classId"": " + Items.ClassIdForCode("rin")
                + @", ""quality"": 4, ""itemFlags"": 16, ""gfxIndex"": 2, ""statsLists"": [] }");

            ItemAppearance appearance = TooltipEngine.Embedded.Appearance(ring);

            Assert.Equal("rin3", appearance.Image);
            Assert.Equal(ColorTable.None, appearance.Color);
            Assert.False(appearance.IsTinted);
        }

        // =================================================================
        // InvTrans — the gate, not the colour
        // =================================================================

        [Fact]
        public void InvTrans_is_read_as_a_number_from_the_item_row()
        {
            int classId = Items.ClassIdForCode("lrg");

            Assert.Equal(Items.GetInt(classId, "InvTrans"), Colors().InvTrans(classId));
        }

        [Fact]
        public void At_least_one_shipped_item_has_a_non_zero_InvTrans()
        {
            // If every item were zero the gate would be vacuous and IsTinted always false.
            int nonZero = 0;
            foreach (string code in new[] { "rin", "amu", "jew", "cm1", "lrg" })
            {
                if (Colors().InvTrans(Items.ClassIdForCode(code)) != 0)
                {
                    ++nonZero;
                }
            }

            Assert.True(nonZero > 0, "no sampled item has a non-zero InvTrans");
        }
    }
}
