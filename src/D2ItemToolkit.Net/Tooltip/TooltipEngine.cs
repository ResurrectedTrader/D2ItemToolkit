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
            Composed composed = Compose(item, viewer, opts, opts.IncludeSockets);

            if (composed.Kind == ItemTooltipKind.IdentifiedSetItem)
            {
                // Nothing in the item document says which siblings the viewer owns, so the default
                // input is "none", which paints every piece red and selects no tier — exactly what
                // the game draws for a character carrying this piece alone.
                return RenderSetItem(item, new SetItemTooltipInput(), viewer, opts);
            }

            IReadOnlyList<ItemTooltipLine> lines =
                composed.Kind == ItemTooltipKind.Book
                    ? composed.Composer.ComposeBook(composed.Context)
                    : composed.Composer.Compose(composed.Context, composed.ModifierStats);

            return new Tooltip(composed.Kind, lines, composed.Composer, opts);
        }

        /// <summary>
        /// ITEM_BuildSetItemTooltip 0x48d1d0, for an IDENTIFIED set item — the tooltip LoadItemDesc
        /// diverts to at 0x48e432 instead of building the generic one.
        ///
        /// <paramref name="set"/> supplies only what the item's own record cannot: which siblings
        /// the viewer is carrying, the two worn masks, whether this piece is equipped, and the
        /// full-set stat block. The piece names, their order, the set name, `add func` and the
        /// partial-bonus stats are all derived here.
        ///
        /// Throws when the item is not an identified set item; <see cref="Render"/> classifies for
        /// you and routes to this automatically.
        /// </summary>
        public Tooltip RenderSetItem(
            IUnit item, SetItemTooltipInput set, IUnit viewer = null, TooltipOptions options = null)
        {
            if (item == null) throw new ArgumentNullException("item");
            if (set == null) throw new ArgumentNullException("set");

            TooltipOptions opts = options ?? TooltipOptions.Default;
            Composed composed = Compose(item, viewer, opts, opts.IncludeSockets, set.IsEquipped);

            if (composed.Kind != ItemTooltipKind.IdentifiedSetItem)
            {
                throw new NotSupportedException(
                    "This item is built by " + composed.Kind +
                    ", not the set-item tooltip path. Call Render instead.");
            }

            var builder = new SetItemTooltipBuilder(_data, _sets, _items, _types);

            SetItemTooltipContent content = builder.Build(
                item, composed.Identity, composed.Viewer, composed.Stats, set, viewer);

            // GetSetItemsLine returning null returns at 0x48d397 and GetSetsLine at 0x48d3ab, in
            // both cases before a single buffer is appended — the game draws no tooltip at all.
            IReadOnlyList<ItemTooltipLine> lines = content == null
                ? new ItemTooltipLine[0]
                : composed.Composer.ComposeSetItem(
                    composed.Context, content, composed.ModifierStats);

            return new Tooltip(composed.Kind, lines, composed.Composer, opts);
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
            ItemIdentity firstSocket = item.Sockets.Count == 0
                ? null
                : ItemRecordReader.ReadIdentity(item.Sockets[0]);

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

            return new TooltipBreakdown(
                Describe(item, viewer, opts, ItemStatReader.ReconstructView(
                    item, ItemStatView.BaseOnly())),
                Describe(item, viewer, opts, ItemStatReader.ReconstructView(item, ItemOwnMods())),
                Describe(item, viewer, opts, SocketContributions(item)),
                Describe(item, viewer, opts, ItemStatReader.ReconstructView(
                    item, ItemStatView.SetBonuses(false))));
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

            foreach (IUnit socket in ItemStatReader.EnumerateSockets(item))
            {
                AddInto(merged, ItemStatReader.ReconstructView(socket, ItemStatView.Modifiers()));
            }

            // Same reason as in Compose: a captured gem or rune has no chain of its own.
            AddInto(merged, _socketStats.Contributions(item));

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
            SortedDictionary<int, int> selected)
        {
            Composed composed = Compose(item, viewer, options, true);

            // The composer built for THIS selection, so the generator's value source and the
            // block's colour carry match what a full render of the same stats would produce.
            var composer = new ItemTooltipComposer(
                composed.Sections, composed.Sections.CreateModifierGenerator(selected));

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

        /// <summary>
        /// False renders the item as if nothing were socketed in it. The game has no such mode;
        /// this exists so a caller can show what the base item is worth on its own.
        /// </summary>
        public bool IncludeSockets = true;

        /// <summary>Appends the trailing quest-colour marker (0x48d1e2).</summary>
        public bool QuestColorPrefix;

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
            TooltipOptions options)
        {
            Kind = kind;
            Lines = lines;
            _composer = composer;

            // Snapshot, not a reference to the options object. Lines are composed eagerly, so
            // every other knob is already baked in; leaving this one live meant mutating the
            // caller's TooltipOptions AFTER Render changed what Text returned, and TypeScript
            // captures it by value. A rendered tooltip should not change under the caller.
            _questColorPrefix = options.QuestColorPrefix;
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
