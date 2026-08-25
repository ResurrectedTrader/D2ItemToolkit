import { describe, expect, it } from 'vitest';
import { ItemRecordFlags } from '../../../src/D2ItemToolkit.Ts/src/Stats/ItemRecord.js';
import { createUnit, type Unit } from '../../../src/D2ItemToolkit.Ts/src/Stats/Unit.js';
import { ItemTable } from '../../../src/D2ItemToolkit.Ts/src/Tables/ItemTable.js';
import { D2DataFiles } from '../../../src/D2ItemToolkit.Ts/src/Tables/TxtDataProviders.js';
import {
  ItemTooltipSection,
  type ItemTooltipLine,
} from '../../../src/D2ItemToolkit.Ts/src/Tooltip/ItemTooltip.js';
import {
  TooltipEngine,
  type Tooltip,
  type TooltipOptions,
} from '../../../src/D2ItemToolkit.Ts/src/Tooltip/TooltipEngine.js';

/**
 * The peer of the C# WornSetItemSocketTests. The separated-socket mode MOVES a filler's stats out
 * of the item's block and must never ADD stats the merged render would not have shown; a worn set
 * item, whose fillers the recalc discards (0x4c1350), is where the two came apart.
 */
const Data = D2DataFiles.load();
const Engine = TooltipEngine.embedded;
const Items = new ItemTable(Data.weapons, Data.armor, Data.misc);

/** setitems.txt post-splice, 0-based. `xsk`, a Death Mask. */
const TalRashasHoradricCrest = 80;

const LocationEquipped = 1;
const LocationStash = 3;

const StatDefense = 31;
const StatNumSockets = 194;
const ListFlagsExtended = 0x80000000;

function crestWithUm(location: number): Unit {
  const um = createUnit({
    unitType: 4,
    classId: Items.classIdForCode('r22'),
    itemFlags: ItemRecordFlags.Identified,
  });

  return createUnit({
    unitType: 4,
    classId: Items.classIdForCode('xsk'),
    quality: 5,
    fileIndex: TalRashasHoradricCrest,
    itemFlags: ItemRecordFlags.Identified | ItemRecordFlags.Socketed,
    location,
    x: 1,
    statsLists: [
      {
        stateNo: 0,
        flags: ListFlagsExtended,
        stats: [
          { id: StatDefense, value: 121 },
          { id: StatNumSockets, value: 1 },
        ],
      },
    ],
    items: [um],
  });
}

const separated: TooltipOptions = { sockets: 'separated' };

function sectioned(tip: Tooltip, section: ItemTooltipSection): string[] {
  return tip.lines
    .filter((l: ItemTooltipLine) => l.section === section)
    .map((l: ItemTooltipLine) => (l.text ?? '').replace(/ÿc./g, '').replace(/\n+$/, ''))
    .filter((t: string) => t.length !== 0);
}

describe('a worn set item and its sockets', () => {
  it('a carried set item moves its filler into a block', () => {
    const carried = crestWithUm(LocationStash);

    expect(sectioned(Engine.render(carried), ItemTooltipSection.Modifiers)).toContain(
      'All Resistances +15',
    );

    const split = Engine.render(carried, null, separated);

    expect(sectioned(split, ItemTooltipSection.Modifiers)).not.toContain('All Resistances +15');
    expect(sectioned(split, ItemTooltipSection.SocketContribution)).toEqual([
      'Um Rune',
      'All Resistances +15',
    ]);
  });

  it('a worn set item has no filler to move and grows no block', () => {
    const worn = crestWithUm(LocationEquipped);

    expect(sectioned(Engine.render(worn), ItemTooltipSection.Modifiers)).not.toContain(
      'All Resistances +15',
    );

    expect(
      sectioned(Engine.render(worn, null, separated), ItemTooltipSection.SocketContribution),
    ).toEqual([]);
  });

  it('the resistance number says whether the rune counted', () => {
    // The Crest's OWN `res-all 15` and an Um's `res-all 15` are the same four stats, so if the rune
    // applied they would MERGE rather than appear twice. That makes the single number the decisive
    // evidence: +15 means the filler does not count, +30 means it does.
    const resists = [39, 41, 43, 45];

    const withOwnResists = (location: number): Unit => {
      const crest = crestWithUm(location);

      crest.statsLists.push({
        stateNo: 0,
        flags: 0x40,
        stats: resists.map(id => ({ id, value: 15 })),
      });

      return crest;
    };

    expect(
      sectioned(Engine.render(withOwnResists(LocationEquipped)), ItemTooltipSection.Modifiers),
    ).toContain('All Resistances +15');

    expect(
      sectioned(Engine.render(withOwnResists(LocationStash)), ItemTooltipSection.Modifiers),
    ).toContain('All Resistances +30');
  });

  it('the defense modifier line does not borrow the section span', () => {
    // REPORTED, from a screenshot: `Defense: 121 [99-131]` beside `+45 Defense [99-131]`. A Death
    // Mask rolls 54..86 and the Crest adds a FIXED 45, so 99..131 belongs to the SECTION alone; the
    // modifier draws the set property, which could never have rolled.
    const crest = crestWithUm(LocationEquipped);

    // The base array holds the ROLL, 76 of 54..86; the set's fixed 45 is a modifier on top.
    crest.statsLists = [
      {
        stateNo: 0,
        flags: ListFlagsExtended,
        stats: [
          { id: StatDefense, value: 76 },
          { id: StatNumSockets, value: 1 },
        ],
      },
      { stateNo: 0, flags: 0x40, stats: [{ id: StatDefense, value: 45 }] },
    ];

    const tip = Engine.render(crest, null, { ranges: { color: -1 } });

    expect(sectioned(tip, ItemTooltipSection.ArmorClass)).toEqual(['Defense: 121 [99-131]']);
    expect(sectioned(tip, ItemTooltipSection.Modifiers).filter(t => t.includes('Defense'))).toEqual(
      ['+45 Defense'],
    );
  });

  it('separating never adds a line the merged render lacks', () => {
    for (const location of [LocationEquipped, LocationStash]) {
      const item = crestWithUm(location);

      const merged = sectioned(Engine.render(item), ItemTooltipSection.Modifiers);

      const split = Engine.render(item, null, separated);
      const own = sectioned(split, ItemTooltipSection.Modifiers);
      const fillers = sectioned(split, ItemTooltipSection.SocketContribution);

      for (const line of [...own, ...fillers]) {
        // The filler block's heading is the rune's NAME, which is not a stat line and is the one
        // thing the merged render has no counterpart for.
        if (line === 'Um Rune') {
          continue;
        }

        expect(merged).toContain(line);
      }
    }
  });
});
