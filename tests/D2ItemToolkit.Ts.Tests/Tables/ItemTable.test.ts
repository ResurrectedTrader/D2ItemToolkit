import { describe, expect, it } from 'vitest';
import { ItemTable } from '../../../src/D2ItemToolkit.Ts/src/Tables/ItemTable.js';
import { D2DataFiles } from '../../../src/D2ItemToolkit.Ts/src/Tables/TxtDataProviders.js';

// ItemTable.cs. TXT_AllocTxt_items compiles weapons (0x633351), then armor (0x63336d), then misc
// (0x63338c) and sums the three counts at 0x6333ab.

const Data = D2DataFiles.load();
const Items = new ItemTable(Data.weapons, Data.armor, Data.misc);

const Weapons = Data.weapons;
const Armor = Data.armor;
const Misc = Data.misc;

describe('ItemTable', () => {
  it('counts the three files as one table', () => {
    expect(Weapons?.rowCount).toBe(306);
    expect(Armor?.rowCount).toBe(202);
    expect(Misc?.rowCount).toBe(151);
    expect(Items.rowCount).toBe(659);
  });

  it('indexes the concatenation weapons, armor, misc — not armor first', () => {
    expect(Items.tryResolve(0)?.file).toBe(Weapons);
    expect(Items.tryResolve(0)?.row).toBe(0);
    expect(Items.code(0)).toBe('hax');

    expect(Items.tryResolve(305)?.file).toBe(Weapons);
    expect(Items.tryResolve(305)?.row).toBe(305);
    expect(Items.code(305)).toBe('amf');

    expect(Items.tryResolve(306)?.file).toBe(Armor);
    expect(Items.tryResolve(306)?.row).toBe(0);
    expect(Items.code(306)).toBe('cap');

    expect(Items.tryResolve(508)?.file).toBe(Misc);
    expect(Items.tryResolve(508)?.row).toBe(0);
    expect(Items.code(508)).toBe('elx');

    expect(Items.tryResolve(658)?.file).toBe(Misc);
    expect(Items.tryResolve(658)?.row).toBe(150);
    expect(Items.code(658)).toBe('std');
  });

  it('returns nothing out of range rather than clamping', () => {
    // 0x6335fc.
    expect(Items.tryResolve(-1)).toBeNull();
    expect(Items.tryResolve(659)).toBeNull();

    expect(Items.getString(659, 'code')).toBe('');
    expect(Items.getInt(659, 'levelreq')).toBe(0);
    expect(Items.code(-1)).toBe('');
    expect(Items.requiredLevel(-1)).toBe(0);
  });

  it('reads by column name so the three schemas do not shift each other', () => {
    // misc.txt has no `type2` values for a potion, and weapons.txt has no `spelldesc`; an absent
    // or blank column yields the loader's default rather than a neighbour's cell.
    expect(Items.primaryTypeCode(0)).toBe('axe');
    expect(Items.secondaryTypeCode(0)).toBe('');
    expect(Items.primaryTypeCode(508)).toBe('elix');

    expect(Items.requiredLevel(305)).toBe(Weapons?.getInt(305, 'levelreq'));
    expect(Items.requiredLevel(306)).toBe(Armor?.getInt(0, 'levelreq'));
    expect(Items.requiredLevel(508)).toBe(Misc?.getInt(0, 'levelreq'));
  });

  it('resolves a class id from a code, case-insensitively', () => {
    expect(Items.classIdForCode('gcv')).toBe(557);
    expect(Items.classIdForCode('GCV')).toBe(557);
    expect(Items.classIdForCode('lrg')).toBe(330);
    expect(Items.classIdForCode('hax')).toBe(0);

    expect(Items.classIdForCode('')).toBe(-1);
    expect(Items.classIdForCode(null)).toBe(-1);
    expect(Items.classIdForCode('nosuchcode')).toBe(-1);
  });

  it('tolerates a missing file', () => {
    const partial = new ItemTable(Weapons, null, Misc);

    expect(partial.rowCount).toBe(457);
    expect(partial.tryResolve(306)?.file).toBe(Misc);
    expect(partial.tryResolve(306)?.row).toBe(0);
  });
});
