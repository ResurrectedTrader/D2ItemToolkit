import { describe, expect, it } from 'vitest';
import { CFormat } from '../../../src/D2ItemToolkit.Ts/src/Description/CFormat.js';
import type { TxtFile } from '../../../src/D2ItemToolkit.Ts/src/Data/TxtFile.js';
import { GameVariant } from '../../../src/D2ItemToolkit.Ts/src/Data/GameVariant.js';
import { ItemRecordFlags } from '../../../src/D2ItemToolkit.Ts/src/Stats/ItemRecord.js';
import { ItemStatListFlags } from '../../../src/D2ItemToolkit.Ts/src/Stats/ItemStatReader.js';
import { createUnit, type Unit } from '../../../src/D2ItemToolkit.Ts/src/Stats/Unit.js';
import { ItemDamageKind } from '../../../src/D2ItemToolkit.Ts/src/Tooltip/ItemDamage.js';
import { ItemQualityNo } from '../../../src/D2ItemToolkit.Ts/src/Tooltip/ItemNameBuilder.js';
import { TooltipEngine } from '../../../src/D2ItemToolkit.Ts/src/Tooltip/TooltipEngine.js';

/**
 * The second verification round's findings: the positional descfunc wrappers, the localised
 * RuneQuote at both ends of the rune letters, and ITEMDESC_GetMinMaxStats' MAX(min, max). The twin
 * of ResurrectedPositionalTests.cs.
 */
const engines = new Map<string, TooltipEngine>();

function engine(language = 'enUS', legacy = false): TooltipEngine {
  const key = language + String(legacy);
  let found = engines.get(key);
  if (found === undefined) {
    found = TooltipEngine.forVariant(GameVariant.ReignOfTheWarlock, {
      language,
      legacyGraphics: legacy,
    });
    engines.set(key, found);
  }

  return found;
}

function viewer(classId: number): Unit {
  return createUnit({
    unitType: 0,
    classId,
    statsLists: [
      {
        stateNo: 0,
        flags: 0,
        stats: [
          { id: 0, value: 200 },
          { id: 2, value: 200 },
          { id: 12, value: 90 },
        ],
      },
    ],
  });
}

function lines(e: TooltipEngine, item: Unit, viewerClass = 1): string[] {
  return e.render(item, viewer(viewerClass)).text.split('\n');
}

function modifier(language: string, stat: number, layer: number, value: number): string[] {
  const e = engine(language);
  const ring = createUnit({
    unitType: 4,
    classId: e.items.classIdForCode('rin'),
    quality: ItemQualityNo.Rare,
    itemFlags: ItemRecordFlags.Identified,
    statsLists: [
      { stateNo: 0, flags: ItemStatListFlags.Magic, stats: [{ id: stat, value, layer }] },
    ],
  });
  return lines(e, ring);
}

function skill(name: string): number {
  return (engine().data.skillRows as TxtFile).findRow('skill', name);
}

describe('the positional wrappers', () => {
  it.each([
    ['%+0 to %1', '+3 to Fire Ball'], // a flag keeps the conversion open
    ['%1 gets %0', 'Fire Ball gets 3'], // arguments follow marker order
    ['%0 only', ''], // %0 without %1 writes nothing
    ['only %1', ''], // %1 without %0 writes nothing
    ['plain %d %s', 'plain 3 Fire Ball'], // no markers: plain printf
    // only the first copy is rewritten; the second is a "%0" flag still open at the NUL, which writes
    // nothing (0x140b477d5)
    ['%0%% %1 %0', '3% Fire Ball '],
  ])('the two-argument wrapper on %j', (format, expected) => {
    expect(CFormat.positionalWrapper(format, 'ds', false, 3, 'Fire Ball')).toBe(expected);
  });

  it.each([
    ['%1 %0 (%2/%3)', 'Teleport 1 (20/30)'], // only %0 and %1 may swap
    ['%0 %2 %1 %3', ''], // %2 must follow %0 and %1
    ['%0 %1 %3 %2', ''], // %3 must follow %2
  ])('the charges wrapper insists on its order: %j', (format, expected) => {
    expect(CFormat.positionalWrapper(format, 'dsdd', true, 1, 'Teleport', 20, 30)).toBe(expected);
  });

  it('an aura in Spanish', () => {
    expect(modifier('esES', 151, skill('Meditation'), 12)).toContain(
      'Aura Meditación de nivel 12 si está equipado',
    );
  });

  it('charges in Spanish and English', () => {
    const layer = (skill('Teleport') << 6) | 1;
    const charges = 20 | (20 << 8);
    expect(modifier('esES', 204, layer, charges)).toContain(
      'Teletransporte de nivel 1 (20/20 cargas)',
    );
    expect(modifier('enUS', 204, layer, charges)).toContain('Level 1 Teleport (20/20 Charges)');
  });

  it('an oskill and a class skill in Japanese', () => {
    expect(modifier('jaJP', 97, skill('Teleport'), 1)).toContain('〈テレポート〉向上（+1）');
    expect(modifier('jaJP', 107, skill('Fire Ball'), 3)).toContain(
      '〈ファイアボール〉スキル向上（+3）（ソーサレス専用）',
    );
  });

  it('self repair in Chinese', () => {
    expect(modifier('zhCN', 252, 0, 10)).toContain('每 10 秒修复 1 点耐久度');
    expect(modifier('enUS', 252, 0, 10)).toContain('Repairs 1 durability in 10 seconds');
  });
});

describe('RuneQuote', () => {
  function runeLetters(language: string, legacy: boolean): string | undefined {
    const e = engine(language, legacy);
    const helm = createUnit({
      unitType: 4,
      classId: e.items.classIdForCode('cap'),
      itemFlags: ItemRecordFlags.Identified | ItemRecordFlags.Socketed,
      statsLists: [
        {
          stateNo: 0,
          flags: ItemStatListFlags.Extended,
          stats: [
            { id: 72, value: 30 },
            { id: 73, value: 30 },
            { id: 194, value: 1 },
          ],
        },
      ],
      items: [
        createUnit({
          unitType: 4,
          classId: e.items.classIdForCode('r30'),
          itemFlags: ItemRecordFlags.Identified,
        }),
      ],
    });
    return lines(e, helm)[1];
  }

  it.each([
    ['enUS', false, "'Ber'"],
    ['ptBR', false, '"Ber"'],
    ['frFR', false, 'Ber'],
    ['zhTW', true, '貝'], // legacy zhTW: empty quotes, localised letters
  ] as const)('%s legacy=%s closes with the same localised quote', (language, legacy, expected) => {
    expect(runeLetters(language, legacy)).toBe(expected);
  });
});

describe('MAX(min, max)', () => {
  function weapon(code: string, ...statValue: number[]): Unit {
    const stats = [
      { id: 72, value: 30 },
      { id: 73, value: 30 },
    ];
    for (let at = 0; at < statValue.length; at += 2) {
      stats.push({ id: statValue[at] ?? 0, value: statValue[at + 1] ?? 0 });
    }

    return createUnit({
      unitType: 4,
      classId: engine().items.classIdForCode(code),
      itemFlags: ItemRecordFlags.Identified,
      statsLists: [{ stateNo: 0, flags: ItemStatListFlags.Extended, stats }],
    });
  }

  it('a throw minimum above its maximum shows the minimum twice', () => {
    const rendered = lines(engine(), weapon('tkf', 21, 10, 22, 11, 159, 12, 160, 9));
    expect(rendered).toContain('ÿc0Throw Damage: 12 to 12');
    expect(rendered).toContain('One-Hand Damage: 10 to 11');
  });

  it("a barbarian's one-hand line takes the maximum too", () => {
    const rendered = lines(engine(), weapon('2hs', 21, 10, 22, 9, 23, 14, 24, 20), 4);
    expect(rendered).toContain('ÿc0One-Hand Damage: 10 to 10');
    expect(rendered).toContain('ÿc0Two-Hand Damage: 14 to 20');
  });

  it('the damage api reports the same maximum', () => {
    const knife = weapon('tkf', 21, 10, 22, 11, 159, 12, 160, 9);
    const thrown = engine()
      .damage(knife, viewer(1))
      .lines.filter(d => d.kind === ItemDamageKind.Throw);
    expect(thrown).toHaveLength(1);
    expect(thrown[0]?.max).toBe(12);
  });
});

describe('a conversion still open at the NUL (sub_140b47700)', () => {
  function damageResistUnique(e: TooltipEngine, code: string, name: string, value: number): Unit {
    return createUnit({
      unitType: 4,
      classId: e.items.classIdForCode(code),
      quality: ItemQualityNo.Unique,
      fileIndex: (e.data.uniqueItems as TxtFile).findRow('index', name),
      itemFlags: ItemRecordFlags.Identified,
      statsLists: [
        {
          stateNo: 0,
          flags: ItemStatListFlags.Extended,
          stats: [
            { id: 31, value: 1000 },
            { id: 72, value: 60 },
            { id: 73, value: 60 },
          ],
        },
        { stateNo: 0, flags: ItemStatListFlags.Magic, stats: [{ id: 36, value }] },
      ],
    });
  }

  it('a trailing percent left open is dropped in French', () => {
    // frFR "…réduits de %d% %": "% " + 0xC2 prints 0xC2, 0xA0 follows, and the final "%" is still
    // open at the NUL (0x140b477d5), so nothing of it is written.
    const e = engine('frFR');
    const rendered = lines(e, damageResistUnique(e, 'uar', 'Shaftstop', 30));
    expect(rendered).toContain('Dégâts physiques subis réduits de 30\u00a0');
    expect(rendered).not.toContain('Dégâts physiques subis réduits de 30\u00a0%');
  });

  it('the negative French text drops it in legacy too', () => {
    // strings-legacy has no ...Negative, so Bone Break falls back to the HD text.
    const e = engine('frFR', true);
    expect(lines(e, damageResistUnique(e, 'cm3', 'Bone Break', -15))).toContain(
      'Dégâts physiques subis augmentés de 15\u00a0',
    );
  });

  it.each([
    ['abc %', 'abc '],
    ['abc %-5', 'abc '],
    ['%d%\u00a0%', '7\u00a0'],
  ])('%j writes nothing of its open conversion', (format, expected) => {
    expect(CFormat.sprintf(format, 7)).toBe(expected);
  });
});
