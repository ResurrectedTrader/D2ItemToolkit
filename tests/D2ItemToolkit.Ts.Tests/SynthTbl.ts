import { TblFile } from '../../src/D2ItemToolkit.Ts/src/Data/TblFile.js';

/**
 * Builds a `.tbl` in memory. Ported from `SynthTblBytes` in the C# RegressionTests.
 *
 * Needed because some rules cannot be driven from the shipped tables at all — the `> 0` versus
 * `>= 0` distinction in `resolveKey` only shows for a key at index 0, and no real key lands there.
 */
export function synthTbl(
  keys: string[],
  overrideIndex = -1,
  overrideValue: string | null = null,
): TblFile {
  return TblFile.parse(synthTblBytes(keys, overrideIndex, overrideValue));
}

export function synthTblBytes(
  keys: string[],
  overrideIndex: number,
  overrideValue: string | null,
): Uint8Array {
  const HeaderLength = 21;
  const NodeLength = 17;

  const indexBase = HeaderLength;
  const nodeBase = indexBase + keys.length * 2;
  const dataBase = nodeBase + keys.length * NodeLength;

  const encoder = new TextEncoder();
  const blob: number[] = [];
  const keyOffset = new Array<number>(keys.length);
  const valueOffset = new Array<number>(keys.length);
  const valueLength = new Array<number>(keys.length);

  for (let i = 0; i < keys.length; ++i) {
    keyOffset[i] = dataBase + blob.length;
    blob.push(...encoder.encode(keys[i] ?? ''));
    blob.push(0);

    valueOffset[i] = dataBase + blob.length;
    const value = i === overrideIndex ? overrideValue : 'V:' + (keys[i] ?? '');
    const encoded = encoder.encode(value ?? '');
    valueLength[i] = encoded.length;
    blob.push(...encoded);
    blob.push(0);
  }

  const bytes = new Uint8Array(dataBase + blob.length);
  const view = new DataView(bytes.buffer);
  view.setUint16(2, keys.length, true);
  view.setUint32(4, keys.length, true);

  for (let i = 0; i < keys.length; ++i) {
    view.setUint16(indexBase + i * 2, i, true);

    const at = nodeBase + i * NodeLength;
    bytes[at] = 1; // used
    view.setUint16(at + 1, i, true);
    view.setUint32(at + 7, keyOffset[i] ?? 0, true);
    view.setUint32(at + 11, valueOffset[i] ?? 0, true);

    // stringLength at +15, which the reader honours because the game does: shipped
    // tables always carry strlen + 1 here.
    view.setUint16(at + 15, (valueLength[i] ?? 0) + 1, true);
  }

  bytes.set(blob, dataBase);
  return bytes;
}
