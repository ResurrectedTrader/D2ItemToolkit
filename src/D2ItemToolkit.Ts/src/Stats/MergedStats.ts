/**
 * One stat of a merged view, in the RAW encoding a capture carries — pre-nValShift, and with op 13
 * already folded in. Max life is `100 << 8`, not 100.
 *
 * Raw on purpose: a consumer indexing items for search derives its bounds from the same
 * itemstatcost scale the record uses, so a display-scaled value would need a second scale beside it
 * and the two would drift.
 */
export interface MergedStat {
  readonly statId: number;

  /** The skill, class or tab. 0 for a plain stat. */
  readonly layer: number;

  readonly value: number;
}

/** Per-call knobs for `TooltipEngine.mergedStats`. */
export interface MergedStatsOptions {
  /**
   * Folds in what the socket fillers grant. A gem or rune carries no stats of its own, so this is
   * what makes them count at all. Defaults to true.
   */
  includeSockets?: boolean;

  /**
   * Earned SET TIERS, from the record's own state 165-170 lists. Off by default because the tiers
   * belong to the wearer's other pieces rather than to this item, which is the rule
   * `TooltipEngine.ranges` follows too.
   *
   * This reads the record rather than choosing tiers, so it cannot answer "what would this give if
   * the set were complete" — only "what did the capture already carry".
   */
  includeSetBonuses?: boolean;
}

/**
 * What an item's stats add up to, the way the game resolves them: its base array, its own affix /
 * unique / setitems / runeword nodes, its socket fillers, and op 13 applied.
 *
 * An op-13 PERCENT survives beside the target it resolved onto — `item_armor_percent` 16 is returned
 * as well as the Defense 31 it already contributed to — because the tooltip draws it as its own line
 * and a caller indexing modifiers needs to find it. Summing both would double count.
 *
 * Deliberately NOT the same question `TooltipEngine.render` answers — see
 * `fillersIgnoredBecauseWorn`.
 */
export interface ItemMergedStats {
  /**
   * One entry per (stat, layer) that resolved to a NON-ZERO value, ordered by LAYER then stat —
   * the key is `(layer << 16) | stat`, so ascending key order is layer-major. Do not binary-search
   * this by stat id.
   *
   * A stat that cancels to zero across two nodes is absent rather than present as 0, which reads
   * the same way to a summing consumer and keeps "absent" meaning one thing.
   */
  readonly stats: readonly MergedStat[];

  /**
   * True when this item is a set piece the wearer has EQUIPPED with something in its sockets — the
   * one case where these totals deliberately differ from what the game is currently granting.
   *
   * ITEM_RecalcAllEquippedItems 0x4c1350 detaches a worn set item's stat list and rebuilds it
   * through ITEM_ProcessSetItemEquip, which re-applies only set state; nothing re-applies the
   * fillers. So the game really does grant a worn Tal Rasha's Horadric Crest with an Um in it
   * `All Resistances +15`, not 30, and `render` reproduces that.
   *
   * These totals ignore it on purpose, because the useful question about a stored item is what it
   * WOULD give — an item must not drop out of a search because something equipped it. This flag is
   * how a caller knows to say so rather than reading as its own bug.
   *
   * Set only when the gems.txt SYNTHESIS actually contributed, which is the only part the discard
   * gates. A JEWEL's affixes arrive through the stat view, which `render` does not gate either, so
   * a jewel-socketed worn set piece leaves the two views in agreement and this stays false — an
   * earlier version keyed it on "has a filler" and claimed a disagreement that was not there.
   */
  readonly fillersIgnoredBecauseWorn: boolean;

  /**
   * Stat ids left OUT of `stats` because their value is a packed encoding rather than a magnitude —
   * stat 204's `(maxCharges << 8) + current`, and the by-time triples 268..303. A packed word is not
   * a quantity, so it must not be summed and it must not be compared against a bound as if it were
   * one.
   *
   * Excluded by STAT rather than by (stat, layer): charges of two different skills sit at different
   * layers and never actually collide, so this is broader than a merge conflict requires — the
   * reason is the encoding itself, not the addition.
   *
   * Reported rather than dropped silently, and ABSENT rather than zero: a zero would satisfy every
   * "at most N" bound a caller applied to them. Ask the raw statlists for these — charges and
   * by-time are provenance questions anyway.
   */
  readonly excludedPackedStats: readonly number[];
}
