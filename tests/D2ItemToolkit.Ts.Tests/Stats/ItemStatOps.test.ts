import { describe, expect, it } from 'vitest';
import { ItemStatOps } from '../../../src/D2ItemToolkit.Ts/src/Stats/ItemStatOps.js';
import { ItemStatReader } from '../../../src/D2ItemToolkit.Ts/src/Stats/ItemStatReader.js';
import type { IItemStatOpTable, ItemStatOpEntry } from '../../../src/D2ItemToolkit.Ts/src/Types.js';

// The reverse index ItemStatCost.txt's five op-13 rows compile to — 16 drives 31, 17 drives
// 22/24/160, 18 drives 21/23/159, 75 drives 73 and 94 drives 92. Spelled out here because the
// real table belongs to the data slice; EndToEndRecordTests pins these same nine pairs against
// the shipped file.
const ShippedEntries: readonly ItemStatOpEntry[] = [
  { percentStat: 16, targetStat: 31 },
  { percentStat: 17, targetStat: 22 },
  { percentStat: 17, targetStat: 24 },
  { percentStat: 17, targetStat: 160 },
  { percentStat: 18, targetStat: 21 },
  { percentStat: 18, targetStat: 23 },
  { percentStat: 18, targetStat: 159 },
  { percentStat: 75, targetStat: 73 },
  { percentStat: 94, targetStat: 92 },
];

function table(entries: readonly ItemStatOpEntry[] = ShippedEntries): IItemStatOpTable {
  return { percentOfBaseEntries: entries };
}

function stats(pairs: readonly [number, number][]): Map<number, number> {
  return new Map(pairs.map(([stat, value]) => [ItemStatReader.packStatKey(0, stat), value]));
}

function at(merged: ReadonlyMap<number, number>, stat: number): number | undefined {
  return merged.get(ItemStatReader.packStatKey(0, stat));
}

describe('ItemStatOps.resolve', () => {
  it('leaves the base view alone', () => {
    // BaseOnly IS op 13's input (0x624ed4 always reads Stats), and DamageIsModified is
    // merged-minus-base — moving the base would strip every colour marker.
    const baseStats = stats([[21, 4]]);
    const merged = stats([
      [21, 4],
      [18, 150],
    ]);

    ItemStatOps.resolve(merged, baseStats, table());

    expect(at(baseStats, 21)).toBe(4);
    expect(at(merged, 21)).toBe(10);
  });

  it('applies the summed percentage once against the base', () => {
    // A 100% prefix plus a 50% jewel is one 150%: the percent is summed in the merged view
    // before this pass sees it.
    const merged = stats([
      [21, 4],
      [22, 7],
      [18, 150],
      [17, 150],
    ]);

    ItemStatOps.resolve(
      merged,
      stats([
        [21, 4],
        [22, 7],
      ]),
      table(),
    );

    expect(at(merged, 21)).toBe(10);
    expect(at(merged, 22)).toBe(17);
  });

  it('truncates a small percent to nothing', () => {
    // Throwing Knife max throw 9; trunc(9 * 10 / 100) = 0, so the numbers do not move.
    const merged = stats([
      [159, 4],
      [160, 9],
      [18, 10],
      [17, 10],
    ]);

    ItemStatOps.resolve(
      merged,
      stats([
        [159, 4],
        [160, 9],
      ]),
      table(),
    );

    expect(at(merged, 159)).toBe(4);
    expect(at(merged, 160)).toBe(9);

    // And the percent stats still drop: the gate is on the TARGET's computed value, which is 9,
    // not on whether the percent changed it. This is why the throw line comes out unmarked.
    expect(at(merged, 17)).toBeUndefined();
    expect(at(merged, 18)).toBeUndefined();
  });

  // These four mirror tests/D2ItemToolkit.Net.Tests/Stats/ItemStatOpsTests.cs one for one.
  it('drops a percent that landed on a target', () => {
    // 75 folds onto 73, then 0x626821 clears the update flag because the owner is an item and
    // 0x626847 skips the write that would have stored 75 itself.
    const merged = stats([
      [73, 62],
      [75, 25],
    ]);

    ItemStatOps.resolve(merged, stats([[73, 62]]), table());

    expect(at(merged, 73)).toBe(77); // 62 + trunc(62 * 25 / 100)
    expect(at(merged, 75)).toBeUndefined();
  });

  it('keeps a percent whose target computed zero', () => {
    // 0x62678d/0x626790: a target that computes ZERO skips the switch entirely, so the flag is
    // never cleared and the percent stat IS stored. Stat 94's target 92 is absent here.
    const merged = stats([[94, 40]]);

    ItemStatOps.resolve(merged, stats([]), table());

    expect(at(merged, 94)).toBe(40);
  });

  it('drops a percent with three targets when only one is non-zero', () => {
    // The flag is per PERCENT stat, not per target: 18 drives 21, 23 and 159, and only 21 exists
    // here. 0x626847 tests the single flag after all three targets.
    const merged = stats([
      [21, 4],
      [18, 150],
    ]);

    ItemStatOps.resolve(merged, stats([[21, 4]]), table());

    expect(at(merged, 21)).toBe(10); // 4 + trunc(4 * 150 / 100)
    expect(at(merged, 18)).toBeUndefined();
  });

  it('never touches the base view when dropping', () => {
    // BaseOnly IS this pass's input (0x624ed4 always reads Stats), and Bonus is merged-minus-base
    // — so the drop must not reach it or the subtraction changes meaning.
    const baseStats = stats([
      [73, 62],
      [75, 25],
    ]);
    const merged = stats([
      [73, 62],
      [75, 25],
    ]);

    ItemStatOps.resolve(merged, baseStats, table());

    expect(at(baseStats, 73)).toBe(62);
    expect(at(baseStats, 75)).toBe(25);
  });

  it('truncates toward zero rather than down', () => {
    const merged = stats([
      [21, -9],
      [18, 50],
    ]);

    ItemStatOps.resolve(merged, stats([[21, -9]]), table());

    // trunc(-4.5) is -4, so the total is -13 rather than -14.
    expect(at(merged, 21)).toBe(-13);
  });

  it('skips an entry whose percent is zero or absent', () => {
    const merged = stats([
      [21, 4],
      [18, 0],
    ]);

    ItemStatOps.resolve(merged, stats([[21, 4]]), table());

    expect(at(merged, 21)).toBe(4);

    // The zero percent moves nothing, but it is still dropped: 0x626821's gate is on the TARGET's
    // computed value (4 here), never on the percent's own.
    expect(at(merged, 18)).toBeUndefined();
    expect(merged.size).toBe(1);
  });

  it('skips an entry whose base is zero or absent', () => {
    const merged = stats([
      [21, 40],
      [18, 150],
    ]);

    ItemStatOps.resolve(merged, stats([[21, 0]]), table());

    expect(at(merged, 21)).toBe(40);

    const noBase = stats([
      [21, 40],
      [18, 150],
    ]);

    ItemStatOps.resolve(noBase, stats([]), table());

    expect(at(noBase, 21)).toBe(40);
  });

  it('reads the percent from the merged view and the value from the base view', () => {
    // The percent stat is only ever on a chain node, so it is absent from the base view.
    const merged = stats([
      [31, 500],
      [16, 60],
    ]);

    ItemStatOps.resolve(merged, stats([[31, 445]]), table());

    // 500 + trunc(445 * 60 / 100) = 500 + 267.
    expect(at(merged, 31)).toBe(767);
  });

  it('adds a target the merged view never carried, in key order', () => {
    // 94 is dropped once it lands, so the ordering this guards is now visible through a percent
    // whose target computes ZERO and therefore survives: 16 -> 31 lands, 94 -> 92 does not.
    const merged = stats([
      [16, 50],
      [94, 25],
    ]);

    ItemStatOps.resolve(merged, stats([[31, 60]]), table());

    expect(at(merged, 31)).toBe(30);
    expect(at(merged, 16)).toBeUndefined();
    expect([...merged.keys()]).toEqual([
      ItemStatReader.packStatKey(0, 31),
      ItemStatReader.packStatKey(0, 94),
    ]);
  });

  it('accumulates every target of one percent stat', () => {
    const merged = stats([
      [21, 4],
      [23, 10],
      [159, 8],
      [18, 150],
    ]);

    ItemStatOps.resolve(
      merged,
      stats([
        [21, 4],
        [23, 10],
        [159, 8],
      ]),
      table(),
    );

    expect(at(merged, 21)).toBe(10);
    expect(at(merged, 23)).toBe(25);
    expect(at(merged, 159)).toBe(20);
  });

  it('does nothing when any argument is missing', () => {
    const merged = stats([
      [21, 4],
      [18, 150],
    ]);

    ItemStatOps.resolve(merged, stats([[21, 4]]), null);
    ItemStatOps.resolve(merged, null, table());
    ItemStatOps.resolve(null, stats([[21, 4]]), table());

    expect(at(merged, 21)).toBe(4);
  });

  it('wraps the product back into int32, as the cast in the engine does', () => {
    // (long)base * percent / 100 is computed wide and then narrowed; 0x40000000 at 1000%
    // overflows the int the result is stored in.
    const merged = stats([
      [21, 0],
      [18, 1000],
    ]);

    ItemStatOps.resolve(merged, stats([[21, 0x40000000]]), table());

    expect(at(merged, 21)).toBe((0x40000000 * 10) | 0);
  });
});
