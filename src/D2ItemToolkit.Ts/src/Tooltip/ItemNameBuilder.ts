import { ItemIdentity, ItemRecordFlags } from '../Stats/ItemRecord.js';
import { ItemTable } from '../Tables/ItemTable.js';
import { ItemTooltipColor } from './ItemTooltip.js';
import { ItemTypeTree } from '../Tables/ItemTypeTree.js';
import { D2DataFiles, TxtKeys } from '../Tables/TxtDataProviders.js';
import { DescStringIds, isNullOrEmpty } from '../Types.js';

// GetItemName 0x48c060. Locale ids it uses; the format strings are POSITIONAL (%0 %1 %2),
// not printf.
export const NameStringIds = {
  SuperiorFormat: 1711, // "%0 %1"
  LowQualityFormat: 1712, // "%0 %1"
  MagicFormat: 1714, // "%0 %1 %2"
  GemmedFormat: 1715, // "%0 %1"
  RareFormat: 1718, // "%0 %1"
  Superior: 1727, // "Superior"
  Gemmed: 1728, // "Gemmed"
  BodyPartFormat: 1716, // "%0 %1"
  SetItemFormat: 10089, // "%0"

  // The quality-2 ear arm at 0x48c2b3.
  EarHardcore: 5126, // extra line when the Named flag is set
  EarLevelLabel: 4141, // 0x102D

  // INV_GetInventoryPageName 0x484a70, indexed by the ear's fileIndex (the dead player's
  // class). Anything at 7 or above HALTS the game rather than falling back.
  ClassName: [4011, 4010, 4009, 4008, 4007, 10097, 10098] as readonly number[],

  // 0x48c542: tome versus scroll, by magic suffix. Suffix 0 and 1 are the only ones handled;
  // anything else leaves the switch having written NOTHING.
  TomeFirst: 2199,
  ScrollFirst: 2200,
  TomeSecond: 2201,
  ScrollSecond: 2202,
} as const;

export const ItemQualityNo = {
  Inferior: 1,
  Normal: 2,
  Superior: 3,
  Magic: 4,
  Set: 5,
  Rare: 6,
  Unique: 7,
  Craft: 8,
  Tempered: 9,
} as const;

export class ItemNameBuilder {
  private readonly data: D2DataFiles;
  private readonly items: ItemTable;
  private readonly types: ItemTypeTree | null;

  constructor(
    data: D2DataFiles | null,
    items: ItemTable | null,
    types: ItemTypeTree | null = null,
  ) {
    if (data === null) throw new Error('data');
    if (items === null) throw new Error('items');

    this.data = data;
    this.items = items;
    this.types = types ?? (data.itemTypes === null ? null : new ItemTypeTree(data.itemTypes));
  }

  // Returns the name, or null when the arm writes nothing. Runeword handling and the two-line
  // runeword form are not modelled here; the quest COLOUR is the composer's, not the text's.
  //
  // `filledSockets` is ITEM_ItemsInItem(pInventory) (0x48c4b5), which only the normal arm
  // reads.
  build(item: ItemIdentity | null, filledSockets = 0): string | null {
    if (item === null) {
      return null;
    }

    const baseName = this.baseName(item.classId);

    const name = this.arm(item, baseName, filledSockets);

    // 0x48caff: INV_FormatPlayerNameOnItem rewrites the WHOLE buffer, whichever arm built
    // it — including the unidentified one, which reaches the tail through 0x48ce54.
    return ItemNameBuilder.personalizeWholeName(item, name);
  }

  private arm(item: ItemIdentity, baseName: string, filledSockets: number): string | null {
    // 0x48c10b/0x48c11a: the runeword flag is tested FIRST — before the identified test at
    // 0x48c1ea and before the quality jump table at 0x48c209 — so neither applies here and
    // a runeword is never "Superior" or "Gemmed".
    //
    // wMagicPrefix[0] is not an affix index on a runeword. ITEM_DeserializeFromBitBuffer
    // 0x62d1ea stores 16 bits straight into it, sourced from runes.txt +0x82, which
    // TXT_AllocTxt_runes 0x639c63 fills with STRTABLE_LookupString of the `Name` column.
    // That is a locale id in GetLocaleString's own space, so it resolves with GetByIndex
    // rather than through the affix tables (0x48c17a/0x48c17f/0x48c181).
    if (item.has(ItemRecordFlags.Runeword)) {
      return (
        baseName +
        this.str(DescStringIds.Newline) +
        ItemTooltipColor.Marker +
        '4' +
        this.str(item.magicPrefix[0] ?? 0)
      );
    }

    // 0x48c1f1: unidentified items show the base name only, whatever the quality.
    if (!item.has(ItemRecordFlags.Identified)) {
      return baseName;
    }

    switch (item.quality) {
      case ItemQualityNo.Inferior:
        return this.lowQuality(item, baseName);

      case ItemQualityNo.Superior:
        return this.format2(
          NameStringIds.SuperiorFormat,
          this.str(NameStringIds.Superior),
          baseName,
        );

      case ItemQualityNo.Magic:
        return this.magic(item, baseName);

      case ItemQualityNo.Set:
        return this.set(item, baseName);

      case ItemQualityNo.Rare:
      case ItemQualityNo.Craft:
      case ItemQualityNo.Tempered:
        return this.rare(item, baseName);

      case ItemQualityNo.Unique:
        return this.unique(item, baseName);

      default:
        return this.normal(item, baseName, filledSockets);
    }
  }

  /**
   * INV_FormatPlayerNameOnItem 0x484c90. It needs the PERSONALIZED flag (0x1000000) and a
   * quality OUTSIDE 5..9 (0x484cb8, an unsigned `quality - 5 <= 4` skip) — set, rare,
   * unique, crafted and tempered personalise inside their own arms instead, through
   * INV_FormatPlayerNameWithBase. The budget is 512 wide characters, not the ear's 100.
   */
  private static personalizeWholeName(item: ItemIdentity, name: string | null): string | null {
    if (name === null || !item.has(ItemRecordFlags.Personalized)) {
      return name;
    }

    if (item.quality >= ItemQualityNo.Set && item.quality <= ItemQualityNo.Tempered) {
      return name;
    }

    return ItemNameBuilder.possessive(item.playerName, name, ItemNameBuilder.WholeNameBudget);
  }

  private static readonly WholeNameBudget = 512;

  /**
   * INV_FormatPlayerNameWithBase 0x484d30, the form the set, unique and rare arms call on
   * one PIECE of the name. It has no quality test of its own and its RESULT flag is the
   * personalized flag alone (0x484d94 versus 0x484da0) — the arms branch on that, not on
   * whether the possessive fitted the budget.
   */
  private static tryPersonalizePart(
    item: ItemIdentity,
    part: string,
  ): { personalized: boolean; named: string } {
    if (!item.has(ItemRecordFlags.Personalized)) {
      return { personalized: false, named: part };
    }

    return {
      personalized: true,
      named: ItemNameBuilder.possessive(item.playerName, part, ItemNameBuilder.WholeNameBudget),
    };
  }

  private normal(item: ItemIdentity, baseName: string, filledSockets: number): string | null {
    // The quality-2 arm tries four branches in this order (0x48c26e, 0x48c27e, 0x48c45a),
    // and only the last one reaches the socketed/plain fallback.
    if (this.isOfType(item, 'scro') || this.isOfType(item, 'book')) {
      return this.tomeOrScroll(item);
    }

    if (this.isOfType(item, 'play')) {
      return this.ear(item, baseName);
    }

    if (this.isOfType(item, 'body')) {
      return this.monsterBodyPart(item, baseName);
    }

    // 0x48c4b5: a three-way gate — the SOCKETED flag, a non-null pInventory, and
    // ITEM_ItemsInItem above zero. An EMPTY socketed item keeps its plain base name.
    if (item.has(ItemRecordFlags.Socketed) && filledSockets > 0) {
      return this.format2(NameStringIds.GemmedFormat, this.str(NameStringIds.Gemmed), baseName);
    }

    return baseName;
  }

  /**
   * 0x48c464 — a monster's body part. The item's fileIndex is a monstats row, and format 1716
   * pairs that creature's NameStr with the part's own base name.
   *
   * The misc.txt `name` column labels these rows "Not used", but `namestr` resolves to real
   * localised names ("Heart", "Brain", ...), so the arm produces sensible output. Whether such an
   * item ever spawns in 1.14d is a separate question and not established here.
   */
  private monsterBodyPart(item: ItemIdentity, baseName: string): string {
    if (!this.data.monsterTypes.monsterExists(item.fileIndex)) {
      return baseName;
    }

    const monster = this.data.monsterTypes.getMonsterName(item.fileIndex);

    return isNullOrEmpty(monster)
      ? baseName
      : this.format2(NameStringIds.BodyPartFormat, monster, baseName);
  }

  /**
   * 0x48c542. A tome and a scroll of the same spell differ only by the item's PRIMARY type
   * being "book", and the spell comes from magic suffix slot 0. A suffix above 1 writes
   * nothing at all — the switch breaks out with the buffer untouched.
   */
  private tomeOrScroll(item: ItemIdentity): string | null {
    const tome = this.isOfType(item, 'book');

    switch (item.magicSuffix[0]) {
      case 0:
        return this.str(tome ? NameStringIds.TomeFirst : NameStringIds.ScrollFirst);

      case 1:
        return this.str(tome ? NameStringIds.TomeSecond : NameStringIds.ScrollSecond);

      default:
        return null;
    }
  }

  /**
   * 0x48c2b3 — a player's ear. Four appended lines, which the bottom-up renderer then shows in
   * reverse, so the possessive name ends up on top:
   *
   *     [locale 5126 when the Named flag is set]
   *     locale 4141 + " " + earLevel
   *     the dead player's class name, from fileIndex
   *     "&lt;playerName&gt;'s &lt;base&gt;"
   */
  private ear(item: ItemIdentity, baseName: string): string {
    let text = '';
    const newline = this.str(DescStringIds.Newline);

    // 0x48c346: the Named flag prepends an extra line ahead of everything else.
    if (item.has(ItemRecordFlags.Named)) {
      text += this.str(NameStringIds.EarHardcore) + newline;
    }

    text +=
      this.str(NameStringIds.EarLevelLabel) +
      this.str(DescStringIds.Space) +
      String(item.earLevel) +
      newline;

    // 0x48c3b9: the ear's fileIndex IS the dead player's class.
    if (item.fileIndex >= 0 && item.fileIndex < NameStringIds.ClassName.length) {
      text += this.str(NameStringIds.ClassName[item.fileIndex] ?? 0) + newline;
    }

    return text + ItemNameBuilder.possessive(item.playerName, baseName, ItemNameBuilder.EarBudget);
  }

  // The ear arm's own call passes 100 (0x48c440), unlike the two personalisation helpers.
  private static readonly EarBudget = 100;

  /**
   * UNICODE_FormatPossessiveName 0x5272b0 for language code 0. The suffix is the dword
   * 0x00207327 stored at 0x52737f — apostrophe, 's', space. The other twelve language cases
   * are NOT transcribed; French for instance prefixes " d'" (0x00276420 at 0x527467).
   *
   * When the two names together would not fit the caller's 100 wide characters it drops the
   * possessive and yields the base name alone (0x5272f6).
   */
  private static possessive(owner: string, baseName: string, budget: number): string {
    if (isNullOrEmpty(owner)) {
      return baseName;
    }

    // 0x5272e1: len(base) + len(owner) + 5 against the budget.
    if (baseName.length + owner.length + 5 > budget) {
      return baseName;
    }

    return owner + "'s " + baseName;
  }

  private isOfType(item: ItemIdentity, code: string): boolean {
    if (this.types === null) {
      return false;
    }

    return this.types.isOfType(
      this.types.row(this.items.primaryTypeCode(item.classId)),
      this.types.row(this.items.secondaryTypeCode(item.classId)),
      this.types.row(code),
    );
  }

  // 0x48c210. A null lowqualityitems row writes NOTHING (0x48c220) — reachable, since
  // dwFileIndex is 3 bits against only 4 rows.
  private lowQuality(item: ItemIdentity, baseName: string): string | null {
    const table = this.data.lowQualityItems;
    if (table === null || item.fileIndex < 0 || item.fileIndex >= table.rowCount) {
      return null;
    }

    const prefix = TxtKeys.text(table, item.fileIndex, 'Name', this.data.strings);
    return this.format2(NameStringIds.LowQualityFormat, prefix, baseName);
  }

  // 0x48cba9. Prefix and suffix index the CONCATENATED magic affix array, 1-based.
  private magic(item: ItemIdentity, baseName: string): string {
    const prefix = this.magicAffix(item.magicPrefix[0] ?? 0);
    const suffix = this.magicAffix(item.magicSuffix[0] ?? 0);

    return this.format3(NameStringIds.MagicFormat, prefix, baseName, suffix);
  }

  // 0x48c5c1. Rare, crafted and tempered are byte-for-byte identical arms. The base name is
  // FIRST, then a newline, then the two affixes.
  private rare(item: ItemIdentity, baseName: string): string {
    const first = this.rareAffix(item.rarePrefix);
    const second = this.rareAffix(item.rareSuffix);

    // 0x48c8ea: the affix line — not the base name above it — is what gets personalised.
    const affixes = ItemNameBuilder.tryPersonalizePart(
      item,
      this.format2(NameStringIds.RareFormat, first, second),
    ).named;

    return baseName + this.str(DescStringIds.Newline) + affixes;
  }

  // 0x48ca1c. Base name, newline, then the set item's own name wrapped in format 10089.
  private set(item: ItemIdentity, baseName: string): string | null {
    const table = this.data.setItems;
    if (table === null || item.fileIndex < 0 || item.fileIndex >= table.rowCount) {
      return null;
    }

    const setName = TxtKeys.text(table, item.fileIndex, 'index', this.data.strings);
    if (isNullOrEmpty(setName)) {
      return null;
    }

    // 0x48cae3: when INV_FormatPlayerNameWithBase succeeds its text REPLACES the 10089
    // wrapper rather than being wrapped by it.
    const personalized = ItemNameBuilder.tryPersonalizePart(item, setName as string);
    const tail = personalized.personalized
      ? personalized.named
      : this.format1(NameStringIds.SetItemFormat, setName);

    return baseName + this.str(DescStringIds.Newline) + tail;
  }

  // 0x48c920. `SkipName` suppresses the base-name line; there is no format wrapper.
  private unique(item: ItemIdentity, baseName: string): string {
    const table = this.data.uniqueItems;
    if (table === null || item.fileIndex < 0 || item.fileIndex >= table.rowCount) {
      return baseName;
    }

    const uniqueName = TxtKeys.text(table, item.fileIndex, 'index', this.data.strings);
    if (isNullOrEmpty(uniqueName)) {
      return baseName;
    }

    // 0x48c9e1: the unique name alone is personalised, whether or not SkipName suppressed
    // the base line above it.
    const named = ItemNameBuilder.tryPersonalizePart(item, uniqueName as string).named;

    if (this.items.getInt(item.classId, 'SkipName') !== 0) {
      return named;
    }

    return baseName + this.str(DescStringIds.Newline) + named;
  }

  private baseName(classId: number): string {
    const at = this.items.tryResolve(classId);
    if (at === null) {
      return '';
    }

    return TxtKeys.text(at.file, at.row, 'namestr', this.data.strings) ?? '';
  }

  // TXT_magicaffixes_GetLine 0x633ee0: 1-based over [MagicSuffix][MagicPrefix][automagic].
  private magicAffix(id: number): string {
    if (id <= 0) {
      return '';
    }

    let at = id - 1;

    for (const table of [this.data.magicSuffix, this.data.magicPrefix, this.data.autoMagic]) {
      if (table === null) {
        continue;
      }

      if (at < table.rowCount) {
        return TxtKeys.text(table, at, 'Name', this.data.strings) ?? '';
      }

      at -= table.rowCount;
    }

    return '';
  }

  // TXT_RareAffixes_GetLine 0x634260: 1-based over [RareSuffix][RarePrefix].
  private rareAffix(id: number): string {
    if (id <= 0) {
      return '';
    }

    let at = id - 1;

    for (const table of [this.data.rareSuffix, this.data.rarePrefix]) {
      if (table === null) {
        continue;
      }

      if (at < table.rowCount) {
        return TxtKeys.text(table, at, 'name', this.data.strings) ?? '';
      }

      at -= table.rowCount;
    }

    return '';
  }

  private str(id: number): string {
    return this.data.strings.getByIndex(id) ?? '';
  }

  private format1(formatId: number, a: string | null): string {
    return ItemNameBuilder.positional(this.str(formatId), a, null, null);
  }

  private format2(formatId: number, a: string | null, b: string | null): string {
    return ItemNameBuilder.positional(this.str(formatId), a, b, null);
  }

  private format3(formatId: number, a: string | null, b: string | null, c: string | null): string {
    return ItemNameBuilder.positional(this.str(formatId), a, b, c);
  }

  // The engine's POSITIONAL formatter: %0, %1, %2 select arguments. A missing argument leaves
  // an empty slot, which is why a magic item with one affix renders with a doubled space.
  // Not private because wsprintf 0x48be80 is the SAME routine the set-item piece list writes
  // through at 0x48d8dd, and two copies of it would be two things to keep in step.
  static positional(format: string, a: string | null, b: string | null, c: string | null): string {
    if (isNullOrEmpty(format)) {
      return '';
    }

    let text = '';

    for (let i = 0; i < format.length; ++i) {
      if (format[i] !== '%' || i + 1 >= format.length) {
        text += format[i];
        continue;
      }

      const which = format[i + 1];
      switch (which) {
        case '0':
          text += a ?? '';
          ++i;
          break;
        case '1':
          text += b ?? '';
          ++i;
          break;
        case '2':
          text += c ?? '';
          ++i;
          break;
        default:
          text += format[i];
          break;
      }
    }

    return text;
  }
}

/** C# `string.IsNullOrEmpty`. */
