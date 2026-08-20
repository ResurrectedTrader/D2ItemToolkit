using System;

namespace D2ItemToolkit
{
    /// <summary>
    /// The three item tiers. Elite is checked before Exceptional because a family whose three
    /// codes are not distinct would otherwise report the lower tier.
    /// </summary>
    public enum ItemTier
    {
        Normal,
        Exceptional,
        Elite,
    }

    // The one table dwClassId indexes. TXT_AllocTxt_items compiles weapons (0x633351), then armor
    // (0x63336d), then misc (0x63338c) and sums the three counts at 0x6333ab — so the order is
    // weapons, armor, misc, NOT armor first.
    //
    // The three files do not share a schema (166 / 164 / 168 columns), so every read is by column
    // NAME and an absent column yields the loader's default rather than a shifted value.
    public sealed class ItemTable
    {
        private readonly TxtFile[] _files;
        private readonly int[] _firstId;
        private readonly int _count;

        public ItemTable(TxtFile weapons, TxtFile armor, TxtFile misc)
        {
            _files = new[] { weapons, armor, misc };
            _firstId = new int[_files.Length];

            int next = 0;
            for (int i = 0; i < _files.Length; ++i)
            {
                _firstId[i] = next;
                next += _files[i] == null ? 0 : _files[i].RowCount;
            }

            _count = next;
        }

        public int RowCount { get { return _count; } }

        // 0x6335fc: out of range returns nothing rather than clamping.
        public bool TryResolve(int classId, out TxtFile file, out int row)
        {
            file = null;
            row = -1;

            if (classId < 0 || classId >= _count)
            {
                return false;
            }

            for (int i = _files.Length - 1; i >= 0; --i)
            {
                if (_files[i] != null && classId >= _firstId[i])
                {
                    file = _files[i];
                    row = classId - _firstId[i];
                    return true;
                }
            }

            return false;
        }

        public string GetString(int classId, string column)
        {
            TxtFile file;
            int row;
            return TryResolve(classId, out file, out row)
                ? file.GetString(row, column)
                : string.Empty;
        }

        public int GetInt(int classId, string column)
        {
            TxtFile file;
            int row;
            return TryResolve(classId, out file, out row) ? file.GetInt(row, column) : 0;
        }

        /// <summary>The whole row, or null when <paramref name="classId"/> is out of range.</summary>
        public ItemRow RowAt(int classId)
        {
            if (classId < 0 || classId >= RowCount)
            {
                return null;
            }

            return new ItemRow(
                classId, Code(classId), Tier(classId), RequiredLevel(classId),
                PrimaryTypeCode(classId), SecondaryTypeCode(classId));
        }

        public string Code(int classId)
        {
            return GetString(classId, "code");
        }

        /// <summary>
        /// Which of the three tiers an item is, by matching its own `code` against the `normcode`
        /// / `ubercode` / `ultracode` triple that names its family.
        ///
        /// NOT TRACED. Every other derivation in this library models a function in the 1.14d
        /// binary; this one is a convenience over the shipped columns. It agrees with the data —
        /// armor splits exactly 68/67/67 across 202 rows — but no disassembly backs the rule.
        ///
        /// <see cref="ItemTier.Normal"/> is the fallback, so the 153 rows that match nothing come
        /// back Normal rather than throwing: all 151 misc rows (misc.txt has no such columns at
        /// all, so gems, runes, potions, charms and jewellery are unclassifiable by construction)
        /// plus Khalim's Flail `qf1` and Khalim's Will `qf2`, whose normcode is `fla` and whose
        /// uber/ultra cells are empty.
        /// </summary>
        public ItemTier Tier(int classId)
        {
            string code = Code(classId).Trim();
            if (code.Length == 0)
            {
                return ItemTier.Normal;
            }

            if (Matches(classId, "ultracode", code))
            {
                return ItemTier.Elite;
            }

            return Matches(classId, "ubercode", code) ? ItemTier.Exceptional : ItemTier.Normal;
        }

        private bool Matches(int classId, string column, string code)
        {
            return string.Equals(
                (GetString(classId, column) ?? string.Empty).Trim(),
                code,
                StringComparison.OrdinalIgnoreCase);
        }

        public int RequiredLevel(int classId)
        {
            return GetInt(classId, "levelreq");
        }

        // items.txt `type` and `type2`, the two codes IsOfType probes.
        public string PrimaryTypeCode(int classId)
        {
            return GetString(classId, "type");
        }

        public string SecondaryTypeCode(int classId)
        {
            return GetString(classId, "type2");
        }

        public int ClassIdForCode(string code)
        {
            if (string.IsNullOrEmpty(code))
            {
                return -1;
            }

            for (int classId = 0; classId < _count; ++classId)
            {
                if (string.Equals(Code(classId), code, StringComparison.OrdinalIgnoreCase))
                {
                    return classId;
                }
            }

            return -1;
        }
    }
}
