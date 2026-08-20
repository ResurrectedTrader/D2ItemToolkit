import {
  ItemRecordReader,
  type ItemIdentity,
  type ItemUnit,
  type ItemViewer,
} from '../Stats/ItemRecord.js';
import { ItemStatOps } from '../Stats/ItemStatOps.js';
import { ItemStatReader, ItemStatView, sortByKey } from '../Stats/ItemStatReader.js';
import type { Unit } from '../Stats/Unit.js';
import { SocketStatSynthesis } from '../Stats/SocketStatSynthesis.js';
import { ArgumentNullException, Int32 } from '../Types.js';
import { ItemTable } from '../Tables/ItemTable.js';
import { ItemInventoryColor } from '../Tables/ItemInventoryColor.js';
import { ItemInventoryGraphics } from '../Tables/ItemInventoryGraphics.js';
import { ItemTypeTree } from '../Tables/ItemTypeTree.js';
import { D2DataFiles } from '../Tables/TxtDataProviders.js';
import {
  ItemTooltipColor,
  ItemTooltipComposer,
  ItemTooltipKind,
  type ItemTooltipContext,
  type ItemTooltipLine,
} from './ItemTooltip.js';
import { RecordSections } from './RecordSections.js';
import { EquipRequirements } from './EquipRequirements.js';
import { RequiredLevelCalculator } from './RequiredLevelCalculator.js';
import { SetTable } from '../Tables/SetTable.js';
import { SetItemTooltipBuilder, type SetItemTooltipInput } from './SetItemTooltip.js';
import { NotSupportedException } from '../Types.js';

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

  for (const socket of ItemStatReader.enumerateSockets(item)) {
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

    const composed = this.compose(item, viewer, options, options.includeSockets ?? true);

    if (composed.kind === ItemTooltipKind.IdentifiedSetItem) {
      // Nothing in the item document says which siblings the viewer owns, so the default input is
      // "none", which paints every piece red and selects no tier — exactly what the game draws for
      // a character carrying this piece alone.
      return this.renderSetItem(item, {}, viewer, options);
    }

    const lines =
      composed.kind === ItemTooltipKind.Book
        ? composed.composer.composeBook(composed.context)
        : composed.composer.compose(composed.context, composed.modifierStats);

    return TooltipEngine.tooltip(composed, lines, options, ItemTooltipComposer.MaxTooltipLength);
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
    const first = item.sockets[0];
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

    return {
      base: this.describe(
        item,
        viewer,
        options,
        ItemStatReader.reconstructView(item, ItemStatView.baseOnly()),
      ),
      magic: this.describe(
        item,
        viewer,
        options,
        ItemStatReader.reconstructView(item, itemOwnMods()),
      ),
      sockets: this.describe(item, viewer, options, socketContributions(item, this.socketStats)),
      setBonuses: this.describe(
        item,
        viewer,
        options,
        ItemStatReader.reconstructView(item, ItemStatView.setBonuses(false)),
      ),
    };
  }

  private describe(
    item: Unit,
    viewer: Unit | null,
    options: TooltipOptions,
    selected: Map<number, number>,
  ): readonly ItemTooltipLine[] {
    const composed = this.compose(item, viewer, options, true);

    // The composer built for THIS selection, so the generator's value source and the block's
    // colour carry match what a full render of the same stats would produce.
    const composer = new ItemTooltipComposer(
      composed.sections,
      composed.sections.createModifierGenerator(selected),
    );

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
