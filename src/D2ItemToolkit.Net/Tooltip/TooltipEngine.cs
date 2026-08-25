using System;
using System.Collections.Generic;

namespace D2ItemToolkit
{
    /// <summary>
    /// The way in. Holds the parsed game tables — building them is the expensive part, so make one
    /// and keep it. Rendering is read-only and safe to share between threads.
    ///
    /// Not quite "immutable once constructed", which this used to claim: constructing the per-render
    /// helpers installs a property-code resolver on the shared gem and set tables. The value written
    /// is functionally the same every time and a reference store is atomic, so concurrent renders
    /// stay correct — but it is a write, and the accurate statement is the one above.
    /// </summary>
    public sealed class TooltipEngine
    {
        private static readonly Lazy<TooltipEngine> EmbeddedInstance =
            new Lazy<TooltipEngine>(() => new TooltipEngine(D2DataFiles.LoadEmbedded()));

        private readonly D2DataFiles _data;
        private readonly ItemTable _items;
        private readonly ItemTypeTree _types;
        private readonly SetTable _sets;

        // Built once with the tables, not per call: GemTable's constructor walks every gems row
        // against every item code, so rebuilding it on each Appearance() was tens of thousands of
        // comparisons for one lookup.
        private readonly ItemInventoryColor _colors;
        private readonly ItemInventoryGraphics _graphics;
        private readonly EquipRequirements _requirements;
        private readonly RequiredLevelCalculator _level;
        private readonly SocketStatSynthesis _socketStats;
        private readonly RolledRangeReconstructor _ranges;

        private TooltipEngine(D2DataFiles data)
        {
            _data = data;
            _items = new ItemTable(data.Weapons, data.Armor, data.Misc);
            _types = new ItemTypeTree(data.ItemTypes);
            _sets = new SetTable(data.Sets, data.SetItems, data.Strings);
            _colors = new ItemInventoryColor(data, _items, _types);
            _graphics = new ItemInventoryGraphics(data, _items, _types);
            _requirements = new EquipRequirements(data, _items);
            _level = new RequiredLevelCalculator(data, _items);
            _socketStats = new SocketStatSynthesis(data, _items, _types);
            _ranges = new RolledRangeReconstructor(
                data, _items, _types, new MagicAffixTable(data), _sets);
        }

        /// <summary>The tables shipped inside this assembly. Built once, then reused.</summary>
        public static TooltipEngine Embedded
        {
            get { return EmbeddedInstance.Value; }
        }

        /// <summary>
        /// The parsed game tables, for lookups this library does not do for you. Everything here
        /// is read-only: `Data.Weapons.GetString(row, "invfile")` and friends.
        ///
        /// The tables are public; the ENGINE is not. What builds a tooltip out of them —
        /// RecordSections, the composer, the description generator — stays internal, because those
        /// shapes exist to mirror the disassembly rather than to be consumed.
        /// </summary>
        public D2DataFiles Data
        {
            get { return _data; }
        }

        /// <summary>weapons + armor + misc as one classId-indexed table, the way the game compiles them.</summary>
        public ItemTable Items
        {
            get { return _items; }
        }

        /// <summary>The itemtypes Equiv1/Equiv2 closure, for `IsOfType` questions.</summary>
        public ItemTypeTree Types
        {
            get { return _types; }
        }

        /// <summary>
        /// sets.txt and setitems.txt, linked the way TXT_AllocTxt_setitems links them (0x63668d).
        /// This is what tells a caller which pieces a set has, and in which order, so it can fill
        /// in <see cref="SetItemTooltipInput.OwnedSetItemIds"/>.
        /// </summary>
        public SetTable Sets
        {
            get { return _sets; }
        }

        /// <summary>Tables read from an MPQ extraction instead of the embedded copy.</summary>
        public static TooltipEngine FromFiles(
            string excelDirectory, string localeDirectory, string globalDirectory = null)
        {
            return new TooltipEngine(
                D2DataFiles.Load(excelDirectory, localeDirectory, globalDirectory));
        }

        /// <summary>
        /// An engine over tables you already hold. <see cref="FromFiles"/> is just this with a
        /// filesystem read in front; this form takes anything you can build a
        /// <see cref="D2DataFiles"/> from.
        /// </summary>
        public static TooltipEngine FromData(D2DataFiles data)
        {
            if (data == null) throw new ArgumentNullException("data");

            return new TooltipEngine(data);
        }

        /// <summary>
        /// The tooltip the game would draw for <paramref name="item"/>, as seen by
        /// <paramref name="viewer"/>. A null viewer renders what the engine produces with no player
        /// unit — level-scaled lines then scale by zero, which is what the game does too
        /// (GetStatUnsignedValue returns 0 for a null unit at 0x625483).
        /// </summary>
        public Tooltip Render(IUnit item, IUnit viewer = null, TooltipOptions options = null)
        {
            if (item == null) throw new ArgumentNullException("item");

            TooltipOptions opts = options ?? TooltipOptions.Default;

            // Separating the fillers means the item's own block must not contain them, which is
            // exactly what IncludeSockets false already does — they are moved, not dropped.
            bool includeSockets = opts.Sockets == SocketMode.Merged;

            // Derived from the viewer rather than defaulted to "none". The old default painted
            // every piece red and selected no tier, and — because the full-set block is gated on
            // IsEquipped — silently suppressed it for anyone actually wearing the set. A viewer
            // that carries nothing still yields exactly that empty input, and a non-set item exits
            // SetStateOf after the Location test, so this costs nothing on the common path.
            SetItemTooltipInput set = SetStateOf(item, viewer);

            // FILLER discard only. `set` keeps the real equipped state, because that is what the
            // full-set block, the worn mask and the piece colours read — gating those on this
            // option would reproduce, inside the library, exactly the bug that faking Location
            // causes outside it.
            bool discardFillers = set.IsEquipped && opts.ApplyWornSetDiscard;

            Composed composed = Compose(item, viewer, opts, includeSockets, discardFillers);

            // Installed BEFORE composing, because the annotation is written into each line's text
            // as it is built rather than patched onto the finished list.
            if (opts.Ranges != null)
            {
                InstallRangeAnnotations(composed.Composer, item, opts, includeSockets);
            }

            IReadOnlyList<ItemTooltipLine> lines;
            switch (composed.Kind)
            {
                case ItemTooltipKind.IdentifiedSetItem:
                    lines = SetItemLines(item, viewer, composed, set);
                    break;

                case ItemTooltipKind.Book:
                    lines = composed.Composer.ComposeBook(composed.Context);
                    break;

                default:
                    lines = composed.Composer.Compose(composed.Context, composed.ModifierStats);
                    break;
            }

            if (opts.Sockets == SocketMode.Separated)
            {
                lines = WithSocketBlocks(item, viewer, opts, lines, discardFillers);
            }

            return new Tooltip(
                composed.Kind, lines, composed.Composer, QuestColorOf(composed.Context));
        }

        /// <summary>
        /// Appends one block per filler BELOW the item. Lines are in display order, so appending
        /// puts them at the bottom, which is where a reader expects "and this is what the gems are
        /// doing".
        ///
        /// <paramref name="hostIsEquipped"/> must be the same value <see cref="Compose"/> was given.
        /// This mode MOVES the fillers out of the item's block; it must never ADD anything the
        /// merged render would not have shown. A worn set item's fillers are discarded by recalc
        /// (see <see cref="SocketStatSynthesis.FillersAreDiscardedByRecalc"/>), so they are absent
        /// from the item's block, and a block listing them below it would claim the item grants
        /// stats it does not.
        /// </summary>
        private IReadOnlyList<ItemTooltipLine> WithSocketBlocks(
            IUnit item,
            IUnit viewer,
            TooltipOptions options,
            IReadOnlyList<ItemTooltipLine> body,
            bool hostIsEquipped)
        {
            int slot = _socketStats.SlotFor(item, hostIsEquipped);
            if (slot < 0)
            {
                return body;
            }

            var all = new List<ItemTooltipLine>(body);

            foreach (IUnit filler in item.Items)
            {
                // A gem or rune has no stats of its own and is synthesised from gems.txt. A JEWEL
                // does carry its own — its affixes are captured like any magic item's — and
                // Contribution deliberately returns nothing for it rather than counting them twice.
                // Its own modifier view is what belongs in its block.
                SortedDictionary<int, int> contribution =
                    _socketStats.Contribution(filler, slot);

                bool carriesOwnStats = contribution.Count == 0;
                if (carriesOwnStats)
                {
                    contribution = ItemStatReader.ReconstructView(filler, ItemStatView.Modifiers());
                }

                if (contribution.Count == 0)
                {
                    continue;
                }

                // The filler's own name, taken from its own render — a socket filler is a unit in
                // its own right, which is what makes this a lookup rather than a special case.
                Composed asItem = Compose(filler, viewer, TooltipOptions.Default, false);
                string name = asItem.Sections.GetSection(ItemTooltipSection.ItemName);

                if (!string.IsNullOrEmpty(name))
                {
                    // A blank row between blocks, so three gems do not read as one list. The game
                    // never draws this section at all, so there is no append-order budget to spend
                    // and no marker to emit — the row is a bare terminator.
                    var gap = new ItemTooltipLine();
                    gap.Text = asItem.Sections.LineTerminator ?? string.Empty;
                    gap.Section = ItemTooltipSection.SocketContribution;
                    gap.Color = ItemTooltipColor.SocketedOrEthereal;
                    gap.EmitsColorMarker = false;
                    all.Add(gap);

                    var header = new ItemTooltipLine();
                    header.Text = name;
                    header.Section = ItemTooltipSection.SocketContribution;
                    header.Color = ItemTooltipColor.SocketedOrEthereal;
                    header.EmitsColorMarker = true;
                    all.Add(header);
                }

                // Described through the same writers as any modifier block, so the text matches
                // what the merged render would have shown — only the selection differs.
                var composer = new ItemTooltipComposer(
                    asItem.Sections, asItem.Sections.CreateModifierGenerator(contribution));

                if (options.Ranges != null)
                {
                    // A jewel's spans come from ITS OWN affixes, so it is ranged as the item it is.
                    // A gem or rune is ranged from the gems.txt properties it lends the host —
                    // which in shipped data never roll, so those blocks come out unannotated.
                    composer.RangeAnnotation = carriesOwnStats
                        ? BuildRangeAnnotation(filler, options)
                        : BuildFillerRangeAnnotation(item, filler, slot, options);
                    composer.RangeColor = options.Ranges.Color;
                }

                foreach (ItemTooltipLine line in composer.ComposeModifiersOnly(contribution))
                {
                    line.Section = ItemTooltipSection.SocketContribution;
                    all.Add(line);
                }
            }

            return all;
        }

        /// <summary>
        /// The spans for EVERY filler at once, for the socket bucket of a breakdown — where the
        /// lines are the fillers' union rather than one block per filler. A jewel's own affixes are
        /// folded in, since those are what rolled.
        /// </summary>
        private Func<IReadOnlyList<int>, int, string> BuildSocketRangeAnnotation(
            IUnit host, TooltipOptions options)
        {
            // The socket bucket of a breakdown is what the fillers are worth, not what a recalc
            // has currently left on the item.
            int slot = _socketStats.SlotFor(host, false);

            var properties = new List<ItemProperty>();
            var byKey = new Dictionary<int, RolledStatRange>();

            if (slot >= 0)
            {
                foreach (IUnit filler in host.Items)
                {
                    properties.AddRange(_socketStats.FillerProperties(filler, slot));

                    // A jewel contributes nothing through gems.txt; its own affixes are the roll,
                    // so its reconstruction is merged in rather than skipped.
                    if (_socketStats.Contribution(filler, slot).Count != 0)
                    {
                        continue;
                    }

                    foreach (RolledStatRange range in Ranges(filler).Stats)
                    {
                        byKey[ItemStatReader.PackStatKey(range.Layer, range.StatId)] = range;
                    }
                }
            }

            ItemRollRanges gems = _ranges.Reconstruct(
                ItemRecordReader.ReadIdentity(host), null, properties, null, false);

            foreach (RolledStatRange range in gems.Stats)
            {
                byKey[ItemStatReader.PackStatKey(range.Layer, range.StatId)] = range;
            }

            return Lookup(byKey, options);
        }

        /// <summary>
        /// The spans for ONE filler's own properties, so a jewel's roll is ranged against the
        /// jewel's own lines rather than against the host's merged totals.
        /// </summary>
        private Func<IReadOnlyList<int>, int, string> BuildFillerRangeAnnotation(
            IUnit host, IUnit filler, int slot, TooltipOptions options)
        {
            ItemRollRanges ranges = _ranges.Reconstruct(
                ItemRecordReader.ReadIdentity(host),
                null,
                _socketStats.FillerProperties(filler, slot),
                null,
                false);

            return Lookup(ByKey(ranges), options);
        }

        /// <summary>
        /// Turns the reconstruction into the (layer, statId) lookup the composer wants. Built once
        /// per render: the reconstruction applies every property twice, which is not work to repeat
        /// per line.
        /// </summary>
        private static Dictionary<int, RolledStatRange> ByKey(ItemRollRanges ranges)
        {
            var byKey = new Dictionary<int, RolledStatRange>();
            foreach (RolledStatRange range in ranges.Stats)
            {
                byKey[ItemStatReader.PackStatKey(range.Layer, range.StatId)] = range;
            }

            return byKey;
        }

        /// <summary>
        /// The (stats, layer) callback the composer wants, over an already-built key map.
        ///
        /// Positions carry the meaning on a multi-stat line, so a PARTIAL answer is worse than none:
        /// one span against "Adds 1-4 cold damage" reads as the whole line's, and the reader cannot
        /// tell which half it came from. All or nothing.
        /// </summary>
        private static Func<IReadOnlyList<int>, int, string> Lookup(
            Dictionary<int, RolledStatRange> byKey, TooltipOptions options)
        {
            Func<IReadOnlyList<RolledStatRange>, string> format =
                options.Ranges.Format ?? DefaultRangeAnnotation;

            return (shownStats, layer) =>
            {
                var found = new List<RolledStatRange>();

                foreach (int statId in shownStats)
                {
                    RolledStatRange range;
                    if (byKey.TryGetValue(ItemStatReader.PackStatKey(layer, statId), out range))
                    {
                        found.Add(range);
                    }
                }

                return found.Count != shownStats.Count || found.Count == 0 ? null : format(found);
            };
        }

        /// <summary>
        /// The default way a span is written: ` [5-15]`, and nothing at all for a stat that could
        /// only have taken one value. A single end would read as a range of one.
        /// </summary>
        internal static string DefaultRangeAnnotation(IReadOnlyList<RolledStatRange> ranges)
        {
            if (ranges == null || ranges.Count == 0)
            {
                return null;
            }

            // Every stat a DescGrp line covers shares the one number the line prints, so their
            // spans agree and repeating them would give "[(2-5)-(2-5)-(2-5)-(2-5)]".
            bool identical = true;
            for (int at = 1; at < ranges.Count && identical; ++at)
            {
                identical = ranges[at].DisplayLow == ranges[0].DisplayLow
                    && ranges[at].DisplayHigh == ranges[0].DisplayHigh;
            }

            if (identical)
            {
                return ranges[0].IsRange ? " [" + Span(ranges[0]) + "]" : null;
            }

            // A min-max line prints two numbers, so it gets two spans: "[(1-2)-(3-5)]" reads as
            // "the first number was 1..2, the second 3..5", which is the only honest single string
            // for it. A degenerate half still appears, because dropping it would leave the reader
            // unable to tell which half the surviving span belongs to.
            var parts = new List<string>();
            bool anyRange = false;
            foreach (RolledStatRange range in ranges)
            {
                parts.Add("(" + Span(range) + ")");
                anyRange = anyRange || range.IsRange;
            }

            return anyRange ? " [" + string.Join("-", parts.ToArray()) + "]" : null;
        }

        /// <summary>
        /// One span, from the DECODED ends — so a charged skill reads as its charge count rather
        /// than as the packed word it is stored in.
        /// </summary>
        private static string Span(RolledStatRange range)
        {
            return range.DisplayLow + "-" + range.DisplayHigh;
        }

        /// <summary>
        /// <paramref name="includeSockets"/> must match what the LINES being annotated contain. The
        /// merged render draws one line holding item plus fillers, so its span is the sum; a body
        /// rendered with the fillers excluded — IncludeSockets false, or the separated mode — draws
        /// the item's own value alone and must get the item's own span. Getting this backwards put
        /// "Fire Resist +20% [16-30]" on a line whose 20 was the item's half only.
        /// </summary>
        private Func<IReadOnlyList<int>, int, string> BuildRangeAnnotation(
            IUnit item,
            TooltipOptions options,
            bool includeSockets = true,
            bool includeBaseDefense = true)
        {
            ItemRollRanges ranges = includeSockets && includeBaseDefense
                ? Ranges(item)
                : _ranges.Reconstruct(
                    ItemRecordReader.ReadIdentity(item),
                    ItemStatReader.ReconstructView(item, ItemOwnMods()),
                    includeSockets ? AllSocketProperties(item) : null,
                    null,
                    true,
                    includeBaseDefense);

            return Lookup(ByKey(ranges), options);
        }

        /// <summary>
        /// The pair a render needs: the SECTION lookup counts the armour's base roll, because the
        /// Defense line draws base plus modifiers, and the MODIFIER lookup does not, because a
        /// `+45 Defense` line draws its own contribution alone. Handing both lines one dictionary
        /// gave the modifier the section's span.
        /// </summary>
        private void InstallRangeAnnotations(
            ItemTooltipComposer composer, IUnit item, TooltipOptions options, bool includeSockets)
        {
            composer.RangeAnnotation =
                BuildRangeAnnotation(item, options, includeSockets, includeBaseDefense: false);
            composer.SectionRangeAnnotation =
                BuildRangeAnnotation(item, options, includeSockets);
            composer.RangeColor = options.Ranges.Color;
        }

        /// <summary>
        /// The set state a viewer implies, so a caller never assembles bit masks by hand. Two
        /// passes with DIFFERENT predicates, which is the whole reason this belongs in the library:
        ///
        /// OWNED — colours the piece list. GetSetItem 0x486770 accepts inventory grid types 1, 3
        /// AND 4 (0x4867d4), so a piece on the alternate weapon set still counts and draws green.
        ///
        /// WORN — drives the bonus tiers. ITEMS_GetEquippedSetItemsMask requires grid type 3 alone
        /// (0x62a3f0), so a swapped piece lights no bit. The bit is the piece's setitems slot
        /// (0x62a474), not its body location.
        ///
        /// The grid type of a body item is INVENTORY_PlaceItemInGrid's `(bodyLoc >= 11) ? 4 : 3`
        /// (0x63b1e2), and 11/12 are the swap pair (0x55f240) — which is what makes those two
        /// predicates disagree at all.
        ///
        /// Anything carried but NOT equipped is treated as a plain carried grid, i.e. owned. Which
        /// locations the game actually stamps as type 1 is UNTRACED, so a producer that puts stash
        /// or cube contents in <see cref="IUnit.Items"/> gets them counted as owned; that affects
        /// the piece list's colour only, never a bonus tier.
        ///
        /// No recursion: a filler is one level below a carried item and no set item is socketable.
        /// </summary>
        internal SetItemTooltipInput SetStateOf(IUnit item, IUnit viewer)
        {
            if (item == null) throw new ArgumentNullException("item");

            var input = new SetItemTooltipInput();
            input.IsEquipped = item.Location == LocationEquipped;

            SetItemRecord self = item.Quality == (int)ItemQuality.Set
                ? Sets.PieceAt(item.FileIndex)
                : null;

            if (self == null || viewer == null)
            {
                return input;
            }

            var owned = new List<int>();
            int worn = 0;

            foreach (CarriedSetPiece carried in CarriedSetPieces(viewer))
            {
                if (carried.Piece.SetId != self.SetId)
                {
                    continue;
                }

                owned.Add(carried.Unit.FileIndex);

                if (carried.Worn)
                {
                    worn |= 1 << carried.Piece.Slot;
                }
            }

            // The hovered piece's own bit comes from the ITEM, not from the list. Nothing obliges a
            // caller to repeat the hovered item inside the viewer's items — "what else the player
            // is carrying" is the natural reading of a list passed ALONGSIDE the item — and taking
            // the bit only from the list silently dropped a tier when they did not. OR is
            // idempotent, so listing it as well changes nothing.
            if (self.Slot >= 0 && IsWorn(item))
            {
                worn |= 1 << self.Slot;
            }

            if (!owned.Contains(item.FileIndex) && IsOwned(item))
            {
                owned.Add(item.FileIndex);
            }

            input.OwnedSetItemIds = owned;
            input.WornMaskIncludingSelf = worn;

            // Now a genuine inverse: self's bit is set above whenever it is worn, so clearing it
            // here is the only difference between the two masks, by construction.
            input.WornMaskExcludingSelf = self.Slot >= 0 ? worn & ~(1 << self.Slot) : worn;

            return input;
        }

        /// <summary>
        /// One carried set piece, with the OWNED / WORN distinction already made. Both derivations
        /// walk the viewer the same way and differ only in which of the two they read, so the walk
        /// and the two predicates live here once rather than being restated per caller.
        /// </summary>
        private sealed class CarriedSetPiece
        {
            public CarriedSetPiece(IUnit unit, SetItemRecord piece, bool worn)
            {
                Unit = unit;
                Piece = piece;
                Worn = worn;
            }

            public readonly IUnit Unit;
            public readonly SetItemRecord Piece;

            /// <summary>
            /// Grid type 3, which is what the worn mask requires (0x62a3f0). Everything yielded here
            /// is OWNED — GetSetItem takes types 1, 3 and 4 (0x4867d4) — so a piece on the alternate
            /// weapon set arrives with this false and counts for the piece list but no bonus.
            /// </summary>
            public readonly bool Worn;
        }

        /// <summary>
        /// GetSetItem's non-set tests: identified (0x4867a2) and on a page it walks
        /// (0x4867b3-0x4867bf). Quality and the setitems lookup are the caller's part.
        /// </summary>
        private static bool IsOwned(IUnit unit)
        {
            return unit.Has(ItemRecordFlags.Identified) && OnAnOwningPage(unit.Location);
        }

        /// <summary>
        /// What ITEMS_GetEquippedSetItemsMask counts: grid type 3 (0x62a3f0), which for a body item
        /// is `bodyLoc &lt; 11` (0x63b1e2), and neither refused flag (0x62a446).
        /// </summary>
        private static bool IsWorn(IUnit unit)
        {
            return unit.Location == LocationEquipped
                && GridTypeOfBodyItem(unit.X) == 3
                && ((uint)unit.ItemFlags & BrokenOrUnequippable) == 0;
        }

        private IEnumerable<CarriedSetPiece> CarriedSetPieces(IUnit viewer)
        {
            foreach (IUnit carried in viewer.Items)
            {
                // GetSetItem 0x486770 takes quality 5 (0x486790) that is IDENTIFIED
                // (CheckItemFlag 0x10, 0x4867a2). Every set item drops unidentified, so a sibling
                // just picked up is the normal case and the game paints it red.
                if (carried == null
                    || carried.Quality != (int)ItemQuality.Set
                    || !IsOwned(carried))
                {
                    continue;
                }

                SetItemRecord piece = Sets.PieceAt(carried.FileIndex);
                if (piece == null || piece.Slot < 0)
                {
                    continue;
                }

                // The mask additionally refuses flag 0x100 and flag 0x4000 (0x62a446) — a broken
                // piece grants no bonus even while worn, and it is already drawn red by name.
                yield return new CarriedSetPiece(carried, piece, IsWorn(carried));
            }
        }

        /// <summary>pItemData item location 1, the body. See <see cref="IUnit.Location"/>.</summary>
        private const int LocationEquipped = 1;

        /// <summary>
        /// The mask's two refusals, 0x62a446. 0x4000 has no name in
        /// <see cref="ItemRecordFlags"/> — <see cref="SocketStatSynthesis"/> spells it the same way
        /// for the recalc loop's identical pair.
        /// </summary>
        private const uint BrokenOrUnequippable = (uint)ItemRecordFlags.Broken | 0x4000;

        /// <summary>
        /// Whether a location can hold a piece GetSetItem would find. It walks the viewer's
        /// inventory and takes pages 0 / 3 / 4 / 0xFF (0x4867b3-0x4867bf), which excludes the TRADE
        /// page; ground and store are not in that chain at all. The location-to-page mapping is by
        /// name rather than traced, so only these three obvious exclusions are made — the rest fall
        /// through as owned, which affects the piece list's colour and never a bonus tier.
        /// </summary>
        private static bool OnAnOwningPage(int location)
        {
            const int Ground = 0, Store = 4, Trade = 5;

            return location != Ground && location != Store && location != Trade;
        }

        /// <summary>
        /// INVENTORY_PlaceItemInGrid 0x63b1e2: `cmp bodyLoc, 0Bh / setnl cl / add cl, 3`, so a body
        /// item is grid type 3 except on the alternate weapon set, which is 4.
        /// </summary>
        private static int GridTypeOfBodyItem(int bodyLocation)
        {
            return bodyLocation >= 11 ? 4 : 3;
        }

        /// <summary>
        /// ITEM_BuildSetItemTooltip 0x48d1d0, for an IDENTIFIED set item — the tooltip LoadItemDesc
        /// diverts to at 0x48e432 instead of building the generic one.
        ///
        /// <paramref name="set"/> supplies only what the item's own record cannot: which siblings
        /// the viewer is carrying, the two worn masks, whether this piece is equipped, and the
        /// full-set stat block. <see cref="SetStateOf"/> works all of that out from a viewer, and is
        /// what <see cref="Render"/> routes through — pass an input here only to force a state the
        /// viewer does not actually have.
        ///
        /// Throws when the item is not an identified set item.
        /// </summary>
        internal Tooltip RenderSetItem(
            IUnit item, SetItemTooltipInput set, IUnit viewer = null, TooltipOptions options = null)
        {
            if (item == null) throw new ArgumentNullException("item");
            if (set == null) throw new ArgumentNullException("set");

            TooltipOptions opts = options ?? TooltipOptions.Default;
            bool includeSockets = opts.Sockets == SocketMode.Merged;
            bool discardFillers = set.IsEquipped && opts.ApplyWornSetDiscard;
            Composed composed = Compose(item, viewer, opts, includeSockets, discardFillers);

            if (composed.Kind != ItemTooltipKind.IdentifiedSetItem)
            {
                throw new NotSupportedException(
                    "This item is built by " + composed.Kind +
                    ", not the set-item tooltip path. Call Render instead.");
            }

            if (opts.Ranges != null)
            {
                InstallRangeAnnotations(composed.Composer, item, opts, includeSockets);
            }

            IReadOnlyList<ItemTooltipLine> lines =
                SetItemLines(item, viewer, composed, set);

            if (opts.Sockets == SocketMode.Separated)
            {
                lines = WithSocketBlocks(item, viewer, opts, lines, discardFillers);
            }

            return new Tooltip(
                composed.Kind, lines, composed.Composer, QuestColorOf(composed.Context));
        }

        /// <summary>
        /// The set-item body, shared by <see cref="Render"/> and <see cref="RenderSetItem"/> so
        /// that both reach it with the option handling already applied.
        /// </summary>
        private IReadOnlyList<ItemTooltipLine> SetItemLines(
            IUnit item,
            IUnit viewer,
            Composed composed,
            SetItemTooltipInput set)
        {
            var builder = new SetItemTooltipBuilder(_data, _sets, _items, _types);

            SetItemTooltipContent content = builder.Build(
                item, composed.Identity, composed.Viewer, composed.Stats, set, viewer);

            // GetSetItemsLine returning null returns at 0x48d397 and GetSetsLine at 0x48d3ab, in
            // both cases before a single buffer is appended — the game draws no tooltip at all.
            return content == null
                ? new ItemTooltipLine[0]
                : composed.Composer.ComposeSetItem(
                    composed.Context, content, composed.ModifierStats);
        }

        /// <summary>
        /// How the item's inventory sprite is tinted. Nothing to do with the tooltip text — this
        /// is what paints a set item green or a magic ring blue, and it is here because it is the
        /// same tables and the same record.
        /// </summary>
        public ItemAppearance Appearance(IUnit item)
        {
            if (item == null) throw new ArgumentNullException("item");

            ItemIdentity identity = ItemRecordReader.ReadIdentity(item);

            // Only socket 0 is consulted, and only when it holds a gem.
            ItemIdentity firstSocket = item.Items.Count == 0
                ? null
                : ItemRecordReader.ReadIdentity(item.Items[0]);

            return new ItemAppearance(
                _graphics.Resolve(identity),
                _colors.Resolve(identity, firstSocket),
                _colors.InvTrans(identity.ClassId));
        }

        /// <summary>
        /// What the item demands of a wearer. The strength and dexterity NUMBERS are the same for
        /// everyone — they come from items.txt folded with the item's own stat 91 and the ethereal
        /// discount — but the required LEVEL is viewer-dependent, and so is every `Met` flag. Pass
        /// the viewer to get both; omit it and the numbers are still right while the flags read as
        /// unmet, because a null unit's stats read as 0 (0x625483).
        /// </summary>
        public ItemRequirements Requirements(IUnit item, IUnit viewer = null)
        {
            if (item == null) throw new ArgumentNullException("item");

            ItemIdentity identity = ItemRecordReader.ReadIdentity(item);
            ItemViewer player = viewer == null ? null : ItemRecordReader.ReadViewer(viewer);

            SortedDictionary<int, int> stats =
                ItemStatReader.ReconstructView(item, ItemStatView.Equipped());
            SortedDictionary<int, int> baseStats =
                ItemStatReader.ReconstructView(item, ItemStatView.BaseOnly());
            ItemStatOps.Resolve(stats, baseStats, _data.ItemStatCost);

            SortedDictionary<int, uint> sockets = ItemStatReader.ReadSockets(item);
            List<ItemUnit> socketUnits = ItemRecordReader.ReadSocketUnits(item);

            return new ItemRequirements(
                _requirements.Requirement(identity, "reqstr", stats),
                _requirements.Requirement(identity, "reqdex", stats),
                _level.Calculate(identity, player, stats, socketUnits, sockets),
                _requirements.ClassRestriction(identity),
                _requirements.MetStrength(identity, player, stats),
                _requirements.MetDexterity(identity, player, stats),
                _requirements.MetLevel(identity, player, stats, socketUnits, sockets),
                _requirements.MetClass(identity, player));
        }

        /// <summary>
        /// Every classId whose `type` or `type2` is <paramref name="typeCode"/> or anything under
        /// it — ask for `swor` and get every sword, including the exceptional and elite tiers and
        /// the class-specific sword types that chain up to it.
        ///
        /// This is the descending counterpart to the ascending question the engine itself asks:
        /// both go through the same Equiv1/Equiv2 closure, so membership here and
        /// <see cref="ItemTypeTree.IsOfType"/> cannot disagree.
        /// </summary>
        public IReadOnlyList<int> ClassIdsOfType(string typeCode)
        {
            var found = new List<int>();

            int query = _types.Row(typeCode);
            if (query < 0)
            {
                return found;
            }

            for (int classId = 0; classId < _items.RowCount; ++classId)
            {
                if (_types.IsOfType(
                        _types.Row(_items.PrimaryTypeCode(classId)),
                        _types.Row(_items.SecondaryTypeCode(classId)),
                        query))
                {
                    found.Add(classId);
                }
            }

            return found;
        }

        /// <summary>
        /// The item's modifiers split by where they come from, for a "hold shift" view. This is a
        /// capability the game does not have — it never draws these separately — so unlike
        /// <see cref="Render"/> it cannot be checked against the original. What it does guarantee is
        /// that every line is produced by the same traced writers; only the stat SELECTION differs,
        /// and each selection is one of the views the engine itself uses.
        /// </summary>
        public TooltipBreakdown Breakdown(
            IUnit item, IUnit viewer = null, TooltipOptions options = null)
        {
            if (item == null) throw new ArgumentNullException("item");

            TooltipOptions opts = options ?? TooltipOptions.Default;

            // The item's own reconstruction annotates three of the four buckets. The SOCKET bucket
            // gets its own, built from the fillers' properties, because the item's spans do not
            // describe what a gem contributes — and for a jewel it is the jewel's own affixes that
            // rolled.
            Func<IReadOnlyList<int>, int, string> own = opts.Ranges != null
                ? BuildRangeAnnotation(item, opts, includeSockets: false)
                : null;

            Func<IReadOnlyList<int>, int, string> sockets = opts.Ranges != null
                ? BuildSocketRangeAnnotation(item, opts)
                : null;

            return new TooltipBreakdown(
                Describe(item, viewer, opts, ItemStatReader.ReconstructView(
                    item, ItemStatView.BaseOnly()), own),
                Describe(
                    item, viewer, opts,
                    ItemStatReader.ReconstructView(item, ItemOwnMods()), own),
                Describe(item, viewer, opts, SocketContributions(item), sockets),
                Describe(item, viewer, opts, ItemStatReader.ReconstructView(
                    item, ItemStatView.SetBonuses(false)), own));
        }


        /// <summary>
        /// What the item's stats ADD UP TO — its base array, its own affix / unique / setitems /
        /// runeword nodes, what its socket fillers grant, and op 13 folded in. One entry per
        /// (stat, layer), in the raw encoding the record carries.
        ///
        /// This is the question a stored item answers, and it is NOT the one
        /// <see cref="Render"/> answers. A gem or rune arrives with an empty stat chain, so
        /// without this a caller has no way to see that an Um grants the helm it sits in
        /// `All Resistances +15`; and an item's own Defense is split across its base array and its
        /// affixes with the total written down nowhere, so `31` reads 76 and 45 rather than 121.
        ///
        /// Set BONUSES are excluded by default — they belong to the wearer's other pieces rather
        /// than to this item — and the worn-set filler discard is deliberately ignored; see
        /// <see cref="ItemMergedStats.FillersIgnoredBecauseWorn"/>.
        /// </summary>
        /// <remarks>
        /// Pass an ITEM. `IUnit.Items` carries two relations — socket fillers on an item, carried
        /// gear on a wearer — and this reads it as the first, so handing it a PLAYER folds every
        /// carried item in as though it were socketed.
        /// </remarks>
        public ItemMergedStats MergedStats(IUnit item, MergedStatsOptions options = null)
        {
            if (item == null) throw new ArgumentNullException("item");

            MergedStatsOptions opts = options ?? MergedStatsOptions.Default;

            ItemStatView view = opts.IncludeSetBonuses
                ? ItemStatView.Equipped()
                : ItemStatView.ForSale();

            // BOTH filler channels, because they are disjoint and each covers what the other
            // cannot. The VIEW walks a filler's own captured stat lists, which is the only place a
            // JEWEL's affixes live; Contributions synthesises from gems.txt and returns nothing for
            // any filler that already carries stats, precisely so the two cannot double-count. The
            // synthesis alone reaches neither a jewel nor a server-side capture's fillers.
            view.IncludeSockets = opts.IncludeSockets;

            SortedDictionary<int, int> merged = ItemStatReader.ReconstructView(item, view);
            SortedDictionary<int, int> baseStats =
                ItemStatReader.ReconstructView(item, ItemStatView.BaseOnly());

            // hostIsEquipped FALSE on purpose. Render passes the item's real state here, which is
            // what correctly gives a worn set piece none of its fillers; these totals answer what
            // the item WOULD grant, so an item cannot drop out of a search because something
            // equipped it.
            var synthesised = new SortedDictionary<int, int>();
            if (opts.IncludeSockets)
            {
                synthesised = _socketStats.Contributions(item, false);
                AddInto(merged, synthesised);
            }

            // dropPercents false: `ac%` and the enhanced-damage pair are drawn as their own lines,
            // so a caller indexing modifiers wants them beside the target they resolved onto.
            ItemStatOps.Resolve(merged, baseStats, _data.ItemStatCost, false);

            var stats = new List<MergedStat>();
            var packed = new SortedSet<int>();

            foreach (KeyValuePair<int, int> entry in merged)
            {
                int statId = ItemStatReader.StatFromKey(entry.Key);

                if (RolledStatRange.IsPackedStat(statId))
                {
                    packed.Add(statId);
                    continue;
                }

                if (entry.Value != 0)
                {
                    stats.Add(new MergedStat(
                        statId, ItemStatReader.LayerFromKey(entry.Key), entry.Value));
                }
            }

            // What the SYNTHESIS contributed, not merely that a filler exists: only the synthesis
            // is gated on the recalc discard, so a socketed JEWEL leaves the two views in agreement.
            return new ItemMergedStats(
                stats,
                synthesised.Count != 0
                    && SocketStatSynthesis.FillersAreDiscardedByRecalc(
                        item, item.Location == LocationEquipped),
                new List<int>(packed));
        }

        /// <summary>
        /// What ONE filler grants the host it sits in, so a caller can store it against the filler
        /// rather than only against the total. A gem or rune is synthesised from gems.txt keyed by
        /// the host's `gemapplytype`, which is why the host is needed; a JEWEL carries its own
        /// affixes instead and those are what come back.
        ///
        /// The slot is items.txt `gemapplytype`, and a row that takes no sockets at all still
        /// reads 0 there — the weapon column — so a non-empty result is NOT evidence that the host
        /// is socketable. 235 of the 659 rows with a usable gemapplytype have `gemsockets` 0.
        /// Ask <see cref="Items"/> for `gemsockets` if that is the question. Empty only when the
        /// row's gemapplytype is outside 0..2.
        /// </summary>
        public IReadOnlyList<MergedStat> SocketFillerStats(IUnit filler, IUnit host)
        {
            if (filler == null) throw new ArgumentNullException("filler");
            if (host == null) throw new ArgumentNullException("host");

            // Not the host's real equipped state, matching MergedStats: the question is what the
            // filler contributes, not whether a recalc has currently thrown it away.
            int slot = _socketStats.SlotFor(host, false);
            if (slot < 0)
            {
                return new MergedStat[0];
            }

            SortedDictionary<int, int> contribution = _socketStats.Contribution(filler, slot);
            if (contribution.Count == 0)
            {
                // A jewel's mods are captured on the jewel itself, so synthesising would count them
                // twice; its own modifier view is what it contributes.
                contribution = ItemStatReader.ReconstructView(filler, ItemStatView.Modifiers());
            }

            var stats = new List<MergedStat>();
            foreach (KeyValuePair<int, int> entry in contribution)
            {
                int statId = ItemStatReader.StatFromKey(entry.Key);

                if (!RolledStatRange.IsPackedStat(statId) && entry.Value != 0)
                {
                    stats.Add(new MergedStat(
                        statId, ItemStatReader.LayerFromKey(entry.Key), entry.Value));
                }
            }

            return stats;
        }
        /// <summary>
        /// The span each of the item's stats could have rolled within, rebuilt from the tables its
        /// own record points at — the affix ids it stores, its UniqueItems or SetItems row, its
        /// runeword, its superior modifier and its socket fillers, plus the base Defense roll.
        ///
        /// Like <see cref="Breakdown"/> this is a capability the game does not have, so it cannot be
        /// checked against the original. What it CAN be checked against is the item's own recorded
        /// values, which must fall inside the spans claimed for them —
        /// <see cref="ItemRollRanges.OutOfRange"/> is empty whenever that holds.
        ///
        /// Set BONUSES are excluded: they belong to the worn set rather than to this item, and
        /// pass <paramref name="earnedSetIds"/> to fold them in.
        /// </summary>
        public ItemRollRanges Ranges(IUnit item, IEnumerable<int> earnedSetIds = null)
        {
            if (item == null) throw new ArgumentNullException("item");

            // Not equipped, matching Breakdown's socket view: an equipped host's fillers are
            // discarded by recalc, which would drop the very properties being ranged.
            return _ranges.Reconstruct(
                ItemRecordReader.ReadIdentity(item),
                RecordedForComparison(item),
                AllSocketProperties(item),
                earnedSetIds);
        }

        /// <summary>
        /// What <see cref="ItemRollRanges.OutOfRange"/> and
        /// <see cref="ItemRollRanges.Unattributed"/> are checked against: the item's own modifiers,
        /// plus the DEFENSE total.
        ///
        /// Defense is the one stat whose span includes a base roll, and the modifier view has no
        /// base group — so stat 31 was absent from the comparand and the check silently skipped the
        /// only stat that could disagree with its span. A Skin of the Vipermagi reading
        /// "Defense: 279 [244-277]" (Serpentskin 111..126 under a fixed 120% ED gives 244..277, so
        /// 279 needs a base of 127) reported OutOfRange empty, which is the opposite of the signal
        /// it exists to give.
        ///
        /// The total is the op-resolved equipped value — the number the Defense line draws — because
        /// the span is op-resolved too.
        /// </summary>
        private SortedDictionary<int, int> RecordedForComparison(IUnit item)
        {
            SortedDictionary<int, int> recorded =
                ItemStatReader.ReconstructView(item, ItemOwnMods());

            SortedDictionary<int, int> equipped =
                ItemStatReader.ReconstructView(item, ItemStatView.Equipped());
            SortedDictionary<int, int> baseStats =
                ItemStatReader.ReconstructView(item, ItemStatView.BaseOnly());

            ItemStatOps.Resolve(equipped, baseStats, _data.ItemStatCost);

            int key = ItemStatReader.PackStatKey(0, StatDefense);

            int total;
            if (equipped.TryGetValue(key, out total))
            {
                recorded[key] = total;
            }

            return recorded;
        }

        private const int StatDefense = 31;

        /// <summary>
        /// The same reconstruction, with the earned sets taken FROM THE VIEWER rather than listed
        /// by hand — the counterpart of <see cref="SetStateOf"/>, and sharing its worn-piece rule so
        /// the two entry points cannot disagree about which tiers a character has.
        ///
        /// A set counts as earned once two of its pieces are worn, which is the point `add func`
        /// 2 lights its first tier (0x4e65b2 gives N worn pieces tiers 0..N-2).
        /// </summary>
        public ItemRollRanges RangesForViewer(IUnit item, IUnit viewer)
        {
            if (item == null) throw new ArgumentNullException("item");

            return Ranges(item, EarnedSetIdsOf(viewer));
        }

        /// <summary>
        /// Set ids the viewer wears at least two pieces of. Uses the worn predicate, not the owned
        /// one: a piece on the alternate weapon set grants no bonus, so it must not raise the count.
        /// </summary>
        internal IReadOnlyList<int> EarnedSetIdsOf(IUnit viewer)
        {
            var earned = new List<int>();
            if (viewer == null)
            {
                return earned;
            }

            // A MASK per set, not a count. The game ORs `1 << slot` (0x62a474), so two copies of the
            // same piece light one bit and count once — and two rings is not a hypothetical: both
            // Cathan's Seal and Angelic Halo are `rin`, and a character has two ring slots. Counting
            // units instead would earn a tier off a single duplicated piece and put set-bonus spans
            // on an item that has none.
            var wornPerSet = new Dictionary<int, int>();

            foreach (CarriedSetPiece carried in CarriedSetPieces(viewer))
            {
                if (!carried.Worn)
                {
                    continue;
                }

                int mask;
                wornPerSet.TryGetValue(carried.Piece.SetId, out mask);
                wornPerSet[carried.Piece.SetId] = mask | (1 << carried.Piece.Slot);
            }

            foreach (KeyValuePair<int, int> entry in wornPerSet)
            {
                if (SetBonusTiers.PopCount(entry.Value) >= 2)
                {
                    earned.Add(entry.Key);
                }
            }

            earned.Sort();
            return earned;
        }

        /// <summary>
        /// What every filler contributes, as properties. A gem or rune lends the host its gems.txt
        /// mods; a JEWEL lends its own affix rolls, which gems.txt knows nothing about.
        ///
        /// Both belong here because the merged render draws ONE line per stat holding the SUM — so
        /// its span has to be the sum of both spans. Leaving the jewel out gave a line reading
        /// "Fire Resist +28% [11-20]", where 28 was item plus jewel but 11-20 was the item alone.
        /// </summary>
        private List<ItemProperty> AllSocketProperties(IUnit item)
        {
            var properties = new List<ItemProperty>(_socketStats.FillerProperties(item, false));

            int slot = _socketStats.SlotFor(item, false);
            if (slot < 0)
            {
                return properties;
            }

            foreach (IUnit filler in item.Items)
            {
                // A filler the synthesis has nothing to say about is one carrying its own stats,
                // and its affixes are the roll.
                if (_socketStats.Contribution(filler, slot).Count != 0)
                {
                    continue;
                }

                properties.AddRange(
                    _ranges.OwnProperties(ItemRecordReader.ReadIdentity(filler)));
            }

            return properties;
        }

        /// <summary>
        /// The item's own affixes with the fillers left out. NOT <see cref="ItemStatView.ItemOnly"/>,
        /// which requires STATLIST_EXTENDED *or* MAGIC and so drags the base array in with it.
        /// </summary>
        private static ItemStatView ItemOwnMods()
        {
            ItemStatView view = ItemStatView.Modifiers();
            view.IncludeSockets = false;
            return view;
        }

        /// <summary>
        /// Only what the fillers contribute. No view expresses this — <see cref="ItemStatView"/>
        /// can drop socket groups but not keep only them — so it is the union of each filler viewed
        /// as an item in its own right, which is what self-similarity makes correct.
        /// </summary>
        private SortedDictionary<int, int> SocketContributions(IUnit item)
        {
            var merged = new SortedDictionary<int, int>();

            foreach (IUnit socket in item.Items)
            {
                AddInto(merged, ItemStatReader.ReconstructView(socket, ItemStatView.Modifiers()));
            }

            // Same reason as in Compose: a captured gem or rune has no chain of its own.
            AddInto(merged, _socketStats.Contributions(item, false));

            return merged;
        }

        private static void AddInto(
            IDictionary<int, int> into, IEnumerable<KeyValuePair<int, int>> from)
        {
            foreach (KeyValuePair<int, int> stat in from)
            {
                int existing;
                into[stat.Key] = into.TryGetValue(stat.Key, out existing)
                    ? existing + stat.Value
                    : stat.Value;
            }
        }

        private IReadOnlyList<ItemTooltipLine> Describe(
            IUnit item,
            IUnit viewer,
            TooltipOptions options,
            SortedDictionary<int, int> selected,
            Func<IReadOnlyList<int>, int, string> annotation = null)
        {
            Composed composed = Compose(item, viewer, options, true);

            // The composer built for THIS selection, so the generator's value source and the
            // block's colour carry match what a full render of the same stats would produce.
            var composer = new ItemTooltipComposer(
                composed.Sections, composed.Sections.CreateModifierGenerator(selected));

            if (annotation != null)
            {
                composer.RangeAnnotation = annotation;
                composer.RangeColor = options.Ranges.Color;
            }

            return composer.ComposeModifiersOnly(selected);
        }

        private struct Composed
        {
            public RecordSections Sections;
            public ItemTooltipComposer Composer;
            public ItemTooltipContext Context;
            public ItemTooltipKind Kind;
            public SortedDictionary<int, int> ModifierStats;
            public ItemIdentity Identity;
            public ItemViewer Viewer;
            public SortedDictionary<int, int> Stats;
        }

        /// <summary>
        /// 0x48ec3f: the trailing quest-colour marker is on when the item's items.txt row has the
        /// `quest` byte set, EXCEPT for Wirt's Leg, which 0x48ec52 excludes by code.
        /// </summary>
        private static bool QuestColorOf(ItemTooltipContext context)
        {
            return context.IsQuestItem && !context.IsWirtsLeg;
        }

        private Composed Compose(
            IUnit item, IUnit viewer, TooltipOptions options, bool includeSockets,
            bool hostIsEquipped = false)
        {
            ItemIdentity identity = ItemRecordReader.ReadIdentity(item);
            ItemViewer player = viewer == null ? null : ItemRecordReader.ReadViewer(viewer);

            ItemStatView equipped = ItemStatView.Equipped();
            ItemStatView modifiers = ItemStatView.Modifiers();
            equipped.IncludeSockets = includeSockets;
            modifiers.IncludeSockets = includeSockets;

            SortedDictionary<int, int> stats = ItemStatReader.ReconstructView(item, equipped);
            SortedDictionary<int, int> baseStats =
                ItemStatReader.ReconstructView(item, ItemStatView.BaseOnly());
            SortedDictionary<int, int> modifierStats =
                ItemStatReader.ReconstructView(item, modifiers);

            // A client capture hands over gems and runes with no stat chain — the mods are assigned
            // in D2Common/D2Game and the client only ever sees the host's merged result. Rebuild
            // them from gems.txt so the host's blue block is not silently short of its fillers.
            if (includeSockets)
            {
                SortedDictionary<int, int> synthesised =
                    _socketStats.Contributions(item, hostIsEquipped);
                AddInto(stats, synthesised);
                AddInto(modifierStats, synthesised);
            }

            // The capture is leaf-per-list, so op 13 is folded back in here rather than by the
            // producer. Without it every by-time stat reads its unresolved value.
            ItemStatOps.Resolve(stats, baseStats, _data.ItemStatCost);

            var sections = new RecordSections(
                _data, _items, _types, identity, player, stats,
                includeSockets
                    ? ItemStatReader.ReadSockets(item)
                    : new SortedDictionary<int, uint>(),
                baseStats,
                includeSockets ? ItemRecordReader.ReadSocketUnits(item) : new List<ItemUnit>(),
                options.ClientPlayer == null
                    ? null
                    : ItemRecordReader.ReadViewer(options.ClientPlayer));

            var composer = new ItemTooltipComposer(
                sections, sections.CreateModifierGenerator(modifierStats));

            var composed = new Composed();
            composed.Sections = sections;
            composed.Composer = composer;
            composed.Context = sections.CreateContext(options.Difficulty);
            composed.Context.ShopMode = options.ShopMode;
            composed.Kind = ItemTooltipComposer.Classify(composed.Context);
            composed.ModifierStats = modifierStats;
            composed.Identity = identity;
            composed.Viewer = player;
            composed.Stats = stats;
            return composed;
        }
    }

    /// <summary>What an item demands of a wearer, and whether this viewer meets it.</summary>
    public sealed class ItemRequirements
    {
        internal ItemRequirements(
            int strength, int dexterity, int level, int classRestriction,
            bool metStrength, bool metDexterity, bool metLevel, bool metClass)
        {
            Strength = strength;
            Dexterity = dexterity;
            Level = level;
            ClassRestriction = classRestriction;
            MetStrength = metStrength;
            MetDexterity = metDexterity;
            MetLevel = metLevel;
            MetClass = metClass;
        }

        /// <summary>items.txt reqstr, folded with stat 91 and the ethereal discount. 0 means none.</summary>
        public int Strength { get; private set; }

        /// <summary>items.txt reqdex, the same way. 0 means none.</summary>
        public int Dexterity { get; private set; }

        /// <summary>
        /// The required level. Viewer-dependent: a classic unique shows none to a non-expansion
        /// viewer (0x62b877), and a class-restricted affix charges its own class `classlevelreq`
        /// instead of `levelreq`.
        /// </summary>
        public int Level { get; private set; }

        /// <summary>The character class id an item type is restricted to, or <see cref="EquipRequirements.NoClassRestriction"/>.</summary>
        public int ClassRestriction { get; private set; }

        public bool MetStrength { get; private set; }
        public bool MetDexterity { get; private set; }
        public bool MetLevel { get; private set; }
        public bool MetClass { get; private set; }

        /// <summary>True when the viewer satisfies all four.</summary>
        public bool AllMet
        {
            get { return MetStrength && MetDexterity && MetLevel && MetClass; }
        }
    }

    /// <summary>How an item's inventory sprite is painted.</summary>
    public sealed class ItemAppearance
    {
        internal ItemAppearance(string image, int color, int invTrans)
        {
            Image = image;
            Color = color;
            InvTrans = invTrans;
        }

        /// <summary>
        /// The inventory sprite name, without extension — a renderer fetches `Image + ".dc6"`.
        /// NOT the item code: exceptional and elite tiers collapse to the base tier, set and
        /// unique items get their own art, and rings/amulets/jewels/charms carry a 1-based
        /// variant suffix.
        /// </summary>
        public string Image { get; private set; }

        /// <summary>
        /// The palette-shift index, 0-20, or -1 for none. 0 is `whit` and 20 is `bwht`; the codes
        /// are colors.txt row order.
        /// </summary>
        public int Color { get; private set; }

        /// <summary>
        /// items.txt InvTrans — which transform table the shift indexes, NOT a colour. Zero on
        /// most items, and that is what stops them being tinted at all, so a renderer gates on
        /// this rather than on <see cref="Color"/> alone.
        /// </summary>
        public int InvTrans { get; private set; }

        /// <summary>True when there is a shift to apply and a table to apply it to.</summary>
        public bool IsTinted
        {
            get { return Color >= 0 && InvTrans != 0; }
        }
    }

    /// <summary>Per-render knobs. Everything else is unit state and comes off the record.</summary>
    /// <summary>
    /// What a render does with the item's socket fillers.
    /// </summary>
    public enum SocketMode
    {
        /// <summary>
        /// What the game draws: the fillers' stats folded into the item's own block, so
        /// `Fire Resist +28%` is the item's 20 plus a jewel's 8 on one line.
        /// </summary>
        Merged = 0,

        /// <summary>
        /// The item as if nothing were socketed in it. The game never draws this; it exists so a
        /// caller can show what the base item is worth on its own — deciding whether to unsocket,
        /// or valuing a runeword base.
        /// </summary>
        Excluded = 1,

        /// <summary>
        /// The item WITHOUT what its fillers contribute, then one block per filler below it, each
        /// carrying <see cref="ItemTooltipSection.SocketContribution"/>, so a reader can tell which
        /// gem or rune is responsible for what. The game never draws this; the fillers are moved,
        /// not dropped.
        /// </summary>
        Separated = 2,
    }

    /// <summary>
    /// Turns on the rolled-range annotation and carries how it is written. Null — the default —
    /// leaves it off, which is what keeps an ordinary render byte-identical to the game.
    ///
    /// </summary>
    public sealed class RangeDisplay
    {
        /// <summary>
        /// How a span is written. Null uses <see cref="TooltipEngine.DefaultRangeAnnotation"/>,
        /// which gives ` [5-15]`. Return null or empty to suppress ONE span, which is how you show
        /// ranges for some stats and not others.
        /// </summary>
        public Func<IReadOnlyList<RolledStatRange>, string> Format;

        /// <summary>
        /// The <see cref="ItemTooltipColor"/> to paint it, or -1 to inherit the line's. A marker
        /// restoring the line's colour follows, so nothing after it is affected.
        ///
        /// The game's grey rather than -1, because a range is an annotation the game never draws
        /// and inheriting the stat line's blue made it read as part of the line.
        /// </summary>
        public int Color = ItemTooltipColor.SocketedOrEthereal;
    }

    public sealed class TooltipOptions
    {
        internal static readonly TooltipOptions Default = new TooltipOptions();

        /// <summary>
        /// GetDificulity() (0x48cb38) — the one input that is game state rather than unit state.
        /// Only a quest item with questdiffcheck set reads it.
        /// </summary>
        public int Difficulty;

        /// <summary>
        /// 0 outside a shop. 1-9 add the transaction-cost line, and any non-zero value suppresses
        /// both usage lines (0x48d082 tests for exactly zero).
        /// </summary>
        public int ShopMode;

        /// <summary>What the render does with the socket fillers.</summary>
        public SocketMode Sockets = SocketMode.Merged;

        /// <summary>
        /// Non-null annotates each stat line with the span it could have rolled within — the same
        /// numbers <see cref="TooltipEngine.Ranges"/> returns, written inline. The game has no such
        /// mode, so this makes the output deliberately NOT byte-identical.
        ///
        /// Only lines that display one stat are annotated: every modifier, plus the Defense line.
        /// </summary>
        public RangeDisplay Ranges;

        /// <summary>
        /// False renders a WORN set piece as though its socket fillers still applied.
        ///
        /// ITEM_RecalcAllEquippedItems 0x4c1350 detaches an equipped set item's stat list and
        /// rebuilds it through ITEM_ProcessSetItemEquip; nothing re-applies the fillers, so the game
        /// grants a worn Tal Rasha's Horadric Crest with an Um in it `All Resistances +15` rather
        /// than 30. Reproducing that is the DEFAULT and stays the default.
        ///
        /// Turn it off when the question is "what is this item worth" rather than "what is it giving
        /// right now" — a stash or mule view, where an item must not appear to lose its rune because
        /// something equipped it. That is the position <see cref="TooltipEngine.MergedStats"/> takes
        /// unconditionally; this is the same choice, made explicit for the render.
        ///
        /// It affects the FILLERS only. Whether the piece is equipped still decides the full-set
        /// block, the worn mask that lights the bonus tiers, and the piece list's colours, because
        /// those are facts about the wearer rather than about the sockets.
        ///
        /// Like <see cref="SocketMode.Separated"/> and a non-null <see cref="Ranges"/>, false is a
        /// deliberate departure from what the game draws.
        /// </summary>
        public bool ApplyWornSetDiscard = true;

        /// <summary>
        /// The CLIENT PLAYER, when that is a different unit from the viewer — i.e. a mercenary's
        /// panel. Almost every caller leaves this null.
        ///
        /// The game reads two units. Requirements, the class restriction, block chance and the
        /// smite gate all use LoadItemDesc's own unit (0x48dee0), which on a merc panel IS the
        /// merc. But INV_FormatAttackSpeedText ignores it and calls GetPlayerUnit_0 (0x463de0)
        /// twice — once for the frame lookup at 0x486201 and once for the speed bucket's class
        /// offset at 0x486250 — so a merc's weapon is timed against the CHARACTER. That is not a
        /// quirk we can derive: it needs the second unit.
        ///
        /// Null means "same as the viewer", which is correct everywhere else.
        /// </summary>
        public IUnit ClientPlayer;
    }

    /// <summary>A rendered tooltip. The lines are in DISPLAY order, top row first.</summary>
    public sealed class Tooltip
    {
        private readonly ItemTooltipComposer _composer;
        private readonly bool _questColorPrefix;

        internal Tooltip(
            ItemTooltipKind kind,
            IReadOnlyList<ItemTooltipLine> lines,
            ItemTooltipComposer composer,
            bool questColorPrefix)
        {
            Kind = kind;
            Lines = lines;
            _composer = composer;

            // DERIVED, not a knob. 0x48ec3f gates the marker on the items.txt `quest` byte of the
            // item's own row (+0x12A) and excludes Wirt's Leg by code (0x48ec52, 'leg '); nothing
            // the caller supplies reaches it.
            _questColorPrefix = questColorPrefix;
        }

        /// <summary>Which of the game's tooltip builders produced this.</summary>
        public ItemTooltipKind Kind { get; private set; }

        public IReadOnlyList<ItemTooltipLine> Lines { get; private set; }

        /// <summary>
        /// The plain text, newline separated. Markers a section writer embedded in its own text
        /// survive — the game embeds those too.
        /// </summary>
        public string Text
        {
            get { return _composer.Render(Lines, _questColorPrefix, MaxLength); }
        }

        /// <summary>
        /// The 1023-wide-char cut LoadItemDesc applies at 0x48ed12 — except on the set-item path,
        /// which never takes it: ITEM_BuildSetItemTooltip runs from MoveArgumentToEAX (0x48db0b)
        /// straight to TEXT_CalcTextDimensions (0x48db1d) over a 2048-WCHAR buffer with no guard.
        /// </summary>
        private int MaxLength
        {
            get
            {
                return Kind == ItemTooltipKind.IdentifiedSetItem
                    ? ItemTooltipComposer.UnlimitedTooltipLength
                    : ItemTooltipComposer.MaxTooltipLength;
            }
        }

        /// <summary>
        /// The text with the per-line U+00FF 'c' N colour markers the game paints with. Both forms
        /// spend the same 1023-character budget, so a long tooltip truncates where the game
        /// truncates.
        /// </summary>
        public string ColoredText
        {
            get
            {
                return _composer.RenderWithColorCodes(
                    Lines, ItemTooltipColor.Marker, _questColorPrefix, MaxLength);
            }
        }

        public override string ToString()
        {
            return Text;
        }
    }

    /// <summary>
    /// The item's modifiers grouped by where they come from. See
    /// <see cref="TooltipEngine.Breakdown"/> for why this is not a fidelity feature.
    /// </summary>
    public sealed class TooltipBreakdown
    {
        internal TooltipBreakdown(
            IReadOnlyList<ItemTooltipLine> baseStats,
            IReadOnlyList<ItemTooltipLine> magic,
            IReadOnlyList<ItemTooltipLine> sockets,
            IReadOnlyList<ItemTooltipLine> setBonuses)
        {
            Base = baseStats;
            Magic = magic;
            Sockets = sockets;
            SetBonuses = setBonuses;
        }

        /// <summary>The base array — what every copy of this item type carries.</summary>
        public IReadOnlyList<ItemTooltipLine> Base { get; private set; }

        /// <summary>The item's own affixes, unique/set mods and runeword, sockets excluded.</summary>
        public IReadOnlyList<ItemTooltipLine> Magic { get; private set; }

        /// <summary>What the socket fillers add.</summary>
        public IReadOnlyList<ItemTooltipLine> Sockets { get; private set; }

        /// <summary>Earned set tiers only. Unearned tiers are excluded, as the game excludes them.</summary>
        public IReadOnlyList<ItemTooltipLine> SetBonuses { get; private set; }
    }
}
