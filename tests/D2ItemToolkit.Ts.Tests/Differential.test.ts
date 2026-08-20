import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';

/**
 * The acceptance test for the TypeScript port: replay the corpus the C# engine rendered and
 * require agreement, case by case.
 *
 * Regenerate the reference after any C# change:
 *   dotnet run --project tools/Corpus    -c Release -- tests/corpus/corpus.json
 *   dotnet run --project tools/Reference -c Release -- tests/corpus/corpus.json tests/corpus/expected.json
 *
 * Cases are compared in layers — views, then sections, then the rendered string — because knowing
 * WHICH layer diverged localises the fault. A wrong merged view and a wrong writer produce the
 * same final mismatch but are nowhere near each other in the code.
 */

const corpusDir = fileURLToPath(new URL('../corpus/', import.meta.url));

interface ExpectedCase {
  name: string;
  views?: Record<string, Record<string, number>>;
  kind?: string;
  genericRefusal?: string;
  set?: unknown;
  sections?: Record<string, string>;
  lines?: { section: string; color: number; text: string }[];
  rendered?: string;
  colored?: string;
  error?: string;
}

interface CorpusCase {
  name: string;
  record: unknown;
  player?: unknown;
  set?: unknown;
  shopMode?: number;
}

const corpus = JSON.parse(readFileSync(corpusDir + 'corpus.json', 'utf8')) as CorpusCase[];
const expected = JSON.parse(readFileSync(corpusDir + 'expected.json', 'utf8')) as ExpectedCase[];

describe('corpus', () => {
  it('is generated and non-trivial', () => {
    expect(corpus.length).toBeGreaterThan(500);
    expect(expected.length).toBe(corpus.length);
  });

  it('covers every tooltip kind the composer can classify', () => {
    const kinds = new Set(expected.map(c => c.kind).filter(Boolean));

    expect(kinds).toContain('Generic');
    expect(kinds).toContain('Book');
    expect(kinds).toContain('IdentifiedSetItem');
  });

  it('exercises the refusal path', () => {
    // A set item is refused by the generic Compose and drawn by ComposeSetItem instead. Both
    // halves are compared: `genericRefusal` records the refusal for every set-item case, so the
    // TypeScript must refuse the same ones rather than rendering something plausible.
    expect(expected.some(c => c.genericRefusal === 'NotSupportedException')).toBe(true);
    expect(
      expected.filter(c => c.kind === 'IdentifiedSetItem').every(c => c.genericRefusal !== 'none'),
    ).toBe(true);
  });

  it('draws the set-item writer rather than refusing it', () => {
    const setCases = expected.filter(c => c.kind === 'IdentifiedSetItem');

    expect(setCases.length).toBeGreaterThan(50);
    expect(setCases.some(c => (c.rendered ?? '').length > 0)).toBe(true);

    // The four buffers only ITEM_BuildSetItemTooltip builds.
    const sections = new Set(setCases.flatMap(c => (c.lines ?? []).map(l => l.section)));
    expect(sections).toContain('SetPieceList');
    expect(sections).toContain('SetName');
    expect(sections).toContain('PartialSetBonus');
    expect(sections).toContain('FullSetBonus');
  });
});

/**
 * Enabled once the engine lands. Until then this file documents and guards the corpus itself —
 * a corpus that silently stopped covering Book or set items would make a later green run
 * meaningless.
 */
const engine = (await import('../../src/D2ItemToolkit.Ts/src/Differential.js')) as Record<
  string,
  unknown
>;
const engineReady = typeof engine['renderRecord'] === 'function';

describe.skipIf(!engineReady)('the two implementations agree', () => {
  it('renders every case identically', () => {
    const renderRecord = engine['renderRecord'] as (
      record: unknown,
      player: unknown,
      set: unknown,
      shopMode: number,
    ) => ExpectedCase;

    const mismatches: string[] = [];

    for (let i = 0; i < corpus.length; ++i) {
      const testCase = corpus[i];
      const want = expected[i];
      if (testCase === undefined || want === undefined) {
        continue;
      }

      let got: ExpectedCase;
      try {
        got = renderRecord(
          testCase.record,
          testCase.player ?? null,
          testCase.set ?? null,
          testCase.shopMode ?? 0,
        );
      } catch (e) {
        got = { name: testCase.name, error: (e as Error).constructor.name };
      }

      // Compare in layers so the first difference names the layer that broke.
      for (const layer of [
        'views',
        'kind',
        'genericRefusal',
        'set',
        'sections',
        'lines',
        'rendered',
        'colored',
        'error',
      ] as const) {
        const a = JSON.stringify(want[layer] ?? null);
        const b = JSON.stringify(got[layer] ?? null);
        if (a !== b) {
          mismatches.push(`${want.name} [${layer}]\n  C#: ${a}\n  TS: ${b}`);
          break;
        }
      }
    }

    expect(mismatches.slice(0, 20).join('\n\n')).toBe('');
    expect(mismatches.length).toBe(0);
  });
});
