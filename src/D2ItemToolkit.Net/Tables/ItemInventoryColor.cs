using System;

namespace D2ItemToolkit
{
    /// <summary>
    /// The inventory palette shift for an item — what tints a ring's sprite blue, or a set item's
    /// green.
    ///
    /// Ported from d2bsng's `ItemColor` (UnitJson.cpp), which models the game's ITEMS_GetColor.
    /// PROVENANCE: unlike the rest of this project, the ORDER of the arms below has not been
    /// traced in the 1.14d disassembly here — it is inherited. What IS verified against the
    /// shipped tables is every lookup it performs: the colours.txt indices, that each column
    /// really holds a code rather than a number, and the reachability counts in the tests.
    ///
    /// The reason this needs colours.txt at all is that we embed the `.txt`: the compiled tables
    /// a live consumer reads already hold the resolved row index, ours hold the 4-char code.
    /// </summary>
    internal sealed class ItemInventoryColor
    {
        /// <summary>
        /// itemtypes.txt `gem`, the game's hardcoded ITEMTYPE_GEM. gem0..gem4 chain to it via
        /// equiv; runes and jewels live under `sock` instead, so this excludes them even though
        /// they share gems.txt — where a rune carries a real transform of 18.
        ///
        /// Looked up BY CODE rather than as the literal 20 d2bsng uses. The literal is in fact
        /// correct here — itemtypes.bin row 20 is `gem `, and the `Expansion` divider sits at raw
        /// row 59, AFTER it, so the splice does not shift it. The code lookup is kept because it
        /// stays correct if a row is ever inserted above 20, which a literal would not.
        /// </summary>
        private const string GemTypeCode = "gem";

        private readonly ItemTable _items;
        private readonly ItemTypeTree _types;
        private readonly ColorTable _colors;
        private readonly MagicAffixTable _affixes;
        private readonly GemTable _gemTable;
        private readonly TxtFile _uniqueItems;
        private readonly TxtFile _setItems;
        private readonly TxtFile _gems;

        public ItemInventoryColor(D2DataFiles data, ItemTable items, ItemTypeTree types)
        {
            if (data == null) throw new ArgumentNullException("data");
            if (items == null) throw new ArgumentNullException("items");
            if (types == null) throw new ArgumentNullException("types");

            _items = items;
            _types = types;
            _colors = new ColorTable(data.Colors);
            _affixes = new MagicAffixTable(data);
            _gemTable = new GemTable(data.Gems, items);
            _uniqueItems = data.UniqueItems;
            _setItems = data.SetItems;
            _gems = data.Gems;
        }

        /// <summary>
        /// The base item's palette-transform GROUP (items.txt InvTrans). This is not a colour: it
        /// says which transform table the shift indexes, and a zero here is what stops most items
        /// being tinted at all. Kept separate because the consumer gates on it.
        /// </summary>
        public int InvTrans(int classId)
        {
            return _items.GetInt(classId, "InvTrans");
        }

        /// <summary>
        /// The palette-shift index, or <see cref="ColorTable.None"/> (-1) for no shift.
        /// <paramref name="firstSocket"/> is the item in socket 0, which is the only one the gem
        /// tint looks at — a rune in socket 0 and a gem in socket 1 gets no tint.
        /// </summary>
        public int Resolve(ItemIdentity item, ItemIdentity firstSocket = null)
        {
            if (item == null) throw new ArgumentNullException("item");

            // Set and unique return DIRECTLY — the game does not fall through to the affix path
            // for these. dwFileIndex is not carried by the client until identified, and the game
            // returns no shift then, so match that rather than reading row -1.
            if (item.Quality == ItemQualityNo.Set || item.Quality == ItemQualityNo.Unique)
            {
                if (!item.Has(ItemRecordFlags.Identified))
                {
                    return ColorTable.None;
                }

                TxtFile table = item.Quality == ItemQualityNo.Unique ? _uniqueItems : _setItems;

                return item.FileIndex >= 0
                    ? ColorTable.Clamp(CodeColumn(table, item.FileIndex, "invtransform"))
                    : ColorTable.None;
            }

            if (item.Quality == ItemQualityNo.Magic || item.Quality == ItemQualityNo.Rare)
            {
                // If no affix carries a colour, fall through to the automagic arm below — the
                // game does the same (its case 4/6 jumps to LABEL_39).
                int affix = MatchAffixColor(item);
                if (affix >= 0)
                {
                    return affix;
                }
            }
            else if (firstSocket != null && IsGem(firstSocket))
            {
                // Tint by ONLY the first socketed item, and only when it is a gem. This arm
                // returns whatever it finds, including nothing — it does not fall through.
                // NOT items.txt `gemoffset`: that column is a LINKER field, populated with the
                // gems row only in the compiled table. In the .txt it is blank, which would read
                // as row 0 and tint every gem like a Chipped Amethyst. GemTable rebuilds the
                // mapping the way TXT_AllocTxt_gems writes it (0x637279).
                int gemRow = _gemTable.RowForFillerClassId(firstSocket.ClassId);
                return ColorTable.Clamp(
                    gemRow >= 0 && _gems != null
                        ? _gems.GetInt(gemRow, "transform", ColorTable.None)
                        : ColorTable.None);
            }

            // The automagic arm (the game's LABEL_39): reached by a magic/rare item whose affixes
            // carry no colour, and by a normal item with no gem in socket 0. wAutoAffix is 0 on
            // almost everything, so this is nearly always no shift.
            return AffixColor(item.AutoAffix);
        }

        /// <summary>Suffixes first, then prefixes, taking the first that carries a real colour.</summary>
        private int MatchAffixColor(ItemIdentity item)
        {
            foreach (int suffix in item.MagicSuffix)
            {
                int color = AffixColor(suffix);
                if (color >= 0)
                {
                    return color;
                }
            }

            foreach (int prefix in item.MagicPrefix)
            {
                int color = AffixColor(prefix);
                if (color >= 0)
                {
                    return color;
                }
            }

            return ColorTable.None;
        }

        /// <summary>
        /// One affix's transformcolor. The id indexes the CONCATENATED
        /// [magicsuffix][magicprefix][automagic] array 1-based, which MagicAffixTable already
        /// models, so id 1 is the first SUFFIX row rather than a prefix.
        /// </summary>
        private int AffixColor(int affixId)
        {
            if (affixId <= 0)
            {
                return ColorTable.None;
            }

            TxtFile table;
            int row;
            return _affixes.TryResolve(affixId, out table, out row)
                ? ColorTable.Clamp(CodeColumn(table, row, "transformcolor"))
                : ColorTable.None;
        }

        /// <summary>
        /// A column holding a colours.txt CODE rather than an index — which is every one of them
        /// except gems.txt `transform`, because we read the .txt and not the compiled table.
        /// </summary>
        private int CodeColumn(TxtFile table, int row, string column)
        {
            return table == null || row < 0 || row >= table.RowCount
                ? ColorTable.None
                : _colors.RowForCode(table.GetString(row, column));
        }

        /// <summary>
        /// items.txt `type` / `type2` are itemtypes CODES in the .txt and row indices only in the
        /// compiled table, so they go through ItemTypeTree.Row rather than being read as ints.
        /// </summary>
        private bool IsGem(ItemIdentity filler)
        {
            return _types.IsOfType(
                _types.Row(_items.PrimaryTypeCode(filler.ClassId)),
                _types.Row(_items.SecondaryTypeCode(filler.ClassId)),
                _types.Row(GemTypeCode));
        }
    }
}
