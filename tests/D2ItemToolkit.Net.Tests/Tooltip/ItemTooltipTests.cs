using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace D2ItemToolkit.Tests
{
    internal sealed class FakeSections : IItemTooltipSections
    {
        public readonly Dictionary<ItemTooltipSection, string> Texts =
            new Dictionary<ItemTooltipSection, string>();

        public readonly HashSet<ItemTooltipSection> Unmet = new HashSet<ItemTooltipSection>();

        public FakeSections Set(ItemTooltipSection section, string text)
        {
            Texts[section] = text;
            return this;
        }

        public FakeSections Unmeetable(ItemTooltipSection section)
        {
            Unmet.Add(section);
            return this;
        }

        public string LineTerminator { get; set; }

        public FakeSections()
        {
            LineTerminator = "\n";
        }

        public string GetSection(ItemTooltipSection section)
        {
            string text;
            return Texts.TryGetValue(section, out text) ? text : null;
        }

        public bool IsRequirementUnmet(ItemTooltipSection section)
        {
            return Unmet.Contains(section);
        }
    }

    public class ItemTooltipTests
    {
        private static ItemDescriptionGenerator Modifiers()
        {
            var stats = new FakeStatCostTable();
            stats.Add(Build.Stat(16, ItemDescFunc.PlusValuePercentString, 100, priority: 100));
            stats.Add(Build.Stat(39, ItemDescFunc.PlusValueString, 101, priority: 50));

            var strings = new FakeStringTable().WithPunctuation()
                .Add(100, "Enhanced Defense")
                .Add(101, "Fire Resist");

            return new ItemDescriptionGenerator(stats, strings);
        }

        private static ItemTooltipComposer Composer(FakeSections sections)
        {
            return new ItemTooltipComposer(sections, Modifiers());
        }

        private static ItemTooltipContext Context(
            ItemQuality quality = ItemQuality.Normal,
            ItemTooltipFlags flags = ItemTooltipFlags.None,
            bool forcesCrafted = false,
            bool unidentifiedInShop = false)
        {
            var context = new ItemTooltipContext();
            context.Quality = quality;
            context.Flags = flags | ItemTooltipFlags.Identified;
            context.ForcesCraftedColor = forcesCrafted;
            context.UnidentifiedInShop = unidentifiedInShop;
            context.IsWeaponOrArmorType = true;
            return context;
        }

        private static readonly KeyValuePair<int, int>[] SampleStats =
        {
            new KeyValuePair<int, int>(0x00000010, 180), // stat 16
            new KeyValuePair<int, int>(0x00000027, 40),  // stat 39
        };

        // =================================================================
        // Guard clauses
        // =================================================================

        [Fact]
        public void Ctor_rejects_null_sections()
        {
            ItemDescriptionGenerator modifiers = Modifiers();
            Assert.Throws<ArgumentNullException>(() => new ItemTooltipComposer(null, modifiers));
        }

        [Fact]
        public void Ctor_rejects_a_null_modifier_generator()
        {
            Assert.Throws<ArgumentNullException>(
                () => new ItemTooltipComposer(new FakeSections(), null));
        }

        [Fact]
        public void Compose_rejects_a_null_context()
        {
            Assert.Throws<ArgumentNullException>(
                () => Composer(new FakeSections()).Compose(null, SampleStats));
        }

        [Fact]
        public void Compose_rejects_null_stats()
        {
            Assert.Throws<ArgumentNullException>(
                () => Composer(new FakeSections()).Compose(Context(), null));
        }

        [Fact]
        public void Render_rejects_null_lines()
        {
            Assert.Throws<ArgumentNullException>(() => Composer(new FakeSections()).Render(null));
        }

        [Fact]
        public void ResolveModifierColor_rejects_a_null_context()
        {
            Assert.Throws<ArgumentNullException>(() => ItemTooltipComposer.ResolveItemNameColor(null));
        }

        // =================================================================
        // Section order
        // =================================================================

        [Fact]
        public void Sections_appear_in_the_order_LoadItemDesc_appends_them()
        {
            FakeSections sections = new FakeSections()
                .Set(ItemTooltipSection.ArmorClass, "Defense: 445")
                .Set(ItemTooltipSection.ItemName, "Shaftstop")
                .Set(ItemTooltipSection.RuneLetters, "Enigma")
                .Set(ItemTooltipSection.Unidentified, "Mesh Armor")
                .Set(ItemTooltipSection.RequiredLevel, "Required Level: 38");

            IReadOnlyList<ItemTooltipLine> lines =
                Composer(sections).Compose(Context(ItemQuality.Unique), SampleStats);

            // Display order is the reverse of append order.
            // A section's text carries its own terminator; the composer supplies one when the
            // provider omits it (GetItemName and the price line do, in the game).
            Assert.Equal(new[]
            {
                "Shaftstop\n",
                "Enigma\n",
                "Defense: 445\n",
                "Required Level: 38\n",
                "Mesh Armor\n",
                // The stat block is ONE buffer in the game, but LoadItemDesc drives it in
                // INLINE mode (0x48e92d pushes arg_4 = 1, reaching arg_14 at 0x4e62ec), so
                // every stat line is terminated with 3998 and the 3852 + 3995 separator is
                // never emitted. Without the terminator the whole block collapses onto one
                // rendered line and glues itself to the section below.
                "+40 Fire Resist\n",
                "+180% Enhanced Defense\n",
            }, lines.Select(l => l.Text).ToArray());
        }

        [Fact]
        public void An_empty_section_is_omitted()
        {
            FakeSections sections = new FakeSections()
                .Set(ItemTooltipSection.ItemName, "Shaftstop")
                .Set(ItemTooltipSection.Unidentified, string.Empty)
                .Set(ItemTooltipSection.BlockChance, null);

            IReadOnlyList<ItemTooltipLine> lines =
                Composer(sections).Compose(Context(), SampleStats);

            Assert.DoesNotContain(ItemTooltipSection.Unidentified, lines.Select(l => l.Section));
            Assert.DoesNotContain(ItemTooltipSection.BlockChance, lines.Select(l => l.Section));
        }

        [Fact]
        public void The_stat_block_is_appended_last()
        {
            FakeSections sections = new FakeSections()
                .Set(ItemTooltipSection.ItemName, "Shaftstop");

            IReadOnlyList<ItemTooltipLine> lines =
                Composer(sections).Compose(Context(), SampleStats);

            // Rendering is bottom-up, so the stat block is appended second and displayed last.
            Assert.Equal(ItemTooltipSection.Modifiers, lines[lines.Count - 1].Section);
        }

        [Fact]
        public void A_tooltip_with_no_stats_is_just_its_sections()
        {
            FakeSections sections = new FakeSections()
                .Set(ItemTooltipSection.ItemName, "Quilted Armor");

            IReadOnlyList<ItemTooltipLine> lines =
                Composer(sections).Compose(Context(), new KeyValuePair<int, int>[0]);

            Assert.Single(lines);
            Assert.Equal("Quilted Armor\n", lines[0].Text);
        }

        [Fact]
        public void Render_concatenates_without_inserting_separators()
        {
            FakeSections sections = new FakeSections()
                .Set(ItemTooltipSection.ItemName, "Shaftstop")
                .Set(ItemTooltipSection.Unidentified, "Mesh Armor");

            ItemTooltipComposer composer = Composer(sections);
            IReadOnlyList<ItemTooltipLine> lines = composer.Compose(Context(), SampleStats);

            // 0x526700 is a plain concatenation; each writer terminates its own text, stat
            // lines included (inline mode). The assembled string ends UNTERMINATED because the
            // two writers that omit a trailing 3998 are the two appended last.
            Assert.Equal(
                "Shaftstop\nMesh Armor\n+40 Fire Resist\n+180% Enhanced Defense",
                composer.Render(lines));
        }

        [Fact]
        public void Render_of_nothing_is_empty()
        {
            Assert.Equal(string.Empty, Composer(new FakeSections()).Render(new ItemTooltipLine[0]));
        }

        [Fact]
        public void A_line_stringifies_to_its_text()
        {
            FakeSections sections = new FakeSections().Set(ItemTooltipSection.ItemName, "Shaftstop");

            IReadOnlyList<ItemTooltipLine> lines =
                Composer(sections).Compose(Context(), new KeyValuePair<int, int>[0]);

            Assert.Equal("Shaftstop\n", lines[0].ToString());
        }

        // =================================================================
        // Section colours
        // =================================================================

        [Theory]
        [InlineData(ItemTooltipSection.RequiredLevel)]
        [InlineData(ItemTooltipSection.RequiredStrength)]
        [InlineData(ItemTooltipSection.RequiredDexterity)]
        [InlineData(ItemTooltipSection.ClassRestriction)]
        public void An_unmet_requirement_turns_red(ItemTooltipSection section)
        {
            FakeSections sections = new FakeSections().Set(section, "requirement").Unmeetable(section);

            ItemTooltipLine line = Composer(sections)
                .Compose(Context(), new KeyValuePair<int, int>[0])
                .Single(l => l.Section == section);

            Assert.Equal(ItemTooltipColor.Red, line.Color);
        }

        [Theory]
        [InlineData(ItemTooltipSection.RequiredLevel)]
        [InlineData(ItemTooltipSection.RequiredStrength)]
        [InlineData(ItemTooltipSection.RequiredDexterity)]
        [InlineData(ItemTooltipSection.ClassRestriction)]
        public void A_met_requirement_stays_white(ItemTooltipSection section)
        {
            FakeSections sections = new FakeSections().Set(section, "requirement");

            ItemTooltipLine line = Composer(sections)
                .Compose(Context(), new KeyValuePair<int, int>[0])
                .Single(l => l.Section == section);

            Assert.Equal(ItemTooltipColor.White, line.Color);
        }

        [Theory]
        [InlineData(ItemTooltipSection.ItemName, ItemTooltipColor.White)]
        [InlineData(ItemTooltipSection.EtherealSocketed, ItemTooltipColor.Magic)]
        [InlineData(ItemTooltipSection.Unidentified, ItemTooltipColor.Red)]
        [InlineData(ItemTooltipSection.RuneLetters, ItemTooltipColor.Unique)]
        [InlineData(ItemTooltipSection.ArmorClass, ItemTooltipColor.White)]
        [InlineData(ItemTooltipSection.AttackSpeed, ItemTooltipColor.White)]
        public void Each_section_carries_the_colour_LoadItemDesc_gives_it(
            ItemTooltipSection section, int expected)
        {
            FakeSections sections = new FakeSections().Set(section, "text");

            ItemTooltipLine line = Composer(sections)
                .Compose(Context(), new KeyValuePair<int, int>[0])
                .Single(l => l.Section == section);

            Assert.Equal(expected, line.Color);
        }

        // =================================================================
        // Stat block colour
        // =================================================================

        // ItemQuality and ItemTooltipFlags are internal — they model the engine, not the API — so
        // the parameter widens to int and casts back. A public xUnit method cannot take an
        // internal type, and making the method internal would stop it being DISCOVERED at all.
        [Theory]
        [InlineData((int)ItemQuality.Magic, ItemTooltipColor.Magic)]
        [InlineData((int)ItemQuality.Set, ItemTooltipColor.Set)]
        [InlineData((int)ItemQuality.Rare, ItemTooltipColor.Rare)]
        [InlineData((int)ItemQuality.Unique, ItemTooltipColor.Unique)]
        [InlineData((int)ItemQuality.Crafted, ItemTooltipColor.Crafted)]
        [InlineData((int)ItemQuality.Tempered, ItemTooltipColor.Tempered)]
        [InlineData((int)ItemQuality.Normal, ItemTooltipColor.White)]
        [InlineData((int)ItemQuality.LowQuality, ItemTooltipColor.White)]
        [InlineData((int)ItemQuality.HighQuality, ItemTooltipColor.White)]
        public void Quality_picks_the_stat_block_colour(int quality, int expected)
        {
            Assert.Equal(
                expected,
                ItemTooltipComposer.ResolveItemNameColor(Context((ItemQuality)quality)));
        }

        [Theory]
        [InlineData((uint)ItemTooltipFlags.Socketed)]
        [InlineData((uint)ItemTooltipFlags.Ethereal)]
        [InlineData((uint)(ItemTooltipFlags.Socketed | ItemTooltipFlags.Ethereal))]
        public void A_socketed_or_ethereal_plain_item_gets_its_own_colour(uint flags)
        {
            Assert.Equal(
                ItemTooltipColor.SocketedOrEthereal,
                ItemTooltipComposer.ResolveItemNameColor(
                    Context(ItemQuality.Normal, (ItemTooltipFlags)flags)));
        }

        [Fact]
        public void Socketed_does_not_override_a_quality_colour()
        {
            Assert.Equal(ItemTooltipColor.Unique,
                ItemTooltipComposer.ResolveItemNameColor(
                    Context(ItemQuality.Unique, ItemTooltipFlags.Socketed)));
        }

        [Fact]
        public void Unidentified_in_a_shop_forces_white()
        {
            Assert.Equal(ItemTooltipColor.White,
                ItemTooltipComposer.ResolveItemNameColor(
                    Context(ItemQuality.Unique, unidentifiedInShop: true)));
        }

        [Fact]
        public void A_forced_crafted_item_code_wins_over_the_shop_override()
        {
            // 0x48ea0c runs after 0x48e8d7.
            Assert.Equal(ItemTooltipColor.Crafted,
                ItemTooltipComposer.ResolveItemNameColor(
                    Context(ItemQuality.Unique, forcesCrafted: true, unidentifiedInShop: true)));
        }

        [Fact]
        public void Broken_wins_over_everything()
        {
            // 0x48ebde is the last override applied.
            Assert.Equal(ItemTooltipColor.Red,
                ItemTooltipComposer.ResolveItemNameColor(
                    Context(ItemQuality.Unique, ItemTooltipFlags.Broken, forcesCrafted: true)));
        }

        [Fact]
        public void The_stat_block_lines_carry_the_literal_magic_colour()
        {
            FakeSections sections = new FakeSections().Set(ItemTooltipSection.ItemName, "Shaftstop");

            IReadOnlyList<ItemTooltipLine> lines =
                Composer(sections).Compose(Context(ItemQuality.Unique), SampleStats);

            // 0x48ea1c appends the whole block with a literal 3; the quality colour goes to
            // the item name instead (0x48ebee).
            Assert.All(lines.Where(l => l.Section == ItemTooltipSection.Modifiers),
                line => Assert.Equal(ItemTooltipColor.Magic, line.Color));
        }
    }
}


