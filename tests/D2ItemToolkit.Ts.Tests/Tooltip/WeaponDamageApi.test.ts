import { describe, expect, it } from 'vitest';
import { ItemRecordFlags } from '../../../src/D2ItemToolkit.Ts/src/Stats/ItemRecord.js';
import { createUnit, type Unit } from '../../../src/D2ItemToolkit.Ts/src/Stats/Unit.js';
import { ItemTable } from '../../../src/D2ItemToolkit.Ts/src/Tables/ItemTable.js';
import { D2DataFiles } from '../../../src/D2ItemToolkit.Ts/src/Tables/TxtDataProviders.js';
import { ItemTooltipSection } from '../../../src/D2ItemToolkit.Ts/src/Tooltip/ItemTooltip.js';
import {
  ItemDamageKind,
  type ItemDamage,
} from '../../../src/D2ItemToolkit.Ts/src/Tooltip/ItemDamage.js';
import { TooltipEngine } from '../../../src/D2ItemToolkit.Ts/src/Tooltip/TooltipEngine.js';

/**
 * The peer of the C# WeaponDamageApiTests. `damage` returns the numbers the WeaponDamage section
 * writes rather than the string it writes them into.
 *
 * The load-bearing test is the last one: the API's numbers must be the numbers in the rendered
 * line, for every shape. The two share `damageValues` but route to it separately, and a routing
 * that drifts is exactly the fault a pair of hand-written tests would each pass while disagreeing.
 */
const Data = D2DataFiles.load();
const Engine = TooltipEngine.embedded;
const Items = new ItemTable(Data.weapons, Data.armor, Data.misc);

/** Bastard Sword: both `1or2handed` and `2handed`, so it reaches the Barbarian arm. */
const Versatile = 'bsw';

const BarbarianClass = 4;
const PaladinClass = 3;

const ListFlagsMagic = 0x40;
const ListFlagsExtended = 0x80000000;

function weapon(code: string, ...statValue: number[]): Unit {
  const stats: { id: number; value: number }[] = [];
  for (let i = 0; i + 1 < statValue.length; i += 2) {
    stats.push({ id: statValue[i] as number, value: statValue[i + 1] as number });
  }

  return createUnit({
    unitType: 4,
    classId: Items.classIdForCode(code),
    itemFlags: ItemRecordFlags.Identified,
    statsLists: [{ stateNo: 0, flags: ListFlagsExtended, stats }],
  });
}

function player(classId: number): Unit {
  return createUnit({ unitType: 0, classId });
}

function single(damage: ItemDamage): ItemDamage['lines'][number] {
  expect(damage.lines).toHaveLength(1);
  return damage.lines[0] as ItemDamage['lines'][number];
}

/**
 * The damage numbers as the tooltip actually draws them, in display order. A line whose min equals
 * its max drops the "to max" half (0x4855bd), which is why a missing second number is read as a
 * repeat rather than as a mismatch.
 */
function renderedDamageNumbers(item: Unit, viewer: Unit | null): number[] {
  const numbers: number[] = [];

  for (const line of Engine.render(item, viewer).lines) {
    if (line.section !== ItemTooltipSection.WeaponDamage) {
      continue;
    }

    const text = (line.text ?? '').replace(/ÿc./g, '');
    const matched = text.match(/-?\d+/g);
    if (matched === null) {
      continue;
    }

    numbers.push(Number(matched[0]));
    numbers.push(Number(matched[matched.length - 1]));
  }

  return numbers;
}

describe('the weapon damage API', () => {
  it('gives a one-handed weapon a single one-hand line', () => {
    // Short Sword is 1-handed, so stats 21/22 are the pair and there is nothing else.
    const line = single(Engine.damage(weapon('ssd', 21, 7, 22, 14)));

    expect(line.kind).toBe(ItemDamageKind.OneHand);
    expect(line.min).toBe(7);
    expect(line.max).toBe(14);
  });

  it('reads the secondary pair for a two-handed weapon', () => {
    // 0x4858f1 picks the pair from items.txt `2handed`. Maul is two-handed only.
    const line = single(Engine.damage(weapon('mau', 23, 30, 24, 60)));

    expect(line.kind).toBe(ItemDamageKind.TwoHand);
    expect(line.min).toBe(30);
    expect(line.max).toBe(60);
  });

  it('gives a barbarian both lines with one-hand on top', () => {
    const damage = Engine.damage(
      weapon(Versatile, 23, 20, 24, 40, 21, 10, 22, 25),
      player(BarbarianClass),
    );

    expect(damage.lines.map(l => l.kind)).toEqual([ItemDamageKind.OneHand, ItemDamageKind.TwoHand]);

    expect(damage.lines[0]?.min).toBe(10);
    expect(damage.lines[0]?.max).toBe(25);
    expect(damage.lines[1]?.min).toBe(20);
    expect(damage.lines[1]?.max).toBe(40);
  });

  it('gives anyone else holding the same weapon one line', () => {
    // The arm is BARBARIAN_CheckItemData_b1or2Handed_isTrue 0x62a1e0 — class 4 alone.
    const damage = Engine.damage(
      weapon(Versatile, 23, 20, 24, 40, 21, 10, 22, 25),
      player(PaladinClass),
    );

    expect(single(damage).kind).toBe(ItemDamageKind.TwoHand);
  });

  it('clamps the single line and not the dual pair', () => {
    // 0x485931 forces max above min; the Barbarian arm at 0x485669 has no such step.
    expect(single(Engine.damage(weapon(Versatile, 23, 40, 24, 40))).max).toBe(41);

    const dual = Engine.damage(
      weapon(Versatile, 23, 40, 24, 40, 21, 15, 22, 15),
      player(BarbarianClass),
    );

    expect(dual.lines[0]?.max).toBe(15);
    expect(dual.lines[1]?.max).toBe(40);
  });

  it('puts a throwable weapon’s throw line above its own', () => {
    // Throwing Knife. The throw pair is 159/160, appended last and emitted reversed, so it is the
    // TOP row and therefore first here.
    const damage = Engine.damage(weapon('tkf', 21, 6, 22, 12, 159, 8, 160, 16));

    expect(damage.lines.map(l => l.kind)).toEqual([ItemDamageKind.Throw, ItemDamageKind.OneHand]);
    expect(damage.lines[0]?.min).toBe(8);
    expect(damage.lines[0]?.max).toBe(16);
  });

  it('reads missiles.txt for a throwing potion and nothing else', () => {
    // 0x485459 takes the tpot arm outright: no ordinary line and no throw line, and the numbers are
    // the missile's rather than any stat's. Fulminating Potion is missile 44.
    const line = single(Engine.damage(weapon('opl', 21, 99, 22, 99)));

    expect(line.kind).toBe(ItemDamageKind.ThrowingPotion);
    // 5 to 15 in the fire colour. The end-to-end assertion for this item reads
    // `Marker + '15 to ' + Marker + '115'`, where the leading 1 of each is the COLOUR digit glued
    // to the number — the concatenation invites exactly that misreading.
    expect(line.min).toBe(5);
    expect(line.max).toBe(15);
    expect(line.modified).toBe(false);
  });

  it('gives a non-weapon no damage at all', () => {
    expect(Engine.damage(weapon('lrg', 21, 10, 22, 20)).lines).toEqual([]);
  });

  it('skips the section for a negative damage stat but not for zero', () => {
    // 0x48e704 / 0x48e716 gate on >= 0, and read stats 21 and 22 even for a two-handed weapon.
    expect(Engine.damage(weapon('mau', 21, -1)).lines).toEqual([]);

    // Zero passes, and the clamp turns it into 0 to 1.
    const line = single(Engine.damage(weapon('mau')));
    expect(line.min).toBe(0);
    expect(line.max).toBe(1);
  });

  it('tracks the colour the line is painted', () => {
    // All on the base list, so base === merged and pModified stays clear.
    expect(single(Engine.damage(weapon('mau', 23, 30, 24, 60))).modified).toBe(false);

    // A magic list puts the merged value above the base one (0x485300).
    const enhanced = weapon('mau', 23, 30, 24, 60);
    enhanced.statsLists.push({
      stateNo: 0,
      flags: ListFlagsMagic,
      stats: [{ id: 23, value: 5 }],
    });

    expect(single(Engine.damage(enhanced)).modified).toBe(true);
  });

  it('returns the numbers the rendered line shows', () => {
    const cases: [Unit, Unit | null][] = [
      [weapon('ssd', 21, 7, 22, 14), null],
      [weapon('mau', 23, 30, 24, 60), null],
      [weapon('mau'), null],
      [weapon(Versatile, 23, 40, 24, 40), null],
      [weapon(Versatile, 23, 20, 24, 40, 21, 10, 22, 25), player(BarbarianClass)],
      [weapon(Versatile, 23, 40, 24, 40, 21, 15, 22, 15), player(BarbarianClass)],
      [weapon('tkf', 21, 6, 22, 12, 159, 8, 160, 16), null],
      [weapon('opl'), null],
    ];

    for (const [item, viewer] of cases) {
      const fromApi = Engine.damage(item, viewer).lines.flatMap(l => [l.min, l.max]);

      expect(fromApi).toEqual(renderedDamageNumbers(item, viewer));
    }
  });
});
