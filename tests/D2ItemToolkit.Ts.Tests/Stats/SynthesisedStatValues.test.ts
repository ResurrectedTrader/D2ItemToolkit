import { describe as suite, expect, it } from 'vitest';
import { ItemIdentity, ItemViewer } from '../../../src/D2ItemToolkit.Ts/src/Stats/ItemRecord.js';
import { ItemStatReader } from '../../../src/D2ItemToolkit.Ts/src/Stats/ItemStatReader.js';
import { ItemTable } from '../../../src/D2ItemToolkit.Ts/src/Tables/ItemTable.js';
import { ItemTypeTree } from '../../../src/D2ItemToolkit.Ts/src/Tables/ItemTypeTree.js';
import { SynthesisedStatValues } from '../../../src/D2ItemToolkit.Ts/src/Stats/SynthesisedStatValues.js';
import { D2DataFiles } from '../../../src/D2ItemToolkit.Ts/src/Tables/TxtDataProviders.js';
import { UndeadDamageLine } from '../../../src/D2ItemToolkit.Ts/src/Tooltip/ItemDamageLines.js';

const Data = D2DataFiles.load();
const Items = new ItemTable(Data.weapons, Data.armor, Data.misc);
const Types = new ItemTypeTree(Data.itemTypes);

function stats(pairs: readonly (readonly [number, number, number])[]): Map<number, number> {
  return new Map(
    pairs.map(([layer, stat, value]) => [ItemStatReader.packStatKey(layer, stat), value]),
  );
}

function identity(code: string): ItemIdentity {
  const item = new ItemIdentity();
  item.classId = Items.classIdForCode(code);
  item.code = code;
  return item;
}

suite('SynthesisedStatValues', () => {
  it('keeps the describe scope and the unit scope separate', () => {
    // GetBaseStatValue is the temp list (the damage aggregate and the 23/24 suppression
    // read it); GetItemStatValue is the unit, which is what the never-breaks gate and
    // GetTxtMaxDurability 0x625e00 ask. Feeding one dictionary to both over-describes.
    const describe = stats([[0, 39, 25]]);

    const unit = stats([
      [0, 39, 25],
      [0, 73, 62],
    ]);

    const values = new SynthesisedStatValues(describe, null, null, null, null, unit);

    expect(values.getBaseStatValue(73, 0)).toBe(0);
    expect(values.getItemStatValue(73)).toBe(62);
    expect(values.getTxtMaxDurability()).toBe(62);
    expect(values.getBaseStatValue(39, 0)).toBe(25);
  });

  it('serves both scopes from one dictionary when no unit set is given', () => {
    const only = stats([[0, 73, 62]]);

    const values = new SynthesisedStatValues(only, null, null, null, null);

    expect(values.getBaseStatValue(73, 0)).toBe(62);
    expect(values.getItemStatValue(73)).toBe(62);
  });

  it('reads the describe scope at the layer it is asked for', () => {
    const values = new SynthesisedStatValues(
      stats([
        [0, 107, 1],
        [2, 107, 3],
      ]),
      null,
      null,
      null,
      null,
    );

    expect(values.getBaseStatValue(107, 0)).toBe(1);
    expect(values.getBaseStatValue(107, 2)).toBe(3);
    expect(values.getBaseStatValue(107, 1)).toBe(0);
  });

  it('tolerates a null stat set', () => {
    const values = new SynthesisedStatValues(null, null, null, null, null);

    expect(values.getBaseStatValue(39, 0)).toBe(0);
    expect(values.getItemStatValue(39)).toBe(0);
  });

  it('scales op 2-5 stats from the VIEWER, not the item', () => {
    // SKILLDESC_CalcStatGroupValue 0x4e4c50 calls GetStatUnsignedValue(GetPlayerUnit(),
    // opBase, 0), and GetPlayerUnit 0x463dd0 returns the local client player.
    const viewer = new ItemViewer();
    viewer.classId = 1;
    viewer.stats.set(ItemStatReader.packStatKey(0, 12), 40);

    const values = new SynthesisedStatValues(stats([[0, 12, 99]]), null, viewer, null, null);

    expect(values.getPlayerStatValue(12)).toBe(40);
    expect(values.playerClass).toBe(1);
  });

  it('reports no viewer as class -1 and every player stat as zero', () => {
    // GetStatUnsignedValue 0x625483 returns 0 for a null unit rather than halting, so
    // the line is still emitted with a zero value.
    const values = new SynthesisedStatValues(stats([]), null, null, null, null);

    expect(values.getPlayerStatValue(12)).toBe(0);
    expect(values.playerClass).toBe(-1);
  });

  it('is always an item', () => {
    expect(new SynthesisedStatValues(null, null, null, null, null).describedUnitIsItem).toBe(true);
  });

  it('probes both items.txt type codes for IsOfType', () => {
    // A war hammer is a Hammer, which sits under Blunt (57) through the closure matrix.
    const hammer = new SynthesisedStatValues(null, identity('whm'), null, Items, Types);
    expect(hammer.isItemOfType(UndeadDamageLine.BluntItemType)).toBe(true);

    // A short sword is not.
    const sword = new SynthesisedStatValues(null, identity('ssd'), null, Items, Types);
    expect(sword.isItemOfType(UndeadDamageLine.BluntItemType)).toBe(false);
  });

  it('cannot answer IsOfType without the tables', () => {
    expect(
      new SynthesisedStatValues(null, identity('whm'), null, null, Types).isItemOfType(
        UndeadDamageLine.BluntItemType,
      ),
    ).toBe(false);
    expect(
      new SynthesisedStatValues(null, identity('whm'), null, Items, null).isItemOfType(
        UndeadDamageLine.BluntItemType,
      ),
    ).toBe(false);
    expect(
      new SynthesisedStatValues(null, null, null, Items, Types).isItemOfType(
        UndeadDamageLine.BluntItemType,
      ),
    ).toBe(false);
  });

  it('allows durability only when the table has one and does not forbid it', () => {
    const hammer = new SynthesisedStatValues(null, identity('whm'), null, Items, Types);
    expect(hammer.itemTableAllowsDurability).toBe(true);

    // A ring carries no durability column value at all.
    const ring = new SynthesisedStatValues(null, identity('rin'), null, Items, Types);
    expect(ring.itemTableAllowsDurability).toBe(false);
  });

  it('cannot answer the durability gate without the item table', () => {
    expect(
      new SynthesisedStatValues(null, identity('whm'), null, null, Types).itemTableAllowsDurability,
    ).toBe(false);
    expect(
      new SynthesisedStatValues(null, null, null, Items, Types).itemTableAllowsDurability,
    ).toBe(false);
  });

  it('reads max durability off the unit scope, not the describe scope', () => {
    // Despite the name, GetTxtMaxDurability 0x625e00 reads the item's STAT 73.
    const values = new SynthesisedStatValues(
      stats([[0, 73, 11]]),
      null,
      null,
      null,
      null,
      stats([[0, 73, 62]]),
    );

    expect(values.getTxtMaxDurability()).toBe(62);
  });
});
