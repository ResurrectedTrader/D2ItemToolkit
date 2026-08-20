import type { TxtFile } from '../Data/TxtFile.js';
import { ItemRecordFlags, type ItemIdentity } from '../Stats/ItemRecord.js';
import { ItemQualityNo } from '../Tooltip/ItemNameBuilder.js';
import { ItemTable } from './ItemTable.js';
import { ItemTypeTree } from './ItemTypeTree.js';
import type { D2DataFiles } from './TxtDataProviders.js';

/**
 * The inventory sprite name — what a renderer fetches as `<image>.dc6`.
 *
 * Ported from d2bsng's `ResolveImageCode` (UnitJson.cpp), itself a port of kolbot's
 * `Item.getItemCode`, mirroring the game's GFXUTIL_SetItemGfxFile. PROVENANCE: as with
 * ItemInventoryColor, the shape of this has not been traced in the 1.14d disassembly here — it is
 * inherited. Every table lookup it makes IS verified against the shipped data, including which
 * fallbacks are reachable.
 *
 * The raw item code is wrong for most items: exceptional and elite tiers share the base tier's
 * art, set and unique items get their own, and the four types with a random inventory graphic need
 * the rolled variant appended.
 */
export class ItemInventoryGraphics {
  private readonly items: ItemTable;
  private readonly types: ItemTypeTree;
  private readonly itemTypes: TxtFile | null;
  private readonly uniqueItems: TxtFile | null;
  private readonly setItems: TxtFile | null;

  constructor(data: D2DataFiles, items: ItemTable, types: ItemTypeTree) {
    this.items = items;
    this.types = types;
    this.itemTypes = data.itemTypes;
    this.uniqueItems = data.uniqueItems;
    this.setItems = data.setItems;
  }

  resolve(item: ItemIdentity): string {
    // The table's own code, from classId. Not a deviation from the reference: the C++ reads
    // szCode off the same items row rather than off the captured document, so the two agree by
    // construction. It also means the optional `code` field in a record cannot disagree with the
    // sprite.
    const code = this.items.code(item.classId).trim();

    const special = this.setOrUniqueGraphic(item);
    if (special !== null && special.length !== 0) {
      // Returns EARLY — before the space strip and before the variant suffix. A set or unique
      // graphic is a complete sprite name, not a code to be decorated.
      return special;
    }

    // A self-named graphic (`invfile` === "inv" + code) means the item has its own art, so the
    // code stands — Tiara/Diadem, Khalim's Flail/Will. Otherwise `invfile` points at a shared
    // graphic and the normal-tier code is the one that names it: that is how `xap` (exceptional
    // Cap, invfile `invcap`) collapses to `cap`.
    const invFile = this.items.getString(item.classId, 'invfile').trim();

    let image = invFile === 'inv' + code ? code : this.items.getString(item.classId, 'normcode');

    if (image.length === 0) {
      // misc.txt carries no `normcode` column at all, so every miscellaneous item lands here
      // unless it took the self-named branch above.
      image = code;
    }

    image = image.replace(/ /g, '');

    // Rings, amulets, jewels and charms carry several random inventory graphics; the rolled one is
    // a 1-based suffix, so a ring is rin1..rin5.
    return this.varInvGfx(item.classId) > 0 ? image + String(item.gfxIndex + 1) : image;
  }

  /**
   * The per-item graphic for an identified set or unique, falling back to the base item's
   * `setinvfile` / `uniqueinvfile`. Unidentified items keep the plain sprite, because the client
   * does not carry dwFileIndex until then.
   *
   * Both halves of the set path matter differently: SetItems.invfile is populated on ZERO shipped
   * rows, so a set item always reaches the `setinvfile` fallback. UniqueItems.invfile has 140, so
   * the unique path normally does NOT — `uniqueinvfile` is what gives the Amulet of the Viper its
   * `invvip`, the one misc row that has it.
   */
  private setOrUniqueGraphic(item: ItemIdentity): string | null {
    if ((item.flags & ItemRecordFlags.Identified) === 0) {
      return null;
    }

    if (item.quality !== ItemQualityNo.Set && item.quality !== ItemQualityNo.Unique) {
      return null;
    }

    const unique = item.quality === ItemQualityNo.Unique;
    const table = unique ? this.uniqueItems : this.setItems;

    const image = item.fileIndex >= 0 ? cell(table, item.fileIndex, 'invfile') : null;

    return image !== null && image.length !== 0
      ? image
      : this.items.getString(item.classId, unique ? 'uniqueinvfile' : 'setinvfile');
  }

  /**
   * itemtypes.txt VarInvGfx for the item's PRIMARY type. Resolved by code rather than as a row
   * number: ItemTypes.txt carries an `Expansion` row that STRUCT_CreateBinFieldExcelAndFillData
   * splices out, so a literal index is only valid post-splice.
   */
  private varInvGfx(classId: number): number {
    const row = this.types.row(this.items.primaryTypeCode(classId));

    return this.itemTypes === null || row < 0 || row >= this.itemTypes.rowCount
      ? 0
      : this.itemTypes.getInt(row, 'VarInvGfx', 0);
  }
}

function cell(table: TxtFile | null, row: number, column: string): string | null {
  return table === null || row < 0 || row >= table.rowCount ? null : table.getString(row, column);
}
