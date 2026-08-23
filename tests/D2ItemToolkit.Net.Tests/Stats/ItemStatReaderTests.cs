using System.Collections.Generic;
using System.Text.Json;
using System.Linq;
using Xunit;

namespace D2ItemToolkit.Tests
{
    public class ItemStatReaderTests
    {
        // A 2 socket set armour with a jewel and a perfect ruby, set not completed.
        private const string SampleRecord = @"{
            
            ""statsLists"": [
              { ""source"": ""base"", ""stateNo"": 0, ""flags"": 2147483648,
                ""stats"": [ { ""id"": 31, ""value"": 445 }, { ""id"": 72, ""value"": 60 },
                             { ""id"": 73, ""value"": 60 }, { ""id"": 194, ""value"": 2 } ] },
              { ""source"": ""quality"", ""stateNo"": 0, ""flags"": 64,
                ""stats"": [ { ""id"": 16, ""value"": 180 }, { ""id"": 39, ""value"": 40 } ] },
              { ""source"": ""setBonus"", ""stateNo"": 165, ""flags"": 8256,
                ""stats"": [ { ""id"": 0, ""value"": 20 } ] },
              { ""source"": ""setBonus"", ""stateNo"": 166, ""flags"": 8256,
                ""stats"": [ { ""id"": 41, ""value"": 15 } ] }
            ],
            ""items"": [
              { ""classId"": 620,
                ""statsLists"": [ { ""source"": ""quality"", ""stateNo"": 0, ""flags"": 64,
                    ""stats"": [ { ""id"": 17, ""value"": 15 },
                                 { ""id"": 97, ""layer"": 2, ""value"": 1 } ] } ] },
              { ""classId"": 604,
                ""statsLists"": [ { ""source"": ""quality"", ""stateNo"": 0, ""flags"": 64,
                    ""stats"": [ { ""id"": 39, ""value"": 38 } ] } ] }
            ]
        }";

        private static Unit Parse(string json)
        {
            return Unit.FromJson(json);
        }

        private static string Render(SortedDictionary<int, int> view)
        {
            return string.Join(" ", view.Select(s =>
            {
                int layer = ItemStatReader.LayerFromKey(s.Key);
                string id = ItemStatReader.StatFromKey(s.Key).ToString();
                return layer != 0 ? id + "/" + layer + "=" + s.Value : id + "=" + s.Value;
            }));
        }

        // =================================================================
        // Key packing
        // =================================================================

        [Theory]
        [InlineData(0, 39)]
        [InlineData(2, 97)]
        [InlineData(0xFFFF, 0xFFFF)]
        public void A_packed_key_round_trips(int layer, int stat)
        {
            int key = ItemStatReader.PackStatKey(layer, stat);

            Assert.Equal(stat, ItemStatReader.StatFromKey(key));
            Assert.Equal(layer, ItemStatReader.LayerFromKey(key));
        }

        [Fact]
        public void A_high_layer_packs_to_a_negative_key_without_losing_bits()
        {
            // Matches int32_t(uint32_t(layer) << 16 | stat) on the C++ side.
            int key = ItemStatReader.PackStatKey(0x8000, 1);

            Assert.True(key < 0);
            Assert.Equal(0x8000, ItemStatReader.LayerFromKey(key));
            Assert.Equal(1, ItemStatReader.StatFromKey(key));
        }

        [Fact]
        public void Packing_masks_off_bits_above_the_field_widths()
        {
            int key = ItemStatReader.PackStatKey(0x1FFFF, 0x1FFFF);

            Assert.Equal(0xFFFF, ItemStatReader.LayerFromKey(key));
            Assert.Equal(0xFFFF, ItemStatReader.StatFromKey(key));
        }

        // =================================================================
        // Views
        // =================================================================

        // =================================================================
        // The modifier block: GetStatList(item, 0, 0x40), GetStatList(item, 171, 0x40) and
        // GetStatList(filler, 0, 0x40) — 0x4e6438 / 0x4e6137 / 0x4e6154 / 0x4e61a0.
        // =================================================================

        [Fact]
        public void The_modifier_block_never_sees_the_base_array()
        {
            Unit doc = Parse(SampleRecord);
            {
                SortedDictionary<int, int> view =
                    ItemStatReader.ReconstructView(doc, ItemStatView.Modifiers());

                // 31 defense, 72/73 durability and 194 sockets are all base stats. The base array
                // hangs off +0x24 and is not in the +0x3C chain GetStatList walks, so none of them
                // can ever be described — only the quality node and the two fillers survive.
                Assert.Equal("16=180 17=15 39=78 97/2=1", Render(view));
            }
        }

        [Theory]
        [InlineData(31)]
        [InlineData(72)]
        [InlineData(73)]
        [InlineData(194)]
        public void A_base_stat_is_absent_from_the_modifier_block(int statId)
        {
            Unit doc = Parse(SampleRecord);
            {
                SortedDictionary<int, int> view =
                    ItemStatReader.ReconstructView(doc, ItemStatView.Modifiers());

                Assert.False(view.ContainsKey(ItemStatReader.PackStatKey(0, statId)));
            }
        }

        [Fact]
        public void A_set_bonus_carries_the_flag_but_is_excluded_by_its_state()
        {
            // Set nodes are flags 0x2040 — they DO have the 0x40 bit. What keeps them out is
            // stateNo: STATE_ITEMSET1..6 are 165-170 and neither query asks for those. Earning a
            // tier clears STATLIST_SET, leaving 0x40, so this holds for both.
            string earned = SampleRecord.Replace(@"""flags"": 8256", @"""flags"": 64");

            Unit doc = Parse(earned);
            {
                SortedDictionary<int, int> view =
                    ItemStatReader.ReconstructView(doc, ItemStatView.Modifiers());

                Assert.False(view.ContainsKey(ItemStatReader.PackStatKey(0, 0)));
                Assert.False(view.ContainsKey(ItemStatReader.PackStatKey(0, 41)));
            }
        }

        [Fact]
        public void The_runeword_node_is_the_second_query()
        {
            string runeword = SampleRecord.Replace(
                @"{ ""source"": ""setBonus"", ""stateNo"": 165, ""flags"": 8256,
                ""stats"": [ { ""id"": 0, ""value"": 20 } ] }",
                @"{ ""source"": ""runeword"", ""stateNo"": 171, ""flags"": 64,
                ""stats"": [ { ""id"": 0, ""value"": 20 } ] }");

            Unit doc = Parse(runeword);
            {
                SortedDictionary<int, int> view =
                    ItemStatReader.ReconstructView(doc, ItemStatView.Modifiers());

                Assert.Equal(20, view[ItemStatReader.PackStatKey(0, 0)]);
            }
        }

        [Fact]
        public void A_flagged_node_on_an_unqueried_state_is_still_excluded()
        {
            // Only 0 and 171 are asked for, so the 0x40 bit alone is not enough.
            string other = SampleRecord.Replace(@"""stateNo"": 171", @"""stateNo"": 200")
                .Replace(
                    @"{ ""source"": ""quality"", ""stateNo"": 0, ""flags"": 64,
                ""stats"": [ { ""id"": 16, ""value"": 180 }, { ""id"": 39, ""value"": 40 } ] }",
                    @"{ ""source"": ""quality"", ""stateNo"": 200, ""flags"": 64,
                ""stats"": [ { ""id"": 16, ""value"": 180 }, { ""id"": 39, ""value"": 40 } ] }");

            Unit doc = Parse(other);
            {
                SortedDictionary<int, int> view =
                    ItemStatReader.ReconstructView(doc, ItemStatView.Modifiers());

                Assert.False(view.ContainsKey(ItemStatReader.PackStatKey(0, 16)));
            }
        }

        [Fact]
        public void Socket_fillers_still_reach_the_modifier_block()
        {
            Unit doc = Parse(SampleRecord);
            {
                SortedDictionary<int, int> view =
                    ItemStatReader.ReconstructView(doc, ItemStatView.Modifiers());

                // 15 from the jewel, and 40 + 38 for fire resist across item and ruby.
                Assert.Equal(15, view[ItemStatReader.PackStatKey(0, 17)]);
                Assert.Equal(78, view[ItemStatReader.PackStatKey(0, 39)]);
            }
        }

        [Fact]
        public void The_section_views_still_carry_the_base_stats()
        {
            // The writers read through SERVER_GetUnitStat, which sees every list — only the
            // modifier block is restricted.
            Unit doc = Parse(SampleRecord);
            {
                SortedDictionary<int, int> sections =
                    ItemStatReader.ReconstructView(doc, ItemStatView.Equipped());

                Assert.Equal(445, sections[ItemStatReader.PackStatKey(0, 31)]);
            }
        }

        [Fact]
        public void The_describe_scope_and_the_unit_scope_are_separate()
        {
            // GetBaseStatValue is the temp list (the damage aggregate and the 23/24 suppression
            // read it); GetItemStatValue is the unit, which is what the never-breaks gate and
            // GetTxtMaxDurability 0x625e00 ask. Feeding one dictionary to both over-describes.
            var describe = new Dictionary<int, int>
            {
                { ItemStatReader.PackStatKey(0, 39), 25 },
            };

            var unit = new Dictionary<int, int>
            {
                { ItemStatReader.PackStatKey(0, 39), 25 },
                { ItemStatReader.PackStatKey(0, 73), 62 },
            };

            var values = new SynthesisedStatValues(describe, null, null, null, null, unit);

            Assert.Equal(0, values.GetBaseStatValue(73, 0));
            Assert.Equal(62, values.GetItemStatValue(73));
            Assert.Equal(62, values.GetTxtMaxDurability());
            Assert.Equal(25, values.GetBaseStatValue(39, 0));
        }

        [Fact]
        public void One_dictionary_still_serves_both_scopes_when_no_unit_set_is_given()
        {
            var stats = new Dictionary<int, int>
            {
                { ItemStatReader.PackStatKey(0, 73), 62 },
            };

            var values = new SynthesisedStatValues(stats, null, null, null, null);

            Assert.Equal(62, values.GetBaseStatValue(73, 0));
            Assert.Equal(62, values.GetItemStatValue(73));
        }

        [Fact]
        public void ForSale_sums_the_item_and_its_sockets_and_omits_set_bonuses()
        {
            Unit doc = Parse(SampleRecord);
            {
                SortedDictionary<int, int> view =
                    ItemStatReader.ReconstructView(doc, ItemStatView.ForSale());

                // Fire resist is 40 on the item plus 38 from the ruby.
                Assert.Equal("16=180 17=15 31=445 39=78 72=60 73=60 194=2 97/2=1", Render(view));
            }
        }

        [Fact]
        public void Equipped_matches_ForSale_while_every_set_tier_is_unearned()
        {
            Unit doc = Parse(SampleRecord);
            {
                Assert.Equal(
                    Render(ItemStatReader.ReconstructView(doc, ItemStatView.ForSale())),
                    Render(ItemStatReader.ReconstructView(doc, ItemStatView.Equipped())));
            }
        }

        [Fact]
        public void Equipped_includes_a_set_bonus_once_it_is_earned()
        {
            // Earning a tier clears STATLIST_SET, so 0x2040 becomes 0x40.
            string earned = SampleRecord.Replace(@"""flags"": 8256", @"""flags"": 64");

            Unit doc = Parse(earned);
            {
                SortedDictionary<int, int> view =
                    ItemStatReader.ReconstructView(doc, ItemStatView.Equipped());

                Assert.Equal(20, view[ItemStatReader.PackStatKey(0, 0)]);
                Assert.Equal(15, view[ItemStatReader.PackStatKey(0, 41)]);
            }
        }

        [Fact]
        public void ItemOnly_drops_the_socket_contributions()
        {
            Unit doc = Parse(SampleRecord);
            {
                SortedDictionary<int, int> view =
                    ItemStatReader.ReconstructView(doc, ItemStatView.ItemOnly());

                Assert.Equal("16=180 31=445 39=40 72=60 73=60 194=2", Render(view));
            }
        }

        [Fact]
        public void SetBonuses_excludes_unearned_tiers_by_default()
        {
            Unit doc = Parse(SampleRecord);
            {
                Assert.Empty(ItemStatReader.ReconstructView(doc, ItemStatView.SetBonuses(false)));
            }
        }

        [Fact]
        public void SetBonuses_can_include_unearned_tiers()
        {
            Unit doc = Parse(SampleRecord);
            {
                SortedDictionary<int, int> view =
                    ItemStatReader.ReconstructView(doc, ItemStatView.SetBonuses(true));

                Assert.Equal("0=20 41=15", Render(view));
            }
        }

        [Theory]
        [InlineData(0, "17=15 97/2=1")]
        [InlineData(1, "39=38")]
        public void A_filler_describes_from_its_own_record(int socket, string expected)
        {
            // No per-socket view exists: a filler is a record of the same shape, so the reader
            // already works on it directly.
            Unit doc = Parse(SampleRecord);
            {
                IUnit filler =
                    doc.Items.ElementAt(socket);

                Assert.Equal(expected,
                    Render(ItemStatReader.ReconstructView(filler, ItemStatView.ItemOnly())));
            }
        }

        [Fact]
        public void Everything_round_trips_the_whole_record()
        {
            Unit doc = Parse(SampleRecord);
            {
                SortedDictionary<int, int> view =
                    ItemStatReader.ReconstructView(doc, ItemStatView.Everything());

                Assert.Equal("0=20 16=180 17=15 31=445 39=78 41=15 72=60 73=60 194=2 97/2=1", Render(view));
            }
        }

        [Fact]
        public void An_unknown_source_is_only_included_by_a_mask_that_asks_for_Other()
        {
            const string json = @"{""statsLists"":[
                {""source"":""charm"",""stateNo"":0,""flags"":0,""stats"":[{""id"":1,""value"":5}]}]}";

            Unit doc = Parse(json);
            {
                Assert.Empty(ItemStatReader.ReconstructView(doc, ItemStatView.ForSale()));
                Assert.Single(ItemStatReader.ReconstructView(doc, ItemStatView.Everything()));
            }
        }

        [Fact]
        public void A_record_with_no_groups_array_yields_nothing()
        {
            Unit doc = Parse(@"{}");
            {
                Assert.Empty(ItemStatReader.EnumerateGroups(doc));
            }
        }

        // =================================================================
        // Sockets
        // =================================================================

        [Fact]
        public void The_socket_table_maps_ordinals_to_class_ids()
        {
            Unit doc = Parse(SampleRecord);
            {
                SortedDictionary<int, uint> sockets = ItemStatReader.ReadSockets(doc);

                Assert.Equal(new[] { 0, 1 }, sockets.Keys.ToArray());
                Assert.Equal(620u, sockets[0]);
                Assert.Equal(604u, sockets[1]);
            }
        }

        [Fact]
        public void A_record_with_no_sockets_array_yields_an_empty_table()
        {
            Unit doc = Parse(@"{""statsLists"":[]}");
            {
                Assert.Empty(ItemStatReader.ReadSockets(doc));
            }
        }

        [Fact]
        public void A_null_filler_reads_as_an_empty_one_rather_than_throwing()
        {
            // `"items": [null]` is legal JSON, and ten reader sites dereference an element. Every
            // C# entry point threw on it while the TypeScript peer — whose reader maps a null to a
            // default unit — rendered the item. Coerced at the DTO boundary so the two agree, and
            // coerced rather than dropped because POSITION is the socket index.
            Unit doc = Parse(@"{""items"":[ null, { ""classId"": 620 } ]}");

            SortedDictionary<int, uint> sockets = ItemStatReader.ReadSockets(doc);

            Assert.Equal(new[] { 0, 1 }, sockets.Keys.ToArray());
            Assert.Equal(0u, sockets[0]);
            Assert.Equal(620u, sockets[1]);

            // And the whole pipeline survives it.
            Assert.NotNull(ItemStatReader.ReconstructView(doc, ItemStatView.Everything()));
        }

        // =================================================================
        // Group projection
        // =================================================================

        [Fact]
        public void A_group_exposes_its_raw_provenance()
        {
            Unit doc = Parse(SampleRecord);
            {
                ItemStatGroup[] groups = ItemStatReader.EnumerateGroups(doc).ToArray();

                Assert.Equal(6, groups.Length);

                Assert.Equal(0x80000000u, groups[0].Flags);
                Assert.False(groups[0].FromSocket);

                Assert.Equal(165, groups[2].StateNo);
                Assert.Equal(ItemStatListFlags.Set, groups[2].Flags & ItemStatListFlags.Set);

                Assert.True(groups[4].FromSocket);
            }
        }

        [Fact]
        public void A_group_enumerates_its_stats_with_layers_intact()
        {
            Unit doc = Parse(SampleRecord);
            {
                ItemStatGroup socketGroup = ItemStatReader.EnumerateGroups(doc)
                    .First(g => g.FromSocket);

                KeyValuePair<int, int>[] stats = socketGroup.EnumerateStats().ToArray();

                Assert.Equal(2, stats.Length);
                Assert.Equal(ItemStatReader.PackStatKey(0, 17), stats[0].Key);
                Assert.Equal(ItemStatReader.PackStatKey(2, 97), stats[1].Key);
                Assert.Equal(1, stats[1].Value);
            }
        }

        [Fact]
        public void A_group_with_no_source_property_reads_as_Other()
        {
            const string json = @"{""statsLists"":[{""stats"":[{""id"":1,""value"":5}]}]}";

            Unit doc = Parse(json);
            {
                ItemStatGroup group = ItemStatReader.EnumerateGroups(doc).Single();
                Assert.Equal(0u, group.Flags);
            }
        }

        [Fact]
        public void A_group_with_no_stats_array_enumerates_nothing()
        {
            const string json = @"{""statsLists"":[{""source"":""base""}]}";

            Unit doc = Parse(json);
            {
                ItemStatGroup group = ItemStatReader.EnumerateGroups(doc).Single();

                // An absent `stats` array parses to an empty list rather than a missing one, so
                // "no array" and "empty array" are the same thing to a consumer — which is what
                // the enumeration already treated them as.
                Assert.Empty(group.Stats);
                Assert.Empty(group.EnumerateStats());
            }
        }

        // =================================================================
        // Malformed documents throw. These three used to assert the opposite — the hand-written
        // reader fell back to 0 on any cell it could not read, because it was built on
        // JsonElement's TryGet* pair. That was a property of the reader, not a decision: a `flags`
        // cell silently reading 0 loses STATLIST_EXTENDED and MAGIC, so the whole node stops
        // contributing and the tooltip renders short with nothing to say why. Deserialising
        // through System.Text.Json throws instead, which is the failure this project wants.
        //
        // The one place a wide value is still ACCEPTED is a stat value, which the producer widens
        // deliberately for unsigned stats — see Int32NarrowingConverter.
        // =================================================================

        [Fact]
        public void A_group_whose_stats_are_not_an_array_throws()
        {
            Assert.Throws<JsonException>(
                () => Parse(@"{""statsLists"":[{""source"":""base"",""stats"":42}]}"));
        }

        [Fact]
        public void A_non_numeric_field_throws()
        {
            Assert.Throws<JsonException>(
                () => Parse(@"{""statsLists"":[
                    {""source"":""base"",""stateNo"":""nope"",""flags"":0,""stats"":[]}]}"));
        }

        [Fact]
        public void A_number_too_large_for_the_field_throws()
        {
            // 3000000000 exceeds int32 for stateNo; 5000000000 exceeds uint32 for flags. Neither
            // is a value the game can hold, so neither is worth guessing at.
            Assert.Throws<JsonException>(
                () => Parse(@"{""statsLists"":[
                    {""source"":""base"",""stateNo"":3000000000,""flags"":0,""stats"":[]}]}"));

            Assert.Throws<JsonException>(
                () => Parse(@"{""statsLists"":[
                    {""source"":""base"",""stateNo"":0,""flags"":5000000000,""stats"":[]}]}"));
        }

        // Experience at level 99: past int.MaxValue, inside uint.MaxValue.
        private const long Experience = 3520485421L;

        [Fact]
        public void A_statlist_stat_value_past_int32_throws()
        {
            // A per-statlist value is genuinely int32 — the producer writes nValue from an int32
            // struct field — so one outside the range is malformed. Wrapping it would invent a
            // plausible number from a broken document.
            Assert.Throws<JsonException>(
                () => Parse(
                    @"{""statsLists"":[{""stateNo"":0,""flags"":0,""stats"":[
                        {""id"":13,""value"":" + Experience + @"}]}]}"));
        }

        [Fact]
        public void A_merged_wearer_stat_past_int32_is_narrowed_instead()
        {
            // The opposite rule, one nesting up. A merged value is widened BY THE PRODUCER to
            // carry unsigned stats through JSON, so it has to come back. Narrowing unchecked
            // restores the game's own 32 bits.
            //
            // The two rules are why the converter hangs off Unit.Stats and not off UnitStat.Value:
            // UnitStat is the element type of both nestings.
            Unit doc = Parse(@"{""stats"":[{""id"":13,""value"":" + Experience + @"}]}");

            Assert.Equal(unchecked((int)Experience), doc.Stats[0].Value);
        }

        [Theory]
        [InlineData(@"""flags"": 8256", true)]
        [InlineData(@"""flags"": 64", false)]
        [InlineData(@"""other"": 1", false)]   // absent: falls back to 0
        public void Whether_a_node_contributes_comes_from_the_flag_alone(string fragment, bool onMyStats)
        {
            string json = @"{""statsLists"":[{" + fragment + @",""stats"":[]}]}";

            Unit doc = Parse(json);
            {
                ItemStatGroup group = ItemStatReader.EnumerateGroups(doc).Single();
                Assert.Equal(onMyStats, (group.Flags & ItemStatListFlags.Set) != 0);
            }
        }

        [Fact]
        public void A_stat_with_no_value_or_id_reads_as_zero()
        {
            const string json = @"{""statsLists"":[{""source"":""base"",""stats"":[{}]}]}";

            Unit doc = Parse(json);
            {
                KeyValuePair<int, int> stat = ItemStatReader.EnumerateGroups(doc)
                    .Single().EnumerateStats().Single();

                Assert.Equal(0, stat.Key);
                Assert.Equal(0, stat.Value);
            }
        }

        // =================================================================
        // Classification is derived from dwFlags — the record carries none.
        // =================================================================

        private static ItemStatGroup GroupFrom(string groupJson)
        {
            // JsonDocument is disposed, but ItemStatGroup reads the fields it needs eagerly
            // except Stats, which these tests do not touch.
            Unit doc = Parse(@"{""statsLists"":[" + groupJson + "]}");
            {
                return ItemStatReader.EnumerateGroups(doc).Single();
            }
        }

        private static SortedDictionary<int, int> ViewOf(string groupsJson, ItemStatView view)
        {
            Unit doc = Parse(@"{""statsLists"":[" + groupsJson + "]}");
            {
                return ItemStatReader.ReconstructView(doc, view);
            }
        }

        private const string OneStat = @"""stats"":[{""id"":39,""value"":1}]";

        [Fact]
        public void The_base_array_is_the_extended_node_whatever_its_state()
        {
            // STATLIST_EXTENDED marks the StatListEx header. State is irrelevant to that.
            string json = @"{""stateNo"":165,""flags"":2147483648," + OneStat + "}";

            Assert.Single(ViewOf(json, ItemStatView.BaseOnly()));
            Assert.Empty(ViewOf(json, ItemStatView.SetBonuses(true)));
        }

        [Fact]
        public void An_unearned_tier_reaches_no_item_view_but_an_earned_one_reaches_Equipped()
        {
            // 0x2040 = STATLIST_SET | STATLIST_MAGIC: still on pMyStats, contributing nothing.
            // Earning it clears STATLIST_SET, leaving a node that flags alone cannot tell apart
            // from any other item mod — only stateNo still says it is a set tier.
            string unearned = @"{""stateNo"":165,""flags"":8256," + OneStat + "}";
            string earned = @"{""stateNo"":165,""flags"":64," + OneStat + "}";

            Assert.Single(ViewOf(unearned, ItemStatView.SetBonuses(true)));
            Assert.Empty(ViewOf(unearned, ItemStatView.SetBonuses(false)));
            Assert.Empty(ViewOf(unearned, ItemStatView.ForSale()));
            Assert.Empty(ViewOf(unearned, ItemStatView.Equipped()));

            Assert.Single(ViewOf(earned, ItemStatView.SetBonuses(false)));
            Assert.Empty(ViewOf(earned, ItemStatView.ForSale()));      // excluded by its state
            Assert.Single(ViewOf(earned, ItemStatView.Equipped()));    // it IS contributing
        }

        [Fact]
        public void A_runeword_node_is_indistinguishable_from_a_quality_node_by_flags()
        {
            // Both are STATLIST_MAGIC; only dwStateNo separates them, and nothing here needs to.
            string quality = @"{""stateNo"":0,""flags"":64," + OneStat + "}";
            string runeword = @"{""stateNo"":171,""flags"":64," + OneStat + "}";

            Assert.Equal(
                Render(ViewOf(quality, ItemStatView.ForSale())),
                Render(ViewOf(runeword, ItemStatView.ForSale())));
        }

        [Fact]
        public void A_node_with_no_recognised_bit_is_excluded_from_the_item_views()
        {
            string json = @"{""stateNo"":200,""flags"":8," + OneStat + "}";   // STATLIST_BUFF

            Assert.Empty(ViewOf(json, ItemStatView.ForSale()));
            Assert.Empty(ViewOf(json, ItemStatView.Equipped()));
            Assert.Empty(ViewOf(json, ItemStatView.BaseOnly()));

            // Everything filters on nothing, so it still sees it.
            Assert.Single(ViewOf(json, ItemStatView.Everything()));
        }

        [Fact]
        public void A_group_exposes_the_raw_struct_fields_only()
        {
            ItemStatGroup group = GroupFrom(@"{""stateNo"":171,""flags"":64,""stats"":[]}");

            Assert.Equal(171, group.StateNo);
            Assert.Equal(64u, group.Flags);
            Assert.False(group.FromSocket);
        }
    }
}
