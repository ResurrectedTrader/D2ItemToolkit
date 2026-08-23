import { describe, expect, it } from 'vitest';
import {
  ItemRecordFlags,
  ItemRecordReader,
} from '../../../src/D2ItemToolkit.Ts/src/Stats/ItemRecord.js';
import { ItemStatReader } from '../../../src/D2ItemToolkit.Ts/src/Stats/ItemStatReader.js';
import {
  RolledRangeReconstructor,
  RollSources,
  type ItemRollRanges,
  type RolledStatRange,
} from '../../../src/D2ItemToolkit.Ts/src/Stats/RolledRangeReconstructor.js';
import { createUnit, type Unit } from '../../../src/D2ItemToolkit.Ts/src/Stats/Unit.js';
import { ItemTable } from '../../../src/D2ItemToolkit.Ts/src/Tables/ItemTable.js';
import { ItemTypeTree } from '../../../src/D2ItemToolkit.Ts/src/Tables/ItemTypeTree.js';
import { MagicAffixTable } from '../../../src/D2ItemToolkit.Ts/src/Tables/MagicAffixTable.js';
import { SetTable } from '../../../src/D2ItemToolkit.Ts/src/Tables/SetTable.js';
import { D2DataFiles } from '../../../src/D2ItemToolkit.Ts/src/Tables/TxtDataProviders.js';
import { TooltipEngine } from '../../../src/D2ItemToolkit.Ts/src/Tooltip/TooltipEngine.js';
import type { TxtFile } from '../../../src/D2ItemToolkit.Ts/src/Data/TxtFile.js';

/**
 * The roll-range reconstruction. Two kinds of assertion here, and the difference matters:
 *
 * The ANCHORS compare a reconstructed span against the shipped table's own min/max columns, read
 * independently in the test. Those are the checks that can fail for a real reason.
 *
 * The SWEEPS assert invariants over every enabled row — low never above high, an item's own
 * application always inside its own span, no func left unsupported. Those cannot prove the spans
 * are right, but they are what catches a gathering bug on the 400th unique rather than the first.
 */
const Data = D2DataFiles.load();
const Engine = TooltipEngine.embedded;
const Items = new ItemTable(Data.weapons, Data.armor, Data.misc);
const Types = new ItemTypeTree(Data.itemTypes);

function reconstructor(): RolledRangeReconstructor {
  return new RolledRangeReconstructor(
    Data,
    Items,
    Types,
    new MagicAffixTable(Data),
    new SetTable(Data.sets, Data.setItems, Data.strings),
  );
}

const uniqueItems = Data.uniqueItems as TxtFile;
const setItems = Data.setItems as TxtFile;
const runes = Data.runes as TxtFile;

function uniqueItem(index: string): Unit {
  const row = uniqueItems.findRow('index', index);
  expect(row, index).toBeGreaterThanOrEqual(0);

  return createUnit({
    unitType: 4,
    quality: 7,
    fileIndex: row,
    classId: Items.classIdForCode(uniqueItems.getString(row, 'code').trim()),
    itemFlags: ItemRecordFlags.Identified,
  });
}

function range(ranges: ItemRollRanges, statId: number): RolledStatRange | undefined {
  return ranges.stats.find(r => r.statId === statId && r.layer === 0);
}

function expectSpan(ranges: ItemRollRanges, statId: number, low: number, high: number): void {
  const found = range(ranges, statId);
  expect(found, 'stat ' + String(statId)).toBeDefined();
  expect((found as RolledStatRange).low, 'stat ' + String(statId) + ' low').toBe(low);
  expect((found as RolledStatRange).high, 'stat ' + String(statId) + ' high').toBe(high);
}

describe('roll-range anchors', () => {
  it("takes a unique's spans from its own min and max columns", () => {
    // The Eye of Etlich carries six ranged props, all simple func 1 adds onto distinct stats, and
    // every stat involved has ValShift 0 — so the span must come out as exactly the row's numbers.
    const ranges = Engine.ranges(uniqueItem('The Eye of Etlich'));

    expectSpan(ranges, 32, 10, 40); // ac-miss   -> armorclass_vs_missile
    expectSpan(ranges, 89, 1, 5); // light     -> item_lightradius
    expectSpan(ranges, 60, 3, 7); // lifesteal -> lifedrainmindam
    expectSpan(ranges, 54, 1, 2); // cold-min  -> coldmindam
    expectSpan(ranges, 55, 3, 5); // cold-max  -> coldmaxdam
    expectSpan(ranges, 56, 50, 250); // cold-len  -> coldlength

    // allskills is 1..1 on this row, so it is present but not a range.
    expectSpan(ranges, 127, 1, 1);
    expect(range(ranges, 127)?.isRange).toBe(false);

    expect(ranges.unsupportedFuncs).toEqual([]);
  });

  it('reads the expected spans out of the table rather than assuming them', () => {
    const row = uniqueItems.findRow('index', 'The Eye of Etlich');
    const ranges = Engine.ranges(uniqueItem('The Eye of Etlich'));

    const byCode = new Map<string, number>([
      ['ac-miss', 32],
      ['light', 89],
      ['lifesteal', 60],
      ['cold-min', 54],
      ['cold-max', 55],
      ['cold-len', 56],
    ]);

    for (let prop = 1; prop <= 12; ++prop) {
      const code = uniqueItems.getString(row, 'prop' + String(prop)).trim();
      const statId = byCode.get(code);
      if (statId === undefined) {
        continue;
      }

      expectSpan(
        ranges,
        statId,
        uniqueItems.getInt(row, 'min' + String(prop)),
        uniqueItems.getInt(row, 'max' + String(prop)),
      );
    }
  });

  it('still rolls the base defense on a unique whose own props are fixed', () => {
    // Harlequin Crest's own props are entirely fixed — every min equals its max — so the ONLY thing
    // that varies on a Shako is the base armour roll off armor.txt.
    const unit = uniqueItem('Harlequin Crest');
    const ranges = Engine.ranges(unit);

    expectSpan(ranges, 127, 2, 2); // allskills, fixed
    expectSpan(ranges, 80, 50, 50); // mag% -> item_magicbonus, fixed

    for (const r of ranges.stats) {
      if (r.statId !== 31) {
        expect(r.isRange, 'stat ' + String(r.statId) + ' should be fixed').toBe(false);
      }
    }

    const defense = range(ranges, 31) as RolledStatRange;
    expect(defense).toBeDefined();
    expect(defense.isRange).toBe(true);
    expect(defense.sources).toBe(RollSources.Base);
    expect(defense.low).toBe(Items.getInt(unit.classId, 'minac'));
    expect(defense.high).toBe(Items.getInt(unit.classId, 'maxac'));
  });

  it('takes a superior span from qualityitems', () => {
    // A superior weapon can only have rolled the weapon-gated rows, and every row carrying `att`
    // agrees on 1..3 while every `dmg%` row agrees on 5..15 — which is what makes an unknown row
    // still give one span per stat.
    const unit = createUnit({
      unitType: 4,
      quality: 3,
      classId: Items.classIdForCode('crs'),
      itemFlags: ItemRecordFlags.Identified,
    });

    const ranges = Engine.ranges(unit);

    expectSpan(ranges, 19, 1, 3); // att  -> tohit, func 1
    expectSpan(ranges, 75, 10, 15); // dur% -> item_maxdurability_percent, func 13

    // dmg% is func 7, the enhanced-damage handler, whose integer arithmetic writes NOTHING at a 5%
    // roll on this base and 15 at a 15% roll — so its span starts at 0 rather than at 5. That is
    // the handler's own truncation, not a gap in the reconstruction.
    expectSpan(ranges, 17, 0, 15);
    expectSpan(ranges, 18, 0, 15);

    for (const r of ranges.stats) {
      expect(r.sources & RollSources.Superior, 'stat ' + String(r.statId)).not.toBe(0);
    }
  });

  it('gates a superior shield away from the weapon rows', () => {
    const unit = createUnit({
      unitType: 4,
      quality: 3,
      classId: Items.classIdForCode('buc'),
      itemFlags: ItemRecordFlags.Identified,
    });

    const ranges = Engine.ranges(unit);

    expect(range(ranges, 19)).toBeUndefined(); // no attack rating
    expectSpan(ranges, 16, 5, 15); // ac% -> item_armor_percent
  });

  it('reports every layer a layer-rolling property could land on', () => {
    // Ormus' Robes rolls its LAYER, not its value: `skill-rand` 36..60 is the twenty-five sorceress
    // skills, each carrying the same +3.
    const ranges = Engine.ranges(uniqueItem("Ormus' Robes"));

    const singleSkill = ranges.layerVaries.filter(r => r.statId === 107);
    expect(singleSkill.length).toBe(1);
    expect(singleSkill[0]?.layerLow).toBe(36);
    expect(singleSkill[0]?.layerHigh).toBe(60);
    expect(singleSkill[0]?.value).toBe(3);
    expect(singleSkill[0]?.sources).toBe(RollSources.Unique);

    // And it must NOT appear as a value span, which is the shape a naive low/high diff gives.
    expect(ranges.stats.some(r => r.statId === 107)).toBe(false);
  });

  it("takes base defense from the armor row's own range", () => {
    const unit = createUnit({
      unitType: 4,
      quality: 2,
      classId: Items.classIdForCode('xhn'),
      itemFlags: ItemRecordFlags.Identified,
    });

    const ranges = Engine.ranges(unit);

    expectSpan(
      ranges,
      31,
      Items.getInt(unit.classId, 'minac'),
      Items.getInt(unit.classId, 'maxac'),
    );

    expect((range(ranges, 31) as RolledStatRange).sources & RollSources.Base).not.toBe(0);
  });
});

describe('the ethereal base-defense bonus', () => {
  it('scales an ethereal armour’s span', () => {
    // ITEMMOD_ApplyEtherealBonus 0x65e4d0 multiplies stat 31 by 3/2 once at spawn (0x65e5d6), so a
    // captured ethereal item's Defense already includes it. A span built from the raw minac/maxac
    // would sit BELOW the value it is supposed to contain.
    const classId = Items.classIdForCode('xhn');

    const plain = createUnit({
      unitType: 4,
      quality: 2,
      classId,
      itemFlags: ItemRecordFlags.Identified,
    });

    const ethereal = createUnit({
      unitType: 4,
      quality: 2,
      classId,
      itemFlags: ItemRecordFlags.Identified | ItemRecordFlags.Ethereal,
    });

    const normal = range(Engine.ranges(plain), 31) as RolledStatRange;
    const scaled = range(Engine.ranges(ethereal), 31) as RolledStatRange;

    expect(normal).toBeDefined();
    expect(scaled).toBeDefined();

    expect(scaled.low).toBe(Math.trunc((normal.low * 3) / 2));
    expect(scaled.high).toBe(Math.trunc((normal.high * 3) / 2));
    expect(scaled.low).toBeGreaterThan(normal.low);
  });

  it('takes the other arm for an ethereal weapon', () => {
    // The bonus branches on isOfType(item, 45) — `weap` — and a weapon gets its DAMAGE stats scaled
    // instead of stat 31 (0x65e51b). A weapon has no minac anyway, so the check is what stops a
    // future armour-shaped weapon being scaled twice.
    const unit = createUnit({
      unitType: 4,
      quality: 2,
      classId: Items.classIdForCode('crs'),
      itemFlags: ItemRecordFlags.Identified | ItemRecordFlags.Ethereal,
    });

    expect(range(Engine.ranges(unit), 31)).toBeUndefined();
  });
});

/**
 * Applies one item's own reconstructed properties and checks the results against the spans claimed
 * for them. Feeding a reconstruction its OWN output is the weakest form of this check, but it is
 * the only one available without real captures, and it does catch a span built from the wrong row.
 */
function expectSelfConsistent(unit: Unit, label: string): void {
  const built = reconstructor();
  const identity = ItemRecordReader.readIdentity(unit);

  const ranges = built.reconstruct(identity, null, null, null);

  expect(ranges.unsupportedFuncs, label + ' hit an unsupported func').toEqual([]);

  for (const r of ranges.stats) {
    expect(r.low, label + ' stat ' + String(r.statId)).toBeLessThanOrEqual(r.high);
  }

  const atLow = new Map<number, number>();
  for (const r of ranges.stats) {
    atLow.set(ItemStatReader.packStatKey(r.layer, r.statId), r.low);
  }

  const checkedLow = built.reconstruct(identity, atLow, null, null);
  expect(checkedLow.outOfRange, label + ' reported its own low end out of range').toEqual([]);
}

describe('roll-range sweeps', () => {
  it('reconstructs every enabled unique consistently', () => {
    let swept = 0;

    for (let row = 0; row < uniqueItems.rowCount; ++row) {
      if (uniqueItems.getInt(row, 'enabled') === 0) {
        continue;
      }

      const classId = Items.classIdForCode(uniqueItems.getString(row, 'code').trim());
      if (classId < 0) {
        continue;
      }

      const unit = createUnit({
        unitType: 4,
        quality: 7,
        fileIndex: row,
        classId,
        itemFlags: ItemRecordFlags.Identified,
      });

      expectSelfConsistent(unit, 'unique ' + uniqueItems.getString(row, 'index'));
      ++swept;
    }

    // Counted, not guessed: 385 of the enabled rows resolve to a shipped item code.
    expect(swept).toBe(385);
  });

  it('reconstructs every set piece consistently', () => {
    let swept = 0;

    for (let row = 0; row < setItems.rowCount; ++row) {
      const classId = Items.classIdForCode(setItems.getString(row, 'item').trim());
      if (classId < 0) {
        continue;
      }

      const unit = createUnit({
        unitType: 4,
        quality: 5,
        fileIndex: row,
        classId,
        itemFlags: ItemRecordFlags.Identified,
      });

      expectSelfConsistent(unit, 'set piece ' + setItems.getString(row, 'index'));
      ++swept;
    }

    expect(swept).toBeGreaterThan(120);
  });

  it('reconstructs every affix consistently', () => {
    const affixes = new MagicAffixTable(Data);
    let swept = 0;

    for (let id = 1; id <= affixes.rowCount; ++id) {
      const unit = createUnit({
        unitType: 4,
        quality: 4,
        classId: Items.classIdForCode('crs'),
        itemFlags: ItemRecordFlags.Identified,
        magicPrefix: [id, 0, 0],
      });

      expectSelfConsistent(unit, 'affix ' + String(id));
      ++swept;
    }

    expect(swept).toBeGreaterThan(1400);
  });

  it('reconstructs every complete runeword consistently', () => {
    let swept = 0;

    for (let row = 0; row < runes.rowCount; ++row) {
      if (runes.getInt(row, 'complete') === 0) {
        continue;
      }

      const key = runes.getString(row, 'Name').trim();
      if (key.length === 0) {
        continue;
      }

      const unit = createUnit({
        unitType: 4,
        quality: 2,
        classId: Items.classIdForCode('crs'),
        itemFlags: ItemRecordFlags.Identified | ItemRecordFlags.Runeword,
        magicPrefix: [Data.strings.resolveKey(key), 0, 0],
      });

      expectSelfConsistent(unit, 'runeword ' + key);
      ++swept;
    }

    expect(swept).toBe(78);
  });
});

describe('what the item level unlocks', () => {
  /** The 1-based affix id of the first `sock` affix, over the concatenated array. */
  function sockAffixId(): number {
    const affixes = new MagicAffixTable(Data);

    for (let id = 1; id <= affixes.rowCount; ++id) {
      const resolved = affixes.tryResolve(id);
      if (resolved === null) {
        continue;
      }

      for (let mod = 1; mod <= 3; ++mod) {
        if (
          resolved.table.getString(resolved.row, 'mod' + String(mod) + 'code').trim() === 'sock'
        ) {
          return id;
        }
      }
    }

    throw new Error('no sock affix in shipped data');
  }

  function sockedUnit(itemLevel: number): Unit {
    return createUnit({
      unitType: 4,
      quality: 4,
      classId: Items.classIdForCode('crs'),
      itemFlags: ItemRecordFlags.Identified,
      magicPrefix: [sockAffixId(), 0, 0],
      itemLevel,
    });
  }

  it('reports an absent item level rather than guessing', () => {
    const unit = sockedUnit(-1);
    expect(unit.itemLevel).toBe(-1);
    expect(Engine.ranges(unit).itemLevelDependent.length).toBeGreaterThan(0);
  });

  it('removes the report once an item level is recorded', () => {
    expect(Engine.ranges(sockedUnit(50)).itemLevelDependent).toEqual([]);
  });
});
