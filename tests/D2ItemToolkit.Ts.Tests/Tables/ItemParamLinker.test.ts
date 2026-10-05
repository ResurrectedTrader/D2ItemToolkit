import { describe, expect, it } from 'vitest';
import type { TxtFile } from '../../../src/D2ItemToolkit.Ts/src/Data/TxtFile.js';
import { GameVariant } from '../../../src/D2ItemToolkit.Ts/src/Data/GameVariant.js';
import { ItemRecordFlags } from '../../../src/D2ItemToolkit.Ts/src/Stats/ItemRecord.js';
import { ItemStatListFlags } from '../../../src/D2ItemToolkit.Ts/src/Stats/ItemStatReader.js';
import { createUnit } from '../../../src/D2ItemToolkit.Ts/src/Stats/Unit.js';
import { TooltipEngine } from '../../../src/D2ItemToolkit.Ts/src/Tooltip/TooltipEngine.js';

/**
 * DATATBLS_ItemParamLinker 0x140214e40: atoi for a leading `-` or digit, else skills `skill`,
 * montype `type`, states `state`, else 0 — through FOG_GetRowFromTxt's ASCII case fold. The twin
 * of ItemParamLinkerTests.cs.
 */
const Engine = TooltipEngine.forVariant(GameVariant.ReignOfTheWarlock);

describe('the item param linker', () => {
  it.each([
    ['12', 12],
    ['-5', -5],
    ['12abc', 12], // atoi stops at the first non-digit
    [' 5', 0], // not a digit first, so a name, and no name matches
    ['Flame Wave', 398], // skills
    ['feral rage', 232], // the case fold; uniqueitems ships it lower-case
    ['undead', 1], // montype
    ['fullsetgeneric', 175], // states
    ['no such name', 0],
    ['', 0],
  ])('resolves %j as the game does', (cell, expected) => {
    expect(Engine.data.paramLinker.resolve(cell)).toBe(expected);
  });

  it.each([
    ['12', 12],
    ['12abc', 0],
    ['Teleport', 0], // 1.14d's linker is untraced: names stay 0
  ])('1.14d keeps the whole-cell reading of %j', (cell, expected) => {
    expect(TooltipEngine.embedded.data.paramLinker.resolve(cell)).toBe(expected);
  });

  it("a named skill param lands on that skill's layer", () => {
    // Opalvein's prop1 is `att-skill` "Flame Wave" 2..15: func 11 writes stat 195 at layer
    // (398 << 6) | 15 with the chance, 2. Parsed as 0 it went to layer 15.
    const layer = (398 << 6) | 15;
    const unit = createUnit({
      unitType: 4,
      quality: 7,
      fileIndex: (Engine.data.uniqueItems as TxtFile).findRow('index', 'Opalvein'),
      classId: Engine.items.classIdForCode('rin'),
      itemFlags: ItemRecordFlags.Identified,
      statsLists: [
        { stateNo: 0, flags: ItemStatListFlags.Magic, stats: [{ id: 195, value: 2, layer }] },
      ],
    });

    const ranges = Engine.ranges(unit);
    const matching = ranges.stats.filter(r => r.statId === 195);
    expect(matching).toHaveLength(1);
    expect(matching[0]?.layer).toBe(layer);
    expect(matching[0]?.low).toBe(2);
    expect(matching[0]?.high).toBe(2);
    expect(ranges.unattributed).not.toContain(195);
  });
});
