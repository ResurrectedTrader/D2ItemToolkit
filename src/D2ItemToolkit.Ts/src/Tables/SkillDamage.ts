import type { TxtFile } from '../Data/TxtFile.js';
import { Int32 } from '../Types.js';

/** The `{ min, max }` pair C# returns through `out int min, out int max`. */
export interface SkillDamageRange {
  min: number;
  max: number;
}

/**
 * The skills.txt arithmetic the tooltip needs for Holy Shield: the level-breakpoint ramp, the
 * damage pair, and the diminishing-returns parameter blend.
 *
 * None of it needs the calc-string evaluator. Holy Shield's `DmgSymPerCalc` is blank and its
 * `SrcDam` is 0, so `SKILL_CalcMinDamage` reduces to table arithmetic. Skills that DO use a calc
 * or a weapon-damage source are rejected rather than approximated.
 */
export class SkillDamage {
  static readonly HolyShieldSkillId = 117;

  /** Unit state 101, the one the tooltip tests for Holy Shield being up. */
  static readonly HolyShieldState = 101;

  private readonly _skills: TxtFile | null;

  constructor(skills: TxtFile | null) {
    this._skills = skills;
  }

  /**
   * SKILLS_GetValueByLevelBreakpoints 0x644b70. Five per-level slopes with breaks after
   * levels 8, 16, 22 and 28; level 1 and below contribute nothing.
   */
  static levelBreakpoints(level: number, slope: readonly number[] | null): number {
    if (level <= 1 || slope === null || slope.length < 5) {
      return 0;
    }

    const s0 = slope[0] ?? 0;
    const s1 = slope[1] ?? 0;
    const s2 = slope[2] ?? 0;
    const s3 = slope[3] ?? 0;
    const s4 = slope[4] ?? 0;

    if (level > 28) {
      return Int32.of(
        Int32.mul(7, s0) +
          Int32.mul(s4, Int32.of(level - 28)) +
          Int32.mul(6, Int32.of(s2 + s3)) +
          Int32.mul(8, s1),
      );
    }

    if (level > 22) {
      return Int32.of(
        Int32.mul(7, s0) +
          Int32.mul(s3, Int32.of(level - 22)) +
          Int32.mul(6, s2) +
          Int32.mul(8, s1),
      );
    }

    if (level > 16) {
      return Int32.of(Int32.mul(7, s0) + Int32.mul(s2, Int32.of(level - 16)) + Int32.mul(8, s1));
    }

    if (level <= 8) {
      return Int32.mul(s0, Int32.of(level - 1));
    }

    return Int32.of(Int32.mul(7, s0) + Int32.mul(s1, Int32.of(level - 8)));
  }

  /**
   * SKILL_CalcMinDamage 0x647bc0 / SKILL_CalcMaxDamage, restricted to the arm that needs no
   * player state: not a Kick skill, no SrcDam weapon contribution, no DmgSymPerCalc. Returns
   * null for anything else so a caller never gets a plausible wrong number.
   *
   * The result is shifted left by HitShift, exactly as the binary returns it; the smite writer
   * then shifts it back down by 8 (0x485e04).
   */
  tryCalcDamage(skillId: number, level: number): SkillDamageRange | null {
    const skills = this._skills;
    if (skills === null || skillId < 0 || skillId >= skills.rowCount) {
      return null;
    }

    // Bit 9 of dwFlags[0], tested as `skill[+5] & 2` at 0x647bf9. A Kick skill derives its
    // damage from strength and dexterity instead.
    if (this.int(skillId, 'Kick') !== 0) {
      return null;
    }

    if (this.int(skillId, 'SrcDam') !== 0) {
      return null;
    }

    // A blank cell compiles to -1 and the percentage step is skipped (0x647cc5).
    if (skills.getString(skillId, 'DmgSymPerCalc').trim().length !== 0) {
      return null;
    }

    const shift = this.int(skillId, 'HitShift');

    return {
      min:
        Int32.of(
          this.int(skillId, 'MinDam') +
            SkillDamage.levelBreakpoints(level, this.slope(skillId, 'MinLevDam')),
        ) << shift,
      max:
        Int32.of(
          this.int(skillId, 'MaxDam') +
            SkillDamage.levelBreakpoints(level, this.slope(skillId, 'MaxLevDam')),
        ) << shift,
    };
  }

  /**
   * SKILLS_GetParam45WithDiminishing 0x645c10 into SKILLS_CalcDiminishingReturns 0x645b20.
   * Despite the name the fields are Param5 and Param6 (+0x158 / +0x15C):
   *
   *     t = level * 110 / (level + 6)
   *     v = param5 + t * (param6 - param5) / 100      capped at param6
   *
   * Level 0 or below yields nothing (0x645c43).
   */
  paramWithDiminishing(skillId: number, level: number): number {
    const skills = this._skills;
    if (skills === null || skillId < 0 || skillId >= skills.rowCount || level <= 0) {
      return 0;
    }

    const param5 = this.int(skillId, 'Param5');
    const param6 = this.int(skillId, 'Param6');

    const ramp = Int32.div(Int32.mul(level, 110), Int32.of(level + 6));
    const value = Int32.of(param5 + Int32.div(Int32.mul(ramp, Int32.of(param6 - param5)), 100));

    return value > param6 ? param6 : value;
  }

  private slope(skillId: number, stem: string): number[] {
    const slope = new Array<number>(5).fill(0);
    for (let i = 0; i < slope.length; ++i) {
      slope[i] = this.int(skillId, stem + String(i + 1));
    }

    return slope;
  }

  private int(row: number, column: string): number {
    const skills = this._skills;
    if (skills === null) {
      return 0;
    }

    return skills.hasColumn(column) ? skills.getInt(row, column) : 0;
  }
}
