import { describe, expect, it } from 'vitest';
import { unitFromJson, type Unit } from '../../src/D2ItemToolkit.Ts/src/Stats/Unit.js';
import {
  ItemDescriptionGenerator,
  ItemDescFunc,
} from '../../src/D2ItemToolkit.Ts/src/Description/ItemDescription.js';
import {
  ItemStatReader,
  ItemStatView,
} from '../../src/D2ItemToolkit.Ts/src/Stats/ItemStatReader.js';
import { Build, FakeSkillTable, FakeStatCostTable, FakeStringTable } from './Fakes.js';

/**
 * Reader to description, end to end: a stored record becomes a view, and the view becomes tooltip
 * lines. Ported from EndToEndTests.cs, which was the one C# test file with no TypeScript
 * counterpart at all — it is the only place the ItemStatReader → ItemDescriptionGenerator seam is
 * exercised, and no other test combines those two classes.
 *
 * The stat-to-DescFunc mapping below is an illustration, not a transcription of vanilla
 * itemstatcost.txt. Real data comes from the IItemStatCostTable implementation you plug in.
 */

const Record = unitFromJson(`{
  "statsLists": [
    { "stateNo": 0, "flags": 2147483648,
      "stats": [ { "id": 31, "value": 445 }, { "id": 72, "value": 60 },
                 { "id": 73, "value": 60 }, { "id": 194, "value": 2 } ] },
    { "stateNo": 0, "flags": 64,
      "stats": [ { "id": 16, "value": 180 }, { "id": 39, "value": 40 } ] },
    { "stateNo": 165, "flags": 8256,
      "stats": [ { "id": 0, "value": 20 } ] }
  ],
  "sockets": [
    { "classId": 620,
      "statsLists": [ { "stateNo": 0, "flags": 64,
        "stats": [ { "id": 17, "value": 15 }, { "id": 97, "layer": 2, "value": 1 } ] } ] },
    { "classId": 604,
      "statsLists": [ { "stateNo": 0, "flags": 64,
        "stats": [ { "id": 39, "value": 38 } ] } ] }
  ]
}`);

function buildGenerator(): ItemDescriptionGenerator {
  const stats = new FakeStatCostTable();

  // Ordered high priority first, as IItemStatCostTable requires.
  stats.add(Build.stat(97, ItemDescFunc.Skill, 300, { priority: 120 }));
  stats.add(Build.stat(17, ItemDescFunc.PlusValuePercentString, 301, { priority: 110 }));
  stats.add(Build.stat(16, ItemDescFunc.PlusValuePercentString, 302, { priority: 100 }));
  stats.add(Build.stat(39, ItemDescFunc.PlusValueString, 303, { descVal: 2, priority: 50 }));
  stats.add(Build.stat(0, ItemDescFunc.PlusValueString, 304, { priority: 40 }));

  // Printed by dedicated client code ahead of the DescFunc loop, so DescFunc 0.
  stats.add(Build.stat(31, 0, 0));
  stats.add(Build.stat(72, 0, 0));
  stats.add(Build.stat(73, 0, 0));
  stats.add(Build.stat(194, 0, 0));

  const strings = new FakeStringTable()
    .withPunctuation()
    .add(301, 'Enhanced Damage')
    .add(302, 'Enhanced Defense')
    .add(303, 'Fire Resist')
    .add(304, 'to Strength');

  const skills = new FakeSkillTable().add(2, 'Charged Bolt');

  return new ItemDescriptionGenerator(stats, strings, null, skills);
}

function describeView(view: ItemStatView): string[] {
  const merged = ItemStatReader.reconstructView(Record, view);
  return buildGenerator()
    .describe(merged)
    .map(line => line.text);
}

describe('reader to description', () => {
  it('describes an item for sale with its own mods and its sockets', () => {
    expect(describeView(ItemStatView.forSale())).toEqual([
      '+1 to Charged Bolt', // socket 0, a jewel
      '+15% Enhanced Damage', // socket 0, same jewel
      '+180% Enhanced Defense', // the item's own affix
      'Fire Resist +78', // 40 on the item plus 38 from the ruby
    ]);
  });

  it('leaves an unearned set bonus out of the for-sale description', () => {
    expect(describeView(ItemStatView.forSale()).join('\n')).not.toContain('to Strength');
  });

  it('describes the set bonus separately for an equipped view', () => {
    expect(describeView(ItemStatView.setBonuses(true))).toEqual(['+20 to Strength']);
  });

  it('leaves out stats the client prints itself', () => {
    // Defence, durability and sockets are present in the view but carry no DescFunc, so they
    // never reach the tooltip through this path. Without this the tooltip prints them twice.
    const merged = ItemStatReader.reconstructView(Record, ItemStatView.forSale());
    const lines = buildGenerator().describe(merged);
    const statIds = lines.map(line => line.statId);

    expect([...merged.keys()]).toContain(ItemStatReader.packStatKey(0, 31));
    expect(statIds).not.toContain(31);
    expect(statIds).not.toContain(194);
  });

  it('describes a socket on its own', () => {
    // A filler is a record of the same shape, so it describes through the same reader.
    const filler = socketAt(Record, 1);
    const merged = ItemStatReader.reconstructView(filler, ItemStatView.itemOnly());

    expect(
      buildGenerator()
        .describe(merged)
        .map(line => line.text),
    ).toEqual(['Fire Resist +38']);
  });
});

/** Indexing is `Unit | undefined` under noUncheckedIndexedAccess; a missing filler is a
 * broken fixture, so say so rather than letting an empty record render a plausible nothing. */
function socketAt(record: Unit, index: number): Unit {
  const filler = ItemStatReader.enumerateSockets(record)[index];
  if (filler === undefined) {
    throw new Error(`the fixture has no socket ${String(index)}`);
  }

  return filler;
}
