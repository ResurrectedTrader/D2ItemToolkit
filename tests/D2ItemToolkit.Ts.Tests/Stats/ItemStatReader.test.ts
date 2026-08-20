import { describe, expect, it } from 'vitest';
import {
  ItemStatGroup,
  ItemStatListFlags,
  ItemStatReader,
  ItemStatView,
} from '../../../src/D2ItemToolkit.Ts/src/Stats/ItemStatReader.js';
import { unitFromJson, type Unit } from '../../../src/D2ItemToolkit.Ts/src/Stats/Unit.js';

// A 2 socket set armour with a jewel and a perfect ruby, set not completed.
const SampleRecord = `{
    "statsLists": [
      { "source": "base", "stateNo": 0, "flags": 2147483648,
        "stats": [ { "id": 31, "value": 445 }, { "id": 72, "value": 60 },
                   { "id": 73, "value": 60 }, { "id": 194, "value": 2 } ] },
      { "source": "quality", "stateNo": 0, "flags": 64,
        "stats": [ { "id": 16, "value": 180 }, { "id": 39, "value": 40 } ] },
      { "source": "setBonus", "stateNo": 165, "flags": 8256,
        "stats": [ { "id": 0, "value": 20 } ] },
      { "source": "setBonus", "stateNo": 166, "flags": 8256,
        "stats": [ { "id": 41, "value": 15 } ] }
    ],
    "sockets": [
      { "classId": 620,
        "statsLists": [ { "source": "quality", "stateNo": 0, "flags": 64,
            "stats": [ { "id": 17, "value": 15 },
                       { "id": 97, "layer": 2, "value": 1 } ] } ] },
      { "classId": 604,
        "statsLists": [ { "source": "quality", "stateNo": 0, "flags": 64,
            "stats": [ { "id": 39, "value": 38 } ] } ] }
    ]
}`;

function parse(json: string): Unit {
  return unitFromJson(json);
}

function render(view: ReadonlyMap<number, number>): string {
  return [...view]
    .map(([key, value]) => {
      const layer = ItemStatReader.layerFromKey(key);
      const id = String(ItemStatReader.statFromKey(key));
      return layer !== 0 ? `${id}/${layer}=${value}` : `${id}=${value}`;
    })
    .join(' ');
}

// =================================================================
// Key packing
// =================================================================

describe('key packing', () => {
  it.each([
    [0, 39],
    [2, 97],
    [0xffff, 0xffff],
  ])('a packed key round trips (layer %i, stat %i)', (layer, stat) => {
    const key = ItemStatReader.packStatKey(layer, stat);

    expect(ItemStatReader.statFromKey(key)).toBe(stat);
    expect(ItemStatReader.layerFromKey(key)).toBe(layer);
  });

  it('a high layer packs to a negative key without losing bits', () => {
    // Matches int32_t(uint32_t(layer) << 16 | stat) on the C++ side.
    const key = ItemStatReader.packStatKey(0x8000, 1);

    expect(key < 0).toBe(true);
    expect(ItemStatReader.layerFromKey(key)).toBe(0x8000);
    expect(ItemStatReader.statFromKey(key)).toBe(1);
  });

  it('packing masks off bits above the field widths', () => {
    const key = ItemStatReader.packStatKey(0x1ffff, 0x1ffff);

    expect(ItemStatReader.layerFromKey(key)).toBe(0xffff);
    expect(ItemStatReader.statFromKey(key)).toBe(0xffff);
  });

  it('unpacking reports both halves at once', () => {
    expect(ItemStatReader.unpackStatKey(ItemStatReader.packStatKey(2, 97))).toEqual({
      layer: 2,
      stat: 97,
    });
  });
});

// =================================================================
// The modifier block: GetStatList(item, 0, 0x40), GetStatList(item, 171, 0x40) and
// GetStatList(filler, 0, 0x40) — 0x4e6438 / 0x4e6137 / 0x4e6154 / 0x4e61a0.
// =================================================================

describe('the modifier block', () => {
  it('never sees the base array', () => {
    const view = ItemStatReader.reconstructView(parse(SampleRecord), ItemStatView.modifiers());

    // 31 defense, 72/73 durability and 194 sockets are all base stats. The base array
    // hangs off +0x24 and is not in the +0x3C chain GetStatList walks, so none of them
    // can ever be described — only the quality node and the two fillers survive.
    expect(render(view)).toBe('16=180 17=15 39=78 97/2=1');
  });

  it.each([[31], [72], [73], [194]])('a base stat %i is absent from the modifier block', statId => {
    const view = ItemStatReader.reconstructView(parse(SampleRecord), ItemStatView.modifiers());

    expect(view.has(ItemStatReader.packStatKey(0, statId))).toBe(false);
  });

  it('a set bonus carries the flag but is excluded by its state', () => {
    // Set nodes are flags 0x2040 — they DO have the 0x40 bit. What keeps them out is
    // stateNo: STATE_ITEMSET1..6 are 165-170 and neither query asks for those. Earning a tier
    // clears STATLIST_SET, leaving 0x40, so this holds for both.
    const earned = SampleRecord.split('"flags": 8256').join('"flags": 64');

    const view = ItemStatReader.reconstructView(parse(earned), ItemStatView.modifiers());

    expect(view.has(ItemStatReader.packStatKey(0, 0))).toBe(false);
    expect(view.has(ItemStatReader.packStatKey(0, 41))).toBe(false);
  });

  it('the runeword node is the second query', () => {
    const runeword = SampleRecord.replace(
      `{ "source": "setBonus", "stateNo": 165, "flags": 8256,
        "stats": [ { "id": 0, "value": 20 } ] }`,
      `{ "source": "runeword", "stateNo": 171, "flags": 64,
        "stats": [ { "id": 0, "value": 20 } ] }`,
    );

    const view = ItemStatReader.reconstructView(parse(runeword), ItemStatView.modifiers());

    expect(view.get(ItemStatReader.packStatKey(0, 0))).toBe(20);
  });

  it('a flagged node on an unqueried state is still excluded', () => {
    // Only 0 and 171 are asked for, so the 0x40 bit alone is not enough.
    const other = SampleRecord.replace('"stateNo": 171', '"stateNo": 200').replace(
      `{ "source": "quality", "stateNo": 0, "flags": 64,
        "stats": [ { "id": 16, "value": 180 }, { "id": 39, "value": 40 } ] }`,
      `{ "source": "quality", "stateNo": 200, "flags": 64,
        "stats": [ { "id": 16, "value": 180 }, { "id": 39, "value": 40 } ] }`,
    );

    const view = ItemStatReader.reconstructView(parse(other), ItemStatView.modifiers());

    expect(view.has(ItemStatReader.packStatKey(0, 16))).toBe(false);
  });

  it('socket fillers still reach the modifier block', () => {
    const view = ItemStatReader.reconstructView(parse(SampleRecord), ItemStatView.modifiers());

    // 15 from the jewel, and 40 + 38 for fire resist across item and ruby.
    expect(view.get(ItemStatReader.packStatKey(0, 17))).toBe(15);
    expect(view.get(ItemStatReader.packStatKey(0, 39))).toBe(78);
  });

  it('the section views still carry the base stats', () => {
    // The writers read through SERVER_GetUnitStat, which sees every list — only the
    // modifier block is restricted.
    const sections = ItemStatReader.reconstructView(parse(SampleRecord), ItemStatView.equipped());

    expect(sections.get(ItemStatReader.packStatKey(0, 31))).toBe(445);
  });
});

// =================================================================
// Views
// =================================================================

describe('views', () => {
  it('ForSale sums the item and its sockets and omits set bonuses', () => {
    const view = ItemStatReader.reconstructView(parse(SampleRecord), ItemStatView.forSale());

    // Fire resist is 40 on the item plus 38 from the ruby.
    expect(render(view)).toBe('16=180 17=15 31=445 39=78 72=60 73=60 194=2 97/2=1');
  });

  it('Equipped matches ForSale while every set tier is unearned', () => {
    expect(
      render(ItemStatReader.reconstructView(parse(SampleRecord), ItemStatView.equipped())),
    ).toBe(render(ItemStatReader.reconstructView(parse(SampleRecord), ItemStatView.forSale())));
  });

  it('Equipped includes a set bonus once it is earned', () => {
    // Earning a tier clears STATLIST_SET, so 0x2040 becomes 0x40.
    const earned = SampleRecord.split('"flags": 8256').join('"flags": 64');

    const view = ItemStatReader.reconstructView(parse(earned), ItemStatView.equipped());

    expect(view.get(ItemStatReader.packStatKey(0, 0))).toBe(20);
    expect(view.get(ItemStatReader.packStatKey(0, 41))).toBe(15);
  });

  it('ItemOnly drops the socket contributions', () => {
    const view = ItemStatReader.reconstructView(parse(SampleRecord), ItemStatView.itemOnly());

    expect(render(view)).toBe('16=180 31=445 39=40 72=60 73=60 194=2');
  });

  it('SetBonuses excludes unearned tiers by default', () => {
    expect(
      ItemStatReader.reconstructView(parse(SampleRecord), ItemStatView.setBonuses(false)).size,
    ).toBe(0);
  });

  it('SetBonuses can include unearned tiers', () => {
    const view = ItemStatReader.reconstructView(parse(SampleRecord), ItemStatView.setBonuses(true));

    expect(render(view)).toBe('0=20 41=15');
  });

  it.each<[number, string]>([
    [0, '17=15 97/2=1'],
    [1, '39=38'],
  ])('filler %i describes from its own record', (socket, expected) => {
    // No per-socket view exists: a filler is a record of the same shape, so the reader already
    // works on it directly.
    const filler = socketAt(parse(SampleRecord), socket);

    expect(render(ItemStatReader.reconstructView(filler, ItemStatView.itemOnly()))).toBe(expected);
  });

  it('Everything round trips the whole record', () => {
    const view = ItemStatReader.reconstructView(parse(SampleRecord), ItemStatView.everything());

    expect(render(view)).toBe('0=20 16=180 17=15 31=445 39=78 41=15 72=60 73=60 194=2 97/2=1');
  });

  it('an unknown source is only included by a mask that asks for Other', () => {
    const json = `{"statsLists":[
        {"source":"charm","stateNo":0,"flags":0,"stats":[{"id":1,"value":5}]}]}`;

    expect(ItemStatReader.reconstructView(parse(json), ItemStatView.forSale()).size).toBe(0);
    expect(ItemStatReader.reconstructView(parse(json), ItemStatView.everything()).size).toBe(1);
  });

  it('a record with no groups array yields nothing', () => {
    expect([...ItemStatReader.enumerateGroups(parse('{}'))]).toHaveLength(0);
  });
});

// =================================================================
// Sockets
// =================================================================

describe('sockets', () => {
  it('the socket table maps ordinals to class ids', () => {
    const sockets = ItemStatReader.readSockets(parse(SampleRecord));

    expect([...sockets.keys()]).toEqual([0, 1]);
    expect(sockets.get(0)).toBe(620);
    expect(sockets.get(1)).toBe(604);
  });

  it('a record with no sockets array yields an empty table', () => {
    expect(ItemStatReader.readSockets(parse('{"statsLists":[]}')).size).toBe(0);
  });
});

// =================================================================
// Group projection
// =================================================================

describe('group projection', () => {
  it('a group exposes its raw provenance', () => {
    const groups = [...ItemStatReader.enumerateGroups(parse(SampleRecord))];

    expect(groups).toHaveLength(6);

    expect(groups[0]?.flags).toBe(0x80000000);
    expect(groups[0]?.fromSocket).toBe(false);

    expect(groups[2]?.stateNo).toBe(165);
    expect((groups[2]?.flags ?? 0) & ItemStatListFlags.Set).toBe(ItemStatListFlags.Set);

    expect(groups[4]?.fromSocket).toBe(true);
  });

  it('a group enumerates its stats with layers intact', () => {
    const socketGroup = [...ItemStatReader.enumerateGroups(parse(SampleRecord))].find(
      g => g.fromSocket,
    ) as ItemStatGroup;

    const stats = [...socketGroup.enumerateStats()];

    expect(stats).toHaveLength(2);
    expect(stats[0]?.[0]).toBe(ItemStatReader.packStatKey(0, 17));
    expect(stats[1]?.[0]).toBe(ItemStatReader.packStatKey(2, 97));
    expect(stats[1]?.[1]).toBe(1);
  });

  it('a group with no source property reads as Other', () => {
    const json = '{"statsLists":[{"stats":[{"id":1,"value":5}]}]}';

    expect(single(json).flags).toBe(0);
  });

  it('a group with no stats array enumerates nothing', () => {
    const json = '{"statsLists":[{"source":"base"}]}';

    const group = single(json);

    // An absent `stats` array parses to an empty list rather than a missing one, so "no array"
    // and "empty array" are the same thing to a consumer — which is what the enumeration already
    // treated them as.
    expect(group.stats).toHaveLength(0);
    expect([...group.enumerateStats()]).toHaveLength(0);
  });

  it('a group whose stats are not an array enumerates nothing', () => {
    const json = '{"statsLists":[{"source":"base","stats":42}]}';

    expect([...single(json).enumerateStats()]).toHaveLength(0);
  });
});

// =================================================================
// Scalar readers
// =================================================================

describe('scalar readers', () => {
  it('a non numeric field falls back rather than throwing', () => {
    const json = `{"statsLists":[
        {"source":"base","stateNo":"nope","flags":"nope","stats":[]}]}`;

    const group = single(json);

    expect(group.stateNo).toBe(0);
    expect(group.flags).toBe(0);
  });

  it('a number too large for the field falls back', () => {
    // 3000000000 exceeds int32, 5000000000 exceeds uint32.
    const json = `{"statsLists":[
        {"source":"base","stateNo":3000000000,"flags":5000000000,"stats":[]}]}`;

    const group = single(json);

    expect(group.stateNo).toBe(0);
    expect(group.flags).toBe(0);
  });

  it.each<[string, boolean]>([
    ['"flags": 8256', true],
    ['"flags": 64', false],
    ['"other": 1', false], // absent: falls back to 0
  ])('whether a node contributes comes from the flag alone (%s)', (fragment, onMyStats) => {
    const json = `{"statsLists":[{${fragment},"stats":[]}]}`;

    expect((single(json).flags & ItemStatListFlags.Set) !== 0).toBe(onMyStats);
  });

  it('a stat with no value or id reads as zero', () => {
    const json = '{"statsLists":[{"source":"base","stats":[{}]}]}';

    const stats = [...single(json).enumerateStats()];

    expect(stats).toHaveLength(1);
    expect(stats[0]?.[0]).toBe(0);
    expect(stats[0]?.[1]).toBe(0);
  });
});

// =================================================================
// Classification is derived from dwFlags — the record carries none.
// =================================================================

function single(json: string): ItemStatGroup {
  const groups = [...ItemStatReader.enumerateGroups(parse(json))];
  expect(groups).toHaveLength(1);
  return groups[0] as ItemStatGroup;
}

function groupFrom(groupJson: string): ItemStatGroup {
  return single(`{"statsLists":[${groupJson}]}`);
}

function viewOf(groupsJson: string, view: ItemStatView): Map<number, number> {
  return ItemStatReader.reconstructView(parse(`{"statsLists":[${groupsJson}]}`), view);
}

const OneStat = '"stats":[{"id":39,"value":1}]';

describe('classification', () => {
  it('the base array is the extended node whatever its state', () => {
    // STATLIST_EXTENDED marks the StatListEx header. State is irrelevant to that.
    const json = `{"stateNo":165,"flags":2147483648,${OneStat}}`;

    expect(viewOf(json, ItemStatView.baseOnly()).size).toBe(1);
    expect(viewOf(json, ItemStatView.setBonuses(true)).size).toBe(0);
  });

  it('an unearned tier reaches no item view but an earned one reaches Equipped', () => {
    // 0x2040 = STATLIST_SET | STATLIST_MAGIC: still on pMyStats, contributing nothing. Earning
    // it clears STATLIST_SET, leaving a node that flags alone cannot tell apart from any other
    // item mod — only stateNo still says it is a set tier.
    const unearned = `{"stateNo":165,"flags":8256,${OneStat}}`;
    const earned = `{"stateNo":165,"flags":64,${OneStat}}`;

    expect(viewOf(unearned, ItemStatView.setBonuses(true)).size).toBe(1);
    expect(viewOf(unearned, ItemStatView.setBonuses(false)).size).toBe(0);
    expect(viewOf(unearned, ItemStatView.forSale()).size).toBe(0);
    expect(viewOf(unearned, ItemStatView.equipped()).size).toBe(0);

    expect(viewOf(earned, ItemStatView.setBonuses(false)).size).toBe(1);
    expect(viewOf(earned, ItemStatView.forSale()).size).toBe(0); // excluded by its state
    expect(viewOf(earned, ItemStatView.equipped()).size).toBe(1); // it IS contributing
  });

  it('a runeword node is indistinguishable from a quality node by flags', () => {
    // Both are STATLIST_MAGIC; only dwStateNo separates them, and nothing here needs to.
    const quality = `{"stateNo":0,"flags":64,${OneStat}}`;
    const runeword = `{"stateNo":171,"flags":64,${OneStat}}`;

    expect(render(viewOf(runeword, ItemStatView.forSale()))).toBe(
      render(viewOf(quality, ItemStatView.forSale())),
    );
  });

  it('a node with no recognised bit is excluded from the item views', () => {
    const json = `{"stateNo":200,"flags":8,${OneStat}}`; // STATLIST_BUFF

    expect(viewOf(json, ItemStatView.forSale()).size).toBe(0);
    expect(viewOf(json, ItemStatView.equipped()).size).toBe(0);
    expect(viewOf(json, ItemStatView.baseOnly()).size).toBe(0);

    // Everything filters on nothing, so it still sees it.
    expect(viewOf(json, ItemStatView.everything()).size).toBe(1);
  });

  it('a group exposes the raw struct fields only', () => {
    const group = groupFrom('{"stateNo":171,"flags":64,"stats":[]}');

    expect(group.stateNo).toBe(171);
    expect(group.flags).toBe(64);
    expect(group.fromSocket).toBe(false);
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
