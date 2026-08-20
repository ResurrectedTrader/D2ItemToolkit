using System;
using System.Collections.Generic;

namespace D2ItemToolkit
{
    /// <summary>
    /// gems.txt plus the dwGemOffset back-reference the loader writes into items.txt at 0x637245.
    /// Nothing resolves a gem by item code at runtime: callers read ItemsTxt.dwGemOffset (+0xF0) and
    /// index gems.txt with it directly (TXT_Gems_GetLine, 0x6372c0, stride 192).
    /// </summary>
    public sealed class GemTable
    {
        private readonly TxtFile _gems;
        private readonly Dictionary<int, int> _offsetByClassId;

        public GemTable(TxtFile gems, ItemTable items)
        {
            _gems = gems;
            _offsetByClassId = new Dictionary<int, int>();

            if (gems == null || items == null)
            {
                return;
            }

            // The loop bound is the GEMS row count, not the items count (0x637243), so it clears
            // dwGemOffset only for the first N item rows. Everything else keeps the zero left by the
            // calloc, which is why readers test `> 0` rather than `>= 0` — item row 0 and a gem that
            // lands on gems row 0 are indistinguishable from "not a gem".
            for (int i = 0; i < gems.RowCount; ++i)
            {
                _offsetByClassId[i] = -1;

                int classId = items.ClassIdForCode(Code(i));
                if (classId >= 0)
                {
                    _offsetByClassId[classId] = i;
                }
            }
        }

        public int RowCount { get { return _gems == null ? 0 : _gems.RowCount; } }

        /// <summary>
        /// The gems row for a socket filler, or -1 when the item is not one. `TXT_Gems_GetLine`
        /// 0x6372c0 rejects only `row >= recordCount` (0x6372cc) and exactly -1 (0x6372d1), so
        /// **row 0 is valid** — it is `gcv`, the Chipped Amethyst. `TXT_AllocTxt_gems` writes the
        /// index into items row +0xF0 at 0x637279 and writes a literal 0 on its first iteration.
        /// </summary>
        public int RowForFillerClassId(int classId)
        {
            int row;
            if (!_offsetByClassId.TryGetValue(classId, out row) || row < 0)
            {
                return -1;
            }

            return row < RowCount ? row : -1;
        }

        /// <summary>
        /// The same lookup for the rune-letter writer, which additionally drops row 0. That is the
        /// `jle` at 0x4866e9 — it belongs to INV_FormatRunewordName 0x486670, NOT to the
        /// socket-filler path, and it sits behind an IsOfType(rune) test at 0x4866d6, so no rune
        /// ever occupies row 0 and the difference is unobservable.
        /// </summary>
        public int RowForRuneClassId(int classId)
        {
            int row = RowForFillerClassId(classId);
            return row > 0 ? row : -1;
        }

        /// <summary>
        /// The letter shown for a socketed rune. Read straight off the record as raw characters
        /// (UTF8_ConvertToWideChar over 6 bytes at gems row +0x20), never through the string table.
        /// </summary>
        public string Letter(int row)
        {
            if (_gems == null || row < 0 || row >= _gems.RowCount || !_gems.HasColumn("letter"))
            {
                return null;
            }

            string letter = _gems.GetString(row, "letter");
            return string.IsNullOrEmpty(letter) ? null : letter;
        }

        /// <summary>
        /// The three property quadruples for one destination slot. The runtime layout is
        /// pProperties[3][3] at gems row +0x30, so slot 0 is the weapon mods, 1 the helm mods and 2
        /// the shield mods (ITEMMOD_GetMaxLevelAtIndex 0x65c6d0).
        /// </summary>
        public IEnumerable<ItemProperty> Properties(int row, int slot)
        {
            if (_gems == null || row < 0 || row >= _gems.RowCount || slot < 0 || slot > 2)
            {
                yield break;
            }

            string prefix = SlotPrefixes[slot];

            for (int mod = 1; mod <= 3; ++mod)
            {
                string stem = prefix + "Mod" + mod;

                var property = new ItemProperty();
                property.PropertyId = _propertyIds == null
                    ? -1
                    : _propertyIds(Cell(row, stem + "Code"));
                property.Param = IntCell(row, stem + "Param");
                property.Min = IntCell(row, stem + "Min");
                property.Max = IntCell(row, stem + "Max");

                yield return property;
            }
        }

        private static readonly string[] SlotPrefixes = { "weapon", "helm", "shield" };

        private Func<string, int> _propertyIds;

        /// <summary>
        /// The mod code columns hold property NAMES; the loader resolves them to Properties.txt rows
        /// at compile time (TXTFIELD_NAMETODWORD via pPropertiesLinker), so the resolver is injected.
        /// </summary>
        internal void ResolvePropertyCodesWith(Func<string, int> resolver)
        {
            _propertyIds = resolver;
        }

        private string Cell(int row, string column)
        {
            return _gems.HasColumn(column) ? _gems.GetString(row, column) : string.Empty;
        }

        private int IntCell(int row, string column)
        {
            return _gems.HasColumn(column) ? _gems.GetInt(row, column) : 0;
        }

        /// <summary>The whole row, or null when the index is out of range.</summary>
        public GemRow RowAt(int row)
        {
            return row < 0 || row >= RowCount ? null : new GemRow(row, Code(row), Letter(row));
        }

        public string Code(int row)
        {
            if (_gems == null || row < 0 || row >= _gems.RowCount || !_gems.HasColumn("code"))
            {
                return null;
            }

            return _gems.GetString(row, "code");
        }

    }
}
