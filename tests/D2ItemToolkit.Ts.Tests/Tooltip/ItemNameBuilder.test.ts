import { describe, expect, it } from 'vitest';
import {
  ItemNameBuilder,
  ItemQualityNo,
} from '../../../src/D2ItemToolkit.Ts/src/Tooltip/ItemNameBuilder.js';
import {
  ItemIdentity,
  ItemRecordFlags,
} from '../../../src/D2ItemToolkit.Ts/src/Stats/ItemRecord.js';
import { ItemTable } from '../../../src/D2ItemToolkit.Ts/src/Tables/ItemTable.js';
import { ItemTooltipColor } from '../../../src/D2ItemToolkit.Ts/src/Tooltip/ItemTooltip.js';
import { ItemTypeTree } from '../../../src/D2ItemToolkit.Ts/src/Tables/ItemTypeTree.js';
import { D2DataFiles } from '../../../src/D2ItemToolkit.Ts/src/Tables/TxtDataProviders.js';
import type { TxtFile } from '../../../src/D2ItemToolkit.Ts/src/Data/TxtFile.js';

// ItemNameTests.cs and EarNameTests.cs.

const Data = D2DataFiles.load();

const Items = new ItemTable(Data.weapons, Data.armor, Data.misc);

const Names = new ItemNameBuilder(Data, Items);

function classId(code: string): number {
  const id = Items.classIdForCode(code);
  expect(id, 'no items row for ' + code).toBeGreaterThanOrEqual(0);
  return id;
}

function item(code: string, quality: number, fileIndex = -1, identified = true): ItemIdentity {
  const built = new ItemIdentity();
  built.classId = classId(code);
  built.code = code;
  built.quality = quality;
  built.fileIndex = fileIndex;
  built.flags = identified ? ItemRecordFlags.Identified : ItemRecordFlags.None;
  return built;
}

function txtKeysProbe(file: TxtFile | null, row: number, column: string): string | null {
  const key = (file as TxtFile).getString(row, column);
  return Data.strings.getByIndex(Data.strings.resolveKey(key));
}

describe('ItemNameBuilder', () => {
  it('takes the base name from namestr', () => {
    expect(Names.build(item('lrg', ItemQualityNo.Normal))).toBe('Large Shield');
    expect(Names.build(item('ssd', ItemQualityNo.Normal))).toBe('Short Sword');
  });

  it('shows only the base name for an unidentified item', () => {
    expect(Names.build(item('lrg', ItemQualityNo.Unique, 0, false))).toBe('Large Shield');
  });

  it('wraps the base name for superior and low quality', () => {
    expect(Names.build(item('lrg', ItemQualityNo.Superior))).toBe('Superior Large Shield');

    // lowqualityitems row 0 is Crude.
    expect(Names.build(item('lrg', ItemQualityNo.Inferior, 0))).toBe('Crude Large Shield');
  });

  it('writes nothing for a null low quality row', () => {
    // dwFileIndex is 3 bits against only 4 rows, so 4..7 is reachable and writes nothing.
    expect(Names.build(item('lrg', ItemQualityNo.Inferior, 6))).toBeNull();
  });

  it('makes a socketed normal item gemmed', () => {
    const socketed = item('lrg', ItemQualityNo.Normal);
    socketed.flags |= ItemRecordFlags.Socketed;

    expect(Names.build(socketed, 1)).toBe('Gemmed Large Shield');
  });

  it('keeps the plain name for an empty socketed item', () => {
    // 0x48c4b5 also needs ITEM_ItemsInItem(pInventory) above zero, so an unfilled socketed
    // white item is not "Gemmed".
    const socketed = item('lrg', ItemQualityNo.Normal);
    socketed.flags |= ItemRecordFlags.Socketed;

    expect(Names.build(socketed, 0)).toBe('Large Shield');
  });

  it('takes a magic prefix and suffix from the concatenated array', () => {
    const magic = item('lrg', ItemQualityNo.Magic);

    // The magic array is [MagicSuffix][MagicPrefix][automagic], 1-based, so id 1 is the
    // FIRST SUFFIX row, not a prefix.
    magic.magicPrefix[0] = 1;
    magic.magicSuffix[0] = 1;

    const name = Names.build(magic) as string;

    expect(name).toContain('Large Shield');

    // Both affix slots resolved to the same row, so the name repeats that word.
    const firstSuffixName = txtKeysProbe(Data.magicSuffix, 0, 'Name') as string;
    expect(name).toContain(firstSuffixName);
  });

  it('falls into the prefix table for an id past the suffix table', () => {
    const magic = item('lrg', ItemQualityNo.Magic);

    const suffixRows = (Data.magicSuffix as TxtFile).rowCount;
    magic.magicPrefix[0] = suffixRows + 1; // first PREFIX row
    magic.magicSuffix[0] = 0;

    const name = Names.build(magic) as string;
    const firstPrefixName = txtKeysProbe(Data.magicPrefix, 0, 'Name') as string;

    expect(name).toContain(firstPrefixName);
  });

  it('puts the base name first then the two affixes for a rare item', () => {
    const rare = item('lrg', ItemQualityNo.Rare);
    rare.rarePrefix = 1;
    rare.rareSuffix = 2;

    const name = Names.build(rare) as string;
    const rows = name.split('\n');

    expect(rows[0]).toBe('Large Shield');
    expect((rows[1] ?? '').trim().length, name).toBeGreaterThan(0);
  });

  it('renders crafted and tempered identically to rare', () => {
    const rare = item('lrg', ItemQualityNo.Rare);
    rare.rarePrefix = 3;
    rare.rareSuffix = 4;

    const craft = item('lrg', ItemQualityNo.Craft);
    craft.rarePrefix = 3;
    craft.rareSuffix = 4;

    const tempered = item('lrg', ItemQualityNo.Tempered);
    tempered.rarePrefix = 3;
    tempered.rareSuffix = 4;

    const expected = Names.build(rare);
    expect(Names.build(craft)).toBe(expected);
    expect(Names.build(tempered)).toBe(expected);
  });

  it('names the uniqueitems row under the base name', () => {
    // UniqueItems row 0 is The Gnasher (a hand axe).
    const uniqueName = txtKeysProbe(Data.uniqueItems, 0, 'index');

    const rows = (Names.build(item('hax', ItemQualityNo.Unique, 0)) as string).split('\n');
    expect(rows[0]).toBe('Hand Axe');
    expect(rows[1]).toBe(uniqueName);
  });

  it('names the setitems row under the base name', () => {
    const setName = txtKeysProbe(Data.setItems, 0, 'index') as string;

    const name = Names.build(item('lrg', ItemQualityNo.Set, 0));

    expect(name).not.toBeNull();
    expect(name as string).toContain(setName);
    expect((name as string).startsWith('Large Shield\n')).toBe(true);
  });

  function personalized(code: string, quality: number, fileIndex = -1): ItemIdentity {
    const built = item(code, quality, fileIndex);
    built.flags |= ItemRecordFlags.Personalized;
    built.playerName = 'Anya';
    return built;
  }

  it('takes the owners possessive on a personalised normal item', () => {
    // INV_FormatPlayerNameOnItem 0x484c90 rewrites the whole buffer for quality 1-4.
    expect(Names.build(personalized('lrg', ItemQualityNo.Normal))).toBe("Anya's Large Shield");
  });

  it('takes the owners possessive on a personalised magic item', () => {
    const name = Names.build(personalized('lrg', ItemQualityNo.Magic)) as string;

    expect(name.startsWith("Anya's ")).toBe(true);
  });

  it('leaves an item without the flag alone', () => {
    // 0x484ca9 gates on flag 0x1000000, so a stray player name is not enough.
    const plain = item('lrg', ItemQualityNo.Normal);
    plain.playerName = 'Anya';

    expect(Names.build(plain)).toBe('Large Shield');
  });

  it('names only the unique line on a personalised unique', () => {
    // 0x484cb8 skips quality 5-9 in the tail; the unique arm personalises its own line
    // through INV_FormatPlayerNameWithBase at 0x48c9e1 instead.
    const uniqueName = txtKeysProbe(Data.uniqueItems, 0, 'index') as string;

    const rows = (Names.build(personalized('hax', ItemQualityNo.Unique, 0)) as string).split('\n');

    expect(rows[0]).toBe('Hand Axe');
    expect(rows[1]).toBe("Anya's " + uniqueName);
  });

  it('replaces the 10089 wrapper on a personalised set item', () => {
    // 0x48cae3: the possessive text is used INSTEAD of the format, not inside it.
    const setName = txtKeysProbe(Data.setItems, 0, 'index') as string;

    const rows = (Names.build(personalized('lrg', ItemQualityNo.Set, 0)) as string).split('\n');

    expect(rows[0]).toBe('Large Shield');
    expect(rows[1]).toBe("Anya's " + setName);
  });

  it('names the affix line on a personalised rare item', () => {
    // 0x48c8ea personalises the 1718-formatted affix line, leaving the base name above it.
    const rare = personalized('lrg', ItemQualityNo.Rare);
    rare.rarePrefix = 1;
    rare.rareSuffix = 2;

    const rows = (Names.build(rare) as string).split('\n');

    expect(rows[0]).toBe('Large Shield');
    expect((rows[1] ?? '').startsWith("Anya's ")).toBe(true);
  });

  it('still gets the possessive on an unidentified personalised item', () => {
    // The unidentified arm reaches the same tail through 0x48ce54.
    const unidentified = personalized('lrg', ItemQualityNo.Magic);
    unidentified.flags &= ~ItemRecordFlags.Identified;

    expect(Names.build(unidentified)).toBe("Anya's Large Shield");
  });

  // =================================================================
  // Runewords: GetItemName 0x48c060 takes the 0x4000000 arm at 0x48c11a, ahead of the
  // identified test at 0x48c1ea and the quality jump table at 0x48c209.
  // =================================================================

  // Runes.txt row 0 "Runeword1": TXT_AllocTxt_runes 0x639c63 stores the Name column's
  // string id at +0x82, and ITEM_DeserializeFromBitBuffer 0x62d1ea copies it straight into
  // wMagicPrefix[0]. So the slot holds a LOCALE ID, not an affix index.
  const AncientsPledgeId = 20507;

  function runeword(code: string, runeStringId: number, quality = 2): ItemIdentity {
    const built = item(code, quality);
    built.flags |= ItemRecordFlags.Runeword;
    built.magicPrefix[0] = runeStringId;
    return built;
  }

  it('names a runeword in gold above the base type', () => {
    expect(Names.build(runeword('crs', AncientsPledgeId))).toBe(
      'Crystal Sword\n' + ItemTooltipColor.Marker + "4Ancients' Pledge",
    );
  });

  it('never makes a runeword gemmed', () => {
    // Before the 0x4000000 arm existed the flag was unread, so a runeword fell through to
    // Normal() and the socket gate renamed it "Gemmed Crystal Sword".
    expect(Names.build(runeword('crs', AncientsPledgeId), 3) as string).not.toContain('Gemmed');
  });

  it('makes a runeword ignore its quality', () => {
    const superior = Names.build(
      runeword('crs', AncientsPledgeId, ItemQualityNo.Superior),
    ) as string;

    expect(superior).not.toContain('Superior');
    expect(superior).toBe(Names.build(runeword('crs', AncientsPledgeId)));
  });

  it('takes the runeword arm before the identified check', () => {
    const unidentified = runeword('crs', AncientsPledgeId);
    unidentified.flags &= ~ItemRecordFlags.Identified;

    expect(Names.build(unidentified) as string).toContain("Ancients' Pledge");
  });

  it('resolves the rune prefix through getByIndex not the affix tables', () => {
    const second = (Names.build(runeword('crs', AncientsPledgeId)) as string).split('\n')[1] ?? '';

    // Strip the marker and its digit, then it must equal the raw locale lookup.
    expect(second.substring(ItemTooltipColor.Marker.length + 1)).toBe(
      Data.strings.getByIndex(AncientsPledgeId),
    );
  });
});

/**
 * The quality-2 arms of GetItemName 0x48c060 that are not the plain base name: the ear at
 * 0x48c2b3 and the tome/scroll split at 0x48c542.
 */
describe('ItemNameBuilder ear and tome arms', () => {
  const EarNames = new ItemNameBuilder(Data, Items, new ItemTypeTree(Data.itemTypes));

  function normalItem(code: string): ItemIdentity {
    const built = new ItemIdentity();
    built.classId = Items.classIdForCode(code);
    built.code = code;
    built.quality = ItemQualityNo.Normal;
    built.flags = ItemRecordFlags.Identified;
    expect(built.classId, code).toBeGreaterThanOrEqual(0);
    return built;
  }

  it('names an ears owner class and level', () => {
    const ear = normalItem('ear');
    ear.playerName = 'Bob';
    ear.earLevel = 42;
    ear.fileIndex = 4; // Barbarian

    const lines = (EarNames.build(ear) as string).split('\n');

    // Appended top-to-bottom; the renderer reverses, so the possessive shows first in game.
    expect(lines).toHaveLength(3);
    expect((lines[0] ?? '').startsWith('Level')).toBe(true);
    expect(lines[0]).toContain('42');
    expect(lines[1]).toBe('Barbarian');
    expect(lines[2]).toBe("Bob's Ear");
  });

  it.each([
    [0, 'Amazon'],
    [1, 'Sorceress'],
    [2, 'Necromancer'],
    [3, 'Paladin'],
    [4, 'Barbarian'],
    [5, 'Druid'],
    [6, 'Assassin'],
  ] as const)('reads the ear file index %i as the dead players class', (fileIndex, expected) => {
    const ear = normalItem('ear');
    ear.playerName = 'X';
    ear.fileIndex = fileIndex;

    expect(EarNames.build(ear) as string).toContain('\n' + expected + '\n');
  });

  it('writes no class line for a class index past the table', () => {
    // 0x484a70 HALTS the game at 7 or above; we omit the line rather than crash.
    const ear = normalItem('ear');
    ear.playerName = 'X';
    ear.fileIndex = 7;

    expect((EarNames.build(ear) as string).split('\n')).toHaveLength(2);
  });

  it('adds a line above everything for the named flag', () => {
    const ear = normalItem('ear');
    ear.playerName = 'Bob';
    ear.fileIndex = 3;
    ear.flags |= ItemRecordFlags.Named;

    const lines = (EarNames.build(ear) as string).split('\n');

    expect(lines).toHaveLength(4);
    expect(lines[3]).toBe("Bob's Ear");
  });

  it('drops the possessive for an over long owner name', () => {
    // 0x5272e1: base + owner + 5 over the caller's 100 wide characters falls back to the base.
    const ear = normalItem('ear');
    ear.playerName = 'x'.repeat(120);
    ear.fileIndex = 3;

    const lines = (EarNames.build(ear) as string).split('\n');

    expect(lines[lines.length - 1]).toBe('Ear');
  });

  // 2199/2201 are the tome pair and 2200/2202 the scroll pair; the suffix picks which spell.
  it.each([
    ['tbk', 0],
    ['tbk', 1],
    ['tsc', 0],
    ['tsc', 1],
  ] as const)('names the spell of a tome or scroll (%s, %i)', (code, suffix) => {
    const tome = normalItem(code);
    tome.magicSuffix[0] = suffix;

    const name = EarNames.build(tome);
    expect(name === null || name.length === 0, code + ' suffix ' + suffix).toBe(false);
  });

  it('names a tome and a scroll of the same spell differently', () => {
    expect(EarNames.build(normalItem('tbk'))).not.toBe(EarNames.build(normalItem('tsc')));
  });

  it('names the creature a monster body part came from', () => {
    // fileIndex on a body part is a monstats row, and the part's own base name resolves
    // through namestr — "Heart" for hrt, whatever the misc.txt `name` column calls it.
    const part = normalItem('hrt');
    part.fileIndex = 0;

    const monster = Data.monsterTypes.getMonsterName(0);
    expect(monster === null || monster.length === 0).toBe(false);

    expect(EarNames.build(part) as string).toContain(monster as string);
  });

  it('falls back to the base name for a body part with no monster row', () => {
    const part = normalItem('hrt');
    part.fileIndex = -1;

    expect(EarNames.build(part)).toBe('Heart');
  });

  it('names nothing for a magic suffix above one', () => {
    const scroll = normalItem('tsc');
    scroll.magicSuffix[0] = 2;

    expect(EarNames.build(scroll)).toBeNull();
  });
});

/**
 * The affix-table walks and the SkipName gate. TXT_RareAffixes_GetLine 0x634260 is the same
 * 1-based concatenation trick as the magic affixes but over only TWO tables, and neither the
 * spill nor the run off the end had a test on either side.
 */
describe('ItemNameBuilder affix spill and SkipName', () => {
  it('shows only the unique line when SkipName is set', () => {
    // 0x48c9e1: items.txt SkipName suppresses the base-name line entirely. The Horadric Staff
    // is uniqueitems row 125 and its item code `hst` carries SkipName.
    expect(Items.getInt(classId('hst'), 'SkipName')).not.toBe(0);

    const uniqueName = txtKeysProbe(Data.uniqueItems, 125, 'index') as string;
    const name = Names.build(item('hst', ItemQualityNo.Unique, 125)) as string;

    expect(name).toBe(uniqueName);
    expect(name).not.toContain('\n');
  });

  it('keeps the base line for a unique whose item does not set SkipName', () => {
    // The same arm, one column apart: a Hand Axe has no SkipName, so the base line survives.
    expect(Items.getInt(classId('hax'), 'SkipName')).toBe(0);

    expect(Names.build(item('hax', ItemQualityNo.Unique, 0)) as string).toContain('\n');
  });

  it('falls into the rare prefix table for an id past the rare suffix table', () => {
    // 1-based over [RareSuffix][RarePrefix] — 155 suffix rows, so id 156 is rare prefix row 0.
    const suffixRows = (Data.rareSuffix as TxtFile).rowCount;
    expect(suffixRows).toBe(155);

    const rare = item('lrg', ItemQualityNo.Rare);
    rare.rarePrefix = suffixRows + 1;
    rare.rareSuffix = 0;

    const firstRarePrefix = txtKeysProbe(Data.rarePrefix, 0, 'name') as string;
    expect(firstRarePrefix.length).toBeGreaterThan(0);

    expect((Names.build(rare) as string).split('\n')[1]).toContain(firstRarePrefix);
  });

  it('names no rare affix for an id past both tables', () => {
    const past = (Data.rareSuffix as TxtFile).rowCount + (Data.rarePrefix as TxtFile).rowCount + 1;

    const rare = item('lrg', ItemQualityNo.Rare);
    rare.rarePrefix = past;
    rare.rareSuffix = past;

    const rows = (Names.build(rare) as string).split('\n');

    // The base line stands; the affix line is the bare "%0 %1" with both slots empty.
    expect(rows[0]).toBe('Large Shield');
    expect((rows[1] ?? '').trim()).toBe('');
  });

  it('names no magic affix for an id past all three tables', () => {
    const past =
      (Data.magicSuffix as TxtFile).rowCount +
      (Data.magicPrefix as TxtFile).rowCount +
      (Data.autoMagic as TxtFile).rowCount +
      1;
    expect(past).toBe(1453);

    const magic = item('lrg', ItemQualityNo.Magic);
    magic.magicPrefix[0] = past;
    magic.magicSuffix[0] = past;

    // Format 1714 is "%0 %1 %2", so an empty affix pair leaves the base name and its spaces.
    expect((Names.build(magic) as string).trim()).toBe('Large Shield');
  });

  it('keeps the plain name when the personalised owner is blank', () => {
    // 0x5272f6 returns the base name untouched rather than emitting a bare "'s".
    const blank = item('lrg', ItemQualityNo.Normal);
    blank.flags |= ItemRecordFlags.Personalized;
    blank.playerName = '';

    expect(Names.build(blank)).toBe('Large Shield');
  });
});
