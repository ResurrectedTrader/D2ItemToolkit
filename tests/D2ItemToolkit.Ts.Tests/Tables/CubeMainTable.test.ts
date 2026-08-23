import { describe, expect, it } from 'vitest';
import { D2DataFiles } from '../../../src/D2ItemToolkit.Ts/src/Tables/TxtDataProviders.js';
import type { TxtFile } from '../../../src/D2ItemToolkit.Ts/src/Data/TxtFile.js';

describe('cubemain.txt', () => {
  const data = D2DataFiles.load();
  const cube = data.cubeMain as TxtFile;

  function rowOf(description: string): number {
    for (let row = 0; row < cube.rowCount; ++row) {
      if (cube.getString(row, 'description').includes(description)) {
        return row;
      }
    }
    return -1;
  }

  it('is loaded from the embedded data', () => {
    expect(data.cubeMain).not.toBeNull();
    expect(cube.rowCount).toBe(151);
  });

  it('marks the crafted recipes by a crf output', () => {
    let crafted = 0;

    for (let row = 0; row < cube.rowCount; ++row) {
      if (cube.getString(row, 'output').includes('crf')) {
        ++crafted;

        // Every one of them ships enabled, so none can be dismissed as unreachable.
        expect(cube.getInt(row, 'enabled')).toBe(1);
      }
    }

    expect(crafted).toBe(36);
  });

  it('carries each crafted recipe’s fixed mods with ranges', () => {
    const row = rowOf('hitpower helm');
    expect(row).toBeGreaterThanOrEqual(0);

    // These are the mods the recipe adds on top of the random affixes it also rolls, and the only
    // record of their ranges.
    expect(cube.getString(row, 'mod 2').trim()).toBe('thorns');
    expect(cube.getInt(row, 'mod 2 min')).toBe(3);
    expect(cube.getInt(row, 'mod 2 max')).toBe(7);

    expect(cube.getString(row, 'mod 3').trim()).toBe('ac-miss');
    expect(cube.getInt(row, 'mod 3 min')).toBe(25);
    expect(cube.getInt(row, 'mod 3 max')).toBe(50);
  });

  it('does not always use min and max as a range', () => {
    const row = rowOf('hitpower helm');
    expect(row).toBeGreaterThanOrEqual(0);

    // gethit-skill is a func-11 property, which reads min as the chance and max as the skill level
    // rather than as the two ends of one range. Hence min > max here, and hence a range
    // reconstruction must switch on the property's func rather than assuming every {min, max} pair
    // is an interval.
    expect(cube.getString(row, 'mod 1').trim()).toBe('gethit-skill');
    expect(cube.getInt(row, 'mod 1 min')).toBe(5);
    expect(cube.getInt(row, 'mod 1 max')).toBe(4);
  });
});
