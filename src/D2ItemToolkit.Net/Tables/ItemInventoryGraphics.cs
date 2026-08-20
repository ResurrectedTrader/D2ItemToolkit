using System;
using System.Globalization;

namespace D2ItemToolkit
{
    /// <summary>
    /// The inventory sprite name — what a renderer fetches as `&lt;image&gt;.dc6`.
    ///
    /// Ported from d2bsng's `ResolveImageCode` (UnitJson.cpp), itself a port of kolbot's
    /// `Item.getItemCode`, mirroring the game's GFXUTIL_SetItemGfxFile. PROVENANCE: as with
    /// <see cref="ItemInventoryColor"/>, the shape of this has not been traced in the 1.14d
    /// disassembly here — it is inherited. Every table lookup it makes IS verified against the
    /// shipped data, including which fallbacks are reachable.
    ///
    /// The raw item code is wrong for most items: exceptional and elite tiers share the base
    /// tier's art, set and unique items get their own, and the four types with a random inventory
    /// graphic need the rolled variant appended.
    /// </summary>
    internal sealed class ItemInventoryGraphics
    {
        private readonly ItemTable _items;
        private readonly ItemTypeTree _types;
        private readonly TxtFile _itemTypes;
        private readonly TxtFile _uniqueItems;
        private readonly TxtFile _setItems;

        public ItemInventoryGraphics(D2DataFiles data, ItemTable items, ItemTypeTree types)
        {
            if (data == null) throw new ArgumentNullException("data");
            if (items == null) throw new ArgumentNullException("items");
            if (types == null) throw new ArgumentNullException("types");

            _items = items;
            _types = types;
            _itemTypes = data.ItemTypes;
            _uniqueItems = data.UniqueItems;
            _setItems = data.SetItems;
        }

        public string Resolve(ItemIdentity item)
        {
            if (item == null) throw new ArgumentNullException("item");

            // The table's own code, from classId. Not a deviation from the reference: the C++
            // reads szCode off the same items row rather than off the captured document, so the
            // two agree by construction. It also means the optional `code` field in a record
            // cannot disagree with the sprite.
            string code = (_items.Code(item.ClassId) ?? string.Empty).Trim();

            string special = SetOrUniqueGraphic(item);
            if (!string.IsNullOrEmpty(special))
            {
                // Returns EARLY — before the space strip and before the variant suffix. A set or
                // unique graphic is a complete sprite name, not a code to be decorated.
                return special;
            }

            // A self-named graphic (`invfile` == "inv" + code) means the item has its own art, so
            // the code stands — Tiara/Diadem, Khalim's Flail/Will. Otherwise `invfile` points at a
            // shared graphic and the normal-tier code is the one that names it: that is how `xap`
            // (exceptional Cap, invfile `invcap`) collapses to `cap`.
            string invFile = (_items.GetString(item.ClassId, "invfile") ?? string.Empty).Trim();

            string image = string.Equals(invFile, "inv" + code, StringComparison.Ordinal)
                ? code
                : _items.GetString(item.ClassId, "normcode");

            if (string.IsNullOrEmpty(image))
            {
                // misc.txt carries no `normcode` column at all, so every miscellaneous item lands
                // here unless it took the self-named branch above.
                image = code;
            }

            image = image.Replace(" ", string.Empty);

            // Rings, amulets, jewels and charms carry several random inventory graphics; the
            // rolled one is a 1-based suffix, so a ring is rin1..rin5.
            //
            // Widened to long before the +1. bInvGfxIdx is a byte in the game, so int.MaxValue is
            // nonsense — but int arithmetic WRAPS it to a negative and yields `rin-2147483648`,
            // where JavaScript's doubles do not. Rubbish in, identical rubbish out.
            return VarInvGfx(item.ClassId) > 0
                ? image + ((long)item.GfxIndex + 1).ToString(CultureInfo.InvariantCulture)
                : image;
        }

        /// <summary>
        /// The per-item graphic for an identified set or unique, falling back to the base item's
        /// `setinvfile` / `uniqueinvfile`. Unidentified items keep the plain sprite, because the
        /// client does not carry dwFileIndex until then.
        ///
        /// Both halves of the set path matter differently: SetItems.invfile is populated on ZERO
        /// shipped rows, so a set item always reaches the `setinvfile` fallback. UniqueItems.invfile
        /// has 140, so the unique path normally does NOT — `uniqueinvfile` is what gives the
        /// Amulet of the Viper its `invvip`, the one misc row that has it.
        /// </summary>
        private string SetOrUniqueGraphic(ItemIdentity item)
        {
            if (!item.Has(ItemRecordFlags.Identified))
            {
                return null;
            }

            if (item.Quality != ItemQualityNo.Set && item.Quality != ItemQualityNo.Unique)
            {
                return null;
            }

            bool unique = item.Quality == ItemQualityNo.Unique;
            TxtFile table = unique ? _uniqueItems : _setItems;

            string image = item.FileIndex >= 0 ? Cell(table, item.FileIndex, "invfile") : null;

            return string.IsNullOrEmpty(image)
                ? _items.GetString(item.ClassId, unique ? "uniqueinvfile" : "setinvfile")
                : image;
        }

        /// <summary>
        /// itemtypes.txt VarInvGfx for the item's PRIMARY type. Resolved by code rather than as a
        /// row number: ItemTypes.txt carries an `Expansion` row that
        /// STRUCT_CreateBinFieldExcelAndFillData splices out, so a literal index is only valid
        /// post-splice.
        /// </summary>
        private int VarInvGfx(int classId)
        {
            int row = _types.Row(_items.PrimaryTypeCode(classId));

            return _itemTypes == null || row < 0 || row >= _itemTypes.RowCount
                ? 0
                : _itemTypes.GetInt(row, "VarInvGfx");
        }

        private static string Cell(TxtFile table, int row, string column)
        {
            return table == null || row < 0 || row >= table.RowCount
                ? null
                : table.GetString(row, column);
        }
    }
}
