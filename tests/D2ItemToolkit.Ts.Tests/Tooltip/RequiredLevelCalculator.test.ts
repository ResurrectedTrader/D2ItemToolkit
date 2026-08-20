import { describe, expect, it } from 'vitest';
import {
  ItemIdentity,
  ItemRecordFlags,
  ItemUnit,
  ItemViewer,
} from '../../../src/D2ItemToolkit.Ts/src/Stats/ItemRecord.js';
import { ItemStatReader } from '../../../src/D2ItemToolkit.Ts/src/Stats/ItemStatReader.js';
import { ItemTable } from '../../../src/D2ItemToolkit.Ts/src/Tables/ItemTable.js';
import { RequiredLevelCalculator } from '../../../src/D2ItemToolkit.Ts/src/Tooltip/RequiredLevelCalculator.js';
import { D2DataFiles } from '../../../src/D2ItemToolkit.Ts/src/Tables/TxtDataProviders.js';

// ITEM_CalcRequiredLevel 0x62b5b0, driven from the embedded tables. RequiredLevelTests.cs.

const Data = D2DataFiles.load();
const Items = new ItemTable(Data.weapons, Data.armor, Data.misc);
const Calculator = new RequiredLevelCalculator(Data, Items);

// ItemQualityNo, which lives with the name builder.
const Normal = 2;
const Magic = 4;
const Set = 5;
const Rare = 6;
const Unique = 7;
const Craft = 8;

function item(code: string, quality: number, fileIndex = -1): ItemIdentity {
  const it = new ItemIdentity();
  it.classId = Items.classIdForCode(code);
  it.code = code;
  it.quality = quality;
  it.fileIndex = fileIndex;
  it.flags = ItemRecordFlags.Identified;
  return it;
}

function level(
  subject: ItemIdentity,
  viewer: ItemViewer | null = null,
  stats: Map<number, number> | null = null,
  sockets: Map<number, number> | null = null,
  socketUnits: ItemUnit[] | null = null,
): number {
  return Calculator.calculate(subject, viewer, stats, socketUnits, sockets);
}

function stats(...layerStatValue: number[]): Map<number, number> {
  const map = new Map<number, number>();
  for (let i = 0; i + 2 < layerStatValue.length + 1; i += 3) {
    map.set(
      ItemStatReader.packStatKey(layerStatValue[i] as number, layerStatValue[i + 1] as number),
      layerStatValue[i + 2] as number,
    );
  }

  return map;
}

function firstClassSkill(): number {
  for (let skill = 0; skill < Data.skills.rowCount; ++skill) {
    const skillClass = Data.skills.getSkillClass(skill);
    if (skillClass >= 0 && skillClass <= 6 && Data.skills.requiredLevel(skill) > 1) {
      return skill;
    }
  }

  throw new Error('no class skill with a level requirement');
}

describe('RequiredLevelCalculator', () => {
  it('a unique takes its uniqueitems level', () => {
    // UniqueItems row 0 is The Gnasher, "lvl req" 5, on a Hand Axe whose levelreq is 0.
    expect(level(item('hax', Unique, 0))).toBe(5);
  });

  it('a set item takes its setitems level', () => {
    // SetItems row 0 is Civerb's Ward, "lvl req" 9, on a Large Shield.
    expect(level(item('lrg', Set, 0))).toBe(9);
  });

  it('a classic unique hides its level from a non-expansion viewer', () => {
    const subject = item('hax', Unique, 0);
    subject.format = 0;

    const viewer = new ItemViewer();
    viewer.unitType = 0;
    viewer.classId = 3;
    viewer.flagsEx = 0;

    expect(level(subject, viewer)).toBe(0);

    viewer.flagsEx = ItemViewer.UnitFlagExpansion;
    expect(level(subject, viewer)).toBe(5);
  });

  it('a magic item takes the highest of its two affixes', () => {
    // The magic array is 1-based over [MagicSuffix][MagicPrefix][automagic], so id 66 is
    // suffix row 65 — "of Regeneration", levelreq 52 and no class restriction.
    const subject = item('lrg', Magic);
    subject.magicSuffix[0] = 66;

    expect(level(subject)).toBe(52);
  });

  it('a magic item ignores affix slots one and two', () => {
    // GetMagicPrefix/GetMagicSuffix are called with index 0 only (0x62b5f2).
    const subject = item('lrg', Magic);
    subject.magicSuffix[1] = 66;
    subject.magicSuffix[2] = 66;

    expect(level(subject)).toBe(0);
  });

  it('a rare item reads every affix slot', () => {
    const subject = item('lrg', Rare);
    subject.magicSuffix[2] = 66;

    expect(level(subject)).toBe(52);
  });

  it('a crafted item adds ten plus three for each affix', () => {
    const subject = item('lrg', Craft);
    subject.magicSuffix[0] = 66;

    // 52 + 10 + 3 for the one affix that resolves.
    expect(level(subject)).toBe(65);
  });

  it('a crafted item is capped one below the maximum character level', () => {
    const subject = item('lrg', Craft);

    // Suffix row 339 is "of Vita", levelreq 97 — the highest in the table. Six affixes take
    // the raw total to 97 + 10 + 18 = 125, well past the ceiling.
    for (let slot = 0; slot < ItemIdentity.MaxAffixSlots; ++slot) {
      subject.magicSuffix[slot] = 340;
      subject.magicPrefix[slot] = 340;
    }

    // experience.txt MaxLvl is 99 and the cap is one below it (0x62b848).
    expect(level(subject)).toBe(98);

    // The cap applies to the crafted subtotal only; stat 92 is added afterwards at 0x62ba27.
    expect(level(subject, null, stats(0, 92, 5))).toBe(98 + 5);
  });

  it('the items table level is a floor', () => {
    // Find any item whose own levelreq is non-zero and check it shows through.
    for (let classId = 0; classId < Items.rowCount; ++classId) {
      const required = Items.requiredLevel(classId);
      if (required <= 1) {
        continue;
      }

      const subject = new ItemIdentity();
      subject.classId = classId;
      subject.quality = Normal;
      expect(level(subject)).toBe(required);
      return;
    }

    throw new Error('no items row carries a level requirement');
  });

  it('stat ninety-two is added on top', () => {
    expect(level(item('hax', Unique, 0), null, stats(0, 92, 7))).toBe(5 + 7);
  });

  it('a negative total clamps to zero', () => {
    expect(level(item('hax', Unique, 0), null, stats(0, 92, -50))).toBe(0);
  });

  it('a socketed filler raises the host requirement', () => {
    const host = item('lrg', Normal);

    let filler = -1;
    let fillerLevel = 0;
    for (let classId = 0; classId < Items.rowCount; ++classId) {
      if (Items.requiredLevel(classId) > 1) {
        filler = classId;
        fillerLevel = Items.requiredLevel(classId);
        break;
      }
    }

    expect(filler).toBeGreaterThanOrEqual(0);

    const sockets = new Map<number, number>([[0, filler]]);
    expect(level(host, null, null, sockets)).toBe(fillerLevel);
  });

  it('an off-class granted skill costs six extra levels', () => {
    const skill = firstClassSkill();
    const skillClass = Data.skills.getSkillClass(skill);
    const reqLevel = Data.skills.requiredLevel(skill);

    const host = item('lrg', Normal);

    const stranger = new ItemViewer();
    stranger.unitType = 0;
    stranger.classId = skillClass === 0 ? 1 : 0;

    const owner = new ItemViewer();
    owner.unitType = 0;
    owner.classId = skillClass;

    // Stat 97 is item_nonclassskill; the LAYER carries the skill id.
    const granted = stats(skill, 97, 1);

    expect(level(host, stranger, granted)).toBe(reqLevel + 6);
    expect(level(host, owner, granted)).toBe(reqLevel);
  });

  it('a single skill never takes the off-class penalty', () => {
    const skill = firstClassSkill();
    const reqLevel = Data.skills.requiredLevel(skill);

    // Stat 107 is item_singleskill, read at 0x62b927 with no class comparison at all.
    expect(level(item('lrg', Normal), null, stats(skill, 107, 1))).toBe(reqLevel);
  });

  it('a magic jewel in a socket raises the host requirement', () => {
    // 0x62b901 recurses the WHOLE calculation into every filler, so the jewel's own
    // quality affixes count. The concatenated magic array is 1-based over
    // [MagicSuffix][MagicPrefix][automagic], so a suffix row's id is its index plus one.
    const suffix = (Data.magicSuffix as NonNullable<typeof Data.magicSuffix>).findRow(
      'Name',
      'of Transcendence',
    );
    expect(suffix).toBeGreaterThanOrEqual(0);
    expect(
      (Data.magicSuffix as NonNullable<typeof Data.magicSuffix>).getInt(suffix, 'levelreq'),
    ).toBe(68);

    const host = item('lrg', Normal);

    const jewel = new ItemIdentity();
    jewel.classId = Items.classIdForCode('jew');
    jewel.quality = Magic;
    jewel.magicSuffix[0] = suffix + 1;

    expect(level(host, null, null, null, [new ItemUnit(jewel)])).toBe(68);

    // The classId-only view cannot see the affix, which is exactly the degradation the
    // richer overload exists to avoid.
    const byClassId = new Map<number, number>([[0, jewel.classId]]);
    expect(level(host, null, null, byClassId)).toBe(Items.requiredLevel(jewel.classId));
  });

  it('a socketed gem still contributes its items.txt level', () => {
    const host = item('lrg', Normal);

    const gem = new ItemIdentity();
    gem.classId = Items.classIdForCode('gpv'); // perfect amethyst

    expect(level(host, null, null, null, [new ItemUnit(gem)])).toBe(
      Items.requiredLevel(gem.classId),
    );
    expect(Items.requiredLevel(gem.classId)).toBeGreaterThan(1);
  });

  it("a filler's stat 92 reaches the host", () => {
    // The recursion adds the filler's OWN stat 92 (0x62ba27) before the max is taken.
    const host = item('lrg', Normal);

    const filler = new ItemIdentity();
    filler.classId = Items.classIdForCode('jew');

    const fillerStats = new Map<number, number>();
    fillerStats.set(ItemStatReader.packStatKey(0, 92), 55);

    expect(level(host, null, null, null, [new ItemUnit(filler, fillerStats)])).toBe(55);
  });
});
