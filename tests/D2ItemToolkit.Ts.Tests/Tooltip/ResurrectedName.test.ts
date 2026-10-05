import { describe, expect, it } from 'vitest';
import type { TxtFile } from '../../../src/D2ItemToolkit.Ts/src/Data/TxtFile.js';
import { GameVariant } from '../../../src/D2ItemToolkit.Ts/src/Data/GameVariant.js';
import { ItemRecordFlags } from '../../../src/D2ItemToolkit.Ts/src/Stats/ItemRecord.js';
import { createUnit } from '../../../src/D2ItemToolkit.Ts/src/Stats/Unit.js';
import { ItemTooltipSection } from '../../../src/D2ItemToolkit.Ts/src/Tooltip/ItemTooltip.js';
import { ItemQualityNo } from '../../../src/D2ItemToolkit.Ts/src/Tooltip/ItemNameBuilder.js';
import { TooltipEngine } from '../../../src/D2ItemToolkit.Ts/src/Tooltip/TooltipEngine.js';

/**
 * ITEMS_GetName 0x140157740 per locale: the grammar header of sub_140251970 and the possessive of
 * sub_140478a70. Several expectations are the game's own odd output (raw gender tags, the Polish
 * trailing space) — reproduced, not fixed.
 */
const engines = new Map<string, TooltipEngine>();

function engineFor(language: string): TooltipEngine {
  let engine = engines.get(language);
  if (engine === undefined) {
    engine = TooltipEngine.forVariant(GameVariant.ReignOfTheWarlock, { language });
    engines.set(language, engine);
  }

  return engine;
}

interface NameParts {
  prefix?: string;
  suffix?: string;
  owner?: string;
  rarePrefix?: string;
  rareSuffix?: string;
}

function name(language: string, code: string, quality: number, parts: NameParts = {}): string {
  const engine = engineFor(language);
  const data = engine.data;
  const magicSuffix = data.magicSuffix as TxtFile;
  const magicPrefix = data.magicPrefix as TxtFile;
  const rareSuffix = data.rareSuffix as TxtFile;
  const rarePrefix = data.rarePrefix as TxtFile;

  const item = createUnit({
    unitType: 4,
    classId: engine.items.classIdForCode(code),
    quality,
    itemFlags:
      ItemRecordFlags.Identified |
      (parts.owner !== undefined ? ItemRecordFlags.Personalized : ItemRecordFlags.None),
    playerName: parts.owner ?? '',
    magicPrefix: [
      parts.prefix === undefined
        ? 0
        : magicSuffix.rowCount + 1 + magicPrefix.findRow('Name', parts.prefix),
      0,
      0,
    ],
    magicSuffix: [
      parts.suffix === undefined ? 0 : 1 + magicSuffix.findRow('Name', parts.suffix),
      0,
      0,
    ],
    rarePrefix:
      parts.rarePrefix === undefined
        ? 0
        : rareSuffix.rowCount + 1 + rarePrefix.findRow('name', parts.rarePrefix),
    rareSuffix:
      parts.rareSuffix === undefined ? 0 : 1 + rareSuffix.findRow('name', parts.rareSuffix),
  });

  let result = '';
  for (const line of engine.render(item).lines) {
    if (line.section === ItemTooltipSection.ItemName) {
      const text = (line.text ?? '').replaceAll('\n', '');
      result = result.length === 0 ? text : text + ' / ' + result;
    }
  }

  return result;
}

describe('D2R item names per locale', () => {
  it.each([
    ['deDE', 'gezacktes Kurzschwert der Dornen'],
    ['frFR', 'Épée courte irrégulière d’épines'],
    ['esES', 'Espada corta dentellada de espinas'],
    ['plPL', 'Zębaty Krótki Miecz Cierni'],
    ['ruRU', 'зазубренный короткий меч с шипами'],
    ['koKR', '바늘의 톱날의 숏소드'],
    ['jaJP', '鋸刃もつ荊棘のショートソード'],
  ])('two-affix magic names agree with the gendered adjective (%s)', (language, expected) => {
    expect(
      name(language, 'ssd', ItemQualityNo.Magic, { prefix: 'Jagged', suffix: 'of Thorns' }),
    ).toBe(expected);
  });

  it('the noun tag picks the adjective variant', () => {
    expect(name('deDE', 'crn', ItemQualityNo.Magic, { prefix: 'Sturdy' })).toBe('robuste Krone');
    expect(name('deDE', 'hlm', ItemQualityNo.Magic, { prefix: 'Sturdy' })).toBe('robuster Helm');
    expect(name('plPL', 'crn', ItemQualityNo.Magic, { prefix: 'Sturdy' })).toBe('Masywna Korona');
  });

  it('Spanish suffix-only names print the header defect literally', () => {
    // esES 26776 is "a0n1:%1 %0": the BASE becomes the adjective, so the suffix leads and a non-[ms]
    // base keeps its tag.
    expect(name('esES', 'crn', ItemQualityNo.Magic, { suffix: 'of the Whale' })).toBe(
      'de la ballena [fs]Corona',
    );
    expect(name('esES', 'ghm', ItemQualityNo.Magic, { suffix: 'of the Whale' })).toBe(
      'de la ballena Gran yelmo',
    );
    expect(name('esMX', 'crn', ItemQualityNo.Magic, { suffix: 'of the Whale' })).toBe(
      'Corona de la ballena',
    );
  });

  it("an adjective without the noun's tag prints whole", () => {
    expect(name('plPL', 'scp', ItemQualityNo.Magic, { prefix: 'Virulent' })).toBe(
      '[ms]Zakażający[fs]Zakażająca[ns]Zakażające[p]Zakażające Berło',
    );
  });

  it('a trailing space in an argument collapses the next one', () => {
    expect(
      name('esMX', 'lbt', ItemQualityNo.Magic, { prefix: 'Screaming', suffix: 'of Thorns' }),
    ).toBe('Botas estridentes de espinas');
    expect(name('esMX', 'lbt', ItemQualityNo.Magic, { prefix: 'Screaming' })).toBe(
      'Botas estridentes ',
    );
  });

  it.each([
    ['enUS', 'Hans', "Hans' Crown"],
    ['enUS', 'Anna', "Anna's Crown"],
    ['deDE', 'Hans', "Hans' Krone"],
    ['deDE', 'Anna', 'Annas Krone'],
    ['frFR', 'Hans', 'Couronne de Hans'],
    ['frFR', 'anna', "Couronne d'anna"],
    ['esES', 'Hans', 'Corona de Hans'],
    ['itIT', 'Hans', 'Corona di Hans'],
    ['plPL', 'Hans', 'Korona(Hans) '],
    ['ruRU', 'Hans', '(Hans) корона'],
    ['jaJP', 'Hans', 'Hans冠'],
  ])('the possessive follows the language (%s, %s)', (language, owner, expected) => {
    expect(name(language, 'crn', ItemQualityNo.Normal, { owner })).toBe(expected);
  });

  it('a rare affix line is personalised alone', () => {
    expect(
      name('deDE', 'crn', ItemQualityNo.Rare, {
        owner: 'Anna',
        rarePrefix: 'Beast',
        rareSuffix: 'bite',
      }),
    ).toBe('Krone / Annas Bestien - Biss');
    expect(
      name('plPL', 'crn', ItemQualityNo.Rare, {
        owner: 'Anna',
        rarePrefix: 'Beast',
        rareSuffix: 'bite',
      }),
    ).toBe('Korona / Bestialskie Ukąszenie(Anna) ');
  });

  it('English has no grammar header and no stray space', () => {
    expect(name('enUS', 'crn', ItemQualityNo.Magic, { prefix: 'Sturdy' })).toBe('Sturdy Crown');
    expect(name('enUS', 'crn', ItemQualityNo.Magic, { suffix: 'of the Whale' })).toBe(
      'Crown of the Whale',
    );
    expect(name('enUS', 'hgl', ItemQualityNo.Superior)).toBe('Superior Gauntlets');
  });
});

describe('a russian plural prefix that ends in a newline', () => {
  it.each([
    ['Corosive', 'clw', ' когти', 'коррозийные'],
    ['Spiritual', 'dr3', ' бараньи рога', 'возвышенные'],
  ])('%s on %s draws the name on two rows', (prefix, code, top, bottom) => {
    // ruRU HD `[pl]коррозийные\n`: the a0n1 header takes [pl] to the end, newline included, and the
    // collapse rule only eats a trailing SPACE, so the base becomes its own row. Rows draw in
    // reverse, so it lands ABOVE the prefix, with the leading space kept.
    const engine = engineFor('ruRU');
    const magicSuffix = engine.data.magicSuffix as TxtFile;
    const magicPrefix = engine.data.magicPrefix as TxtFile;
    const item = createUnit({
      unitType: 4,
      classId: engine.items.classIdForCode(code),
      quality: ItemQualityNo.Magic,
      itemFlags: ItemRecordFlags.Identified,
      magicPrefix: [magicSuffix.rowCount + 1 + magicPrefix.findRow('Name', prefix), 0, 0],
    });

    const lines = engine.render(item).text.split('\n');
    expect(lines[0]).toBe(top);
    expect(lines[1]).toBe(bottom);
  });
});
