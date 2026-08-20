import { ItemStatReader, sortByKey } from './ItemStatReader.js';
import { Int32, type IItemStatOpTable } from '../Types.js';

/**
 * Re-applies the op-13 stats the engine folds into FullStats.
 *
 * ItemStatCost's op stats are a REVERSE index: the loader writes an entry into the record of
 * each of a row's `op stat1..3` TARGETS, so op 13 declared on stat 18 modifies 21, 23 and 159
 * — not the other way round (walked at 0x626259 / 0x626273 / 0x62666b, +0xDE, 6-byte stride,
 * 16 slots).
 *
 * `STATLIST_CalcCombinedStatValue` 0x626200 case 13 (0x626626-0x626662) accumulates
 * `D2ApplyPercent(base[target], combined[percent], 100)`. Both inputs are forced to layer 0
 * (`push 0` at 0x626635, `shl edx, 10h` at 0x626648) and no nValShift is applied anywhere in
 * that arm. A zero on either side skips the entry; only op 0 ends the walk.
 *
 * The base value comes from STATLIST_LookupBaseStatWithMinAccr 0x624ed0, which always reads
 * `Stats` (+0x24) — the captured `base` group — while the result lands in FullStats (0x625158),
 * which is what GetStatUnsignedValue reads. So the pass is not self-referential: it consumes
 * the base view and produces the merged one.
 */
export class ItemStatOps {
  /**
   * Applies every op-13 entry to `merged` in place. Only the Equipped and ForSale views may be
   * passed here.
   *
   * NOT BaseOnly: that view IS this pass's input, and DamageIsModified/Bonus are
   * merged-minus-base, so moving base would silently strip every colour marker.
   * NOT Modifiers: those are individual chain nodes, and the stats described there are
   * 16/17/18 themselves.
   */
  static resolve(
    merged: Map<number, number> | null | undefined,
    baseStats: ReadonlyMap<number, number> | null | undefined,
    table: IItemStatOpTable | null | undefined,
  ): void {
    if (
      merged === null ||
      merged === undefined ||
      baseStats === null ||
      baseStats === undefined ||
      table === null ||
      table === undefined
    ) {
      return;
    }

    let added = false;

    for (const entry of table.percentOfBaseEntries) {
      const percent = merged.get(ItemStatReader.packStatKey(0, entry.percentStat));
      if (percent === undefined || percent === 0) {
        continue;
      }

      const baseValue = baseStats.get(ItemStatReader.packStatKey(0, entry.targetStat));
      if (baseValue === undefined || baseValue === 0) {
        continue;
      }

      const key = ItemStatReader.packStatKey(0, entry.targetStat);
      const existing = merged.get(key);
      added = added || existing === undefined;
      merged.set(
        key,
        Int32.of(
          (existing === undefined ? 0 : existing) + ItemStatOps.applyPercent(baseValue, percent),
        ),
      );
    }

    ItemStatOps.dropResolvedPercents(merged, table);

    if (added) {
      // The merged view is a SortedDictionary, so a target the merge never saw still lands in
      // key order rather than at the end.
      const sorted = sortByKey(merged);
      merged.clear();
      for (const [key, value] of sorted) {
        merged.set(key, value);
      }
    }
  }

  /**
   * An op-13 percent stat is NOT stored in an item's FullStats once it has landed on a target.
   *
   * STATLIST_ApplyComplexStatFormula 0x626770 writes each TARGET to FullStats (0x626786), then —
   * when that target computed non-zero (0x62678d/0x626790) — switches on the op. Case 13 is
   * 0x626821: `cmp dword ptr [esi+8], 4`, i.e. dwOwnerType == UNIT_ITEM, and only then does
   * 0x626827 clear the update flag. After the three-target loop, 0x626847 skips the
   * STATLIST_SetFullStatValue at 0x626868 that would have stored the PERCENT stat itself. D2MOO
   * states it plainly: `case STAT_OP_ADD_ITEM_STAT_PCT: if (dwOwnerType == UNIT_ITEM) bUpdate =
   * FALSE;`.
   *
   * So on an item, `GetStatUnsignedValue(item, 75, 0)` reads 0 however large the durability bonus
   * is, and STATLIST_GetStatBonusFromLists — merged minus base, 0x625570 — is 0 too. That is what
   * stops INV_FormatDurabilityText colouring the max on a Superior weapon (0x484f14 gates the
   * marker on exactly that difference).
   *
   * The gate is per-percent-stat, not per-target: one target computing non-zero drops it. A target
   * that computed ZERO leaves the flag alone, which is why the removal is conditional rather than
   * unconditional.
   *
   * This applies to an ITEM's list only. On a player's list dwOwnerType is not 4, the flag
   * survives, and the percent stat IS stored — which is why `resolve` documents that only item
   * views may be passed to it.
   */
  private static dropResolvedPercents(merged: Map<number, number>, table: IItemStatOpTable): void {
    const resolved: number[] = [];

    for (const entry of table.percentOfBaseEntries) {
      const target = merged.get(ItemStatReader.packStatKey(0, entry.targetStat));
      if (target === undefined || target === 0) {
        continue;
      }

      const key = ItemStatReader.packStatKey(0, entry.percentStat);
      if (merged.has(key) && !resolved.includes(key)) {
        resolved.push(key);
      }
    }

    // Removed after the walk: a percent stat has up to three targets, and dropping it while the
    // enumeration is still reading those entries would cut the later ones short.
    for (const key of resolved) {
      merged.delete(key);
    }
  }

  /**
   * D2ApplyPercent with a 100 divisor. 1.14d does this in integers — the truncation is
   * load-bearing: a +10% prefix on a Throwing Knife (max throw damage 9) produces 0 and the
   * item renders identically to an unmodified one.
   */
  private static applyPercent(value: number, percent: number): number {
    // The product is a C# `long` before the cast back to int, which JS doubles cannot hold
    // exactly across the whole int32 range.
    return Number(BigInt.asIntN(32, (BigInt(value) * BigInt(percent)) / 100n));
  }
}
