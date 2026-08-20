import { describe, expect, it } from 'vitest';
import { PropertiesTable } from '../../../src/D2ItemToolkit.Ts/src/Tables/PropertiesTable.js';
import { D2DataFiles } from '../../../src/D2ItemToolkit.Ts/src/Tables/TxtDataProviders.js';

// The PropertiesTable half of SocketFillerTests.cs.

const Data = D2DataFiles.load();
const Properties = new PropertiesTable(Data.properties, Data.itemStatCost);

describe('PropertiesTable', () => {
  it('loads and resolves names', () => {
    // properties.bin carries 268 records.
    expect(Properties.rowCount).toBe(268);

    const resAll = Properties.rowForCode('res-all');
    expect(resAll >= 0).toBe(true);

    // "res-all" fans out to the four single resistances, so several sets carry a stat.
    const row = Properties.getRow(resAll);
    expect(row?.stat.filter(s => s >= 0).length, String(row?.stat)).toBeGreaterThanOrEqual(4);
  });

  it('keeps seven parallel sets per row', () => {
    const row = Properties.getRow(Properties.rowForCode('res-all'));

    expect(PropertiesTable.SetsPerProperty).toBe(7);
    expect(row?.set).toHaveLength(7);
    expect(row?.func).toHaveLength(7);
    expect(row?.stat).toHaveLength(7);

    expect(row?.func).toEqual([1, 3, 3, 3, 0, 0, 0]);
    expect(row?.set).toEqual([0, 0, 0, 0, 0, 0, 0]);
    expect(row?.stat).toEqual([39, 41, 43, 45, -1, -1, -1]);
    expect(row?.code).toBe('res-all');
  });

  it('gives an unresolvable or blank stat name -1', () => {
    // ITEMMODS_AddPropertyToItemStatList rejects a stat it cannot find an ItemStatCost record
    // for, which is what the loader's -1 means.
    for (let i = 0; i < Properties.rowCount; ++i) {
      const row = Properties.getRow(i);
      expect(row).not.toBeNull();
      for (let set = 0; set < PropertiesTable.SetsPerProperty; ++set) {
        const name = Data.properties?.getString(i, 'stat' + String(set + 1)) ?? '';
        if (name.trim().length === 0) {
          expect(row?.stat[set], `row ${i} set ${set}`).toBe(-1);
        }
      }
    }
  });

  it('resolves a code to its compiled id, case-insensitively', () => {
    expect(Properties.rowForCode('res-all')).toBe(41);
    expect(Properties.rowForCode('RES-ALL')).toBe(41);
    expect(Properties.rowForCode('fire-min')).toBe(20);
    expect(Properties.rowForCode('fire-max')).toBe(21);

    expect(Properties.rowForCode('')).toBe(-1);
    expect(Properties.rowForCode(null)).toBe(-1);
    expect(Properties.rowForCode('nosuchcode')).toBe(-1);
  });

  it('returns null outside the table', () => {
    expect(Properties.getRow(-1)).toBeNull();
    expect(Properties.getRow(268)).toBeNull();
  });

  it('is empty without a file and unresolved without a stat table', () => {
    expect(new PropertiesTable(null, Data.itemStatCost).rowCount).toBe(0);
    expect(new PropertiesTable(null, Data.itemStatCost).rowForCode('res-all')).toBe(-1);

    const unlinked = new PropertiesTable(Data.properties, null);
    expect(unlinked.rowCount).toBe(268);
    expect(unlinked.getRow(unlinked.rowForCode('res-all'))?.stat).toEqual([
      -1, -1, -1, -1, -1, -1, -1,
    ]);
  });
});
