using System.Collections.Generic;

namespace D2ItemToolkit
{
    // One record per table row, so every public table is walked the same way: `RowCount` for the
    // bound and `RowAt(index)` for the row. The per-field getters they wrap are all still there —
    // these exist because the tables previously disagreed on both halves (Count vs RowCount vs
    // StatCount; Code(i) vs CodeAt(i) vs an indexer), which made iterating several of them a matter
    // of remembering which spelling each one chose.
    //
    // `RowAt` returns null for an out-of-range index rather than throwing, matching what the
    // underlying getters already do.
    //
    // Two tables keep two counts because they genuinely have two row spaces, and so name their
    // accessors after them instead: SetTable (SetAt / PieceAt) and TxtMonsterTypeTable
    // (MonsterAt / MonsterTypeAt). TxtFile keeps only RowCount — it is the generic column reader
    // every other table is built on, and a "row" there has no fixed shape to hand back.

    /// <summary>A row of the concatenated weapons/armor/misc table, keyed by classId.</summary>
    public sealed class ItemRow
    {
        internal ItemRow(
            int classId, string code, ItemTier tier, int requiredLevel,
            string primaryTypeCode, string secondaryTypeCode)
        {
            ClassId = classId;
            Code = code;
            Tier = tier;
            RequiredLevel = requiredLevel;
            PrimaryTypeCode = primaryTypeCode;
            SecondaryTypeCode = secondaryTypeCode;
        }

        public int ClassId { get; private set; }
        public string Code { get; private set; }
        public ItemTier Tier { get; private set; }
        public int RequiredLevel { get; private set; }

        /// <summary>items.txt `type`.</summary>
        public string PrimaryTypeCode { get; private set; }

        /// <summary>items.txt `type2`; empty when the row declares only one.</summary>
        public string SecondaryTypeCode { get; private set; }
    }

    /// <summary>A row of ItemTypes.txt.</summary>
    public sealed class ItemTypeRow
    {
        internal ItemTypeRow(int row, string code, string classCode, bool isThrowable)
        {
            Row = row;
            Code = code;
            ClassCode = classCode;
            IsThrowable = isThrowable;
        }

        public int Row { get; private set; }
        public string Code { get; private set; }

        /// <summary>The `Class` column — empty unless the type is class-restricted.</summary>
        public string ClassCode { get; private set; }

        public bool IsThrowable { get; private set; }
    }

    /// <summary>A row of colors.txt. The ROW INDEX is the palette-shift value items store.</summary>
    public sealed class ColorRow
    {
        internal ColorRow(int row, string code)
        {
            Row = row;
            Code = code;
        }

        public int Row { get; private set; }
        public string Code { get; private set; }
    }

    /// <summary>A row of gems.txt.</summary>
    public sealed class GemRow
    {
        internal GemRow(int row, string code, string letter)
        {
            Row = row;
            Code = code;
            Letter = letter;
        }

        public int Row { get; private set; }
        public string Code { get; private set; }

        /// <summary>The rune letter a runeword name is spelled with; empty for a gem.</summary>
        public string Letter { get; private set; }
    }

    /// <summary>A row of skills.txt.</summary>
    public sealed class SkillRow
    {
        internal SkillRow(int skillId, string name, int classId, int requiredLevel)
        {
            SkillId = skillId;
            Name = name;
            ClassId = classId;
            RequiredLevel = requiredLevel;
        }

        public int SkillId { get; private set; }
        public string Name { get; private set; }

        /// <summary>0-6, or -1 when the skill belongs to no class.</summary>
        public int ClassId { get; private set; }

        public int RequiredLevel { get; private set; }
    }

    /// <summary>A row of charstats.txt.</summary>
    public sealed class CharacterClassRow
    {
        internal CharacterClassRow(
            int classId, string allSkillsText, string classOnlyText,
            IReadOnlyList<string> skillTabTexts)
        {
            ClassId = classId;
            AllSkillsText = allSkillsText;
            ClassOnlyText = classOnlyText;
            SkillTabTexts = skillTabTexts;
        }

        public int ClassId { get; private set; }
        public string AllSkillsText { get; private set; }
        public string ClassOnlyText { get; private set; }

        /// <summary>The three tab names, in tab order.</summary>
        public IReadOnlyList<string> SkillTabTexts { get; private set; }
    }

    /// <summary>A row of monstats.txt, as far as the tooltip needs it.</summary>
    public sealed class MonsterRow
    {
        internal MonsterRow(int monsterId, string name)
        {
            MonsterId = monsterId;
            Name = name;
        }

        public int MonsterId { get; private set; }
        public string Name { get; private set; }
    }

    /// <summary>A row of MonType.txt.</summary>
    public sealed class MonsterTypeRow
    {
        internal MonsterTypeRow(int monsterTypeId, string name)
        {
            MonsterTypeId = monsterTypeId;
            Name = name;
        }

        public int MonsterTypeId { get; private set; }
        public string Name { get; private set; }
    }
}
