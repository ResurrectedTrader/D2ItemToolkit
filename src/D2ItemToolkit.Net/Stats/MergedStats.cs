using System.Collections.Generic;

namespace D2ItemToolkit
{
    /// <summary>
    /// One stat of a merged view, in the RAW encoding a capture carries — pre-nValShift, and with
    /// op 13 already folded in. Max life is `100 &lt;&lt; 8`, not 100.
    ///
    /// Raw on purpose: a consumer indexing items for search derives its bounds from the same
    /// itemstatcost scale the record uses, so a display-scaled value would need a second scale
    /// beside it and the two would drift.
    /// </summary>
    public struct MergedStat
    {
        public int StatId;

        /// <summary>The skill, class or tab. 0 for a plain stat.</summary>
        public int Layer;

        public int Value;

        public MergedStat(int statId, int layer, int value)
        {
            StatId = statId;
            Layer = layer;
            Value = value;
        }

        public override string ToString()
        {
            return Layer == 0
                ? string.Format("stat {0} = {1}", StatId, Value)
                : string.Format("stat {0} layer {1} = {2}", StatId, Layer, Value);
        }
    }

    /// <summary>Per-call knobs for <see cref="TooltipEngine.MergedStats"/>.</summary>
    public sealed class MergedStatsOptions
    {
        // NOT a shared instance: the fields are public and mutable, so one caller writing through a
        // singleton would poison every later defaulted call in the process.
        internal static MergedStatsOptions Default
        {
            get { return new MergedStatsOptions(); }
        }

        /// <summary>
        /// Folds in what the socket fillers grant. A gem or rune carries no stats of its own, so
        /// this is what makes them count at all.
        /// </summary>
        public bool IncludeSockets = true;

        /// <summary>
        /// Earned SET TIERS, from the record's own state 165-170 lists. Off by default because the
        /// tiers belong to the wearer's other pieces rather than to this item, which is the rule
        /// <see cref="TooltipEngine.Ranges"/> follows too.
        ///
        /// This reads the record rather than choosing tiers, so it cannot answer "what would this
        /// give if the set were complete" — only "what did the capture already carry".
        /// </summary>
        public bool IncludeSetBonuses;
    }

    /// <summary>
    /// What an item's stats add up to, the way the game resolves them: its base array, its own
    /// affix / unique / setitems / runeword nodes, its socket fillers, and op 13 applied.
    ///
    /// An op-13 PERCENT survives beside the target it resolved onto — `item_armor_percent` 16 is
    /// returned as well as the Defense 31 it already contributed to — because the tooltip draws it
    /// as its own line and a caller indexing modifiers needs to find it. Summing both would double
    /// count.
    /// </summary>
    public sealed class ItemMergedStats
    {
        internal ItemMergedStats(
            IReadOnlyList<MergedStat> stats,
            IReadOnlyList<int> excludedPackedStats)
        {
            Stats = stats;
            ExcludedPackedStats = excludedPackedStats;
        }

        /// <summary>
        /// One entry per (stat, layer) that resolved to a NON-ZERO value, ordered by LAYER then
        /// stat — the key is `(layer &lt;&lt; 16) | stat`, so ascending key order is layer-major.
        /// Do not binary-search this by stat id.
        ///
        /// A stat that cancels to zero across two nodes is absent rather than present as 0, which
        /// reads the same way to a summing consumer and keeps "absent" meaning one thing.
        /// </summary>
        public IReadOnlyList<MergedStat> Stats { get; private set; }

        /// <summary>
        /// Stat ids left OUT of <see cref="Stats"/> because their value is a packed encoding rather
        /// than a magnitude — stat 204's `(maxCharges &lt;&lt; 8) + current`, and the by-time triples
        /// 268..303. A packed word is not a quantity, so it must not be summed and it must not be
        /// compared against a bound as if it were one.
        ///
        /// Excluded by STAT rather than by (stat, layer): charges of two different skills sit at
        /// different layers and never actually collide, so this is broader than a merge conflict
        /// requires — the reason is the encoding itself, not the addition.
        ///
        /// Reported rather than dropped silently, and ABSENT rather than zero: a zero would satisfy
        /// every "at most N" bound a caller applied to them. Ask the raw statlists for these —
        /// charges and by-time are provenance questions anyway.
        /// </summary>
        public IReadOnlyList<int> ExcludedPackedStats { get; private set; }
    }
}
