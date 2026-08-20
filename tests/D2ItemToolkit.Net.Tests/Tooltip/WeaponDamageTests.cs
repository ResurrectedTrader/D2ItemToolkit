using System;
using System.Collections.Generic;
using Xunit;

namespace D2ItemToolkit.Tests
{
    /// <summary>
    /// The weapon damage writer at 0x485410, in particular the Barbarian dual-wield arm.
    /// </summary>
    public class WeaponDamageTests
    {
        private static readonly D2DataFiles Data = D2DataFiles.LoadEmbedded();

        private static readonly ItemTable Items = new ItemTable(
            Data.Weapons, Data.Armor, Data.Misc);

        private static readonly ItemTypeTree Types = new ItemTypeTree(Data.ItemTypes);

        // Bastard Sword: 1or2handed AND 2handed are both set, so it is the case the Barbarian arm is
        // for — usable in one hand by a Barbarian, two-handed by anyone else.
        private const string Versatile = "bsw";

        private static string Damage(string code, int? viewerClass, params int[] statValue)
        {
            var item = new ItemIdentity();
            item.ClassId = Items.ClassIdForCode(code);
            item.Code = code;
            item.Flags = ItemRecordFlags.Identified;
            Assert.True(item.ClassId >= 0, code);

            ItemViewer viewer = null;
            if (viewerClass.HasValue)
            {
                viewer = new ItemViewer();
                viewer.UnitType = 0;
                viewer.ClassId = viewerClass.Value;
            }

            var stats = new Dictionary<int, int>();
            for (int i = 0; i + 1 < statValue.Length; i += 2)
            {
                stats[ItemStatReader.PackStatKey(0, statValue[i])] = statValue[i + 1];
            }

            var sections = new RecordSections(
                Data, Items, Types, item, viewer, stats, null, stats, null);
            return sections.GetSection(ItemTooltipSection.WeaponDamage);
        }

        [Fact]
        public void A_barbarian_sees_both_the_two_hand_and_the_one_hand_line()
        {
            // Stats 23/24 are the two-hand pair, 21/22 the one-hand pair.
            string text = Damage(Versatile, 4, 23, 20, 24, 40, 21, 10, 22, 25);

            Assert.Contains("Two-Hand Damage: 20 to 40", text, StringComparison.Ordinal);
            Assert.Contains("One-Hand Damage: 10 to 25", text, StringComparison.Ordinal);

            // Two-hand FIRST (0x4856a2 before 0x4857c5), each line carrying its own colour 0.
            Assert.True(
                text.IndexOf("Two-Hand", StringComparison.Ordinal)
                < text.IndexOf("One-Hand", StringComparison.Ordinal),
                text);
            Assert.Equal(2, CountMarkers(text));
        }

        [Fact]
        public void Every_other_class_sees_one_line_only()
        {
            foreach (int classId in new[] { 0, 1, 2, 3, 5, 6 })
            {
                string text = Damage(Versatile, classId, 23, 20, 24, 40, 21, 10, 22, 25);

                Assert.DoesNotContain("One-Hand Damage", text, StringComparison.Ordinal);
                Assert.Contains("Two-Hand Damage: 20 to 40", text, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void A_viewerless_tooltip_takes_the_single_line_path()
        {
            string text = Damage(Versatile, null, 23, 20, 24, 40, 21, 10, 22, 25);

            Assert.DoesNotContain("One-Hand Damage", text, StringComparison.Ordinal);
        }

        [Fact]
        public void A_barbarian_on_a_plain_two_hander_still_gets_one_line()
        {
            // Two-Handed Sword has 1or2handed set too, but a weapon WITHOUT it must not dual-line.
            // Maul is 2handed only.
            string text = Damage("mau", 4, 23, 30, 24, 60);

            Assert.DoesNotContain("One-Hand Damage", text, StringComparison.Ordinal);
        }

        [Fact]
        public void The_single_line_path_forces_max_above_min_but_the_dual_path_does_not()
        {
            // 0x485931 clamps max to min + 1; the Barbarian arm has no such clamp.
            string single = Damage(Versatile, 3, 23, 40, 24, 40);
            Assert.Contains("Two-Hand Damage: 40 to 41", single, StringComparison.Ordinal);

            string dual = Damage(Versatile, 4, 23, 40, 24, 40, 21, 15, 22, 15);
            Assert.Contains("Two-Hand Damage: 40 to 40", dual, StringComparison.Ordinal);
            Assert.Contains("One-Hand Damage: 15 to 15", dual, StringComparison.Ordinal);
        }

        [Fact]
        public void An_unmodified_damage_number_carries_no_colour()
        {
            // base == merged, so INV_CalcWeaponDamageRange would leave pModified clear.
            string text = Damage(Versatile, 3, 23, 20, 24, 40);

            Assert.DoesNotContain(ItemTooltipColor.Marker, text, StringComparison.Ordinal);
        }

        [Theory]
        // A base BELOW the merged value on either end sets the flag (0x485300).
        [InlineData(10, 40, true)]      // min raised
        [InlineData(20, 30, true)]      // max raised
        [InlineData(20, 40, false)]     // untouched
        public void The_min_number_is_coloured_when_the_base_is_below_the_merged(
            int baseMin, int baseMax, bool coloured)
        {
            string text = DamageWithBase(
                new[] { 23, 20, 24, 40 }, new[] { 23, baseMin, 24, baseMax });

            Assert.Equal(coloured, text.Contains(ItemTooltipColor.Marker + "3"));

            // The MAX never gets it: the shared number buffer is overwritten before it is appended.
            Assert.Equal(coloured ? 1 : 0, CountMarkers(text));
        }

        [Fact]
        public void The_marker_sits_after_the_label_so_the_whole_numeric_run_is_coloured()
        {
            string text = DamageWithBase(
                new[] { 23, 20, 24, 40 }, new[] { 23, 10, 24, 40 });

            // One marker, placed immediately before the MIN. A colour code stays in force until the
            // next one, so "20 to 40" all renders in colour 3 while the label does not.
            Assert.Equal("Two-Hand Damage: " + ItemTooltipColor.Marker + "320 to 40\n", text);
            Assert.Equal(1, CountMarkers(text));

            int at = text.IndexOf(ItemTooltipColor.Marker, StringComparison.Ordinal);
            Assert.Contains("Damage:", text.Substring(0, at), StringComparison.Ordinal);
            Assert.DoesNotContain("20", text.Substring(0, at), StringComparison.Ordinal);
        }

        [Fact]
        public void The_composer_carries_the_embedded_colour_to_the_end_of_the_line()
        {
            // Proves the marker is not cosmetic: the composer's own colour tracking picks it up.
            var item = new ItemIdentity();
            item.ClassId = Items.ClassIdForCode(Versatile);
            item.Flags = ItemRecordFlags.Identified;

            var viewer = new ItemViewer();
            viewer.UnitType = 0;
            viewer.ClassId = 3;

            var sections = new RecordSections(
                Data, Items, Types, item, viewer,
                Pairs(new[] { 23, 20, 24, 40 }), null, Pairs(new[] { 23, 10, 24, 40 }), null);

            var composer = new ItemTooltipComposer(
                sections, sections.CreateModifierGenerator(Pairs(new[] { 23, 20, 24, 40 })));
            ItemTooltipContext context = sections.CreateContext();

            IReadOnlyList<ItemTooltipLine> lines = composer.Compose(
                context, Pairs(new[] { 23, 20, 24, 40 }));

            Assert.Contains(
                lines,
                l => l.Section == ItemTooltipSection.WeaponDamage
                     && l.Text.Contains(ItemTooltipColor.Marker + "3"));
        }

        [Theory]
        [InlineData(272)]   // flat by-time damage
        [InlineData(273)]   // percentage by-time damage
        public void A_by_time_damage_stat_alone_colours_the_number(int statId)
        {
            // 0x485372 / 0x4853eb set pModified from these even with base == merged.
            string text = DamageWithBase(
                new[] { 23, 20, 24, 40, statId, 5 }, new[] { 23, 20, 24, 40 });

            Assert.Contains(ItemTooltipColor.Marker + "3", text, StringComparison.Ordinal);
        }

        [Fact]
        public void A_weapon_with_no_damage_stats_still_gets_a_line()
        {
            // 0x48e704 gates on >= 0, so ZERO passes and the min+1 clamp yields "0 to 1".
            string text = DamageWithBase(new int[0], new int[0]);

            Assert.Contains("Two-Hand Damage: 0 to 1", text, StringComparison.Ordinal);
        }

        [Fact]
        public void A_negative_damage_stat_skips_the_section()
        {
            Assert.Null(DamageWithBase(new[] { 21, -1 }, new int[0]));
        }

        private static string DamageWithBase(int[] merged, int[] baseValues)
        {
            var item = new ItemIdentity();
            item.ClassId = Items.ClassIdForCode(Versatile);
            item.Flags = ItemRecordFlags.Identified;

            var viewer = new ItemViewer();
            viewer.UnitType = 0;
            viewer.ClassId = 3;

            var sections = new RecordSections(
                Data, Items, Types, item, viewer, Pairs(merged), null, Pairs(baseValues), null);

            return sections.GetSection(ItemTooltipSection.WeaponDamage);
        }

        private static Dictionary<int, int> Pairs(int[] statValue)
        {
            var stats = new Dictionary<int, int>();
            for (int i = 0; i + 1 < statValue.Length; i += 2)
            {
                stats[ItemStatReader.PackStatKey(0, statValue[i])] = statValue[i + 1];
            }

            return stats;
        }

        private static int CountMarkers(string text)
        {
            int count = 0;
            int at = text.IndexOf(ItemTooltipColor.Marker, StringComparison.Ordinal);
            while (at >= 0)
            {
                ++count;
                at = text.IndexOf(
                    ItemTooltipColor.Marker, at + 1, StringComparison.Ordinal);
            }

            return count;
        }

        // =================================================================
        // The throw line has its own emission shape: colour 0 on the label (0x485a97) and a marker
        // on BOTH numbers (0x485afd / 0x485b7c), plus a flag pre-seeded from stats 18/17/159/160
        // at 0x485a14-0x485a54 that the 1H/2H lines never receive (theirs is zeroed at 0x485662).
        // =================================================================

        private static string ThrowLine(int[] merged, int[] baseValues)
        {
            var item = new ItemIdentity();
            item.ClassId = Items.ClassIdForCode("tax");
            item.Code = "tax";
            item.Flags = ItemRecordFlags.Identified;

            var viewer = new ItemViewer();
            viewer.UnitType = 0;
            viewer.ClassId = 3;

            var sections = new RecordSections(
                Data, Items, Types, item, viewer, Pairs(merged), null, Pairs(baseValues), null);

            return sections.GetSection(ItemTooltipSection.WeaponDamage);
        }

        [Fact]
        public void An_unmodified_throw_line_marks_both_numbers_with_colour_zero()
        {
            string text = ThrowLine(
                new[] { 159, 8, 160, 12 }, new[] { 159, 8, 160, 12 });

            string c0 = ItemTooltipColor.Marker + "0";
            Assert.Contains(
                c0 + "Throw Damage: " + c0 + "8 to " + c0 + "12\n",
                text, StringComparison.Ordinal);
        }

        [Fact]
        public void A_modified_throw_line_marks_both_numbers_with_colour_three()
        {
            string text = ThrowLine(
                new[] { 159, 8, 160, 20 }, new[] { 159, 8, 160, 12 });

            string c0 = ItemTooltipColor.Marker + "0";
            string c3 = ItemTooltipColor.Marker + "3";
            Assert.Contains(
                c0 + "Throw Damage: " + c3 + "8 to " + c3 + "20\n",
                text, StringComparison.Ordinal);
        }

        [Fact]
        public void An_enhanced_damage_bonus_alone_marks_the_throw_line()
        {
            // The pre-seed case: ED% moves neither 159 nor 160 in a leaf-summed view, so without
            // the stat 18/17 terms the line stayed unmarked on every ED throwing weapon.
            string text = ThrowLine(
                new[] { 159, 8, 160, 12, 18, 150, 17, 150 },
                new[] { 159, 8, 160, 12 });

            Assert.Contains(
                ItemTooltipColor.Marker + "3" + "8 to " + ItemTooltipColor.Marker + "3" + "12\n",
                text, StringComparison.Ordinal);
        }

        [Fact]
        public void The_one_hand_line_does_not_take_the_pre_seed()
        {
            // 0x485662 zeroes the 1H/2H flag; only the throw block gets stats 18/17 folded in.
            string text = ThrowLine(
                new[] { 21, 4, 22, 7, 159, 8, 160, 12, 18, 150, 17, 150 },
                new[] { 21, 4, 22, 7, 159, 8, 160, 12 });

            string oneHand = text.Split('\n')[0];
            Assert.DoesNotContain(ItemTooltipColor.Marker + "3", oneHand, StringComparison.Ordinal);
        }
    }
}
