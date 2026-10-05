import { ItemRecordFlags, type ItemIdentity } from './ItemRecord.js';
import { ItemStatReader } from './ItemStatReader.js';
import { ItemStatOps } from './ItemStatOps.js';
import { Int32 } from '../Types.js';
import { PropertyApplier, RollEnd, type ItemProperty } from './PropertyApplier.js';
import type { ItemTable } from '../Tables/ItemTable.js';
import type { ItemTypeTree } from '../Tables/ItemTypeTree.js';
import type { MagicAffixTable } from '../Tables/MagicAffixTable.js';
import { PropertiesTable } from '../Tables/PropertiesTable.js';
import { PropertyGroupsTable, PropertyRefKind } from '../Tables/PropertyGroupsTable.js';
import type { SetTable } from '../Tables/SetTable.js';
import type { TxtFile } from '../Data/TxtFile.js';
import type { D2DataFiles, TxtItemStatCostTable } from '../Tables/TxtDataProviders.js';

/**
 * Where a reconstructed range came from. Flags, because two sources can land on one stat — a
 * unique's own `res-all` and a socketed rune's, for instance.
 */
export enum RollSources {
  None = 0,

  /** The base item's own rolled Defense, armor.txt `minac`..`maxac`. */
  Base = 1,

  /** A magic, rare or crafted affix, from the ids the record stores. */
  Affix = 2,
  Unique = 4,
  SetItem = 8,

  /** An earned set tier's partial or full bonus. */
  SetBonus = 16,
  Runeword = 32,

  /** A socket filler's gem/rune mods. */
  Socket = 64,

  /** A superior item's qualityitems.txt modifier. */
  Superior = 128,

  /** The fixed mods of the cubemain.txt recipe a crafted item was made by. */
  Crafted = 256,

  /** Written through a PropertyGroups.txt pick or a func-25 stat pick; ORed with the outer source. */
  PropertyGroup = 512,
}

/**
 * How a choice picks. The first three are PropertyGroups.txt `pickmode` (sub_14028A190
 * 0x14028a2c1..0x14028a2e5); `StatPick` is func 25's one-of-K stat.
 */
export enum ChoicePickMode {
  /** pickmode 0: every entry, in column order. */
  All = 0,

  /** pickmode 1, sub_14028A6D0: N entries without replacement. */
  Exactly = 1,

  /** pickmode 2, sub_14028A970: N draws with replacement, so up to N entries. */
  UpTo = 2,

  /** ITEMMODS_PropertyFunc25 0x140289d70: one stat of the target property's. */
  StatPick = 25,
}

export enum ChoiceResolution {
  /** No record to resolve against; every outcome is listed. */
  NoRecord = 0,

  /** Exactly one outcome of this choice agrees with the record. */
  Resolved = 1,

  /** More than one outcome agrees with the record. */
  Ambiguous = 2,

  /** No combination of outcomes agrees with the record; every outcome is listed. */
  Contradicted = 3,

  /**
   * Not enumerated — a layer-rolling or func-25 option, a param span over 64, more than 1024
   * outcomes, or more than 4096 combinations across the item. Contributes nothing.
   */
  Unsupported = 4,
}

/** One applied entry of an outcome: which option, at which param. */
export interface RolledChoicePick {
  /** Index into `RolledChoice.options`. */
  readonly option: number;
  readonly param: number;
}

/** One option applied ALONE at one param, both ends. */
export interface RolledChoiceVariant {
  /** The param this variant was applied at; for `skilltab` it selects the layer. */
  readonly param: number;
  readonly stats: readonly RolledStatRange[];
}

export interface RolledChoiceOption {
  /** The group's column number, 1..8; for a stat pick, the target row's set, 1..7. */
  readonly entry: number;

  /** The Properties.txt row applied; -1 for a nested group. */
  readonly propertyId: number;
  readonly code: string;

  /** max(chance, 1), 0x14028a050. */
  readonly weight: number;
  readonly paramLow: number;
  readonly paramHigh: number;

  /** `modMin`/`modMax` as authored; a nested group's count. */
  readonly min: number;
  readonly max: number;

  /** One per param in paramLow..paramHigh; empty for a nested group. */
  readonly variants: readonly RolledChoiceVariant[];

  /**
   * A group entry naming an earlier group, or null. Its outcomes are folded into this choice's; the
   * nested node itself is descriptive and always reports NoRecord or Unsupported.
   */
  readonly nested: RolledChoice | null;
}

/**
 * A property cell whose stats are PICKED at spawn rather than fixed: a PropertyGroups.txt code
 * (kind-1 link, sub_140214F40 0x140214fb9) or a func-25 property. The record holds only the
 * outcome, so the choice is resolved by keeping the outcomes consistent with it.
 */
export interface RolledChoice {
  /** The PropertyGroups.txt row; -1 for a stat pick. */
  readonly groupRow: number;

  /** The group code, or the func-25 property's code. */
  readonly code: string;

  /** The outer source ORed with `RollSources.PropertyGroup`. */
  readonly sources: RollSources;
  readonly pickMode: ChoicePickMode;

  /** The fewest entries N can apply, after the 0x14028a738 rule. */
  readonly countLow: number;
  readonly countHigh: number;
  readonly options: readonly RolledChoiceOption[];
  readonly resolution: ChoiceResolution;

  /**
   * The outcomes of THIS choice that agree with the record — every outcome for NoRecord and
   * Contradicted, none for Unsupported. Each is a list of picks ordered by option; an empty list is
   * the "nothing applied" outcome.
   */
  readonly consistent: readonly (readonly RolledChoicePick[])[];
}

/** One stat's reconstructed span, as the item's own sources could have rolled it. */
export interface RolledStatRange {
  readonly statId: number;

  /** The stat's layer — a skill id, a class, a skill tab. 0 for a plain stat. */
  readonly layer: number;

  /** The value when every contributing property rolls its minimum. */
  readonly low: number;

  /** The value when every contributing property rolls its maximum. */
  readonly high: number;

  /**
   * Which sources contribute. Advisory: the low/high values come from one combined application of
   * every property, so a stat two sources both write carries both flags but is not split between
   * them.
   */
  readonly sources: RollSources;

  /** False when the stat could only ever have taken one value. */
  readonly isRange: boolean;

  /**
   * True when the value is a PACKED encoding rather than a magnitude, so low and high are not a
   * range anyone should show: stat 204 packs `(maxCharges << 8) + current` (func 19, 0x65f84b) and
   * stats 268..303 pack `param + 4 * ((max + 256) << 10 | (min + 256))` (func 18, 0x65f934).
   *
   * The span is still correct — it is the span of the packed word — which is exactly why it must be
   * flagged: printed raw it reads as "(5/9 Charges) [2306-2313]". Both encodings already carry
   * their own two ends inside the value, so a caller wanting a real range there should decode
   * rather than subtract.
   */
  readonly isPackedEncoding: boolean;

  /**
   * The low end as a READER sees it, with a packed value decoded.
   *
   * For stat 204 that is the CURRENT charge count: the value is `(maxCharges << 8) + current`, the
   * high byte is identical at both ends because the max is fixed by the property, and only the low
   * byte is drawn off the seed (0x65f7ec..0x65f80e). So the low byte alone is the whole span, and it
   * is the number the "(5/9 Charges)" line shows first.
   *
   * The by-time stats need no decoding: func 18 packs property.min and property.max straight in and
   * **never rolls** (0x65f870 has no RollRandomValue call), so both ends produce the identical word
   * and `isRange` is always false for them. They are in `isPackedEncoding` defensively, not because
   * a span can appear there.
   */
  readonly displayLow: number;

  /** The high end, decoded the same way as `displayLow`. */
  readonly displayHigh: number;
}

const StatArmorPercent = 16;
const StatChargedSkill = 204;
const FirstByTime = 268;
const LastByTime = 303;

/**
 * The same test as `RolledStatRange.isPackedEncoding`, for a bare stat id — so a caller deciding
 * which stats may be summed reads the rule from here rather than deriving its own from `descFunc`.
 * Two derivations of one fact drift; this is the owner.
 */
export function isPackedStat(statId: number): boolean {
  return packedEncoding(statId);
}

function packedEncoding(statId: number): boolean {
  return statId === StatChargedSkill || (statId >= FirstByTime && statId <= LastByTime);
}

function displayValue(statId: number, packed: number, valShift: number): number {
  if (statId === StatChargedSkill) {
    return packed & 0xff;
  }

  // A packed triple is not a magnitude, so shifting it would corrupt it rather than scale it.
  if (packedEncoding(statId)) {
    return packed;
  }

  // itemstatcost ValShift. Life, mana and stamina are stored 8.8 fixed point and every WRITER
  // shifts them down before printing, so a span that skipped it read 256x too large:
  // "+11 to Life [2816-3840]".
  return packed >> valShift;
}

/**
 * A property whose ROLL picks the stat's LAYER instead of its value — funcs 12 and 36. The value is
 * fixed; what varies is which skill or class it lands on.
 */
export interface RolledLayerRange {
  readonly statId: number;

  /** The lowest layer the roll could land on — inclusive. */
  readonly layerLow: number;

  /** The highest layer the roll could land on — inclusive. */
  readonly layerHigh: number;

  /** The value, which does not vary. Ormus' Robes is always +3, to one of 25 skills. */
  readonly value: number;

  readonly sources: RollSources;
}

/**
 * The spans an item's stats could have rolled within, reconstructed from the tables its own record
 * points at. Like a breakdown this is a capability the game does not have, so it cannot be checked
 * against the original; what it can be checked against is the item's OWN recorded values, which
 * must fall inside the spans claimed for them.
 */
export interface ItemRollRanges {
  /** Every stat a reconstructed property explains, ordered by stat then layer. */
  readonly stats: readonly RolledStatRange[];

  /**
   * Properties whose ROLL picks the layer rather than the value — funcs 12 and 36, `skill-rand` and
   * `randclassskill`. Kept apart from {@link stats} because a span of VALUES is the wrong shape for
   * them: the value is fixed and the layer is what varies.
   */
  readonly layerVaries: readonly RolledLayerRange[];

  /**
   * The picks the item's sources make — PropertyGroups.txt codes and func-25 properties — in gather
   * order. The resolved outcomes are already folded into {@link stats}.
   */
  readonly choices: readonly RolledChoice[];

  /**
   * Stat ids the item carries whose recorded value falls OUTSIDE the span reconstructed for it.
   * Always empty for a record the game produced; a non-empty list means the reconstruction is
   * wrong, so it is surfaced rather than hidden.
   */
  readonly outOfRange: readonly number[];

  /**
   * Stat ids the item carries that no reconstructed property accounts for. Expected to be non-empty
   * in ordinary use — a charm's own base stats, anything the producer synthesised — so this is a
   * coverage report, not an error.
   */
  readonly unattributed: readonly number[];

  /**
   * Property ids whose value the game derives from the ITEM's level, which a record need not carry
   * (funcs 11, 14 and 19). Their spans are floored rather than exact.
   */
  readonly itemLevelDependent: readonly number[];

  /** Property funcs reached that this port does not implement. Func 9 only. */
  readonly unsupportedFuncs: readonly number[];

  /**
   * True for a crafted item: the record stores its affixes but NOT which cubemain.txt recipe made
   * it, so the recipe's fixed mods cannot be attributed. The affixes still are.
   */
  readonly craftedRecipeUnknown: boolean;

  /**
   * The cubemain.txt row the item was crafted from, or -1 when it is not crafted or the recipe
   * could not be pinned.
   */
  readonly craftedRecipe: number;
}

/**
 * One gathered property and the source that contributed it. A `seed` marks a choice instead, and
 * `property` is then unused.
 */
interface Sourced {
  readonly property: ItemProperty;
  readonly source: RollSources;
  readonly seed?: ChoiceSeed;
  /**
   * D2R: a set item's tier mod (aprop with `add func` != 0). sub_1402866E0 case 4 writes it to a
   * STATE_ITEMSET list, states 165..169 with flags 0x2040 (0x140286a18-0x140286a68), so it is not one
   * of the item's own stats. It still reaches func 2 with pItem = the item, which is why it keeps
   * maximising the base Defense. Once its tier is earned the list counts toward the drawn Defense,
   * so it joins that span (and only that one).
   */
  readonly maximiseOnly?: boolean;
  /**
   * The STATE_ITEMSET list a `maximiseOnly` tier mod lands in: aprop N goes to state 164 + N
   * (`dword_141641400[k>>1]`, 0x140286a33).
   */
  readonly tierState?: number;
}

/** A group cell (outer min/max = the count) or a func-25 property. */
interface ChoiceSeed {
  readonly groupRow: number;
  readonly min: number;
  readonly max: number;
  readonly statPick: ItemProperty | null;
}

const OptionProperty = 0;
const OptionNested = 1;
const OptionStat = 2;

interface OptionModel {
  kind: number;
  propertyId: number;
  modMin: number;
  modMax: number;
  statId: number;
  statSet: number;
  statProperty: ItemProperty | null;
  nested: ChoiceNode | null;
  paramLow: number;
  paramHigh: number;
}

interface Pick {
  readonly option: number;
  readonly param: number;
  readonly nested: Pick[] | null;
}

type Writable<T> = { -readonly [K in keyof T]: T[K] };

interface ChoiceNode {
  public: Writable<RolledChoice>;
  sources: RollSources;
  unsupported: boolean;
  readonly options: OptionModel[];
  outcomes: Pick[][];
  readonly keys: Set<number>;
}

const MaxParamSpan = 64;
const MaxOutcomesPerChoice = 1024;
const MaxCombos = 4096;
const StatPickFunc = 25;

function elementAt<T>(items: readonly T[], index: number): T {
  const found = items[index];
  if (found === undefined) {
    throw new RangeError('index ' + String(index));
  }

  return found;
}

const StatDefense = 31;

// Quality numbers, matching ItemQuality in the tooltip layer.
const QualityHighQuality = 3;
const QualitySet = 5;
const QualityRare = 6;
const QualityUnique = 7;
const QualityCrafted = 8;

const CraftedModsPerRecipe = 5;

/**
 * The nine slots the crafted recipes cover, as itemtypes.txt codes. Disjoint over the shipped tree
 * — of the 98 itemtypes rows carrying a code none is under two of them, and of the 659 items 481
 * are under one and 178 under none — so the order here is inert. What it does decide is which
 * shields resolve at all; see `craftSlotOf`.
 */
const CraftSlots: readonly string[] = [
  'helm',
  'tors',
  'shie',
  'glov',
  'boot',
  'belt',
  'amul',
  'ring',
  'weap',
];

// Column in qualityitems.txt -> the ItemTypes code it gates on.
const SuperiorGates: readonly (readonly [string, string])[] = [
  ['armor', 'armo'],
  ['weapon', 'weap'],
  ['shield', 'shld'],
  ['thrown', 'thro'],
  ['scepter', 'scep'],
  ['wand', 'wand'],
  ['staff', 'staf'],
  ['bow', 'bow'],
  ['boots', 'boot'],
  ['gloves', 'glov'],
  ['belt', 'belt'],
];

/**
 * Rebuilds the property list an item's own sources would have rolled from, applies it at both ends
 * of every range, and reports the difference.
 *
 * The ends come from {@link RollEnd}: the traced handlers are run twice, unchanged, so a span is
 * whatever the real code produces at each end rather than an arithmetic guess. That is also why an
 * unimplemented func or an absent item level degrades into a report instead of a wrong number.
 */
export class RolledRangeReconstructor {
  private readonly groups: PropertyGroupsTable;

  constructor(
    private readonly data: D2DataFiles,
    private readonly items: ItemTable,
    private readonly types: ItemTypeTree,
    private readonly affixes: MagicAffixTable,
    private readonly sets: SetTable,
  ) {
    this.groups = new PropertyGroupsTable(
      data.propertyGroups,
      new PropertiesTable(data.properties, data.itemStatCost),
      data.paramLinker,
    );
  }

  /**
   * `includeBaseDefense` false drops the armour's own `minac`..`maxac` roll, leaving the item's
   * MODIFIERS alone. The Defense SECTION draws the base plus every modifier and wants it; a
   * `+45 Defense` modifier line draws its own contribution and does not â with it, that line was
   * offered the section's span.
   *
   * `includeOwnSources` false applies ONLY `socketProperties` — no affixes, no unique row, nothing
   * of the item's own. That is what a socket-only view needs: asking for "just the fillers" while
   * the identity's own sources were folded in silently gave a gem's line the HOST's affix span.
   *
   * `earnedTierStates` are the set-tier states (165..169) whose list no longer carries
   * STATLIST_SET: ITEMS_RecalculateSetItemSpecificMods 0x14028ab90 clears the bit once the tier is
   * earned, and the Defense line then counts that list. They join the Defense span only; their
   * other stats are drawn in the tier block.
   *
   * `viewerStat` is the unit the Defense line attaches the item to (STATLIST_MergeStatLists
   * 0x1401d1df1), which re-runs ops 4/5 against it.
   */
  reconstruct(
    item: ItemIdentity,
    recorded: Map<number, number> | null,
    socketProperties: readonly ItemProperty[] | null,
    earnedSetIds: readonly number[] | null,
    includeOwnSources = true,
    includeBaseDefense = true,
    earnedTierStates: ReadonlySet<number> | null = null,
    viewerStat: ((statId: number) => number) | null = null,
  ): ItemRollRanges {
    const gathered: Sourced[] = [];

    // -1 unless the item is crafted AND its recipe was pinned. A socket-only pass never gathers the
    // item's own sources, so it leaves this untouched.
    let craftedRecipe = -1;

    // A PropertyApplier is needed before gathering, because every source stores property CODES and
    // only the table can turn one into an id.
    const low = new PropertyApplier(this.data, this.items, this.types, RollEnd.Low);
    const high = new PropertyApplier(this.data, this.items, this.types, RollEnd.High);

    if (includeOwnSources) {
      gathered.push(...this.gather(item, low.properties, earnedSetIds));
      craftedRecipe = this.gatherCrafted(item, low, gathered, recorded);
    }

    for (const property of socketProperties ?? []) {
      gathered.push({ property, source: RollSources.Socket });
    }

    const lowStats = new Map<number, number>();
    const highStats = new Map<number, number>();
    const sourceOf = new Map<number, RollSources>();
    const layerVaries: RolledLayerRange[] = [];
    const choices: ChoiceNode[] = [];
    const tierLow = new Map<number, number>();
    const tierHigh = new Map<number, number>();

    for (const entry of gathered) {
      if (entry.maximiseOnly === true) {
        if (
          earnedTierStates?.has(entry.tierState ?? 0) === true &&
          entry.seed === undefined &&
          !RolledRangeReconstructor.rollsTheLayer(low.properties, entry.property.propertyId)
        ) {
          low.apply(PropertyApplier.PropModeGem, item, entry.property, tierLow);
          high.apply(PropertyApplier.PropModeGem, item, entry.property, tierHigh);
        }

        continue;
      }

      if (entry.seed !== undefined) {
        choices.push(this.buildChoice(low, high, item, entry.seed, entry.source));
        continue;
      }

      // A layer-rolling property is pulled out BEFORE the combined application, because summing it
      // into the totals would add one arbitrary layer's value to them.
      if (RolledRangeReconstructor.rollsTheLayer(low.properties, entry.property.propertyId)) {
        RolledRangeReconstructor.addLayerRange(low, high, item, entry, layerVaries);
        continue;
      }

      low.apply(PropertyApplier.PropModeGem, item, entry.property, lowStats);
      high.apply(PropertyApplier.PropModeGem, item, entry.property, highStats);

      // Attribution runs into scratch maps so one property's keys can be told apart from the
      // combined totals. BOTH ends are scanned: a property whose low end truncates to nothing still
      // writes at its high end, and attributing only the low one left those stats sourceless.
      RolledRangeReconstructor.attribute(low, item, entry, sourceOf);
      RolledRangeReconstructor.attribute(high, item, entry, sourceOf);
    }

    const combos = RolledRangeReconstructor.combos(choices);
    const contested = RolledRangeReconstructor.contestedKeys(choices);
    const maximisedByG = RolledRangeReconstructor.maximisesBaseDefense(
      gathered,
      low.properties,
      this.data.isResurrected,
    );

    const lows: Map<number, number>[] = [];
    const highs: Map<number, number>[] = [];
    const sources: Map<number, RollSources>[] = [];

    for (const combo of combos) {
      const lowX = new Map<number, number>(lowStats);
      const highX = new Map<number, number>(highStats);
      const sourceX = new Map<number, RollSources>(sourceOf);
      let maximised = maximisedByG;

      for (let c = 0; c < choices.length; ++c) {
        const node = elementAt(choices, c);
        for (const pick of elementAt(node.outcomes, elementAt(combo, c))) {
          RolledRangeReconstructor.applyPick(low, item, node, pick, lowX);
          RolledRangeReconstructor.applyPick(high, item, node, pick, highX);
          RolledRangeReconstructor.attributePick(low, high, item, node, pick, sourceX);
          if (
            !(this.data.isResurrected && RolledRangeReconstructor.fillerAssigned(node.sources)) &&
            RolledRangeReconstructor.pickWritesStat(low.properties, node, pick, StatArmorPercent)
          ) {
            maximised = true;
          }
        }
      }

      // Gated with the rest of the item's own sources: the base armour roll IS one, so a
      // socket-only reconstruction that added it gave a gem block the HOST's base span —
      // "+30 Defense [33-35]" where 33-35 was the cap's 3..5 plus the rune's fixed 30.
      //
      // The BASE view at each end is kept apart from the merged one because op 13 consumes the two
      // separately (STATLIST_LookupBaseStatWithMinAccr 0x624ed0 reads `Stats`, the result lands in
      // FullStats at 0x625158). Only Defense rolls a base.
      const lowBase = new Map<number, number>();
      const highBase = new Map<number, number>();
      const defenseSection = includeOwnSources && includeBaseDefense;
      if (defenseSection) {
        this.addBaseDefense(item, lowX, highX, lowBase, highBase, sourceX, maximised);
      }

      // Taken before op 13 resolves lowX, because the drawn Defense resolves its own copy.
      let defenseLow: number | undefined;
      let defenseHigh: number | undefined;
      if (defenseSection && (tierLow.size !== 0 || tierHigh.size !== 0 || viewerStat !== null)) {
        defenseLow = this.drawnDefense(lowX, tierLow, lowBase, viewerStat);
        defenseHigh = this.drawnDefense(highX, tierHigh, highBase, viewerStat);
      }

      // The Defense line draws the OP-RESOLVED value, so its span has to be resolved too. A Large
      // Shield rolling 12..14 under +150% Enhanced Defense prints 32, which the unresolved 12..14
      // can never contain.
      this.resolveBaseOps(lowX, lowBase);
      this.resolveBaseOps(highX, highBase);

      if (defenseLow !== undefined || defenseHigh !== undefined) {
        const defenseKey = ItemStatReader.packStatKey(0, StatDefense);
        lowX.set(defenseKey, defenseLow ?? 0);
        highX.set(defenseKey, defenseHigh ?? 0);
        if (tierLow.has(defenseKey) || tierHigh.has(defenseKey)) {
          sourceX.set(
            defenseKey,
            (sourceX.get(defenseKey) ?? RollSources.None) | RollSources.SetItem,
          );
        }
      }

      lows.push(lowX);
      highs.push(highX);
      sources.push(sourceX);
    }

    const outside = new Set<number>();
    const kept = RolledRangeReconstructor.resolve(
      choices,
      combos,
      lows,
      highs,
      contested,
      recorded,
      outside,
    );

    const stats = this.collectKept(kept, lows, highs, sources);

    stats.sort((a, b) => a.statId - b.statId || a.layer - b.layer);
    layerVaries.sort((a, b) => a.statId - b.statId || a.layerLow - b.layerLow);

    for (const stat of RolledRangeReconstructor.outOfRange(stats, recorded)) {
      outside.add(stat);
    }

    return {
      stats,
      layerVaries,
      choices: choices.map(node => node.public),
      outOfRange: [...outside].sort((a, b) => a - b),
      unattributed: RolledRangeReconstructor.unattributed(kept, lows, highs, layerVaries, recorded),
      itemLevelDependent: RolledRangeReconstructor.merge(
        low.itemLevelDependent,
        high.itemLevelDependent,
      ),
      unsupportedFuncs: RolledRangeReconstructor.merge(low.unsupportedFunc, high.unsupportedFunc),
      craftedRecipeUnknown: item.quality === QualityCrafted && craftedRecipe < 0,
      craftedRecipe,
    };
  }

  /**
   * Every property the item's OWN sources contribute. Exposed so a caller can fold a socket filler
   * that carries its own affixes — a jewel — into the host's spans, which is what the merged render
   * needs: the line it draws is the SUM of both, so the span must be too.
   */
  ownProperties(item: ItemIdentity): ItemProperty[] {
    const applier = new PropertyApplier(this.data, this.items, this.types);

    // No crafted recipe: this method's caller folds a socket filler into a host, and no filler is
    // crafted. Nor does a filler carry a choice: the group prefixes are `lcha` and `tors` only.
    return this.gather(item, applier.properties, null)
      .filter(entry => entry.seed === undefined && entry.maximiseOnly !== true)
      .map(entry => entry.property);
  }

  private gather(
    item: ItemIdentity,
    properties: PropertiesTable,
    earnedSetIds: readonly number[] | null,
  ): Sourced[] {
    const gathered: Sourced[] = [];

    // A runeword's magicPrefix[0] is a string id, not an affix id, so the two are mutually
    // exclusive rather than additive.
    if ((item.flags & ItemRecordFlags.Runeword) !== 0) {
      this.gatherRuneword(item, properties, gathered);
    } else {
      this.gatherAffixes(item, properties, gathered);
    }

    this.gatherUnique(item, properties, gathered);
    this.gatherSetItem(item, properties, gathered);
    this.gatherSetBonuses(earnedSetIds, gathered);
    this.gatherSuperior(item, properties, gathered);

    return gathered;
  }

  /**
   * A crafted item's recipe is not in its record, but it is deducible from the shape of
   * cubemain.txt: the 36 crafted rows are **four families over nine equipment slots**, with exactly
   * one row per (family, slot). The output cell is `usetype,crf` — the crafted item keeps the
   * input's type — so the recipe's slot is the item's own slot, which narrows the field to four.
   * `pickByRecordedStats` then keeps the one candidate EVERY stat of which the record carries.
   *
   * Matching on the SLOT rather than on `input 1`'s exact base code is deliberate. That cell is not
   * a plain item code — four of the 36 name an item TYPE (`blun`, `axe`, `rod`, `spea`), `amul` and
   * `ring` are types with no item of that code at all, and 24 carry a trailing `upg`. How the cube
   * resolves it is not traced here, and it does not need to be: whatever it accepts, the accepted
   * item is in the recipe's slot, and the slot is all this needs.
   *
   * Returns the cubemain row, or -1 when no recipe could be pinned.
   */
  private gatherCrafted(
    item: ItemIdentity,
    low: PropertyApplier,
    gathered: Sourced[],
    recorded: Map<number, number> | null,
  ): number {
    const cube = this.data.cubeMain;
    if (item.quality !== QualityCrafted || cube === null) {
      return -1;
    }

    const slot = this.craftSlotOf(item.classId);
    if (slot < 0) {
      return -1;
    }

    const candidates: number[] = [];
    for (let row = 0; row < cube.rowCount; ++row) {
      if (this.isCraftedRecipe(cube, row) && this.recipeSlot(cube, row) === slot) {
        candidates.push(row);
      }
    }

    const chosen = this.pickByRecordedStats(item, low, cube, candidates, recorded);
    if (chosen < 0) {
      return -1;
    }

    this.addRecipeMods(cube, low.properties, gathered, chosen);
    return chosen;
  }

  /**
   * Index into {@link CraftSlots}, or -1 for an item no recipe covers.
   *
   * -1 for 30 shields, because `shie` is the slot and the class shields hang off `shld` instead: 15
   * paladin auric shields (`ashd`) and 15 necromancer voodoo heads (`head`). That is correct rather
   * than merely harmless, and `shld` would be wrong. The four shield recipes name `gts`, `spk`,
   * `sml` and `kit` — item codes, none of which is also a type code — and all twelve items in their
   * ubercode/ultracode chains are plain `shie`. So no reading of the cell reaches a class shield:
   * not the code, not the code plus its upgrade tiers, and not the code's own type, since `ashd`
   * and `head` are SIBLINGS of `shie` under `shld` rather than descendants. Only a grandparent
   * climb would, and that same reading would have the `crn` helm recipe accept everything under
   * `armo`.
   */
  private craftSlotOf(classId: number): number {
    const primary = this.types.row(this.items.primaryTypeCode(classId));
    const secondary = this.types.row(this.items.secondaryTypeCode(classId));

    for (let i = 0; i < CraftSlots.length; ++i) {
      const slot = this.types.row(CraftSlots[i]);
      if (slot >= 0 && this.types.isOfType(primary, secondary, slot)) {
        return i;
      }
    }

    return -1;
  }

  /**
   * The slot a recipe produces, from `input 1`'s first cell. The cell is either an item code or an
   * item TYPE code, so both are tried — but only to reach the slot, never to decide whether the
   * cube would accept a particular base.
   */
  private recipeSlot(cube: TxtFile, row: number): number {
    const spec = cube.getString(row, 'input 1').replace(/"/g, '');
    const comma = spec.indexOf(',');
    const code = (comma < 0 ? spec : spec.slice(0, comma)).trim();

    if (code.length === 0) {
      return -1;
    }

    const classId = this.items.classIdForCode(code);
    if (classId >= 0) {
      return this.craftSlotOf(classId);
    }

    const typeRow = this.types.row(code);
    if (typeRow < 0) {
      return -1;
    }

    for (let i = 0; i < CraftSlots.length; ++i) {
      const slot = this.types.row(CraftSlots[i]);
      if (slot >= 0 && this.types.isUnder(typeRow, slot)) {
        return i;
      }
    }

    return -1;
  }

  /** Whether this cubemain row produces a crafted item. */
  private isCraftedRecipe(cube: TxtFile, row: number): boolean {
    return cube
      .getString(row, 'output')
      .replace(/"/g, '')
      .split(',')
      .some(part => part.trim() === 'crf');
  }

  private addRecipeMods(
    cube: TxtFile,
    properties: PropertiesTable,
    into: Sourced[],
    row: number,
  ): void {
    for (let mod = 1; mod <= CraftedModsPerRecipe; ++mod) {
      this.addProperty(
        properties,
        into,
        RollSources.Crafted,
        cube.getString(row, 'mod ' + String(mod)),
        cube.getString(row, 'mod ' + String(mod) + ' param'),
        cube.getInt(row, 'mod ' + String(mod) + ' min'),
        cube.getInt(row, 'mod ' + String(mod) + ' max'),
      );
    }
  }

  /**
   * Picks between the four recipes sharing a slot by asking which one's fixed mods the item
   * actually carries. A recipe's mods always apply — every `mod N chance` cell is blank and every
   * roll bottoms out at 1 or more, so none can truncate to the nothing a zero value writes
   * (0x65ea63) — which makes "every stat this recipe writes is recorded" a sound filter rather than
   * a heuristic.
   *
   * Anything other than exactly one survivor leaves the recipe unknown rather than guessed: the
   * item's own affixes can supply a rival family's stats by chance, and a wrong recipe would
   * attribute spans to stats that never rolled from it.
   *
   * The stat KEYS come from APPLYING each candidate rather than from reading its property rows, so
   * a mod writing several stats is handled by the same traced code that writes it for real.
   */
  private pickByRecordedStats(
    item: ItemIdentity,
    low: PropertyApplier,
    cube: TxtFile,
    candidates: readonly number[],
    recorded: Map<number, number> | null,
  ): number {
    if (recorded === null) {
      return -1;
    }

    let viable = -1;
    let count = 0;

    // Probing through the CALLER's applier rather than a throwaway one would normally risk a losing
    // candidate polluting itemLevelDependent or unsupportedFunc. It cannot here: the 36 crafted
    // rows between them reach only funcs 1, 2, 7, 8 and 11, so no func 9 and no func 14 or 19, and
    // the single func-11 code `gethit-skill` ships max 4, which skips the item-level arm.
    //
    // Probed at the LOW end only, and `dmg%` (func 7) is the one crafted mod whose written stat
    // KEYS depend on the rolled value: enhancedDamage writes stats 17 and 18 unless
    // `value * maxdam / 100` truncates to 0, where it degrades to the max-damage family instead.
    // The probe can therefore disagree with the real roll only where the two ENDS disagree, which is
    // maxdam of exactly 2 — 35 floors to 0 there and 60 does not. Below that both ends degrade alike
    // and above it neither does, so neither is a hazard. The one `weap` item at 2 is `d33`, not
    // spawnable and of a type no recipe takes.
    for (const row of candidates) {
      const probe: Sourced[] = [];
      this.addRecipeMods(cube, low.properties, probe, row);

      const scratch = new Map<number, number>();
      for (const entry of probe) {
        if (entry.seed !== undefined) {
          continue;
        }

        low.apply(PropertyApplier.PropModeGem, item, entry.property, scratch);
      }

      if (scratch.size === 0) {
        continue;
      }

      let all = true;
      for (const key of scratch.keys()) {
        if (!recorded.has(key)) {
          all = false;
          break;
        }
      }

      if (all) {
        viable = row;
        ++count;
      }
    }

    return count === 1 ? viable : -1;
  }

  /**
   * A key written at only ONE end is not an error and not a layer roll: the stat simply contributes
   * nothing at the other end, because a zero value writes nothing (0x65ea63). So the absent end is
   * a value of 0. `dmg%` does exactly this — at a low enough roll the enhanced-damage handler's
   * integer arithmetic truncates to nothing.
   */
  private static collectRanges(
    lowStats: Map<number, number>,
    highStats: Map<number, number>,
    sourceOf: Map<number, RollSources>,
    statCost: TxtItemStatCostTable,
  ): RolledStatRange[] {
    const keys = new Set<number>([...lowStats.keys(), ...highStats.keys()]);
    const stats: RolledStatRange[] = [];

    for (const key of keys) {
      const lowValue = lowStats.get(key) ?? 0;
      const highValue = highStats.get(key) ?? 0;

      // Normalised, because a negative property rolls its "high" end lowest — `dmg-ac` runs
      // -25..-40, so the arithmetic low is the second number.
      const min = Math.min(lowValue, highValue);
      const max = Math.max(lowValue, highValue);

      const statId = ItemStatReader.statFromKey(key);
      const valShift = statCost.tryGetStat(statId)?.valShift ?? 0;

      stats.push({
        statId,
        layer: ItemStatReader.layerFromKey(key),
        low: min,
        high: max,
        sources: sourceOf.get(key) ?? RollSources.None,
        isRange: min !== max,
        isPackedEncoding: packedEncoding(statId),
        displayLow: displayValue(statId, min, valShift),
        displayHigh: displayValue(statId, max, valShift),
      });
    }

    return stats;
  }

  /**
   * Whether any gathered property writes `item_armor_percent`, which is what sends the base
   * defense through ITEMMOD_MaximizeStatForEnhanced. Checked by STAT rather than by code, because
   * the game's dispatch table keys the handler off the property row's stat id.
   */
  private static maximisesBaseDefense(
    gathered: readonly Sourced[],
    properties: PropertiesTable,
    resurrected: boolean,
  ): boolean {
    for (const entry of gathered) {
      if (resurrected && RolledRangeReconstructor.fillerAssigned(entry.source)) {
        continue;
      }

      const row = entry.seed === undefined ? properties.rowAt(entry.property.propertyId) : null;
      if (row === null) {
        continue;
      }

      for (const stat of row.stat) {
        if (stat === StatArmorPercent) {
          return true;
        }
      }
    }

    return false;
  }

  // D2R assigns runeword and socket mods with pItem = the FILLER, not the host
  // (ITEMMODS_UpdateRuneword at 0x1402d39ec / 0x140286f5c; ITEMS_ApplyGemOrRuneAndRefreshSets
  // 0x1400a6772), so ITEMS_SetBaseStatValue (0x140287afd) tests a rune and never maximises the
  // host's Defense.
  private static fillerAssigned(source: RollSources): boolean {
    return (source & (RollSources.Runeword | RollSources.Socket)) !== 0;
  }

  /** True when any of the property's seven sets uses func 12 or 36. */
  private static rollsTheLayer(properties: PropertiesTable, propertyId: number): boolean {
    const row = properties.getRow(propertyId);
    if (row === null) {
      return false;
    }

    return row.func.some(func => func === 12 || func === 36);
  }

  /**
   * Applies one layer-rolling property at both ends: the two keys differ only in their layer and
   * carry the same value, which is the span of layers the roll could have chosen.
   */
  private static addLayerRange(
    low: PropertyApplier,
    high: PropertyApplier,
    item: ItemIdentity,
    entry: Sourced,
    into: RolledLayerRange[],
  ): void {
    const atLow = new Map<number, number>();
    const atHigh = new Map<number, number>();

    low.apply(PropertyApplier.PropModeGem, item, entry.property, atLow);
    high.apply(PropertyApplier.PropModeGem, item, entry.property, atHigh);

    for (const [key, value] of atLow) {
      const statId = ItemStatReader.statFromKey(key);
      const layerLow = ItemStatReader.layerFromKey(key);
      let layerHigh = layerLow;

      for (const other of atHigh.keys()) {
        if (ItemStatReader.statFromKey(other) === statId) {
          layerHigh = ItemStatReader.layerFromKey(other);
        }
      }

      into.push({
        statId,
        layerLow: Math.min(layerLow, layerHigh),
        layerHigh: Math.max(layerLow, layerHigh),
        value,
        sources: entry.source,
      });
    }
  }

  private static attribute(
    applier: PropertyApplier,
    item: ItemIdentity,
    entry: Sourced,
    sourceOf: Map<number, RollSources>,
  ): void {
    const scratch = new Map<number, number>();
    applier.apply(PropertyApplier.PropModeGem, item, entry.property, scratch);

    for (const key of scratch.keys()) {
      sourceOf.set(key, (sourceOf.get(key) ?? RollSources.None) | entry.source);
    }
  }

  /**
   * armor.txt rolls a base Defense between `minac` and `maxac` — the one base column that is a
   * genuine range. Weapon base damage and durability are single columns and do not roll.
   */
  private addBaseDefense(
    item: ItemIdentity,
    lowStats: Map<number, number>,
    highStats: Map<number, number>,
    lowBase: Map<number, number>,
    highBase: Map<number, number>,
    sourceOf: Map<number, RollSources>,
    maximised: boolean,
  ): void {
    let minac = this.items.getInt(item.classId, 'minac');
    let maxac = this.items.getInt(item.classId, 'maxac');
    if (minac <= 0 && maxac <= 0) {
      return;
    }

    // An `ac%` property does not just scale the base — it REPLACES it.
    //
    // ITEMMOD_MaximizeStatForEnhanced 0x65ccc0, cases 16 and 31: for an `armo` item (`push 32h` at
    // 0x65ccfc) with a non-zero maxac (0x65cd0c reads the items record at +0xD0, the same field
    // ITEM_RollBaseArmorClass rolls against), it computes `max(getUnitStat(31) + 1, maxac + 1)`
    // (0x65cd29-0x65cd30) and STORES it (0x65cd39). Every roll ITEM_RollBaseArmorClass can produce
    // is <= maxac — it halts the game otherwise (0x5563b2) — so both arms land on exactly
    // maxac + 1.
    //
    // Only `ac%` reaches it. The per-property dispatch table at 0x745b58 is {handler, statId} with
    // an 8-byte stride indexed by properties.txt row: row 0 `ac` (stat 31) takes
    // PropertyFunc_SimpleStatWrapper, which passes the "enhanced" flag as 0 (`push 0` at
    // 0x65d1ce), while row 5 `ac%` (stat 16) takes PropertyFunc_SimpleStatWrapper2, which passes 1
    // (`push 1` at 0x65d2be) — and ITEMMOD_ApplyRandomStatValue maximises unconditionally when
    // that flag is set (0x65cf52).
    //
    // So the base does not roll at all here: Skin of the Vipermagi is 127 every time, not 111..126,
    // and its Defense is a fixed 279 rather than a span.
    //
    // D2R's cube upgrades undo that: PLRTRADE_CreateCubeOutputs keeps the item for a `mod` output,
    // swaps its class and re-runs D2GAME_InitItemStats (0x1403c0251), whose armour roll is a plain
    // minac..maxac store (0x1402de731); only ApplyEthereality follows (0x1403c027b). The mods
    // survive, so `ac%` stays on an unmaximised base.
    if (maximised && this.data.isResurrected) {
      if (this.upgradedByCube(item)) {
        maximised = false;
      } else if (item.quality === QualityRare && this.cubeUpgradeTarget(item.classId)) {
        // A rare does not record whether it was upgraded, so it spans both outcomes: minac..maxac
        // rolled, or maxac + 1 native — contiguous. The ethereal scaling below is the same 3/2
        // either path applies.
        maximised = false;
        maxac += 1;
      }
    }

    if (maximised) {
      // 1.14d: the store is ABSOLUTE and reads the RAW items.txt maxac; its ordering against
      // ITEMMOD_ApplyEtherealBonus is untraced, so the literal reading is what is modelled. D2R
      // applies the quality mods first (the switch at 0x1402e0e12) and only then makes the item
      // ethereal (ITEMS_MakeEthereal 0x1402e14c0), which scales the stored base by 3/2
      // (ITEMMODS_ApplyEthereality 0x140286dc2). Sets are never made ethereal there.
      const fixedBase =
        this.data.isResurrected &&
        (item.flags & ItemRecordFlags.Ethereal) !== 0 &&
        !this.isOfType(item, 'weap') &&
        item.quality !== QualitySet
          ? Int32.div((maxac + 1) * 3, 2)
          : maxac + 1;
      const maximisedKey = ItemStatReader.packStatKey(0, StatDefense);

      lowStats.set(maximisedKey, (lowStats.get(maximisedKey) ?? 0) + fixedBase);
      highStats.set(maximisedKey, (highStats.get(maximisedKey) ?? 0) + fixedBase);
      lowBase.set(maximisedKey, fixedBase);
      highBase.set(maximisedKey, fixedBase);
      sourceOf.set(
        maximisedKey,
        (sourceOf.get(maximisedKey) ?? RollSources.None) | RollSources.Base,
      );
      return;
    }

    // ITEMMOD_ApplyEtherealBonus 0x65e4d0 scales the base by 3/2 ONCE at spawn — the six damage
    // stats for a `weap` item (0x65e51b onward, itemtypes row 45), stat 31 for anything else
    // (0x65e5d6). A captured ethereal item's recorded Defense therefore already includes it, so the
    // reconstructed span has to as well or it sits below the value it is meant to contain.
    //
    // `lea eax,[eax+eax*2]` then `cdq; sub eax,edx; sar eax,1` is a truncate-toward-zero halving,
    // which is what Int32.div gives.
    if ((item.flags & ItemRecordFlags.Ethereal) !== 0 && !this.isOfType(item, 'weap')) {
      minac = Int32.div(minac * 3, 2);
      maxac = Int32.div(maxac * 3, 2);
    }

    const key = ItemStatReader.packStatKey(0, StatDefense);
    lowStats.set(key, (lowStats.get(key) ?? 0) + minac);
    highStats.set(key, (highStats.get(key) ?? 0) + maxac);
    lowBase.set(key, minac);
    highBase.set(key, maxac);
    sourceOf.set(key, (sourceOf.get(key) ?? RollSources.None) | RollSources.Base);
  }

  /**
   * The Defense the line draws at one end: the item's stats plus its earned tier lists, op 13
   * resolved, then ops 4/5 against the viewer — the order compose applies them in. Undefined when
   * nothing writes Defense.
   */
  private drawnDefense(
    stats: ReadonlyMap<number, number>,
    tiers: ReadonlyMap<number, number>,
    baseStats: Map<number, number>,
    viewerStat: ((statId: number) => number) | null,
  ): number | undefined {
    const drawn = new Map<number, number>(stats);
    for (const [key, value] of tiers) {
      drawn.set(key, Int32.of((drawn.get(key) ?? 0) + value));
    }

    const preOp = new Map<number, number>(drawn);
    this.resolveBaseOps(drawn, baseStats);
    if (viewerStat !== null) {
      ItemStatOps.resolveLevelScaled(
        drawn,
        preOp,
        this.data.itemStatCost.levelScaledEntries,
        viewerStat,
      );
    }

    return drawn.get(ItemStatReader.packStatKey(0, StatDefense));
  }

  /**
   * Applies op 13 to one end of the reconstruction, writing back only the TARGET stats.
   *
   * The percent stats themselves are deliberately left in place. On the item they are dropped from
   * FullStats (0x626821), but the reconstruction feeds two different lines: the Defense line, which
   * draws the resolved target, and `+150% Enhanced Defense`, which is drawn from the modifier view
   * where the percent survives. Transplanting only the targets gives each line a span in its own
   * units.
   */
  private resolveBaseOps(stats: Map<number, number>, baseStats: Map<number, number>): void {
    if (baseStats.size === 0) {
      return;
    }

    const merged = new Map<number, number>(stats);
    ItemStatOps.resolve(merged, baseStats, this.data.itemStatCost);

    for (const entry of this.data.itemStatCost.percentOfBaseEntries) {
      const key = ItemStatReader.packStatKey(0, entry.targetStat);

      const resolved = merged.get(key);
      if (resolved !== undefined) {
        stats.set(key, resolved);
      }
    }
  }

  /**
   * The affix ids the record stores, resolved through the concatenated
   * [MagicSuffix][MagicPrefix][automagic] array. Covers magic, rare and the random half of a
   * crafted item, since all three store their affixes the same way.
   */
  private gatherAffixes(item: ItemIdentity, properties: PropertiesTable, into: Sourced[]): void {
    for (let slot = 0; slot < item.magicPrefix.length; ++slot) {
      this.addAffix(item.magicPrefix[slot] ?? 0, properties, into);
      this.addAffix(item.magicSuffix[slot] ?? 0, properties, into);
    }

    this.addAffix(item.autoAffix, properties, into);
  }

  private addAffix(affixId: number, properties: PropertiesTable, into: Sourced[]): void {
    const resolved = this.affixes.tryResolve(affixId);
    if (resolved === null) {
      return;
    }

    for (let mod = 1; mod <= 3; ++mod) {
      this.addProperty(
        properties,
        into,
        RollSources.Affix,
        resolved.table.getString(resolved.row, 'mod' + String(mod) + 'code'),
        resolved.table.getString(resolved.row, 'mod' + String(mod) + 'param'),
        resolved.table.getInt(resolved.row, 'mod' + String(mod) + 'min'),
        resolved.table.getInt(resolved.row, 'mod' + String(mod) + 'max'),
      );
    }
  }

  private gatherUnique(item: ItemIdentity, properties: PropertiesTable, into: Sourced[]): void {
    if (item.quality !== QualityUnique) {
      return;
    }

    const table = this.data.uniqueItems;
    if (table === null || item.fileIndex < 0 || item.fileIndex >= table.rowCount) {
      return;
    }

    for (let prop = 1; prop <= 12; ++prop) {
      this.addProperty(
        properties,
        into,
        RollSources.Unique,
        table.getString(item.fileIndex, 'prop' + String(prop)),
        table.getString(item.fileIndex, 'par' + String(prop)),
        table.getInt(item.fileIndex, 'min' + String(prop)),
        table.getInt(item.fileIndex, 'max' + String(prop)),
      );
    }
  }

  private gatherSetItem(item: ItemIdentity, properties: PropertiesTable, into: Sourced[]): void {
    if (item.quality !== QualitySet) {
      return;
    }

    const table = this.data.setItems;
    if (table === null || item.fileIndex < 0 || item.fileIndex >= table.rowCount) {
      return;
    }

    // sub_1402866E0 case 4: a classic-format item takes props 1..2 and never reaches the aprop loop
    // (0x140286905, 0x140286911).
    const classic = this.data.isResurrected && item.format === 0;
    const props = classic ? 2 : 9;
    for (let prop = 1; prop <= props; ++prop) {
      this.addProperty(
        properties,
        into,
        RollSources.SetItem,
        table.getString(item.fileIndex, 'prop' + String(prop)),
        table.getString(item.fileIndex, 'par' + String(prop)),
        table.getInt(item.fileIndex, 'min' + String(prop)),
        table.getInt(item.fileIndex, 'max' + String(prop)),
      );
    }

    if (classic) {
      return;
    }

    // aprop<n>a/b are the piece's OWN extra mods, granted as more of the set is worn. They are the
    // item's mods rather than the set's, which is why they live in SetItems.txt. With `add func` set
    // they land in tier lists, not the item's own stats (see Sourced.maximiseOnly); with it blank
    // they are ordinary own mods (0x140286a22).
    const tiered = this.data.isResurrected && table.getInt(item.fileIndex, 'add func') !== 0;
    for (let prop = 1; prop <= 5; ++prop) {
      for (const half of ['a', 'b']) {
        const before = into.length;
        this.addProperty(
          properties,
          into,
          RollSources.SetItem,
          table.getString(item.fileIndex, 'aprop' + String(prop) + half),
          table.getString(item.fileIndex, 'apar' + String(prop) + half),
          table.getInt(item.fileIndex, 'amin' + String(prop) + half),
          table.getInt(item.fileIndex, 'amax' + String(prop) + half),
        );

        for (let at = before; tiered && at < into.length; ++at) {
          into[at] = { ...elementAt(into, at), maximiseOnly: true, tierState: 164 + prop };
        }
      }
    }
  }

  private gatherSetBonuses(earnedSetIds: readonly number[] | null, into: Sourced[]): void {
    for (const setId of earnedSetIds ?? []) {
      for (const property of this.sets.partialProperties(setId)) {
        into.push({ property, source: RollSources.SetBonus });
      }

      for (const property of this.sets.fullProperties(setId)) {
        into.push({ property, source: RollSources.SetBonus });
      }
    }
  }

  /**
   * A runeword's granted properties live in runes.txt, found by the string id the record carries in
   * magicPrefix[0] — TXT_AllocTxt_runes 0x639c63 resolved the row's `Name` to that id at
   * table-compile time, so matching it back is exact.
   */
  private gatherRuneword(item: ItemIdentity, properties: PropertiesTable, into: Sourced[]): void {
    const runes = this.data.runes;
    if (runes === null) {
      return;
    }

    const nameId = item.magicPrefix[0] ?? 0;
    let found = -1;

    for (let row = 0; row < runes.rowCount && found < 0; ++row) {
      const key = runes.getString(row, 'Name').trim();
      if (key.length !== 0 && this.data.strings.resolveKey(key) === nameId) {
        found = row;
      }
    }

    if (found < 0) {
      return;
    }

    for (let prop = 1; prop <= 7; ++prop) {
      this.addProperty(
        properties,
        into,
        RollSources.Runeword,
        runes.getString(found, 'T1Code' + String(prop)),
        runes.getString(found, 'T1Param' + String(prop)),
        runes.getInt(found, 'T1Min' + String(prop)),
        runes.getInt(found, 'T1Max' + String(prop)),
      );
    }
  }

  /**
   * A superior item's modifier comes from qualityitems.txt. On D2R the record's file index IS the
   * row that rolled; on 1.14d that is untraced, so every row whose type gate admits this item is a candidate. That would be ambiguous
   * except that in shipped data each mod code carries the SAME range in every row it appears in
   * (`att` 1..3, `dmg%` and `ac%` 5..15, `dur%` 10..15), so the union over candidates is one span
   * per stat either way. A test asserts that.
   */
  private gatherSuperior(item: ItemIdentity, properties: PropertiesTable, into: Sourced[]): void {
    const table = this.data.qualityItems;
    if (item.quality !== QualityHighQuality || table === null) {
      return;
    }

    const seen = new Set<string>();

    // D2R: LOD114d_sub_5C2970 draws ONE row, stores it as the file index (ITEMS_SetFileIndex
    // 0x140381dc3) and applies only its mods (0x140381dd2-0x140381e23).
    const drawn =
      this.data.isResurrected && item.fileIndex >= 0 && item.fileIndex < table.rowCount
        ? item.fileIndex
        : -1;

    for (let row = 0; row < table.rowCount; ++row) {
      if (drawn >= 0 ? row !== drawn : !this.superiorRowApplies(item, table, row)) {
        continue;
      }

      for (let mod = 1; mod <= 2; ++mod) {
        const code = table.getString(row, 'mod' + String(mod) + 'code').trim();
        if (code.length === 0 || seen.has(code)) {
          continue;
        }

        seen.add(code);

        this.addProperty(
          properties,
          into,
          RollSources.Superior,
          code,
          table.getString(row, 'mod' + String(mod) + 'param'),
          table.getInt(row, 'mod' + String(mod) + 'min'),
          table.getInt(row, 'mod' + String(mod) + 'max'),
        );
      }
    }
  }

  /**
   * qualityitems.txt gates each row by item shape with one column per family. They are read against
   * the item's own type tree rather than its code, so a base inherits the gate the same way the
   * game's type checks do.
   */
  private superiorRowApplies(item: ItemIdentity, table: TxtFile, row: number): boolean {
    for (const [column, typeCode] of SuperiorGates) {
      if (table.getInt(row, column) === 0) {
        continue;
      }

      if (this.isOfType(item, typeCode)) {
        return true;
      }
    }

    return false;
  }

  /**
   * A unique or set item spawns only on its row's base, so a different class means the cube
   * replaced it (`v69->nClassId = v67` in PLRTRADE_CreateCubeOutputs 0x1403bfaa0).
   */
  private upgradedByCube(item: ItemIdentity): boolean {
    let table: TxtFile | null;
    let column: string;
    if (item.quality === QualityUnique) {
      table = this.data.uniqueItems;
      column = 'code';
    } else if (item.quality === QualitySet) {
      table = this.data.setItems;
      column = 'item';
    } else {
      return false;
    }

    if (table === null || item.fileIndex < 0 || item.fileIndex >= table.rowCount) {
      return false;
    }

    const rowClass = this.items.classIdForCode(table.getString(item.fileIndex, column).trim());
    return rowClass >= 0 && rowClass !== item.classId;
  }

  /** A base an upgrade can produce: one whose `normcode` is another item. */
  private cubeUpgradeTarget(classId: number): boolean {
    const normal = this.items.getString(classId, 'normcode').trim();
    return (
      normal.length !== 0 && normal.toLowerCase() !== this.items.code(classId).trim().toLowerCase()
    );
  }

  private isOfType(item: ItemIdentity, typeCode: string): boolean {
    return this.types.isOfType(
      this.types.row(this.items.primaryTypeCode(item.classId)),
      this.types.row(this.items.secondaryTypeCode(item.classId)),
      this.types.row(typeCode),
    );
  }

  private addProperty(
    properties: PropertiesTable,
    into: Sourced[],
    source: RollSources,
    code: string,
    param: string,
    min: number,
    max: number,
  ): void {
    const trimmed = code.trim();

    // Eleven enabled uniques carry a commented-out `*`-prefixed code. The game's table compiler
    // never resolves those, so they are skipped rather than reported missing.
    if (trimmed.length === 0 || trimmed.startsWith('*')) {
      return;
    }

    const link = PropertyGroupsTable.link(trimmed, properties, this.groups);
    if (link.row < 0) {
      return;
    }

    const unused: ItemProperty = { propertyId: -1, param: 0, min: 0, max: 0 };

    if (link.kind === PropertyRefKind.Group) {
      // ITEMMODS_AssignProperty 0x14028a67d: the outer min/max is the group's count and its param
      // is unused.
      into.push({
        property: unused,
        source,
        seed: { groupRow: link.row, min, max, statPick: null },
      });
      return;
    }

    const property: ItemProperty = {
      propertyId: link.row,
      param: this.data.paramLinker.resolve(param),
      min,
      max,
    };

    // D2R's func 25 picks its stat off the seed; 1.14d's slot 25 is null and stays with the
    // applier, which reports it. So does a func 25 that would write nothing.
    if (
      this.data.isResurrected &&
      RolledRangeReconstructor.hasFunc(properties, link.row, StatPickFunc) &&
      this.canBuildStatPick(properties, property.param)
    ) {
      into.push({
        property: unused,
        source,
        seed: { groupRow: -1, min: 0, max: 0, statPick: property },
      });
      return;
    }

    into.push({ property, source });
  }

  private static hasFunc(properties: PropertiesTable, propertyId: number, func: number): boolean {
    const row = properties.getRow(propertyId);
    return row !== null && row.func.includes(func);
  }

  // 0x140289e71: `0 < stat < ItemStatCostCount`, so strength (0) is never a candidate.
  private isStatPickCandidate(stat: number): boolean {
    return stat > 0 && this.data.itemStatCost.tryGetStat(stat) !== null;
  }

  /**
   * Whether func 25 would write anything: a param inside Properties.txt (0x140289dc5..0x140289e0c)
   * whose row carries a valid stat.
   */
  private canBuildStatPick(properties: PropertiesTable, param: number): boolean {
    const target = properties.getRow(param);
    return target !== null && target.stat.some(stat => this.isStatPickCandidate(stat));
  }

  /**
   * A group cell becomes one node per sub_14028A190 0x14028a190 dispatch; its outcomes are the
   * subsets the pick mode can apply, times each applied entry's independently rolled param
   * (0x14028a34b..0x14028a3c7). A func-25 property becomes a one-of-K node over its target row's
   * stats (0x140289e71).
   */
  private buildChoice(
    low: PropertyApplier,
    high: PropertyApplier,
    item: ItemIdentity,
    seed: ChoiceSeed,
    source: RollSources,
  ): ChoiceNode {
    const sources = source | RollSources.PropertyGroup;
    return seed.statPick !== null
      ? this.buildStatPick(low, high, seed.statPick, sources)
      : this.buildGroup(low, high, item, seed.groupRow, seed.min, seed.max, sources);
  }

  private buildGroup(
    low: PropertyApplier,
    high: PropertyApplier,
    item: ItemIdentity,
    groupRow: number,
    min: number,
    max: number,
    sources: RollSources,
  ): ChoiceNode {
    const group = this.groups.getRow(groupRow);
    if (group === null) {
      throw new RangeError('group row ' + String(groupRow));
    }

    const node: ChoiceNode = {
      public: {
        groupRow,
        code: group.code,
        sources,
        pickMode: group.pickMode,
        countLow: 0,
        countHigh: 0,
        options: [],
        resolution: ChoiceResolution.NoRecord,
        consistent: [],
      },
      sources,
      unsupported: false,
      options: [],
      outcomes: [],
      keys: new Set<number>(),
    };

    const options: RolledChoiceOption[] = [];

    for (let k = 0; k < PropertyGroupsTable.EntriesPerGroup; ++k) {
      const entry = elementAt(group.entries, k);

      // 0x14028a038: any entry whose row is non-negative joins the list, nested included.
      if (entry.prop.row < 0) {
        continue;
      }

      const model: OptionModel = {
        kind: OptionProperty,
        propertyId: entry.prop.row,
        modMin: entry.modMin,
        modMax: entry.modMax,
        statId: 0,
        statSet: 0,
        statProperty: null,
        nested: null,
        paramLow: Math.min(entry.parMin, entry.parMax),
        paramHigh: Math.max(entry.parMin, entry.parMax),
      };

      const variants: RolledChoiceVariant[] = [];
      let nested: RolledChoice | null = null;
      let code: string;

      if (entry.prop.kind === PropertyRefKind.Group) {
        // A nested entry's param is rolled but unused, and its modMin..modMax is the nested count;
        // one param stands for all of them.
        model.kind = OptionNested;
        model.propertyId = -1;
        model.paramHigh = model.paramLow;
        model.nested = this.buildGroup(
          low,
          high,
          item,
          entry.prop.row,
          entry.modMin,
          entry.modMax,
          sources,
        );
        if (model.nested.unsupported) {
          node.unsupported = true;
        }
        nested = model.nested.public;
        code = this.groups.getRow(entry.prop.row)?.code ?? '';
      } else {
        const row = low.properties.getRow(entry.prop.row);
        code = row === null ? '' : row.code;

        if (
          RolledRangeReconstructor.rollsTheLayer(low.properties, model.propertyId) ||
          RolledRangeReconstructor.hasFunc(low.properties, model.propertyId, StatPickFunc) ||
          model.paramHigh - model.paramLow + 1 > MaxParamSpan
        ) {
          node.unsupported = true;
        } else {
          for (let param = model.paramLow; param <= model.paramHigh; ++param) {
            const property: ItemProperty = {
              propertyId: model.propertyId,
              param,
              min: model.modMin,
              max: model.modMax,
            };

            const atLow = new Map<number, number>();
            const atHigh = new Map<number, number>();
            low.apply(PropertyApplier.PropModeGem, item, property, atLow);
            high.apply(PropertyApplier.PropModeGem, item, property, atHigh);
            variants.push({ param, stats: this.variantRanges(atLow, atHigh, sources, node.keys) });
          }
        }
      }

      node.options.push(model);
      options.push({
        entry: k + 1,
        propertyId: model.propertyId,
        code,
        weight: entry.weight,
        paramLow: model.paramLow,
        paramHigh: model.paramHigh,
        min: entry.modMin,
        max: entry.modMax,
        variants,
        nested,
      });
    }

    let countLow: number;
    let countHigh: number;
    switch (group.pickMode) {
      case 0:
        countLow = countHigh = node.options.length;
        break;
      case 1:
      case 2:
        // 0x14028a738..0x14028a753: equal ends floor at 1, otherwise the seed rolls N over the
        // normalised pair.
        if (min === max) {
          countLow = countHigh = Math.max(min, 1);
        } else {
          countLow = Math.min(min, max);
          countHigh = Math.max(min, max);
        }
        break;
      default:
        countLow = countHigh = 0;
        break;
    }

    node.public.countLow = countLow;
    node.public.countHigh = countHigh;
    node.public.options = options;

    if (!node.unsupported) {
      const outcomes = RolledRangeReconstructor.groupOutcomes(
        node,
        group.pickMode,
        countLow,
        countHigh,
      );
      if (outcomes === null) {
        node.unsupported = true;
      } else {
        node.outcomes = outcomes;
      }
    }

    if (node.unsupported) {
      node.outcomes = [[]];
      node.keys.clear();
    }

    return node;
  }

  /**
   * Which entry subsets a pick mode can apply. pickmode 1 removes each pick (0x14028a8e7), so N
   * picks give a subset of min(N, K); a negative N passes the unsigned loop test (0x14028a8ff) and
   * drains the list. pickmode 2 keeps the drawn slot (0x14028ab19), so N draws give any non-empty
   * subset of up to min(N, K). Null when the count exceeds the cap.
   */
  private static groupOutcomes(
    node: ChoiceNode,
    pickMode: number,
    countLow: number,
    countHigh: number,
  ): Pick[][] | null {
    const k = node.options.length;
    const sizes = new Array<boolean>(k + 1).fill(false);

    if (k === 0 || pickMode >= 3) {
      sizes[0] = true;
    } else if (pickMode === 0) {
      sizes[k] = true;
    } else {
      if (countLow <= 0 && countHigh >= 0) {
        sizes[0] = true;
      }

      if (pickMode === 1) {
        if (countLow < 0) {
          sizes[k] = true;
        }

        for (let n = Math.max(countLow, 1); n <= countHigh && n <= k; ++n) {
          sizes[n] = true;
        }

        if (countHigh > k) {
          sizes[k] = true;
        }
      } else {
        const top = countLow < 0 ? k : Math.min(countHigh, k);
        for (let n = 1; n <= top; ++n) {
          sizes[n] = true;
        }
      }
    }

    const subsets: number[][] = [];
    for (let size = 0; size <= k; ++size) {
      if (sizes[size]) {
        RolledRangeReconstructor.addCombinations(k, size, subsets);
      }
    }

    let total = 0;
    for (const subset of subsets) {
      let product = 1;
      for (const option of subset) {
        product *= RolledRangeReconstructor.optionOutcomeCount(elementAt(node.options, option));
        if (product > MaxOutcomesPerChoice) {
          return null;
        }
      }

      total += product;
      if (total > MaxOutcomesPerChoice) {
        return null;
      }
    }

    const outcomes: Pick[][] = [];
    for (const subset of subsets) {
      RolledRangeReconstructor.expand(node, subset, 0, [], outcomes);
    }

    return outcomes;
  }

  private static optionOutcomeCount(option: OptionModel): number {
    return option.kind === OptionNested && option.nested !== null
      ? option.nested.outcomes.length
      : option.paramHigh - option.paramLow + 1;
  }

  private static addCombinations(k: number, size: number, into: number[][]): void {
    const current: number[] = [];
    for (let i = 0; i < size; ++i) {
      current.push(i);
    }

    for (;;) {
      into.push([...current]);

      let at = size - 1;
      while (at >= 0 && current[at] === k - size + at) {
        --at;
      }

      if (at < 0) {
        return;
      }

      let next = elementAt(current, at) + 1;
      for (let i = at; i < size; ++i) {
        current[i] = next++;
      }
    }
  }

  private static expand(
    node: ChoiceNode,
    subset: readonly number[],
    index: number,
    prefix: Pick[],
    into: Pick[][],
  ): void {
    if (index === subset.length) {
      into.push([...prefix]);
      return;
    }

    const optionIndex = elementAt(subset, index);
    const option = elementAt(node.options, optionIndex);

    if (option.kind === OptionNested && option.nested !== null) {
      for (const inner of option.nested.outcomes) {
        prefix.push({ option: optionIndex, param: option.paramLow, nested: inner });
        RolledRangeReconstructor.expand(node, subset, index + 1, prefix, into);
        prefix.pop();
      }

      return;
    }

    for (let param = option.paramLow; param <= option.paramHigh; ++param) {
      prefix.push({ option: optionIndex, param, nested: null });
      RolledRangeReconstructor.expand(node, subset, index + 1, prefix, into);
      prefix.pop();
    }
  }

  private buildStatPick(
    low: PropertyApplier,
    high: PropertyApplier,
    property: ItemProperty,
    sources: RollSources,
  ): ChoiceNode {
    const row = low.properties.getRow(property.propertyId);
    const target = low.properties.getRow(property.param);
    if (row === null || target === null) {
      throw new RangeError('stat pick ' + String(property.propertyId));
    }

    const node: ChoiceNode = {
      public: {
        groupRow: -1,
        code: row.code,
        sources,
        pickMode: ChoicePickMode.StatPick,
        countLow: 1,
        countHigh: 1,
        options: [],
        resolution: ChoiceResolution.NoRecord,
        consistent: [],
      },
      sources,
      unsupported: false,
      options: [],
      outcomes: [],
      keys: new Set<number>(),
    };

    let statSet = 0;
    for (let set = 0; set < PropertiesTable.SetsPerProperty; ++set) {
      if (row.func[set] === StatPickFunc) {
        statSet = row.set[set] ?? 0;
        break;
      }
    }

    const options: RolledChoiceOption[] = [];
    for (let set = 0; set < PropertiesTable.SetsPerProperty; ++set) {
      const stat = target.stat[set] ?? -1;
      if (!this.isStatPickCandidate(stat)) {
        continue;
      }

      const atLow = new Map<number, number>();
      const atHigh = new Map<number, number>();
      low.applyStatPick(statSet, stat, property, atLow);
      high.applyStatPick(statSet, stat, property, atHigh);

      node.options.push({
        kind: OptionStat,
        propertyId: property.param,
        modMin: 0,
        modMax: 0,
        statId: stat,
        statSet,
        statProperty: property,
        nested: null,
        paramLow: property.param,
        paramHigh: property.param,
      });
      node.outcomes.push([{ option: options.length, param: property.param, nested: null }]);
      options.push({
        entry: set + 1,
        propertyId: property.param,
        code: target.code,
        weight: 1,
        paramLow: property.param,
        paramHigh: property.param,
        min: property.min,
        max: property.max,
        variants: [
          {
            param: property.param,
            stats: this.variantRanges(atLow, atHigh, sources, node.keys),
          },
        ],
        nested: null,
      });
    }

    node.public.options = options;
    return node;
  }

  private variantRanges(
    atLow: Map<number, number>,
    atHigh: Map<number, number>,
    sources: RollSources,
    keys: Set<number>,
  ): RolledStatRange[] {
    const sourceOf = new Map<number, RollSources>();
    for (const key of [...atLow.keys(), ...atHigh.keys()]) {
      sourceOf.set(key, sources);
      keys.add(key);
    }

    const ranges = RolledRangeReconstructor.collectRanges(
      atLow,
      atHigh,
      sourceOf,
      this.data.itemStatCost,
    );
    ranges.sort((a, b) => a.statId - b.statId || a.layer - b.layer);
    return ranges;
  }

  private static applyPick(
    applier: PropertyApplier,
    item: ItemIdentity,
    node: ChoiceNode,
    pick: Pick,
    into: Map<number, number>,
  ): void {
    const option = elementAt(node.options, pick.option);
    if (option.kind === OptionNested) {
      if (option.nested !== null && pick.nested !== null) {
        for (const inner of pick.nested) {
          RolledRangeReconstructor.applyPick(applier, item, option.nested, inner, into);
        }
      }
    } else if (option.kind === OptionStat) {
      if (option.statProperty !== null) {
        applier.applyStatPick(option.statSet, option.statId, option.statProperty, into);
      }
    } else {
      applier.apply(
        PropertyApplier.PropModeGem,
        item,
        {
          propertyId: option.propertyId,
          param: pick.param,
          min: option.modMin,
          max: option.modMax,
        },
        into,
      );
    }
  }

  private static attributePick(
    low: PropertyApplier,
    high: PropertyApplier,
    item: ItemIdentity,
    node: ChoiceNode,
    pick: Pick,
    sourceOf: Map<number, RollSources>,
  ): void {
    const scratch = new Map<number, number>();
    RolledRangeReconstructor.applyPick(low, item, node, pick, scratch);
    RolledRangeReconstructor.applyPick(high, item, node, pick, scratch);

    for (const key of scratch.keys()) {
      sourceOf.set(key, (sourceOf.get(key) ?? RollSources.None) | node.sources);
    }
  }

  private static pickWritesStat(
    properties: PropertiesTable,
    node: ChoiceNode,
    pick: Pick,
    statId: number,
  ): boolean {
    const option = elementAt(node.options, pick.option);
    if (option.kind === OptionNested) {
      const nested = option.nested;
      return (
        nested !== null &&
        pick.nested !== null &&
        pick.nested.some(inner =>
          RolledRangeReconstructor.pickWritesStat(properties, nested, inner, statId),
        )
      );
    }

    if (option.kind === OptionStat) {
      return option.statId === statId;
    }

    const row = properties.rowAt(option.propertyId);
    return row !== null && row.stat.includes(statId);
  }

  /**
   * The cartesian product of every choice's outcomes, first choice slowest. Past the cap every
   * choice is demoted to unsupported and the item is reconstructed without them.
   */
  private static combos(choices: ChoiceNode[]): number[][] {
    let total = 1;
    for (const node of choices) {
      total *= node.outcomes.length;
      if (total > MaxCombos) {
        break;
      }
    }

    if (total > MaxCombos) {
      for (const node of choices) {
        node.unsupported = true;
        node.outcomes = [[]];
        node.keys.clear();
      }
    }

    const combos: number[][] = [];
    const current = new Array<number>(choices.length).fill(0);

    for (;;) {
      combos.push([...current]);

      let at = choices.length - 1;
      while (at >= 0 && current[at] === elementAt(choices, at).outcomes.length - 1) {
        current[at] = 0;
        --at;
      }

      if (at < 0) {
        return combos;
      }

      current[at] = elementAt(current, at) + 1;
    }
  }

  private static contestedKeys(choices: readonly ChoiceNode[]): Set<number> {
    const contested = new Set<number>();
    for (const node of choices) {
      RolledRangeReconstructor.addKeys(node, contested);
    }

    return contested;
  }

  private static addKeys(node: ChoiceNode, into: Set<number>): void {
    if (node.unsupported) {
      return;
    }

    for (const key of node.keys) {
      into.add(key);
    }

    for (const option of node.options) {
      if (option.nested !== null) {
        RolledRangeReconstructor.addKeys(option.nested, into);
      }
    }
  }

  /**
   * Keeps the combinations consistent with the record over the CONTESTED keys only, so a defect
   * elsewhere in the reconstruction cannot flip a choice. Returns the kept combination indices and
   * fills in each choice's resolution.
   */
  private static resolve(
    choices: ChoiceNode[],
    combos: number[][],
    lows: Map<number, number>[],
    highs: Map<number, number>[],
    contested: Set<number>,
    recorded: Map<number, number> | null,
    outside: Set<number>,
  ): number[] {
    const all = combos.map((_, i) => i);

    if (recorded === null) {
      RolledRangeReconstructor.report(choices, combos, all, ChoiceResolution.NoRecord);
      return all;
    }

    const kept = all.filter(i =>
      RolledRangeReconstructor.consistent(
        elementAt(lows, i),
        elementAt(highs, i),
        contested,
        recorded,
      ),
    );

    if (kept.length === 0) {
      RolledRangeReconstructor.report(choices, combos, all, ChoiceResolution.Contradicted);
      for (const key of contested) {
        if (recorded.has(key)) {
          outside.add(ItemStatReader.statFromKey(key));
        }
      }

      return all;
    }

    RolledRangeReconstructor.report(choices, combos, kept, ChoiceResolution.Resolved);
    return kept;
  }

  private static consistent(
    lowX: Map<number, number>,
    highX: Map<number, number>,
    contested: Set<number>,
    recorded: Map<number, number>,
  ): boolean {
    for (const key of contested) {
      const written = lowX.has(key) || highX.has(key);
      const a = lowX.get(key) ?? 0;
      const b = highX.get(key) ?? 0;
      const lo = Math.min(a, b);
      const hi = Math.max(a, b);

      const value = recorded.get(key);
      if (value !== undefined) {
        if (!written || value < lo || value > hi) {
          return false;
        }
      } else if (written && (lo > 0 || hi < 0)) {
        return false;
      }
    }

    return true;
  }

  /**
   * `resolution` Resolved means "decide per choice": one distinct outcome is Resolved, several
   * Ambiguous.
   */
  private static report(
    choices: readonly ChoiceNode[],
    combos: number[][],
    kept: readonly number[],
    resolution: ChoiceResolution,
  ): void {
    for (let c = 0; c < choices.length; ++c) {
      const node = elementAt(choices, c);
      if (node.unsupported) {
        RolledRangeReconstructor.markUnsupported(node);
        continue;
      }

      const indices = [...new Set<number>(kept.map(i => elementAt(elementAt(combos, i), c)))].sort(
        (a, b) => a - b,
      );

      const seen = new Set<string>();
      const consistent: RolledChoicePick[][] = [];
      for (const index of indices) {
        const outcome = elementAt(node.outcomes, index);
        const key = RolledRangeReconstructor.outcomeKey(outcome);
        if (!seen.has(key)) {
          seen.add(key);
          consistent.push(RolledRangeReconstructor.publicPicks(outcome));
        }
      }

      node.public.consistent = consistent;
      node.public.resolution =
        resolution !== ChoiceResolution.Resolved
          ? resolution
          : consistent.length === 1
            ? ChoiceResolution.Resolved
            : ChoiceResolution.Ambiguous;

      for (const option of node.options) {
        if (option.nested !== null) {
          RolledRangeReconstructor.reportNested(option.nested);
        }
      }
    }
  }

  private static reportNested(node: ChoiceNode): void {
    if (node.unsupported) {
      RolledRangeReconstructor.markUnsupported(node);
      return;
    }

    const seen = new Set<string>();
    const consistent: RolledChoicePick[][] = [];
    for (const outcome of node.outcomes) {
      const key = RolledRangeReconstructor.outcomeKey(outcome);
      if (!seen.has(key)) {
        seen.add(key);
        consistent.push(RolledRangeReconstructor.publicPicks(outcome));
      }
    }

    node.public.consistent = consistent;
    node.public.resolution = ChoiceResolution.NoRecord;

    for (const option of node.options) {
      if (option.nested !== null) {
        RolledRangeReconstructor.reportNested(option.nested);
      }
    }
  }

  private static markUnsupported(node: ChoiceNode): void {
    node.public.resolution = ChoiceResolution.Unsupported;
    node.public.consistent = [];

    for (const option of node.options) {
      if (option.nested !== null) {
        RolledRangeReconstructor.markUnsupported(option.nested);
      }
    }
  }

  private static outcomeKey(outcome: readonly Pick[]): string {
    return outcome.map(pick => String(pick.option) + ':' + String(pick.param)).join(',');
  }

  private static publicPicks(outcome: readonly Pick[]): RolledChoicePick[] {
    return outcome.map(pick => ({ option: pick.option, param: pick.param }));
  }

  /**
   * A key gets a span only if EVERY kept combination writes it; the span is the union of each
   * combination's own normalised span, and the sources the union of theirs.
   */
  private collectKept(
    kept: readonly number[],
    lows: Map<number, number>[],
    highs: Map<number, number>[],
    sources: Map<number, RollSources>[],
  ): RolledStatRange[] {
    if (kept.length === 1) {
      const only = elementAt(kept, 0);
      return RolledRangeReconstructor.collectRanges(
        elementAt(lows, only),
        elementAt(highs, only),
        elementAt(sources, only),
        this.data.itemStatCost,
      );
    }

    const keys = new Set<number>();
    for (const i of kept) {
      for (const key of elementAt(lows, i).keys()) {
        keys.add(key);
      }

      for (const key of elementAt(highs, i).keys()) {
        keys.add(key);
      }
    }

    const lowOut = new Map<number, number>();
    const highOut = new Map<number, number>();
    const sourceOut = new Map<number, RollSources>();

    for (const key of keys) {
      let everywhere = true;
      let low = Number.POSITIVE_INFINITY;
      let high = Number.NEGATIVE_INFINITY;
      let from = RollSources.None;

      for (const i of kept) {
        if (!elementAt(lows, i).has(key) && !elementAt(highs, i).has(key)) {
          everywhere = false;
          break;
        }

        const a = elementAt(lows, i).get(key) ?? 0;
        const b = elementAt(highs, i).get(key) ?? 0;
        low = Math.min(low, a, b);
        high = Math.max(high, a, b);
        from |= elementAt(sources, i).get(key) ?? RollSources.None;
      }

      if (everywhere) {
        lowOut.set(key, low);
        highOut.set(key, high);
        sourceOut.set(key, from);
      }
    }

    return RolledRangeReconstructor.collectRanges(
      lowOut,
      highOut,
      sourceOut,
      this.data.itemStatCost,
    );
  }

  private static outOfRange(
    stats: readonly RolledStatRange[],
    recorded: Map<number, number> | null,
  ): number[] {
    if (recorded === null) {
      return [];
    }

    const outside = new Set<number>();

    for (const range of stats) {
      const value = recorded.get(ItemStatReader.packStatKey(range.layer, range.statId));
      if (value === undefined) {
        continue;
      }

      if (value < range.low || value > range.high) {
        outside.add(range.statId);
      }
    }

    return [...outside].sort((a, b) => a - b);
  }

  private static unattributed(
    kept: readonly number[],
    lows: Map<number, number>[],
    highs: Map<number, number>[],
    layerVaries: readonly RolledLayerRange[],
    recorded: Map<number, number> | null,
  ): number[] {
    if (recorded === null) {
      return [];
    }

    const missing = new Set<number>();

    for (const [key, value] of recorded) {
      const written = kept.some(i => elementAt(lows, i).has(key) || elementAt(highs, i).has(key));
      if (!written && !RolledRangeReconstructor.explainedByLayerRoll(key, value, layerVaries)) {
        missing.add(ItemStatReader.statFromKey(key));
      }
    }

    return [...missing].sort((a, b) => a - b);
  }

  /**
   * A func-12/36 stat never enters the combined totals (`addLayerRange`), so a recorded one is
   * explained by a layer range of the same stat that spans its layer and carries its value.
   */
  private static explainedByLayerRoll(
    key: number,
    value: number,
    layerVaries: readonly RolledLayerRange[],
  ): boolean {
    const statId = ItemStatReader.statFromKey(key);
    const layer = ItemStatReader.layerFromKey(key);

    return layerVaries.some(
      range =>
        range.statId === statId &&
        range.layerLow <= layer &&
        layer <= range.layerHigh &&
        range.value === value,
    );
  }

  private static merge(a: ReadonlySet<number>, b: ReadonlySet<number>): number[] {
    return [...new Set<number>([...a, ...b])].sort((x, y) => x - y);
  }
}
