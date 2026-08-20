using System.Collections.Generic;
using Xunit;

namespace D2ItemToolkit.Tests
{
    /// <summary>
    /// The IStatValues adapter the description engine reads through — the counterpart of
    /// SynthesisedStatValues.test.ts. The describe scope and the unit scope are DIFFERENT
    /// dictionaries, and collapsing them over-describes the item.
    /// </summary>
    public class SynthesisedStatValuesTests
    {
        private static readonly D2DataFiles Data = D2DataFiles.LoadEmbedded();

        private static readonly ItemTable Items = new ItemTable(
            Data.Weapons, Data.Armor, Data.Misc);

        private static readonly ItemTypeTree Types = new ItemTypeTree(Data.ItemTypes);

        private static Dictionary<int, int> Stats(params int[] triples)
        {
            var stats = new Dictionary<int, int>();
            for (int i = 0; i < triples.Length; i += 3)
            {
                stats[ItemStatReader.PackStatKey(triples[i], triples[i + 1])] = triples[i + 2];
            }

            return stats;
        }

        private static ItemIdentity Identity(string code)
        {
            var item = new ItemIdentity();
            item.ClassId = Items.ClassIdForCode(code);
            item.Code = code;
            return item;
        }

        [Fact]
        public void Keeps_the_describe_scope_and_the_unit_scope_separate()
        {
            // GetBaseStatValue is the temp list (the damage aggregate and the 23/24 suppression
            // read it); GetItemStatValue is the unit, which is what the never-breaks gate and
            // GetTxtMaxDurability 0x625e00 ask. Feeding one dictionary to both over-describes.
            var values = new SynthesisedStatValues(
                Stats(0, 39, 25), null, null, null, null, Stats(0, 39, 25, 0, 73, 62));

            Assert.Equal(0, values.GetBaseStatValue(73, 0));
            Assert.Equal(62, values.GetItemStatValue(73));
            Assert.Equal(62, values.GetTxtMaxDurability());
            Assert.Equal(25, values.GetBaseStatValue(39, 0));
        }

        [Fact]
        public void Serves_both_scopes_from_one_dictionary_when_no_unit_set_is_given()
        {
            var values = new SynthesisedStatValues(Stats(0, 73, 62), null, null, null, null);

            Assert.Equal(62, values.GetBaseStatValue(73, 0));
            Assert.Equal(62, values.GetItemStatValue(73));
        }

        [Fact]
        public void Reads_the_describe_scope_at_the_layer_it_is_asked_for()
        {
            var values = new SynthesisedStatValues(
                Stats(0, 107, 1, 2, 107, 3), null, null, null, null);

            Assert.Equal(1, values.GetBaseStatValue(107, 0));
            Assert.Equal(3, values.GetBaseStatValue(107, 2));
            Assert.Equal(0, values.GetBaseStatValue(107, 1));
        }

        [Fact]
        public void Tolerates_a_null_stat_set()
        {
            var values = new SynthesisedStatValues(null, null, null, null, null);

            Assert.Equal(0, values.GetBaseStatValue(39, 0));
            Assert.Equal(0, values.GetItemStatValue(39));
        }

        [Fact]
        public void Scales_op_two_to_five_stats_from_the_viewer_not_the_item()
        {
            // SKILLDESC_CalcStatGroupValue 0x4e4c50 calls GetStatUnsignedValue(GetPlayerUnit(),
            // opBase, 0), and GetPlayerUnit 0x463dd0 returns the local client player.
            var viewer = new ItemViewer();
            viewer.ClassId = 1;
            viewer.Stats[ItemStatReader.PackStatKey(0, 12)] = 40;

            var values = new SynthesisedStatValues(
                Stats(0, 12, 99), null, viewer, null, null);

            Assert.Equal(40, values.GetPlayerStatValue(12));
            Assert.Equal(1, values.PlayerClass);
        }

        [Fact]
        public void Reports_no_viewer_as_class_minus_one_and_every_player_stat_as_zero()
        {
            // GetStatUnsignedValue 0x625483 returns 0 for a null unit rather than halting, so the
            // line is still emitted with a zero value.
            var values = new SynthesisedStatValues(Stats(), null, null, null, null);

            Assert.Equal(0, values.GetPlayerStatValue(12));
            Assert.Equal(-1, values.PlayerClass);
        }

        [Fact]
        public void Is_always_an_item()
        {
            Assert.True(
                new SynthesisedStatValues(null, null, null, null, null).DescribedUnitIsItem);
        }

        [Fact]
        public void Probes_both_items_txt_type_codes_for_is_of_type()
        {
            // A war hammer is a Hammer, which sits under Blunt (57) through the closure matrix.
            var hammer = new SynthesisedStatValues(null, Identity("whm"), null, Items, Types);
            Assert.True(hammer.IsItemOfType(UndeadDamageLine.BluntItemType));

            // A short sword is not.
            var sword = new SynthesisedStatValues(null, Identity("ssd"), null, Items, Types);
            Assert.False(sword.IsItemOfType(UndeadDamageLine.BluntItemType));
        }

        [Fact]
        public void Cannot_answer_is_of_type_without_the_tables()
        {
            Assert.False(new SynthesisedStatValues(null, Identity("whm"), null, null, Types)
                .IsItemOfType(UndeadDamageLine.BluntItemType));
            Assert.False(new SynthesisedStatValues(null, Identity("whm"), null, Items, null)
                .IsItemOfType(UndeadDamageLine.BluntItemType));
            Assert.False(new SynthesisedStatValues(null, null, null, Items, Types)
                .IsItemOfType(UndeadDamageLine.BluntItemType));
        }

        [Fact]
        public void Allows_durability_only_when_the_table_has_one_and_does_not_forbid_it()
        {
            var hammer = new SynthesisedStatValues(null, Identity("whm"), null, Items, Types);
            Assert.True(hammer.ItemTableAllowsDurability);

            // A ring carries no durability column value at all.
            var ring = new SynthesisedStatValues(null, Identity("rin"), null, Items, Types);
            Assert.False(ring.ItemTableAllowsDurability);
        }

        [Fact]
        public void Cannot_answer_the_durability_gate_without_the_item_table()
        {
            Assert.False(new SynthesisedStatValues(null, Identity("whm"), null, null, Types)
                .ItemTableAllowsDurability);
            Assert.False(new SynthesisedStatValues(null, null, null, Items, Types)
                .ItemTableAllowsDurability);
        }

        [Fact]
        public void Reads_max_durability_off_the_unit_scope_not_the_describe_scope()
        {
            // Despite the name, GetTxtMaxDurability 0x625e00 reads the item's STAT 73.
            var values = new SynthesisedStatValues(
                Stats(0, 73, 11), null, null, null, null, Stats(0, 73, 62));

            Assert.Equal(62, values.GetTxtMaxDurability());
        }
    }
}
