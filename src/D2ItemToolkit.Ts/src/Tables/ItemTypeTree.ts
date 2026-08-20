import type { ItemTypeRow } from './TableRows.js';
import type { TxtFile } from '../Data/TxtFile.js';

// The Equiv1/Equiv2 closure from ItemTypes.txt. The game does not walk this at runtime: the
// loader bakes it into a bit matrix (allocated 0x639368, filled by ITEMTBLS_CheckItemTypeRecursive
// via `or [eax], ecx` at 0x6393c2) and IsOfType (0x629bb0) just probes it.
//
// Two parents, not one: 16 of the 103 post-splice rows set Equiv2, so this is a DAG walk.
export class ItemTypeTree {
  private readonly rowByCode: Map<string, number>;
  private readonly isUnderMatrix: boolean[][];
  private readonly throwable: boolean[];
  private readonly classCodes: string[];

  // The itemtypes `Code` column. NOT classCodes, which is the `Class` column — the character
  // class an item type is restricted to. Two different columns, easy to confuse.
  private readonly codes: string[];

  /**
   * The itemtypes `Class` restriction for this row, or empty when unrestricted. This is the
   * pair `TXT_ItemTypes_CheckClass` / `TXT_ItemTypes_GetClass` reads (record +0x21), and the
   * row is the item's PRIMARY type only (+0x11E) — there is no equivalence walk.
   */
  classCode(itemTypeRow: number): string {
    return itemTypeRow >= 0 && itemTypeRow < this.classCodes.length
      ? (this.classCodes[itemTypeRow] ?? '')
      : '';
  }

  /**
   * ITEMS_CheckItemTypeIfThrowable reads this row's own Throwable column with no equivalence
   * walk, so it is a flat lookup rather than a closure query.
   */
  isThrowable(itemTypeRow: number): boolean {
    return (
      itemTypeRow >= 0 &&
      itemTypeRow < this.throwable.length &&
      (this.throwable[itemTypeRow] ?? false)
    );
  }

  constructor(itemTypes: TxtFile | null | undefined) {
    if (itemTypes === null || itemTypes === undefined) {
      throw new Error('itemTypes');
    }

    const rows = itemTypes.rowCount;

    this.throwable = new Array<boolean>(rows).fill(false);
    const hasThrowable = itemTypes.hasColumn('Throwable');
    for (let row = 0; row < rows; ++row) {
      this.throwable[row] = hasThrowable && itemTypes.getInt(row, 'Throwable') !== 0;
    }

    this.classCodes = new Array<string>(rows).fill('');
    const hasClass = itemTypes.hasColumn('Class');
    for (let row = 0; row < rows; ++row) {
      this.classCodes[row] = hasClass ? itemTypes.getString(row, 'Class').trim() : '';
    }

    // OrdinalIgnoreCase, matching the C# dictionary.
    this.codes = new Array<string>(rows).fill('');
    this.rowByCode = new Map<string, number>();
    for (let row = 0; row < rows; ++row) {
      const code = itemTypes.getString(row, 'Code');
      this.codes[row] = code.trim();
      const key = code.toLowerCase();
      if (code.length !== 0 && !this.rowByCode.has(key)) {
        this.rowByCode.set(key, row);
      }
    }

    const parents: number[][] = new Array<number[]>(rows);
    for (let row = 0; row < rows; ++row) {
      parents[row] = [
        this.row(itemTypes.getString(row, 'Equiv1')),
        this.row(itemTypes.getString(row, 'Equiv2')),
      ];
    }

    this.isUnderMatrix = new Array<boolean[]>(rows);
    for (let row = 0; row < rows; ++row) {
      this.isUnderMatrix[row] = new Array<boolean>(rows).fill(false);
      this.markAncestors(row, row, parents);
    }
  }

  get rowCount(): number {
    return this.isUnderMatrix.length;
  }

  row(code: string | null | undefined): number {
    if (code === null || code === undefined || code.length === 0) {
      return -1;
    }

    const found = this.rowByCode.get(code.toLowerCase());
    return found === undefined ? -1 : found;
  }

  /** The itemtypes `Code` at a row, or empty when out of range. The inverse of `row`. */
  /** The whole row, or null when the index is out of range. */
  rowAt(itemTypeRow: number): ItemTypeRow | null {
    if (itemTypeRow < 0 || itemTypeRow >= this.rowCount) {
      return null;
    }

    return {
      row: itemTypeRow,
      code: this.codeAt(itemTypeRow),
      classCode: this.classCode(itemTypeRow),
      isThrowable: this.isThrowable(itemTypeRow),
    };
  }

  codeAt(itemTypeRow: number): string {
    return itemTypeRow >= 0 && itemTypeRow < this.codes.length
      ? (this.codes[itemTypeRow] ?? '')
      : '';
  }

  /**
   * Every type row at or below `itemTypeRow` — `swor` yields itself plus every sword-ish type
   * that chains up to it. REFLEXIVE: the row itself is always included, because `isUnder` is
   * reflexive and "all swords" has to include swords.
   *
   * This is the descending direction; `isUnder` and `isOfType` answer the ascending one. Both
   * read the same closure, so a type is in this list exactly when isUnder would say so — one
   * cannot drift from the other.
   *
   * Equiv1/Equiv2 make this a DAG rather than a chain, so the result is a SET in row order, not a
   * path. An unknown row yields nothing.
   */
  descendants(itemTypeRow: number): number[] {
    const rows: number[] = [];

    if (itemTypeRow < 0 || itemTypeRow >= this.isUnderMatrix.length) {
      return rows;
    }

    for (let row = 0; row < this.isUnderMatrix.length; ++row) {
      if (this.isUnderMatrix[row]?.[itemTypeRow] === true) {
        rows.push(row);
      }
    }

    return rows;
  }

  // True when itemTypeRow is queryRow or has it as an ancestor. Row indices, not codes.
  isUnder(itemTypeRow: number, queryRow: number): boolean {
    if (itemTypeRow < 0 || itemTypeRow >= this.isUnderMatrix.length) {
      return false;
    }

    return (
      queryRow >= 0 &&
      queryRow < this.isUnderMatrix.length &&
      (this.isUnderMatrix[itemTypeRow]?.[queryRow] ?? false)
    );
  }

  // IsOfType's two-type probe: a miss on the first type is retried against the second, which
  // must be > 0 (0x629c3b / 0x629c3e). Pass -1 for an absent second type.
  isOfType(primaryTypeRow: number, secondaryTypeRow: number, queryRow: number): boolean {
    if (this.isUnder(primaryTypeRow, queryRow)) {
      return true;
    }

    return secondaryTypeRow > 0 && this.isUnder(secondaryTypeRow, queryRow);
  }

  private markAncestors(start: number, at: number, parents: number[][]): void {
    const marks = this.isUnderMatrix[start];
    if (marks === undefined || at < 0 || marks[at] === true) {
      return;
    }

    marks[at] = true;

    for (const parent of parents[at] ?? []) {
      this.markAncestors(start, parent, parents);
    }
  }
}
