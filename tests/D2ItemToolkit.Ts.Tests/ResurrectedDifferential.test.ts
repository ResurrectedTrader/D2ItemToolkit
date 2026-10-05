import { readdirSync, readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import { GameVariant } from '../../src/D2ItemToolkit.Ts/src/Data/GameVariant.js';
import { renderRecord } from '../../src/D2ItemToolkit.Ts/src/Differential.js';

/**
 * The D2R half of the differential. Two corpora, one per table set, each rendered by C# with every
 * layer; and the Reign of the Warlock corpus again in every one of D2R's thirteen locales, HD and
 * legacy text, keeping only the text layers — which is all a locale can change.
 *
 * Regenerate after any C# change:
 *   dotnet run --project tools/Corpus    -c Release -- tests/corpus/d2r-rotw-corpus.json ReignOfTheWarlock
 *   dotnet run --project tools/Corpus    -c Release -- tests/corpus/d2r-base-corpus.json Resurrected
 *   dotnet run --project tools/Reference -c Release -- d2r-suite tests/corpus/d2r-rotw-corpus.json tests/corpus/d2r-base-corpus.json tests/corpus
 */

const corpusDir = fileURLToPath(new URL('../corpus/', import.meta.url));

interface CorpusCase {
  name: string;
  record: unknown;
  player?: unknown;
  set?: unknown;
  shopMode?: number;
  difficulty?: number;
  desecratedZones?: boolean;
}

type ExpectedCase = { name: string } & Record<string, unknown>;

function load<T>(file: string): T {
  return JSON.parse(readFileSync(corpusDir + file, 'utf8')) as T;
}

const rotwCorpus = load<CorpusCase[]>('d2r-rotw-corpus.json');
const baseCorpus = load<CorpusCase[]>('d2r-base-corpus.json');

const FullLayers = [
  'views',
  'kind',
  'genericRefusal',
  'set',
  'sections',
  'lines',
  'rendered',
  'colored',
  'ranges',
  'mergedStats',
  'damage',
  'annotated',
  'socketsSplit',
  'breakdown',
  'error',
] as const;

const SlimLayers = ['sections', 'rendered', 'colored', 'error'] as const;

/** `d2r-rotw-deDE-legacy.json` → `deDE+legacy`, the key Differential.renderRecord understands. */
const LocaleFiles: readonly (readonly [string, string])[] = readdirSync(corpusDir + 'locales')
  .filter(file => file.startsWith('d2r-rotw-') && file.endsWith('.json'))
  .sort()
  .map(file => {
    const stem = file.substring('d2r-rotw-'.length, file.length - '.json'.length);
    return [file, stem.endsWith('-legacy') ? stem.replace('-legacy', '+legacy') : stem] as const;
  });

function compare(
  corpus: readonly CorpusCase[],
  reference: readonly ExpectedCase[],
  variant: GameVariant,
  language: string | null,
  layers: readonly string[],
): string[] {
  const mismatches: string[] = [];

  for (let i = 0; i < corpus.length; ++i) {
    const testCase = corpus[i];
    const want = reference[i];
    if (testCase === undefined || want === undefined) {
      continue;
    }

    let got: Record<string, unknown>;
    try {
      got = renderRecord(
        testCase.record,
        testCase.player ?? null,
        testCase.set ?? null,
        testCase.shopMode ?? 0,
        variant,
        testCase.difficulty ?? 0,
        testCase.desecratedZones ?? false,
        language,
      ) as Record<string, unknown>;
    } catch (e) {
      got = { error: (e as Error).constructor.name };
    }

    // Compare in layers so the first difference names the layer that broke.
    for (const layer of layers) {
      const a = JSON.stringify(want[layer] ?? null);
      const b = JSON.stringify(got[layer] ?? null);
      if (a !== b) {
        mismatches.push(`${testCase.name} [${layer}]\n  C#: ${a}\n  TS: ${b}`);
        break;
      }
    }
  }

  return mismatches;
}

describe('the D2R corpora', () => {
  it('are generated and reach the D2R-only cases', () => {
    expect(rotwCorpus.length).toBeGreaterThan(500);
    expect(baseCorpus.length).toBeGreaterThan(500);
    expect(LocaleFiles.length).toBe(26);

    const names = new Set(rotwCorpus.map(c => c.name));
    expect(names).toContain('d2r-xa1-hell-zones');
    expect(names).toContain('d2r-mastery-sword');
  });

  it('reach the D2R-only sections', () => {
    const reference = load<ExpectedCase[]>('d2r-rotw-expected.json');
    const sections = new Set(
      reference.flatMap(c => Object.keys((c['sections'] ?? {}) as Record<string, string>)),
    );
    expect(sections).toContain('BeltSize');
  });
});

describe('the two implementations agree on D2R', () => {
  it('Reign of the Warlock, every layer', () => {
    const mismatches = compare(
      rotwCorpus,
      load<ExpectedCase[]>('d2r-rotw-expected.json'),
      GameVariant.ReignOfTheWarlock,
      null,
      FullLayers,
    );
    expect(mismatches.slice(0, 20).join('\n\n')).toBe('');
  });

  it('Resurrected base tables, every layer', () => {
    const mismatches = compare(
      baseCorpus,
      load<ExpectedCase[]>('d2r-base-expected.json'),
      GameVariant.Resurrected,
      null,
      FullLayers,
    );
    expect(mismatches.slice(0, 20).join('\n\n')).toBe('');
  });

  it.each(LocaleFiles)('%s, the text layers', (file, language) => {
    const mismatches = compare(
      rotwCorpus,
      load<ExpectedCase[]>('locales/' + file),
      GameVariant.ReignOfTheWarlock,
      language,
      SlimLayers,
    );
    expect(mismatches.slice(0, 20).join('\n\n')).toBe('');
  });
});
