using System;
using System.Collections.Generic;
using System.Globalization;

namespace D2ItemToolkit
{
    /// <summary>One of skilldesc's descline1..6 groups.</summary>
    internal sealed class SkillDescLineRow
    {
        public int Func;
        public int TextA;
        public int TextB;
        public string CalcA;
        public string CalcB;
    }

    /// <summary>
    /// What descfunc 15's <c>item proc text</c> arm reads of one skill: the skilldesc row's proc
    /// columns and desclines, and the skills row's params.
    /// </summary>
    internal sealed class SkillItemProc
    {
        public const int MaxLines = 6;

        /// <summary>The string id of <c>item proc text</c>; 5382 when blank or the key misses.</summary>
        public int TextId;

        public int LineCount;

        public SkillDescLineRow[] Lines;

        /// <summary>skills.txt Param1..Param8.</summary>
        public int[] Params;

        public string AuraLenCalc;

        /// <summary>Null when skilldesc has no <c>item proc text</c> column (1.14d).</summary>
        internal static SkillItemProc Read(
            TxtFile skills, int skillRow, TxtFile skillDesc, int descRow, StringTable strings)
        {
            if (!skillDesc.HasColumn("item proc text"))
            {
                return null;
            }

            var proc = new SkillItemProc();
            proc.TextId = TxtKeys.Id(skillDesc, descRow, "item proc text", strings);
            proc.LineCount = skillDesc.GetInt(descRow, "item proc descline count");
            proc.Lines = new SkillDescLineRow[MaxLines];
            for (int line = 0; line < MaxLines; ++line)
            {
                string n = (line + 1).ToString(CultureInfo.InvariantCulture);
                var row = new SkillDescLineRow();
                row.Func = skillDesc.GetInt(descRow, "descline" + n);
                row.TextA = TxtKeys.Id(skillDesc, descRow, "desctexta" + n, strings);
                row.TextB = TxtKeys.Id(skillDesc, descRow, "desctextb" + n, strings);
                row.CalcA = Cell(skillDesc, descRow, "desccalca" + n);
                row.CalcB = Cell(skillDesc, descRow, "desccalcb" + n);
                proc.Lines[line] = row;
            }

            proc.Params = new int[8];
            for (int at = 0; at < proc.Params.Length; ++at)
            {
                proc.Params[at] = skills.GetInt(
                    skillRow, "Param" + (at + 1).ToString(CultureInfo.InvariantCulture));
            }

            proc.AuraLenCalc = Cell(skills, skillRow, "auralencalc");
            return proc;
        }

        private static string Cell(TxtFile file, int row, string column)
        {
            return file.HasColumn(column) ? file.GetString(row, column) : string.Empty;
        }
    }

    internal interface ISkillItemProcSource
    {
        /// <summary>Null when the skill has no skilldesc row.</summary>
        SkillItemProc GetItemProc(int skillId);
    }

    /// <summary>
    /// The part of D2R's skill-description engine that descfunc 15's <c>item proc text</c> arm
    /// (ITEMSTATDESC_Build 0x1401ec3b3-0x1401ec8b0) reaches: the line writer sub_1401cbd30 for
    /// desclines 12 and 74, and the desccalc evaluator sub_14028d340 for literals and the skillcalc
    /// keywords SKILLS_GetSpecialParamValue 0x140246ca0 answers from the skills row alone.
    /// </summary>
    internal static class SkillDescCalc
    {
        private const int MissingString = 5382;
        private const int StrSkill0 = 4252;
        private const int Second = 4267;
        private const int Seconds = 4268;

        private const int ProcBufferSize = 0x100;
        private const int LineTempSize = 200;
        private const int LineBufferSize = 0x800;

        // skillcalc.txt row indices, which are the param ids SKILLS_GetSpecialParamValue switches
        // on. The names are data, not code; these are the 1.14d-compatible rows both D2R table
        // sets ship (base is a byte-prefix of RotW).
        private static readonly Dictionary<string, int> SkillCalcRows = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            { "ln12", 0 }, { "ln34", 2 }, { "ln56", 4 }, { "ln78", 6 },
            { "par1", 8 }, { "par2", 9 }, { "par3", 10 }, { "par4", 11 },
            { "par5", 12 }, { "par6", 13 }, { "par7", 14 }, { "par8", 15 },
            { "lvl", 16 }, { "len", 54 },
        };

        /// <summary>
        /// The <c>item proc text</c> line, or the empty string where the game leaves the buffer
        /// untouched (no text, or fewer lines produced than the row promises).
        /// </summary>
        public static string FormatItemProc(SkillItemProc skill, int level, IStringTable strings)
        {
            string proc = strings.GetByIndex(skill.TextId);
            if (string.IsNullOrEmpty(proc))
            {
                return string.Empty;
            }

            int total = CFormat.Utf8Length(proc);
            int count = Math.Min(Math.Max(skill.LineCount, 0), SkillItemProc.MaxLines);
            var args = new List<object>(count);
            for (int line = 0; line < count; ++line)
            {
                string text = DescribeLine(skill, level, line, strings);
                if (text == null)
                {
                    continue;
                }

                // 0x1401ec534: the guard measures the line before its newlines are removed.
                if (total + CFormat.Utf8Length(text) >= ProcBufferSize)
                {
                    break;
                }

                text = text.Replace("\n", string.Empty);
                args.Add(text);
                total += CFormat.Utf8Length(text);
            }

            if (args.Count != count)
            {
                return string.Empty;
            }

            return CFormat.Bounded(
                count == 0 ? proc : CFormat.Sprintf(proc, args.ToArray()), ProcBufferSize);
        }

        /// <summary>sub_1401cbd30 for one descline; null when it reports nothing written.</summary>
        public static string DescribeLine(SkillItemProc skill, int level, int line, IStringTable strings)
        {
            SkillDescLineRow row = skill.Lines[line];
            string start = CFormat.Bounded(Nz(strings.GetByIndex(StrSkill0)), LineBufferSize);
            string buffer = start;
            int calcA;

            switch (row.Func)
            {
                case 12:
                    if (!TryEvaluate(row.CalcA, skill, level, out calcA))
                    {
                        return null;
                    }

                    if (row.TextB != MissingString)
                    {
                        buffer += Nz(strings.GetByIndex(row.TextB));
                    }

                    buffer += SecondsText(row.TextA, calcA, strings);
                    break;

                case 74:
                    if (row.TextA == MissingString)
                    {
                        return null;
                    }

                    if (!TryEvaluate(row.CalcA, skill, level, out calcA) || calcA == 0)
                    {
                        return null;
                    }

                    buffer += CFormat.Bounded(
                                  CFormat.Sprintf(Nz(strings.GetByIndex(row.TextA)), calcA),
                                  LineTempSize)
                              + Nz(strings.GetByIndex(DescStringIds.Newline));
                    break;

                default:
                    // Every other line writer belongs to the skill tooltip; no proc row uses one.
                    return null;
            }

            return string.Equals(buffer, start, StringComparison.Ordinal) ? null : buffer;
        }

        // sub_1401c0c60, frames to "N seconds" with C's truncating division.
        private static string SecondsText(int textId, int frames, IStringTable strings)
        {
            int whole = frames / 25;
            int tenths = 10 * (frames % 25) / 25;
            if (whole == 0 && tenths == 0)
            {
                return string.Empty;
            }

            string number = tenths != 0
                ? CFormat.Sprintf("%d.%d", whole, tenths)
                : CFormat.Sprintf("%d", whole);

            return Nz(strings.GetByIndex(textId))
                   + number
                   + Nz(strings.GetByIndex(whole == 1 && tenths == 0 ? Second : Seconds))
                   + Nz(strings.GetByIndex(DescStringIds.Newline));
        }

        /// <summary>
        /// A desccalc cell: blank is 0 (sub_14028d340 on code offset -1), a decimal literal is
        /// itself, a lone skillcalc keyword is its param. Anything else needs the expression
        /// evaluator and the live unit, and is reported as unsupported.
        /// </summary>
        public static bool TryEvaluate(string cell, SkillItemProc skill, int level, out int value)
        {
            return TryEvaluate(cell, skill, level, true, out value);
        }

        private static bool TryEvaluate(
            string cell, SkillItemProc skill, int level, bool allowLen, out int value)
        {
            value = 0;
            if (string.IsNullOrEmpty(cell))
            {
                return true;
            }

            if (IsDigits(cell))
            {
                return int.TryParse(cell, NumberStyles.None, CultureInfo.InvariantCulture, out value);
            }

            int param;
            if (!SkillCalcRows.TryGetValue(cell, out param))
            {
                return false;
            }

            switch (param)
            {
                case 0:
                case 2:
                case 4:
                case 6:
                    value = level <= 0
                        ? 0
                        : unchecked(skill.Params[param] + (skill.Params[param + 1] * (level - 1)));
                    return true;

                case 16:
                    value = level;
                    return true;

                case 54:
                    // SKILLS_EvaluateSkillFormula over auralencalc (+128); a blank cell there is
                    // untraced, so only a cell this subset can read is accepted.
                    return allowLen
                           && !string.IsNullOrEmpty(skill.AuraLenCalc)
                           && TryEvaluate(skill.AuraLenCalc, skill, level, false, out value);

                default:
                    value = skill.Params[param - 8];
                    return true;
            }
        }

        private static bool IsDigits(string text)
        {
            foreach (char c in text)
            {
                if (c < '0' || c > '9')
                {
                    return false;
                }
            }

            return true;
        }

        private static string Nz(string text)
        {
            return text ?? string.Empty;
        }
    }
}
