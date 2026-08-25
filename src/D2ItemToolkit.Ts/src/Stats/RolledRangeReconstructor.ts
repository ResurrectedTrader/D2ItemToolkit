import { ItemRecordFlags, type ItemIdentity } from './ItemRecord.js';
import { ItemStatReader } from './ItemStatReader.js';
import { ItemStatOps } from './ItemStatOps.js';
import { Int32 } from '../Types.js';
import { PropertyApplier, RollEnd, type ItemProperty } from './PropertyApplier.js';
import type { ItemTable } from '../Tables/ItemTable.js';
import type { ItemTypeTree } from '../Tables/ItemTypeTree.js';
import type { MagicAffixTable } from '../Tables/MagicAffixTable.js';
import type { PropertiesTable } from '../Tables/PropertiesTable.js';
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

/** One gathered property and the source that contributed it. */
interface Sourced {
  readonly property: ItemProperty;
  readonly source: RollSources;
}

const StatDefense = 31;

// Quality numbers, matching ItemQuality in the tooltip layer.
const QualityHighQuality = 3;
const QualitySet = 5;
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
  constructor(
    private readonly data: D2DataFiles,
    private readonly items: ItemTable,
    private readonly types: ItemTypeTree,
    private readonly affixes: MagicAffixTable,
    private readonly sets: SetTable,
  ) {}

  /**
   * `includeBaseDefense` false drops the armour's own `minac`..`maxac` roll, leaving the item's
   * MODIFIERS alone. The Defense SECTION draws the base plus every modifier and wants it; a
   * `+45 Defense` modifier line draws its own contribution and does not â with it, that line was
   * offered the section's span.
   *
   * `includeOwnSources` false applies ONLY `socketProperties` — no affixes, no unique row, nothing
   * of the item's own. That is what a socket-only view needs: asking for "just the fillers" while
   * the identity's own sources were folded in silently gave a gem's line the HOST's affix span.
   */
  reconstruct(
    item: ItemIdentity,
    recorded: Map<number, number> | null,
    socketProperties: readonly ItemProperty[] | null,
    earnedSetIds: readonly number[] | null,
    includeOwnSources = true,
    includeBaseDefense = true,
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

    // The BASE view at each end, kept apart from the merged one because op 13 consumes the two
    // separately (STATLIST_LookupBaseStatWithMinAccr 0x624ed0 reads `Stats`, the result lands in
    // FullStats at 0x625158). Only Defense rolls a base, so only Defense is in here.
    const lowBase = new Map<number, number>();
    const highBase = new Map<number, number>();

    const sourceOf = new Map<number, RollSources>();
    const layerVaries: RolledLayerRange[] = [];

    for (const entry of gathered) {
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

    // Gated with the rest of the item's own sources: the base armour roll IS one, so a socket-only
    // reconstruction that added it gave a gem block the HOST's base span — "+30 Defense [33-35]"
    // where 33-35 was the cap's 3..5 plus the rune's fixed 30.
    if (includeOwnSources && includeBaseDefense) {
      this.addBaseDefense(
        item,
        lowStats,
        highStats,
        lowBase,
        highBase,
        sourceOf,
        RolledRangeReconstructor.maximisesBaseDefense(gathered, low.properties),
      );
    }

    // The Defense line draws the OP-RESOLVED value, so its span has to be resolved too. A Large
    // Shield rolling 12..14 under +150% Enhanced Defense prints 32 — a number that can never fall
    // inside the 12..14 the base rolled within, which is what the span used to offer.
    this.resolveBaseOps(lowStats, lowBase);
    this.resolveBaseOps(highStats, highBase);

    const stats = RolledRangeReconstructor.collectRanges(
      lowStats,
      highStats,
      sourceOf,
      this.data.itemStatCost,
    );

    stats.sort((a, b) => a.statId - b.statId || a.layer - b.layer);
    layerVaries.sort((a, b) => a.statId - b.statId || a.layerLow - b.layerLow);

    return {
      stats,
      layerVaries,
      outOfRange: RolledRangeReconstructor.outOfRange(stats, recorded),
      unattributed: RolledRangeReconstructor.unattributed(lowStats, highStats, recorded),
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
    // crafted.
    return this.gather(item, applier.properties, null).map(entry => entry.property);
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
      RolledRangeReconstructor.addProperty(
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
  ): boolean {
    for (const entry of gathered) {
      const row = properties.rowAt(entry.property.propertyId);
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
    if (maximised) {
      // The store is ABSOLUTE and reads the RAW items.txt maxac, so it overwrites whatever the
      // ethereal bonus did rather than scaling with it. The ordering against
      // ITEMMOD_ApplyEtherealBonus is untraced and no captured ethereal armour carries `ac%`, so
      // the literal reading is what is modelled.
      const fixedBase = maxac + 1;
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
      RolledRangeReconstructor.addProperty(
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
      RolledRangeReconstructor.addProperty(
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

    for (let prop = 1; prop <= 9; ++prop) {
      RolledRangeReconstructor.addProperty(
        properties,
        into,
        RollSources.SetItem,
        table.getString(item.fileIndex, 'prop' + String(prop)),
        table.getString(item.fileIndex, 'par' + String(prop)),
        table.getInt(item.fileIndex, 'min' + String(prop)),
        table.getInt(item.fileIndex, 'max' + String(prop)),
      );
    }

    // aprop<n>a/b are the piece's OWN extra mods, granted as more of the set is worn. They are the
    // item's mods rather than the set's, which is why they live in SetItems.txt.
    for (let prop = 1; prop <= 5; ++prop) {
      for (const half of ['a', 'b']) {
        RolledRangeReconstructor.addProperty(
          properties,
          into,
          RollSources.SetItem,
          table.getString(item.fileIndex, 'aprop' + String(prop) + half),
          table.getString(item.fileIndex, 'apar' + String(prop) + half),
          table.getInt(item.fileIndex, 'amin' + String(prop) + half),
          table.getInt(item.fileIndex, 'amax' + String(prop) + half),
        );
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
      RolledRangeReconstructor.addProperty(
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
   * A superior item's modifier comes from qualityitems.txt, but the record does not say WHICH row
   * rolled — so every row whose type gate admits this item is a candidate. That would be ambiguous
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

    for (let row = 0; row < table.rowCount; ++row) {
      if (!this.superiorRowApplies(item, table, row)) {
        continue;
      }

      for (let mod = 1; mod <= 2; ++mod) {
        const code = table.getString(row, 'mod' + String(mod) + 'code').trim();
        if (code.length === 0 || seen.has(code)) {
          continue;
        }

        seen.add(code);

        RolledRangeReconstructor.addProperty(
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

  private isOfType(item: ItemIdentity, typeCode: string): boolean {
    return this.types.isOfType(
      this.types.row(this.items.primaryTypeCode(item.classId)),
      this.types.row(this.items.secondaryTypeCode(item.classId)),
      this.types.row(typeCode),
    );
  }

  private static addProperty(
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

    const id = properties.rowForCode(trimmed);
    if (id < 0) {
      return;
    }

    into.push({
      property: {
        propertyId: id,
        param: RolledRangeReconstructor.parseParam(param),
        min,
        max,
      },
      source,
    });
  }

  /**
   * A param cell is usually a number but sometimes a skill or class NAME — `charged` carries
   * "Hydra". The tables the game compiles resolve those to ids; this port has no general resolver,
   * so a non-numeric param yields 0 and the property still reports its range.
   *
   * Deliberately C# `int.TryParse` and not `parseInt`: whole-cell, and 0 for anything out of Int32.
   * The game's own parser for this cell is NOT traced, so the two implementations are aligned for
   * consistency rather than because strictness is known to be right — but the C# is what generates
   * the differential's expected output, and no shipped cell tells them apart, so a divergence here
   * could only ever surface on a modded table and never in the corpus.
   */
  private static parseParam(param: string): number {
    const trimmed = param.trim();
    if (!/^[+-]?\d+$/.test(trimmed)) {
      return 0;
    }

    const value = Number(trimmed);
    return value >= -2147483648 && value <= 2147483647 ? value : 0;
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
    lowStats: Map<number, number>,
    highStats: Map<number, number>,
    recorded: Map<number, number> | null,
  ): number[] {
    if (recorded === null) {
      return [];
    }

    const missing = new Set<number>();

    for (const key of recorded.keys()) {
      if (!lowStats.has(key) && !highStats.has(key)) {
        missing.add(ItemStatReader.statFromKey(key));
      }
    }

    return [...missing].sort((a, b) => a - b);
  }

  private static merge(a: ReadonlySet<number>, b: ReadonlySet<number>): number[] {
    return [...new Set<number>([...a, ...b])].sort((x, y) => x - y);
  }
}
