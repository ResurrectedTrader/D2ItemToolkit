import { describe, expect, it } from 'vitest';
import { ItemRecordFlags } from '../../../src/D2ItemToolkit.Ts/src/Stats/ItemRecord.js';
import {
  createUnit,
  type Unit,
  type UnitStat,
} from '../../../src/D2ItemToolkit.Ts/src/Stats/Unit.js';
import { ItemTable } from '../../../src/D2ItemToolkit.Ts/src/Tables/ItemTable.js';
import { MagicAffixTable } from '../../../src/D2ItemToolkit.Ts/src/Tables/MagicAffixTable.js';
import { D2DataFiles } from '../../../src/D2ItemToolkit.Ts/src/Tables/TxtDataProviders.js';
import {
  ItemTooltipColor,
  ItemTooltipSection,
} from '../../../src/D2ItemToolkit.Ts/src/Tooltip/ItemTooltip.js';
import {
  TooltipEngine,
  type Tooltip,
  type TooltipOptions,
} from '../../../src/D2ItemToolkit.Ts/src/Tooltip/TooltipEngine.js';
import type { TxtFile } from '../../../src/D2ItemToolkit.Ts/src/Data/TxtFile.js';

/**
 * The showRolledRanges render flag. The game has no such mode, so the only things worth asserting
 * are that it is INERT when off, that it annotates exactly the lines where one span is unambiguous,
 * and that it stays silent on the lines where it would not be.
 */
const Data = D2DataFiles.load();
const Engine = TooltipEngine.embedded;
const Items = new ItemTable(Data.weapons, Data.armor, Data.misc);
const uniqueItems = Data.uniqueItems as TxtFile;
const runes = Data.runes as TxtFile;

/**
 * A record whose stats sit at the midpoint of every reconstructed span, so the rendered lines are
 * ones a real item could carry and every annotation has something to attach to.
 */
function atMidRoll(shell: Unit): Unit {
  const ranges = Engine.ranges(shell);

  const baseStats: UnitStat[] = [];
  const modStats: UnitStat[] = [];

  for (const range of ranges.stats) {
    const stat: UnitStat = {
      id: range.statId,
      layer: range.layer,
      value: range.low + Math.trunc((range.high - range.low) / 2),
    };

    (range.statId === 31 ? baseStats : modStats).push(stat);
  }

  // A layer-rolling property contributes a stat too — at one of its possible layers, the low one
  // here. Without it the record has no skill line to inspect.
  for (const range of ranges.layerVaries) {
    modStats.push({ id: range.statId, layer: range.layerLow, value: range.value });
  }

  shell.statsLists = [
    { stateNo: 0, flags: 2147483648, stats: baseStats },
    { stateNo: 0, flags: 64, stats: modStats },
  ];

  return shell;
}

function uniqueItem(index: string): Unit {
  const row = uniqueItems.findRow('index', index);
  expect(row, index).toBeGreaterThanOrEqual(0);

  return atMidRoll(
    createUnit({
      unitType: 4,
      quality: 7,
      fileIndex: row,
      classId: Items.classIdForCode(uniqueItems.getString(row, 'code').trim()),
      itemFlags: ItemRecordFlags.Identified,
      itemLevel: 80,
    }),
  );
}

/**
 * Ranges on, colour off. The DEFAULT paints them grey, which embeds two markers into every
 * annotated line — so the tests below, which are about the TEXT, opt out of it. The default itself
 * is pinned by 'is grey unless asked otherwise'.
 */
const annotating: TooltipOptions = { ranges: { color: -1 } };

function texts(tooltip: Tooltip): string[] {
  return tooltip.lines.map(l => l.text ?? '');
}

describe('the showRolledRanges flag', () => {
  it('is inert when off', () => {
    // The whole point: an un-annotated render has to stay what the game draws, which is what the
    // corpus differential also holds.
    const item = uniqueItem('The Eye of Etlich');
    expect(Engine.render(item, null, {}).text).toBe(Engine.render(item).text);
  });

  it('gives a modifier line its span', () => {
    const lines = texts(Engine.render(uniqueItem('The Eye of Etlich'), null, annotating));

    expect(lines.some(l => l.includes('Life stolen per hit [3-7]'))).toBe(true);
    expect(lines.some(l => l.includes('Defense vs. Missile [10-40]'))).toBe(true);
    expect(lines.some(l => l.includes('Light Radius [1-5]'))).toBe(true);
  });

  it('gives the Defense line the base armour roll', () => {
    // The one SECTION that gets annotated: Defense shows a single stat whose base rolls.
    const shako = uniqueItem('Harlequin Crest');
    const lines = texts(Engine.render(shako, null, annotating));

    const expectedSpan =
      ' [' +
      String(Items.getInt(shako.classId, 'minac')) +
      '-' +
      String(Items.getInt(shako.classId, 'maxac')) +
      ']';

    expect(lines.some(l => l.startsWith('Defense:') && l.includes(expectedSpan))).toBe(true);
  });

  it('gives a two-valued damage line both spans', () => {
    // "Adds 1-4 cold damage" prints coldmindam AND coldmaxdam, whose spans differ (1..2 and 3..5).
    // One number would belong to neither half, so both are written positionally.
    const lines = texts(Engine.render(uniqueItem('The Eye of Etlich'), null, annotating));
    const adds = lines.filter(l => l.includes('cold damage'));

    expect(adds.length).toBe(1);
    expect(adds[0]).toContain('[(1-2)-(3-5)]');
  });

  it('collapses a group line’s members to one span', () => {
    // Every stat a DescGrp line covers shares the single number it prints, so their spans agree —
    // repeating them would give "[(2-5)-(2-5)-(2-5)-(2-5)]". Not a range on this item, so nothing
    // at all rather than a four-way degenerate.
    const lines = texts(Engine.render(uniqueItem('The Eye of Etlich'), null, annotating));
    const allSkills = lines.filter(l => l.includes('to All Skills'));

    expect(allSkills.length).toBe(1);
    expect(allSkills[0]).not.toContain('[');
  });

  it('annotates the single-valued enhanced-damage line', () => {
    // The counterpart: Enhanced Damage is also aggregated, but it prints the MIN half alone, so its
    // span is unambiguous and DOES appear. Getting this wrong silences the most-wanted range.
    const lines = texts(Engine.render(uniqueItem("Titan's Revenge"), null, annotating));

    expect(lines.some(l => l.includes('Enhanced Damage [150-200]'))).toBe(true);
  });

  it('decodes a packed value rather than printing it raw', () => {
    // Stat 204 packs (maxCharges << 8) + current. Printed raw the span reads "[2306-2313]"; decoded
    // it is the CURRENT charge count, which is the number the line shows first and the only part the
    // seed varies.
    const row = runes.findRow('Name', 'Runeword88');
    expect(row).toBeGreaterThanOrEqual(0);

    const unit = atMidRoll(
      createUnit({
        unitType: 4,
        quality: 2,
        classId: Items.classIdForCode('crs'),
        itemFlags: ItemRecordFlags.Identified | ItemRecordFlags.Runeword,
        itemLevel: 70,
        magicPrefix: [Data.strings.resolveKey(runes.getString(row, 'Name').trim()), 0, 0],
      }),
    );

    const charges = texts(Engine.render(unit, null, annotating)).filter(l => l.includes('Charges'));
    expect(charges.length).toBe(1);

    const packed = Engine.ranges(unit).stats.filter(r => r.statId === 204);
    expect(packed.length).toBe(1);
    expect(packed[0]?.isPackedEncoding).toBe(true);
    expect(packed[0]?.isRange).toBe(true);

    // The raw span must NOT appear; the decoded charge count must.
    const raw = '[' + String(packed[0]?.low) + '-' + String(packed[0]?.high) + ']';
    const decoded =
      '[' + String(packed[0]?.displayLow) + '-' + String(packed[0]?.displayHigh) + ']';

    expect(charges[0]).not.toContain(raw);
    expect(charges[0]).toContain(decoded);

    // And the decoded end really is the low byte, which is the "5" in "(5/9 Charges)".
    expect(packed[0]?.displayLow).toBe((packed[0]?.low ?? 0) & 0xff);
    expect(packed[0]?.displayHigh).toBe((packed[0]?.high ?? 0) & 0xff);
  });

  it('never has a span to show for a by-time stat', () => {
    // Func 18 packs property.min and property.max straight in and never rolls (0x65f870 has no
    // RollRandomValue call), so both ends produce the identical word. There is nothing to unpack for
    // a range because there is no range — which is why by-time needs no decoding even though it is a
    // packed encoding.
    const affixes = new MagicAffixTable(Data);
    let affix = -1;

    for (let id = 1; id <= affixes.rowCount && affix < 0; ++id) {
      const resolved = affixes.tryResolve(id);
      if (resolved === null) {
        continue;
      }

      for (let mod = 1; mod <= 3; ++mod) {
        if (
          resolved.table.getString(resolved.row, 'mod' + String(mod) + 'code').trim() === 'ac/time'
        ) {
          affix = id;
        }
      }
    }

    expect(affix).toBeGreaterThan(0);

    const unit = createUnit({
      unitType: 4,
      quality: 4,
      classId: Items.classIdForCode('cap'),
      itemFlags: ItemRecordFlags.Identified,
      magicPrefix: [affix, 0, 0],
    });

    const byTime = Engine.ranges(unit).stats.filter(r => r.statId === 268);
    expect(byTime.length).toBe(1);
    expect(byTime[0]?.isPackedEncoding).toBe(true);
    expect(byTime[0]?.isRange).toBe(false);
  });

  it('is grey unless asked otherwise', () => {
    // A range is text the game never draws, so inheriting the stat line's blue made it read as part
    // of the line. The default is the game's own grey — asserted here rather than left implicit,
    // because every other test in this file opts out of it.
    const lines = texts(Engine.render(uniqueItem('The Eye of Etlich'), null, { ranges: {} }));
    const light = lines.filter(l => l.includes('Light Radius'));

    expect(light[0]).toContain(ItemTooltipColor.Marker + '5 [1-5]' + ItemTooltipColor.Marker + '3');
  });

  it('can paint the annotation its own colour', () => {
    const options: TooltipOptions = {
      ranges: { color: ItemTooltipColor.White },
    };

    const lines = texts(Engine.render(uniqueItem('The Eye of Etlich'), null, options));
    const light = lines.filter(l => l.includes('Light Radius'));

    // The annotation is wrapped whole — its leading space included, which has no glyph — by a
    // marker for the range colour and then one restoring the line's own.
    expect(light[0]).toContain(ItemTooltipColor.Marker + '0 [1-5]' + ItemTooltipColor.Marker + '3');
  });

  it('does not let a coloured annotation leak into the next line', () => {
    // The running colour is tracked from the UN-annotated text, so the marker the annotation embeds
    // cannot change what the following line inherits.
    const item = uniqueItem('The Eye of Etlich');

    const plain = Engine.render(item).lines;
    const painted = Engine.render(item, null, {
      ranges: { color: ItemTooltipColor.White },
    }).lines;

    expect(painted.length).toBe(plain.length);
    for (let at = 0; at < plain.length; ++at) {
      expect(painted[at]?.color).toBe(plain[at]?.color);
    }
  });

  it('leaves a stat that could not have varied alone', () => {
    const lines = texts(Engine.render(uniqueItem('Harlequin Crest'), null, annotating));
    const marked = lines.filter(l => l.includes('['));

    expect(marked.length).toBe(1);
    expect(marked[0]?.startsWith('Defense:')).toBe(true);
  });

  it('lets the caller choose the format', () => {
    const options: TooltipOptions = {
      ranges: {
        color: -1,
        format: ranges =>
          ranges[0]?.statId === 89
            ? ' (' + String(ranges[0].low) + '..' + String(ranges[0].high) + ')'
            : null,
      },
    };

    const lines = texts(Engine.render(uniqueItem('The Eye of Etlich'), null, options));

    expect(lines.some(l => l.includes('Light Radius (1..5)'))).toBe(true);

    // Returning null for every other stat suppresses them, so exactly one line is marked.
    expect(lines.filter(l => l.includes('(1..5)')).length).toBe(1);
    expect(lines.some(l => l.includes('[3-7]'))).toBe(false);
  });

  it('reports which stat each line shows', () => {
    // Independent of the flag: the line-to-stat mapping is what lets a caller build its own display
    // instead of re-deriving which stat a line came from.
    const tooltip = Engine.render(uniqueItem('The Eye of Etlich'));

    const light = tooltip.lines.filter(l => (l.text ?? '').includes('Light Radius'));
    expect(light.length).toBe(1);
    expect(light[0]?.statId).toBe(89);
    expect(light[0]?.layer).toBe(0);

    // A line that shows no stat says so rather than claiming stat 0.
    for (const line of tooltip.lines.filter(l => l.section === ItemTooltipSection.RequiredLevel)) {
      expect(line.statId).toBe(-1);
    }
  });

  it('reports a skill line’s layer', () => {
    // Ormus' Robes grants a rolled sorceress skill, so the line's identity is the LAYER — a stat id
    // alone would not say which skill.
    const tooltip = Engine.render(uniqueItem("Ormus' Robes"));
    const skill = tooltip.lines.filter(l => l.statId === 107);

    expect(skill.length).toBeGreaterThan(0);
    expect(skill[0]?.layer).toBeGreaterThanOrEqual(36);
    expect(skill[0]?.layer).toBeLessThanOrEqual(60);
  });
});
