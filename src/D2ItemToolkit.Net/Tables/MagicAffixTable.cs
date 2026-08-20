namespace D2ItemToolkit
{
    /// <summary>
    /// TXT_magicaffixes_GetLine 0x633ee0. The three affix files are compiled into ONE array in the
    /// order [MagicSuffix][MagicPrefix][automagic] and addressed 1-based, so id 1 is the first
    /// SUFFIX row and an id past the suffix count spills into the prefixes.
    /// </summary>
    public sealed class MagicAffixTable
    {
        private readonly TxtFile[] _tables;
        private readonly TxtSkillTable _skills;

        public MagicAffixTable(D2DataFiles data)
        {
            _tables = new[] { data.MagicSuffix, data.MagicPrefix, data.AutoMagic };
            _skills = data.Skills;
        }

        /// <summary>
        /// How many 1-based affix ids <see cref="TryResolve"/> will accept — the CONCATENATED length
        /// of [MagicSuffix][MagicPrefix][automagic], which is the array the game indexes. Iterate
        /// `1..Count` inclusive, since 0 is "no affix".
        /// </summary>
        public int RowCount
        {
            get
            {
                int total = 0;
                foreach (TxtFile table in _tables)
                {
                    if (table != null)
                    {
                        total += table.RowCount;
                    }
                }

                return total;
            }
        }

        public bool TryResolve(int id, out TxtFile table, out int row)
        {
            table = null;
            row = -1;

            if (id <= 0)
            {
                return false;
            }

            int at = id - 1;

            foreach (TxtFile candidate in _tables)
            {
                if (candidate == null)
                {
                    continue;
                }

                if (at < candidate.RowCount)
                {
                    table = candidate;
                    row = at;
                    return true;
                }

                at -= candidate.RowCount;
            }

            return false;
        }

        /// <summary>
        /// ITEMS_nullsub 0x628830 — despite the name, the level-requirement fold. Takes the running
        /// maximum and raises it to this affix's requirement, preferring classlevelreq when the
        /// affix is restricted to the viewer's own class.
        /// </summary>
        internal int RaiseLevelRequirement(int running, int id, ItemViewer viewer)
        {
            TxtFile table;
            int row;
            if (!TryResolve(id, out table, out row))
            {
                return running;
            }

            // nClass is 0xFF when the affix has no class restriction; the compiler writes that for a
            // blank "class" cell, so a missing column reads as unrestricted here.
            int restrictedTo = ClassCode(table, row, _skills);

            int required = restrictedTo != NoClass && viewer != null && restrictedTo == viewer.ClassId
                ? table.GetInt(row, "classlevelreq")
                : table.GetInt(row, "levelreq");

            return running <= required ? required : running;
        }

        public const int NoClass = 0xFF;

        private static int ClassCode(TxtFile table, int row, TxtSkillTable skills)
        {
            if (skills == null || !table.HasColumn("class"))
            {
                return NoClass;
            }

            string code = table.GetString(row, "class");
            if (string.IsNullOrEmpty(code))
            {
                return NoClass;
            }

            int classId = skills.ClassIdForCode(code);
            return classId < 0 ? NoClass : classId;
        }
    }
}
