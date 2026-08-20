using System.Collections.Generic;
using Xunit;

namespace D2ItemToolkit.Tests
{
    /// <summary>
    /// Two documents written in EXACTLY the shape ITEMSTATS_StoreUnit emits — every key spelled as the
    /// C++ side spells it, nothing translated by a test helper — driven through the whole pipeline.
    /// This is the compatibility check between producer and consumer.
    /// </summary>
    public class ProducerShapeTests
    {
        private static readonly D2DataFiles Data = D2DataFiles.LoadEmbedded();

        private static readonly ItemTable Items = new ItemTable(
            Data.Weapons, Data.Armor, Data.Misc);

        private static readonly ItemTypeTree Types = new ItemTypeTree(Data.ItemTypes);

        // A Paladin holding a socketed Large Shield with a Perfect Ruby in it. unitType 4 is UNIT_ITEM.
        private const string ItemDoc = @"{
            ""unitType"": 4,
            ""classId"": %LRG%,
            ""code"": ""lrg"",
            ""quality"": 2,
            ""itemFlags"": 2064,
            ""format"": 100,
            ""fileIndex"": -1,
            ""rarePrefix"": 0,
            ""rareSuffix"": 0,
            ""autoAffix"": 0,
            ""magicPrefix"": [ 0, 0, 0 ],
            ""magicSuffix"": [ 0, 0, 0 ],
            ""earLevel"": 0,
            ""playerName"": """",
            ""statsLists"": [
              { ""source"": ""base"", ""stateNo"": 0, ""flags"": 2147483648,
                ""stats"": [ { ""id"": 31, ""value"": 120 }, { ""id"": 72, ""value"": 40 },
                             { ""id"": 73, ""value"": 62 }, { ""id"": 194, ""value"": 1 },
                             { ""id"": 20, ""value"": 25 } ] }
            ],
            ""sockets"": [
              { ""unitType"": 4, ""classId"": %GPR%, ""code"": ""gpr"", ""quality"": 2,
                ""itemFlags"": 16, ""format"": 100, ""fileIndex"": -1,
                ""magicPrefix"": [ 0, 0, 0 ], ""magicSuffix"": [ 0, 0, 0 ],
                ""statsLists"": [
                  { ""source"": ""quality"", ""stateNo"": 0, ""flags"": 64,
                    ""stats"": [ { ""id"": 39, ""value"": 40 } ] }
                ] }
            ]
        }";

        // unitType 0 is UNIT_PLAYER. Attributes are stats; the skill level is a skills entry.
        private const string PlayerDoc = @"{
            ""unitType"": 0,
            ""classId"": 3,
            ""flagsEx"": 33554432,
            ""name"": ""Bob"",
            ""skills"": [ { ""skill"": 117, ""level"": 20 } ],
            ""statsLists"": [
              { ""source"": ""base"", ""stateNo"": 0, ""flags"": 0,
                ""stats"": [ { ""id"": 12, ""value"": 60 }, { ""id"": 0, ""value"": 120 },
                             { ""id"": 2, ""value"": 90 } ] },
              { ""source"": ""other"", ""stateNo"": 101, ""flags"": 0,
                ""stats"": [ { ""id"": 20, ""value"": 35 } ] }
            ]
        }";

        private static string Render()
        {
            Unit item = Unit.FromJson(
                ItemDoc.Replace("%LRG%", Items.ClassIdForCode("lrg").ToString())
                       .Replace("%GPR%", Items.ClassIdForCode("gpr").ToString()));

            Unit player = Unit.FromJson(PlayerDoc);

            ItemIdentity identity = ItemRecordReader.ReadIdentity(item);
            ItemViewer viewer = ItemRecordReader.ReadViewer(player);

            var sections = new RecordSections(
                Data, Items, Types, identity, viewer,
                ItemStatReader.ReconstructView(item, ItemStatView.Equipped()),
                ItemStatReader.ReadSockets(item),
                ItemStatReader.ReconstructView(item, ItemStatView.BaseOnly()),
                ItemRecordReader.ReadSocketUnits(item));

            SortedDictionary<int, int> modifierStats =
                ItemStatReader.ReconstructView(item, ItemStatView.Modifiers());

            var composer = new ItemTooltipComposer(
                sections, sections.CreateModifierGenerator(modifierStats));

            IReadOnlyList<ItemTooltipLine> lines =
                composer.Compose(sections.CreateContext(), modifierStats);

            return composer.Render(lines);
        }

        [Fact]
        public void The_producers_own_shape_reconstructs_a_full_description()
        {
            string text = Render();

            Assert.False(string.IsNullOrEmpty(text));

            // From the item's identity and items.txt.
            Assert.Contains("Large Shield", text, System.StringComparison.Ordinal);

            // Merged from the base list plus the socketed ruby's 40.
            Assert.Contains("Defense: 120", text, System.StringComparison.Ordinal);
            Assert.Contains("Durability: 40 of 62", text, System.StringComparison.Ordinal);
            Assert.Contains("Socketed (1)", text, System.StringComparison.Ordinal);
        }

        [Fact]
        public void The_player_document_drives_every_viewer_dependent_line()
        {
            string text = Render();

            // Smite needs classId 3 read off the player document.
            Assert.Contains("Smite Damage", text, System.StringComparison.Ordinal);

            // Block chance = item stat 20 (25) + charstats BlockFactor + Holy Shield at level 20,
            // capped at 75. Holy Shield is active because a stat list carries stateNo 101, and its
            // level comes from the skills array — neither is a bespoke field.
            Assert.Contains("Chance to Block", text, System.StringComparison.Ordinal);

            ItemViewer viewer = ItemRecordReader.ReadViewer(
                Unit.FromJson(PlayerDoc));

            Assert.Equal(60, viewer.Level);
            Assert.Equal(120, viewer.Strength);
            Assert.Equal(90, viewer.Dexterity);
            Assert.True(viewer.IsExpansion);
            Assert.Equal(20, viewer.SkillLevel(SkillDamage.HolyShieldSkillId));
            Assert.Contains(SkillDamage.HolyShieldState, viewer.ActiveStates);
        }

        [Fact]
        public void The_socket_reconstructs_as_an_item_in_its_own_right()
        {
            Unit item = Unit.FromJson(
                ItemDoc.Replace("%LRG%", Items.ClassIdForCode("lrg").ToString())
                       .Replace("%GPR%", Items.ClassIdForCode("gpr").ToString()));

            foreach (IUnit socket in ItemStatReader.EnumerateSockets(item))
            {
                ItemIdentity filler = ItemRecordReader.ReadIdentity(socket);

                Assert.Equal("gpr", filler.Code);
                Assert.Equal(Items.ClassIdForCode("gpr"), filler.ClassId);
                Assert.True(filler.Has(ItemRecordFlags.Identified));

                // And the same section builder describes it, because it is just an item.
                var sections = new RecordSections(
                    Data, Items, Types, filler, null,
                    ItemStatReader.ReconstructView(socket, ItemStatView.Equipped()),
                    null, null, null);

                Assert.Contains(
                    "Fire Resist",
                    sections.GetSection(ItemTooltipSection.SocketFillerDescription),
                    System.StringComparison.Ordinal);
            }
        }

        [Fact]
        public void A_minus_one_fileIndex_arrives_as_the_unsigned_dword_the_producer_writes()
        {
            // dwFileIndex is a DWORD. "No row" is -1, which nlohmann emits as 4294967295, and an
            // `int` property rejects that outright. The narrowing restores the exact 32 bits.
            //
            // The adversarial corpus found this: it was the one case where the C# reader threw and
            // the TypeScript reader rendered, and the value is producer-legal, not malformed.
            Unit wide = Unit.FromJson(
                @"{ ""unitType"": 4, ""classId"": 330, ""quality"": 7, ""itemFlags"": 16,
                    ""fileIndex"": 4294967295, ""statsLists"": [] }");

            Assert.Equal(-1, wide.FileIndex);

            Unit signed = Unit.FromJson(
                @"{ ""unitType"": 4, ""classId"": 330, ""fileIndex"": -1 }");

            Assert.Equal(signed.FileIndex, wide.FileIndex);
        }
    }
}
