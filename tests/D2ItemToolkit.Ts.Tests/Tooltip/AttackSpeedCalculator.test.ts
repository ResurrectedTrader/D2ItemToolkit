import { describe, expect, it } from 'vitest';
import { AnimDataFile } from '../../../src/D2ItemToolkit.Ts/src/Data/AnimDataFile.js';
import { AttackSpeedCalculator } from '../../../src/D2ItemToolkit.Ts/src/Tooltip/AttackSpeedCalculator.js';
import {
  ItemIdentity,
  ItemRecordFlags,
  ItemViewer,
} from '../../../src/D2ItemToolkit.Ts/src/Stats/ItemRecord.js';
import { ItemStatReader } from '../../../src/D2ItemToolkit.Ts/src/Stats/ItemStatReader.js';
import { ItemTable } from '../../../src/D2ItemToolkit.Ts/src/Tables/ItemTable.js';
import { ItemTypeTree } from '../../../src/D2ItemToolkit.Ts/src/Tables/ItemTypeTree.js';
import { ItemTooltipSection } from '../../../src/D2ItemToolkit.Ts/src/Tooltip/ItemTooltip.js';
import { RecordSections } from '../../../src/D2ItemToolkit.Ts/src/Tooltip/RecordSections.js';
import type { TxtFile } from '../../../src/D2ItemToolkit.Ts/src/Data/TxtFile.js';
import { D2DataFiles } from '../../../src/D2ItemToolkit.Ts/src/Tables/TxtDataProviders.js';

// ITEM_CalcWeaponAttackSpeed 0x62a710 driven from the embedded AnimData.D2. AttackSpeedTests.cs.

const Data = D2DataFiles.load();
const Items = new ItemTable(Data.weapons, Data.armor, Data.misc);
const Calculator = new AttackSpeedCalculator(Data, Items);

function item(code: string): ItemIdentity {
  const it = new ItemIdentity();
  it.classId = Items.classIdForCode(code);
  it.code = code;
  it.flags = ItemRecordFlags.Identified;
  return it;
}

function player(classId: number): ItemViewer {
  const viewer = new ItemViewer();
  viewer.unitType = 0;
  viewer.classId = classId;
  viewer.level = 40;
  return viewer;
}

// A mercenary is a MONSTER: dwUnitType 1, dwClassId a monstats row. These four are the only rows
// hireling.txt `Class` names, post-splice (monstats.txt's Expansion divider is row 410, so 271 and
// 338 are unaffected by it and 560/561 sit after it).
const RogueHireling = 271; // roguehire, Code "RG", BaseW "hth"
const Act2Hireling = 338; // act2hire,  Code "GU", BaseW "hth"
const Act3Hireling = 359; // act3hire,  Code "IW", BaseW "1hs"
const Act5Hireling = 561; // act5hire2, Code "0A", BaseW "2hs"

function monster(classId: number): ItemViewer {
  const viewer = new ItemViewer();
  viewer.unitType = 1;
  viewer.classId = classId;
  viewer.level = 40;
  return viewer;
}

function speedLineFor(viewer: ItemViewer, code: string, ias: number): string | null {
  const stats = new Map<number, number>();
  stats.set(ItemStatReader.packStatKey(0, 93), ias);

  const sections = new RecordSections(
    Data,
    Items,
    new ItemTypeTree(Data.itemTypes),
    item(code),
    viewer,
    stats,
    null,
    new Map<number, number>(),
    null,
  );

  return sections.getSection(ItemTooltipSection.AttackSpeed);
}

describe('AttackSpeedCalculator', () => {
  it('the embedded animdata parses', () => {
    expect(Data.animData).not.toBeNull();
    expect((Data.animData as AnimDataFile).rowCount).toBeGreaterThan(1000);
  });

  it('the name hash is an unsigned byte sum', () => {
    // 'P' + 'A' = 80 + 65 = 145, and lower case folds first.
    expect(AnimDataFile.hash('PA')).toBe(145);
    expect(AnimDataFile.hash('pa')).toBe(145);

    // Wraps at 256 rather than widening (0x66a926 accumulates into a byte).
    expect(AnimDataFile.hash('PA')).toBe(AnimDataFile.hash('PA') & 0xff);
  });

  it.each([
    [0, 'AMA11hs'], // Amazon
    [3, 'PAA11hs'], // Paladin
    [5, 'DZA11hs'], // Druid — PlrType row 5 after the Expansion divider is dropped
    [6, 'AIA11hs'], // Assassin
  ])('the animation name is token plus mode plus weapon class (%i)', (classId, name) => {
    // Short Sword's wclass is lower-case "1hs" in weapons.txt and is copied verbatim;
    // only ANIMDATA_GetRecordByNameHash upper-cases, and it does so on its own copy.
    expect(Calculator.animationName(item('ssd'), player(classId))).toBe(name);
  });

  it('there is no name without a viewer', () => {
    expect(Calculator.animationName(item('ssd'), null)).toBeNull();
    expect(Calculator.tryCalculate(item('ssd'), null, null)).toBeNull();
  });

  it('a paladin short sword resolves to a real animation', () => {
    const record = (Data.animData as AnimDataFile).tryGet('PAA11HS');
    expect(record).not.toBeNull();
    expect((record as NonNullable<typeof record>).framesPerDirection).toBeGreaterThan(0);
    expect((record as NonNullable<typeof record>).animationSpeed).toBeGreaterThan(0);

    const speed = Calculator.tryCalculate(item('ssd'), player(3), null);

    // (frames << 8) / (animSpeed * (0 + 100 + 0) / 100).
    expect(speed).toBe(
      Math.trunc(
        ((record as NonNullable<typeof record>).framesPerDirection << 8) /
          (record as NonNullable<typeof record>).animationSpeed,
      ),
    );
  });

  it('faster attack rate lowers the speed value', () => {
    const stats = new Map<number, number>();
    stats.set(ItemStatReader.packStatKey(0, 93), 40);

    const plain = Calculator.tryCalculate(item('ssd'), player(3), null);
    const hasted = Calculator.tryCalculate(item('ssd'), player(3), stats);

    expect(plain).not.toBeNull();
    expect(hasted).not.toBeNull();
    expect(hasted as number).toBeLessThan(plain as number);
  });

  it('an unknown animation falls back to forty-five', () => {
    // A ring has no wclass, so the name degenerates and misses every record. 0x62a7c5
    // returns 45 in that case rather than failing.
    const ring = new ItemIdentity();
    ring.classId = Items.classIdForCode('rin');

    expect(Calculator.tryCalculate(ring, player(3), null)).toBe(
      AttackSpeedCalculator.MissingAnimationSpeed,
    );
  });

  it('a speed 27 weapon with no player class lands on bucket zero', () => {
    // With no player unit the class index is -1 (0x486274), so dword_722078[2*-1] reads
    // dword_721F10's last dword — a 5. 5*(27-10)+5 = 90 then indexes ONE PAST that table,
    // onto dword_722078[0] = 0, and word_721E88[0] is locale 4088. A non-player viewer is a legal
    // call here even though the game never makes it, so this must not throw.
    //
    // The animation is the MONSTER one now, so the tuning stat is against "IWSC1hs" — the Act 3
    // mercenary, the one hireling whose mode-7 name resolves at all.
    const stats = new Map<number, number>();
    stats.set(ItemStatReader.packStatKey(0, 68), -34);

    expect(Calculator.tryCalculate(item('ssd'), monster(Act3Hireling), stats)).toBe(27);
  });

  it.each([
    [RogueHireling, 'RGSChth'],
    [Act2Hireling, 'GUSChth'],
    [Act3Hireling, 'IWSC1hs'],
    [Act5Hireling, '0ASC2hs'],
  ])('a mercenary names the animation from monstats (%i)', (classId, name) => {
    // COMPOSIT_BuildCofPath's monster arm 0x64f6db: monstats `Code` (+16) + MonMode[7] `token`
    // (+32) + COMPOSIT_ResolveWeaponClass, which for mode 7 returns monstats2 `BaseW` (+16)
    // reached through the `MonStatsEx` link (0x64f0e7). All three offsets and the link were read
    // back out of monstats.bin / monstats2.bin / monmode.bin.
    //
    // MonMode row 7 is CAST — "SC" — not attack. PlrMode row 7 is Attack1. The mode index is the
    // same literal 7 pushed at 0x62a7a2 for both.
    expect(Calculator.animationName(item('7o7'), monster(classId))).toBe(name);
  });

  it("a mercenary's weapon class comes from monstats2, not from the item", () => {
    // The caller seeds *a7 with the item's own wclass at 0x62a79c, but the monster arm OVERWRITES
    // it (0x64f751) — unlike the player arm, which keeps it because a9 is 0. So swapping a
    // two-handed polearm for a one-handed sword changes nothing.
    expect(Calculator.animationName(item('ssd'), monster(Act2Hireling))).toBe(
      Calculator.animationName(item('7o7'), monster(Act2Hireling)),
    );
  });

  it("a mercenary's ogre axe still writes the line", () => {
    // The reported symptom is a null line, because the name was built from
    // PlrType[classId] and no PlrType row 338 exists.
    //
    // "GUSChth" is not in AnimData.D2 — the Act 2 mercenary has no cast animation — so
    // ANIMDATA_GetFramesSpeedAndTrigger fails and 0x62a7c5 returns 45. 45 >= 28 takes the
    // 0x486231 arm, bucket 5, word_721E88[15] = locale 4093.
    expect(speedLineFor(monster(Act2Hireling), '7o7', 30)).toBe(
      'Polearm Class - ÿc3Very Slow Attack Speed\n',
    );

    expect(Calculator.tryCalculate(item('7o7'), monster(Act2Hireling), null)).toBe(
      AttackSpeedCalculator.MissingAnimationSpeed,
    );
  });

  it('the act three mercenary is the one hireling whose animation resolves', () => {
    // "IWSC1hs" IS in AnimData.D2 (18 frames at speed 256), so this one takes the real arithmetic
    // rather than the 45 fallback — which is what keeps the fallback from being the only thing the
    // monster arm is ever tested through.
    expect(speedLineFor(monster(Act3Hireling), '7o7', 30)).toBe(
      'Polearm Class - ÿc3Fast Attack Speed\n',
    );
  });

  it('times the line against the CLIENT PLAYER, not the viewer', () => {
    // INV_FormatAttackSpeedText never reads the tooltip's own unit. It calls GetPlayerUnit_0
    // (0x463de0) twice — 0x486201 for the frame lookup and 0x486250 for the bucket's class offset
    // — so hovering a MERC's polearm shows the speed the CHARACTER would swing it at. The merc is
    // still the viewer for everything else on the tooltip, which is why the two units have to be
    // supplied separately.
    //
    // A real capture is what settled this: the game drew `Very Fast` for a merc-equipped Bonehew,
    // and the merc's own animation ("GUSChth", absent from AnimData.D2) gives `Very Slow`.
    const stats = new Map<number, number>();
    stats.set(ItemStatReader.packStatKey(0, 93), 30);

    const types = new ItemTypeTree(Data.itemTypes);

    const build = (clientPlayer: ItemViewer | null): RecordSections =>
      new RecordSections(
        Data,
        Items,
        types,
        item('7o7'),
        monster(Act2Hireling),
        stats,
        null,
        new Map<number, number>(),
        null,
        clientPlayer,
      );

    expect(build(null).getSection(ItemTooltipSection.AttackSpeed)).toBe(
      'Polearm Class - ÿc3Very Slow Attack Speed\n',
    );

    expect(build(player(1)).getSection(ItemTooltipSection.AttackSpeed)).toBe(
      'Polearm Class - ÿc3Very Fast Attack Speed\n',
    );
  });

  it('a player viewer is untouched by the monster arm', () => {
    expect(Calculator.animationName(item('7o7'), player(3))).toBe('PAA1stf');

    expect(speedLineFor(player(3), '7o7', 30)).toBe('Polearm Class - ÿc3Very Fast Attack Speed\n');
  });

  it('a viewer that is neither player nor monster has no name', () => {
    // 0x64f5d1: unit types other than 0, 1 and 2 fall straight out of the switch with the name
    // buffer never written. Objects (2) have an arm, but no item is ever described against one, so
    // it is not modelled either.
    const objectViewer = new ItemViewer();
    objectViewer.unitType = 2;
    objectViewer.classId = Act2Hireling;
    expect(Calculator.animationName(item('7o7'), objectViewer)).toBeNull();

    // 0x64f6e6 range-checks the class against the monstats record count and, failing it, returns
    // leaving the buffer uninitialised. There is nothing defined to reproduce.
    expect(
      Calculator.animationName(item('7o7'), monster((Data.monsterStats as TxtFile).rowCount)),
    ).toBeNull();
  });
});
