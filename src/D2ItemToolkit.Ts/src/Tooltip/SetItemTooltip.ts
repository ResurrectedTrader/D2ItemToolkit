import type { ItemIdentity, ItemViewer } from '../Stats/ItemRecord.js';
import { ItemStatListFlags, ItemStatListStates, ItemStatReader } from '../Stats/ItemStatReader.js';
import { PropertyApplier } from '../Stats/PropertyApplier.js';
import { SynthesisedStatValues } from '../Stats/SynthesisedStatValues.js';
import type { Unit } from '../Stats/Unit.js';
import type { ItemTable } from '../Tables/ItemTable.js';
import type { ItemTypeTree } from '../Tables/ItemTypeTree.js';
import type { SetItemRecord, SetRecord, SetTable } from '../Tables/SetTable.js';
import type { D2DataFiles } from '../Tables/TxtDataProviders.js';
import { ArgumentNullException, DescStringIds } from '../Types.js';
import { ItemDescriptionGenerator } from '../Description/ItemDescription.js';
import { ItemNameBuilder } from './ItemNameBuilder.js';

/**
 * The parts of an identified set item's tooltip that the item's own record cannot supply.
 *
 * Everything else — the piece names, their order, the count, the set name, `add func`, this
 * piece's slot and the partial-bonus stats — is derived from setitems.txt and the record, and is
 * deliberately NOT settable here.
 *
 * Both masks and the owned set carry the same fact from two different game functions, and the
 * writer really does call both: GetSetItem 0x486770 for each piece's colour and
 * ITEMS_GetEquippedSetItemsMask 0x62a370 for the tier arithmetic. They do not agree in general —
 * GetSetItem accepts inventory pages 0/3/4/0xFF and grid types 1/3/4 (0x4867b3), the mask accepts
 * grid type 3 alone (0x62a3f0) — so a set piece in the backpack, or on weapon swap, colours green
 * while contributing no bit.
 */
export interface SetItemTooltipInput {
  /**
   * The `setitems.txt` row indices GetSetItem 0x486770 would return non-null for, i.e. the pieces
   * of this set the viewer is carrying somewhere it counts. A piece not listed here is painted
   * red (0x48d902).
   *
   * Grid types 1, 3 AND 4 all qualify (0x4867d4), so a piece on WEAPON SWAP belongs in this list
   * even though it contributes no mask bit — see the masks below.
   */
  ownedSetItemIds?: Iterable<number> | null;

  /**
   * ITEMS_GetEquippedSetItemsMask(viewer, item, 1) — bit `slot` per WORN sibling, the hovered item
   * included. Feeds `add func == 2` (0x4e65b2) and, through its popcount, the derived set-bonus
   * block.
   *
   * EXCLUDE BODY LOCATIONS 11 AND 12. INVENTORY_PlaceItemInGrid 0x63afd0 stamps the grid type from
   * the body slot at 0x63b1e2 — `cmp [ebp+arg_0], 0Bh / setnl cl / add cl, 3 / mov [edx+0Dh], cl`,
   * i.e. `(bodyLoc >= 11) ? 4 : 3` written to pItemData+105 — and 11/12 are the swap pair
   * (ITEMMODE_GetAlternateBodyLoc 0x55f240). The mask requires grid type 3 alone (0x62a3f0), so a
   * set piece on weapon swap lights NO bit and does not raise the piece count. A caller that
   * counted it would light one tier too many.
   */
  wornMaskIncludingSelf?: number;

  /**
   * ITEMS_GetEquippedSetItemsMask(viewer, item, 0) — the same mask with the hovered item excluded.
   * Feeds `add func == 1` (0x4e6618). The weapon-swap exclusion above applies here too.
   */
  wornMaskExcludingSelf?: number;

  /**
   * dwAnimMode == 1, tested at 0x48d870. False suppresses the full-set block outright, and the
   * game suppresses it for the same reason.
   */
  isEquipped?: boolean;

  /**
   * OPTIONAL OVERRIDE, and the FIRST of three sources. Leave unset and the block comes from the
   * VIEWER's own record, which is where the game reads it: SKILLDESC_AppendItemBuffTextAlt
   * 0x4e6680 walks GetStatsByState(wearer, STATE_ITEMSET k) for k 0..5 (0x4e66c9) and takes the
   * list whose stat 71 equals this set's id (0x4e66d7).
   *
   * Failing that it is DERIVED from sets.txt plus the equipped-piece count, by replaying
   * ITEMMOD_ApplySetBonuses 0x660120 — the function that filled the wearer's list to begin with.
   * That is exact for 217 of the 220 shipped property slots; the three genuine ranges (Vidala's
   * Rig FMin1/FMax1 15..20, Cathan's Traps PMin2a/PMax2a 15..20, Cow King's Leathers FMin5/FMax5
   * 25/5, inverted) resolve to the low end, as every other seed-dependent range in this port does.
   *
   * Set this only to override both.
   */
  fullSetStats?: Iterable<readonly [number, number]> | null;
}

/** One row of the piece list, in setitems.txt order. */
export interface SetPieceLine {
  text: string;

  /** GetSetItem returned non-null: green (0x48d8fb) rather than red (0x48d902). */
  owned: boolean;
}

/**
 * The four set-specific buffers of ITEM_BuildSetItemTooltip, already built. The composer only
 * orders and colours them; deriving them is {@link SetItemTooltipBuilder}'s job.
 */
export interface SetItemTooltipContent {
  /** var_4790, built at 0x48d88e-0x48d92a. */
  pieces: readonly SetPieceLine[];

  /** var_1538 — `str(sets[+0x02]) + str(3998)`, built at 0x48d3b5-0x48d3d0. */
  setName: string;

  /** var_3390, SKILLDESC_AppendItemBuffTextAlt 0x4e6680. */
  fullSetText: string;

  /** var_2F90, SKILLDESC_AppendItemBuffText 0x4e6560. */
  partialText: string;

  /**
   * var_138 — locale 3333 plus its terminator, written at 0x48dab1-0x48dac3 when
   * NPCMENU_CalculateItemTransactionCost refuses and ShopMode is not 4.
   */
  transactionRefusedText: string;
}

/** dword_6DBD70 = { 165, 166, 167, 168, 169, 170 }, read directly. */
export const ItemSetStates: readonly number[] = [165, 166, 167, 168, 169, 170];

/**
 * dword_6DBD90, a 64-entry popcount table indexed by the mask. 0x4e65ba refuses anything at 0x40
 * or above and substitutes a count of zero rather than reading past the table.
 * ITEMMOD_ApplySetBonuses reads the same table under a second name, dword_6EDA40, behind the
 * identical guard (`cmp eax, 40h / jb` at 0x660190).
 */
export function popCount(mask: number): number {
  if (mask < 0 || mask >= 64) {
    return 0;
  }

  let count = 0;
  for (let bit = 0; bit < 6; ++bit) {
    if ((mask & (1 << bit)) !== 0) {
      ++count;
    }
  }

  return count;
}

/**
 * Which STATE_ITEMSET n tiers SKILLDESC_AppendItemBuffText 0x4e6560 asks for. Pure arithmetic over
 * `add func` and the worn mask — no data lookup, which is why it is testable on its own.
 */
export function selectSetBonusTiers(
  addFunc: number,
  selfSlot: number,
  wornMaskIncludingSelf: number,
  wornMaskExcludingSelf: number,
): number[] {
  const states: number[] = [];

  // 0x4e659f loads the byte and subtracts one, so 0 falls through both arms.
  if (addFunc === 1) {
    // 0x4e6622-0x4e665c. WHICH sibling is worn picks WHICH aprop pair, and the index collapses
    // over the gap this piece leaves (0x4e662f).
    for (let j = 0; j < 6; ++j) {
      if (j === selfSlot) {
        continue;
      }

      if ((wornMaskExcludingSelf & (1 << j)) === 0) {
        continue;
      }

      states.push(ItemSetStates[j > selfSlot ? j - 1 : j] as number);
    }
  } else if (addFunc === 2) {
    // 0x4e65b2-0x4e65f9: N worn pieces light tiers 0 .. N-2.
    const tiers = popCount(wornMaskIncludingSelf) - 1;
    for (let i = 0; i < tiers; ++i) {
      states.push(ItemSetStates[i] as number);
    }
  }

  return states;
}

/** The `%0` the piece list is written through (0x48d8d0 pushes 2769h). */
const SetPieceFormat = 10089;

/** Locale 3333, "Item cannot be traded here." (0x48dab6). */
const TransactionRefusedStringId = 3333;

/** itemstatcost `value`, post-splice row 71 — the set id STATLIST_GetBaseStatValue reads at
 * 0x4e66d7. */
const StatSetValue = 71;

/**
 * The nPropMode ITEMMOD_ApplySetBonuses pushes for both blocks (`push 4` at 0x6601df and
 * 0x66021e). It is recorded rather than left as a bare literal because it is inert here: the only
 * handler in the table that reads the mode is func 1, whose "enhanced" reset needs mode 1
 * (`cmp ecx, 1`, 0x65eb59), so 4 selects nothing.
 */
const SetBonusPropMode = 4;

/**
 * Derives {@link SetItemTooltipContent} from the captured record plus the caller's
 * {@link SetItemTooltipInput}.
 */
export class SetItemTooltipBuilder {
  private readonly data: D2DataFiles;
  private readonly sets: SetTable;
  private readonly items: ItemTable | null;
  private readonly types: ItemTypeTree | null;
  private readonly applier: PropertyApplier;

  constructor(
    data: D2DataFiles | null,
    sets: SetTable | null,
    items: ItemTable | null,
    types: ItemTypeTree | null,
  ) {
    if (data === null) throw new ArgumentNullException('data');
    if (sets === null) throw new ArgumentNullException('sets');

    this.data = data;
    this.sets = sets;
    this.items = items;
    this.types = types;

    this.applier = new PropertyApplier(data, items as ItemTable, types as ItemTypeTree);
    this.sets.resolvePropertyCodesWith(code => this.applier.properties.rowForCode(code));
  }

  /**
   * Null when the writer would draw nothing at all: GetSetItemsLine returning null returns at
   * 0x48d397 and GetSetsLine at 0x48d3ab, both before a single buffer is appended.
   */
  build(
    record: Unit | null,
    item: ItemIdentity | null,
    viewer: ItemViewer | null,
    stats: ReadonlyMap<number, number>,
    input: SetItemTooltipInput | null,
    wearer: Unit | null = null,
  ): SetItemTooltipContent | null {
    if (item === null) throw new ArgumentNullException('item');
    if (input === null) throw new ArgumentNullException('input');

    const piece = this.sets.pieceAt(item.fileIndex);
    if (piece === null) {
      return null;
    }

    const set = this.sets.setAt(piece.setId);
    if (set === null) {
      return null;
    }

    return {
      setName: this.str(set.nameStringId) + this.terminator,
      transactionRefusedText: this.str(TransactionRefusedStringId) + this.terminator,
      pieces: this.buildPieces(set, input),
      partialText: this.buildPartial(record, item, viewer, stats, piece, input),
      fullSetText: this.buildFullSet(item, viewer, wearer, piece.setId, stats, input),
    };
  }

  private buildPieces(set: SetRecord, input: SetItemTooltipInput): SetPieceLine[] {
    const owned = new Set<number>(input.ownedSetItemIds ?? []);

    const pieces: SetPieceLine[] = [];

    // The loop is bounded by sets[+0x0C], the RUNTIME member count, and breaks on the first null
    // pointer (0x48d8a7) — both of which SetRecord.pieces already models.
    for (const member of set.pieces) {
      pieces.push({
        // wsprintf 0x48be80 is Blizzard's positional templater, not the Win32 one, and with ENG
        // data the format is bare "%0" — the name verbatim (0x48d8dd).
        text:
          ItemNameBuilder.positional(
            this.str(SetPieceFormat),
            this.str(member.nameStringId),
            null,
            null,
          ) + this.terminator,
        owned: owned.has(member.setItemId),
      });
    }

    return pieces;
  }

  /**
   * SKILLDESC_AppendItemBuffText 0x4e6560, one BuildStatBuffDesc per selected tier.
   *
   * A selected tier still renders nothing unless its list is ENABLED. BuildStatBuffDesc reaches it
   * through GetStatList(item, state, 0) (0x4e60ff), whose zero mask sends it down the pMyLastList
   * chain at +0x3C (0x6257ef); STATLIST_ToggleStateDisabled parks a disabled tier on the OTHER
   * chain by setting STATLIST_SET (0x6279e7) and re-attaching, and STATLIST_AttachStatListToUnit
   * files a 0x2000 list under +0x40 (0x626e67). So a tier carrying STATLIST_SET is unreachable
   * from here.
   */
  private buildPartial(
    record: Unit | null,
    item: ItemIdentity,
    viewer: ItemViewer | null,
    stats: ReadonlyMap<number, number>,
    piece: SetItemRecord,
    input: SetItemTooltipInput,
  ): string {
    const states = selectSetBonusTiers(
      piece.addFunc,
      piece.slot,
      input.wornMaskIncludingSelf ?? 0,
      input.wornMaskExcludingSelf ?? 0,
    );

    if (states.length === 0 || record === null) {
      return '';
    }

    let text = '';

    for (const state of states) {
      const tier = SetItemTooltipBuilder.enabledTier(record, state);
      if (tier.size === 0) {
        continue;
      }

      text += this.describe(tier, item, viewer, stats, true);
    }

    return text;
  }

  /**
   * SKILLDESC_AppendItemBuffTextAlt 0x4e6680. The block lives on the PLAYER's statlist, so the
   * described unit is the player (0x4e670c passes a1) and the never-breaks tail at 0x4e63a4 —
   * which needs `*v8 == 4` — cannot fire.
   */
  private buildFullSet(
    item: ItemIdentity,
    viewer: ItemViewer | null,
    wearer: Unit | null,
    setId: number,
    stats: ReadonlyMap<number, number>,
    input: SetItemTooltipInput,
  ): string {
    if (input.isEquipped !== true) {
      return '';
    }

    const source =
      input.fullSetStats ??
      SetItemTooltipBuilder.fullSetStatsOfWearer(wearer, setId) ??
      this.deriveSetBonuses(item, setId, input.wornMaskIncludingSelf ?? 0);

    // No null check: deriveSetBonuses is the last fallback and always returns a Map, so `source`
    // cannot be null. The empty case is the `full.size === 0` test below.
    const full = new Map<number, number>();
    for (const [key, value] of source) {
      full.set(key, (full.get(key) ?? 0) + value);
    }

    if (full.size === 0) {
      return '';
    }

    return this.describe(full, item, viewer, stats, false);
  }

  /**
   * ITEMMOD_ApplySetBonuses 0x660120, replayed against sets.txt. This is what PUT the block on the
   * wearer's chain in the first place — ITEM_ManageSetBonusStatList 0x663c9e opens a
   * STATE_ITEMSET list on the wearer, stamps stat 71 with the set id (0x663c93) and then calls it
   * (0x663c9e) — so replaying it reconstructs exactly the list SKILLDESC_AppendItemBuffTextAlt
   * would have read, without needing the wearer's chain.
   *
   * Note that the block is NOT just the FCode properties: the same list receives the PARTIAL
   * PCode tiers (0x6601c4), which is why a four-of-five set still shows a gold block. The rebuild
   * is exact for shipped data because 217 of the 220 property slots have FMin == FMax; the three
   * that do not resolve to the low end here, the same way {@link PropertyApplier} handles every
   * other seed-dependent range.
   */
  private deriveSetBonuses(
    item: ItemIdentity,
    setId: number,
    wornMaskIncludingSelf: number,
  ): Map<number, number> {
    const stats = new Map<number, number>();

    const set = this.sets.setAt(setId);
    if (set === null) {
      return stats;
    }

    const count = popCount(wornMaskIncludingSelf);

    // sets +0x0C is the RUNTIME member count the link loop built (0x6366ff), which is
    // pieces.length. n = min(count, nSetItems - 1) at 0x6601ae-0x6601b5, then
    // `lea eax, [eax+eax-2]` at 0x6601b7.
    const members = set.pieces.length;
    const capped = count < members - 1 ? count : members - 1;
    const limit = 2 * capped - 2;

    let slot = 0;
    for (const property of this.sets.partialProperties(setId)) {
      // `test eax,eax / jle` at 0x6601c2 skips the block outright for a non-positive limit, and
      // the tail test is `cmp ebx, eax / jl` at 0x6601ef.
      if (slot >= limit) {
        break;
      }

      // 0x6601ca skips a blank slot and carries on. It does NOT break — that asymmetry with the
      // full block below is the whole reason both walks exist separately.
      if (property.propertyId >= 0) {
        this.applier.apply(SetBonusPropMode, item, property, stats);
      }

      ++slot;
    }

    // 0x6601f9: the full block needs the WHOLE set, not one short of it.
    if (count < members) {
      return stats;
    }

    for (const property of this.sets.fullProperties(setId)) {
      // 0x660209 ends the walk rather than skipping.
      if (property.propertyId < 0) {
        break;
      }

      this.applier.apply(SetBonusPropMode, item, property, stats);
    }

    return stats;
  }

  /**
   * The wearer's STATE_ITEMSET list for THIS set — 0x4e66c9 walks states 165..170 and 0x4e66d7
   * keeps the one whose stat 71 (`value`) is the set id, which is how the engine tells one worn
   * set's block from another's when a character wears two.
   *
   * Null when the wearer carries no statlist chain at all, which is not the same as an empty one:
   * a producer that only records merged attributes has nothing to offer here, and the caller has to
   * supply `SetItemTooltipInput.fullSetStats` instead.
   */
  private static fullSetStatsOfWearer(
    wearer: Unit | null,
    setId: number,
  ): Map<number, number> | null {
    if (wearer === null || wearer.statsLists.length === 0) {
      return null;
    }

    for (const group of wearer.statsLists) {
      if (
        group.stateNo < ItemStatListStates.ItemSet1 ||
        group.stateNo > ItemStatListStates.ItemSet6
      ) {
        continue;
      }

      const packed = new Map<number, number>();
      let isThisSet = false;

      for (const stat of group.stats) {
        const layer = stat.layer ?? 0;

        if (stat.id === StatSetValue && layer === 0 && stat.value === setId) {
          isThisSet = true;
        }

        packed.set(ItemStatReader.packStatKey(layer, stat.id), stat.value);
      }

      if (isThisSet) {
        return packed;
      }
    }

    return null;
  }

  /**
   * The item's own STATE_ITEMSET n list, dropped when STATLIST_SET marks it disabled. The stats
   * are NOT merged across sockets: BuildStatBuffDesc's filler walk (0x4e6162) only ever finds
   * state-0 lists on a gem.
   */
  private static enabledTier(record: Unit, state: number): Map<number, number> {
    const tier = new Map<number, number>();

    for (const group of ItemStatReader.enumerateGroups(record)) {
      if (
        group.fromSocket ||
        group.stateNo !== state ||
        (group.flags & ItemStatListFlags.Set) !== 0
      ) {
        continue;
      }

      for (const [key, value] of group.enumerateStats()) {
        tier.set(key, (tier.get(key) ?? 0) + value);
      }
    }

    return tier;
  }

  /**
   * Both set blocks pass isMainStatBlock = 0 (0x4e65f9 / 0x4e670c), which costs them the inherent
   * damage-to-undead line (0x4e61ea), and a8 = 1, which terminates every line.
   */
  private describe(
    tier: ReadonlyMap<number, number>,
    item: ItemIdentity,
    viewer: ItemViewer | null,
    stats: ReadonlyMap<number, number>,
    describedUnitIsItem: boolean,
  ): string {
    const values = new SynthesisedStatValues(
      tier,
      item,
      viewer,
      this.items,
      this.types,
      stats,
      describedUnitIsItem,
    );

    const generator = new ItemDescriptionGenerator(
      this.data.itemStatCost,
      this.data.strings,
      values,
      this.data.skills,
      this.data.classes,
      this.data.monsterTypes,
      null,
      false,
    );

    return generator.join(generator.describe(tier));
  }

  private str(id: number): string {
    return this.data.strings.getByIndex(id) ?? '';
  }

  private get terminator(): string {
    return this.str(DescStringIds.Newline);
  }
}
