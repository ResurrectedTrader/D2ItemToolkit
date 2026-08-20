using System;
using System.Collections.Generic;

namespace D2ItemToolkit
{
    /// <summary>
    /// sets.txt and setitems.txt as TXT_AllocTxt_setitems compiles them, including the LINK it
    /// builds between the two at 0x63668d-0x63670d.
    ///
    /// Record sizes are read off the accessors: GetSetsLine 0x483410 does `imul eax, 128h`
    /// (296 bytes) and GetSetItemsLine 0x483440 `imul eax, 1B8h` (440). Both counts are
    /// POST-SPLICE — <see cref="TxtFile"/> drops the `Expansion` divider row the way
    /// STRUCT_CreateBinFieldExcelAndFillData does at 0x6bd640 — so a row index here is the
    /// index the binary uses.
    /// </summary>
    public sealed class SetTable
    {
        /// <summary>
        /// 0x6366b9: when STRTABLE_LookupString finds no entry for the `index` cell the compiled
        /// record keeps 1507h instead, which is "an evil force".
        /// </summary>
        public const int MissingSetItemNameStringId = 5383;

        /// <summary>
        /// `cmp dword ptr [eax+0Ch], 6 / jge` at 0x6366df — a seventh member is silently dropped,
        /// and pSetItem[] is exactly six pointers wide (0x128 - 0x110).
        /// </summary>
        public const int MaxPiecesPerSet = 6;

        /// <summary>
        /// Eight quadruples at +0x10 and eight more at +0x90. The field table lays PCode2a at
        /// offset 0x10 (0x634e7e) and FCode1 at 0x90 (0x63533e), four bytes to a cell, so each
        /// property is sixteen bytes and each block is 0x80 — which is exactly the stride both
        /// walks in ITEMMOD_ApplySetBonuses use (`add edi, 10h` at 0x6601ec and 0x660228).
        /// </summary>
        public const int PropertiesPerBlock = 8;

        private readonly SetRecord[] _sets;
        private readonly SetItemRecord[] _pieces;
        private readonly TxtFile _setsTxt;
        private Func<string, int> _propertyIds;

        public SetTable(TxtFile sets, TxtFile setItems, TblStringTable strings)
        {
            if (strings == null) throw new ArgumentNullException("strings");

            _setsTxt = sets;

            // A missing table is an empty one rather than a throw: every other provider treats a
            // table it could not read that way, and both counts then bound the loops to zero.
            //
            // The `!= null` conjunct in each loop condition below can never fail, because the count
            // it sits beside is already zero in that case. It stays anyway: flow analysis cannot
            // follow the reasoning, so dropping it just trades a dead per-iteration test for a
            // nullable-dereference warning on every read inside the loop.
            int setCount = sets == null ? 0 : sets.RowCount;
            int pieceCount = setItems == null ? 0 : setItems.RowCount;

            _sets = new SetRecord[setCount];
            var members = new List<SetItemRecord>[setCount];

            for (int row = 0; sets != null && row < setCount; ++row)
            {
                members[row] = new List<SetItemRecord>();

                // +0x02 is filled through DATATBLS_LookupStringId (0x634e14), the same converter
                // every other key column uses, so a miss substitutes 5382 rather than 5383.
                int nameId = TxtKeys.Id(sets, row, "name", strings);

                _sets[row] = new SetRecord(
                    row,
                    sets.GetString(row, "index"),
                    nameId,
                    strings.GetByIndex(nameId),
                    members[row]);
            }

            _pieces = new SetItemRecord[pieceCount];

            // Ascending setitems.txt row order IS pSetItem[] order: the loop assigns
            // wSetItemId = i (0x636690) and appends in the same pass.
            for (int row = 0; setItems != null && row < pieceCount; ++row)
            {
                string key = setItems.GetString(row, "index");

                int nameId = strings.GetIndexByKey(key);
                if (nameId <= 0)
                {
                    nameId = MissingSetItemNameStringId;
                }

                int setId = SetIdForKey(sets, setItems.GetString(row, "set"));

                var piece = new SetItemRecord(
                    row,
                    key,
                    setId,
                    setItems.GetInt(row, "add func"),
                    nameId,
                    strings.GetByIndex(nameId));

                _pieces[row] = piece;

                // 0x6366c3 / 0x6366d1 / 0x6366df: in range, and the set not already full.
                if (setId < 0 || setId >= setCount || members[setId].Count >= MaxPiecesPerSet)
                {
                    continue;
                }

                // +0x2E is the set's CURRENT member count at the moment of the append
                // (0x6366f4), i.e. this piece's slot inside pSetItem[].
                piece.SetSlot(members[setId].Count);
                members[setId].Add(piece);
            }
        }

        /// <summary>
        /// The `set` cell is a linker key over sets.txt `index` (field type 0x0D), not a row
        /// number, so the compiled +0x2C is whatever row carries that index.
        /// </summary>
        private static int SetIdForKey(TxtFile sets, string key)
        {
            return sets == null || key.Length == 0 ? -1 : sets.FindRow("index", key);
        }

        /// <summary>32 with shipped data; the `Expansion` divider is spliced out.</summary>
        public int SetCount
        {
            get { return _sets.Length; }
        }

        /// <summary>127 with shipped data.</summary>
        public int PieceCount
        {
            get { return _pieces.Length; }
        }

        /// <summary>GetSetsLine 0x483410 — null outside the record count.</summary>
        public SetRecord SetAt(int setId)
        {
            return setId >= 0 && setId < _sets.Length ? _sets[setId] : null;
        }

        /// <summary>GetSetItemsLine 0x483440 — null outside the record count.</summary>
        public SetItemRecord PieceAt(int setItemId)
        {
            return setItemId >= 0 && setItemId < _pieces.Length ? _pieces[setItemId] : null;
        }

        /// <summary>
        /// The `PCode*`/`FCode*` cells hold property NAMES; the loader resolves them to
        /// Properties.txt rows at compile time through pPropertiesLinker, exactly as the gems.txt
        /// mod codes are, so the resolver is injected the same way
        /// <see cref="GemTable.ResolvePropertyCodesWith"/> injects it.
        /// </summary>
        internal void ResolvePropertyCodesWith(Func<string, int> resolver)
        {
            _propertyIds = resolver;
        }

        /// <summary>
        /// The eight PARTIAL quadruples at +0x10, in record order: PCode2a, PCode2b, PCode3a,
        /// PCode3b, PCode4a, PCode4b, PCode5a, PCode5b.
        ///
        /// All eight are yielded because the walk at 0x6601c4 SKIPS a slot whose code is negative
        /// (`jl` at 0x6601ca lands past the call, not past the loop) instead of stopping — a set
        /// with a blank `b` slot still reaches the next tier's `a`.
        /// </summary>
        public IEnumerable<ItemProperty> PartialProperties(int setId)
        {
            for (int slot = 0; slot < PropertiesPerBlock; ++slot)
            {
                yield return Property(
                    setId, "P", (2 + slot / 2).ToString() + (slot % 2 == 0 ? "a" : "b"));
            }
        }

        /// <summary>
        /// The eight FULL-SET quadruples at +0x90, FCode1..FCode8. Note the asymmetry with
        /// <see cref="PartialProperties"/>: this walk BREAKS at the first negative code
        /// (`jl` at 0x660209 jumps to the epilogue), so a caller must stop rather than skip.
        /// </summary>
        public IEnumerable<ItemProperty> FullProperties(int setId)
        {
            for (int slot = 0; slot < PropertiesPerBlock; ++slot)
            {
                yield return Property(setId, "F", (slot + 1).ToString());
            }
        }

        private ItemProperty Property(int setId, string prefix, string suffix)
        {
            var property = new ItemProperty();
            property.PropertyId = -1;

            if (_setsTxt == null || setId < 0 || setId >= _sets.Length)
            {
                return property;
            }

            if (_propertyIds != null)
            {
                property.PropertyId = _propertyIds(Cell(setId, prefix + "Code" + suffix));
            }

            property.Param = IntCell(setId, prefix + "Param" + suffix);
            property.Min = IntCell(setId, prefix + "Min" + suffix);
            property.Max = IntCell(setId, prefix + "Max" + suffix);
            return property;
        }

        private string Cell(int row, string column)
        {
            return _setsTxt.HasColumn(column) ? _setsTxt.GetString(row, column) : string.Empty;
        }

        private int IntCell(int row, string column)
        {
            return _setsTxt.HasColumn(column) ? _setsTxt.GetInt(row, column) : 0;
        }
    }

    /// <summary>One sets.txt record, 296 bytes in the game.</summary>
    public sealed class SetRecord
    {
        private readonly IReadOnlyList<SetItemRecord> _pieces;

        internal SetRecord(
            int setId, string key, int nameStringId, string name, IReadOnlyList<SetItemRecord> pieces)
        {
            SetId = setId;
            Key = key;
            NameStringId = nameStringId;
            Name = name;
            _pieces = pieces;
        }

        /// <summary>The post-splice row index, which is what setitems.txt +0x2C stores.</summary>
        public int SetId { get; private set; }

        /// <summary>The `index` cell — the key setitems.txt `set` resolves against.</summary>
        public string Key { get; private set; }

        /// <summary>+0x02, read at 0x48d3b1 and handed to GetLocaleString.</summary>
        public int NameStringId { get; private set; }

        /// <summary>The resolved display name. It is NOT the key: `Angelical Raiment` -> `Angelic Raiment`.</summary>
        public string Name { get; private set; }

        /// <summary>
        /// pSetItem[] at +0x110, in the order the link loop appended it — ascending setitems.txt
        /// row. Never longer than six.
        /// </summary>
        public IReadOnlyList<SetItemRecord> Pieces
        {
            get { return _pieces; }
        }
    }

    /// <summary>One setitems.txt record, 440 bytes in the game.</summary>
    public sealed class SetItemRecord
    {
        internal SetItemRecord(
            int setItemId, string key, int setId, int addFunc, int nameStringId, string name)
        {
            SetItemId = setItemId;
            Key = key;
            SetId = setId;
            AddFunc = addFunc;
            NameStringId = nameStringId;
            Name = name;
            Slot = -1;
        }

        internal void SetSlot(int slot)
        {
            Slot = slot;
        }

        /// <summary>+0x00, and the row index — the two are the same thing (0x636690).</summary>
        public int SetItemId { get; private set; }

        /// <summary>The `index` cell, which is also the display-name key.</summary>
        public string Key { get; private set; }

        /// <summary>+0x2C.</summary>
        public int SetId { get; private set; }

        /// <summary>
        /// +0x2E — this piece's index INSIDE its set, 0..5, and the bit
        /// ITEMS_GetEquippedSetItemsMask sets for it (0x62a474). -1 when the link loop dropped it.
        /// </summary>
        public int Slot { get; private set; }

        /// <summary>+0x87, read at 0x4e659f and 0x663a5d. 0 none, 1 per-piece, 2 progressive.</summary>
        public int AddFunc { get; private set; }

        /// <summary>+0x24, defaulted to 5383 at 0x6366b9.</summary>
        public int NameStringId { get; private set; }

        /// <summary>The resolved display name.</summary>
        public string Name { get; private set; }
    }
}
