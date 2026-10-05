import { describe, expect, it } from 'vitest';
import { embeddedSource } from '../../../src/D2ItemToolkit.Ts/src/Data/EmbeddedData.js';
import { GameVariant } from '../../../src/D2ItemToolkit.Ts/src/Data/GameVariant.js';
import { TxtFile } from '../../../src/D2ItemToolkit.Ts/src/Data/TxtFile.js';
import type { ByteSource } from '../../../src/D2ItemToolkit.Ts/src/Data/TxtDataSource.js';
import {
  ItemRecordFlags,
  ItemRecordReader,
  type ItemIdentity,
} from '../../../src/D2ItemToolkit.Ts/src/Stats/ItemRecord.js';
import { ItemStatReader } from '../../../src/D2ItemToolkit.Ts/src/Stats/ItemStatReader.js';
import {
  ChoicePickMode,
  ChoiceResolution,
  RolledRangeReconstructor,
  type ItemRollRanges,
  type RolledChoice,
  type RolledChoiceOption,
  type RolledChoicePick,
  type RolledStatRange,
} from '../../../src/D2ItemToolkit.Ts/src/Stats/RolledRangeReconstructor.js';
import { createUnit } from '../../../src/D2ItemToolkit.Ts/src/Stats/Unit.js';
import { MagicAffixTable } from '../../../src/D2ItemToolkit.Ts/src/Tables/MagicAffixTable.js';
import { PropertiesTable } from '../../../src/D2ItemToolkit.Ts/src/Tables/PropertiesTable.js';
import {
  PropertyGroupsTable,
  PropertyRefKind,
} from '../../../src/D2ItemToolkit.Ts/src/Tables/PropertyGroupsTable.js';
import { SetTable } from '../../../src/D2ItemToolkit.Ts/src/Tables/SetTable.js';
import { D2DataFiles } from '../../../src/D2ItemToolkit.Ts/src/Tables/TxtDataProviders.js';
import { TooltipEngine } from '../../../src/D2ItemToolkit.Ts/src/Tooltip/TooltipEngine.js';

/**
 * The peer of the C# PropertyGroupTests: PropertyGroups.txt choices and func-25 stat picks in the
 * roll-range reconstruction, every shipped user pinned with the same strings.
 */
const Engine = TooltipEngine.forVariant(GameVariant.ReignOfTheWarlock);
const Data = Engine.data;
const uniqueItems = Data.uniqueItems as TxtFile;
const propertyGroups = Data.propertyGroups as TxtFile;

const ListFlagsMagic = 0x40;

function reconstructor(data: D2DataFiles = Data): RolledRangeReconstructor {
  return new RolledRangeReconstructor(
    data,
    Engine.items,
    Engine.types,
    new MagicAffixTable(data),
    new SetTable(data.sets, data.setItems, data.strings),
  );
}

function key(statId: number, layer = 0): number {
  return ItemStatReader.packStatKey(layer, statId);
}

function unique(index: string, expectedRow: number): ItemIdentity {
  const row = uniqueItems.findRow('index', index);
  expect(row).toBe(expectedRow);

  return ItemRecordReader.readIdentity(
    createUnit({
      unitType: 4,
      quality: 7,
      fileIndex: row,
      classId: Engine.items.classIdForCode(uniqueItems.getString(row, 'code').trim()),
      itemFlags: ItemRecordFlags.Identified,
    }),
  );
}

function magic(prefixId: number, code: string): ItemIdentity {
  return ItemRecordReader.readIdentity(
    createUnit({
      unitType: 4,
      quality: 4,
      classId: Engine.items.classIdForCode(code),
      itemFlags: ItemRecordFlags.Identified,
      magicPrefix: [prefixId, 0, 0],
    }),
  );
}

function ranges(
  item: ItemIdentity,
  recorded: [number, number][] | null,
  data: D2DataFiles = Data,
): ItemRollRanges {
  return reconstructor(data).reconstruct(
    item,
    recorded === null ? null : new Map(recorded),
    null,
    null,
  );
}

function describeRange(range: RolledStatRange): string {
  return `${String(range.statId)}@${String(range.layer)} ${String(range.low)}..${String(range.high)} (${String(range.sources)})`;
}

function outcomes(consistent: readonly (readonly RolledChoicePick[])[]): string {
  return (
    '[' +
    consistent
      .map(
        outcome =>
          '[' + outcome.map(pick => `${String(pick.option)}:${String(pick.param)}`).join(',') + ']',
      )
      .join(',') +
    ']'
  );
}

function describeChoice(choice: RolledChoice): string {
  return [
    String(choice.groupRow),
    choice.code,
    String(choice.sources),
    ChoicePickMode[choice.pickMode],
    `${String(choice.countLow)}..${String(choice.countHigh)}`,
    ChoiceResolution[choice.resolution],
    outcomes(choice.consistent),
  ].join(' ');
}

function describeOption(option: RolledChoiceOption): string {
  return (
    `${String(option.entry)} ${String(option.propertyId)} ${option.code} w${String(option.weight)}` +
    ` p${String(option.paramLow)}..${String(option.paramHigh)}` +
    ` m${String(option.min)}..${String(option.max)} | ` +
    option.variants
      .map(variant => `${String(variant.param)}: ` + variant.stats.map(describeRange).join(', '))
      .join('; ')
  );
}

function choices(result: ItemRollRanges): string[] {
  return result.choices.map(describeChoice);
}

function stats(result: ItemRollRanges): string[] {
  return result.stats.map(describeRange);
}

function options(result: ItemRollRanges, index: number): string[] {
  const choice = result.choices[index];
  expect(choice).toBeDefined();
  return (choice as RolledChoice).options.map(describeOption);
}

describe('Wraithstep', () => {
  const option =
    '1 124 skilltab w1 p21..23 m1..1 | 21: 188@56 1..1 (516); 22: 188@57 1..1 (516); ' +
    '23: 188@58 1..1 (516)';

  it.each([
    [21, 56],
    [22, 57],
    [23, 58],
  ])('resolves the tab the record carries (param %i, layer %i)', (param, layer) => {
    const result = ranges(unique('Wraithstep', 413), [[key(188, layer), 1]]);

    expect(choices(result)).toEqual([
      `0 skilltab-war 516 Exactly 1..1 Resolved [[0:${String(param)}]]`,
    ]);
    expect(options(result, 0)).toEqual([option]);
    expect(stats(result)).toContain(`188@${String(layer)} 1..1 (516)`);
    expect(result.stats.filter(r => r.statId === 188)).toHaveLength(1);
    expect(result.unattributed).toEqual([]);
    expect(result.outOfRange).toEqual([]);
  });

  it('with no record lists every tab and claims none', () => {
    const result = ranges(unique('Wraithstep', 413), null);

    expect(choices(result)).toEqual([
      '0 skilltab-war 516 Exactly 1..1 NoRecord [[0:21],[0:22],[0:23]]',
    ]);
    expect(result.stats.some(r => r.statId === 188)).toBe(false);
  });

  it('with two tabs recorded is contradicted', () => {
    const result = ranges(unique('Wraithstep', 413), [
      [key(188, 56), 1],
      [key(188, 57), 1],
    ]);

    expect(choices(result)).toEqual([
      '0 skilltab-war 516 Exactly 1..1 Contradicted [[0:21],[0:22],[0:23]]',
    ]);
    expect(result.outOfRange).toEqual([188]);
  });

  it('with no tab recorded is contradicted', () => {
    const result = ranges(unique('Wraithstep', 413), [[key(96), 30]]);

    expect(choices(result)).toEqual([
      '0 skilltab-war 516 Exactly 1..1 Contradicted [[0:21],[0:22],[0:23]]',
    ]);
    expect(result.outOfRange).toEqual([]);
  });
});

describe('Opalvein', () => {
  it('offers the six magdam-rand options', () => {
    const result = ranges(unique('Opalvein', 416), null);

    expect(choices(result)).toEqual([
      '1 magdam-rand 516 Exactly 1..1 NoRecord [[0:0],[1:0],[2:0],[3:0],[4:0],[5:0]]',
    ]);
    expect(options(result, 0)).toEqual([
      '1 283 extra-mag w1 p0..0 m3..5 | 0: 357@0 3..5 (516)',
      '2 29 dmg% w1 p0..0 m20..40 | 0: 17@0 20..40 (516), 18@0 20..40 (516)',
      '3 244 extra-fire w1 p0..0 m3..5 | 0: 329@0 3..5 (516)',
      '4 246 extra-cold w1 p0..0 m3..5 | 0: 331@0 3..5 (516)',
      '5 245 extra-ltng w1 p0..0 m3..5 | 0: 330@0 3..5 (516)',
      '6 247 extra-pois w1 p0..0 m3..5 | 0: 332@0 3..5 (516)',
    ]);
    expect(result.choices[0]?.options.every(option => option.nested === null)).toBe(true);
  });

  it.each([
    [0, [357], 4, '357@0 3..5 (516)'],
    [1, [17, 18], 33, '17@0 20..40 (516)|18@0 20..40 (516)'],
    [2, [329], 4, '329@0 3..5 (516)'],
    [3, [331], 4, '331@0 3..5 (516)'],
    [4, [330], 4, '330@0 3..5 (516)'],
    [5, [332], 4, '332@0 3..5 (516)'],
  ])('resolves option %i', (option, statIds, value, expected) => {
    const result = ranges(
      unique('Opalvein', 416),
      statIds.map(stat => [key(stat), value]),
    );

    expect(choices(result)).toEqual([
      `1 magdam-rand 516 Exactly 1..1 Resolved [[${String(option)}:0]]`,
    ]);
    for (const line of expected.split('|')) {
      expect(stats(result)).toContain(line);
    }

    expect(result.unattributed).toEqual([]);
    expect(result.outOfRange).toEqual([]);
  });

  it('with two options recorded is contradicted', () => {
    const result = ranges(unique('Opalvein', 416), [
      [key(329), 4],
      [key(331), 4],
    ]);

    expect(choices(result)).toEqual([
      '1 magdam-rand 516 Exactly 1..1 Contradicted [[0:0],[1:0],[2:0],[3:0],[4:0],[5:0]]',
    ]);
    expect(result.outOfRange).toEqual([329, 331]);
  });
});

describe('the six crafted charms', () => {
  it('Crafted Cold Rupture worked example', () => {
    const result = ranges(unique('Crafted Cold Rupture', 427), [
      [key(187), 300],
      [key(43), -70],
      [key(335), 7],
      [key(9), 40 << 8],
      [key(80), 20],
      [key(99), 18],
      [key(34), 6],
    ]);

    expect(choices(result)).toEqual([
      '3 Gelid-Affix1 516 UpTo 1..1 Resolved [[1:0]]',
      '9 Gelid-Affix2 516 UpTo 1..1 Resolved [[0:0]]',
      '15 Gelid-Affix3 516 UpTo 1..1 Resolved [[1:0]]',
      '21 Gelid-Affix4 516 UpTo 1..1 Resolved [[1:0]]',
      '27 Gelid-Affix6 516 UpTo 1..1 Resolved [[1:0]]',
    ]);
    expect(stats(result)).toEqual([
      '9@0 2560..19200 (516)',
      '34@0 5..10 (516)',
      '43@0 -70..-70 (4)',
      '80@0 14..25 (516)',
      '99@0 12..24 (516)',
      '187@0 300..300 (4)',
      '335@0 5..10 (516)',
    ]);
    expect(result.unattributed).toEqual([]);
    expect(result.outOfRange).toEqual([]);
  });

  it("a crafted charm's options are the group's columns", () => {
    const result = ranges(unique('Crafted Cold Rupture', 427), null);

    expect(result.choices.flatMap(c => c.options.map(describeOption))).toEqual([
      '1 246 extra-cold w1 p0..0 m5..15 | 0: 331@0 5..15 (516)',
      '2 234 pierce-cold w1 p0..0 m5..10 | 0: 335@0 5..10 (516)',
      '1 59 mag% w1 p0..0 m14..25 | 0: 80@0 14..25 (516)',
      '2 58 gold% w1 p0..0 m20..55 | 0: 79@0 20..55 (516)',
      '1 13 hp w1 p0..0 m10..65 | 0: 7@0 2560..16640 (516)',
      '2 11 mana w1 p0..0 m10..75 | 0: 9@0 2560..19200 (516)',
      '1 76 move1 w1 p0..0 m5..10 | 0: 96@0 5..10 (516)',
      '2 79 balance1 w3 p0..0 m12..24 | 0: 99@0 12..24 (516)',
      '3 251 all-stats w1 p0..0 m3..8 | 0: 0@0 3..8 (516), 1@0 3..8 (516), ' +
        '2@0 3..8 (516), 3@0 3..8 (516)',
      '1 6 red-mag w1 p0..0 m5..10 | 0: 35@0 5..10 (516)',
      '2 3 red-dmg w1 p0..0 m5..10 | 0: 34@0 5..10 (516)',
    ]);
  });

  // index, row, group rows, pierce-immunity stat, fixed stat, fixed value, Affix1 option 0
  it.each([
    ['Crafted Cold Rupture', 427, [3, 9, 15, 21, 27], 187, 43, -70, [331]],
    ['Crafted Flame Rift', 433, [5, 11, 17, 23, 29], 189, 39, -70, [329]],
    ['Crafted Crack of the Heavens', 434, [4, 10, 16, 22, 28], 190, 41, -70, [330]],
    ['Crafted Rotting Fissure', 435, [2, 8, 14, 20, 26], 191, 45, -70, [332]],
    ['Crafted Bone Break', 436, [6, 12, 18, 24, 30], 192, 36, -10, [17, 18]],
    ['Crafted Black Cleft', 437, [7, 13, 19, 25, 31], 193, 37, -45, [357]],
  ])(
    '%s resolves one fixed pick set',
    (index, row, groupRows, pierceImmunity, fixedStat, fixedValue, affix1) => {
      // Affix1 option 0, Affix2 option 1 (gold%), Affix3 option 0 (hp), Affix4 option 2
      // (all-stats), Affix6 option 0 (red-mag).
      const recorded: [number, number][] = [
        [key(pierceImmunity), 300],
        [key(fixedStat), fixedValue],
        [key(79), 30],
        [key(7), 20 << 8],
        [key(0), 5],
        [key(1), 5],
        [key(2), 5],
        [key(3), 5],
        [key(35), 7],
        // dmg% (17, 18) rolls 75..100; every other Affix1 option 0 rolls inside 5..15.
        ...affix1.map((stat): [number, number] => [key(stat), affix1.length === 2 ? 80 : 12]),
      ];

      const result = ranges(unique(index, row), recorded);

      const picks = ['[[0:0]]', '[[1:0]]', '[[0:0]]', '[[2:0]]', '[[0:0]]'];
      expect(choices(result)).toEqual(
        groupRows.map(
          (group, i) =>
            `${String(group)} ${propertyGroups.getString(group, 'code')} 516 UpTo 1..1 Resolved ` +
            (picks[i] ?? ''),
        ),
      );
      expect(result.unattributed).toEqual([]);
      expect(result.outOfRange).toEqual([]);
    },
  );

  it('a crafted charm annotates its picked lines', () => {
    const row = uniqueItems.findRow('index', 'Crafted Cold Rupture');
    const charm = createUnit({
      unitType: 4,
      quality: 7,
      fileIndex: row,
      classId: Engine.items.classIdForCode('cs2'),
      itemFlags: ItemRecordFlags.Identified,
      statsLists: [
        {
          stateNo: 0,
          flags: ListFlagsMagic,
          stats: [
            { id: 187, value: 300 },
            { id: 43, value: -70 },
            { id: 335, value: 7 },
            { id: 9, value: 40 << 8 },
            { id: 80, value: 20 },
            { id: 99, value: 18 },
            { id: 34, value: 6 },
          ],
        },
      ],
    });

    const lines = Engine.render(charm, null, { ranges: { color: -1 } }).lines.map(l =>
      (l.text ?? '').replace(/\n+$/, ''),
    );

    expect(lines).toEqual([
      'Renewed Cold Rupture',
      'Grand Charm',
      'Keep in Inventory to Gain Bonus',
      'Required Level: 75',
      'Monster Cold Immunity is Sundered',
      '+18% Faster Hit Recovery [12-24]',
      '-7% to Enemy Cold Resistance [5-10]',
      '+40 to Mana [10-75]',
      'Cold Resist -70%',
      'Damage Reduced by 6 [5-10]',
      '20% Better Chance of Getting Magic Items [14-25]',
    ]);
  });
});

describe('the six group prefixes', () => {
  const virulentA = '2 Virulent-Affix1 514 UpTo 0..1 ';
  const virulentB = '8 Virulent-Affix2 514 UpTo 0..1 ';

  it('Virulent resolves both choices when both picked', () => {
    const result = ranges(magic(1501, 'cm2'), [
      [key(336), 12],
      [key(79), 30],
    ]);

    expect(choices(result)).toEqual([
      virulentA + 'Resolved [[1:0]]',
      virulentB + 'Resolved [[1:0]]',
    ]);
    expect(stats(result)).toContain('336@0 7..18 (514)');
    expect(stats(result)).toContain('79@0 20..55 (514)');
    expect(result.outOfRange).toEqual([]);
  });

  it('Virulent below the group floor resolves to nothing picked', () => {
    const result = ranges(magic(1501, 'cm2'), [[key(336), 5]]);

    expect(choices(result)).toEqual([virulentA + 'Resolved [[]]', virulentB + 'Resolved [[]]']);
    expect(stats(result)).toContain('336@0 2..8 (2)');
  });

  it('Virulent inside both spans is ambiguous', () => {
    const result = ranges(magic(1501, 'cm2'), [[key(336), 7]]);

    expect(choices(result)).toEqual([
      virulentA + 'Ambiguous [[],[1:0]]',
      virulentB + 'Resolved [[]]',
    ]);
    expect(stats(result)).toContain('336@0 2..18 (514)');
    expect(result.unattributed).toEqual([]);
    expect(result.outOfRange).toEqual([]);
  });

  it('Virulent resolves the mastery option', () => {
    const result = ranges(magic(1501, 'cm2'), [
      [key(336), 4],
      [key(332), 9],
    ]);

    expect(choices(result)).toEqual([virulentA + 'Resolved [[0:0]]', virulentB + 'Resolved [[]]']);
    expect(stats(result)).toContain('336@0 2..8 (2)');
    expect(stats(result)).toContain('332@0 5..15 (514)');
  });

  it('Virulent above every span is contradicted', () => {
    const result = ranges(magic(1501, 'cm2'), [[key(336), 20]]);

    expect(choices(result)).toEqual([
      virulentA + 'Contradicted [[],[0:0],[1:0]]',
      virulentB + 'Contradicted [[],[0:0],[1:0]]',
    ]);
    expect(result.outOfRange).toEqual([336]);
  });

  // prefix id, base, mod1 stat, A group row, A option 0 stats, B group row
  it.each([
    [1502, 'cm2', 333, 3, [331], 9],
    [1503, 'qui', 335, 4, [330], 10],
    [1504, 'qui', 334, 5, [329], 11],
    [1505, 'qui', 358, 6, [17, 18], 12],
    [1506, 'qui', 366, 7, [357], 13],
  ])(
    'prefix %i resolves by the group its cells name',
    (prefixId, code, mod1, groupA, optionStats, groupB) => {
      const recorded: [number, number][] = [
        [key(mod1), 3],
        ...optionStats.map((stat): [number, number] => [
          key(stat),
          optionStats.length === 2 ? 80 : 11,
        ]),
      ];

      const result = ranges(magic(prefixId, code), recorded);

      expect(choices(result)).toEqual([
        `${String(groupA)} ${propertyGroups.getString(groupA, 'code')} 514 UpTo 0..1 Resolved [[0:0]]`,
        `${String(groupB)} ${propertyGroups.getString(groupB, 'code')} 514 UpTo 0..1 Resolved [[]]`,
      ]);
      expect(stats(result)).toContain(`${String(mod1)}@0 2..8 (2)`);
      expect(result.unattributed).toEqual([]);
      expect(result.outOfRange).toEqual([]);
    },
  );

  it('Incendiary names the Gelid groups', () => {
    const result = ranges(magic(1502, 'cm2'), [
      [key(333), 3],
      [key(331), 11],
    ]);

    expect(choices(result)).toEqual([
      '3 Gelid-Affix1 514 UpTo 0..1 Resolved [[0:0]]',
      '9 Gelid-Affix2 514 UpTo 0..1 Resolved [[]]',
    ]);
    expect(stats(result)).toContain('333@0 2..8 (2)');
    expect(stats(result)).toContain('331@0 5..15 (514)');
  });
});

describe('1.14d and the table itself', () => {
  it('a 1.14d item has no choices', () => {
    const lod = TooltipEngine.embedded;
    expect(lod.data.propertyGroups).toBeNull();

    const table = lod.data.uniqueItems as TxtFile;
    const row = table.findRow('index', 'The Eye of Etlich');
    const unit = createUnit({
      unitType: 4,
      quality: 7,
      fileIndex: row,
      classId: lod.items.classIdForCode(table.getString(row, 'code').trim()),
      itemFlags: ItemRecordFlags.Identified,
    });

    expect(lod.ranges(unit).choices).toEqual([]);
  });

  it('the shipped tables have 32 and 20 groups', () => {
    const rotw = new PropertyGroupsTable(
      Data.propertyGroups,
      new PropertiesTable(Data.properties, Data.itemStatCost),
    );
    expect(rotw.rowCount).toBe(32);

    const first = rotw.getRow(0);
    expect(first?.pickMode).toBe(1);
    expect(first?.entries[0]?.prop).toEqual({ row: 124, kind: PropertyRefKind.Property });
    expect(first?.entries[0]?.parMin).toBe(21);
    expect(first?.entries[0]?.parMax).toBe(23);
    expect(first?.entries[1]?.prop.row).toBe(-1);
    expect(rotw.getRow(20)?.entries[1]?.weight).toBe(3);
    expect(rotw.rowForCode('virulent-affix6')).toBe(26);

    const base = D2DataFiles.loadEmbedded(GameVariant.Resurrected);
    expect(
      new PropertyGroupsTable(
        base.propertyGroups,
        new PropertiesTable(base.properties, base.itemStatCost),
      ).rowCount,
    ).toBe(20);
  });

  it('a group entry may name only an earlier group', () => {
    let text = 'code\tpickmode\tprop1\tprop2\tprop3\r\n';
    for (let row = 0; row < 6; ++row) {
      const props = row === 3 ? 'g3\tg5\tg1' : '\t\t';
      text += `g${String(row)}\t1\t${props}\r\n`;
    }

    const groups = new PropertyGroupsTable(
      TxtFile.parse(text, GameVariant.ReignOfTheWarlock),
      new PropertiesTable(Data.properties, Data.itemStatCost),
    );

    const entries = groups.getRow(3)?.entries ?? [];
    expect(entries[0]?.prop).toEqual({ row: -1, kind: PropertyRefKind.Group });
    expect(entries[1]?.prop.row).toBe(-1);
    expect(entries[2]?.prop).toEqual({ row: 1, kind: PropertyRefKind.Group });
    expect(entries[3]?.prop.kind).toBe(PropertyRefKind.Unresolved);
  });

  it('Properties win over a group of the same code', () => {
    const properties = new PropertiesTable(Data.properties, Data.itemStatCost);
    const groups = new PropertyGroupsTable(
      TxtFile.parse('code\tpickmode\r\nstr\t0\r\nmine\t0\r\n', GameVariant.ReignOfTheWarlock),
      properties,
    );

    expect(PropertyGroupsTable.link('STR', properties, groups)).toEqual({
      row: properties.rowForCode('str'),
      kind: PropertyRefKind.Property,
    });
    expect(PropertyGroupsTable.link('MINE', properties, groups)).toEqual({
      row: 1,
      kind: PropertyRefKind.Group,
    });
  });
});

describe('synthetic: pickmode 0 and func 25', () => {
  it('a pickmode 0 group and a func 25 stat pick', () => {
    const data = synthetic();
    const target = new PropertiesTable(data.properties, data.itemStatCost).rowForCode(
      'test-target',
    );
    const t = String(target);

    const result = ranges(
      unique('Wraithstep', 413),
      [
        [key(39), 4],
        [key(41), 7],
        [key(2), 16],
      ],
      data,
    );

    expect(choices(result)).toEqual([
      '32 test-all 516 All 2..2 Resolved [[0:0,1:0]]',
      `-1 test-statpick 516 StatPick 1..1 Resolved [[0:${t}]]`,
    ]);
    expect(options(result, 0)).toEqual([
      '1 31 res-fire w1 p0..0 m3..5 | 0: 39@0 3..5 (516)',
      '2 33 res-ltng w1 p0..0 m7..7 | 0: 41@0 7..7 (516)',
    ]);

    // Strength (stat 0) is never a candidate, and an unresolved stat name links to -1.
    expect(options(result, 1)).toEqual([
      `2 ${t} test-target w1 p${t}..${t} m4..6 | ${t}: 2@0 4..6 (516)`,
      `3 ${t} test-target w1 p${t}..${t} m4..6 | ${t}: 7@0 1024..1536 (516)`,
    ]);

    expect(stats(result)).toContain('39@0 3..5 (516)');
    expect(stats(result)).toContain('41@0 7..7 (516)');
    expect(stats(result)).toContain('2@0 14..21 (516)');
    expect(result.unsupportedFuncs).toEqual([]);
    expect(result.outOfRange).toEqual([]);
  });
});

function decode(bytes: Uint8Array): string {
  let text = '';
  for (const byte of bytes) {
    text += String.fromCharCode(byte);
  }

  return text;
}

function encode(text: string): Uint8Array {
  return Uint8Array.from(text, c => c.charCodeAt(0));
}

function editTable(
  source: ByteSource,
  name: string,
  edit: (header: string[], rows: string[][]) => void,
): string {
  const bytes = source(name);
  if (bytes === null) {
    throw new Error(name);
  }

  const lines = decode(bytes).split('\r\n');
  const header = (lines[0] ?? '').split('\t');
  // The final CRLF leaves one empty element; interior blank lines are rows and stay.
  const rows = lines.slice(1, lines.length - 1).map(line => line.split('\t'));
  edit(header, rows);
  return [lines[0] ?? '', ...rows.map(row => row.join('\t'))].join('\r\n') + '\r\n';
}

function row(header: readonly string[], cells: Record<string, string>): string[] {
  return header.map(column => cells[column] ?? '');
}

/**
 * The embedded RotW tables with two properties and a group appended, and Wraithstep's first two
 * cells pointed at them.
 */
function synthetic(): D2DataFiles {
  const excel = embeddedSource('d2r/excel');

  const properties = editTable(excel, 'properties.txt', (header, rows) => {
    rows.push(row(header, { code: 'test-statpick', func1: '25' }));
    rows.push(
      row(header, {
        code: 'test-target',
        stat1: 'strength',
        stat2: 'dexterity',
        stat3: 'maxhp',
        stat4: 'no-such-stat',
      }),
    );
  });

  const groups = editTable(excel, 'propertygroups.txt', (header, rows) => {
    rows.push(
      row(header, {
        code: 'test-all',
        PickMode: '0',
        Prop1: 'res-fire',
        ModMin1: '3',
        ModMax1: '5',
        Prop2: 'res-ltng',
        ModMin2: '7',
        ModMax2: '7',
      }),
    );
  });

  // The param is a row number, known only once properties.txt is extended.
  const target = TxtFile.parse(properties, GameVariant.ReignOfTheWarlock).findRow(
    'code',
    'test-target',
  );

  const uniques = editTable(excel, 'uniqueitems.txt', (header, rows) => {
    const index = header.indexOf('index');
    const wraithstep = rows.find(r => r[index] === 'Wraithstep');
    if (wraithstep === undefined) {
      throw new Error('Wraithstep');
    }

    wraithstep[header.indexOf('prop1')] = 'test-all';
    wraithstep[header.indexOf('prop2')] = 'test-statpick';
    wraithstep[header.indexOf('par2')] = String(target);
    wraithstep[header.indexOf('min2')] = '4';
    wraithstep[header.indexOf('max2')] = '6';
  });

  const overrides = new Map<string, Uint8Array>([
    ['properties.txt', encode(properties)],
    ['propertygroups.txt', encode(groups)],
    ['uniqueitems.txt', encode(uniques)],
  ]);

  return D2DataFiles.buildResurrected(
    GameVariant.ReignOfTheWarlock,
    name => overrides.get(name.toLowerCase()) ?? excel(name),
    embeddedSource('d2r/strings'),
    null,
    () => null,
  );
}
