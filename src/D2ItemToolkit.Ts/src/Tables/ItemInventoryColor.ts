import type { TxtFile } from '../Data/TxtFile.js';
import { ItemQualityNo } from '../Tooltip/ItemNameBuilder.js';
import { ItemRecordFlags, type ItemIdentity } from '../Stats/ItemRecord.js';
import { ColorTable } from './ColorTable.js';
import { GemTable } from './GemTable.js';
import { ItemTable } from './ItemTable.js';
import { ItemTypeTree } from './ItemTypeTree.js';
import { MagicAffixTable } from './MagicAffixTable.js';
import type { D2DataFiles } from './TxtDataProviders.js';

/**
 * The inventory palette shift for an item — what tints a ring's sprite blue, or a set item's
 * green.
 *
 * Ported from d2bsng's `ItemColor` (UnitJson.cpp), which models the game's ITEMS_GetColor.
 * PROVENANCE: unlike the rest of this project, the ORDER of the arms below has not been traced in
 * the 1.14d disassembly here — it is inherited. What IS verified against the shipped tables is
 * every lookup it performs.
 *
 * The reason this needs colors.txt at all is that we embed the `.txt`: the compiled tables a live
 * consumer reads already hold the resolved row index, ours hold the 4-char code.
 */
export class ItemInventoryColor {
  /**
   * itemtypes.txt `gem`, the game's hardcoded ITEMTYPE_GEM. gem0..gem4 chain to it via equiv;
   * runes and jewels live under `sock` instead, so this excludes them even though they share
   * gems.txt — where a rune carries a real transform of 18.
   *
   * Looked up BY CODE rather than as the literal 20 d2bsng uses. The literal is in fact correct
   * here — itemtypes.bin row 20 is `gem `, and the `Expansion` divider sits at raw row 59, AFTER
   * it, so the splice does not shift it. The code lookup is kept because it stays correct if a
   * row is ever inserted above 20, which a literal would not.
   */
  private static readonly GemTypeCode = 'gem';

  private readonly items: ItemTable;
  private readonly types: ItemTypeTree;
  private readonly colors: ColorTable;
  private readonly affixes: MagicAffixTable;
  private readonly gemTable: GemTable;
  private readonly uniqueItems: TxtFile | null;
  private readonly setItems: TxtFile | null;
  private readonly gems: TxtFile | null;

  constructor(data: D2DataFiles, items: ItemTable, types: ItemTypeTree) {
    this.items = items;
    this.types = types;
    this.colors = new ColorTable(data.colors);
    this.affixes = new MagicAffixTable(data);
    this.gemTable = new GemTable(data.gems, items);
    this.uniqueItems = data.uniqueItems;
    this.setItems = data.setItems;
    this.gems = data.gems;
  }

  /**
   * The base item's palette-transform GROUP (items.txt InvTrans). This is not a colour: it says
   * which transform table the shift indexes, and a zero here is what stops most items being
   * tinted at all. Kept separate because the consumer gates on it.
   */
  invTrans(classId: number): number {
    return this.items.getInt(classId, 'InvTrans');
  }

  /**
   * The palette-shift index, or `ColorTable.None` (-1) for no shift. `firstSocket` is the item in
   * socket 0, which is the only one the gem tint looks at — a rune in socket 0 and a gem in
   * socket 1 gets no tint.
   */
  resolve(item: ItemIdentity, firstSocket: ItemIdentity | null = null): number {
    // Set and unique return DIRECTLY — the game does not fall through to the affix path for
    // these. dwFileIndex is not carried by the client until identified, and the game returns no
    // shift then, so match that rather than reading row -1.
    if (item.quality === ItemQualityNo.Set || item.quality === ItemQualityNo.Unique) {
      if ((item.flags & ItemRecordFlags.Identified) === 0) {
        return ColorTable.None;
      }

      const table = item.quality === ItemQualityNo.Unique ? this.uniqueItems : this.setItems;

      return item.fileIndex >= 0
        ? ColorTable.clamp(this.codeColumn(table, item.fileIndex, 'invtransform'))
        : ColorTable.None;
    }

    if (item.quality === ItemQualityNo.Magic || item.quality === ItemQualityNo.Rare) {
      // If no affix carries a colour, fall through to the automagic arm below — the game does
      // the same (its case 4/6 jumps to LABEL_39).
      const affix = this.matchAffixColor(item);
      if (affix >= 0) {
        return affix;
      }
    } else if (firstSocket !== null && this.isGem(firstSocket)) {
      // Tint by ONLY the first socketed item, and only when it is a gem. This arm returns
      // whatever it finds, including nothing — it does not fall through.
      //
      // NOT items.txt `gemoffset`: that column is a LINKER field, populated with the gems row
      // only in the compiled table. In the .txt it is blank, which would read as row 0 and tint
      // every gem like a Chipped Amethyst. GemTable rebuilds the mapping the way
      // TXT_AllocTxt_gems writes it (0x637279).
      const gemRow = this.gemTable.rowForFillerClassId(firstSocket.classId);
      return ColorTable.clamp(
        gemRow >= 0 && this.gems !== null
          ? this.gems.getInt(gemRow, 'transform', ColorTable.None)
          : ColorTable.None,
      );
    }

    // The automagic arm (the game's LABEL_39): reached by a magic/rare item whose affixes carry
    // no colour, and by a normal item with no gem in socket 0. wAutoAffix is 0 on almost
    // everything, so this is nearly always no shift.
    return this.affixColor(item.autoAffix);
  }

  /** Suffixes first, then prefixes, taking the first that carries a real colour. */
  private matchAffixColor(item: ItemIdentity): number {
    for (const suffix of item.magicSuffix) {
      const color = this.affixColor(suffix);
      if (color >= 0) {
        return color;
      }
    }

    for (const prefix of item.magicPrefix) {
      const color = this.affixColor(prefix);
      if (color >= 0) {
        return color;
      }
    }

    return ColorTable.None;
  }

  /**
   * One affix's transformcolor. The id indexes the CONCATENATED
   * [magicsuffix][magicprefix][automagic] array 1-based, which MagicAffixTable already models, so
   * id 1 is the first SUFFIX row rather than a prefix.
   */
  private affixColor(affixId: number): number {
    if (affixId <= 0) {
      return ColorTable.None;
    }

    const at = this.affixes.tryResolve(affixId);
    return at === null
      ? ColorTable.None
      : ColorTable.clamp(this.codeColumn(at.table, at.row, 'transformcolor'));
  }

  /**
   * A column holding a colors.txt CODE rather than an index — which is every one of them except
   * gems.txt `transform`, because we read the .txt and not the compiled table.
   */
  private codeColumn(table: TxtFile | null, row: number, column: string): number {
    return table === null || row < 0 || row >= table.rowCount
      ? ColorTable.None
      : this.colors.rowForCode(table.getString(row, column));
  }

  /**
   * items.txt `type` / `type2` are itemtypes CODES in the .txt and row indices only in the
   * compiled table, so they go through ItemTypeTree.row rather than being read as ints.
   */
  private isGem(filler: ItemIdentity): boolean {
    return this.types.isOfType(
      this.types.row(this.items.primaryTypeCode(filler.classId)),
      this.types.row(this.items.secondaryTypeCode(filler.classId)),
      this.types.row(ItemInventoryColor.GemTypeCode),
    );
  }
}
