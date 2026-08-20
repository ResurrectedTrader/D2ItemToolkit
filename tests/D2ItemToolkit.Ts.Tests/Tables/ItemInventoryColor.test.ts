import { describe, expect, it } from 'vitest';
import { ColorTable } from '../../../src/D2ItemToolkit.Ts/src/Tables/ColorTable.js';
import { GemTable } from '../../../src/D2ItemToolkit.Ts/src/Tables/GemTable.js';
import { ItemInventoryColor } from '../../../src/D2ItemToolkit.Ts/src/Tables/ItemInventoryColor.js';
import { ItemInventoryGraphics } from '../../../src/D2ItemToolkit.Ts/src/Tables/ItemInventoryGraphics.js';
import { TooltipEngine } from '../../../src/D2ItemToolkit.Ts/src/Tooltip/TooltipEngine.js';
import { unitFromJson } from '../../../src/D2ItemToolkit.Ts/src/Stats/Unit.js';
import { ItemTable } from '../../../src/D2ItemToolkit.Ts/src/Tables/ItemTable.js';
import { ItemTypeTree } from '../../../src/D2ItemToolkit.Ts/src/Tables/ItemTypeTree.js';
import { D2DataFiles } from '../../../src/D2ItemToolkit.Ts/src/Tables/TxtDataProviders.js';
import {
  ItemIdentity,
  ItemRecordFlags,
} from '../../../src/D2ItemToolkit.Ts/src/Stats/ItemRecord.js';
import { ItemQualityNo } from '../../../src/D2ItemToolkit.Ts/src/Tooltip/ItemNameBuilder.js';
import type { TxtFile } from '../../../src/D2ItemToolkit.Ts/src/Data/TxtFile.js';

/**
 * The C# counterpart is tests/D2ItemToolkit.Net.Tests/Tables/ItemInventoryColorTests.cs and asserts
 * the same things. The point of most of these is the .txt/.bin distinction: every column feeding
 * this holds a 4-char CODE in the files we embed and a resolved row index in the compiled table.
 */

const Data = D2DataFiles.load();
const Items = new ItemTable(Data.weapons, Data.armor, Data.misc);
const Types = new ItemTypeTree(Data.itemTypes);

function colors(): ItemInventoryColor {
  return new ItemInventoryColor(Data, Items, Types);
}

function table(): ColorTable {
  return new ColorTable(Data.colors);
}

function item(code: string, quality: number, flags: number): ItemIdentity {
  const identity = new ItemIdentity();
  identity.classId = Items.classIdForCode(code);
  identity.quality = quality;
  identity.flags = flags;
  return identity;
}

describe('colors.txt', () => {
  it('is twenty one rows and is not spliced', () => {
    // No `Expansion` first cell, so the row index is the literal 0-based position. ItemTypes.txt,
    // by contrast, IS spliced — which is why nothing here uses a literal itemtypes row.
    expect(table().rowCount).toBe(21);
  });

  it.each([
    [0, 'whit'],
    [3, 'blac'],
    [9, 'cred'],
    [15, 'lgld'],
    [20, 'bwht'],
  ])('maps row %i to %s', (row, code) => {
    expect(table().rowForCode(code)).toBe(row);
    expect(table().codeAt(row)).toBe(code);
  });

  it('treats the last row as the ceiling every lookup clamps to', () => {
    expect(ColorTable.MaxPaletteIndex).toBe(table().rowCount - 1);
    expect(ColorTable.clamp(ColorTable.MaxPaletteIndex + 1)).toBe(ColorTable.None);
    expect(ColorTable.clamp(-1)).toBe(ColorTable.None);
    expect(ColorTable.clamp(20)).toBe(20);
  });

  it('treats an unknown or empty code as no shift rather than row zero', () => {
    // Row 0 is `whit`. Falling back to it would silently paint every unmatched item white.
    expect(table().rowForCode('zzzz')).toBe(ColorTable.None);
    expect(table().rowForCode('')).toBe(ColorTable.None);
    expect(table().rowForCode(null)).toBe(ColorTable.None);
  });
});

describe('every colour column in the shipped data', () => {
  function assertAllResolve(source: TxtFile | null, column: string, label: string): void {
    expect(source).not.toBeNull();
    const file = source as TxtFile;

    const lookup = table();
    const orphans: string[] = [];
    let populated = 0;

    for (let row = 0; row < file.rowCount; ++row) {
      const code = (file.getString(row, column) ?? '').trim();
      if (code.length === 0) {
        continue;
      }

      ++populated;
      if (lookup.rowForCode(code) < 0) {
        orphans.push(String(row) + ':' + code);
      }
    }

    expect(populated, label + ' has no ' + column + ' cells at all').toBeGreaterThan(0);
    expect(orphans).toEqual([]);
  }

  it('resolves every magicprefix transformcolor', () => {
    assertAllResolve(Data.magicPrefix, 'transformcolor', 'MagicPrefix');
  });

  it('resolves every magicsuffix transformcolor', () => {
    assertAllResolve(Data.magicSuffix, 'transformcolor', 'MagicSuffix');
  });

  it('resolves every automagic transformcolor', () => {
    assertAllResolve(Data.autoMagic, 'transformcolor', 'AutoMagic');
  });

  it('resolves every uniqueitems invtransform', () => {
    assertAllResolve(Data.uniqueItems, 'invtransform', 'UniqueItems');
  });

  it('resolves every setitems invtransform', () => {
    assertAllResolve(Data.setItems, 'invtransform', 'SetItems');
  });

  it('keeps gems.txt transform numeric and in range', () => {
    // The one column that is ALREADY an index in the .txt, which is why the gem arm does not go
    // through ColorTable.
    const gems = Data.gems as TxtFile;
    let populated = 0;

    for (let row = 0; row < gems.rowCount; ++row) {
      const transform = gems.getInt(row, 'transform', -1);
      if (transform < 0) {
        continue;
      }

      ++populated;
      expect(transform).toBeLessThanOrEqual(ColorTable.MaxPaletteIndex);
    }

    expect(populated).toBeGreaterThan(0);
  });
});

/** The first id in the concatenated affix array whose row carries a colour. */
function firstAffixWithAColour(): number {
  const suffixes = Data.magicSuffix as TxtFile;
  const lookup = table();

  for (let row = 0; row < suffixes.rowCount; ++row) {
    const code = (suffixes.getString(row, 'transformcolor') ?? '').trim();
    if (code.length !== 0 && lookup.rowForCode(code) >= 0) {
      return row + 1; // 1-based into [magicsuffix][magicprefix][automagic]
    }
  }

  return 0;
}

describe('resolution', () => {
  it('takes a set row invtransform', () => {
    // SetItems row 3 is Hsarus' Iron Heel, invtransform `dred`.
    const boots = item('lbt', ItemQualityNo.Set, ItemRecordFlags.Identified);
    boots.fileIndex = 3;

    expect(colors().resolve(boots)).toBe(table().rowForCode('dred'));
  });

  it('gives an unidentified set or unique no shift', () => {
    // dwFileIndex is not carried by the client until identified, so the game returns no shift
    // rather than reading a row it does not have.
    const boots = item('lbt', ItemQualityNo.Set, ItemRecordFlags.None);
    boots.fileIndex = 3;

    expect(colors().resolve(boots)).toBe(ColorTable.None);
  });

  it('never falls through from set or unique to the affix path', () => {
    const boots = item('lbt', ItemQualityNo.Set, ItemRecordFlags.Identified);
    boots.fileIndex = -1;
    boots.magicSuffix[0] = firstAffixWithAColour();

    expect(colors().resolve(boots)).toBe(ColorTable.None);
  });

  it('takes a magic item suffix transformcolor', () => {
    const affixId = firstAffixWithAColour();
    expect(affixId).toBeGreaterThan(0);

    const expected = (
      (Data.magicSuffix as TxtFile).getString(affixId - 1, 'transformcolor') ?? ''
    ).trim();

    const ring = item('rin', ItemQualityNo.Magic, ItemRecordFlags.Identified);
    ring.magicSuffix[0] = affixId;

    expect(colors().resolve(ring)).toBe(table().rowForCode(expected));
  });

  it('gives a magic item with no coloured affix no shift', () => {
    expect(colors().resolve(item('rin', ItemQualityNo.Magic, ItemRecordFlags.Identified))).toBe(
      ColorTable.None,
    );
  });

  it('tints a normal item by a gem in the first socket', () => {
    const shield = item('lrg', ItemQualityNo.Normal, ItemRecordFlags.Identified);
    const gem = item('gpv', ItemQualityNo.Normal, ItemRecordFlags.Identified);

    const gems = new GemTable(Data.gems, Items);
    const row = gems.rowForFillerClassId(gem.classId);
    expect(row).toBeGreaterThanOrEqual(0);

    const expected = (Data.gems as TxtFile).getInt(row, 'transform', -1);
    expect(expected).toBeGreaterThanOrEqual(0);

    expect(colors().resolve(shield, gem)).toBe(expected);
  });

  it('does not tint from a rune in the first socket', () => {
    // Runes share gems.txt — el carries a real transform — but they are itemtype `rune` under
    // `sock`, not `gem`, so isOfType(gem) excludes them.
    const shield = item('lrg', ItemQualityNo.Normal, ItemRecordFlags.Identified);
    const rune = item('r01', ItemQualityNo.Normal, ItemRecordFlags.Identified);

    expect(colors().resolve(shield, rune)).toBe(ColorTable.None);
  });

  it('leaves an item with no sockets untinted', () => {
    expect(colors().resolve(item('lrg', ItemQualityNo.Normal, ItemRecordFlags.Identified))).toBe(
      ColorTable.None,
    );
  });
});

describe('the inventory sprite name', () => {
  function graphics(): ItemInventoryGraphics {
    return new ItemInventoryGraphics(Data, Items, Types);
  }

  it('keeps the item own code for a self-named graphic', () => {
    // lrg's invfile is `invlrg`, which IS "inv" + code, so the item has its own art.
    expect(graphics().resolve(item('lrg', ItemQualityNo.Normal, ItemRecordFlags.Identified))).toBe(
      'lrg',
    );
  });

  it.each([
    ['xap', 'cap'],
    ['xkp', 'skp'],
    ['xlm', 'hlm'],
  ])('collapses the exceptional %s to its normal code %s', (code, expected) => {
    // xap is the exceptional Cap and its invfile is `invcap` — a SHARED graphic — so the sprite
    // is named by the normal tier, not by the item.
    expect(graphics().resolve(item(code, ItemQualityNo.Normal, ItemRecordFlags.Identified))).toBe(
      expected,
    );
  });

  it.each([
    [0, 'rin1'],
    [4, 'rin5'],
  ])('appends a ring one-based variant %i as %s', (gfxIndex, expected) => {
    // itemtypes `ring` has VarInvGfx 5. gfxIndex is 0-based and the suffix is 1-based, so the
    // only thing separating rin1 from rin5 is the field the producer now emits.
    const ring = item('rin', ItemQualityNo.Magic, ItemRecordFlags.Identified);
    ring.gfxIndex = gfxIndex;

    expect(graphics().resolve(ring)).toBe(expected);
  });

  it('adds no suffix for a type without variants', () => {
    // `shie` has no VarInvGfx, so a non-zero index must NOT leak into the name.
    const shield = item('lrg', ItemQualityNo.Normal, ItemRecordFlags.Identified);
    shield.gfxIndex = 3;

    expect(graphics().resolve(shield)).toBe('lrg');
  });

  it('takes an identified unique own row graphic', () => {
    // UniqueItems row 0 is The Gnasher, invfile `invhaxu`.
    const axe = item('hax', ItemQualityNo.Unique, ItemRecordFlags.Identified);
    axe.fileIndex = 0;

    expect(graphics().resolve(axe)).toBe('invhaxu');
  });

  it('keeps the plain sprite for an unidentified unique', () => {
    // dwFileIndex is not carried until identified, so the special graphic cannot apply.
    const axe = item('hax', ItemQualityNo.Unique, ItemRecordFlags.None);
    axe.fileIndex = 0;

    expect(graphics().resolve(axe)).toBe('hax');
  });

  it('falls back to uniqueinvfile when the row has no graphic', () => {
    // The Amulet of the Viper: the one misc row carrying uniqueinvfile.
    const amulet = item('vip', ItemQualityNo.Unique, ItemRecordFlags.Identified);
    amulet.fileIndex = -1;

    expect(graphics().resolve(amulet)).toBe('invvip');
  });

  it('has no shipped set row carrying its own graphic', () => {
    // Reachability, not decoration: SetItems.invfile is empty on EVERY row, so the set arm ALWAYS
    // reaches the items.txt setinvfile fallback.
    const sets = Data.setItems as TxtFile;
    let populated = 0;
    for (let row = 0; row < sets.rowCount; ++row) {
      if ((sets.getString(row, 'invfile') ?? '').trim().length !== 0) {
        ++populated;
      }
    }

    expect(populated).toBe(0);

    // And the counterpart IS populated, so the unique arm really does use it.
    const uniques = Data.uniqueItems as TxtFile;
    let uniqueCount = 0;
    for (let row = 0; row < uniques.rowCount; ++row) {
      if ((uniques.getString(row, 'invfile') ?? '').trim().length !== 0) {
        ++uniqueCount;
      }
    }

    expect(uniqueCount).toBe(140);
  });

  it('gives the sprite, the colour and the gate together', () => {
    const ring = unitFromJson(
      '{ "unitType": 4, "classId": ' +
        String(Items.classIdForCode('rin')) +
        ', "quality": 4, "itemFlags": 16, "gfxIndex": 2, "statsLists": [] }',
    );

    const appearance = TooltipEngine.embedded.appearance(ring);

    expect(appearance.image).toBe('rin3');
    expect(appearance.color).toBe(ColorTable.None);
    expect(appearance.isTinted).toBe(false);
  });
});

describe('invTrans', () => {
  it('is read as a number from the item row', () => {
    const classId = Items.classIdForCode('lrg');

    expect(colors().invTrans(classId)).toBe(Items.getInt(classId, 'InvTrans'));
  });

  it('is non-zero on at least one shipped item', () => {
    // If every item were zero the gate would be vacuous and isTinted always false.
    const nonZero = ['rin', 'amu', 'jew', 'cm1', 'lrg'].filter(
      code => colors().invTrans(Items.classIdForCode(code)) !== 0,
    );

    expect(nonZero.length).toBeGreaterThan(0);
  });
});
