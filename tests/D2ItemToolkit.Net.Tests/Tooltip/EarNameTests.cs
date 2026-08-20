using Xunit;

namespace D2ItemToolkit.Tests
{
    /// <summary>
    /// The quality-2 arms of GetItemName 0x48c060 that are not the plain base name: the ear at
    /// 0x48c2b3 and the tome/scroll split at 0x48c542.
    /// </summary>
    public class EarNameTests
    {
        private static readonly D2DataFiles Data = D2DataFiles.LoadEmbedded();

        private static readonly ItemTable Items = new ItemTable(
            Data.Weapons, Data.Armor, Data.Misc);

        private static readonly ItemNameBuilder Names = new ItemNameBuilder(
            Data, Items, new ItemTypeTree(Data.ItemTypes));

        private static ItemIdentity Item(string code)
        {
            var item = new ItemIdentity();
            item.ClassId = Items.ClassIdForCode(code);
            item.Code = code;
            item.Quality = ItemQualityNo.Normal;
            item.Flags = ItemRecordFlags.Identified;
            Assert.True(item.ClassId >= 0, code);
            return item;
        }

        [Fact]
        public void An_ear_names_its_owner_class_and_level()
        {
            ItemIdentity ear = Item("ear");
            ear.PlayerName = "Bob";
            ear.EarLevel = 42;
            ear.FileIndex = 4;      // Barbarian

            string[] lines = Names.Build(ear).Split('\n');

            // Appended top-to-bottom; the renderer reverses, so the possessive shows first in game.
            Assert.Equal(3, lines.Length);
            Assert.StartsWith("Level", lines[0], System.StringComparison.Ordinal);
            Assert.Contains("42", lines[0], System.StringComparison.Ordinal);
            Assert.Equal("Barbarian", lines[1]);
            Assert.Equal("Bob's Ear", lines[2]);
        }

        [Theory]
        [InlineData(0, "Amazon")]
        [InlineData(1, "Sorceress")]
        [InlineData(2, "Necromancer")]
        [InlineData(3, "Paladin")]
        [InlineData(4, "Barbarian")]
        [InlineData(5, "Druid")]
        [InlineData(6, "Assassin")]
        public void The_ear_file_index_is_the_dead_players_class(int fileIndex, string expected)
        {
            ItemIdentity ear = Item("ear");
            ear.PlayerName = "X";
            ear.FileIndex = fileIndex;

            Assert.Contains("\n" + expected + "\n", Names.Build(ear), System.StringComparison.Ordinal);
        }

        [Fact]
        public void A_class_index_past_the_table_writes_no_class_line()
        {
            // 0x484a70 HALTS the game at 7 or above; we omit the line rather than crash.
            ItemIdentity ear = Item("ear");
            ear.PlayerName = "X";
            ear.FileIndex = 7;

            Assert.Equal(2, Names.Build(ear).Split('\n').Length);
        }

        [Fact]
        public void The_named_flag_adds_a_line_above_everything()
        {
            ItemIdentity ear = Item("ear");
            ear.PlayerName = "Bob";
            ear.FileIndex = 3;
            ear.Flags |= ItemRecordFlags.Named;

            string[] lines = Names.Build(ear).Split('\n');

            Assert.Equal(4, lines.Length);
            Assert.Equal("Bob's Ear", lines[3]);
        }

        [Fact]
        public void An_over_long_owner_name_drops_the_possessive()
        {
            // 0x5272e1: base + owner + 5 over the caller's 100 wide characters falls back to the base.
            ItemIdentity ear = Item("ear");
            ear.PlayerName = new string('x', 120);
            ear.FileIndex = 3;

            string[] lines = Names.Build(ear).Split('\n');

            Assert.Equal("Ear", lines[lines.Length - 1]);
        }

        [Theory]
        // 2199/2201 are the tome pair and 2200/2202 the scroll pair; the suffix picks which spell.
        [InlineData("tbk", 0)]
        [InlineData("tbk", 1)]
        [InlineData("tsc", 0)]
        [InlineData("tsc", 1)]
        public void A_tome_and_a_scroll_name_their_spell(string code, int suffix)
        {
            ItemIdentity item = Item(code);
            item.MagicSuffix[0] = suffix;

            Assert.False(string.IsNullOrEmpty(Names.Build(item)), code + " suffix " + suffix);
        }

        [Fact]
        public void A_tome_and_a_scroll_of_the_same_spell_differ()
        {
            ItemIdentity tome = Item("tbk");
            ItemIdentity scroll = Item("tsc");

            Assert.NotEqual(Names.Build(tome), Names.Build(scroll));
        }

        [Fact]
        public void A_monster_body_part_names_the_creature_it_came_from()
        {
            // fileIndex on a body part is a monstats row, and the part's own base name resolves
            // through namestr — "Heart" for hrt, whatever the misc.txt `name` column calls it.
            ItemIdentity part = Item("hrt");
            part.FileIndex = 0;

            string monster = Data.MonsterTypes.GetMonsterName(0);
            Assert.False(string.IsNullOrEmpty(monster));

            Assert.Contains(monster, Names.Build(part), System.StringComparison.Ordinal);
        }

        [Fact]
        public void A_body_part_with_no_monster_row_falls_back_to_the_base_name()
        {
            ItemIdentity part = Item("hrt");
            part.FileIndex = -1;

            Assert.Equal("Heart", Names.Build(part));
        }

        [Fact]
        public void A_magic_suffix_above_one_names_nothing()
        {
            ItemIdentity item = Item("tsc");
            item.MagicSuffix[0] = 2;

            Assert.Null(Names.Build(item));
        }
    }
}
