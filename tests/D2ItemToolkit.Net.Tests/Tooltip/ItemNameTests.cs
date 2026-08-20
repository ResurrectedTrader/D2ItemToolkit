using System;
using Xunit;

namespace D2ItemToolkit.Tests
{
    public class ItemNameTests
    {
        private static readonly D2DataFiles Data = D2DataFiles.LoadEmbedded();

        private static readonly ItemTable Items = new ItemTable(
            Data.Weapons, Data.Armor, Data.Misc);

        private static readonly ItemNameBuilder Names = new ItemNameBuilder(Data, Items);

        private static int ClassId(string code)
        {
            int id = Items.ClassIdForCode(code);
            Assert.True(id >= 0, "no items row for " + code);
            return id;
        }

        private static ItemIdentity Item(
            string code, int quality, int fileIndex = -1, bool identified = true)
        {
            var item = new ItemIdentity();
            item.ClassId = ClassId(code);
            item.Code = code;
            item.Quality = quality;
            item.FileIndex = fileIndex;
            item.Flags = identified ? ItemRecordFlags.Identified : ItemRecordFlags.None;
            return item;
        }

        [Fact]
        public void The_base_name_comes_from_namestr()
        {
            Assert.Equal("Large Shield", Names.Build(Item("lrg", ItemQualityNo.Normal)));
            Assert.Equal("Short Sword", Names.Build(Item("ssd", ItemQualityNo.Normal)));
        }

        [Fact]
        public void An_unidentified_item_shows_only_the_base_name()
        {
            ItemIdentity item = Item("lrg", ItemQualityNo.Unique, 0, identified: false);

            Assert.Equal("Large Shield", Names.Build(item));
        }

        [Fact]
        public void Superior_and_low_quality_wrap_the_base_name()
        {
            Assert.Equal("Superior Large Shield", Names.Build(Item("lrg", ItemQualityNo.Superior)));

            // lowqualityitems row 0 is Crude.
            Assert.Equal("Crude Large Shield", Names.Build(Item("lrg", ItemQualityNo.Inferior, 0)));
        }

        [Fact]
        public void A_null_low_quality_row_writes_nothing()
        {
            // dwFileIndex is 3 bits against only 4 rows, so 4..7 is reachable and writes nothing.
            Assert.Null(Names.Build(Item("lrg", ItemQualityNo.Inferior, 6)));
        }

        [Fact]
        public void A_socketed_normal_item_becomes_gemmed()
        {
            ItemIdentity item = Item("lrg", ItemQualityNo.Normal);
            item.Flags |= ItemRecordFlags.Socketed;

            Assert.Equal("Gemmed Large Shield", Names.Build(item, 1));
        }

        [Fact]
        public void An_empty_socketed_item_keeps_its_plain_name()
        {
            // 0x48c4b5 also needs ITEM_ItemsInItem(pInventory) above zero, so an unfilled socketed
            // white item is not "Gemmed".
            ItemIdentity item = Item("lrg", ItemQualityNo.Normal);
            item.Flags |= ItemRecordFlags.Socketed;

            Assert.Equal("Large Shield", Names.Build(item));
        }

        [Fact]
        public void A_magic_item_takes_a_prefix_and_a_suffix_from_the_concatenated_array()
        {
            ItemIdentity item = Item("lrg", ItemQualityNo.Magic);

            // The magic array is [MagicSuffix][MagicPrefix][automagic], 1-based, so id 1 is the
            // FIRST SUFFIX row, not a prefix.
            item.MagicPrefix[0] = 1;
            item.MagicSuffix[0] = 1;

            string name = Names.Build(item);

            Assert.Contains("Large Shield", name, StringComparison.Ordinal);

            // Both affix slots resolved to the same row, so the name repeats that word.
            string firstSuffixName =
                TxtKeysProbe(Data.MagicSuffix, 0, "Name");
            Assert.Contains(firstSuffixName, name, StringComparison.Ordinal);
        }

        [Fact]
        public void An_id_past_the_suffix_table_falls_into_the_prefix_table()
        {
            ItemIdentity item = Item("lrg", ItemQualityNo.Magic);

            int suffixRows = Data.MagicSuffix.RowCount;
            item.MagicPrefix[0] = suffixRows + 1; // first PREFIX row
            item.MagicSuffix[0] = 0;

            string name = Names.Build(item);
            string firstPrefixName = TxtKeysProbe(Data.MagicPrefix, 0, "Name");

            Assert.Contains(firstPrefixName, name, StringComparison.Ordinal);
        }

        [Fact]
        public void A_rare_item_puts_the_base_name_first_then_the_two_affixes()
        {
            ItemIdentity item = Item("lrg", ItemQualityNo.Rare);
            item.RarePrefix = 1;
            item.RareSuffix = 2;

            string name = Names.Build(item);
            string[] rows = name.Split('\n');

            Assert.Equal("Large Shield", rows[0]);
            Assert.False(string.IsNullOrWhiteSpace(rows[1]), name);
        }

        [Fact]
        public void Crafted_and_tempered_render_identically_to_rare()
        {
            ItemIdentity rare = Item("lrg", ItemQualityNo.Rare);
            rare.RarePrefix = 3;
            rare.RareSuffix = 4;

            ItemIdentity craft = Item("lrg", ItemQualityNo.Craft);
            craft.RarePrefix = 3;
            craft.RareSuffix = 4;

            ItemIdentity tempered = Item("lrg", ItemQualityNo.Tempered);
            tempered.RarePrefix = 3;
            tempered.RareSuffix = 4;

            string expected = Names.Build(rare);
            Assert.Equal(expected, Names.Build(craft));
            Assert.Equal(expected, Names.Build(tempered));
        }

        [Fact]
        public void A_unique_item_names_its_uniqueitems_row_under_the_base_name()
        {
            // UniqueItems row 0 is The Gnasher (a hand axe).
            string uniqueName = TxtKeysProbe(Data.UniqueItems, 0, "index");

            ItemIdentity item = Item("hax", ItemQualityNo.Unique, 0);
            string name = Names.Build(item);

            string[] rows = name.Split('\n');
            Assert.Equal("Hand Axe", rows[0]);
            Assert.Equal(uniqueName, rows[1]);
        }

        [Fact]
        public void A_set_item_names_its_setitems_row_under_the_base_name()
        {
            string setName = TxtKeysProbe(Data.SetItems, 0, "index");

            ItemIdentity item = Item("lrg", ItemQualityNo.Set, 0);
            string name = Names.Build(item);

            Assert.NotNull(name);
            Assert.Contains(setName, name, StringComparison.Ordinal);
            Assert.StartsWith("Large Shield\n", name, StringComparison.Ordinal);
        }

        private static ItemIdentity Personalized(string code, int quality, int fileIndex = -1)
        {
            ItemIdentity item = Item(code, quality, fileIndex);
            item.Flags |= ItemRecordFlags.Personalized;
            item.PlayerName = "Anya";
            return item;
        }

        [Fact]
        public void A_personalised_normal_item_takes_the_owners_possessive()
        {
            // INV_FormatPlayerNameOnItem 0x484c90 rewrites the whole buffer for quality 1-4.
            Assert.Equal(
                "Anya's Large Shield",
                Names.Build(Personalized("lrg", ItemQualityNo.Normal)));
        }

        [Fact]
        public void A_personalised_magic_item_takes_the_owners_possessive()
        {
            ItemIdentity item = Personalized("lrg", ItemQualityNo.Magic);

            Assert.StartsWith("Anya's ", Names.Build(item), StringComparison.Ordinal);
        }

        [Fact]
        public void An_item_without_the_flag_is_left_alone()
        {
            // 0x484ca9 gates on flag 0x1000000, so a stray player name is not enough.
            ItemIdentity item = Item("lrg", ItemQualityNo.Normal);
            item.PlayerName = "Anya";

            Assert.Equal("Large Shield", Names.Build(item));
        }

        [Fact]
        public void A_personalised_unique_names_only_the_unique_line()
        {
            // 0x484cb8 skips quality 5-9 in the tail; the unique arm personalises its own line
            // through INV_FormatPlayerNameWithBase at 0x48c9e1 instead.
            string uniqueName = TxtKeysProbe(Data.UniqueItems, 0, "index");

            string[] rows = Names.Build(Personalized("hax", ItemQualityNo.Unique, 0)).Split('\n');

            Assert.Equal("Hand Axe", rows[0]);
            Assert.Equal("Anya's " + uniqueName, rows[1]);
        }

        [Fact]
        public void A_personalised_set_item_replaces_the_10089_wrapper()
        {
            // 0x48cae3: the possessive text is used INSTEAD of the format, not inside it.
            string setName = TxtKeysProbe(Data.SetItems, 0, "index");

            string[] rows = Names.Build(Personalized("lrg", ItemQualityNo.Set, 0)).Split('\n');

            Assert.Equal("Large Shield", rows[0]);
            Assert.Equal("Anya's " + setName, rows[1]);
        }

        [Fact]
        public void A_personalised_rare_item_names_the_affix_line()
        {
            // 0x48c8ea personalises the 1718-formatted affix line, leaving the base name above it.
            ItemIdentity item = Personalized("lrg", ItemQualityNo.Rare);
            item.RarePrefix = 1;
            item.RareSuffix = 2;

            string[] rows = Names.Build(item).Split('\n');

            Assert.Equal("Large Shield", rows[0]);
            Assert.StartsWith("Anya's ", rows[1], StringComparison.Ordinal);
        }

        [Fact]
        public void An_unidentified_personalised_item_still_gets_the_possessive()
        {
            // The unidentified arm reaches the same tail through 0x48ce54.
            ItemIdentity item = Personalized("lrg", ItemQualityNo.Magic);
            item.Flags &= ~ItemRecordFlags.Identified;

            Assert.Equal("Anya's Large Shield", Names.Build(item));
        }

        private static string TxtKeysProbe(TxtFile file, int row, string column)
        {
            string key = file.GetString(row, column);
            return Data.Strings.GetByIndex(Data.Strings.ResolveKey(key));
        }

        // =================================================================
        // Runewords: GetItemName 0x48c060 takes the 0x4000000 arm at 0x48c11a, ahead of the
        // identified test at 0x48c1ea and the quality jump table at 0x48c209.
        // =================================================================

        // Runes.txt row 0 "Runeword1": TXT_AllocTxt_runes 0x639c63 stores the Name column's
        // string id at +0x82, and ITEM_DeserializeFromBitBuffer 0x62d1ea copies it straight into
        // wMagicPrefix[0]. So the slot holds a LOCALE ID, not an affix index.
        private const int AncientsPledgeId = 20507;

        private static ItemIdentity Runeword(string code, int runeStringId, int quality = 2)
        {
            ItemIdentity item = Item(code, quality);
            item.Flags |= ItemRecordFlags.Runeword;
            item.MagicPrefix[0] = runeStringId;
            return item;
        }

        [Fact]
        public void A_runeword_names_itself_in_gold_above_the_base_type()
        {
            Assert.Equal(
                "Crystal Sword\n" + ItemTooltipColor.Marker + "4Ancients' Pledge",
                Names.Build(Runeword("crs", AncientsPledgeId)));
        }

        [Fact]
        public void A_runeword_is_never_gemmed()
        {
            // Before the 0x4000000 arm existed the flag was unread, so a runeword fell through to
            // Normal() and the socket gate renamed it "Gemmed Crystal Sword".
            Assert.DoesNotContain(
                "Gemmed",
                Names.Build(Runeword("crs", AncientsPledgeId), 3),
                StringComparison.Ordinal);
        }

        [Fact]
        public void A_runeword_ignores_its_quality()
        {
            string superior = Names.Build(Runeword("crs", AncientsPledgeId, ItemQualityNo.Superior));

            Assert.DoesNotContain("Superior", superior, StringComparison.Ordinal);
            Assert.Equal(Names.Build(Runeword("crs", AncientsPledgeId)), superior);
        }

        [Fact]
        public void The_runeword_arm_precedes_the_identified_check()
        {
            ItemIdentity item = Runeword("crs", AncientsPledgeId);
            item.Flags &= ~ItemRecordFlags.Identified;

            Assert.Contains("Ancients' Pledge", Names.Build(item), StringComparison.Ordinal);
        }

        [Fact]
        public void The_rune_prefix_resolves_through_GetByIndex_not_the_affix_tables()
        {
            string second = Names.Build(Runeword("crs", AncientsPledgeId)).Split('\n')[1];

            // Strip the marker and its digit, then it must equal the raw locale lookup.
            Assert.Equal(
                Data.Strings.GetByIndex(AncientsPledgeId),
                second.Substring(ItemTooltipColor.Marker.Length + 1));
        }
    }
}
