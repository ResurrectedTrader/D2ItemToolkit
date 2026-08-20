using Xunit;

namespace D2ItemToolkit.Tests
{
    public class ItemRecordTests
    {
        private const string Item = @"{
            ""unitType"": 4,
            ""classId"": 511, ""code"": ""rin"", ""quality"": 7, ""itemFlags"": 4194320,
            ""fileIndex"": 25, ""rarePrefix"": 0, ""rareSuffix"": 0, ""autoAffix"": 0,
            ""magicPrefix"": [ 12, 0, 0 ], ""magicSuffix"": [ 34, 0, 0 ],
            ""earLevel"": 0, ""playerName"": ""Bob"",
            ""statsLists"": [
              { ""stateNo"": 0, ""flags"": 2147483648,
                ""stats"": [ { ""id"": 31, ""value"": 445 } ] }
            ]
        }";

        private const string Player = @"{
            ""unitType"": 0, ""classId"": 1, ""skills"": [ { ""skill"": 117, ""level"": 12 } ],
            ""statsLists"": [
              { ""stateNo"": 0, ""flags"": 2147483648,
                ""stats"": [ { ""id"": 12, ""value"": 42 }, { ""id"": 0, ""value"": 88 },
                              { ""id"": 2, ""value"": 55 } ] },
              { ""stateNo"": 101, ""flags"": 64,
                ""stats"": [ { ""id"": 20, ""value"": 30 } ] }
            ]
        }";
        private static Unit Parse(string json)
        {
            return Unit.FromJson(json);
        }

        [Fact]
        public void The_item_object_round_trips()
        {
            ItemIdentity item = ItemRecordReader.ReadIdentity(Parse(Item));

            Assert.Equal(511, item.ClassId);
            Assert.Equal("rin", item.Code);
            Assert.Equal(7, item.Quality);
            Assert.Equal(25, item.FileIndex);
            Assert.Equal(new[] { 12, 0, 0 }, item.MagicPrefix);
            Assert.Equal(new[] { 34, 0, 0 }, item.MagicSuffix);
            Assert.Equal("Bob", item.PlayerName);
        }

        [Fact]
        public void Item_flags_decode_to_the_engines_bits()
        {
            ItemIdentity item = ItemRecordReader.ReadIdentity(Parse(Item));

            // 4194320 = 0x400010 = ETHEREAL | IDENTIFIED.
            Assert.True(item.Has(ItemRecordFlags.Identified));
            Assert.True(item.Has(ItemRecordFlags.Ethereal));
            Assert.False(item.Has(ItemRecordFlags.Socketed));
            Assert.False(item.Has(ItemRecordFlags.Runeword));
            Assert.False(item.Has(ItemRecordFlags.Personalized));
        }

        [Fact]
        public void A_monster_viewer_is_not_a_player()
        {
            Unit record = Parse(@"{ ""unitType"": 1, ""classId"": 3, ""statsLists"": [] }");

            ItemViewer viewer = ItemRecordReader.ReadViewer(record);

            // Class id 3 is Paladin for a player, but this is a monster — the Smite gate must not
            // fire on it, which is the bug LoadItemDesc has at 0x48e75c.
            Assert.Equal(3, viewer.ClassId);
            Assert.False(viewer.IsPlayer);
        }

        [Fact]
        public void The_viewer_derives_its_attributes_from_its_own_stat_lists()
        {
            // Level, strength and dexterity are stats 12, 0 and 2 — no special fields.
            ItemViewer viewer = ItemRecordReader.ReadViewer(Parse(Player));

            Assert.True(viewer.IsPlayer);
            Assert.Equal(1, viewer.ClassId);
            Assert.Equal(42, viewer.Level);
            Assert.Equal(88, viewer.Strength);
            Assert.Equal(55, viewer.Dexterity);
        }

        [Fact]
        public void Holy_shield_being_up_is_derived_from_the_state_on_a_stat_list()
        {
            // A state IS a stat list carrying its dwStateNo; 101 is Holy Shield's.
            Assert.Contains(
                SkillDamage.HolyShieldState,
                ItemRecordReader.ReadViewer(Parse(Player)).ActiveStates);
            Assert.Equal(
                12,
                ItemRecordReader.ReadViewer(Parse(Player)).SkillLevel(SkillDamage.HolyShieldSkillId));

            Unit noState = Parse(
                @"{ ""unitType"": 0, ""classId"": 3, ""skills"": [ { ""skill"": 117, ""level"": 12 } ], ""statsLists"": [] }");
            Assert.DoesNotContain(
                SkillDamage.HolyShieldState,
                ItemRecordReader.ReadViewer(noState).ActiveStates);
        }

        [Fact]
        public void The_stat_lists_read_off_the_flattened_document()
        {
            var stats = ItemStatReader.ReconstructView(Parse(Item), ItemStatView.ItemOnly());

            Assert.Single(stats);
            Assert.Equal(445, stats[ItemStatReader.PackStatKey(0, 31)]);
        }
    }
}
