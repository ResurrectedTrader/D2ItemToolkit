import { describe, expect, it } from 'vitest';
import { ColorTable } from '../../../src/D2ItemToolkit.Ts/src/Tables/ColorTable.js';
import { GemTable } from '../../../src/D2ItemToolkit.Ts/src/Tables/GemTable.js';
import { ItemTable } from '../../../src/D2ItemToolkit.Ts/src/Tables/ItemTable.js';
import { ItemTypeTree } from '../../../src/D2ItemToolkit.Ts/src/Tables/ItemTypeTree.js';
import { MagicAffixTable } from '../../../src/D2ItemToolkit.Ts/src/Tables/MagicAffixTable.js';
import { MissileTable } from '../../../src/D2ItemToolkit.Ts/src/Tables/MissileTable.js';
import { PropertiesTable } from '../../../src/D2ItemToolkit.Ts/src/Tables/PropertiesTable.js';
import { SetTable } from '../../../src/D2ItemToolkit.Ts/src/Tables/SetTable.js';
import {
  D2DataFiles,
  TxtCharacterClassTable,
} from '../../../src/D2ItemToolkit.Ts/src/Tables/TxtDataProviders.js';

/**
 * The peer of TableEnumerationTests.cs. Every public data table is walked the same way:
 * `rowCount` for the bound and `rowAt(index)` for the row. Before this the tables disagreed on both
 * halves — count vs rowCount vs statCount vs skillCount, and code(i) vs codeAt(i) vs getRow(i) — so
 * iterating several of them meant remembering which spelling each had picked. Four had no count.
 *
 * Two tables keep two counts because they have two row spaces, and name their accessors after them:
 * SetTable (setAt / pieceAt) and TxtMonsterTypeTable (monsterAt / monsterTypeAt).
 */
describe('table enumeration', () => {
  const data = D2DataFiles.load();
  const items = new ItemTable(data.weapons, data.armor, data.misc);

  it('reports the affix table as its concatenated length', () => {
    const affixes = new MagicAffixTable(data);

    // [MagicSuffix][MagicPrefix][automagic], which is the array the game indexes 1-based.
    const expected =
      (data.magicSuffix?.rowCount ?? 0) +
      (data.magicPrefix?.rowCount ?? 0) +
      (data.autoMagic?.rowCount ?? 0);

    expect(affixes.rowCount).toBe(expected);

    // Counted against the shipped files, and the same number the C# test asserts.
    expect(affixes.rowCount).toBe(1452);

    expect(affixes.tryResolve(affixes.rowCount)).not.toBeNull();
    expect(affixes.tryResolve(affixes.rowCount + 1)).toBeNull();
    expect(affixes.tryResolve(0)).toBeNull();
  });

  it('reports the missile table rows', () => {
    const missiles = new MissileTable(data.missiles, data.elementTypes);

    expect(missiles.rowCount).toBe(data.missiles?.rowCount ?? 0);
    expect(missiles.tryGetThrowDamage(missiles.rowCount)).toBeNull();
  });

  it('reports the class table rows', () => {
    expect(data.classes.rowCount).toBe(data.charStats?.rowCount ?? 0);
    expect(data.classes.rowCount).toBeGreaterThanOrEqual(7);

    for (let classId = 0; classId < data.classes.rowCount; ++classId) {
      expect(data.classes.classExists(classId)).toBe(true);

      const row = data.classes.rowAt(classId);
      expect(row).not.toBeNull();
      expect(row!.classId).toBe(classId);
      expect(row!.skillTabTexts.length).toBe(TxtCharacterClassTable.SkillTabsPerClass);
    }

    expect(data.classes.rowAt(data.classes.rowCount)).toBeNull();
    expect(data.classes.rowAt(-1)).toBeNull();
  });

  it('names both monster row spaces', () => {
    expect(data.monsterTypes.monsterCount).toBe(data.monsterStats?.rowCount ?? 0);
    expect(data.monsterTypes.monsterTypeCount).toBeGreaterThan(0);

    expect(data.monsterTypes.monsterAt(0)).not.toBeNull();
    expect(data.monsterTypes.monsterTypeAt(0)).not.toBeNull();
    expect(data.monsterTypes.monsterAt(data.monsterTypes.monsterCount)).toBeNull();
    expect(data.monsterTypes.monsterTypeAt(data.monsterTypes.monsterTypeCount)).toBeNull();
  });

  it('walks every table by rowCount and rowAt', () => {
    for (let i = 0; i < items.rowCount; ++i) {
      const row = items.rowAt(i);
      expect(row).not.toBeNull();
      expect(row!.classId).toBe(i);
      expect(row!.code).toBe(items.code(i));
    }

    const types = new ItemTypeTree(data.itemTypes);
    for (let i = 0; i < types.rowCount; ++i) {
      expect(types.rowAt(i)!.code).toBe(types.codeAt(i));
    }

    const colors = new ColorTable(data.colors);
    for (let i = 0; i < colors.rowCount; ++i) {
      expect(colors.rowAt(i)!.code).toBe(colors.codeAt(i));
    }

    const gems = new GemTable(data.gems, items);
    for (let i = 0; i < gems.rowCount; ++i) {
      const row = gems.rowAt(i);
      expect(row!.code).toBe(gems.code(i));
      expect(row!.letter).toBe(gems.letter(i));
    }

    const properties = new PropertiesTable(data.properties, data.itemStatCost);
    for (let i = 0; i < properties.rowCount; ++i) {
      expect(properties.rowAt(i)).toBe(properties.getRow(i));
    }

    for (let i = 0; i < data.itemStatCost.rowCount; ++i) {
      expect(data.itemStatCost.rowAt(i)!.statId).toBe(i);
    }

    for (let i = 0; i < data.skills.rowCount; ++i) {
      expect(data.skills.skillExists(i)).toBe(true);

      const row = data.skills.rowAt(i);
      expect(row!.skillId).toBe(i);
      expect(row!.name).toBe(data.skills.getSkillName(i));
    }
  });

  it('names both set row spaces', () => {
    const sets = new SetTable(data.sets, data.setItems, data.strings);

    for (let i = 0; i < sets.setCount; ++i) {
      expect(sets.setAt(i)).not.toBeNull();
    }

    for (let i = 0; i < sets.pieceCount; ++i) {
      expect(sets.pieceAt(i)).not.toBeNull();
    }
  });

  it('returns null past the end rather than throwing', () => {
    const types = new ItemTypeTree(data.itemTypes);
    const colors = new ColorTable(data.colors);
    const gems = new GemTable(data.gems, items);
    const properties = new PropertiesTable(data.properties, data.itemStatCost);

    expect(items.rowAt(items.rowCount)).toBeNull();
    expect(types.rowAt(types.rowCount)).toBeNull();
    expect(colors.rowAt(colors.rowCount)).toBeNull();
    expect(gems.rowAt(gems.rowCount)).toBeNull();
    expect(properties.rowAt(properties.rowCount)).toBeNull();
    expect(data.itemStatCost.rowAt(data.itemStatCost.rowCount)).toBeNull();
    expect(data.skills.rowAt(data.skills.rowCount)).toBeNull();

    expect(items.rowAt(-1)).toBeNull();
    expect(types.rowAt(-1)).toBeNull();
    expect(gems.rowAt(-1)).toBeNull();
  });

  it('freezes stat descriptors so a caller cannot corrupt the shared table', () => {
    // The C# hands out a copy from the public accessor; TypeScript has no `internal`, so the
    // descriptors are frozen instead — cheaper, and a stray write throws in module code rather
    // than silently landing on a copy.
    const descriptor = data.itemStatCost.rowAt(39);
    expect(descriptor).not.toBeNull();
    expect(Object.isFrozen(descriptor)).toBe(true);
    expect(() => {
      (descriptor as unknown as { descFunc: number }).descFunc = 3;
    }).toThrow();
  });
});
