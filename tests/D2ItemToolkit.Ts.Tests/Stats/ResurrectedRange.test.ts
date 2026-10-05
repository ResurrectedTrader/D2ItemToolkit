import { describe, expect, it } from 'vitest';
import { GameVariant } from '../../../src/D2ItemToolkit.Ts/src/Data/GameVariant.js';
import type { ItemRollRanges } from '../../../src/D2ItemToolkit.Ts/src/Stats/RolledRangeReconstructor.js';
import { unitFromJson } from '../../../src/D2ItemToolkit.Ts/src/Stats/Unit.js';
import { ItemTooltipSection } from '../../../src/D2ItemToolkit.Ts/src/Tooltip/ItemTooltip.js';
import { TooltipEngine } from '../../../src/D2ItemToolkit.Ts/src/Tooltip/TooltipEngine.js';

/**
 * The D2R spawn-order facts the roll-range reconstruction depends on. The twin of
 * ResurrectedRangeTests.cs.
 */
const Rotw = TooltipEngine.forVariant(GameVariant.ReignOfTheWarlock);

function ranges(json: string): ItemRollRanges {
  return Rotw.ranges(unitFromJson(json));
}

function spans(r: ItemRollRanges): string {
  return r.stats
    .map(
      s =>
        `${String(s.statId)}${s.layer === 0 ? '' : '@' + String(s.layer)} ${String(s.low)}..${String(s.high)}`,
    )
    .join('|');
}

function defense(r: ItemRollRanges): string {
  const found = r.stats.filter(s => s.statId === 31 && s.layer === 0);
  expect(found).toHaveLength(1);
  return `${String(found[0]?.low)}..${String(found[0]?.high)}`;
}

const EtherealVipermagi =
  '{"unitType":4,"classId":360,"quality":7,"itemFlags":4194320,"format":100,"fileIndex":210,"itemLevel":60,' +
  '"statsLists":[{"stateNo":0,"flags":2147483648,"stats":[{"id":31,"value":190},{"id":73,"value":19},{"id":72,"value":19}]},' +
  '{"stateNo":0,"flags":64,"stats":[{"id":16,"value":120},{"id":39,"value":30},{"id":41,"value":30},{"id":43,"value":30},' +
  '{"id":45,"value":30},{"id":105,"value":30},{"id":35,"value":10},{"id":127,"value":1}]}]}';

const EtherealSuperiorAncientArmor =
  '{"unitType":4,"classId":326,"quality":3,"itemFlags":4194320,"format":100,"fileIndex":2,"itemLevel":60,' +
  '"statsLists":[{"stateNo":0,"flags":2147483648,"stats":[{"id":31,"value":351},{"id":73,"value":31},{"id":72,"value":31}]},' +
  '{"stateNo":0,"flags":64,"stats":[{"id":16,"value":10}]}]}';

function fortitude(flags: number, baseDefense: number): string {
  return (
    `{"unitType":4,"classId":443,"quality":2,"itemFlags":${String(flags)},"format":100,"fileIndex":-1,"itemLevel":60,` +
    `"magicPrefix":[20547,0,0],"statsLists":[{"stateNo":0,"flags":2147483648,"stats":[{"id":31,"value":${String(baseDefense)}},` +
    `{"id":73,"value":60},{"id":72,"value":60},{"id":194,"value":4}]},` +
    `{"stateNo":171,"flags":64,"stats":[{"id":16,"value":200}]}]}`
  );
}

const PulInGrandCrown =
  '{"unitType":4,"classId":357,"quality":2,"itemFlags":2064,"format":100,"fileIndex":-1,"itemLevel":60,' +
  '"statsLists":[{"stateNo":0,"flags":2147483648,"stats":[{"id":31,"value":90},{"id":194,"value":1}]}],' +
  '"items":[{"unitType":4,"classId":645,"quality":2,"itemFlags":16,"format":100,"statsLists":[]}]}';

const SuperiorAncientArmorDurability =
  '{"unitType":4,"classId":326,"quality":3,"itemFlags":16,"format":100,"fileIndex":4,"itemLevel":60,' +
  '"statsLists":[{"stateNo":0,"flags":2147483648,"stats":[{"id":31,"value":220},{"id":73,"value":60},{"id":72,"value":60}]},' +
  '{"stateNo":0,"flags":64,"stats":[{"id":75,"value":12}]}]}';

const SuperiorJavelinAttackRating =
  '{"unitType":4,"classId":47,"quality":3,"itemFlags":16,"format":100,"fileIndex":0,"itemLevel":60,' +
  '"statsLists":[{"stateNo":0,"flags":2147483648,"stats":[]},{"stateNo":0,"flags":64,"stats":[{"id":19,"value":2}]}]}';

const IrathasCollar =
  '{"unitType":4,"classId":535,"quality":5,"itemFlags":16,"format":100,"fileIndex":9,"itemLevel":60,' +
  '"statsLists":[{"stateNo":0,"flags":2147483648,"stats":[]},' +
  '{"stateNo":0,"flags":64,"stats":[{"id":45,"value":30},{"id":110,"value":75}]},' +
  '{"stateNo":165,"flags":8256,"stats":[{"id":39,"value":15},{"id":41,"value":15},{"id":43,"value":15},{"id":45,"value":15}]}]}';

const ImmortalKingsForge =
  '{"unitType":4,"classId":384,"quality":5,"itemFlags":16,"format":100,"fileIndex":73,"itemLevel":60,' +
  '"statsLists":[{"stateNo":0,"flags":2147483648,"stats":[{"id":31,"value":50},{"id":73,"value":24},{"id":72,"value":24}]},' +
  '{"stateNo":0,"flags":64,"stats":[{"id":31,"value":65},{"id":0,"value":20},{"id":2,"value":20},{"id":201,"layer":2436,"value":12}]},' +
  '{"stateNo":165,"flags":8256,"stats":[{"id":93,"value":25}]},' +
  '{"stateNo":166,"flags":8256,"stats":[{"id":31,"value":120}]},' +
  '{"stateNo":167,"flags":8256,"stats":[{"id":60,"value":10}]},' +
  '{"stateNo":168,"flags":8256,"stats":[{"id":62,"value":10}]},' +
  '{"stateNo":169,"flags":8256,"stats":[{"id":134,"value":2}]}]}';

describe('the D2R roll-range spawn order', () => {
  it('an ethereal armour with spawn ac% scales the maximised base', () => {
    // Props first (0x1402e0fc3), base = maxac+1 (0x140283f88), then ITEMS_MakeEthereal
    // (0x1402e14c0) scales it by 3/2: 127 * 3 / 2 = 190, plus 120% = 418.
    const r = ranges(EtherealVipermagi);
    expect(defense(r)).toBe('418..418');
    expect(r.outOfRange).toEqual([]);
  });

  it('an ethereal superior armour scales its maximised base too', () => {
    const r = ranges(EtherealSuperiorAncientArmor);
    expect(defense(r)).toBe('368..403');
    expect(r.outOfRange).toEqual([]);
  });

  it.each([
    [67110928, 500, '1230..1572'],
    [71305232, 750, '1845..2358'],
  ])("a runeword's ac%% does not maximise the base (flags %i)", (flags, base, expected) => {
    // ITEMMODS_UpdateRuneword assigns with pItem = the rune (0x1402d39ec, 0x140286f5c).
    const r = ranges(fortitude(flags, base));
    expect(defense(r)).toBe(expected);
    expect(r.outOfRange).not.toContain(31);
  });

  it("a socketed Pul does not maximise the host's base", () => {
    // ITEMS_ApplyGemOrRuneAndRefreshSets passes the filler as pItem (0x1400a6772).
    expect(defense(ranges(PulInGrandCrown))).toBe('101..146');
  });

  it.each([
    [SuperiorAncientArmorDurability, '31 218..233|75 10..15'],
    [SuperiorJavelinAttackRating, '19 1..3'],
  ])('a superior item rolls only the qualityitems row its fileIndex names', (json, expected) => {
    // LOD114d_sub_5C2970 stores the drawn row (0x140381dc3) and applies only its mods.
    const r = ranges(json);
    expect(spans(r)).toBe(expected);
    expect(r.outOfRange).toEqual([]);
  });

  it("a set item's tiered aprops are not summed into its own stats", () => {
    // sub_1402866E0 case 4 puts aprops in states 165..169 when `add func` is set.
    const r = ranges(IrathasCollar);
    expect(spans(r)).toBe('45 30..30|110 75..75');
    expect(r.outOfRange).toEqual([]);
  });

  it('an inactive tier ac does not join the defense span', () => {
    const r = ranges(ImmortalKingsForge);
    expect(spans(r)).toBe('0 20..20|2 20..20|31 108..118|201@2436 12..12');
    expect(r.outOfRange).toEqual([]);
  });
});

const ImmortalKingsForgeEarned =
  '{"unitType":4,"classId":384,"quality":5,"itemFlags":16,"format":100,"fileIndex":73,"itemLevel":60,' +
  '"statsLists":[{"stateNo":0,"flags":2147483648,"stats":[{"id":31,"value":50},{"id":73,"value":24},{"id":72,"value":24}]},' +
  '{"stateNo":0,"flags":64,"stats":[{"id":31,"value":65},{"id":0,"value":20},{"id":2,"value":20},{"id":201,"layer":2436,"value":12}]},' +
  '{"stateNo":165,"flags":64,"stats":[{"id":93,"value":25}]},{"stateNo":166,"flags":64,"stats":[{"id":31,"value":120}]},' +
  '{"stateNo":167,"flags":8256,"stats":[{"id":60,"value":10}]},{"stateNo":168,"flags":8256,"stats":[{"id":62,"value":10}]},' +
  '{"stateNo":169,"flags":8256,"stats":[{"id":134,"value":2}]}]}';

const Stormshield =
  '{"unitType":4,"classId":447,"quality":7,"itemFlags":16,"format":100,"fileIndex":253,"itemLevel":80,' +
  '"statsLists":[{"stateNo":0,"flags":2147483648,"stats":[{"id":31,"value":140},{"id":73,"value":86},{"id":72,"value":86}]},' +
  '{"stateNo":0,"flags":64,"stats":[{"id":214,"value":30},{"id":36,"value":35},{"id":0,"value":30},{"id":152,"value":1},' +
  '{"id":20,"value":25},{"id":41,"value":25},{"id":43,"value":60},{"id":128,"value":10}]}]}';

const Level80 =
  '{"unitType":0,"classId":1,"statsLists":[{"stateNo":0,"flags":2147483648,"stats":' +
  '[{"id":12,"value":80},{"id":0,"value":200},{"id":2,"value":100}]}]}';

function vipermagiWithUm(runeLists: string): string {
  return (
    '{"unitType":4,"classId":360,"quality":7,"itemFlags":2064,"format":100,"fileIndex":210,"itemLevel":60,' +
    '"statsLists":[{"stateNo":0,"flags":2147483648,"stats":[{"id":31,"value":127},{"id":73,"value":38},{"id":72,"value":38},{"id":194,"value":1}]},' +
    '{"stateNo":0,"flags":64,"stats":[{"id":16,"value":120},{"id":39,"value":30},{"id":41,"value":30},{"id":43,"value":30},' +
    '{"id":45,"value":30},{"id":105,"value":30},{"id":35,"value":10},{"id":127,"value":1}]}],' +
    `"items":[{"unitType":4,"classId":646,"quality":2,"itemFlags":16,"format":100,"statsLists":${runeLists}}]}`
  );
}

const VipermagiWithUmClient = vipermagiWithUm('[]');

const VipermagiWithUmServer = vipermagiWithUm(
  '[{"stateNo":0,"flags":64,"stats":[{"id":39,"value":15},{"id":41,"value":15},{"id":43,"value":15},{"id":45,"value":15}]}]',
);

const ClassicMilabregasRobe =
  '{"unitType":4,"classId":326,"quality":5,"itemFlags":16,"format":0,"fileIndex":24,"itemLevel":30,' +
  '"statsLists":[{"stateNo":0,"flags":2147483648,"stats":[{"id":31,"value":225},{"id":73,"value":60},{"id":72,"value":60}]},' +
  '{"stateNo":0,"flags":64,"stats":[{"id":78,"value":3},{"id":34,"value":2}]}]}';

function upgradedVipermagi(flags: number, baseDefense: number): string {
  return (
    `{"unitType":4,"classId":430,"quality":7,"itemFlags":${String(flags)},"format":100,"fileIndex":210,"itemLevel":60,` +
    `"statsLists":[{"stateNo":0,"flags":2147483648,"stats":[{"id":31,"value":${String(baseDefense)}},{"id":73,"value":36},{"id":72,"value":36}]},` +
    '{"stateNo":0,"flags":64,"stats":[{"id":16,"value":120},{"id":39,"value":30},{"id":41,"value":30},' +
    '{"id":43,"value":30},{"id":45,"value":30},{"id":105,"value":30},{"id":35,"value":10},{"id":127,"value":1}]}]}'
  );
}

// Wyrmhide (uea 364..470, normcode lea) with magicprefix row 6, Holy `ac%` 81..100.
const RareWyrmhide =
  '{"unitType":4,"classId":430,"quality":6,"itemFlags":16,"format":100,"fileIndex":-1,"itemLevel":60,' +
  '"magicPrefix":[792,0,0],"statsLists":[{"stateNo":0,"flags":2147483648,' +
  '"stats":[{"id":31,"value":471},{"id":73,"value":36},{"id":72,"value":36}]},' +
  '{"stateNo":0,"flags":64,"stats":[{"id":16,"value":90}]}]}';

function armorClassLine(json: string, viewer: string | null): string | null | undefined {
  return Rotw.render(unitFromJson(json), viewer === null ? null : unitFromJson(viewer), {
    ranges: { color: -1 },
  }).lines.find(l => l.section === ItemTooltipSection.ArmorClass)?.text;
}

describe('the D2R roll-range spawn order, round 2', () => {
  it("an earned tier's ac joins the defense span", () => {
    // sub_1402866E0 puts aprop2a `ac` 120 in state 166 (0x140286a33); once earned the list drops
    // STATLIST_SET (0x14028ab90) and the Defense line draws 50 + 65 + 120.
    const r = ranges(ImmortalKingsForgeEarned);
    expect(defense(r)).toBe('228..238');
    expect(r.outOfRange).toEqual([]);
    expect(armorClassLine(ImmortalKingsForgeEarned, null)).toBe('Defense: ÿc3235 [228-238]\n');
  });

  it("the defense span includes the viewer's level-scaled defense", () => {
    // ITEMDESC_Defense attaches the item to the viewer (0x1401d1df1), re-running op 4 for `ac/lvl`:
    // (30 * 80) >> 3 = 300 on top of the 133..148 roll.
    expect(armorClassLine(Stormshield, Level80)).toBe('Defense: ÿc3440 [433-448]\n');
    expect(defense(ranges(Stormshield))).toBe('133..148');

    const forViewer = Rotw.rangesForViewer(unitFromJson(Stormshield), unitFromJson(Level80));
    expect(defense(forViewer)).toBe('433..448');
    expect(forViewer.outOfRange).toEqual([]);
  });

  it("a filler's contribution is in the comparand", () => {
    // The span counts the Um (gems.txt helm/armor res-all 15); so must what it is checked against.
    expect(Rotw.items.classIdForCode('r22')).toBe(646);
    expect(ranges(VipermagiWithUmClient).outOfRange).toEqual([]);
    expect(ranges(PulInGrandCrown).outOfRange).toEqual([]);
  });

  it('a server-captured rune is ranged from gems.txt', () => {
    // ITEMS_ApplyGemOrRuneAndRefreshSets assigns to the filler (0x1400a6772), so a server capture
    // carries the rune's list; its roll is still gems.txt's.
    const r = ranges(VipermagiWithUmServer);
    expect(spans(r)).toContain('39 35..50');
    expect(r.outOfRange).toEqual([]);

    const tooltip = Rotw.render(unitFromJson(VipermagiWithUmServer), null, {
      ranges: { color: -1 },
    });
    expect(tooltip.lines.map(l => l.text)).toContain('All Resistances +45 [35-50]\n');
  });

  it('a classic set item gets no aprops and so no maximised base', () => {
    // sub_1402866E0 case 4: format 0 applies props 1..2 only (0x140286911); the aprop `ac%` that
    // would maximise the base is never assigned.
    const r = ranges(ClassicMilabregasRobe);
    expect(spans(r)).toBe('31 218..233|34 2..2|78 3..3');
    expect(r.outOfRange).toEqual([]);
  });

  it.each([
    [16, 400, '800..1034'],
    [4194320, 600, '1201..1551'],
  ])('a cube-upgraded unique rerolls its base unmaximised (flags %i)', (flags, base, expected) => {
    // PLRTRADE_CreateCubeOutputs keeps the item for a `mod` output and re-runs InitItemStats
    // (0x1403c0251), whose armour roll is a plain minac..maxac store (0x1402de731); only
    // ApplyEthereality follows (0x1403c027b).
    const r = ranges(upgradedVipermagi(flags, base));
    expect(defense(r)).toBe(expected);
    expect(r.outOfRange).toEqual([]);
  });

  it('a rare on an upgradable base spans both the maximised and the rerolled base', () => {
    // Native: maxac + 1 = 471; upgraded: 364..470. 364 + 81% = 658, 471 + 100% = 942.
    expect(Rotw.data.magicSuffix?.rowCount).toBe(785);
    const r = ranges(RareWyrmhide);
    expect(spans(r)).toBe('16 81..100|31 658..942');
    expect(r.outOfRange).toEqual([]);
  });

  it('set bonus codes do not depend on a set item having been rendered', () => {
    const fresh = TooltipEngine.fromData(Rotw.data);
    const collar = unitFromJson(IrathasCollar);
    const before = spans(fresh.ranges(collar, [3]));
    fresh.render(collar);

    expect(spans(fresh.ranges(collar, [3]))).toBe(before);
  });
});
