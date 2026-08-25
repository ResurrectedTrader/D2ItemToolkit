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
            IReadOnlyList<int> outOfRange,
            IReadOnlyList<int> unattributed,
            IReadOnlyList<int> itemLevelDependent,
            IReadOnlyList<int> unsupportedFuncs,
            bool craftedRecipeUnknown,
            int craftedRecipe)
        {
            Stats = stats;
            LayerVaries = layerVaries;
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
        }

        /// <summary>One gathered property and the source that contributed it.</summary>
        private struct Sourced
        {
            public ItemProperty Property;
            public RollSources Source;
        }

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
        /// </summary>
        public ItemRollRanges Reconstruct(
            ItemIdentity item,
            IDictionary<int, int> recorded,
            IEnumerable<ItemProperty> socketProperties,
            IEnumerable<int> earnedSetIds,
            bool includeOwnSources,
            bool includeBaseDefense = true)
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

            // The BASE view at each end, kept apart from the merged one because op 13 consumes the
            // two separately (STATLIST_LookupBaseStatWithMinAccr 0x624ed0 reads `Stats`, the result
            // lands in FullStats at 0x625158). Only Defense rolls a base, so only Defense is in
            // here.
            var lowBase = new Dictionary<int, int>();
            var highBase = new Dictionary<int, int>();
            var sourceOf = new Dictionary<int, RollSources>();
            var layerVaries = new List<RolledLayerRange>();

            foreach (Sourced entry in gathered)
            {
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

            // Gated with the rest of the item's own sources: the base armour roll IS one, so a
            // socket-only reconstruction that added it gave a gem block the HOST's base span —
            // "+30 Defense [33-35]" where 33-35 was the cap's 3..5 plus the rune's fixed 30.
            if (includeOwnSources && includeBaseDefense)
            {
                AddBaseDefense(
                    item, lowStats, highStats, lowBase, highBase, sourceOf,
                    MaximisesBaseDefense(gathered, low.Properties));
            }

            // The Defense line draws the OP-RESOLVED value, so its span has to be resolved too. A
            // Large Shield rolling 12..14 under +150% Enhanced Defense prints 32 — a number that
            // can never fall inside the 12..14 the base rolled within, which is what the span used
            // to offer.
            ResolveBaseOps(lowStats, lowBase);
            ResolveBaseOps(highStats, highBase);

            var stats = new List<RolledStatRange>();
            CollectRanges(lowStats, highStats, sourceOf, stats, _data.ItemStatCost);

            stats.Sort(CompareRanges);
            layerVaries.Sort(CompareLayerRanges);

            return new ItemRollRanges(
                stats,
                layerVaries,
                OutOfRange(stats, recorded),
                Unattributed(lowStats, highStats, recorded),
                Merge(low.ItemLevelDependent, high.ItemLevelDependent),
                Merge(low.UnsupportedFunc, high.UnsupportedFunc),
                item.Quality == (int)ItemQuality.Crafted && craftedRecipe < 0,
                craftedRecipe);
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
            // filler is crafted.
            foreach (Sourced entry in Gather(item, applier.Properties, null))
            {
                properties.Add(entry.Property);
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
            if (maximised)
            {
                // The store is ABSOLUTE and reads the RAW items.txt maxac, so it overwrites
                // whatever the ethereal bonus did rather than scaling with it. The ordering
                // against ITEMMOD_ApplyEtherealBonus is untraced and no captured ethereal armour
                // carries `ac%`, so the literal reading is what is modelled.
                minac = maxac + 1;
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
        /// Whether any gathered property writes `item_armor_percent`, which is what sends the base
        /// defense through ITEMMOD_MaximizeStatForEnhanced. Checked by STAT rather than by code,
        /// because the game's dispatch table keys the handler off the property row's stat id.
        /// </summary>
        private static bool MaximisesBaseDefense(
            List<Sourced> gathered, PropertiesTable properties)
        {
            foreach (Sourced entry in gathered)
            {
                PropertiesTable.Row row = properties.RowAt(entry.Property.PropertyId);
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

            for (int prop = 1; prop <= 9; ++prop)
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

            // aprop<n>a/b are the piece's OWN extra mods, granted as more of the set is worn. They
            // are the item's mods rather than the set's, which is why they live in SetItems.txt.
            for (int prop = 1; prop <= 5; ++prop)
            {
                foreach (string half in new[] { "a", "b" })
                {
                    AddProperty(
                        properties,
                        into,
                        RollSources.SetItem,
                        table.GetString(item.FileIndex, "aprop" + prop + half),
                        table.GetString(item.FileIndex, "apar" + prop + half),
                        table.GetInt(item.FileIndex, "amin" + prop + half),
                        table.GetInt(item.FileIndex, "amax" + prop + half));
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
        /// A superior item's modifier comes from qualityitems.txt, but the record does not say WHICH
        /// row rolled — so every row whose type gate admits this item is a candidate. That would be
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

            for (int row = 0; row < _data.QualityItems.RowCount; ++row)
            {
                if (!SuperiorRowApplies(item, row))
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

        private bool IsOfType(ItemIdentity item, string typeCode)
        {
            return _types.IsOfType(
                _types.Row(_items.PrimaryTypeCode(item.ClassId)),
                _types.Row(_items.SecondaryTypeCode(item.ClassId)),
                _types.Row(typeCode));
        }

        private static void AddProperty(
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

            int id = properties.RowForCode(trimmed);
            if (id < 0)
            {
                return;
            }

            var property = new ItemProperty();
            property.PropertyId = id;
            property.Param = ParseParam(param);
            property.Min = min;
            property.Max = max;

            Add(into, property, source);
        }

        private static void Add(List<Sourced> into, ItemProperty property, RollSources source)
        {
            var sourced = new Sourced();
            sourced.Property = property;
            sourced.Source = source;
            into.Add(sourced);
        }

        /// <summary>
        /// A param cell is usually a number but sometimes a skill or class NAME — `charged` carries
        /// "Hydra". The tables the game compiles resolve those to ids; this port has no general
        /// resolver, so a non-numeric param yields 0 and the property still reports its range.
        /// </summary>
        private static int ParseParam(string param)
        {
            if (param == null)
            {
                return 0;
            }

            int value;
            return int.TryParse(param.Trim(), out value) ? value : 0;
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
            Dictionary<int, int> lowStats,
            Dictionary<int, int> highStats,
            IDictionary<int, int> recorded)
        {
            var missing = new SortedSet<int>();
            if (recorded == null)
            {
                return new List<int>();
            }

            foreach (int key in recorded.Keys)
            {
                if (!lowStats.ContainsKey(key) && !highStats.ContainsKey(key))
                {
                    missing.Add(ItemStatReader.StatFromKey(key));
                }
            }

            return new List<int>(missing);
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
