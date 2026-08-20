using System;
using System.Collections.Generic;
using System.IO;

namespace D2ItemToolkit
{
    public sealed class TxtFile
    {
        private readonly Dictionary<string, int> _columns;
        private readonly string[][] _rows;

        private TxtFile(Dictionary<string, int> columns, string[][] rows)
        {
            _columns = columns;
            _rows = rows;
        }

        public int RowCount { get { return _rows.Length; } }

        public IReadOnlyList<string> ColumnNames
        {
            get
            {
                // Sized by HEADER WIDTH, not distinct-name count: shipped headers carry duplicates
                // and blanks (armor.txt 164 fields / 162 names), and FOG_ParseBinField marks a
                // descriptor used at 0x6bd00f so only the first matching column binds.
                int width = 0;
                foreach (KeyValuePair<string, int> pair in _columns)
                {
                    if (pair.Value >= width)
                    {
                        width = pair.Value + 1;
                    }
                }

                var names = new string[width];
                foreach (KeyValuePair<string, int> pair in _columns)
                {
                    names[pair.Value] = pair.Key;
                }

                return names;
            }
        }

        private static readonly string[] RowTerminator = { "\r\n" };

        private const int MaxHeaderFields = 280;

        // The compiler tokenizes RAW BYTES (0x6bd714 `mov al,[esi]`) and never decodes anything, so
        // each byte must survive as one char. File.ReadAllText would decode UTF-8 and fold every
        // invalid byte to U+FFFD: objects.txt (two 0x85) and UniqueItems.txt (one 0x92, in
        // "Hunter's Bow") both contain bytes that are not valid UTF-8.
        public static TxtFile Load(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException("bytes");

            var chars = new char[bytes.Length];
            for (int i = 0; i < bytes.Length; ++i)
            {
                chars[i] = (char)bytes[i];
            }

            return Parse(new string(chars));
        }

        private static string[] SplitCells(string line)
        {
            if (line.IndexOf('\r') >= 0)
            {
                throw new InvalidDataException(
                    "Malformed .txt: a carriage return that is not part of a CRLF row terminator. " +
                    "The compiler halts on this at 0x6bd733.");
            }

            return line.Split('\t');
        }

        public static TxtFile Parse(string content)
        {
            if (content == null) throw new ArgumentNullException("content");

            // Rows terminate on CRLF and ONLY CRLF. The scanner tests just TAB (0x6bd718) and CR
            // (0x6bd722), and a CR must be followed by LF or it halts (0x6bd733). 0x0A matches
            // neither, so a bare LF is ordinary CELL CONTENT. Splitting on '\n' would let one stray
            // byte split a row and renumber every record id after it.
            string[] lines = content.Split(RowTerminator, StringSplitOptions.None);

            var columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            string[] header = SplitCells(lines[0]);

            // 0x6bd6f6 `cmp eax, 118h` / 0x6bd6fb `jbe`: more than 280 header fields halts the game
            // (error 0x67). The column map is a _WORD[280]. Shipped maximum is skills.txt at 256.
            if (header.Length > MaxHeaderFields)
            {
                throw new InvalidDataException(
                    "Malformed .txt: " + header.Length + " header fields exceeds the loader's " +
                    "limit of " + MaxHeaderFields + " (the game halts at 0x6bd6fd).");
            }
            for (int i = 0; i < header.Length; ++i)
            {
                string name = header[i];
                if (name.Length != 0 && !columns.ContainsKey(name))
                {
                    columns.Add(name, i);
                }
            }

            var rows = new List<string[]>(lines.Length);
            for (int i = 1; i < lines.Length; ++i)
            {
                string line = lines[i];

                // The row counter increments at exactly one site, 0x6bd737, reached only through the
                // CRLF test. An unterminated final line exits at 0x6bd728 with the counter untouched,
                // so the game DROPS it. Interior blank lines must be kept: row index is the record id.
                if (i == lines.Length - 1)
                {
                    continue;
                }

                string[] cells = SplitCells(line);

                // The compiler SKIPS the "Expansion" divider row, so it must not consume a record id
                // — keeping it shifts Druid to 6 and Assassin to 7. The compare is ordinal,
                // CASE-SENSITIVE and untrimmed (_strncmp over 10 bytes at 0x6bd742): objgroup.txt
                // spells it "EXPANSION" and objgroup.bin proves the compiler kept that one.
                if (cells.Length > 0
                    && string.Equals(cells[0], "Expansion", StringComparison.Ordinal))
                {
                    continue;
                }

                rows.Add(cells);
            }

            return new TxtFile(columns, rows.ToArray());
        }

        public int ColumnIndex(string name)
        {
            int index;
            return name != null && _columns.TryGetValue(name, out index) ? index : -1;
        }

        public bool HasColumn(string name)
        {
            return ColumnIndex(name) >= 0;
        }

        // Raw cell, NOT trimmed. The tokenizer NUL-terminates a field at the tab and nowhere else
        // (0x6bd71c), and the key converters copy it verbatim, so a padded key misses in the game
        // where a trimmed one would hit.
        public string GetString(int row, int column)
        {
            if (row < 0 || row >= _rows.Length || column < 0)
            {
                return string.Empty;
            }

            string[] cells = _rows[row];
            return column < cells.Length ? cells[column] : string.Empty;
        }

        public string GetString(int row, string column)
        {
            return GetString(row, ColumnIndex(column));
        }

        // Reproduces the games parser (0x6bde0d): one optional leading minus, then
        // n = n * 10 + (b - 48) over EVERY remaining byte with no digit test and no overflow check.
        // So "3x" is 102 and "+5" is -45. int.TryParse would reject both and substitute 0, which is
        // a different value rather than a safer one.
        public int GetInt(int row, int column, int fallback = 0)
        {
            string text = GetString(row, column);
            if (text.Length == 0)
            {
                return fallback;
            }

            bool negative = text[0] == '-';
            int value = 0;

            // Each byte is SIGN-EXTENDED before it is accumulated (0x6bde20 `movsx ecx, cl`), so a
            // byte >= 0x80 contributes a NEGATIVE amount: 0xC3 is -61, not 195.
            for (int i = negative ? 1 : 0; i < text.Length; ++i)
            {
                value = unchecked((value * 10) + (unchecked((sbyte)text[i]) - '0'));
            }

            return negative ? -value : value;
        }

        public int GetInt(int row, string column, int fallback = 0)
        {
            return GetInt(row, ColumnIndex(column), fallback);
        }

        // TXTFIELD_BIT: ANY non-zero sets the bit (0x6bde7c / 0x6bde7e), so "2" and "-1" are true.
        public bool GetBool(int row, string column)
        {
            return GetInt(row, column) != 0;
        }

        public int FindRow(string column, string value)
        {
            int index = ColumnIndex(column);
            if (index < 0 || value == null)
            {
                return -1;
            }

            for (int row = 0; row < _rows.Length; ++row)
            {
                if (string.Equals(GetString(row, index), value, StringComparison.OrdinalIgnoreCase))
                {
                    return row;
                }
            }

            return -1;
        }
    }
}
