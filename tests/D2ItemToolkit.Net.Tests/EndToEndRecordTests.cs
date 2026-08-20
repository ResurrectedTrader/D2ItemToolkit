using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace D2ItemToolkit.Tests
{
    // A v2 record in, a rendered description out, using only the embedded tables.
    public class EndToEndRecordTests
    {
        private static readonly D2DataFiles Data = D2DataFiles.LoadEmbedded();

        private static readonly ItemTable Items = new ItemTable(
            Data.Weapons, Data.Armor, Data.Misc);

        private static readonly ItemTypeTree Types = new ItemTypeTree(Data.ItemTypes);

        private static int ClassIdOf(string code)
        {
            int classId = Items.ClassIdForCode(code);
            Assert.True(classId >= 0, "no items row for code " + code);
            return classId;
        }

        /// <summary>
        /// Turns the fixtures' shorthand player object into a real unit document: level, strength and
        /// dexterity become stats 12, 0 and 2, and an active Holy Shield becomes a stat list carrying
        /// state 101.
        /// </summary>
        private static Unit PlayerUnit(JsonElement inline)
        {
            int Scalar(string name)
            {
                JsonElement value;
                return inline.TryGetProperty(name, out value)
                       && value.ValueKind == JsonValueKind.Number
                    ? value.GetInt32()
                    : 0;
            }

            var player = new Unit();
            player.UnitType = Scalar("unitType");
            player.ClassId = Scalar("classId");
            player.Skills.Add(new UnitSkill(HolyShieldSkillId, Scalar("holyShieldLevel")));

            player.StatsLists.Add(
                new UnitStatList(0, ItemStatListFlags.Extended)
                    .Add(StatLevel, Scalar("level"))
                    .Add(StatStrength, Scalar("strength"))
                    .Add(StatDexterity, Scalar("dexterity")));

            JsonElement active;
            if (inline.TryGetProperty("holyShieldActive", out active)
                && active.ValueKind == JsonValueKind.True)
            {
                player.StatsLists.Add(
                    new UnitStatList(HolyShieldState, ItemStatListFlags.Magic));
            }

            return player;
        }

        private const int StatStrength = 0;
        private const int StatDexterity = 2;
        private const int StatLevel = 12;
        private const int HolyShieldSkillId = 117;
        private const int HolyShieldState = 101;

        private static string Describe(string json, out IReadOnlyList<ItemTooltipLine> lines)
        {
            Unit record = Unit.FromJson(json);

            ItemIdentity item = ItemRecordReader.ReadIdentity(record);

            // The player is a SEPARATE unit document. These fixtures still spell its attributes as
            // scalars for readability, so translate them into the stat lists the reader expects —
            // the same thing a caller with two real documents would already have. Unit does
            // not model the shorthand, so the raw document is re-read just for this.
            JsonElement inline;
            ItemViewer viewer =
                JsonDocument.Parse(json).RootElement.TryGetProperty("player", out inline)
                    ? ItemRecordReader.ReadViewer(PlayerUnit(inline))
                    : null;

            SortedDictionary<int, int> stats =
                ItemStatReader.ReconstructView(record, ItemStatView.Equipped());

            SortedDictionary<int, uint> sockets = ItemStatReader.ReadSockets(record);

            SortedDictionary<int, int> baseStats =
                ItemStatReader.ReconstructView(record, ItemStatView.BaseOnly());

            // op 13 is folded into FullStats by the engine (0x626626) and is NOT in the captured
            // leaf lists, so it has to be re-applied to the merged view — and only to that view.
            ItemStatOps.Resolve(stats, baseStats, Data.ItemStatCost);

            var sections = new RecordSections(
                Data, Items, Types, item, viewer, stats, sockets, baseStats,
                ItemRecordReader.ReadSocketUnits(record));

            // The section writers read the unit's stats through SERVER_GetUnitStat, so they see
            // everything; the modifier block is built from a temp list that only ever receives
            // 0x40 chain nodes (0x4e6452), so it gets its own view.
            SortedDictionary<int, int> modifierStats =
                ItemStatReader.ReconstructView(record, ItemStatView.Modifiers());

            var composer = new ItemTooltipComposer(
                sections, sections.CreateModifierGenerator(modifierStats));

            lines = composer.Compose(sections.CreateContext(), modifierStats);
            return composer.Render(lines);
        }

        /// <summary>
        /// A record whose base array carries <paramref name="baseStats"/> and whose quality node
        /// carries <paramref name="modStats"/>. Only the latter can reach the modifier block: the
        /// base array is not in the chain GetStatList 0x6257d0 walks.
        /// </summary>
        private static string RecordWithMods(
            int classId, string extraItem, string baseStats, string modStats)
        {
            return @"{
                ""classId"": " + classId + @", ""quality"": 2, " + extraItem + @",
                ""statsLists"": [
                    { ""source"": ""base"", ""stateNo"": 0, ""flags"": 2147483648,
                      ""stats"": [ " + baseStats + @" ] },
                    { ""source"": ""quality"", ""stateNo"": 0, ""flags"": 64,
                      ""stats"": [ " + modStats + @" ] } ] }";
        }

        private static string Record(int classId, string extraItem, string stats)
        {
            return @"{ 
                ""classId"": " + classId + @", ""quality"": 2, " + extraItem + @",
                ""statsLists"": [ { ""source"": ""base"", ""stateNo"": 0, ""flags"": 2147483648,
                                ""stats"": [ " + stats + @" ] } ] }";
        }

        [Fact]
        public void A_shield_renders_defense_and_requirements_from_the_record()
        {
            // Large Shield: has reqstr and a block value in armor.txt.
            int classId = ClassIdOf("lrg");

            string rendered = Describe(
                Record(
                    classId,
                    @"""itemFlags"": 16",
                    @"{ ""id"": 31, ""value"": 120 }, { ""id"": 72, ""value"": 40 },
                      { ""id"": 73, ""value"": 62 }"),
                out _);

            string[] rows = rendered.Split('\n');

            Assert.Contains(rows, r => r == "Defense: 120");
            Assert.Contains(rows, r => r == "Durability: 40 of 62");
            Assert.Contains(rows, r => r.StartsWith("Required Strength:", StringComparison.Ordinal));
        }

        [Fact]
        public void Ethereal_reduces_the_strength_requirement_by_ten()
        {
            int classId = ClassIdOf("lrg");
            int plain = RequiredStrength(classId, 16);
            int ethereal = RequiredStrength(classId, 16 | 0x400000);

            Assert.Equal(plain - 10, ethereal);
        }

        private static int RequiredStrength(int classId, int flags)
        {
            IReadOnlyList<ItemTooltipLine> lines;
            Describe(
                Record(classId, @"""itemFlags"": " + flags, string.Empty),
                out lines);

            string text = lines
                .Single(l => l.Section == ItemTooltipSection.RequiredStrength)
                .Text;

            return int.Parse(new string(text.Where(char.IsDigit).ToArray()));
        }

        [Fact]
        public void An_ethereal_socketed_item_names_both_states()
        {
            int classId = ClassIdOf("lrg");

            IReadOnlyList<ItemTooltipLine> lines;
            Describe(
                Record(
                    classId,
                    @"""itemFlags"": " + (16 | 0x800 | 0x400000),
                    @"{ ""id"": 194, ""value"": 3 }"),
                out lines);

            string text = lines
                .Single(l => l.Section == ItemTooltipSection.EtherealSocketed)
                .Text;

            Assert.Contains("Ethereal", text, StringComparison.Ordinal);
            Assert.Contains("Socketed (3)", text, StringComparison.Ordinal);
        }

        [Fact]
        public void The_required_level_line_appears_only_above_one()
        {
            int classId = ClassIdOf("lrg");

            // Large Shield has levelreq 0, so stat 92 (item_levelreq) is the whole requirement.
            IReadOnlyList<ItemTooltipLine> atOne;
            Describe(
                Record(classId, @"""itemFlags"": 16", @"{ ""id"": 92, ""value"": 1 }"),
                out atOne);
            Assert.DoesNotContain(atOne, l => l.Section == ItemTooltipSection.RequiredLevel);

            string rendered = Describe(
                Record(classId, @"""itemFlags"": 16", @"{ ""id"": 92, ""value"": 41 }"),
                out _);

            Assert.Contains("Required Level: 41", rendered, StringComparison.Ordinal);
        }

        [Fact]
        public void An_unmet_requirement_turns_that_line_red()
        {
            int classId = ClassIdOf("lrg");

            // Large Shield needs 34 strength; this player has it but is well short of level 41, so
            // exactly one of the two lines turns red.
            IReadOnlyList<ItemTooltipLine> lines;
            Describe(
                @"{ 
                    ""classId"": " + classId + @", ""quality"": 2, ""itemFlags"": 16,
                    ""player"": { ""unitType"": 0, ""classId"": 3, ""level"": 12,
                                  ""strength"": 60, ""dexterity"": 60 },
                    ""statsLists"": [ { ""stateNo"": 0, ""flags"": 2147483648,
                        ""stats"": [ { ""id"": 92, ""value"": 41 } ] } ] }",
                out lines);

            Assert.Equal(
                ItemTooltipColor.Red,
                lines.Single(l => l.Section == ItemTooltipSection.RequiredLevel).Color);

            Assert.Equal(
                ItemTooltipColor.White,
                lines.Single(l => l.Section == ItemTooltipSection.RequiredStrength).Color);
        }

        [Fact]
        public void The_stat_block_and_the_state_lines_render_together_bottom_up()
        {
            int classId = ClassIdOf("lrg");

            string rendered = Describe(
                RecordWithMods(
                    classId,
                    @"""itemFlags"": 16",
                    @"{ ""id"": 31, ""value"": 120 }",
                    @"{ ""id"": 39, ""value"": 30 }"),
                out _);

            string[] rows = rendered.Split('\n');

            // Defense is a section; Fire Resist is a DescFunc stat line. Both present, and the
            // stat block sits below the state lines because it is appended earlier.
            Assert.Contains(rows, r => r == "Defense: 120");
            Assert.Contains(rows, r => r == "Fire Resist +30%");

            int defense = Array.FindIndex(rows, r => r == "Defense: 120");
            int resist = Array.FindIndex(rows, r => r == "Fire Resist +30%");
            Assert.True(defense < resist, rendered);
        }

        [Fact]
        public void A_paladin_shield_gets_smite_damage_and_a_sorceress_does_not()
        {
            int classId = ClassIdOf("lrg");

            IReadOnlyList<ItemTooltipLine> paladin;
            Describe(
                @"{ 
                    ""classId"": " + classId + @", ""quality"": 2, ""itemFlags"": 16,
                    ""player"": { ""unitType"": 0, ""classId"": 3, ""level"": 40 },
                    ""runtime"": { ""smiteMin"": 3, ""smiteMax"": 6 },
                    ""statsLists"": [] }",
                out paladin);

            Assert.Contains(paladin, l => l.Section == ItemTooltipSection.SmiteOrKickDamage);

            IReadOnlyList<ItemTooltipLine> sorceress;
            Describe(
                @"{ 
                    ""classId"": " + classId + @", ""quality"": 2, ""itemFlags"": 16,
                    ""player"": { ""unitType"": 0, ""classId"": 1, ""level"": 40 },
                    ""statsLists"": [] }",
                out sorceress);

            Assert.DoesNotContain(sorceress, l => l.Section == ItemTooltipSection.SmiteOrKickDamage);
        }

        [Fact]
        public void A_monster_viewer_does_not_trigger_the_class_gated_lines()
        {
            int classId = ClassIdOf("lrg");

            IReadOnlyList<ItemTooltipLine> lines;
            Describe(
                @"{ 
                    ""classId"": " + classId + @", ""quality"": 2, ""itemFlags"": 16,
                    ""player"": { ""unitType"": 1, ""classId"": 3 },
                    ""runtime"": { ""smiteMin"": 3, ""smiteMax"": 6 },
                    ""statsLists"": [] }",
                out lines);

            // LoadItemDesc would emit Smite here (it checks dwClassId only, 0x48e75c).
            Assert.DoesNotContain(lines, l => l.Section == ItemTooltipSection.SmiteOrKickDamage);
        }

        [Fact]
        public void A_unique_item_renders_name_state_and_stats_in_one_description()
        {
            int classId = ClassIdOf("hax");

            string rendered = Describe(
                @"{ 
                    ""classId"": " + classId + @", ""quality"": 7,
                                ""itemFlags"": " + (16 | 0x800) + @", ""fileIndex"": 0,
                    ""player"": { ""unitType"": 0, ""classId"": 1, ""level"": 40 },
                    ""runtime"": {},
                    ""statsLists"": [
                        { ""source"": ""base"", ""stateNo"": 0, ""flags"": 2147483648,
                          ""stats"": [ { ""id"": 194, ""value"": 2 },
                                       { ""id"": 72, ""value"": 26 }, { ""id"": 73, ""value"": 28 } ] },
                        { ""source"": ""quality"", ""stateNo"": 0, ""flags"": 64,
                          ""stats"": [ { ""id"": 39, ""value"": 25 } ] } ] }",
                out _);

            string[] rows = rendered.Split('\n');

            // GetItemName builds "base \n unique", and the renderer draws bottom-up, so the UNIQUE
            // name ends up on top with the base type beneath it — as in the game.
            Assert.Equal("The Gnasher", rows[0]);
            Assert.Equal("Hand Axe", rows[1]);
            // UniqueItems.txt row 0 carries "lvl req" 5, and Hand Axe has levelreq 0.
            Assert.Contains(rows, r => r == "Required Level: 5");
            Assert.Contains(rows, r => r == "Durability: 26 of 28");
            Assert.Contains(rows, r => r == "Socketed (2)");
            Assert.Contains(rows, r => r == "Fire Resist +25%");
        }

        [Fact]
        public void A_one_handed_weapon_renders_its_damage_range()
        {
            int classId = ClassIdOf("ssd");

            string rendered = Describe(
                Record(
                    classId,
                    @"""itemFlags"": 16",
                    @"{ ""id"": 21, ""value"": 8 }, { ""id"": 22, ""value"": 15 }"),
                out _);

            Assert.Contains("One-Hand Damage: 8 to 15", rendered, StringComparison.Ordinal);
        }

        [Fact]
        public void A_two_handed_weapon_uses_the_secondary_stats_and_label()
        {
            // Two Handed Sword.
            int classId = ClassIdOf("2hs");

            string rendered = Describe(
                Record(
                    classId,
                    @"""itemFlags"": 16",
                    @"{ ""id"": 23, ""value"": 9 }, { ""id"": 24, ""value"": 20 }"),
                out _);

            Assert.Contains("Two-Hand Damage: 9 to 20", rendered, StringComparison.Ordinal);
        }

        [Fact]
        public void The_damage_line_never_shows_a_single_value()
        {
            int classId = ClassIdOf("ssd");

            string rendered = Describe(
                Record(
                    classId,
                    @"""itemFlags"": 16",
                    @"{ ""id"": 21, ""value"": 12 }, { ""id"": 22, ""value"": 12 }"),
                out _);

            // 0x485928: max = MAX(max, min + 1), so equal min/max renders as N to N+1.
            Assert.Contains("One-Hand Damage: 12 to 13", rendered, StringComparison.Ordinal);
        }

        [Fact]
        public void A_shield_shows_block_chance_including_the_class_factor()
        {
            int classId = ClassIdOf("lrg");

            string rendered = Describe(
                @"{ 
                    ""classId"": " + classId + @", ""quality"": 2, ""itemFlags"": 16,
                    ""player"": { ""unitType"": 0, ""classId"": 3, ""level"": 40 },
                    ""statsLists"": [ { ""stateNo"": 0, ""flags"": 2147483648,
                        ""stats"": [ { ""id"": 20, ""value"": 20 } ] } ] }",
                out _);

            // Paladin BlockFactor is 30 in charstats, so 20 + 30 = 50. Large Shield's items.txt
            // block is 12, so the NUMBER carries colour 3 (0x485cea) behind the label's explicit
            // colour 0 (0x485d0e).
            Assert.Contains(
                ItemTooltipColor.Marker + "0Chance to Block: " + ItemTooltipColor.Marker + "350%",
                rendered, StringComparison.Ordinal);
        }

        [Fact]
        public void Block_chance_is_capped_at_seventy_five()
        {
            int classId = ClassIdOf("lrg");

            string rendered = Describe(
                @"{ 
                    ""classId"": " + classId + @", ""quality"": 2, ""itemFlags"": 16,
                    ""player"": { ""unitType"": 0, ""classId"": 3, ""level"": 40 },
                    ""statsLists"": [ { ""stateNo"": 0, ""flags"": 2147483648,
                        ""stats"": [ { ""id"": 20, ""value"": 90 } ] } ] }",
                out _);

            Assert.Contains(
                ItemTooltipColor.Marker + "0Chance to Block: " + ItemTooltipColor.Marker + "375%",
                rendered, StringComparison.Ordinal);
        }

        [Fact]
        public void A_class_restricted_item_names_the_class()
        {
            // Amazon-only spear type.
            int classId = ClassIdOf("am1");

            IReadOnlyList<ItemTooltipLine> lines;
            Describe(
                Record(classId, @"""itemFlags"": 16", string.Empty),
                out lines);

            ItemTooltipLine[] restriction = lines
                .Where(l => l.Section == ItemTooltipSection.ClassRestriction).ToArray();

            Assert.Single(restriction);
            Assert.Contains("Amazon", restriction[0].Text, StringComparison.Ordinal);
        }

        [Fact]
        public void A_stackable_item_shows_its_quantity()
        {
            // Throwing knives stack.
            int classId = ClassIdOf("tkf");

            string rendered = Describe(
                Record(
                    classId,
                    @"""itemFlags"": 16",
                    @"{ ""id"": 70, ""value"": 120 }"),
                out _);

            Assert.Contains("Quantity: 120", rendered, StringComparison.Ordinal);
        }

        [Fact]
        public void A_charm_gets_the_charm_line()
        {
            int classId = ClassIdOf("cm1");

            IReadOnlyList<ItemTooltipLine> lines;
            Describe(
                Record(classId, @"""itemFlags"": 16", string.Empty),
                out lines);

            Assert.Contains(lines, l => l.Section == ItemTooltipSection.CharmDescription);
        }

        [Fact]
        public void A_weapon_shows_its_class_and_speed_word()
        {
            int classId = ClassIdOf("ssd");

            IReadOnlyList<ItemTooltipLine> lines;
            Describe(
                @"{
                    ""classId"": " + classId + @", ""quality"": 2, ""itemFlags"": 16,
                    ""player"": { ""unitType"": 0, ""classId"": 3, ""level"": 40 },
                    ""runtime"": { ""attackSpeed"": 15 },
                    ""statsLists"": [] }",
                out lines);

            ItemTooltipLine speed = lines
                .Single(l => l.Section == ItemTooltipSection.AttackSpeed);

            // Short Sword is under "swor", so the prefix is the Sword Class word.
            Assert.Contains("Sword Class", speed.Text, StringComparison.Ordinal);
            Assert.Contains("Attack Speed", speed.Text, StringComparison.Ordinal);
        }

        [Fact]
        public void A_runeword_lists_its_rune_letters()
        {
            int classId = ClassIdOf("ssd");
            int amn = ClassIdOf("r11");
            int ral = ClassIdOf("r08");

            IReadOnlyList<ItemTooltipLine> lines;
            Describe(
                @"{ 
                    ""classId"": " + classId + @", ""quality"": 2,
                                ""itemFlags"": " + (16 | 0x800 | 0x04000000) + @",
                    ""statsLists"": [],
                    ""sockets"": [ { ""classId"": " + ral + @" },
                                   { ""classId"": " + amn + @" } ] }",
                out lines);

            ItemTooltipLine runes = lines
                .Single(l => l.Section == ItemTooltipSection.RuneLetters);

            // Socket order, then a hardcoded apostrophe (0x486742).
            Assert.EndsWith("'\n", runes.Text, StringComparison.Ordinal);
            Assert.True(runes.Text.Length > 2, runes.Text);
        }

        [Fact]
        public void A_record_with_no_player_still_describes_the_item()
        {
            int classId = ClassIdOf("lrg");

            string rendered = Describe(
                Record(classId, @"""itemFlags"": 16",
                    @"{ ""id"": 31, ""value"": 99 }"),
                out _);

            Assert.Contains("Defense: 99", rendered, StringComparison.Ordinal);
        }

        /// <summary>
        /// A record whose stats are split between the item's own `base` list and a `quality` one,
        /// which is what SERVER_GetUnitStat and GetStatUnsignedValue read separately.
        /// </summary>
        private static string Layered(int classId, string flags, string baseStats, string bonusStats)
        {
            return @"{ ""classId"": " + classId + @", ""quality"": 2, ""itemFlags"": " + flags + @",
                ""statsLists"": [
                    { ""stateNo"": 0, ""flags"": 2147483648,
                      ""stats"": [ " + baseStats + @" ] },
                    { ""stateNo"": 0, ""flags"": 64,
                      ""stats"": [ " + bonusStats + @" ] } ] }";
        }

        [Fact]
        public void A_boosted_defense_number_is_blue()
        {
            // 0x485fb1: the base stat 31 is 100 and the merged one 120, so the NUMBER — not the
            // label — carries colour 3 (0x4860de).
            int classId = ClassIdOf("lrg");

            string rendered = Describe(
                Layered(classId, "16", @"{ ""id"": 31, ""value"": 100 }",
                    @"{ ""id"": 31, ""value"": 20 }"),
                out _);

            Assert.Contains(
                "Defense: " + ItemTooltipColor.Marker + "3120", rendered, StringComparison.Ordinal);
        }

        [Fact]
        public void An_unboosted_defense_number_carries_no_marker()
        {
            int classId = ClassIdOf("lrg");

            string rendered = Describe(
                Record(classId, @"""itemFlags"": 16",
                    @"{ ""id"": 31, ""value"": 120 }"),
                out _);

            Assert.Contains("Defense: 120\n", rendered, StringComparison.Ordinal);
        }

        [Fact]
        public void Increased_maximum_durability_raises_the_max_but_never_colours_it()
        {
            // 0x484f0b reads STATLIST_GetStatBonusFromLists(item, 75, 0) — merged minus base
            // (0x625570) — and would prepend the marker to the MAX buffer alone (0x484fc6). On an
            // ITEM that difference is always zero, so the marker never appears: once stat 75 has
            // landed on a non-zero target, STATLIST_ApplyComplexStatFormula refuses to store the
            // percent stat itself in FullStats (0x626821 tests dwOwnerType == UNIT_ITEM, 0x626847
            // then skips the write at 0x626868).
            //
            // This asserted the marker for four rounds. A real capture settled it: the game draws
            // `Durability: 22 of 22` for a Superior Crystal Sword carrying +13% max durability.
            int classId = ClassIdOf("lrg");

            string rendered = Describe(
                Layered(
                    classId, "16",
                    @"{ ""id"": 72, ""value"": 40 }, { ""id"": 73, ""value"": 62 }",
                    @"{ ""id"": 75, ""value"": 25 }"),
                out _);

            // 62 + trunc(62 * 25 / 100) = 77. The op still RESOLVES onto stat 73 — only the percent
            // stat's own entry is dropped.
            Assert.Contains("Durability: 40 of 77\n", rendered, StringComparison.Ordinal);
            Assert.DoesNotContain(
                ItemTooltipColor.Marker + "377", rendered, StringComparison.Ordinal);
        }

        [Fact]
        public void An_unidentified_item_shows_no_required_level()
        {
            // 0x48e54f wraps the whole Required Level block in CheckItemFlag(item, 0x10).
            int classId = ClassIdOf("lrg");

            IReadOnlyList<ItemTooltipLine> identified;
            Describe(
                Record(classId, @"""itemFlags"": 16", @"{ ""id"": 92, ""value"": 41 }"),
                out identified);
            Assert.Contains(identified, l => l.Section == ItemTooltipSection.RequiredLevel);

            IReadOnlyList<ItemTooltipLine> unidentified;
            Describe(
                Record(classId, @"""itemFlags"": 0", @"{ ""id"": 92, ""value"": 41 }"),
                out unidentified);
            Assert.DoesNotContain(
                unidentified, l => l.Section == ItemTooltipSection.RequiredLevel);
        }

        [Fact]
        public void An_unidentified_stackable_shows_no_quantity()
        {
            // AppendQuanity is reached only through CheckItemFlag(item, 0x10) at 0x48e8ef.
            int classId = ClassIdOf("tkf");

            string rendered = Describe(
                Record(classId, @"""itemFlags"": 0", @"{ ""id"": 70, ""value"": 120 }"),
                out _);

            Assert.DoesNotContain("Quantity", rendered, StringComparison.Ordinal);
        }

        [Fact]
        public void A_socketed_stackable_shows_no_quantity()
        {
            // The second gate at 0x48e90d: CheckItemFlag(item, 0x800) must be CLEAR.
            int classId = ClassIdOf("tkf");

            string rendered = Describe(
                Record(
                    classId, @"""itemFlags"": " + (16 | 0x800),
                    @"{ ""id"": 70, ""value"": 120 }"),
                out _);

            Assert.DoesNotContain("Quantity", rendered, StringComparison.Ordinal);
        }

        [Fact]
        public void A_throwing_potion_gets_a_single_elemental_throw_damage_line()
        {
            // 0x485459 tests tpot first and its arm COPIES the buffer, so none of the ordinary
            // one-hand or throw text survives. Rancid Gas Potion fires missile 49: 192 poison
            // over an ELen of 50, divided by 50/25 = 2 (0x4854fd), and min == max suppresses the
            // "to max" half (0x4855bd). Poison takes colour 2 from the table at 0x4854d0.
            int classId = ClassIdOf("gps");

            string rendered = Describe(
                Record(classId, @"""itemFlags"": 16", string.Empty), out _);

            Assert.Contains(
                ItemTooltipColor.Marker + "0Throw Damage: " + ItemTooltipColor.Marker + "296",
                rendered, StringComparison.Ordinal);

            Assert.DoesNotContain("One-Hand Damage", rendered, StringComparison.Ordinal);
        }

        [Fact]
        public void An_oil_potion_shows_a_range_in_the_fire_colour()
        {
            // Fulminating Potion fires missile 44: physical 2-7 plus fire 3-8, both shifted by the
            // record's HitShift of 8 and shifted back at 0x48554c / 0x485559.
            int classId = ClassIdOf("opl");

            string rendered = Describe(
                Record(classId, @"""itemFlags"": 16", string.Empty), out _);

            Assert.Contains(
                ItemTooltipColor.Marker + "0Throw Damage: " + ItemTooltipColor.Marker + "15 to "
                + ItemTooltipColor.Marker + "115",
                rendered, StringComparison.Ordinal);
        }

        [Fact]
        public void A_rune_name_is_forced_to_colour_eight()
        {
            // 0x48ea0c: IsOfType(item, 74).
            int classId = ClassIdOf("r01");

            IReadOnlyList<ItemTooltipLine> lines;
            Describe(Record(classId, @"""itemFlags"": 16", string.Empty), out lines);

            Assert.Equal(
                ItemTooltipColor.Crafted,
                lines.Single(l => l.Section == ItemTooltipSection.ItemName).Color);
        }

        [Fact]
        public void An_essence_name_is_forced_to_colour_eight_by_its_code()
        {
            // "tes " is one of the eleven dwords compared at 0x48e9b0; it is not a rune.
            int classId = ClassIdOf("tes");

            IReadOnlyList<ItemTooltipLine> lines;
            Describe(Record(classId, @"""itemFlags"": 16", string.Empty), out lines);

            Assert.Equal(
                ItemTooltipColor.Crafted,
                lines.Single(l => l.Section == ItemTooltipSection.ItemName).Color);
        }

        [Fact]
        public void A_gem_is_not_forced_to_colour_eight()
        {
            int classId = ClassIdOf("gcv");

            IReadOnlyList<ItemTooltipLine> lines;
            Describe(Record(classId, @"""itemFlags"": 16", string.Empty), out lines);

            Assert.Equal(
                ItemTooltipColor.White,
                lines.Single(l => l.Section == ItemTooltipSection.ItemName).Color);
        }

        [Fact]
        public void A_quest_item_name_is_gold()
        {
            // items.txt nQuest at +0x12A (0x48cb0b); the Horadric Cube leaves nQuestDiffCheck
            // blank, so it takes the 0x48ce6d arm outright.
            //
            // The gold is in the name buffer's TEXT, not the section colour. AppendAsWideChar
            // prepends, so GetItemName's marker lands at the head of the buffer and LoadItemDesc
            // then stacks v105 — 0 for a normal-quality cube — in front of it. Asserting it as the
            // section Colour collapsed the two markers the game draws into one.
            int classId = ClassIdOf("box");

            IReadOnlyList<ItemTooltipLine> lines;
            Describe(Record(classId, @"""itemFlags"": 16", string.Empty), out lines);

            ItemTooltipLine name = lines.Single(l => l.Section == ItemTooltipSection.ItemName);

            Assert.Equal(ItemTooltipColor.White, name.Color);
            Assert.StartsWith(
                ItemTooltipColor.Marker + "4", name.Text, StringComparison.Ordinal);
        }

        [Fact]
        public void Wirts_leg_is_excluded_from_the_gold_arm()
        {
            // 0x48ce59 compares the items.txt code dword against "leg " before colouring.
            int classId = ClassIdOf("leg");

            IReadOnlyList<ItemTooltipLine> lines;
            Describe(Record(classId, @"""itemFlags"": 16", string.Empty), out lines);

            Assert.Equal(
                ItemTooltipColor.White,
                lines.Single(l => l.Section == ItemTooltipSection.ItemName).Color);
        }

        [Fact]
        public void An_empty_socketed_item_is_not_named_gemmed()
        {
            // 0x48c4b5 needs ITEM_ItemsInItem above zero as well as the 0x800 flag.
            int classId = ClassIdOf("lrg");

            string rendered = Describe(
                Record(classId, @"""itemFlags"": " + (16 | 0x800), string.Empty),
                out _);

            Assert.DoesNotContain("Gemmed", rendered, StringComparison.Ordinal);
        }

        [Fact]
        public void Paired_damage_stats_merge_into_one_added_damage_line()
        {
            // SKILLDESC_BuildStatListDesc 0x4e49c0 latches the pair off the described unit's own
            // statlists, so the generator has to be built from the same stats the sections see.
            int classId = ClassIdOf("ssd");

            string rendered = Describe(
                RecordWithMods(
                    classId, @"""itemFlags"": 16", string.Empty,
                    @"{ ""id"": 48, ""value"": 15 }, { ""id"": 49, ""value"": 20 }"),
                out _);

            Assert.Contains("15-20", rendered, StringComparison.Ordinal);
        }

        // =================================================================
        // op 13: ItemStatCost's op stats are a REVERSE index — a row's `op stat1..3` name the
        // TARGETS it modifies, so 18 (mindamage%) drives 21/23/159 and 17 drives 22/24/160.
        // STATLIST_CalcCombinedStatValue case 13 (0x626626) accumulates
        // D2ApplyPercent(base[target], merged[percent], 100) into FullStats.
        // =================================================================

        [Fact]
        public void Enhanced_damage_scales_the_one_hand_numbers()
        {
            // Throwing Axe base 4-7 melee. +150% ED => 4+6=10, 7+10=17.
            string rendered = Describe(
                Layered(
                    ClassIdOf("tax"), "16",
                    @"{ ""id"": 21, ""value"": 4 }, { ""id"": 22, ""value"": 7 }",
                    @"{ ""id"": 18, ""value"": 150 }, { ""id"": 17, ""value"": 150 }"),
                out _);

            Assert.Contains(
                "One-Hand Damage: " + ItemTooltipColor.Marker + "310 to 17",
                rendered, StringComparison.Ordinal);
        }

        [Fact]
        public void Enhanced_damage_scales_the_throw_numbers_too()
        {
            // Same item, throw base 8-12 => 8+12=20, 12+18=30.
            string rendered = Describe(
                Layered(
                    ClassIdOf("tax"), "16",
                    @"{ ""id"": 159, ""value"": 8 }, { ""id"": 160, ""value"": 12 }",
                    @"{ ""id"": 18, ""value"": 150 }, { ""id"": 17, ""value"": 150 }"),
                out _);

            string c3 = ItemTooltipColor.Marker + "3";
            Assert.Contains(
                "Throw Damage: " + c3 + "20 to " + c3 + "30",
                rendered, StringComparison.Ordinal);
        }

        [Fact]
        public void A_small_percent_truncates_to_nothing()
        {
            // Throwing Knife max throw 9; trunc(9 * 10 / 100) = 0, so the numbers do not move.
            string rendered = Describe(
                Layered(
                    ClassIdOf("tkf"), "16",
                    @"{ ""id"": 159, ""value"": 4 }, { ""id"": 160, ""value"": 9 }",
                    @"{ ""id"": 18, ""value"": 10 }, { ""id"": 17, ""value"": 10 }"),
                out _);

            // And the line is NOT marked. pModified is base-vs-merged (0x485300) plus stats 272/273,
            // and nothing moved — while stats 17 and 18 are gone from an item's FullStats entirely
            // once they have landed on a non-zero target (0x626821 / 0x626847). The throw line is
            // the one that states colour 0 explicitly when unmodified: `esi = modified ? 3 : 0` at
            // 0x485AEE-0x485AF2.
            //
            // This asserted colour 3 while the percent stat was still being left in the merged view.
            string c0 = ItemTooltipColor.Marker + "0";
            Assert.Contains(
                "Throw Damage: " + c0 + "4 to " + c0 + "9", rendered, StringComparison.Ordinal);
        }

        [Fact]
        public void Two_percent_sources_sum_before_being_applied_once()
        {
            // A 100% prefix plus a 50% jewel must equal a single 150%: the percent is summed in the
            // merged view, then applied once against the base.
            string split = Describe(
                Layered(
                    ClassIdOf("tax"), "16",
                    @"{ ""id"": 21, ""value"": 4 }, { ""id"": 22, ""value"": 7 }",
                    @"{ ""id"": 18, ""value"": 100 }, { ""id"": 17, ""value"": 100 }"),
                out _);

            Assert.Contains("8 to 14", split, StringComparison.Ordinal);
        }

        [Fact]
        public void Op_resolution_leaves_the_base_view_alone()
        {
            // BaseOnly IS op 13's input (0x624ed4 always reads Stats), and DamageIsModified is
            // merged-minus-base — moving the base would strip every colour marker.
            var baseStats = new SortedDictionary<int, int>
            {
                { ItemStatReader.PackStatKey(0, 21), 4 },
            };

            var merged = new SortedDictionary<int, int>
            {
                { ItemStatReader.PackStatKey(0, 21), 4 },
                { ItemStatReader.PackStatKey(0, 18), 150 },
            };

            ItemStatOps.Resolve(merged, baseStats, Data.ItemStatCost);

            Assert.Equal(4, baseStats[ItemStatReader.PackStatKey(0, 21)]);
            Assert.Equal(10, merged[ItemStatReader.PackStatKey(0, 21)]);
        }

        [Fact]
        public void The_op_table_is_the_reverse_index_the_loader_builds()
        {
            var byPercent = new SortedDictionary<int, SortedSet<int>>();
            foreach (ItemStatOpEntry entry in Data.ItemStatCost.PercentOfBaseEntries)
            {
                if (!byPercent.ContainsKey(entry.PercentStat))
                {
                    byPercent[entry.PercentStat] = new SortedSet<int>();
                }

                byPercent[entry.PercentStat].Add(entry.TargetStat);
            }

            // Five op-13 rows, nine targets.
            Assert.Equal(new[] { 16, 17, 18, 75, 94 }, byPercent.Keys.ToArray());
            Assert.Equal(new[] { 31 }, byPercent[16].ToArray());
            Assert.Equal(new[] { 22, 24, 160 }, byPercent[17].ToArray());
            Assert.Equal(new[] { 21, 23, 159 }, byPercent[18].ToArray());
            Assert.Equal(new[] { 73 }, byPercent[75].ToArray());
            Assert.Equal(new[] { 92 }, byPercent[94].ToArray());
        }
    }
}
