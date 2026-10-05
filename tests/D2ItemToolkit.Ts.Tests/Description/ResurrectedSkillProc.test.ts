import { describe, expect, it } from 'vitest';
import { GameVariant } from '../../../src/D2ItemToolkit.Ts/src/Data/GameVariant.js';
import { CFormat } from '../../../src/D2ItemToolkit.Ts/src/Description/CFormat.js';
import { ItemDescriptionGenerator } from '../../../src/D2ItemToolkit.Ts/src/Description/ItemDescription.js';
import {
  SkillDescCalc,
  type SkillDescLineRow,
  type SkillItemProc,
  SkillItemProcMaxLines,
} from '../../../src/D2ItemToolkit.Ts/src/Description/SkillDescCalc.js';
import { ItemStatReader } from '../../../src/D2ItemToolkit.Ts/src/Stats/ItemStatReader.js';
import { TooltipEngine } from '../../../src/D2ItemToolkit.Ts/src/Tooltip/TooltipEngine.js';
import { DescStringIds } from '../../../src/D2ItemToolkit.Ts/src/Types.js';
import { FakeStringTable } from '../Fakes.js';

/**
 * D2R descfunc 15 (ITEMSTATDESC_Build 0x1401ec332-0x1401ec8de): the positional (value, level, name)
 * wrapper sub_14060d840, and the `item proc text` arm that only Metamorphosis's Mark of the Bear
 * and Mark of the Wolf reach.
 */
const SkillOnAttack = 195;
const SkillOnKill = 196;
const SkillOnDeath = 197;
const SkillOnHit = 198;
const SkillOnLevelUp = 199;
const SkillOnGetHit = 201;

const MarkOfTheBear = 371;
const MarkOfTheWolf = 372;

const engines = new Map<string, TooltipEngine>();

function engine(variant: GameVariant, language: string, legacy: boolean): TooltipEngine {
  const key = `${String(variant)}/${language}/${String(legacy)}`;
  let found = engines.get(key);
  if (found === undefined) {
    found = TooltipEngine.forVariant(variant, { language, legacyGraphics: legacy });
    engines.set(key, found);
  }

  return found;
}

function line(
  language: string,
  stat: number,
  skill: number,
  level: number,
  value: number,
  variant: GameVariant = GameVariant.ReignOfTheWarlock,
  legacy = false,
): string {
  const data = engine(variant, language, legacy).data;
  const generator = new ItemDescriptionGenerator(
    data.itemStatCost,
    data.strings,
    null,
    data.skills,
    data.classes,
    data.monsterTypes,
    null,
    true,
    data.isResurrected,
  );
  const layer = (skill << 6) | level;
  const lines = generator.describe([[ItemStatReader.packStatKey(layer, stat), value]]);

  expect(lines).toHaveLength(1);
  return (lines[0] as { text: string }).text;
}

describe('D2R descfunc 15 item proc text', () => {
  it.each([GameVariant.ReignOfTheWarlock, GameVariant.Resurrected])(
    'Metamorphosis marks render their proc text in English (%s)',
    variant => {
      expect(line('enUS', SkillOnHit, MarkOfTheBear, 1, 100, variant)).toBe(
        '\nPhysical Damage Received Reduced by 20%\n+25% Attack Speed\nMark of the Bear:\n' +
          'Werebear strikes grant Mark for 180 seconds',
      );
      expect(line('enUS', SkillOnHit, MarkOfTheWolf, 1, 100, variant)).toBe(
        '\nIncrease Maximum Life 40%\n30% Bonus to Attack Rating\nMark of the Wolf:\n' +
          'Werewolf strikes grant Mark for 180 seconds',
      );
    },
  );

  it('Metamorphosis marks render their proc text in German', () => {
    expect(line('deDE', SkillOnHit, MarkOfTheBear, 1, 100)).toBe(
      '\nErlittener physischer Schaden um 20% verringert\n+25% Angriffsgeschwindigkeit\n' +
        'Mal des Bären:\nWerbärangriffe gewähren 180 Sek. lang das Mal.',
    );
    expect(line('deDE', SkillOnHit, MarkOfTheWolf, 1, 100)).toBe(
      '\nErhöht max. Leben 40% \n30% Bonus zu Angriffswert\nMal des Wolfs:\n' +
        'Werwolfangriffe gewähren 180 Sek. lang das Mal.',
    );
  });

  it('Metamorphosis marks take the legacy strings where they exist', () => {
    const r = GameVariant.ReignOfTheWarlock;
    expect(line('enUS', SkillOnHit, MarkOfTheBear, 1, 100, r, true)).toBe(
      '\nDamage Reduced by 20%\n+25% Attack Speed\nMark of the Bear:\n' +
        'Werebear strikes grant Mark for 180 seconds',
    );
    expect(line('enUS', SkillOnHit, MarkOfTheWolf, 1, 100, r, true)).toBe(
      '\nIncrease Maximum Life 40%\n30% Bonus to Attack Rating\nMark of the Wolf:\n' +
        'Werewolf strikes grant Mark for 180 seconds',
    );
    expect(line('deDE', SkillOnHit, MarkOfTheBear, 1, 100, r, true)).toBe(
      '\nSchaden reduziert um 20%\n+25% Angriffsgeschwindigkeit\nMal des Bären:\n' +
        'Werbärangriffe gewähren 180 Sekunden lang das Mal.',
    );
    expect(line('deDE', SkillOnHit, MarkOfTheWolf, 1, 100, r, true)).toBe(
      '\nErhöht max. Leben 40% \n30% Bonus zu Angriffswert\nMal des Wolfs:\n' +
        'Werwolfangriffe gewähren 180 Sekunden lang das Mal.',
    );
  });

  it('the proc text drops the chance and level', () => {
    // Neither shipped row's calcs read the level, and the chance is never printed.
    expect(line('enUS', SkillOnGetHit, MarkOfTheBear, 20, 5)).toBe(
      line('enUS', SkillOnHit, MarkOfTheBear, 1, 100),
    );
  });

  it('a skill without proc text keeps the chance line', () => {
    // Coldkill: hit-skill Ice Blast (45) 10% level 10.
    expect(line('enUS', SkillOnHit, 45, 10, 10)).toBe(
      '10% Chance to cast level 10 Ice Blast on striking',
    );
  });
});

describe('D2R descfunc 15 chance to cast', () => {
  it.each([
    // Coldkill: hit-skill Ice Blast (45) 10% level 10.
    [
      'esES',
      SkillOnHit,
      45,
      10,
      10,
      '10% de probabilidad de lanzar Explosión de hielo de nivel 10 al golpear',
    ],
    [
      'frFR',
      SkillOnHit,
      45,
      10,
      10,
      '10% de chances de lancer Décharge de glace - niv. 10 en touchant',
    ],
    [
      'esMX',
      SkillOnHit,
      45,
      10,
      10,
      '10% de probabilidad de lanzar Resplandor gélido nivel 10 al golpear',
    ],
    ['jaJP', SkillOnHit, 45, 10, 10, '命中時に10%の確率で〈アイスブラスト〉（レベル10）を発動'],
    [
      'ptBR',
      SkillOnHit,
      45,
      10,
      10,
      '10% de chance de lançar Impacto Gélido de nível 10 ao atingir um inimigo',
    ],
    [
      'ruRU',
      SkillOnHit,
      45,
      10,
      10,
      'Вероятность 10% применить умение «Ледяной удар» 10-го уровня при ударе',
    ],
    // Arm of King Leoric: gethit-skill Bone Prison (88) 10% level 2, Bone Spirit (93) 5% level 10.
    [
      'ruRU',
      SkillOnGetHit,
      88,
      2,
      10,
      'Вероятность 10% применить умение «Костяная клетка» 2-го уровня при получении урона',
    ],
    [
      'ruRU',
      SkillOnGetHit,
      93,
      10,
      5,
      'Вероятность 5% применить умение «Костяной дух» 10-го уровня при получении урона',
    ],
    [
      'esES',
      SkillOnGetHit,
      88,
      2,
      10,
      '10% de probabilidad de lanzar Prisión de huesos de nivel 2 al recibir un golpe',
    ],
    // Todesfaelle Flamme: att-skill Fire Ball (47) 10% level 6.
    [
      'frFR',
      SkillOnAttack,
      47,
      6,
      10,
      '10% de chances de lancer Boule de feu - niv. 6 en attaquant',
    ],
    ['jaJP', SkillOnAttack, 47, 6, 10, '攻撃時に10%の確率で〈ファイアボール〉（レベル6）を発動'],
    // Executioner's Justice: kill-skill Decrepify (87) 50% level 6.
    [
      'ptBR',
      SkillOnKill,
      87,
      6,
      50,
      '50% de chance de lançar Decrepitar de nível 6 quando você abate um inimigo',
    ],
    // Medusa's Gaze: death-skill Nova (48) 100% level 44.
    [
      'esMX',
      SkillOnDeath,
      48,
      44,
      100,
      '100% de probabilidad de lanzar Nova nivel 44 cuando mueres',
    ],
    // Rainbow Facet: levelup-skill Nova (48) 100% level 41.
    [
      'ruRU',
      SkillOnLevelUp,
      48,
      41,
      100,
      'Вероятность 100% применить умение «Кольцо молний» 41-го уровня при достижении нового уровня',
    ],
  ] as const)('%s stat %i skill %i formats positionally', (language, stat, skill, lvl, v, want) => {
    expect(line(language, stat, skill, lvl, v)).toBe(want);
  });
});

describe('sub_14060d840', () => {
  it('passes arguments in marker order', () => {
    expect(CFormat.positionalValueLevelName('%0 %1 %2', 7, 3, 'x')).toBe('7 3 x');
    expect(CFormat.positionalValueLevelName('%2 %1 %0', 7, 3, 'x')).toBe('x 3 7');
    expect(CFormat.positionalValueLevelName('%1 %2 %0', 7, 3, 'x')).toBe('3 x 7');
    expect(CFormat.positionalValueLevelName('%0%% %2 %1', 7, 3, 'x')).toBe('7% x 3');
  });

  it('rewrites only the first marker after flags', () => {
    // A flag keeps the conversion open (0x14060d8f3), so "%+0" is the value marker.
    expect(CFormat.positionalValueLevelName('%+0 %1 %2', 7, 3, 'x')).toBe('+7 3 x');
    // "%%0" is a literal percent then a digit, not a marker: no markers at all means plain printf
    // with (value, level, name).
    expect(CFormat.positionalValueLevelName('%%0 %d', 7, 3, 'x')).toBe('%0 7');
  });

  it('writes nothing for a partial set', () => {
    expect(CFormat.positionalValueLevelName('%0 %1', 7, 3, 'x')).toBe('');
    expect(CFormat.positionalValueLevelName('%d %2', 7, 3, 'x')).toBe('');
  });
});

function row(func: number, textA: number, calcA: string, textB = 5382): SkillDescLineRow {
  return { func, textA, textB, calcA, calcB: '' };
}

function proc(count: number, ...lines: SkillDescLineRow[]): SkillItemProc {
  const all: SkillDescLineRow[] = [];
  for (let at = 0; at < SkillItemProcMaxLines; ++at) {
    all.push(lines[at] ?? row(0, 0, ''));
  }

  return {
    textId: 900,
    lineCount: count,
    lines: all,
    params: [25, 20, 0, 0, 0, 0, 0, 0],
    auraLenCalc: '4500',
  };
}

function procStrings(text: string): FakeStringTable {
  return new FakeStringTable()
    .add(900, text)
    .add(901, '%d%% A')
    .add(902, 'B ')
    .add(4252, '')
    .add(4267, ' second')
    .add(4268, ' seconds')
    .add(DescStringIds.Newline, '\n');
}

describe('SkillDescCalc', () => {
  it('a line that writes nothing empties the whole proc line', () => {
    // calcA 0 on line 74 skips the argument (0x1401ec50c); the count check then fails.
    const skill = proc(2, row(74, 901, 'par1'), row(74, 901, '0'));
    expect(SkillDescCalc.formatItemProc(skill, 1, procStrings('%s|%s'))).toBe('');
    expect(SkillDescCalc.formatItemProc(proc(1, row(74, 901, 'par1')), 1, procStrings('%s'))).toBe(
      '25% A',
    );
  });

  it('an unsupported calc is a line not produced', () => {
    expect(SkillDescCalc.formatItemProc(proc(1, row(74, 901, 'edmn')), 1, procStrings('%s'))).toBe(
      '',
    );
  });

  it('a zero count appends the proc text verbatim', () => {
    expect(SkillDescCalc.formatItemProc(proc(0), 1, procStrings('100%% %s'))).toBe('100%% %s');
  });

  it('line 12 writes seconds with tenths and its text B first', () => {
    expect(
      SkillDescCalc.formatItemProc(proc(1, row(12, 4252, '25', 902)), 1, procStrings('%s')),
    ).toBe('B 1 second');
    expect(SkillDescCalc.formatItemProc(proc(1, row(12, 4252, '60')), 1, procStrings('%s'))).toBe(
      '2.4 seconds',
    );
    expect(SkillDescCalc.formatItemProc(proc(1, row(12, 4252, '2')), 1, procStrings('%s'))).toBe(
      '',
    );
  });

  it('the length guard counts the newline before it is stripped', () => {
    // proc 250 bytes + "25% A\n" (6) = 256: the argument is refused, so the line is empty.
    const refused = 'x'.repeat(248) + '%s';
    expect(
      SkillDescCalc.formatItemProc(proc(1, row(74, 901, 'par1')), 1, procStrings(refused)),
    ).toBe('');
    const shorter = 'x'.repeat(247) + '%s';
    expect(
      SkillDescCalc.formatItemProc(proc(1, row(74, 901, 'par1')), 1, procStrings(shorter)),
    ).toBe('x'.repeat(247) + '25% A');
  });

  it('the calc subset reads the skills row', () => {
    const skill = proc(0);
    skill.params = [10, 3, 0, 0, 0, 0, 0, 0];
    expect(SkillDescCalc.tryEvaluate('ln12', skill, 5)).toBe(22);
    expect(SkillDescCalc.tryEvaluate('ln12', skill, 0)).toBe(0);
    expect(SkillDescCalc.tryEvaluate('lvl', skill, 5)).toBe(5);
    expect(SkillDescCalc.tryEvaluate('len', skill, 5)).toBe(4500);
    expect(SkillDescCalc.tryEvaluate('', skill, 5)).toBe(0);
    expect(SkillDescCalc.tryEvaluate('par1+1', skill, 5)).toBeNull();
    expect(SkillDescCalc.tryEvaluate('edmn', skill, 5)).toBeNull();
  });
});
