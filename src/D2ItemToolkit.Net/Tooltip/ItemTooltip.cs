using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace D2ItemToolkit
{
    // Mirrors LoadItemDesc (0x48dd90).
    //
    // APPEND ORDER IS NOT DISPLAY ORDER. The game concatenates 18 stack buffers top to bottom,
    // but D2WINFONT_DrawWideString steps the cursor UPWARDS on every newline (0x501c17), so the
    // last buffer appended is the TOP line. Compose reverses into display order. Nearly every bug
    // this file has had came from applying an append-order fact — a colour marker, a terminator,
    // the 1023 cut — to the display-order list or the reverse.

    public enum ItemTooltipSection
    {
        /// <summary>
        /// Not a section the game has. Every line carries a real one by the time Compose returns;
        /// this is only the pre-assignment state, and it exists so that `default` is nameable in
        /// both implementations — the TypeScript enum is string-valued and so has no zero.
        /// Enumerators over this type skip it.
        /// </summary>
        None = 0,

        EtherealSocketed = 1,

        Modifiers = 2,

        Unidentified = 3,

        AttackSpeed = 4,

        RequiredLevel = 5,

        RequiredStrength = 6,

        RequiredDexterity = 7,

        ClassRestriction = 8,

        Durability = 9,

        SocketFillerDescription = 10,

        CharmDescription = 11,

        QuantityAndSpellDescription = 12,

        WeaponDamage = 13,

        SmiteOrKickDamage = 14,

        BlockChance = 15,

        ArmorClass = 16,

        RuneLetters = 17,

        ItemName = 18,

        TransactionCost = 19,

        /// <summary>
        /// 0x48ec3f: prepended to the FINISHED buffer, so it renders as the BOTTOM row and is
        /// therefore appended ahead of everything else.
        /// </summary>
        QuestUsage = 20,

        // INV_ShowBookTooltip 0x48d060's three lines. Its quantity is NOT the generic one: the
        // call at 0x48d07d has none of the identified/not-socketed gating 0x48e8ef applies.
        BookQuantity = 21,

        BookRightClickToUse = 22,

        BookInsertScrolls = 23,

        // ITEM_BuildSetItemTooltip 0x48d1d0's four extra buffers. Everything else it emits is one
        // of 1-20 above, built by the same writer at the same address.
        SetPieceList = 24,

        SetName = 25,

        FullSetBonus = 26,

        PartialSetBonus = 27,

        /// <summary>
        /// Not a section the game has. One block per socket filler, emitted below the item when
        /// <see cref="TooltipOptions.Sockets"/> is <see cref="SocketMode.Separated"/>, so a reader can tell
        /// what each gem or rune is actually contributing. Never produced otherwise.
        /// </summary>
        SocketContribution = 28,
    }

    public static class ItemTooltipColor
    {
        public const int White = 0;
        public const int Red = 1;
        public const int Set = 2;
        public const int Magic = 3;
        public const int Unique = 4;
        public const int SocketedOrEthereal = 5;
        public const int Crafted = 8;
        public const int Rare = 9;
        public const int Tempered = 10;

        public const int MarkerStringId = 3994;

        public const string Marker = "\u00FFc";
    }

    internal enum ItemQuality
    {
        LowQuality = 1,
        Normal = 2,
        HighQuality = 3,
        Magic = 4,
        Set = 5,
        Rare = 6,
        Unique = 7,
        Crafted = 8,
        Tempered = 9,
    }

    [Flags]
    internal enum ItemTooltipFlags : uint
    {
        None = 0,
        Identified = 0x00000010,
        Broken = 0x00000100,
        Socketed = 0x00000800,
        Ethereal = 0x00400000,
    }

    public enum ItemTooltipKind
    {
        Generic,
        ShopTransaction,

        Transmogrify,

        IdentifiedSetItem,

        Book,
    }

    public sealed class ItemTooltipLine
    {
        public string Text;
        public ItemTooltipSection Section;

        public int Color;

        /// <summary>
        /// The stat this line displays, or -1 when it displays none — a name, a requirement, a
        /// blank. Set for every modifier line and for the Defense line. With
        /// <see cref="Layer"/> it is the key to look the line up in
        /// <see cref="ItemRollRanges.Stats"/>, which is what makes a caller's own range display
        /// possible without re-deriving the mapping.
        /// </summary>
        public int StatId = -1;

        /// <summary>The stat's layer — the skill, class or tab. 0 for a plain stat.</summary>
        public int Layer;

        /// <summary>
        /// Every stat this line displays a number for, in the order the numbers appear. Null means
        /// just <see cref="StatId"/>.
        ///
        /// "Adds 1-4 Cold Damage" is coldmindam and coldmaxdam on one line, and "+2 to All
        /// Attributes" is a DescGrp standing for four. A caller matching lines back to
        /// <see cref="ItemRollRanges.Stats"/> needs all of them, not the first.
        /// </summary>
        public int[] ShownStats;

        /// <summary>
        /// True when the line speaks for more than the one stat in <see cref="StatId"/> — the same
        /// condition <see cref="ShownStats"/> is populated under, exposed as a flag because a
        /// caller usually only wants to know whether one stat is the whole story.
        /// </summary>
        public bool Aggregated;

        public bool EmitsColorMarker;

        /// <summary>
        /// A SECOND marker, emitted in front of this line's own. Exactly one line ever carries it:
        /// the first-appended piece of a set item's piece list, because 0x48d93b prepends `ÿc2` to
        /// the whole var_4790 buffer while every piece inside it already carries a marker of its
        /// own (0x48d907). The outer one paints nothing and is in the game's string regardless.
        /// -1 means none.
        /// </summary>
        internal int LeadingMarkerColor = -1;

        internal ItemTooltipSection? SplicedSection;

        public override string ToString()
        {
            return Text;
        }
    }

    internal sealed class ItemTooltipContext
    {
        public ItemQuality Quality;
        public ItemTooltipFlags Flags;

        public bool ForcesCraftedColor;

        public bool UnidentifiedInShop;

        public bool IsShopTransaction;
        public bool IsTransmogrify;
        public bool IsBook;

        public bool IsQuestItem;

        public bool IsWirtsLeg;

        public bool IsWeaponOrArmorType;

        /// <summary>
        /// IsOfType(item, 51) — itemtypes row 51, `shld`. The generic path never needs it because
        /// its smite/kick and block gates are the writers' own; ITEM_BuildSetItemTooltip wraps both
        /// in one shield test at 0x48d681 and so never emits Kick Damage.
        /// </summary>
        public bool IsShieldType;

        public int ShopMode;
    }

    internal interface IItemTooltipSections
    {
        string LineTerminator { get; }

        // Return the buffer's text verbatim, markers included, TERMINATED; empty when the section
        // does not apply. Provider-supplied sections signal inapplicability by returning empty —
        // that is how Unidentified stays mutually exclusive with Modifiers (0x48e8f6), which the
        // composer gates only because Modifiers is the one section it generates itself.
        //
        // KNOWN LIMIT: GetItemName appends no terminator of its own (0x48ce72), and the cost tail's
        // separator is unconditional (0x48cf7a), so a name whose OWN text ended with a newline
        // would give the game two in a row and a blank row under the price. This interface cannot
        // distinguish that from the terminate-your-own-text convention above, so it is not modelled.
        // Unreachable with stock ENG data.
        string GetSection(ItemTooltipSection section);

        bool IsRequirementUnmet(ItemTooltipSection section);
    }

    internal sealed class ItemTooltipComposer
    {
        private static readonly ItemTooltipSection[] AppendOrder =
        {
            ItemTooltipSection.QuestUsage,
            ItemTooltipSection.EtherealSocketed,
            ItemTooltipSection.Modifiers,
            ItemTooltipSection.Unidentified,
            ItemTooltipSection.AttackSpeed,
            ItemTooltipSection.RequiredLevel,
            ItemTooltipSection.RequiredStrength,
            ItemTooltipSection.RequiredDexterity,
            ItemTooltipSection.ClassRestriction,
            ItemTooltipSection.Durability,
            ItemTooltipSection.SocketFillerDescription,
            ItemTooltipSection.CharmDescription,
            ItemTooltipSection.QuantityAndSpellDescription,
            ItemTooltipSection.WeaponDamage,
            ItemTooltipSection.SmiteOrKickDamage,
            ItemTooltipSection.BlockChance,
            ItemTooltipSection.ArmorClass,
            ItemTooltipSection.RuneLetters,
            ItemTooltipSection.ItemName,
            ItemTooltipSection.TransactionCost,
        };

        /// <summary>
        /// INV_ShowBookTooltip 0x48d060 in append order: quantity (0x48d07d), then — only when
        /// ShopMode is zero (0x48d082) — locale 2203 and 2206 each followed by 3998, then
        /// GetItemName into a 128-wide buffer with no terminator (0x48d0ed). Rendered bottom-up
        /// that gives name, Insert Scrolls, Right Click to Use, Quantity.
        /// </summary>
        private static readonly ItemTooltipSection[] BookAppendOrder =
        {
            ItemTooltipSection.BookQuantity,
            ItemTooltipSection.BookRightClickToUse,
            ItemTooltipSection.BookInsertScrolls,
            ItemTooltipSection.ItemName,
        };

        private readonly IItemTooltipSections _sections;
        private readonly ItemDescriptionGenerator _modifiers;

        public ItemTooltipComposer(IItemTooltipSections sections, ItemDescriptionGenerator modifiers)
        {
            if (sections == null) throw new ArgumentNullException("sections");
            if (modifiers == null) throw new ArgumentNullException("modifiers");

            _sections = sections;
            _modifiers = modifiers;
        }

        public static ItemTooltipKind Classify(ItemTooltipContext context)
        {
            if (context == null) throw new ArgumentNullException("context");

            if (context.IsShopTransaction)
            {
                return ItemTooltipKind.ShopTransaction;
            }

            if (context.IsTransmogrify)
            {
                return ItemTooltipKind.Transmogrify;
            }

            if (context.Quality == ItemQuality.Set
                && (context.Flags & ItemTooltipFlags.Identified) != 0)
            {
                return ItemTooltipKind.IdentifiedSetItem;
            }

            if (context.IsBook)
            {
                return ItemTooltipKind.Book;
            }

            return ItemTooltipKind.Generic;
        }

        /// <summary>
        /// INV_ShowBookTooltip 0x48d060. It shares nothing with the generic path but GetItemName:
        /// no requirement lines, no modifier block, no colour markers anywhere (there is no
        /// AppendAsWideChar in the function, and GetItemName's own colour tail is skipped for
        /// `quest == 0` at 0x48cb0b).
        ///
        /// The shop-mode routing at 0x48d126-0x48d154 — where 1..9 sends the whole buffer through
        /// INV_FormatItemTooltipWithCost — is the same TransactionCost gap the generic path has, so
        /// the cost text is absent here too.
        /// </summary>
        public IReadOnlyList<ItemTooltipLine> ComposeBook(ItemTooltipContext context)
        {
            if (context == null) throw new ArgumentNullException("context");

            ItemTooltipKind kind = Classify(context);
            if (kind != ItemTooltipKind.Book)
            {
                throw new NotSupportedException(
                    "This item is built by " + kind + ", not the book tooltip path.");
            }

            var appended = new List<ItemTooltipLine>();

            foreach (ItemTooltipSection section in BookAppendOrder)
            {
                if (context.ShopMode != 0
                    && (section == ItemTooltipSection.BookRightClickToUse
                        || section == ItemTooltipSection.BookInsertScrolls))
                {
                    continue;
                }

                string text = _sections.GetSection(section);
                if (string.IsNullOrEmpty(text))
                {
                    continue;
                }

                foreach (string row in SplitLines(text))
                {
                    var line = new ItemTooltipLine();
                    line.Text = row;
                    line.Section = section;
                    line.Color = ItemTooltipColor.White;
                    appended.Add(line);
                }
            }

            // Render consumes DISPLAY order and walks it backwards to spend the budget, so the
            // append order built above has to be flipped exactly as the generic path flips it.
            appended.Reverse();

            return appended;
        }

        /// <summary>
        /// The generic accumulator var_2138, in APPEND order — 0x48d514 through 0x48d7c4. The
        /// shared writers behind these eleven buffers are the same functions LoadItemDesc calls,
        /// so only the order and the gating are this writer's own.
        /// </summary>
        private static readonly ItemTooltipSection[] SetGenericAppendOrder =
        {
            ItemTooltipSection.RequiredLevel,
            ItemTooltipSection.RequiredStrength,
            ItemTooltipSection.RequiredDexterity,
            ItemTooltipSection.ClassRestriction,
            ItemTooltipSection.Durability,
            ItemTooltipSection.AttackSpeed,
            ItemTooltipSection.WeaponDamage,
            ItemTooltipSection.SmiteOrKickDamage,
            ItemTooltipSection.BlockChance,
            ItemTooltipSection.ArmorClass,
            ItemTooltipSection.ItemName,
        };

        /// <summary>
        /// ITEM_BuildSetItemTooltip 0x48d1d0 — the tooltip for an identified set item. LoadItemDesc
        /// diverts to it at 0x48e432 and returns at 0x48e43d, so the generic path is never built
        /// for one and <see cref="Compose"/> refuses it.
        ///
        /// What it does NOT emit, because there is no call site for any of them in its 638
        /// instructions: quest usage, the unidentified line, the socket-filler description, the
        /// charm line, quantity/spelldesc, and the runeword letters. Kick damage is absent too —
        /// INV_FormatDefenseRangeText is reached only inside `IsOfType(item, 51)` at 0x48d68a, so
        /// an Assassin hovering set boots gets no Kick Damage line where the generic path gives her
        /// one.
        /// </summary>
        public IReadOnlyList<ItemTooltipLine> ComposeSetItem(
            ItemTooltipContext context,
            SetItemTooltipContent set,
            IEnumerable<KeyValuePair<int, int>> packedStats)
        {
            if (context == null) throw new ArgumentNullException("context");
            if (set == null) throw new ArgumentNullException("set");
            if (packedStats == null) throw new ArgumentNullException("packedStats");

            ItemTooltipKind kind = Classify(context);
            if (kind != ItemTooltipKind.IdentifiedSetItem)
            {
                throw new NotSupportedException(
                    "This item is built by " + kind + ", not the set-item tooltip path. " +
                    "Call Classify first.");
            }

            var appended = new List<ItemTooltipLine>();

            // The colour in force at the END of the assembled string, which is what the price
            // tail inherits — 0x48da87 appends it with no AppendAsWideChar of its own. Every
            // block below carries it forward, because any of them can be the last non-empty one.
            int carriedColor = ItemTooltipColor.White;

            // --- var_4790, 0x48d88e-0x48d92a, then copied in at 0x48d948 ---------------------
            for (int i = 0; i < set.Pieces.Count; ++i)
            {
                SetPieceLine piece = set.Pieces[i];

                var line = new ItemTooltipLine();
                line.Text = piece.Text;
                line.Section = ItemTooltipSection.SetPieceList;
                line.Color = piece.Owned ? ItemTooltipColor.Set : ItemTooltipColor.Red;
                line.EmitsColorMarker = true;

                // 0x48d93b prepends one more `ÿc2` to the assembled list. AppendAsWideChar
                // no-ops on an empty buffer (0x4521cd), so an empty list emits neither.
                if (i == 0)
                {
                    line.LeadingMarkerColor = ItemTooltipColor.Set;
                }

                carriedColor = LastEmbeddedColor(line.Text, line.Color);
                appended.Add(line);
            }

            // --- var_1538, 0x48d958 ----------------------------------------------------------
            carriedColor = AppendSetSection(
                appended, set.SetName, ItemTooltipSection.SetName, ItemTooltipColor.Unique,
                carriedColor);

            // --- var_3390, 0x48d96a-0x48d99c: the separator is INSIDE the non-empty test ------
            if (!string.IsNullOrEmpty(set.FullSetText))
            {
                AppendBlankRow(appended, ItemTooltipSection.FullSetBonus, carriedColor);
                carriedColor = AppendSetSection(
                    appended, set.FullSetText, ItemTooltipSection.FullSetBonus,
                    ItemTooltipColor.Unique, carriedColor);
            }

            // --- 0x48d9a9: unconditional, and it produces a blank row because the buffer above
            //     already ends in 3998 -------------------------------------------------------
            AppendBlankRow(appended, ItemTooltipSection.PartialSetBonus, carriedColor);

            // --- var_2F90, 0x48d9b6-0x48d9d0 -------------------------------------------------
            carriedColor = AppendSetSection(
                appended, set.PartialText, ItemTooltipSection.PartialSetBonus,
                ItemTooltipColor.Set, carriedColor);

            // --- var_4F90, 0x48d7df-0x48d83a, appended with ONE marker at 0x48d9e0 -----------
            // The ethereal/socketed text and the modifier block share a buffer here, where the
            // generic path keeps them apart. Its gate is the SOCKETED flag alone (0x48d7e6), not
            // the ethereal-or-socketed test INV_FormatEtherealSocketedText itself makes, so an
            // ethereal set item that is not socketed gets no "Cannot Be Repaired" line.
            int sharedBufferStart = appended.Count;

            if ((context.Flags & ItemTooltipFlags.Socketed) != 0)
            {
                carriedColor = AppendSetSection(
                    appended, _sections.GetSection(ItemTooltipSection.EtherealSocketed),
                    ItemTooltipSection.EtherealSocketed, ItemTooltipColor.Magic, carriedColor);
            }

            int modifiersStart = appended.Count;

            string suppliedModifiers = _sections.GetSection(ItemTooltipSection.Modifiers);
            int afterModifiers = string.IsNullOrEmpty(suppliedModifiers)
                ? AppendModifiers(appended, packedStats)
                : AppendSuppliedModifiers(appended, suppliedModifiers);

            if (appended.Count != modifiersStart)
            {
                carriedColor = afterModifiers;
            }

            // ONE buffer, so ONE AppendAsWideChar. Both helpers mark their own first row because
            // in the generic path the two buffers are separate; here the modifier block's marker
            // has to go when the ethereal text already claimed the buffer's.
            if (modifiersStart > sharedBufferStart && modifiersStart < appended.Count)
            {
                appended[modifiersStart].EmitsColorMarker = false;
            }

            // --- var_2138, appended whole at 0x48d9fe ----------------------------------------
            foreach (ItemTooltipSection section in SetGenericAppendOrder)
            {
                if (!context.IsWeaponOrArmorType && IsWeaponOrArmorSection(section))
                {
                    continue;
                }

                // 0x48d681: BOTH the smite line and the block line sit inside `IsOfType(item, 51)`.
                // The generic path reaches INV_FormatDefenseRangeText for an Assassin's boots as
                // well (WRITERS.md), so a set boot is the one case where this writer emits strictly
                // less — no Kick Damage line at all.
                if (!context.IsShieldType
                    && (section == ItemTooltipSection.SmiteOrKickDamage
                        || section == ItemTooltipSection.BlockChance))
                {
                    continue;
                }

                string text = _sections.GetSection(section);
                if (string.IsNullOrEmpty(text))
                {
                    continue; // AppendAsWideChar no-ops, and there is no blank-row credit here
                }

                // 0x48d79a-0x48d7ae: the ONLY thing that reddens the name on this path is flag
                // 0x100. Quality is set by construction and the quest/rune/shop arms of
                // ResolveItemNameColor have no call site in this writer.
                int color = section == ItemTooltipSection.ItemName
                    ? ((context.Flags & ItemTooltipFlags.Broken) != 0
                        ? ItemTooltipColor.Red
                        : ItemTooltipColor.Set)
                    : ResolveSectionColor(section, context);

                int running = color;
                bool firstOfSection = true;
                int sectionStat = StatOfSection(section);

                var parts = new List<string>(SplitLines(text));
                for (int at = 0; at < parts.Count; ++at)
                {
                    string part = parts[at];

                    var line = new ItemTooltipLine();
                    line.Text = firstOfSection
                        ? Annotated(part, 0, sectionStat, running)
                        : part;

                    // The item's own name is the TOP display row, which is the LAST part in append
                    // order - a unique's section holds the base name first.
                    if (at == parts.Count - 1 && section == ItemTooltipSection.ItemName)
                    {
                        line.Text = WithItemLevel(line.Text, running);
                    }
                    line.Section = section;
                    line.Color = running;
                    line.StatId = firstOfSection ? sectionStat : -1;
                    line.EmitsColorMarker = firstOfSection;
                    firstOfSection = false;
                    appended.Add(line);

                    running = LastEmbeddedColor(part, running);
                }

                carriedColor = running;
            }

            // --- the inlined cost tail, 0x48da03-0x48db00 ------------------------------------
            if (context.ShopMode >= 1 && context.ShopMode <= 9)
            {
                string cost = _sections.GetSection(ItemTooltipSection.TransactionCost);

                if (!string.IsNullOrEmpty(cost))
                {
                    // 0x48da64: the separator is skipped when the cost buffer is empty, and the
                    // price itself gets NO colour marker (0x48da87).
                    AppendBlankRow(appended, ItemTooltipSection.TransactionCost, carriedColor);

                    var line = new ItemTooltipLine();
                    line.Text = cost;
                    line.Section = ItemTooltipSection.TransactionCost;
                    line.Color = carriedColor;
                    line.EmitsColorMarker = false;
                    appended.Add(line);
                }
                else if (context.ShopMode != 4)
                {
                    // 0x48da93-0x48daed. INV_FormatItemTooltipWithCost would also emit locale
                    // 22746 for an ethereal item here (0x48cef9); this writer does not.
                    AppendBlankRow(appended, ItemTooltipSection.TransactionCost, carriedColor);
                    AppendSetSection(
                        appended, set.TransactionRefusedText,
                        ItemTooltipSection.TransactionCost, ItemTooltipColor.Red, carriedColor);
                }
            }

            MergeUnterminatedRuns(appended);

            appended.Reverse();

            return appended;
        }

        private string LineTerminator
        {
            get { return _sections.LineTerminator ?? string.Empty; }
        }

        /// <summary>
        /// One AppendToBuffer of a whole buffer, preceded by one AppendAsWideChar. Empty buffers
        /// are skipped rather than emitting a bare marker, which is what 0x4521cd does.
        /// </summary>
        private int AppendSetSection(
            List<ItemTooltipLine> appended, string text, ItemTooltipSection section, int color,
            int carried)
        {
            if (string.IsNullOrEmpty(text))
            {
                return carried;
            }

            int running = color;
            bool firstOfSection = true;
            foreach (string part in SplitLines(text, terminateTrailing: false))
            {
                var line = new ItemTooltipLine();
                line.Text = part;
                line.Section = section;
                line.Color = running;
                line.EmitsColorMarker = firstOfSection;
                firstOfSection = false;
                appended.Add(line);

                running = LastEmbeddedColor(part, running);
            }

            return running;
        }

        /// <summary>
        /// A bare `AppendToBuffer(dest, str(3998))`. The buffer above it already ends in a
        /// terminator, so the row it produces has no glyphs — and no marker, because there is no
        /// AppendAsWideChar in front of it. It CARRIES the colour rather than resetting it: the
        /// game appends one character here and it is not a marker.
        /// </summary>
        private void AppendBlankRow(
            List<ItemTooltipLine> appended, ItemTooltipSection section, int carried)
        {
            var blank = new ItemTooltipLine();
            blank.Text = LineTerminator;
            blank.Section = section;
            blank.Color = carried;
            blank.EmitsColorMarker = false;
            appended.Add(blank);
        }

        public IReadOnlyList<ItemTooltipLine> Compose(
            ItemTooltipContext context, IEnumerable<KeyValuePair<int, int>> packedStats)
        {
            if (context == null) throw new ArgumentNullException("context");
            if (packedStats == null) throw new ArgumentNullException("packedStats");

            // Materialised once: the only consumer sits inside the AppendOrder loop below, so a
            // lazy sequence would be enumerated per iteration reached.
            KeyValuePair<int, int>[] stats = packedStats as KeyValuePair<int, int>[]
                                             ?? new List<KeyValuePair<int, int>>(packedStats).ToArray();

            ItemTooltipKind kind = Classify(context);
            if (kind != ItemTooltipKind.Generic)
            {
                throw new NotSupportedException(
                    "This item is built by " + kind + ", not the generic tooltip path. " +
                    "Call Classify first.");
            }

            var appended = new List<ItemTooltipLine>();

            int carriedColor = ItemTooltipColor.White;

            foreach (ItemTooltipSection section in AppendOrder)
            {
                if (section == ItemTooltipSection.TransactionCost
                    && (context.ShopMode < 1 || context.ShopMode > 9))
                {
                    continue;
                }

                if (!context.IsWeaponOrArmorType && IsWeaponOrArmorSection(section))
                {
                    continue;
                }

                if (section == ItemTooltipSection.Modifiers)
                {
                    if ((context.Flags & ItemTooltipFlags.Identified) != 0)
                    {
                        // SKILLDESC_BuildStatBuffDesc returns at 0x4e60df before building anything
                        // when the item is an elixir, so a provider that supplies text for this
                        // section REPLACES the generated block rather than adding to it.
                        string supplied = _sections.GetSection(ItemTooltipSection.Modifiers);

                        int before = appended.Count;
                        int after = string.IsNullOrEmpty(supplied)
                            ? AppendModifiers(appended, stats)
                            : AppendSuppliedModifiers(appended, supplied);

                        if (appended.Count != before)
                        {
                            carriedColor = after;
                        }
                    }

                    continue;
                }

                string text = _sections.GetSection(section);
                if (string.IsNullOrEmpty(text))
                {
                    // ItemName is the one buffer whose writer appends no terminator of its own
                    // (GetItemName's tail, 0x48ce72), so normally it is the unterminated END of the
                    // game's string and DropTrailingTerminator models that. If it is EMPTY the
                    // string instead ends with the previous section's own 3998, and the renderer
                    // steps a row for it (0x501b97 -> 0x501c17) — a blank row at the top. Emit a
                    // terminator-only line so that row survives the reversal.
                    //
                    // The buffer really can be empty: GetItemName's LowQuality arm bails at
                    // 0x48c220 when TXT_LowQualityItems_GetLine returns null and never writes the
                    // destination. Unreachable with stock data, where every arm writes.
                    //
                    // It costs no budget: EmitsColorMarker is false, and being last-appended it
                    // gets ApplyAppendOrderBudget's i == 0 terminator credit, so it measures 0 —
                    // matching the game, which spends no extra character either.
                    if (section == ItemTooltipSection.ItemName && appended.Count != 0)
                    {
                        var blankTop = new ItemTooltipLine();
                        blankTop.Text = _sections.LineTerminator ?? string.Empty;
                        blankTop.Section = ItemTooltipSection.ItemName;
                        blankTop.Color = carriedColor;
                        blankTop.EmitsColorMarker = false;
                        appended.Add(blankTop);
                    }

                    continue;
                }


                int color;
                if (section == ItemTooltipSection.TransactionCost)
                {
                    color = carriedColor;
                }
                else
                {
                    color = ResolveSectionColor(section, context);
                }

                int running = color;
                bool firstOfSection = true;
                int sectionStat = StatOfSection(section);

                var parts = new List<string>(SplitLines(text));
                for (int at = 0; at < parts.Count; ++at)
                {
                    string part = parts[at];

                    var line = new ItemTooltipLine();
                    line.Text = firstOfSection
                        ? Annotated(part, 0, sectionStat, running)
                        : part;

                    // The item's own name is the TOP display row, which is the LAST part in append
                    // order - a unique's section holds the base name first.
                    if (at == parts.Count - 1 && section == ItemTooltipSection.ItemName)
                    {
                        line.Text = WithItemLevel(line.Text, running);
                    }
                    line.Section = section;
                    line.Color = running;
                    line.StatId = firstOfSection ? sectionStat : -1;

                    line.EmitsColorMarker = firstOfSection;
                    firstOfSection = false;
                    appended.Add(line);

                    running = LastEmbeddedColor(part, running);
                }

                carriedColor = running;
            }

            MergeUnterminatedRuns(appended);

            appended.Reverse();

            return appended;
        }

        private void MergeUnterminatedRuns(List<ItemTooltipLine> appendOrder)
        {
            string terminator = _sections.LineTerminator;
            if (string.IsNullOrEmpty(terminator))
            {
                return;
            }

            for (int i = 0; i < appendOrder.Count - 1;)
            {
                ItemTooltipLine line = appendOrder[i];
                if (line.Text != null && line.Text.EndsWith(terminator, StringComparison.Ordinal))
                {
                    ++i;
                    continue;
                }

                ItemTooltipLine swallowed = appendOrder[i + 1];

                // Splice a marker only ACROSS sections. Each of the 18 buffers gets its own
                // AppendAsWideChar, so a merged line really does change colour part way through;
                // within a section the game emits nothing between the parts (the only producer of
                // an unterminated part is a PreJoined stat line, and 0x48ea1c gives the whole stat
                // block one marker), so splicing there would invent 3 characters.
                bool crossesSection = swallowed.Section != line.Section;
                bool splicesMarker = swallowed.EmitsColorMarker && crossesSection;

                line.Text += (splicesMarker
                                 ? ItemTooltipColor.Marker + EncodeColorDigit(swallowed.Color)
                                 : string.Empty)
                             + swallowed.Text;

                // The swallowed line was its section's first-APPENDED one, so it owned the game's
                // single marker for that section. Record it: if the section has further lines they
                // survive this merge and must not be charged for that marker again.
                if (splicesMarker)
                {
                    line.SplicedSection = swallowed.Section;
                }

                appendOrder.RemoveAt(i + 1);
            }
        }

        public const int MaxTooltipLength = 1023;

        /// <summary>
        /// ITEM_BuildSetItemTooltip has no 1023 cut: LoadItemDesc truncates explicitly at 0x48ed12
        /// but the set writer goes straight from MoveArgumentToEAX (0x48db0b) to
        /// TEXT_CalcTextDimensions (0x48db1d), and its output buffer is 2048 WCHARs with no guard.
        /// So the budget is a knob rather than a constant — pass <see cref="int.MaxValue"/> to
        /// spend nothing.
        /// </summary>
        public const int UnlimitedTooltipLength = int.MaxValue;

        public string Render(
            IEnumerable<ItemTooltipLine> lines, bool questColorPrefix = false,
            int maxLength = MaxTooltipLength)
        {
            if (lines == null) throw new ArgumentNullException("lines");

            ItemTooltipLine[] ordered = lines as ItemTooltipLine[]
                                        ?? new List<ItemTooltipLine>(lines).ToArray();

            var builder = new StringBuilder();
            foreach (ItemTooltipLine line in
                ApplyAppendOrderBudget(ordered, ItemTooltipColor.Marker, questColorPrefix,
                    _sections.LineTerminator, maxLength))
            {
                builder.Append(line.Text);
            }

            return DropTrailingTerminator(builder.ToString());
        }

        private string DropTrailingTerminator(string assembled)
        {
            string terminator = _sections.LineTerminator;

            if (string.IsNullOrEmpty(terminator)
                || !assembled.EndsWith(terminator, StringComparison.Ordinal))
            {
                return assembled;
            }

            return assembled.Substring(0, assembled.Length - terminator.Length);
        }


        public string RenderWithColorCodes(
            IEnumerable<ItemTooltipLine> lines,
            string colorMarker = ItemTooltipColor.Marker,
            bool questColorPrefix = false,
            int maxLength = MaxTooltipLength)
        {
            if (lines == null) throw new ArgumentNullException("lines");

            ItemTooltipLine[] all = lines as ItemTooltipLine[]
                                    ?? new List<ItemTooltipLine>(lines).ToArray();

            List<ItemTooltipLine> ordered = ApplyAppendOrderBudget(
                all, colorMarker, questColorPrefix, _sections.LineTerminator, maxLength);

            return Emit(ordered, colorMarker, questColorPrefix);
        }

        private string Emit(
            List<ItemTooltipLine> ordered, string colorMarker, bool questColorPrefix)
        {
            var builder = new StringBuilder();

            for (int i = 0; i < ordered.Count; ++i)
            {
                ItemTooltipLine line = ordered[i];

                if (string.IsNullOrEmpty(line.Text))
                {
                    continue; // AppendAsWideChar skips empty buffers entirely
                }

                // A SECOND game marker in front of the row's own, and the only producer of one is
                // 0x48d93b — see ItemTooltipLine.LeadingMarkerColor.
                if (line.LeadingMarkerColor >= 0)
                {
                    builder.Append(colorMarker);
                    builder.Append(EncodeColorDigit(line.LeadingMarkerColor));
                }

                if (WillEmitMarker(line, colorMarker, _sections.LineTerminator))
                {
                    builder.Append(colorMarker);
                    builder.Append(EncodeColorDigit(line.Color));
                }

                builder.Append(line.Text);
            }

            string assembled = DropTrailingTerminator(builder.ToString());

            if (questColorPrefix)
            {
                assembled += colorMarker + EncodeColorDigit(ItemTooltipColor.Unique);
            }

            return assembled;
        }


        private static int MarkerLength(string colorMarker)
        {
            return (colorMarker == null ? 0 : colorMarker.Length) + 1;
        }

        /// <summary>
        /// Two markers stack here, and they are different things.
        ///
        /// The GAME's own: `AppendAsWideChar` (0x4521c0) prepends one marker to each section BUFFER,
        /// which lands on that section's first-APPENDED row and is what
        /// <see cref="ItemTooltipLine.EmitsColorMarker"/> records. It is unconditional bar an empty
        /// buffer, so it stacks on top of a marker the writer already put in the text — which is
        /// why `ÿc0ÿc0Chance to Block:` is real (INV_FormatBlockChanceText 0x485d0e, then
        /// LoadItemDesc 0x48eb80), and why a blank first row comes out as a bare colour code.
        ///
        /// The DISPLAY re-anchor: the game's buffer is append order and drawn bottom-up (0x501c17),
        /// so it never produces a display-ordered string, and reversing the rows breaks the
        /// stickiness (0x501bec) every later row of a section relied on. Those rows are re-anchored
        /// with the colour that WAS in force at them, which is what <see cref="ItemTooltipLine.Color"/>
        /// carries. A row that already opens with a marker needs no anchor — it states its own
        /// colour — and a row with no glyphs gets none, because a marker there would draw a colour
        /// code instead of a blank line.
        /// </summary>
        private static bool WillEmitMarker(
            ItemTooltipLine line, string colorMarker, string terminator)
        {
            if (line.EmitsColorMarker)
            {
                return true;
            }

            if (!string.IsNullOrEmpty(terminator)
                && string.CompareOrdinal(line.Text, terminator) == 0)
            {
                return false;
            }

            return string.IsNullOrEmpty(colorMarker)
                   || !line.Text.StartsWith(colorMarker, StringComparison.Ordinal);
        }

        public static char EncodeColorDigit(int color)
        {
            return (char)('0' + color);
        }

        // The game truncates at 1023 wide chars (0x48ed12 / NUL written at 0x48ed19), and
        // TEXT_TooltipSetAttributes DISCARDS the whole string at 1024 or more (0x502292) — so this
        // is load-bearing, not cosmetic.
        //
        // Two things here look wrong and are not:
        //  * It charges the GAME's accounting — one marker per section, on that section's
        //    first-APPENDED line — not the per-line markers this class emits. The point is to
        //    reproduce which LINES survive. Reserving for our extra markers was tried twice and
        //    reverted both times: it displaces the cut and truncates where the game does not.
        //  * TransactionCost is charged NOTHING, because the game spends nothing on it (0x48cf87
        //    raw-appends the price with no AppendAsWideChar).
        //
        // Consequence, accepted: the emitted string may exceed 1023 by a few chars when a colour
        // the game carried by stickiness has to be restated on a line that is now display-first.
        private static List<ItemTooltipLine> ApplyAppendOrderBudget(
            ItemTooltipLine[] displayOrder, string colorMarker, bool questColorPrefix,
            string terminator, int maxLength)
        {
            int markerLength = MarkerLength(colorMarker);

            // 0x48ecf2 prepends the quest marker before the length is taken, so it costs budget
            // even though it paints nothing.
            int used = questColorPrefix ? markerLength : 0;

            var keptAppendOrder = new List<ItemTooltipLine>();

            // Walks APPEND order, which is display order reversed.
            for (int i = displayOrder.Length - 1; i >= 0; --i)
            {
                ItemTooltipLine line = displayOrder[i];
                string text = line.Text ?? string.Empty;

                // Last clause: MergeUnterminatedRuns may have spliced this section's marker into
                // the previously-appended line's text, where it is already counted.
                bool carriesGameMarker =
                    line.Section != ItemTooltipSection.TransactionCost
                    && (i == displayOrder.Length - 1
                        || displayOrder[i + 1].Section != line.Section)
                    && !(i + 1 < displayOrder.Length
                         && displayOrder[i + 1].SplicedSection == line.Section);


                int measured = text.Length;
                if (i == 0
                    && !string.IsNullOrEmpty(terminator)
                    && text.EndsWith(terminator, StringComparison.Ordinal))
                {
                    measured -= terminator.Length;
                }

                int overhead = carriesGameMarker ? markerLength : 0;
                if (used + overhead >= maxLength)
                {
                    int remaining = maxLength - used;

                    var blank = new ItemTooltipLine();
                    blank.Text = (remaining == 1 && !string.IsNullOrEmpty(colorMarker)
                                     ? colorMarker.Substring(0, 1)
                                     : string.Empty)
                                 + (terminator ?? string.Empty);
                    blank.Section = line.Section;

                    blank.Color = i + 1 < displayOrder.Length
                        ? LastEmbeddedColor(
                            displayOrder[i + 1].Text, displayOrder[i + 1].Color)
                        : ItemTooltipColor.White;
                    blank.EmitsColorMarker = remaining == 1;

                    keptAppendOrder.Add(blank);
                    break;
                }

                used += overhead;

                if (used + measured <= maxLength)
                {
                    used += measured;
                    keptAppendOrder.Add(line);
                    continue;
                }

                int cut = maxLength - used;

                if (!string.IsNullOrEmpty(colorMarker)
                    && cut >= colorMarker.Length
                    && string.CompareOrdinal(
                        text, cut - colorMarker.Length, colorMarker, 0, colorMarker.Length) == 0)
                {
                    cut -= colorMarker.Length;
                }

                var partial = new ItemTooltipLine();
                partial.Text = text.Substring(0, cut) + (terminator ?? string.Empty);
                partial.Section = line.Section;
                partial.Color = line.Color;
                partial.EmitsColorMarker = line.EmitsColorMarker;
                keptAppendOrder.Add(partial);
                break;
            }

            keptAppendOrder.Reverse();
            return keptAppendOrder;
        }

        /// <summary>
        /// Supplies the range text for a line, given every stat the line shows a number for and the
        /// layer they share. Null by default, so an un-annotated render is byte-identical to what
        /// the game draws — the corpus never sets it and the differential holds that.
        /// </summary>
        internal Func<IReadOnlyList<int>, int, string> RangeAnnotation;

        /// <summary>
        /// The same, for a SECTION line rather than a modifier line. Both exist because the Defense
        /// line and a `+45 Defense` modifier line report stat 31 and draw different numbers: the
        /// section shows the base roll plus every modifier, the modifier shows its own contribution
        /// alone. One dictionary served both and gave the modifier line the section's span —
        /// "+45 Defense [99-131]" on a Tal Rasha's Horadric Crest, whose 45 is a FIXED set property
        /// and could never have rolled at all.
        ///
        /// Null falls back to <see cref="RangeAnnotation"/>, which is what a caller building a
        /// composer without the engine gets.
        /// </summary>
        internal Func<IReadOnlyList<int>, int, string> SectionRangeAnnotation;

        /// <summary>
        /// The colour the annotation is painted in, or -1 to inherit the line's. A marker restoring
        /// the line's own colour follows it, so the rest of the line is unaffected — and the
        /// running colour is tracked from the UN-annotated text, so an annotation can never bleed
        /// into the next line.
        /// </summary>
        internal int RangeColor = -1;

        /// <summary>
        /// Appended to the item's NAME line, painted grey. Null draws nothing, which is the game's
        /// own output - it has no item-level line.
        /// </summary>
        internal string ItemLevelSuffix;

        /// <summary>
        /// The single stat a section displays, or -1. Only the Defense line qualifies: it shows one
        /// stat whose base genuinely rolls. Durability and the damage lines are excluded on purpose
        /// — their base columns do not roll, so a span there would be about the `dur%` or `dmg%`
        /// modifier and belongs on that modifier's own line, where it already is.
        /// </summary>
        private static int StatOfSection(ItemTooltipSection section)
        {
            return section == ItemTooltipSection.ArmorClass ? StatArmorClass : -1;
        }

        private const int StatArmorClass = 31;

        /// <summary>
        /// Appends the range text INSIDE the line — before its trailing terminator, since
        /// <see cref="SplitLines"/> keeps that on the part it belongs to and appending after it
        /// would put the annotation on the following line.
        /// </summary>
        private string Annotated(string part, int layer, int statId, int lineColor)
        {
            return Annotate(
                part,
                layer,
                statId < 0 ? null : new[] { statId },
                lineColor,
                SectionRangeAnnotation ?? RangeAnnotation);
        }

        private string Annotated(
            string part, int layer, IReadOnlyList<int> shownStats, int lineColor)
        {
            return Annotate(part, layer, shownStats, lineColor, RangeAnnotation);
        }

        private string Annotate(
            string part,
            int layer,
            IReadOnlyList<int> shownStats,
            int lineColor,
            Func<IReadOnlyList<int>, int, string> source)
        {
            if (source == null || shownStats == null || shownStats.Count == 0 || part == null)
            {
                return part;
            }

            return AppendInsideTerminator(part, source(shownStats, layer), lineColor, RangeColor);
        }

        /// <summary>
        /// ` [ilvl 67]` after the item's name, or the part unchanged when no level is set. The game
        /// never draws this; a record without one carries -1.
        /// </summary>
        private string WithItemLevel(string part, int lineColor)
        {
            if (ItemLevelSuffix == null)
            {
                return part;
            }

            // The game pads a magic or rare name with a trailing space, so a separator of our own
            // reads as a double space on most items.
            string terminator = _sections.LineTerminator;
            bool padded = (!string.IsNullOrEmpty(terminator)
                    && part.EndsWith(terminator, StringComparison.Ordinal)
                ? part.Substring(0, part.Length - terminator.Length)
                : part).EndsWith(" ", StringComparison.Ordinal);

            return AppendInsideTerminator(
                part,
                padded ? ItemLevelSuffix : " " + ItemLevelSuffix,
                lineColor,
                ItemTooltipColor.SocketedOrEthereal);
        }

        /// <summary>
        /// Appends INSIDE the trailing terminator, since <see cref="SplitLines"/> keeps that on the
        /// part it belongs to and appending after it would push the text onto the following line.
        /// A marker restoring <paramref name="lineColor"/> follows, so nothing after is affected.
        /// </summary>
        private string AppendInsideTerminator(
            string part, string addition, int lineColor, int color)
        {
            if (string.IsNullOrEmpty(addition))
            {
                return part;
            }

            if (color >= 0 && color != lineColor)
            {
                addition = ItemTooltipColor.Marker + color.ToString(CultureInfo.InvariantCulture)
                    + addition
                    + ItemTooltipColor.Marker + lineColor.ToString(CultureInfo.InvariantCulture);
            }

            string terminator = _sections.LineTerminator;
            if (!string.IsNullOrEmpty(terminator)
                && part.EndsWith(terminator, StringComparison.Ordinal))
            {
                return part.Substring(0, part.Length - terminator.Length)
                    + addition + terminator;
            }

            return part + addition;
        }

        private IEnumerable<string> SplitLines(string text, bool terminateTrailing = true)
        {
            string terminator = _sections.LineTerminator;
            if (string.IsNullOrEmpty(terminator))
            {
                yield return text;
                yield break;
            }

            int start = 0;
            while (start < text.Length)
            {
                int at = text.IndexOf(terminator, start, StringComparison.Ordinal);
                if (at < 0)
                {
                    yield return terminateTrailing
                        ? text.Substring(start) + terminator
                        : text.Substring(start);
                    yield break;
                }

                yield return text.Substring(start, at - start + terminator.Length);
                start = at + terminator.Length;
            }
        }

        /// <summary>
        /// The elixir case: the provider hands over the whole block already built, so it is split and
        /// coloured exactly as a generated one would be.
        /// </summary>
        private int AppendSuppliedModifiers(List<ItemTooltipLine> lines, string text)
        {
            int running = ItemTooltipColor.Magic;
            bool firstOfSection = true;

            foreach (string part in SplitLines(text, terminateTrailing: false))
            {
                var line = new ItemTooltipLine();
                line.Text = part;
                line.Section = ItemTooltipSection.Modifiers;
                line.Color = running;

                line.EmitsColorMarker = firstOfSection;
                firstOfSection = false;
                lines.Add(line);

                running = LastEmbeddedColor(part, running);
            }

            return running;
        }

        /// <summary>
        /// Just the blue block, in display order. Used by the breakdown view, which shows the
        /// modifiers from one source at a time. It goes through AppendModifiers rather than
        /// rebuilding the loop so the colour carry and the terminator split cannot drift, and it
        /// reverses for the same reason Compose does — the game appends bottom row first.
        /// </summary>
        internal IReadOnlyList<ItemTooltipLine> ComposeModifiersOnly(
            IEnumerable<KeyValuePair<int, int>> packedStats)
        {
            if (packedStats == null) throw new ArgumentNullException("packedStats");

            var lines = new List<ItemTooltipLine>();
            AppendModifiers(lines, packedStats);
            lines.Reverse();
            return lines;
        }

        private int AppendModifiers(
            List<ItemTooltipLine> lines, IEnumerable<KeyValuePair<int, int>> packedStats)
        {
            string terminator = _sections.LineTerminator ?? string.Empty;

            int running = ItemTooltipColor.Magic;
            bool firstOfSection = true;

            foreach (ItemDescriptionLine modifier in _modifiers.Describe(packedStats))
            {
                string text = modifier.PreJoined
                    ? modifier.Text ?? string.Empty
                    : (modifier.Text ?? string.Empty) + terminator;

                bool firstPart = true;

                foreach (string part in SplitLines(text, terminateTrailing: false))
                {
                    var line = new ItemTooltipLine();
                    // An aggregated line gets its stats named, so the formatter can show a
                    // composite span rather than one number belonging to neither half.
                    line.Text = firstPart
                        ? Annotated(
                            part,
                            modifier.Layer,
                            modifier.ShownStats ?? new[] { modifier.StatId },
                            running)
                        : part;
                    line.Section = ItemTooltipSection.Modifiers;
                    line.Color = running;
                    line.StatId = modifier.StatId;
                    line.Layer = modifier.Layer;
                    line.ShownStats = modifier.ShownStats;
                    line.Aggregated = modifier.Aggregated;

                    line.EmitsColorMarker = firstOfSection;
                    firstOfSection = false;
                    firstPart = false;
                    lines.Add(line);

                    running = LastEmbeddedColor(part, running);
                }
            }

            return running;
        }

        private static int LastEmbeddedColor(string text, int fallback)
        {
            if (string.IsNullOrEmpty(text))
            {
                return fallback;
            }

            int color = fallback;
            for (int i = 0; i + ItemTooltipColor.Marker.Length < text.Length; ++i)
            {
                if (string.CompareOrdinal(
                        text, i, ItemTooltipColor.Marker, 0, ItemTooltipColor.Marker.Length) != 0)
                {
                    continue;
                }

                color = text[i + ItemTooltipColor.Marker.Length] - '0';
            }

            return color;
        }

        private static bool IsWeaponOrArmorSection(ItemTooltipSection section)
        {
            switch (section)
            {
                case ItemTooltipSection.EtherealSocketed:
                case ItemTooltipSection.AttackSpeed:
                case ItemTooltipSection.RequiredStrength:
                case ItemTooltipSection.RequiredDexterity:
                case ItemTooltipSection.WeaponDamage:
                case ItemTooltipSection.SmiteOrKickDamage:
                case ItemTooltipSection.BlockChance:
                case ItemTooltipSection.ArmorClass:
                    return true;

                default:
                    return false;
            }
        }

        private int ResolveSectionColor(ItemTooltipSection section, ItemTooltipContext context)
        {
            switch (section)
            {
                case ItemTooltipSection.RequiredLevel:
                case ItemTooltipSection.RequiredStrength:
                case ItemTooltipSection.RequiredDexterity:
                case ItemTooltipSection.ClassRestriction:
                    return _sections.IsRequirementUnmet(section)
                        ? ItemTooltipColor.Red
                        : ItemTooltipColor.White;

                case ItemTooltipSection.ItemName:
                    return ResolveItemNameColor(context);


                case ItemTooltipSection.EtherealSocketed:
                    return ItemTooltipColor.Magic; // literal 3 at 0x48e993

                case ItemTooltipSection.Unidentified:
                    return ItemTooltipColor.Red; // literal 1 at 0x48ea39

                case ItemTooltipSection.RuneLetters:
                    return ItemTooltipColor.Unique; // literal 4 at 0x48ebac

                case ItemTooltipSection.QuestUsage:
                    // 0x48ecf2 prepends colour 4 to the FINISHED buffer, and it is reached for any
                    // quest item whose code is not `leg ` (0x48ec58 compares the dword 0x2067656C).
                    // Prepending to an append-ordered buffer puts it at the head of the FIRST
                    // appended row, which is the BOTTOM display row — this one. Unconditionally 4:
                    // the red difficulty variant lives in GetItemName (0x48cb50) and colours the
                    // name buffer alone.
                    return context.IsQuestItem && !context.IsWirtsLeg
                        ? ItemTooltipColor.Unique
                        : ItemTooltipColor.White;

                default:
                    return ItemTooltipColor.White;
            }
        }

        public static int ResolveItemNameColor(ItemTooltipContext context)
        {
            if (context == null) throw new ArgumentNullException("context");

            int color;

            switch (context.Quality)
            {
                case ItemQuality.Magic:
                    color = ItemTooltipColor.Magic;
                    break;
                case ItemQuality.Set:
                    color = ItemTooltipColor.Set;
                    break;
                case ItemQuality.Rare:
                    color = ItemTooltipColor.Rare;
                    break;
                case ItemQuality.Unique:
                    color = ItemTooltipColor.Unique;
                    break;
                case ItemQuality.Crafted:
                    color = ItemTooltipColor.Crafted;
                    break;
                case ItemQuality.Tempered:
                    color = ItemTooltipColor.Tempered;
                    break;
                default:
                    bool socketedOrEthereal =
                        (context.Flags & (ItemTooltipFlags.Socketed | ItemTooltipFlags.Ethereal)) != 0;
                    color = socketedOrEthereal ? ItemTooltipColor.SocketedOrEthereal : ItemTooltipColor.White;
                    break;
            }

            if (context.UnidentifiedInShop)
            {
                color = ItemTooltipColor.White;
            }

            if (context.ForcesCraftedColor)
            {
                color = ItemTooltipColor.Crafted;
            }

            if ((context.Flags & ItemTooltipFlags.Broken) != 0)
            {
                color = ItemTooltipColor.Red;
            }

            // The quest colour is NOT part of this. GetItemName prepends it INSIDE the name buffer
            // (0x48cb50 red / 0x48ce6d gold), so it belongs to the section's TEXT, and LoadItemDesc
            // then prepends v105 — the value computed above — in front of it. The game really does
            // draw both: `ÿc0ÿc4Horadric Cube`. Folding the quest colour in here collapsed them to
            // one and lost the section marker. See RecordSections.QuestNameColorPrefix.
            return color;
        }
    }
}

