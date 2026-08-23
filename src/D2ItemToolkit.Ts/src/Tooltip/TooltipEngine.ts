import {
  ItemRecordFlags,
  ItemRecordReader,
  type ItemIdentity,
  type ItemUnit,
  type ItemViewer,
} from '../Stats/ItemRecord.js';
import { ItemStatOps } from '../Stats/ItemStatOps.js';
import { ItemStatReader, ItemStatView, sortByKey } from '../Stats/ItemStatReader.js';
import type { Unit } from '../Stats/Unit.js';
import { SocketStatSynthesis } from '../Stats/SocketStatSynthesis.js';
import type { ItemProperty } from '../Stats/PropertyApplier.js';
import { ArgumentNullException, Int32, isNullOrEmpty } from '../Types.js';
import { ItemTable } from '../Tables/ItemTable.js';
import { ItemInventoryColor } from '../Tables/ItemInventoryColor.js';
import { ItemInventoryGraphics } from '../Tables/ItemInventoryGraphics.js';
import { ItemTypeTree } from '../Tables/ItemTypeTree.js';
import { D2DataFiles } from '../Tables/TxtDataProviders.js';
import {
  ItemTooltipColor,
  ItemTooltipComposer,
  ItemTooltipKind,
  ItemTooltipLine,
  ItemTooltipSection,
  type ItemTooltipContext,
} from './ItemTooltip.js';
import { RecordSections } from './RecordSections.js';
import { EquipRequirements } from './EquipRequirements.js';
import { RequiredLevelCalculator } from './RequiredLevelCalculator.js';
import { SetTable, type SetItemRecord } from '../Tables/SetTable.js';
import { MagicAffixTable } from '../Tables/MagicAffixTable.js';
import {
  RolledRangeReconstructor,
  type ItemRollRanges,
  type RolledStatRange,
} from '../Stats/RolledRangeReconstructor.js';
import { SetItemTooltipBuilder, popCount, type SetItemTooltipInput } from './SetItemTooltip.js';
import { NotSupportedException } from '../Types.js';

/**
 * pItemData item location 1, the body. See `Unit.location`.
 *
 * A body item's grid type is INVENTORY_PlaceItemInGrid's `(bodyLoc >= 11) ? 4 : 3` (0x63b1e2), and
 * 11/12 are the alternate weapon set (0x55f240) — which is what makes OWNED and WORN disagree.
 */
const LocationEquipped = 1;

/** dwQualityNo 5. Compared as a number, matching every other quality test in the port. */
const QualitySet = 5;

/**
 * The mask's two refusals, 0x62a446. 0x4000 has no name in `ItemRecordFlags` — `SocketStatSynthesis`
 * spells it the same way for the recalc loop's identical pair.
 */
const BrokenOrUnequippable = ItemRecordFlags.Broken | 0x4000;

/**
 * INVENTORY_PlaceItemInGrid 0x63b1e2: `cmp bodyLoc, 0Bh / setnl cl / add cl, 3`, so a body item is
 * grid type 3 except on the alternate weapon set, which is 4.
 */
function gridTypeOfBodyItem(bodyLocation: number): number {
  return bodyLocation >= 11 ? 4 : 3;
}

/**
 * Whether a location can hold a piece GetSetItem would find. It walks the viewer's inventory and
 * takes pages 0 / 3 / 4 / 0xFF (0x4867b3-0x4867bf), which excludes the TRADE page; ground and store
 * are not in that chain at all. The location-to-page mapping is by name rather than traced, so only
 * these three obvious exclusions are made — the rest fall through as owned, which affects the piece
 * list's colour and never a bonus tier.
 */
/**
 * GetSetItem's non-set tests: identified (0x4867a2) and on a page it walks (0x4867b3-0x4867bf).
 * Quality and the setitems lookup are the caller's part.
 */
function isOwned(unit: Unit): boolean {
  return (unit.itemFlags & ItemRecordFlags.Identified) !== 0 && onAnOwningPage(unit.location);
}

/**
 * What ITEMS_GetEquippedSetItemsMask counts: grid type 3 (0x62a3f0), which for a body item is
 * `bodyLoc < 11` (0x63b1e2), and neither refused flag (0x62a446).
 */
function isWorn(unit: Unit): boolean {
  return (
    unit.location === LocationEquipped &&
    gridTypeOfBodyItem(unit.x) === 3 &&
    (unit.itemFlags & BrokenOrUnequippable) === 0
  );
}

function onAnOwningPage(location: number): boolean {
  const Ground = 0;
  const Store = 4;
  const Trade = 5;

  return location !== Ground && location !== Store && location !== Trade;
}

/** Per-render knobs. Everything else is unit state and comes off the record. */
export interface TooltipOptions {
  /**
   * GetDificulity() (0x48cb38) — the one input that is game state rather than unit state. Only a
   * quest item with questdiffcheck set reads it.
   */
  difficulty?: number;

  /**
   * 0 outside a shop. 1-9 add the transaction-cost line, and any non-zero value suppresses both
   * usage lines (0x48d082 tests for exactly zero).
   */
  shopMode?: number;

  /**
   * False renders the item as if nothing were socketed in it. The game has no such mode; this
   * exists so a caller can show what the base item is worth on its own.
   */
  includeSockets?: boolean;

  /** Appends the trailing quest-colour marker (0x48d1e2). */
  questColorPrefix?: boolean;

  /**
   * Annotates each stat line with the span it could have rolled within — the same numbers
   * `ranges` returns, written inline.
   *
   * The game has no such mode, so this makes the output deliberately NOT byte-identical. Off by
   * default, which is why every existing render is unaffected.
   *
   * Only lines that display one stat are annotated: every modifier, plus the Defense line. A stat
   * with no span, or one whose value could only ever have been what it is, is left alone rather
   * than annotated with a degenerate range.
   */
  showRolledRanges?: boolean;

  /**
   * How a span is written when `showRolledRanges` is on. Unset uses `defaultRangeAnnotation`,
   * which gives ` [5-15]`.
   *
   * Return null or an empty string to suppress one — which is how you show ranges for some stats
   * and not others.
   */
  rangeAnnotation?: (ranges: readonly RolledStatRange[]) => string | null;

  /**
   * The `ItemTooltipColor` to paint the annotation, or -1 to inherit the line's. A marker restoring
   * the line's own colour follows the annotation, so nothing after it is affected. Only meaningful
   * for the coloured text — `Tooltip.text` strips no markers, so they appear there too, exactly as
   * the game's own embedded markers do.
   *
   * Defaults to the game's grey rather than to -1: a range is an annotation the game never draws,
   * so inheriting the stat line's blue made it read as part of the line.
   */
  rangeColor?: number;

  /**
   * Renders the item WITHOUT what its fillers contribute, then one block per filler below it, so a
   * reader can tell which gem or rune is responsible for what.
   *
   * The game never draws this — it merges the fillers into the item's own block, which is what
   * `render` does by default. Setting this implies `includeSockets` false for the item's own lines;
   * the fillers are not dropped but moved. The blocks carry
   * `ItemTooltipSection.SocketContribution`, and combined with `showRolledRanges` each filler's own
   * spans appear against its own lines.
   */
  separateSocketContributions?: boolean;

  /**
   * The CLIENT PLAYER, when that is a different unit from the viewer — i.e. a mercenary's panel.
   * Almost every caller leaves this unset.
   *
   * The game reads two units. Requirements, the class restriction, block chance and the smite gate
   * all use LoadItemDesc's own unit (0x48dee0), which on a merc panel IS the merc. But
   * INV_FormatAttackSpeedText ignores it and calls GetPlayerUnit_0 (0x463de0) twice — once for the
   * frame lookup at 0x486201 and once for the speed bucket's class offset at 0x486250 — so a
   * merc's weapon is timed against the CHARACTER. That is not a quirk we can derive: it needs the
   * second unit.
   *
   * Unset means "same as the viewer", which is correct everywhere else.
   */
  clientPlayer?: Unit | null;
}

/** A rendered tooltip. The lines are in DISPLAY order, top row first. */
export interface Tooltip {
  /** Which of the game's tooltip builders produced this. */
  readonly kind: ItemTooltipKind;

  readonly lines: readonly ItemTooltipLine[];

  /**
   * The plain text, newline separated. Markers a section writer embedded in its own text
   * survive — the game embeds those too.
   */
  readonly text: string;

  /**
   * The text with the per-line U+00FF 'c' N colour markers the game paints with. Both forms spend
   * the same 1023-character budget, so a long tooltip truncates where the game truncates.
   */
  readonly coloredText: string;
}

/**
 * The item's modifiers grouped by where they come from. See `breakdown` for why this is not a
 * fidelity feature.
 */
export interface TooltipBreakdown {
  /** The base array — what every copy of this item type carries. */
  readonly base: readonly ItemTooltipLine[];
  /** The item's own affixes, unique/set mods and runeword, sockets excluded. */
  readonly magic: readonly ItemTooltipLine[];
  /** What the socket fillers add. */
  readonly sockets: readonly ItemTooltipLine[];
  /** Earned set tiers only. Unearned tiers are excluded, as the game excludes them. */
  readonly setBonuses: readonly ItemTooltipLine[];
}

/** What an item demands of a wearer, and whether this viewer meets it. */
export interface ItemRequirements {
  /** items.txt reqstr, folded with stat 91 and the ethereal discount. 0 means none. */
  readonly strength: number;
  /** items.txt reqdex, the same way. 0 means none. */
  readonly dexterity: number;
  /**
   * The required level. Viewer-dependent: a classic unique shows none to a non-expansion viewer
   * (0x62b877), and a class-restricted affix charges its own class `classlevelreq` instead of
   * `levelreq`.
   */
  readonly level: number;
  /** The character class id an item type is restricted to, or EquipRequirements.NoClassRestriction. */
  readonly classRestriction: number;
  readonly metStrength: boolean;
  readonly metDexterity: boolean;
  readonly metLevel: boolean;
  readonly metClass: boolean;

  /** True when the viewer satisfies all four. */
  readonly allMet: boolean;
}

/** How an item's inventory sprite is painted. */
export interface ItemAppearance {
  /**
   * The inventory sprite name, without extension — a renderer fetches `image + '.dc6'`. NOT the
   * item code: exceptional and elite tiers collapse to the base tier, set and unique items get
   * their own art, and rings/amulets/jewels/charms carry a 1-based variant suffix.
   */
  readonly image: string;

  /**
   * The palette-shift index, 0-20, or -1 for none. 0 is `whit` and 20 is `bwht`; the codes are
   * colors.txt row order.
   */
  readonly color: number;

  /**
   * items.txt InvTrans — which transform table the shift indexes, NOT a colour. Zero on most
   * items, and that is what stops them being tinted at all, so a renderer gates on this rather
   * than on `color` alone.
   */
  readonly invTrans: number;

  /** True when there is a shift to apply and a table to apply it to. */
  readonly isTinted: boolean;
}

interface Composed {
  sections: RecordSections;
  composer: ItemTooltipComposer;
  context: ItemTooltipContext;
  kind: ItemTooltipKind;
  modifierStats: Map<number, number>;
  identity: ItemIdentity;
  viewer: ItemViewer | null;
  stats: Map<number, number>;
}

/**
 * The item's own affixes with the fillers left out. NOT `ItemStatView.itemOnly()`, which requires
 * STATLIST_EXTENDED *or* MAGIC and so drags the base array in with it.
 */
function itemOwnMods(): ItemStatView {
  const view = ItemStatView.modifiers();
  view.includeSockets = false;
  return view;
}

/**
 * Only what the fillers contribute. No view expresses this — ItemStatView can drop socket groups
 * but not keep only them — so it is the union of each filler viewed as an item in its own right,
 * which is what self-similarity makes correct.
 */
/**
 * Merges `from` into `into`, wrapping like the game's int32 sums, and RE-SORTS. C#'s
 * SortedDictionary reorders on insert; a Map keeps insertion order, so without the sort the two
 * views hold the same pairs in a different order.
 */
function addInto(
  into: Map<number, number>,
  from: ReadonlyMap<number, number>,
): Map<number, number> {
  for (const [key, value] of from) {
    const existing = into.get(key);
    into.set(key, existing === undefined ? value : Int32.of(existing + value));
  }

  return sortByKey(into);
}

function socketContributions(item: Unit, synthesis: SocketStatSynthesis): Map<number, number> {
  const merged = new Map<number, number>();

  for (const socket of item.items) {
    for (const [key, value] of ItemStatReader.reconstructView(socket, ItemStatView.modifiers())) {
      const existing = merged.get(key);
      // Int32.of, like every other accumulation site: the game stores stats as int32 and its
      // sums wrap. Without it two fillers summing past 2^31 diverge from C#'s int arithmetic —
      // the repo's own adversarial corpus already ships a record that triggers it.
      merged.set(key, existing === undefined ? value : Int32.of(existing + value));
    }
  }

  // Same reason as in compose: a captured gem or rune has no chain of its own.
  for (const [key, value] of synthesis.contributions(item)) {
    const existing = merged.get(key);
    merged.set(key, existing === undefined ? value : Int32.of(existing + value));
  }

  return sortByKey(merged);
}

/**
 * Mirrors the `ArgumentNullException` guards on the C# entry points. Without it a null unit
 * surfaces as a `TypeError` from wherever the first field access happens to land — a different
 * error type, thrown from a less useful place.
 */
function requireUnit(unit: Unit, name: string): void {
  if (unit === null || unit === undefined) {
    throw new ArgumentNullException(name);
  }
}

/**
 * The way in. Holds the parsed game tables — building them is the expensive part, so make one and
 * keep it. It is immutable once constructed and safe to share.
 */
export class TooltipEngine {
  private static embeddedInstance: TooltipEngine | null = null;

  /**
   * The parsed game tables, for lookups this library does not do for you. Read-only:
   * `data.weapons.getString(row, 'invfile')` and friends.
   *
   * The tables are public; the ENGINE is not. What builds a tooltip out of them — RecordSections,
   * the composer, the description generator — stays unexported, because those shapes exist to
   * mirror the disassembly rather than to be consumed.
   */
  readonly data: D2DataFiles;

  /** weapons + armor + misc as one classId-indexed table, the way the game compiles them. */
  readonly items: ItemTable;

  /** The itemtypes Equiv1/Equiv2 closure, for `isOfType` questions. */
  readonly types: ItemTypeTree;

  /**
   * sets.txt and setitems.txt, linked the way TXT_AllocTxt_setitems links them (0x63668d). This is
   * what tells a caller which pieces a set has, and in which order, so it can fill in
   * `SetItemTooltipInput.ownedSetItemIds`.
   */
  readonly sets: SetTable;

  // Built once with the tables, not per call: GemTable's constructor walks every gems row against
  // every item code, so rebuilding it on each appearance() was tens of thousands of comparisons
  // for one lookup.
  private readonly colors: ItemInventoryColor;
  private readonly graphics: ItemInventoryGraphics;
  private readonly requirementsTable: EquipRequirements;
  private readonly level: RequiredLevelCalculator;
  private readonly socketStats: SocketStatSynthesis;
  private readonly rangesReconstructor: RolledRangeReconstructor;

  private constructor(data: D2DataFiles) {
    this.data = data;
    this.items = new ItemTable(data.weapons, data.armor, data.misc);
    this.types = new ItemTypeTree(data.itemTypes);
    this.sets = new SetTable(data.sets, data.setItems, data.strings);
    this.colors = new ItemInventoryColor(data, this.items, this.types);
    this.graphics = new ItemInventoryGraphics(data, this.items, this.types);
    this.requirementsTable = new EquipRequirements(data, this.items);
    this.level = new RequiredLevelCalculator(data, this.items);
    this.socketStats = new SocketStatSynthesis(data, this.items, this.types);
    this.rangesReconstructor = new RolledRangeReconstructor(
      data,
      this.items,
      this.types,
      new MagicAffixTable(data),
      this.sets,
    );
  }

  /** The tables shipped inside this package. Built once, then reused. */
  static get embedded(): TooltipEngine {
    TooltipEngine.embeddedInstance ??= new TooltipEngine(D2DataFiles.load());
    return TooltipEngine.embeddedInstance;
  }

  /**
   * Tables read from an MPQ extraction instead of the embedded copy — the counterpart to C#'s
   * `FromFiles`. Reads from disk, so it is Node-only; `fromData` is the portable form.
   */
  static fromFiles(
    excelDirectory: string,
    localeDirectory: string,
    globalDirectory: string | null = null,
  ): TooltipEngine {
    return new TooltipEngine(D2DataFiles.load(excelDirectory, localeDirectory, globalDirectory));
  }

  /**
   * An engine over tables you already hold. This is the one that works in a browser: build a
   * `D2DataFiles` however you like — fetched bytes, a bundled archive, a modded extraction — and
   * hand it over. `fromFiles` is just this with a filesystem read in front.
   */
  static fromData(data: D2DataFiles): TooltipEngine {
    if (data === null || data === undefined) {
      throw new ArgumentNullException('data');
    }

    return new TooltipEngine(data);
  }

  /**
   * The tooltip the game would draw for `item`, as seen by `viewer`. A null viewer renders what
   * the engine produces with no player unit — level-scaled lines then scale by zero, which is
   * what the game does too (GetStatUnsignedValue returns 0 for a null unit at 0x625483).
   */
  render(item: Unit, viewer: Unit | null = null, options: TooltipOptions | null = {}): Tooltip {
    requireUnit(item, 'item');

    // A default parameter only fires for `undefined`, so an explicit null would fall straight
    // through to a field access and a TypeError. C# accepts null here (`options ?? Default`).
    options = options ?? {};

    // Separating the fillers means the item's own block must not contain them, which is exactly
    // what includeSockets false already does — they are moved, not dropped.
    const includeSockets =
      (options.includeSockets ?? true) && !(options.separateSocketContributions ?? false);

    const composed = this.compose(item, viewer, options, includeSockets);

    if (composed.kind === ItemTooltipKind.IdentifiedSetItem) {
      // Derived from the viewer rather than defaulted to "none". The old default painted every
      // piece red and selected no tier, and — because the full-set block is gated on isEquipped —
      // silently suppressed it for anyone actually wearing the set. A viewer that carries nothing
      // still yields exactly that empty input.
      return this.renderSetItem(item, this.setStateOf(item, viewer), viewer, options);
    }

    // Installed BEFORE composing, because the annotation is written into each line's text as it is
    // built rather than patched onto the finished list.
    if (options.showRolledRanges ?? false) {
      composed.composer.rangeAnnotation = this.buildRangeAnnotation(item, options, includeSockets);
      composed.composer.rangeColor = options.rangeColor ?? ItemTooltipColor.SocketedOrEthereal;
    }

    let lines =
      composed.kind === ItemTooltipKind.Book
        ? composed.composer.composeBook(composed.context)
        : composed.composer.compose(composed.context, composed.modifierStats);

    if (options.separateSocketContributions ?? false) {
      lines = this.withSocketBlocks(item, viewer, options, lines);
    }

    return TooltipEngine.tooltip(composed, lines, options, ItemTooltipComposer.MaxTooltipLength);
  }

  /**
   * Appends one block per filler BELOW the item. Lines are in display order, so appending puts them
   * at the bottom, which is where a reader expects "and this is what the gems are doing".
   */
  private withSocketBlocks(
    item: Unit,
    viewer: Unit | null,
    options: TooltipOptions,
    body: readonly ItemTooltipLine[],
  ): readonly ItemTooltipLine[] {
    const slot = this.socketStats.slotFor(item);
    if (slot < 0) {
      return body;
    }

    const all: ItemTooltipLine[] = [...body];

    for (const filler of item.items) {
      // A gem or rune has no stats of its own and is synthesised from gems.txt. A JEWEL does carry
      // its own — its affixes are captured like any magic item's — and contribution deliberately
      // returns nothing for it rather than counting them twice. Its own modifier view is what
      // belongs in its block.
      let contribution = this.socketStats.contribution(filler, slot);

      const carriesOwnStats = contribution.size === 0;
      if (carriesOwnStats) {
        contribution = ItemStatReader.reconstructView(filler, ItemStatView.modifiers());
      }

      if (contribution.size === 0) {
        continue;
      }

      // The filler's own name, taken from its own render — a socket filler is a unit in its own
      // right, which is what makes this a lookup rather than a special case.
      const asItem = this.compose(filler, viewer, {}, false);
      const name = asItem.sections.getSection(ItemTooltipSection.ItemName);

      if (!isNullOrEmpty(name)) {
        // A blank row between blocks, so three gems do not read as one list. The game never draws
        // this section at all, so there is no append-order budget to spend and no marker to emit —
        // the row is a bare terminator.
        const gap = new ItemTooltipLine();
        gap.text = asItem.sections.lineTerminator ?? '';
        gap.section = ItemTooltipSection.SocketContribution;
        gap.color = ItemTooltipColor.SocketedOrEthereal;
        gap.emitsColorMarker = false;
        all.push(gap);

        const header = new ItemTooltipLine();
        header.text = name;
        header.section = ItemTooltipSection.SocketContribution;
        header.color = ItemTooltipColor.SocketedOrEthereal;
        header.emitsColorMarker = true;
        all.push(header);
      }

      // Described through the same writers as any modifier block, so the text matches what the
      // merged render would have shown — only the selection differs.
      const composer = new ItemTooltipComposer(
        asItem.sections,
        asItem.sections.createModifierGenerator(contribution),
      );

      if (options.showRolledRanges ?? false) {
        // A jewel's spans come from ITS OWN affixes, so it is ranged as the item it is. A gem or
        // rune is ranged from the gems.txt properties it lends the host — which in shipped data
        // never roll, so those blocks come out unannotated.
        composer.rangeAnnotation = carriesOwnStats
          ? this.buildRangeAnnotation(filler, options)
          : TooltipEngine.lookup(
              this.rangesReconstructor.reconstruct(
                ItemRecordReader.readIdentity(item),
                null,
                this.socketStats.fillerPropertiesOf(filler, slot),
                null,
                false,
              ),
              options,
            );
        composer.rangeColor = options.rangeColor ?? ItemTooltipColor.SocketedOrEthereal;
      }

      for (const line of composer.composeModifiersOnly(contribution)) {
        line.section = ItemTooltipSection.SocketContribution;
        all.push(line);
      }
    }

    return all;
  }

  /**
   * The default way a span is written: ` [5-15]`, and nothing at all for a stat that could only
   * have taken one value. A single end would read as a range of one.
   */
  /** @internal The C# peer is `internal`; set `rangeAnnotation` to override the format. */
  static defaultRangeAnnotation(ranges: readonly RolledStatRange[]): string | null {
    if (ranges.length === 0) {
      return null;
    }

    const first = ranges[0] as RolledStatRange;

    // Every stat a DescGrp line covers shares the one number the line prints, so their spans agree
    // and repeating them would give "[(2-5)-(2-5)-(2-5)-(2-5)]".
    const identical = ranges.every(
      r => r.displayLow === first.displayLow && r.displayHigh === first.displayHigh,
    );

    if (identical) {
      return first.isRange ? ' [' + TooltipEngine.span(first) + ']' : null;
    }

    // A min-max line prints two numbers, so it gets two spans: "[(1-2)-(3-5)]" reads as "the first
    // number was 1..2, the second 3..5", which is the only honest single string for it. A degenerate
    // half still appears, because dropping it would leave the reader unable to tell which half the
    // surviving span belongs to.
    const anyRange = ranges.some(r => r.isRange);
    return anyRange
      ? ' [' + ranges.map(r => '(' + TooltipEngine.span(r) + ')').join('-') + ']'
      : null;
  }

  /**
   * One span, from the DECODED ends — so a charged skill reads as its charge count rather than as
   * the packed word it is stored in.
   */
  private static span(range: RolledStatRange): string {
    return String(range.displayLow) + '-' + String(range.displayHigh);
  }

  /**
   * Turns the reconstruction into the (layer, statId) lookup the composer wants. Built once per
   * render: the reconstruction applies every property twice, which is not work to repeat per line.
   */
  /**
   * `includeSockets` must match what the LINES being annotated contain. The merged render draws one
   * line holding item plus fillers, so its span is the sum; a body rendered with the fillers
   * excluded — includeSockets false, or the separated mode — draws the item's own value alone and
   * must get the item's own span. Getting this backwards put "Fire Resist +20% [16-30]" on a line
   * whose 20 was the item's half only.
   */
  private buildRangeAnnotation(
    item: Unit,
    options: TooltipOptions,
    includeSockets = true,
  ): (shownStats: readonly number[], layer: number) => string | null {
    const reconstructed = includeSockets
      ? this.ranges(item)
      : this.rangesReconstructor.reconstruct(
          ItemRecordReader.readIdentity(item),
          ItemStatReader.reconstructView(item, itemOwnMods()),
          null,
          null,
        );

    return TooltipEngine.lookup(reconstructed, options);
  }

  /**
   * The spans for EVERY filler at once, for the socket bucket of a breakdown — where the lines are
   * the fillers' union rather than one block per filler. A jewel's own affixes are folded in, since
   * those are what rolled.
   */
  private buildSocketRangeAnnotation(
    host: Unit,
    options: TooltipOptions,
  ): (shownStats: readonly number[], layer: number) => string | null {
    const slot = this.socketStats.slotFor(host);
    const properties: ItemProperty[] = [];
    const byKey = new Map<number, RolledStatRange>();

    if (slot >= 0) {
      for (const filler of host.items) {
        properties.push(...this.socketStats.fillerPropertiesOf(filler, slot));

        // A jewel contributes nothing through gems.txt; its own affixes are the roll.
        if (this.socketStats.contribution(filler, slot).size !== 0) {
          continue;
        }

        for (const range of this.ranges(filler).stats) {
          byKey.set(ItemStatReader.packStatKey(range.layer, range.statId), range);
        }
      }
    }

    const gems = this.rangesReconstructor.reconstruct(
      ItemRecordReader.readIdentity(host),
      null,
      properties,
      null,
      false,
    );

    for (const range of gems.stats) {
      byKey.set(ItemStatReader.packStatKey(range.layer, range.statId), range);
    }

    return TooltipEngine.lookupBy(byKey, options);
  }

  /**
   * Turns a reconstruction into the (stats, layer) lookup the composer wants. Built once per render:
   * the reconstruction applies every property twice, which is not work to repeat per line.
   */
  private static lookup(
    reconstructed: ItemRollRanges,
    options: TooltipOptions,
  ): (shownStats: readonly number[], layer: number) => string | null {
    const byKey = new Map<number, RolledStatRange>();
    for (const range of reconstructed.stats) {
      byKey.set(ItemStatReader.packStatKey(range.layer, range.statId), range);
    }

    return TooltipEngine.lookupBy(byKey, options);
  }

  private static lookupBy(
    byKey: Map<number, RolledStatRange>,
    options: TooltipOptions,
  ): (shownStats: readonly number[], layer: number) => string | null {
    const format = options.rangeAnnotation ?? TooltipEngine.defaultRangeAnnotation;

    return (shownStats, layer) => {
      const found: RolledStatRange[] = [];

      for (const statId of shownStats) {
        const range = byKey.get(ItemStatReader.packStatKey(layer, statId));
        if (range !== undefined) {
          found.push(range);
        }
      }

      // Positions carry the meaning on a multi-stat line, so a PARTIAL answer is worse than none:
      // one span against "Adds 1-4 cold damage" reads as the whole line's, and the reader cannot
      // tell which half it came from. All or nothing.
      return found.length !== shownStats.length || found.length === 0 ? null : format(found);
    };
  }

  /**
   * ITEM_BuildSetItemTooltip 0x48d1d0, for an IDENTIFIED set item — the tooltip LoadItemDesc
   * diverts to at 0x48e432 instead of building the generic one.
   *
   * `set` supplies only what the item's own record cannot: which siblings the viewer is carrying,
   * the two worn masks, whether this piece is equipped, and the full-set stat block. The piece
   * names, their order, the set name, `add func` and the partial-bonus stats are all derived here.
   *
   * Throws when the item is not an identified set item; `render` classifies for you and routes to
   * this automatically.
   *
   * @internal The C# peer is `internal`. Reachable inside the package, absent from the published
   * surface: a caller derives set state by handing `render` a viewer.
   */
  renderSetItem(
    item: Unit,
    set: SetItemTooltipInput,
    viewer: Unit | null = null,
    options: TooltipOptions | null = {},
  ): Tooltip {
    requireUnit(item, 'item');
    if (set === null || set === undefined) {
      throw new ArgumentNullException('set');
    }

    options = options ?? {};

    const composed = this.compose(
      item,
      viewer,
      options,
      options.includeSockets ?? true,
      set.isEquipped ?? false,
    );

    if (composed.kind !== ItemTooltipKind.IdentifiedSetItem) {
      throw new NotSupportedException(
        'This item is built by ' +
          composed.kind +
          ', not the set-item tooltip path. Call render instead.',
      );
    }

    const builder = new SetItemTooltipBuilder(this.data, this.sets, this.items, this.types);

    const content = builder.build(
      item,
      composed.identity,
      composed.viewer,
      composed.stats,
      set,
      viewer,
    );

    // GetSetItemsLine returning null returns at 0x48d397 and GetSetsLine at 0x48d3ab, in both
    // cases before a single buffer is appended — the game draws no tooltip at all.
    const lines =
      content === null
        ? []
        : composed.composer.composeSetItem(composed.context, content, composed.modifierStats);

    return TooltipEngine.tooltip(
      composed,
      lines,
      options,
      ItemTooltipComposer.UnlimitedTooltipLength,
    );
  }

  /**
   * `maxLength` is the 1023-wide-char cut LoadItemDesc applies at 0x48ed12 — except on the
   * set-item path, which never takes it: ITEM_BuildSetItemTooltip runs from MoveArgumentToEAX
   * (0x48db0b) straight to TEXT_CalcTextDimensions (0x48db1d) over a 2048-WCHAR buffer with no
   * guard.
   */
  private static tooltip(
    composed: Composed,
    lines: readonly ItemTooltipLine[],
    options: TooltipOptions,
    maxLength: number,
  ): Tooltip {
    const questPrefix = options.questColorPrefix ?? false;
    const composer = composed.composer;

    return {
      kind: composed.kind,
      lines,
      get text() {
        return composer.render(lines, questPrefix, maxLength);
      },
      get coloredText() {
        return composer.renderWithColorCodes(
          lines,
          ItemTooltipColor.Marker,
          questPrefix,
          maxLength,
        );
      },
    };
  }

  /**
   * How the item's inventory sprite is tinted. Nothing to do with the tooltip text — this is what
   * paints a set item green or a magic ring blue, and it is here because it is the same tables
   * and the same record.
   */
  appearance(item: Unit): ItemAppearance {
    requireUnit(item, 'item');

    const identity = ItemRecordReader.readIdentity(item);

    // Only socket 0 is consulted, and only when it holds a gem.
    const first = item.items[0];
    const firstSocket = first === undefined ? null : ItemRecordReader.readIdentity(first);

    const color = this.colors.resolve(identity, firstSocket);
    const invTrans = this.colors.invTrans(identity.classId);

    return {
      image: this.graphics.resolve(identity),
      color,
      invTrans,
      isTinted: color >= 0 && invTrans !== 0,
    };
  }

  /**
   * What the item demands of a wearer. The strength and dexterity NUMBERS are the same for
   * everyone — they come from items.txt folded with the item's own stat 91 and the ethereal
   * discount — but the required LEVEL is viewer-dependent, and so is every `met` flag. Pass the
   * viewer to get both; omit it and the numbers are still right while the flags read as unmet,
   * because a null unit's stats read as 0 (0x625483).
   */
  requirements(item: Unit, viewer: Unit | null = null): ItemRequirements {
    requireUnit(item, 'item');

    const identity = ItemRecordReader.readIdentity(item);
    const player = viewer === null ? null : ItemRecordReader.readViewer(viewer);

    const stats = ItemStatReader.reconstructView(item, ItemStatView.equipped());
    const baseStats = ItemStatReader.reconstructView(item, ItemStatView.baseOnly());
    ItemStatOps.resolve(stats, baseStats, this.data.itemStatCost);

    const sockets = ItemStatReader.readSockets(item);
    const socketUnits = ItemRecordReader.readSocketUnits(item);

    const metStrength = this.requirementsTable.metStrength(identity, player, stats);
    const metDexterity = this.requirementsTable.metDexterity(identity, player, stats);
    const metLevel = this.requirementsTable.metLevel(identity, player, stats, socketUnits, sockets);
    const metClass = this.requirementsTable.metClass(identity, player);

    return {
      strength: this.requirementsTable.requirement(identity, 'reqstr', stats),
      dexterity: this.requirementsTable.requirement(identity, 'reqdex', stats),
      level: this.level.calculate(identity, player, stats, socketUnits, sockets),
      classRestriction: this.requirementsTable.classRestriction(identity),
      metStrength,
      metDexterity,
      metLevel,
      metClass,
      allMet: metStrength && metDexterity && metLevel && metClass,
    };
  }

  /**
   * Every classId whose `type` or `type2` is `typeCode` or anything under it — ask for `swor` and
   * get every sword, including the exceptional and elite tiers and the class-specific sword types
   * that chain up to it.
   *
   * This is the descending counterpart to the ascending question the engine itself asks: both go
   * through the same Equiv1/Equiv2 closure, so membership here and `isOfType` cannot disagree.
   */
  classIdsOfType(typeCode: string): number[] {
    const found: number[] = [];

    const query = this.types.row(typeCode);
    if (query < 0) {
      return found;
    }

    for (let classId = 0; classId < this.items.rowCount; ++classId) {
      if (
        this.types.isOfType(
          this.types.row(this.items.primaryTypeCode(classId)),
          this.types.row(this.items.secondaryTypeCode(classId)),
          query,
        )
      ) {
        found.push(classId);
      }
    }

    return found;
  }

  /**
   * The set state a viewer implies, so a caller never assembles bit masks by hand. Two passes with
   * DIFFERENT predicates, which is the whole reason this belongs in the library:
   *
   * OWNED — colours the piece list. GetSetItem 0x486770 accepts inventory grid types 1, 3 AND 4
   * (0x4867d4), so a piece on the alternate weapon set still counts and draws green.
   *
   * WORN — drives the bonus tiers. ITEMS_GetEquippedSetItemsMask requires grid type 3 alone
   * (0x62a3f0), so a swapped piece lights no bit. The bit is the piece's setitems slot (0x62a474),
   * not its body location.
   *
   * Anything carried but NOT equipped is treated as a plain carried grid, i.e. owned. Which
   * locations the game stamps as type 1 is UNTRACED, so a producer that puts stash or cube contents
   * in `items` gets them counted as owned; that affects the piece list's colour only, never a tier.
   *
   * @internal The C# peer is `internal`. Reachable inside the package, absent from the published
   * surface: a caller derives set state by handing `render` a viewer.
   */
  setStateOf(item: Unit, viewer: Unit | null): SetItemTooltipInput {
    requireUnit(item, 'item');

    const input: SetItemTooltipInput = { isEquipped: item.location === LocationEquipped };

    const self = item.quality === QualitySet ? this.sets.pieceAt(item.fileIndex) : null;

    if (self === null || viewer === null) {
      return input;
    }

    const owned: number[] = [];
    let worn = 0;

    for (const carried of this.carriedSetPieces(viewer)) {
      if (carried.piece.setId !== self.setId) {
        continue;
      }

      owned.push(carried.unit.fileIndex);

      if (carried.worn) {
        worn |= 1 << carried.piece.slot;
      }
    }

    // The hovered piece's own bit comes from the ITEM, not from the list. Nothing obliges a caller
    // to repeat the hovered item inside the viewer's items — "what else the player is carrying" is
    // the natural reading of a list passed ALONGSIDE the item — and taking the bit only from the
    // list silently dropped a tier when they did not. OR is idempotent, so listing it changes
    // nothing.
    if (self.slot >= 0 && isWorn(item)) {
      worn |= 1 << self.slot;
    }

    if (!owned.includes(item.fileIndex) && isOwned(item)) {
      owned.push(item.fileIndex);
    }

    input.ownedSetItemIds = owned;
    input.wornMaskIncludingSelf = worn;

    // Now a genuine inverse: self's bit is set above whenever it is worn, so clearing it here is
    // the only difference between the two masks, by construction.
    input.wornMaskExcludingSelf = self.slot >= 0 ? worn & ~(1 << self.slot) : worn;

    return input;
  }

  /**
   * Set ids the viewer wears at least two pieces of — the point `add func` 2 lights its first tier
   * (0x4e65b2 gives N worn pieces tiers 0..N-2). Uses the WORN predicate, not the owned one: a
   * piece on the alternate weapon set grants no bonus, so it must not raise the count.
   *
   * @internal The C# peer is `internal`. Reachable inside the package, absent from the published
   * surface: a caller derives set state by handing `render` a viewer.
   */
  earnedSetIdsOf(viewer: Unit | null): number[] {
    // A MASK per set, not a count. The game ORs `1 << slot` (0x62a474), so two copies of the same
    // piece light one bit and count once — and two rings is not a hypothetical: both Cathan's Seal
    // and Angelic Halo are `rin`, and a character has two ring slots. Counting units instead would
    // earn a tier off a single duplicated piece and put set-bonus spans on an item that has none.
    const wornPerSet = new Map<number, number>();

    if (viewer !== null) {
      for (const carried of this.carriedSetPieces(viewer)) {
        if (carried.worn) {
          const setId = carried.piece.setId;
          wornPerSet.set(setId, (wornPerSet.get(setId) ?? 0) | (1 << carried.piece.slot));
        }
      }
    }

    const earned: number[] = [];
    for (const [setId, mask] of wornPerSet) {
      if (popCount(mask) >= 2) {
        earned.push(setId);
      }
    }

    earned.sort((a, b) => a - b);
    return earned;
  }

  /**
   * One carried set piece, with the OWNED / WORN distinction already made. Both derivations walk
   * the viewer the same way and differ only in which of the two they read, so the walk and the two
   * predicates live here once rather than being restated per caller.
   *
   * No recursion: a filler is one level below a carried item and no set item is socketable.
   */
  private *carriedSetPieces(
    viewer: Unit,
  ): Generator<{ unit: Unit; piece: SetItemRecord; worn: boolean }> {
    for (const carried of viewer.items) {
      // GetSetItem 0x486770 takes quality 5 (0x486790) that is IDENTIFIED (CheckItemFlag 0x10,
      // 0x4867a2). Every set item drops unidentified, so a sibling just picked up is the normal
      // case and the game paints it red.
      if (carried.quality !== QualitySet || !isOwned(carried)) {
        continue;
      }

      const piece = this.sets.pieceAt(carried.fileIndex);
      if (piece === null || piece.slot < 0) {
        continue;
      }

      yield {
        unit: carried,
        piece,
        // Grid type 3, which is what the worn mask requires. INVENTORY_PlaceItemInGrid stamps a
        // body item as `(bodyLoc >= 11) ? 4 : 3` (0x63b1e2), and 11/12 are the swap pair. The mask
        // additionally refuses flag 0x100 and flag 0x4000 (0x62a446) — a broken piece grants no
        // bonus even while worn, and it is already drawn red by name.
        worn: isWorn(carried),
      };
    }
  }

  /**
   * The same reconstruction as `ranges`, with the earned sets taken FROM THE VIEWER rather than
   * listed by hand — sharing `setStateOf`'s worn-piece rule so the two entry points cannot disagree
   * about which tiers a character has.
   */
  rangesForViewer(item: Unit, viewer: Unit | null): ItemRollRanges {
    return this.ranges(item, this.earnedSetIdsOf(viewer));
  }

  /**
   * The span each of the item's stats could have rolled within, rebuilt from the tables its own
   * record points at — the affix ids it stores, its UniqueItems or SetItems row, its runeword, its
   * superior modifier and its socket fillers, plus the base Defense roll.
   *
   * Like `breakdown` this is a capability the game does not have, so it cannot be checked against
   * the original. What it CAN be checked against is the item's own recorded values, which must fall
   * inside the spans claimed for them — `outOfRange` is empty whenever that holds.
   *
   * Set BONUSES are excluded: they belong to the worn set rather than to this item. Pass
   * `earnedSetIds` to fold them in, or use `rangesForViewer` to take them from a viewer.
   */
  ranges(item: Unit, earnedSetIds: readonly number[] | null = null): ItemRollRanges {
    requireUnit(item, 'item');

    // Not equipped, matching breakdown's socket view: an equipped host's fillers are discarded by
    // recalc, which would drop the very properties being ranged.
    return this.rangesReconstructor.reconstruct(
      ItemRecordReader.readIdentity(item),
      ItemStatReader.reconstructView(item, itemOwnMods()),
      this.allSocketProperties(item),
      earnedSetIds,
    );
  }

  /**
   * What every filler contributes, as properties. A gem or rune lends the host its gems.txt mods; a
   * JEWEL lends its own affix rolls, which gems.txt knows nothing about.
   *
   * Both belong here because the merged render draws ONE line per stat holding the SUM — so its span
   * has to be the sum of both spans. Leaving the jewel out gave a line reading
   * "Fire Resist +28% [11-20]", where 28 was item plus jewel but 11-20 was the item alone.
   */
  private allSocketProperties(item: Unit): ItemProperty[] {
    const properties: ItemProperty[] = [...this.socketStats.fillerProperties(item)];

    const slot = this.socketStats.slotFor(item);
    if (slot < 0) {
      return properties;
    }

    for (const filler of item.items) {
      // A filler the synthesis has nothing to say about is one carrying its own stats, and its
      // affixes are the roll.
      if (this.socketStats.contribution(filler, slot).size !== 0) {
        continue;
      }

      properties.push(
        ...this.rangesReconstructor.ownProperties(ItemRecordReader.readIdentity(filler)),
      );
    }

    return properties;
  }

  /**
   * The item's modifiers split by where they come from, for a "hold shift" view. This is a
   * capability the game does not have — it never draws these separately — so unlike `render` it
   * cannot be checked against the original. What it does guarantee is that every line is produced
   * by the same traced writers; only the stat SELECTION differs, and each selection is one of the
   * views the engine itself uses.
   */
  breakdown(
    item: Unit,
    viewer: Unit | null = null,
    options: TooltipOptions | null = {},
  ): TooltipBreakdown {
    requireUnit(item, 'item');
    options = options ?? {};

    // The item's own reconstruction annotates three of the four buckets, WITHOUT sockets — those
    // buckets show the item's own values. The socket bucket gets its own, built from the fillers'
    // properties, because the item's spans do not describe what a gem contributes.
    const own =
      (options.showRolledRanges ?? false) ? this.buildRangeAnnotation(item, options, false) : null;

    const sockets =
      (options.showRolledRanges ?? false) ? this.buildSocketRangeAnnotation(item, options) : null;

    return {
      base: this.describe(
        item,
        viewer,
        options,
        ItemStatReader.reconstructView(item, ItemStatView.baseOnly()),
        own,
      ),
      magic: this.describe(
        item,
        viewer,
        options,
        ItemStatReader.reconstructView(item, itemOwnMods()),
        own,
      ),
      sockets: this.describe(
        item,
        viewer,
        options,
        socketContributions(item, this.socketStats),
        sockets,
      ),
      setBonuses: this.describe(
        item,
        viewer,
        options,
        ItemStatReader.reconstructView(item, ItemStatView.setBonuses(false)),
        own,
      ),
    };
  }

  private describe(
    item: Unit,
    viewer: Unit | null,
    options: TooltipOptions,
    selected: Map<number, number>,
    annotation: ((shownStats: readonly number[], layer: number) => string | null) | null = null,
  ): readonly ItemTooltipLine[] {
    const composed = this.compose(item, viewer, options, true);

    // The composer built for THIS selection, so the generator's value source and the block's
    // colour carry match what a full render of the same stats would produce.
    const composer = new ItemTooltipComposer(
      composed.sections,
      composed.sections.createModifierGenerator(selected),
    );

    if (annotation !== null) {
      composer.rangeAnnotation = annotation;
      composer.rangeColor = options.rangeColor ?? ItemTooltipColor.SocketedOrEthereal;
    }

    return composer.composeModifiersOnly(selected);
  }

  private compose(
    item: Unit,
    viewer: Unit | null,
    options: TooltipOptions,
    includeSockets: boolean,
    hostIsEquipped = false,
  ): Composed {
    const identity = ItemRecordReader.readIdentity(item);
    const player: ItemViewer | null = viewer === null ? null : ItemRecordReader.readViewer(viewer);

    const equipped = ItemStatView.equipped();
    const modifiers = ItemStatView.modifiers();
    equipped.includeSockets = includeSockets;
    modifiers.includeSockets = includeSockets;

    let stats = ItemStatReader.reconstructView(item, equipped);
    const baseStats = ItemStatReader.reconstructView(item, ItemStatView.baseOnly());
    let modifierStats = ItemStatReader.reconstructView(item, modifiers);

    // A client capture hands over gems and runes with no stat chain — the mods are assigned in
    // D2Common/D2Game and the client only ever sees the host's merged result. Rebuild them from
    // gems.txt so the host's blue block is not silently short of its fillers.
    if (includeSockets) {
      const synthesised = this.socketStats.contributions(item, hostIsEquipped);
      stats = addInto(stats, synthesised);
      modifierStats = addInto(modifierStats, synthesised);
    }

    // The capture is leaf-per-list, so op 13 is folded back in here rather than by the producer.
    // Without it every by-time stat reads its unresolved value.
    ItemStatOps.resolve(stats, baseStats, this.data.itemStatCost);

    const socketUnits: ItemUnit[] = includeSockets ? ItemRecordReader.readSocketUnits(item) : [];

    const sections = new RecordSections(
      this.data,
      this.items,
      this.types,
      identity,
      player,
      stats,
      includeSockets ? ItemStatReader.readSockets(item) : new Map<number, number>(),
      baseStats,
      socketUnits,
      options.clientPlayer === null || options.clientPlayer === undefined
        ? null
        : ItemRecordReader.readViewer(options.clientPlayer),
    );

    const context = sections.createContext(options.difficulty ?? 0);
    context.shopMode = options.shopMode ?? 0;

    return {
      sections,
      composer: new ItemTooltipComposer(sections, sections.createModifierGenerator(modifierStats)),
      context,
      kind: ItemTooltipComposer.classify(context),
      modifierStats,
      identity,
      viewer: player,
      stats,
    };
  }
}
