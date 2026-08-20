import { describe, expect, it } from 'vitest';
import { GemTable } from '../../../src/D2ItemToolkit.Ts/src/Tables/GemTable.js';
import { ItemIdentity } from '../../../src/D2ItemToolkit.Ts/src/Stats/ItemRecord.js';
import { ItemTable } from '../../../src/D2ItemToolkit.Ts/src/Tables/ItemTable.js';
import { ItemTypeTree } from '../../../src/D2ItemToolkit.Ts/src/Tables/ItemTypeTree.js';
import { PropertyApplier } from '../../../src/D2ItemToolkit.Ts/src/Stats/PropertyApplier.js';
import { D2DataFiles } from '../../../src/D2ItemToolkit.Ts/src/Tables/TxtDataProviders.js';
import type { TxtFile } from '../../../src/D2ItemToolkit.Ts/src/Data/TxtFile.js';

// The property-engine half of SocketFillerTests.cs. The arms that render a socket-filler
// description go through RecordSections, which is not ported yet.

const Data = D2DataFiles.load();
const Items = new ItemTable(Data.weapons, Data.armor, Data.misc);
const Types = new ItemTypeTree(Data.itemTypes);
const Gems = Data.gems as TxtFile;

describe('PropertyApplier', () => {
  it('the properties table loads and resolves names', () => {
    const applier = new PropertyApplier(Data, Items, Types);

    // properties.bin carries 268 records.
    expect(applier.properties.rowCount).toBe(268);

    const resAll = applier.properties.rowForCode('res-all');
    expect(resAll).toBeGreaterThanOrEqual(0);

    // "res-all" fans out to the four single resistances, so several sets carry a stat.
    const row = applier.properties.getRow(resAll);
    expect(row).not.toBeNull();
    expect((row as NonNullable<typeof row>).stat.filter(s => s >= 0).length).toBeGreaterThanOrEqual(
      4,
    );
  });

  it('no gem property needs the item seed', () => {
    // A property with min != max would have to be rolled from the item seed. If no gem or
    // rune mod is ranged, the seed is irrelevant to socket-filler descriptions.
    const applier = new PropertyApplier(Data, Items, Types);
    const gems = new GemTable(Gems, Items);
    gems.resolvePropertyCodesWith(code => applier.properties.rowForCode(code));

    const stats = new Map<number, number>();

    for (let row = 0; row < Gems.rowCount; ++row) {
      const classId = Items.classIdForCode(Gems.getString(row, 'code').trim());
      if (classId < 0) {
        continue;
      }

      const item = new ItemIdentity();
      item.classId = classId;

      for (let slot = 0; slot < 3; ++slot) {
        for (const property of gems.properties(row, slot)) {
          if (property.propertyId < 0) {
            break;
          }

          applier.apply(PropertyApplier.PropModeGem, item, property, stats);
        }
      }
    }

    expect([...applier.rolledRanges]).toEqual([]);
  });

  it('no gem property reaches an unimplemented func', () => {
    const applier = new PropertyApplier(Data, Items, Types);
    const gems = new GemTable(Gems, Items);
    gems.resolvePropertyCodesWith(code => applier.properties.rowForCode(code));

    const stats = new Map<number, number>();

    for (let row = 0; row < Gems.rowCount; ++row) {
      const code = Gems.getString(row, 'code').trim();
      const classId = Items.classIdForCode(code);
      if (classId < 0) {
        continue;
      }

      const item = new ItemIdentity();
      item.classId = classId;
      item.code = code;

      for (let slot = 0; slot < 3; ++slot) {
        for (const property of gems.properties(row, slot)) {
          if (property.propertyId < 0) {
            break;
          }

          applier.apply(PropertyApplier.PropModeGem, item, property, stats);
        }
      }
    }

    expect([...applier.unsupportedFunc]).toEqual([]);
  });
});
