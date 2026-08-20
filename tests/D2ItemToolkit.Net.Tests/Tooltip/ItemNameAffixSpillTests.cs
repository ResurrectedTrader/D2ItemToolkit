using System;
using Xunit;

namespace D2ItemToolkit.Tests
{
    /// <summary>
    /// The affix-table walks and the SkipName gate of GetItemName 0x48c060.
    /// TXT_RareAffixes_GetLine 0x634260 is the same 1-based concatenation trick as the magic
    /// affixes but over only TWO tables, and neither the spill nor the run off the end had a
    /// test on either side.
    /// </summary>
    public class ItemNameAffixSpillTests
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

        private static ItemIdentity Item(string code, int quality, int fileIndex = -1)
        {
            var item = new ItemIdentity();
            item.ClassId = ClassId(code);
            item.Code = code;
            item.Quality = quality;
            item.FileIndex = fileIndex;
            item.Flags = ItemRecordFlags.Identified;
            return item;
        }

        private static string TxtKeysProbe(TxtFile file, int row, string column)
        {
            string key = file.GetString(row, column);
            return Data.Strings.GetByIndex(Data.Strings.ResolveKey(key));
        }

        [Fact]
        public void Only_the_unique_line_is_shown_when_skipname_is_set()
        {
            // 0x48c9e1: items.txt SkipName suppresses the base-name line entirely. The Horadric
            // Staff is uniqueitems row 125 and its item code `hst` carries SkipName.
            Assert.NotEqual(0, Items.GetInt(ClassId("hst"), "SkipName"));

            string uniqueName = TxtKeysProbe(Data.UniqueItems, 125, "index");
            string name = Names.Build(Item("hst", ItemQualityNo.Unique, 125));

            Assert.Equal(uniqueName, name);
            Assert.DoesNotContain("\n", name, StringComparison.Ordinal);
        }

        [Fact]
        public void The_base_line_survives_for_a_unique_whose_item_does_not_set_skipname()
        {
            // The same arm, one column apart: a Hand Axe has no SkipName.
            Assert.Equal(0, Items.GetInt(ClassId("hax"), "SkipName"));

            Assert.Contains(
                "\n", Names.Build(Item("hax", ItemQualityNo.Unique, 0)), StringComparison.Ordinal);
        }

        [Fact]
        public void An_id_past_the_rare_suffix_table_falls_into_the_rare_prefix_table()
        {
            // 1-based over [RareSuffix][RarePrefix] — 155 suffix rows, so 156 is prefix row 0.
            int suffixRows = Data.RareSuffix.RowCount;
            Assert.Equal(155, suffixRows);

            ItemIdentity rare = Item("lrg", ItemQualityNo.Rare);
            rare.RarePrefix = suffixRows + 1;
            rare.RareSuffix = 0;

            string firstRarePrefix = TxtKeysProbe(Data.RarePrefix, 0, "name");
            Assert.NotEmpty(firstRarePrefix);

            Assert.Contains(
                firstRarePrefix, Names.Build(rare).Split('\n')[1], StringComparison.Ordinal);
        }

        [Fact]
        public void No_rare_affix_is_named_for_an_id_past_both_tables()
        {
            int past = Data.RareSuffix.RowCount + Data.RarePrefix.RowCount + 1;

            ItemIdentity rare = Item("lrg", ItemQualityNo.Rare);
            rare.RarePrefix = past;
            rare.RareSuffix = past;

            string[] rows = Names.Build(rare).Split('\n');

            // The base line stands; the affix line is the bare "%0 %1" with both slots empty.
            Assert.Equal("Large Shield", rows[0]);
            Assert.Equal(string.Empty, rows[1].Trim());
        }

        [Fact]
        public void No_magic_affix_is_named_for_an_id_past_all_three_tables()
        {
            int past = Data.MagicSuffix.RowCount + Data.MagicPrefix.RowCount
                + Data.AutoMagic.RowCount + 1;
            Assert.Equal(1453, past);

            ItemIdentity magic = Item("lrg", ItemQualityNo.Magic);
            magic.MagicPrefix[0] = past;
            magic.MagicSuffix[0] = past;

            // Format 1714 is "%0 %1 %2", so an empty affix pair leaves the base name and spaces.
            Assert.Equal("Large Shield", Names.Build(magic).Trim());
        }

        [Fact]
        public void A_blank_personalised_owner_keeps_the_plain_name()
        {
            // 0x5272f6 returns the base name untouched rather than emitting a bare "'s".
            ItemIdentity blank = Item("lrg", ItemQualityNo.Normal);
            blank.Flags |= ItemRecordFlags.Personalized;
            blank.PlayerName = string.Empty;

            Assert.Equal("Large Shield", Names.Build(blank));
        }
    }
}
