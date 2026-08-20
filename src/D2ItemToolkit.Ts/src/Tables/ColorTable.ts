import type { ColorRow } from './TableRows.js';
import type { TxtFile } from '../Data/TxtFile.js';

/**
 * colors.txt, whose ROW INDEX is the palette-shift value everything else stores.
 *
 * This table exists here only because we embed the `.txt` rather than the `.bin`. The game's table
 * compiler resolves `transformcolor` / `invtransform` from a 4-char code to this row index at
 * load, so a consumer reading the compiled tables never needs the file at all — it reads an
 * integer. Ours still hold `lgld` / `bwht` / `cred`, so the mapping has to happen somewhere, and
 * it happens here.
 *
 * The file has no `Expansion` row, so it is NOT spliced by
 * STRUCT_CreateBinFieldExcelAndFillData and the row index is the literal 0-based position: 21
 * rows, `whit` = 0 through `bwht` = 20.
 */
export class ColorTable {
  /**
   * Above this is not a real inventory colour. 20 is `bwht`, the last row, so anything larger
   * came from a column that does not hold a colour at all.
   */
  static readonly MaxPaletteIndex = 20;

  /** No shift. Not a row — the columns use a missing/None cell to mean this. */
  static readonly None = -1;

  private readonly rowForCodeMap = new Map<string, number>();
  private readonly codes: string[] = [];

  constructor(colors: TxtFile | null) {
    if (colors === null) {
      return;
    }

    for (let row = 0; row < colors.rowCount; ++row) {
      const code = colors.getString(row, 'Code').trim();

      this.codes.push(code);

      // First wins: a duplicate code would otherwise silently re-point an earlier index.
      if (code.length !== 0 && !this.rowForCodeMap.has(code.toLowerCase())) {
        this.rowForCodeMap.set(code.toLowerCase(), row);
      }
    }
  }

  get rowCount(): number {
    return this.codes.length;
  }

  /**
   * The palette-shift index for a 4-char code, or `None` when the cell is empty or names no row.
   * An unknown code is treated as no shift rather than as row 0, which would silently paint
   * everything white.
   */
  rowForCode(code: string | null | undefined): number {
    if (code === null || code === undefined || code.length === 0) {
      return ColorTable.None;
    }

    return this.rowForCodeMap.get(code.trim().toLowerCase()) ?? ColorTable.None;
  }

  /** The code at a row, or empty when out of range. */
  /** The whole row, or null when the index is out of range. */
  rowAt(row: number): ColorRow | null {
    return row < 0 || row >= this.rowCount ? null : { row, code: this.codeAt(row) };
  }

  codeAt(row: number): string {
    return row >= 0 && row < this.codes.length ? (this.codes[row] ?? '') : '';
  }

  /**
   * A shift that is outside the table is not a colour. Mirrors the range test d2bsng applies to
   * every one of these lookups.
   */
  static clamp(shift: number): number {
    return shift < 0 || shift > ColorTable.MaxPaletteIndex ? ColorTable.None : shift;
  }
}
