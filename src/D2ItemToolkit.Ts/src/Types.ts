/**
 * The contracts every table implementation and the description engine share. Ported verbatim from
 * ItemDescription.cs — the shapes are dictated by what the engine reads, not by convenience.
 */

export interface IStringTable {
  getByIndex(index: number): string | null;
}

export interface IStatValueSource {
  /**
   * Describe scope: the temp list the engine builds at 0x4e612b. The damage aggregate (0x4e49c0)
   * and the 23/24 suppression pair (0x4e62d2) both read it, NOT the unit.
   */
  getBaseStatValue(statId: number, layer: number): number;

  /**
   * The VIEWER's stat. SKILLDESC_CalcStatGroupValue 0x4e4c50 scales an op 2-5 stat by
   * GetStatUnsignedValue(GetPlayerUnit(), opBase, 0) — the local client player, categorically not
   * the item being described.
   */
  getPlayerStatValue(statId: number): number;

  /** Unit scope: every list on the item, which is what GetTxtMaxDurability 0x625e00 queries. */
  getItemStatValue(statId: number): number;

  readonly playerClass: number;

  isItemOfType(itemTypeId: number): boolean;

  readonly describedUnitIsItem: boolean;

  readonly itemTableAllowsDurability: boolean;

  getTxtMaxDurability(): number;
}

export interface ISkillTable {
  readonly rowCount: number;
  skillExists(skillId: number): boolean;
  /** Null for an out-of-range id, matching the C# — the caller drops the line. */
  getSkillName(skillId: number): string | null;
  getSkillClass(skillId: number): number;
}

export interface ICharacterClassTable {
  getAllSkillsText(classId: number): string | null;
  getSkillTabText(classId: number, tabIndex: number): string | null;
  getClassOnlyText(classId: number): string | null;
  classExists(classId: number): boolean;
}

export interface IMonsterTypeTable {
  monsterTypeExists(monsterTypeId: number): boolean;
  getMonsterTypeName(monsterTypeId: number): string | null;
  monsterExists(monsterId: number): boolean;
  getMonsterName(monsterId: number): string | null;
}

export interface IGameTimeProvider {
  /** Returns null when the angle is unavailable, matching the C# `out` + bool pair. */
  getTimeAngle(): number | null;
}

/** One ItemStatCost row. Field offsets are the record's, kept for cross-checking against the asm. */
export interface StatDescriptor {
  statId: number;
  descPriority: number;
  descFunc: number; // +0x36
  descVal: number;
  descStrPos: number; // +0x38
  descStrNeg: number; // +0x3A
  descStr2: number; // +0x3C
  descGrp: number;
  descGrpFunc: number; // +0x40
  descGrpVal: number;
  descGrpStrPos: number; // +0x42
  descGrpStrNeg: number; // +0x44
  descGrpStr2: number; // +0x46
  valShift: number;
  op: number;
  opParam: number; // +0x55
  opBase: number; // +0x56
}

export interface IItemStatCostTable {
  tryGetStat(statId: number): StatDescriptor | null;
  readonly rowCount: number;
  readonly statIdsByDescPriority: readonly number[];
  getStatsInDescGroup(descGrp: number): readonly number[];
  readonly skillIdShift: number;
}

/** One op-13 relationship: PercentStat's value is a percentage of TargetStat's BASE value. */
export interface ItemStatOpEntry {
  percentStat: number;
  targetStat: number;
}

export interface IItemStatOpTable {
  readonly percentOfBaseEntries: readonly ItemStatOpEntry[];
}

/** Locale ids the description engine emits directly. */
export const DescStringIds = {
  Space: 3995, // " "
  Colon: 3997, // ":" (key "colon"), DescFunc 22
  Newline: 3998, // "\n" (key "newline") — the line terminator
  ListComma: 3852, // "," (key "KeyComma") — block-mode separator
  Percent: 4001, // "%"
  Plus: 4002, // "+"
  To: 4003, // "to", DescFunc 27 and 28
  DescStr2Override: 11091, // used when DescStr2 == 5382
  RepairSingleCount: 21241,
  RepairCountAndSeconds: 21242,
  Level: 21249, // DescFunc 24
  NeverBreaks: 21240,
  DescStr2Sentinel: 5382,
} as const;

/**
 * 32-bit helpers. C# `int` arithmetic is two's-complement 32-bit and several places in the engine
 * rely on it overflowing; JavaScript numbers are doubles, so anything mirroring C# `int` maths has
 * to be forced back through these.
 */
export const Int32 = {
  /** C# `unchecked(a * b)` */
  mul(a: number, b: number): number {
    return Math.imul(a, b);
  },

  /** C# `(int)` truncation of a division — toward zero, not floor. */
  div(a: number, b: number): number {
    return Math.trunc(a / b);
  },

  /** Force a value back into int32, as C# `unchecked((int)x)` would. */
  of(value: number): number {
    return value | 0;
  },
} as const;

/**
 * The framework exception types the C# raises, as named subclasses.
 *
 * They live here rather than in the modules that throw them because more than one module throws
 * the same type, and because callers observe them BY NAME — the differential harness reports the
 * constructor name, so collapsing these onto `Error` would make a malformed record that halts the
 * C# reader look identical to one that renders fine.
 */
export class ArgumentNullException extends Error {
  override readonly name = 'ArgumentNullException';
}

export class NotSupportedException extends Error {
  override readonly name = 'NotSupportedException';
}

/**
 * C# `string.IsNullOrEmpty`. A TypeScript-only helper — the C# side uses the BCL — and it lived in
 * three files as three byte-identical copies.
 */
export function isNullOrEmpty(text: string | null | undefined): boolean {
  return text === null || text === undefined || text.length === 0;
}
