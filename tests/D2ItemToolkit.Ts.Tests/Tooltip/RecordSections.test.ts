import { describe, expect, it } from 'vitest';
import { GemTable } from '../../../src/D2ItemToolkit.Ts/src/Tables/GemTable.js';
import { DamageStatIds } from '../../../src/D2ItemToolkit.Ts/src/Tooltip/ItemDamageLines.js';
import {
  type ItemDescriptionLine,
  ItemDescriptionGenerator,
} from '../../../src/D2ItemToolkit.Ts/src/Description/ItemDescription.js';
import { ItemQualityNo } from '../../../src/D2ItemToolkit.Ts/src/Tooltip/ItemNameBuilder.js';
import {
  ItemIdentity,
  ItemRecordFlags,
  ItemRecordReader,
  ItemViewer,
} from '../../../src/D2ItemToolkit.Ts/src/Stats/ItemRecord.js';
import { ItemStatOps } from '../../../src/D2ItemToolkit.Ts/src/Stats/ItemStatOps.js';
import {
  ItemStatReader,
  ItemStatView,
} from '../../../src/D2ItemToolkit.Ts/src/Stats/ItemStatReader.js';
import { unitFromJson, type Unit } from '../../../src/D2ItemToolkit.Ts/src/Stats/Unit.js';
import { ItemTable } from '../../../src/D2ItemToolkit.Ts/src/Tables/ItemTable.js';
import {
  type IItemTooltipSections,
  type ItemTooltipLine,
  ItemQuality,
  ItemTooltipColor,
  ItemTooltipComposer,
  ItemTooltipContext,
  ItemTooltipFlags,
  ItemTooltipKind,
  ItemTooltipSection,
} from '../../../src/D2ItemToolkit.Ts/src/Tooltip/ItemTooltip.js';
import { ItemTypeTree } from '../../../src/D2ItemToolkit.Ts/src/Tables/ItemTypeTree.js';
import { PropertyApplier } from '../../../src/D2ItemToolkit.Ts/src/Stats/PropertyApplier.js';
import {
  RecordSections,
  SectionStringIds,
} from '../../../src/D2ItemToolkit.Ts/src/Tooltip/RecordSections.js';
import { SkillDamage } from '../../../src/D2ItemToolkit.Ts/src/Tables/SkillDamage.js';
import { D2DataFiles } from '../../../src/D2ItemToolkit.Ts/src/Tables/TxtDataProviders.js';
import { DescStringIds, type IStatValueSource } from '../../../src/D2ItemToolkit.Ts/src/Types.js';

// EndToEndRecordTests.cs, SocketFillerTests.cs, WeaponDamageTests.cs, HolyShieldTests.cs,
// SpellDescriptionTests.cs, ElixirTests.cs, TooltipKindTests.cs, ProducerShapeTests.cs and
// RealDataTooltipTests.cs.

const Data = D2DataFiles.load();

const Items = new ItemTable(Data.weapons, Data.armor, Data.misc);

const Types = new ItemTypeTree(Data.itemTypes);

function classIdOf(code: string): number {
  const classId = Items.classIdForCode(code);
  expect(classId, 'no items row for code ' + code).toBeGreaterThanOrEqual(0);
  return classId;
}

function pairs(statValue: readonly number[]): Map<number, number> {
  const stats = new Map<number, number>();
  for (let i = 0; i + 1 < statValue.length; i += 2) {
    stats.set(ItemStatReader.packStatKey(0, statValue[i] as number), statValue[i + 1] as number);
  }

  return stats;
}

function identity(code: string, mutate?: (item: ItemIdentity) => void): ItemIdentity {
  const item = new ItemIdentity();
  item.classId = Items.classIdForCode(code);
  item.code = code;
  item.flags = ItemRecordFlags.Identified;
  expect(item.classId, 'no items row for ' + code).toBeGreaterThanOrEqual(0);

  if (mutate !== undefined) {
    mutate(item);
  }

  return item;
}

function single(
  lines: readonly ItemTooltipLine[],
  predicate: (line: ItemTooltipLine) => boolean,
): ItemTooltipLine {
  const matched = lines.filter(predicate);
  expect(matched).toHaveLength(1);
  return matched[0] as ItemTooltipLine;
}

function countMarkers(text: string): number {
  let count = 0;
  let at = text.indexOf(ItemTooltipColor.Marker);
  while (at >= 0) {
    ++count;
    at = text.indexOf(ItemTooltipColor.Marker, at + 1);
  }

  return count;
}

// =====================================================================================
// EndToEndRecordTests.cs
// =====================================================================================

/**
 * Turns the fixtures' shorthand player object into a real unit document: level, strength and
 * dexterity become stats 12, 0 and 2, and an active Holy Shield becomes a stat list carrying
 * state 101.
 */
function playerUnit(inline: Record<string, unknown>): Unit {
  function scalar(name: string): number {
    const value = inline[name];
    return typeof value === 'number' ? value : 0;
  }

  const lists: string[] = [
    '{ "stateNo": 0, "flags": 2147483648, "stats": [ ' +
      '{ "id": 12, "value": ' +
      String(scalar('level')) +
      ' }, ' +
      '{ "id": 0, "value": ' +
      String(scalar('strength')) +
      ' }, ' +
      '{ "id": 2, "value": ' +
      String(scalar('dexterity')) +
      ' } ] }',
  ];

  if (inline['holyShieldActive'] === true) {
    lists.push('{ "stateNo": 101, "flags": 64, "stats": [] }');
  }

  const json =
    '{ "unitType": ' +
    String(scalar('unitType')) +
    ', "classId": ' +
    String(scalar('classId')) +
    ', "skills": [ { "skill": 117, "level": ' +
    String(scalar('holyShieldLevel')) +
    ' } ]' +
    ', "statsLists": [ ' +
    lists.join(', ') +
    ' ] }';

  return unitFromJson(json);
}

interface Described {
  rendered: string;
  lines: readonly ItemTooltipLine[];
}

function describeRecord(json: string): Described {
  const record = unitFromJson(json);

  // Unit does not model the fixtures' inline player shorthand, so the raw document is
  // re-read just for that one field.
  const raw = JSON.parse(json) as Record<string, unknown>;

  const item = ItemRecordReader.readIdentity(record);

  // The player is a SEPARATE unit document. These fixtures still spell its attributes as
  // scalars for readability, so translate them into the stat lists the reader expects —
  // the same thing a caller with two real documents would already have.
  const inline = raw['player'];
  const viewer: ItemViewer | null =
    inline === undefined
      ? null
      : ItemRecordReader.readViewer(playerUnit(inline as Record<string, unknown>));

  const stats = ItemStatReader.reconstructView(record, ItemStatView.equipped());

  const sockets = ItemStatReader.readSockets(record);

  const baseStats = ItemStatReader.reconstructView(record, ItemStatView.baseOnly());

  // op 13 is folded into FullStats by the engine (0x626626) and is NOT in the captured
  // leaf lists, so it has to be re-applied to the merged view — and only to that view.
  ItemStatOps.resolve(stats, baseStats, Data.itemStatCost);

  const sections = new RecordSections(
    Data,
    Items,
    Types,
    item,
    viewer,
    stats,
    sockets,
    baseStats,
    ItemRecordReader.readSocketUnits(record),
  );

  // The section writers read the unit's stats through SERVER_GetUnitStat, so they see
  // everything; the modifier block is built from a temp list that only ever receives
  // 0x40 chain nodes (0x4e6452), so it gets its own view.
  const modifierStats = ItemStatReader.reconstructView(record, ItemStatView.modifiers());

  const composer = new ItemTooltipComposer(
    sections,
    sections.createModifierGenerator(modifierStats),
  );

  const lines = composer.compose(sections.createContext(), modifierStats);
  return { rendered: composer.render(lines), lines };
}

/**
 * A record whose base array carries `baseStats` and whose quality node carries `modStats`. Only
 * the latter can reach the modifier block: the base array is not in the chain GetStatList
 * 0x6257d0 walks.
 */
function recordWithMods(
  classId: number,
  extraItem: string,
  baseStats: string,
  modStats: string,
): string {
  return `{
    "classId": ${classId}, "quality": 2, ${extraItem},
    "statsLists": [
      { "source": "base", "stateNo": 0, "flags": 2147483648,
        "stats": [ ${baseStats} ] },
      { "source": "quality", "stateNo": 0, "flags": 64,
        "stats": [ ${modStats} ] } ] }`;
}

function record(classId: number, extraItem: string, _unused: string, stats: string): string {
  return `{
    "classId": ${classId}, "quality": 2, ${extraItem},
    "statsLists": [ { "source": "base", "stateNo": 0, "flags": 2147483648,
                    "stats": [ ${stats} ] } ] }`;
}

/**
 * A record whose stats are split between the item's own `base` list and a `quality` one,
 * which is what SERVER_GetUnitStat and GetStatUnsignedValue read separately.
 */
function layered(classId: number, flags: string, baseStats: string, bonusStats: string): string {
  return `{ "classId": ${classId}, "quality": 2, "itemFlags": ${flags},
    "statsLists": [
      { "stateNo": 0, "flags": 2147483648,
        "stats": [ ${baseStats} ] },
      { "stateNo": 0, "flags": 64,
        "stats": [ ${bonusStats} ] } ] }`;
}

describe('a record end to end', () => {
  it('a shield renders defense and requirements from the record', () => {
    // Large Shield: has reqstr and a block value in armor.txt.
    const classId = classIdOf('lrg');

    const { rendered } = describeRecord(
      record(
        classId,
        '"itemFlags": 16',
        '"requiredLevel": 0',
        `{ "id": 31, "value": 120 }, { "id": 72, "value": 40 },
          { "id": 73, "value": 62 }`,
      ),
    );

    const rows = rendered.split('\n');

    expect(rows).toContain('Defense: 120');
    expect(rows).toContain('Durability: 40 of 62');
    expect(rows.some(r => r.startsWith('Required Strength:'))).toBe(true);
  });

  function requiredStrength(classId: number, flags: number): number {
    const { lines } = describeRecord(
      record(classId, '"itemFlags": ' + String(flags), '"requiredLevel": 0', ''),
    );

    const text = single(lines, l => l.section === ItemTooltipSection.RequiredStrength)
      .text as string;

    return Number.parseInt(text.replace(/\D/g, ''), 10);
  }

  it('ethereal reduces the strength requirement by ten', () => {
    const classId = classIdOf('lrg');
    const plain = requiredStrength(classId, 16);
    const ethereal = requiredStrength(classId, 16 | 0x400000);

    expect(ethereal).toBe(plain - 10);
  });

  it('an ethereal socketed item names both states', () => {
    const classId = classIdOf('lrg');

    const { lines } = describeRecord(
      record(
        classId,
        '"itemFlags": ' + String(16 | 0x800 | 0x400000),
        '"requiredLevel": 0',
        '{ "id": 194, "value": 3 }',
      ),
    );

    const text = single(lines, l => l.section === ItemTooltipSection.EtherealSocketed)
      .text as string;

    expect(text).toContain('Ethereal');
    expect(text).toContain('Socketed (3)');
  });

  it('the required level line appears only above one', () => {
    const classId = classIdOf('lrg');

    // Large Shield has levelreq 0, so stat 92 (item_levelreq) is the whole requirement.
    const atOne = describeRecord(
      record(classId, '"itemFlags": 16', '', '{ "id": 92, "value": 1 }'),
    );
    expect(atOne.lines.some(l => l.section === ItemTooltipSection.RequiredLevel)).toBe(false);

    const higher = describeRecord(
      record(classId, '"itemFlags": 16', '', '{ "id": 92, "value": 41 }'),
    );

    expect(higher.rendered).toContain('Required Level: 41');
  });

  it('an unmet requirement turns that line red', () => {
    const classId = classIdOf('lrg');

    // Large Shield needs 34 strength; this player has it but is well short of level 41, so
    // exactly one of the two lines turns red.
    const { lines } = describeRecord(`{
      "classId": ${classId}, "quality": 2, "itemFlags": 16,
      "player": { "unitType": 0, "classId": 3, "level": 12,
                  "strength": 60, "dexterity": 60 },
      "statsLists": [ { "stateNo": 0, "flags": 2147483648,
          "stats": [ { "id": 92, "value": 41 } ] } ] }`);

    expect(single(lines, l => l.section === ItemTooltipSection.RequiredLevel).color).toBe(
      ItemTooltipColor.Red,
    );

    expect(single(lines, l => l.section === ItemTooltipSection.RequiredStrength).color).toBe(
      ItemTooltipColor.White,
    );
  });

  it('the stat block and the state lines render together bottom up', () => {
    const classId = classIdOf('lrg');

    const { rendered } = describeRecord(
      recordWithMods(
        classId,
        '"itemFlags": 16',
        '{ "id": 31, "value": 120 }',
        '{ "id": 39, "value": 30 }',
      ),
    );

    const rows = rendered.split('\n');

    // Defense is a section; Fire Resist is a DescFunc stat line. Both present, and the
    // stat block sits below the state lines because it is appended earlier.
    expect(rows).toContain('Defense: 120');
    expect(rows).toContain('Fire Resist +30%');

    expect(rows.indexOf('Defense: 120')).toBeLessThan(rows.indexOf('Fire Resist +30%'));
  });

  it('a paladin shield gets smite damage and a sorceress does not', () => {
    const classId = classIdOf('lrg');

    const paladin = describeRecord(`{
      "classId": ${classId}, "quality": 2, "itemFlags": 16,
      "player": { "unitType": 0, "classId": 3, "level": 40 },
      "runtime": { "smiteMin": 3, "smiteMax": 6 },
      "statsLists": [] }`);

    expect(paladin.lines.some(l => l.section === ItemTooltipSection.SmiteOrKickDamage)).toBe(true);

    const sorceress = describeRecord(`{
      "classId": ${classId}, "quality": 2, "itemFlags": 16,
      "player": { "unitType": 0, "classId": 1, "level": 40 },
      "statsLists": [] }`);

    expect(sorceress.lines.some(l => l.section === ItemTooltipSection.SmiteOrKickDamage)).toBe(
      false,
    );
  });

  it('a monster viewer does not trigger the class gated lines', () => {
    const classId = classIdOf('lrg');

    const { lines } = describeRecord(`{
      "classId": ${classId}, "quality": 2, "itemFlags": 16,
      "player": { "unitType": 1, "classId": 3 },
      "runtime": { "smiteMin": 3, "smiteMax": 6 },
      "statsLists": [] }`);

    // LoadItemDesc would emit Smite here (it checks dwClassId only, 0x48e75c).
    expect(lines.some(l => l.section === ItemTooltipSection.SmiteOrKickDamage)).toBe(false);
  });

  it('a unique item renders name state and stats in one description', () => {
    const classId = classIdOf('hax');

    const { rendered } = describeRecord(`{
      "classId": ${classId}, "quality": 7,
                  "itemFlags": ${16 | 0x800}, "fileIndex": 0,
      "player": { "unitType": 0, "classId": 1, "level": 40 },
      "runtime": {},
      "statsLists": [
          { "source": "base", "stateNo": 0, "flags": 2147483648,
            "stats": [ { "id": 194, "value": 2 },
                       { "id": 72, "value": 26 }, { "id": 73, "value": 28 } ] },
          { "source": "quality", "stateNo": 0, "flags": 64,
            "stats": [ { "id": 39, "value": 25 } ] } ] }`);

    const rows = rendered.split('\n');

    // GetItemName builds "base \n unique", and the renderer draws bottom-up, so the UNIQUE
    // name ends up on top with the base type beneath it — as in the game.
    expect(rows[0]).toBe('The Gnasher');
    expect(rows[1]).toBe('Hand Axe');
    // UniqueItems.txt row 0 carries "lvl req" 5, and Hand Axe has levelreq 0.
    expect(rows).toContain('Required Level: 5');
    expect(rows).toContain('Durability: 26 of 28');
    expect(rows).toContain('Socketed (2)');
    expect(rows).toContain('Fire Resist +25%');
  });

  it('a one handed weapon renders its damage range', () => {
    const classId = classIdOf('ssd');

    const { rendered } = describeRecord(
      record(
        classId,
        '"itemFlags": 16',
        '"requiredLevel": 0',
        '{ "id": 21, "value": 8 }, { "id": 22, "value": 15 }',
      ),
    );

    expect(rendered).toContain('One-Hand Damage: 8 to 15');
  });

  it('a two handed weapon uses the secondary stats and label', () => {
    // Two Handed Sword.
    const classId = classIdOf('2hs');

    const { rendered } = describeRecord(
      record(
        classId,
        '"itemFlags": 16',
        '"requiredLevel": 0',
        '{ "id": 23, "value": 9 }, { "id": 24, "value": 20 }',
      ),
    );

    expect(rendered).toContain('Two-Hand Damage: 9 to 20');
  });

  it('the damage line never shows a single value', () => {
    const classId = classIdOf('ssd');

    const { rendered } = describeRecord(
      record(
        classId,
        '"itemFlags": 16',
        '"requiredLevel": 0',
        '{ "id": 21, "value": 12 }, { "id": 22, "value": 12 }',
      ),
    );

    // 0x485928: max = MAX(max, min + 1), so equal min/max renders as N to N+1.
    expect(rendered).toContain('One-Hand Damage: 12 to 13');
  });

  it('a shield shows block chance including the class factor', () => {
    const classId = classIdOf('lrg');

    const { rendered } = describeRecord(`{
      "classId": ${classId}, "quality": 2, "itemFlags": 16,
      "player": { "unitType": 0, "classId": 3, "level": 40 },
      "statsLists": [ { "stateNo": 0, "flags": 2147483648,
          "stats": [ { "id": 20, "value": 20 } ] } ] }`);

    // Paladin BlockFactor is 30 in charstats, so 20 + 30 = 50. Large Shield's items.txt
    // block is 12, so the NUMBER carries colour 3 (0x485cea) behind the label's explicit
    // colour 0 (0x485d0e).
    expect(rendered).toContain(
      ItemTooltipColor.Marker + '0Chance to Block: ' + ItemTooltipColor.Marker + '350%',
    );
  });

  it('block chance is capped at seventy five', () => {
    const classId = classIdOf('lrg');

    const { rendered } = describeRecord(`{
      "classId": ${classId}, "quality": 2, "itemFlags": 16,
      "player": { "unitType": 0, "classId": 3, "level": 40 },
      "statsLists": [ { "stateNo": 0, "flags": 2147483648,
          "stats": [ { "id": 20, "value": 90 } ] } ] }`);

    expect(rendered).toContain(
      ItemTooltipColor.Marker + '0Chance to Block: ' + ItemTooltipColor.Marker + '375%',
    );
  });

  it('a class restricted item names the class', () => {
    // Amazon-only spear type.
    const classId = classIdOf('am1');

    const { lines } = describeRecord(record(classId, '"itemFlags": 16', '"requiredLevel": 0', ''));

    const restriction = lines.filter(l => l.section === ItemTooltipSection.ClassRestriction);

    expect(restriction).toHaveLength(1);
    expect((restriction[0] as ItemTooltipLine).text).toContain('Amazon');
  });

  it('a stackable item shows its quantity', () => {
    // Throwing knives stack.
    const classId = classIdOf('tkf');

    const { rendered } = describeRecord(
      record(classId, '"itemFlags": 16', '"requiredLevel": 0', '{ "id": 70, "value": 120 }'),
    );

    expect(rendered).toContain('Quantity: 120');
  });

  it('a charm gets the charm line', () => {
    const classId = classIdOf('cm1');

    const { lines } = describeRecord(record(classId, '"itemFlags": 16', '"requiredLevel": 0', ''));

    expect(lines.some(l => l.section === ItemTooltipSection.CharmDescription)).toBe(true);
  });

  it('a weapon shows its class and speed word', () => {
    const classId = classIdOf('ssd');

    const { lines } = describeRecord(`{
      "classId": ${classId}, "quality": 2, "itemFlags": 16,
      "player": { "unitType": 0, "classId": 3, "level": 40 },
      "runtime": { "attackSpeed": 15 },
      "statsLists": [] }`);

    const speed = single(lines, l => l.section === ItemTooltipSection.AttackSpeed);

    // Short Sword is under "swor", so the prefix is the Sword Class word.
    expect(speed.text).toContain('Sword Class');
    expect(speed.text).toContain('Attack Speed');
  });

  it('colours the speed word by the attack rate bonus, not the total', () => {
    // 0x486224 reads STATLIST_GetStatBonusFromLists 0x625560, which is merged MINUS base
    // (0x625570). Attack rate sitting on the item's own BASE array contributes nothing to the
    // bonus, so the word stays uncoloured even though the merged stat is non-zero.
    //
    // No shipped weapon has a base stat 93, so neither the corpus nor the adversarial sweep can
    // tell the two predicates apart — this is the only thing that pins it.
    const classId = classIdOf('ssd');
    const marker = ItemTooltipColor.Marker + '3';

    const speedLine = (flags: number): string => {
      const { lines } = describeRecord(`{
        "classId": ${classId}, "quality": 2, "itemFlags": 16,
        "player": { "unitType": 0, "classId": 3, "level": 40 },
        "statsLists": [
          { "stateNo": 0, "flags": ${flags}, "stats": [ { "id": 93, "value": 40 } ] } ] }`);

      return single(lines, l => l.section === ItemTooltipSection.AttackSpeed).text as string;
    };

    // On a magic list the whole 40 is bonus, so the word is coloured.
    expect(speedLine(64)).toContain(marker);

    // On the base list (0x80000000) merged and base agree, so the bonus is zero.
    expect(speedLine(2147483648)).not.toContain(marker);
  });

  it('a runeword lists its rune letters', () => {
    const classId = classIdOf('ssd');
    const amn = classIdOf('r11');
    const ral = classIdOf('r08');

    const { lines } = describeRecord(`{
      "classId": ${classId}, "quality": 2,
                  "itemFlags": ${16 | 0x800 | 0x04000000},
      "statsLists": [],
      "sockets": [ { "classId": ${ral} },
                   { "classId": ${amn} } ] }`);

    const runes = single(lines, l => l.section === ItemTooltipSection.RuneLetters);

    // Socket order, then a hardcoded apostrophe (0x486742).
    expect((runes.text as string).endsWith("'\n")).toBe(true);
    expect((runes.text as string).length).toBeGreaterThan(2);
  });

  it('a record with no player still describes the item', () => {
    const classId = classIdOf('lrg');

    const { rendered } = describeRecord(
      record(classId, '"itemFlags": 16', '"requiredLevel": 0', '{ "id": 31, "value": 99 }'),
    );

    expect(rendered).toContain('Defense: 99');
  });

  it('a boosted defense number is blue', () => {
    // 0x485fb1: the base stat 31 is 100 and the merged one 120, so the NUMBER — not the
    // label — carries colour 3 (0x4860de).
    const classId = classIdOf('lrg');

    const { rendered } = describeRecord(
      layered(classId, '16', '{ "id": 31, "value": 100 }', '{ "id": 31, "value": 20 }'),
    );

    expect(rendered).toContain('Defense: ' + ItemTooltipColor.Marker + '3120');
  });

  it('an unboosted defense number carries no marker', () => {
    const classId = classIdOf('lrg');

    const { rendered } = describeRecord(
      record(classId, '"itemFlags": 16', '', '{ "id": 31, "value": 120 }'),
    );

    expect(rendered).toContain('Defense: 120\n');
  });

  it('raises the durability max but never colours it', () => {
    // 0x484f0b reads STATLIST_GetStatBonusFromLists(item, 75, 0) — merged minus base (0x625570) —
    // and would prepend the marker to the MAX buffer alone (0x484fc6). On an ITEM that difference
    // is always zero, so the marker never appears: once stat 75 has landed on a non-zero target,
    // STATLIST_ApplyComplexStatFormula refuses to store the percent stat itself in FullStats
    // (0x626821 tests dwOwnerType == UNIT_ITEM, 0x626847 then skips the write at 0x626868).
    //
    // This asserted the marker for four rounds. A real capture settled it: the game draws
    // `Durability: 22 of 22` for a Superior Crystal Sword carrying +13% max durability.
    const classId = classIdOf('lrg');

    const { rendered } = describeRecord(
      layered(
        classId,
        '16',
        '{ "id": 72, "value": 40 }, { "id": 73, "value": 62 }',
        '{ "id": 75, "value": 25 }',
      ),
    );

    // 62 + trunc(62 * 25 / 100) = 77. The op still RESOLVES onto stat 73 — only the percent stat's
    // own entry is dropped.
    expect(rendered).toContain('Durability: 40 of 77\n');
    expect(rendered).not.toContain(ItemTooltipColor.Marker + '377');
  });

  it('an unidentified item shows no required level', () => {
    // 0x48e54f wraps the whole Required Level block in CheckItemFlag(item, 0x10).
    const classId = classIdOf('lrg');

    const identified = describeRecord(
      record(classId, '"itemFlags": 16', '', '{ "id": 92, "value": 41 }'),
    );
    expect(identified.lines.some(l => l.section === ItemTooltipSection.RequiredLevel)).toBe(true);

    const unidentified = describeRecord(
      record(classId, '"itemFlags": 0', '', '{ "id": 92, "value": 41 }'),
    );
    expect(unidentified.lines.some(l => l.section === ItemTooltipSection.RequiredLevel)).toBe(
      false,
    );
  });

  it('an unidentified stackable shows no quantity', () => {
    // AppendQuanity is reached only through CheckItemFlag(item, 0x10) at 0x48e8ef.
    const classId = classIdOf('tkf');

    const { rendered } = describeRecord(
      record(classId, '"itemFlags": 0', '', '{ "id": 70, "value": 120 }'),
    );

    expect(rendered).not.toContain('Quantity');
  });

  it('a socketed stackable shows no quantity', () => {
    // The second gate at 0x48e90d: CheckItemFlag(item, 0x800) must be CLEAR.
    const classId = classIdOf('tkf');

    const { rendered } = describeRecord(
      record(classId, '"itemFlags": ' + String(16 | 0x800), '', '{ "id": 70, "value": 120 }'),
    );

    expect(rendered).not.toContain('Quantity');
  });

  it('a throwing potion gets a single elemental throw damage line', () => {
    // 0x485459 tests tpot first and its arm COPIES the buffer, so none of the ordinary
    // one-hand or throw text survives. Rancid Gas Potion fires missile 49: 192 poison
    // over an ELen of 50, divided by 50/25 = 2 (0x4854fd), and min == max suppresses the
    // "to max" half (0x4855bd). Poison takes colour 2 from the table at 0x4854d0.
    const classId = classIdOf('gps');

    const { rendered } = describeRecord(record(classId, '"itemFlags": 16', '', ''));

    expect(rendered).toContain(
      ItemTooltipColor.Marker + '0Throw Damage: ' + ItemTooltipColor.Marker + '296',
    );

    expect(rendered).not.toContain('One-Hand Damage');
  });

  it('an oil potion shows a range in the fire colour', () => {
    // Fulminating Potion fires missile 44: physical 2-7 plus fire 3-8, both shifted by the
    // record's HitShift of 8 and shifted back at 0x48554c / 0x485559.
    const classId = classIdOf('opl');

    const { rendered } = describeRecord(record(classId, '"itemFlags": 16', '', ''));

    expect(rendered).toContain(
      ItemTooltipColor.Marker +
        '0Throw Damage: ' +
        ItemTooltipColor.Marker +
        '15 to ' +
        ItemTooltipColor.Marker +
        '115',
    );
  });

  it('a rune name is forced to colour eight', () => {
    // 0x48ea0c: IsOfType(item, 74).
    const classId = classIdOf('r01');

    const { lines } = describeRecord(record(classId, '"itemFlags": 16', '', ''));

    expect(single(lines, l => l.section === ItemTooltipSection.ItemName).color).toBe(
      ItemTooltipColor.Crafted,
    );
  });

  it('an essence name is forced to colour eight by its code', () => {
    // "tes " is one of the eleven dwords compared at 0x48e9b0; it is not a rune.
    const classId = classIdOf('tes');

    const { lines } = describeRecord(record(classId, '"itemFlags": 16', '', ''));

    expect(single(lines, l => l.section === ItemTooltipSection.ItemName).color).toBe(
      ItemTooltipColor.Crafted,
    );
  });

  it('a gem is not forced to colour eight', () => {
    const classId = classIdOf('gcv');

    const { lines } = describeRecord(record(classId, '"itemFlags": 16', '', ''));

    expect(single(lines, l => l.section === ItemTooltipSection.ItemName).color).toBe(
      ItemTooltipColor.White,
    );
  });

  it('a quest item name is gold', () => {
    // items.txt nQuest at +0x12A (0x48cb0b); the Horadric Cube leaves nQuestDiffCheck
    // blank, so it takes the 0x48ce6d arm outright.
    const classId = classIdOf('box');

    // The gold is in the name buffer's TEXT, not the section colour. AppendAsWideChar prepends,
    // so GetItemName's marker lands at the head of the buffer and LoadItemDesc then stacks v105 —
    // 0 for a normal-quality cube — in front of it. Asserting it as the section colour collapsed
    // the two markers the game draws into one.
    const { lines } = describeRecord(record(classId, '"itemFlags": 16', '', ''));

    const name = single(lines, l => l.section === ItemTooltipSection.ItemName);

    expect(name.color).toBe(ItemTooltipColor.White);
    expect((name.text ?? '').startsWith(ItemTooltipColor.Marker + '4')).toBe(true);
  });

  it("wirt's leg is excluded from the gold arm", () => {
    // 0x48ce59 compares the items.txt code dword against "leg " before colouring.
    const classId = classIdOf('leg');

    const { lines } = describeRecord(record(classId, '"itemFlags": 16', '', ''));

    expect(single(lines, l => l.section === ItemTooltipSection.ItemName).color).toBe(
      ItemTooltipColor.White,
    );
  });

  it('an empty socketed item is not named gemmed', () => {
    // 0x48c4b5 needs ITEM_ItemsInItem above zero as well as the 0x800 flag.
    const classId = classIdOf('lrg');

    const { rendered } = describeRecord(
      record(classId, '"itemFlags": ' + String(16 | 0x800), '', ''),
    );

    expect(rendered).not.toContain('Gemmed');
  });

  it('paired damage stats merge into one added damage line', () => {
    // SKILLDESC_BuildStatListDesc 0x4e49c0 latches the pair off the described unit's own
    // statlists, so the generator has to be built from the same stats the sections see.
    const classId = classIdOf('ssd');

    const { rendered } = describeRecord(
      recordWithMods(
        classId,
        '"itemFlags": 16',
        '',
        '{ "id": 48, "value": 15 }, { "id": 49, "value": 20 }',
      ),
    );

    expect(rendered).toContain('15-20');
  });

  // op 13: ItemStatCost's op stats are a REVERSE index — a row's `op stat1..3` name the
  // TARGETS it modifies, so 18 (mindamage%) drives 21/23/159 and 17 drives 22/24/160.

  it('enhanced damage scales the one hand numbers', () => {
    // Throwing Axe base 4-7 melee. +150% ED => 4+6=10, 7+10=17.
    const { rendered } = describeRecord(
      layered(
        classIdOf('tax'),
        '16',
        '{ "id": 21, "value": 4 }, { "id": 22, "value": 7 }',
        '{ "id": 18, "value": 150 }, { "id": 17, "value": 150 }',
      ),
    );

    expect(rendered).toContain('One-Hand Damage: ' + ItemTooltipColor.Marker + '310 to 17');
  });

  it('enhanced damage scales the throw numbers too', () => {
    // Same item, throw base 8-12 => 8+12=20, 12+18=30.
    const { rendered } = describeRecord(
      layered(
        classIdOf('tax'),
        '16',
        '{ "id": 159, "value": 8 }, { "id": 160, "value": 12 }',
        '{ "id": 18, "value": 150 }, { "id": 17, "value": 150 }',
      ),
    );

    const c3 = ItemTooltipColor.Marker + '3';
    expect(rendered).toContain('Throw Damage: ' + c3 + '20 to ' + c3 + '30');
  });

  it('a small percent truncates to nothing', () => {
    // Throwing Knife max throw 9; trunc(9 * 10 / 100) = 0, so the numbers do not move.
    const { rendered } = describeRecord(
      layered(
        classIdOf('tkf'),
        '16',
        '{ "id": 159, "value": 4 }, { "id": 160, "value": 9 }',
        '{ "id": 18, "value": 10 }, { "id": 17, "value": 10 }',
      ),
    );

    // And the line is NOT marked. pModified is base-vs-merged (0x485300) plus stats 272/273, and
    // nothing moved — while stats 17 and 18 are gone from an item's FullStats entirely once they
    // have landed on a non-zero target (0x626821 / 0x626847). The throw line is the one that states
    // colour 0 explicitly when unmodified: `esi = modified ? 3 : 0` at 0x485AEE-0x485AF2.
    //
    // This asserted colour 3 while the percent stat was still being left in the merged view.
    const c0 = ItemTooltipColor.Marker + '0';
    expect(rendered).toContain('Throw Damage: ' + c0 + '4 to ' + c0 + '9');
  });

  it('two percent sources sum before being applied once', () => {
    const { rendered } = describeRecord(
      layered(
        classIdOf('tax'),
        '16',
        '{ "id": 21, "value": 4 }, { "id": 22, "value": 7 }',
        '{ "id": 18, "value": 100 }, { "id": 17, "value": 100 }',
      ),
    );

    expect(rendered).toContain('8 to 14');
  });
});

// =====================================================================================
// SocketFillerTests.cs
// =====================================================================================

function fillerSections(code: string): RecordSections {
  return new RecordSections(
    Data,
    Items,
    Types,
    identity(code),
    null,
    new Map<number, number>(),
    null,
    null,
    null,
  );
}

function describeFiller(code: string): string | null {
  return fillerSections(code).getSection(ItemTooltipSection.SocketFillerDescription);
}

describe('the socket filler description', () => {
  it('the properties table loads and resolves names', () => {
    const applier = new PropertyApplier(Data, Items, Types);

    // properties.bin carries 268 records.
    expect(applier.properties.rowCount).toBe(268);

    const resAll = applier.properties.rowForCode('res-all');
    expect(resAll).toBeGreaterThanOrEqual(0);

    // "res-all" fans out to the four single resistances, so several sets carry a stat.
    const row = applier.properties.getRow(resAll);
    expect((row?.stat ?? []).filter(s => s >= 0).length).toBeGreaterThanOrEqual(4);
  });

  it('a perfect ruby describes what it does in each destination', () => {
    // gpr is the Perfect Ruby: fire damage in a weapon, life in armour, fire resist in a shield.
    const text = describeFiller('gpr');

    expect(text === null || text.length === 0).toBe(false);
    expect((text as string).toLowerCase()).toContain('fire');
  });

  it('a rune describes its destinations too', () => {
    // r08 is Ral: fire resist in armour and shields, fire damage in a weapon.
    const text = describeFiller('r08');

    expect(text === null || text.length === 0).toBe(false);
    expect((text as string).toLowerCase()).toContain('fire');
  });

  it('a perfect ruby matches the values the game grants', () => {
    // Real 1.14d Perfect Ruby: weapon +15-20 fire damage, armour/helm +38 life,
    // shield +40% fire resist.
    expect(describeFiller('gpr')).toBe(
      '\nShields: Fire Resist +40%' +
        '\nHelms: +38 to Life' +
        '\nArmor: +38 to Life' +
        '\nWeapons: Adds 15-20 fire damage' +
        '\n\nCan be Inserted into Socketed Items\n',
    );
  });

  it('a ral rune matches the values the game grants', () => {
    const text = describeFiller('r08') as string;

    expect(text).toContain('Shields: Fire Resist +35%');
    expect(text).toContain('Helms: Fire Resist +30%');
    expect(text).toContain('Armor: Fire Resist +30%');
    expect(text).toContain('Weapons: Adds 5-30 fire damage');
  });

  it('an unidentified item says so and an identified one does not', () => {
    const item = new ItemIdentity();
    item.classId = Items.classIdForCode('lrg');
    item.quality = ItemQualityNo.Unique;
    item.fileIndex = 0;

    const sections = new RecordSections(
      Data,
      Items,
      Types,
      item,
      null,
      new Map<number, number>(),
      null,
      null,
      null,
    );

    expect(sections.getSection(ItemTooltipSection.Unidentified)).toBe('Unidentified\n');

    item.flags = ItemRecordFlags.Identified;
    expect(
      new RecordSections(
        Data,
        Items,
        Types,
        item,
        null,
        new Map<number, number>(),
        null,
        null,
        null,
      ).getSection(ItemTooltipSection.Unidentified),
    ).toBeNull();
  });

  it('a non filler writes nothing', () => {
    expect(describeFiller('lrg')).toBeNull();
    expect(describeFiller('ssd')).toBeNull();
  });

  it('every gem and rune produces something', () => {
    const empty: string[] = [];

    const gems = Data.gems;
    for (let row = 0; row < (gems?.rowCount ?? 0); ++row) {
      const code = (gems as NonNullable<typeof gems>).getString(row, 'code').trim();
      if (code.length === 0 || Items.classIdForCode(code) < 0) {
        continue;
      }

      const text = describeFiller(code);
      if (text === null || text.length === 0) {
        empty.push(code);
      }
    }

    expect(empty).toEqual([]);
  });

  it('no gem property needs the item seed', () => {
    // A property with min != max would have to be rolled from the item seed.
    const applier = new PropertyApplier(Data, Items, Types);
    const gems = new GemTable(Data.gems, Items);
    gems.resolvePropertyCodesWith(code => applier.properties.rowForCode(code));

    const stats = new Map<number, number>();

    for (let row = 0; row < (Data.gems?.rowCount ?? 0); ++row) {
      const classId = Items.classIdForCode(
        (Data.gems as NonNullable<typeof Data.gems>).getString(row, 'code').trim(),
      );
      if (classId < 0) {
        continue;
      }

      const item = new ItemIdentity();
      item.classId = classId;

      for (let slot = 0; slot < 3; ++slot) {
        for (const property of gems.properties(row, slot)) {
          if (property.propertyId < 0) {
            break;
          }

          applier.apply(PropertyApplier.PropModeGem, item, property, stats);
        }
      }
    }

    expect([...applier.rolledRanges]).toEqual([]);
  });

  it('no gem property reaches an unimplemented func', () => {
    const applier = new PropertyApplier(Data, Items, Types);
    const gems = new GemTable(Data.gems, Items);
    gems.resolvePropertyCodesWith(code => applier.properties.rowForCode(code));

    const stats = new Map<number, number>();

    for (let row = 0; row < (Data.gems?.rowCount ?? 0); ++row) {
      const code = (Data.gems as NonNullable<typeof Data.gems>).getString(row, 'code').trim();
      const classId = Items.classIdForCode(code);
      if (classId < 0) {
        continue;
      }

      const item = new ItemIdentity();
      item.classId = classId;
      item.code = code;

      for (let slot = 0; slot < 3; ++slot) {
        for (const property of gems.properties(row, slot)) {
          if (property.propertyId < 0) {
            break;
          }

          applier.apply(PropertyApplier.PropModeGem, item, property, stats);
        }
      }
    }

    expect([...applier.unsupportedFunc]).toEqual([]);
  });

  it('a jewel still gets the socket filler trailer', () => {
    // LoadItemDesc routes every IsOfType(sock) item here (0x48e58c), and
    // SKILLDESC_BuildMagicAffixDesc bails at 0x4e6a7a for a jewel with the buffer merely
    // emptied (0x4e68bc). The 11080 + 3998 tail at 0x48661f is appended regardless.
    const item = identity('jew', i => {
      i.quality = ItemQualityNo.Magic;
    });

    const sections = new RecordSections(
      Data,
      Items,
      Types,
      item,
      null,
      new Map<number, number>(),
      null,
      null,
      null,
    );

    expect(sections.getSection(ItemTooltipSection.SocketFillerDescription)).toBe(
      (Data.strings.getByIndex(SectionStringIds.SocketFillerClose) as string) +
        (Data.strings.getByIndex(DescStringIds.Newline) as string),
    );
  });

  it('a non socket item gets no trailer at all', () => {
    const sections = new RecordSections(
      Data,
      Items,
      Types,
      identity('lrg'),
      null,
      new Map<number, number>(),
      null,
      null,
      null,
    );

    expect(sections.getSection(ItemTooltipSection.SocketFillerDescription)).toBeNull();
  });

  // Gems and runes join their per-slot lines differently.

  it('a gem slot with two stats joins them inline', () => {
    // Perfect Skull is the visible case: manasteal+lifesteal on weapons and
    // regen+regen-mana on helms are independent stats, so each slot yields two lines.
    const text = describeFiller('skz') as string;

    expect(text).toContain('Weapons: 4% Life stolen per hit, 3% Mana stolen per hit\n');
    expect(text).toContain('Helms: Regenerate Mana 19%, Replenish Life +5\n');
  });

  it('the whole skull block renders exactly', () => {
    expect(describeFiller('skz')).toBe(
      '\nShields: Attacker Takes Damage of 20\n' +
        'Helms: Regenerate Mana 19%, Replenish Life +5\n' +
        'Armor: Regenerate Mana 19%, Replenish Life +5\n' +
        'Weapons: 4% Life stolen per hit, 3% Mana stolen per hit\n' +
        '\nCan be Inserted into Socketed Items\n',
    );
  });

  it.each(['skc', 'skf', 'sku', 'skl', 'skz'])(
    'every skull joins its paired slots inline (%s)',
    code => {
      const text = describeFiller(code) as string;

      // Two stats on one row rather than two rows.
      expect(text).toContain(', ');
    },
  );

  it('a rune keeps the newline join', () => {
    // El Rune has two independent mods per slot, but the rune arm pushes 1, so each stat
    // keeps its own line. If this ever renders ", " the routing is wrong.
    const text = describeFiller('r01');

    expect(text === null || text.length === 0).toBe(false);
    expect(text as string).not.toContain(', ');
  });

  it('a single stat gem slot is unchanged by the join mode', () => {
    const ruby = describeFiller('gpr');

    expect(ruby === null || ruby.length === 0).toBe(false);
    expect(ruby as string).not.toContain(', ');
  });

  // gems row 0 is a real row.

  it('the first gems row is a real gem', () => {
    expect((Data.gems as NonNullable<typeof Data.gems>).getString(0, 'code').trim()).toBe('gcv');
  });

  it('a chipped amethyst describes all four destinations', () => {
    expect(describeFiller('gcv')).toBe(
      '\nShields: +8 Defense' +
        '\nHelms: +3 to Strength' +
        '\nArmor: +3 to Strength' +
        '\nWeapons: +40 to Attack Rating' +
        '\n\nCan be Inserted into Socketed Items\n',
    );
  });

  it('row zero is not confused with not a filler', () => {
    // The old `> 0` gate collapsed "row 0" into "no gems row", so gcv rendered only the
    // trailer while every other amethyst rendered in full.
    const chipped = describeFiller('gcv') as string;
    const flawed = describeFiller('gfv') as string;

    expect(chipped === null || chipped.length === 0).toBe(false);
    expect(chipped).toContain('Shields:');
    expect(chipped.split('\n')).toHaveLength(flawed.split('\n').length);
  });

  it('a rune letter still ignores row zero', () => {
    const gems = new GemTable(Data.gems, Items);

    expect(gems.rowForFillerClassId(Items.classIdForCode('gcv'))).toBe(0);
    expect(gems.rowForRuneClassId(Items.classIdForCode('gcv'))).toBe(-1);
  });
});

// =====================================================================================
// WeaponDamageTests.cs
// =====================================================================================

// Bastard Sword: 1or2handed AND 2handed are both set, so it is the case the Barbarian arm is
// for — usable in one hand by a Barbarian, two-handed by anyone else.
const Versatile = 'bsw';

function playerViewer(classId: number): ItemViewer {
  const viewer = new ItemViewer();
  viewer.unitType = 0;
  viewer.classId = classId;
  return viewer;
}

function damage(code: string, viewerClass: number | null, ...statValue: number[]): string | null {
  const item = identity(code);

  const viewer = viewerClass === null ? null : playerViewer(viewerClass);

  const stats = pairs(statValue);

  const sections = new RecordSections(Data, Items, Types, item, viewer, stats, null, stats, null);
  return sections.getSection(ItemTooltipSection.WeaponDamage);
}

function damageWithBase(merged: number[], baseValues: number[]): string | null {
  const item = new ItemIdentity();
  item.classId = Items.classIdForCode(Versatile);
  item.flags = ItemRecordFlags.Identified;

  const sections = new RecordSections(
    Data,
    Items,
    Types,
    item,
    playerViewer(3),
    pairs(merged),
    null,
    pairs(baseValues),
    null,
  );

  return sections.getSection(ItemTooltipSection.WeaponDamage);
}

function throwLine(merged: number[], baseValues: number[]): string | null {
  const item = identity('tax');

  const sections = new RecordSections(
    Data,
    Items,
    Types,
    item,
    playerViewer(3),
    pairs(merged),
    null,
    pairs(baseValues),
    null,
  );

  return sections.getSection(ItemTooltipSection.WeaponDamage);
}

describe('the weapon damage writer', () => {
  it('a barbarian sees both the two hand and the one hand line', () => {
    // Stats 23/24 are the two-hand pair, 21/22 the one-hand pair.
    const text = damage(Versatile, 4, 23, 20, 24, 40, 21, 10, 22, 25) as string;

    expect(text).toContain('Two-Hand Damage: 20 to 40');
    expect(text).toContain('One-Hand Damage: 10 to 25');

    // Two-hand FIRST (0x4856a2 before 0x4857c5), each line carrying its own colour 0.
    expect(text.indexOf('Two-Hand')).toBeLessThan(text.indexOf('One-Hand'));
    expect(countMarkers(text)).toBe(2);
  });

  it('every other class sees one line only', () => {
    for (const classId of [0, 1, 2, 3, 5, 6]) {
      const text = damage(Versatile, classId, 23, 20, 24, 40, 21, 10, 22, 25) as string;

      expect(text).not.toContain('One-Hand Damage');
      expect(text).toContain('Two-Hand Damage: 20 to 40');
    }
  });

  it('a viewerless tooltip takes the single line path', () => {
    const text = damage(Versatile, null, 23, 20, 24, 40, 21, 10, 22, 25) as string;

    expect(text).not.toContain('One-Hand Damage');
  });

  it('a barbarian on a plain two hander still gets one line', () => {
    // Maul is 2handed only.
    const text = damage('mau', 4, 23, 30, 24, 60) as string;

    expect(text).not.toContain('One-Hand Damage');
  });

  it('the single line path forces max above min but the dual path does not', () => {
    // 0x485931 clamps max to min + 1; the Barbarian arm has no such clamp.
    const singleLine = damage(Versatile, 3, 23, 40, 24, 40) as string;
    expect(singleLine).toContain('Two-Hand Damage: 40 to 41');

    const dual = damage(Versatile, 4, 23, 40, 24, 40, 21, 15, 22, 15) as string;
    expect(dual).toContain('Two-Hand Damage: 40 to 40');
    expect(dual).toContain('One-Hand Damage: 15 to 15');
  });

  it('an unmodified damage number carries no colour', () => {
    // base == merged, so INV_CalcWeaponDamageRange would leave pModified clear.
    const text = damage(Versatile, 3, 23, 20, 24, 40) as string;

    expect(text).not.toContain(ItemTooltipColor.Marker);
  });

  it.each([
    [10, 40, true], // min raised
    [20, 30, true], // max raised
    [20, 40, false], // untouched
  ])(
    'the min number is coloured when the base is below the merged (%i, %i)',
    (baseMin, baseMax, coloured) => {
      const text = damageWithBase([23, 20, 24, 40], [23, baseMin, 24, baseMax]);

      expect((text as string).includes(ItemTooltipColor.Marker + '3')).toBe(coloured);

      // The MAX never gets it: the shared number buffer is overwritten before it is appended.
      expect(countMarkers(text as string)).toBe(coloured ? 1 : 0);
    },
  );

  it('the marker sits after the label so the whole numeric run is coloured', () => {
    const text = damageWithBase([23, 20, 24, 40], [23, 10, 24, 40]) as string;

    // One marker, placed immediately before the MIN.
    expect(text).toBe('Two-Hand Damage: ' + ItemTooltipColor.Marker + '320 to 40\n');
    expect(countMarkers(text)).toBe(1);

    const at = text.indexOf(ItemTooltipColor.Marker);
    expect(text.substring(0, at)).toContain('Damage:');
    expect(text.substring(0, at)).not.toContain('20');
  });

  it('the composer carries the embedded colour to the end of the line', () => {
    const item = new ItemIdentity();
    item.classId = Items.classIdForCode(Versatile);
    item.flags = ItemRecordFlags.Identified;

    const sections = new RecordSections(
      Data,
      Items,
      Types,
      item,
      playerViewer(3),
      pairs([23, 20, 24, 40]),
      null,
      pairs([23, 10, 24, 40]),
      null,
    );

    const composer = new ItemTooltipComposer(
      sections,
      sections.createModifierGenerator(pairs([23, 20, 24, 40])),
    );
    const context = sections.createContext();

    const lines = composer.compose(context, pairs([23, 20, 24, 40]));

    expect(
      lines.some(
        l =>
          l.section === ItemTooltipSection.WeaponDamage &&
          (l.text as string).includes(ItemTooltipColor.Marker + '3'),
      ),
    ).toBe(true);
  });

  it.each([272, 273])('a by time damage stat alone colours the number (%i)', statId => {
    // 0x485372 / 0x4853eb set pModified from these even with base == merged.
    const text = damageWithBase([23, 20, 24, 40, statId, 5], [23, 20, 24, 40]) as string;

    expect(text).toContain(ItemTooltipColor.Marker + '3');
  });

  it('a weapon with no damage stats still gets a line', () => {
    // 0x48e704 gates on >= 0, so ZERO passes and the min+1 clamp yields "0 to 1".
    const text = damageWithBase([], []) as string;

    expect(text).toContain('Two-Hand Damage: 0 to 1');
  });

  it('a negative damage stat skips the section', () => {
    expect(damageWithBase([21, -1], [])).toBeNull();
  });

  // The throw line has its own emission shape.

  it('an unmodified throw line marks both numbers with colour zero', () => {
    const text = throwLine([159, 8, 160, 12], [159, 8, 160, 12]) as string;

    const c0 = ItemTooltipColor.Marker + '0';
    expect(text).toContain(c0 + 'Throw Damage: ' + c0 + '8 to ' + c0 + '12\n');
  });

  it('a modified throw line marks both numbers with colour three', () => {
    const text = throwLine([159, 8, 160, 20], [159, 8, 160, 12]) as string;

    const c0 = ItemTooltipColor.Marker + '0';
    const c3 = ItemTooltipColor.Marker + '3';
    expect(text).toContain(c0 + 'Throw Damage: ' + c3 + '8 to ' + c3 + '20\n');
  });

  it('an enhanced damage bonus alone marks the throw line', () => {
    // The pre-seed case: ED% moves neither 159 nor 160 in a leaf-summed view.
    const text = throwLine([159, 8, 160, 12, 18, 150, 17, 150], [159, 8, 160, 12]) as string;

    expect(text).toContain(
      ItemTooltipColor.Marker + '3' + '8 to ' + ItemTooltipColor.Marker + '3' + '12\n',
    );
  });

  it('the one hand line does not take the pre seed', () => {
    // 0x485662 zeroes the 1H/2H flag; only the throw block gets stats 18/17 folded in.
    const text = throwLine(
      [21, 4, 22, 7, 159, 8, 160, 12, 18, 150, 17, 150],
      [21, 4, 22, 7, 159, 8, 160, 12],
    ) as string;

    const oneHand = text.split('\n')[0] as string;
    expect(oneHand).not.toContain(ItemTooltipColor.Marker + '3');
  });
});

// =====================================================================================
// HolyShieldTests.cs — the parts driven through RecordSections
// =====================================================================================

function holyShieldDescribe(holyShieldLevel: number, active: boolean): string {
  const item = identity('lrg');

  const viewer = playerViewer(3);
  viewer.level = 40;
  viewer.skills.set(SkillDamage.HolyShieldSkillId, holyShieldLevel);
  if (active) {
    viewer.activeStates.add(SkillDamage.HolyShieldState);
  }

  const stats = new Map<number, number>();
  stats.set(ItemStatReader.packStatKey(0, 20), 25);

  const sections = new RecordSections(Data, Items, Types, item, viewer, stats, null, null, null);

  return (
    String(sections.getSection(ItemTooltipSection.SmiteOrKickDamage)) +
    '|' +
    String(sections.getSection(ItemTooltipSection.BlockChance))
  );
}

function smiteFor(code: string): string | null {
  const sections = new RecordSections(
    Data,
    Items,
    Types,
    identity(code),
    playerViewer(3),
    new Map<number, number>(),
    null,
    null,
    null,
  );

  return sections.getSection(ItemTooltipSection.SmiteOrKickDamage);
}

describe("holy shield's contribution", () => {
  it('an active holy shield raises smite damage and block chance', () => {
    const bare = holyShieldDescribe(0, false);
    const buffed = holyShieldDescribe(10, true);

    expect(buffed).not.toBe(bare);
    expect(buffed).toContain('Smite Damage');
  });

  it('an inactive holy shield contributes nothing even at high level', () => {
    expect(holyShieldDescribe(40, false)).toBe(holyShieldDescribe(0, false));
  });

  // 0x48e768/0x48e778: a class-restricted shield whose Class is not Paladin never gets the
  // smite line. `head` (Voodoo Heads) is Equiv1=shld with Class=nec.

  it.each(['ne1', 'ne9', 'nef'])(
    'a paladin gets no smite line on a necromancer head (%s)',
    code => {
      expect(smiteFor(code)).toBeNull();
    },
  );

  it('an unrestricted shield still smites', () => {
    expect(smiteFor('lrg')).not.toBeNull();
  });

  it('a paladin restricted shield still smites', () => {
    // ashd (Auric Shields, pa1..paf) is Class=pal, so the restriction matches.
    expect(smiteFor('pa1')).not.toBeNull();
    expect(smiteFor('paf')).not.toBeNull();
  });

  it('every voodoo head is refused', () => {
    const offenders: string[] = [];

    for (const code of [
      'ne1',
      'ne2',
      'ne3',
      'ne4',
      'ne5',
      'ne6',
      'ne7',
      'ne8',
      'ne9',
      'nea',
      'neb',
      'neg',
      'ned',
      'nee',
      'nef',
    ]) {
      if (smiteFor(code) !== null) {
        offenders.push(code);
      }
    }

    expect(offenders).toEqual([]);
  });
});

// =====================================================================================
// SpellDescriptionTests.cs
// =====================================================================================

const Amazon = 0;
const Sorceress = 1;
const Paladin = 3;
const Barbarian = 4;

function describeQuantity(code: string, classId: number | null, quantity = 0): string | null {
  const item = identity(code);

  const viewer = classId === null ? null : playerViewer(classId);

  const stats = new Map<number, number>();
  if (quantity !== 0) {
    stats.set(ItemStatReader.packStatKey(0, 70), quantity);
  }

  const sections = new RecordSections(Data, Items, Types, item, viewer, stats, null, null, null);
  return sections.getSection(ItemTooltipSection.QuantityAndSpellDescription);
}

describe('the quantity and spell description buffer', () => {
  it('a rejuv potion uses mode one and shows the string alone', () => {
    // rvs has spelldesc 1: the locale string with no value appended.
    const text = describeQuantity('rvs', Paladin);

    expect(text === null || text.length === 0).toBe(false);
    expect(text as string).not.toContain('Quantity');
  });

  it.each([
    ['hp3', Amazon, 150],
    ['hp3', Paladin, 150],
    ['hp3', Barbarian, 200],
    ['hp3', Sorceress, 100],
  ])('a healing potion scales per class (%s, %i)', (code, classId, expected) => {
    expect(describeQuantity(code, classId)).toContain(' ' + String(expected));
  });

  it.each([
    ['mp3', Amazon, 120],
    ['mp3', Paladin, 120],
    ['mp3', Barbarian, 80],
    ['mp3', Sorceress, 160],
  ])('a mana potion scales the other way (%s, %i)', (code, classId, expected) => {
    expect(describeQuantity(code, classId)).toContain(' ' + String(expected));
  });

  it('no viewer means no spell description at all', () => {
    // 0x4863a2 bails before every arm when there is no player unit.
    const text = describeQuantity('hp3', null);

    expect(text ?? '').not.toContain('100');
  });

  it('a spell description replaces the quantity line', () => {
    const text = describeQuantity('hp3', Paladin, 3) as string;

    expect(text).not.toContain('Quantity');
  });

  it('a stackable item shows its quantity even at zero', () => {
    // 0x486160 gates on `stat 70 > 0 OR maxstack > 0`.
    expect(describeQuantity('tkf', Paladin)).toContain('Quantity: 0');
  });

  it('a non stackable item with no spelldesc writes nothing', () => {
    expect(describeQuantity('lrg', Paladin)).toBeNull();
  });

  it('only modes one and two appear in the shipped tables', () => {
    const modes = new Set<number>();

    for (let classId = 0; classId < Items.rowCount; ++classId) {
      const mode = Items.getInt(classId, 'spelldesc');
      if (mode > 0) {
        modes.add(mode);
      }
    }

    expect([...modes].sort((a, b) => a - b)).toEqual([1, 2]);
  });
});

// =====================================================================================
// ElixirTests.cs
// =====================================================================================

const StatValue = 71;

function elixirSections(code: string, fileIndex: number, value: number): RecordSections {
  const item = identity(code, i => {
    i.quality = ItemQualityNo.Normal;
    i.fileIndex = fileIndex;
  });

  const stats = new Map<number, number>();
  if (value !== 0) {
    stats.set(ItemStatReader.packStatKey(0, StatValue), value);
  }

  return new RecordSections(Data, Items, Types, item, null, stats, null, null, null);
}

function describeElixir(code: string, fileIndex: number, value: number): string | null {
  return elixirSections(code, fileIndex, value).getSection(ItemTooltipSection.Modifiers);
}

describe('the elixir description', () => {
  it.each([0, 1, 2, 3])('an attribute elixir names what it raises (%i)', fileIndex => {
    const text = describeElixir('elx', fileIndex, 5);

    expect(text === null || text.length === 0, 'fileIndex ' + String(fileIndex)).toBe(false);
    expect(text as string).toContain('5');
    expect((text as string).endsWith('\n')).toBe(true);
  });

  it('the four attribute elixirs all name something different', () => {
    const names = [0, 1, 2, 3].map(f => describeElixir('elx', f, 5));

    expect(new Set(names).size).toBe(4);
  });

  it.each([7, 9])('a life or mana elixir shifts the value down by eight (%i)', fileIndex => {
    // 20 << 8 renders as 20; the four attribute entries would show the raw number.
    expect(describeElixir('elx', fileIndex, 20 << 8)).toContain('20');
    expect(describeElixir('elx', fileIndex, 20 << 8)).not.toContain(String(20 << 8));
  });

  it('an attribute elixir does not shift', () => {
    expect(describeElixir('elx', 0, 5)).toContain('5');
  });

  it('a zero value writes nothing', () => {
    // 0x4e5f7d skips the whole emission when the value is zero.
    expect(describeElixir('elx', 0, 0)).toBeNull();
  });

  it('a negative value omits the plus', () => {
    const positive = describeElixir('elx', 0, 5) as string;
    const negative = describeElixir('elx', 0, -5) as string;

    expect(negative === null || negative.length === 0).toBe(false);

    // 0x4e5fe5 prefixes locale 4002 only on the positive branch.
    expect(negative).toContain('-5');
    expect(negative).not.toBe(positive.replace(/5/g, '-5'));
  });

  it('a file index outside the table writes nothing', () => {
    // Only 0, 1, 2, 3, 9 and 7 appear in the six entries.
    expect(describeElixir('elx', 4, 5)).toBeNull();
    expect(describeElixir('elx', 42, 5)).toBeNull();
  });

  it.each([
    [0, 5, 'Elixir of Strength +5\n'],
    [1, 5, 'Elixir of Energy +5\n'],
    [2, 5, 'Elixir of Dexterity +5\n'],
    [3, 5, 'Elixir of Vitality +5\n'],
    [7, 20 << 8, 'Elixir of Life +20\n'],
    [9, 20 << 8, 'Elixir of Mana +20\n'],
    [0, -5, 'Elixir of Strength -5\n'],
  ])('the elixir line renders exactly (%i, %i)', (fileIndex, value, expected) => {
    expect(describeElixir('elx', fileIndex, value)).toBe(expected);
  });

  it('a non elixir never takes this path', () => {
    expect(describeElixir('lrg', 0, 5)).toBeNull();
    expect(describeElixir('gpr', 0, 5)).toBeNull();
  });

  it('the elixir line replaces the generated modifier block', () => {
    // Give the item a stat the normal engine WOULD describe, and prove it does not appear.
    const item = new ItemIdentity();
    item.classId = Items.classIdForCode('elx');
    item.quality = ItemQualityNo.Normal;
    item.flags = ItemRecordFlags.Identified;
    item.fileIndex = 0;

    const stats = new Map<number, number>();
    stats.set(ItemStatReader.packStatKey(0, StatValue), 5);
    stats.set(ItemStatReader.packStatKey(0, 39), 25); // fire resist

    const sections = new RecordSections(Data, Items, Types, item, null, stats, null, null, null);
    const composer = new ItemTooltipComposer(
      sections,
      new ItemDescriptionGenerator(Data.itemStatCost, Data.strings),
    );

    const context = new ItemTooltipContext();
    context.quality = ItemQuality.Normal;
    context.flags = ItemTooltipFlags.Identified;

    const lines = composer.compose(context, stats);
    const text = composer.render(lines);

    expect(text).not.toContain('Fire Resist');
    expect(lines.some(l => l.section === ItemTooltipSection.Modifiers)).toBe(true);
  });
});

// =====================================================================================
// TooltipKindTests.cs
// =====================================================================================

function contextFor(code: string): ItemTooltipContext {
  return new RecordSections(
    Data,
    Items,
    Types,
    identity(code),
    null,
    new Map<number, number>(),
    null,
    null,
    null,
  ).createContext();
}

function sectionsFor(code: string): RecordSections {
  return new RecordSections(
    Data,
    Items,
    Types,
    identity(code),
    null,
    new Map<number, number>(),
    null,
    null,
    null,
  );
}

function renderBook(code: string, quantity: number, shopMode: number, spell = 0): string {
  const item = identity(code, i => {
    // GetItemName's tome/scroll arm at 0x48c542 picks the spell from MagicSuffix[0]
    // (2199/2201 for a tome), not from the item code.
    i.magicSuffix[0] = spell;
  });

  const stats = new Map<number, number>();
  if (quantity > 0) {
    stats.set(ItemStatReader.packStatKey(0, 70), quantity);
  }

  const sections = new RecordSections(Data, Items, Types, item, null, stats, null, null, null);

  const composer = new ItemTooltipComposer(sections, sections.createModifierGenerator(stats));

  const context = sections.createContext();
  context.shopMode = shopMode;

  return composer.render(composer.composeBook(context));
}

describe('the tooltip kind the item data implies', () => {
  it.each(['tbk', 'ibk'])('a tome is classified as a book (%s)', code => {
    const context = contextFor(code);

    expect(context.isBook).toBe(true);
    expect(ItemTooltipComposer.classify(context)).toBe(ItemTooltipKind.Book);
  });

  it.each(['tsc', 'isc', 'gpr', 'lrg', 'ssd'])('a non tome is not a book (%s)', code => {
    const context = contextFor(code);

    expect(context.isBook).toBe(false);
    expect(ItemTooltipComposer.classify(context)).toBe(ItemTooltipKind.Generic);
  });

  it('exactly two shipped codes are books', () => {
    const books: string[] = [];

    for (let classId = 0; classId < Items.rowCount; ++classId) {
      const code = Items.code(classId);
      if (code === null || code.length === 0) {
        continue;
      }

      if (contextFor(code.trim()).isBook) {
        books.push(code.trim());
      }
    }

    books.sort();
    expect(books).toEqual(['ibk', 'tbk']);
  });

  it('a book is refused by the generic compose path', () => {
    const sections = sectionsFor('tbk');

    const composer = new ItemTooltipComposer(
      sections,
      sections.createModifierGenerator(new Map<number, number>()),
    );

    expect(() => composer.compose(sections.createContext(), new Map<number, number>())).toThrow();
  });

  // 0x48ec3f: the quest-usage line.

  it('the horadric cube says right click to open', () => {
    expect(sectionsFor('box').getSection(ItemTooltipSection.QuestUsage)).toBe(
      'Right Click to Open\n',
    );
  });

  it('the cairn stones key says right click to read', () => {
    expect(sectionsFor('bkd').getSection(ItemTooltipSection.QuestUsage)).toBe(
      'Right Click to Read\n',
    );
  });

  it("wirt's leg is excluded from the quest usage line", () => {
    expect(sectionsFor('leg').getSection(ItemTooltipSection.QuestUsage)).toBeNull();
  });

  it('a quest item without a usage line writes nothing', () => {
    // 24 other quest items pass the outer gate and fall to the colour-only branch
    // at 0x48ece5, emitting no line at all.
    expect(sectionsFor('hdm').getSection(ItemTooltipSection.QuestUsage)).toBeNull();
  });

  it('a non quest item writes nothing', () => {
    expect(sectionsFor('lrg').getSection(ItemTooltipSection.QuestUsage)).toBeNull();
    expect(sectionsFor('gpr').getSection(ItemTooltipSection.QuestUsage)).toBeNull();
  });

  it('the quest usage line renders as the bottom row', () => {
    // D2WINFONT_DrawWideString 0x501a80 does y += lineHeight / -10 at 0x501c17, so
    // position 0 is the bottom row. Appended first => drawn last => lowest.
    const sections = sectionsFor('box');

    const composer = new ItemTooltipComposer(
      sections,
      sections.createModifierGenerator(new Map<number, number>()),
    );

    const rendered = composer.render(
      composer.compose(sections.createContext(), new Map<number, number>()),
    );

    // The gold marker is INSIDE the name buffer (GetItemName prepends it at 0x48ce6d), so it is
    // part of the section's text and survives the marker-free render — the same way every other
    // writer-embedded marker does.
    expect(rendered).toBe('ÿc4Horadric Cube\nRight Click to Open');

    // And with markers: v105 is 0 for a normal-quality cube, and 0x48ecf2's colour 4 lands on the
    // bottom row. Character for character what a real capture holds.
    expect(
      composer.renderWithColorCodes(
        composer.compose(sections.createContext(), new Map<number, number>()),
      ),
    ).toBe('ÿc0ÿc4Horadric Cube\nÿc4Right Click to Open');
  });

  // op 2..5 scale by the VIEWER's stat, not the item's.

  function perLevelLine(statId: number, storedValue: number, viewerLevel: number): string {
    const item = identity('lrg');

    let viewer: ItemViewer | null = null;
    if (viewerLevel >= 0) {
      viewer = playerViewer(3);
      viewer.level = viewerLevel;
      viewer.stats.set(ItemStatReader.packStatKey(0, 12), viewerLevel);
    }

    const stats = new Map<number, number>();
    stats.set(ItemStatReader.packStatKey(0, statId), storedValue);

    const sections = new RecordSections(Data, Items, Types, item, viewer, stats, null, null, null);

    const composer = new ItemTooltipComposer(sections, sections.createModifierGenerator(stats));

    return composer.render(composer.compose(sections.createContext(), stats));
  }

  it('a per level stat scales by the viewer level', () => {
    // ItemStatCost 214 item_armor_percent_perlevel, op 2, op base level, op param 8.
    expect(perLevelLine(214, 16, 50)).toContain('+100 Defense (Based on Character Level)');
  });

  it('the same stat scales differently at a different level', () => {
    expect(perLevelLine(214, 16, 20)).not.toBe(perLevelLine(214, 16, 60));
  });

  it('a viewerless tooltip scales to zero but still emits the line', () => {
    expect(perLevelLine(214, 16, -1)).toContain('(Based on Character Level)');
  });

  // INV_ShowBookTooltip 0x48d060.

  it('a tome of town portal renders its whole tooltip', () => {
    expect(renderBook('tbk', 20, 0)).toBe(
      'Tome of Town Portal\nInsert Scrolls\nRight Click to Use\nQuantity: 20',
    );
  });

  it('a tome of identify renders its whole tooltip', () => {
    expect(renderBook('ibk', 20, 0, 1)).toBe(
      'Tome of Identify\nInsert Scrolls\nRight Click to Use\nQuantity: 20',
    );
  });

  it('an identify tome in a shop loses the usage lines too', () => {
    expect(renderBook('ibk', 20, 1, 1)).toBe('Tome of Identify\nQuantity: 20');
  });

  it('both tomes differ only in their name row', () => {
    const portal = renderBook('tbk', 20, 0);
    const identify = renderBook('ibk', 20, 0, 1);

    expect(identify).not.toBe(portal);
    expect(portal.split('\n').slice(1).join('\n')).toBe(identify.split('\n').slice(1).join('\n'));
  });

  it('the spell comes from the suffix not the item code', () => {
    expect(renderBook('ibk', 20, 0, 1).startsWith('Tome of Identify\n')).toBe(true);
    expect(renderBook('tbk', 20, 0, 1).startsWith('Tome of Identify\n')).toBe(true);
    expect(renderBook('ibk', 20, 0).startsWith('Tome of Town Portal\n')).toBe(true);
  });

  it.each([1, 5, 9, 10])('a shop mode drops both usage lines (%i)', shopMode => {
    const text = renderBook('tbk', 20, shopMode);

    expect(text).not.toContain('Insert Scrolls');
    expect(text).not.toContain('Right Click to Use');
    expect(text).toBe('Tome of Town Portal\nQuantity: 20');
  });

  it('the book quantity is not gated like the generic one', () => {
    // 0x48d07d has no identified / not-socketed test, unlike 0x48e8ef / 0x48e90d.
    const item = new ItemIdentity();
    item.classId = Items.classIdForCode('tbk');
    item.code = 'tbk';
    item.flags = ItemRecordFlags.None; // unidentified

    const stats = new Map<number, number>();
    stats.set(ItemStatReader.packStatKey(0, 70), 7);

    const sections = new RecordSections(Data, Items, Types, item, null, stats, null, null, null);

    expect(sections.getSection(ItemTooltipSection.BookQuantity)).toBe('Quantity: 7\n');
    expect(sections.getSection(ItemTooltipSection.QuantityAndSpellDescription)).toBeNull();
  });

  it('a book tooltip carries no colour markers', () => {
    expect(renderBook('tbk', 20, 0)).not.toContain(ItemTooltipColor.Marker);
  });

  it('the generic path still refuses a book', () => {
    const sections = sectionsFor('tbk');

    const composer = new ItemTooltipComposer(
      sections,
      sections.createModifierGenerator(new Map<number, number>()),
    );

    expect(() => composer.compose(sections.createContext(), new Map<number, number>())).toThrow();
  });

  it('the book path refuses a non book', () => {
    const sections = sectionsFor('lrg');

    const composer = new ItemTooltipComposer(
      sections,
      sections.createModifierGenerator(new Map<number, number>()),
    );

    expect(() => composer.composeBook(sections.createContext())).toThrow();
  });
});

// =====================================================================================
// ProducerShapeTests.cs
// =====================================================================================

// A Paladin holding a socketed Large Shield with a Perfect Ruby in it. unitType 4 is UNIT_ITEM.
const ItemDoc = `{
  "unitType": 4,
  "classId": %LRG%,
  "code": "lrg",
  "quality": 2,
  "itemFlags": 2064,
  "format": 100,
  "fileIndex": -1,
  "rarePrefix": 0,
  "rareSuffix": 0,
  "autoAffix": 0,
  "magicPrefix": [ 0, 0, 0 ],
  "magicSuffix": [ 0, 0, 0 ],
  "earLevel": 0,
  "playerName": "",
  "statsLists": [
    { "source": "base", "stateNo": 0, "flags": 2147483648,
      "stats": [ { "id": 31, "value": 120 }, { "id": 72, "value": 40 },
                 { "id": 73, "value": 62 }, { "id": 194, "value": 1 },
                 { "id": 20, "value": 25 } ] }
  ],
  "sockets": [
    { "unitType": 4, "classId": %GPR%, "code": "gpr", "quality": 2,
      "itemFlags": 16, "format": 100, "fileIndex": -1,
      "magicPrefix": [ 0, 0, 0 ], "magicSuffix": [ 0, 0, 0 ],
      "statsLists": [
        { "source": "quality", "stateNo": 0, "flags": 64,
          "stats": [ { "id": 39, "value": 40 } ] }
      ] }
  ]
}`;

// unitType 0 is UNIT_PLAYER. Attributes are stats; the skill level is a skills entry.
const PlayerDoc = `{
  "unitType": 0,
  "classId": 3,
  "flagsEx": 33554432,
  "name": "Bob",
  "skills": [ { "skill": 117, "level": 20 } ],
  "statsLists": [
    { "source": "base", "stateNo": 0, "flags": 0,
      "stats": [ { "id": 12, "value": 60 }, { "id": 0, "value": 120 },
                 { "id": 2, "value": 90 } ] },
    { "source": "other", "stateNo": 101, "flags": 0,
      "stats": [ { "id": 20, "value": 35 } ] }
  ]
}`;

function producerItemDoc(): Unit {
  return unitFromJson(
    ItemDoc.replace('%LRG%', String(Items.classIdForCode('lrg'))).replace(
      '%GPR%',
      String(Items.classIdForCode('gpr')),
    ),
  );
}

function renderProducerShape(): string {
  const item = producerItemDoc();
  const player = unitFromJson(PlayerDoc);

  const identityDoc = ItemRecordReader.readIdentity(item);
  const viewer = ItemRecordReader.readViewer(player);

  const sections = new RecordSections(
    Data,
    Items,
    Types,
    identityDoc,
    viewer,
    ItemStatReader.reconstructView(item, ItemStatView.equipped()),
    ItemStatReader.readSockets(item),
    ItemStatReader.reconstructView(item, ItemStatView.baseOnly()),
    ItemRecordReader.readSocketUnits(item),
  );

  const modifierStats = ItemStatReader.reconstructView(item, ItemStatView.modifiers());

  const composer = new ItemTooltipComposer(
    sections,
    sections.createModifierGenerator(modifierStats),
  );

  return composer.render(composer.compose(sections.createContext(), modifierStats));
}

describe("the producer's own document shape", () => {
  it('reconstructs a full description', () => {
    const text = renderProducerShape();

    expect(text.length).toBeGreaterThan(0);

    // From the item's identity and items.txt.
    expect(text).toContain('Large Shield');

    // Merged from the base list plus the socketed ruby's 40.
    expect(text).toContain('Defense: 120');
    expect(text).toContain('Durability: 40 of 62');
    expect(text).toContain('Socketed (1)');
  });

  it('drives every viewer dependent line from the player document', () => {
    const text = renderProducerShape();

    // Smite needs classId 3 read off the player document.
    expect(text).toContain('Smite Damage');

    // Block chance = item stat 20 (25) + charstats BlockFactor + Holy Shield at level 20.
    expect(text).toContain('Chance to Block');

    const viewer = ItemRecordReader.readViewer(unitFromJson(PlayerDoc));

    expect(viewer.level).toBe(60);
    expect(viewer.strength).toBe(120);
    expect(viewer.dexterity).toBe(90);
    expect(viewer.isExpansion).toBe(true);
    expect(viewer.skillLevel(SkillDamage.HolyShieldSkillId)).toBe(20);
    expect(viewer.activeStates.has(SkillDamage.HolyShieldState)).toBe(true);
  });

  it('reconstructs the socket as an item in its own right', () => {
    const item = producerItemDoc();

    let seen = 0;
    for (const socket of ItemStatReader.enumerateSockets(item)) {
      ++seen;
      const filler = ItemRecordReader.readIdentity(socket);

      expect(filler.code).toBe('gpr');
      expect(filler.classId).toBe(Items.classIdForCode('gpr'));
      expect(filler.has(ItemRecordFlags.Identified)).toBe(true);

      // And the same section builder describes it, because it is just an item.
      const sections = new RecordSections(
        Data,
        Items,
        Types,
        filler,
        null,
        ItemStatReader.reconstructView(socket, ItemStatView.equipped()),
        null,
        null,
        null,
      );

      expect(sections.getSection(ItemTooltipSection.SocketFillerDescription)).toContain(
        'Fire Resist',
      );
    }

    expect(seen).toBe(1);
  });
});

// =====================================================================================
// RealDataTooltipTests.cs — the composer over the real tables
// =====================================================================================

class Values implements IStatValueSource {
  readonly base = new Map<number, number>();
  readonly item = new Map<number, number>();
  readonly player = new Map<number, number>();
  playerClassId = 1;
  itemType = -1;

  // Non-zero, so the "Indestructible" tail is not appended to every block.
  maxDurability = 20;

  getBaseStatValue(statId: number, _layer: number): number {
    return this.base.get(statId) ?? 0;
  }

  // The op 2-5 scale reads the PLAYER (0x4e4c93).
  getPlayerStatValue(statId: number): number {
    return this.player.get(statId) ?? 0;
  }

  getItemStatValue(statId: number): number {
    return this.item.get(statId) ?? 0;
  }

  get playerClass(): number {
    return this.playerClassId;
  }

  isItemOfType(itemTypeId: number): boolean {
    return itemTypeId === this.itemType;
  }

  get describedUnitIsItem(): boolean {
    return true;
  }

  get itemTableAllowsDurability(): boolean {
    return true;
  }

  getTxtMaxDurability(): number {
    return this.maxDurability;
  }
}

class FakeSections implements IItemTooltipSections {
  private readonly texts = new Map<ItemTooltipSection, string>();
  private readonly unmet = new Set<ItemTooltipSection>();

  get lineTerminator(): string | null {
    return Data.strings.getByIndex(DescStringIds.Newline);
  }

  set(section: ItemTooltipSection, text: string): FakeSections {
    this.texts.set(section, text);
    return this;
  }

  unmeetable(section: ItemTooltipSection): FakeSections {
    this.unmet.add(section);
    return this;
  }

  getSection(section: ItemTooltipSection): string | null {
    return this.texts.get(section) ?? null;
  }

  isRequirementUnmet(section: ItemTooltipSection): boolean {
    return this.unmet.has(section);
  }
}

function realComposer(sections: FakeSections, values: Values): ItemTooltipComposer {
  return new ItemTooltipComposer(
    sections,
    new ItemDescriptionGenerator(
      Data.itemStatCost,
      Data.strings,
      values,
      Data.skills,
      Data.classes,
      Data.monsterTypes,
    ),
  );
}

function uniqueContext(): ItemTooltipContext {
  const context = new ItemTooltipContext();
  context.quality = ItemQuality.Unique;
  context.flags = ItemTooltipFlags.Identified;
  context.isWeaponOrArmorType = true;
  return context;
}

describe('the composer over the real tables', () => {
  it('a unique ring renders real stat lines bottom up', () => {
    const values = new Values();
    const sections = new FakeSections()
      .set(ItemTooltipSection.ItemName, 'Nagelring')
      .set(ItemTooltipSection.RequiredLevel, 'Required Level: 7\n');

    const composer = realComposer(sections, values);

    const lines = composer.compose(uniqueContext(), [
      [80, 25], // item_magicbonus
      [39, 30], // fireresist
    ]);

    const rows = composer.render(lines).split('\n');

    expect(rows[0]).toBe('Nagelring');
    expect(rows[1]).toBe('Required Level: 7');

    expect(rows).toContain('25% Better Chance of Getting Magic Items');
    expect(rows).toContain('Fire Resist +30%');
  });

  it('the item name carries the quality colour and the stat block is always three', () => {
    const sections = new FakeSections().set(ItemTooltipSection.ItemName, 'Nagelring');
    const composer = realComposer(sections, new Values());

    const lines = composer.compose(uniqueContext(), [[80, 25]]);

    expect(single(lines, l => l.section === ItemTooltipSection.ItemName).color).toBe(
      ItemTooltipColor.Unique,
    );

    for (const line of lines.filter(l => l.section === ItemTooltipSection.Modifiers)) {
      expect(line.color).toBe(ItemTooltipColor.Magic);
    }
  });

  it('colour codes are emitted around the real strings', () => {
    const sections = new FakeSections().set(ItemTooltipSection.ItemName, 'Nagelring');
    const composer = realComposer(sections, new Values());

    const lines = composer.compose(uniqueContext(), [[80, 25]]);

    const colored = composer.renderWithColorCodes(lines);

    const unique =
      ItemTooltipColor.Marker + ItemTooltipComposer.encodeColorDigit(ItemTooltipColor.Unique);
    const magic =
      ItemTooltipColor.Marker + ItemTooltipComposer.encodeColorDigit(ItemTooltipColor.Magic);

    expect(colored.startsWith(unique + 'Nagelring')).toBe(true);
    expect(colored).toContain(magic);
  });

  it('damage stats fold into one real aggregate line', () => {
    const values = new Values();
    values.base.set(DamageStatIds.FireMinDamage, 5);
    values.base.set(DamageStatIds.FireMaxDamage, 12);

    const sections = new FakeSections().set(ItemTooltipSection.ItemName, 'Torch');
    const composer = realComposer(sections, values);

    const lines = composer.compose(uniqueContext(), [
      [DamageStatIds.FireMinDamage, 5],
      [DamageStatIds.FireMaxDamage, 12],
    ]);

    const mods = lines.filter(l => l.section === ItemTooltipSection.Modifiers);

    expect(mods).toHaveLength(1);
    expect((mods[0] as ItemTooltipLine).text).toBe('Adds 5-12 fire damage\n');
  });

  it('a single skill stat takes the skill from the layer and names its class', () => {
    const sections = new FakeSections().set(ItemTooltipSection.ItemName, 'Wand');
    const composer = realComposer(sections, new Values());

    // Stat 107 is item_singleskill, descfunc 27, and it reads the skill id from the LAYER
    // rather than the value. Skill 36 is Fire Bolt, charclass "sor".
    const key = ItemStatReader.packStatKey(36, 107);

    const lines = composer.compose(uniqueContext(), [[key, 1]]);

    const skill = single(lines, l => l.section === ItemTooltipSection.Modifiers);

    expect(skill.text).toBe('+1 to Fire Bolt (Sorceress Only)\n');
  });

  it('an unmet requirement turns the real requirement line red', () => {
    const sections = new FakeSections()
      .set(ItemTooltipSection.ItemName, 'Nagelring')
      .set(ItemTooltipSection.RequiredLevel, 'Required Level: 99\n')
      .unmeetable(ItemTooltipSection.RequiredLevel);

    const composer = realComposer(sections, new Values());

    const lines = composer.compose(uniqueContext(), []);

    expect(single(lines, l => l.section === ItemTooltipSection.RequiredLevel).color).toBe(
      ItemTooltipColor.Red,
    );
  });

  it("the transaction cost inherits the name's colour", () => {
    const sections = new FakeSections()
      .set(ItemTooltipSection.ItemName, 'Nagelring')
      .set(ItemTooltipSection.TransactionCost, 'Repair Cost: 137 Gold\n');

    const context = uniqueContext();
    context.shopMode = 4;

    const composer = realComposer(sections, new Values());
    const lines = composer.compose(context, []);

    // The cost is appended last, so it renders on top.
    expect(composer.render(lines).startsWith('Repair Cost: 137 Gold\n')).toBe(true);
    expect(single(lines, l => l.section === ItemTooltipSection.TransactionCost).color).toBe(
      ItemTooltipColor.Unique,
    );
  });

  it('every described stat produces a line or is deliberately silent', () => {
    const generator = new ItemDescriptionGenerator(
      Data.itemStatCost,
      Data.strings,
      new Values(),
      Data.skills,
      Data.classes,
      Data.monsterTypes,
    );

    let described = 0;
    for (const statId of Data.itemStatCost.statIdsByDescPriority) {
      const lines: readonly ItemDescriptionLine[] = generator.describe([[statId, 5]]);

      for (const line of lines) {
        expect(line.text).not.toBeNull();
        ++described;
      }
    }

    // Most of the 207 described stats emit something for a plain value of 5.
    expect(described).toBeGreaterThan(150);
  });

  it('a tooltip longer than the clamp keeps the bottom and loses the top', () => {
    const sections = new FakeSections()
      .set(ItemTooltipSection.ItemName, 'Nagelring')
      .set(
        ItemTooltipSection.EtherealSocketed,
        'e'.repeat(ItemTooltipComposer.MaxTooltipLength) + '\n',
      );

    const composer = realComposer(sections, new Values());
    const lines = composer.compose(uniqueContext(), []);

    const rendered = composer.render(lines);

    expect(rendered).not.toContain('Nagelring');
    expect(rendered.length).toBeLessThanOrEqual(ItemTooltipComposer.MaxTooltipLength);
  });
});
