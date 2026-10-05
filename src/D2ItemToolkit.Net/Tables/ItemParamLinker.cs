using System;
using System.Collections.Generic;

namespace D2ItemToolkit
{
    /// <summary>
    /// How a property PARAM cell — uniqueitems `par`, runes `T1Param`, sets/setitems, propertygroups
    /// `parmin`/`parmax` — becomes the number the property functions see.
    ///
    /// D2R, DATATBLS_ItemParamLinker 0x140214e40: a cell starting with `-` or a digit is atoi'd
    /// (0x140214efb); anything else is a NAME tried against three linkers in order — skills `skill`
    /// (tables +0x11C8, 0x1401fd5c5), montype `type` (+0x1400, 0x140270a65), states `state`
    /// (+0x2A8, 0x1402020a9) — and a miss on all three is 0 (0x140214f0d). FOG_GetRowFromTxt
    /// 0x1407622c0 folds ASCII to lower case before hashing, so the match ignores case.
    ///
    /// 1.14d keeps its old reading — a whole-cell integer, else 0 — because its linker is UNTRACED:
    /// there is no 1.14d database, and 192 shipped 1.14d cells name a skill (4 only ignoring case)
    /// that this therefore leaves at 0.
    /// </summary>
    internal sealed class ItemParamLinker
    {
        private readonly bool _resurrected;
        private readonly Dictionary<string, int>[] _names;

        public ItemParamLinker(D2DataFiles data)
        {
            _resurrected = data.IsResurrected;
            _names = _resurrected
                ? new[]
                {
                    Keys(data.SkillRows, "skill"),
                    Keys(data.MonTypeRows, "type"),
                    Keys(data.States, "state"),
                }
                : new Dictionary<string, int>[0];
        }

        public int Resolve(string cell)
        {
            if (string.IsNullOrEmpty(cell))
            {
                return 0;
            }

            if (!_resurrected)
            {
                int value;
                return int.TryParse(cell.Trim(), out value) ? value : 0;
            }

            if (cell[0] == '-' || (cell[0] >= '0' && cell[0] <= '9'))
            {
                return Atoi(cell);
            }

            foreach (Dictionary<string, int> names in _names)
            {
                int row;
                if (names.TryGetValue(AsciiLower(cell), out row))
                {
                    return row;
                }
            }

            return 0;
        }

        private static int Atoi(string cell)
        {
            bool negative = cell[0] == '-';
            int value = 0;
            for (int i = negative ? 1 : 0; i < cell.Length && cell[i] >= '0' && cell[i] <= '9'; ++i)
            {
                value = unchecked((value * 10) + (cell[i] - '0'));
            }

            return negative ? unchecked(-value) : value;
        }

        // byte_141578F50: ASCII A-Z to a-z, every other byte unchanged.
        private static string AsciiLower(string text)
        {
            var chars = text.ToCharArray();
            for (int i = 0; i < chars.Length; ++i)
            {
                if (chars[i] >= 'A' && chars[i] <= 'Z')
                {
                    chars[i] = (char)(chars[i] + 32);
                }
            }

            return new string(chars);
        }

        // First occurrence wins, as a linker keeps the row a key was first registered at.
        private static Dictionary<string, int> Keys(TxtFile table, string column)
        {
            var keys = new Dictionary<string, int>(StringComparer.Ordinal);
            if (table == null || !table.HasColumn(column))
            {
                return keys;
            }

            for (int row = 0; row < table.RowCount; ++row)
            {
                string key = AsciiLower(table.GetString(row, column));
                if (key.Length != 0 && !keys.ContainsKey(key))
                {
                    keys.Add(key, row);
                }
            }

            return keys;
        }
    }
}
