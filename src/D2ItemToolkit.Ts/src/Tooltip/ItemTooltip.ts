import type { ItemDescriptionGenerator } from '../Description/ItemDescription.js';
import { ArgumentNullException, NotSupportedException, isNullOrEmpty } from '../Types.js';
import type { SetItemTooltipContent, SetPieceLine } from './SetItemTooltip.js';

// Mirrors LoadItemDesc (0x48dd90).
//
// APPEND ORDER IS NOT DISPLAY ORDER. The game concatenates 18 stack buffers top to bottom,
// but D2WINFONT_DrawWideString steps the cursor UPWARDS on every newline (0x501c17), so the
// last buffer appended is the TOP line. Compose reverses into display order. Nearly every bug
// this file has had came from applying an append-order fact — a colour marker, a terminator,
// the 1023 cut — to the display-order list or the reverse.

// String-valued so `line.section` reads as `"Durability"` rather than `9`, matching what the C#
// enum prints. These names are also the wire form the differential compares, so they are the same
// fact in both places rather than a number plus a lookup to name it. The game has no ordering that
// these values encode — append order is the AppendOrder arrays below.
export enum ItemTooltipSection {
  /**
   * Not a section the game has. Every line carries a real one by the time compose returns; this is
   * only the pre-assignment state, and it exists because a string enum has no zero to default to.
   * Enumerators over this type skip it.
   */
  None = 'None',

  EtherealSocketed = 'EtherealSocketed',

  Modifiers = 'Modifiers',

  Unidentified = 'Unidentified',

  AttackSpeed = 'AttackSpeed',

  RequiredLevel = 'RequiredLevel',

  RequiredStrength = 'RequiredStrength',

  RequiredDexterity = 'RequiredDexterity',

  ClassRestriction = 'ClassRestriction',

  Durability = 'Durability',

  SocketFillerDescription = 'SocketFillerDescription',

  CharmDescription = 'CharmDescription',

  QuantityAndSpellDescription = 'QuantityAndSpellDescription',

  WeaponDamage = 'WeaponDamage',

  SmiteOrKickDamage = 'SmiteOrKickDamage',

  BlockChance = 'BlockChance',

  ArmorClass = 'ArmorClass',

  RuneLetters = 'RuneLetters',

  ItemName = 'ItemName',

  TransactionCost = 'TransactionCost',

  /**
   * 0x48ec3f: prepended to the FINISHED buffer, so it renders as the BOTTOM row and is
   * therefore appended ahead of everything else.
   */
  QuestUsage = 'QuestUsage',

  // INV_ShowBookTooltip 0x48d060's three lines. Its quantity is NOT the generic one: the
  // call at 0x48d07d has none of the identified/not-socketed gating 0x48e8ef applies.
  BookQuantity = 'BookQuantity',

  BookRightClickToUse = 'BookRightClickToUse',

  BookInsertScrolls = 'BookInsertScrolls',

  // ITEM_BuildSetItemTooltip 0x48d1d0's four extra buffers. Everything else it emits is one of
  // the generic sections above, built by the same writer at the same address.
  SetPieceList = 'SetPieceList',

  SetName = 'SetName',

  FullSetBonus = 'FullSetBonus',

  PartialSetBonus = 'PartialSetBonus',

  /**
   * Not a section the game has. One block per socket filler, emitted below the item when
   * `TooltipOptions.separateSocketContributions` is set, so a reader can tell what each gem or rune
   * is actually contributing. Never produced otherwise.
   */
  SocketContribution = 'SocketContribution',
}

export const ItemTooltipColor = {
  White: 0,
  Red: 1,
  Set: 2,
  Magic: 3,
  Unique: 4,
  SocketedOrEthereal: 5,
  Crafted: 8,
  Rare: 9,
  Tempered: 10,

  MarkerStringId: 3994,

  Marker: '\u00FFc',
} as const;

export enum ItemQuality {
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

export enum ItemTooltipFlags {
  None = 0,
  Identified = 0x00000010,
  Broken = 0x00000100,
  Socketed = 0x00000800,
  Ethereal = 0x00400000,
}

// String-valued so `tooltip.kind` reads as `"Generic"` rather than `0`, matching what the C# enum
// prints. These names are also the wire form the differential compares, so they are the same fact
// in both places instead of a numeric value plus a lookup to name it.
export enum ItemTooltipKind {
  Generic = 'Generic',
  ShopTransaction = 'ShopTransaction',

  Transmogrify = 'Transmogrify',

  IdentifiedSetItem = 'IdentifiedSetItem',

  Book = 'Book',
}

export class ItemTooltipLine {
  text: string | null = null;
  section: ItemTooltipSection = ItemTooltipSection.None;

  color = 0;

  /**
   * The stat this line displays, or -1 when it displays none — a name, a requirement, a blank. Set
   * for every modifier line and for the Defense line. With `layer` it is the key to look the line
   * up in `ItemRollRanges.stats`, which is what makes a caller's own range display possible
   * without re-deriving the mapping.
   */
  statId = -1;

  /** The stat's layer — the skill, class or tab. 0 for a plain stat. */
  layer = 0;

  /**
   * Every stat this line displays a number for, in the order the numbers appear. Null means just
   * `statId`.
   *
   * "Adds 1-4 Cold Damage" is coldmindam and coldmaxdam on one line, and "+2 to All Attributes" is a
   * DescGrp standing for four. A caller matching lines back to `ItemRollRanges.stats` needs all of
   * them, not the first.
   */
  shownStats: number[] | null = null;

  /**
   * True when the line speaks for more than the one stat in `statId` — the same condition
   * `shownStats` is populated under, exposed as a flag because a caller usually only wants to know
   * whether one stat is the whole story.
   */
  aggregated = false;

  emitsColorMarker = false;

  /**
   * A SECOND marker, emitted in front of this line's own. Exactly one line ever carries it: the
   * first-appended piece of a set item's piece list, because 0x48d93b prepends `ÿc2` to the whole
   * var_4790 buffer while every piece inside it already carries a marker of its own (0x48d907).
   * The outer one paints nothing and is in the game's string regardless. -1 means none.
   */
  leadingMarkerColor = -1;

  splicedSection: ItemTooltipSection | null = null;

  toString(): string {
    return this.text as string;
  }
}

export class ItemTooltipContext {
  quality: ItemQuality = 0 as ItemQuality;
  flags: number = ItemTooltipFlags.None;

  forcesCraftedColor = false;

  unidentifiedInShop = false;

  isShopTransaction = false;
  isTransmogrify = false;
  isBook = false;

  isQuestItem = false;

  isWirtsLeg = false;

  isWeaponOrArmorType = false;

  /**
   * IsOfType(item, 51) — itemtypes row 51, `shld`. The generic path never needs it because its
   * smite/kick and block gates are the writers' own; ITEM_BuildSetItemTooltip wraps both in one
   * shield test at 0x48d681 and so never emits Kick Damage.
   */
  isShieldType = false;

  shopMode = 0;
}

export interface IItemTooltipSections {
  readonly lineTerminator: string | null;

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
  getSection(section: ItemTooltipSection): string | null;

  isRequirementUnmet(section: ItemTooltipSection): boolean;
}

/** The C# `IEnumerable<KeyValuePair<int, int>>` the description engine consumes. */
export type PackedStatEntries = Iterable<readonly [number, number]>;

export class ItemTooltipComposer {
  private static readonly AppendOrder: readonly ItemTooltipSection[] = [
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
  ];

  /**
   * INV_ShowBookTooltip 0x48d060 in append order: quantity (0x48d07d), then — only when
   * ShopMode is zero (0x48d082) — locale 2203 and 2206 each followed by 3998, then
   * GetItemName into a 128-wide buffer with no terminator (0x48d0ed). Rendered bottom-up
   * that gives name, Insert Scrolls, Right Click to Use, Quantity.
   */
  private static readonly BookAppendOrder: readonly ItemTooltipSection[] = [
    ItemTooltipSection.BookQuantity,
    ItemTooltipSection.BookRightClickToUse,
    ItemTooltipSection.BookInsertScrolls,
    ItemTooltipSection.ItemName,
  ];

  private readonly sections: IItemTooltipSections;
  private readonly modifiers: ItemDescriptionGenerator;

  constructor(sections: IItemTooltipSections | null, modifiers: ItemDescriptionGenerator | null) {
    if (sections === null) throw new ArgumentNullException('sections');
    if (modifiers === null) throw new ArgumentNullException('modifiers');

    this.sections = sections;
    this.modifiers = modifiers;
  }

  static classify(context: ItemTooltipContext | null): ItemTooltipKind {
    if (context === null) throw new ArgumentNullException('context');

    if (context.isShopTransaction) {
      return ItemTooltipKind.ShopTransaction;
    }

    if (context.isTransmogrify) {
      return ItemTooltipKind.Transmogrify;
    }

    if (
      context.quality === ItemQuality.Set &&
      (context.flags & ItemTooltipFlags.Identified) !== 0
    ) {
      return ItemTooltipKind.IdentifiedSetItem;
    }

    if (context.isBook) {
      return ItemTooltipKind.Book;
    }

    return ItemTooltipKind.Generic;
  }

  /**
   * INV_ShowBookTooltip 0x48d060. It shares nothing with the generic path but GetItemName:
   * no requirement lines, no modifier block, no colour markers anywhere (there is no
   * AppendAsWideChar in the function, and GetItemName's own colour tail is skipped for
   * `quest == 0` at 0x48cb0b).
   *
   * The shop-mode routing at 0x48d126-0x48d154 — where 1..9 sends the whole buffer through
   * INV_FormatItemTooltipWithCost — is the same TransactionCost gap the generic path has, so
   * the cost text is absent here too.
   */
  composeBook(context: ItemTooltipContext | null): readonly ItemTooltipLine[] {
    if (context === null) throw new ArgumentNullException('context');

    const kind = ItemTooltipComposer.classify(context);
    if (kind !== ItemTooltipKind.Book) {
      throw new NotSupportedException(
        'This item is built by ' + kind + ', not the book tooltip path.',
      );
    }

    const appended: ItemTooltipLine[] = [];

    for (const section of ItemTooltipComposer.BookAppendOrder) {
      if (
        context.shopMode !== 0 &&
        (section === ItemTooltipSection.BookRightClickToUse ||
          section === ItemTooltipSection.BookInsertScrolls)
      ) {
        continue;
      }

      const text = this.sections.getSection(section);
      if (isNullOrEmpty(text)) {
        continue;
      }

      for (const row of this.splitLines(text as string)) {
        const line = new ItemTooltipLine();
        line.text = row;
        line.section = section;
        line.color = ItemTooltipColor.White;
        appended.push(line);
      }
    }

    // Render consumes DISPLAY order and walks it backwards to spend the budget, so the
    // append order built above has to be flipped exactly as the generic path flips it.
    appended.reverse();

    return appended;
  }

  /**
   * The generic accumulator var_2138, in APPEND order — 0x48d514 through 0x48d7c4. The shared
   * writers behind these eleven buffers are the same functions LoadItemDesc calls, so only the
   * order and the gating are this writer's own.
   */
  private static readonly SetGenericAppendOrder: readonly ItemTooltipSection[] = [
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
  ];

  /**
   * ITEM_BuildSetItemTooltip 0x48d1d0 — the tooltip for an identified set item. LoadItemDesc
   * diverts to it at 0x48e432 and returns at 0x48e43d, so the generic path is never built for one
   * and {@link compose} refuses it.
   *
   * What it does NOT emit, because there is no call site for any of them in its 638 instructions:
   * quest usage, the unidentified line, the socket-filler description, the charm line,
   * quantity/spelldesc, and the runeword letters. Kick damage is absent too —
   * INV_FormatDefenseRangeText is reached only inside `IsOfType(item, 51)` at 0x48d68a, so an
   * Assassin hovering set boots gets no Kick Damage line where the generic path gives her one.
   */
  composeSetItem(
    context: ItemTooltipContext | null,
    set: SetItemTooltipContent | null,
    packedStats: PackedStatEntries | null,
  ): readonly ItemTooltipLine[] {
    if (context === null) throw new ArgumentNullException('context');
    if (set === null) throw new ArgumentNullException('set');
    if (packedStats === null) throw new ArgumentNullException('packedStats');

    const kind = ItemTooltipComposer.classify(context);
    if (kind !== ItemTooltipKind.IdentifiedSetItem) {
      throw new NotSupportedException(
        'This item is built by ' + kind + ', not the set-item tooltip path. Call Classify first.',
      );
    }

    const appended: ItemTooltipLine[] = [];

    // The colour in force at the END of the assembled string, which is what the price tail
    // inherits — 0x48da87 appends it with no AppendAsWideChar of its own. Every block below
    // carries it forward, because any of them can be the last non-empty one.
    let carriedColor: number = ItemTooltipColor.White;

    // --- var_4790, 0x48d88e-0x48d92a, then copied in at 0x48d948 ---------------------------
    for (let i = 0; i < set.pieces.length; ++i) {
      const piece = set.pieces[i] as SetPieceLine;

      const line = new ItemTooltipLine();
      line.text = piece.text;
      line.section = ItemTooltipSection.SetPieceList;
      line.color = piece.owned ? ItemTooltipColor.Set : ItemTooltipColor.Red;
      line.emitsColorMarker = true;

      // 0x48d93b prepends one more `ÿc2` to the assembled list. AppendAsWideChar no-ops on an
      // empty buffer (0x4521cd), so an empty list emits neither.
      if (i === 0) {
        line.leadingMarkerColor = ItemTooltipColor.Set;
      }

      carriedColor = ItemTooltipComposer.lastEmbeddedColor(line.text, line.color);
      appended.push(line);
    }

    // --- var_1538, 0x48d958 ----------------------------------------------------------------
    carriedColor = this.appendSetSection(
      appended,
      set.setName,
      ItemTooltipSection.SetName,
      ItemTooltipColor.Unique,
      carriedColor,
    );

    // --- var_3390, 0x48d96a-0x48d99c: the separator is INSIDE the non-empty test ------------
    if (!isNullOrEmpty(set.fullSetText)) {
      this.appendBlankRow(appended, ItemTooltipSection.FullSetBonus, carriedColor);
      carriedColor = this.appendSetSection(
        appended,
        set.fullSetText,
        ItemTooltipSection.FullSetBonus,
        ItemTooltipColor.Unique,
        carriedColor,
      );
    }

    // --- 0x48d9a9: unconditional, and it produces a blank row because the buffer above already
    //     ends in 3998 ---------------------------------------------------------------------
    this.appendBlankRow(appended, ItemTooltipSection.PartialSetBonus, carriedColor);

    // --- var_2F90, 0x48d9b6-0x48d9d0 -------------------------------------------------------
    carriedColor = this.appendSetSection(
      appended,
      set.partialText,
      ItemTooltipSection.PartialSetBonus,
      ItemTooltipColor.Set,
      carriedColor,
    );

    // --- var_4F90, 0x48d7df-0x48d83a, appended with ONE marker at 0x48d9e0 -----------------
    // The ethereal/socketed text and the modifier block share a buffer here, where the generic
    // path keeps them apart. Its gate is the SOCKETED flag alone (0x48d7e6), not the
    // ethereal-or-socketed test INV_FormatEtherealSocketedText itself makes, so an ethereal set
    // item that is not socketed gets no "Cannot Be Repaired" line.
    const sharedBufferStart = appended.length;

    if ((context.flags & ItemTooltipFlags.Socketed) !== 0) {
      carriedColor = this.appendSetSection(
        appended,
        this.sections.getSection(ItemTooltipSection.EtherealSocketed),
        ItemTooltipSection.EtherealSocketed,
        ItemTooltipColor.Magic,
        carriedColor,
      );
    }

    const modifiersStart = appended.length;

    const suppliedModifiers = this.sections.getSection(ItemTooltipSection.Modifiers);
    const afterModifiers = isNullOrEmpty(suppliedModifiers)
      ? this.appendModifiers(appended, packedStats)
      : this.appendSuppliedModifiers(appended, suppliedModifiers as string);

    if (appended.length !== modifiersStart) {
      carriedColor = afterModifiers;
    }

    // ONE buffer, so ONE AppendAsWideChar. Both helpers mark their own first row because in the
    // generic path the two buffers are separate; here the modifier block's marker has to go when
    // the ethereal text already claimed the buffer's.
    if (modifiersStart > sharedBufferStart && modifiersStart < appended.length) {
      (appended[modifiersStart] as ItemTooltipLine).emitsColorMarker = false;
    }

    // --- var_2138, appended whole at 0x48d9fe ----------------------------------------------
    for (const section of ItemTooltipComposer.SetGenericAppendOrder) {
      if (!context.isWeaponOrArmorType && ItemTooltipComposer.isWeaponOrArmorSection(section)) {
        continue;
      }

      // 0x48d681: BOTH the smite line and the block line sit inside `IsOfType(item, 51)`. The
      // generic path reaches INV_FormatDefenseRangeText for an Assassin's boots as well, so a set
      // boot is the one case where this writer emits strictly less — no Kick Damage line at all.
      if (
        !context.isShieldType &&
        (section === ItemTooltipSection.SmiteOrKickDamage ||
          section === ItemTooltipSection.BlockChance)
      ) {
        continue;
      }

      const text = this.sections.getSection(section);
      if (isNullOrEmpty(text)) {
        continue; // AppendAsWideChar no-ops, and there is no blank-row credit here
      }

      // 0x48d79a-0x48d7ae: the ONLY thing that reddens the name on this path is flag 0x100.
      // Quality is set by construction and the quest/rune/shop arms of resolveItemNameColor have
      // no call site in this writer.
      const color =
        section === ItemTooltipSection.ItemName
          ? (context.flags & ItemTooltipFlags.Broken) !== 0
            ? ItemTooltipColor.Red
            : ItemTooltipColor.Set
          : this.resolveSectionColor(section, context);

      let running = color;
      let firstOfSection = true;
      const sectionStat = ItemTooltipComposer.statOfSection(section);

      for (const part of this.splitLines(text as string)) {
        const line = new ItemTooltipLine();
        line.text = firstOfSection
          ? this.annotated(part, 0, sectionStat < 0 ? null : [sectionStat], running)
          : part;
        line.section = section;
        line.color = running;
        line.statId = firstOfSection ? sectionStat : -1;
        line.emitsColorMarker = firstOfSection;
        firstOfSection = false;
        appended.push(line);

        running = ItemTooltipComposer.lastEmbeddedColor(part, running);
      }

      carriedColor = running;
    }

    // --- the inlined cost tail, 0x48da03-0x48db00 ------------------------------------------
    if (context.shopMode >= 1 && context.shopMode <= 9) {
      const cost = this.sections.getSection(ItemTooltipSection.TransactionCost);

      if (!isNullOrEmpty(cost)) {
        // 0x48da64: the separator is skipped when the cost buffer is empty, and the price itself
        // gets NO colour marker (0x48da87).
        this.appendBlankRow(appended, ItemTooltipSection.TransactionCost, carriedColor);

        const line = new ItemTooltipLine();
        line.text = cost;
        line.section = ItemTooltipSection.TransactionCost;
        line.color = carriedColor;
        line.emitsColorMarker = false;
        appended.push(line);
      } else if (context.shopMode !== 4) {
        // 0x48da93-0x48daed. INV_FormatItemTooltipWithCost would also emit locale 22746 for an
        // ethereal item here (0x48cef9); this writer does not.
        this.appendBlankRow(appended, ItemTooltipSection.TransactionCost, carriedColor);
        this.appendSetSection(
          appended,
          set.transactionRefusedText,
          ItemTooltipSection.TransactionCost,
          ItemTooltipColor.Red,
          carriedColor,
        );
      }
    }

    this.mergeUnterminatedRuns(appended);

    appended.reverse();

    return appended;
  }

  private get lineTerminator(): string {
    return this.sections.lineTerminator ?? '';
  }

  /**
   * One AppendToBuffer of a whole buffer, preceded by one AppendAsWideChar. Empty buffers are
   * skipped rather than emitting a bare marker, which is what 0x4521cd does.
   */
  private appendSetSection(
    appended: ItemTooltipLine[],
    text: string | null,
    section: ItemTooltipSection,
    color: number,
    carried: number,
  ): number {
    if (isNullOrEmpty(text)) {
      return carried;
    }

    let running = color;
    let firstOfSection = true;
    for (const part of this.splitLines(text as string, false)) {
      const line = new ItemTooltipLine();
      line.text = part;
      line.section = section;
      line.color = running;
      line.emitsColorMarker = firstOfSection;
      firstOfSection = false;
      appended.push(line);

      running = ItemTooltipComposer.lastEmbeddedColor(part, running);
    }

    return running;
  }

  /**
   * A bare `AppendToBuffer(dest, str(3998))`. The buffer above it already ends in a terminator, so
   * the row it produces has no glyphs — and no marker, because there is no AppendAsWideChar in
   * front of it. It CARRIES the colour rather than resetting it: the game appends one character
   * here and it is not a marker.
   */
  private appendBlankRow(
    appended: ItemTooltipLine[],
    section: ItemTooltipSection,
    carried: number,
  ): void {
    const blank = new ItemTooltipLine();
    blank.text = this.lineTerminator;
    blank.section = section;
    blank.color = carried;
    blank.emitsColorMarker = false;
    appended.push(blank);
  }

  compose(
    context: ItemTooltipContext | null,
    packedStats: PackedStatEntries | null,
  ): readonly ItemTooltipLine[] {
    if (context === null) throw new ArgumentNullException('context');
    if (packedStats === null) throw new ArgumentNullException('packedStats');

    const kind = ItemTooltipComposer.classify(context);
    if (kind !== ItemTooltipKind.Generic) {
      throw new NotSupportedException(
        'This item is built by ' +
          kind +
          ', not the generic tooltip path. ' +
          'Call Classify first.',
      );
    }

    const appended: ItemTooltipLine[] = [];

    let carriedColor: number = ItemTooltipColor.White;

    for (const section of ItemTooltipComposer.AppendOrder) {
      if (
        section === ItemTooltipSection.TransactionCost &&
        (context.shopMode < 1 || context.shopMode > 9)
      ) {
        continue;
      }

      if (!context.isWeaponOrArmorType && ItemTooltipComposer.isWeaponOrArmorSection(section)) {
        continue;
      }

      if (section === ItemTooltipSection.Modifiers) {
        if ((context.flags & ItemTooltipFlags.Identified) !== 0) {
          // SKILLDESC_BuildStatBuffDesc returns at 0x4e60df before building anything
          // when the item is an elixir, so a provider that supplies text for this
          // section REPLACES the generated block rather than adding to it.
          const supplied = this.sections.getSection(ItemTooltipSection.Modifiers);

          const before = appended.length;
          const after = isNullOrEmpty(supplied)
            ? this.appendModifiers(appended, packedStats)
            : this.appendSuppliedModifiers(appended, supplied as string);

          if (appended.length !== before) {
            carriedColor = after;
          }
        }

        continue;
      }

      const text = this.sections.getSection(section);
      if (isNullOrEmpty(text)) {
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
        if (section === ItemTooltipSection.ItemName && appended.length !== 0) {
          const blankTop = new ItemTooltipLine();
          blankTop.text = this.sections.lineTerminator ?? '';
          blankTop.section = ItemTooltipSection.ItemName;
          blankTop.color = carriedColor;
          blankTop.emitsColorMarker = false;
          appended.push(blankTop);
        }

        continue;
      }

      const parts = [...this.splitLines(text as string)];

      let color: number;
      if (section === ItemTooltipSection.TransactionCost) {
        color = carriedColor;
      } else {
        color = this.resolveSectionColor(section, context);
      }

      let running = color;
      let firstOfSection = true;
      const sectionStat = ItemTooltipComposer.statOfSection(section);

      for (const part of parts) {
        const line = new ItemTooltipLine();
        line.text = firstOfSection
          ? this.annotated(part, 0, sectionStat < 0 ? null : [sectionStat], running)
          : part;
        line.section = section;
        line.color = running;
        line.statId = firstOfSection ? sectionStat : -1;

        line.emitsColorMarker = firstOfSection;
        firstOfSection = false;
        appended.push(line);

        running = ItemTooltipComposer.lastEmbeddedColor(part, running);
      }

      carriedColor = running;
    }

    this.mergeUnterminatedRuns(appended);

    appended.reverse();

    return appended;
  }

  private mergeUnterminatedRuns(appendOrder: ItemTooltipLine[]): void {
    const terminator = this.sections.lineTerminator;
    if (isNullOrEmpty(terminator)) {
      return;
    }

    for (let i = 0; i < appendOrder.length - 1;) {
      const line = appendOrder[i] as ItemTooltipLine;
      if (line.text !== null && line.text.endsWith(terminator as string)) {
        ++i;
        continue;
      }

      const swallowed = appendOrder[i + 1] as ItemTooltipLine;

      // Splice a marker only ACROSS sections. Each of the 18 buffers gets its own
      // AppendAsWideChar, so a merged line really does change colour part way through;
      // within a section the game emits nothing between the parts (the only producer of
      // an unterminated part is a PreJoined stat line, and 0x48ea1c gives the whole stat
      // block one marker), so splicing there would invent 3 characters.
      const crossesSection = swallowed.section !== line.section;
      const splicesMarker = swallowed.emitsColorMarker && crossesSection;

      line.text =
        (line.text ?? '') +
        (splicesMarker
          ? ItemTooltipColor.Marker + ItemTooltipComposer.encodeColorDigit(swallowed.color)
          : '') +
        (swallowed.text ?? '');

      // The swallowed line was its section's first-APPENDED one, so it owned the game's
      // single marker for that section. Record it: if the section has further lines they
      // survive this merge and must not be charged for that marker again.
      if (splicesMarker) {
        line.splicedSection = swallowed.section;
      }

      appendOrder.splice(i + 1, 1);
    }
  }

  static readonly MaxTooltipLength = 1023;

  /**
   * ITEM_BuildSetItemTooltip has no 1023 cut: LoadItemDesc truncates explicitly at 0x48ed12 but
   * the set writer goes straight from MoveArgumentToEAX (0x48db0b) to TEXT_CalcTextDimensions
   * (0x48db1d), and its output buffer is 2048 WCHARs with no guard. So the budget is a knob rather
   * than a constant — pass this to spend nothing.
   */
  static readonly UnlimitedTooltipLength = Number.MAX_SAFE_INTEGER;

  render(
    lines: Iterable<ItemTooltipLine> | null,
    questColorPrefix = false,
    maxLength: number = ItemTooltipComposer.MaxTooltipLength,
  ): string {
    if (lines === null) throw new ArgumentNullException('lines');

    const ordered: ItemTooltipLine[] = Array.isArray(lines)
      ? (lines as ItemTooltipLine[])
      : [...lines];

    let builder = '';
    for (const line of ItemTooltipComposer.applyAppendOrderBudget(
      ordered,
      ItemTooltipColor.Marker,
      questColorPrefix,
      this.sections.lineTerminator,
      maxLength,
    )) {
      builder += line.text ?? '';
    }

    return this.dropTrailingTerminator(builder);
  }

  private dropTrailingTerminator(assembled: string): string {
    const terminator = this.sections.lineTerminator;

    if (isNullOrEmpty(terminator) || !assembled.endsWith(terminator as string)) {
      return assembled;
    }

    return assembled.substring(0, assembled.length - (terminator as string).length);
  }

  renderWithColorCodes(
    lines: Iterable<ItemTooltipLine> | null,
    colorMarker: string | null = ItemTooltipColor.Marker,
    questColorPrefix = false,
    maxLength: number = ItemTooltipComposer.MaxTooltipLength,
  ): string {
    if (lines === null) throw new ArgumentNullException('lines');

    const all: ItemTooltipLine[] = Array.isArray(lines) ? (lines as ItemTooltipLine[]) : [...lines];

    const ordered = ItemTooltipComposer.applyAppendOrderBudget(
      all,
      colorMarker,
      questColorPrefix,
      this.sections.lineTerminator,
      maxLength,
    );

    return this.emit(ordered, colorMarker, questColorPrefix);
  }

  private emit(
    ordered: ItemTooltipLine[],
    colorMarker: string | null,
    questColorPrefix: boolean,
  ): string {
    let builder = '';

    for (let i = 0; i < ordered.length; ++i) {
      const line = ordered[i] as ItemTooltipLine;

      if (isNullOrEmpty(line.text)) {
        continue; // AppendAsWideChar skips empty buffers entirely
      }

      // A SECOND game marker in front of the row's own, and the only producer of one is
      // 0x48d93b — see ItemTooltipLine.leadingMarkerColor.
      if (line.leadingMarkerColor >= 0) {
        builder += colorMarker ?? '';
        builder += ItemTooltipComposer.encodeColorDigit(line.leadingMarkerColor);
      }

      if (ItemTooltipComposer.willEmitMarker(line, colorMarker, this.sections.lineTerminator)) {
        builder += colorMarker ?? '';
        builder += ItemTooltipComposer.encodeColorDigit(line.color);
      }

      builder += line.text ?? '';
    }

    let assembled = this.dropTrailingTerminator(builder);

    if (questColorPrefix) {
      assembled +=
        (colorMarker ?? '') + ItemTooltipComposer.encodeColorDigit(ItemTooltipColor.Unique);
    }

    return assembled;
  }

  private static markerLength(colorMarker: string | null): number {
    return (colorMarker === null ? 0 : colorMarker.length) + 1;
  }

  /**
   * Two markers stack here, and they are different things.
   *
   * The GAME's own: `AppendAsWideChar` (0x4521c0) prepends one marker to each section BUFFER, which
   * lands on that section's first-APPENDED row and is what `ItemTooltipLine.emitsColorMarker`
   * records. It is unconditional bar an empty buffer, so it stacks on top of a marker the writer
   * already put in the text — which is why `ÿc0ÿc0Chance to Block:` is real
   * (INV_FormatBlockChanceText 0x485d0e, then LoadItemDesc 0x48eb80), and why a blank first row
   * comes out as a bare colour code.
   *
   * The DISPLAY re-anchor: the game's buffer is append order and drawn bottom-up (0x501c17), so it
   * never produces a display-ordered string, and reversing the rows breaks the stickiness
   * (0x501bec) every later row of a section relied on. Those rows are re-anchored with the colour
   * that WAS in force at them, which is what `ItemTooltipLine.color` carries. A row that already
   * opens with a marker needs no anchor — it states its own colour — and a row with no glyphs gets
   * none, because a marker there would draw a colour code instead of a blank line.
   */
  private static willEmitMarker(
    line: ItemTooltipLine,
    colorMarker: string | null,
    terminator: string | null,
  ): boolean {
    if (line.emitsColorMarker) {
      return true;
    }

    if (!isNullOrEmpty(terminator) && line.text === terminator) {
      return false;
    }

    return isNullOrEmpty(colorMarker) || !(line.text ?? '').startsWith(colorMarker as string);
  }

  static encodeColorDigit(color: number): string {
    return String.fromCharCode((0x30 + color) & 0xffff);
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
  private static applyAppendOrderBudget(
    displayOrder: ItemTooltipLine[],
    colorMarker: string | null,
    questColorPrefix: boolean,
    terminator: string | null,
    maxLength: number,
  ): ItemTooltipLine[] {
    const markerLength = ItemTooltipComposer.markerLength(colorMarker);

    // 0x48ecf2 prepends the quest marker before the length is taken, so it costs budget
    // even though it paints nothing.
    let used = questColorPrefix ? markerLength : 0;

    const keptAppendOrder: ItemTooltipLine[] = [];

    // Walks APPEND order, which is display order reversed.
    for (let i = displayOrder.length - 1; i >= 0; --i) {
      const line = displayOrder[i] as ItemTooltipLine;
      const next = displayOrder[i + 1];
      const text = line.text ?? '';

      // Last clause: MergeUnterminatedRuns may have spliced this section's marker into
      // the previously-appended line's text, where it is already counted.
      const carriesGameMarker =
        line.section !== ItemTooltipSection.TransactionCost &&
        (i === displayOrder.length - 1 || next === undefined || next.section !== line.section) &&
        !(
          i + 1 < displayOrder.length &&
          next !== undefined &&
          next.splicedSection === line.section
        );

      let measured = text.length;
      if (i === 0 && !isNullOrEmpty(terminator) && text.endsWith(terminator as string)) {
        measured -= (terminator as string).length;
      }

      const overhead = carriesGameMarker ? markerLength : 0;
      if (used + overhead >= maxLength) {
        const remaining = maxLength - used;

        const blank = new ItemTooltipLine();
        blank.text =
          (remaining === 1 && !isNullOrEmpty(colorMarker)
            ? (colorMarker as string).substring(0, 1)
            : '') + (terminator ?? '');
        blank.section = line.section;

        blank.color =
          i + 1 < displayOrder.length && next !== undefined
            ? ItemTooltipComposer.lastEmbeddedColor(next.text, next.color)
            : ItemTooltipColor.White;
        blank.emitsColorMarker = remaining === 1;

        keptAppendOrder.push(blank);
        break;
      }

      used += overhead;

      if (used + measured <= maxLength) {
        used += measured;
        keptAppendOrder.push(line);
        continue;
      }

      let cut = maxLength - used;

      if (
        !isNullOrEmpty(colorMarker) &&
        cut >= (colorMarker as string).length &&
        text.substring(cut - (colorMarker as string).length, cut) === colorMarker
      ) {
        cut -= colorMarker.length;
      }

      const partial = new ItemTooltipLine();
      partial.text = text.substring(0, cut) + (terminator ?? '');
      partial.section = line.section;
      partial.color = line.color;
      partial.emitsColorMarker = line.emitsColorMarker;
      keptAppendOrder.push(partial);
      break;
    }

    keptAppendOrder.reverse();
    return keptAppendOrder;
  }

  /**
   * Supplies the range text to append to a line, or null for none. Null by default, so an
   * un-annotated render is byte-identical to what the game draws — the corpus never sets it and the
   * differential holds that.
   */
  rangeAnnotation: ((shownStats: readonly number[], layer: number) => string | null) | null = null;

  /**
   * The colour the annotation is painted in, or -1 to inherit the line's. A marker restoring the
   * line's own colour follows it, so the rest of the line is unaffected — and the running colour is
   * tracked from the UN-annotated text, so an annotation can never bleed into the next line.
   */
  rangeColor = -1;

  /**
   * The single stat a section displays, or -1. Only the Defense line qualifies: it shows one stat
   * whose base genuinely rolls. Durability and the damage lines are excluded on purpose — their
   * base columns do not roll, so a span there would be about the `dur%` or `dmg%` modifier and
   * belongs on that modifier's own line, where it already is.
   */
  private static statOfSection(section: ItemTooltipSection): number {
    return section === ItemTooltipSection.ArmorClass ? ItemTooltipComposer.StatArmorClass : -1;
  }

  private static readonly StatArmorClass = 31;

  /**
   * Appends the range text INSIDE the line — before its trailing terminator, since `splitLines`
   * keeps that on the part it belongs to and appending after it would put the annotation on the
   * following line.
   */
  private annotated(
    part: string,
    layer: number,
    shownStats: readonly number[] | null,
    lineColor: number,
  ): string {
    if (this.rangeAnnotation === null || shownStats === null || shownStats.length === 0) {
      return part;
    }

    let annotation = this.rangeAnnotation(shownStats, layer);
    if (annotation === null || annotation.length === 0) {
      return part;
    }

    if (this.rangeColor >= 0 && this.rangeColor !== lineColor) {
      annotation =
        ItemTooltipColor.Marker +
        String(this.rangeColor) +
        annotation +
        ItemTooltipColor.Marker +
        String(lineColor);
    }

    const terminator = this.sections.lineTerminator ?? '';
    if (terminator.length !== 0 && part.endsWith(terminator)) {
      return part.slice(0, part.length - terminator.length) + annotation + terminator;
    }

    return part + annotation;
  }

  private *splitLines(text: string, terminateTrailing = true): Generator<string> {
    const terminator = this.sections.lineTerminator;
    if (isNullOrEmpty(terminator)) {
      yield text;
      return;
    }

    const end = terminator as string;

    let start = 0;
    while (start < text.length) {
      const at = text.indexOf(end, start);
      if (at < 0) {
        yield terminateTrailing ? text.substring(start) + end : text.substring(start);
        return;
      }

      yield text.substring(start, at + end.length);
      start = at + end.length;
    }
  }

  /**
   * The elixir case: the provider hands over the whole block already built, so it is split and
   * coloured exactly as a generated one would be.
   */
  private appendSuppliedModifiers(lines: ItemTooltipLine[], text: string): number {
    let running: number = ItemTooltipColor.Magic;
    let firstOfSection = true;

    for (const part of this.splitLines(text, false)) {
      const line = new ItemTooltipLine();
      line.text = part;
      line.section = ItemTooltipSection.Modifiers;
      line.color = running;

      line.emitsColorMarker = firstOfSection;
      firstOfSection = false;
      lines.push(line);

      running = ItemTooltipComposer.lastEmbeddedColor(part, running);
    }

    return running;
  }

  /**
   * Just the blue block, in display order. Used by the breakdown view, which shows the modifiers
   * from one source at a time. It goes through appendModifiers rather than rebuilding the loop so
   * the colour carry and the terminator split cannot drift, and it reverses for the same reason
   * compose does — the game appends bottom row first.
   */
  composeModifiersOnly(packedStats: PackedStatEntries): readonly ItemTooltipLine[] {
    const lines: ItemTooltipLine[] = [];
    this.appendModifiers(lines, packedStats);
    lines.reverse();
    return lines;
  }

  private appendModifiers(lines: ItemTooltipLine[], packedStats: PackedStatEntries): number {
    const terminator = this.sections.lineTerminator ?? '';

    let running: number = ItemTooltipColor.Magic;
    let firstOfSection = true;

    for (const modifier of this.modifiers.describe(packedStats)) {
      const text: string = modifier.preJoined ? modifier.text : modifier.text + terminator;

      let firstPart = true;

      for (const part of this.splitLines(text, false)) {
        const line = new ItemTooltipLine();

        // An aggregated line speaks for several stats, so one stat's span against it would be
        // unattributable to either half — the line still carries statId and layer, which is what a
        // caller wanting a richer display works from.
        // An aggregated line gets its stats named, so the formatter can show a composite span
        // rather than one number belonging to neither half.
        line.text = firstPart
          ? this.annotated(part, modifier.layer, modifier.shownStats ?? [modifier.statId], running)
          : part;
        line.section = ItemTooltipSection.Modifiers;
        line.color = running;
        line.statId = modifier.statId;
        line.layer = modifier.layer;
        line.shownStats = modifier.shownStats;
        line.aggregated = modifier.aggregated;

        line.emitsColorMarker = firstOfSection;
        firstOfSection = false;
        firstPart = false;
        lines.push(line);

        running = ItemTooltipComposer.lastEmbeddedColor(part, running);
      }
    }

    return running;
  }

  private static lastEmbeddedColor(text: string | null, fallback: number): number {
    if (isNullOrEmpty(text)) {
      return fallback;
    }

    const source = text as string;

    let color = fallback;
    for (let i = 0; i + ItemTooltipColor.Marker.length < source.length; ++i) {
      if (source.substring(i, i + ItemTooltipColor.Marker.length) !== ItemTooltipColor.Marker) {
        continue;
      }

      color = source.charCodeAt(i + ItemTooltipColor.Marker.length) - 0x30;
    }

    return color;
  }

  private static isWeaponOrArmorSection(section: ItemTooltipSection): boolean {
    switch (section) {
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

  private resolveSectionColor(section: ItemTooltipSection, context: ItemTooltipContext): number {
    switch (section) {
      case ItemTooltipSection.RequiredLevel:
      case ItemTooltipSection.RequiredStrength:
      case ItemTooltipSection.RequiredDexterity:
      case ItemTooltipSection.ClassRestriction:
        return this.sections.isRequirementUnmet(section)
          ? ItemTooltipColor.Red
          : ItemTooltipColor.White;

      case ItemTooltipSection.ItemName:
        return ItemTooltipComposer.resolveItemNameColor(context);

      case ItemTooltipSection.EtherealSocketed:
        return ItemTooltipColor.Magic; // literal 3 at 0x48e993

      case ItemTooltipSection.Unidentified:
        return ItemTooltipColor.Red; // literal 1 at 0x48ea39

      case ItemTooltipSection.RuneLetters:
        return ItemTooltipColor.Unique; // literal 4 at 0x48ebac

      case ItemTooltipSection.QuestUsage:
        // 0x48ecf2 prepends colour 4 to the FINISHED buffer, and it is reached for any quest item
        // whose code is not `leg ` (0x48ec58 compares the dword 0x2067656C). Prepending to an
        // append-ordered buffer puts it at the head of the FIRST appended row, which is the BOTTOM
        // display row — this one. Unconditionally 4: the red difficulty variant lives in
        // GetItemName (0x48cb50) and colours the name buffer alone.
        return context.isQuestItem && !context.isWirtsLeg
          ? ItemTooltipColor.Unique
          : ItemTooltipColor.White;

      default:
        return ItemTooltipColor.White;
    }
  }

  static resolveItemNameColor(context: ItemTooltipContext | null): number {
    if (context === null) throw new ArgumentNullException('context');

    let color: number;

    switch (context.quality) {
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
      default: {
        const socketedOrEthereal =
          (context.flags & (ItemTooltipFlags.Socketed | ItemTooltipFlags.Ethereal)) !== 0;
        color = socketedOrEthereal ? ItemTooltipColor.SocketedOrEthereal : ItemTooltipColor.White;
        break;
      }
    }

    if (context.unidentifiedInShop) {
      color = ItemTooltipColor.White;
    }

    if (context.forcesCraftedColor) {
      color = ItemTooltipColor.Crafted;
    }

    if ((context.flags & ItemTooltipFlags.Broken) !== 0) {
      color = ItemTooltipColor.Red;
    }

    // The quest colour is NOT part of this. GetItemName prepends it INSIDE the name buffer
    // (0x48cb50 red / 0x48ce6d gold), so it belongs to the section's TEXT, and LoadItemDesc then
    // prepends v105 — the value computed above — in front of it. The game really does draw both:
    // `ÿc0ÿc4Horadric Cube`. Folding the quest colour in here collapsed them to one and lost the
    // section marker. See RecordSections.questNameColorPrefix.
    return color;
  }
}

/** C# `string.IsNullOrEmpty`. */
