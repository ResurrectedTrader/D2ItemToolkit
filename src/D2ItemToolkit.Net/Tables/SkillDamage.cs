namespace D2ItemToolkit
{
    /// <summary>
    /// The skills.txt arithmetic the tooltip needs for Holy Shield: the level-breakpoint ramp, the
    /// damage pair, and the diminishing-returns parameter blend.
    ///
    /// None of it needs the calc-string evaluator. Holy Shield's `DmgSymPerCalc` is blank and its
    /// `SrcDam` is 0, so `SKILL_CalcMinDamage` reduces to table arithmetic. Skills that DO use a calc
    /// or a weapon-damage source are rejected rather than approximated.
    /// </summary>
    public sealed class SkillDamage
    {
        public const int HolyShieldSkillId = 117;

        /// <summary>Unit state 101, the one the tooltip tests for Holy Shield being up.</summary>
        public const int HolyShieldState = 101;

        private readonly TxtFile _skills;

        public SkillDamage(TxtFile skills)
        {
            _skills = skills;
        }

        /// <summary>
        /// SKILLS_GetValueByLevelBreakpoints 0x644b70. Five per-level slopes with breaks after
        /// levels 8, 16, 22 and 28; level 1 and below contribute nothing.
        /// </summary>
        public static int LevelBreakpoints(int level, int[] slope)
        {
            if (level <= 1 || slope == null || slope.Length < 5)
            {
                return 0;
            }

            if (level > 28)
            {
                return (7 * slope[0]) + (slope[4] * (level - 28))
                       + (6 * (slope[2] + slope[3])) + (8 * slope[1]);
            }

            if (level > 22)
            {
                return (7 * slope[0]) + (slope[3] * (level - 22))
                       + (6 * slope[2]) + (8 * slope[1]);
            }

            if (level > 16)
            {
                return (7 * slope[0]) + (slope[2] * (level - 16)) + (8 * slope[1]);
            }

            if (level <= 8)
            {
                return slope[0] * (level - 1);
            }

            return (7 * slope[0]) + (slope[1] * (level - 8));
        }

        /// <summary>
        /// SKILL_CalcMinDamage 0x647bc0 / SKILL_CalcMaxDamage, restricted to the arm that needs no
        /// player state: not a Kick skill, no SrcDam weapon contribution, no DmgSymPerCalc. Returns
        /// false for anything else so a caller never gets a plausible wrong number.
        ///
        /// The result is shifted left by HitShift, exactly as the binary returns it; the smite writer
        /// then shifts it back down by 8 (0x485e04).
        /// </summary>
        public bool TryCalcDamage(int skillId, int level, out int min, out int max)
        {
            min = 0;
            max = 0;

            if (_skills == null || skillId < 0 || skillId >= _skills.RowCount)
            {
                return false;
            }

            // Bit 9 of dwFlags[0], tested as `skill[+5] & 2` at 0x647bf9. A Kick skill derives its
            // damage from strength and dexterity instead.
            if (Int(skillId, "Kick") != 0)
            {
                return false;
            }

            if (Int(skillId, "SrcDam") != 0)
            {
                return false;
            }

            // A blank cell compiles to -1 and the percentage step is skipped (0x647cc5).
            if (_skills.GetString(skillId, "DmgSymPerCalc").Trim().Length != 0)
            {
                return false;
            }

            int shift = Int(skillId, "HitShift");

            min = (Int(skillId, "MinDam") + LevelBreakpoints(level, Slope(skillId, "MinLevDam")))
                  << shift;
            max = (Int(skillId, "MaxDam") + LevelBreakpoints(level, Slope(skillId, "MaxLevDam")))
                  << shift;

            return true;
        }

        /// <summary>
        /// SKILLS_GetParam45WithDiminishing 0x645c10 into SKILLS_CalcDiminishingReturns 0x645b20.
        /// Despite the name the fields are Param5 and Param6 (+0x158 / +0x15C):
        ///
        ///     t = level * 110 / (level + 6)
        ///     v = param5 + t * (param6 - param5) / 100      capped at param6
        ///
        /// Level 0 or below yields nothing (0x645c43).
        /// </summary>
        public int ParamWithDiminishing(int skillId, int level)
        {
            if (_skills == null || skillId < 0 || skillId >= _skills.RowCount || level <= 0)
            {
                return 0;
            }

            int param5 = Int(skillId, "Param5");
            int param6 = Int(skillId, "Param6");

            int ramp = level * 110 / (level + 6);
            int value = param5 + (ramp * (param6 - param5) / 100);

            return value > param6 ? param6 : value;
        }

        private int[] Slope(int skillId, string stem)
        {
            var slope = new int[5];
            for (int i = 0; i < slope.Length; ++i)
            {
                slope[i] = Int(skillId, stem + (i + 1));
            }

            return slope;
        }

        private int Int(int row, string column)
        {
            return _skills.HasColumn(column) ? _skills.GetInt(row, column) : 0;
        }
    }
}
