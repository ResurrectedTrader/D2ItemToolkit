using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace D2ItemToolkit.Tests
{
    /// <summary>
    /// The public facade, exercised the way a consumer would: a Unit in, a Tooltip out, with
    /// nothing internal named. Every expectation here is the EXACT rendered text.
    /// </summary>
    public class TooltipEngineTests
    {
        // A magic Large Shield with one socketed sapphire. The affix indices are 1-based into the
        // CONCATENATED [magicsuffix][magicprefix][automagic] array, so 962 is past the 747 suffix
        // rows and lands in the prefix table.
        private const string ItemJson = @"{
          ""unitType"": 4, ""classId"": 330, ""quality"": 4, ""itemFlags"": 16,
          ""magicPrefix"": [ 962, 0, 0 ], ""magicSuffix"": [ 121, 0, 0 ],
          ""statsLists"": [
            { ""stateNo"": 0, ""flags"": 2147483648,
              ""stats"": [ { ""id"": 31, ""value"": 120 }, { ""id"": 72, ""value"": 40 },
                           { ""id"": 73, ""value"": 62 } ] },
            { ""stateNo"": 0, ""flags"": 64,
              ""stats"": [ { ""id"": 16, ""value"": 150 }, { ""id"": 39, ""value"": 25 },
                           { ""id"": 80, ""value"": 30 } ] } ],
          ""items"": [
            { ""unitType"": 4, ""classId"": 604,
              ""statsLists"": [ { ""stateNo"": 0, ""flags"": 64,
                                  ""stats"": [ { ""id"": 39, ""value"": 38 } ] } ] } ]
        }";

        private const string PlayerJson = @"{
          ""unitType"": 0, ""classId"": 3,
          ""statsLists"": [
            { ""stateNo"": 0, ""flags"": 2147483648,
              ""stats"": [ { ""id"": 12, ""value"": 30 }, { ""id"": 0, ""value"": 60 },
                           { ""id"": 2, ""value"": 55 } ] } ] }";

        private static Unit Item()
        {
            return Unit.FromJson(ItemJson);
        }

        private static Unit Player()
        {
            return Unit.FromJson(PlayerJson);
        }

        private static string[] TextOf(IReadOnlyList<ItemTooltipLine> lines)
        {
            var text = new string[lines.Count];
            for (int i = 0; i < lines.Count; ++i)
            {
                text[i] = lines[i].Text.Replace("\n", string.Empty);
            }

            return text;
        }

        [Fact]
        public void Render_produces_the_whole_tooltip_from_two_records()
        {
            Tooltip tip = TooltipEngine.Embedded.Render(Item(), Player());

            Assert.Equal(ItemTooltipKind.Generic, tip.Kind);
            Assert.Equal(
                "Vigorous Large Shield of Absorption\n"
                + "Defense: ÿc3300\n"
                + "ÿc0Chance to Block: ÿc330%\n"
                + "Smite Damage: 2 to 4\n"
                + "Durability: 40 of 62\n"
                + "Required Strength: 34\n"
                + "Required Level: 24\n"
                + "+150% Enhanced Defense\n"
                + "Fire Resist +63%\n"
                + "30% Better Chance of Getting Magic Items",
                tip.Text);
        }

        [Fact]
        public void The_socketed_gem_is_merged_into_the_resist_line()
        {
            // 25 on the item, 38 on the filler. The engine SUMS them into one line rather than
            // listing them separately, which is what makes IncludeSockets worth having.
            Assert.Contains("Fire Resist +63%", TooltipEngine.Embedded.Render(Item(), Player()).Text);
        }

        [Fact]
        public void Excluding_sockets_drops_only_the_fillers_contribution()
        {
            var options = new TooltipOptions();
            options.IncludeSockets = false;

            string text = TooltipEngine.Embedded.Render(Item(), Player(), options).Text;

            Assert.Contains("Fire Resist +25%", text);
            Assert.DoesNotContain("Fire Resist +63%", text);

            // The base sections are unaffected — the gem contributed nothing to them.
            Assert.Contains("Defense: ÿc3300", text);
            Assert.Contains("Durability: 40 of 62", text);
        }

        [Fact]
        public void ColoredText_prepends_the_per_line_marker_that_Text_omits()
        {
            Tooltip tip = TooltipEngine.Embedded.Render(Item(), Player());

            // Two markers on the block line, and that is the game: INV_FormatBlockChanceText
            // prepends colour 0 to its own label buffer (0x485d0e) and LoadItemDesc prepends the
            // section's on top (0x48eb80). The composer used to swallow its own whenever the text
            // already began with a marker, which lost one of the pair.
            Assert.Equal(
                "ÿc3Vigorous Large Shield of Absorption\n"
                + "ÿc0Defense: ÿc3300\n"
                + "ÿc0ÿc0Chance to Block: ÿc330%\n"
                + "ÿc0Smite Damage: 2 to 4\n"
                + "ÿc0Durability: 40 of 62\n"
                + "ÿc0Required Strength: 34\n"
                + "ÿc0Required Level: 24\n"
                + "ÿc3+150% Enhanced Defense\n"
                + "ÿc3Fire Resist +63%\n"
                + "ÿc330% Better Chance of Getting Magic Items",
                tip.ColoredText);

            // Text keeps markers a writer embedded in its OWN text, and drops only the per-line
            // ones — the game embeds those too.
            Assert.StartsWith("Vigorous Large Shield of Absorption\nDefense: ÿc3300", tip.Text);
        }

        // =================================================================
        // Breakdown — NOT a fidelity feature. The game never draws these separately, so what is
        // pinned here is that each source selects the right stats and that the lines come out of
        // the same traced writers.
        // =================================================================

        [Fact]
        public void Breakdown_base_is_the_base_array_alone()
        {
            TooltipBreakdown breakdown = TooltipEngine.Embedded.Breakdown(Item(), Player());

            Assert.Equal(new[] { "+120 Defense" }, TextOf(breakdown.Base));
        }

        [Fact]
        public void Breakdown_magic_excludes_both_the_base_array_and_the_fillers()
        {
            TooltipBreakdown breakdown = TooltipEngine.Embedded.Breakdown(Item(), Player());

            // ItemStatView.ItemOnly() would be wrong here: it requires EXTENDED *or* MAGIC, so it
            // drags "+120 Defense" in with it.
            Assert.Equal(
                new[]
                {
                    "+150% Enhanced Defense",
                    "Fire Resist +25%",
                    "30% Better Chance of Getting Magic Items",
                },
                TextOf(breakdown.Magic));
        }

        [Fact]
        public void Breakdown_sockets_is_only_what_the_filler_contributes()
        {
            TooltipBreakdown breakdown = TooltipEngine.Embedded.Breakdown(Item(), Player());

            // 38, not the merged 63 — the item's own 25 belongs to Magic.
            Assert.Equal(new[] { "Fire Resist +38%" }, TextOf(breakdown.Sockets));
        }

        [Fact]
        public void Breakdown_set_bonuses_is_empty_on_an_item_that_is_not_part_of_a_set()
        {
            Assert.Empty(TooltipEngine.Embedded.Breakdown(Item(), Player()).SetBonuses);
        }

        [Fact]
        public void An_earned_set_tier_shows_up_under_set_bonuses()
        {
            // An unearned tier keeps STATLIST_SET (0x2000); earning it clears the bit, so 0x40
            // alone on state 165 is exactly an earned tier.
            Unit item = Item();
            item.StatsLists.Add(
                new UnitStatList(ItemStatListStates.ItemSet1, ItemStatListFlags.Magic)
                    .Add(39, 11));

            Assert.Equal(
                new[] { "Fire Resist +11%" },
                TextOf(TooltipEngine.Embedded.Breakdown(item, Player()).SetBonuses));
        }

        [Fact]
        public void An_unearned_set_tier_is_excluded()
        {
            Unit item = Item();
            item.StatsLists.Add(
                new UnitStatList(
                        ItemStatListStates.ItemSet1,
                        ItemStatListFlags.Magic | ItemStatListFlags.Set)
                    .Add(39, 11));

            Assert.Empty(TooltipEngine.Embedded.Breakdown(item, Player()).SetBonuses);
        }

        // =================================================================
        // The record can be built in code, not only parsed
        // =================================================================

        [Fact]
        public void A_record_built_in_code_renders_the_same_as_the_parsed_one()
        {
            var item = new Unit();
            item.UnitType = 4;
            item.ClassId = 330;
            item.Quality = ItemQualityNo.Magic;
            item.ItemFlags = ItemRecordFlags.Identified;
            item.MagicPrefix[0] = 962;
            item.MagicSuffix[0] = 121;

            item.StatsLists.Add(
                new UnitStatList(0, ItemStatListFlags.Extended)
                    .Add(31, 120).Add(72, 40).Add(73, 62));
            item.StatsLists.Add(
                new UnitStatList(0, ItemStatListFlags.Magic)
                    .Add(16, 150).Add(39, 25).Add(80, 30));

            var socket = new Unit();
            socket.UnitType = 4;
            socket.ClassId = 604;
            socket.StatsLists.Add(new UnitStatList(0, ItemStatListFlags.Magic).Add(39, 38));
            item.Items.Add(socket);

            Assert.Equal(
                TooltipEngine.Embedded.Render(Item(), Player()).Text,
                TooltipEngine.Embedded.Render(item, Player()).Text);
        }

        [Fact]
        public void The_graphics_index_is_read_and_round_trips()
        {
            // bInvGfxIdx. Only rings, amulets, jewels and charms have a non-zero itemtypes
            // VarInvGfx, so this is the one field that decides rin1 from rin5 — and nothing else
            // in the document implies it.
            Unit ring = Unit.FromJson(
                @"{ ""classId"": 1, ""gfxIndex"": 4, ""statsLists"": [] }");

            Assert.Equal(4, ring.GfxIndex);
            Assert.Equal(4, Unit.FromJson(ring.ToJson()).GfxIndex);
        }

        [Fact]
        public void An_absent_graphics_index_is_zero_not_negative()
        {
            // 0 is a REAL variant (the first), and the producer emits the field unconditionally,
            // so absence means "the first one" rather than "unknown" — unlike fileIndex, where -1
            // is the sentinel.
            Assert.Equal(0, Unit.FromJson(@"{ ""classId"": 1 }").GfxIndex);
        }

        [Fact]
        public void A_record_round_trips_through_its_own_json()
        {
            Assert.Equal(
                TooltipEngine.Embedded.Render(Item(), Player()).Text,
                TooltipEngine.Embedded.Render(Unit.FromJson(Item().ToJson()), Player()).Text);
        }

        // =================================================================
        // The game tables are public; the engine is not
        // =================================================================

        [Fact]
        public void The_tables_are_reachable_for_lookups_the_library_does_not_do()
        {
            TooltipEngine engine = TooltipEngine.Embedded;

            int classId = engine.Items.ClassIdForCode("lrg");

            // A raw cell, the way a consumer wanting its own lookup would read one. TryResolve
            // says which of the three item files a classId lands in, and at what row.
            TxtFile file;
            int row;
            Assert.True(engine.Items.TryResolve(classId, out file, out row));
            Assert.Equal("invlrg", file.GetString(row, "invfile"));

            // The same cell through the classId-indexed view.
            Assert.Equal("invlrg", engine.Items.GetString(classId, "invfile"));

            // And the typed views over them.
            Assert.Equal("shie", engine.Items.PrimaryTypeCode(classId));
            Assert.True(engine.Types.Row("gem") >= 0);
            Assert.Equal(21, new ColorTable(engine.Data.Colors).RowCount);
        }

        // =================================================================
        // Requirements, tier and the type tree
        // =================================================================

        [Fact]
        public void Strength_and_dexterity_are_the_same_number_for_every_viewer()
        {
            TooltipEngine engine = TooltipEngine.Embedded;

            int reqstr = engine.Items.GetInt(engine.Items.ClassIdForCode("lrg"), "reqstr");
            Assert.True(reqstr > 0, "lrg has no reqstr to test with");

            // No viewer, a weak viewer and a strong one all read the same NUMBER.
            Assert.Equal(reqstr, engine.Requirements(Item()).Strength);
            Assert.Equal(reqstr, engine.Requirements(Item(), Player()).Strength);
        }

        [Fact]
        public void Dexterity_comes_from_the_same_fold_as_strength()
        {
            TooltipEngine engine = TooltipEngine.Embedded;

            // Scimitar: reqdex 21, and no reqstr percent stat on a bare record, so the fold is
            // the identity and the number is the table's.
            Unit scimitar = Unit.FromJson(
                @"{ ""unitType"": 4, ""classId"": " + engine.Items.ClassIdForCode("scm")
                + @", ""quality"": 2, ""itemFlags"": 16, ""statsLists"": [] }");

            Assert.Equal(21, engine.Requirements(scimitar).Dexterity);
        }

        [Fact]
        public void The_required_level_matches_what_the_tooltip_prints()
        {
            // The rendered tooltip for this fixture says "Required Level: 24", so the structured
            // answer has to agree — one of them being wrong would otherwise go unnoticed.
            Assert.Equal(24, TooltipEngine.Embedded.Requirements(Item(), Player()).Level);
            Assert.Contains(
                "Required Level: 24", TooltipEngine.Embedded.Render(Item(), Player()).Text);
        }

        [Fact]
        public void Class_restriction_is_the_item_types_class_or_none()
        {
            TooltipEngine engine = TooltipEngine.Embedded;

            // A Large Shield is not class-restricted.
            Assert.Equal(
                EquipRequirements.NoClassRestriction,
                engine.Requirements(Item()).ClassRestriction);

            // The restriction is a property of the itemtype, not the item: `pala` carries Class
            // `pal`, so every paladin shield inherits it.
            Assert.Equal("pal", engine.Types.ClassCode(engine.Types.Row("pala")));
        }

        [Fact]
        public void Whether_a_requirement_is_met_does_depend_on_the_viewer()
        {
            TooltipEngine engine = TooltipEngine.Embedded;

            // The fixture player has 60 strength; lrg needs 34.
            Assert.True(engine.Requirements(Item(), Player()).MetStrength);

            // With no viewer at all the stats read as 0, so nothing is met (0x625483).
            Assert.False(engine.Requirements(Item()).MetStrength);
        }

        [Theory]
        [InlineData("cap", ItemTier.Normal)]
        [InlineData("xap", ItemTier.Exceptional)]
        [InlineData("uap", ItemTier.Elite)]
        [InlineData("lrg", ItemTier.Normal)]
        public void Tier_comes_from_the_normcode_ubercode_ultracode_triple(string code, ItemTier expected)
        {
            TooltipEngine engine = TooltipEngine.Embedded;

            Assert.Equal(expected, engine.Items.Tier(engine.Items.ClassIdForCode(code)));
        }

        [Theory]
        [InlineData("qf1")]   // Khalim's Flail — normcode `fla`, no uber/ultra
        [InlineData("qf2")]   // Khalim's Will
        [InlineData("gpv")]   // a gem: misc.txt has no tier columns at all
        [InlineData("r01")]   // a rune, likewise
        public void An_item_that_matches_no_tier_code_reads_as_normal(string code)
        {
            // 153 shipped rows are in this position — all 151 misc rows plus the two Khalim quest
            // weapons. Normal is a deliberate fallback, not a classification.
            TooltipEngine engine = TooltipEngine.Embedded;

            Assert.Equal(ItemTier.Normal, engine.Items.Tier(engine.Items.ClassIdForCode(code)));
        }

        [Fact]
        public void An_items_two_type_codes_are_readable()
        {
            TooltipEngine engine = TooltipEngine.Embedded;
            int gem = engine.Items.ClassIdForCode("gpv");

            Assert.Equal("shie", engine.Items.PrimaryTypeCode(engine.Items.ClassIdForCode("lrg")));

            // A perfect amethyst carries both: `gema` (amethyst) and `gem4` (perfect). The two
            // axes are what make type2 worth reading — colour and grade are separate hierarchies.
            Assert.Equal("gema", engine.Items.PrimaryTypeCode(gem));
            Assert.Equal("gem4", engine.Items.SecondaryTypeCode(gem));

            Assert.Equal("gemr", engine.Items.PrimaryTypeCode(engine.Items.ClassIdForCode("gpr")));
        }

        [Fact]
        public void Descendants_includes_the_type_itself_and_everything_under_it()
        {
            ItemTypeTree types = TooltipEngine.Embedded.Types;

            int gem = types.Row("gem");
            IReadOnlyList<int> under = types.Descendants(gem);

            // Reflexive — "all gems" has to include `gem`.
            Assert.Contains(gem, under);

            // gem0..gem4 chain up to it via equiv.
            Assert.Contains(types.Row("gem4"), under);

            // A rune does not: it lives under `sock`, which is why the gem tint excludes runes.
            Assert.DoesNotContain(types.Row("rune"), under);

            // Descendants and IsUnder read the same closure, so they cannot disagree.
            for (int row = 0; row < types.RowCount; ++row)
            {
                Assert.Equal(types.IsUnder(row, gem), under.Contains(row));
            }
        }

        [Fact]
        public void ClassIdsOfType_finds_every_item_at_or_below_a_type()
        {
            TooltipEngine engine = TooltipEngine.Embedded;

            IReadOnlyList<int> swords = engine.ClassIdsOfType("swor");

            Assert.NotEmpty(swords);

            // Every tier of the same family is in, which is the point of asking by type.
            Assert.Contains(engine.Items.ClassIdForCode("ssd"), swords);

            // And nothing that is not a sword.
            Assert.DoesNotContain(engine.Items.ClassIdForCode("lrg"), swords);
            Assert.DoesNotContain(engine.Items.ClassIdForCode("gpv"), swords);

            // The list agrees with the per-item test the engine itself uses.
            foreach (int classId in swords)
            {
                Assert.True(engine.Types.IsOfType(
                    engine.Types.Row(engine.Items.PrimaryTypeCode(classId)),
                    engine.Types.Row(engine.Items.SecondaryTypeCode(classId)),
                    engine.Types.Row("swor")));
            }
        }

        [Fact]
        public void An_unknown_type_code_yields_nothing_rather_than_everything()
        {
            Assert.Empty(TooltipEngine.Embedded.ClassIdsOfType("zzzz"));
            Assert.Empty(TooltipEngine.Embedded.Types.Descendants(-1));
        }

        // =================================================================
        // IUnit is the contract; Unit is one implementation of it
        // =================================================================

        /// <summary>
        /// An IUnit backed by nothing but closures — no Unit anywhere, no collections copied from
        /// one. If the engine can render this, a consumer holding unit state in its own shape can
        /// implement over that shape instead of marshalling into ours.
        /// </summary>
        private sealed class ComputedUnit : IUnit
        {
            public int UnitType { get { return 4; } }
            public int ClassId { get { return 330; } }
            public string Code { get { return string.Empty; } }
            public int Quality { get { return ItemQualityNo.Magic; } }
            public ItemRecordFlags ItemFlags { get { return ItemRecordFlags.Identified; } }
            public int FileIndex { get { return -1; } }
            public int ItemLevel { get { return -1; } }
            public int RarePrefix { get { return 0; } }
            public int RareSuffix { get { return 0; } }
            public int AutoAffix { get { return 0; } }
            public int Format { get { return 0; } }
            public IReadOnlyList<int> MagicPrefix { get { return new[] { 962, 0, 0 }; } }
            public IReadOnlyList<int> MagicSuffix { get { return new[] { 121, 0, 0 }; } }
            public int EarLevel { get { return 0; } }
            public string PlayerName { get { return string.Empty; } }
            public int GfxIndex { get { return 0; } }
            public uint FlagsEx { get { return Unit.UnitFlagExpansion; } }

            public IReadOnlyList<IUnitStatList> StatsLists
            {
                get
                {
                    return new IUnitStatList[]
                    {
                        new ComputedStatList(
                            0,
                            ItemStatListFlags.Extended,
                            new IUnitStat[] { new ComputedStat(31, 120), new ComputedStat(72, 40),
                                    new ComputedStat(73, 62) }),
                        new ComputedStatList(
                            0,
                            ItemStatListFlags.Magic,
                            new IUnitStat[] { new ComputedStat(16, 150), new ComputedStat(39, 25),
                                    new ComputedStat(80, 30) }),
                    };
                }
            }

            public IReadOnlyList<IUnitStat> Stats { get { return new IUnitStat[0]; } }

            public IReadOnlyList<IUnit> Items { get { return new IUnit[0]; } }

            public int Location { get { return -1; } }

            public int X { get { return 0; } }

            // NOT empty, and NOT UnitSkill. ReadViewer used to iterate this as `foreach
            // (UnitSkill ...)`, which compiles — foreach inserts a downcast — and threw
            // InvalidCastException for any implementation that did not happen to use our class.
            // An empty list here would have hidden that, so it carries a real skill.
            public IReadOnlyList<IUnitSkill> Skills
            {
                get { return new IUnitSkill[] { new ComputedSkill(117, 20) }; }
            }
        }

        private sealed class ComputedSkill : IUnitSkill
        {
            public ComputedSkill(int skill, int level)
            {
                Skill = skill;
                Level = level;
            }

            public int Skill { get; private set; }
            public int Level { get; private set; }
        }

        private sealed class ComputedStatList : IUnitStatList
        {
            private readonly IUnitStat[] _stats;

            public ComputedStatList(int stateNo, uint flags, IUnitStat[] stats)
            {
                StateNo = stateNo;
                Flags = flags;
                _stats = stats;
            }

            public int StateNo { get; private set; }
            public uint Flags { get; private set; }
            public IReadOnlyList<IUnitStat> Stats { get { return _stats; } }
        }

        private sealed class ComputedStat : IUnitStat
        {
            public ComputedStat(int id, int value)
            {
                Id = id;
                Value = value;
            }

            public int Id { get; private set; }
            public int Value { get; private set; }
            public int Layer { get { return 0; } }
        }

        [Fact]
        public void A_custom_IUnit_renders_the_same_as_the_deserialised_one()
        {
            TooltipEngine engine = TooltipEngine.Embedded;

            // Same item, two unrelated implementations of the contract. Sockets are empty here, so
            // compare against the socketless render.
            var options = new TooltipOptions();
            options.IncludeSockets = false;

            Assert.Equal(
                engine.Render(Item(), Player(), options).Text,
                engine.Render(new ComputedUnit(), Player()).Text);
        }

        /// <summary>An IUnit whose affix lists are not three long, which the contract permits.</summary>
        private sealed class ShortAffixUnit : IUnit
        {
            private readonly IReadOnlyList<int> _prefix;
            private readonly IReadOnlyList<int> _suffix;

            public ShortAffixUnit(IReadOnlyList<int> prefix, IReadOnlyList<int> suffix)
            {
                _prefix = prefix;
                _suffix = suffix;
            }

            public int UnitType { get { return 4; } }
            public int ClassId { get { return 330; } }
            public string Code { get { return string.Empty; } }
            public int Quality { get { return ItemQualityNo.Magic; } }
            public ItemRecordFlags ItemFlags { get { return ItemRecordFlags.Identified; } }
            public int FileIndex { get { return -1; } }
            public int ItemLevel { get { return -1; } }
            public int RarePrefix { get { return 0; } }
            public int RareSuffix { get { return 0; } }
            public int AutoAffix { get { return 0; } }
            public int Format { get { return 0; } }
            public IReadOnlyList<int> MagicPrefix { get { return _prefix; } }
            public IReadOnlyList<int> MagicSuffix { get { return _suffix; } }
            public int EarLevel { get { return 0; } }
            public string PlayerName { get { return string.Empty; } }
            public int GfxIndex { get { return 0; } }
            public uint FlagsEx { get { return Unit.UnitFlagExpansion; } }
            public IReadOnlyList<IUnitStatList> StatsLists { get { return new IUnitStatList[0]; } }
            public IReadOnlyList<IUnitStat> Stats { get { return new IUnitStat[0]; } }
            public IReadOnlyList<IUnit> Items { get { return new IUnit[0]; } }

            public int Location { get { return -1; } }

            public int X { get { return 0; } }
            public IReadOnlyList<IUnitSkill> Skills { get { return new IUnitSkill[0]; } }
        }

        [Fact]
        public void An_affix_list_shorter_than_three_slots_reads_the_rest_as_zero()
        {
            // The game struct is wMagicPrefix[3], but the contract is a list, so an implementation
            // need not pad. One slot filled and two absent must mean the same as one filled and
            // two zero.
            TooltipEngine engine = TooltipEngine.Embedded;

            string oneSlot = engine.Render(new ShortAffixUnit(new[] { 962 }, new[] { 121 })).Text;
            string threeSlots =
                engine.Render(new ShortAffixUnit(new[] { 962, 0, 0 }, new[] { 121, 0, 0 })).Text;

            Assert.Equal(threeSlots, oneSlot);
            Assert.Contains("Vigorous Large Shield of Absorption", oneSlot);
        }

        [Fact]
        public void An_affix_list_longer_than_three_slots_ignores_the_extras()
        {
            // The engine reads MaxAffixSlots and stops; a longer list must not shift or overflow.
            TooltipEngine engine = TooltipEngine.Embedded;

            Assert.Equal(
                engine.Render(new ShortAffixUnit(new[] { 962, 0, 0 }, new[] { 121, 0, 0 })).Text,
                engine.Render(
                    new ShortAffixUnit(new[] { 962, 0, 0, 999, 999 }, new[] { 121, 0, 0, 999 })).Text);
        }

        [Fact]
        public void An_empty_affix_list_is_the_same_as_no_affixes()
        {
            TooltipEngine engine = TooltipEngine.Embedded;

            Assert.Equal(
                engine.Render(new ShortAffixUnit(new[] { 0, 0, 0 }, new[] { 0, 0, 0 })).Text,
                engine.Render(new ShortAffixUnit(new int[0], new int[0])).Text);
        }

        [Fact]
        public void A_custom_IUnit_works_on_every_public_entry_point()
        {
            TooltipEngine engine = TooltipEngine.Embedded;
            IUnit unit = new ComputedUnit();

            // As the VIEWER too, which is the path that reads Skills — a custom IUnitSkill has to
            // survive ReadViewer, not just a custom IUnit.
            Assert.Equal(ItemTooltipKind.Generic, engine.Render(Item(), unit).Kind);

            Assert.Equal(ItemTooltipKind.Generic, engine.Render(unit).Kind);
            Assert.Equal("lrg", engine.Appearance(unit).Image);
            Assert.Equal(34, engine.Requirements(unit, Player()).Strength);
            Assert.Equal(new[] { "+120 Defense" }, TextOf(engine.Breakdown(unit, Player()).Base));

            // And it re-serialises, because the writer takes the interface too.
            Assert.Equal(
                engine.Render(unit).Text,
                engine.Render(Unit.FromJson(UnitJson.Write(unit))).Text);
        }

        // =================================================================
        // A wearer's MERGED stats. Its statlist chain is structural — states, but pre-gear
        // attribute values — so requirement checks read the merged set instead.
        // =================================================================

        [Fact]
        public void Merged_stats_overwrite_the_chain_rather_than_adding_to_it()
        {
            // 60 strength on the chain, 90 merged (60 base + 30 from gear the chain cannot see).
            // Summing would give 150 and let the wearer equip things they cannot.
            Unit viewer = Unit.FromJson(
                @"{ ""unitType"": 0, ""classId"": 3,
                    ""statsLists"": [ { ""stateNo"": 0, ""flags"": 2147483648,
                      ""stats"": [ { ""id"": 0, ""value"": 60 }, { ""id"": 12, ""value"": 30 } ] } ],
                    ""stats"": [ { ""id"": 0, ""value"": 90 }, { ""id"": 12, ""value"": 40 } ] }");

            ItemViewer read = ItemRecordReader.ReadViewer(viewer);

            Assert.Equal(90, read.Strength);
            Assert.Equal(40, read.Level);
        }

        [Fact]
        public void Without_merged_stats_the_chain_values_stand()
        {
            // A hand-built viewer, or a producer that sends no merged set.
            Unit viewer = Unit.FromJson(
                @"{ ""unitType"": 0, ""classId"": 3,
                    ""statsLists"": [ { ""stateNo"": 0, ""flags"": 2147483648,
                      ""stats"": [ { ""id"": 0, ""value"": 60 } ] } ] }");

            Assert.Equal(60, ItemRecordReader.ReadViewer(viewer).Strength);
        }

        [Fact]
        public void Merged_stats_do_not_supply_active_states()
        {
            // A state is a statlist node carrying its own dwStateNo. Merged values have no
            // provenance, so nothing can recover it — Holy Shield reads as inactive for a viewer
            // that only sent merged stats. This is a limit of merged data, not a bug: do not
            // "fix" it by inventing a synthetic state.
            Unit merged = Unit.FromJson(
                @"{ ""unitType"": 0, ""classId"": 3,
                    ""stats"": [ { ""id"": 0, ""value"": 90 } ] }");

            Assert.DoesNotContain(101, ItemRecordReader.ReadViewer(merged).ActiveStates);

            // The chain still supplies them when it is present.
            Unit withChain = Unit.FromJson(
                @"{ ""unitType"": 0, ""classId"": 3,
                    ""statsLists"": [ { ""stateNo"": 101, ""flags"": 64, ""stats"": [] } ],
                    ""stats"": [ { ""id"": 0, ""value"": 90 } ] }");

            Assert.Contains(101, ItemRecordReader.ReadViewer(withChain).ActiveStates);
        }

        [Fact]
        public void A_merged_stat_past_int32_narrows_to_the_games_own_bits()
        {
            // Experience at level 99 is ~3.52 billion: past int.MaxValue, inside uint.MaxValue.
            // The game stores int32, so a producer widening it for JSON has to be narrowed back
            // unchecked — the alternative is the value silently reading as 0.
            const long Experience = 3520485421L;

            Unit viewer = Unit.FromJson(
                @"{ ""unitType"": 0, ""classId"": 3,
                    ""stats"": [ { ""id"": 13, ""value"": " + Experience + @" } ] }");

            Assert.Single(viewer.Stats);
            Assert.Equal(unchecked((int)Experience), viewer.Stats[0].Value);

            // And the round trip is lossless in the game's own 32 bits.
            Assert.Equal(
                viewer.Stats[0].Value,
                Unit.FromJson(viewer.ToJson()).Stats[0].Value);
        }

        [Fact]
        public void Merged_stats_reach_the_requirement_checks()
        {
            // The whole point: an item needing 34 strength is unmet on a 20-strength chain and met
            // once the merged set reports the geared 60.
            TooltipEngine engine = TooltipEngine.Embedded;

            const string Chain =
                @"""statsLists"": [ { ""stateNo"": 0, ""flags"": 2147483648,
                    ""stats"": [ { ""id"": 0, ""value"": 20 }, { ""id"": 12, ""value"": 30 } ] } ]";

            Unit weak = Unit.FromJson(@"{ ""unitType"": 0, ""classId"": 3, " + Chain + " }");
            Unit geared = Unit.FromJson(
                @"{ ""unitType"": 0, ""classId"": 3, " + Chain
                + @", ""stats"": [ { ""id"": 0, ""value"": 60 }, { ""id"": 12, ""value"": 30 } ] }");

            Assert.False(engine.Requirements(Item(), weak).MetStrength);
            Assert.True(engine.Requirements(Item(), geared).MetStrength);
        }

        [Fact]
        public void An_item_carries_no_merged_stats()
        {
            // `stats` at the top level is a WEARER field. On an item the same key appears only
            // inside each statlist node, which is a different nesting and must not be picked up.
            Assert.Empty(Item().Stats);
        }

        // =================================================================
        // Parity with the TypeScript reader. These pin cases a divergence sweep found, where the
        // two implementations disagreed on documents neither the producer nor the corpus emits.
        // =================================================================

        [Fact]
        public void An_explicit_null_member_means_the_same_as_an_absent_one()
        {
            // JsonConverter<T>.HandleNull is false for reference types, so `"code": null` bypasses
            // every converter and used to land a null on the DTO — which the engine then
            // dereferenced, turning a malformed document into a NullReferenceException from deep
            // inside a writer. TypeScript coerced to defaults throughout; now both do.
            Unit unit = Unit.FromJson(
                @"{ ""code"": null, ""playerName"": null, ""magicPrefix"": null,
                    ""magicSuffix"": null, ""statsLists"": null, ""stats"": null,
                    ""items"": null, ""skills"": null }");

            Assert.Equal(string.Empty, unit.Code);
            Assert.Equal(string.Empty, unit.PlayerName);
            Assert.Equal(new[] { 0, 0, 0 }, unit.MagicPrefix);
            Assert.Equal(new[] { 0, 0, 0 }, unit.MagicSuffix);
            Assert.Empty(unit.StatsLists);
            Assert.Empty(unit.Stats);
            Assert.Empty(unit.Items);
            Assert.Empty(unit.Skills);

            // And it renders rather than throwing.
            Assert.NotNull(TooltipEngine.Embedded.Render(unit).Text);
        }

        [Fact]
        public void A_skill_entry_without_an_id_is_not_skill_zero()
        {
            // Skill id 0 is Attack, a REAL skill, so an absent `skill` must not read as it —
            // ItemViewer.SkillLevel(0) would then answer for a skill the document never mentioned.
            Unit viewer = Unit.FromJson(@"{ ""skills"": [ { ""level"": 3 } ] }");

            Assert.Equal(-1, viewer.Skills[0].Skill);
        }

        [Fact]
        public void Both_FromJson_overloads_agree_on_a_non_object_root()
        {
            // The string overload used to throw where the JsonElement overload returned a default
            // unit. tools/Reference happens to use the element overload, so the differential never
            // saw it.
            foreach (string root in new[] { "5", "\"hi\"", "[]", "true", "null" })
            {
                Unit fromString = Unit.FromJson(root);

                using (JsonDocument document = JsonDocument.Parse(root))
                {
                    Unit fromElement = Unit.FromJson(document.RootElement);

                    Assert.Equal(fromElement.ClassId, fromString.ClassId);
                    Assert.Equal(fromElement.UnitType, fromString.UnitType);
                    Assert.Equal(fromElement.FlagsEx, fromString.FlagsEx);
                }
            }
        }

        [Fact]
        public void Socket_contributions_wrap_at_int32_like_every_other_sum()
        {
            // The game stores stats as int32 and its sums wrap. Two fillers each carrying
            // int.MaxValue must fold to -2, not to a 64-bit total — every other accumulation site
            // in both engines narrows, and this one used not to on the TypeScript side.
            Unit item = Unit.FromJson(
                @"{ ""unitType"": 4, ""classId"": 330, ""quality"": 2, ""itemFlags"": 16,
                    ""statsLists"": [],
                    ""items"": [
                      { ""unitType"": 4, ""classId"": 604, ""statsLists"": [
                        { ""stateNo"": 0, ""flags"": 64,
                          ""stats"": [ { ""id"": 39, ""value"": 2147483647 } ] } ] },
                      { ""unitType"": 4, ""classId"": 604, ""statsLists"": [
                        { ""stateNo"": 0, ""flags"": 64,
                          ""stats"": [ { ""id"": 39, ""value"": 2147483647 } ] } ] } ] }");

            // Contains, not Equal: the fixture has no durability stat, so the block also carries
            // an "Indestructible" line. The wrap is the point here.
            Assert.Contains(
                "Fire Resist -2%",
                TextOf(TooltipEngine.Embedded.Breakdown(item).Sockets));
        }

        [Fact]
        public void A_rendered_tooltip_does_not_change_when_the_options_object_does()
        {
            // Lines are composed eagerly, so every knob is baked in at Render time. Reading the
            // options object again from the Text getter made the result change under the caller,
            // and TypeScript captured by value — so the same sequence gave two different strings.
            var options = new TooltipOptions();
            options.QuestColorPrefix = false;

            Tooltip tip = TooltipEngine.Embedded.Render(Item(), Player(), options);
            string before = tip.Text;
            string beforeColored = tip.ColoredText;

            options.QuestColorPrefix = true;

            Assert.Equal(before, tip.Text);
            Assert.Equal(beforeColored, tip.ColoredText);
        }

        [Fact]
        public void A_null_options_object_means_the_defaults()
        {
            // Passing the default EXPLICITLY is the subject of this test, so the inspector's
            // "redundant argument" is exactly the thing being asserted. Suppressed rather than
            // worked around: a local typed to null only trades it for "expression is always null".
            // ReSharper disable RedundantArgumentDefaultValue
            Assert.Equal(
                TooltipEngine.Embedded.Render(Item(), Player()).Text,
                TooltipEngine.Embedded.Render(Item(), Player(), null).Text);

            Assert.NotEmpty(TooltipEngine.Embedded.Breakdown(Item(), Player(), null).Magic);

            // ReSharper restore RedundantArgumentDefaultValue
        }

        [Fact]
        public void A_null_item_is_rejected_on_every_entry_point()
        {
            TooltipEngine engine = TooltipEngine.Embedded;

            Assert.Throws<ArgumentNullException>(() => engine.Render(null));
            Assert.Throws<ArgumentNullException>(() => engine.Appearance(null));
            Assert.Throws<ArgumentNullException>(() => engine.Requirements(null));
            Assert.Throws<ArgumentNullException>(() => engine.Breakdown(null));
        }

        [Fact]
        public void An_engine_can_be_built_over_tables_the_caller_supplies()
        {
            // The portable form: hand over a D2DataFiles from wherever — a modded extraction,
            // fetched bytes, a bundled archive — rather than going through the filesystem.
            // TypeScript has the same pair, which it did not before a parity sweep found the gap.
            TooltipEngine supplied = TooltipEngine.FromData(D2DataFiles.LoadEmbedded());

            Assert.Equal(
                TooltipEngine.Embedded.Render(Item(), Player()).Text,
                supplied.Render(Item(), Player()).Text);

            // And it is a separate engine, with its own tables.
            Assert.NotSame(TooltipEngine.Embedded, supplied);
            Assert.NotSame(TooltipEngine.Embedded.Data, supplied.Data);
        }

        [Fact]
        public void FromData_rejects_a_null_table_set()
        {
            Assert.Throws<ArgumentNullException>(() => TooltipEngine.FromData(null));
        }

        [Fact]
        public void A_viewer_is_optional()
        {
            // GetStatUnsignedValue returns 0 for a null unit (0x625483) rather than halting, so a
            // viewerless render still produces the whole tooltip.
            Tooltip tip = TooltipEngine.Embedded.Render(Item());

            Assert.Equal(ItemTooltipKind.Generic, tip.Kind);
            Assert.Contains("Vigorous Large Shield of Absorption", tip.Text);
        }

        [Fact]
        public void A_viewerless_render_paints_no_requirement_red()
        {
            // The binary would paint all three red here — GetStatUnsignedValue reads 0 off a null
            // unit and 0x62ebd5 / 0x62ec31 gate on `> 0` — but the game never reaches that branch,
            // and red asserts a viewer failed a check nobody ran. See RecordSections
            // .IsRequirementUnmet.
            Tooltip tip = TooltipEngine.Embedded.Render(Item());

            foreach (ItemTooltipLine line in tip.Lines)
            {
                if (line.Section == ItemTooltipSection.RequiredLevel
                    || line.Section == ItemTooltipSection.RequiredStrength
                    || line.Section == ItemTooltipSection.RequiredDexterity
                    || line.Section == ItemTooltipSection.ClassRestriction)
                {
                    Assert.Equal(ItemTooltipColor.White, line.Color);
                }
            }

            // The lines are really there, or the loop above proves nothing.
            Assert.Contains("Required Strength: 34", tip.Text);
            Assert.Contains("Required Level: 24", tip.Text);
        }

        [Fact]
        public void A_viewer_who_falls_short_still_gets_red()
        {
            // The deviation is scoped to a MISSING viewer. Supply one and 0x62eaf0's answer stands.
            Unit weakling = Unit.FromJson(
                @"{ ""unitType"": 0, ""classId"": 3, ""level"": 1,
                    ""statsLists"": [ { ""stateNo"": 0, ""flags"": 2147483648,
                        ""stats"": [ { ""id"": 0, ""value"": 10 }, { ""id"": 2, ""value"": 10 },
                                     { ""id"": 12, ""value"": 1 } ] } ] }");

            Tooltip tip = TooltipEngine.Embedded.Render(Item(), weakling);

            ItemTooltipLine strength = null;
            ItemTooltipLine level = null;
            foreach (ItemTooltipLine line in tip.Lines)
            {
                if (line.Section == ItemTooltipSection.RequiredStrength) strength = line;
                if (line.Section == ItemTooltipSection.RequiredLevel) level = line;
            }

            Assert.NotNull(strength);
            Assert.NotNull(level);
            Assert.Equal(ItemTooltipColor.Red, strength.Color);
            Assert.Equal(ItemTooltipColor.Red, level.Color);
        }
    }
}
