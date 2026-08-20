import type { ItemTier } from './ItemTable.js';

// One record per table row, so every public table is walked the same way: `rowCount` for the bound
// and `rowAt(index)` for the row. The per-field getters they wrap are all still there — these exist
// because the tables previously disagreed on both halves (count vs rowCount vs statCount;
// code(i) vs codeAt(i) vs getRow(i)), which made iterating several of them a matter of remembering
// which spelling each one chose. The peer of TableRows.cs.
//
// `rowAt` returns null for an out-of-range index rather than throwing, matching what the underlying
// getters already do.
//
// Two tables keep two counts because they genuinely have two row spaces, and so name their
// accessors after them instead: SetTable (setAt / pieceAt) and TxtMonsterTypeTable (monsterAt /
// monsterTypeAt). TxtFile keeps only rowCount — it is the generic column reader every other table
// is built on, and a "row" there has no fixed shape to hand back.

/** A row of the concatenated weapons/armor/misc table, keyed by classId. */
export interface ItemRow {
  readonly classId: number;
  readonly code: string;
  readonly tier: ItemTier;
  readonly requiredLevel: number;

  /** items.txt `type`. */
  readonly primaryTypeCode: string;

  /** items.txt `type2`; empty when the row declares only one. */
  readonly secondaryTypeCode: string;
}

/** A row of ItemTypes.txt. */
export interface ItemTypeRow {
  readonly row: number;
  readonly code: string;

  /** The `Class` column — empty unless the type is class-restricted. */
  readonly classCode: string;

  readonly isThrowable: boolean;
}

/** A row of colors.txt. The ROW INDEX is the palette-shift value items store. */
export interface ColorRow {
  readonly row: number;
  readonly code: string | null;
}

/** A row of gems.txt. */
export interface GemRow {
  readonly row: number;
  readonly code: string | null;

  /** The rune letter a runeword name is spelled with; empty for a gem. */
  readonly letter: string | null;
}

/** A row of skills.txt. */
export interface SkillRow {
  readonly skillId: number;
  readonly name: string | null;

  /** 0-6, or -1 when the skill belongs to no class. */
  readonly classId: number;

  readonly requiredLevel: number;
}

/** A row of charstats.txt. */
export interface CharacterClassRow {
  readonly classId: number;
  readonly allSkillsText: string | null;
  readonly classOnlyText: string | null;

  /** The three tab names, in tab order. */
  readonly skillTabTexts: readonly (string | null)[];
}

/** A row of monstats.txt, as far as the tooltip needs it. */
export interface MonsterRow {
  readonly monsterId: number;
  readonly name: string | null;
}

/** A row of MonType.txt. */
export interface MonsterTypeRow {
  readonly monsterTypeId: number;
  readonly name: string | null;
}
