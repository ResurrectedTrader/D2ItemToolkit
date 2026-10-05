import { describe, expect, it } from 'vitest';
import { GameVariant } from '../../../src/D2ItemToolkit.Ts/src/Data/GameVariant.js';
import { ItemRecordFlags } from '../../../src/D2ItemToolkit.Ts/src/Stats/ItemRecord.js';
import { ItemStatListFlags } from '../../../src/D2ItemToolkit.Ts/src/Stats/ItemStatReader.js';
import { createUnit, type Unit } from '../../../src/D2ItemToolkit.Ts/src/Stats/Unit.js';
import { TooltipEngine } from '../../../src/D2ItemToolkit.Ts/src/Tooltip/TooltipEngine.js';
import { WeaponMastery } from '../../../src/D2ItemToolkit.Ts/src/Tooltip/WeaponMastery.js';

/**
 * D2R's mastery terms: stat 203 on the strength/dexterity requirement
 * (SKILLS_GetWeaponMasteryBonus 0x14024d610, its throwing arm 0x14024d380, the dual-melee test
 * 0x140239c30) and stat 209 on the level requirement (0x140228913-0x140228aa0). The twin of
 * WeaponMasteryTests.cs, string for string.
 */
const Engine = TooltipEngine.forVariant(GameVariant.ReignOfTheWarlock);

const Levitate = 0x402d; // weap (45), condition 1: not dual-wielding
const WhileDualMelee = 0x802d; // weap, condition 2: only dual-wielding

function lines(item: Unit, viewer: Unit, engine: TooltipEngine = Engine): string[] {
  return engine.render(item, viewer).text.split('\n');
}

function item(code: string, stat92 = 0, engine: TooltipEngine = Engine): Unit {
  const classId = engine.items.classIdForCode(code);
  expect(classId, code).toBeGreaterThanOrEqual(0);
  const stats = [
    { id: 72, value: 30 },
    { id: 73, value: 30 },
  ];
  if (stat92 !== 0) {
    stats.push({ id: 92, value: stat92 });
  }

  return createUnit({
    unitType: 4,
    classId,
    itemFlags: ItemRecordFlags.Identified,
    itemLevel: 50,
    statsLists: [{ stateNo: 0, flags: ItemStatListFlags.Extended, stats }],
  });
}

function player(
  classId: number,
  statId: number,
  value: number,
  layer: number,
  lastUsedSkill = -1,
  dexterity = 100,
): Unit {
  return createUnit({
    unitType: 0,
    classId,
    lastUsedSkill,
    statsLists: [
      {
        stateNo: 0,
        flags: 0,
        stats: [
          { id: 0, value: 100 },
          { id: 2, value: dexterity },
          { id: 12, value: 90 },
          { id: statId, value, layer },
        ],
      },
    ],
  });
}

function warlock(lastUsedSkill = -1, dexterity = 100): Unit {
  return player(
    7,
    WeaponMastery.StatItemRequirementPercent,
    -20,
    Levitate,
    lastUsedSkill,
    dexterity,
  );
}

function wield(wearer: Unit, code: string, bodyLocation: number, extra = 0): void {
  const weapon = item(code);
  weapon.itemFlags |= extra;
  weapon.location = 1;
  weapon.x = bodyLocation;
  wearer.items.push(weapon);
}

describe('the D2R weapon mastery terms', () => {
  it.each([
    [-1, 'Required Dexterity: 17'], // no last-used skill: the generic arm, -20%
    [0, 'Required Dexterity: 17'], // Attack: itypea1 is not a throwing type
    [2, 'Required Dexterity: 21'], // Throw engages the arm; 16429 matches nothing
    [15, 'Required Dexterity: 21'], // Poison Javelin
  ])('a throwing skill takes the throwing arm, which returns even zero (%i)', (skill, expected) => {
    expect(lines(item('tkf'), warlock(skill))).toContain(expected);
  });

  it.each([
    [-1, 20],
    [2, 25],
  ])('a javelin takes the arm on both requirements (%i)', (skill, expected) => {
    const rendered = lines(item('9ja'), warlock(skill));
    expect(rendered).toContain(`Required Strength: ${expected}`);
    expect(rendered).toContain(`Required Dexterity: ${expected}`);
  });

  it('a weapon that is not throwable never takes the arm', () => {
    const rendered = lines(item('sbr'), warlock(2));
    expect(rendered).toContain('Required Strength: 20');
    expect(rendered).toContain('Required Dexterity: 20');
  });

  it('the met flag follows the arm', () => {
    expect(Engine.render(item('tkf'), warlock(2, 18)).coloredText.split('\n')).toContain(
      'ÿc1Required Dexterity: 21',
    );
    expect(Engine.render(item('tkf'), warlock(-1, 18)).coloredText.split('\n')).toContain(
      'ÿc0Required Dexterity: 17',
    );
  });

  it.each([
    [Levitate, 'scm', 5, 0, 'Required Strength: 25'],
    [Levitate, 'scm', 5, ItemRecordFlags.Broken, 'Required Strength: 20'],
    [Levitate, 'scm', 12, 0, 'Required Strength: 20'],
    [WhileDualMelee, 'scm', 5, 0, 'Required Strength: 20'],
    [WhileDualMelee, null, 5, 0, 'Required Strength: 25'],
    [Levitate, 'lrg', 5, 0, 'Required Strength: 20'],
  ] as const)(
    'dual melee is two clean melee weapons in the active hands (%i %s %i %i)',
    (layer, offHand, offHandLocation, offHandFlags, expected) => {
      const barbarian = player(4, WeaponMastery.StatItemRequirementPercent, -20, layer);
      wield(barbarian, 'sbr', 4);
      if (offHand !== null) {
        wield(barbarian, offHand, offHandLocation, offHandFlags);
      }

      expect(lines(item('sbr'), barbarian)).toContain(expected);
    },
  );

  it.each([
    [0, -25, 'Required Level: 30'],
    [45, -25, 'Required Level: 40'], // weap, and a ring is not one
  ])('stat 209 scales the finished level (%i, %i)', (layer, percent, expected) => {
    const viewer = player(1, WeaponMastery.StatItemLevelRequirementPercent, percent, layer);
    expect(lines(item('rin', 40), viewer)).toContain(expected);
  });

  it('stat 209 is not clamped, so a full discount hides the line', () => {
    const viewer = player(1, WeaponMastery.StatItemLevelRequirementPercent, -100, 0);
    expect(lines(item('rin', 40), viewer).some(l => l.startsWith('Required Level'))).toBe(false);
  });

  it('stat 209 compounds through a socket filler', () => {
    // Ber's levelreq 63 becomes 63 + (63 * -50) / 100 = 32 inside the recursion, the cap takes it
    // as its own and applies the term again: 32 - 16 = 16. Applying it once would give 32.
    const helm = item('cap');
    helm.itemFlags |= ItemRecordFlags.Socketed;
    helm.items.push(item('r30'));

    const viewer = player(1, WeaponMastery.StatItemLevelRequirementPercent, -50, 0);
    expect(lines(helm, viewer)).toContain('Required Level: 16');
  });

  it('LoD ignores both stats', () => {
    const viewer = player(1, WeaponMastery.StatItemLevelRequirementPercent, -25, 0);
    viewer.statsLists[0]?.stats.push({ id: WeaponMastery.StatItemRequirementPercent, value: -20 });
    const lod = TooltipEngine.embedded;
    expect(lines(item('rin', 40, lod), viewer, lod)).toContain('Required Level: 40');
  });

  it('reads lastUsedSkill off the wire and defaults it to -1', async () => {
    const { unitFromJson } = await import('../../../src/D2ItemToolkit.Ts/src/Stats/Unit.js');
    expect(unitFromJson({ lastUsedSkill: 2 }).lastUsedSkill).toBe(2);
    expect(unitFromJson({}).lastUsedSkill).toBe(-1);
  });
});
