import { describe, expect, it } from 'vitest';
import { ItemRecordFlags } from '../../../src/D2ItemToolkit.Ts/src/Stats/ItemRecord.js';
import {
  createUnit,
  type Unit,
  type UnitStat,
} from '../../../src/D2ItemToolkit.Ts/src/Stats/Unit.js';
import { ItemTable } from '../../../src/D2ItemToolkit.Ts/src/Tables/ItemTable.js';
import { MagicAffixTable } from '../../../src/D2ItemToolkit.Ts/src/Tables/MagicAffixTable.js';
import { D2DataFiles } from '../../../src/D2ItemToolkit.Ts/src/Tables/TxtDataProviders.js';
import { ItemTooltipSection } from '../../../src/D2ItemToolkit.Ts/src/Tooltip/ItemTooltip.js';
import {
  TooltipEngine,
  type Tooltip,
  type TooltipOptions,
} from '../../../src/D2ItemToolkit.Ts/src/Tooltip/TooltipEngine.js';

/**
 * separateSocketContributions. The game always merges a filler's mods into the item's own block, so
 * this mode has no original to be checked against — what it must guarantee is that nothing is lost
 * or double-counted, and that each block is attributed to the right filler.
 */
const Data = D2DataFiles.load();
const Engine = TooltipEngine.embedded;
const Items = new ItemTable(Data.weapons, Data.armor, Data.misc);

function filler(code: string): Unit {
  return createUnit({
    unitType: 4,
    quality: 2,
    classId: Items.classIdForCode(code),
    code,
    itemFlags: ItemRecordFlags.Identified,
  });
}

function rangedDamageAffix(): number {
  const affixes = new MagicAffixTable(Data);

  for (let id = 1; id <= affixes.rowCount; ++id) {
    const resolved = affixes.tryResolve(id);
    if (resolved === null) {
      continue;
    }

    for (let mod = 1; mod <= 3; ++mod) {
      const code = resolved.table.getString(resolved.row, 'mod' + String(mod) + 'code').trim();
      if (
        code === 'dmg%' &&
        resolved.table.getInt(resolved.row, 'mod' + String(mod) + 'min') !==
          resolved.table.getInt(resolved.row, 'mod' + String(mod) + 'max')
      ) {
        return id;
      }
    }
  }

  throw new Error('no ranged dmg% affix in shipped data');
}

/** A jewel with its OWN rolled affix, which is the case gems and runes are not. */
function jewel(): Unit {
  const stats: UnitStat[] = [
    { id: 17, value: 20 },
    { id: 18, value: 20 },
  ];

  return createUnit({
    unitType: 4,
    quality: 4,
    classId: Items.classIdForCode('jew'),
    code: 'jew',
    itemFlags: ItemRecordFlags.Identified,
    magicPrefix: [rangedDamageAffix(), 0, 0],
    statsLists: [{ stateNo: 0, flags: 64, stats }],
  });
}

function socketedSword(...fillers: Unit[]): Unit {
  const baseStats: UnitStat[] = [
    { id: 21, value: 5 },
    { id: 22, value: 15 },
    { id: 194, value: fillers.length },
  ];

  return createUnit({
    unitType: 4,
    quality: 2,
    classId: Items.classIdForCode('crs'),
    itemFlags: ItemRecordFlags.Identified | ItemRecordFlags.Socketed,
    itemLevel: 60,
    statsLists: [{ stateNo: 0, flags: 2147483648, stats: baseStats }],
    items: fillers,
  });
}

/**
 * Colour is switched off with -1 rather than left at its grey default: these tests assert on the
 * annotation's TEXT, and the default wraps every span in two colour markers.
 */
function separated(withRanges = false): TooltipOptions {
  return { separateSocketContributions: true, showRolledRanges: withRanges, rangeColor: -1 };
}

function section(tooltip: Tooltip, want: ItemTooltipSection): string[] {
  return tooltip.lines.filter(l => l.section === want).map(l => (l.text ?? '').replace(/\n+$/, ''));
}

/**
 * A rare item with its own fire resist, socketed with a jewel that also gives fire resist. The three
 * views have to disagree in exactly the right way, because each shows a different VALUE and the span
 * must match the value beside it.
 */
function fireResCase(): {
  host: Unit;
  itemLow: number;
  itemHigh: number;
  jewelLow: number;
  jewelHigh: number;
} {
  const affixes = new MagicAffixTable(Data);
  const found: number[][] = [];

  for (let id = 1; id <= affixes.rowCount; ++id) {
    const resolved = affixes.tryResolve(id);
    if (resolved === null) {
      continue;
    }

    for (let mod = 1; mod <= 3; ++mod) {
      if (
        resolved.table.getString(resolved.row, 'mod' + String(mod) + 'code').trim() !== 'res-fire'
      ) {
        continue;
      }

      const lo = resolved.table.getInt(resolved.row, 'mod' + String(mod) + 'min');
      const hi = resolved.table.getInt(resolved.row, 'mod' + String(mod) + 'max');
      if (lo !== hi) {
        found.push([id, lo, hi]);
      }
    }
  }

  expect(found.length).toBeGreaterThanOrEqual(3);

  const itemAffix = found[2] as number[];
  const jewelAffix = found[0] as number[];

  const itemLow = itemAffix[1] as number;
  const itemHigh = itemAffix[2] as number;
  const jewelLow = jewelAffix[1] as number;
  const jewelHigh = jewelAffix[2] as number;

  const jewelUnit = createUnit({
    unitType: 4,
    quality: 4,
    classId: Items.classIdForCode('jew'),
    code: 'jew',
    itemFlags: ItemRecordFlags.Identified,
    magicPrefix: [jewelAffix[0] as number, 0, 0],
    statsLists: [{ stateNo: 0, flags: 64, stats: [{ id: 39, value: jewelLow }] }],
  });

  const host = createUnit({
    unitType: 4,
    quality: 6,
    classId: Items.classIdForCode('xhn'),
    itemFlags: ItemRecordFlags.Identified | ItemRecordFlags.Socketed,
    itemLevel: 70,
    magicPrefix: [itemAffix[0] as number, 0, 0],
    statsLists: [
      { stateNo: 0, flags: 2147483648, stats: [{ id: 194, value: 1 }] },
      { stateNo: 0, flags: 64, stats: [{ id: 39, value: itemLow }] },
    ],
    items: [jewelUnit],
  });

  return { host, itemLow, itemHigh, jewelLow, jewelHigh };
}

describe('separateSocketContributions', () => {
  it('still merges them by default', () => {
    // The mode is opt-in, and off means the game's own behaviour: the fillers' mods appear in the
    // item's own block and the name gains "Gemmed".
    const host = socketedSword(filler('r08'), filler('gpr'));
    const merged = Engine.render(host);

    expect(section(merged, ItemTooltipSection.SocketContribution)).toEqual([]);
    expect(section(merged, ItemTooltipSection.Modifiers).some(l => l.includes('fire damage'))).toBe(
      true,
    );
  });

  it("moves the fillers out of the item's own block", () => {
    const host = socketedSword(filler('r08'), filler('gpr'));
    const result = Engine.render(host, null, separated());

    // Gone from the item's own modifiers...
    expect(section(result, ItemTooltipSection.Modifiers).some(l => l.includes('fire damage'))).toBe(
      false,
    );

    // ...and present below it, one block per filler, each headed by the filler's name.
    const blocks = section(result, ItemTooltipSection.SocketContribution);

    expect(blocks).toContain('Ral Rune');
    expect(blocks).toContain('Perfect Ruby');
    expect(blocks.filter(l => l.includes('fire damage')).length).toBe(2);
  });

  it('puts the blocks below the item', () => {
    // Lines are in display order, so every socket block must come after every line of the item's
    // own tooltip — otherwise they read as part of it.
    const result = Engine.render(socketedSword(filler('r08')), null, separated());

    let lastOwn = -1;
    let firstBlock = Number.MAX_SAFE_INTEGER;

    result.lines.forEach((line, at) => {
      if (line.section === ItemTooltipSection.SocketContribution) {
        firstBlock = Math.min(firstBlock, at);
      } else {
        lastOwn = at;
      }
    });

    expect(firstBlock).toBeGreaterThan(lastOwn);
  });

  it('loses nothing by separating them', () => {
    const host = socketedSword(filler('r08'), filler('gpr'));

    // Merged, the two fillers' fire damage adds up; separated, each shows its own half.
    const mergedFire = section(Engine.render(host), ItemTooltipSection.Modifiers).filter(l =>
      l.includes('fire damage'),
    );

    expect(mergedFire.length).toBe(1);
    expect(mergedFire[0]).toContain('20');

    const blocks = section(
      Engine.render(host, null, separated()),
      ItemTooltipSection.SocketContribution,
    );

    expect(blocks.some(l => l.includes('5-30 fire damage'))).toBe(true);
    expect(blocks.some(l => l.includes('15-20 fire damage'))).toBe(true);
  });

  it('ranges a jewel from its own affixes', () => {
    // The case the mode exists for. A gem or rune has no stats of its own and no gems.txt cell
    // rolls, so its block carries no span — but a jewel's affixes DO roll, and its block is where
    // that span belongs.
    const host = socketedSword(filler('gpr'), jewel());

    const blocks = section(
      Engine.render(host, null, separated(true)),
      ItemTooltipSection.SocketContribution,
    );

    expect(blocks.some(l => l.includes('Enhanced Damage [10-20]'))).toBe(true);

    // The gem's own line is present and deliberately unannotated.
    const gem = blocks.filter(l => l.includes('15-20 fire damage'));
    expect(gem.length).toBe(1);
    expect(gem[0]).not.toContain('[');
  });

  it('gives a gem block no span, because no gem cell rolls', () => {
    // Ral, Ort and Thul are the only gems.txt rows whose min differs from their max, and that pair
    // is funcs 15/16 — the two ENDS of a damage range, both fixed. So a rune block shows the damage
    // and no span, which is the correct answer rather than a missing one.
    const blocks = section(
      Engine.render(socketedSword(filler('r08')), null, separated(true)),
      ItemTooltipSection.SocketContribution,
    );

    expect(blocks.some(l => l.includes('5-30 fire damage'))).toBe(true);
    expect(blocks.some(l => l.includes('['))).toBe(false);
  });

  it('separates the blocks with a blank row', () => {
    // Three gems in a row read as one list without it.
    const blocks = section(
      Engine.render(socketedSword(filler('r08'), filler('gpr')), null, separated()),
      ItemTooltipSection.SocketContribution,
    );

    // The trailing terminator is stripped by `section`, so a gap row is the empty string.
    expect(blocks.filter(l => l.length === 0).length).toBe(2);
    expect(blocks[0]).toBe('');
  });

  it('gives a merged line the SUM of both spans', () => {
    // The merged render draws ONE Fire Resist line holding item plus jewel, so its span has to be
    // the sum of the two. Annotating it with the item's span alone read as
    // "Fire Resist +28% [11-20]" — a number outside its own range.
    const c = fireResCase();

    const line = Engine.render(c.host, null, { showRolledRanges: true })
      .lines.map(l => l.text ?? '')
      .filter(l => l.includes('Fire Resist'));

    expect(line.length).toBe(1);
    expect(line[0]).toContain(
      '[' + String(c.itemLow + c.jewelLow) + '-' + String(c.itemHigh + c.jewelHigh) + ']',
    );
  });

  it('gives a separated line only its own span', () => {
    // The mirror image: with the fillers moved out, the item's line shows its own value, so its
    // span must be its own too — and the jewel's block gets the jewel's.
    const c = fireResCase();
    const tooltip = Engine.render(c.host, null, separated(true));

    const own = section(tooltip, ItemTooltipSection.Modifiers).filter(l =>
      l.includes('Fire Resist'),
    );
    const filled = section(tooltip, ItemTooltipSection.SocketContribution).filter(l =>
      l.includes('Fire Resist'),
    );

    expect(own[0]).toContain('[' + String(c.itemLow) + '-' + String(c.itemHigh) + ']');
    expect(filled[0]).toContain('[' + String(c.jewelLow) + '-' + String(c.jewelHigh) + ']');
  });

  it('splits the spans the same way in a breakdown', () => {
    // Breakdown had no ranges at all until now, and once wired the socket bucket showed the jewel's
    // VALUE against the item's SPAN — because reconstructing "just the fillers" silently folded in
    // the host's own sources.
    const c = fireResCase();
    const b = Engine.breakdown(c.host, null, { showRolledRanges: true });

    const magic = b.magic.map(l => l.text ?? '').filter(l => l.includes('Fire Resist'));
    const sockets = b.sockets.map(l => l.text ?? '').filter(l => l.includes('Fire Resist'));

    expect(magic[0]).toContain('[' + String(c.itemLow) + '-' + String(c.itemHigh) + ']');
    expect(sockets[0]).toContain('[' + String(c.jewelLow) + '-' + String(c.jewelHigh) + ']');
  });

  it('produces no block for an empty socket', () => {
    expect(
      section(
        Engine.render(socketedSword(), null, separated()),
        ItemTooltipSection.SocketContribution,
      ),
    ).toEqual([]);
  });
});
