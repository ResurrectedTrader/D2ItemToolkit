import { describe, expect, it } from 'vitest';
import { ItemTable } from '../../../src/D2ItemToolkit.Ts/src/Tables/ItemTable.js';
import { MissileTable } from '../../../src/D2ItemToolkit.Ts/src/Tables/MissileTable.js';
import { D2DataFiles } from '../../../src/D2ItemToolkit.Ts/src/Tables/TxtDataProviders.js';

// MissileTable.cs — the slice of missiles.txt the throwing-potion damage arm reads (0x485410).

const Data = D2DataFiles.load();
const Items = new ItemTable(Data.weapons, Data.armor, Data.misc);
const Missiles = new MissileTable(Data.missiles, Data.elementTypes);

describe('MissileTable', () => {
  it('spreads a poison cloud over its duration and collapses an equal range', () => {
    // Rancid Gas Potion fires missile 49: 192 poison over an ELen of 50, divided by 50/25 = 2
    // (0x4854fd). Poison takes colour 2 from the table at 0x4854d0.
    expect(Items.getInt(Items.classIdForCode('gps'), 'missiletype')).toBe(49);

    expect(Missiles.tryGetThrowDamage(49)).toEqual({ min: 96, max: 96, color: 2 });
  });

  it('adds the elemental half to the physical half and shifts both back', () => {
    // Fulminating Potion fires missile 44: physical 2-7 plus fire 3-8, both shifted by the
    // record's HitShift of 8 and shifted back at 0x48554c / 0x485559.
    expect(Items.getInt(Items.classIdForCode('opl'), 'missiletype')).toBe(44);

    expect(Missiles.tryGetThrowDamage(44)).toEqual({ min: 5, max: 15, color: 1 });
  });

  it('picks the colour from the jump table at 0x4854d0', () => {
    // Indexed by elemType - 1. Magic (3) and everything outside 1..5 take the default arm,
    // which leaves the colour at 0.
    expect(Missiles.tryGetThrowDamage(22)?.color).toBe(1); // fire
    expect(Missiles.tryGetThrowDamage(99)?.color).toBe(4); // ltng
    expect(Missiles.tryGetThrowDamage(107)?.color).toBe(3); // cold
    expect(Missiles.tryGetThrowDamage(32)?.color).toBe(2); // pois
    expect(Missiles.tryGetThrowDamage(77)?.color).toBe(0); // mag
    expect(Missiles.tryGetThrowDamage(271)?.color).toBe(0); // frze, past the table
    expect(Missiles.tryGetThrowDamage(7)?.color).toBe(0); // no EType at all
  });

  it('never lets max fall below min', () => {
    // 0x48555c raises max to min, never the other way round.
    for (let id = 0; id < (Data.missiles?.rowCount ?? 0); ++id) {
      const damage = Missiles.tryGetThrowDamage(id);
      expect(damage, String(id)).not.toBeNull();
      expect(damage!.max >= damage!.min, String(id)).toBe(true);
    }
  });

  it('rejects an id outside the table', () => {
    expect(Data.missiles?.rowCount).toBe(684);

    expect(Missiles.tryGetThrowDamage(-1)).toBeNull();
    expect(Missiles.tryGetThrowDamage(684)).toBeNull();
    expect(new MissileTable(null, Data.elementTypes).tryGetThrowDamage(49)).toBeNull();
  });

  it('reads EType as a row index into elemtypes.txt', () => {
    // The linker field stores the ROW INDEX (0x612993), so an unknown or blank code is row 0 and
    // takes the colourless arm.
    expect(Data.elementTypes?.getString(5, 'Code')).toBe('pois');
    expect(new MissileTable(Data.missiles, null).tryGetThrowDamage(49)?.color).toBe(0);
  });
});
