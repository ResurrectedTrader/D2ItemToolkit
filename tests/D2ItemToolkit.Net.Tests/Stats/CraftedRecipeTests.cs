using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace D2ItemToolkit.Tests
{
    /// <summary>
    /// Crafted-recipe identification. A record stores a crafted item's affixes but not which
    /// cubemain.txt row made it, so the recipe's fixed mods would otherwise sit in
    /// <see cref="ItemRollRanges.Unattributed"/> forever.
    ///
    /// Two kinds of test here. The STRUCTURAL ones read cubemain.txt directly and pin the four
    /// properties the identification rests on — one row per (family, slot), families disjoint on
    /// their marker mods, no mod able to roll to nothing. If a data drift breaks one of those, the
    /// identification silently becomes a guess, and these are what say so.
    ///
    /// The ANCHORS build an item, hand it exactly the stats one recipe writes, and require that
    /// recipe back. One of them does that for ALL 36 rows, the only thing here that reaches
    /// every slot and both mod counts; the named ones single out the shapes the obvious base-code
    /// matching gets wrong — a weapon, whose recipe names an item TYPE, and an amulet, whose recipe
    /// names a type with no item of that code at all.
    /// </summary>
    public class CraftedRecipeTests
    {
        private static readonly D2DataFiles Data = D2DataFiles.LoadEmbedded();
        private static readonly TooltipEngine Engine = TooltipEngine.Embedded;

        private static readonly ItemTable Items = new ItemTable(
            Data.Weapons, Data.Armor, Data.Misc);

        private const int QualityCrafted = 8;

        /// <summary>Every cubemain row whose output cell carries `crf`.</summary>
        private static List<int> CraftedRows()
        {
            var rows = new List<int>();
            for (int row = 0; row < Data.CubeMain.RowCount; ++row)
            {
                string[] parts = Data.CubeMain.GetString(row, "output")
                    .Replace("\"", string.Empty)
                    .Split(',');

                if (parts.Any(p => p.Trim() == "crf"))
                {
                    rows.Add(row);
                }
            }

            return rows;
        }

        /// <summary>
        /// The recipe's family and slot, taken from the shipped description — "-> safety helm" —
        /// rather than from the production slot derivation, so the structural tests below check the
        /// table against an independent reading of it.
        /// </summary>
        private static string[] FamilyAndSlot(int row)
        {
            string description = Data.CubeMain.GetString(row, "description");
            int arrow = description.LastIndexOf("-> ", StringComparison.Ordinal);
            Assert.True(arrow >= 0, "cubemain row " + row + " has no '-> ' in its description");

            string[] words = description.Substring(arrow + 3).Trim().Split(' ');
            Assert.Equal(2, words.Length);
            return words;
        }

        /// <summary>The property codes a recipe's five mod slots carry, blanks dropped.</summary>
        private static List<string> ModCodes(int row)
        {
            var codes = new List<string>();
            for (int mod = 1; mod <= 5; ++mod)
            {
                string code = Data.CubeMain.GetString(row, "mod " + mod).Trim();
                if (code.Length > 0)
                {
                    codes.Add(code);
                }
            }

            return codes;
        }

        // ---- structural ------------------------------------------------------------------------

        [Fact]
        public void The_crafted_recipes_are_four_families_over_nine_slots()
        {
            // The whole narrowing rests on this: the item's own slot leaves four candidates, never
            // more, and the four are one per family. Counted against the shipped file rather than
            // asserted from memory.
            List<int> rows = CraftedRows();
            Assert.Equal(36, rows.Count);

            var bySlot = new Dictionary<string, List<string>>();
            foreach (int row in rows)
            {
                string[] pair = FamilyAndSlot(row);
                if (!bySlot.ContainsKey(pair[1]))
                {
                    bySlot[pair[1]] = new List<string>();
                }

                bySlot[pair[1]].Add(pair[0]);
            }

            Assert.Equal(9, bySlot.Count);
            foreach (KeyValuePair<string, List<string>> slot in bySlot)
            {
                Assert.Equal(4, slot.Value.Count);
                Assert.Equal(4, slot.Value.Distinct().Count());
            }
        }

        [Fact]
        public void Each_family_is_marked_by_a_mod_pair_no_other_family_carries()
        {
            // A drift canary, not the mechanism. PickByRecordedStats requires EVERY stat a
            // candidate writes to be recorded, not a marker pair — but the four families do each
            // keep one opening pair across all nine of their slots, and no two families share one:
            // hitpower gethit-skill + thorns, blood lifesteal + hp, caster regen-mana + mana,
            // safety red-dmg + red-mag. That is what keeps two families sharing a slot from
            // overlapping, and a drift that broke it would show up as recipes going unknown rather
            // than as anything failing outright. Individual mods DO recur across families —
            // `thorns` is also blood's shield mod, `block` appears in three — so the pair is what
            // stays disjoint, not any one code.
            var markerOf = new Dictionary<string, string>();
            int threeMod = 0;
            int fourMod = 0;

            foreach (int row in CraftedRows())
            {
                List<string> mods = ModCodes(row);
                if (mods.Count == 3)
                {
                    ++threeMod;
                }
                else if (mods.Count == 4)
                {
                    ++fourMod;
                }

                string family = FamilyAndSlot(row)[0];
                string marker = mods[0] + "+" + mods[1];

                if (markerOf.ContainsKey(family))
                {
                    Assert.Equal(markerOf[family], marker);
                }
                else
                {
                    Assert.DoesNotContain(marker, markerOf.Values);
                    markerOf[family] = marker;
                }
            }

            Assert.Equal(4, markerOf.Count);

            // Thirty rows write three mods; six — safety's helm, boots, gloves, belt, shield and
            // body — write a fourth, `ac%`. That fourth mod is the only reason the production
            // reader runs past mod 3, so its count is worth pinning: a drift that dropped one would
            // otherwise only show as a quietly missing span.
            Assert.Equal(30, threeMod);
            Assert.Equal(6, fourMod);
        }

        [Fact]
        public void No_crafted_mod_can_roll_to_nothing()
        {
            // What PickByRecordedStats needs is not that a recipe writes SOMETHING but that the set
            // of stat keys it writes is the same at either end of the roll — the filter demands
            // every one of them be recorded, and the pinned spans then have to cover every recorded
            // key. Two cells give that. A value of zero writes nothing at all (0x65ea63), so every
            // min must be 1 or more — compared against max rather than assumed to be the smaller,
            // since reversed cells are legal (ITEMMOD_RollRandomValue swaps them at 0x65e9e0) and
            // `gethit-skill` ships 5/4 for a further reason still, its two cells being a chance and
            // a level rather than a range at all. And a blank `mod N chance` cell is what makes a
            // mod unconditional; a number there would let a recipe's stat be absent, and the
            // all-present filter would reject the right recipe.
            //
            // One code writes a DIFFERENT key set at a low enough roll rather than none: `dmg%` is
            // func 7, and ITEMMODS_PropertyFunc07 degrades into a flat +1 max damage when
            // value * maxdam / 100 rounds away. That only misleads the low-end probe where the two
            // ENDS disagree — maxdam of exactly 2, where 35 rounds away and 60 does not; at 1 both
            // ends degrade alike and at 3 neither does. Slot matching probes every weapon recipe
            // against every `weap` base, so the question is the whole subtree rather than the `blun`
            // and `axe` a dmg% recipe names, and it holds exactly one base at 2: `d33`, which is
            // not spawnable.
            foreach (int row in CraftedRows())
            {
                for (int mod = 1; mod <= 5; ++mod)
                {
                    string where = "cubemain row " + row + " mod " + mod;

                    Assert.True(
                        Data.CubeMain.GetString(row, "mod " + mod + " chance").Trim().Length == 0,
                        where + " is conditional");

                    if (Data.CubeMain.GetString(row, "mod " + mod).Trim().Length == 0)
                    {
                        continue;
                    }

                    int min = Data.CubeMain.GetInt(row, "mod " + mod + " min");
                    int max = Data.CubeMain.GetInt(row, "mod " + mod + " max");

                    Assert.True(Math.Min(min, max) >= 1, where + " can roll to zero");
                }
            }
        }

        [Fact]
        public void Within_a_slot_no_family_is_a_subset_of_another()
        {
            // Two families sharing a slot must be told apart by stats alone. If one family's mods
            // were a subset of another's, an item of the larger would satisfy both and the
            // identification would report unknown for a case it ought to settle.
            var bySlot = new Dictionary<string, List<List<string>>>();

            foreach (int row in CraftedRows())
            {
                string slot = FamilyAndSlot(row)[1];
                if (!bySlot.ContainsKey(slot))
                {
                    bySlot[slot] = new List<List<string>>();
                }

                bySlot[slot].Add(ModCodes(row));
            }

            foreach (KeyValuePair<string, List<List<string>>> slot in bySlot)
            {
                for (int a = 0; a < slot.Value.Count; ++a)
                {
                    for (int b = 0; b < slot.Value.Count; ++b)
                    {
                        if (a == b)
                        {
                            continue;
                        }

                        Assert.False(
                            slot.Value[a].All(slot.Value[b].Contains),
                            slot.Key + ": recipe " + a + " is a subset of " + b);
                    }
                }
            }
        }

        // ---- anchors ---------------------------------------------------------------------------

        private sealed class Stat
        {
            public int Id { get; set; }

            public int Value { get; set; }

            public int Layer { get; set; }
        }

        private static Stat Plain(int id)
        {
            return new Stat { Id = id, Value = 1 };
        }

        private static Unit OfQuality(string code, int quality, params Stat[] stats)
        {
            var unit = new Unit();
            unit.UnitType = 4;
            unit.Quality = quality;
            unit.ClassId = Items.ClassIdForCode(code);
            unit.ItemFlags = ItemRecordFlags.Identified;
            Assert.True(unit.ClassId >= 0, "no item row for " + code);

            var list = new UnitStatList(0, ItemStatListFlags.Magic);
            foreach (Stat stat in stats)
            {
                list.Add(stat.Id, stat.Value, stat.Layer);
            }

            unit.StatsLists.Add(list);
            return unit;
        }

        private static Unit Crafted(string code, params Stat[] stats)
        {
            return OfQuality(code, QualityCrafted, stats);
        }

        private static string RecipeName(ItemRollRanges ranges)
        {
            Assert.True(ranges.CraftedRecipe >= 0, "no recipe identified");

            string[] pair = FamilyAndSlot(ranges.CraftedRecipe);
            return pair[0] + " " + pair[1];
        }

        // Stat ids of the four helm families' fixed mods, resolved from properties.txt stat1 and
        // itemstatcost.txt in the comments below rather than at runtime, so a wrong id here fails
        // loudly instead of tracking a table change.
        private const int NormalDamageReduction = 34;   // red-dmg
        private const int MagicDamageReduction = 35;    // red-mag
        private const int LightResist = 41;             // res-ltng
        private const int ItemArmorPercent = 16;        // ac%
        private const int SkillOnGetHit = 201;          // gethit-skill
        private const int AttackerTakesDamage = 78;     // thorns
        private const int ArmorClassVsMissile = 32;     // ac-miss
        private const int LifeDrainMinDam = 60;         // lifesteal
        private const int MaxHp = 7;                    // hp
        private const int DeadlyStrike = 141;           // deadly
        private const int ManaRecoveryBonus = 27;       // regen-mana
        private const int MaxMana = 9;                  // mana
        private const int ManaDrainMinDam = 62;         // manasteal
        private const int MaxDamagePercent = 17;        // dmg%, high end
        private const int MinDamagePercent = 18;        // dmg%, low end
        private const int FasterCastRate = 105;         // cast1

        /// <summary>
        /// A base the recipe's slot holds, for the twelve rows whose `input 1` names an item TYPE
        /// rather than an item code. `amul` and `ring` have no item of that code at all, so a
        /// member of the type has to stand in either way.
        /// </summary>
        private static readonly Dictionary<string, string> BaseForType =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "blun", "clb" },
                { "axe", "lax" },
                { "rod", "wnd" },
                { "spea", "spr" },
                { "amul", "amu" },
                { "ring", "rin" },
            };

        private static string BaseCodeFor(int row)
        {
            string spec = Data.CubeMain.GetString(row, "input 1").Replace("\"", string.Empty);
            int comma = spec.IndexOf(',');
            string code = (comma < 0 ? spec : spec.Substring(0, comma)).Trim();

            string substitute;
            return BaseForType.TryGetValue(code, out substitute) ? substitute : code;
        }

        /// <summary>
        /// The stats one recipe writes, derived from the shipped tables rather than restated: each
        /// mod code's properties.txt `stat1` resolved through itemstatcost.txt. Two codes need more
        /// than that, and both are recognised by their FUNC and checked for rather than assumed, so
        /// a drift that moved either fails here instead of quietly narrowing the expectation —
        /// `dmg%` is func 7 and carries no `stat1` at all, ITEMMODS_PropertyFunc07 writing the
        /// min/max damage percent pair, and `gethit-skill` is func 11, whose stat sits on the
        /// packed layer `(level &amp; 0x3F) + (skill &lt;&lt; 6)` (0x65f54f), with the mod's param
        /// as the skill and its max as the level.
        /// </summary>
        private static List<Stat> RecipeStats(int row)
        {
            var stats = new List<Stat>();

            for (int mod = 1; mod <= 5; ++mod)
            {
                string where = "cubemain row " + row + " mod " + mod;

                string code = Data.CubeMain.GetString(row, "mod " + mod).Trim();
                if (code.Length == 0)
                {
                    continue;
                }

                int property = Data.Properties.FindRow("code", code);
                Assert.True(property >= 0, where + ": no properties.txt row for " + code);

                // A second set would write a second stat this derivation knows nothing about.
                Assert.True(
                    Data.Properties.GetString(property, "stat2").Trim().Length == 0,
                    where + ": " + code + " writes more than one stat");

                int func = Data.Properties.GetInt(property, "func1");
                string statName = Data.Properties.GetString(property, "stat1").Trim();

                if (func == 7)
                {
                    Assert.Equal(string.Empty, statName);
                    stats.Add(Plain(MinDamagePercent));
                    stats.Add(Plain(MaxDamagePercent));
                    continue;
                }

                int statId = Data.ItemStatCost.StatIdForName(statName);
                Assert.True(statId >= 0, where + ": no itemstatcost.txt row for " + statName);

                if (func == 11)
                {
                    int skill = Data.CubeMain.GetInt(row, "mod " + mod + " param");
                    int level = Data.CubeMain.GetInt(row, "mod " + mod + " max");

                    // A non-positive level is derived from the ITEM's level instead, which a record
                    // need not carry — no crafted mod ships one, and this says so if that changes.
                    Assert.True(level > 0, where + ": level is not literal");

                    stats.Add(new Stat
                    {
                        Id = statId,
                        Value = 1,
                        Layer = (level & 0x3F) + (skill << 6),
                    });
                    continue;
                }

                Assert.True(
                    func == 1 || func == 2 || func == 8,
                    where + ": " + code + " uses unhandled func " + func);

                stats.Add(Plain(statId));
            }

            return stats;
        }

        [Theory]
        [InlineData("safety helm", NormalDamageReduction, MagicDamageReduction, LightResist,
            ItemArmorPercent)]
        [InlineData("blood helm", LifeDrainMinDam, MaxHp, DeadlyStrike)]
        [InlineData("caster helm", ManaRecoveryBonus, MaxMana, ManaDrainMinDam)]
        public void A_family_is_picked_from_the_stats_within_one_slot(string expected,
            params int[] stats)
        {
            // Same base every time — a Crown, whose slot holds these four and nothing else — so the
            // only thing separating them is what the item carries.
            ItemRollRanges ranges = Engine.Ranges(
                Crafted("crn", stats.Select(Plain).ToArray()));

            Assert.Equal(expected, RecipeName(ranges));
            Assert.False(ranges.CraftedRecipeUnknown);
        }

        [Fact]
        public void A_family_marked_by_a_layered_stat_is_matched_on_its_layer()
        {
            // Hitpower opens with `gethit-skill(44)`, a func 11 chance-to-cast. Its stat does not
            // sit on layer 0: the skill and the level are packed into the layer as
            // `(level & 0x3F) + (skill << 6)` (0x65f54f), with the chance as the value. Matching on
            // the bare stat id would have accepted any chance-to-cast-when-struck item as a
            // hitpower craft; matching on the packed key is what makes the marker specific.
            const int FrostNova = 44;
            const int Level = 4;
            const int Layer = (Level & 0x3F) + (FrostNova << 6);

            ItemRollRanges ranges = Engine.Ranges(Crafted(
                "crn",
                new Stat { Id = SkillOnGetHit, Value = 5, Layer = Layer },
                new Stat { Id = AttackerTakesDamage, Value = 5 },
                new Stat { Id = ArmorClassVsMissile, Value = 30 }));

            Assert.Equal("hitpower helm", RecipeName(ranges));

            // The same three stats with the skill on layer 0 is a different item, and not one any
            // recipe makes.
            ItemRollRanges wrongLayer = Engine.Ranges(Crafted(
                "crn",
                new Stat { Id = SkillOnGetHit, Value = 5 },
                new Stat { Id = AttackerTakesDamage, Value = 5 },
                new Stat { Id = ArmorClassVsMissile, Value = 30 }));

            Assert.Equal(-1, wrongLayer.CraftedRecipe);
        }

        [Fact]
        public void A_crafted_weapon_is_reached_through_the_item_type_tree()
        {
            // The four weapon recipes name item TYPES in `input 1` — blun, axe, rod, spea — not item
            // codes, and matching on the code alone found nothing for any weapon. A Large Axe is an
            // `axe`, so its slot is the weapon slot and blood weapon is the family that fits.
            ItemRollRanges ranges = Engine.Ranges(Crafted(
                "lax",
                Plain(LifeDrainMinDam),
                Plain(MaxHp),
                Plain(MinDamagePercent),
                Plain(MaxDamagePercent)));

            Assert.Equal("blood weapon", RecipeName(ranges));
        }

        [Fact]
        public void A_crafted_amulet_is_reached_although_no_item_carries_that_code()
        {
            // `amul` and `ring` are itemtypes.txt codes; the items are `amu` and `rin`. Nothing
            // resolves `amul` as an item code, so this is the case that proves the type fallback is
            // load-bearing rather than defensive.
            ItemRollRanges ranges = Engine.Ranges(Crafted(
                "amu", Plain(ManaRecoveryBonus), Plain(MaxMana), Plain(FasterCastRate)));

            Assert.Equal("caster amulet", RecipeName(ranges));
        }

        [Fact]
        public void Every_recipe_is_identified_from_exactly_the_stats_it_writes()
        {
            // All 36 rows, which is the only thing here that reaches every one of the nine slots
            // and both mod counts. Handing an item EXACTLY the stats its recipe writes and then
            // requiring Unattributed to be empty is what pins the mod count: narrowing the
            // production reader from five mod slots to three still identifies all 36 — the filter
            // only asks that a candidate's stats all be present, so a shorter candidate still fits
            // — but the six four-mod safety rows then lose their `ac%`, and that recorded stat has
            // nowhere left to go but Unattributed.
            List<int> rows = CraftedRows();
            Assert.Equal(36, rows.Count);

            foreach (int row in rows)
            {
                string[] pair = FamilyAndSlot(row);
                string where = pair[0] + " " + pair[1];

                ItemRollRanges ranges = Engine.Ranges(
                    Crafted(BaseCodeFor(row), RecipeStats(row).ToArray()));

                Assert.True(
                    ranges.CraftedRecipe == row,
                    where + ": identified row " + ranges.CraftedRecipe + ", wanted " + row);
                Assert.False(ranges.CraftedRecipeUnknown, where);
                Assert.True(
                    ranges.Unattributed.Count == 0,
                    where + ": unattributed " + string.Join(", ", ranges.Unattributed));
            }
        }

        [Fact]
        public void A_pinned_recipe_moves_its_mods_out_of_unattributed()
        {
            // The point of the whole exercise. Without the recipe those four stats are leftovers;
            // with it they carry spans read off the recipe's own min and max cells.
            Unit item = Crafted(
                "crn",
                Plain(NormalDamageReduction),
                Plain(MagicDamageReduction),
                Plain(LightResist),
                Plain(ItemArmorPercent));

            ItemRollRanges ranges = Engine.Ranges(item);
            int row = ranges.CraftedRecipe;

            Assert.Empty(ranges.Unattributed);

            RolledStatRange resist = ranges.Stats.Single(
                r => r.StatId == LightResist && r.Layer == 0);

            Assert.Equal(Data.CubeMain.GetInt(row, "mod 3 min"), resist.Low);
            Assert.Equal(Data.CubeMain.GetInt(row, "mod 3 max"), resist.High);
            Assert.True(resist.Sources.HasFlag(RollSources.Crafted));
        }

        [Fact]
        public void An_item_carrying_two_families_leaves_the_recipe_unknown()
        {
            // Nothing stops a crafted blood helm's own affixes from supplying mana and mana regen
            // as well. Both families then fit, and a coin-flip between them would attribute spans
            // to stats that never rolled from a recipe — so the answer is no answer.
            ItemRollRanges ranges = Engine.Ranges(Crafted(
                "crn",
                Plain(LifeDrainMinDam),
                Plain(MaxHp),
                Plain(DeadlyStrike),
                Plain(ManaRecoveryBonus),
                Plain(MaxMana),
                Plain(ManaDrainMinDam)));

            Assert.Equal(-1, ranges.CraftedRecipe);
            Assert.True(ranges.CraftedRecipeUnknown);
        }

        [Fact]
        public void An_item_matching_no_family_leaves_the_recipe_unknown()
        {
            ItemRollRanges ranges = Engine.Ranges(Crafted("crn", Plain(LightResist)));

            Assert.Equal(-1, ranges.CraftedRecipe);
            Assert.True(ranges.CraftedRecipeUnknown);
        }

        [Fact]
        public void A_crafted_bow_reaches_the_weapon_slot_and_still_fits_no_family()
        {
            // A bow IS in a slot the recipes cover: itemtypes puts `bow` under `miss` under `weap`,
            // and `weap` is the ninth CraftSlot, so all four weapon recipes are candidates. What
            // rejects them is the stat filter — blood WEAPON writes `dmg%` where blood helm writes
            // `deadly`, so none of the four has every stat it writes recorded here, and none of the
            // other three comes close. The answer is no answer rather than the family the stats
            // happen to resemble.
            ItemRollRanges ranges = Engine.Ranges(Crafted(
                "swb", Plain(LifeDrainMinDam), Plain(MaxHp), Plain(DeadlyStrike)));

            Assert.Equal(-1, ranges.CraftedRecipe);
            Assert.True(ranges.CraftedRecipeUnknown);
        }

        [Fact]
        public void A_crafted_item_in_a_slot_no_recipe_covers_stays_unknown()
        {
            // The other way of reaching unknown, and the only test that gets there: a Small Charm
            // is a `scha`, under `char` under `misc`, so it is under none of the nine CraftSlots
            // and CraftSlotOf returns -1 before any candidate is collected. The stats are blood's,
            // which WOULD pin blood helm in a slot that held recipes, so this fails if the slot
            // lookup ever falls through to one rather than giving up.
            ItemRollRanges ranges = Engine.Ranges(Crafted(
                "cm1", Plain(LifeDrainMinDam), Plain(MaxHp), Plain(DeadlyStrike)));

            Assert.Equal(-1, ranges.CraftedRecipe);
            Assert.True(ranges.CraftedRecipeUnknown);
        }

        [Fact]
        public void An_uncrafted_item_never_reports_a_recipe()
        {
            ItemRollRanges ranges = Engine.Ranges(OfQuality(
                "crn",
                4,
                Plain(NormalDamageReduction),
                Plain(MagicDamageReduction),
                Plain(LightResist),
                Plain(ItemArmorPercent)));

            Assert.Equal(-1, ranges.CraftedRecipe);
            Assert.False(ranges.CraftedRecipeUnknown);
        }
    }
}
