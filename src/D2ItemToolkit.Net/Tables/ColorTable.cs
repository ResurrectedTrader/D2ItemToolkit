using System;
using System.Collections.Generic;

namespace D2ItemToolkit
{
    /// <summary>
    /// colors.txt, whose ROW INDEX is the palette-shift value everything else stores.
    ///
    /// This table exists here only because we embed the `.txt` rather than the `.bin`. The game's
    /// table compiler resolves `transformcolor` / `invtransform` from a 4-char code to this row
    /// index at load, so a consumer reading the compiled tables never needs the file at all — it
    /// reads an integer. Ours still hold `lgld` / `bwht` / `cred`, so the mapping has to happen
    /// somewhere, and it happens here.
    ///
    /// The file has no `Expansion` row, so it is NOT spliced by
    /// STRUCT_CreateBinFieldExcelAndFillData and the row index is the literal 0-based position:
    /// 21 rows, `whit` = 0 through `bwht` = 20.
    /// </summary>
    public sealed class ColorTable
    {
        /// <summary>
        /// Above this is not a real inventory colour. 20 is `bwht`, the last row, so anything
        /// larger came from a column that does not hold a colour at all.
        /// </summary>
        public const int MaxPaletteIndex = 20;

        /// <summary>No shift. Not a row — the columns use a missing/None cell to mean this.</summary>
        public const int None = -1;

        private readonly Dictionary<string, int> _rowForCode =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private readonly List<string> _codes = new List<string>();

        public ColorTable(TxtFile colors)
        {
            if (colors == null)
            {
                return;
            }

            for (int row = 0; row < colors.RowCount; ++row)
            {
                string code = (colors.GetString(row, "Code") ?? string.Empty).Trim();

                _codes.Add(code);

                // First wins: a duplicate code would otherwise silently re-point an earlier index.
                if (code.Length != 0 && !_rowForCode.ContainsKey(code))
                {
                    _rowForCode[code] = row;
                }
            }
        }

        public int RowCount
        {
            get { return _codes.Count; }
        }

        /// <summary>
        /// The palette-shift index for a 4-char code, or <see cref="None"/> when the cell is empty
        /// or names no row. An unknown code is treated as no shift rather than as row 0, which
        /// would silently paint everything white.
        /// </summary>
        public int RowForCode(string code)
        {
            if (string.IsNullOrEmpty(code))
            {
                return None;
            }

            int row;
            return _rowForCode.TryGetValue(code.Trim(), out row) ? row : None;
        }

        /// <summary>The code at a row, or empty when out of range.</summary>
        /// <summary>The whole row, or null when the index is out of range.</summary>
        public ColorRow RowAt(int row)
        {
            return row < 0 || row >= RowCount ? null : new ColorRow(row, CodeAt(row));
        }

        public string CodeAt(int row)
        {
            return row >= 0 && row < _codes.Count ? _codes[row] : string.Empty;
        }

        /// <summary>
        /// A shift that is outside the table is not a colour. Mirrors the range test d2bsng
        /// applies to every one of these lookups.
        /// </summary>
        public static int Clamp(int shift)
        {
            return shift < 0 || shift > MaxPaletteIndex ? None : shift;
        }
    }
}
