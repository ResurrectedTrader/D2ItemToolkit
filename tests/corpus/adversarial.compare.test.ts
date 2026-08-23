import { readFileSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';

/**
 * Replays tests/corpus/adversarial.json against the TypeScript engine and diffs it against the C#
 * reference render, exactly as Differential.test.ts does for the shipped corpus. Writes every
 * mismatch to adversarial-mismatches.json so a failing run is inspectable rather than truncated.
 */

const corpusDir = fileURLToPath(new URL('./', import.meta.url));

interface ExpectedCase {
  name: string;
  views?: Record<string, Record<string, number>>;
  kind?: string;
  sections?: Record<string, string>;
  lines?: { section: string; color: number; text: string }[];
  rendered?: string;
  colored?: string;
  ranges?: unknown;
  error?: string;
}

interface CorpusCase {
  name: string;
  record: unknown;
  player?: unknown;
}

const corpus = JSON.parse(readFileSync(corpusDir + 'adversarial.json', 'utf8')) as CorpusCase[];
const expected = JSON.parse(
  readFileSync(corpusDir + 'adversarial-expected.json', 'utf8'),
) as ExpectedCase[];

// Differential.js, NOT index.js: `renderRecord` is the harness entry point and is deliberately
// absent from the package's public surface. Importing index.js left it undefined, the call threw a
// TypeError into the catch below, and every single case then "diverged" on the views layer — a
// green-to-red flip that said nothing about either engine.
const engine = (await import('../../src/D2ItemToolkit.Ts/src/Differential.js')) as Record<
  string,
  unknown
>;

describe('the two implementations agree on the adversarial corpus', () => {
  it('renders every case identically', () => {
    const renderRecord = engine['renderRecord'] as (
      record: unknown,
      player: unknown,
    ) => ExpectedCase;

    const mismatches: { name: string; layer: string; cs: string; ts: string }[] = [];

    for (let i = 0; i < corpus.length; ++i) {
      const testCase = corpus[i];
      const want = expected[i];
      if (testCase === undefined || want === undefined) continue;

      let got: ExpectedCase;
      try {
        got = renderRecord(testCase.record, testCase.player ?? null);
      } catch (e) {
        got = { name: testCase.name, error: (e as Error).constructor.name };
      }

      for (const layer of [
        'views',
        'kind',
        'sections',
        'lines',
        'rendered',
        'colored',
        // The reconstruction, which the shipped corpus compares but this one did not — so the
        // property handlers it reaches were policed only by cases built from the same tables they
        // read. These records are hostile, so this is where a handler that survives a legal roll
        // but not an implausible one shows up.
        'ranges',
        'error',
      ] as const) {
        const a = JSON.stringify(want[layer] ?? null);
        const b = JSON.stringify(got[layer] ?? null);
        if (a !== b) {
          mismatches.push({ name: want.name, layer, cs: a, ts: b });
          break;
        }
      }
    }

    writeFileSync(corpusDir + 'adversarial-mismatches.json', JSON.stringify(mismatches, null, 1));

    // eslint-disable-next-line no-console
    console.log(mismatches.length + ' / ' + corpus.length + ' cases diverge');

    expect(mismatches.length).toBe(0);
  });
});
