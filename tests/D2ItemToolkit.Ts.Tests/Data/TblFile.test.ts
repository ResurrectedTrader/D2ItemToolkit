import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import { TblFile, TblStringTable } from '../../../src/D2ItemToolkit.Ts/src/Data/TblFile.js';
import { localeDirectory } from '../../../src/D2ItemToolkit.Ts/src/Data/TxtDataSource.js';
import { synthTbl } from '../SynthTbl.js';

function load(name: string): TblFile {
  return TblFile.parse(new Uint8Array(readFileSync(localeDirectory() + '/' + name)));
}

const strings = new TblStringTable(
  load('string.tbl'),
  load('patchstring.tbl'),
  load('expansionstring.tbl'),
);

describe('the shipped tables parse', () => {
  it('resolves the punctuation the engine depends on', () => {
    // These are load-bearing: 3998 terminates every stat line, 3994 is the colour marker prefix,
    // 3852 + 3995 is the ", " that joins a gem's socket-filler block.
    expect(strings.getByIndex(3998)).toBe('\n');
    expect(strings.getByIndex(3994)).toBe('ÿc');
    expect(strings.getByIndex(3852)).toBe(',');
    expect(strings.getByIndex(3995)).toBe(' ');
  });

  it('resolves ids from each of the three tables', () => {
    // base, patch (>= 10000) and expansion (>= 20000).
    expect(strings.getByIndex(3461)).toBe('Defense:');
    expect(strings.getByIndex(11080)).toBe('Can be Inserted into Socketed Items');
    expect(strings.getByIndex(20506)).toBeTruthy();
  });

  it('resolves a key through patch, expansion, then base', () => {
    // 0x524d93 / 0x524dc4 / 0x524de7. Searching base first produced 44 wrong fields against the
    // shipped itemstatcost.bin. ModStr1a is a real descstrpos key from ItemStatCost.txt.
    const index = strings.getIndexByKey('ModStr1a');

    expect(index).toBeGreaterThan(0);
    expect(strings.getByIndex(index)).toBe('to Strength');
  });
});

describe('the id cascade', () => {
  it('rewrites an expansion id to 11078 when there is no expansion table', () => {
    // 0x524a44. This is the classic-D2 path: ids >= 20000 cannot resolve, so the game
    // substitutes one fixed id rather than returning nothing.
    const noExpansion = new TblStringTable(load('string.tbl'), load('patchstring.tbl'), null);

    expect(noExpansion.getByIndex(20506)).toBe(strings.getByIndex(11078));
  });

  it('uses the LOW 16 BITS for the range TESTS only', () => {
    // 0x524a33 compares `si`, not `esi`. The mask decides WHICH TABLE answers; it is not applied
    // to the lookup index. So a high-half id still takes the expansion branch...
    const highHalf = 0x10000 + 20506;
    expect(strings.getByIndex(highHalf)).toBe(
      // ...and asks expansion for (id - 20000), which is out of range, so index 500 stands in.
      load('expansionstring.tbl').getByIndex(500),
    );
  });

  it('asks the base table for the id UNCHANGED, not masked', () => {
    // 0x524ab8. A high-half id whose low bits are a valid base id does NOT resolve to that base
    // string — the raw id is out of range and index 500 substitutes. Masking here would be a
    // plausible-looking bug that silently returns the wrong string.
    expect(strings.getByIndex(3461)).toBe('Defense:');
    expect(strings.getByIndex(0x10000 + 3461)).not.toBe('Defense:');
  });

  it('substitutes index 500 from whichever table the cascade reached', () => {
    // 999999 & 0xFFFF is 16959, which is >= 10000, so the PATCH table answers — and its
    // index 500 is the substitute, not the base table's.
    expect(strings.getByIndex(999999)).toBe(load('patchstring.tbl').getByIndex(500));
  });
});

describe('resolveKey', () => {
  // DescStringIds.DescStr2Sentinel — fixed inside resolveKey, as in the C#.
  const sentinel = 5382;

  it('returns the sentinel for an empty key', () => {
    expect(strings.resolveKey('')).toBe(sentinel);
  });

  it('returns the sentinel for an unknown key', () => {
    expect(strings.resolveKey('no-such-key-anywhere')).toBe(sentinel);
  });

  it('treats a base hit at index 0 as a miss', () => {
    // `> 0`, not `>= 0`: the converter writes 0 for both a hit at 0 and a miss, so they are
    // indistinguishable downstream. Break this and every stat keyed at base index 0 renders
    // whatever string.tbl holds there instead of "an evil force".
    //
    // This has to be driven with a SYNTHETIC table, because no real key lands at index 0. The
    // earlier version looked one up and guarded the assertion on it landing there — it never does
    // (`x` is 5101), so the test was green having executed no assertion at all.
    const table = new TblStringTable(synthTbl(['zeroth', 'first']), null, null);

    expect(table.getIndexByKey('zeroth')).toBe(0);
    expect(table.resolveKey('zeroth')).toBe(sentinel);
  });

  it('resolves a key that is NOT at index 0 to its own index', () => {
    // The other half of the same rule: `> 0` must not reject everything.
    const index = strings.getIndexByKey('ModStr1a');

    expect(index).toBeGreaterThan(0);
    expect(strings.resolveKey('ModStr1a')).toBe(index);
  });
});

describe('malformed input', () => {
  it('rejects a file shorter than the header', () => {
    expect(() => TblFile.parse(new Uint8Array(20))).toThrow(/21 byte header/);
  });

  it('rejects a hash table that runs past the end', () => {
    const bytes = new Uint8Array(32);
    const view = new DataView(bytes.buffer);
    view.setUint16(2, 1, true); // one element
    view.setUint32(4, 1000, true); // but a thousand hash slots

    expect(() => TblFile.parse(bytes)).toThrow(/past the end/);
  });
});
