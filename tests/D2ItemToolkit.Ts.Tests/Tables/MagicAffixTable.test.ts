import { describe, expect, it } from 'vitest';
import { ItemViewer } from '../../../src/D2ItemToolkit.Ts/src/Stats/ItemRecord.js';
import { MagicAffixTable } from '../../../src/D2ItemToolkit.Ts/src/Tables/MagicAffixTable.js';
import { D2DataFiles } from '../../../src/D2ItemToolkit.Ts/src/Tables/TxtDataProviders.js';

// MagicAffixTable.cs — TXT_magicaffixes_GetLine 0x633ee0.

const Data = D2DataFiles.load();
const Affixes = new MagicAffixTable(Data);

const Suffix = Data.magicSuffix;
const Prefix = Data.magicPrefix;
const AutoMagic = Data.autoMagic;

// "of Lightning", the first suffix carrying a class restriction: sorceress, levelreq 18,
// classlevelreq 9.
const OfLightning = 438;

function viewer(classId: number): ItemViewer {
  const it = new ItemViewer();
  it.classId = classId;
  return it;
}

describe('MagicAffixTable', () => {
  it('is one 1-based array in the order suffix, prefix, automagic', () => {
    expect(Suffix?.rowCount).toBe(747);
    expect(Prefix?.rowCount).toBe(669);
    expect(AutoMagic?.rowCount).toBe(36);

    // Id 1 is the first SUFFIX row.
    expect(Affixes.tryResolve(1)?.table).toBe(Suffix);
    expect(Affixes.tryResolve(1)?.row).toBe(0);
    expect(Suffix?.getString(0, 'Name')).toBe('of Health');

    expect(Affixes.tryResolve(747)?.table).toBe(Suffix);
    expect(Affixes.tryResolve(747)?.row).toBe(746);
    expect(Suffix?.getString(746, 'Name')).toBe('of the Vampire');

    // An id past the suffix count spills into the prefixes.
    expect(Affixes.tryResolve(748)?.table).toBe(Prefix);
    expect(Affixes.tryResolve(748)?.row).toBe(0);
    expect(Affixes.tryResolve(749)?.table).toBe(Prefix);
    expect(Affixes.tryResolve(749)?.row).toBe(1);
    expect(Prefix?.getString(1, 'Name')).toBe('Sturdy');

    // And past the prefixes into automagic.
    expect(Affixes.tryResolve(1417)?.table).toBe(AutoMagic);
    expect(Affixes.tryResolve(1417)?.row).toBe(0);
    expect(AutoMagic?.getString(0, 'Name')).toBe("Fletcher's");

    expect(Affixes.tryResolve(1452)?.table).toBe(AutoMagic);
    expect(Affixes.tryResolve(1452)?.row).toBe(35);
  });

  it('rejects ids off both ends', () => {
    expect(Affixes.tryResolve(0)).toBeNull();
    expect(Affixes.tryResolve(-1)).toBeNull();
    expect(Affixes.tryResolve(1453)).toBeNull();
    expect(Affixes.tryResolve(100000)).toBeNull();
  });

  it('folds the level requirement upward, keeping the running maximum', () => {
    // ITEMS_nullsub 0x628830.
    expect(Suffix?.getInt(OfLightning - 1, 'levelreq')).toBe(18);

    expect(Affixes.raiseLevelRequirement(0, OfLightning, null)).toBe(18);
    expect(Affixes.raiseLevelRequirement(18, OfLightning, null)).toBe(18);
    expect(Affixes.raiseLevelRequirement(50, OfLightning, null)).toBe(50);

    // An id that resolves to nothing leaves the running maximum alone.
    expect(Affixes.raiseLevelRequirement(7, 0, null)).toBe(7);
    expect(Affixes.raiseLevelRequirement(7, 1453, null)).toBe(7);
  });

  it('prefers classlevelreq when the affix is restricted to the viewer own class', () => {
    expect(Suffix?.getString(OfLightning - 1, 'class')).toBe('sor');
    expect(Suffix?.getInt(OfLightning - 1, 'classlevelreq')).toBe(9);
    expect(Data.skills.classIdForCode('sor')).toBe(1);

    expect(Affixes.raiseLevelRequirement(0, OfLightning, viewer(1))).toBe(9);

    // A different class, or none at all, takes levelreq.
    expect(Affixes.raiseLevelRequirement(0, OfLightning, viewer(0))).toBe(18);
    expect(Affixes.raiseLevelRequirement(0, OfLightning, viewer(-1))).toBe(18);
    expect(Affixes.raiseLevelRequirement(0, OfLightning, null)).toBe(18);
  });

  it('treats an unrestricted affix as class 0xFF', () => {
    // nClass is 0xFF when the affix has no class restriction, so no viewer class can match it.
    expect(MagicAffixTable.NoClass).toBe(0xff);
    expect(Suffix?.getString(0, 'class')).toBe('');

    expect(Affixes.raiseLevelRequirement(0, 1, viewer(0xff))).toBe(Suffix?.getInt(0, 'levelreq'));
  });
});
