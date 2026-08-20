using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace D2ItemToolkit.Tests
{
    /// <summary>
    /// INV_FormatSocketFillerDesc 0x4865d0 and the property engine behind it.
    /// </summary>
    public class SocketFillerTests
    {
        private static readonly D2DataFiles Data = D2DataFiles.LoadEmbedded();

        private static readonly ItemTable Items = new ItemTable(
            Data.Weapons, Data.Armor, Data.Misc);

        private static readonly ItemTypeTree Types = new ItemTypeTree(Data.ItemTypes);

        private static string Describe(string code)
        {
            var item = new ItemIdentity();
            item.ClassId = Items.ClassIdForCode(code);
            item.Code = code;
            item.Flags = ItemRecordFlags.Identified;
            Assert.True(item.ClassId >= 0, "no items row for " + code);

            var sections = new RecordSections(
                Data, Items, Types, item, null, new Dictionary<int, int>(), null, null, null);

            return sections.GetSection(ItemTooltipSection.SocketFillerDescription);
        }

        [Fact]
        public void The_properties_table_loads_and_resolves_names()
        {
            var applier = new PropertyApplier(Data, Items, Types);

            // properties.bin carries 268 records.
            Assert.Equal(268, applier.Properties.RowCount);

            int resAll = applier.Properties.RowForCode("res-all");
            Assert.True(resAll >= 0);

            // "res-all" fans out to the four single resistances, so several sets carry a stat.
            PropertiesTable.Row row = applier.Properties[resAll];
            Assert.True(row.Stat.Count(s => s >= 0) >= 4, string.Join(",", row.Stat));
        }

        [Fact]
        public void A_perfect_ruby_describes_what_it_does_in_each_destination()
        {
            // gpr is the Perfect Ruby: fire damage in a weapon, life in armour, fire resist in a shield.
            string text = Describe("gpr");

            Assert.False(string.IsNullOrEmpty(text), "no description produced");
            Assert.Contains("Fire", text, System.StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void A_rune_describes_its_destinations_too()
        {
            // r08 is Ral: fire resist in armour and shields, fire damage in a weapon.
            string text = Describe("r08");

            Assert.False(string.IsNullOrEmpty(text), "no description produced");
            Assert.Contains("Fire", text, System.StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void A_perfect_ruby_matches_the_values_the_game_grants()
        {
            // Real 1.14d Perfect Ruby: weapon +15-20 fire damage, armour/helm +38 life,
            // shield +40% fire resist.
            // The weapon block is ONE line, so SKILLDESC_AppendStatBuffText takes the 0x4e64b9 path
            // and prefixes the label instead of splicing it before the final line.
            Assert.Equal(
                "\nShields: Fire Resist +40%"
                + "\nHelms: +38 to Life"
                + "\nArmor: +38 to Life"
                + "\nWeapons: Adds 15-20 fire damage"
                + "\n\nCan be Inserted into Socketed Items\n",
                Describe("gpr"));
        }

        [Fact]
        public void A_ral_rune_matches_the_values_the_game_grants()
        {
            // Real Ral: weapon +5-30 fire damage, armour/helm +30% fire resist, shield +35%.
            string text = Describe("r08");

            Assert.Contains("Shields: Fire Resist +35%", text, System.StringComparison.Ordinal);
            Assert.Contains("Helms: Fire Resist +30%", text, System.StringComparison.Ordinal);
            Assert.Contains("Armor: Fire Resist +30%", text, System.StringComparison.Ordinal);
            Assert.Contains("Weapons: Adds 5-30 fire damage", text, System.StringComparison.Ordinal);
        }

        [Fact]
        public void An_unidentified_item_says_so_and_an_identified_one_does_not()
        {
            var item = new ItemIdentity();
            item.ClassId = Items.ClassIdForCode("lrg");
            item.Quality = ItemQualityNo.Unique;
            item.FileIndex = 0;

            var sections = new RecordSections(
                Data, Items, Types, item, null, new Dictionary<int, int>(), null, null, null);

            Assert.Equal(
                "Unidentified\n", sections.GetSection(ItemTooltipSection.Unidentified));

            item.Flags = ItemRecordFlags.Identified;
            Assert.Null(
                new RecordSections(
                        Data, Items, Types, item, null, new Dictionary<int, int>(),
                        null, null, null)
                    .GetSection(ItemTooltipSection.Unidentified));
        }

        [Fact]
        public void A_non_filler_writes_nothing()
        {
            Assert.Null(Describe("lrg"));
            Assert.Null(Describe("ssd"));
        }

        [Fact]
        public void Every_gem_and_rune_produces_something()
        {
            var empty = new List<string>();

            for (int row = 0; row < Data.Gems.RowCount; ++row)
            {
                string code = Data.Gems.GetString(row, "code").Trim();
                if (code.Length == 0 || Items.ClassIdForCode(code) < 0)
                {
                    continue;
                }

                if (string.IsNullOrEmpty(Describe(code)))
                {
                    empty.Add(code);
                }
            }

            Assert.Empty(empty);
        }

        [Fact]
        public void No_gem_property_needs_the_item_seed()
        {
            // A property with min != max would have to be rolled from the item seed. If no gem or
            // rune mod is ranged, the seed is irrelevant to socket-filler descriptions.
            var applier = new PropertyApplier(Data, Items, Types);
            var gems = new GemTable(Data.Gems, Items);
            gems.ResolvePropertyCodesWith(applier.Properties.RowForCode);

            var stats = new SortedDictionary<int, int>();

            for (int row = 0; row < Data.Gems.RowCount; ++row)
            {
                int classId = Items.ClassIdForCode(Data.Gems.GetString(row, "code").Trim());
                if (classId < 0)
                {
                    continue;
                }

                var item = new ItemIdentity();
                item.ClassId = classId;

                for (int slot = 0; slot < 3; ++slot)
                {
                    foreach (ItemProperty property in gems.Properties(row, slot))
                    {
                        if (property.PropertyId < 0)
                        {
                            break;
                        }

                        applier.Apply(PropertyApplier.PropModeGem, item, property, stats);
                    }
                }
            }

            Assert.Empty(applier.RolledRanges);
        }

        [Fact]
        public void No_gem_property_reaches_an_unimplemented_func()
        {
            var applier = new PropertyApplier(Data, Items, Types);
            var gems = new GemTable(Data.Gems, Items);
            gems.ResolvePropertyCodesWith(applier.Properties.RowForCode);

            var stats = new SortedDictionary<int, int>();

            for (int row = 0; row < Data.Gems.RowCount; ++row)
            {
                string code = Data.Gems.GetString(row, "code").Trim();
                int classId = Items.ClassIdForCode(code);
                if (classId < 0)
                {
                    continue;
                }

                var item = new ItemIdentity();
                item.ClassId = classId;
                item.Code = code;

                for (int slot = 0; slot < 3; ++slot)
                {
                    foreach (ItemProperty property in gems.Properties(row, slot))
                    {
                        if (property.PropertyId < 0)
                        {
                            break;
                        }

                        applier.Apply(PropertyApplier.PropModeGem, item, property, stats);
                    }
                }
            }

            Assert.Empty(applier.UnsupportedFunc);
        }

        [Fact]
        public void A_jewel_still_gets_the_socket_filler_trailer()
        {
            // LoadItemDesc routes every IsOfType(sock) item here (0x48e58c), and
            // SKILLDESC_BuildMagicAffixDesc bails at 0x4e6a7a for a jewel with the buffer merely
            // emptied (0x4e68bc). The 11080 + 3998 tail at 0x48661f is appended regardless.
            var item = new ItemIdentity();
            item.ClassId = Items.ClassIdForCode("jew");
            item.Code = "jew";
            item.Quality = ItemQualityNo.Magic;
            item.Flags = ItemRecordFlags.Identified;

            var sections = new RecordSections(
                Data, Items, Types, item, null, new Dictionary<int, int>(), null, null, null);

            Assert.Equal(
                Data.Strings.GetByIndex(SectionStringIds.SocketFillerClose)
                + Data.Strings.GetByIndex(DescStringIds.Newline),
                sections.GetSection(ItemTooltipSection.SocketFillerDescription));
        }

        [Fact]
        public void A_non_socket_item_gets_no_trailer_at_all()
        {
            // The gate is IsOfType(item, 53) in the CALLER (0x48e58c); anything else never reaches
            // INV_FormatSocketFillerDesc, whose own miss is a halt rather than a fallback.
            var item = new ItemIdentity();
            item.ClassId = Items.ClassIdForCode("lrg");
            item.Code = "lrg";
            item.Flags = ItemRecordFlags.Identified;

            var sections = new RecordSections(
                Data, Items, Types, item, null, new Dictionary<int, int>(), null, null, null);

            Assert.Null(sections.GetSection(ItemTooltipSection.SocketFillerDescription));
        }

        // =================================================================
        // Gems and runes join their per-slot lines differently.
        // SKILLDESC_BuildMagicAffixDesc 0x4e6850 routes gems to 0x4e67d0 (pushes 0 at 0x4e67f3)
        // and runes to 0x4e6720 (pushes 1 at 0x4e6755). That reaches BuildStatBuffDesc as a8:
        // 1 terminates every line with 3998, 0 separates with 3852+3995 (", ") and terminates
        // nothing, so the slot renders as ONE line.
        // =================================================================

        [Fact]
        public void A_gem_slot_with_two_stats_joins_them_inline()
        {
            // Perfect Skull is the visible case: manasteal+lifesteal on weapons and
            // regen+regen-mana on helms are independent stats, so each slot yields two lines.
            string text = Describe("skz");

            Assert.Contains(
                "Weapons: 4% Life stolen per hit, 3% Mana stolen per hit\n",
                text, System.StringComparison.Ordinal);

            Assert.Contains(
                "Helms: Regenerate Mana 19%, Replenish Life +5\n",
                text, System.StringComparison.Ordinal);
        }

        [Fact]
        public void The_whole_skull_block_renders_exactly()
        {
            Assert.Equal(
                "\nShields: Attacker Takes Damage of 20\n"
                + "Helms: Regenerate Mana 19%, Replenish Life +5\n"
                + "Armor: Regenerate Mana 19%, Replenish Life +5\n"
                + "Weapons: 4% Life stolen per hit, 3% Mana stolen per hit\n"
                + "\nCan be Inserted into Socketed Items\n",
                Describe("skz"));
        }

        [Theory]
        [InlineData("skc")]
        [InlineData("skf")]
        [InlineData("sku")]
        [InlineData("skl")]
        [InlineData("skz")]
        public void Every_skull_joins_its_paired_slots_inline(string code)
        {
            string text = Describe(code);

            // Two stats on one row rather than two rows: the separator appears, and no slot label
            // is ever left stranded on a line of its own.
            Assert.Contains(", ", text, System.StringComparison.Ordinal);
        }

        [Fact]
        public void A_rune_keeps_the_newline_join()
        {
            // The control case. El Rune has two independent mods per slot, but the rune arm pushes
            // 1, so each stat keeps its own line. If this ever renders ", " the routing is wrong.
            string text = Describe("r01");

            Assert.False(string.IsNullOrEmpty(text));
            Assert.DoesNotContain(", ", text, System.StringComparison.Ordinal);
        }

        [Fact]
        public void A_single_stat_gem_slot_is_unchanged_by_the_join_mode()
        {
            // Every non-Skull gem yields one line per slot, so both modes produce the same bytes.
            string ruby = Describe("gpr");

            Assert.False(string.IsNullOrEmpty(ruby));
            Assert.DoesNotContain(", ", ruby, System.StringComparison.Ordinal);
        }

        // =================================================================
        // gems row 0 is a real row. TXT_Gems_GetLine 0x6372c0 rejects only `>= recordCount`
        // (0x6372cc) and exactly -1 (0x6372d1); the `jle` that also drops 0 is at 0x4866e9 and
        // belongs to INV_FormatRunewordName, behind an IsOfType(rune) test at 0x4866d6.
        // =================================================================

        [Fact]
        public void The_first_gems_row_is_a_real_gem()
        {
            // TXT_AllocTxt_gems 0x637279 writes the row index into items +0xF0 and writes a
            // literal 0 on its first iteration, so gcv's offset genuinely is 0.
            Assert.Equal("gcv", Data.Gems.GetString(0, "code").Trim());
        }

        [Fact]
        public void A_chipped_amethyst_describes_all_four_destinations()
        {
            Assert.Equal(
                "\nShields: +8 Defense"
                + "\nHelms: +3 to Strength"
                + "\nArmor: +3 to Strength"
                + "\nWeapons: +40 to Attack Rating"
                + "\n\nCan be Inserted into Socketed Items\n",
                Describe("gcv"));
        }

        [Fact]
        public void Row_zero_is_not_confused_with_not_a_filler()
        {
            // The old `> 0` gate collapsed "row 0" into "no gems row", so gcv rendered only the
            // trailer while every other amethyst rendered in full.
            string chipped = Describe("gcv");
            string flawed = Describe("gfv");

            Assert.False(string.IsNullOrEmpty(chipped));
            Assert.Contains("Shields:", chipped, System.StringComparison.Ordinal);
            Assert.Equal(
                flawed.Split('\n').Length, chipped.Split('\n').Length);
        }

        [Fact]
        public void A_rune_letter_still_ignores_row_zero()
        {
            // RowForRuneClassId keeps the 0x4866e9 `jle`. No rune occupies row 0 (it is gcv), so
            // this is faithful and unobservable, but the two lookups must stay distinct.
            var gems = new GemTable(Data.Gems, Items);

            Assert.Equal(0, gems.RowForFillerClassId(Items.ClassIdForCode("gcv")));
            Assert.Equal(-1, gems.RowForRuneClassId(Items.ClassIdForCode("gcv")));
        }
    }
}
