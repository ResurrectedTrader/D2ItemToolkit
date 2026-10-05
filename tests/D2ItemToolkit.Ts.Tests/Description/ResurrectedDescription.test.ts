import { describe, expect, it } from 'vitest';
import { GameVariant } from '../../../src/D2ItemToolkit.Ts/src/Data/GameVariant.js';
import { ItemRecordFlags } from '../../../src/D2ItemToolkit.Ts/src/Stats/ItemRecord.js';
import { ItemStatListFlags } from '../../../src/D2ItemToolkit.Ts/src/Stats/ItemStatReader.js';
import { createUnit, type UnitStat } from '../../../src/D2ItemToolkit.Ts/src/Stats/Unit.js';
import { ItemQualityNo } from '../../../src/D2ItemToolkit.Ts/src/Tooltip/ItemNameBuilder.js';
import { TooltipEngine } from '../../../src/D2ItemToolkit.Ts/src/Tooltip/TooltipEngine.js';

/** ITEMSTATDESC_Build 0x1401eba60 on D2R data, where the line text lives in printf formats. */
const Engine = TooltipEngine.forVariant(GameVariant.ReignOfTheWarlock);

function modifiers(classId: number, ...statLayerValue: number[]): string {
  const stats: UnitStat[] = [];
  for (let at = 0; at < statLayerValue.length; at += 3) {
    stats.push({
      id: statLayerValue[at] as number,
      layer: statLayerValue[at + 1] as number,
      value: statLayerValue[at + 2] as number,
    });
  }

  const ring = createUnit({
    unitType: 4,
    classId: Engine.items.classIdForCode('rin'),
    quality: ItemQualityNo.Rare,
    itemFlags: ItemRecordFlags.Identified,
    statsLists: [{ stateNo: 0, flags: ItemStatListFlags.Magic, stats }],
  });

  const viewer = createUnit({
    unitType: 0,
    classId,
    statsLists: [
      {
        stateNo: 0,
        flags: 0,
        stats: [
          { id: 0, value: 100 },
          { id: 2, value: 100 },
          { id: 12, value: 60 },
        ],
      },
    ],
  });

  return Engine.render(ring, viewer).text;
}

function skill(name: string): number {
  const skills = Engine.data.skillRows;
  if (skills === null) {
    return -1;
  }

  for (let row = 0; row < skills.rowCount; ++row) {
    if (skills.getString(row, 'skill') === name) {
      return row;
    }
  }

  return -1;
}

describe('the D2R description engine', () => {
  it('per-level func 19 appends its descstr2', () => {
    // (8 * level 60) >> 3, then " " + increaseswithplaylevelX (0x1401ecb3a).
    expect(modifiers(0, 214, 0, 8)).toContain('+60 Defense (Based on Character Level)');
  });

  it('func 29 picks the string by sign then prints the magnitude', () => {
    expect(modifiers(0, 36, 0, 10)).toContain('Physical Damage Received Reduced by 10%');
    expect(modifiers(0, 36, 0, -15)).toContain('Physical Damage Received Increased by 15%');
  });

  it('the own-class oskill clamp is dead', () => {
    const battleOrders = skill('Battle Orders');
    expect(modifiers(4, 97, battleOrders, 6)).toContain('+6 to Battle Orders');
    expect(modifiers(0, 97, battleOrders, 6)).toContain('+6 to Battle Orders');
  });

  it('a warlock class skill keeps its class line', () => {
    expect(modifiers(7, 107, skill('Levitate'), 2)).toContain(
      '+2 to Levitation Mastery (Warlock Only)',
    );
  });

  it('class skill levels format the charstats string', () => {
    expect(modifiers(7, 83, 7, 1)).toContain('+1 to Warlock Skills');
    expect(modifiers(4, 83, 4, 2)).toContain('+2 to Barbarian Skill Levels');
  });

  it('howl formats its own string', () => {
    expect(modifiers(0, 112, 0, 64)).toContain('Hit Causes Monster to Flee +50%');
  });

  it('the damage aggregate uses the d2r keys', () => {
    // Each aggregate line ends with the `newline` key the game appends (0x1401ea91e); the rare
    // name above it is " " (1718 with two empty affixes) over the base.
    expect(modifiers(0, 48, 0, 5, 49, 0, 10)).toBe(' \nRing\nAdds 5-10 fire damage');
    expect(modifiers(0, 48, 0, 7, 49, 0, 7)).toBe(' \nRing\n+7 fire damage');
    expect(modifiers(0, 57, 0, 256, 58, 0, 512, 59, 0, 75)).toBe(
      ' \nRing\nAdds 75-150 poison damage over 3 seconds',
    );
    expect(modifiers(0, 48, 0, 5)).toContain('+5 to Minimum Fire Damage');
  });
});
