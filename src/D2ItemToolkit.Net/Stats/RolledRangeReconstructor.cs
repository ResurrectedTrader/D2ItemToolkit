using System;
using System.Collections.Generic;

namespace D2ItemToolkit
{
    /// <summary>
    /// Where a reconstructed range came from. Flags, because two sources can land on one stat — a
    /// unique's own `res-all` and a socketed rune's, for instance.
    /// </summary>
    [Flags]
    public enum RollSources
    {
        None = 0,

        /// <summary>The base item's own rolled Defense, armor.txt `minac`..`maxac`.</summary>
        Base = 1,

        /// <summary>A magic, rare or crafted affix, from the ids the record stores.</summary>
        Affix = 2,
        Unique = 4,
        SetItem = 8,

        /// <summary>An earned set tier's partial or full bonus.</summary>
        SetBonus = 16,
        Runeword = 32,

        /// <summary>A socket filler's gem/rune mods.</summary>
        Socket = 64,

        /// <summary>A superior item's qualityitems.txt modifier.</summary>
        Superior = 128,

        /// <summary>A crafted recipe's FIXED mods, once the recipe has been identified.</summary>
        Crafted = 256,

        /// <summary>
        /// Written through a PropertyGroups.txt pick or a func-25 stat pick; ORed with the outer
        /// source.
        /// </summary>
        PropertyGroup = 512,
    }

    /// <summary>
    /// How a choice picks. The first three are PropertyGroups.txt `pickmode` (sub_14028A190
    /// 0x14028a2c1..0x14028a2e5); <see cref="StatPick"/> is func 25's one-of-K stat.
    /// </summary>
    public enum ChoicePickMode
    {
        /// <summary>pickmode 0: every entry, in column order.</summary>
        All = 0,

        /// <summary>pickmode 1, sub_14028A6D0: N entries without replacement.</summary>
        Exactly = 1,

        /// <summary>pickmode 2, sub_14028A970: N draws with replacement, so up to N entries.</summary>
        UpTo = 2,

        /// <summary>ITEMMODS_PropertyFunc25 0x140289d70: one stat of the target property's.</summary>
        StatPick = 25,
    }

    public enum ChoiceResolution
    {
        /// <summary>No record to resolve against; every outcome is listed.</summary>
        NoRecord,

        /// <summary>Exactly one outcome of this choice agrees with the record.</summary>
        Resolved,

        /// <summary>More than one outcome agrees with the record.</summary>
        Ambiguous,

        /// <summary>No combination of outcomes agrees with the record; every outcome is listed.</summary>
        Contradicted,

        /// <summary>
        /// Not enumerated — a layer-rolling or func-25 option, a param span over 64, more than 1024
        /// outcomes, or more than 4096 combinations across the item. Contributes nothing.
        /// </summary>
        Unsupported,
    }

    /// <summary>One applied entry of an outcome: which option, at which param.</summary>
    public sealed class RolledChoicePick
    {
        internal RolledChoicePick(int option, int param)
        {
            Option = option;
            Param = param;
        }

        /// <summary>Index into <see cref="RolledChoice.Options"/>.</summary>
        public int Option { get; private set; }

        public int Param { get; private set; }
    }

    /// <summary>One option applied ALONE at one param, both ends.</summary>
    public sealed class RolledChoiceVariant
    {
        internal RolledChoiceVariant(int param, IReadOnlyList<RolledStatRange> stats)
        {
            Param = param;
            Stats = stats;
        }

        /// <summary>The param this variant was applied at; for `skilltab` it selects the layer.</summary>
        public int Param { get; private set; }

        public IReadOnlyList<RolledStatRange> Stats { get; private set; }
    }

    public sealed class RolledChoiceOption
    {
        internal RolledChoiceOption(
            int entry,
            int propertyId,
            string code,
            int weight,
            int paramLow,
            int paramHigh,
            int min,
            int max,
            IReadOnlyList<RolledChoiceVariant> variants,
            RolledChoice nested)
        {
            Entry = entry;
            PropertyId = propertyId;
            Code = code;
            Weight = weight;
            ParamLow = paramLow;
            ParamHigh = paramHigh;
            Min = min;
            Max = max;
            Variants = variants;
            Nested = nested;
        }

        /// <summary>The group's column number, 1..8; for a stat pick, the target row's set, 1..7.</summary>
        public int Entry { get; private set; }

        /// <summary>The Properties.txt row applied; -1 for a nested group.</summary>
        public int PropertyId { get; private set; }

        public string Code { get; private set; }

        /// <summary>max(chance, 1), 0x14028a050.</summary>
        public int Weight { get; private set; }

        public int ParamLow { get; private set; }
        public int ParamHigh { get; private set; }

        /// <summary>`modMin`/`modMax` as authored; a nested group's count.</summary>
        public int Min { get; private set; }

        public int Max { get; private set; }

        /// <summary>One per param in ParamLow..ParamHigh; empty for a nested group.</summary>
        public IReadOnlyList<RolledChoiceVariant> Variants { get; private set; }

        /// <summary>
        /// A group entry naming an earlier group, or null. Its outcomes are folded into this
        /// choice's; the nested node itself is descriptive and always reports
        /// <see cref="ChoiceResolution.NoRecord"/> or <see cref="ChoiceResolution.Unsupported"/>.
        /// </summary>
        public RolledChoice Nested { get; private set; }
    }

    /// <summary>
    /// A property cell whose stats are PICKED at spawn rather than fixed: a PropertyGroups.txt
    /// code (kind-1 link, sub_140214F40 0x140214fb9) or a func-25 property. The record holds only
    /// the outcome, so the choice is resolved by keeping the outcomes consistent with it.
    /// </summary>
    public sealed class RolledChoice
    {
        internal RolledChoice(
            int groupRow,
            string code,
            RollSources sources,
            ChoicePickMode pickMode,
            int countLow,
            int countHigh,
            IReadOnlyList<RolledChoiceOption> options)
        {
            GroupRow = groupRow;
            Code = code;
            Sources = sources;
            PickMode = pickMode;
            CountLow = countLow;
            CountHigh = countHigh;
            Options = options;
            Consistent = new List<IReadOnlyList<RolledChoicePick>>();
        }

        /// <summary>The PropertyGroups.txt row; -1 for a stat pick.</summary>
        public int GroupRow { get; private set; }

        /// <summary>The group code, or the func-25 property's code.</summary>
        public string Code { get; private set; }

        /// <summary>The outer source ORed with <see cref="RollSources.PropertyGroup"/>.</summary>
        public RollSources Sources { get; private set; }

        public ChoicePickMode PickMode { get; private set; }

        /// <summary>The fewest entries N can apply, after the 0x14028a738 rule.</summary>
        public int CountLow { get; private set; }

        public int CountHigh { get; private set; }

        public IReadOnlyList<RolledChoiceOption> Options { get; private set; }

        public ChoiceResolution Resolution { get; internal set; }

        /// <summary>
        /// The outcomes of THIS choice that agree with the record — every outcome for
        /// <see cref="ChoiceResolution.NoRecord"/> and <see cref="ChoiceResolution.Contradicted"/>,
        /// none for <see cref="ChoiceResolution.Unsupported"/>. Each is a list of picks ordered by
        /// option; an empty list is the "nothing applied" outcome.
        /// </summary>
        public IReadOnlyList<IReadOnlyList<RolledChoicePick>> Consistent { get; internal set; }
    }

    /// <summary>One stat's reconstructed span, as the item's own sources could have rolled it.</summary>
    public sealed class RolledStatRange
    {
        internal RolledStatRange(
            int statId, int layer, int low, int high, RollSources sources, int valShift)
        {
            StatId = statId;
            Layer = layer;
            Low = low;
            High = high;
            Sources = sources;
            _valShift = valShift;
        }

        private readonly int _valShift;

        public int StatId { get; private set; }

        /// <summary>The stat's layer — a skill id, a class, a skill tab. 0 for a plain stat.</summary>
        public int Layer { get; private set; }

        /// <summary>The value when every contributing property rolls its minimum.</summary>
        public int Low { get; private set; }

        /// <summary>The value when every contributing property rolls its maximum.</summary>
        public int High { get; private set; }

        /// <summary>
        /// Which sources contribute. Advisory: the Low/High values come from one combined
        /// application of every property, so a stat two sources both write carries both flags but
        /// is not split between them.
        /// </summary>
        public RollSources Sources { get; private set; }

        /// <summary>False when the stat could only ever have taken one value.</summary>
        public bool IsRange { get { return Low != High; } }

        /// <summary>
        /// True when the value is a PACKED encoding rather than a magnitude, so
        /// <see cref="Low"/> and <see cref="High"/> are not a range anyone should show: stat 204
        /// packs `(maxCharges &lt;&lt; 8) + current` (func 19, 0x65f84b) and stats 268..303 pack
        /// `param + 4 * ((max + 256) &lt;&lt; 10 | (min + 256))` (func 18, 0x65f934).
        ///
        /// The span is still correct — it is the span of the packed word — which is exactly why it
        /// must be flagged: printed raw it reads as "(5/9 Charges) [2306-2313]". Both encodings
        /// already carry their own two ends inside the value, so a caller wanting a real range
        /// there should decode rather than subtract.
        /// </summary>
        public bool IsPackedEncoding
        {
            get { return IsPackedStat(StatId); }
        }

        /// <summary>
        /// The same test as <see cref="IsPackedEncoding"/>, for a bare stat id — so a caller
        /// deciding which stats may be summed reads the rule from here rather than deriving its own
        /// from `descFunc`. Two derivations of one fact drift; this is the owner.
        /// </summary>
        public static bool IsPackedStat(int statId)
        {
            return statId == StatChargedSkill || (statId >= FirstByTime && statId <= LastByTime);
        }

        /// <summary>
        /// The low end as a READER sees it, with a packed value decoded.
        ///
        /// For stat 204 that is the CURRENT charge count: the value is
        /// `(maxCharges &lt;&lt; 8) + current`, the high byte is identical at both ends because the
        /// max is fixed by the property, and only the low byte is drawn off the seed
        /// (0x65f7ec..0x65f80e). So the low byte alone is the whole span, and it is the number the
        /// "(5/9 Charges)" line shows first.
        ///
        /// The by-time stats need no decoding: func 18 packs `property.Min` and `property.Max`
        /// straight in and **never rolls** (0x65f870 has no RollRandomValue call), so both ends
        /// produce the identical word and <see cref="IsRange"/> is always false for them. They are
        /// in <see cref="IsPackedEncoding"/> defensively, not because a span can appear there.
        /// </summary>
        public int DisplayLow { get { return Display(Low); } }

        /// <summary>The high end, decoded the same way as <see cref="DisplayLow"/>.</summary>
        public int DisplayHigh { get { return Display(High); } }

        private int Display(int packed)
        {
            if (StatId == StatChargedSkill)
            {
                return packed & 0xFF;
            }

            // A packed triple is not a magnitude, so shifting it would corrupt it rather than
            // scale it.
            if (IsPackedEncoding)
            {
                return packed;
            }

            // itemstatcost ValShift. Life, mana and stamina are stored 8.8 fixed point and every
            // WRITER shifts them down before printing, so a span that skipped it read 256x too
            // large: "+11 to Life [2816-3840]".
            return packed >> _valShift;
        }

        private const int StatChargedSkill = 204;
        private const int FirstByTime = 268;
        private const int LastByTime = 303;

        public override string ToString()
        {
            return Layer == 0
                ? string.Format("stat {0}: {1}..{2}", StatId, Low, High)
                : string.Format("stat {0} layer {1}: {2}..{3}", StatId, Layer, Low, High);
        }
    }

    /// <summary>
    /// A property whose ROLL picks the stat's LAYER instead of its value — funcs 12 and 36. The
    /// value is fixed; what varies is which skill or class it lands on.
    /// </summary>
    public sealed class RolledLayerRange
    {
        internal RolledLayerRange(
            int statId, int layerLow, int layerHigh, int value, RollSources sources)
        {
            StatId = statId;
            LayerLow = layerLow;
            LayerHigh = layerHigh;
            Value = value;
            Sources = sources;
        }

        public int StatId { get; private set; }

        /// <summary>The lowest layer the roll could land on — inclusive.</summary>
        public int LayerLow { get; private set; }

        /// <summary>The highest layer the roll could land on — inclusive.</summary>
        public int LayerHigh { get; private set; }

        /// <summary>The value, which does not vary. Ormus' Robes is always +3, to one of 25 skills.</summary>
        public int Value { get; private set; }

        public RollSources Sources { get; private set; }

        public override string ToString()
        {
            return string.Format(
                "stat {0}: {1} at one layer in {2}..{3}", StatId, Value, LayerLow, LayerHigh);
        }
    }

    /// <summary>
    /// The spans an item's stats could have rolled within, reconstructed from the tables its own
    /// record points at. Like <see cref="TooltipBreakdown"/> this is a capability the game does not
    /// have, so it cannot be checked against the original; what it can be checked against is the
    /// item's OWN recorded values, which must fall inside the spans claimed for them.
    /// </summary>
    public sealed class ItemRollRanges
    {
        internal ItemRollRanges(
            IReadOnlyList<RolledStatRange> stats,
            IReadOnlyList<RolledLayerRange> layerVaries,
            IReadOnlyList<RolledChoice> choices,
            IReadOnlyList<int> outOfRange,
            IReadOnlyList<int> unattributed,
            IReadOnlyList<int> itemLevelDependent,
            IReadOnlyList<int> unsupportedFuncs,
            bool craftedRecipeUnknown,
            int craftedRecipe)
        {
            Stats = stats;
            LayerVaries = layerVaries;
            Choices = choices;
            OutOfRange = outOfRange;
            Unattributed = unattributed;
            ItemLevelDependent = itemLevelDependent;
            UnsupportedFuncs = unsupportedFuncs;
            CraftedRecipeUnknown = craftedRecipeUnknown;
            CraftedRecipe = craftedRecipe;
        }

        /// <summary>Every stat a reconstructed property explains, ordered by stat then layer.</summary>
        public IReadOnlyList<RolledStatRange> Stats { get; private set; }

        /// <summary>
        /// Properties whose ROLL picks the layer rather than the value — funcs 12 and 36,
        /// `skill-rand` and `randclassskill`. Kept apart from <see cref="Stats"/> because a span of
        /// VALUES is the wrong shape for them: the value is fixed and the layer is what varies.
        /// </summary>
        public IReadOnlyList<RolledLayerRange> LayerVaries { get; private set; }

        /// <summary>
        /// The picks the item's sources make — PropertyGroups.txt codes and func-25 properties — in
        /// gather order. The resolved outcomes are already folded into <see cref="Stats"/>.
        /// </summary>
        public IReadOnlyList<RolledChoice> Choices { get; private set; }

        /// <summary>
        /// Stat ids the item carries whose recorded value falls OUTSIDE the span reconstructed for
        /// it. Always empty for a record the game produced; a non-empty list means the
        /// reconstruction is wrong, so it is surfaced rather than hidden.
        /// </summary>
        public IReadOnlyList<int> OutOfRange { get; private set; }

        /// <summary>
        /// Stat ids the item carries that no reconstructed property accounts for. Expected to be
        /// non-empty in ordinary use — a charm's own base stats, anything the producer synthesised —
        /// so this is a coverage report, not an error.
        /// </summary>
        public IReadOnlyList<int> Unattributed { get; private set; }

        /// <summary>
        /// Property ids whose value the game derives from the ITEM's level, which a record need not
        /// carry (funcs 11, 14 and 19). Their spans are floored rather than exact.
        /// </summary>
        public IReadOnlyList<int> ItemLevelDependent { get; private set; }

        /// <summary>Property funcs reached that this port does not implement. Func 9 only.</summary>
        public IReadOnlyList<int> UnsupportedFuncs { get; private set; }

        /// <summary>
        /// True for a crafted item: the record stores its affixes but NOT which cubemain.txt recipe
        /// made it, so the recipe's fixed mods cannot be attributed. The affixes still are.
        /// </summary>
        public bool CraftedRecipeUnknown { get; private set; }

        /// <summary>
        /// The cubemain.txt row the item was crafted from, or -1 when it is not crafted or the
        /// recipe could not be pinned.
        /// </summary>
        public int CraftedRecipe { get; private set; }
    }

    /// <summary>
    /// Rebuilds the property list an item's own sources would have rolled from, applies it at both
    /// ends of every range, and reports the difference.
    ///
    /// The ends come from <see cref="RollEnd"/>: the traced handlers are run twice, unchanged, so a
    /// span is whatever the real code produces at each end rather than an arithmetic guess. That is
    /// also why an unimplemented func or an absent item level degrades into a report instead of a
    /// wrong number.
    /// </summary>
    internal sealed class RolledRangeReconstructor
    {
        private const int StatDefense = 31;

        private readonly D2DataFiles _data;
        private readonly ItemTable _items;
        private readonly ItemTypeTree _types;
        private readonly MagicAffixTable _affixes;
        private readonly SetTable _sets;
        private readonly PropertyGroupsTable _groups;

        public RolledRangeReconstructor(
            D2DataFiles data,
            ItemTable items,
            ItemTypeTree types,
            MagicAffixTable affixes,
            SetTable sets)
        {
            _data = data;
            _items = items;
            _types = types;
            _affixes = affixes;
            _sets = sets;
            _groups = new PropertyGroupsTable(
                data.PropertyGroups, new PropertiesTable(data.Properties, data.ItemStatCost),
                data.ParamLinker);
        }

        /// <summary>
        /// One gathered property and the source that contributed it. A non-null
        /// <see cref="Seed"/> marks a choice instead, and <see cref="Property"/> is then unused.
        /// </summary>
        private struct Sourced
        {
            public ItemProperty Property;
            public RollSources Source;
            public ChoiceSeed Seed;

            /// <summary>
            /// D2R: a set item's tier mod (aprop with `add func` != 0). sub_1402866E0 case 4 writes it
            /// to a STATE_ITEMSET list, states 165..169 with flags 0x2040 (0x140286a18-0x140286a68),
            /// so it is not one of the item's own stats. It still reaches func 2 with pItem = the item,
            /// which is why it keeps maximising the base Defense. Once its tier is earned the list
            /// counts toward the drawn Defense, so it joins that span (and only that one).
            /// </summary>
            public bool MaximiseOnly;

            /// <summary>
            /// The STATE_ITEMSET list a <see cref="MaximiseOnly"/> tier mod lands in: aprop N goes to
            /// state 164 + N (`dword_141641400[k>>1]`, 0x140286a33).
            /// </summary>
            public int TierState;
        }

        /// <summary>A group cell (outer min/max = the count) or a func-25 property.</summary>
        private sealed class ChoiceSeed
        {
            public int GroupRow;
            public int Min;
            public int Max;
            public bool IsStatPick;
            public ItemProperty StatPick;
        }

        private const int OptionProperty = 0;
        private const int OptionNested = 1;
        private const int OptionStat = 2;

        private sealed class OptionModel
        {
            public int Kind;
            public int PropertyId;
            public int ModMin;
            public int ModMax;
            public int StatId;
            public int StatSet;
            public ItemProperty StatProperty;
            public ChoiceNode Nested;
            public int ParamLow;
            public int ParamHigh;
        }

        private sealed class Pick
        {
            public int Option;
            public int Param;
            public List<Pick> Nested;
        }

        private sealed class ChoiceNode
        {
            public RolledChoice Public;
            public RollSources Sources;
            public bool Unsupported;
            public readonly List<OptionModel> Options = new List<OptionModel>();
            public List<List<Pick>> Outcomes = new List<List<Pick>>();
            public readonly HashSet<int> Keys = new HashSet<int>();
        }

        private const int MaxParamSpan = 64;
        private const int MaxOutcomesPerChoice = 1024;
        private const int MaxCombos = 4096;

        public ItemRollRanges Reconstruct(
            ItemIdentity item,
            IDictionary<int, int> recorded,
            IEnumerable<ItemProperty> socketProperties,
            IEnumerable<int> earnedSetIds)
        {
            return Reconstruct(item, recorded, socketProperties, earnedSetIds, true);
        }

        /// <summary>
        /// <paramref name="includeBaseDefense"/> false drops the armour's own `minac`..`maxac` roll,
        /// leaving the item's MODIFIERS alone. The Defense SECTION draws the base plus every
        /// modifier and wants it; a `+45 Defense` modifier line draws its own contribution and does
        /// not â with it, that line was offered the section's span.
        ///
        /// <paramref name="includeOwnSources"/> false applies ONLY
        /// <paramref name="socketProperties"/> — no affixes, no unique row, nothing of the item's
        /// own. That is what a socket-only view needs: asking for "just the fillers" while the
        /// identity's own sources were folded in silently gave a gem's line the HOST's affix span.
        ///
        /// <paramref name="earnedTierStates"/> are the set-tier states (165..169) whose list no
        /// longer carries STATLIST_SET: ITEMS_RecalculateSetItemSpecificMods 0x14028ab90 clears the
        /// bit once the tier is earned, and the Defense line then counts that list. They join the
        /// Defense span only; their other stats are drawn in the tier block.
        ///
        /// <paramref name="viewerStat"/> is the unit the Defense line attaches the item to
        /// (STATLIST_MergeStatLists 0x1401d1df1), which re-runs ops 4/5 against it.
        /// </summary>
        public ItemRollRanges Reconstruct(
            ItemIdentity item,
            IDictionary<int, int> recorded,
            IEnumerable<ItemProperty> socketProperties,
            IEnumerable<int> earnedSetIds,
            bool includeOwnSources,
            bool includeBaseDefense = true,
            ICollection<int> earnedTierStates = null,
            Func<int, int> viewerStat = null)
        {
            var gathered = new List<Sourced>();

            // -1 unless the item is crafted AND its recipe was pinned. A socket-only pass never
            // gathers the item's own sources, so it leaves this untouched.
            int craftedRecipe = -1;

            // A PropertyApplier is needed before gathering, because every source stores property
            // CODES and only the table can turn one into an id.
            var low = new PropertyApplier(_data, _items, _types);
            var high = new PropertyApplier(_data, _items, _types, RollEnd.High);

            if (includeOwnSources)
            {
                gathered.AddRange(Gather(item, low.Properties, earnedSetIds));
                craftedRecipe = GatherCrafted(item, low, gathered, recorded);
            }

            if (socketProperties != null)
            {
                foreach (ItemProperty property in socketProperties)
                {
                    Add(gathered, property, RollSources.Socket);
                }
            }

            var lowStats = new Dictionary<int, int>();
            var highStats = new Dictionary<int, int>();
            var sourceOf = new Dictionary<int, RollSources>();
            var layerVaries = new List<RolledLayerRange>();
            var choices = new List<ChoiceNode>();
            var tierLow = new Dictionary<int, int>();
            var tierHigh = new Dictionary<int, int>();

            foreach (Sourced entry in gathered)
            {
                if (entry.MaximiseOnly)
                {
                    if (earnedTierStates != null && entry.Seed == null
                        && earnedTierStates.Contains(entry.TierState)
                        && !RollsTheLayer(low.Properties, entry.Property.PropertyId))
                    {
                        low.Apply(PropertyApplier.PropModeGem, item, entry.Property, tierLow);
                        high.Apply(PropertyApplier.PropModeGem, item, entry.Property, tierHigh);
                    }

                    continue;
                }

                if (entry.Seed != null)
                {
                    choices.Add(BuildChoice(low, high, item, entry.Seed, entry.Source));
                    continue;
                }

                // A layer-rolling property is pulled out BEFORE the combined application, because
                // summing it into the totals would add one arbitrary layer's value to them.
                if (RollsTheLayer(low.Properties, entry.Property.PropertyId))
                {
                    AddLayerRange(low, high, item, entry, layerVaries);
                    continue;
                }

                low.Apply(PropertyApplier.PropModeGem, item, entry.Property, lowStats);
                high.Apply(PropertyApplier.PropModeGem, item, entry.Property, highStats);

                // Attribution runs into scratch lists so one property's keys can be told apart from
                // the combined totals. BOTH ends are scanned: a property whose low end truncates to
                // nothing still writes at its high end, and attributing only the low one left those
                // stats sourceless.
                Attribute(low, item, entry, sourceOf);
                Attribute(high, item, entry, sourceOf);
            }

            List<int[]> combos = Combos(choices);
            HashSet<int> contested = ContestedKeys(choices);
            bool maximisedByG = MaximisesBaseDefense(gathered, low.Properties, _data.IsResurrected);

            var lows = new List<Dictionary<int, int>>(combos.Count);
            var highs = new List<Dictionary<int, int>>(combos.Count);
            var sources = new List<Dictionary<int, RollSources>>(combos.Count);

            foreach (int[] combo in combos)
            {
                var lowX = new Dictionary<int, int>(lowStats);
                var highX = new Dictionary<int, int>(highStats);
                var sourceX = new Dictionary<int, RollSources>(sourceOf);
                bool maximised = maximisedByG;

                for (int c = 0; c < choices.Count; ++c)
                {
                    ChoiceNode node = choices[c];
                    foreach (Pick pick in node.Outcomes[combo[c]])
                    {
                        ApplyPick(low, item, node, pick, lowX);
                        ApplyPick(high, item, node, pick, highX);
                        AttributePick(low, high, item, node, pick, sourceX);
                        maximised |= !(_data.IsResurrected && FillerAssigned(node.Sources))
                                     && PickWritesStat(low.Properties, node, pick, StatArmorPercent);
                    }
                }

                // Gated with the rest of the item's own sources: the base armour roll IS one, so a
                // socket-only reconstruction that added it gave a gem block the HOST's base span —
                // "+30 Defense [33-35]" where 33-35 was the cap's 3..5 plus the rune's fixed 30.
                //
                // The BASE view at each end is kept apart from the merged one because op 13 consumes
                // the two separately (STATLIST_LookupBaseStatWithMinAccr 0x624ed0 reads `Stats`, the
                // result lands in FullStats at 0x625158). Only Defense rolls a base.
                var lowBase = new Dictionary<int, int>();
                var highBase = new Dictionary<int, int>();
                bool defenseSection = includeOwnSources && includeBaseDefense;
                if (defenseSection)
                {
                    AddBaseDefense(item, lowX, highX, lowBase, highBase, sourceX, maximised);
                }

                // Taken before op 13 resolves lowX, because the drawn Defense resolves its own copy.
                int defenseLow = 0;
                int defenseHigh = 0;
                bool drawnDefense = false;
                if (defenseSection && (tierLow.Count != 0 || tierHigh.Count != 0 || viewerStat != null))
                {
                    drawnDefense = DrawnDefense(lowX, tierLow, lowBase, viewerStat, out defenseLow);
                    drawnDefense |= DrawnDefense(highX, tierHigh, highBase, viewerStat, out defenseHigh);
                }

                // The Defense line draws the OP-RESOLVED value, so its span has to be resolved
                // too. A Large Shield rolling 12..14 under +150% Enhanced Defense prints 32, which
                // the unresolved 12..14 can never contain.
                ResolveBaseOps(lowX, lowBase);
                ResolveBaseOps(highX, highBase);

                if (drawnDefense)
                {
                    int defenseKey = ItemStatReader.PackStatKey(0, StatDefense);
                    lowX[defenseKey] = defenseLow;
                    highX[defenseKey] = defenseHigh;
                    if (tierLow.ContainsKey(defenseKey) || tierHigh.ContainsKey(defenseKey))
                    {
                        RollSources had;
                        sourceX[defenseKey] = sourceX.TryGetValue(defenseKey, out had)
                            ? had | RollSources.SetItem
                            : RollSources.SetItem;
                    }
                }

                lows.Add(lowX);
                highs.Add(highX);
                sources.Add(sourceX);
            }

            var outside = new SortedSet<int>();
            List<int> kept = Resolve(choices, combos, lows, highs, contested, recorded, outside);

            var stats = new List<RolledStatRange>();
            CollectRanges(kept, lows, highs, sources, stats, _data.ItemStatCost);

            stats.Sort(CompareRanges);
            layerVaries.Sort(CompareLayerRanges);

            foreach (int stat in OutOfRange(stats, recorded))
            {
                outside.Add(stat);
            }

            var publicChoices = new List<RolledChoice>(choices.Count);
            foreach (ChoiceNode node in choices)
            {
                publicChoices.Add(node.Public);
            }

            return new ItemRollRanges(
                stats,
                layerVaries,
                publicChoices,
                new List<int>(outside),
                Unattributed(kept, lows, highs, layerVaries, recorded),
                Merge(low.ItemLevelDependent, high.ItemLevelDependent),
                Merge(low.UnsupportedFunc, high.UnsupportedFunc),
                item.Quality == (int)ItemQuality.Crafted && craftedRecipe < 0,
                craftedRecipe);
        }

        /// <summary>
        /// A group cell becomes one node per sub_14028A190 0x14028a190 dispatch; its outcomes are
        /// the subsets the pick mode can apply, times each applied entry's independently rolled param
        /// (0x14028a34b..0x14028a3c7). A func-25 property becomes a one-of-K node over its target
        /// row's stats (0x140289e71).
        /// </summary>
        private ChoiceNode BuildChoice(
            PropertyApplier low, PropertyApplier high, ItemIdentity item, ChoiceSeed seed,
            RollSources source)
        {
            RollSources sources = source | RollSources.PropertyGroup;
            return seed.IsStatPick
                ? BuildStatPick(low, high, seed.StatPick, sources)
                : BuildGroup(low, high, item, seed.GroupRow, seed.Min, seed.Max, sources);
        }

        private ChoiceNode BuildGroup(
            PropertyApplier low, PropertyApplier high, ItemIdentity item, int groupRow, int min,
            int max, RollSources sources)
        {
            PropertyGroupsTable.Row group = _groups[groupRow];
            var node = new ChoiceNode();
            node.Sources = sources;

            var options = new List<RolledChoiceOption>();

            for (int k = 0; k < PropertyGroupsTable.EntriesPerGroup; ++k)
            {
                PropertyGroupsTable.Entry entry = group.Entries[k];

                // 0x14028a038: any entry whose row is non-negative joins the list, nested included.
                if (entry.Prop.Row < 0)
                {
                    continue;
                }

                var model = new OptionModel();
                model.ParamLow = Math.Min(entry.ParMin, entry.ParMax);
                model.ParamHigh = Math.Max(entry.ParMin, entry.ParMax);
                model.ModMin = entry.ModMin;
                model.ModMax = entry.ModMax;

                var variants = new List<RolledChoiceVariant>();
                RolledChoice nested = null;
                string code;

                if (entry.Prop.Kind == PropertyRef.KindGroup)
                {
                    // A nested entry's param is rolled but unused, and its modMin..modMax is the
                    // nested count; one param stands for all of them.
                    model.Kind = OptionNested;
                    model.PropertyId = -1;
                    model.ParamHigh = model.ParamLow;
                    model.Nested = BuildGroup(
                        low, high, item, entry.Prop.Row, entry.ModMin, entry.ModMax, sources);
                    node.Unsupported |= model.Nested.Unsupported;
                    nested = model.Nested.Public;
                    code = _groups[entry.Prop.Row].Code;
                }
                else
                {
                    model.Kind = OptionProperty;
                    model.PropertyId = entry.Prop.Row;
                    PropertiesTable.Row row = low.Properties[entry.Prop.Row];
                    code = row == null ? string.Empty : row.Code;

                    if (RollsTheLayer(low.Properties, model.PropertyId)
                        || HasFunc(low.Properties, model.PropertyId, StatPickFunc)
                        || (long)model.ParamHigh - model.ParamLow + 1 > MaxParamSpan)
                    {
                        node.Unsupported = true;
                    }
                    else
                    {
                        for (int param = model.ParamLow; param <= model.ParamHigh; ++param)
                        {
                            var property = new ItemProperty();
                            property.PropertyId = model.PropertyId;
                            property.Param = param;
                            property.Min = model.ModMin;
                            property.Max = model.ModMax;

                            var atLow = new Dictionary<int, int>();
                            var atHigh = new Dictionary<int, int>();
                            low.Apply(PropertyApplier.PropModeGem, item, property, atLow);
                            high.Apply(PropertyApplier.PropModeGem, item, property, atHigh);
                            variants.Add(new RolledChoiceVariant(
                                param, VariantRanges(atLow, atHigh, sources, node.Keys)));
                        }
                    }
                }

                node.Options.Add(model);
                options.Add(new RolledChoiceOption(
                    k + 1, model.PropertyId, code, entry.Weight, model.ParamLow, model.ParamHigh,
                    entry.ModMin, entry.ModMax, variants, nested));
            }

            int countLow;
            int countHigh;
            switch (group.PickMode)
            {
                case 0:
                    countLow = countHigh = node.Options.Count;
                    break;
                case 1:
                case 2:
                    // 0x14028a738..0x14028a753: equal ends floor at 1, otherwise the seed rolls N
                    // over the normalised pair.
                    if (min == max)
                    {
                        countLow = countHigh = Math.Max(min, 1);
                    }
                    else
                    {
                        countLow = Math.Min(min, max);
                        countHigh = Math.Max(min, max);
                    }

                    break;
                default:
                    countLow = countHigh = 0;
                    break;
            }

            node.Public = new RolledChoice(
                groupRow, group.Code, sources, (ChoicePickMode)group.PickMode, countLow, countHigh,
                options);

            if (!node.Unsupported)
            {
                node.Outcomes = GroupOutcomes(node, group.PickMode, countLow, countHigh);
                if (node.Outcomes == null)
                {
                    node.Unsupported = true;
                }
            }

            if (node.Unsupported)
            {
                node.Outcomes = new List<List<Pick>> { new List<Pick>() };
                node.Keys.Clear();
            }

            return node;
        }

        /// <summary>
        /// Which entry subsets a pick mode can apply. pickmode 1 removes each pick (0x14028a8e7),
        /// so N picks give a subset of min(N, K); a negative N passes the unsigned loop test
        /// (0x14028a8ff) and drains the list. pickmode 2 keeps the drawn slot (0x14028ab19), so N
        /// draws give any non-empty subset of up to min(N, K). Null when the count exceeds the cap.
        /// </summary>
        private static List<List<Pick>> GroupOutcomes(
            ChoiceNode node, int pickMode, int countLow, int countHigh)
        {
            int k = node.Options.Count;
            var sizes = new bool[k + 1];

            if (k == 0 || pickMode >= 3)
            {
                sizes[0] = true;
            }
            else if (pickMode == 0)
            {
                sizes[k] = true;
            }
            else
            {
                if (countLow <= 0 && countHigh >= 0)
                {
                    sizes[0] = true;
                }

                if (pickMode == 1)
                {
                    if (countLow < 0)
                    {
                        sizes[k] = true;
                    }

                    for (long n = Math.Max(countLow, 1); n <= countHigh && n <= k; ++n)
                    {
                        sizes[n] = true;
                    }

                    if (countHigh > k)
                    {
                        sizes[k] = true;
                    }
                }
                else
                {
                    int top = countLow < 0 ? k : Math.Min(countHigh, k);
                    for (int n = 1; n <= top; ++n)
                    {
                        sizes[n] = true;
                    }
                }
            }

            var subsets = new List<int[]>();
            for (int size = 0; size <= k; ++size)
            {
                if (sizes[size])
                {
                    AddCombinations(k, size, subsets);
                }
            }

            long total = 0;
            foreach (int[] subset in subsets)
            {
                long product = 1;
                foreach (int option in subset)
                {
                    product *= OptionOutcomeCount(node.Options[option]);
                    if (product > MaxOutcomesPerChoice)
                    {
                        return null;
                    }
                }

                total += product;
                if (total > MaxOutcomesPerChoice)
                {
                    return null;
                }
            }

            var outcomes = new List<List<Pick>>();
            foreach (int[] subset in subsets)
            {
                Expand(node, subset, 0, new List<Pick>(), outcomes);
            }

            return outcomes;
        }

        private static long OptionOutcomeCount(OptionModel option)
        {
            return option.Kind == OptionNested
                ? option.Nested.Outcomes.Count
                : (long)option.ParamHigh - option.ParamLow + 1;
        }

        private static void AddCombinations(int k, int size, List<int[]> into)
        {
            var current = new int[size];
            for (int i = 0; i < size; ++i)
            {
                current[i] = i;
            }

            while (true)
            {
                into.Add((int[])current.Clone());

                int at = size - 1;
                while (at >= 0 && current[at] == k - size + at)
                {
                    --at;
                }

                if (at < 0)
                {
                    return;
                }

                ++current[at];
                for (int i = at + 1; i < size; ++i)
                {
                    current[i] = current[i - 1] + 1;
                }
            }
        }

        private static void Expand(
            ChoiceNode node, int[] subset, int index, List<Pick> prefix, List<List<Pick>> into)
        {
            if (index == subset.Length)
            {
                into.Add(new List<Pick>(prefix));
                return;
            }

            OptionModel option = node.Options[subset[index]];

            if (option.Kind == OptionNested)
            {
                foreach (List<Pick> inner in option.Nested.Outcomes)
                {
                    prefix.Add(new Pick { Option = subset[index], Param = option.ParamLow, Nested = inner });
                    Expand(node, subset, index + 1, prefix, into);
                    prefix.RemoveAt(prefix.Count - 1);
                }

                return;
            }

            for (int param = option.ParamLow; param <= option.ParamHigh; ++param)
            {
                prefix.Add(new Pick { Option = subset[index], Param = param });
                Expand(node, subset, index + 1, prefix, into);
                prefix.RemoveAt(prefix.Count - 1);
            }
        }

        private ChoiceNode BuildStatPick(
            PropertyApplier low, PropertyApplier high, ItemProperty property, RollSources sources)
        {
            PropertiesTable.Row row = low.Properties[property.PropertyId];
            PropertiesTable.Row target = low.Properties[property.Param];

            var node = new ChoiceNode();
            node.Sources = sources;

            int statSet = 0;
            for (int set = 0; set < PropertiesTable.SetsPerProperty; ++set)
            {
                if (row.Func[set] == StatPickFunc)
                {
                    statSet = row.Set[set];
                    break;
                }
            }

            var options = new List<RolledChoiceOption>();
            for (int set = 0; set < PropertiesTable.SetsPerProperty; ++set)
            {
                int stat = target.Stat[set];
                if (!IsStatPickCandidate(stat))
                {
                    continue;
                }

                var model = new OptionModel();
                model.Kind = OptionStat;
                model.PropertyId = property.Param;
                model.StatId = stat;
                model.StatSet = statSet;
                model.StatProperty = property;
                model.ParamLow = property.Param;
                model.ParamHigh = property.Param;

                var atLow = new Dictionary<int, int>();
                var atHigh = new Dictionary<int, int>();
                low.ApplyStatPick(statSet, stat, property, atLow);
                high.ApplyStatPick(statSet, stat, property, atHigh);

                node.Options.Add(model);
                node.Outcomes.Add(new List<Pick> { new Pick { Option = options.Count, Param = property.Param } });
                options.Add(new RolledChoiceOption(
                    set + 1, property.Param, target.Code, 1, property.Param, property.Param,
                    property.Min, property.Max,
                    new List<RolledChoiceVariant>
                    {
                        new RolledChoiceVariant(
                            property.Param, VariantRanges(atLow, atHigh, sources, node.Keys)),
                    },
                    null));
            }

            node.Public = new RolledChoice(
                -1, row.Code, sources, ChoicePickMode.StatPick, 1, 1, options);
            return node;
        }

        private const int StatPickFunc = 25;

        // 0x140289e71: `0 < stat < ItemStatCostCount`, so strength (0) is never a candidate.
        private bool IsStatPickCandidate(int stat)
        {
            return stat > 0 && _data.ItemStatCost.TryGetStat(stat, out _);
        }

        /// <summary>
        /// Whether func 25 would write anything: a param inside Properties.txt
        /// (0x140289dc5..0x140289e0c) whose row carries a valid stat.
        /// </summary>
        private bool CanBuildStatPick(PropertiesTable properties, int param)
        {
            PropertiesTable.Row target = properties[param];
            if (target == null)
            {
                return false;
            }

            foreach (int stat in target.Stat)
            {
                if (IsStatPickCandidate(stat))
                {
                    return true;
                }
            }

            return false;
        }

        private List<RolledStatRange> VariantRanges(
            Dictionary<int, int> atLow, Dictionary<int, int> atHigh, RollSources sources,
            HashSet<int> keys)
        {
            var sourceOf = new Dictionary<int, RollSources>();
            foreach (int key in atLow.Keys)
            {
                sourceOf[key] = sources;
                keys.Add(key);
            }

            foreach (int key in atHigh.Keys)
            {
                sourceOf[key] = sources;
                keys.Add(key);
            }

            var ranges = new List<RolledStatRange>();
            CollectRanges(atLow, atHigh, sourceOf, ranges, _data.ItemStatCost);
            ranges.Sort(CompareRanges);
            return ranges;
        }

        private static void ApplyPick(
            PropertyApplier applier, ItemIdentity item, ChoiceNode node, Pick pick,
            IDictionary<int, int> into)
        {
            OptionModel option = node.Options[pick.Option];
            switch (option.Kind)
            {
                case OptionNested:
                    foreach (Pick inner in pick.Nested)
                    {
                        ApplyPick(applier, item, option.Nested, inner, into);
                    }

                    break;
                case OptionStat:
                    applier.ApplyStatPick(option.StatSet, option.StatId, option.StatProperty, into);
                    break;
                default:
                    var property = new ItemProperty();
                    property.PropertyId = option.PropertyId;
                    property.Param = pick.Param;
                    property.Min = option.ModMin;
                    property.Max = option.ModMax;
                    applier.Apply(PropertyApplier.PropModeGem, item, property, into);
                    break;
            }
        }

        private static void AttributePick(
            PropertyApplier low, PropertyApplier high, ItemIdentity item, ChoiceNode node,
            Pick pick, Dictionary<int, RollSources> sourceOf)
        {
            var scratch = new Dictionary<int, int>();
            ApplyPick(low, item, node, pick, scratch);
            ApplyPick(high, item, node, pick, scratch);

            foreach (int key in scratch.Keys)
            {
                RollSources existing;
                sourceOf[key] = sourceOf.TryGetValue(key, out existing)
                    ? existing | node.Sources
                    : node.Sources;
            }
        }

        private static bool PickWritesStat(
            PropertiesTable properties, ChoiceNode node, Pick pick, int statId)
        {
            OptionModel option = node.Options[pick.Option];
            switch (option.Kind)
            {
                case OptionNested:
                    foreach (Pick inner in pick.Nested)
                    {
                        if (PickWritesStat(properties, option.Nested, inner, statId))
                        {
                            return true;
                        }
                    }

                    return false;
                case OptionStat:
                    return option.StatId == statId;
                default:
                    PropertiesTable.Row row = properties.RowAt(option.PropertyId);
                    return row != null && Array.IndexOf(row.Stat, statId) >= 0;
            }
        }

        /// <summary>
        /// The cartesian product of every choice's outcomes, first choice slowest. Past the cap
        /// every choice is demoted to unsupported and the item is reconstructed without them.
        /// </summary>
        private static List<int[]> Combos(List<ChoiceNode> choices)
        {
            long total = 1;
            foreach (ChoiceNode node in choices)
            {
                total *= node.Outcomes.Count;
                if (total > MaxCombos)
                {
                    break;
                }
            }

            if (total > MaxCombos)
            {
                foreach (ChoiceNode node in choices)
                {
                    node.Unsupported = true;
                    node.Outcomes = new List<List<Pick>> { new List<Pick>() };
                    node.Keys.Clear();
                }

                total = 1;
            }

            var combos = new List<int[]>((int)total);
            var current = new int[choices.Count];

            while (true)
            {
                combos.Add((int[])current.Clone());

                int at = choices.Count - 1;
                while (at >= 0 && current[at] == choices[at].Outcomes.Count - 1)
                {
                    current[at] = 0;
                    --at;
                }

                if (at < 0)
                {
                    return combos;
                }

                ++current[at];
            }
        }

        private static HashSet<int> ContestedKeys(List<ChoiceNode> choices)
        {
            var contested = new HashSet<int>();
            foreach (ChoiceNode node in choices)
            {
                AddKeys(node, contested);
            }

            return contested;
        }

        private static void AddKeys(ChoiceNode node, HashSet<int> into)
        {
            if (node.Unsupported)
            {
                return;
            }

            into.UnionWith(node.Keys);
            foreach (OptionModel option in node.Options)
            {
                if (option.Nested != null)
                {
                    AddKeys(option.Nested, into);
                }
            }
        }

        /// <summary>
        /// Keeps the combinations consistent with the record over the CONTESTED keys only, so a
        /// defect elsewhere in the reconstruction cannot flip a choice. Returns the kept
        /// combination indices and fills in each choice's resolution.
        /// </summary>
        private static List<int> Resolve(
            List<ChoiceNode> choices,
            List<int[]> combos,
            List<Dictionary<int, int>> lows,
            List<Dictionary<int, int>> highs,
            HashSet<int> contested,
            IDictionary<int, int> recorded,
            SortedSet<int> outside)
        {
            var all = new List<int>();
            for (int i = 0; i < combos.Count; ++i)
            {
                all.Add(i);
            }

            if (recorded == null)
            {
                Report(choices, combos, all, ChoiceResolution.NoRecord);
                return all;
            }

            var kept = new List<int>();
            for (int i = 0; i < combos.Count; ++i)
            {
                if (Consistent(lows[i], highs[i], contested, recorded))
                {
                    kept.Add(i);
                }
            }

            if (kept.Count == 0)
            {
                Report(choices, combos, all, ChoiceResolution.Contradicted);
                foreach (int key in contested)
                {
                    if (recorded.ContainsKey(key))
                    {
                        outside.Add(ItemStatReader.StatFromKey(key));
                    }
                }

                return all;
            }

            Report(choices, combos, kept, ChoiceResolution.Resolved);
            return kept;
        }

        private static bool Consistent(
            Dictionary<int, int> lowX,
            Dictionary<int, int> highX,
            HashSet<int> contested,
            IDictionary<int, int> recorded)
        {
            foreach (int key in contested)
            {
                int a;
                int b;
                bool written = lowX.TryGetValue(key, out a) | highX.TryGetValue(key, out b);
                int lo = Math.Min(a, b);
                int hi = Math.Max(a, b);

                int value;
                if (recorded.TryGetValue(key, out value))
                {
                    if (!written || value < lo || value > hi)
                    {
                        return false;
                    }
                }
                else if (written && (lo > 0 || hi < 0))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// <paramref name="resolution"/> Resolved means "decide per choice": one distinct outcome
        /// is Resolved, several Ambiguous.
        /// </summary>
        private static void Report(
            List<ChoiceNode> choices, List<int[]> combos, List<int> kept,
            ChoiceResolution resolution)
        {
            for (int c = 0; c < choices.Count; ++c)
            {
                ChoiceNode node = choices[c];
                if (node.Unsupported)
                {
                    MarkUnsupported(node);
                    continue;
                }

                var indices = new SortedSet<int>();
                foreach (int i in kept)
                {
                    indices.Add(combos[i][c]);
                }

                var seen = new HashSet<string>();
                var consistent = new List<IReadOnlyList<RolledChoicePick>>();
                foreach (int index in indices)
                {
                    List<Pick> outcome = node.Outcomes[index];
                    if (seen.Add(OutcomeKey(outcome)))
                    {
                        consistent.Add(PublicPicks(outcome));
                    }
                }

                node.Public.Consistent = consistent;
                node.Public.Resolution = resolution != ChoiceResolution.Resolved
                    ? resolution
                    : (consistent.Count == 1 ? ChoiceResolution.Resolved : ChoiceResolution.Ambiguous);

                foreach (OptionModel option in node.Options)
                {
                    if (option.Nested != null)
                    {
                        ReportNested(option.Nested);
                    }
                }
            }
        }

        private static void ReportNested(ChoiceNode node)
        {
            if (node.Unsupported)
            {
                MarkUnsupported(node);
                return;
            }

            var seen = new HashSet<string>();
            var consistent = new List<IReadOnlyList<RolledChoicePick>>();
            foreach (List<Pick> outcome in node.Outcomes)
            {
                if (seen.Add(OutcomeKey(outcome)))
                {
                    consistent.Add(PublicPicks(outcome));
                }
            }

            node.Public.Consistent = consistent;
            node.Public.Resolution = ChoiceResolution.NoRecord;

            foreach (OptionModel option in node.Options)
            {
                if (option.Nested != null)
                {
                    ReportNested(option.Nested);
                }
            }
        }

        private static void MarkUnsupported(ChoiceNode node)
        {
            node.Public.Resolution = ChoiceResolution.Unsupported;
            node.Public.Consistent = new List<IReadOnlyList<RolledChoicePick>>();

            foreach (OptionModel option in node.Options)
            {
                if (option.Nested != null)
                {
                    MarkUnsupported(option.Nested);
                }
            }
        }

        private static string OutcomeKey(List<Pick> outcome)
        {
            var parts = new List<string>(outcome.Count);
            foreach (Pick pick in outcome)
            {
                parts.Add(pick.Option + ":" + pick.Param);
            }

            return string.Join(",", parts);
        }

        private static IReadOnlyList<RolledChoicePick> PublicPicks(List<Pick> outcome)
        {
            var picks = new List<RolledChoicePick>(outcome.Count);
            foreach (Pick pick in outcome)
            {
                picks.Add(new RolledChoicePick(pick.Option, pick.Param));
            }

            return picks;
        }

        /// <summary>
        /// A key gets a span only if EVERY kept combination writes it; the span is the union of
        /// each combination's own normalised span, and the sources the union of theirs.
        /// </summary>
        private static void CollectRanges(
            List<int> kept,
            List<Dictionary<int, int>> lows,
            List<Dictionary<int, int>> highs,
            List<Dictionary<int, RollSources>> sources,
            List<RolledStatRange> stats,
            IItemStatCostTable statCost)
        {
            if (kept.Count == 1)
            {
                int only = kept[0];
                CollectRanges(lows[only], highs[only], sources[only], stats, statCost);
                return;
            }

            var keys = new SortedSet<int>();
            foreach (int i in kept)
            {
                keys.UnionWith(lows[i].Keys);
                keys.UnionWith(highs[i].Keys);
            }

            foreach (int key in keys)
            {
                bool everywhere = true;
                int low = int.MaxValue;
                int high = int.MinValue;
                RollSources from = RollSources.None;

                foreach (int i in kept)
                {
                    int a;
                    int b;
                    if (!(lows[i].TryGetValue(key, out a) | highs[i].TryGetValue(key, out b)))
                    {
                        everywhere = false;
                        break;
                    }

                    low = Math.Min(low, Math.Min(a, b));
                    high = Math.Max(high, Math.Max(a, b));

                    RollSources had;
                    if (sources[i].TryGetValue(key, out had))
                    {
                        from |= had;
                    }
                }

                if (!everywhere)
                {
                    continue;
                }

                var lowOnly = new Dictionary<int, int> { { key, low } };
                var highOnly = new Dictionary<int, int> { { key, high } };
                var sourceOnly = new Dictionary<int, RollSources> { { key, from } };
                CollectRanges(lowOnly, highOnly, sourceOnly, stats, statCost);
            }
        }

        /// <summary>
        /// Every property the item's OWN sources contribute. Exposed so a caller can fold a socket
        /// filler that carries its own affixes — a jewel — into the host's spans, which is what the
        /// merged render needs: the line it draws is the SUM of both, so the span must be too.
        /// </summary>
        public IReadOnlyList<ItemProperty> OwnProperties(ItemIdentity item)
        {
            var applier = new PropertyApplier(_data, _items, _types);

            var properties = new List<ItemProperty>();

            // No crafted recipe: this overload's caller folds a socket filler into a host, and no
            // filler is crafted. Nor does a filler carry a choice: the group prefixes are `lcha`
            // and `tors` only.
            foreach (Sourced entry in Gather(item, applier.Properties, null))
            {
                if (entry.Seed == null && !entry.MaximiseOnly)
                {
                    properties.Add(entry.Property);
                }
            }

            return properties;
        }

        private List<Sourced> Gather(
            ItemIdentity item,
            PropertiesTable properties,
            IEnumerable<int> earnedSetIds)
        {
            var gathered = new List<Sourced>();

            // A runeword's MagicPrefix[0] is a string id, not an affix id, so the two are mutually
            // exclusive rather than additive.
            if (item.Has(ItemRecordFlags.Runeword))
            {
                GatherRuneword(item, properties, gathered);
            }
            else
            {
                GatherAffixes(item, properties, gathered);
            }

            GatherUnique(item, properties, gathered);
            GatherSetItem(item, properties, gathered);
            GatherSetBonuses(earnedSetIds, gathered);
            GatherSuperior(item, properties, gathered);

            return gathered;
        }

        /// <summary>
        /// A crafted item's recipe is not in its record, but it is deducible from the shape of
        /// cubemain.txt: the 36 crafted rows are **four families over nine equipment slots**, with
        /// exactly one row per (family, slot). The output cell is `usetype,crf` — the crafted item
        /// keeps the input's type — so the recipe's slot is the item's own slot, which narrows the
        /// field to four. <see cref="PickByRecordedStats"/> then keeps the one candidate EVERY stat
        /// of which the record carries.
        ///
        /// Matching on the SLOT rather than on `input 1`'s exact base code is deliberate. That cell
        /// is not a plain item code — four of the 36 name an item TYPE (`blun`, `axe`, `rod`,
        /// `spea`), `amul` and `ring` are types with no item of that code at all, and 24 carry a
        /// trailing `upg`. How the cube resolves it is not traced here, and it does not need to be:
        /// whatever it accepts, the accepted item is in the recipe's slot, and the slot is all this
        /// needs.
        ///
        /// Returns the cubemain row, or -1 when no recipe could be pinned.
        /// </summary>
        private int GatherCrafted(
            ItemIdentity item,
            PropertyApplier low,
            List<Sourced> gathered,
            IDictionary<int, int> recorded)
        {
            if (item.Quality != (int)ItemQuality.Crafted || _data.CubeMain == null)
            {
                return -1;
            }

            int slot = CraftSlotOf(item.ClassId);
            if (slot < 0)
            {
                return -1;
            }

            var candidates = new List<int>();
            for (int row = 0; row < _data.CubeMain.RowCount; ++row)
            {
                if (IsCraftedRecipe(row) && RecipeSlot(row) == slot)
                {
                    candidates.Add(row);
                }
            }

            int chosen = PickByRecordedStats(item, low, candidates, recorded);
            if (chosen < 0)
            {
                return -1;
            }

            AddRecipeMods(low.Properties, gathered, chosen);
            return chosen;
        }

        private const int CraftedModsPerRecipe = 5;

        /// <summary>
        /// The nine slots the crafted recipes cover, as itemtypes.txt codes. Disjoint over the
        /// shipped tree — of the 98 itemtypes rows carrying a code none is under two of them, and
        /// of the 659 items 481 are under one and 178 under none — so the order here is inert. What
        /// it does decide is which shields resolve at all; see <see cref="CraftSlotOf"/>.
        /// </summary>
        private static readonly string[] CraftSlots =
        {
            "helm", "tors", "shie", "glov", "boot", "belt", "amul", "ring", "weap",
        };

        /// <summary>
        /// Index into <see cref="CraftSlots"/>, or -1 for an item no recipe covers.
        ///
        /// -1 for 30 shields, because `shie` is the slot and the class shields hang off `shld`
        /// instead: 15 paladin auric shields (`ashd`) and 15 necromancer voodoo heads (`head`).
        /// That is correct rather than merely harmless, and `shld` would be wrong. The four shield
        /// recipes name `gts`, `spk`, `sml` and `kit` — item codes, none of which is also a type
        /// code — and all twelve items in their ubercode/ultracode chains are plain `shie`. So no
        /// reading of the cell reaches a class shield: not the code, not the code plus its upgrade
        /// tiers, and not the code's own type, since `ashd` and `head` are SIBLINGS of `shie` under
        /// `shld` rather than descendants. Only a grandparent climb would, and that same reading
        /// would have the `crn` helm recipe accept everything under `armo`.
        /// </summary>
        private int CraftSlotOf(int classId)
        {
            int primary = _types.Row(_items.PrimaryTypeCode(classId));
            int secondary = _types.Row(_items.SecondaryTypeCode(classId));

            for (int i = 0; i < CraftSlots.Length; ++i)
            {
                int slot = _types.Row(CraftSlots[i]);
                if (slot >= 0 && _types.IsOfType(primary, secondary, slot))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// The slot a recipe produces, from `input 1`'s first cell. The cell is either an item code
        /// or an item TYPE code, so both are tried — but only to reach the slot, never to decide
        /// whether the cube would accept a particular base.
        /// </summary>
        private int RecipeSlot(int row)
        {
            string spec = _data.CubeMain.GetString(row, "input 1").Replace("\"", string.Empty);
            int comma = spec.IndexOf(',');
            string code = (comma < 0 ? spec : spec.Substring(0, comma)).Trim();

            if (code.Length == 0)
            {
                return -1;
            }

            int classId = _items.ClassIdForCode(code);
            if (classId >= 0)
            {
                return CraftSlotOf(classId);
            }

            int typeRow = _types.Row(code);
            if (typeRow < 0)
            {
                return -1;
            }

            for (int i = 0; i < CraftSlots.Length; ++i)
            {
                int slot = _types.Row(CraftSlots[i]);
                if (slot >= 0 && _types.IsUnder(typeRow, slot))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>Whether this cubemain row produces a crafted item.</summary>
        private bool IsCraftedRecipe(int row)
        {
            foreach (string part in
                _data.CubeMain.GetString(row, "output").Replace("\"", string.Empty).Split(','))
            {
                if (string.Equals(part.Trim(), "crf", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private void AddRecipeMods(PropertiesTable properties, List<Sourced> into, int row)
        {
            for (int mod = 1; mod <= CraftedModsPerRecipe; ++mod)
            {
                AddProperty(
                    properties,
                    into,
                    RollSources.Crafted,
                    _data.CubeMain.GetString(row, "mod " + mod),
                    _data.CubeMain.GetString(row, "mod " + mod + " param"),
                    _data.CubeMain.GetInt(row, "mod " + mod + " min"),
                    _data.CubeMain.GetInt(row, "mod " + mod + " max"));
            }
        }

        /// <summary>
        /// Picks between the four recipes sharing a slot by asking which one's fixed mods the item
        /// actually carries. A recipe's mods always apply — every `mod N chance` cell is blank and
        /// every roll bottoms out at 1 or more, so none can truncate to the nothing a zero value
        /// writes (0x65ea63) — which makes "every stat this recipe writes is recorded" a sound
        /// filter rather than a heuristic.
        ///
        /// Anything other than exactly one survivor leaves the recipe unknown rather than guessed:
        /// the item's own affixes can supply a rival family's stats by chance, and a wrong recipe
        /// would attribute spans to stats that never rolled from it.
        ///
        /// The stat KEYS come from APPLYING each candidate rather than from reading its property
        /// rows, so a mod writing several stats is handled by the same traced code that writes it
        /// for real.
        /// </summary>
        private int PickByRecordedStats(
            ItemIdentity item,
            PropertyApplier low,
            List<int> candidates,
            IDictionary<int, int> recorded)
        {
            if (recorded == null)
            {
                return -1;
            }

            int viable = -1;
            int count = 0;

            // Probing through the CALLER's applier rather than a throwaway one would normally
            // risk a losing candidate polluting ItemLevelDependent or UnsupportedFunc. It cannot
            // here: the 36 crafted rows between them reach only funcs 1, 2, 7, 8 and 11, so no
            // func 9 and no func 14 or 19, and the single func-11 code `gethit-skill` ships max 4,
            // which skips the item-level arm.
            //
            // Probed at the LOW end only, and `dmg%` (func 7) is the one crafted mod whose written
            // stat KEYS depend on the rolled value: EnhancedDamage writes stats 17 and 18 unless
            // `value * maxdam / 100` truncates to 0, where it degrades to the max-damage family
            // instead. The probe can therefore disagree with the real roll only where the two ENDS
            // disagree, which is maxdam of exactly 2 — 35 floors to 0 there and 60 does not. Below
            // that both ends degrade alike and above it neither does, so neither is a hazard. The
            // one `weap` item at 2 is `d33`, not spawnable and of a type no recipe takes.
            foreach (int row in candidates)
            {
                var probe = new List<Sourced>();
                AddRecipeMods(low.Properties, probe, row);

                var scratch = new Dictionary<int, int>();
                foreach (Sourced entry in probe)
                {
                    if (entry.Seed != null)
                    {
                        continue;
                    }

                    low.Apply(PropertyApplier.PropModeGem, item, entry.Property, scratch);
                }

                if (scratch.Count == 0)
                {
                    continue;
                }

                bool all = true;
                foreach (int key in scratch.Keys)
                {
                    if (!recorded.ContainsKey(key))
                    {
                        all = false;
                        break;
                    }
                }

                if (all)
                {
                    viable = row;
                    ++count;
                }
            }

            return count == 1 ? viable : -1;
        }

        /// <summary>
        /// A key written at only ONE end is not an error and not a layer roll: the stat simply
        /// contributes nothing at the other end, because a zero value writes nothing (0x65ea63). So
        /// the absent end is a value of 0. `dmg%` does exactly this — at a low enough roll the
        /// enhanced-damage handler's integer arithmetic truncates to nothing.
        /// </summary>
        private static void CollectRanges(
            Dictionary<int, int> lowStats,
            Dictionary<int, int> highStats,
            Dictionary<int, RollSources> sourceOf,
            List<RolledStatRange> stats,
            IItemStatCostTable statCost)
        {
            var keys = new SortedSet<int>(lowStats.Keys);
            foreach (int key in highStats.Keys)
            {
                keys.Add(key);
            }

            foreach (int key in keys)
            {
                int lowValue;
                lowStats.TryGetValue(key, out lowValue);

                int highValue;
                highStats.TryGetValue(key, out highValue);

                RollSources sources;
                if (!sourceOf.TryGetValue(key, out sources))
                {
                    sources = RollSources.None;
                }

                // Normalised, because a negative property rolls its "high" end lowest — `dmg-ac`
                // runs -25..-40, so the arithmetic low is the second number.
                int statId = ItemStatReader.StatFromKey(key);

                StatDescriptor descriptor;
                int valShift = statCost != null && statCost.TryGetStat(statId, out descriptor)
                    ? descriptor.ValShift
                    : 0;

                stats.Add(new RolledStatRange(
                    statId,
                    ItemStatReader.LayerFromKey(key),
                    lowValue < highValue ? lowValue : highValue,
                    lowValue < highValue ? highValue : lowValue,
                    sources,
                    valShift));
            }
        }

        /// <summary>True when any of the property's seven sets uses func 12 or 36.</summary>
        private static bool RollsTheLayer(PropertiesTable properties, int propertyId)
        {
            PropertiesTable.Row row = properties[propertyId];
            if (row == null)
            {
                return false;
            }

            foreach (int func in row.Func)
            {
                if (func == 12 || func == 36)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Applies one layer-rolling property at both ends: the two keys differ only in their layer
        /// and carry the same value, which is the span of layers the roll could have chosen.
        /// </summary>
        private static void AddLayerRange(
            PropertyApplier low,
            PropertyApplier high,
            ItemIdentity item,
            Sourced entry,
            List<RolledLayerRange> into)
        {
            var atLow = new Dictionary<int, int>();
            var atHigh = new Dictionary<int, int>();

            low.Apply(PropertyApplier.PropModeGem, item, entry.Property, atLow);
            high.Apply(PropertyApplier.PropModeGem, item, entry.Property, atHigh);

            foreach (KeyValuePair<int, int> written in atLow)
            {
                int statId = ItemStatReader.StatFromKey(written.Key);
                int layerLow = ItemStatReader.LayerFromKey(written.Key);
                int layerHigh = layerLow;

                foreach (KeyValuePair<int, int> other in atHigh)
                {
                    if (ItemStatReader.StatFromKey(other.Key) == statId)
                    {
                        layerHigh = ItemStatReader.LayerFromKey(other.Key);
                    }
                }

                into.Add(new RolledLayerRange(
                    statId,
                    layerLow < layerHigh ? layerLow : layerHigh,
                    layerLow < layerHigh ? layerHigh : layerLow,
                    written.Value,
                    entry.Source));
            }
        }

        private static void Attribute(
            PropertyApplier applier,
            ItemIdentity item,
            Sourced entry,
            Dictionary<int, RollSources> sourceOf)
        {
            var scratch = new Dictionary<int, int>();
            applier.Apply(PropertyApplier.PropModeGem, item, entry.Property, scratch);

            foreach (int key in scratch.Keys)
            {
                RollSources existing;
                sourceOf[key] = sourceOf.TryGetValue(key, out existing)
                    ? existing | entry.Source
                    : entry.Source;
            }
        }

        private static int CompareRanges(RolledStatRange a, RolledStatRange b)
        {
            int byStat = a.StatId.CompareTo(b.StatId);
            return byStat != 0 ? byStat : a.Layer.CompareTo(b.Layer);
        }

        private static int CompareLayerRanges(RolledLayerRange a, RolledLayerRange b)
        {
            int byStat = a.StatId.CompareTo(b.StatId);
            return byStat != 0 ? byStat : a.LayerLow.CompareTo(b.LayerLow);
        }

        /// <summary>
        /// armor.txt rolls a base Defense between `minac` and `maxac` — the one base column that is
        /// a genuine range. Weapon base damage and durability are single columns and do not roll.
        /// </summary>
        private void AddBaseDefense(
            ItemIdentity item,
            Dictionary<int, int> lowStats,
            Dictionary<int, int> highStats,
            Dictionary<int, int> lowBase,
            Dictionary<int, int> highBase,
            Dictionary<int, RollSources> sourceOf,
            bool maximised)
        {
            int minac = _items.GetInt(item.ClassId, "minac");
            int maxac = _items.GetInt(item.ClassId, "maxac");
            if (minac <= 0 && maxac <= 0)
            {
                return;
            }

            // An `ac%` property does not just scale the base — it REPLACES it.
            //
            // ITEMMOD_MaximizeStatForEnhanced 0x65ccc0, cases 16 and 31: for an `armo` item
            // (`push 32h` at 0x65ccfc) with a non-zero maxac (0x65cd0c reads the items record at
            // +0xD0, the same field ITEM_RollBaseArmorClass rolls against), it computes
            // `max(GetUnitStat(31) + 1, maxac + 1)` (0x65cd29-0x65cd30) and STORES it (0x65cd39).
            // Every roll ITEM_RollBaseArmorClass can produce is <= maxac — it halts the game
            // otherwise (0x5563b2) — so both arms land on exactly maxac + 1.
            //
            // Only `ac%` reaches it. The per-property dispatch table at 0x745b58 is
            // {handler, statId} with an 8-byte stride indexed by properties.txt row: row 0 `ac`
            // (stat 31) takes PropertyFunc_SimpleStatWrapper, which passes the "enhanced" flag as
            // 0 (`push 0` at 0x65d1ce), while row 5 `ac%` (stat 16) takes
            // PropertyFunc_SimpleStatWrapper2, which passes 1 (`push 1` at 0x65d2be) — and
            // ITEMMOD_ApplyRandomStatValue maximises unconditionally when that flag is set
            // (0x65cf52).
            //
            // So the base does not roll at all here: Skin of the Vipermagi is 127 every time, not
            // 111..126, and its Defense is a fixed 279 rather than a span.
            //
            // D2R's cube upgrades undo that: PLRTRADE_CreateCubeOutputs keeps the item for a `mod`
            // output, swaps its class and re-runs D2GAME_InitItemStats (0x1403c0251), whose armour
            // roll is a plain minac..maxac store (0x1402de731); only ApplyEthereality follows
            // (0x1403c027b). The mods survive, so `ac%` stays on an unmaximised base.
            if (maximised && _data.IsResurrected)
            {
                if (UpgradedByCube(item))
                {
                    maximised = false;
                }
                else if (item.Quality == (int)ItemQuality.Rare && CubeUpgradeTarget(item.ClassId))
                {
                    // A rare does not record whether it was upgraded, so it spans both outcomes:
                    // minac..maxac rolled, or maxac + 1 native — contiguous. The ethereal scaling
                    // below is the same 3/2 either path applies.
                    maximised = false;
                    maxac += 1;
                }
            }

            if (maximised)
            {
                // 1.14d: the store is ABSOLUTE and reads the RAW items.txt maxac; its ordering against
                // ITEMMOD_ApplyEtherealBonus is untraced, so the literal reading is what is modelled.
                // D2R applies the quality mods first (the switch at 0x1402e0e12) and only then makes
                // the item ethereal (ITEMS_MakeEthereal 0x1402e14c0), which scales the stored base by
                // 3/2 (ITEMMODS_ApplyEthereality 0x140286dc2). Sets are never made ethereal there.
                minac = maxac + 1;
                if (_data.IsResurrected && item.Has(ItemRecordFlags.Ethereal) && !IsOfType(item, "weap")
                    && item.Quality != (int)ItemQuality.Set)
                {
                    minac = minac * 3 / 2;
                }

                maxac = minac;

                int maximisedKey = ItemStatReader.PackStatKey(0, StatDefense);
                Accumulate(lowStats, maximisedKey, minac);
                Accumulate(highStats, maximisedKey, maxac);
                lowBase[maximisedKey] = minac;
                highBase[maximisedKey] = maxac;

                RollSources had;
                sourceOf[maximisedKey] = sourceOf.TryGetValue(maximisedKey, out had)
                    ? had | RollSources.Base
                    : RollSources.Base;
                return;
            }

            // ITEMMOD_ApplyEtherealBonus 0x65e4d0 scales the base by 3/2 ONCE at spawn — the six
            // damage stats for an `weap` item (0x65e51b onward, itemtypes row 45), stat 31 for
            // anything else (0x65e5d6). A captured ethereal item's recorded Defense therefore
            // already includes it, so the reconstructed span has to as well or it sits below the
            // value it is meant to contain.
            //
            // `lea eax,[eax+eax*2]` then `cdq; sub eax,edx; sar eax,1` is a truncate-toward-zero
            // halving, which is what integer division gives.
            if (item.Has(ItemRecordFlags.Ethereal) && !IsOfType(item, "weap"))
            {
                minac = minac * 3 / 2;
                maxac = maxac * 3 / 2;
            }

            int key = ItemStatReader.PackStatKey(0, StatDefense);
            Accumulate(lowStats, key, minac);
            Accumulate(highStats, key, maxac);
            lowBase[key] = minac;
            highBase[key] = maxac;

            RollSources existing;
            sourceOf[key] = sourceOf.TryGetValue(key, out existing)
                ? existing | RollSources.Base
                : RollSources.Base;
        }

        /// <summary>
        /// Applies op 13 to one end of the reconstruction, writing back only the TARGET stats.
        ///
        /// The percent stats themselves are deliberately left in place. On the item they are
        /// dropped from FullStats (0x626821), but the reconstruction feeds two different lines: the
        /// Defense line, which draws the resolved target, and `+150% Enhanced Defense`, which is
        /// drawn from the modifier view where the percent survives. Transplanting only the targets
        /// gives each line a span in its own units.
        /// </summary>
        private void ResolveBaseOps(Dictionary<int, int> stats, Dictionary<int, int> baseStats)
        {
            if (baseStats.Count == 0)
            {
                return;
            }

            var merged = new Dictionary<int, int>(stats);
            ItemStatOps.Resolve(merged, baseStats, _data.ItemStatCost);

            foreach (ItemStatOpEntry entry in _data.ItemStatCost.PercentOfBaseEntries)
            {
                int key = ItemStatReader.PackStatKey(0, entry.TargetStat);

                int resolved;
                if (merged.TryGetValue(key, out resolved))
                {
                    stats[key] = resolved;
                }
            }
        }

        /// <summary>
        /// The Defense the line draws at one end: the item's stats plus its earned tier lists,
        /// op 13 resolved, then ops 4/5 against the viewer — the order Compose applies them in.
        /// False when nothing writes Defense.
        /// </summary>
        private bool DrawnDefense(
            Dictionary<int, int> stats,
            Dictionary<int, int> tiers,
            Dictionary<int, int> baseStats,
            Func<int, int> viewerStat,
            out int defense)
        {
            var drawn = new Dictionary<int, int>(stats);
            foreach (KeyValuePair<int, int> tier in tiers)
            {
                Accumulate(drawn, tier.Key, tier.Value);
            }

            var preOp = new Dictionary<int, int>(drawn);
            ResolveBaseOps(drawn, baseStats);
            ItemStatOps.ResolveLevelScaled(
                drawn, preOp, _data.ItemStatCost.LevelScaledEntries, viewerStat);

            return drawn.TryGetValue(ItemStatReader.PackStatKey(0, StatDefense), out defense);
        }

        /// <summary>
        /// Whether any gathered property writes `item_armor_percent`, which is what sends the base
        /// defense through ITEMMOD_MaximizeStatForEnhanced. Checked by STAT rather than by code,
        /// because the game's dispatch table keys the handler off the property row's stat id.
        /// </summary>
        private static bool MaximisesBaseDefense(
            List<Sourced> gathered, PropertiesTable properties, bool resurrected)
        {
            foreach (Sourced entry in gathered)
            {
                if (resurrected && FillerAssigned(entry.Source))
                {
                    continue;
                }

                PropertiesTable.Row row = entry.Seed != null
                    ? null
                    : properties.RowAt(entry.Property.PropertyId);
                if (row == null)
                {
                    continue;
                }

                for (int set = 0; set < PropertiesTable.SetsPerProperty; ++set)
                {
                    if (row.Stat[set] == StatArmorPercent)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private const int StatArmorPercent = 16;

        // D2R assigns runeword and socket mods with pItem = the FILLER, not the host
        // (ITEMMODS_UpdateRuneword at 0x1402d39ec / 0x140286f5c; ITEMS_ApplyGemOrRuneAndRefreshSets
        // 0x1400a6772), so ITEMS_SetBaseStatValue (0x140287afd) tests a rune and never maximises the
        // host's Defense.
        private static bool FillerAssigned(RollSources source)
        {
            return (source & (RollSources.Runeword | RollSources.Socket)) != 0;
        }

        private static void Accumulate(Dictionary<int, int> into, int key, int value)
        {
            int existing;
            into[key] = into.TryGetValue(key, out existing) ? existing + value : value;
        }

        /// <summary>
        /// The affix ids the record stores, resolved through the concatenated
        /// [MagicSuffix][MagicPrefix][automagic] array. Covers magic, rare and the random half of a
        /// crafted item, since all three store their affixes the same way.
        /// </summary>
        private void GatherAffixes(
            ItemIdentity item, PropertiesTable properties, List<Sourced> into)
        {
            for (int slot = 0; slot < ItemIdentity.MaxAffixSlots; ++slot)
            {
                AddAffix(item.MagicPrefix[slot], properties, into);
                AddAffix(item.MagicSuffix[slot], properties, into);
            }

            AddAffix(item.AutoAffix, properties, into);
        }

        private void AddAffix(int affixId, PropertiesTable properties, List<Sourced> into)
        {
            TxtFile table;
            int row;
            if (!_affixes.TryResolve(affixId, out table, out row))
            {
                return;
            }

            for (int mod = 1; mod <= 3; ++mod)
            {
                AddProperty(
                    properties,
                    into,
                    RollSources.Affix,
                    table.GetString(row, "mod" + mod + "code"),
                    table.GetString(row, "mod" + mod + "param"),
                    table.GetInt(row, "mod" + mod + "min"),
                    table.GetInt(row, "mod" + mod + "max"));
            }
        }

        private void GatherUnique(
            ItemIdentity item, PropertiesTable properties, List<Sourced> into)
        {
            if (item.Quality != (int)ItemQuality.Unique)
            {
                return;
            }

            TxtFile table = _data.UniqueItems;
            if (table == null || item.FileIndex < 0 || item.FileIndex >= table.RowCount)
            {
                return;
            }

            for (int prop = 1; prop <= 12; ++prop)
            {
                AddProperty(
                    properties,
                    into,
                    RollSources.Unique,
                    table.GetString(item.FileIndex, "prop" + prop),
                    table.GetString(item.FileIndex, "par" + prop),
                    table.GetInt(item.FileIndex, "min" + prop),
                    table.GetInt(item.FileIndex, "max" + prop));
            }
        }

        private void GatherSetItem(
            ItemIdentity item, PropertiesTable properties, List<Sourced> into)
        {
            if (item.Quality != (int)ItemQuality.Set)
            {
                return;
            }

            TxtFile table = _data.SetItems;
            if (table == null || item.FileIndex < 0 || item.FileIndex >= table.RowCount)
            {
                return;
            }

            // sub_1402866E0 case 4: a classic-format item takes props 1..2 and never reaches the
            // aprop loop (0x140286905, 0x140286911).
            bool classic = _data.IsResurrected && item.Format == 0;
            int props = classic ? 2 : 9;
            for (int prop = 1; prop <= props; ++prop)
            {
                AddProperty(
                    properties,
                    into,
                    RollSources.SetItem,
                    table.GetString(item.FileIndex, "prop" + prop),
                    table.GetString(item.FileIndex, "par" + prop),
                    table.GetInt(item.FileIndex, "min" + prop),
                    table.GetInt(item.FileIndex, "max" + prop));
            }

            if (classic)
            {
                return;
            }

            // aprop<n>a/b are the piece's OWN extra mods, granted as more of the set is worn. They
            // are the item's mods rather than the set's, which is why they live in SetItems.txt.
            // With `add func` set they land in tier lists, not the item's own stats (see
            // Sourced.MaximiseOnly); with it blank they are ordinary own mods (0x140286a22).
            bool tiered = _data.IsResurrected && table.GetInt(item.FileIndex, "add func") != 0;
            for (int prop = 1; prop <= 5; ++prop)
            {
                foreach (string half in new[] { "a", "b" })
                {
                    int before = into.Count;
                    AddProperty(
                        properties,
                        into,
                        RollSources.SetItem,
                        table.GetString(item.FileIndex, "aprop" + prop + half),
                        table.GetString(item.FileIndex, "apar" + prop + half),
                        table.GetInt(item.FileIndex, "amin" + prop + half),
                        table.GetInt(item.FileIndex, "amax" + prop + half));

                    for (int at = before; tiered && at < into.Count; ++at)
                    {
                        Sourced tier = into[at];
                        tier.MaximiseOnly = true;
                        tier.TierState = 164 + prop;
                        into[at] = tier;
                    }
                }
            }
        }

        private void GatherSetBonuses(IEnumerable<int> earnedSetIds, List<Sourced> into)
        {
            if (earnedSetIds == null)
            {
                return;
            }

            foreach (int setId in earnedSetIds)
            {
                foreach (ItemProperty property in _sets.PartialProperties(setId))
                {
                    Add(into, property, RollSources.SetBonus);
                }

                foreach (ItemProperty property in _sets.FullProperties(setId))
                {
                    Add(into, property, RollSources.SetBonus);
                }
            }
        }

        /// <summary>
        /// A runeword's granted properties live in runes.txt, found by the string id the record
        /// carries in MagicPrefix[0] — TXT_AllocTxt_runes 0x639c63 resolved the row's `Name` to that
        /// id at table-compile time, so matching it back is exact.
        /// </summary>
        private void GatherRuneword(
            ItemIdentity item, PropertiesTable properties, List<Sourced> into)
        {
            if (_data.Runes == null)
            {
                return;
            }

            int nameId = item.MagicPrefix[0];
            int found = -1;

            for (int row = 0; row < _data.Runes.RowCount && found < 0; ++row)
            {
                string key = _data.Runes.GetString(row, "Name").Trim();
                if (key.Length != 0 && _data.Strings.ResolveKey(key) == nameId)
                {
                    found = row;
                }
            }

            if (found < 0)
            {
                return;
            }

            for (int prop = 1; prop <= 7; ++prop)
            {
                AddProperty(
                    properties,
                    into,
                    RollSources.Runeword,
                    _data.Runes.GetString(found, "T1Code" + prop),
                    _data.Runes.GetString(found, "T1Param" + prop),
                    _data.Runes.GetInt(found, "T1Min" + prop),
                    _data.Runes.GetInt(found, "T1Max" + prop));
            }
        }

        /// <summary>
        /// A superior item's modifier comes from qualityitems.txt. On D2R the record's file index IS
        /// the row that rolled; on 1.14d that is untraced, so every row whose type gate admits this item is a candidate. That would be
        /// ambiguous except that in shipped data each mod code carries the SAME range in every row
        /// it appears in (`att` 1..3, `dmg%` and `ac%` 5..15, `dur%` 10..15), so the union over
        /// candidates is one span per stat either way. A test asserts that.
        /// </summary>
        private void GatherSuperior(
            ItemIdentity item, PropertiesTable properties, List<Sourced> into)
        {
            if (item.Quality != (int)ItemQuality.HighQuality || _data.QualityItems == null)
            {
                return;
            }

            var seen = new SortedSet<string>();

            // D2R: LOD114d_sub_5C2970 draws ONE row, stores it as the file index (ITEMS_SetFileIndex
            // 0x140381dc3) and applies only its mods (0x140381dd2-0x140381e23).
            int drawn = _data.IsResurrected && item.FileIndex >= 0 && item.FileIndex < _data.QualityItems.RowCount
                ? item.FileIndex
                : -1;

            for (int row = 0; row < _data.QualityItems.RowCount; ++row)
            {
                if (drawn >= 0 ? row != drawn : !SuperiorRowApplies(item, row))
                {
                    continue;
                }

                for (int mod = 1; mod <= 2; ++mod)
                {
                    string code = _data.QualityItems.GetString(row, "mod" + mod + "code").Trim();
                    if (code.Length == 0 || !seen.Add(code))
                    {
                        continue;
                    }

                    AddProperty(
                        properties,
                        into,
                        RollSources.Superior,
                        code,
                        _data.QualityItems.GetString(row, "mod" + mod + "param"),
                        _data.QualityItems.GetInt(row, "mod" + mod + "min"),
                        _data.QualityItems.GetInt(row, "mod" + mod + "max"));
                }
            }
        }

        /// <summary>
        /// qualityitems.txt gates each row by item shape with one column per family. They are read
        /// against the item's own type tree rather than its code, so a base inherits the gate the
        /// same way the game's type checks do.
        /// </summary>
        private bool SuperiorRowApplies(ItemIdentity item, int row)
        {
            foreach (KeyValuePair<string, string> gate in SuperiorGates)
            {
                if (_data.QualityItems.GetInt(row, gate.Key) == 0)
                {
                    continue;
                }

                if (IsOfType(item, gate.Value))
                {
                    return true;
                }
            }

            return false;
        }

        // Column in qualityitems.txt -> the ItemTypes code it gates on.
        private static readonly KeyValuePair<string, string>[] SuperiorGates =
        {
            new KeyValuePair<string, string>("armor", "armo"),
            new KeyValuePair<string, string>("weapon", "weap"),
            new KeyValuePair<string, string>("shield", "shld"),
            new KeyValuePair<string, string>("thrown", "thro"),
            new KeyValuePair<string, string>("scepter", "scep"),
            new KeyValuePair<string, string>("wand", "wand"),
            new KeyValuePair<string, string>("staff", "staf"),
            new KeyValuePair<string, string>("bow", "bow"),
            new KeyValuePair<string, string>("boots", "boot"),
            new KeyValuePair<string, string>("gloves", "glov"),
            new KeyValuePair<string, string>("belt", "belt"),
        };

        /// <summary>
        /// A unique or set item spawns only on its row's base, so a different class means the cube
        /// replaced it (`v69->nClassId = v67` in PLRTRADE_CreateCubeOutputs 0x1403bfaa0).
        /// </summary>
        private bool UpgradedByCube(ItemIdentity item)
        {
            TxtFile table;
            string column;
            if (item.Quality == (int)ItemQuality.Unique)
            {
                table = _data.UniqueItems;
                column = "code";
            }
            else if (item.Quality == (int)ItemQuality.Set)
            {
                table = _data.SetItems;
                column = "item";
            }
            else
            {
                return false;
            }

            if (table == null || item.FileIndex < 0 || item.FileIndex >= table.RowCount)
            {
                return false;
            }

            int rowClass = _items.ClassIdForCode(table.GetString(item.FileIndex, column).Trim());
            return rowClass >= 0 && rowClass != item.ClassId;
        }

        /// <summary>A base an upgrade can produce: one whose `normcode` is another item.</summary>
        private bool CubeUpgradeTarget(int classId)
        {
            string normal = _items.GetString(classId, "normcode").Trim();
            return normal.Length != 0
                   && !string.Equals(normal, _items.Code(classId).Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private bool IsOfType(ItemIdentity item, string typeCode)
        {
            return _types.IsOfType(
                _types.Row(_items.PrimaryTypeCode(item.ClassId)),
                _types.Row(_items.SecondaryTypeCode(item.ClassId)),
                _types.Row(typeCode));
        }

        private void AddProperty(
            PropertiesTable properties,
            List<Sourced> into,
            RollSources source,
            string code,
            string param,
            int min,
            int max)
        {
            string trimmed = code.Trim();

            // Eleven enabled uniques carry a commented-out `*`-prefixed code. The game's table
            // compiler never resolves those, so they are skipped rather than reported missing.
            if (trimmed.Length == 0 || trimmed[0] == '*')
            {
                return;
            }

            PropertyRef link = PropertyGroupsTable.Link(trimmed, properties, _groups);
            if (link.Row < 0)
            {
                return;
            }

            if (link.Kind == PropertyRef.KindGroup)
            {
                // ITEMMODS_AssignProperty 0x14028a67d: the outer min/max is the group's count and
                // its param is unused.
                var group = new Sourced();
                group.Source = source;
                group.Seed = new ChoiceSeed { GroupRow = link.Row, Min = min, Max = max };
                into.Add(group);
                return;
            }

            var property = new ItemProperty();
            property.PropertyId = link.Row;
            property.Param = _data.ParamLinker.Resolve(param);
            property.Min = min;
            property.Max = max;

            // D2R's func 25 picks its stat off the seed; 1.14d's slot 25 is null and stays with the
            // applier, which reports it. So does a func 25 that would write nothing.
            if (_data.IsResurrected
                && HasFunc(properties, link.Row, StatPickFunc)
                && CanBuildStatPick(properties, property.Param))
            {
                var pick = new Sourced();
                pick.Source = source;
                pick.Seed = new ChoiceSeed { GroupRow = -1, IsStatPick = true, StatPick = property };
                into.Add(pick);
                return;
            }

            Add(into, property, source);
        }

        private static bool HasFunc(PropertiesTable properties, int propertyId, int func)
        {
            PropertiesTable.Row row = properties[propertyId];
            return row != null && Array.IndexOf(row.Func, func) >= 0;
        }

        private static void Add(List<Sourced> into, ItemProperty property, RollSources source)
        {
            var sourced = new Sourced();
            sourced.Property = property;
            sourced.Source = source;
            into.Add(sourced);
        }

        private static IReadOnlyList<int> OutOfRange(
            List<RolledStatRange> stats, IDictionary<int, int> recorded)
        {
            var outside = new SortedSet<int>();
            if (recorded == null)
            {
                return new List<int>();
            }

            foreach (RolledStatRange range in stats)
            {
                int value;
                if (!recorded.TryGetValue(
                        ItemStatReader.PackStatKey(range.Layer, range.StatId), out value))
                {
                    continue;
                }

                if (value < range.Low || value > range.High)
                {
                    outside.Add(range.StatId);
                }
            }

            return new List<int>(outside);
        }

        private static IReadOnlyList<int> Unattributed(
            List<int> kept,
            List<Dictionary<int, int>> lows,
            List<Dictionary<int, int>> highs,
            List<RolledLayerRange> layerVaries,
            IDictionary<int, int> recorded)
        {
            var missing = new SortedSet<int>();
            if (recorded == null)
            {
                return new List<int>();
            }

            foreach (KeyValuePair<int, int> entry in recorded)
            {
                bool written = false;
                foreach (int i in kept)
                {
                    if (lows[i].ContainsKey(entry.Key) || highs[i].ContainsKey(entry.Key))
                    {
                        written = true;
                        break;
                    }
                }

                if (!written && !ExplainedByLayerRoll(entry.Key, entry.Value, layerVaries))
                {
                    missing.Add(ItemStatReader.StatFromKey(entry.Key));
                }
            }

            return new List<int>(missing);
        }

        /// <summary>
        /// A func-12/36 stat never enters the combined totals (<see cref="AddLayerRange"/>), so a
        /// recorded one is explained by a layer range of the same stat that spans its layer and
        /// carries its value.
        /// </summary>
        private static bool ExplainedByLayerRoll(
            int key, int value, List<RolledLayerRange> layerVaries)
        {
            int statId = ItemStatReader.StatFromKey(key);
            int layer = ItemStatReader.LayerFromKey(key);

            foreach (RolledLayerRange range in layerVaries)
            {
                if (range.StatId == statId && range.LayerLow <= layer && layer <= range.LayerHigh
                    && range.Value == value)
                {
                    return true;
                }
            }

            return false;
        }

        private static IReadOnlyList<int> Merge(SortedSet<int> a, SortedSet<int> b)
        {
            var merged = new SortedSet<int>(a);
            foreach (int value in b)
            {
                merged.Add(value);
            }

            return new List<int>(merged);
        }
    }
}
