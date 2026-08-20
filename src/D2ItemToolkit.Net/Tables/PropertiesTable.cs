using System;
using System.Collections.Generic;

namespace D2ItemToolkit
{
    /// <summary>
    /// Properties.txt, compiled the way DATATBLS does it: a 46-byte record per row with seven
    /// parallel sets. `stat&lt;n&gt;` is a NAME in the .txt and a resolved ItemStatCost row at runtime
    /// (TXTFIELD_NAMETOWORD via pItemStatCostLinker), so it is resolved here too.
    /// </summary>
    public sealed class PropertiesTable
    {
        public const int SetsPerProperty = 7;

        public sealed class Row
        {
            public string Code;
            public readonly int[] Set = new int[SetsPerProperty];
            public readonly int[] Func = new int[SetsPerProperty];
            public readonly int[] Stat = new int[SetsPerProperty];

            /// <summary>
            /// `val&lt;n&gt;`, the record's +10+2n word. Only func 21 reads it (0x65fb7e passes it
            /// straight through as the stat LAYER), and for the seven class-skill codes the cell is
            /// the class number — `ama` 0 through `ass` 6.
            /// </summary>
            public readonly int[] Val = new int[SetsPerProperty];
        }

        private readonly Row[] _rows;
        private readonly Dictionary<string, int> _byCode =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public PropertiesTable(TxtFile properties, TxtItemStatCostTable statCost)
        {
            if (properties == null)
            {
                _rows = new Row[0];
                return;
            }

            _rows = new Row[properties.RowCount];

            for (int i = 0; i < properties.RowCount; ++i)
            {
                var row = new Row();
                row.Code = properties.GetString(i, "code");

                for (int set = 0; set < SetsPerProperty; ++set)
                {
                    string suffix = (set + 1).ToString();
                    row.Set[set] = Int(properties, i, "set" + suffix);
                    row.Val[set] = Int(properties, i, "val" + suffix);
                    row.Func[set] = Int(properties, i, "func" + suffix);
                    row.Stat[set] = ResolveStat(properties, i, "stat" + suffix, statCost);
                }

                _rows[i] = row;

                if (!string.IsNullOrEmpty(row.Code) && !_byCode.ContainsKey(row.Code))
                {
                    _byCode.Add(row.Code, i);
                }
            }
        }

        public int RowCount { get { return _rows.Length; } }

        public Row this[int index]
        {
            get { return index >= 0 && index < _rows.Length ? _rows[index] : null; }
        }

        /// <summary>
        /// The compiled property id for a code, or -1. A -1 is what the loader writes for an
        /// unresolved cell, and the appliers treat a negative id as "stop".
        /// </summary>
        /// <summary>The whole row, or null when the index is out of range. Same as the indexer.</summary>
        public Row RowAt(int row)
        {
            return row < 0 || row >= RowCount ? null : this[row];
        }

        public int RowForCode(string code)
        {
            int row;
            return !string.IsNullOrEmpty(code) && _byCode.TryGetValue(code, out row) ? row : -1;
        }

        private static int Int(TxtFile file, int row, string column)
        {
            return file.HasColumn(column) ? file.GetInt(row, column) : 0;
        }

        // An unresolvable or blank stat name compiles to -1, which ITEMMODS_AddPropertyToItemStatList
        // rejects when it fails to find an ItemStatCost record.
        private static int ResolveStat(
            TxtFile file, int row, string column, TxtItemStatCostTable statCost)
        {
            if (statCost == null || !file.HasColumn(column))
            {
                return -1;
            }

            string name = file.GetString(row, column);
            return string.IsNullOrEmpty(name.Trim()) ? -1 : statCost.StatIdForName(name.Trim());
        }
    }
}
