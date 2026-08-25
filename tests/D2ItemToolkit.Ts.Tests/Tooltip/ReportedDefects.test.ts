import { describe, expect, it } from 'vitest';
import { ItemRecordFlags } from '../../../src/D2ItemToolkit.Ts/src/Stats/ItemRecord.js';
import { createUnit, type Unit } from '../../../src/D2ItemToolkit.Ts/src/Stats/Unit.js';
import { ItemTable } from '../../../src/D2ItemToolkit.Ts/src/Tables/ItemTable.js';
import { MagicAffixTable } from '../../../src/D2ItemToolkit.Ts/src/Tables/MagicAffixTable.js';
import { D2DataFiles } from '../../../src/D2ItemToolkit.Ts/src/Tables/TxtDataProviders.js';
import { ItemTooltipSection } from '../../../src/D2ItemToolkit.Ts/src/Tooltip/ItemTooltip.js';
import { TooltipEngine } from '../../../src/D2ItemToolkit.Ts/src/Tooltip/TooltipEngine.js';

/**
 * The peer of the C# ReportedDefectTests — three defects reported from a live consumer, each
 * asserting the behaviour the caller expected.
 */
const Data = D2DataFiles.load();
const Engine = TooltipEngine.embedded;
const Items = new ItemTable(Data.weapons, Data.armor, Data.misc);

/** Griswold's Valor — a Death Mask, three sockets. Post-splice setitems row. */
const SocketableSetHelm = 80;

const StatDefense = 31;
const StatArmorPercent = 16;
const StatNumSockets = 194;

const ListFlagsMagic = 0x40;
const ListFlagsExtended = 0x80000000;

function socketedSetHelm(): Unit {
  const rune = createUnit({
    unitType: 4,
    classId: Items.classIdForCode('r22'),
    itemFlags: ItemRecordFlags.Identified,
  });

  return createUnit({
    unitType: 4,
    classId: Items.classIdForCode('xsk'),
    quality: 5,
    fileIndex: SocketableSetHelm,
    itemFlags: ItemRecordFlags.Identified | ItemRecordFlags.Socketed,
    location: 3,
    x: 1,
    statsLists: [
      {
        stateNo: 0,
        flags: ListFlagsExtended,
        stats: [
          { id: StatDefense, value: 100 },
          { id: StatNumSockets, value: 1 },
        ],
      },
    ],
    items: [rune],
  });
}

/** 1-based id of the first magic affix whose mod grants `code` over a non-degenerate range. */
function firstAffixGranting(code: string): number {
  const affixes = new MagicAffixTable(Data);

  for (let id = 1; id <= affixes.rowCount; ++id) {
    const resolved = affixes.tryResolve(id);
    if (resolved === null) {
      continue;
    }

    for (let mod = 1; mod <= 3; ++mod) {
      if (
        resolved.table.getString(resolved.row, 'mod' + String(mod) + 'code').trim() === code &&
        resolved.table.getInt(resolved.row, 'mod' + String(mod) + 'min') !==
          resolved.table.getInt(resolved.row, 'mod' + String(mod) + 'max')
      ) {
        return id;
      }
    }
  }

  return -1;
}

/** The midpoint of the roll `affix` gives `code`. */
function midRollOf(affix: number, code: string): number {
  const affixes = new MagicAffixTable(Data);

  const resolved = affixes.tryResolve(affix);
  if (resolved === null) {
    throw new Error('affix ' + String(affix) + ' does not resolve');
  }

  for (let mod = 1; mod <= 3; ++mod) {
    if (resolved.table.getString(resolved.row, 'mod' + String(mod) + 'code').trim() !== code) {
      continue;
    }

    return Math.trunc(
      (resolved.table.getInt(resolved.row, 'mod' + String(mod) + 'min') +
        resolved.table.getInt(resolved.row, 'mod' + String(mod) + 'max')) /
        2,
    );
  }

  throw new Error('affix ' + String(affix) + ' does not grant ' + code);
}

/** The line without its embedded colour markers or trailing terminator. */
function plain(text: string | null): string {
  return (text ?? '').replace(/ÿc./g, '').replace(/\n+$/, '');
}

function readSpan(text: string): { low: number; high: number } | null {
  const match = /\[(-?\d+)-(-?\d+)]/.exec(text);
  return match === null ? null : { low: Number(match[1]), high: Number(match[2]) };
}

describe('reported defects', () => {
  it('a socketed set item honours separateSocketContributions', () => {
    // REPORTED: "socketed talrasha helm with um rune, when I press ctrl, it just removes some line
    // breaks, but does not break the um rune apart, as a separate item."
    const tip = Engine.render(socketedSetHelm(), null, { sockets: 'separated' });

    expect(tip.lines.some(l => l.section === ItemTooltipSection.SocketContribution)).toBe(true);
  });

  it('a set item honours showRolledRanges', () => {
    // The same defect's other half: render returned through the set-item builder before the
    // annotation was installed, so ctrl did nothing on any of the 127 set pieces.
    const tip = Engine.render(socketedSetHelm(), null, {
      ranges: { color: -1 },
    });

    expect(tip.lines.some(l => (l.text ?? '').includes('['))).toBe(true);
  });

  it('the defense span brackets the defense the line shows', () => {
    // REPORTED: "items that have enhanced defence, control show the range of the base defence, but
    // the actual number is enhanced, so it's not clear where it rolled in the base range."
    //
    // The prefix is a real one, because the span is rebuilt from the affix the record names. A
    // hand-authored `ac%` on the stat list alone gives the reconstruction nothing to roll.
    const defAffix = firstAffixGranting('ac%');
    expect(defAffix).toBeGreaterThan(0);

    const largeShield = Items.classIdForCode('lrg');

    // maxac + 1, because the `ac%` affix maximises the base — see DefenseOutOfRange.test.ts. A
    // hand-authored roll inside minac..maxac is a record the game cannot produce, and the span then
    // correctly refuses to contain it.
    const baseDefense = Items.getInt(largeShield, 'maxac') + 1;

    const shield = createUnit({
      unitType: 4,
      classId: largeShield,
      quality: 4,
      itemFlags: ItemRecordFlags.Identified,
      magicPrefix: [defAffix, 0, 0],
      statsLists: [
        { stateNo: 0, flags: ListFlagsExtended, stats: [{ id: StatDefense, value: baseDefense }] },
        {
          stateNo: 0,
          flags: ListFlagsMagic,
          stats: [{ id: StatArmorPercent, value: midRollOf(defAffix, 'ac%') }],
        },
      ],
    });

    const tip = Engine.render(shield, null, { ranges: { color: -1 } });

    // Markers stripped first: the Defense line carries an embedded marker, and reading digits
    // straight off it yields "332" for a value of 32.
    const defense = tip.lines.map(l => plain(l.text)).find(t => t.startsWith('Defense:'));
    expect(defense).toBeDefined();

    const shown = Number(/Defense:\s*(\d+)/.exec(defense as string)?.[1]);
    const span = readSpan(defense as string);

    expect(span).not.toBeNull();
    expect(shown).toBeGreaterThanOrEqual((span as { low: number }).low);
    expect(shown).toBeLessThanOrEqual((span as { high: number }).high);
  });

  it("a shifted stat's span is written in the units the line prints", () => {
    // REPORTED: "+11 to Life [2816-3840]" — 2816 is 11 << 8. Life, mana and stamina carry ValShift
    // 8, so the stat is stored 8.8 fixed point and the writer shifts it down before printing.
    const hpAffix = firstAffixGranting('hp');
    expect(hpAffix).toBeGreaterThan(0);

    const charm = createUnit({
      unitType: 4,
      classId: Items.classIdForCode('cm1'),
      quality: 4,
      itemFlags: ItemRecordFlags.Identified,
      magicPrefix: [hpAffix, 0, 0],
      statsLists: [{ stateNo: 0, flags: ListFlagsMagic, stats: [{ id: 7, value: 11 << 8 }] }],
    });

    const tip = Engine.render(charm, null, { ranges: { color: -1 } });

    const life = tip.lines.map(l => plain(l.text)).find(t => t.includes('Life'));
    expect(life).toBeDefined();

    const span = readSpan(life as string);
    expect(span).not.toBeNull();

    // The line prints 11. A span in the same units brackets it; one in raw storage units is 256x
    // too large.
    expect((span as { low: number }).low).toBeGreaterThan(11 - 100);
    expect((span as { high: number }).high).toBeLessThan(11 + 100);
  });
});

describe('line stat identity', () => {
  it('an aggregated line names every stat it shows', () => {
    // REQUESTED by a consumer: tooltip.lines gave no way to say which stat a line is about for a
    // line that speaks for more than one. ItemDescriptionLine already carried shownStats and
    // aggregated; they were dropped on the way up to ItemTooltipLine.
    //
    // firemindam(48) and firemaxdam(49) are written as ONE line, "Adds 1-4 fire damage".
    const FireMin = 48;
    const FireMax = 49;

    const sword = createUnit({
      unitType: 4,
      classId: Items.classIdForCode('crs'),
      quality: 4,
      itemFlags: ItemRecordFlags.Identified,
      statsLists: [
        {
          stateNo: 0,
          flags: ListFlagsMagic,
          stats: [
            { id: FireMin, value: 1 },
            { id: FireMax, value: 4 },
          ],
        },
      ],
    });

    const lines = Engine.render(sword).lines;
    const fire = lines.filter(l => l.statId === FireMin);

    expect(fire).toHaveLength(1);
    expect(plain(fire[0]?.text ?? null)).toBe('Adds 1-4 fire damage');
    expect(fire[0]?.aggregated).toBe(true);
    expect(fire[0]?.shownStats).toEqual([FireMin, FireMax]);

    // A single-stat line reports itself as one: null shownStats means "just statId", which is what
    // lets a caller treat the two uniformly without a special case.
    const single = lines.find(
      l => l.section === ItemTooltipSection.Modifiers && l.statId >= 0 && !l.aggregated,
    );

    expect(single?.shownStats).toBeNull();
  });
});
