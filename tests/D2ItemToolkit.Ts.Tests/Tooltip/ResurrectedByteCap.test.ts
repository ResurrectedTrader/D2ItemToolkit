import { describe, expect, it } from 'vitest';
import type { TxtFile } from '../../../src/D2ItemToolkit.Ts/src/Data/TxtFile.js';
import { GameVariant } from '../../../src/D2ItemToolkit.Ts/src/Data/GameVariant.js';
import { ItemRecordFlags } from '../../../src/D2ItemToolkit.Ts/src/Stats/ItemRecord.js';
import { ItemStatListFlags } from '../../../src/D2ItemToolkit.Ts/src/Stats/ItemStatReader.js';
import {
  createUnit,
  unitFromJson,
  type Unit,
  type UnitStat,
} from '../../../src/D2ItemToolkit.Ts/src/Stats/Unit.js';
import { ItemQualityNo } from '../../../src/D2ItemToolkit.Ts/src/Tooltip/ItemNameBuilder.js';
import { TooltipEngine } from '../../../src/D2ItemToolkit.Ts/src/Tooltip/TooltipEngine.js';

/**
 * D2R's 1023-byte buffers: the modifier walk's strlcat (sub_1401E8BE0, 0x1401e91f6) and the colour
 * wrap (D2RGFX_D2R_Text_ApplyColorCode 0x14008c9f0) leave 1019 bytes of modifier text. The twin of
 * ResurrectedByteCapTests.cs.
 */
const Russian = TooltipEngine.forVariant(GameVariant.ReignOfTheWarlock, { language: 'ruRU' });
const Marker = 'ÿc';

function leoric(socketed: boolean): Unit {
  const uniques = Russian.data.uniqueItems as TxtFile;
  const row = uniques.findRow('index', 'Arm of King Leoric');
  const item = createUnit({
    unitType: 4,
    quality: ItemQualityNo.Unique,
    fileIndex: row,
    classId: Russian.items.classIdForCode(uniques.getString(row, 'code').trim()),
    itemFlags: ItemRecordFlags.Identified | (socketed ? ItemRecordFlags.Socketed : 0),
    itemLevel: 85,
  });

  // Every rolled stat at its high end, as the game would hold a max roll.
  const mods: UnitStat[] = Russian.ranges(item).stats.map(range => ({
    id: range.statId,
    value: range.high,
    layer: range.layer,
  }));

  item.statsLists.push(
    {
      stateNo: 0,
      flags: ItemStatListFlags.Extended,
      stats: [
        { id: 21, value: 10 },
        { id: 22, value: 22 },
        { id: 72, value: 50 },
        { id: 73, value: 50 },
        { id: 194, value: socketed ? 1 : 0 },
      ],
    },
    { stateNo: 0, flags: ItemStatListFlags.Magic, stats: mods },
  );

  if (socketed) {
    item.items.push(
      createUnit({
        unitType: 4,
        classId: Russian.items.classIdForCode('jew'),
        quality: ItemQualityNo.Magic,
        itemFlags: ItemRecordFlags.Identified,
        statsLists: [
          { stateNo: 0, flags: ItemStatListFlags.Magic, stats: [{ id: 93, value: 15 }] },
        ],
      }),
    );
  }

  return item;
}

function necromancer(): Unit {
  return createUnit({
    unitType: 0,
    classId: 2,
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

const whole = 'Вероятность 5% применить умение «Костяной дух» 10-го уровня при получении урона';

describe('the D2R modifier byte cap', () => {
  it('unsocketed, the block is whole', () => {
    expect(Russian.render(leoric(false), necromancer()).text.split('\n')).toContain(whole);
  });

  it('a jewel pushes the top modifier row past the cap and glues the next buffer on', () => {
    const lines = Russian.render(leoric(true), necromancer()).text.split('\n');
    expect(lines).not.toContain(whole);
    expect(
      lines.filter(line => line.startsWith(whole.substring(0, whole.length - 1) + Marker)),
    ).toHaveLength(1);
  });

  it('the glued row is exact', () => {
    // 4 + 1022 bytes: the wrap drops the last three, "а\n", of the top modifier row.
    expect(Russian.render(leoric(true), necromancer()).text.split('\n')).toContain(
      whole.substring(0, whole.length - 1) +
        Marker +
        '0Посох – ' +
        Marker +
        '3высокая скорость атаки',
    );
  });

  it('English is far under the cap', () => {
    const english = TooltipEngine.forVariant(GameVariant.ReignOfTheWarlock);
    expect(english.render(leoric(true), necromancer()).text.split('\n')).toContain(
      '5% Chance to cast level 10 Bone Spirit when struck',
    );
  });
});

// Griswold's Redemption, 4 sockets, the first four RotW unique Colossal Jewels at max roll.
const RedemptionWithColossalJewels =
  '{ "unitType": 4, "classId": 213, "quality": 5, "itemFlags": 2064, "fileIndex": 83, "itemLevel": 99, "location": 0, "statsLists": [ { "stateNo": 0, "flags": 2147483648, "stats": [ { "id": 72, "value": 250 } ] }, { "stateNo": 0, "flags": 64, "stats": [ { "id": 17, "layer": 0, "value": 240 }, { "id": 18, "layer": 0, "value": 240 }, { "id": 91, "layer": 0, "value": -20 }, { "id": 93, "layer": 0, "value": 40 }, { "id": 122, "layer": 0, "value": 200 }, { "id": 194, "layer": 0, "value": 4 } ] } ] , "items": [ { "unitType": 4, "classId": 690, "quality": 7, "fileIndex": 420, "itemFlags": 16, "statsLists": [ { "stateNo": 0, "flags": 64, "stats": [ { "id": 57, "layer": 0, "value": 975 }, { "id": 58, "layer": 0, "value": 975 }, { "id": 59, "layer": 0, "value": 25 }, { "id": 79, "layer": 0, "value": 50 }, { "id": 80, "layer": 0, "value": 35 }, { "id": 85, "layer": 0, "value": 5 }, { "id": 326, "layer": 0, "value": 1 }, { "id": 332, "layer": 0, "value": 10 }, { "id": 336, "layer": 0, "value": 10 }, { "id": 201, "layer": 4377, "value": 1 } ] } ] }, { "unitType": 4, "classId": 690, "quality": 7, "fileIndex": 421, "itemFlags": 16, "statsLists": [ { "stateNo": 0, "flags": 64, "stats": [ { "id": 50, "layer": 0, "value": 1 }, { "id": 51, "layer": 0, "value": 75 }, { "id": 79, "layer": 0, "value": 50 }, { "id": 80, "layer": 0, "value": 35 }, { "id": 85, "layer": 0, "value": 5 }, { "id": 330, "layer": 0, "value": 10 }, { "id": 334, "layer": 0, "value": 10 }, { "id": 201, "layer": 15065, "value": 1 } ] } ] }, { "unitType": 4, "classId": 690, "quality": 7, "fileIndex": 422, "itemFlags": 16, "statsLists": [ { "stateNo": 0, "flags": 64, "stats": [ { "id": 54, "layer": 0, "value": 10 }, { "id": 55, "layer": 0, "value": 30 }, { "id": 56, "layer": 0, "value": 125 }, { "id": 79, "layer": 0, "value": 50 }, { "id": 80, "layer": 0, "value": 35 }, { "id": 85, "layer": 0, "value": 5 }, { "id": 331, "layer": 0, "value": 10 }, { "id": 335, "layer": 0, "value": 10 }, { "id": 201, "layer": 2585, "value": 1 } ] } ] }, { "unitType": 4, "classId": 690, "quality": 7, "fileIndex": 423, "itemFlags": 16, "statsLists": [ { "stateNo": 0, "flags": 64, "stats": [ { "id": 48, "layer": 0, "value": 20 }, { "id": 49, "layer": 0, "value": 60 }, { "id": 79, "layer": 0, "value": 50 }, { "id": 80, "layer": 0, "value": 35 }, { "id": 85, "layer": 0, "value": 5 }, { "id": 329, "layer": 0, "value": 10 }, { "id": 333, "layer": 0, "value": 10 }, { "id": 201, "layer": 2969, "value": 1 } ] } ] } ] }';

describe('the D2R set-item byte cap', () => {
  it('shares the budget with the socket text', () => {
    // UI_DrawSetItemDescBox 0x1401d49ad / 0x1401d49fb / 0x1401d4da7: socket text (24 bytes) and
    // modifiers in one wrapped buffer, so 995 bytes of modifiers survive, not 1023.
    expect(
      Russian.render(unitFromJson(RedemptionWithColossalJewels), necromancer()).text.split('\n'),
    ).toContain(
      'Вероятность 1% применить умение «Ледяной доспех» 25-го уровня при получении уро' +
        Marker +
        '0Требуемый уровень: 75-й',
    );
  });
});
