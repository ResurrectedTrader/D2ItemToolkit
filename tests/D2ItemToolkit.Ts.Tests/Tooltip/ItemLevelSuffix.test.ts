import { describe, expect, it } from 'vitest';
import { ItemRecordFlags } from '../../../src/D2ItemToolkit.Ts/src/Stats/ItemRecord.js';
import { createUnit, type Unit } from '../../../src/D2ItemToolkit.Ts/src/Stats/Unit.js';
import { ItemTable } from '../../../src/D2ItemToolkit.Ts/src/Tables/ItemTable.js';
import { D2DataFiles } from '../../../src/D2ItemToolkit.Ts/src/Tables/TxtDataProviders.js';
import {
  ItemTooltipKind,
  ItemTooltipSection,
} from '../../../src/D2ItemToolkit.Ts/src/Tooltip/ItemTooltip.js';
import {
  TooltipEngine,
  type Tooltip,
  type TooltipOptions,
} from '../../../src/D2ItemToolkit.Ts/src/Tooltip/TooltipEngine.js';

/**
 * The peer of the C# ItemLevelSuffixTests. `showItemLevel` appends ` [ilvl N]` after the item's
 * name; the game draws no such line, so this is one of the options that departs from it.
 */
const Data = D2DataFiles.load();
const Engine = TooltipEngine.embedded;
const Items = new ItemTable(Data.weapons, Data.armor, Data.misc);

/** UniqueItems.txt post-splice, 0-based. `xea`, a Serpentskin Armor. */
const SkinOfTheVipermagi = 210;

const ListFlagsMagic = 0x40;
const ListFlagsExtended = 0x80000000;

const showing: TooltipOptions = { showItemLevel: true };

function vipermagi(itemLevel: number): Unit {
  return createUnit({
    unitType: 4,
    classId: Items.classIdForCode('xea'),
    quality: 7,
    fileIndex: SkinOfTheVipermagi,
    itemFlags: ItemRecordFlags.Identified,
    itemLevel,
    statsLists: [
      { stateNo: 0, flags: ListFlagsExtended, stats: [{ id: 31, value: 127 }] },
      { stateNo: 0, flags: ListFlagsMagic, stats: [{ id: 16, value: 120 }] },
    ],
  });
}

function names(tip: Tooltip): string[] {
  return tip.lines
    .filter(l => l.section === ItemTooltipSection.ItemName)
    .map(l => (l.text ?? '').replace(/ÿc./g, '').replace(/\n+$/, ''));
}

describe('the item-level suffix', () => {
  it('follows the name and not the base name', () => {
    // A unique's name section is two lines. The suffix belongs on the item's own name.
    expect(names(Engine.render(vipermagi(67), null, showing))).toEqual([
      'Skin of the Vipermagi [ilvl 67]',
      'Serpentskin Armor',
    ]);
  });

  it('appends nothing when the record carries no level', () => {
    // -1 is the documented absent sentinel, and the option must not invent an "ilvl -1".
    expect(names(Engine.render(vipermagi(-1), null, showing))).toEqual([
      'Skin of the Vipermagi',
      'Serpentskin Armor',
    ]);
  });

  it('shows level zero, because only -1 is the sentinel', () => {
    // The game floors item level at 1, so a 0 is a producer that defaulted rather than a real
    // level — but -1 is the documented sentinel, so 0 is treated as a real level.
    expect(names(Engine.render(vipermagi(0), null, showing))[0]).toContain('[ilvl 0]');
  });

  it('is inert when off', () => {
    expect(Engine.render(vipermagi(67), null, {}).text).toBe(Engine.render(vipermagi(67)).text);
    expect(Engine.render(vipermagi(67)).text).not.toContain('ilvl');
  });

  it('is grey and restores the line colour', () => {
    // Same grey the range annotation uses, and a marker restoring the name's own colour follows it
    // so nothing after is repainted.
    expect(Engine.render(vipermagi(67), null, showing).coloredText).toContain(
      'Skin of the Vipermagiÿc5 [ilvl 67]ÿc4',
    );
  });

  it('does not double-space a padded name', () => {
    // The game pads a magic or rare name with a trailing space, which is the common case.
    const shield = createUnit({
      unitType: 4,
      classId: Items.classIdForCode('lrg'),
      quality: 4,
      itemFlags: ItemRecordFlags.Identified,
      itemLevel: 42,
      statsLists: [{ stateNo: 0, flags: ListFlagsExtended, stats: [{ id: 31, value: 15 }] }],
    });

    const name = names(Engine.render(shield, null, showing))[0] as string;

    expect(name).not.toContain('  [ilvl');
    expect(name).toContain(' [ilvl 42]');
  });

  it('reaches a set item too', () => {
    // The set-item builder is a separate compose path; both have to carry the suffix or the option
    // is silently half-implemented.
    const crest = createUnit({
      unitType: 4,
      classId: Items.classIdForCode('xsk'),
      quality: 5,
      fileIndex: 80,
      itemFlags: ItemRecordFlags.Identified,
      itemLevel: 84,
      statsLists: [{ stateNo: 0, flags: ListFlagsExtended, stats: [{ id: 31, value: 76 }] }],
    });

    const tip = Engine.render(crest, null, showing);

    expect(tip.kind).toBe(ItemTooltipKind.IdentifiedSetItem);
    expect(names(tip)).toContain("Tal Rasha's Horadric Crest [ilvl 84]");
  });
});
