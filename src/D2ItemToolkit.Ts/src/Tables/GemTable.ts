import type { GemRow } from './TableRows.js';
import type { ItemTable } from './ItemTable.js';
import type { TxtFile } from '../Data/TxtFile.js';

/**
 * gems.txt plus the dwGemOffset back-reference the loader writes into items.txt at 0x637245.
 * Nothing resolves a gem by item code at runtime: callers read ItemsTxt.dwGemOffset (+0xF0) and
 * index gems.txt with it directly (TXT_Gems_GetLine, 0x6372c0, stride 192).
 */
export class GemTable {
  private readonly gems: TxtFile | null;
  private readonly offsetByClassId: Map<number, number>;

  constructor(gems: TxtFile | null, items: ItemTable | null) {
    this.gems = gems ?? null;
    this.offsetByClassId = new Map<number, number>();

    if (gems === null || gems === undefined || items === null || items === undefined) {
      return;
    }

    // The loop bound is the GEMS row count, not the items count (0x637243), so it clears
    // dwGemOffset only for the first N item rows. Everything else keeps the zero left by the
    // calloc, which is why readers test `> 0` rather than `>= 0` — item row 0 and a gem that
    // lands on gems row 0 are indistinguishable from "not a gem".
    for (let i = 0; i < gems.rowCount; ++i) {
      this.offsetByClassId.set(i, -1);

      const classId = items.classIdForCode(this.code(i));
      if (classId >= 0) {
        this.offsetByClassId.set(classId, i);
      }
    }
  }

  get rowCount(): number {
    return this.gems === null ? 0 : this.gems.rowCount;
  }

  /**
   * The gems row for a socket filler, or -1 when the item is not one. `TXT_Gems_GetLine`
   * 0x6372c0 rejects only `row >= recordCount` (0x6372cc) and exactly -1 (0x6372d1), so
   * **row 0 is valid** — it is `gcv`, the Chipped Amethyst. `TXT_AllocTxt_gems` writes the
   * index into items row +0xF0 at 0x637279 and writes a literal 0 on its first iteration.
   */
  rowForFillerClassId(classId: number): number {
    const row = this.offsetByClassId.get(classId);
    if (row === undefined || row < 0) {
      return -1;
    }

    return row < this.rowCount ? row : -1;
  }

  /**
   * The same lookup for the rune-letter writer, which additionally drops row 0. That is the
   * `jle` at 0x4866e9 — it belongs to INV_FormatRunewordName 0x486670, NOT to the
   * socket-filler path, and it sits behind an IsOfType(rune) test at 0x4866d6, so no rune
   * ever occupies row 0 and the difference is unobservable.
   */
  rowForRuneClassId(classId: number): number {
    const row = this.rowForFillerClassId(classId);
    return row > 0 ? row : -1;
  }

  /**
   * The letter shown for a socketed rune. Read straight off the record as raw characters
   * (UTF8_ConvertToWideChar over 6 bytes at gems row +0x20), never through the string table.
   */
  letter(row: number): string | null {
    if (
      this.gems === null ||
      row < 0 ||
      row >= this.gems.rowCount ||
      !this.gems.hasColumn('letter')
    ) {
      return null;
    }

    const letter = this.gems.getString(row, 'letter');
    return letter.length === 0 ? null : letter;
  }

  /**
   * The three property quadruples for one destination slot. The runtime layout is
   * pProperties[3][3] at gems row +0x30, so slot 0 is the weapon mods, 1 the helm mods and 2
   * the shield mods (ITEMMOD_GetMaxLevelAtIndex 0x65c6d0).
   */
  *properties(row: number, slot: number) {
    if (this.gems === null || row < 0 || row >= this.gems.rowCount || slot < 0 || slot > 2) {
      return;
    }

    const prefix = GemTable.SlotPrefixes[slot] ?? '';

    for (let mod = 1; mod <= 3; ++mod) {
      const stem = prefix + 'Mod' + String(mod);

      // gems.txt's {code, param, min, max} quadruple.
      yield {
        propertyId:
          this.propertyIds === null ? -1 : this.propertyIds(this.cell(row, stem + 'Code')),
        param: this.intCell(row, stem + 'Param'),
        min: this.intCell(row, stem + 'Min'),
        max: this.intCell(row, stem + 'Max'),
      };
    }
  }

  private static readonly SlotPrefixes: readonly string[] = ['weapon', 'helm', 'shield'];

  private propertyIds: ((code: string) => number) | null = null;

  /**
   * The mod code columns hold property NAMES; the loader resolves them to Properties.txt rows
   * at compile time (TXTFIELD_NAMETODWORD via pPropertiesLinker), so the resolver is injected.
   */
  resolvePropertyCodesWith(resolver: ((code: string) => number) | null): void {
    this.propertyIds = resolver;
  }

  private cell(row: number, column: string): string {
    const gems = this.gems;
    if (gems === null) {
      return '';
    }

    return gems.hasColumn(column) ? gems.getString(row, column) : '';
  }

  private intCell(row: number, column: string): number {
    const gems = this.gems;
    if (gems === null) {
      return 0;
    }

    return gems.hasColumn(column) ? gems.getInt(row, column) : 0;
  }

  /** The whole row, or null when the index is out of range. */
  rowAt(row: number): GemRow | null {
    return row < 0 || row >= this.rowCount
      ? null
      : { row, code: this.code(row), letter: this.letter(row) };
  }

  code(row: number): string | null {
    if (
      this.gems === null ||
      row < 0 ||
      row >= this.gems.rowCount ||
      !this.gems.hasColumn('code')
    ) {
      return null;
    }

    return this.gems.getString(row, 'code');
  }
}
