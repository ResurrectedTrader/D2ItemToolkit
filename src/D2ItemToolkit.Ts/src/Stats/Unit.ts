import { ItemRecordFlags } from './ItemRecord.js';

export const MaxAffixSlots = 3;

/** UNITFLAGEX_ISEXPANSION. */
export const UnitFlagExpansion = 0x02000000;

/**
 * One stat. The value is RAW — pre nValShift, pre op resolution — which is what makes a capture
 * stable across wearers. Shift and resolve at display time.
 */
export interface UnitStat {
  id: number;
  value: number;
  /** Omitted from the document when zero. */
  layer?: number;
}

/**
 * One statlist node. `flags` and `stateNo` are copied verbatim and already say which chain the
 * node was on, which is why neither is interpreted here.
 */
export interface UnitStatList {
  /** dwStateNo. 165-170 are the set tiers, 171 a runeword. */
  stateNo: number;
  /** dwFlags. See ItemStatListFlags. */
  flags: number;
  stats: UnitStat[];
}

export interface UnitSkill {
  skill: number;
  level: number;
}

/**
 * A captured D2UnitStrc. An item and a player are the same struct in the game, so both
 * deserialise to this one type; a socket filler is another whole unit, and its POSITION in
 * `sockets` is the socket index.
 *
 * This mirrors the producer's document field for field. It carries no classification: the engine
 * derives base / item-mod / set-tier from `flags` and `stateNo`, and a `source` field here would
 * invite filtering on something the engine does not filter on.
 */
export interface Unit {
  /** 4 is UNIT_ITEM; 0 is a player. */
  unitType: number;
  classId: number;
  /** items.txt szCode. Present so a consumer can validate its own table ordering. */
  code: string;
  /** dwQualityNo: 1 inferior … 9 tempered. */
  quality: number;
  itemFlags: ItemRecordFlags;
  /**
   * dwFileIndex, overloaded by quality: a lowqualityitems row, a UniqueItems row (or -1), a
   * SetItems row, a monstats row for a body part, or a character class for an ear.
   */
  fileIndex: number;
  rarePrefix: number;
  rareSuffix: number;
  autoAffix: number;
  /** wItemFormat (+0x30). 0 is a classic item. */
  format: number;
  /**
   * 1-based indices into the CONCATENATED `[magicsuffix][magicprefix][automagic]` arrays — so an
   * index past the suffix rows lands in the prefix table. On a runeword `magicPrefix[0]` is not an
   * affix index at all but a locale string id from runes.txt (0x639c63).
   */
  magicPrefix: readonly number[];
  magicSuffix: readonly number[];
  earLevel: number;
  playerName: string;
  /**
   * bInvGfxIdx — which of the random inventory graphics this instance rolled, 0-based. Only
   * meaningful for item types with a non-zero itemtypes.txt VarInvGfx (rings, amulets, jewels,
   * charms), where the sprite is `code` + the 1-based index: rin1..rin5. Nothing else in the
   * document implies it, so resolving the graphic requires it.
   */
  gfxIndex: number;
  /**
   * dwFlagEx, on a viewer. Defaults to having UNITFLAGEX_ISEXPANSION because an expansion
   * character is the normal case, and a missing flag would otherwise silently hide a classic
   * unique's level requirement (0x62b877).
   */
  flagsEx: number;
  /** Both statlist chains, flattened. */
  statsLists: UnitStatList[];

  /**
   * A WEARER's already-merged stat values — what GetStat reads off FullStats, so they carry the
   * gear contributions the raw chain does not. Empty on an item, and empty on a viewer whose
   * capture did not supply them.
   *
   * These are the values requirement checks compare against. `statsLists` on a wearer is the
   * STRUCTURAL chain: it says which states are active, but its attribute values are pre-gear. So
   * the two are not alternatives — readViewer takes states from the chain and values from here,
   * and these OVERWRITE rather than add, because summing an already-merged value into a chain
   * total double-counts the kit.
   *
   * Values are the game's own int32. A producer may widen them to fit unsigned stats into JSON —
   * experience at level 99 is ~3.52 billion, past int32 but inside uint32 — and the reader
   * narrows them back, which restores the exact 32 bits the game holds.
   */
  stats: UnitStat[];
  /**
   * Contained units in socket-ordinal order. Only an item nests: a player's chain carries an
   * extended child per equipped piece, and nesting those would re-serialise the wearer's whole
   * kit inside one item.
   */
  sockets: Unit[];
  /**
   * A viewer's skills and their BONUSED levels. This is the one thing a stat capture cannot
   * reach — SKILLS_GetSkillLevel reads it off the skill list (0x485df1 passes bBonus = 1).
   */
  skills: UnitSkill[];
}

/** Every field defaulted, so a caller can build a unit by overriding only what matters. */
export function createUnit(overrides: Partial<Unit> = {}): Unit {
  return {
    unitType: -1,
    classId: -1,
    code: '',
    quality: 0,
    itemFlags: 0,
    fileIndex: -1,
    rarePrefix: 0,
    rareSuffix: 0,
    autoAffix: 0,
    format: 0,
    magicPrefix: [0, 0, 0],
    magicSuffix: [0, 0, 0],
    earLevel: 0,
    playerName: '',
    gfxIndex: 0,
    flagsEx: UnitFlagExpansion,
    statsLists: [],
    stats: [],
    sockets: [],
    skills: [],
    ...overrides,
  };
}

/**
 * The only place a capture document is narrowed by hand. Everything downstream works on
 * Unit, so the untyped shape stops here rather than running through the readers.
 */
export function unitFromJson(document: unknown): Unit {
  const source = typeof document === 'string' ? (JSON.parse(document) as unknown) : document;
  return readUnit(source);
}

function readUnit(value: unknown): Unit {
  const o = asObject(value);

  return {
    unitType: int(o, 'unitType', -1),
    classId: int(o, 'classId', -1),
    code: str(o, 'code'),
    quality: int(o, 'quality', 0),
    itemFlags: uint(o, 'itemFlags', 0),
    fileIndex: int(o, 'fileIndex', -1),
    rarePrefix: int(o, 'rarePrefix', 0),
    rareSuffix: int(o, 'rareSuffix', 0),
    autoAffix: int(o, 'autoAffix', 0),
    format: int(o, 'format', 0),
    magicPrefix: triple(o, 'magicPrefix'),
    magicSuffix: triple(o, 'magicSuffix'),
    earLevel: int(o, 'earLevel', 0),
    playerName: str(o, 'playerName'),
    gfxIndex: int(o, 'gfxIndex', 0),
    flagsEx: uint(o, 'flagsEx', UnitFlagExpansion),
    statsLists: array(o, 'statsLists').map(readStatList),
    stats: array(o, 'stats').map(readMergedStat),
    sockets: array(o, 'sockets').map(readUnit),
    skills: array(o, 'skills').map(skill => {
      const s = asObject(skill);
      return { skill: int(s, 'skill', -1), level: int(s, 'level', 0) };
    }),
  };
}

/**
 * A merged wearer stat, which does NOT fit int32 in general. The game stores stats as int32, but a
 * producer serialising an unsigned one has to widen it or emit a negative: experience at level 99
 * is ~3.52 billion, past int32 and inside uint32. `| 0` restores the exact 32 bits the game holds,
 * so the round trip is lossless for every value the game can actually store.
 *
 * The per-statlist values do NOT go through this — those are genuinely int32, and a value outside
 * the range is malformed rather than widened.
 */
function readMergedStat(value: unknown): UnitStat {
  const s = asObject(value);
  const raw = s['value'];

  return {
    id: int(s, 'id', 0),
    value: typeof raw === 'number' && Number.isFinite(raw) ? raw | 0 : 0,
    layer: int(s, 'layer', 0),
  };
}

function readStatList(value: unknown): UnitStatList {
  const o = asObject(value);

  return {
    stateNo: int(o, 'stateNo', 0),
    flags: uint(o, 'flags', 0),
    stats: array(o, 'stats').map(stat => {
      const s = asObject(stat);
      return { id: int(s, 'id', 0), value: int(s, 'value', 0), layer: int(s, 'layer', 0) };
    }),
  };
}

type Obj = Record<string, unknown>;

function asObject(value: unknown): Obj {
  return value !== null && typeof value === 'object' && !Array.isArray(value) ? (value as Obj) : {};
}

function array(o: Obj, name: string): unknown[] {
  const value = o[name];
  return Array.isArray(value) ? (value as unknown[]) : [];
}

// Out of range, fractional, or not a number at all yields the FALLBACK rather than throwing.
//
// This is where the two implementations part company: the C# reader deserialises through
// System.Text.Json, which throws on all three. Neither shape is reachable from the producer — it
// writes int32/uint32 struct fields as plain integer tokens — but the two libraries do not agree
// on what a malformed document means, and one of them should change.
//
// Note also `Number.isInteger(2.0)` is true here while System.Text.Json rejects `2.0` as a
// non-integral token, so a float-typed serialiser upstream diverges even on an in-range value.
function int(o: Obj, name: string, fallback: number): number {
  const value = o[name];
  return typeof value === 'number' &&
    Number.isInteger(value) &&
    value >= -2147483648 &&
    value <= 2147483647
    ? value
    : fallback;
}

function uint(o: Obj, name: string, fallback: number): number {
  const value = o[name];
  return typeof value === 'number' && Number.isInteger(value) && value >= 0 && value <= 4294967295
    ? value
    : fallback;
}

function str(o: Obj, name: string): string {
  const value = o[name];
  return typeof value === 'string' ? value : '';
}

function triple(o: Obj, name: string): number[] {
  const values = array(o, name);
  const out = [0, 0, 0];
  for (let i = 0; i < MaxAffixSlots && i < values.length; ++i) {
    const value = values[i];
    out[i] =
      typeof value === 'number' &&
      Number.isInteger(value) &&
      value >= -2147483648 &&
      value <= 2147483647
        ? value
        : 0;
  }

  return out;
}
