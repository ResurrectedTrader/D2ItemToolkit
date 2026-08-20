import { describe, expect, it } from 'vitest';
import { ItemIdentity } from '../../../src/D2ItemToolkit.Ts/src/Stats/ItemRecord.js';
import { ItemStatReader } from '../../../src/D2ItemToolkit.Ts/src/Stats/ItemStatReader.js';
import { ItemTable } from '../../../src/D2ItemToolkit.Ts/src/Tables/ItemTable.js';
import { ItemTypeTree } from '../../../src/D2ItemToolkit.Ts/src/Tables/ItemTypeTree.js';
import {
  PropertyApplier,
  type ItemProperty,
} from '../../../src/D2ItemToolkit.Ts/src/Stats/PropertyApplier.js';
import { D2DataFiles } from '../../../src/D2ItemToolkit.Ts/src/Tables/TxtDataProviders.js';

/**
 * The property handlers behind dword_7462F8 (0x65eb30..0x65fae0), one func at a time.
 *
 * PropertyApplier.test.ts only sweeps the funcs that gems.txt happens to reach, which leaves the
 * damage handlers and their weapon-shape arms unexercised. Every property below is a REAL
 * properties.txt row, so the func/stat/set triples come from shipped data rather than being made
 * up; only the {param, min, max} quadruple is supplied, exactly as a gems.txt mod would.
 */

const Data = D2DataFiles.load();
const Items = new ItemTable(Data.weapons, Data.armor, Data.misc);
const Types = new ItemTypeTree(Data.itemTypes);

const StatMinDamage = 21;
const StatMaxDamage = 22;
const StatSecondaryMinDamage = 23;
const StatSecondaryMaxDamage = 24;
const StatMaxDamagePercent = 17;
const StatMinDamagePercent = 18;
const StatThrowMinDamage = 159;
const StatThrowMaxDamage = 160;
const StatPoisonMaxDamage = 58;
const StatPoisonCount = 326;
const StatIndestructible = 152;

function applier(): PropertyApplier {
  return new PropertyApplier(Data, Items, Types);
}

function itemFor(code: string): ItemIdentity {
  const item = new ItemIdentity();
  item.classId = Items.classIdForCode(code);
  item.code = code;
  expect(item.classId, code).toBeGreaterThanOrEqual(0);
  return item;
}

function property(code: string, param: number, min: number, max: number): ItemProperty {
  const applied = applier();
  const id = applied.properties.rowForCode(code);
  expect(id, code).toBeGreaterThanOrEqual(0);
  return { propertyId: id, param, min, max };
}

/** Applies one property to a bare item and returns the resulting stats as layer/stat pairs. */
function apply(
  code: string,
  itemCode: string,
  quad: { param?: number; min: number; max: number },
): Map<number, number> {
  const into = new Map<number, number>();
  applier().apply(
    PropertyApplier.PropModeGem,
    itemFor(itemCode),
    property(code, quad.param ?? 0, quad.min, quad.max),
    into,
  );

  return into;
}

function stat(stats: Map<number, number>, statId: number): number | undefined {
  return stats.get(ItemStatReader.packStatKey(0, statId));
}

/** The stat ids present, ascending — the assertion that nothing EXTRA was written. */
function ids(stats: Map<number, number>): number[] {
  return [...stats.keys()].map(k => ItemStatReader.statFromKey(k)).sort((a, b) => a - b);
}

describe('PropertyApplier funcs', () => {
  it('func 1 rolls once and writes the row stat', () => {
    // "ac" is a lone func 1 onto stat 31.
    const stats = apply('ac', 'cap', { min: 12, max: 12 });

    expect(ids(stats)).toEqual([31]);
    expect(stat(stats, 31)).toBe(12);
  });

  it('func 3 reuses the value func 1 already rolled', () => {
    // "res-all" is func [1,3,3,3] onto the four single resistances. The whole point of func 3 is
    // that it does NOT roll again — all four resistances have to come out identical.
    const stats = apply('res-all', 'cap', { min: 30, max: 30 });

    expect(ids(stats)).toEqual([39, 41, 43, 45]);
    for (const id of [39, 41, 43, 45]) {
      expect(stat(stats, id), String(id)).toBe(30);
    }
  });

  it('func 2 writes the percentage stat', () => {
    const stats = apply('ac%', 'cap', { min: 15, max: 15 });

    expect(ids(stats)).toEqual([16]);
    expect(stat(stats, 16)).toBe(15);
  });

  it('func 8 writes its row stat like func 1', () => {
    // "swing1" is func 8 onto stat 93 (increased attack speed).
    const stats = apply('swing1', 'cap', { min: 20, max: 20 });

    expect(ids(stats)).toEqual([93]);
    expect(stat(stats, 93)).toBe(20);
  });

  it('func 20 writes indestructible unshifted at one regardless of the quadruple', () => {
    const stats = apply('indestruct', 'cap', { min: 0, max: 0 });

    expect(ids(stats)).toEqual([StatIndestructible]);
    expect(stat(stats, StatIndestructible)).toBe(1);
  });

  it('func 17 prefers the param over the range', () => {
    // "ac/lvl" is func 17 onto stat 214. Param wins outright — the range is never consulted.
    const stats = apply('ac/lvl', 'cap', { param: 7, min: 3, max: 3 });

    expect(ids(stats)).toEqual([214]);
    expect(stat(stats, 214)).toBe(7);
  });

  it('func 17 falls back to the range when the param is zero', () => {
    const stats = apply('ac/lvl', 'cap', { param: 0, min: 4, max: 4 });

    expect(stat(stats, 214)).toBe(4);
  });

  it('a stat with a nonzero ValShift is stored shifted left', () => {
    // stat 216 (hp/lvl) carries ValShift 8. The description engine shifts back down, so an
    // unshifted store would render as 1/256th of the real value.
    expect(Data.itemStatCost.tryGetStat(216)?.valShift).toBe(8);

    const stats = apply('hp/lvl', 'cap', { param: 4, min: 0, max: 0 });
    expect(stat(stats, 216)).toBe(4 << 8);
  });

  it('a zero value writes no stat at all', () => {
    // 0x65ea50 returns before touching the list when the value is zero, so a property that
    // rolls nothing leaves no trace rather than an explicit zero.
    expect(apply('ac', 'cap', { min: 0, max: 0 }).size).toBe(0);
  });
});

describe('PropertyApplier ranges', () => {
  it('an equal range needs no seed', () => {
    const applied = applier();
    const into = new Map<number, number>();
    applied.apply(PropertyApplier.PropModeGem, itemFor('cap'), property('ac', 0, 9, 9), into);

    expect(stat(into, 31)).toBe(9);
    expect([...applied.rolledRanges]).toEqual([]);
  });

  it('a genuine range resolves to its low end and is reported', () => {
    const applied = applier();
    const into = new Map<number, number>();
    const prop = property('ac', 0, 5, 40);
    applied.apply(PropertyApplier.PropModeGem, itemFor('cap'), prop, into);

    expect(stat(into, 31)).toBe(5);
    expect([...applied.rolledRanges]).toEqual([prop.propertyId]);
  });

  it('an inverted range is swapped before the low end is taken', () => {
    // max < min swaps the pair (0x65eb6a), so the low end is the SMALLER of the two either way.
    const applied = applier();
    const into = new Map<number, number>();
    const prop = property('ac', 0, 40, 5);
    applied.apply(PropertyApplier.PropModeGem, itemFor('cap'), prop, into);

    expect(stat(into, 31)).toBe(5);
    expect([...applied.rolledRanges]).toEqual([prop.propertyId]);
  });
});

/**
 * Funcs 5 and 6 pick which of the three damage stats to write from the item's own damage columns,
 * so the same property lands differently on a one-hander, a pure two-hander, a versatile weapon, a
 * throwable and a non-weapon. This matrix is the only thing that separates the six arms.
 */
describe('PropertyApplier damage shape', () => {
  it.each([
    // A non-weapon fails every weapon test, so all three destinations are written.
    ['cap', [StatMinDamage, StatSecondaryMinDamage, StatThrowMinDamage]],
    // One-handed only: mindam is set, 2handmindam is not.
    ['axe', [StatMinDamage]],
    // Two-handed only: mindam is zero, so the primary arm is skipped entirely.
    ['bax', [StatSecondaryMinDamage]],
    // Versatile: both columns are set, so both arms fire.
    ['2hs', [StatMinDamage, StatSecondaryMinDamage]],
    // Throwable: the missile arm joins the one-handed arm.
    ['tkf', [StatMinDamage, StatThrowMinDamage]],
  ])('func 5 on %s writes only the damage stats that item can carry', (code, expected) => {
    const stats = apply('dmg-min', code, { min: 6, max: 6 });

    expect(ids(stats)).toEqual([...expected].sort((a, b) => a - b));
    for (const id of expected) {
      expect(stat(stats, id), String(id)).toBe(6);
    }
  });

  it.each([
    ['cap', [StatMaxDamage, StatSecondaryMaxDamage, StatThrowMaxDamage]],
    ['axe', [StatMaxDamage]],
    ['bax', [StatSecondaryMaxDamage]],
    ['2hs', [StatMaxDamage, StatSecondaryMaxDamage]],
    ['tkf', [StatMaxDamage, StatThrowMaxDamage]],
  ])('func 6 on %s mirrors func 5 across the max stats', (code, expected) => {
    const stats = apply('dmg-max', code, { min: 9, max: 9 });

    expect(ids(stats)).toEqual([...expected].sort((a, b) => a - b));
    for (const id of expected) {
      expect(stat(stats, id), String(id)).toBe(9);
    }
  });

  it('func 5 floors the total at one rather than at zero', () => {
    // axe has mindam 4. A -10 would take it to -6, so the value is clamped to 1 - 4 = -3,
    // leaving a displayed minimum of exactly 1.
    expect(Items.getInt(Items.classIdForCode('axe'), 'mindam')).toBe(4);

    const stats = apply('dmg-min', 'axe', { min: -10, max: -10 });
    expect(stat(stats, StatMinDamage)).toBe(-3);
  });

  it('func 6 floors the total at zero, not at one', () => {
    // The one place funcs 5 and 6 are not mirror images: axe has maxdam 11, and -20 clamps to
    // 0 - 11 rather than 1 - 11.
    expect(Items.getInt(Items.classIdForCode('axe'), 'maxdam')).toBe(11);

    const stats = apply('dmg-max', 'axe', { min: -20, max: -20 });
    expect(stat(stats, StatMaxDamage)).toBe(-11);
  });

  it('a clamp that lands on zero writes nothing', () => {
    // cap has no damage columns at all, so baseDamage is zero and the clamp never engages —
    // the raw negative is written straight through.
    const stats = apply('dmg-max', 'cap', { min: -20, max: -20 });
    expect(stat(stats, StatMaxDamage)).toBe(-20);
  });
});

describe('PropertyApplier enhanced damage', () => {
  it('writes the percentage pair when the bonus survives the divide', () => {
    // axe maxdam 11, so 50% is 5 — a real increase, and the percentages stand.
    const stats = apply('dmg%', 'axe', { min: 50, max: 50 });

    expect(ids(stats)).toEqual([StatMaxDamagePercent, StatMinDamagePercent]);
    expect(stat(stats, StatMinDamagePercent)).toBe(50);
    expect(stat(stats, StatMaxDamagePercent)).toBe(50);
  });

  it('degrades to a flat plus one max damage when the percentage rounds away', () => {
    // 5% of 11 truncates to 0, so on a WEAPON the percentage pair would be worthless and the
    // handler substitutes func 6 with a value of 1 instead.
    const stats = apply('dmg%', 'axe', { min: 5, max: 5 });

    expect(ids(stats)).toEqual([StatMaxDamage]);
    expect(stat(stats, StatMaxDamage)).toBe(1);
  });

  it('never degrades on a non-weapon even with no damage to scale', () => {
    // cap has maxdam 0, so the bonus is 0 — but the weapon test fails first, so the
    // percentages are written as-is.
    const stats = apply('dmg%', 'cap', { min: 5, max: 5 });

    expect(ids(stats)).toEqual([StatMaxDamagePercent, StatMinDamagePercent]);
    expect(stat(stats, StatMinDamagePercent)).toBe(5);
  });

  it('scales off the larger of the one and two handed columns', () => {
    // 2hs has maxdam 9 and 2handmaxdam 17. 6% of 17 is 1, but 6% of 9 is 0 — so reading the
    // one-handed column alone would wrongly degrade this to +1 max damage.
    expect(Items.getInt(Items.classIdForCode('2hs'), 'maxdam')).toBe(9);
    expect(Items.getInt(Items.classIdForCode('2hs'), '2handmaxdam')).toBe(17);

    const stats = apply('dmg%', '2hs', { min: 6, max: 6 });
    expect(stat(stats, StatMinDamagePercent)).toBe(6);
  });
});

describe('PropertyApplier func 15 and 16', () => {
  it('writes the elemental min and max directly', () => {
    // "dmg-fire" is func [15,16] onto stats 48 and 49. Neither is a physical damage stat, so
    // both go straight to the stat list with no weapon-shape routing.
    const stats = apply('dmg-fire', 'cap', { min: 3, max: 14 });

    expect(ids(stats)).toEqual([48, 49]);
    expect(stat(stats, 48)).toBe(3);
    // Func 16 takes nMax, not nMin — the one asymmetry between the two.
    expect(stat(stats, 49)).toBe(14);
  });

  it('routes physical damage back through the weapon-shape handlers', () => {
    // "dmg-norm" is func [15,16] onto stats 21 and 22, which ARE the damage stats — so on a
    // one-hander the secondary and throwing arms must stay empty.
    const stats = apply('dmg-norm', 'axe', { min: 3, max: 14 });

    expect(ids(stats)).toEqual([StatMinDamage, StatMaxDamage]);
    expect(stat(stats, StatMinDamage)).toBe(3);
    expect(stat(stats, StatMaxDamage)).toBe(14);
  });

  it('fans physical damage across every arm on a non-weapon', () => {
    const stats = apply('dmg-norm', 'cap', { min: 3, max: 14 });

    expect(ids(stats)).toEqual(
      [
        StatMinDamage,
        StatMaxDamage,
        StatSecondaryMinDamage,
        StatSecondaryMaxDamage,
        StatThrowMinDamage,
        StatThrowMaxDamage,
      ].sort((a, b) => a - b),
    );

    expect(stat(stats, StatSecondaryMinDamage)).toBe(3);
    expect(stat(stats, StatThrowMaxDamage)).toBe(14);
  });

  it('writes the throwing damage pair without routing', () => {
    // "dmg-throw" targets stats 159 and 160 by name. Func 15 only reroutes on stat 21, so
    // these are written directly even though they are damage stats.
    const stats = apply('dmg-throw', 'axe', { min: 2, max: 8 });

    expect(ids(stats)).toEqual([StatThrowMinDamage, StatThrowMaxDamage]);
    expect(stat(stats, StatThrowMinDamage)).toBe(2);
    expect(stat(stats, StatThrowMaxDamage)).toBe(8);
  });
});

describe('PropertyApplier poison', () => {
  it('drags a duration along with the poison damage', () => {
    // "dmg-pois" is func [15,16,17] onto stats 57, 58 and 59. Writing stat 58 pulls stat 326
    // with it, or the description reads "over 0 seconds".
    const stats = apply('dmg-pois', 'cap', { param: 75, min: 10, max: 20 });

    expect(stat(stats, 57)).toBe(10);
    expect(stat(stats, StatPoisonMaxDamage)).toBe(20);
    expect(stat(stats, StatPoisonCount)).toBe(1);
    // Func 17 on stat 59 takes the param — the poison's length.
    expect(stat(stats, 59)).toBe(75);
  });

  it('accumulates the duration once per application', () => {
    // Two poison gems in two sockets each add their own count, matching the ADD arm at 0x65eb0a.
    const applied = applier();
    const into = new Map<number, number>();
    const item = itemFor('cap');
    const prop = property('dmg-pois', 75, 10, 20);

    applied.apply(PropertyApplier.PropModeGem, item, prop, into);
    applied.apply(PropertyApplier.PropModeGem, item, prop, into);

    expect(stat(into, StatPoisonMaxDamage)).toBe(40);
    expect(stat(into, StatPoisonCount)).toBe(2);
  });

  it('leaves the duration alone when no poison damage is written', () => {
    const stats = apply('dmg-fire', 'cap', { min: 3, max: 14 });
    expect(stat(stats, StatPoisonCount)).toBeUndefined();
  });
});
