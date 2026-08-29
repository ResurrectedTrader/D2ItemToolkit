import { describe, expect, it } from 'vitest';
import { ItemRecordFlags } from '../../../src/D2ItemToolkit.Ts/src/Stats/ItemRecord.js';
import { ItemStatListStates } from '../../../src/D2ItemToolkit.Ts/src/Stats/ItemStatReader.js';
import { createUnit, type Unit } from '../../../src/D2ItemToolkit.Ts/src/Stats/Unit.js';
import type { ItemMergedStats } from '../../../src/D2ItemToolkit.Ts/src/Stats/MergedStats.js';
import { ItemTable } from '../../../src/D2ItemToolkit.Ts/src/Tables/ItemTable.js';
import { D2DataFiles } from '../../../src/D2ItemToolkit.Ts/src/Tables/TxtDataProviders.js';
import {
  TooltipEngine,
  type Tooltip,
} from '../../../src/D2ItemToolkit.Ts/src/Tooltip/TooltipEngine.js';
import { ItemTooltipSection } from '../../../src/D2ItemToolkit.Ts/src/Tooltip/ItemTooltip.js';

/**
 * The peer of the C# MergedStatsTests: the three things a stored item cannot answer from its raw
 * statlists — a filler's stats are not in the capture at all, an item's own stats are split across
 * lists with no total anywhere, and op 13 is unapplied.
 */
const Data = D2DataFiles.load();
const Engine = TooltipEngine.embedded;
const Items = new ItemTable(Data.weapons, Data.armor, Data.misc);

const StatMaxLife = 7;
const StatArmorPercent = 16;
const StatDefense = 31;
const StatFireResist = 39;
const StatChargedSkill = 204;

const LocationEquipped = 1;
const LocationStash = 3;

const ListFlagsMagic = 0x40;
const ListFlagsSet = 0x2000;
const ListFlagsExtended = 0x80000000;

/** setitems.txt post-splice, 0-based. `xsk`, a Death Mask. */
const TalRashasHoradricCrest = 80;

/** UniqueItems.txt post-splice, 0-based. `xea`, a Serpentskin Armor. */
const SkinOfTheVipermagi = 210;

function sectionOf(tip: Tooltip, section: ItemTooltipSection): string[] {
  return tip.lines
    .filter(l => l.section === section)
    .map(l => (l.text ?? '').replace(/ÿc./g, '').replace(/\n+$/, ''))
    .filter(t => t.length !== 0);
}

function valueOf(merged: ItemMergedStats, statId: number, layer = 0): number {
  return merged.stats.find(s => s.statId === statId && s.layer === layer)?.value ?? 0;
}

/**
 * The captured Tal Rasha's Horadric Crest: base list 31 = 76, its own set mods on the 0x40 list, an
 * Um rune in its one socket.
 */
function crestWithUm(location: number): Unit {
  const um = createUnit({
    unitType: 4,
    classId: Items.classIdForCode('r22'),
    itemFlags: ItemRecordFlags.Identified,
  });

  return createUnit({
    unitType: 4,
    classId: Items.classIdForCode('xsk'),
    quality: 5,
    fileIndex: TalRashasHoradricCrest,
    itemFlags: ItemRecordFlags.Identified | ItemRecordFlags.Socketed,
    location,
    x: 1,
    statsLists: [
      {
        stateNo: 0,
        flags: ListFlagsExtended,
        stats: [
          { id: StatDefense, value: 76 },
          { id: 72, value: 16 },
          { id: 73, value: 20 },
          { id: 194, value: 1 },
        ],
      },
      {
        stateNo: 0,
        flags: ListFlagsMagic,
        stats: [
          { id: StatMaxLife, value: 60 << 8 },
          { id: 9, value: 30 << 8 },
          { id: StatDefense, value: 45 },
          { id: 39, value: 15 },
          { id: 41, value: 15 },
          { id: 43, value: 15 },
          { id: 45, value: 15 },
          { id: 60, value: 10 },
          { id: 62, value: 10 },
        ],
      },
    ],
    items: [um],
  });
}

describe('merged item stats', () => {
  it("sums an item's own stats across its lists", () => {
    // The base array holds 76 and the affix list 45, and the tooltip prints 121. Nothing in the raw
    // chain holds 121, so `defence >= 100` could never match this item.
    expect(valueOf(Engine.mergedStats(crestWithUm(LocationStash)), StatDefense)).toBe(121);
  });

  it("synthesises a rune's stats from gems.txt", () => {
    // An Um arrives with an EMPTY stat chain, so its `Helms: All Resistances +15` is nowhere in the
    // capture. The Crest grants 15 of its own, so a correct merge reads 30.
    const carried = crestWithUm(LocationStash);

    expect(valueOf(Engine.mergedStats(carried), StatFireResist)).toBe(30);
    expect(valueOf(Engine.mergedStats(carried, { includeSockets: false }), StatFireResist)).toBe(
      15,
    );
  });

  it('keeps a worn set piece its filler totals', () => {
    // The GAME throws the Um away when the piece is worn (0x4c15fd gates the recalc loop on quality
    // 5). These totals answer what the ITEM grants, which wearing it does not change.
    expect(valueOf(Engine.mergedStats(crestWithUm(LocationEquipped)), StatFireResist)).toBe(30);
    expect(valueOf(Engine.mergedStats(crestWithUm(LocationStash)), StatFireResist)).toBe(30);
  });

  it('applies op 13 and keeps the percent', () => {
    // Skin of the Vipermagi is base 127 under a fixed 120% enhanced defence, and the game prints
    // 279. The percent stays because the tooltip draws it as its own line.
    const armor = createUnit({
      unitType: 4,
      classId: Items.classIdForCode('xea'),
      quality: 7,
      fileIndex: SkinOfTheVipermagi,
      itemFlags: ItemRecordFlags.Identified,
      statsLists: [
        { stateNo: 0, flags: ListFlagsExtended, stats: [{ id: StatDefense, value: 127 }] },
        { stateNo: 0, flags: ListFlagsMagic, stats: [{ id: StatArmorPercent, value: 120 }] },
      ],
    });

    const merged = Engine.mergedStats(armor);

    expect(valueOf(merged, StatDefense)).toBe(279);
    expect(valueOf(merged, StatArmorPercent)).toBe(120);
  });

  it('returns raw values rather than display-scaled ones', () => {
    // `+60 to Life` is stored 8.8 fixed point, and a consumer's bounds derive from the same scale.
    expect(valueOf(Engine.mergedStats(crestWithUm(LocationStash)), StatMaxLife)).toBe(60 << 8);
  });

  it('excludes packed encodings rather than summing or zeroing them', () => {
    // Stat 204 packs `(maxCharges << 8) + current`. Adding two of those produces a number that
    // looks real and is not, so it is left out — ABSENT rather than zero, because a zero would
    // satisfy every "at most N" bound.
    const wand = createUnit({
      unitType: 4,
      classId: Items.classIdForCode('wnd'),
      quality: 4,
      itemFlags: ItemRecordFlags.Identified,
      statsLists: [
        {
          stateNo: 0,
          flags: ListFlagsMagic,
          stats: [
            { id: StatChargedSkill, value: (9 << 8) + 5, layer: 56 },
            { id: StatFireResist, value: 20 },
          ],
        },
      ],
    });

    const merged = Engine.mergedStats(wand);

    expect(merged.stats.some(s => s.statId === StatChargedSkill)).toBe(false);
    expect(merged.excludedPackedStats).toContain(StatChargedSkill);
    expect(valueOf(merged, StatFireResist)).toBe(20);
  });

  it('never merges layers', () => {
    // `+1 to Fire Skills` and `+1 to Cold Skills` are one stat at two layers.
    const StatElementalSkills = 126;

    const amulet = createUnit({
      unitType: 4,
      classId: Items.classIdForCode('amu'),
      quality: 4,
      itemFlags: ItemRecordFlags.Identified,
      statsLists: [
        {
          stateNo: 0,
          flags: ListFlagsMagic,
          stats: [
            { id: StatElementalSkills, value: 1, layer: 1 },
            { id: StatElementalSkills, value: 2, layer: 2 },
          ],
        },
      ],
    });

    const merged = Engine.mergedStats(amulet);

    expect(valueOf(merged, StatElementalSkills, 1)).toBe(1);
    expect(valueOf(merged, StatElementalSkills, 2)).toBe(2);
  });

  it('excludes set bonuses by default and opts in', () => {
    const crest = crestWithUm(LocationStash);

    crest.statsLists.push({
      stateNo: ItemStatListStates.ItemSet1,
      flags: ListFlagsMagic,
      stats: [{ id: StatFireResist, value: 50 }],
    });

    expect(valueOf(Engine.mergedStats(crest), StatFireResist)).toBe(30);
    expect(valueOf(Engine.mergedStats(crest, { includeSetBonuses: true }), StatFireResist)).toBe(
      80,
    );
  });

  it("counts a socketed jewel's own affixes", () => {
    // A jewel carries CAPTURED stats, so the gems.txt synthesis deliberately returns nothing for
    // it — and folding fillers in through the synthesis ALONE therefore counted it zero times while
    // socketFillerStats reported it. The same hole swallowed every filler of a server-side capture,
    // which records the mods the engine already assigned.
    const jewel = createUnit({
      unitType: 4,
      classId: Items.classIdForCode('jew'),
      quality: 4,
      itemFlags: ItemRecordFlags.Identified,
      statsLists: [
        { stateNo: 0, flags: ListFlagsMagic, stats: [{ id: StatFireResist, value: 15 }] },
      ],
    });

    const helm = createUnit({
      unitType: 4,
      classId: Items.classIdForCode('xsk'),
      itemFlags: ItemRecordFlags.Identified | ItemRecordFlags.Socketed,
      statsLists: [
        {
          stateNo: 0,
          flags: ListFlagsExtended,
          stats: [
            { id: StatDefense, value: 76 },
            { id: 194, value: 1 },
          ],
        },
      ],
      items: [jewel],
    });

    expect(valueOf(Engine.mergedStats(helm), StatFireResist)).toBe(15);

    // The two entry points must agree about the same filler.
    expect(
      Engine.socketFillerStats(jewel, helm).find(s => s.statId === StatFireResist)?.value,
    ).toBe(15);

    expect(valueOf(Engine.mergedStats(helm, { includeSockets: false }), StatFireResist)).toBe(0);
  });

  it('applies op 13 to the BASE defense rather than the merged one', () => {
    // ItemStatOps' own doc calls this load-bearing: the percent applies to the BASE array, never to
    // base-plus-affixes. A fixture whose defence lives only on the base list cannot tell the two
    // apart, so this one splits it 100 base + 100 affix under +100%.
    const armor = createUnit({
      unitType: 4,
      classId: Items.classIdForCode('xea'),
      quality: 4,
      itemFlags: ItemRecordFlags.Identified,
      statsLists: [
        { stateNo: 0, flags: ListFlagsExtended, stats: [{ id: StatDefense, value: 100 }] },
        {
          stateNo: 0,
          flags: ListFlagsMagic,
          stats: [
            { id: StatDefense, value: 100 },
            { id: StatArmorPercent, value: 100 },
          ],
        },
      ],
    });

    // 100 + 100 + (100 base * 100%) = 300. Reading the merged 200 as the base gives 400.
    expect(valueOf(Engine.mergedStats(armor), StatDefense)).toBe(300);
  });

  it('drops a stat that cancels to zero rather than returning 0', () => {
    // Absent and 0 read the same way to a summing consumer, but not to one applying an "at most N"
    // bound — a leaked zero would satisfy every such bound.
    const ring = createUnit({
      unitType: 4,
      classId: Items.classIdForCode('rin'),
      quality: 4,
      itemFlags: ItemRecordFlags.Identified,
      statsLists: [
        { stateNo: 0, flags: ListFlagsMagic, stats: [{ id: StatFireResist, value: 20 }] },
        { stateNo: 0, flags: ListFlagsMagic, stats: [{ id: StatFireResist, value: -20 }] },
      ],
    });

    expect(Engine.mergedStats(ring).stats.some(s => s.statId === StatFireResist)).toBe(false);
  });

  it('excludes an UNEARNED set tier even with bonuses on', () => {
    // The opt-in reads the record's own tiers, and an unearned tier keeps STATLIST_SET while an
    // earned one has it cleared. That bit — not the state number — is what separates them.
    const crest = crestWithUm(LocationStash);

    crest.statsLists.push({
      stateNo: ItemStatListStates.ItemSet1,
      flags: ListFlagsMagic,
      stats: [{ id: StatFireResist, value: 50 }],
    });
    crest.statsLists.push({
      stateNo: ItemStatListStates.ItemSet1 + 1,
      flags: ListFlagsMagic | ListFlagsSet,
      stats: [{ id: StatFireResist, value: 500 }],
    });

    // 15 own + 15 rune + 50 earned. The unearned 500 stays out.
    expect(valueOf(Engine.mergedStats(crest, { includeSetBonuses: true }), StatFireResist)).toBe(
      80,
    );
  });

  it('returns stats layer-major', () => {
    // The key is `(layer << 16) | stat`, so ascending key order sorts by LAYER first. A caller
    // binary-searching by stat id would be wrong.
    const amulet = createUnit({
      unitType: 4,
      classId: Items.classIdForCode('amu'),
      quality: 4,
      itemFlags: ItemRecordFlags.Identified,
      statsLists: [
        {
          stateNo: 0,
          flags: ListFlagsMagic,
          stats: [
            { id: 127, value: 1 },
            { id: 83, value: 2, layer: 1 },
            { id: StatFireResist, value: 20 },
          ],
        },
      ],
    });

    expect(Engine.mergedStats(amulet).stats.map(s => `${s.statId}/${s.layer}`)).toEqual([
      '39/0',
      '127/0',
      '83/1',
    ]);
  });

  it("exposes a filler's own contribution separately", () => {
    const crest = crestWithUm(LocationStash);

    const um = Engine.socketFillerStats(crest.items[0] as Unit, crest);
    expect(um.find(s => s.statId === StatFireResist)?.value).toBe(15);

    // Keyed by the HOST: an Um is +22 all resist in a shield and +15 in a helm, and the difference
    // is gems.txt `gemapplytype`, which is why the host has to be passed.
    const shield = createUnit({
      unitType: 4,
      classId: Items.classIdForCode('lrg'),
      itemFlags: ItemRecordFlags.Identified | ItemRecordFlags.Socketed,
    });

    const inShield = Engine.socketFillerStats(crest.items[0] as Unit, shield);
    expect(inShield.find(s => s.statId === StatFireResist)?.value).toBe(22);
  });
  it('keeps a worn set piece its socket fillers', () => {
    // The GAME does not: 0x4c15fd gates a loop on quality 5 that detaches the item's stat list and
    // rebuilds it through ITEM_ProcessSetItemEquip, so the character is granted 15. That divergence
    const worn = crestWithUm(LocationEquipped);

    // The Crest grants res-all 15 of its OWN and an Um grants a helm another 15, so the two are
    // indistinguishable by presence — only the NUMBER says whether the rune counted.
    expect(sectionOf(Engine.render(worn), ItemTooltipSection.Modifiers)).toContain(
      'All Resistances +30',
    );
    expect(valueOf(Engine.mergedStats(worn), StatFireResist)).toBe(30);
  });

  it('grants the same whether the piece is worn or stashed', () => {
    // The modifier block is what the fillers reach; the set sections legitimately differ, because
    // wearing the piece is what lights a tier.
    expect(
      sectionOf(Engine.render(crestWithUm(LocationEquipped)), ItemTooltipSection.Modifiers),
    ).toEqual(sectionOf(Engine.render(crestWithUm(LocationStash)), ItemTooltipSection.Modifiers));
  });
});
