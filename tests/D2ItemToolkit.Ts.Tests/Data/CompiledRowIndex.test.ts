import { describe, expect, it } from 'vitest';
import { ItemTable } from '../../../src/D2ItemToolkit.Ts/src/Tables/ItemTable.js';
import { ItemTypeTree } from '../../../src/D2ItemToolkit.Ts/src/Tables/ItemTypeTree.js';
import { D2DataFiles } from '../../../src/D2ItemToolkit.Ts/src/Tables/TxtDataProviders.js';
import type { TxtFile } from '../../../src/D2ItemToolkit.Ts/src/Data/TxtFile.js';

// Ported from CompiledRowIndexTests.cs.
//
// Row index IS the record id: the C++ producer emits the game's classId, so a single extra or
// missing row silently renames every item after it. The expected counts are the record counts
// in the shipped .bin files the game actually loads (DATATBLS_LoadFromBin), which are one less
// than the .txt data row count because 0x6bd742 splices out the "Expansion" divider.

const Data = D2DataFiles.load();

const Items = new ItemTable(Data.weapons, Data.armor, Data.misc);

const Types = new ItemTypeTree(Data.itemTypes);

function table(name: string): TxtFile | null {
  switch (name) {
    case 'itemtypes':
      return Data.itemTypes;
    case 'weapons':
      return Data.weapons;
    case 'armor':
      return Data.armor;
    case 'misc':
      return Data.misc;
    case 'uniqueitems':
      return Data.uniqueItems;
    case 'setitems':
      return Data.setItems;
    default:
      return null;
  }
}

describe('compiled row indices', () => {
  it.each([
    ['itemtypes', 103],
    ['weapons', 306],
    ['armor', 202],
    ['misc', 151],
    ['uniqueitems', 402],
    ['setitems', 127],
  ] as const)('%s row count matches the compiled bin', (name, expected) => {
    expect(table(name)?.rowCount).toBe(expected);
  });

  it.each([
    [13, 'char'],
    [20, 'gem'],
    [45, 'weap'],
    [53, 'sock'],
    [58, 'jewl'],
    [74, 'rune'],
  ] as const)('ItemTypes row %i lands where the binary indexes it', (row, code) => {
    // These are the literal constants pushed at IsOfType call sites: 13 at 0x48e5c6,
    // 20/53/74 at 0x4e68bd / 0x4865e2 / 0x4e6a6c. 58 is the first row past the divider.
    expect(Data.itemTypes?.getString(row, 'Code').trim()).toBe(code);
    expect(Types.row(code)).toBe(row);
  });

  it.each([
    [174, 'qf2'],
    [175, 'ktr'],
    [176, 'wrb'],
  ] as const)('weapon class id %i skips the divider', (classId, code) => {
    // weapons.txt puts "Expansion" at data row 175, so Katar compiles to 175, not 176.
    expect(Items.code(classId)).toBe(code);
  });
});
