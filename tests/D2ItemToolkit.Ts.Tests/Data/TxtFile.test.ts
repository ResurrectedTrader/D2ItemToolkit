import { describe, expect, it } from 'vitest';
import { TxtFile } from '../../../src/D2ItemToolkit.Ts/src/Data/TxtFile.js';

// Ported from RegressionTests.cs. Every rule here comes from
// STRUCT_CreateBinFieldExcelAndFillData and TXT_ParseDataAndPutIntoStructure.

describe('TxtFile row and cell rules', () => {
  it('drops a final line with no terminator', () => {
    // The row counter increments only at 0x6bd737, reached through the CR/LF test at
    // 0x6bd72c / 0x6bd733, so an unterminated last line is not a record.
    expect(TxtFile.parse('h1\th2\r\na\tb\r\nc\td').rowCount).toBe(1);
    expect(TxtFile.parse('h1\th2\r\na\tb\r\nc\td\r\n').rowCount).toBe(2);
  });

  it('does not trim cells', () => {
    // The tokenizer NUL-terminates only at the tab (0x6bd71c) and the key converters compare
    // verbatim (0x524ca8), so a padded key misses where a trimmed one would hit.
    const padded = TxtFile.parse('k\r\n ModStr1a\r\n');
    expect(padded.getString(0, 'k')).toBe(' ModStr1a');
  });

  it('does not trim header names either', () => {
    // FOG_ParseBinField compares the field as tokenised (0x6bcf58, __strnicmp — case-insensitive
    // but never trimmed). A padded header does not bind, and its descriptor takes the
    // absent-column default (0x6bdfc5 writes 0).
    const paddedHeader = TxtFile.parse(' descfunc\tok\r\n1\t2\r\n');

    expect(paddedHeader.hasColumn('descfunc')).toBe(false);
    expect(paddedHeader.hasColumn(' descfunc')).toBe(true);
    expect(paddedHeader.hasColumn('ok')).toBe(true);
  });

  it('binds header names case-insensitively', () => {
    const file = TxtFile.parse('DescFunc\tOK\r\n1\t2\r\n');

    expect(file.getString(0, 'descfunc')).toBe('1');
    expect(file.getString(0, 'ok')).toBe('2');
  });

  it('keeps interior blank lines because the row index is the record id', () => {
    const file = TxtFile.parse('a\r\n1\r\n\r\n3\r\n');

    expect(file.rowCount).toBe(3);
    expect(file.getString(1, 'a')).toBe('');
    expect(file.getString(2, 'a')).toBe('3');
  });

  it('treats a bare LF as cell content, not a row break', () => {
    // The scanner tests only TAB (0x6bd718) and CR (0x6bd722); 0x0A matches neither. Splitting on
    // '\n' would let one stray byte renumber every record id after it.
    const file = TxtFile.parse('a\tb\r\n1\tx\ny\r\n2\tz\r\n');

    expect(file.rowCount).toBe(2);
    expect(file.getString(0, 'b')).toBe('x\ny');
  });

  it('rejects a carriage return that is not part of a CRLF', () => {
    // 0x6bd733: a CR must be followed by LF or the game halts.
    expect(() => TxtFile.parse('a\tb\r\n1\tx\ry\r\n')).toThrow();
  });

  it('rejects more than 280 header fields', () => {
    // 0x6bd6f6 `cmp eax, 118h` / 0x6bd6fb `jbe`: the column map is a _WORD[280].
    const wide = new Array<string>(281).fill('h').join('\t');
    expect(() => TxtFile.parse(`${wide}\r\n`)).toThrow();

    const atLimit = new Array<string>(280).fill('h').join('\t');
    expect(() => TxtFile.parse(`${atLimit}\r\n`)).not.toThrow();
  });
});

describe('TxtFile integer parsing', () => {
  it('accumulates over every byte after one optional minus', () => {
    // 0x6bde10 / 0x6bde15, with no digit test. "3x" is 102: '3' gives 3, then
    // 3*10 + (120 - 48) = 102. "+5" is -45: '+' gives 43 - 48 = -5, then -5*10 + (53 - 48) = -45.
    const numbers = TxtFile.parse('n\r\n3x\r\n+5\r\n-12\r\n7\r\n');

    expect(numbers.getInt(0, 'n')).toBe(102);
    expect(numbers.getInt(1, 'n')).toBe(-45);
    expect(numbers.getInt(2, 'n')).toBe(-12);
    expect(numbers.getInt(3, 'n')).toBe(7);
  });

  it('sign-extends a byte at or above 0x80', () => {
    // 0x6bde20 `movsx ecx, cl`: 0xC3 contributes -61, not 195.
    const file = TxtFile.parse(`n\r\n${String.fromCharCode(0xc3)}\r\n`);

    expect(file.getInt(0, 'n')).toBe(0xc3 - 0x100 - 0x30);
  });

  it('returns the fallback for an empty cell', () => {
    const file = TxtFile.parse('n\ta\r\n\t1\r\n');

    expect(file.getInt(0, 'n', 7)).toBe(7);
    expect(file.getInt(0, 'a', 7)).toBe(1);
  });

  it('returns the fallback for an absent column', () => {
    const file = TxtFile.parse('other\r\nx\r\n');

    expect(file.hasColumn('descstrpos')).toBe(false);
    expect(file.getInt(0, 'descstrpos', 3)).toBe(3);
  });

  it('sets a bit column on any non-zero value', () => {
    // TXTFIELD_BIT tests with `test eax, eax` / `jnz` at 0x6bde7c / 0x6bde7e, so "2", "-1" and
    // "01" are all true where a "1"/"true" test would read them as false.
    const file = TxtFile.parse('b\r\n1\r\n0\r\n2\r\n-1\r\n01\r\n\r\n');

    expect(file.getBool(0, 'b')).toBe(true);
    expect(file.getBool(1, 'b')).toBe(false);
    expect(file.getBool(2, 'b')).toBe(true);
    expect(file.getBool(3, 'b')).toBe(true);
    expect(file.getBool(4, 'b')).toBe(true);
  });
});

describe('TxtFile header binding', () => {
  it('binds only the first of a duplicate header name', () => {
    // Only the first matching header column binds (0x6bd00f). Shipped headers really do carry
    // duplicates and blanks — armor.txt is 164 fields but 162 names.
    const dupes = TxtFile.parse('a\tb\ta\tc\r\n1\t2\t3\t4\r\n');

    const names = dupes.columnNames;
    expect(names.length).toBe(4);
    expect(names[0]).toBe('a');
    expect(names[1]).toBe('b');
    expect(names[2]).toBe('');
    expect(names[3]).toBe('c');

    expect(dupes.getString(0, 'a')).toBe('1');
  });

  it('sizes the header by width, not by distinct-name count', () => {
    const blank = TxtFile.parse('a\t\tc\r\n1\t2\t3\r\n');

    expect(blank.columnNames.length).toBe(3);
    expect(blank.getString(0, 'c')).toBe('3');
  });
});

describe('TxtFile Expansion splice', () => {
  it('drops a row whose first cell is exactly Expansion', () => {
    // Case-SENSITIVE and untrimmed (_strncmp over 10 bytes at 0x6bd742). Keeping the row would
    // shift every record id after it.
    const exact = TxtFile.parse('a\tb\r\n1\tx\r\nExpansion\t\r\n2\ty\r\n');
    expect(exact.rowCount).toBe(2);
    expect(exact.getString(1, 'a')).toBe('2');
  });

  it('keeps a differently cased or padded divider', () => {
    // objgroup.txt spells it "EXPANSION" and objgroup.bin proves the compiler kept that one.
    const shouted = TxtFile.parse('a\tb\r\n1\tx\r\nEXPANSION\t\r\n2\ty\r\n');
    expect(shouted.rowCount).toBe(3);

    const padded = TxtFile.parse('a\tb\r\n1\tx\r\n Expansion\t\r\n2\ty\r\n');
    expect(padded.rowCount).toBe(3);
  });
});

describe('TxtFile byte preservation', () => {
  it('maps each byte to one character without decoding', () => {
    // 0x6bd714 `mov al,[esi]` — the compiler never decodes. UTF-8 decoding would fold invalid
    // bytes to U+FFFD; objects.txt carries two 0x85 and UniqueItems.txt one 0x92.
    const bytes = Uint8Array.from([
      0x6b,
      0x0d,
      0x0a, // "k\r\n"
      0x92,
      0x85,
      0x0d,
      0x0a,
    ]);

    const file = TxtFile.load(bytes);

    expect(file.rowCount).toBe(1);
    expect(file.getString(0, 'k')).toBe(String.fromCharCode(0x92, 0x85));
  });
});
