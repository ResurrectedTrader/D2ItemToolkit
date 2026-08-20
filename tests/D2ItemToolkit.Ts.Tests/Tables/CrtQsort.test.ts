import { describe, expect, it } from 'vitest';
import { ItemDescriptionGenerator } from '../../../src/D2ItemToolkit.Ts/src/Description/ItemDescription.js';
import { ItemStatReader } from '../../../src/D2ItemToolkit.Ts/src/Stats/ItemStatReader.js';
import { crtQsort } from '../../../src/D2ItemToolkit.Ts/src/Tables/CrtQsort.js';
import { D2DataFiles } from '../../../src/D2ItemToolkit.Ts/src/Tables/TxtDataProviders.js';

/**
 * The order of two stats that share a descpriority — the peer of DescPriorityOrderTests.cs.
 *
 * SORT_ItemDescPriority 0x6379d0 compares the priority word alone and returns 0 for a tie, so the
 * game's order within a tie group is decided entirely by the CRT qsort permutation at 0x638571.
 * This was a known divergence until a capture of Call to Arms discriminated it: the game prints
 * the three oskills ABOVE Prevent Monster Heal, all four at priority 81.
 */
describe('descpriority tie order', () => {
  const data = D2DataFiles.load();

  // Battle Cry, Battle Orders and Battle Command — skills.txt rows 146, 149 and 155.
  const BattleCry = 146;
  const BattleOrders = 149;
  const BattleCommand = 155;

  const PreventMonsterHeal = 117; // item_preventheal
  const NonClassSkill = 97; // item_nonclassskill, the oskill stat

  // Every tie group, in the order 0x638571 leaves them. Eight of the twelve differ from a plain
  // ascending-stat-id tie-break, so this is the pin that stops one creeping back in: a stable
  // sort would leave every group in ascending id order.
  const expected = new Map<number, number[]>([
    [1, [252, 204]],
    [8, [87, 80]], // Gheed's Fortune
    [11, [114, 85]],
    [16, [86, 138]],
    [22, [34, 36]],
    [33, [233, 147]],
    [81, [117, 107, 108, 97]], // Call to Arms
    [88, [306, 305, 335, 308, 329, 330, 60, 336, 307, 331, 332, 333, 334]],
    [106, [124, 180]],
    [108, [122, 179]],
    [160, [195, 197, 198, 199, 201, 152, 196]],
    [
      180,
      [
        280, 281, 282, 283, 284, 285, 286, 272, 288, 289, 290, 279, 293, 294, 295, 296, 297, 298,
        299, 300, 301, 302, 303, 278, 271, 277, 276, 275, 274, 292, 270, 273, 269, 268, 287,
      ],
    ],
  ]);

  function grouped(): Map<number, number[]> {
    const byPriority = new Map<number, number[]>();

    for (const statId of data.itemStatCost.statIdsByDescPriority) {
      const descriptor = data.itemStatCost.tryGetStat(statId);
      expect(descriptor).not.toBeNull();

      const priority = descriptor!.descPriority;
      const bucket = byPriority.get(priority);
      if (bucket === undefined) {
        byPriority.set(priority, [statId]);
      } else {
        bucket.push(statId);
      }
    }

    return byPriority;
  }

  it('leaves the described stats in the CRT qsort permutation', () => {
    const order = data.itemStatCost.statIdsByDescPriority;
    const byPriority = grouped();
    const ties = [...byPriority.values()].filter(g => g.length > 1);

    // 12 groups covering 75 of the 207 described stats — counted against the shipped file, so a
    // data change that adds or drops a tie fails here rather than silently.
    expect(order.length).toBe(207);
    expect(ties.length).toBe(12);
    expect(ties.reduce((sum, g) => sum + g.length, 0)).toBe(75);

    for (const [priority, ids] of expected) {
      expect(byPriority.get(priority)).toEqual(ids);
    }
  });

  it('still orders the priorities ascending', () => {
    // The permutation reorders ties only; the fold in SKILLDESC_BuildStatBuffDesc walks forward
    // and depends on the array being ordered.
    let previous = Number.MIN_SAFE_INTEGER;

    for (const statId of data.itemStatCost.statIdsByDescPriority) {
      const descriptor = data.itemStatCost.tryGetStat(statId);
      expect(descriptor).not.toBeNull();
      expect(descriptor!.descPriority).toBeGreaterThanOrEqual(previous);
      previous = descriptor!.descPriority;
    }
  });

  it('prints Call to Arms oskills above Prevent Monster Heal', () => {
    // The captured game tooltip, which is what re-opened this. Lines come back in APPEND order
    // and the renderer draws them bottom-up, so Prevent Monster Heal appearing FIRST here is what
    // puts it BELOW the three oskills on screen.
    const lines = new ItemDescriptionGenerator(
      data.itemStatCost,
      data.strings,
      null,
      data.skills,
      data.classes,
    ).describe([
      [ItemStatReader.packStatKey(BattleOrders, NonClassSkill), 6],
      [ItemStatReader.packStatKey(0, PreventMonsterHeal), 1],
      [ItemStatReader.packStatKey(BattleCry, NonClassSkill), 1],
      [ItemStatReader.packStatKey(BattleCommand, NonClassSkill), 4],
    ]);

    expect(lines.map(l => l.text)).toEqual([
      'Prevent Monster Heal',
      '+1 to Battle Cry',
      '+6 to Battle Orders',
      '+4 to Battle Command',
    ]);
  });

  it('rotates equal elements in the short sort', () => {
    // _shortsort 0x685ac0 is a selection sort. With every element equal the maximum stays at lo,
    // so each pass swaps lo with the shrinking hi and the run comes out rotated left by one — not
    // reversed, and not stable. Eight or fewer elements never reach the partition path at all
    // (0x685bfe), which is why every two-element tie group moved.
    const items = [0, 1, 2, 3, 4];
    crtQsort(items, () => 0);

    expect(items).toEqual([1, 2, 3, 4, 0]);
  });

  it('leaves a single element alone', () => {
    // 0x685bd1: cmp esi, 2 / jb — fewer than two elements returns before touching memory.
    const items = [7];
    crtQsort(items, () => 0);

    expect(items).toEqual([7]);
  });
});
