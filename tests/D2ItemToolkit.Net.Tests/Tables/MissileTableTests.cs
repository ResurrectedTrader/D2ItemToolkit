using Xunit;

namespace D2ItemToolkit.Tests
{
    /// <summary>
    /// The slice of missiles.txt the throwing-potion damage arm reads (0x485410) — the counterpart
    /// of MissileTable.test.ts, which had no C# equivalent.
    /// </summary>
    public class MissileTableTests
    {
        private static readonly D2DataFiles Data = D2DataFiles.LoadEmbedded();

        private static readonly ItemTable Items = new ItemTable(
            Data.Weapons, Data.Armor, Data.Misc);

        private static readonly MissileTable Missiles = new MissileTable(
            Data.Missiles, Data.ElementTypes);

        private static MissileThrowDamage Damage(int missileId)
        {
            MissileThrowDamage damage;
            Assert.True(Missiles.TryGetThrowDamage(missileId, out damage), "missile " + missileId);
            return damage;
        }

        [Fact]
        public void Spreads_a_poison_cloud_over_its_duration_and_collapses_an_equal_range()
        {
            // Rancid Gas Potion fires missile 49: 192 poison over an ELen of 50, divided by
            // 50/25 = 2 (0x4854fd). Poison takes colour 2 from the table at 0x4854d0.
            Assert.Equal(49, Items.GetInt(Items.ClassIdForCode("gps"), "missiletype"));

            MissileThrowDamage damage = Damage(49);
            Assert.Equal(96, damage.Min);
            Assert.Equal(96, damage.Max);
            Assert.Equal(2, damage.Color);
        }

        [Fact]
        public void Adds_the_elemental_half_to_the_physical_half_and_shifts_both_back()
        {
            // Fulminating Potion fires missile 44: physical 2-7 plus fire 3-8, both shifted by the
            // record's HitShift of 8 and shifted back at 0x48554c / 0x485559.
            Assert.Equal(44, Items.GetInt(Items.ClassIdForCode("opl"), "missiletype"));

            MissileThrowDamage damage = Damage(44);
            Assert.Equal(5, damage.Min);
            Assert.Equal(15, damage.Max);
            Assert.Equal(1, damage.Color);
        }

        [Theory]
        // Indexed by elemType - 1. Magic (3) and everything outside 1..5 take the default arm,
        // which leaves the colour at 0.
        [InlineData(22, 1)]   // fire
        [InlineData(99, 4)]   // ltng
        [InlineData(107, 3)]  // cold
        [InlineData(32, 2)]   // pois
        [InlineData(77, 0)]   // mag
        [InlineData(271, 0)]  // frze, past the table
        [InlineData(7, 0)]    // no EType at all
        public void Picks_the_colour_from_the_jump_table(int missileId, int color)
        {
            Assert.Equal(color, Damage(missileId).Color);
        }

        [Fact]
        public void Never_lets_max_fall_below_min()
        {
            // 0x48555c raises max to min, never the other way round.
            for (int id = 0; id < Data.Missiles.RowCount; ++id)
            {
                MissileThrowDamage damage = Damage(id);
                Assert.True(damage.Max >= damage.Min, "missile " + id);
            }
        }

        [Fact]
        public void Rejects_an_id_outside_the_table()
        {
            Assert.Equal(684, Data.Missiles.RowCount);

            Assert.False(Missiles.TryGetThrowDamage(-1, out _));
            Assert.False(Missiles.TryGetThrowDamage(684, out _));
            Assert.False(
                new MissileTable(null, Data.ElementTypes).TryGetThrowDamage(49, out _));
        }

        [Fact]
        public void Reads_etype_as_a_row_index_into_elemtypes_txt()
        {
            // The linker field stores the ROW INDEX (0x612993), so an unknown or blank code is
            // row 0 and takes the colourless arm.
            Assert.Equal("pois", Data.ElementTypes.GetString(5, "Code"));

            MissileThrowDamage damage;
            Assert.True(new MissileTable(Data.Missiles, null).TryGetThrowDamage(49, out damage));
            Assert.Equal(0, damage.Color);
        }
    }
}
