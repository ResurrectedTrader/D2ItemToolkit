import { describe, expect, it } from 'vitest';
import type { TxtFile } from '../../../src/D2ItemToolkit.Ts/src/Data/TxtFile.js';
import { GameVariant } from '../../../src/D2ItemToolkit.Ts/src/Data/GameVariant.js';
import { ItemRecordFlags } from '../../../src/D2ItemToolkit.Ts/src/Stats/ItemRecord.js';
import { ItemStatListFlags } from '../../../src/D2ItemToolkit.Ts/src/Stats/ItemStatReader.js';
import {
  createUnit,
  type Unit,
  type UnitStatList,
} from '../../../src/D2ItemToolkit.Ts/src/Stats/Unit.js';
import { ItemQualityNo } from '../../../src/D2ItemToolkit.Ts/src/Tooltip/ItemNameBuilder.js';
import {
  TooltipEngine,
  type TooltipOptions,
} from '../../../src/D2ItemToolkit.Ts/src/Tooltip/TooltipEngine.js';

/**
 * ITEMS_GetFullDescription 0x1401d5200 and its writers, on the Reign of the Warlock tables. Every
 * expected string is the traced D2R behaviour written out by hand — "~" stands for the colour
 * escape so the markers stay readable.
 */
const Engine = TooltipEngine.forVariant(GameVariant.ReignOfTheWarlock);

function colored(item: Unit, viewer: Unit | null = null, options: TooltipOptions = {}): string {
  return Engine.render(item, viewer, options).coloredText.replaceAll('ÿc', '~');
}

function list(flags: number, ...pairs: [number, number][]): UnitStatList {
  return { stateNo: 0, flags, stats: pairs.map(([id, value]) => ({ id, value })) };
}

function item(code: string, quality: number = ItemQualityNo.Normal): Unit {
  const classId = Engine.items.classIdForCode(code);
  expect(classId, code).toBeGreaterThanOrEqual(0);
  return createUnit({
    unitType: 4,
    classId,
    quality,
    itemFlags: ItemRecordFlags.Identified,
    itemLevel: 50,
  });
}

function player(classId: number, strength = 100, dexterity = 100, level = 60): Unit {
  return createUnit({
    unitType: 0,
    classId,
    statsLists: [list(0, [0, strength], [2, dexterity], [12, level])],
  });
}

function magicPrefix(name: string): number {
  const prefixes = Engine.data.magicPrefix as TxtFile;
  for (let row = 0; row < prefixes.rowCount; ++row) {
    if (prefixes.getString(row, 'Name') === name) {
      // 1-based over [MagicSuffix][MagicPrefix][automagic].
      return (Engine.data.magicSuffix as TxtFile).rowCount + row + 1;
    }
  }

  return -1;
}

describe('the D2R tooltip', () => {
  it('the variants are distinct engines', () => {
    expect(TooltipEngine.embedded.variant).toBe(GameVariant.Lod114d);
    expect(TooltipEngine.forVariant(GameVariant.Resurrected).variant).toBe(GameVariant.Resurrected);
    expect(Engine.variant).toBe(GameVariant.ReignOfTheWarlock);
  });

  it('a one-prefix magic name has no trailing space and durability no marker', () => {
    // ItemNameMagicFormatPrefixOnly (0x1401587ff); ITEMDESC_Durability formats (cur, max) with no
    // colour-3 on an enhanced max (0x1401d051b).
    const shield = item('lrg', ItemQualityNo.Magic);
    shield.magicPrefix = [magicPrefix('Sturdy'), 0, 0];
    shield.statsLists.push(list(ItemStatListFlags.Extended, [31, 120], [72, 40], [73, 62]));
    shield.statsLists.push(list(ItemStatListFlags.Magic, [16, 30], [75, 20]));

    expect(colored(shield, player(3))).toBe(
      '~3Sturdy Large Shield\n' +
        '~0Defense: ~3156\n' +
        '~0Chance to Block: ~330%\n' +
        '~0Smite Damage: 2 to 4\n' +
        '~0Durability: 40 of 74\n' +
        '~0Required Strength: 34\n' +
        '~0Required Level: 3\n' +
        '~3+30% Enhanced Defense\n' +
        '~3Increase Maximum Durability 20%',
    );
  });

  it('a belt shows its extra slots over the default belt', () => {
    // belts row 4 (light belt, 8) less row 2 (default, 4), "%+d".
    const belt = item('vbl');
    belt.statsLists.push(list(ItemStatListFlags.Extended, [31, 4], [72, 12], [73, 12]));

    expect(colored(belt, player(0))).toBe(
      '~0Light Belt\n~0Defense: 4\n~0Belt Size: +4 Slots\n~0Durability: 12 of 12',
    );
  });

  it('a grimoire names its class and reddens for another', () => {
    const grimoire = item('wa1');
    grimoire.statsLists.push(list(ItemStatListFlags.Extended, [31, 10], [72, 20], [73, 20]));

    expect(colored(grimoire, player(1))).toContain('~1(Warlock Only)\n');
    expect(colored(grimoire, player(7))).toContain('~0(Warlock Only)\n');
  });

  it('quantity carries the stack size in the HD text only', () => {
    const arrows = item('aqv');
    arrows.statsLists.push(list(ItemStatListFlags.Extended, [70, 250]));

    expect(colored(arrows, player(0))).toBe('~0Arrows\n~0Quantity: 250 of 500');

    const legacy = TooltipEngine.forVariant(GameVariant.ReignOfTheWarlock, {
      legacyGraphics: true,
    });
    expect(legacy.render(arrows, player(0)).coloredText.replaceAll('ÿc', '~')).toBe(
      '~0Arrows\n~0Quantity: 250',
    );
  });

  it('runes and event items take the new name colours', () => {
    expect(colored(item('r01'), player(0)).startsWith('~JEl Rune\n')).toBe(true);
    expect(colored(item('pk1'), player(0))).toBe('~LKey of Terror');
  });

  it('the cube speaks through its spelldesc with quest colour 14', () => {
    // No QuestUsage section: the usage line is mode-1 spelldesc with spelldesccolor 4.
    expect(colored(item('box'), player(0))).toBe('~0~>Horadric Cube\n~0~4Right Click to Open');
  });

  it('potions scale by the charstats percent and rejuvenation uses mode 4', () => {
    expect(colored(item('hp5'), player(7))).toBe('~0Super Healing Potion\n~0Points: 320');
    expect(colored(item('hp5'), player(4))).toBe('~0Super Healing Potion\n~0Points: 640');
    expect(colored(item('rvl'), player(7))).toBe(
      '~0Full Rejuvenation Potion\n~0Heals 100% Life and Mana',
    );
  });

  it('a throw line marks only the first number and only when modified', () => {
    const javelin = item('jav');
    javelin.statsLists.push(
      list(
        ItemStatListFlags.Extended,
        [21, 6],
        [22, 14],
        [159, 6],
        [160, 14],
        [70, 40],
        [72, 0],
        [73, 0],
      ),
    );
    javelin.statsLists.push(list(ItemStatListFlags.Magic, [160, 5]));

    // 1.14d marked both numbers ("~0Throw Damage: ~36 to ~319"); D2R splices one marker. The line
    // carries its own ~0, so the display needs no re-anchor in front of it.
    expect(colored(javelin, player(0))).toContain('\n~0Throw Damage: ~36 to 19\n');
  });

  it('a worldstone shard is red unless desecrated zones run in hell', () => {
    const shard = item('xa1');

    // EventItem gives the section colour 28; the failed condition prepends red inside it.
    expect(colored(shard, player(0)).startsWith('~L~1')).toBe(true);

    const text = colored(shard, player(0), { difficulty: 2, desecratedZonesEnabled: true });
    expect(text.startsWith('~L')).toBe(true);
    expect(text.startsWith('~L~1')).toBe(false);
  });

  it('keeps the HD ids the tables were loaded with under legacy text', () => {
    // 0x140063844 loads the txts before 0x14061d604 ever sets g_IsRunningLecgacyGfx, so
    // spelldescstr is the HD id 27574, which the legacy table refused; the miss returns the HD
    // table's strMissingString (loaded last, 0x140477ac5).
    const shard = item('xa1');
    const render = (language: string): string =>
      TooltipEngine.forVariant(GameVariant.ReignOfTheWarlock, { legacyGraphics: true, language })
        .render(shard, player(0))
        .coloredText.replaceAll('ÿc', '~');

    expect(render('enUS')).toBe('~L~1Western Worldstone Shard\n~0~8Missing string');
    expect(render('frFR')).toBe('~L~1Fragment occidental de la pierre-monde\n~0~8Ligne manquante');
    expect(render('ruRU')).toBe('~L~1Western Worldstone Shard\n~0~8Отсутствует строка');
    expect(colored(shard, player(0))).toBe(
      '~L~1Western Worldstone Shard\n~0~8Right Click to terrorize Act 1',
    );
  });

  it.each([
    ['enUS', 'Missing string'],
    ['deDE', 'Missing string'],
    ['esES', 'Missing string'],
    ['frFR', 'Ligne manquante'],
    ['itIT', 'Stringa mancante'],
    ['koKR', '없는 문자열'],
    ['plPL', 'Missing string'],
    ['ruRU', 'Отсутствует строка'],
    ['zhCN', '丢失字符串'],
    ['zhTW', 'Missing string'],
    ['esMX', 'Texto faltante'],
    ['jaJP', '文字列が見つかりません'],
    ['ptBR', 'String ausente'],
  ])('a %s legacy miss shows the HD table text', (language, missing) => {
    // The HD load never forces enUS (only the legacy one does, 0x140476dbe), so the fallback is
    // the HD strMissingString in the locale's own column.
    const legacy = TooltipEngine.forVariant(GameVariant.ReignOfTheWarlock, {
      legacyGraphics: true,
      language,
    });
    for (const code of ['xa1', 'xa2', 'xa3', 'xa4', 'xa5']) {
      const text = legacy.render(item(code), player(0)).coloredText.replaceAll('ÿc', '~');
      expect(text.endsWith('\n~0~8' + missing), `${code}: ${text}`).toBe(true);
    }
  });

  it('names a legacy skill by its HD id', () => {
    // skilldesc `str name` BaneHexName is HD id 27448; legacy holds 27448 as UseTerrorTokenAct2,
    // so the legacy table refused BaneHexName (0x140476f85).
    const legacy = TooltipEngine.forVariant(GameVariant.ReignOfTheWarlock, {
      legacyGraphics: true,
    });
    const ring = item('rin', ItemQualityNo.Magic);
    ring.statsLists.push({
      stateNo: 0,
      flags: ItemStatListFlags.Magic,
      stats: [{ id: 107, value: 3, layer: 385 }],
    });
    expect(legacy.render(ring, player(7)).coloredText.replaceAll('ÿc', '~')).toContain(
      '~3+3 to Right Click to terrorize Act 2 (Warlock Only)',
    );
  });
});

describe('the viewer-attached ops 4/5', () => {
  it('defense and damage include the viewer level-scaled stats', () => {
    // ITEMDESC_Defense 0x1401d1d00 / ITEMDESC_GetMinMaxStats 0x1401d07f0 attach the item to the
    // viewer first; ops 4/5 add level * value >> 3 to the target (0x14020c57d).
    const armor = item('qui', ItemQualityNo.Magic);
    armor.magicPrefix = [magicPrefix('Paleocene'), 0, 0];
    armor.statsLists.push(list(ItemStatListFlags.Extended, [31, 10], [72, 20], [73, 20]));
    armor.statsLists.push(list(ItemStatListFlags.Magic, [214, 24]));

    expect(colored(armor, player(0, 100, 100, 40))).toBe(
      '~3Faithful Quilted Armor\n' +
        '~0Defense: ~3130\n' +
        '~0Durability: 20 of 20\n' +
        '~0Required Strength: 12\n' +
        '~0Required Level: 22\n' +
        '~3+120 Defense (Based on Character Level)',
    );

    const axe = item('hax', ItemQualityNo.Magic);
    axe.magicPrefix = [magicPrefix('Gritty'), 0, 0];
    axe.statsLists.push(list(ItemStatListFlags.Extended, [21, 3], [22, 6], [72, 28], [73, 28]));
    axe.statsLists.push(list(ItemStatListFlags.Magic, [218, 6]));

    expect(colored(axe, player(0, 100, 100, 40))).toBe(
      '~3Grinding Hand Axe\n' +
        '~0One-Hand Damage: ~33 to 36\n' +
        '~0Durability: 28 of 28\n' +
        '~0Required Level: 37\n' +
        '~0Axe Class - Fast Attack Speed\n' +
        '~3+30 to Maximum Damage (Based on Character Level)',
    );
  });

  it('op 5 takes a percent of the pre-op defense', () => {
    // ac%/lvl 12 at level 40: (40 * 12) >> 3 = 60%, of the pre-op 10 = 6.
    const armor = item('qui');
    armor.statsLists.push(list(ItemStatListFlags.Extended, [31, 10], [72, 20], [73, 20]));
    armor.statsLists.push(list(ItemStatListFlags.Magic, [215, 12]));

    expect(colored(armor, player(0, 100, 100, 40))).toContain('~0Defense: ~316\n');
  });
});
