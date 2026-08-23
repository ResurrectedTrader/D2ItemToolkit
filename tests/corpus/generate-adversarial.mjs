// Generates tests/corpus/adversarial.json — a corpus written to BREAK parity between the C# and
// TypeScript engines rather than to cover the shipped tables. Run:
//
//   node tests/corpus/generate-adversarial.mjs        (or: npm run generate:adversarial)
//
// Nothing here walks the tables; every case is a deliberate boundary or a hostile combination.
// Item class ids are lifted from the existing corpus so the records name real rows.
//
// ---------------------------------------------------------------------------------------------
// SCOPE: every case here is input the C++ producer can actually emit.
// ---------------------------------------------------------------------------------------------
//
// Hostile, yes — int32 extremes, exhaustive stat sweeps, odd-but-legal quality x flag pairings,
// truncation boundaries either side of a fold — but structurally a document
// `ITEMSTATS_StoreUnit` could have written. That restriction is what makes a divergence here a
// BUG. Ground truth is producer/ItemStatStorage.cpp and docs/record-format.md.
//
// An earlier revision also generated documents the producer cannot write: `null` and `{}` and
// `'x'` where an array or object belongs, missing `id` / `value` / `flags` / `stateNo` keys,
// fractional numbers, numbers outside uint32, negative class ids / layers / stat ids, affix
// arrays that are not three numeric slots, and socket fillers nested more than one level. Those
// were removed. They cannot ever agree and they never could: the C# reader is
// System.Text.Json bound to the `Unit` DTO (src/D2ItemToolkit.Net/Stats/UnitJson.cs), which throws on
// a type or range mismatch, while the TypeScript reader defaults every field
// (src/D2ItemToolkit.Ts/src/Stats/Unit.ts). Comparing the two on malformed input measures a
// documented, deliberate difference in reader strictness, not engine parity — a permanent red
// that hides the real ones.
//
// The concrete producer facts the removals rest on:
//
//   * `ITEMSTATS_StoreVisitor` returns early on `nStatCount == 0`, so an empty leaf is never
//     emitted, and every emitted leaf carries `stateNo`, `flags` and a non-empty `stats`.
//   * every emitted stat carries `id` and `value`; `layer` is omitted only when zero.
//   * `ITEMSTATS_PackStatKey(uint16_t nLayer, uint16_t nStat)` — layers and stat ids are uint16.
//   * `dwFlagEx`, `dwItemFlags`, `dwFileIndex` and `dwFlags` are uint32: never negative, never a
//     string, and 4294967295 IS reachable (a -1 `dwFileIndex` serialises as exactly that).
//   * `magicPrefix` / `magicSuffix` are always written as exactly three numbers from
//     `wMagicPrefix[3]` / `wMagicSuffix[3]`.
//   * `ITEMSTATS_StoreUnitIdentity` returns early at `if (!pItemData) return;`, so a unit
//     carrying only `unitType` + `classId` is real; and `items` is omitted when empty.
//   * producer/ItemStatCapture.cpp: "A jewel cannot itself hold sockets, so one level of nesting
//     is all vanilla produces" — a socket filler never has an `items` array.
//   * `dwClassId` on an item indexes the single table compiled from weapons, then armor, then
//     misc (0x633351 / 0x63336d / 0x63338c, summed at 0x6333ab). Against the shipped tables and
//     after the `Expansion` splice that is 306 + 202 + 151 = 659 rows, so 0..658.

import { writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

const ID = {
  lrg: 330, aar: 326, cap: 306, bsw: 36, ssd: 25, tax: 44, axe: 1, wnd: 10, twohs: 33,
  r08: 617, r01: 610, tbk: 518, ibk: 519, tsc: 529, isc: 530, box: 549, ne1: 413, ne9: 486,
  gpr: 581, gcv: 557, skz: 601, jew: 643, elx: 508, ear: 556, cm1: 603, cm2: 604, cm3: 605,
  crs: 29, pa1: 408, hdm: 89, leg: 88, bkd: 525, gpm: 82, gps: 80, opl: 85, hrt: 531,
  vbt: 340, xtb: 388,
};

const INT_MAX = 2147483647;
const INT_MIN = -2147483648;

const cases = [];
let seq = 0;

function add(name, record, player) {
  const c = { name: name + '#' + (seq++), record };
  if (player !== undefined && player !== null) c.player = player;
  cases.push(c);
}

/** The generator's standard base/mod pair shape. */
function record(classId, opts = {}) {
  const r = { unitType: 4, classId, quality: 2, itemFlags: 16, fileIndex: 0 };
  Object.assign(r, opts.top ?? {});
  const lists = [];
  if (opts.base !== undefined) {
    lists.push({ stateNo: 0, flags: 2147483648, stats: opts.base });
  }
  if (opts.mods !== undefined) {
    lists.push({ stateNo: 0, flags: 64, stats: opts.mods });
  }
  if (opts.extraLists) lists.push(...opts.extraLists);
  if (opts.lists !== undefined) {
    if (opts.lists !== 'omit') r.statsLists = opts.lists;
  } else {
    r.statsLists = lists;
  }
  if (opts.items !== undefined) r.items = opts.items;
  return r;
}

function player(classId, level, opts = {}) {
  // `| 0` because a stat value is int32_t in the capture: 20 + int.MaxValue has to wrap the way
  // the game's own add does, not widen into a double no producer could have written.
  const attribute = (20 + level) | 0;
  const p = {
    unitType: 0,
    classId,
    flagsEx: 33554432,
    skills: [{ skill: 117, level: 10 }],
    statsLists: [{
      stateNo: 0,
      flags: 2147483648,
      stats: [
        { id: 12, value: level },
        { id: 0, value: attribute },
        { id: 2, value: attribute },
      ],
    }],
  };
  Object.assign(p, opts);
  return p;
}

const PLAYER = player(3, 50);

// ---------------------------------------------------------------------------------------------
// 1. Boundary stat values. The engine truncates toward zero; JS Math.floor does not. Every value
//    here is fed through a stat that reaches a formatter, a division or a colour comparison.
// ---------------------------------------------------------------------------------------------

const BOUNDARIES = [
  0, 1, -1, 2, -2, 3, -3, 7, -7, 8, -8, 9, -9, 25, -25, 49, -49, 50, -50, 99, -99, 100, -100,
  101, -101, 127, -128, 128, 255, -255, 256, 1000, -1000, 4095, -4096,
  32767, -32768, 32768, -32769, 65535, -65535, 65536, 1000000, -1000000,
  16777216, -16777216, 2147483646, INT_MAX, INT_MIN, -2147483647,
];

// Defence (31), an ac% (16) folded onto it by op 13, and the colour comparison that follows.
for (const v of BOUNDARIES) {
  add('bound-def-' + v, record(ID.lrg, { base: [{ id: 31, value: v }], mods: [{ id: 39, value: 5 }] }), PLAYER);
}

// ac% is the op-13 percent: (base * percent) / 100 truncated toward zero.
for (const p of BOUNDARIES) {
  add('bound-acpct-' + p, record(ID.lrg, {
    base: [{ id: 31, value: 120 }],
    mods: [{ id: 16, value: p }],
  }), PLAYER);
}

// The truncating division landing exactly on an integer versus one short of it.
for (const base of [99, 100, 101, 199, 200, 201, -99, -100, -101, -199, -200, -201, 7, -7]) {
  for (const p of [1, -1, 99, -99, 100, -100, 150, -150]) {
    add('bound-actrunc-' + base + '_' + p, record(ID.lrg, {
      base: [{ id: 31, value: base }],
      mods: [{ id: 16, value: p }],
    }), PLAYER);
  }
}

// Weapon damage pairs, one-hand (21/22) and throw (159/160), with the ED percent (17/18).
for (const v of BOUNDARIES) {
  add('bound-dmg-' + v, record(ID.ssd, {
    base: [{ id: 21, value: v }, { id: 22, value: v }],
    mods: [{ id: 18, value: 100 }, { id: 17, value: 100 }],
  }), PLAYER);
  add('bound-ed-' + v, record(ID.ssd, {
    base: [{ id: 21, value: 8 }, { id: 22, value: 15 }],
    mods: [{ id: 18, value: v }, { id: 17, value: v }],
  }), PLAYER);
  add('bound-throw-' + v, record(ID.tax, {
    base: [{ id: 159, value: v }, { id: 160, value: v }],
    mods: [{ id: 18, value: 100 }, { id: 17, value: 100 }],
  }), PLAYER);
}

// Durability (72 current / 73 max) with the dur% (75) op-13 fold, and the "never breaks" arm.
for (const v of BOUNDARIES) {
  add('bound-dur-' + v, record(ID.lrg, {
    base: [{ id: 72, value: v }, { id: 73, value: v }],
    mods: [{ id: 75, value: 50 }],
  }), PLAYER);
  add('bound-durpct-' + v, record(ID.lrg, {
    base: [{ id: 72, value: 40 }, { id: 73, value: 62 }],
    mods: [{ id: 75, value: v }],
  }), PLAYER);
}

// Requirement stats: 91 (req level), 92 (req str... actually item level), quantity 70, block 20.
for (const v of [0, 1, -1, 99, 100, INT_MAX, INT_MIN, 32767, -32768]) {
  for (const id of [20, 70, 71, 91, 92, 93, 105, 106, 194, 214, 215, 216]) {
    add('bound-s' + id + '-' + v, record(ID.lrg, {
      base: [{ id: 31, value: 100 }],
      mods: [{ id, value: v }],
    }), PLAYER);
  }
}

// Resistances and attributes: the ordinary signed formatters.
for (const v of [0, 1, -1, INT_MAX, INT_MIN, -32768, 32767, 100, -100]) {
  for (const id of [0, 1, 2, 3, 39, 41, 43, 45, 27, 28, 60, 62, 78, 79, 80, 81]) {
    add('bound-m' + id + '-' + v, record(ID.lrg, {
      base: [{ id: 31, value: 100 }],
      mods: [{ id, value: v }],
    }), PLAYER);
  }
}

// Percent-of-level stats (op 2-5) scale by the VIEWER's level: the multiply that can overflow.
for (const v of [1, -1, INT_MAX, INT_MIN, 65536, -65536, 100000]) {
  for (const lvl of [0, 1, 99, INT_MAX]) {
    add('bound-perlevel-' + v + '_' + lvl, record(ID.lrg, {
      base: [{ id: 31, value: 100 }],
      mods: [{ id: 83, layer: 1, value: v }, { id: 214, value: v }, { id: 219, value: v }],
    }), player(3, lvl));
  }
}

// Two leaves carrying the same stat: the merge adds them, and C# int addition wraps.
for (const [a, b] of [[INT_MAX, 1], [INT_MAX, INT_MAX], [INT_MIN, -1], [INT_MIN, INT_MIN],
  [2000000000, 2000000000], [-2000000000, -2000000000], [INT_MAX, INT_MIN], [1073741824, 1073741824]]) {
  add('bound-sum-def-' + a + '_' + b, record(ID.lrg, {
    lists: [
      { stateNo: 0, flags: 2147483648, stats: [{ id: 31, value: a }] },
      { stateNo: 0, flags: 2147483648, stats: [{ id: 31, value: b }] },
      { stateNo: 0, flags: 64, stats: [{ id: 39, value: 5 }] },
    ],
  }), PLAYER);
  add('bound-sum-mod-' + a + '_' + b, record(ID.lrg, {
    lists: [
      { stateNo: 0, flags: 2147483648, stats: [{ id: 31, value: 100 }] },
      { stateNo: 0, flags: 64, stats: [{ id: 39, value: a }] },
      { stateNo: 0, flags: 64, stats: [{ id: 39, value: b }] },
    ],
  }), PLAYER);
}

// ---------------------------------------------------------------------------------------------
// 2. The layered key packing. Layer-major means a layer >= 0x8000 makes the key negative, which
//    changes where it sorts and how it unpacks. The producer packs through
//    ITEMSTATS_PackStatKey(uint16_t, uint16_t), so the range is 0..65535 and the interesting
//    values are the ones either side of the sign flip at 0x8000.
// ---------------------------------------------------------------------------------------------

const LAYERS = [0, 1, 2, 6, 7, 8, 32, 127, 128, 255, 256, 32766, 32767, 32768, 32769,
  49152, 65534, 65535];

for (const layer of LAYERS) {
  // 83 ITEM_ADDCLASSSKILLS, 97 ITEM_SINGLESKILL, 188 ITEM_ADDSKILL_TAB, 107 ITEM_NONCLASSSKILL.
  add('layer-83-' + layer, record(ID.lrg, {
    base: [{ id: 31, value: 100 }],
    mods: [{ id: 83, layer, value: 2 }],
  }), PLAYER);
  add('layer-97-' + layer, record(ID.lrg, {
    base: [{ id: 31, value: 100 }],
    mods: [{ id: 97, layer, value: 3 }],
  }), PLAYER);
  add('layer-188-' + layer, record(ID.lrg, {
    base: [{ id: 31, value: 100 }],
    mods: [{ id: 188, layer, value: 1 }],
  }), PLAYER);
  add('layer-107-' + layer, record(ID.lrg, {
    base: [{ id: 31, value: 100 }],
    mods: [{ id: 107, layer, value: 4 }],
  }), PLAYER);
  // A negative-key entry alongside ordinary ones exercises the SortedDictionary ordering.
  add('layer-mix-' + layer, record(ID.lrg, {
    base: [{ id: 31, value: 100 }],
    mods: [
      { id: 39, value: 10 },
      { id: 97, layer, value: 3 },
      { id: 83, layer: 1, value: 2 },
      { id: 31, layer, value: 5 },
    ],
  }), PLAYER);
}

// ---------------------------------------------------------------------------------------------
// 3. Empty and absent — only the four shapes the producer really writes. A leaf with no stats is
//    dropped by ITEMSTATS_StoreVisitor rather than emitted, and `items` is omitted when empty,
//    so neither has a case here.
// ---------------------------------------------------------------------------------------------

add('empty-no-statslists', { unitType: 4, classId: ID.lrg, quality: 2, itemFlags: 16, fileIndex: 0 }, PLAYER);
add('empty-statslists-array', record(ID.lrg, { lists: [] }), PLAYER);
// A filler with no item data at all: ITEMSTATS_StoreUnitIdentity returns at `if (!pItemData)`.
add('empty-socket-bare', record(ID.lrg, {
  base: [{ id: 31, value: 100 }, { id: 194, value: 1 }], top: { itemFlags: 16 | 0x800 },
  items: [{ unitType: 4, classId: ID.gpr }],
}), PLAYER);
add('empty-socket-empty-lists', record(ID.lrg, {
  base: [{ id: 31, value: 100 }, { id: 194, value: 1 }], top: { itemFlags: 16 | 0x800 },
  items: [{ unitType: 4, classId: ID.gpr, statsLists: [] }],
}), PLAYER);
add('empty-viewer-omitted', record(ID.lrg, { base: [{ id: 31, value: 100 }], mods: [{ id: 214, value: 16 }] }), undefined);

// ---------------------------------------------------------------------------------------------
// 4. Identity fields at their edges. Every value here is one the producer's own field type can
//    hold; the class ids stop at 658, the last row of the compiled weapons+armor+misc table.
// ---------------------------------------------------------------------------------------------

const LAST_ITEM_CLASS = 658;

for (const classId of [0, 1, 2, 3, LAST_ITEM_CLASS - 2, LAST_ITEM_CLASS - 1, LAST_ITEM_CLASS]) {
  add('bad-classid-' + classId, record(classId, { base: [{ id: 31, value: 100 }], mods: [{ id: 39, value: 5 }] }), PLAYER);
}
add('bad-classid-absent', { unitType: 4, quality: 2, itemFlags: 16, statsLists: [] }, PLAYER);

for (const quality of [-1, 0, 1, 9, 10, 11, 255, 256, 65536, INT_MAX, INT_MIN]) {
  for (const code of ['lrg', 'aar', 'ssd', 'r08', 'jew', 'cm1', 'ear', 'tbk', 'elx', 'gpr']) {
    add('bad-quality-' + code + '-' + quality, record(ID[code], {
      base: [{ id: 31, value: 100 }], mods: [{ id: 39, value: 5 }], top: { quality },
    }), PLAYER);
  }
}
add('bad-quality-absent', { unitType: 4, classId: ID.lrg, itemFlags: 16, statsLists: [] }, PLAYER);

// The negative values here are the one place this file knowingly disagrees with itself.
// docs/record-format.md documents fileIndex as "a UniqueItems row (or -1)" and the C# Unit models
// it as an int defaulting to -1, but dwFileIndex is a DWORD, so a -1 serialises as 4294967295 —
// which is why `wide-fileindex-4294967295` exists too. One of the two forms is wrong; resolving
// that needs the struct, not a guess here.
for (const fileIndex of [-2, -1, 0, 1, 100, 400, 401, 999, 65535, INT_MAX, INT_MIN]) {
  for (const [code, quality] of [['lrg', 7], ['lrg', 5], ['lrg', 1], ['ear', 2], ['hrt', 2], ['elx', 2], ['aar', 8]]) {
    add('bad-fileindex-' + code + '-q' + quality + '-' + fileIndex, record(ID[code], {
      base: [{ id: 31, value: 100 }], top: { quality, fileIndex },
    }), PLAYER);
  }
}
add('bad-fileindex-absent', { unitType: 4, classId: ID.lrg, quality: 7, itemFlags: 16, statsLists: [] }, PLAYER);

// dwItemFlags is uint32, so the all-bits and top-bit cases are written unsigned rather than as
// -1 and int.MinValue — the same 32 bits, in the encoding nlohmann emits for a DWORD.
for (const itemFlags of [0, 4294967295, 1, 0x100 | 16, 0x8000 | 16, 0x1000000 | 16, 0x800 | 16,
  0x400000 | 16, 0x4000000 | 16, 0x7fffffff, 2147483648, 0x04400900 | 16, 0x01008910,
  0x2aaaaaaa, 0x55555555]) {
  for (const code of ['lrg', 'ssd', 'r08', 'jew', 'cm1', 'tbk', 'ear', 'gpr']) {
    add('bad-flags-' + code + '-' + itemFlags, record(ID[code], {
      base: [{ id: 31, value: 100 }, { id: 194, value: 2 }],
      mods: [{ id: 39, value: 5 }],
      top: { itemFlags },
    }), PLAYER);
  }
}

for (const format of [-1, 0, 1, 2, 100, 255, 65535, INT_MAX, INT_MIN]) {
  add('bad-format-' + format, record(ID.lrg, {
    base: [{ id: 31, value: 100 }], top: { quality: 7, format, fileIndex: 0 },
  }), PLAYER);
}

for (const v of [-1, 0, 1, 2, 3, 999, 2000, 65535, INT_MAX, INT_MIN]) {
  add('bad-magicprefix-' + v, record(ID.lrg, {
    base: [{ id: 31, value: 100 }], top: { quality: 4, magicPrefix: [v, 0, 0] },
  }), PLAYER);
  add('bad-magicsuffix-' + v, record(ID.lrg, {
    base: [{ id: 31, value: 100 }], top: { quality: 4, magicSuffix: [v, 0, 0] },
  }), PLAYER);
  add('bad-rareprefix-' + v, record(ID.lrg, {
    base: [{ id: 31, value: 100 }], top: { quality: 6, rarePrefix: v, rareSuffix: v },
  }), PLAYER);
  add('bad-autoaffix-' + v, record(ID.lrg, {
    base: [{ id: 31, value: 100 }], top: { quality: 6, autoAffix: v },
  }), PLAYER);
  add('bad-runeword-prefix-' + v, record(ID.crs, {
    top: { itemFlags: 16 | 0x4000000 | 0x800, magicPrefix: [v, 0, 0] },
    lists: [{ stateNo: 171, flags: 64, stats: [{ id: 39, value: 30 }] }],
  }), PLAYER);
}
for (const earLevel of [-1, 0, 1, 99, 100, 255, INT_MAX, INT_MIN]) {
  add('bad-earlevel-' + earLevel, record(ID.ear, {
    lists: [], top: { fileIndex: 0, earLevel, playerName: 'Bob' },
  }), PLAYER);
}
add('bad-earname-empty', record(ID.ear, { lists: [], top: { fileIndex: 0, earLevel: 42, playerName: '' } }), PLAYER);
add('bad-earname-absent', record(ID.ear, { lists: [], top: { fileIndex: 0, earLevel: 42 } }), PLAYER);
add('bad-earname-long', record(ID.ear, { lists: [], top: { fileIndex: 0, earLevel: 42, playerName: 'X'.repeat(200) } }), PLAYER);
add('bad-earname-unicode', record(ID.ear, { lists: [], top: { fileIndex: 0, earLevel: 42, playerName: 'ÿc1ÿ中' } }), PLAYER);

// Stat ids outside ItemStatCost, and the sentinel edges of the table. nStat is uint16, so the
// range stops at 65535.
for (const id of [0, 356, 357, 358, 359, 360, 400, 511, 512, 1000, 9999, 32767, 32768,
  65534, 65535]) {
  add('bad-statid-' + id, record(ID.lrg, {
    base: [{ id: 31, value: 100 }], mods: [{ id, value: 5 }],
  }), PLAYER);
  add('bad-statid-base-' + id, record(ID.lrg, {
    base: [{ id, value: 100 }], mods: [{ id: 39, value: 5 }],
  }), PLAYER);
}

// stateNo and flags on a stat list: unknown states, every documented mask, and the bits between.
for (const stateNo of [0, 1, 100, 101, 164, 165, 166, 170, 171, 172, 255, 65535, INT_MAX]) {
  for (const flags of [0, 64, 0x2000, 0x2040, 2147483648, 4294967295, 0x80000040]) {
    add('bad-node-s' + stateNo + '-f' + flags, record(ID.lrg, {
      lists: [
        { stateNo: 0, flags: 2147483648, stats: [{ id: 31, value: 100 }] },
        { stateNo, flags, stats: [{ id: 39, value: 7 }, { id: 0, value: 3 }] },
      ],
    }), PLAYER);
  }
}
// STATLIST_SET alone, with no STATLIST_MAGIC, is a shape the engine never builds: a set tier is
// created as MAGIC|SET. Every view that asks for MAGIC therefore drops it.
add('bad-node-set-without-magic', record(ID.lrg, {
  lists: [{ stateNo: 165, flags: 8192, stats: [{ id: 0, value: 20 }] }],
}), PLAYER);

// ---------------------------------------------------------------------------------------------
// 5. JSON numbers past int32 — but only where the producer's field really is a uint32 and can
//    really reach there. `dwFileIndex` holding -1 for "no unique row" is the live example:
//    nlohmann serialises a DWORD, so the document says 4294967295.
//
//    Everything else that used to live here (fractional values, > uint32, negatives on an
//    unsigned field, > int32 on a signed one) was removed — see the header.
// ---------------------------------------------------------------------------------------------

const UINT32_MAX = 4294967295;

add('wide-itemflags-' + UINT32_MAX, { unitType: 4, classId: ID.lrg, quality: 2, itemFlags: UINT32_MAX, statsLists: [] }, PLAYER);
add('wide-fileindex-' + UINT32_MAX, { unitType: 4, classId: ID.lrg, quality: 7, itemFlags: 16, fileIndex: UINT32_MAX, statsLists: [] }, PLAYER);
add('wide-nodeflags-' + UINT32_MAX, record(ID.lrg, { lists: [{ stateNo: 0, flags: UINT32_MAX, stats: [{ id: 31, value: 100 }] }] }), PLAYER);
add('wide-viewer-flagsex-' + UINT32_MAX, record(ID.lrg, { base: [{ id: 31, value: 100 }], top: { quality: 7 } }), player(3, 50, { flagsEx: UINT32_MAX }));

// dwFlagEx is uint32: never negative, never a string. Only the expansion bit is read today, so
// the interesting values are the ones either side of it and the two ends of the range.
for (const v of [0, 33554432, 2147483647, 2147483648, 4294967295]) {
  add('flagsex-' + v, record(ID.lrg, {
    base: [{ id: 31, value: 100 }], top: { quality: 7, format: 0, fileIndex: 0 },
  }), player(3, 50, { flagsEx: v }));
}
add('flagsex-absent', record(ID.lrg, { base: [{ id: 31, value: 100 }], top: { quality: 7 } }),
  { unitType: 0, classId: 3, skills: [], statsLists: [{ stateNo: 0, flags: 2147483648, stats: [{ id: 12, value: 50 }] }] });

// ---------------------------------------------------------------------------------------------
// 6. Sockets — more fillers than the declared count and fewer, and the rune-versus-gem join.
//    No nesting past one level: producer/ItemStatCapture.cpp notes that a jewel cannot itself
//    hold sockets, so a filler never carries an `items` array.
// ---------------------------------------------------------------------------------------------

// ITEMSTATS_StoreVisitor drops a leaf with no stats, so a filler that grants nothing carries an
// EMPTY statsLists rather than a leaf holding an empty array.
function filler(classId, stats = [{ id: 39, value: 30 }], extra = {}) {
  return Object.assign({
    unitType: 4, classId,
    statsLists: stats.length === 0 ? [] : [{ stateNo: 0, flags: 64, stats }],
  }, extra);
}

for (const declared of [0, 1, 2, 6, -1, INT_MAX, INT_MIN]) {
  for (const count of [0, 1, 2, 7]) {
    const fillers = [];
    for (let i = 0; i < count; ++i) fillers.push(filler(ID.gpr, [{ id: 39, value: 10 + i }]));
    add('sock-d' + declared + '-n' + count, record(ID.lrg, {
      base: [{ id: 31, value: 100 }, { id: 194, value: declared }],
      top: { itemFlags: 16 | 0x800 },
      // `items` is omitted when empty, not written as [].
      items: count === 0 ? undefined : fillers,
    }), PLAYER);
  }
}

// A filler carrying its own base array, so ItemOnly picks it up.
add('sock-filler-base-array', record(ID.lrg, {
  base: [{ id: 31, value: 100 }, { id: 194, value: 1 }],
  top: { itemFlags: 16 | 0x800 },
  items: [{
    unitType: 4, classId: ID.jew, quality: 4,
    statsLists: [
      { stateNo: 0, flags: 2147483648, stats: [{ id: 31, value: INT_MAX }] },
      { stateNo: 0, flags: 64, stats: [{ id: 16, value: 100 }] },
    ],
  }],
}), PLAYER);
// Fillers of every kind at once — the ", " versus newline join.
add('sock-mixed-join', record(ID.crs, {
  base: [{ id: 194, value: 4 }],
  top: { itemFlags: 16 | 0x800 },
  items: [filler(ID.skz), filler(ID.r01, []), filler(ID.gcv), filler(ID.r08, [])],
}), PLAYER);
add('sock-runes-only', record(ID.crs, {
  base: [{ id: 194, value: 3 }],
  top: { itemFlags: 16 | 0x800 },
  items: [filler(ID.r01, []), filler(ID.r08, []), filler(ID.r01, [])],
}), PLAYER);
// A filler at each end of the compiled items table — a filler is an item, so the same 0..658.
for (const classId of [0, LAST_ITEM_CLASS - 1, LAST_ITEM_CLASS]) {
  add('sock-filler-class-' + classId, record(ID.lrg, {
    base: [{ id: 31, value: 100 }, { id: 194, value: 1 }],
    top: { itemFlags: 16 | 0x800 },
    items: [filler(classId)],
  }), PLAYER);
}
// Socket fillers carrying overflowing contributions.
add('sock-overflow', record(ID.lrg, {
  base: [{ id: 31, value: INT_MAX }, { id: 194, value: 2 }],
  top: { itemFlags: 16 | 0x800 },
  items: [filler(ID.gpr, [{ id: 31, value: INT_MAX }]), filler(ID.gpr, [{ id: 31, value: INT_MAX }])],
}), PLAYER);

// ---------------------------------------------------------------------------------------------
// 7. Viewer edge cases.
// ---------------------------------------------------------------------------------------------

const ITEM_FOR_VIEWER = () => record(ID.lrg, {
  base: [{ id: 31, value: 120 }, { id: 21, value: 5 }, { id: 22, value: 12 }],
  mods: [{ id: 39, value: 25 }, { id: 214, value: 16 }, { id: 93, value: 20 }, { id: 20, value: 15 }],
  top: { quality: 7 },
});

for (const classId of [0, 1, 2, 3, 4, 5, 6, 7, 8, 100, 65535, INT_MAX]) {
  add('viewer-class-' + classId, ITEM_FOR_VIEWER(), player(classId, 50));
}
for (const level of [0, 1, -1, 99, 100, 255, 65535, INT_MAX, INT_MIN]) {
  add('viewer-level-' + level, ITEM_FOR_VIEWER(), player(3, level));
}
add('viewer-no-statslists', ITEM_FOR_VIEWER(), { unitType: 0, classId: 3, flagsEx: 33554432, skills: [] });
add('viewer-empty-statslists', ITEM_FOR_VIEWER(), { unitType: 0, classId: 3, flagsEx: 33554432, skills: [], statsLists: [] });
add('viewer-no-skills', ITEM_FOR_VIEWER(), { unitType: 0, classId: 3, flagsEx: 33554432, statsLists: [{ stateNo: 0, flags: 2147483648, stats: [{ id: 12, value: 50 }] }] });
add('viewer-skills-empty', ITEM_FOR_VIEWER(), player(3, 50, { skills: [] }));
add('viewer-skill-negative-level', ITEM_FOR_VIEWER(), player(3, 50, { skills: [{ skill: 117, level: -5 }] }));
add('viewer-skill-huge', ITEM_FOR_VIEWER(), player(3, 50, { skills: [{ skill: INT_MAX, level: INT_MAX }] }));
add('viewer-skill-dupe', ITEM_FOR_VIEWER(), player(3, 50, { skills: [{ skill: 117, level: 1 }, { skill: 117, level: 9 }] }));
add('viewer-unittype-nonplayer', ITEM_FOR_VIEWER(), player(3, 50, { unitType: 1 }));
add('viewer-unittype-absent', ITEM_FOR_VIEWER(), { classId: 3, statsLists: [{ stateNo: 0, flags: 2147483648, stats: [{ id: 12, value: 50 }] }] });
add('viewer-noncontributing-only', ITEM_FOR_VIEWER(), { unitType: 0, classId: 3, statsLists: [{ stateNo: 0, flags: 8256, stats: [{ id: 12, value: 50 }, { id: 0, value: 70 }] }] });
add('viewer-holyshield', ITEM_FOR_VIEWER(), player(3, 50, {
  statsLists: [
    { stateNo: 0, flags: 2147483648, stats: [{ id: 12, value: 50 }, { id: 0, value: 70 }, { id: 2, value: 70 }] },
    { stateNo: 101, flags: 64, stats: [{ id: 20, value: 30 }] },
  ],
}));
// The carried filler holds STRENGTH, not a resist: a viewer reads stats 0/2/12, so a gem carrying
// only stat 39 rendered identically to a viewer carrying nothing and could not see a reader that
// folded carried gear into the wearer's own attributes.
add('viewer-carrying-a-gem', ITEM_FOR_VIEWER(), player(3, 50, { items: [filler(ID.gpr, [{ id: 0, value: 100 }])] }));
add('viewer-overflowing-level', ITEM_FOR_VIEWER(), {
  unitType: 0, classId: 3,
  statsLists: [
    { stateNo: 0, flags: 2147483648, stats: [{ id: 12, value: INT_MAX }] },
    { stateNo: 0, flags: 64, stats: [{ id: 12, value: 1 }] },
  ],
});
for (const [str, dex] of [[0, 0], [-1, -1], [INT_MAX, INT_MAX], [INT_MIN, INT_MIN], [1, 1]]) {
  add('viewer-attrs-' + str + '_' + dex, ITEM_FOR_VIEWER(), {
    unitType: 0, classId: 3, flagsEx: 33554432,
    statsLists: [{ stateNo: 0, flags: 2147483648, stats: [{ id: 12, value: 50 }, { id: 0, value: str }, { id: 2, value: dex }] }],
  });
}
add('viewer-record-empty', ITEM_FOR_VIEWER(), {});

// ---------------------------------------------------------------------------------------------
// 8. Every quality crossed with every relevant flag, on item types the shipped sweep skipped.
// ---------------------------------------------------------------------------------------------

const SKIPPED_CODES = ['jew', 'cm1', 'cm2', 'cm3', 'r01', 'gcv', 'skz', 'ear', 'hrt', 'elx',
  'tsc', 'isc', 'ibk', 'crs', 'axe', 'wnd', 'twohs', 'pa1', 'vbt', 'xtb', 'ne9', 'gps', 'opl',
  'hdm', 'leg', 'bkd'];
const FLAG_SET = [0, 16, 16 | 0x100, 16 | 0x800, 16 | 0x8000, 16 | 0x400000, 16 | 0x1000000,
  16 | 0x4000000, 16 | 0x800 | 0x4000000 | 0x400000];

for (const code of SKIPPED_CODES) {
  for (let quality = 1; quality <= 9; ++quality) {
    for (const itemFlags of FLAG_SET) {
      add('sweep-' + code + '-q' + quality + '-f' + itemFlags, record(ID[code], {
        base: [{ id: 31, value: 120 }, { id: 72, value: 40 }, { id: 73, value: 62 },
          { id: 21, value: 8 }, { id: 22, value: 15 }, { id: 194, value: 2 }],
        mods: [{ id: 39, value: 25 }, { id: 18, value: 150 }, { id: 17, value: 150 }],
        top: { quality, itemFlags },
      }), PLAYER);
    }
  }
}

// ---------------------------------------------------------------------------------------------
// 9. Set items, books and the refusal paths, at their boundaries.
// ---------------------------------------------------------------------------------------------

// An unearned tier keeps STATLIST_SET (0x2040); earning it clears the bit, leaving 0x40.
for (const stateNo of [164, 165, 166, 167, 168, 169, 170, 171, 172]) {
  for (const unearned of [true, false]) {
    add('set-state' + stateNo + '-' + (unearned ? 'unearned' : 'earned'), record(ID.aar, {
      top: { quality: 5, fileIndex: 0 },
      lists: [{ stateNo, flags: unearned ? 8256 : 64, stats: [{ id: 0, value: 20 }] }],
    }), PLAYER);
  }
}
for (const fileIndex of [-1, 0, 1, 126, 127, 128, INT_MAX]) {
  add('set-fileindex-' + fileIndex, record(ID.aar, {
    top: { quality: 5, fileIndex },
    lists: [{ stateNo: 165, flags: 8256, stats: [{ id: 0, value: 20 }] }],
  }), PLAYER);
}
for (const suffix of [-1, 0, 1, 2, 20, 21, 22, 999, INT_MAX, INT_MIN]) {
  for (const code of ['tbk', 'ibk', 'tsc', 'isc']) {
    add('book-' + code + '-s' + suffix, record(ID[code], {
      top: { magicSuffix: [suffix, 0, 0] },
      base: [{ id: 70, value: 20 }],
    }), PLAYER);
  }
}
for (const qty of [0, 1, -1, 20, INT_MAX, INT_MIN]) {
  add('book-qty-' + qty, record(ID.tbk, { top: { magicSuffix: [0, 0, 0] }, base: [{ id: 70, value: qty }] }), PLAYER);
}

// ---------------------------------------------------------------------------------------------
// 10. Elixirs, charms, throwing potions and quest items at their boundaries.
// ---------------------------------------------------------------------------------------------

for (const fileIndex of [-1, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 42, 999, INT_MAX, INT_MIN]) {
  add('elixir-' + fileIndex, record(ID.elx, {
    top: { fileIndex },
    lists: [{ stateNo: 0, flags: 64, stats: [{ id: 71, value: 5120 }] }],
  }), PLAYER);
}
for (const v of [0, 1, -1, 5120, INT_MAX, INT_MIN]) {
  add('elixir-value-' + v, record(ID.elx, {
    top: { fileIndex: 0 },
    lists: [{ stateNo: 0, flags: 64, stats: [{ id: 71, value: v }] }],
  }), PLAYER);
}
for (const code of ['cm1', 'cm2', 'cm3']) {
  for (const v of [0, 1, -1, INT_MAX, INT_MIN]) {
    add('charm-' + code + '-' + v, record(ID[code], {
      lists: [{ stateNo: 0, flags: 64, stats: [{ id: 39, value: v }] }],
    }), PLAYER);
  }
}
for (const code of ['gps', 'opl', 'gpm']) {
  for (const v of [0, 1, -1, INT_MAX, INT_MIN]) {
    add('tpot-' + code + '-' + v, record(ID[code], {
      base: [{ id: 159, value: v }, { id: 160, value: v }],
    }), PLAYER);
  }
}

// ---------------------------------------------------------------------------------------------
// 11. Op-13 targets whose base is present but whose merged entry is not, and vice versa: the
//     pass inserts a key the merge never saw, which has to land in sorted position.
// ---------------------------------------------------------------------------------------------

add('op13-insert-only', record(ID.lrg, {
  lists: [
    { stateNo: 0, flags: 2147483648, stats: [{ id: 31, value: 200 }, { id: 73, value: 60 }] },
    { stateNo: 0, flags: 64, stats: [{ id: 16, value: 50 }, { id: 75, value: 50 }, { id: 254, value: 1 }] },
  ],
}), PLAYER);
add('op13-zero-percent', record(ID.lrg, {
  base: [{ id: 31, value: 200 }], mods: [{ id: 16, value: 0 }],
}), PLAYER);
add('op13-zero-base', record(ID.lrg, {
  base: [{ id: 31, value: 0 }], mods: [{ id: 16, value: 100 }],
}), PLAYER);
add('op13-negative-base', record(ID.lrg, {
  base: [{ id: 31, value: -200 }], mods: [{ id: 16, value: 50 }],
}), PLAYER);
add('op13-negative-both', record(ID.lrg, {
  base: [{ id: 31, value: -200 }], mods: [{ id: 16, value: -50 }],
}), PLAYER);
for (const [base, pct] of [[INT_MAX, 100], [INT_MAX, INT_MAX], [INT_MIN, 100], [INT_MIN, -1],
  [INT_MIN, INT_MIN], [INT_MAX, -100], [1, INT_MAX], [-1, INT_MIN], [100, INT_MAX]]) {
  add('op13-overflow-' + base + '_' + pct, record(ID.lrg, {
    base: [{ id: 31, value: base }], mods: [{ id: 16, value: pct }],
  }), PLAYER);
  add('op13-overflow-dmg-' + base + '_' + pct, record(ID.ssd, {
    base: [{ id: 21, value: base }, { id: 22, value: base }],
    mods: [{ id: 18, value: pct }, { id: 17, value: pct }],
  }), PLAYER);
}

// A negative layer-0 target with a positive percent: floor versus truncate diverge on the
// quotient's sign, which only a negative product reveals.
for (const base of [-1, -3, -7, -49, -99, -101, -199]) {
  for (const pct of [1, 3, 7, 33, 49, 99, 101]) {
    add('op13-trunc-' + base + '_' + pct, record(ID.lrg, {
      base: [{ id: 31, value: base }], mods: [{ id: 16, value: pct }],
    }), PLAYER);
  }
}

// ---------------------------------------------------------------------------------------------
// 12. Attack speed, block, level requirement and the per-level writers at their edges.
// ---------------------------------------------------------------------------------------------

for (const ias of [0, 1, -1, 20, -20, 100, -100, INT_MAX, INT_MIN]) {
  for (const code of ['ssd', 'axe', 'wnd', 'bsw', 'twohs', 'tax']) {
    add('speed-' + code + '-' + ias, record(ID[code], {
      base: [{ id: 21, value: 5 }, { id: 22, value: 12 }],
      mods: [{ id: 93, value: ias }],
    }), player(3, 40));
  }
}
for (const block of [0, 1, -1, 50, 100, INT_MAX, INT_MIN]) {
  for (const cls of [3, 0, 6]) {
    add('block-' + cls + '-' + block, record(ID.lrg, {
      base: [{ id: 31, value: 90 }], mods: [{ id: 20, value: block }],
    }), player(cls, 60));
  }
}
for (const req of [0, 1, -1, 100, INT_MAX, INT_MIN]) {
  add('reqlevel-' + req, record(ID.lrg, {
    base: [{ id: 31, value: 100 }], mods: [{ id: 91, value: req }, { id: 92, value: req }],
  }), PLAYER);
}

// ---------------------------------------------------------------------------------------------
// 13. Every stat id in ItemStatCost crossed with the int32 extremes. The sweep in tools/Corpus
//     touches about twenty stat ids; ItemStatCost has 359 rows and the descfunc switch has 24
//     arms, several of which negate, shift or multiply the value.
// ---------------------------------------------------------------------------------------------

const STAT_COUNT = 359;
const HOSTS = [['lrg', ID.lrg], ['mac', 19], ['ssd', ID.ssd]];

for (const [tag, classId] of HOSTS) {
  for (let id = 0; id < STAT_COUNT; ++id) {
    for (const value of [INT_MIN, INT_MAX, -1, 1]) {
      add('allstat-' + tag + '-' + id + '-' + value, record(classId, {
        base: [{ id: 31, value: 100 }, { id: 21, value: 5 }, { id: 22, value: 12 }],
        mods: [{ id, value }],
      }), PLAYER);
    }
  }
}

// descfunc 20 negates the value before formatting: -(int.MinValue) is int.MinValue in C#.
for (const id of [116, 305, 306, 307, 308, 333, 334, 335, 336]) {
  for (const value of [INT_MIN, INT_MIN + 1, INT_MAX, -1, 0, 1, -2147483647]) {
    add('negate-' + id + '-' + value, record(ID.lrg, {
      base: [{ id: 31, value: 100 }],
      mods: [{ id, value }],
    }), PLAYER);
  }
}

// Stat 122 gets +50 on a blunt weapon (itemtype 57), which is the one unguarded addition.
for (const [tag, classId] of [['mac', 19], ['clb', 14], ['whm', 22], ['lrg', ID.lrg], ['ssd', ID.ssd]]) {
  for (const value of [INT_MAX, INT_MAX - 49, INT_MAX - 50, INT_MAX - 51, 2147483598,
    INT_MIN, -50, -51, -49, 0, 1, -1]) {
    add('stat122-' + tag + '-' + value, record(classId, {
      base: [{ id: 21, value: 5 }, { id: 22, value: 12 }, { id: 31, value: 100 }],
      mods: [{ id: 122, value }],
    }), PLAYER);
  }
}

// ---------------------------------------------------------------------------------------------
// 14. The damage clamp: `max <= min + 1` with min at the top of the range.
// ---------------------------------------------------------------------------------------------

const DMG_PAIRS = [
  [INT_MAX, 0], [INT_MAX, INT_MAX], [INT_MAX, -1], [INT_MAX, INT_MIN], [INT_MAX, 1],
  [INT_MAX - 1, INT_MAX], [INT_MIN, INT_MIN], [INT_MIN, 0], [INT_MIN, INT_MAX],
  [0, INT_MIN], [-1, INT_MIN], [-1, 0], [-1, -1], [0, 0], [5, 5], [5, 6], [5, 4],
  [2147483646, 2147483647], [2147483646, 0],
];

for (const [min, max] of DMG_PAIRS) {
  for (const [tag, classId] of [['ssd', ID.ssd], ['twohs', ID.twohs], ['bsw', ID.bsw],
    ['tax', ID.tax], ['sst', 63]]) {
    // one-hand 21/22, two-hand 23/24, throw 159/160.
    add('clamp-1h-' + tag + '-' + min + '_' + max, record(classId, {
      base: [{ id: 21, value: min }, { id: 22, value: max }],
    }), PLAYER);
    add('clamp-2h-' + tag + '-' + min + '_' + max, record(classId, {
      base: [{ id: 21, value: 5 }, { id: 22, value: 12 },
        { id: 23, value: min }, { id: 24, value: max }],
    }), PLAYER);
    add('clamp-th-' + tag + '-' + min + '_' + max, record(classId, {
      base: [{ id: 21, value: 5 }, { id: 22, value: 12 },
        { id: 159, value: min }, { id: 160, value: max }],
    }), PLAYER);
  }
}

// ---------------------------------------------------------------------------------------------
// 15. The requirement fold: base + D2ApplyPercent(base, stat 91). Items whose reqstr/reqdex is
//     100 or more can drive the sum past int.MaxValue with a percent that is itself in range.
// ---------------------------------------------------------------------------------------------

const HEAVY = [['aar', ID.aar, 100], ['gsd', 38, 100], ['9gd', 131, 170], ['7la', 201, 196],
  ['7bt', 203, 189], ['7ba', 202, 166], ['7ga', 204, 167], ['9wc', 155, 140],
  ['9b8', 139, 106], ['9ts', 144, 118], ['8lw', 168, 118], ['7gd', 234, 189]];

for (const [tag, classId, required] of HEAVY) {
  const pivot = Math.floor(((2147483647 - required) * 100) / required);
  const candidates = new Set([INT_MAX, INT_MIN, -1, 0, 1, 100, -100]);
  for (let k = -2; k <= 2; ++k) {
    candidates.add(pivot + k);
    candidates.add(Math.floor((2147483647 * 100) / required) + k);
    candidates.add(-(pivot + k));
  }
  for (const percent of candidates) {
    if (percent > INT_MAX || percent < INT_MIN) continue;
    for (const itemFlags of [16, 16 | 0x400000]) {
      add('req-' + tag + '-' + percent + '-f' + itemFlags, record(classId, {
        base: [{ id: 31, value: 100 }, { id: 21, value: 5 }, { id: 22, value: 12 }],
        mods: [{ id: 91, value: percent }],
        top: { itemFlags },
      }), player(3, 50));
    }
  }
}

// ---------------------------------------------------------------------------------------------
// 16. (was: non-object elements wherever a JsonElement is dereferenced — `null`, `7`, `'x'`,
//     `true`, `false`, `[]` in place of the record, the player, a stat-list group, a stat, a
//     socket filler, a skill, an affix slot. `nlohmann::json` writes an object at every one of
//     those positions and nothing else, so the family was removed; see the header.)
// ---------------------------------------------------------------------------------------------

// ---------------------------------------------------------------------------------------------
// 17. The same stat sweep through the paths the modifier block does not take: the BASE array
//     (which drives every colour comparison), a non-zero layer (which several formatters read as
//     a skill / class / monster-type selector), and a level-1 viewer (which scales op 2-5).
// ---------------------------------------------------------------------------------------------

for (let id = 0; id < STAT_COUNT; ++id) {
  for (const value of [INT_MIN, INT_MAX]) {
    add('allbase-' + id + '-' + value, record(ID.lrg, {
      base: [{ id: 31, value: 100 }, { id, value }],
      mods: [{ id: 39, value: 5 }],
    }), PLAYER);

    for (const layer of [1, 32768]) {
      add('alllayer-' + id + '-' + layer + '-' + value, record(ID.lrg, {
        base: [{ id: 31, value: 100 }],
        mods: [{ id, layer, value }],
      }), PLAYER);
    }

    add('alllvl1-' + id + '-' + value, record(ID.lrg, {
      base: [{ id: 31, value: 100 }],
      mods: [{ id, value }],
    }), player(1, 1));
  }
}

// ---------------------------------------------------------------------------------------------
// 18. The op-13 fold landing exactly on the damage clamp, and a dual-wielding Barbarian (the
//     one viewer that changes which damage line a two-handed weapon takes).
// ---------------------------------------------------------------------------------------------

for (const [min, max] of DMG_PAIRS) {
  add('clamp-barb-' + min + '_' + max, record(ID.twohs, {
    base: [{ id: 21, value: min }, { id: 22, value: max },
      { id: 23, value: min }, { id: 24, value: max }],
  }), player(4, 50));
  add('clamp-ed-' + min + '_' + max, record(ID.ssd, {
    base: [{ id: 21, value: min }, { id: 22, value: max }],
    mods: [{ id: 18, value: 100 }, { id: 17, value: 100 }],
  }), PLAYER);
}

const out = fileURLToPath(new URL('adversarial.json', import.meta.url));
writeFileSync(out, '[\n  ' + cases.map((c) => JSON.stringify(c)).join(',\n  ') + '\n]\n');
console.log(cases.length + ' cases -> ' + out);
