import { describe, expect, it } from 'vitest';
import { ItemRecordFlags } from '../../../src/D2ItemToolkit.Ts/src/Stats/ItemRecord.js';
import {
  RollSources,
  type ItemRollRanges,
  type RolledStatRange,
} from '../../../src/D2ItemToolkit.Ts/src/Stats/RolledRangeReconstructor.js';
import { createUnit, type Unit } from '../../../src/D2ItemToolkit.Ts/src/Stats/Unit.js';
import { ItemTable } from '../../../src/D2ItemToolkit.Ts/src/Tables/ItemTable.js';
import { D2DataFiles } from '../../../src/D2ItemToolkit.Ts/src/Tables/TxtDataProviders.js';
import { TooltipEngine } from '../../../src/D2ItemToolkit.Ts/src/Tooltip/TooltipEngine.js';
import type { TxtFile } from '../../../src/D2ItemToolkit.Ts/src/Data/TxtFile.js';

/**
 * Crafted-recipe identification. A record stores a crafted item's affixes but not which
 * cubemain.txt row made it, so the recipe's fixed mods would otherwise sit in `unattributed`
 * forever.
 *
 * Two kinds of test here. The STRUCTURAL ones read cubemain.txt directly and pin the four
 * properties the identification rests on — one row per (family, slot), families disjoint on their
 * marker mods, no mod able to roll to nothing. If a data drift breaks one of those, the
 * identification silently becomes a guess, and these are what say so.
 *
 * The ANCHORS build an item, hand it exactly the stats one recipe writes, and require that recipe
 * back. One of them does that for ALL 36 rows, which is the only thing here that reaches every slot
 * and both mod counts; the named ones single out the shapes the obvious base-code matching gets
 * wrong — a weapon, whose recipe names an item TYPE, and an amulet, whose recipe names a type with
 * no item of that code at all.
 */
const Data = D2DataFiles.load();
const Engine = TooltipEngine.embedded;
const Items = new ItemTable(Data.weapons, Data.armor, Data.misc);
const cubeMain = Data.cubeMain as TxtFile;
const properties = Data.properties as TxtFile;

const QualityCrafted = 8;

/** Every cubemain row whose output cell carries `crf`. */
function craftedRows(): number[] {
  const rows: number[] = [];
  for (let row = 0; row < cubeMain.rowCount; ++row) {
    const parts = cubeMain.getString(row, 'output').replace(/"/g, '').split(',');

    if (parts.some(p => p.trim() === 'crf')) {
      rows.push(row);
    }
  }

  return rows;
}

/**
 * The recipe's family and slot, taken from the shipped description — "-> safety helm" — rather than
 * from the production slot derivation, so the structural tests below check the table against an
 * independent reading of it.
 */
function familyAndSlot(row: number): [string, string] {
  const description = cubeMain.getString(row, 'description');
  const arrow = description.lastIndexOf('-> ');
  expect(arrow, 'cubemain row ' + String(row) + ' has no "-> "').toBeGreaterThanOrEqual(0);

  const words = description
    .slice(arrow + 3)
    .trim()
    .split(' ');
  expect(words.length).toBe(2);
  return [words[0] as string, words[1] as string];
}

/** The property codes a recipe's five mod slots carry, blanks dropped. */
function modCodes(row: number): string[] {
  const codes: string[] = [];
  for (let mod = 1; mod <= 5; ++mod) {
    const code = cubeMain.getString(row, 'mod ' + String(mod)).trim();
    if (code.length > 0) {
      codes.push(code);
    }
  }

  return codes;
}

describe('crafted recipes, as shipped', () => {
  it('are four families over nine slots', () => {
    // The whole narrowing rests on this: the item's own slot leaves four candidates, never more,
    // and the four are one per family. Counted against the shipped file rather than asserted from
    // memory.
    const rows = craftedRows();
    expect(rows.length).toBe(36);

    const bySlot = new Map<string, string[]>();
    for (const row of rows) {
      const [family, slot] = familyAndSlot(row);
      const families = bySlot.get(slot) ?? [];
      families.push(family);
      bySlot.set(slot, families);
    }

    expect(bySlot.size).toBe(9);
    for (const [slot, families] of bySlot) {
      expect(families.length, slot).toBe(4);
      expect(new Set(families).size, slot).toBe(4);
    }
  });

  it('mark each family with a mod pair no other family carries', () => {
    // A drift canary, not the mechanism. pickByRecordedStats requires EVERY stat a candidate writes
    // to be recorded, not a marker pair — but the four families do each keep one opening pair
    // across all nine of their slots, and no two families share one: hitpower gethit-skill +
    // thorns, blood lifesteal + hp, caster regen-mana + mana, safety red-dmg + red-mag. That is
    // what keeps two families sharing a slot from overlapping, and a drift that broke it would show
    // up as recipes going unknown rather than as anything failing outright. Individual mods DO
    // recur across families — `thorns` is also blood's shield mod, `block` appears in three — so
    // the pair is what stays disjoint, not any one code.
    const markerOf = new Map<string, string>();
    let threeMod = 0;
    let fourMod = 0;

    for (const row of craftedRows()) {
      const mods = modCodes(row);
      if (mods.length === 3) {
        ++threeMod;
      } else if (mods.length === 4) {
        ++fourMod;
      }

      const family = familyAndSlot(row)[0];
      const marker = mods[0] + '+' + mods[1];

      const seen = markerOf.get(family);
      if (seen !== undefined) {
        expect(seen).toBe(marker);
      } else {
        expect([...markerOf.values()]).not.toContain(marker);
        markerOf.set(family, marker);
      }
    }

    expect(markerOf.size).toBe(4);

    // Thirty rows write three mods; six — safety's helm, boots, gloves, belt, shield and body —
    // write a fourth, `ac%`. That fourth mod is the only reason the production reader runs past mod
    // 3, so its count is worth pinning: a drift that dropped one would otherwise only show as a
    // quietly missing span.
    expect(threeMod).toBe(30);
    expect(fourMod).toBe(6);
  });

  it('carry no mod that can roll to nothing', () => {
    // What pickByRecordedStats needs is not that a recipe writes SOMETHING but that the set of stat
    // keys it writes is the same at either end of the roll — the filter demands every one of them
    // be recorded, and the pinned spans then have to cover every recorded key. Two cells give that.
    // A value of zero writes nothing at all (0x65ea63), so every min must be 1 or more — compared
    // against max rather than assumed to be the smaller, since reversed cells are legal
    // (ITEMMOD_RollRandomValue swaps them at 0x65e9e0) and `gethit-skill` ships 5/4 for a further
    // reason still, its two cells being a chance and a level rather than a range at all. And a
    // blank `mod N chance` cell is what makes a mod unconditional; a number there would let a
    // recipe's stat be absent, and the all-present filter would reject the right recipe.
    //
    // One code writes a DIFFERENT key set at a low enough roll rather than none: `dmg%` is func 7,
    // and ITEMMODS_PropertyFunc07 degrades into a flat +1 max damage when value * maxdam / 100
    // rounds away. That only misleads the low-end probe where the two ENDS disagree — maxdam of
    // exactly 2, where 35 rounds away and 60 does not; at 1 both ends degrade alike and at 3
    // neither does. Slot matching probes every weapon recipe against every `weap` base, so the
    // question is the whole subtree rather than the `blun` and `axe` a dmg% recipe names, and it
    // holds exactly one base at 2: `d33`, which is not spawnable.
    for (const row of craftedRows()) {
      for (let mod = 1; mod <= 5; ++mod) {
        const where = 'cubemain row ' + String(row) + ' mod ' + String(mod);

        expect(cubeMain.getString(row, 'mod ' + String(mod) + ' chance').trim(), where).toBe('');

        if (cubeMain.getString(row, 'mod ' + String(mod)).trim().length === 0) {
          continue;
        }

        const min = cubeMain.getInt(row, 'mod ' + String(mod) + ' min');
        const max = cubeMain.getInt(row, 'mod ' + String(mod) + ' max');
        expect(Math.min(min, max), where).toBeGreaterThanOrEqual(1);
      }
    }
  });

  it('never make one family a subset of another within a slot', () => {
    // Two families sharing a slot must be told apart by stats alone. If one family's mods were a
    // subset of another's, an item of the larger would satisfy both and the identification would
    // report unknown for a case it ought to settle.
    const bySlot = new Map<string, string[][]>();

    for (const row of craftedRows()) {
      const slot = familyAndSlot(row)[1];
      const recipes = bySlot.get(slot) ?? [];
      recipes.push(modCodes(row));
      bySlot.set(slot, recipes);
    }

    for (const [slot, recipes] of bySlot) {
      for (let a = 0; a < recipes.length; ++a) {
        for (let b = 0; b < recipes.length; ++b) {
          if (a === b) {
            continue;
          }

          const left = recipes[a] as string[];
          const right = recipes[b] as string[];
          expect(
            left.every(code => right.includes(code)),
            slot + ': recipe ' + String(a) + ' is a subset of ' + String(b),
          ).toBe(false);
        }
      }
    }
  });
});

// Stat ids of the crafted mods used below, resolved from properties.txt stat1 against
// itemstatcost.txt. `dmg%` is the exception: it carries no stat1 cell and writes both damage
// percentages from the func 7 handler.
const NormalDamageReduction = 34; // red-dmg
const MagicDamageReduction = 35; // red-mag
const LightResist = 41; // res-ltng
const ItemArmorPercent = 16; // ac%
const SkillOnGetHit = 201; // gethit-skill
const AttackerTakesDamage = 78; // thorns
const ArmorClassVsMissile = 32; // ac-miss
const LifeDrainMinDam = 60; // lifesteal
const MaxHp = 7; // hp
const DeadlyStrike = 141; // deadly
const ManaRecoveryBonus = 27; // regen-mana
const MaxMana = 9; // mana
const ManaDrainMinDam = 62; // manasteal
const MaxDamagePercent = 17; // dmg%, high end
const MinDamagePercent = 18; // dmg%, low end
const FasterCastRate = 105; // cast1

interface Stat {
  id: number;
  value: number;
  layer?: number;
}

function ofQuality(code: string, quality: number, ...stats: Stat[]): Unit {
  const classId = Items.classIdForCode(code);
  expect(classId, code).toBeGreaterThanOrEqual(0);

  return createUnit({
    unitType: 4,
    quality,
    classId,
    itemFlags: ItemRecordFlags.Identified,
    statsLists: [{ stateNo: 0, flags: 64, stats }],
  });
}

function crafted(code: string, ...stats: Stat[]): Unit {
  return ofQuality(code, QualityCrafted, ...stats);
}

function plain(id: number): Stat {
  return { id, value: 1 };
}

function recipeName(ranges: ItemRollRanges): string {
  expect(ranges.craftedRecipe, 'no recipe identified').toBeGreaterThanOrEqual(0);

  return familyAndSlot(ranges.craftedRecipe).join(' ');
}

/**
 * A base the recipe's slot holds, for the twelve rows whose `input 1` names an item TYPE rather
 * than an item code. `amul` and `ring` have no item of that code at all, so a member of the type
 * has to stand in either way.
 */
const baseForType = new Map<string, string>([
  ['blun', 'clb'],
  ['axe', 'lax'],
  ['rod', 'wnd'],
  ['spea', 'spr'],
  ['amul', 'amu'],
  ['ring', 'rin'],
]);

function baseCodeFor(row: number): string {
  const spec = cubeMain.getString(row, 'input 1').replace(/"/g, '');
  const comma = spec.indexOf(',');
  const code = (comma < 0 ? spec : spec.slice(0, comma)).trim();

  return baseForType.get(code) ?? code;
}

/**
 * The stats one recipe writes, derived from the shipped tables rather than restated: each mod
 * code's properties.txt `stat1` resolved through itemstatcost.txt. Two codes need more than that,
 * and both are recognised by their FUNC and checked for rather than assumed, so a drift that moved
 * either fails here instead of quietly narrowing the expectation — `dmg%` is func 7 and carries no
 * `stat1` at all, ITEMMODS_PropertyFunc07 writing the min/max damage percent pair, and
 * `gethit-skill` is func 11, whose stat sits on the packed layer `(level & 0x3F) + (skill << 6)`
 * (0x65f54f) with the mod's param as the skill and its max as the level.
 */
function recipeStats(row: number): Stat[] {
  const stats: Stat[] = [];

  for (let mod = 1; mod <= 5; ++mod) {
    const where = 'cubemain row ' + String(row) + ' mod ' + String(mod);

    const code = cubeMain.getString(row, 'mod ' + String(mod)).trim();
    if (code.length === 0) {
      continue;
    }

    const property = properties.findRow('code', code);
    expect(property, where + ': no properties.txt row for ' + code).toBeGreaterThanOrEqual(0);

    // A second set would write a second stat this derivation knows nothing about.
    expect(
      properties.getString(property, 'stat2').trim(),
      where + ': ' + code + ' writes more than one stat',
    ).toBe('');

    const func = properties.getInt(property, 'func1');
    const statName = properties.getString(property, 'stat1').trim();

    if (func === 7) {
      expect(statName).toBe('');
      stats.push(plain(MinDamagePercent));
      stats.push(plain(MaxDamagePercent));
      continue;
    }

    const statId = Data.itemStatCost.statIdForName(statName);
    expect(statId, where + ': no itemstatcost.txt row for ' + statName).toBeGreaterThanOrEqual(0);

    if (func === 11) {
      const skill = cubeMain.getInt(row, 'mod ' + String(mod) + ' param');
      const level = cubeMain.getInt(row, 'mod ' + String(mod) + ' max');

      // A non-positive level is derived from the ITEM's level instead, which a record need not
      // carry — no crafted mod ships one, and this says so if that changes.
      expect(level, where + ': level is not literal').toBeGreaterThan(0);

      stats.push({ id: statId, value: 1, layer: (level & 0x3f) + (skill << 6) });
      continue;
    }

    expect([1, 2, 8], where + ': ' + code + ' uses unhandled func ' + String(func)).toContain(func);
    stats.push(plain(statId));
  }

  return stats;
}

describe('crafted recipe identification', () => {
  it.each([
    ['safety helm', [NormalDamageReduction, MagicDamageReduction, LightResist, ItemArmorPercent]],
    ['blood helm', [LifeDrainMinDam, MaxHp, DeadlyStrike]],
    ['caster helm', [ManaRecoveryBonus, MaxMana, ManaDrainMinDam]],
  ])('picks %s from the stats within one slot', (expected, stats) => {
    // Same base every time — a Crown, whose slot holds four recipes and nothing else — so the only
    // thing separating them is what the item carries.
    const ranges = Engine.ranges(crafted('crn', ...stats.map(id => plain(id))));

    expect(recipeName(ranges)).toBe(expected);
    expect(ranges.craftedRecipeUnknown).toBe(false);
  });

  it('matches a family marked by a layered stat on its layer', () => {
    // Hitpower opens with `gethit-skill(44)`, a func 11 chance-to-cast. Its stat does not sit on
    // layer 0: the skill and the level are packed into the layer as `(level & 0x3F) + (skill << 6)`
    // (0x65f54f), with the chance as the value. Matching on the bare stat id would have accepted any
    // chance-to-cast-when-struck item as a hitpower craft; matching on the packed key is what makes
    // the marker specific.
    const FrostNova = 44;
    const Level = 4;
    const Layer = (Level & 0x3f) + (FrostNova << 6);

    const ranges = Engine.ranges(
      crafted(
        'crn',
        { id: SkillOnGetHit, value: 5, layer: Layer },
        { id: AttackerTakesDamage, value: 5 },
        { id: ArmorClassVsMissile, value: 30 },
      ),
    );

    expect(recipeName(ranges)).toBe('hitpower helm');

    // The same three stats with the skill on layer 0 is a different item, and not one any recipe
    // makes.
    const wrongLayer = Engine.ranges(
      crafted(
        'crn',
        { id: SkillOnGetHit, value: 5 },
        { id: AttackerTakesDamage, value: 5 },
        { id: ArmorClassVsMissile, value: 30 },
      ),
    );

    expect(wrongLayer.craftedRecipe).toBe(-1);
  });

  it('reaches a crafted weapon through the item type tree', () => {
    // The four weapon recipes name item TYPES in `input 1` — blun, axe, rod, spea — not item codes,
    // and matching on the code alone found nothing for any weapon. A Large Axe is an `axe`, so its
    // slot is the weapon slot and blood weapon is the family that fits.
    const ranges = Engine.ranges(
      crafted(
        'lax',
        plain(LifeDrainMinDam),
        plain(MaxHp),
        plain(MinDamagePercent),
        plain(MaxDamagePercent),
      ),
    );

    expect(recipeName(ranges)).toBe('blood weapon');
  });

  it('reaches a crafted amulet although no item carries that code', () => {
    // `amul` and `ring` are itemtypes.txt codes; the items are `amu` and `rin`. Nothing resolves
    // `amul` as an item code, so this is the case that proves the type fallback is load-bearing
    // rather than defensive.
    const ranges = Engine.ranges(
      crafted('amu', plain(ManaRecoveryBonus), plain(MaxMana), plain(FasterCastRate)),
    );

    expect(recipeName(ranges)).toBe('caster amulet');
  });

  it('identifies every recipe from exactly the stats it writes', () => {
    // All 36 rows, which is the only thing here that reaches every one of the nine slots and both
    // mod counts. Handing an item EXACTLY the stats its recipe writes and then requiring
    // `unattributed` to be empty is what pins the mod count: narrowing the production reader from
    // five mod slots to three still identifies all 36 — the filter only asks that a candidate's
    // stats all be present, so a shorter candidate still fits — but the six four-mod safety rows
    // then lose their `ac%`, and that recorded stat has nowhere left to go but `unattributed`.
    const rows = craftedRows();
    expect(rows.length).toBe(36);

    for (const row of rows) {
      const where = familyAndSlot(row).join(' ');
      const ranges = Engine.ranges(crafted(baseCodeFor(row), ...recipeStats(row)));

      expect(ranges.craftedRecipe, where).toBe(row);
      expect(ranges.craftedRecipeUnknown, where).toBe(false);
      expect(ranges.unattributed, where).toEqual([]);
    }
  });

  it("moves a pinned recipe's mods out of unattributed", () => {
    // The point of the whole exercise. Without the recipe those four stats are leftovers; with it
    // they carry spans read off the recipe's own min and max cells.
    const ranges = Engine.ranges(
      crafted(
        'crn',
        plain(NormalDamageReduction),
        plain(MagicDamageReduction),
        plain(LightResist),
        plain(ItemArmorPercent),
      ),
    );

    const row = ranges.craftedRecipe;
    expect(ranges.unattributed).toEqual([]);

    const resists = ranges.stats.filter(r => r.statId === LightResist && r.layer === 0);
    expect(resists.length).toBe(1);

    const resist = resists[0] as RolledStatRange;
    expect(resist.low).toBe(cubeMain.getInt(row, 'mod 3 min'));
    expect(resist.high).toBe(cubeMain.getInt(row, 'mod 3 max'));
    expect(resist.sources & RollSources.Crafted).not.toBe(0);
  });

  it('leaves the recipe unknown when two families fit', () => {
    // Nothing stops a crafted blood helm's own affixes from supplying mana and mana regen as well.
    // Both families then fit, and a coin-flip between them would attribute spans to stats that never
    // rolled from a recipe — so the answer is no answer.
    const ranges = Engine.ranges(
      crafted(
        'crn',
        plain(LifeDrainMinDam),
        plain(MaxHp),
        plain(DeadlyStrike),
        plain(ManaRecoveryBonus),
        plain(MaxMana),
        plain(ManaDrainMinDam),
      ),
    );

    expect(ranges.craftedRecipe).toBe(-1);
    expect(ranges.craftedRecipeUnknown).toBe(true);
  });

  it('leaves the recipe unknown when no family fits', () => {
    const ranges = Engine.ranges(crafted('crn', plain(LightResist)));

    expect(ranges.craftedRecipe).toBe(-1);
    expect(ranges.craftedRecipeUnknown).toBe(true);
  });

  it('reaches the weapon slot for a bow and still fits no family', () => {
    // A bow IS in a slot the recipes cover: itemtypes puts `bow` under `miss` under `weap`, and
    // `weap` is the ninth craft slot, so all four weapon recipes are candidates. What rejects them
    // is the stat filter — blood WEAPON writes `dmg%` where blood helm writes `deadly`, so none of
    // the four has every stat it writes recorded here, and none of the other three comes close. The
    // answer is no answer rather than the family the stats happen to resemble.
    const ranges = Engine.ranges(
      crafted('swb', plain(LifeDrainMinDam), plain(MaxHp), plain(DeadlyStrike)),
    );

    expect(ranges.craftedRecipe).toBe(-1);
    expect(ranges.craftedRecipeUnknown).toBe(true);
  });

  it('leaves the recipe unknown in a slot no recipe covers', () => {
    // The other way of reaching unknown, and the only test that gets there: a Small Charm is a
    // `scha`, under `char` under `misc`, so it is under none of the nine craft slots and
    // craftSlotOf returns -1 before any candidate is collected. The stats are blood's, which WOULD
    // pin blood helm in a slot that held recipes, so this fails if the slot lookup ever falls
    // through to one rather than giving up.
    const ranges = Engine.ranges(
      crafted('cm1', plain(LifeDrainMinDam), plain(MaxHp), plain(DeadlyStrike)),
    );

    expect(ranges.craftedRecipe).toBe(-1);
    expect(ranges.craftedRecipeUnknown).toBe(true);
  });

  it('never reports a recipe for an uncrafted item', () => {
    const ranges = Engine.ranges(
      ofQuality(
        'crn',
        4,
        plain(NormalDamageReduction),
        plain(MagicDamageReduction),
        plain(LightResist),
        plain(ItemArmorPercent),
      ),
    );

    expect(ranges.craftedRecipe).toBe(-1);
    expect(ranges.craftedRecipeUnknown).toBe(false);
  });
});
