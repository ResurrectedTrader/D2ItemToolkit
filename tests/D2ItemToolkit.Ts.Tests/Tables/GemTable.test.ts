import { describe, expect, it } from 'vitest';
import { GemTable } from '../../../src/D2ItemToolkit.Ts/src/Tables/GemTable.js';
import { ItemTable } from '../../../src/D2ItemToolkit.Ts/src/Tables/ItemTable.js';
import { PropertiesTable } from '../../../src/D2ItemToolkit.Ts/src/Tables/PropertiesTable.js';
import { D2DataFiles } from '../../../src/D2ItemToolkit.Ts/src/Tables/TxtDataProviders.js';

// The gem-table half of SocketFillerTests.cs.

const Data = D2DataFiles.load();
const Items = new ItemTable(Data.weapons, Data.armor, Data.misc);

describe('GemTable', () => {
  // =================================================================
  // gems row 0 is a real row. TXT_Gems_GetLine 0x6372c0 rejects only `>= recordCount`
  // (0x6372cc) and exactly -1 (0x6372d1); the `jle` that also drops 0 is at 0x4866e9 and
  // belongs to INV_FormatRunewordName, behind an IsOfType(rune) test at 0x4866d6.
  // =================================================================

  it('the first gems row is a real gem', () => {
    // TXT_AllocTxt_gems 0x637279 writes the row index into items +0xF0 and writes a
    // literal 0 on its first iteration, so gcv's offset genuinely is 0.
    expect(Data.gems?.getString(0, 'code').trim()).toBe('gcv');
  });

  it('a rune letter still ignores row zero', () => {
    // RowForRuneClassId keeps the 0x4866e9 `jle`. No rune occupies row 0 (it is gcv), so
    // this is faithful and unobservable, but the two lookups must stay distinct.
    const gems = new GemTable(Data.gems, Items);

    expect(gems.rowForFillerClassId(Items.classIdForCode('gcv'))).toBe(0);
    expect(gems.rowForRuneClassId(Items.classIdForCode('gcv'))).toBe(-1);
  });

  it('a non-filler resolves to no gems row at all', () => {
    const gems = new GemTable(Data.gems, Items);

    expect(gems.rowCount).toBe(68);
    expect(gems.rowForFillerClassId(Items.classIdForCode('lrg'))).toBe(-1);
    expect(gems.rowForFillerClassId(-1)).toBe(-1);
    expect(gems.rowForRuneClassId(Items.classIdForCode('lrg'))).toBe(-1);
  });

  it('reads the rune letter off the record and leaves gems letterless', () => {
    const gems = new GemTable(Data.gems, Items);

    const ral = gems.rowForRuneClassId(Items.classIdForCode('r08'));
    expect(ral).toBe(42);
    expect(gems.letter(ral)).toBe('Ral');

    expect(gems.letter(0)).toBeNull();
    expect(gems.letter(-1)).toBeNull();
    expect(gems.letter(68)).toBeNull();
  });

  it('reports the gems.txt code for a row', () => {
    const gems = new GemTable(Data.gems, Items);

    expect(gems.code(0)).toBe('gcv');
    expect(gems.code(-1)).toBeNull();
    expect(gems.code(68)).toBeNull();
  });

  it('reads the three quadruples of each destination slot', () => {
    // pProperties[3][3] at gems row +0x30: slot 0 is the weapon mods, 1 the helm mods and 2 the
    // shield mods. Perfect Ruby is weapon 15-20 fire damage, helm/armor +38 life, shield +40%
    // fire resist.
    const gems = new GemTable(Data.gems, Items);
    const row = gems.rowForFillerClassId(Items.classIdForCode('gpr'));
    expect(row).toBe(19);

    const quadruples = (slot: number) =>
      [...gems.properties(row, slot)].map(p => [p.param, p.min, p.max]);

    expect(quadruples(0)).toEqual([
      [0, 15, 15],
      [0, 20, 20],
      [0, 0, 0],
    ]);
    expect(quadruples(1)).toEqual([
      [0, 38, 38],
      [0, 0, 0],
      [0, 0, 0],
    ]);
    expect(quadruples(2)).toEqual([
      [0, 40, 40],
      [0, 0, 0],
      [0, 0, 0],
    ]);

    expect([...gems.properties(row, -1)]).toHaveLength(0);
    expect([...gems.properties(row, 3)]).toHaveLength(0);
    expect([...gems.properties(-1, 0)]).toHaveLength(0);
    expect([...gems.properties(68, 0)]).toHaveLength(0);
  });

  it('leaves every property id at -1 until a resolver is injected', () => {
    // The mod code columns hold property NAMES; without pPropertiesLinker there is nothing to
    // resolve them against, and the appliers treat a negative id as "stop".
    const gems = new GemTable(Data.gems, Items);
    const row = gems.rowForFillerClassId(Items.classIdForCode('gpr'));

    expect([...gems.properties(row, 0)].map(p => p.propertyId)).toEqual([-1, -1, -1]);

    const properties = new PropertiesTable(Data.properties, Data.itemStatCost);
    gems.resolvePropertyCodesWith(code => properties.rowForCode(code));

    expect([...gems.properties(row, 0)].map(p => p.propertyId)).toEqual([
      properties.rowForCode('fire-min'),
      properties.rowForCode('fire-max'),
      -1,
    ]);
    expect(properties.rowForCode('fire-min')).toBe(20);
    expect(properties.rowForCode('fire-max')).toBe(21);
  });

  it('is empty when either file is missing', () => {
    expect(new GemTable(null, Items).rowCount).toBe(0);
    expect(new GemTable(null, Items).rowForFillerClassId(0)).toBe(-1);
    expect(new GemTable(Data.gems, null).rowForFillerClassId(Items.classIdForCode('gcv'))).toBe(-1);
  });
});
