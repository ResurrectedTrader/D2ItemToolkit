import { ItemRecordReader, type ItemViewer } from './Stats/ItemRecord.js';
import { ItemStatOps } from './Stats/ItemStatOps.js';
import { ItemStatReader, ItemStatView, sortByKey } from './Stats/ItemStatReader.js';
import { unitFromJson, type Unit } from './Stats/Unit.js';
import { ItemTable } from './Tables/ItemTable.js';
import { ItemTypeTree } from './Tables/ItemTypeTree.js';
import { D2DataFiles } from './Tables/TxtDataProviders.js';
import {
  type ItemTooltipContext,
  type ItemTooltipLine,
  ItemTooltipColor,
  ItemTooltipComposer,
  ItemTooltipKind,
  ItemTooltipSection,
} from './Tooltip/ItemTooltip.js';
import { RecordSections } from './Tooltip/RecordSections.js';
import { SetTable } from './Tables/SetTable.js';
import {
  SetItemTooltipBuilder,
  type SetItemTooltipContent,
  type SetItemTooltipInput,
} from './Tooltip/SetItemTooltip.js';
import { SocketStatSynthesis } from './Stats/SocketStatSynthesis.js';
import { TooltipEngine } from './Tooltip/TooltipEngine.js';
import { MagicAffixTable } from './Tables/MagicAffixTable.js';
import { RolledRangeReconstructor, type ItemRollRanges } from './Stats/RolledRangeReconstructor.js';
import type { ItemMergedStats } from './Stats/MergedStats.js';
import { Int32 } from './Types.js';

// The differential harness. NOT part of the package's public surface — it deliberately reaches
// past the facade to emit the INTERMEDIATE layers (views, kind, sections, lines) that
// tools/Reference emits on the C# side, because a mismatch that names its layer is what makes the
// corpus diagnosable. The C# counterpart gets the same reach via InternalsVisibleTo.

/** The result shape `tools/Reference/Program.cs` emits for one corpus case. */
export interface RenderedRecord {
  name?: string;
  views?: Record<string, Record<string, number>>;
  kind?: string;
  genericRefusal?: string;
  set?: {
    pieces: { text: string; owned: boolean }[];
    setName: string;
    fullSetText: string;
    partialText: string;
  } | null;
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

/** The `ranges` object, shaped to match `PackRanges` in tools/Reference/Program.cs exactly. */
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

/** The four breakdown buckets as text, matching `Breakdown` in tools/Reference/Program.cs. */
interface PackedBreakdown {
  base: string[];
  magic: string[];
  sockets: string[];
  setBonuses: string[];
}

/** The optional `set` object of a corpus case, mirroring `ReadSetInput` on the C# side. */
interface CorpusSetInput {
  ownedSetItemIds?: number[];
  wornMaskIncludingSelf?: number;
  wornMaskExcludingSelf?: number;
  isEquipped?: boolean;
  fullSetStats?: { id: number; value: number; layer?: number }[];
}

let cachedData: D2DataFiles | null = null;
let cachedItems: ItemTable | null = null;
let cachedTypes: ItemTypeTree | null = null;
let cachedSocketStats: SocketStatSynthesis | null = null;
let cachedSets: SetTable | null = null;

function tables(): {
  data: D2DataFiles;
  items: ItemTable;
  types: ItemTypeTree;
  socketStats: SocketStatSynthesis;
  sets: SetTable;
} {
  if (cachedData === null) {
    cachedData = D2DataFiles.load();
    cachedItems = new ItemTable(cachedData.weapons, cachedData.armor, cachedData.misc);
    cachedTypes = new ItemTypeTree(cachedData.itemTypes);
    cachedSocketStats = new SocketStatSynthesis(cachedData, cachedItems, cachedTypes);
    cachedSets = new SetTable(cachedData.sets, cachedData.setItems, cachedData.strings);
  }

  return {
    data: cachedData,
    items: cachedItems as ItemTable,
    types: cachedTypes as ItemTypeTree,
    socketStats: cachedSocketStats as SocketStatSynthesis,
    sets: cachedSets as SetTable,
  };
}

let cachedRanges: RolledRangeReconstructor | null = null;

function ranges(): RolledRangeReconstructor {
  if (cachedRanges === null) {
    const { data, items, types, sets } = tables();
    cachedRanges = new RolledRangeReconstructor(
      data,
      items,
      types,
      new MagicAffixTable(data),
      sets,
    );
  }

  return cachedRanges;
}

let cachedEngine: TooltipEngine | null = null;

function engine(): TooltipEngine {
  if (cachedEngine === null) {
    cachedEngine = TooltipEngine.fromData(tables().data);
  }

  return cachedEngine;
}

function packMergedStats(source: ItemMergedStats): PackedMergedStats {
  return {
    stats: source.stats.map(s => ({ stat: s.statId, layer: s.layer, value: s.value })),
    excludedPackedStats: [...source.excludedPackedStats],
  };
}

function packRanges(source: ItemRollRanges): PackedRanges {
  return {
    stats: source.stats.map(r => ({
      stat: r.statId,
      layer: r.layer,
      low: r.low,
      high: r.high,
      displayLow: r.displayLow,
      displayHigh: r.displayHigh,
      sources: r.sources,
    })),
    layerVaries: source.layerVaries.map(r => ({
      stat: r.statId,
      layerLow: r.layerLow,
      layerHigh: r.layerHigh,
      value: r.value,
      sources: r.sources,
    })),
    outOfRange: [...source.outOfRange],
    unattributed: [...source.unattributed],
    itemLevelDependent: [...source.itemLevelDependent],
    unsupportedFuncs: [...source.unsupportedFuncs],
    craftedRecipeUnknown: source.craftedRecipeUnknown,
    craftedRecipe: source.craftedRecipe,
  };
}

function pack(view: ReadonlyMap<number, number>): Record<string, number> {
  const packed: Record<string, number> = {};
  for (const entry of view) {
    packed[
      String(ItemStatReader.layerFromKey(entry[0])) +
        '/' +
        String(ItemStatReader.statFromKey(entry[0]))
    ] = entry[1];
  }

  return packed;
}

/**
 * `Enum.GetValues(typeof(ItemTooltipSection))` in declaration order, which for a string enum is
 * what `Object.values` gives. `None` is skipped on both sides — it is the pre-assignment default,
 * not a section, and querying it would add a key the C# reference does not produce.
 */
function allSections(): ItemTooltipSection[] {
  return Object.values(ItemTooltipSection).filter(v => v !== ItemTooltipSection.None);
}

function packSections(sections: RecordSections): Record<string, string> {
  const packed: Record<string, string> = {};

  for (const section of allSections()) {
    let text: string | null;
    try {
      text = sections.getSection(section);
    } catch (e) {
      text = '<<' + errorName(e) + '>>';
    }

    if (text !== null && text.length !== 0) {
      packed[section] = text;
    }
  }

  return packed;
}

type PackedLine = {
  section: string;
  color: number;
  statId: number;
  layer: number;
  shownStats: number[] | null;
  aggregated: boolean;
  text: string;
};

// statId and layer are public members a caller reads, and they were NOT compared: one
// implementation decoded the damage line's layer a second time and reported 0 for every line,
// which nothing here could see.
function packLines(lines: readonly ItemTooltipLine[]): PackedLine[] {
  const packed: PackedLine[] = [];
  for (const line of lines) {
    packed.push({
      section: line.section,
      color: line.color,
      statId: line.statId,
      layer: line.layer,
      shownStats: line.shownStats,
      aggregated: line.aggregated,
      text: line.text as string,
    });
  }

  return packed;
}

/**
 * Mirrors `e.GetType().Name`. ItemTooltip.ts declares ArgumentNullException and
 * NotSupportedException as named subclasses precisely so this reports the same names the C# does
 * — the refusal of a set-item tooltip is a different event from a null argument, and the
 * differential compares it.
 */
function errorName(e: unknown): string {
  return e instanceof Error ? e.constructor.name : String(e);
}

/**
 * Merge and RE-SORT. C#'s SortedDictionary reorders on insert; a Map keeps insertion order, so
 * without the sort the two views hold the same pairs in a different order and the differential
 * reports a divergence that is only key ordering.
 */
function addSynthesised(
  into: Map<number, number>,
  from: ReadonlyMap<number, number>,
): Map<number, number> {
  for (const [key, value] of from) {
    const existing = into.get(key);
    into.set(key, existing === undefined ? value : Int32.of(existing + value));
  }

  return sortByKey(into);
}

/**
 * The generic compose refuses a set item. Recorded per case so the refusal stays inside the
 * differential now that set items render through their own writer.
 */
function refusal(
  composer: ItemTooltipComposer,
  context: ItemTooltipContext,
  modifierStats: ReadonlyMap<number, number>,
): string {
  try {
    composer.compose(context, modifierStats);
    return 'none';
  } catch (e) {
    return errorName(e);
  }
}

/** Mirrors `ReadSetInput`: everything ITEM_BuildSetItemTooltip needs that the record cannot say. */
/**
 * The explicit override when a case carries a `set` member, and otherwise whatever the VIEWER
 * implies — mirroring render, which derives rather than defaulting to "none". A case with no `set`
 * and no viewer still gets the empty input.
 */
function readSetInput(set: unknown, record: Unit, wearer: Unit | null): SetItemTooltipInput {
  if (set === null || set === undefined || typeof set !== 'object') {
    return engine().setStateOf(record, wearer);
  }

  const source = set as CorpusSetInput;

  const full = source.fullSetStats;

  return {
    ownedSetItemIds: source.ownedSetItemIds ?? null,
    wornMaskIncludingSelf: source.wornMaskIncludingSelf ?? 0,
    wornMaskExcludingSelf: source.wornMaskExcludingSelf ?? 0,
    isEquipped: source.isEquipped ?? false,
    fullSetStats:
      full === undefined
        ? null
        : full.map(
            stat => [ItemStatReader.packStatKey(stat.layer ?? 0, stat.id), stat.value] as const,
          ),
  };
}

/**
 * The four derived buffers. Emitted separately from `lines` because a divergence in the piece
 * list, the tier selection or the set name has three different causes and only one of them is the
 * composer's.
 */
function packSetContent(
  content: SetItemTooltipContent | null,
): NonNullable<RenderedRecord['set']> | null {
  if (content === null) {
    return null;
  }

  return {
    pieces: content.pieces.map(piece => ({ text: piece.text, owned: piece.owned })),
    setName: content.setName,
    fullSetText: content.fullSetText,
    partialText: content.partialText,
  };
}

/**
 * Renders one corpus case exactly as `tools/Reference/Program.cs` does: the three intermediate
 * views, the classified kind, every non-empty section, the composed lines and the final string.
 */
export function renderRecord(
  record: unknown,
  player: unknown,
  set: unknown = null,
  shopMode = 0,
): RenderedRecord {
  const payload: RenderedRecord = {};

  try {
    const { data, items, types, socketStats, sets } = tables();

    const wearer: Unit | null =
      player === null || player === undefined ? null : unitFromJson(player);

    const viewer: ItemViewer | null = wearer === null ? null : ItemRecordReader.readViewer(wearer);

    const unit = unitFromJson(record);
    const item = ItemRecordReader.readIdentity(unit);

    // Read BEFORE the socket synthesis: ITEM_RecalcAllEquippedItems 0x4c1350 throws an equipped
    // set item's fillers away (0x4c1658 / 0x4c1661), so `isEquipped` decides whether there is a
    // contribution at all and TooltipEngine.renderSetItem passes it.
    const setInput = readSetInput(set, unit, wearer);

    let stats = ItemStatReader.reconstructView(unit, ItemStatView.equipped());
    const baseStats = ItemStatReader.reconstructView(unit, ItemStatView.baseOnly());
    let modifierStats = ItemStatReader.reconstructView(unit, ItemStatView.modifiers());

    // Mirrors TooltipEngine.compose: a captured gem or rune has no stat chain, so its contribution
    // is rebuilt from gems.txt. Omitting it here would leave the whole synthesis outside the
    // differential.
    const synthesised = socketStats.contributions(unit);
    stats = addSynthesised(stats, synthesised);
    modifierStats = addSynthesised(modifierStats, synthesised);

    ItemStatOps.resolve(stats, baseStats, data.itemStatCost);

    payload.views = {
      equipped: pack(stats),
      base: pack(baseStats),
      modifiers: pack(modifierStats),
    };

    // The two opt-in render modes, as text. Without these the annotation formatter, the range
    // colour and the socket-block layout are all outside the differential — exercised only by
    // hand-written tests on each side, which cannot catch the two implementations agreeing to
    // differ.
    payload.annotated = engine().render(unit, wearer, {
      ranges: { color: ItemTooltipColor.White },
      showItemLevel: true,
    }).coloredText;

    payload.socketsSplit = engine().render(unit, wearer, {
      sockets: 'separated',
      ranges: {},
    }).coloredText;

    // Breakdown was outside the differential entirely, which left its per-bucket span choice — the
    // item's own for three of them, the fillers' for the fourth — checked only by hand-written
    // tests on each side.
    const b = engine().breakdown(unit, wearer, { ranges: {} });
    const texts = (lines: readonly { text: string | null }[]): string[] =>
      lines.map(l => l.text ?? '');

    payload.breakdown = {
      base: texts(b.base),
      magic: texts(b.magic),
      sockets: texts(b.sockets),
      setBonuses: texts(b.setBonuses),
    };

    // The roll-range reconstruction. It reaches property handlers no rendering path touches — the
    // affix, unique, runeword and superior codes — so without it those branches are invisible to
    // the differential, which is exactly how the colour-3 marker gap survived.
    payload.ranges = packRanges(
      ranges().reconstruct(
        item,
        modifierStats,
        socketStats.fillerProperties(unit),
        // The tiers the WEARER has earned, not null. Passing null left RollSources.SetBonus reached
        // by zero of the 935 cases, so the whole earned-set fold sat outside the differential.
        engine().earnedSetIdsOf(wearer),
      ),
    );

    // The TOTALS surface, which shares nothing with the render path: it folds the gems.txt
    // synthesis and op 13 into one merged view, so none of that is reachable through the layers
    // above.
    payload.mergedStats = packMergedStats(engine().mergedStats(unit));

    const sections = new RecordSections(
      data,
      items,
      types,
      item,
      viewer,
      stats,
      ItemStatReader.readSockets(unit),
      baseStats,
      ItemRecordReader.readSocketUnits(unit),
    );

    const composer = new ItemTooltipComposer(
      sections,
      sections.createModifierGenerator(modifierStats),
    );

    const context = sections.createContext();

    // Game state, not unit state, so it is carried on the case rather than derived.
    context.shopMode = shopMode;

    const kind = ItemTooltipComposer.classify(context);

    payload.kind = kind;

    let lines: readonly ItemTooltipLine[];
    let maxLength = ItemTooltipComposer.MaxTooltipLength;

    if (kind === ItemTooltipKind.IdentifiedSetItem) {
      // The generic composer REFUSES a set item, and that refusal is behaviour worth comparing.
      payload.genericRefusal = refusal(composer, context, modifierStats);

      const builder = new SetItemTooltipBuilder(data, sets, items, types);
      const content = builder.build(unit, item, viewer, stats, setInput, wearer);

      lines = content === null ? [] : composer.composeSetItem(context, content, modifierStats);

      payload.set = packSetContent(content);

      // 0x48db0b -> 0x48db1d with no length test: this path has no 1023 cut.
      maxLength = ItemTooltipComposer.UnlimitedTooltipLength;
    } else {
      lines =
        kind === ItemTooltipKind.Book
          ? composer.composeBook(context)
          : composer.compose(context, modifierStats);
    }

    payload.sections = packSections(sections);
    payload.lines = packLines(lines);
    payload.rendered = composer.render(lines, false, maxLength);

    // Render drops every marker the composer would add, so on its own it leaves the whole
    // marker-placement rule outside the differential.
    payload.colored = composer.renderWithColorCodes(
      lines,
      ItemTooltipColor.Marker,
      false,
      maxLength,
    );
  } catch (e) {
    // A throw is itself observable behaviour worth comparing — Compose refuses a set
    // item and a book, and the TypeScript must refuse the same ones.
    payload.error = errorName(e);
  }

  return payload;
}
