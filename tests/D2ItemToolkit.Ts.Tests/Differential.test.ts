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
  lines?: {
    section: string;
    color: number;
    statId: number;
    layer: number;
    shownStats: number[] | null;
    aggregated: boolean;
    text: string;
  }[];
  rendered?: string;
  colored?: string;
  ranges?: PackedRanges;
  mergedStats?: PackedMergedStats;
  annotated?: string;
  socketsSplit?: string;
  breakdown?: PackedBreakdown;
  error?: string;
}

/** The `ranges` object, as `PackRanges` in tools/Reference/Program.cs emits it. */
interface PackedRanges {
  stats: {
    stat: number;
    layer: number;
    low: number;
    high: number;
    displayLow: number;
    displayHigh: number;
    sources: number;
  }[];
  layerVaries: {
    stat: number;
    layerLow: number;
    layerHigh: number;
    value: number;
    sources: number;
  }[];
  outOfRange: number[];
  unattributed: number[];
  itemLevelDependent: number[];
  unsupportedFuncs: number[];
  craftedRecipeUnknown: boolean;
  craftedRecipe: number;
}

/** The `mergedStats` object, as `PackMergedStats` in tools/Reference/Program.cs emits it. */
interface PackedMergedStats {
  stats: { stat: number; layer: number; value: number }[];
  excludedPackedStats: number[];
}

/** The four breakdown buckets as text, as `Breakdown` in tools/Reference/Program.cs emits them. */
interface PackedBreakdown {
  base: string[];
  magic: string[];
  sockets: string[];
  setBonuses: string[];
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

  it('reaches the roll-range branches no rendered line touches', () => {
    // The reconstruction is the only thing that puts the affix, unique, runeword and superior
    // property handlers in front of the differential, and two of its branches are reached by
    // nothing else in the corpus. If this stops holding, the ranges layer is comparing empty
    // objects and passing vacuously.
    const ranges = expected.map(c => c.ranges).filter(r => r !== undefined);

    expect(ranges.length).toBe(expected.length);
    expect(ranges.filter(r => r.stats.length > 0).length).toBeGreaterThan(500);

    // Funcs 12 and 36 — the roll picks the LAYER, not the value.
    expect(ranges.filter(r => r.layerVaries.length > 0).length).toBeGreaterThan(0);

    // The arms that need an item level and report themselves without one.
    expect(ranges.filter(r => r.itemLevelDependent.length > 0).length).toBeGreaterThan(0);

    // Every source the reconstruction can attribute, as a union of the masks seen.
    const masks = new Set<number>();
    for (const r of ranges) {
      for (const s of r.stats) {
        masks.add(s.sources);
      }
      for (const s of r.layerVaries) {
        masks.add(s.sources);
      }
    }

    const union = [...masks].reduce((a, b) => a | b, 0);
    for (const source of [1, 2, 4, 8, 16, 32, 64, 128, 256]) {
      expect(union & source, 'source ' + String(source) + ' unreached').not.toBe(0);
    }

    // Func 9 is the only unported handler and no shipped table carries it, so nothing in the
    // corpus may reach an unsupported func.
    expect(ranges.filter(r => r.unsupportedFuncs.length > 0)).toEqual([]);

    // outOfRange is NOT expected to be empty here: the corpus's stat values are hand-authored
    // rather than rolled, so the reconstruction correctly reports that they do not fit any span
    // the tables could produce. That it fires at all is what proves the check is live.
    expect(ranges.filter(r => r.outOfRange.length > 0).length).toBeGreaterThan(0);
  });

  it('derives set state from the viewer rather than defaulting it away', () => {
    // These cases carry no `set` override, so every layer here is what the engine worked out from
    // the wearer's carried items. Without them the derivation is compared on nothing.
    const named = new Map(expected.map(c => [c.name, c]));
    const at = (name: string): ExpectedCase | undefined => named.get(name);

    const pieceColors = (name: string): number[] =>
      (at(name)?.lines ?? []).filter(l => l.section === 'SetPieceList').map(l => l.color);

    // Two pieces carried, four in the set: owned pieces are green (2), the rest red (1). The old
    // empty default painted all four red, so this is the assertion that catches a regression to it.
    expect(pieceColors('setderive-two-worn').filter(c => c === 2).length).toBe(2);
    expect(pieceColors('setderive-two-worn').filter(c => c === 1).length).toBe(2);

    // THE discriminating pair. The same sickle on the alternate weapon set versus the active one:
    // owned either way, but only the active slot lights a bit and raises a tier. An engine that
    // conflated the two predicates would render these identically.
    expect(at('setderive-weapon-swap')?.rendered).not.toBe(at('setderive-weapon-active')?.rendered);

    // A foreign set's piece must change nothing at all.
    expect(at('setderive-foreign-piece')?.rendered).toBe(at('setderive-two-worn')?.rendered);

    // The full-set block was DEAD on this path — render passed isEquipped false, so it returned
    // empty no matter what the wearer had. It must appear when the set is worn...
    const hasFullSet = (name: string): boolean =>
      (at(name)?.lines ?? []).some(l => l.section === 'FullSetBonus');

    expect(hasFullSet('setderive-full-set')).toBe(true);

    // ...and still be suppressed when the hovered piece itself is not equipped.
    expect(hasFullSet('setderive-hovered-loose')).toBe(false);
  });

  it('pins a crafted recipe, and declines to pin the three that cannot be', () => {
    // Crafted identification is the one layer whose answer is an index into a table rather than a
    // rendered string, so a divergence here would otherwise surface only as a missing span. Both
    // outcomes have to be present: pinning nothing at all would pass the "agrees" test vacuously.
    const named = new Map(expected.map(c => [c.name, c]));
    const recipeOf = (name: string): number => named.get(name)?.ranges?.craftedRecipe ?? -2;

    const pinnedNames = [
      'crafted-safety-helm',
      'crafted-hitpower-helm',
      'crafted-blood-weapon',
      'crafted-caster-amulet',
      'crafted-with-affix',
    ];

    for (const name of pinnedNames) {
      expect(recipeOf(name), name).toBeGreaterThanOrEqual(0);

      // The row index alone is not the payoff: a recipe pinned but applied with too few of its mods
      // keeps the index right while a stat it explains silently loses its span and lands here.
      expect(named.get(name)?.ranges?.unattributed, name).toEqual([]);
    }

    // Two families fit; a bow reaches four candidates and no viable one; a charm is in no craft
    // slot at all. Three reasons, same refusal.
    expect(recipeOf('crafted-ambiguous')).toBe(-1);
    expect(recipeOf('crafted-no-viable-recipe')).toBe(-1);
    expect(recipeOf('crafted-no-recipe-slot')).toBe(-1);

    // The four families are distinguished by the item's stats alone, so no two of these may land on
    // the same row.
    const pinned = [
      recipeOf('crafted-safety-helm'),
      recipeOf('crafted-hitpower-helm'),
      recipeOf('crafted-blood-weapon'),
      recipeOf('crafted-caster-amulet'),
    ];
    expect(new Set(pinned).size).toBe(pinned.length);
  });

  it('puts every crafted recipe in front of the differential', () => {
    // The named cases above reach four rows. Six of the nine slots and 30 of the 36 rows were
    // otherwise touched by nothing, so a slot derivation that broke for belts — or a family whose
    // stats stopped separating — would have diverged silently between the two engines.
    const sweep = expected.filter(c => c.name.startsWith('craftsweep-'));
    expect(sweep.length).toBe(36);

    // One row each, all distinct: the sweep builds every item from its own recipe's stats, so
    // anything less means two recipes are no longer told apart.
    const rows = sweep.map(c => c.ranges?.craftedRecipe ?? -1);
    expect(rows.filter(r => r < 0)).toEqual([]);
    expect(new Set(rows).size).toBe(36);

    // Nothing left over and nothing out of span. These are the two assertions that catch a recipe
    // pinned but applied with the wrong mods: the row index stays right while a stat it should
    // explain lands in `unattributed`, or its recorded value stops fitting the span claimed for it.
    for (const c of sweep) {
      expect(c.ranges?.unattributed, c.name).toEqual([]);
      expect(c.ranges?.outOfRange, c.name).toEqual([]);
    }
  });

  it('exercises both opt-in render modes', () => {
    // The annotated and socket-split layers would compare equal-and-empty if the corpus stopped
    // reaching them, and pass without testing anything. Each must differ from the plain render on a
    // real share of cases, and the annotation must actually emit spans.
    const annotated = expected.filter(c => c.annotated !== undefined);
    const split = expected.filter(c => c.socketsSplit !== undefined);

    expect(annotated.length).toBe(expected.length);
    expect(split.length).toBe(expected.length);

    expect(annotated.filter(c => (c.annotated ?? '').includes('[')).length).toBeGreaterThan(100);
    expect(annotated.filter(c => c.annotated !== c.colored).length).toBeGreaterThan(100);
    expect(split.filter(c => c.socketsSplit !== c.colored).length).toBeGreaterThan(100);

    // The range colour is set on the annotated layer, so its marker must appear somewhere.
    expect(annotated.some(c => (c.annotated ?? '').includes('ÿc0 ['))).toBe(true);
  });

  it('compares a breakdown whose buckets carry different spans', () => {
    // Breakdown was outside the differential entirely. Its whole point is that each bucket gets the
    // span matching ITS numbers, so the layer is only worth comparing if some case has a socket
    // bucket that differs from the item's own.
    const breakdowns = expected.map(c => c.breakdown).filter(b => b !== undefined);

    expect(breakdowns.length).toBe(expected.length);
    expect(breakdowns.filter(b => b.sockets.length > 0).length).toBeGreaterThan(20);
    expect(breakdowns.some(b => b.base.some(l => l.includes('[')))).toBe(true);
    expect(breakdowns.some(b => b.sockets.some(l => l.includes('[')))).toBe(true);
  });

  it('reaches a socketed set piece both worn and carried', () => {
    // The corpus has to hold a worn socketed SET item, because that is the one shape the game
    // treats specially (0x4c15fd) and so the one shape where an engine could plausibly grow a
    // discard on one side only. Both states need a case or half of it is unpoliced.
    const named = new Map(expected.map(c => [c.name, c]));

    const worn = named.get('set-socketed-um-worn');
    const bag = named.get('set-socketed-um-bag');

    expect(worn, 'set-socketed-um-worn').toBeDefined();
    expect(bag, 'set-socketed-um-bag').toBeDefined();

    // The Um's line is present either way. This is what fails if an engine reintroduces the
    // discard, and it is compared C# against TS by the layer walk below.
    for (const c of [worn, bag]) {
      expect((c?.rendered ?? '').includes('All Resistances +15'), c?.name).toBe(true);
    }

    // And the totals do not move either, so no surface disagrees with another about it.
    const resists = (c?: ExpectedCase): number[] =>
      (c?.mergedStats?.stats ?? [])
        .filter(s => [39, 41, 43, 45].includes(s.stat))
        .map(s => s.value);

    expect(resists(worn)).toEqual(resists(bag));
    expect(resists(worn)).not.toEqual([]);
  });

  it('covers a filler that carries its own rolled affixes', () => {
    // A jewel contributes nothing through gems.txt, so its roll reaches the host only through its
    // own affixes. Without a case like this the summing — merged span = item + jewel, each split
    // block = one half — is covered by hand-written tests alone.
    const named = new Map(expected.map(c => [c.name, c]));

    for (const name of ['jewel-sharedstat', 'jewel-and-gem', 'jewel-only']) {
      const c = named.get(name);
      expect(c, name).toBeDefined();

      const jewelBlock = (c?.socketsSplit ?? '').includes('Jewel');
      expect(jewelBlock, name + ' has no jewel block').toBe(true);
    }

    // The shared-stat case is the discriminating one: the merged span must be WIDER than the item's
    // own, because it holds both halves. If those ever match, the summing has been lost.
    const shared = named.get('jewel-sharedstat');
    expect((shared?.annotated ?? '').includes('[16-30]')).toBe(true);
    expect((shared?.socketsSplit ?? '').includes('[11-20]')).toBe(true);
    expect((shared?.socketsSplit ?? '').includes('[5-10]')).toBe(true);
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
        // The reconstruction is compared here rather than in its own suite because it is the only
        // thing that puts the affix, unique, runeword and superior property handlers in front of the
        // differential — no rendering path reaches them.
        'ranges',
        // The merged TOTALS. Nothing above reaches them: they fold the gems.txt synthesis and
        // op 13 into one view, which no render path does.
        'mergedStats',
        // The two opt-in render modes. Their formatter, colour wrapping and block layout are
        // otherwise covered only by hand-written tests on each side, which cannot catch the two
        // implementations agreeing to differ.
        'annotated',
        'socketsSplit',
        'breakdown',
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
