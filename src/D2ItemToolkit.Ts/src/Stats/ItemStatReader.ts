import { Int32 } from '../Types.js';
import type { Unit, UnitStat, UnitStatList } from './Unit.js';

/** D2C_StatlistFlags (D2StatList.h). MOO's names, not ours. */
export const ItemStatListFlags = {
  /**
   * The bit GetStatList is asked for when the description engine collects a unit's mods
   * (0x4e6438). A StatListEx header carries STATLIST_EXTENDED instead (0x6257dd), which is how
   * the base array is distinguishable from the chain nodes.
   */
  Magic: 0x40,

  /**
   * Despite the name this does not mean "is a set bonus". It says the node is posted to the
   * pMyStats chain rather than pMyLastList, where it contributes nothing (D2StatList.cpp:1083).
   * D2Common_10574 (#10574) flips the bit and re-posts, so the bit and the chain never disagree —
   * which is why the record stores no separate field for which chain a node was on.
   *
   * Set tiers are the bit's main user rather than its meaning. ItemMods.cpp:2335 creates
   * STATE_ITEMSET1..6 as MAGIC|SET, so a tier starts out not contributing and the bit is cleared
   * once the equipped count reaches it. An EARNED tier is therefore MAGIC-only and
   * indistinguishable by flags from any other item mod: only its stateNo says it is a tier.
   */
  Set: 0x2000,

  /** Marks the StatListEx header carrying the base array. */
  Extended: 0x80000000,
} as const;

/** D2C_States (D2States.h), which is sequential from STATE_NONE = 0. */
export const ItemStatListStates = {
  None: 0,
  ItemSet1: 165,
  ItemSet6: 170,
  Runeword: 171,
} as const;

export class ItemStatReader {
  // A unit document is self-similar: identity fields, `statsLists` and `sockets`,
  // where each socket entry is another unit document and its POSITION is the socket index. An
  // item and a player are both D2UnitStrc, so both serialise to this same shape.
  //
  // There is no per-socket view, and no socket index anywhere: to describe one filler, take
  // [...enumerateSockets(record)][n] and view THAT record with itemOnly(). Self-similarity means
  // the whole reader already works on it.

  // (layer << 16) | stat — LAYER-major, which is the MIRROR of the engine's own packing.
  // D2SLayerStatIdStrc is { uint16 nLayer @0x00; uint16 nStat @0x02 }, so a captured
  // nPackedValue is (stat << 16) | layer. A key from here is NOT comparable with one of those;
  // convert with ((p & 0xFFFF) << 16) | (p >> 16). Layer-major is kept because it sorts by
  // layer, which is the order the description engine consumes entries in.
  static packStatKey(layer: number, stat: number): number {
    return ((layer & 0xffff) << 16) | (stat & 0xffff) | 0;
  }

  static statFromKey(key: number): number {
    return key & 0xffff;
  }

  /**
   * Both halves of a packed key. Not a try- method: every key unpacks, which is why the C# peer
   * returns void rather than a bool that was always true.
   */
  static unpackStatKey(key: number): { layer: number; stat: number } {
    return {
      layer: ItemStatReader.layerFromKey(key),
      stat: ItemStatReader.statFromKey(key),
    };
  }

  static layerFromKey(key: number): number {
    return key >>> 16;
  }

  static reconstructView(record: Unit, view: ItemStatView): Map<number, number> {
    const merged = new Map<number, number>();

    for (const group of ItemStatReader.enumerateGroups(record)) {
      if (view.excludedFlags !== 0 && (group.flags & view.excludedFlags) !== 0) {
        continue;
      }

      if (group.fromSocket && !view.includeSockets) {
        continue;
      }

      if (view.requiredFlags !== 0 && (group.flags & view.requiredFlags) === 0) {
        continue;
      }

      if (view.allowedStates !== null && view.allowedStates.indexOf(group.stateNo) < 0) {
        continue;
      }

      if (view.excludedStates !== null && view.excludedStates.indexOf(group.stateNo) >= 0) {
        continue;
      }

      for (const stat of group.stats) {
        const key = ItemStatReader.packStatKey(stat.layer ?? 0, stat.id);

        const existing = merged.get(key);
        merged.set(key, existing === undefined ? stat.value : Int32.of(existing + stat.value));
      }
    }

    return sortByKey(merged);
  }

  /**
   * This record's own groups followed by its sockets', each tagged with whether it was reached
   * through a socket so a view can drop the fillers.
   */
  static enumerateGroups(record: Unit): IterableIterator<ItemStatGroup> {
    return enumerateGroupsOf(record, false);
  }

  /**
   * The socket records in index order. Position IS the index: the producer sorts by the
   * ordinal INVENTORY_PlaceItemInSocket assigned, which is contiguous from 0.
   */
  static enumerateSockets(record: Unit): readonly Unit[] {
    return record.sockets;
  }

  /** Socket index to the filler's classId, for the writers that only need that. */
  static readSockets(record: Unit): Map<number, number> {
    const sockets = new Map<number, number>();
    let index = 0;
    for (const socket of ItemStatReader.enumerateSockets(record)) {
      // The document's two fallbacks for a missing classId differ: identity wants -1 ("no
      // such row"), this map wants 0. Keep the 0 — a negative would widen to 0xFFFFFFFF.
      sockets.set(index, socket.classId < 0 ? 0 : socket.classId >>> 0);
      ++index;
    }

    return sockets;
  }
}

/** SortedDictionary ordering: signed, so a high-layer key sorts ahead of every layer-0 one. */
export function sortByKey(map: Map<number, number>): Map<number, number> {
  return new Map([...map.entries()].sort((left, right) => left[0] - right[0]));
}

function* enumerateGroupsOf(record: Unit, fromSocket: boolean): IterableIterator<ItemStatGroup> {
  for (const group of record.statsLists) {
    yield new ItemStatGroup(group, fromSocket);
  }

  for (const socket of ItemStatReader.enumerateSockets(record)) {
    for (const group of enumerateGroupsOf(socket, true)) {
      yield group;
    }
  }
}

const ModifierBlockStates: readonly number[] = [
  ItemStatListStates.None,
  ItemStatListStates.Runeword,
];

// Since an earned tier drops STATLIST_SET, stateNo is the ONLY thing that still identifies a set
// tier — a flag test cannot do it.
const SetTierStates: readonly number[] = Array.from(
  { length: ItemStatListStates.ItemSet6 - ItemStatListStates.ItemSet1 + 1 },
  (_unused, offset) => ItemStatListStates.ItemSet1 + offset,
);

// A node the item itself grants, in either sense: STATLIST_EXTENDED is the header carrying
// the base array, STATLIST_MAGIC is every affix / unique / setitems / runeword node.
const ItemOwn = (ItemStatListFlags.Extended | ItemStatListFlags.Magic) >>> 0;

export class ItemStatView {
  /** False drops every group reached through a socket filler. */
  includeSockets = true;

  /** Any of these bits must be present. Zero means "do not filter on flags". */
  requiredFlags = 0;

  /** None of these bits may be present. Zero means "exclude nothing". */
  excludedFlags = 0;

  /** Null means "do not filter on stateNo". */
  allowedStates: readonly number[] | null = null;

  /** Null means "exclude no state". */
  excludedStates: readonly number[] | null = null;

  /**
   * What the blue modifier block is actually built from. SKILLDESC_AppendStatBuffText
   * 0x4e6438 passes mask 0x40 and states 0 and 171, and the temp list receives exactly
   * three kinds of node (0x4e6137 / 0x4e6154 / 0x4e61a0): the item's state-0 node, its
   * runeword node, and one per socket filler.
   *
   * GetStatList 0x6257d0 walks the pMyLastList chain at +0x3C and keeps a node only when
   * its stateNo matches AND `node->dwFlags & mask` is non-zero (0x62580d). The base stat
   * array lives at +0x24, is not in that chain, and carries STATLIST_EXTENDED rather than
   * MAGIC, so base stats can never be described here. Set bonuses DO carry the 0x40 bit —
   * what keeps them out is that they sit on states 165-170 and neither query asks for those.
   */
  static modifiers(): ItemStatView {
    const view = ItemStatView.everything();
    view.requiredFlags = ItemStatListFlags.Magic;
    view.excludedFlags = ItemStatListFlags.Set;
    view.allowedStates = ModifierBlockStates;
    return view;
  }

  /** What the item is worth on its own: its base array and its own mods, no set tiers. */
  static forSale(): ItemStatView {
    const view = ItemStatView.everything();
    view.requiredFlags = ItemOwn;
    view.excludedFlags = ItemStatListFlags.Set;
    view.excludedStates = SetTierStates;
    return view;
  }

  /** What it is currently giving its wearer, so earned set tiers count too. */
  static equipped(): ItemStatView {
    const view = ItemStatView.forSale();
    view.excludedStates = null;
    return view;
  }

  /**
   * The set tiers on the item itself. An unearned tier still carries STATLIST_SET, an earned one
   * has had it cleared, so the flag is exactly the earned/unearned test.
   */
  static setBonuses(includeUnearned: boolean): ItemStatView {
    const view = ItemStatView.everything();
    view.includeSockets = false;
    view.requiredFlags = ItemStatListFlags.Magic;
    view.allowedStates = SetTierStates;
    if (!includeUnearned) {
      view.excludedFlags = ItemStatListFlags.Set;
    }

    return view;
  }

  static itemOnly(): ItemStatView {
    const view = ItemStatView.forSale();
    view.includeSockets = false;
    return view;
  }

  /**
   * The base array on the item itself and nothing else — what SERVER_GetUnitStat reads.
   * INV_CalcWeaponDamageRange compares this against the merged value to decide whether a
   * damage number has been modified (0x485300).
   */
  static baseOnly(): ItemStatView {
    const view = ItemStatView.everything();
    view.includeSockets = false;
    view.requiredFlags = ItemStatListFlags.Extended;
    view.excludedFlags = ItemStatListFlags.Set;
    return view;
  }

  static everything(): ItemStatView {
    const view = new ItemStatView();
    view.includeSockets = true;
    view.requiredFlags = 0;
    view.excludedFlags = 0;
    view.allowedStates = null;
    view.excludedStates = null;
    return view;
  }
}

export class ItemStatGroup {
  private readonly list: UnitStatList;

  readonly stateNo: number;

  readonly flags: number;

  /** True when this group belongs to a socket filler rather than the item itself. */
  readonly fromSocket: boolean;

  constructor(list: UnitStatList, fromSocket: boolean) {
    this.list = list;

    this.stateNo = list.stateNo;
    this.flags = list.flags;
    // Not stored on the group: it comes from which record we reached it through.
    this.fromSocket = fromSocket;
  }

  get stats(): readonly UnitStat[] {
    return this.list.stats;
  }

  *enumerateStats(): IterableIterator<[number, number]> {
    for (const stat of this.list.stats) {
      yield [ItemStatReader.packStatKey(stat.layer ?? 0, stat.id), stat.value];
    }
  }
}
