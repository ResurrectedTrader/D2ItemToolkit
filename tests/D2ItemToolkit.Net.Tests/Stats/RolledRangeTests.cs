using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace D2ItemToolkit.Tests
{
    /// <summary>
    /// The roll-range reconstruction. Two kinds of assertion here, and the difference matters:
    ///
    /// The ANCHORS compare a reconstructed span against the shipped table's own min/max columns,
    /// read independently in the test. Those are the checks that can fail for a real reason.
    ///
    /// The SWEEPS assert invariants over every enabled row — low never above high, an item's own
    /// application always inside its own span, no func left unsupported. Those cannot prove the
    /// spans are right, but they are what catches a gathering bug on the 400th unique rather than
    /// the first.
    /// </summary>
    public class RolledRangeTests
    {
        private static readonly D2DataFiles Data = D2DataFiles.LoadEmbedded();
        private static readonly TooltipEngine Engine = TooltipEngine.Embedded;

        private static readonly ItemTable Items = new ItemTable(
            Data.Weapons, Data.Armor, Data.Misc);

        private static readonly ItemTypeTree Types = new ItemTypeTree(Data.ItemTypes);

        private static RolledRangeReconstructor Reconstructor()
        {
            return new RolledRangeReconstructor(
                Data,
                Items,
                Types,
                new MagicAffixTable(Data),
                new SetTable(Data.Sets, Data.SetItems, Data.Strings));
        }

        private static Unit UniqueItem(string index)
        {
            int row = Data.UniqueItems.FindRow("index", index);
            Assert.True(row >= 0, "no UniqueItems row for " + index);

            var unit = new Unit();
            unit.UnitType = 4;
            unit.Quality = 7;
            unit.FileIndex = row;
            unit.ClassId = Items.ClassIdForCode(Data.UniqueItems.GetString(row, "code").Trim());
            unit.ItemFlags = ItemRecordFlags.Identified;
            return unit;
        }

        private static RolledStatRange Range(ItemRollRanges ranges, int statId)
        {
            return ranges.Stats.FirstOrDefault(r => r.StatId == statId && r.Layer == 0);
        }

        private static void AssertSpan(ItemRollRanges ranges, int statId, int low, int high)
        {
            RolledStatRange range = Range(ranges, statId);
            Assert.NotNull(range);
            Assert.Equal(low, range.Low);
            Assert.Equal(high, range.High);
        }

        // ---- anchors ---------------------------------------------------------------------------

        [Fact]
        public void A_uniques_spans_are_its_own_min_and_max_columns()
        {
            // The Eye of Etlich carries six ranged props, all simple func 1 adds onto distinct
            // stats, and every stat involved has ValShift 0 — so the span must come out as exactly
            // the numbers in the row.
            ItemRollRanges ranges = Engine.Ranges(UniqueItem("The Eye of Etlich"));

            AssertSpan(ranges, 32, 10, 40);   // ac-miss     -> armorclass_vs_missile
            AssertSpan(ranges, 89, 1, 5);     // light       -> item_lightradius
            AssertSpan(ranges, 60, 3, 7);     // lifesteal   -> lifedrainmindam
            AssertSpan(ranges, 54, 1, 2);     // cold-min    -> coldmindam
            AssertSpan(ranges, 55, 3, 5);     // cold-max    -> coldmaxdam
            AssertSpan(ranges, 56, 50, 250);  // cold-len    -> coldlength

            // allskills is 1..1 on this row, so it is present but not a range.
            AssertSpan(ranges, 127, 1, 1);
            Assert.False(Range(ranges, 127).IsRange);

            Assert.Empty(ranges.UnsupportedFuncs);
        }

        [Fact]
        public void The_spans_are_read_from_the_table_rather_than_assumed()
        {
            // The same claim as above, but with the expected numbers read out of the shipped file
            // in the test instead of written down — so the assertion cannot drift from the data.
            int row = Data.UniqueItems.FindRow("index", "The Eye of Etlich");
            ItemRollRanges ranges = Engine.Ranges(UniqueItem("The Eye of Etlich"));

            var byCode = new Dictionary<string, int>
            {
                { "ac-miss", 32 }, { "light", 89 }, { "lifesteal", 60 },
                { "cold-min", 54 }, { "cold-max", 55 }, { "cold-len", 56 },
            };

            for (int prop = 1; prop <= 12; ++prop)
            {
                string code = Data.UniqueItems.GetString(row, "prop" + prop).Trim();

                int statId;
                if (code.Length == 0 || !byCode.TryGetValue(code, out statId))
                {
                    continue;
                }

                AssertSpan(
                    ranges,
                    statId,
                    Data.UniqueItems.GetInt(row, "min" + prop),
                    Data.UniqueItems.GetInt(row, "max" + prop));
            }
        }

        [Fact]
        public void A_unique_with_no_ranged_props_still_rolls_its_base_defense()
        {
            // Harlequin Crest's own props are entirely fixed — every min equals its max — so the
            // ONLY thing that varies on a Shako is the base armour roll off armor.txt. Which makes
            // it the cleanest check that the two contributions stay separate.
            ItemRollRanges ranges = Engine.Ranges(UniqueItem("Harlequin Crest"));

            AssertSpan(ranges, 127, 2, 2);   // allskills, fixed
            AssertSpan(ranges, 80, 50, 50);  // mag% -> item_magicbonus, fixed

            Assert.All(
                ranges.Stats.Where(r => r.StatId != 31),
                range => Assert.False(range.IsRange, "stat " + range.StatId + " should be fixed"));

            RolledStatRange defense = Range(ranges, 31);
            Assert.NotNull(defense);
            Assert.True(defense.IsRange);
            Assert.Equal(RollSources.Base, defense.Sources);
            Assert.Equal(Items.GetInt(UniqueItem("Harlequin Crest").ClassId, "minac"), defense.Low);
            Assert.Equal(Items.GetInt(UniqueItem("Harlequin Crest").ClassId, "maxac"), defense.High);
        }

        [Fact]
        public void A_superior_item_takes_its_span_from_qualityitems()
        {
            // A superior weapon can only have rolled the weapon-gated rows, and every row carrying
            // `att` agrees on 1..3 while every `dmg%` row agrees on 5..15 — which is what makes an
            // unknown row still give one span per stat.
            var unit = new Unit();
            unit.UnitType = 4;
            unit.Quality = 3; // HighQuality, i.e. superior
            unit.ClassId = Items.ClassIdForCode("crs");
            unit.ItemFlags = ItemRecordFlags.Identified;

            ItemRollRanges ranges = Engine.Ranges(unit);

            AssertSpan(ranges, 19, 1, 3);    // att  -> tohit, func 1
            AssertSpan(ranges, 75, 10, 15);  // dur% -> item_maxdurability_percent, func 13

            // dmg% is func 7, the enhanced-damage handler, whose integer arithmetic writes NOTHING
            // at a 5% roll on this base and 15 at a 15% roll — so its span starts at 0 rather than
            // at 5. That is the handler's own truncation, not a gap in the reconstruction.
            AssertSpan(ranges, 17, 0, 15);   // maxdamage_percent
            AssertSpan(ranges, 18, 0, 15);   // mindamage_percent

            Assert.All(
                ranges.Stats,
                range => Assert.True(
                    (range.Sources & RollSources.Superior) != 0,
                    "stat " + range.StatId + " was not attributed to the superior row"));
        }

        [Fact]
        public void A_superior_shield_is_gated_away_from_the_weapon_rows()
        {
            // The gate is the point: a shield must not pick up the weapon-only attack-rating roll.
            var unit = new Unit();
            unit.UnitType = 4;
            unit.Quality = 3;
            unit.ClassId = Items.ClassIdForCode("buc"); // buckler
            unit.ItemFlags = ItemRecordFlags.Identified;

            ItemRollRanges ranges = Engine.Ranges(unit);

            Assert.Null(Range(ranges, 19));  // no attack rating
            AssertSpan(ranges, 16, 5, 15);   // ac% -> armorclass_percent
        }

        [Fact]
        public void A_layer_rolling_property_reports_every_layer_it_could_land_on()
        {
            // Ormus' Robes rolls its LAYER, not its value: `skill-rand` 36..60 is the twenty-five
            // sorceress skills, each carrying the same +3. So the answer is 25 entries rather than
            // one span, which is why they are kept apart from Stats.
            ItemRollRanges ranges = Engine.Ranges(UniqueItem("Ormus' Robes"));

            RolledLayerRange singleSkill =
                ranges.LayerVaries.Single(r => r.StatId == 107);

            Assert.Equal(36, singleSkill.LayerLow);
            Assert.Equal(60, singleSkill.LayerHigh);
            Assert.Equal(3, singleSkill.Value);
            Assert.Equal(RollSources.Unique, singleSkill.Sources);

            // And it must NOT appear as a value span, which is the shape a naive low/high diff
            // gives: two entries claiming "+36" and "+60" at layers 3 and 3.
            Assert.DoesNotContain(ranges.Stats, r => r.StatId == 107);
        }

        [Fact]
        public void Base_defense_comes_from_the_armor_rows_own_range()
        {
            // The one base column that genuinely rolls. A plain normal-quality armour has no
            // affixes at all, so this is the only span it can have.
            var unit = new Unit();
            unit.UnitType = 4;
            unit.Quality = 2; // Normal
            unit.ClassId = Items.ClassIdForCode("xhn"); // full helm, exceptional
            unit.ItemFlags = ItemRecordFlags.Identified;

            ItemRollRanges ranges = Engine.Ranges(unit);

            AssertSpan(
                ranges,
                31,
                Items.GetInt(unit.ClassId, "minac"),
                Items.GetInt(unit.ClassId, "maxac"));

            Assert.True((Range(ranges, 31).Sources & RollSources.Base) != 0);
        }

        [Fact]
        public void An_ethereal_armours_base_defense_span_is_scaled()
        {
            // ITEMMOD_ApplyEtherealBonus 0x65e4d0 multiplies stat 31 by 3/2 once at spawn
            // (0x65e5d6), so a captured ethereal item's Defense already includes it. A span built
            // from the raw minac/maxac would sit BELOW the value it is supposed to contain.
            var plain = new Unit();
            plain.UnitType = 4;
            plain.Quality = 2;
            plain.ClassId = Items.ClassIdForCode("xhn");
            plain.ItemFlags = ItemRecordFlags.Identified;

            var ethereal = new Unit();
            ethereal.UnitType = 4;
            ethereal.Quality = 2;
            ethereal.ClassId = plain.ClassId;
            ethereal.ItemFlags = ItemRecordFlags.Identified | ItemRecordFlags.Ethereal;

            RolledStatRange normal = Range(Engine.Ranges(plain), 31);
            RolledStatRange scaled = Range(Engine.Ranges(ethereal), 31);

            Assert.NotNull(normal);
            Assert.NotNull(scaled);

            Assert.Equal(normal.Low * 3 / 2, scaled.Low);
            Assert.Equal(normal.High * 3 / 2, scaled.High);
            Assert.True(scaled.Low > normal.Low, "the ethereal span must be higher");
        }

        [Fact]
        public void An_ethereal_weapon_takes_the_other_arm()
        {
            // The bonus branches on IsOfType(item, 45) — `weap` — and a weapon gets its DAMAGE
            // stats scaled instead of stat 31 (0x65e51b). A weapon has no minac anyway, so the
            // check is what stops a future armour-shaped weapon being scaled twice.
            var unit = new Unit();
            unit.UnitType = 4;
            unit.Quality = 2;
            unit.ClassId = Items.ClassIdForCode("crs");
            unit.ItemFlags = ItemRecordFlags.Identified | ItemRecordFlags.Ethereal;

            Assert.Null(Range(Engine.Ranges(unit), 31));
        }

        // ---- sweeps ----------------------------------------------------------------------------

        /// <summary>
        /// Applies one item's own reconstructed properties at a given end and checks the resulting
        /// stats against the span claimed for them. Feeding a reconstruction its OWN output is the
        /// weakest form of this check, but it is the only one available without real captures, and
        /// it does catch a span built from the wrong row or in the wrong order.
        /// </summary>
        private static void AssertSelfConsistent(Unit unit, string label)
        {
            RolledRangeReconstructor reconstructor = Reconstructor();
            ItemIdentity identity = ItemRecordReader.ReadIdentity(unit);

            ItemRollRanges ranges = reconstructor.Reconstruct(identity, null, null, null);

            Assert.True(ranges.UnsupportedFuncs.Count == 0, label + " hit an unsupported func");

            foreach (RolledStatRange range in ranges.Stats)
            {
                Assert.True(
                    range.Low <= range.High,
                    label + " stat " + range.StatId + " has low " + range.Low
                        + " above high " + range.High);
            }

            // The item's own low-end application must sit inside its own spans, and so must the
            // high-end one.
            var atLow = new Dictionary<int, int>();
            foreach (RolledStatRange range in ranges.Stats)
            {
                atLow[ItemStatReader.PackStatKey(range.Layer, range.StatId)] = range.Low;
            }

            ItemRollRanges checkedLow = reconstructor.Reconstruct(identity, atLow, null, null);
            Assert.True(
                checkedLow.OutOfRange.Count == 0,
                label + " reported its own low end out of range: "
                    + string.Join(",", checkedLow.OutOfRange));
        }

        [Fact]
        public void Every_enabled_unique_reconstructs_consistently()
        {
            int swept = 0;

            for (int row = 0; row < Data.UniqueItems.RowCount; ++row)
            {
                if (Data.UniqueItems.GetInt(row, "enabled") == 0)
                {
                    continue;
                }

                string code = Data.UniqueItems.GetString(row, "code").Trim();
                int classId = Items.ClassIdForCode(code);
                if (classId < 0)
                {
                    continue;
                }

                var unit = new Unit();
                unit.UnitType = 4;
                unit.Quality = 7;
                unit.FileIndex = row;
                unit.ClassId = classId;
                unit.ItemFlags = ItemRecordFlags.Identified;

                AssertSelfConsistent(unit, "unique " + Data.UniqueItems.GetString(row, "index"));
                ++swept;
            }

            // Counted, not guessed: 385 of the enabled rows resolve to a shipped item code. The
            // sweep is worthless if it silently covered a handful, so the number is pinned.
            Assert.Equal(385, swept);
        }

        [Fact]
        public void Every_set_piece_reconstructs_consistently()
        {
            int swept = 0;

            for (int row = 0; row < Data.SetItems.RowCount; ++row)
            {
                string code = Data.SetItems.GetString(row, "item").Trim();
                int classId = Items.ClassIdForCode(code);
                if (classId < 0)
                {
                    continue;
                }

                var unit = new Unit();
                unit.UnitType = 4;
                unit.Quality = 5;
                unit.FileIndex = row;
                unit.ClassId = classId;
                unit.ItemFlags = ItemRecordFlags.Identified;

                AssertSelfConsistent(unit, "set piece " + Data.SetItems.GetString(row, "index"));
                ++swept;
            }

            Assert.True(swept > 120, "only swept " + swept + " set pieces");
        }

        [Fact]
        public void Every_affix_reconstructs_consistently()
        {
            var affixes = new MagicAffixTable(Data);
            int swept = 0;

            for (int id = 1; id <= affixes.RowCount; ++id)
            {
                var unit = new Unit();
                unit.UnitType = 4;
                unit.Quality = 4;
                unit.ClassId = Items.ClassIdForCode("crs");
                unit.ItemFlags = ItemRecordFlags.Identified;
                unit.MagicPrefix[0] = id;

                AssertSelfConsistent(unit, "affix " + id);
                ++swept;
            }

            Assert.True(swept > 1400, "only swept " + swept + " affixes");
        }

        [Fact]
        public void Every_complete_runeword_reconstructs_consistently()
        {
            int swept = 0;

            for (int row = 0; row < Data.Runes.RowCount; ++row)
            {
                if (Data.Runes.GetInt(row, "complete") == 0)
                {
                    continue;
                }

                string key = Data.Runes.GetString(row, "Name").Trim();
                if (key.Length == 0)
                {
                    continue;
                }

                var unit = new Unit();
                unit.UnitType = 4;
                unit.Quality = 2;
                unit.ClassId = Items.ClassIdForCode("crs");
                unit.ItemFlags = ItemRecordFlags.Identified | ItemRecordFlags.Runeword;
                unit.MagicPrefix[0] = Data.Strings.ResolveKey(key);

                AssertSelfConsistent(unit, "runeword " + key);
                ++swept;
            }

            Assert.Equal(78, swept);
        }

        // ---- what the item level unlocks -------------------------------------------------------

        [Fact]
        public void An_absent_item_level_is_reported_rather_than_guessed()
        {
            // Rune of Storms-style `sock` props need the MaxSock tier, which needs a level.
            var unit = new Unit();
            unit.UnitType = 4;
            unit.Quality = 4;
            unit.ClassId = Items.ClassIdForCode("crs");
            unit.ItemFlags = ItemRecordFlags.Identified;
            unit.MagicPrefix[0] = SockAffixId();

            Assert.Equal(-1, unit.ItemLevel);
            Assert.NotEmpty(Engine.Ranges(unit).ItemLevelDependent);
        }

        [Fact]
        public void A_recorded_item_level_removes_the_report()
        {
            var unit = new Unit();
            unit.UnitType = 4;
            unit.Quality = 4;
            unit.ClassId = Items.ClassIdForCode("crs");
            unit.ItemFlags = ItemRecordFlags.Identified;
            unit.MagicPrefix[0] = SockAffixId();
            unit.ItemLevel = 50;

            Assert.Empty(Engine.Ranges(unit).ItemLevelDependent);
        }

        /// <summary>The 1-based affix id of the first `sock` affix, over the concatenated array.</summary>
        private static int SockAffixId()
        {
            var affixes = new MagicAffixTable(Data);

            for (int id = 1; id <= affixes.RowCount; ++id)
            {
                TxtFile table;
                int row;
                if (!affixes.TryResolve(id, out table, out row))
                {
                    continue;
                }

                for (int mod = 1; mod <= 3; ++mod)
                {
                    if (table.GetString(row, "mod" + mod + "code").Trim() == "sock")
                    {
                        return id;
                    }
                }
            }

            Assert.Fail("no sock affix in shipped data");
            return -1;
        }
    }
}
