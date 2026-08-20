import { describe, expect, it } from 'vitest';
import { EquipRequirements } from '../../../src/D2ItemToolkit.Ts/src/Tooltip/EquipRequirements.js';
import {
  ItemIdentity,
  ItemRecordFlags,
  ItemViewer,
} from '../../../src/D2ItemToolkit.Ts/src/Stats/ItemRecord.js';
import { ItemStatReader } from '../../../src/D2ItemToolkit.Ts/src/Stats/ItemStatReader.js';
import { ItemTable } from '../../../src/D2ItemToolkit.Ts/src/Tables/ItemTable.js';
import { D2DataFiles } from '../../../src/D2ItemToolkit.Ts/src/Tables/TxtDataProviders.js';

// ITEM_CheckEquipRequirements 0x62eaf0. The C# has no direct tests — it is only reached through
// RecordSections — so these drive the class straight, which is what that class is not yet able
// to do.

const Data = D2DataFiles.load();
const Items = new ItemTable(Data.weapons, Data.armor, Data.misc);
const Requirements = new EquipRequirements(Data, Items);

function item(code: string, flags: number = ItemRecordFlags.Identified): ItemIdentity {
  const it = new ItemIdentity();
  it.classId = Items.classIdForCode(code);
  it.code = code;
  it.flags = flags;
  return it;
}

function player(classId: number, strength: number, level = 40): ItemViewer {
  const viewer = new ItemViewer();
  viewer.unitType = 0;
  viewer.classId = classId;
  viewer.strength = strength;
  viewer.level = level;
  return viewer;
}

function percent(value: number): Map<number, number> {
  const stats = new Map<number, number>();
  stats.set(ItemStatReader.packStatKey(0, 91), value);
  return stats;
}

describe('EquipRequirements', () => {
  it('the displayed requirement is the items.txt value', () => {
    // Large Shield: reqstr 34 in armor.txt. armor.txt carries no reqdex column at all, so the
    // absent column reads as the loader's 0 rather than a shifted value.
    expect(Requirements.requirement(item('lrg'), 'reqstr', null)).toBe(34);
    expect(Requirements.requirement(item('lrg'), 'reqdex', null)).toBe(0);
  });

  it('stat 91 is applied as a percentage on top', () => {
    // 34 + D2ApplyPercent(34, 50, 100) = 34 + 17.
    expect(Requirements.requirement(item('lrg'), 'reqstr', percent(50))).toBe(51);

    // Both sites skip D2ApplyPercent entirely when the percent is zero (0x48e651).
    expect(Requirements.requirement(item('lrg'), 'reqstr', percent(0))).toBe(34);

    // The percentage truncates toward zero: 34 * 33 / 100 = 11.22.
    expect(Requirements.requirement(item('lrg'), 'reqstr', percent(33))).toBe(45);
  });

  it('an ethereal item discounts ten', () => {
    const ethereal = item('lrg', ItemRecordFlags.Identified | ItemRecordFlags.Ethereal);

    expect(Requirements.requirement(ethereal, 'reqstr', null)).toBe(24);
    expect(Requirements.requirement(ethereal, 'reqstr', percent(50))).toBe(41);

    // The discount applies to the requirement, not to an absent one: a zero base returns
    // before it and never goes negative.
    expect(Requirements.requirement(ethereal, 'reqdex', null)).toBe(0);
  });

  it('a viewer with no strength at all fails', () => {
    // 0x62ebcf: `available > 0` comes first, so a zero-strength viewer fails even a
    // requirement of zero.
    expect(Requirements.metStrength(item('lrg'), player(3, 0), null)).toBe(false);
    expect(Requirements.metStrength(item('lrg'), null, null)).toBe(false);
    expect(Requirements.metDexterity(item('lrg'), player(3, 0), null)).toBe(false);
  });

  it('the strength check is a plain greater-or-equal against the displayed total', () => {
    expect(Requirements.metStrength(item('lrg'), player(3, 34), null)).toBe(true);
    expect(Requirements.metStrength(item('lrg'), player(3, 33), null)).toBe(false);

    // Ethereal moves the line and the check together.
    const ethereal = item('lrg', ItemRecordFlags.Identified | ItemRecordFlags.Ethereal);
    expect(Requirements.metStrength(ethereal, player(3, 24), null)).toBe(true);
    expect(Requirements.metStrength(ethereal, player(3, 23), null)).toBe(false);
  });

  it('the level check compares the viewer level against the calculated requirement', () => {
    // Large Shield's own items.txt levelreq is 0, so a bare one is met by anyone — including
    // a null viewer, which 0x62ec88 reads as level 0.
    expect(Requirements.metLevel(item('lrg'), null, null, null, null)).toBe(true);

    // Stat 92 is item_levelreq, which ITEM_CalcRequiredLevel adds on top (0x62ba27).
    const required = new Map<number, number>();
    required.set(ItemStatReader.packStatKey(0, 92), 25);

    expect(Requirements.metLevel(item('lrg'), player(3, 100, 25), required, null, null)).toBe(true);
    expect(Requirements.metLevel(item('lrg'), player(3, 100, 24), required, null, null)).toBe(
      false,
    );
    expect(Requirements.metLevel(item('lrg'), null, required, null, null)).toBe(false);
  });

  it('the class restriction is the primary type rows Class column', () => {
    // ItemTypes: shie has a blank Class, head is nec and ashd is pal.
    expect(Requirements.classRestriction(item('lrg'))).toBe(EquipRequirements.NoClassRestriction);
    expect(Requirements.classRestriction(item('ne1'))).toBe(2);
    expect(Requirements.classRestriction(item('pa1'))).toBe(3);
  });

  it('an unrestricted item is met by everyone, including no viewer at all', () => {
    expect(Requirements.metClass(item('lrg'), null)).toBe(true);
    expect(Requirements.metClass(item('lrg'), player(6, 0))).toBe(true);
  });

  it('a restricted item compares the class id with no unit-type test', () => {
    // 0x48e4a6 compares the player unit's class id straight against the restriction, so a
    // non-player viewer whose class id happens to match reads as met.
    expect(Requirements.metClass(item('ne1'), player(2, 0))).toBe(true);
    expect(Requirements.metClass(item('ne1'), player(3, 0))).toBe(false);
    expect(Requirements.metClass(item('ne1'), null)).toBe(false);

    const monster = new ItemViewer();
    monster.unitType = 1;
    monster.classId = 2;
    expect(Requirements.metClass(item('ne1'), monster)).toBe(true);
  });
});
