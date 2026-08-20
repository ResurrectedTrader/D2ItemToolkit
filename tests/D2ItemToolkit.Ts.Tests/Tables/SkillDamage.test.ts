import { describe, expect, it } from 'vitest';
import { SkillDamage } from '../../../src/D2ItemToolkit.Ts/src/Tables/SkillDamage.js';
import { D2DataFiles } from '../../../src/D2ItemToolkit.Ts/src/Tables/TxtDataProviders.js';
import type { TxtFile } from '../../../src/D2ItemToolkit.Ts/src/Data/TxtFile.js';

// Holy Shield's contribution to smite damage (0x485df1) and block chance (0x485c58), computed
// from skills.txt rather than supplied by the producer. HolyShieldTests.cs — the arms that render
// a whole tooltip through RecordSections are deferred until that class is ported.

const Data = D2DataFiles.load();
const SkillRows = Data.skillRows as TxtFile;
const Skills = new SkillDamage(SkillRows);

describe('SkillDamage', () => {
  it('skill one hundred and seventeen is holy shield', () => {
    expect(SkillRows.getString(SkillDamage.HolyShieldSkillId, 'skill')).toBe('Holy Shield');
  });

  it.each([
    // slope [2,3,4,4,4] from MinLevDam1..5. Level 1 contributes nothing at all.
    [1, 0],
    [5, 8], // 2 * (5 - 1)
    [8, 14], // 2 * 7
    [10, 20], // 7*2 + 3*(10-8)
    [20, 54], // 7*2 + 4*(20-16) + 8*3
    [25, 74], // 7*2 + 4*(25-22) + 6*4 + 8*3 = 14+12+24+24
    [30, 94], // 7*2 + 4*(30-28) + 6*(4+4) + 8*3 = 14+8+48+24
  ])('the level breakpoint ramp matches the five slopes (%i)', (level, expected) => {
    expect(SkillDamage.levelBreakpoints(level, [2, 3, 4, 4, 4])).toBe(expected);
  });

  it('holy shield damage is table arithmetic with no calc string', () => {
    const damage = Skills.tryCalcDamage(SkillDamage.HolyShieldSkillId, 10);
    expect(damage).not.toBeNull();

    // MinDam 3 and MaxDam 6, plus the ramp, all shifted left by HitShift 8. The smite writer
    // shifts back down by 8, so the visible bonus is 3 + 20 and 6 + 20.
    expect((damage as NonNullable<typeof damage>).min).toBe((3 + 20) << 8);
    expect((damage as NonNullable<typeof damage>).max).toBe((6 + 20) << 8);
  });

  it.each([
    // param5 10, param6 40. ramp = level*110/(level+6); v = 10 + ramp*30/100, capped at 40.
    [1, 14],
    [10, 30],
    [20, 35],
    [60, 40],
  ])('the block bonus blends param five into param six (%i)', (level, expected) => {
    expect(Skills.paramWithDiminishing(SkillDamage.HolyShieldSkillId, level)).toBe(expected);
  });

  it('level zero yields no block bonus', () => {
    expect(Skills.paramWithDiminishing(SkillDamage.HolyShieldSkillId, 0)).toBe(0);
  });

  it('a skill that needs a calc string is refused rather than approximated', () => {
    const refused: number[] = [];

    for (let skill = 0; skill < SkillRows.rowCount; ++skill) {
      if (Skills.tryCalcDamage(skill, 10) === null) {
        refused.push(skill);
      }
    }

    // Plenty of skills DO use SrcDam or a calc; the point is that they are refused, and that
    // Holy Shield is not among them.
    expect(refused.length).toBeGreaterThan(0);
    expect(refused).not.toContain(SkillDamage.HolyShieldSkillId);
  });
});
