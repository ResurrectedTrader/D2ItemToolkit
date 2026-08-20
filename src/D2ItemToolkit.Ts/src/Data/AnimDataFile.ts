/** One AnimData.D2 record — `AnimDataFile.Record` in the C#. */
export interface AnimDataRecord {
  framesPerDirection: number;
  animationSpeed: number;
}

/**
 * AnimData.D2, looked up the way ANIMDATA_GetRecordByNameHash does it (0x66a8f0): the name is
 * upper-cased, hashed by summing its bytes into a byte, and matched EXACTLY over eight bytes
 * including the NUL padding. 256 buckets, each a count followed by that many 160-byte records.
 */
export class AnimDataFile {
  static readonly BucketCount = 256;
  static readonly RecordSize = 160;
  static readonly NameLength = 8;

  private static readonly FramesOffset = 8;
  private static readonly SpeedOffset = 12;

  // Ordinal, matching the C# dictionary.
  private readonly records = new Map<string, AnimDataRecord>();

  private constructor() {}

  get rowCount(): number {
    return this.records.size;
  }

  static parse(bytes: Uint8Array | null): AnimDataFile {
    const file = new AnimDataFile();
    if (bytes === null) {
      return file;
    }

    const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);

    let at = 0;
    for (let bucket = 0; bucket < AnimDataFile.BucketCount; ++bucket) {
      if (at + 4 > bytes.length) {
        throw new Error('AnimData.D2 ends inside the block count for bucket ' + bucket + '.');
      }

      const count = view.getInt32(at, true);
      at += 4;

      if (count < 0 || at + count * AnimDataFile.RecordSize > bytes.length) {
        throw new Error(
          'AnimData.D2 bucket ' +
            bucket +
            ' claims ' +
            count +
            ' records, which ' +
            'runs past the end of the file.',
        );
      }

      for (let i = 0; i < count; ++i, at += AnimDataFile.RecordSize) {
        const name = AnimDataFile.readName(bytes, at);
        if (name.length === 0) {
          continue;
        }

        const record: AnimDataRecord = {
          framesPerDirection: view.getInt32(at + AnimDataFile.FramesOffset, true),
          animationSpeed: view.getInt32(at + AnimDataFile.SpeedOffset, true),
        };

        // Duplicate names exist; the scan returns the FIRST match in bucket order.
        if (!file.records.has(name)) {
          file.records.set(name, record);
        }
      }
    }

    return file;
  }

  /** The C# `bool TryGet(string, out Record)` pair: null is the false case. */
  tryGet(name: string | null): AnimDataRecord | null {
    if (name === null || name.length === 0 || name.length > AnimDataFile.NameLength) {
      return null;
    }

    return this.records.get(AnimDataFile.upper(name)) ?? null;
  }

  /**
   * The bucket a name lands in: an unsigned byte sum over the upper-cased name (0x66a926).
   * Exposed because it is the only part of the lookup that is not an ordinary dictionary hit.
   */
  static hash(name: string): number {
    let sum = 0;
    const upper = AnimDataFile.upper(name);

    for (let i = 0; i < upper.length; ++i) {
      sum = (sum + upper.charCodeAt(i)) & 0xff;
    }

    return sum;
  }

  private static upper(name: string): string {
    // 0x66a8ff folds only a-z; every other byte passes through untouched.
    const chars: string[] = [];
    for (let i = 0; i < name.length; ++i) {
      const code = name.charCodeAt(i);
      chars.push(
        code >= 0x61 && code <= 0x7a ? String.fromCharCode(code - 32) : String.fromCharCode(code),
      );
    }

    return chars.join('');
  }

  private static readName(bytes: Uint8Array, at: number): string {
    let length = 0;
    while (length < AnimDataFile.NameLength && bytes[at + length] !== 0) {
      ++length;
    }

    let name = '';
    for (let i = 0; i < length; ++i) {
      name += String.fromCharCode(bytes[at + i] ?? 0);
    }

    return name;
  }
}
