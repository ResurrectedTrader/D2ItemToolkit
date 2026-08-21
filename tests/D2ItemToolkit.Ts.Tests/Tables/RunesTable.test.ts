import { describe, expect, it } from 'vitest';
import { D2DataFiles } from '../../../src/D2ItemToolkit.Ts/src/Tables/TxtDataProviders.js';
import type { TxtFile } from '../../../src/D2ItemToolkit.Ts/src/Data/TxtFile.js';

describe('runes.txt', () => {
  const data = D2DataFiles.load();
  const runes = data.runes as TxtFile;

  it('is loaded from the embedded data', () => {
    expect(data.runes).not.toBeNull();
    expect(runes.rowCount).toBe(169);
  });

  it('lets runeword names be enumerated', () => {
    // The `Name` column is a string-table KEY, and it is the same key the game resolved to an id at
    // table-compile time — which is what a runeword item then carries in magicPrefix[0]. So a caller
    // reaches the displayed name the same way the engine does.
    const names: string[] = [];

    for (let row = 0; row < runes.rowCount; ++row) {
      if (runes.getInt(row, 'complete') === 0) {
        continue;
      }

      const key = runes.getString(row, 'Name').trim();
      if (key.length === 0) {
        continue;
      }

      const name = data.strings.getByIndex(data.strings.resolveKey(key));
      if (name !== null && name.length !== 0) {
        names.push(name);
      }
    }

    // Ancients' Pledge, not Ancient's — the apostrophe is where the .tbl puts it, and the whole
    // point of resolving through the string table is that the file's own `Rune Name` column is not
    // the displayed text.
    expect(names).toContain("Ancients' Pledge");
    expect(names).toContain('Call to Arms');

    // 78 of the 169 rows are `complete` in shipped data, and every row resolves.
    expect(names.length).toBe(78);
  });

  it('exposes the recipe columns', () => {
    const row = runes.findRow('Name', 'Runeword1');
    expect(row).toBeGreaterThanOrEqual(0);

    expect(runes.getString(row, 'Rune1').trim()).toBe('r08');
    expect(runes.getString(row, 'Rune2').trim()).toBe('r09');
    expect(runes.getString(row, 'Rune3').trim()).toBe('r07');
  });
});
