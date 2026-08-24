import { describe, expect, it } from 'vitest';
import { ItemRecordFlags } from '../../../src/D2ItemToolkit.Ts/src/Stats/ItemRecord.js';
import { createUnit, type Unit } from '../../../src/D2ItemToolkit.Ts/src/Stats/Unit.js';
import { ItemTable } from '../../../src/D2ItemToolkit.Ts/src/Tables/ItemTable.js';
import { D2DataFiles } from '../../../src/D2ItemToolkit.Ts/src/Tables/TxtDataProviders.js';
import { TooltipEngine } from '../../../src/D2ItemToolkit.Ts/src/Tooltip/TooltipEngine.js';

/**
 * The peer of the C# DefenseOutOfRangeTests. A Skin of the Vipermagi reading
 * `Defense: 279 [244-277]`: the capture stores the game's own `Defense: 279` and a base stat 31 of
 * 127, which is `maxac + 1` for a Serpentskin (111..126). The roll alone cannot produce that —
 * ITEM_RollBaseArmorClass 0x556360 halts the game above maxac (0x5563b2) — the `ac%` property does,
 * via ITEMMOD_MaximizeStatForEnhanced. So the base does not roll and there is no span to show.
 */
const Data = D2DataFiles.load();
const Engine = TooltipEngine.embedded;
const Items = new ItemTable(Data.weapons, Data.armor, Data.misc);

/** UniqueItems.txt post-splice, 0-based. */
const SkinOfTheVipermagi = 210;

const StatDefense = 31;
const StatArmorPercent = 16;
const ListFlagsExtended = 0x80000000;
const ListFlagsMagic = 0x40;

/** armor.txt `xea`: minac 111, maxac 126. */
const SerpentskinMaxAc = 126;

function vipermagi(baseDefense: number): Unit {
  return createUnit({
    unitType: 4,
    classId: Items.classIdForCode('xea'),
    quality: 7,
    fileIndex: SkinOfTheVipermagi,
    itemFlags: ItemRecordFlags.Identified,
    statsLists: [
      {
        stateNo: 0,
        flags: ListFlagsExtended,
        stats: [
          { id: StatDefense, value: baseDefense },
          { id: 72, value: 22 },
          { id: 73, value: 24 },
        ],
      },
      { stateNo: 0, flags: ListFlagsMagic, stats: [{ id: StatArmorPercent, value: 120 }] },
    ],
  });
}

function defenseLine(item: Unit): string {
  const line = Engine.render(item, null, { showRolledRanges: true, rangeColor: -1 })
    .lines.map(l => (l.text ?? '').replace(/ÿc./g, '').replace(/\n+$/, ''))
    .find(t => t.startsWith('Defense:'));

  expect(line).toBeDefined();
  return line as string;
}

describe('the defense span and what falls outside it', () => {
  it('an enhanced-defence item has a fixed base and so no span', () => {
    const captured = vipermagi(SerpentskinMaxAc + 1);

    expect(defenseLine(captured)).toBe('Defense: 279');

    const defense = Engine.ranges(captured).stats.find(
      r => r.statId === StatDefense && r.layer === 0,
    );

    expect(defense?.isRange).toBe(false);
    expect(defense?.low).toBe(279);
    expect(defense?.high).toBe(279);
    expect(Engine.ranges(captured).outOfRange).toEqual([]);
  });

  it('a base the maximise could not have left behind is reported', () => {
    // maxac itself is now OUT of range, because the maximise always lands one above it. Before this
    // was traced the span was the raw 111..126 roll, so this record looked ordinary and the real
    // one — 127 — looked broken. Exactly backwards.
    const impossible = vipermagi(SerpentskinMaxAc);

    expect(defenseLine(impossible)).toBe('Defense: 277');
    expect(Engine.ranges(impossible).outOfRange).toContain(StatDefense);
  });

  it('without an armour percent the base still rolls', () => {
    // The maximise is reached only through `ac%`. Strip it and the span is the ordinary armor.txt
    // roll again, so this is the control that stops the fix over-applying.
    const plain = vipermagi(120);
    plain.statsLists = plain.statsLists.slice(0, 1);
    plain.fileIndex = -1;
    plain.quality = 2;

    const defense = Engine.ranges(plain).stats.find(r => r.statId === StatDefense && r.layer === 0);

    expect(defense?.isRange).toBe(true);
    expect(defense?.low).toBe(111);
    expect(defense?.high).toBe(126);
  });

  /**
   * KNOWN FAILURE, kept so the gap is not forgotten. See the C# peer for the full trace: everything
   * says Magefist's `ac%` must maximise its Battle Gauntlets base to 48, and a War Traveler with the
   * IDENTICAL 39..47 range and one `ac%` IS 48 in the same capture — but Magefist's captured base is
   * 45 and the game's own string agrees (`Defense: 68` = 45 + 10 + 45 * 29 / 100). Un-skip once the
   * discriminator is traced.
   */
  it.skip('magefist keeps its rolled base despite carrying an armour percent', () => {
    const glove = createUnit({
      unitType: 4,
      classId: Items.classIdForCode('xtg'),
      quality: 7,
      fileIndex: 105,
      itemFlags: ItemRecordFlags.Identified,
      statsLists: [
        {
          stateNo: 0,
          flags: ListFlagsExtended,
          stats: [
            { id: StatDefense, value: 45 },
            { id: 72, value: 18 },
            { id: 73, value: 18 },
          ],
        },
        {
          stateNo: 0,
          flags: ListFlagsMagic,
          stats: [
            { id: StatArmorPercent, value: 29 },
            { id: StatDefense, value: 10 },
          ],
        },
      ],
    });

    expect(defenseLine(glove)).toBe('Defense: 68');
    expect(Engine.ranges(glove).outOfRange).toEqual([]);
  });
});
