import { describe, expect, it } from 'vitest';
import { AnimDataFile } from '../../../src/D2ItemToolkit.Ts/src/Data/AnimDataFile.js';

// ANIMDATA_GetRecordByNameHash 0x66a8f0. The C# exercises this through AttackSpeedTests; these
// drive the parser's own edges, which that route cannot reach.

function build(records: readonly { name: string; frames: number; speed: number }[]): Uint8Array {
  const bytes = new Uint8Array(
    AnimDataFile.BucketCount * 4 + records.length * AnimDataFile.RecordSize,
  );
  const view = new DataView(bytes.buffer);

  const buckets: { name: string; frames: number; speed: number }[][] = [];
  for (let i = 0; i < AnimDataFile.BucketCount; ++i) {
    buckets.push([]);
  }

  for (const record of records) {
    (buckets[AnimDataFile.hash(record.name)] as (typeof records)[number][]).push(record);
  }

  // Each bucket is a count followed inline by exactly that many records.
  let at = 0;

  for (const bucket of buckets) {
    view.setInt32(at, bucket.length, true);
    at += 4;

    for (const record of bucket) {
      for (let i = 0; i < record.name.length; ++i) {
        bytes[at + i] = record.name.charCodeAt(i);
      }

      view.setInt32(at + 8, record.frames, true);
      view.setInt32(at + 12, record.speed, true);
      at += AnimDataFile.RecordSize;
    }
  }

  return bytes;
}

describe('AnimDataFile', () => {
  it('matches on the upper-cased name and reports the two fields', () => {
    const file = AnimDataFile.parse(build([{ name: 'PAA11HS', frames: 15, speed: 256 }]));

    expect(file.rowCount).toBe(1);
    expect(file.tryGet('PAA11HS')).toEqual({ framesPerDirection: 15, animationSpeed: 256 });

    // 0x66a8ff folds only a-z, so the lookup is case-insensitive over ASCII letters.
    expect(file.tryGet('paa11hs')).toEqual({ framesPerDirection: 15, animationSpeed: 256 });
    expect(file.tryGet('PAA11HT')).toBeNull();
  });

  it('rejects a name that cannot fit the eight-byte field', () => {
    const file = AnimDataFile.parse(build([{ name: 'PAA11HS', frames: 15, speed: 256 }]));

    expect(file.tryGet('')).toBeNull();
    expect(file.tryGet(null)).toBeNull();
    expect(file.tryGet('PAA11HSX')).toBeNull(); // exactly eight, so it is looked up and misses
    expect(file.tryGet('PAA11HSXY')).toBeNull();
  });

  it('keeps the first of two records sharing a name', () => {
    // Duplicate names exist; the scan returns the FIRST match in bucket order.
    const file = AnimDataFile.parse(
      build([
        { name: 'DUPE', frames: 1, speed: 2 },
        { name: 'DUPE', frames: 3, speed: 4 },
      ]),
    );

    expect(file.rowCount).toBe(1);
    expect(file.tryGet('DUPE')).toEqual({ framesPerDirection: 1, animationSpeed: 2 });
  });

  it('is empty for absent bytes and halts on a truncated file', () => {
    expect(AnimDataFile.parse(null).rowCount).toBe(0);

    expect(() => AnimDataFile.parse(new Uint8Array(2))).toThrow(/block count for bucket 0/);

    const overrun = new Uint8Array(AnimDataFile.BucketCount * 4);
    new DataView(overrun.buffer).setInt32(0, 1000, true);
    expect(() => AnimDataFile.parse(overrun)).toThrow(/runs past the end of the file/);
  });
});
