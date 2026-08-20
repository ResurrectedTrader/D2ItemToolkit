using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace D2ItemToolkit.Tests
{
    public class RealDataTooltipTests
    {
        private static readonly D2DataFiles Data = D2DataFiles.LoadEmbedded();

        private sealed class Values : IStatValueSource
        {
            public readonly Dictionary<int, int> Base = new Dictionary<int, int>();

            // Empty today, and deliberately kept rather than collapsed back into `return 0`:
            // that hardcoding is the bug the GetPlayerStatValue comment below records, and the
            // seam is what lets a test set a value without reintroducing it.
            // ReSharper disable once CollectionNeverUpdated.Local
            public readonly Dictionary<int, int> Item = new Dictionary<int, int>();

            // ReSharper disable once CollectionNeverUpdated.Local
            public readonly Dictionary<int, int> Player = new Dictionary<int, int>();
            public int Class = 1;
            public int ItemType = -1;

            public int GetBaseStatValue(int statId, int layer)
            {
                int v;
                return Base.TryGetValue(statId, out v) ? v : 0;
            }

            // The op 2-5 scale reads the PLAYER (0x4e4c93). Hardcoding 0 here silently asserted
            // that every "(Based on Character Level)" line renders its number as 0.
            public int GetPlayerStatValue(int statId)
            {
                int v;
                return Player.TryGetValue(statId, out v) ? v : 0;
            }

            public int GetItemStatValue(int statId)
            {
                int v;
                return Item.TryGetValue(statId, out v) ? v : 0;
            }

            // Non-zero, so the "Indestructible" tail is not appended to every block.
            public int MaxDurability = 20;

            public int PlayerClass { get { return Class; } }
            public bool IsItemOfType(int itemTypeId) { return itemTypeId == ItemType; }
            public bool DescribedUnitIsItem { get { return true; } }
            public bool ItemTableAllowsDurability { get { return true; } }
            public int GetTxtMaxDurability() { return MaxDurability; }
        }

        private sealed class Sections : IItemTooltipSections
        {
            private readonly Dictionary<ItemTooltipSection, string> _texts =
                new Dictionary<ItemTooltipSection, string>();

            private readonly HashSet<ItemTooltipSection> _unmet =
                new HashSet<ItemTooltipSection>();

            public string LineTerminator
            {
                get { return Data.Strings.GetByIndex(DescStringIds.Newline); }
            }

            public Sections Set(ItemTooltipSection section, string text)
            {
                _texts[section] = text;
                return this;
            }

            public Sections Unmeetable(ItemTooltipSection section)
            {
                _unmet.Add(section);
                return this;
            }

            public string GetSection(ItemTooltipSection section)
            {
                string text;
                return _texts.TryGetValue(section, out text) ? text : null;
            }

            public bool IsRequirementUnmet(ItemTooltipSection section)
            {
                return _unmet.Contains(section);
            }
        }

        private static string Locale(int id)
        {
            return Data.Strings.GetByIndex(id);
        }

        private static ItemTooltipComposer Composer(Sections sections, Values values)
        {
            return new ItemTooltipComposer(sections, Data.CreateGenerator(values));
        }

        private static ItemTooltipContext Unique()
        {
            var context = new ItemTooltipContext();
            context.Quality = ItemQuality.Unique;
            context.Flags = ItemTooltipFlags.Identified;
            context.IsWeaponOrArmorType = true;
            return context;
        }

        [Fact]
        public void Every_embedded_table_is_present_and_the_right_size()
        {
            string[] names = D2DataFiles.EmbeddedResourceNames.OrderBy(n => n).ToArray();

            // Required rather than exhaustive: tables get added as writers are implemented, and a
            // fixed list would fail for the wrong reason.
            foreach (string required in new[]
            {
                "excel.ItemStatCost.txt", "excel.ItemTypes.txt", "excel.armor.txt",
                "excel.weapons.txt", "excel.misc.txt", "excel.charstats.txt",
                "excel.skills.txt", "excel.skilldesc.txt", "excel.PlayerClass.txt",
                "excel.MonType.txt", "excel.monstats.txt", "excel.lowqualityitems.txt",
                "excel.UniqueItems.txt", "excel.SetItems.txt",
                "excel.MagicPrefix.txt", "excel.MagicSuffix.txt", "excel.automagic.txt",
                "excel.RarePrefix.txt", "excel.RareSuffix.txt",
                "locale.eng.string.tbl", "locale.eng.patchstring.tbl",
                "locale.eng.expansionstring.tbl",
            })
            {
                Assert.Contains(required, names);
            }

            // These pin the extraction: they are the counts only Patch_D2.mpq's tables produce.
            Assert.Equal(359, Data.ItemStatCost.RowCount);
            Assert.Equal(357, Data.Skills.RowCount);
            Assert.Equal(6, Data.ItemStatCost.SkillIdShift);
            Assert.Equal(207, Data.ItemStatCost.StatIdsByDescPriority.Count);
        }

        [Fact]
        public void The_punctuation_the_engine_depends_on_is_what_the_tables_hold()
        {
            Assert.Equal(" ", Locale(DescStringIds.Space));
            Assert.Equal(":", Locale(DescStringIds.Colon));
            Assert.Equal("\n", Locale(DescStringIds.Newline));
            Assert.Equal(",", Locale(DescStringIds.ListComma));
            Assert.Equal("%", Locale(DescStringIds.Percent));
            Assert.Equal("+", Locale(DescStringIds.Plus));
            Assert.Equal("to", Locale(DescStringIds.To));
            Assert.Equal("an evil force", Locale(DescStringIds.DescStr2Sentinel));

            Assert.Equal(ItemTooltipColor.Marker, Locale(ItemTooltipColor.MarkerStringId));
        }

        [Fact]
        public void A_unique_ring_renders_real_stat_lines_bottom_up()
        {
            var values = new Values();
            var sections = new Sections()
                .Set(ItemTooltipSection.ItemName, "Nagelring")
                .Set(ItemTooltipSection.RequiredLevel, "Required Level: 7\n");

            ItemTooltipComposer composer = Composer(sections, values);

            IReadOnlyList<ItemTooltipLine> lines = composer.Compose(
                Unique(),
                new[]
                {
                    new KeyValuePair<int, int>(80, 25),   // item_magicbonus
                    new KeyValuePair<int, int>(39, 30),   // fireresist
                });

            string rendered = composer.Render(lines);
            string[] rows = rendered.Split('\n');

            Assert.Equal("Nagelring", rows[0]);
            Assert.Equal("Required Level: 7", rows[1]);

            Assert.Contains(
                rows, r => r == "25% Better Chance of Getting Magic Items");
            Assert.Contains(rows, r => r == "Fire Resist +30%");
        }

        [Fact]
        public void The_item_name_carries_the_quality_colour_and_the_stat_block_is_always_three()
        {
            var sections = new Sections().Set(ItemTooltipSection.ItemName, "Nagelring");
            ItemTooltipComposer composer = Composer(sections, new Values());

            IReadOnlyList<ItemTooltipLine> lines = composer.Compose(
                Unique(), new[] { new KeyValuePair<int, int>(80, 25) });

            Assert.Equal(
                ItemTooltipColor.Unique,
                lines.Single(l => l.Section == ItemTooltipSection.ItemName).Color);

            Assert.All(
                lines.Where(l => l.Section == ItemTooltipSection.Modifiers),
                l => Assert.Equal(ItemTooltipColor.Magic, l.Color));
        }

        [Fact]
        public void Colour_codes_are_emitted_around_the_real_strings()
        {
            var sections = new Sections().Set(ItemTooltipSection.ItemName, "Nagelring");
            ItemTooltipComposer composer = Composer(sections, new Values());

            IReadOnlyList<ItemTooltipLine> lines = composer.Compose(
                Unique(), new[] { new KeyValuePair<int, int>(80, 25) });

            string colored = composer.RenderWithColorCodes(lines);

            string unique = ItemTooltipColor.Marker
                            + ItemTooltipComposer.EncodeColorDigit(ItemTooltipColor.Unique);
            string magic = ItemTooltipColor.Marker
                           + ItemTooltipComposer.EncodeColorDigit(ItemTooltipColor.Magic);

            Assert.StartsWith(unique + "Nagelring", colored, StringComparison.Ordinal);
            Assert.Contains(magic, colored, StringComparison.Ordinal);
        }

        [Fact]
        public void Damage_stats_fold_into_one_real_aggregate_line()
        {
            var values = new Values();
            values.Base[DamageStatIds.FireMinDamage] = 5;
            values.Base[DamageStatIds.FireMaxDamage] = 12;

            var sections = new Sections().Set(ItemTooltipSection.ItemName, "Torch");
            ItemTooltipComposer composer = Composer(sections, values);

            IReadOnlyList<ItemTooltipLine> lines = composer.Compose(
                Unique(),
                new[]
                {
                    new KeyValuePair<int, int>(DamageStatIds.FireMinDamage, 5),
                    new KeyValuePair<int, int>(DamageStatIds.FireMaxDamage, 12),
                });

            ItemTooltipLine[] mods =
                lines.Where(l => l.Section == ItemTooltipSection.Modifiers).ToArray();

            Assert.Equal("Adds 5-12 fire damage\n", mods.Single().Text);
        }

        [Fact]
        public void A_single_skill_stat_takes_the_skill_from_the_layer_and_names_its_class()
        {
            var values = new Values();
            var sections = new Sections().Set(ItemTooltipSection.ItemName, "Wand");
            ItemTooltipComposer composer = Composer(sections, values);

            // Stat 107 is item_singleskill, descfunc 27, and it reads the skill id from the LAYER
            // rather than the value. Skill 36 is Fire Bolt, charclass "sor".
            int key = ItemStatReader.PackStatKey(36, 107);

            IReadOnlyList<ItemTooltipLine> lines = composer.Compose(
                Unique(), new[] { new KeyValuePair<int, int>(key, 1) });

            ItemTooltipLine skill =
                lines.Single(l => l.Section == ItemTooltipSection.Modifiers);

            Assert.Equal("+1 to Fire Bolt (Sorceress Only)\n", skill.Text);
        }

        [Fact]
        public void An_unmet_requirement_turns_the_real_requirement_line_red()
        {
            var sections = new Sections()
                .Set(ItemTooltipSection.ItemName, "Nagelring")
                .Set(ItemTooltipSection.RequiredLevel, "Required Level: 99\n")
                .Unmeetable(ItemTooltipSection.RequiredLevel);

            ItemTooltipComposer composer = Composer(sections, new Values());

            IReadOnlyList<ItemTooltipLine> lines =
                composer.Compose(Unique(), new KeyValuePair<int, int>[0]);

            Assert.Equal(
                ItemTooltipColor.Red,
                lines.Single(l => l.Section == ItemTooltipSection.RequiredLevel).Color);
        }

        [Fact]
        public void The_transaction_cost_inherits_the_names_colour()
        {
            var sections = new Sections()
                .Set(ItemTooltipSection.ItemName, "Nagelring")
                .Set(ItemTooltipSection.TransactionCost, "Repair Cost: 137 Gold\n");

            ItemTooltipContext context = Unique();
            context.ShopMode = 4;

            ItemTooltipComposer composer = Composer(sections, new Values());
            IReadOnlyList<ItemTooltipLine> lines =
                composer.Compose(context, new KeyValuePair<int, int>[0]);

            string rendered = composer.Render(lines);

            // The cost is appended last, so it renders on top.
            Assert.StartsWith("Repair Cost: 137 Gold\n", rendered, StringComparison.Ordinal);
            Assert.Equal(
                ItemTooltipColor.Unique,
                lines.Single(l => l.Section == ItemTooltipSection.TransactionCost).Color);
        }

        [Fact]
        public void Every_described_stat_produces_a_line_or_is_deliberately_silent()
        {
            var values = new Values();
            ItemDescriptionGenerator generator = Data.CreateGenerator(values);

            int described = 0;
            foreach (int statId in Data.ItemStatCost.StatIdsByDescPriority)
            {
                IReadOnlyList<ItemDescriptionLine> lines = generator.Describe(
                    new[] { new KeyValuePair<int, int>(statId, 5) });

                foreach (ItemDescriptionLine line in lines)
                {
                    Assert.NotNull(line.Text);
                    ++described;
                }
            }

            // Most of the 207 described stats emit something for a plain value of 5.
            Assert.True(described > 150, "only " + described + " stats produced a line");
        }

        [Fact]
        public void A_tooltip_longer_than_the_clamp_keeps_the_bottom_and_loses_the_top()
        {
            var sections = new Sections()
                .Set(ItemTooltipSection.ItemName, "Nagelring")
                .Set(
                    ItemTooltipSection.EtherealSocketed,
                    new string('e', ItemTooltipComposer.MaxTooltipLength) + "\n");

            ItemTooltipComposer composer = Composer(sections, new Values());
            IReadOnlyList<ItemTooltipLine> lines =
                composer.Compose(Unique(), new KeyValuePair<int, int>[0]);

            string rendered = composer.Render(lines);

            Assert.DoesNotContain("Nagelring", rendered, StringComparison.Ordinal);
            Assert.True(
                rendered.Length <= ItemTooltipComposer.MaxTooltipLength,
                "rendered " + rendered.Length + " characters");
        }
    }
}
