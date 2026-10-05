using System;
using System.Collections.Generic;

namespace D2ItemToolkit
{
    /// <summary>
    /// What a property cell links to, as sub_140214F40 writes it: a Properties.txt row (kind 0) or
    /// a PropertyGroups.txt row (kind 1). An unresolved cell is stored as 0xFFFF:FFFF (0x140214f75),
    /// so <see cref="Row"/> is -1 and <see cref="Kind"/> 0xFFFF.
    /// </summary>
    internal struct PropertyRef
    {
        public const int KindProperty = 0;
        public const int KindGroup = 1;
        public const int KindUnresolved = 0xFFFF;

        public PropertyRef(int row, int kind)
        {
            Row = row;
            Kind = kind;
        }

        public int Row;
        public int Kind;

        public static PropertyRef Unresolved
        {
            get { return new PropertyRef(-1, KindUnresolved); }
        }
    }

    /// <summary>
    /// PropertyGroups.txt, compiled as DATATBLS_LoadPropertiesTxt does it (record 0xC8, field table
    /// 0x14021c53d..0x14021ce3c): a key, a pick mode, and eight {prop, parMin, parMax, modMin,
    /// modMax, chance} entries. D2R only; 1.14d has no such table and gets an empty one.
    /// </summary>
    internal sealed class PropertyGroupsTable
    {
        public const int EntriesPerGroup = 8;

        public sealed class Entry
        {
            public PropertyRef Prop;
            public int ParMin;
            public int ParMax;
            public int ModMin;
            public int ModMax;
            public int Chance;

            /// <summary>The draw weight: a blank or non-positive chance counts as 1 (0x14028a050).</summary>
            public int Weight
            {
                get { return Chance < 1 ? 1 : Chance; }
            }
        }

        public sealed class Row
        {
            public string Code;
            public int PickMode;
            public readonly Entry[] Entries = new Entry[EntriesPerGroup];
        }

        private readonly Row[] _rows;
        private readonly Dictionary<string, int> _byCode =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public PropertyGroupsTable(
            TxtFile groups, PropertiesTable properties, ItemParamLinker paramLinker = null)
        {
            if (groups == null)
            {
                _rows = new Row[0];
                return;
            }

            _rows = new Row[groups.RowCount];

            for (int i = 0; i < groups.RowCount; ++i)
            {
                string code = groups.GetString(i, "code");
                if (code.Length != 0 && !_byCode.ContainsKey(code))
                {
                    _byCode.Add(code, i);
                }
            }

            for (int i = 0; i < groups.RowCount; ++i)
            {
                var row = new Row();
                row.Code = groups.GetString(i, "code");
                row.PickMode = groups.GetInt(i, "pickmode") & 0xFF;

                for (int k = 0; k < EntriesPerGroup; ++k)
                {
                    string n = (k + 1).ToString();
                    var entry = new Entry();
                    entry.Prop = Link(groups.GetString(i, "prop" + n), properties, this);

                    // 0x14021cf7b..0x14021cfd4: a group entry may name only an EARLIER group.
                    if (entry.Prop.Kind == PropertyRef.KindGroup && entry.Prop.Row >= i)
                    {
                        entry.Prop.Row = -1;
                    }

                    entry.ParMin = ParseParam(groups.GetString(i, "parmin" + n), paramLinker);
                    entry.ParMax = ParseParam(groups.GetString(i, "parmax" + n), paramLinker);
                    entry.ModMin = groups.GetInt(i, "modmin" + n);
                    entry.ModMax = groups.GetInt(i, "modmax" + n);
                    entry.Chance = groups.GetInt(i, "chance" + n);
                    row.Entries[k] = entry;
                }

                _rows[i] = row;
            }
        }

        public int RowCount
        {
            get { return _rows.Length; }
        }

        public Row this[int row]
        {
            get { return row >= 0 && row < _rows.Length ? _rows[row] : null; }
        }

        public int RowForCode(string code)
        {
            int row;
            return !string.IsNullOrEmpty(code) && _byCode.TryGetValue(code, out row) ? row : -1;
        }

        /// <summary>
        /// sub_140214F40: Properties.txt first (0x140214f8c), PropertyGroups.txt only on a miss
        /// (0x140214fb9).
        /// </summary>
        public static PropertyRef Link(
            string code, PropertiesTable properties, PropertyGroupsTable groups)
        {
            if (string.IsNullOrEmpty(code))
            {
                return PropertyRef.Unresolved;
            }

            int row = properties == null ? -1 : properties.RowForCode(code);
            if (row >= 0)
            {
                return new PropertyRef(row, PropertyRef.KindProperty);
            }

            row = groups == null ? -1 : groups.RowForCode(code);
            return row >= 0 ? new PropertyRef(row, PropertyRef.KindGroup) : PropertyRef.Unresolved;
        }

        /// <summary>
        /// DATATBLS_ItemParamLinker 0x140214e40, the same linker every property table's params go
        /// through. Without one, a number is atoi'd and a name is 0.
        /// </summary>
        private static int ParseParam(string cell, ItemParamLinker paramLinker)
        {
            if (paramLinker != null)
            {
                return paramLinker.Resolve(cell);
            }

            if (cell.Length == 0 || (cell[0] != '-' && (cell[0] < '0' || cell[0] > '9')))
            {
                return 0;
            }

            bool negative = cell[0] == '-';
            int value = 0;
            for (int i = negative ? 1 : 0; i < cell.Length && cell[i] >= '0' && cell[i] <= '9'; ++i)
            {
                value = unchecked((value * 10) + (cell[i] - '0'));
            }

            return negative ? unchecked(-value) : value;
        }
    }
}
