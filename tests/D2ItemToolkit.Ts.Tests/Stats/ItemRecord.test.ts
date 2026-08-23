import { describe, expect, it } from 'vitest';
import {
  ItemRecordFlags,
  ItemRecordReader,
} from '../../../src/D2ItemToolkit.Ts/src/Stats/ItemRecord.js';
import {
  ItemStatReader,
  ItemStatView,
} from '../../../src/D2ItemToolkit.Ts/src/Stats/ItemStatReader.js';
import { unitFromJson, type Unit } from '../../../src/D2ItemToolkit.Ts/src/Stats/Unit.js';

// SkillDamage.HolyShieldSkillId / HolyShieldState, spelled out here because that port belongs
// to another slice.
const HolyShieldSkillId = 117;
const HolyShieldState = 101;

const Item = `{
    "unitType": 4,
    "classId": 511, "code": "rin", "quality": 7, "itemFlags": 4194320,
    "fileIndex": 25, "rarePrefix": 0, "rareSuffix": 0, "autoAffix": 0,
    "magicPrefix": [ 12, 0, 0 ], "magicSuffix": [ 34, 0, 0 ],
    "earLevel": 0, "playerName": "Bob",
    "statsLists": [
      { "stateNo": 0, "flags": 2147483648,
        "stats": [ { "id": 31, "value": 445 } ] }
    ]
}`;

const Player = `{
    "unitType": 0, "classId": 1, "skills": [ { "skill": 117, "level": 12 } ],
    "statsLists": [
      { "stateNo": 0, "flags": 2147483648,
        "stats": [ { "id": 12, "value": 42 }, { "id": 0, "value": 88 },
                    { "id": 2, "value": 55 } ] },
      { "stateNo": 101, "flags": 64,
        "stats": [ { "id": 20, "value": 30 } ] }
    ]
}`;

function parse(json: string): Unit {
  return unitFromJson(json);
}

describe('ItemRecordReader', () => {
  it('the item object round trips', () => {
    const item = ItemRecordReader.readIdentity(parse(Item));

    expect(item.classId).toBe(511);
    expect(item.code).toBe('rin');
    expect(item.quality).toBe(7);
    expect(item.fileIndex).toBe(25);
    expect(item.magicPrefix).toEqual([12, 0, 0]);
    expect(item.magicSuffix).toEqual([34, 0, 0]);
    expect(item.playerName).toBe('Bob');
  });

  it('reads a minus-one fileIndex as the unsigned dword the producer writes', () => {
    // dwFileIndex is a DWORD. "No row" is -1, which nlohmann emits as 4294967295. The C# peer is
    // tests/D2ItemToolkit.Net.Tests/Stats/ProducerShapeTests.cs; it needed a narrowing converter to
    // accept this at all, and the adversarial corpus is what found the disagreement.
    const wide = unitFromJson({
      unitType: 4,
      classId: 330,
      quality: 7,
      itemFlags: 16,
      fileIndex: 4294967295,
      statsLists: [],
    });

    expect(wide.fileIndex).toBe(-1);

    const signed = unitFromJson({ unitType: 4, classId: 330, fileIndex: -1 });
    expect(signed.fileIndex).toBe(wide.fileIndex);
  });

  it('item flags decode to the engines bits', () => {
    const item = ItemRecordReader.readIdentity(parse(Item));

    // 4194320 = 0x400010 = ETHEREAL | IDENTIFIED.
    expect(item.has(ItemRecordFlags.Identified)).toBe(true);
    expect(item.has(ItemRecordFlags.Ethereal)).toBe(true);
    expect(item.has(ItemRecordFlags.Socketed)).toBe(false);
    expect(item.has(ItemRecordFlags.Runeword)).toBe(false);
    expect(item.has(ItemRecordFlags.Personalized)).toBe(false);
  });

  it('a monster viewer is not a player', () => {
    const record = parse('{ "unitType": 1, "classId": 3, "statsLists": [] }');

    const viewer = ItemRecordReader.readViewer(record);

    // Class id 3 is Paladin for a player, but this is a monster — the Smite gate must not
    // fire on it, which is the bug LoadItemDesc has at 0x48e75c.
    expect(viewer.classId).toBe(3);
    expect(viewer.isPlayer).toBe(false);
  });

  it('the viewer derives its attributes from its own stat lists', () => {
    // Level, strength and dexterity are stats 12, 0 and 2 — no special fields.
    const viewer = ItemRecordReader.readViewer(parse(Player));

    expect(viewer.isPlayer).toBe(true);
    expect(viewer.classId).toBe(1);
    expect(viewer.level).toBe(42);
    expect(viewer.strength).toBe(88);
    expect(viewer.dexterity).toBe(55);
  });

  it('holy shield being up is derived from the state on a stat list', () => {
    // A state IS a stat list carrying its dwStateNo; 101 is Holy Shield's.
    expect(ItemRecordReader.readViewer(parse(Player)).activeStates.has(HolyShieldState)).toBe(true);
    expect(ItemRecordReader.readViewer(parse(Player)).skillLevel(HolyShieldSkillId)).toBe(12);

    const noState = parse(
      '{ "unitType": 0, "classId": 3, "skills": [ { "skill": 117, "level": 12 } ], "statsLists": [] }',
    );
    expect(ItemRecordReader.readViewer(noState).activeStates.has(HolyShieldState)).toBe(false);
  });

  it('the stat lists read off the flattened document', () => {
    const stats = ItemStatReader.reconstructView(parse(Item), ItemStatView.itemOnly());

    expect(stats.size).toBe(1);
    expect(stats.get(ItemStatReader.packStatKey(0, 31))).toBe(445);
  });
});

// =================================================================
// A record is self-similar: a socket entry is another record, and its POSITION in the array is
// the socket index.
// =================================================================

const Nested = `{
    "classId": 100, "quality": 2, "itemFlags": 16,
    "statsLists": [
      { "stateNo": 0, "flags": 2147483648,
        "stats": [ { "id": 31, "value": 100 } ] }
    ],
    "items": [
      { "classId": 620, "quality": 2,
        "statsLists": [ { "source": "quality", "stateNo": 0, "flags": 64,
            "stats": [ { "id": 39, "value": 10 } ] } ] },
      { "classId": 604, "quality": 2,
        "statsLists": [ { "source": "quality", "stateNo": 0, "flags": 64,
            "stats": [ { "id": 39, "value": 20 } ] } ] }
    ]
}`;

function root(): Unit {
  return parse(Nested);
}

describe('nested sockets', () => {
  it('array position is the socket index', () => {
    const sockets = ItemStatReader.readSockets(root());

    expect([...sockets.keys()]).toEqual([0, 1]);
    expect(sockets.get(0)).toBe(620);
    expect(sockets.get(1)).toBe(604);
  });

  it('a socket entry is a record of the same shape', () => {
    // The same reader works on a socket as on the root, which is the point of the fold.
    const filler = socketAt(root(), 0);

    const identity = ItemRecordReader.readIdentity(filler);
    expect(identity).not.toBeNull();
    expect(identity.classId).toBe(620);

    expect([...ItemStatReader.enumerateGroups(filler)]).toHaveLength(1);
  });

  it('the equipped view folds every socket in', () => {
    const equipped = ItemStatReader.reconstructView(root(), ItemStatView.equipped());

    // 10 + 20 from the two fillers.
    expect(equipped.get(ItemStatReader.packStatKey(0, 39))).toBe(30);
    expect(equipped.get(ItemStatReader.packStatKey(0, 31))).toBe(100);
  });

  it('the item only view excludes every socket', () => {
    const itemOnly = ItemStatReader.reconstructView(root(), ItemStatView.itemOnly());

    expect(itemOnly.has(ItemStatReader.packStatKey(0, 39))).toBe(false);
    expect(itemOnly.get(ItemStatReader.packStatKey(0, 31))).toBe(100);
  });

  it.each<[number, number]>([
    [0, 10],
    [1, 20],
  ])('the filler at position %i describes from its own record', (socket, expected) => {
    // Position in `sockets` IS the socket index, so a per-socket view is unnecessary: take the
    // entry and run the reader on it.
    const filler = socketAt(root(), socket);

    const view = ItemStatReader.reconstructView(filler, ItemStatView.itemOnly());

    expect(view.get(ItemStatReader.packStatKey(0, 39))).toBe(expected);
    expect(view.has(ItemStatReader.packStatKey(0, 31))).toBe(false);
  });

  it('a group records whether it came through a socket', () => {
    const groups = [...ItemStatReader.enumerateGroups(root())];

    expect(groups).toHaveLength(3);
    expect(groups[0]?.fromSocket).toBe(false);
    expect(groups[1]?.fromSocket).toBe(true);
    expect(groups[2]?.fromSocket).toBe(true);
  });

  it('an item with no sockets omits the array entirely', () => {
    const bare = parse('{ "classId": 1, "statsLists": [] }');

    expect([...bare.items]).toHaveLength(0);
    expect(ItemStatReader.readSockets(bare).size).toBe(0);
  });

  it('the socket units read as whole units, recursively', () => {
    const units = ItemRecordReader.readSocketUnits(root());

    expect(units).toHaveLength(2);
    expect(units[0]?.identity.classId).toBe(620);
    expect(units[0]?.stats.get(ItemStatReader.packStatKey(0, 39))).toBe(10);
    expect(units[1]?.items).toHaveLength(0);
  });
});

/** Indexing is `Unit | undefined` under noUncheckedIndexedAccess; a missing filler is a
 * broken fixture, so say so rather than letting an empty record render a plausible nothing. */
function socketAt(record: Unit, index: number): Unit {
  const filler = record.items[index];
  if (filler === undefined) {
    throw new Error(`the fixture has no socket ${String(index)}`);
  }

  return filler;
}
