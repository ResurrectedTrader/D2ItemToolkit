using System;
using System.Collections.Generic;

namespace D2ItemToolkit
{
    // The Equiv1/Equiv2 closure from ItemTypes.txt. The game does not walk this at runtime: the
    // loader bakes it into a bit matrix (allocated 0x639368, filled by ITEMTBLS_CheckItemTypeRecursive
    // via `or [eax], ecx` at 0x6393c2) and IsOfType (0x629bb0) just probes it.
    //
    // Two parents, not one: 16 of the 103 post-splice rows set Equiv2, so this is a DAG walk.
    public sealed class ItemTypeTree
    {
        private readonly Dictionary<string, int> _rowByCode;
        private readonly bool[][] _isUnder;
        private readonly bool[] _throwable;
        private readonly string[] _classCode;

        // The itemtypes `Code` column. NOT _classCode, which is the `Class` column — the character
        // class an item type is restricted to. Two different columns, easy to confuse.
        private readonly string[] _code;

        /// <summary>
        /// The itemtypes `Class` restriction for this row, or empty when unrestricted. This is the
        /// pair `TXT_ItemTypes_CheckClass` / `TXT_ItemTypes_GetClass` reads (record +0x21), and the
        /// row is the item's PRIMARY type only (+0x11E) — there is no equivalence walk.
        /// </summary>
        public string ClassCode(int itemTypeRow)
        {
            return itemTypeRow >= 0 && itemTypeRow < _classCode.Length
                ? _classCode[itemTypeRow]
                : string.Empty;
        }

        /// <summary>
        /// ITEMS_CheckItemTypeIfThrowable reads this row's own Throwable column with no equivalence
        /// walk, so it is a flat lookup rather than a closure query.
        /// </summary>
        public bool IsThrowable(int itemTypeRow)
        {
            return itemTypeRow >= 0 && itemTypeRow < _throwable.Length && _throwable[itemTypeRow];
        }

        /// <summary>
        /// The socket cap for this type at a given item level — ITEM_GetMaxSockCount 0x62bc20 picks
        /// `MaxSock1` at level &lt;= 25, `MaxSock25` at &lt;= 40 and `MaxSock40` above (0x62bc81,
        /// 0x62bc8c). The column NAMES are the level each tier starts at, not the cap it holds.
        ///
        /// Returns -1 for an unknown level (<see cref="IUnit.ItemLevel"/> being -1) so a caller can
        /// tell "no cap known" from "a cap of zero", which is a real answer for boots and gloves.
        /// </summary>
        public int MaxSockets(int itemTypeRow, int itemLevel)
        {
            if (itemLevel < 0 || itemTypeRow < 0 || itemTypeRow >= _maxSock.Length)
            {
                return -1;
            }

            int[] tiers = _maxSock[itemTypeRow];
            return itemLevel <= 25 ? tiers[0] : (itemLevel <= 40 ? tiers[1] : tiers[2]);
        }

        private readonly int[][] _maxSock;

        public ItemTypeTree(TxtFile itemTypes)
        {
            if (itemTypes == null) throw new ArgumentNullException("itemTypes");

            int rows = itemTypes.RowCount;

            _maxSock = new int[rows][];
            for (int row = 0; row < rows; ++row)
            {
                _maxSock[row] = new[]
                {
                    itemTypes.GetInt(row, "MaxSock1"),
                    itemTypes.GetInt(row, "MaxSock25"),
                    itemTypes.GetInt(row, "MaxSock40"),
                };
            }

            _throwable = new bool[rows];
            bool hasThrowable = itemTypes.HasColumn("Throwable");
            for (int row = 0; row < rows; ++row)
            {
                _throwable[row] = hasThrowable && itemTypes.GetInt(row, "Throwable") != 0;
            }

            _classCode = new string[rows];
            bool hasClass = itemTypes.HasColumn("Class");
            for (int row = 0; row < rows; ++row)
            {
                _classCode[row] = hasClass ? itemTypes.GetString(row, "Class").Trim() : string.Empty;
            }

            _rowByCode = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            _code = new string[rows];
            for (int row = 0; row < rows; ++row)
            {
                string code = itemTypes.GetString(row, "Code");
                _code[row] = code.Trim();

                if (code.Length != 0 && !_rowByCode.ContainsKey(code))
                {
                    _rowByCode.Add(code, row);
                }
            }

            var parents = new int[rows][];
            for (int row = 0; row < rows; ++row)
            {
                parents[row] = new[]
                {
                    Row(itemTypes.GetString(row, "Equiv1")),
                    Row(itemTypes.GetString(row, "Equiv2")),
                };
            }

            _isUnder = new bool[rows][];
            for (int row = 0; row < rows; ++row)
            {
                _isUnder[row] = new bool[rows];
                MarkAncestors(row, row, parents);
            }
        }

        public int RowCount { get { return _isUnder.Length; } }

        public int Row(string code)
        {
            int row;
            return !string.IsNullOrEmpty(code) && _rowByCode.TryGetValue(code, out row) ? row : -1;
        }

        /// <summary>The itemtypes `Code` at a row, or empty when out of range. The inverse of <see cref="Row"/>.</summary>
        /// <summary>The whole row, or null when the index is out of range.</summary>
        public ItemTypeRow RowAt(int itemTypeRow)
        {
            if (itemTypeRow < 0 || itemTypeRow >= RowCount)
            {
                return null;
            }

            return new ItemTypeRow(
                itemTypeRow, CodeAt(itemTypeRow), ClassCode(itemTypeRow),
                IsThrowable(itemTypeRow));
        }

        public string CodeAt(int itemTypeRow)
        {
            return itemTypeRow >= 0 && itemTypeRow < _code.Length ? _code[itemTypeRow] : string.Empty;
        }

        /// <summary>
        /// Every type row at or below <paramref name="itemTypeRow"/> — `swor` yields itself plus
        /// every sword-ish type that chains up to it. REFLEXIVE: the row itself is always first,
        /// because <see cref="IsUnder"/> is reflexive and "all swords" has to include swords.
        ///
        /// This is the descending direction; <see cref="IsUnder"/> and <see cref="IsOfType"/>
        /// answer the ascending one. Both read the same closure, so a type is in this list exactly
        /// when IsUnder would say so — one cannot drift from the other.
        ///
        /// Equiv1/Equiv2 make this a DAG rather than a chain, so the result is a SET in row order,
        /// not a path. An unknown row yields nothing.
        /// </summary>
        public IReadOnlyList<int> Descendants(int itemTypeRow)
        {
            var rows = new List<int>();

            if (itemTypeRow < 0 || itemTypeRow >= _isUnder.Length)
            {
                return rows;
            }

            for (int row = 0; row < _isUnder.Length; ++row)
            {
                if (_isUnder[row][itemTypeRow])
                {
                    rows.Add(row);
                }
            }

            return rows;
        }

        // True when itemTypeRow is queryRow or has it as an ancestor. Row indices, not codes.
        public bool IsUnder(int itemTypeRow, int queryRow)
        {
            if (itemTypeRow < 0 || itemTypeRow >= _isUnder.Length)
            {
                return false;
            }

            return queryRow >= 0 && queryRow < _isUnder.Length && _isUnder[itemTypeRow][queryRow];
        }

        // IsOfType's two-type probe: a miss on the first type is retried against the second, which
        // must be > 0 (0x629c3b / 0x629c3e). Pass -1 for an absent second type.
        public bool IsOfType(int primaryTypeRow, int secondaryTypeRow, int queryRow)
        {
            if (IsUnder(primaryTypeRow, queryRow))
            {
                return true;
            }

            return secondaryTypeRow > 0 && IsUnder(secondaryTypeRow, queryRow);
        }

        private void MarkAncestors(int start, int at, int[][] parents)
        {
            if (at < 0 || _isUnder[start][at])
            {
                return;
            }

            _isUnder[start][at] = true;

            foreach (int parent in parents[at])
            {
                MarkAncestors(start, parent, parents);
            }
        }
    }
}
