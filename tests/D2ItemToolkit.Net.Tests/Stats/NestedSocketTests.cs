using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace D2ItemToolkit.Tests
{
    /// <summary>
    /// A record is self-similar: a socket entry is another record, and its POSITION in the array is
    /// the socket index.
    /// </summary>
    public class NestedSocketTests
    {
        private const string Nested = @"{
            
            ""classId"": 100, ""quality"": 2, ""itemFlags"": 16,
            ""statsLists"": [
              { ""stateNo"": 0, ""flags"": 2147483648,
                ""stats"": [ { ""id"": 31, ""value"": 100 } ] }
            ],
            ""items"": [
              { ""classId"": 620, ""quality"": 2,
                ""statsLists"": [ { ""source"": ""quality"", ""stateNo"": 0, ""flags"": 64,
                    ""stats"": [ { ""id"": 39, ""value"": 10 } ] } ] },
              { ""classId"": 604, ""quality"": 2,
                ""statsLists"": [ { ""source"": ""quality"", ""stateNo"": 0, ""flags"": 64,
                    ""stats"": [ { ""id"": 39, ""value"": 20 } ] } ] }
            ]
        }";

        private static Unit Root
        {
            get { return Unit.FromJson(Nested); }
        }

        [Fact]
        public void Array_position_is_the_socket_index()
        {
            SortedDictionary<int, uint> sockets = ItemStatReader.ReadSockets(Root);

            Assert.Equal(new[] { 0, 1 }, sockets.Keys.ToArray());
            Assert.Equal(620u, sockets[0]);
            Assert.Equal(604u, sockets[1]);
        }

        [Fact]
        public void A_socket_entry_is_a_record_of_the_same_shape()
        {
            // The same reader works on a socket as on the root, which is the point of the fold.
            IUnit filler = Root.Items.First();

            ItemIdentity identity = ItemRecordReader.ReadIdentity(filler);
            Assert.NotNull(identity);
            Assert.Equal(620, identity.ClassId);

            Assert.Single(ItemStatReader.EnumerateGroups(filler));
        }

        [Fact]
        public void The_equipped_view_folds_every_socket_in()
        {
            SortedDictionary<int, int> equipped =
                ItemStatReader.ReconstructView(Root, ItemStatView.Equipped());

            // 10 + 20 from the two fillers.
            Assert.Equal(30, equipped[ItemStatReader.PackStatKey(0, 39)]);
            Assert.Equal(100, equipped[ItemStatReader.PackStatKey(0, 31)]);
        }

        [Fact]
        public void The_item_only_view_excludes_every_socket()
        {
            SortedDictionary<int, int> itemOnly =
                ItemStatReader.ReconstructView(Root, ItemStatView.ItemOnly());

            Assert.False(itemOnly.ContainsKey(ItemStatReader.PackStatKey(0, 39)));
            Assert.Equal(100, itemOnly[ItemStatReader.PackStatKey(0, 31)]);
        }

        [Theory]
        [InlineData(0, 10)]
        [InlineData(1, 20)]
        public void A_filler_at_a_position_describes_from_its_own_record(int socket, int expected)
        {
            // Position in `sockets` IS the socket index, so a per-socket view is unnecessary:
            // take the entry and run the reader on it.
            IUnit filler = Root.Items.ElementAt(socket);

            SortedDictionary<int, int> view =
                ItemStatReader.ReconstructView(filler, ItemStatView.ItemOnly());

            Assert.Equal(expected, view[ItemStatReader.PackStatKey(0, 39)]);
            Assert.False(view.ContainsKey(ItemStatReader.PackStatKey(0, 31)));
        }

        [Fact]
        public void A_group_records_whether_it_came_through_a_socket()
        {
            List<ItemStatGroup> groups = ItemStatReader.EnumerateGroups(Root).ToList();

            Assert.Equal(3, groups.Count);
            Assert.False(groups[0].FromSocket);
            Assert.True(groups[1].FromSocket);
            Assert.True(groups[2].FromSocket);
        }

        [Fact]
        public void An_item_with_no_sockets_omits_the_array_entirely()
        {
            Unit bare = Unit.FromJson(
                @"{ ""classId"": 1, ""statsLists"": [] }");

            Assert.Empty(bare.Items);
            Assert.Empty(ItemStatReader.ReadSockets(bare));
        }
    }
}
