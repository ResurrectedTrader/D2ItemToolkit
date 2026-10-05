import type { StringTable } from '../Data/StringTable.js';
import type { TxtFile } from '../Data/TxtFile.js';
import { DescStringIds, Int32, type IStringTable } from '../Types.js';
import { CFormat } from './CFormat.js';

/** One of skilldesc's descline1..6 groups. */
export interface SkillDescLineRow {
  func: number;
  textA: number;
  textB: number;
  calcA: string;
  calcB: string;
}

/**
 * What descfunc 15's `item proc text` arm reads of one skill: the skilldesc row's proc columns and
 * desclines, and the skills row's params.
 */
export interface SkillItemProc {
  /** The string id of `item proc text`; 5382 when blank or the key misses. */
  textId: number;
  lineCount: number;
  lines: SkillDescLineRow[];
  /** skills.txt Param1..Param8. */
  params: number[];
  auraLenCalc: string;
}

export const SkillItemProcMaxLines = 6;

export interface ISkillItemProcSource {
  /** Null when the skill has no skilldesc row. */
  getItemProc(skillId: number): SkillItemProc | null;
}

export function isSkillItemProcSource(value: unknown): value is ISkillItemProcSource {
  return (
    typeof value === 'object' &&
    value !== null &&
    typeof (value as Partial<ISkillItemProcSource>).getItemProc === 'function'
  );
}

/** Null when skilldesc has no `item proc text` column (1.14d). */
export function readSkillItemProc(
  skills: TxtFile,
  skillRow: number,
  skillDesc: TxtFile,
  descRow: number,
  strings: StringTable,
): SkillItemProc | null {
  if (!skillDesc.hasColumn('item proc text')) {
    return null;
  }

  // TxtKeys.id: an absent column is 0, a blank or missing key 5382.
  const keyId = (column: string): number =>
    skillDesc.hasColumn(column) ? strings.resolveKey(skillDesc.getString(descRow, column)) : 0;
  const cell = (file: TxtFile, row: number, column: string): string =>
    file.hasColumn(column) ? file.getString(row, column) : '';

  const lines: SkillDescLineRow[] = [];
  for (let line = 0; line < SkillItemProcMaxLines; ++line) {
    const n = String(line + 1);
    lines.push({
      func: skillDesc.getInt(descRow, 'descline' + n),
      textA: keyId('desctexta' + n),
      textB: keyId('desctextb' + n),
      calcA: cell(skillDesc, descRow, 'desccalca' + n),
      calcB: cell(skillDesc, descRow, 'desccalcb' + n),
    });
  }

  const params: number[] = [];
  for (let at = 0; at < 8; ++at) {
    params.push(skills.getInt(skillRow, 'Param' + String(at + 1)));
  }

  return {
    textId: keyId('item proc text'),
    lineCount: skillDesc.getInt(descRow, 'item proc descline count'),
    lines,
    params,
    auraLenCalc: cell(skills, skillRow, 'auralencalc'),
  };
}

const MissingString = 5382;
const StrSkill0 = 4252;
const Second = 4267;
const Seconds = 4268;

const ProcBufferSize = 0x100;
const LineTempSize = 200;
const LineBufferSize = 0x800;

// skillcalc.txt row indices, which are the param ids SKILLS_GetSpecialParamValue switches on. The
// names are data, not code; these are the 1.14d-compatible rows both D2R table sets ship (base is
// a byte-prefix of RotW).
const SkillCalcRows: ReadonlyMap<string, number> = new Map([
  ['ln12', 0],
  ['ln34', 2],
  ['ln56', 4],
  ['ln78', 6],
  ['par1', 8],
  ['par2', 9],
  ['par3', 10],
  ['par4', 11],
  ['par5', 12],
  ['par6', 13],
  ['par7', 14],
  ['par8', 15],
  ['lvl', 16],
  ['len', 54],
]);

/**
 * The part of D2R's skill-description engine that descfunc 15's `item proc text` arm
 * (ITEMSTATDESC_Build 0x1401ec3b3-0x1401ec8b0) reaches: the line writer sub_1401cbd30 for
 * desclines 12 and 74, and the desccalc evaluator sub_14028d340 for literals and the skillcalc
 * keywords SKILLS_GetSpecialParamValue 0x140246ca0 answers from the skills row alone.
 */
export const SkillDescCalc = {
  /**
   * The `item proc text` line, or the empty string where the game leaves the buffer untouched (no
   * text, or fewer lines produced than the row promises).
   */
  formatItemProc(skill: SkillItemProc, level: number, strings: IStringTable): string {
    const proc = strings.getByIndex(skill.textId);
    if (proc === null || proc.length === 0) {
      return '';
    }

    let total = CFormat.utf8Length(proc);
    const count = Math.min(Math.max(skill.lineCount, 0), SkillItemProcMaxLines);
    const args: string[] = [];
    for (let line = 0; line < count; ++line) {
      let text = SkillDescCalc.describeLine(skill, level, line, strings);
      if (text === null) {
        continue;
      }

      // 0x1401ec534: the guard measures the line before its newlines are removed.
      if (total + CFormat.utf8Length(text) >= ProcBufferSize) {
        break;
      }

      text = text.split('\n').join('');
      args.push(text);
      total += CFormat.utf8Length(text);
    }

    if (args.length !== count) {
      return '';
    }

    return CFormat.bounded(count === 0 ? proc : CFormat.sprintf(proc, ...args), ProcBufferSize);
  },

  /** sub_1401cbd30 for one descline; null when it reports nothing written. */
  describeLine(
    skill: SkillItemProc,
    level: number,
    line: number,
    strings: IStringTable,
  ): string | null {
    const row = skill.lines[line] as SkillDescLineRow;
    const start = CFormat.bounded(nz(strings.getByIndex(StrSkill0)), LineBufferSize);
    let buffer = start;

    switch (row.func) {
      case 12: {
        const calcA = SkillDescCalc.tryEvaluate(row.calcA, skill, level);
        if (calcA === null) {
          return null;
        }

        if (row.textB !== MissingString) {
          buffer += nz(strings.getByIndex(row.textB));
        }

        buffer += secondsText(row.textA, calcA, strings);
        break;
      }

      case 74: {
        if (row.textA === MissingString) {
          return null;
        }

        const calcA = SkillDescCalc.tryEvaluate(row.calcA, skill, level);
        if (calcA === null || calcA === 0) {
          return null;
        }

        buffer +=
          CFormat.bounded(CFormat.sprintf(nz(strings.getByIndex(row.textA)), calcA), LineTempSize) +
          nz(strings.getByIndex(DescStringIds.Newline));
        break;
      }

      default:
        // Every other line writer belongs to the skill tooltip; no proc row uses one.
        return null;
    }

    return buffer === start ? null : buffer;
  },

  /**
   * A desccalc cell: blank is 0 (sub_14028d340 on code offset -1), a decimal literal is itself, a
   * lone skillcalc keyword is its param. Anything else needs the expression evaluator and the
   * live unit, and is reported as unsupported (null).
   */
  tryEvaluate(cell: string, skill: SkillItemProc, level: number): number | null {
    return evaluate(cell, skill, level, true);
  },
};

function evaluate(
  cell: string,
  skill: SkillItemProc,
  level: number,
  allowLen: boolean,
): number | null {
  if (cell.length === 0) {
    return 0;
  }

  if (/^[0-9]+$/.test(cell)) {
    const value = Number(cell);
    return value <= 0x7fffffff ? value : null;
  }

  const param = SkillCalcRows.get(cell);
  if (param === undefined) {
    return null;
  }

  const p = (index: number): number => skill.params[index] ?? 0;
  switch (param) {
    case 0:
    case 2:
    case 4:
    case 6:
      return level <= 0 ? 0 : Int32.of(p(param) + Int32.mul(p(param + 1), level - 1));

    case 16:
      return level;

    case 54:
      // SKILLS_EvaluateSkillFormula over auralencalc (+128); a blank cell there is untraced, so
      // only a cell this subset can read is accepted.
      return allowLen && skill.auraLenCalc.length !== 0
        ? evaluate(skill.auraLenCalc, skill, level, false)
        : null;

    default:
      return p(param - 8);
  }
}

// sub_1401c0c60, frames to "N seconds" with C's truncating division.
function secondsText(textId: number, frames: number, strings: IStringTable): string {
  const whole = Int32.div(frames, 25);
  const tenths = Int32.div(10 * (frames % 25), 25);
  if (whole === 0 && tenths === 0) {
    return '';
  }

  const number =
    tenths !== 0 ? CFormat.sprintf('%d.%d', whole, tenths) : CFormat.sprintf('%d', whole);

  return (
    nz(strings.getByIndex(textId)) +
    number +
    nz(strings.getByIndex(whole === 1 && tenths === 0 ? Second : Seconds)) +
    nz(strings.getByIndex(DescStringIds.Newline))
  );
}

function nz(text: string | null): string {
  return text ?? '';
}
