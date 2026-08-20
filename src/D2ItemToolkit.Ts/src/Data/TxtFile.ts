/**
 * A tab-separated game table, parsed the way the game's own compiler parses it rather than the way
 * a modern CSV reader would. Every deviation below is deliberate and cited.
 */
export class TxtFile {
  private readonly columns: Map<string, number>;
  private readonly rows: string[][];

  private constructor(columns: Map<string, number>, rows: string[][]) {
    this.columns = columns;
    this.rows = rows;
  }

  get rowCount(): number {
    return this.rows.length;
  }

  /**
   * Sized by HEADER WIDTH, not distinct-name count: shipped headers carry duplicates and blanks
   * (armor.txt is 164 fields / 162 names), and FOG_ParseBinField marks a descriptor used at
   * 0x6bd00f so only the first matching column binds.
   */
  get columnNames(): readonly string[] {
    let width = 0;
    for (const index of this.columns.values()) {
      if (index >= width) {
        width = index + 1;
      }
    }

    const names: string[] = new Array<string>(width).fill('');
    for (const [name, index] of this.columns) {
      names[index] = name;
    }

    return names;
  }

  private static readonly MaxHeaderFields = 280;

  /**
   * The compiler tokenizes RAW BYTES (0x6bd714 `mov al,[esi]`) and never decodes anything, so each
   * byte must survive as one char. Decoding as UTF-8 would fold every invalid byte to U+FFFD:
   * objects.txt (two 0x85) and UniqueItems.txt (one 0x92, in "Hunter's Bow") both contain bytes
   * that are not valid UTF-8. `latin1` is the one-byte-one-char mapping we need.
   */
  static load(bytes: Uint8Array): TxtFile {
    let text = '';
    const chunk = 0x8000;
    for (let i = 0; i < bytes.length; i += chunk) {
      text += String.fromCharCode(...bytes.subarray(i, Math.min(i + chunk, bytes.length)));
    }

    return TxtFile.parse(text);
  }

  private static splitCells(line: string): string[] {
    if (line.indexOf('\r') >= 0) {
      throw new Error(
        'Malformed .txt: a carriage return that is not part of a CRLF row terminator. ' +
          'The compiler halts on this at 0x6bd733.',
      );
    }

    return line.split('\t');
  }

  static parse(content: string): TxtFile {
    // Rows terminate on CRLF and ONLY CRLF. The scanner tests just TAB (0x6bd718) and CR
    // (0x6bd722), and a CR must be followed by LF or it halts (0x6bd733). 0x0A matches neither, so
    // a bare LF is ordinary CELL CONTENT. Splitting on '\n' would let one stray byte split a row
    // and renumber every record id after it.
    const lines = content.split('\r\n');

    const columns = new Map<string, number>();
    const header = TxtFile.splitCells(lines[0] ?? '');

    // 0x6bd6f6 `cmp eax, 118h` / 0x6bd6fb `jbe`: more than 280 header fields halts the game
    // (error 0x67). The column map is a _WORD[280]. Shipped maximum is skills.txt at 256.
    if (header.length > TxtFile.MaxHeaderFields) {
      throw new Error(
        `Malformed .txt: ${header.length} header fields exceeds the loader's limit of ` +
          `${TxtFile.MaxHeaderFields} (the game halts at 0x6bd6fd).`,
      );
    }

    for (let i = 0; i < header.length; ++i) {
      const name = header[i] ?? '';
      // Case-insensitive, first match wins — matching the C# OrdinalIgnoreCase dictionary.
      const key = name.toLowerCase();
      if (name.length !== 0 && !columns.has(key)) {
        columns.set(key, i);
      }
    }

    const rows: string[][] = [];
    for (let i = 1; i < lines.length; ++i) {
      // The row counter increments at exactly one site, 0x6bd737, reached only through the CRLF
      // test. An unterminated final line exits at 0x6bd728 with the counter untouched, so the game
      // DROPS it. Interior blank lines must be kept: row index is the record id.
      if (i === lines.length - 1) {
        continue;
      }

      const cells = TxtFile.splitCells(lines[i] ?? '');

      // The compiler SKIPS the "Expansion" divider row, so it must not consume a record id —
      // keeping it shifts Druid to 6 and Assassin to 7. The compare is ordinal, CASE-SENSITIVE and
      // untrimmed (_strncmp over 10 bytes at 0x6bd742): objgroup.txt spells it "EXPANSION" and
      // objgroup.bin proves the compiler kept that one.
      if (cells.length > 0 && cells[0] === 'Expansion') {
        continue;
      }

      rows.push(cells);
    }

    return new TxtFile(columns, rows);
  }

  columnIndex(name: string): number {
    const index = this.columns.get(name.toLowerCase());
    return index === undefined ? -1 : index;
  }

  hasColumn(name: string): boolean {
    return this.columnIndex(name) >= 0;
  }

  /**
   * Raw cell, NOT trimmed. The tokenizer NUL-terminates a field at the tab and nowhere else
   * (0x6bd71c), and the key converters copy it verbatim, so a padded key misses in the game where
   * a trimmed one would hit.
   */
  getString(row: number, column: number | string): string {
    const index = typeof column === 'string' ? this.columnIndex(column) : column;
    if (row < 0 || row >= this.rows.length || index < 0) {
      return '';
    }

    const cells = this.rows[row];
    if (cells === undefined) {
      return '';
    }

    return index < cells.length ? (cells[index] ?? '') : '';
  }

  /**
   * Reproduces the game's parser (0x6bde0d): one optional leading minus, then
   * `n = n * 10 + (b - 48)` over EVERY remaining byte with no digit test and no overflow check.
   * So "3x" is 102 and "+5" is -45. A strict parse would reject both and substitute 0, which is a
   * different value rather than a safer one.
   */
  getInt(row: number, column: number | string, fallback = 0): number {
    const text = this.getString(row, column);
    if (text.length === 0) {
      return fallback;
    }

    const negative = text.charCodeAt(0) === 0x2d; // '-'
    let value = 0;

    for (let i = negative ? 1 : 0; i < text.length; ++i) {
      // Each byte is SIGN-EXTENDED before it is accumulated (0x6bde20 `movsx ecx, cl`), so a byte
      // >= 0x80 contributes a NEGATIVE amount: 0xC3 is -61, not 195.
      const byte = text.charCodeAt(i) & 0xff;
      const signed = byte >= 0x80 ? byte - 0x100 : byte;

      // Math.imul keeps the multiply in 32-bit two's complement, matching C# `unchecked`.
      value = (Math.imul(value, 10) + (signed - 0x30)) | 0;
    }

    return negative ? -value | 0 : value;
  }

  /** TXTFIELD_BIT: ANY non-zero sets the bit (0x6bde7c / 0x6bde7e), so "2" and "-1" are true. */
  getBool(row: number, column: string): boolean {
    return this.getInt(row, column) !== 0;
  }

  /**
   * Case-INSENSITIVE, matching the C#'s `StringComparison.OrdinalIgnoreCase`. Three render-path
   * linkages resolve through here — `op base` into ItemStatCost, `skills.skilldesc` into
   * skilldesc.txt, and `Missiles.EType` into ElemTypes — and every one of them silently degrades
   * on a miss rather than failing: viewer-level scaling stops, a skill name becomes "an evil
   * force", a throwing potion loses its elemental colour. Shipped data happens to match case
   * exactly, so a bare `===` is dormant on vanilla and live on any re-cased or modded table.
   */
  findRow(column: string, value: string): number {
    const index = this.columnIndex(column);
    if (index < 0) {
      return -1;
    }

    const wanted = value.toLowerCase();
    for (let row = 0; row < this.rows.length; ++row) {
      if (this.getString(row, index).toLowerCase() === wanted) {
        return row;
      }
    }

    return -1;
  }
}
