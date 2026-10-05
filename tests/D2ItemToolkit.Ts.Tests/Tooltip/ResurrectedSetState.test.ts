import { describe, expect, it } from 'vitest';
import type { TxtFile } from '../../../src/D2ItemToolkit.Ts/src/Data/TxtFile.js';
import { GameVariant } from '../../../src/D2ItemToolkit.Ts/src/Data/GameVariant.js';
import { ItemRecordFlags } from '../../../src/D2ItemToolkit.Ts/src/Stats/ItemRecord.js';
import { ItemStatListFlags } from '../../../src/D2ItemToolkit.Ts/src/Stats/ItemStatReader.js';
import { createUnit, type Unit } from '../../../src/D2ItemToolkit.Ts/src/Stats/Unit.js';
import { ItemQualityNo } from '../../../src/D2ItemToolkit.Ts/src/Tooltip/ItemNameBuilder.js';
import { TooltipEngine } from '../../../src/D2ItemToolkit.Ts/src/Tooltip/TooltipEngine.js';

/**
 * D2R's worn mask, ITEMS_GetSetItemsMask 0x14022eb70: grid type 3, quality 5, flags 0x4000 and
 * 0x100 clear (0x14022ec4e-0x14022ec75) — and no identified test, unlike the piece list's ownership
 * walk (0x1401d3889). The twin of ResurrectedSetStateTests.cs.
 */
const Engine = TooltipEngine.forVariant(GameVariant.ReignOfTheWarlock);

const SigonsGage = 35; // hgl, add func 2, aprop1a swing3 30
const SigonsVisor = 36; // ghm

const BodyHead = 1;
const BodyGloves = 10;

function piece(engine: TooltipEngine, row: number, x: number, identified = true): Unit {
  const code = (engine.data.setItems as TxtFile).getString(row, 'item').trim();
  return createUnit({
    unitType: 4,
    quality: ItemQualityNo.Set,
    itemFlags: identified ? ItemRecordFlags.Identified : 0,
    fileIndex: row,
    classId: engine.items.classIdForCode(code),
    location: 1,
    x,
    itemLevel: 50,
    statsLists: [
      {
        stateNo: 0,
        flags: ItemStatListFlags.Extended,
        stats: [
          { id: 31, value: 10 },
          { id: 72, value: 20 },
          { id: 73, value: 20 },
        ],
      },
    ],
  });
}

function wearer(...carried: Unit[]): Unit {
  return createUnit({
    unitType: 0,
    classId: 0,
    items: carried,
    statsLists: [
      {
        stateNo: 0,
        flags: 0,
        stats: [
          { id: 0, value: 100 },
          { id: 2, value: 100 },
          { id: 12, value: 60 },
        ],
      },
    ],
  });
}

function bit(engine: TooltipEngine, row: number): number {
  return 1 << (engine.sets.pieceAt(row)?.slot ?? -1);
}

describe('the D2R worn set mask', () => {
  it('an unidentified worn sibling lights its bit but is not owned', () => {
    const gage = piece(Engine, SigonsGage, BodyGloves);
    const state = Engine.setStateOf(
      gage,
      wearer(gage, piece(Engine, SigonsVisor, BodyHead, false)),
    );

    expect(state.ownedSetItemIds).toEqual([SigonsGage]);
    expect(state.wornMaskIncludingSelf).toBe(bit(Engine, SigonsGage) | bit(Engine, SigonsVisor));
  });

  it('an unidentified worn sibling raises the first tier', () => {
    // Tier 0 is state 165; swing3 is stat 93. The record carries the enabled list, and the mask
    // decides whether the writer reaches it.
    const gage = piece(Engine, SigonsGage, BodyGloves);
    gage.statsLists.push({
      stateNo: 165,
      flags: ItemStatListFlags.Magic,
      stats: [{ id: 93, value: 30 }],
    });

    const worn = Engine.render(
      gage,
      wearer(gage, piece(Engine, SigonsVisor, BodyHead, false)),
    ).coloredText.split('\n');
    const alone = Engine.render(gage, wearer(gage)).coloredText.split('\n');

    expect(worn).toContain('ÿc2+30% Increased Attack Speed');
    expect(worn).toContain("ÿc1Sigon's Visor");
    expect(alone).not.toContain('ÿc2+30% Increased Attack Speed');
  });

  it('LoD keeps the identified test', () => {
    const lod = TooltipEngine.embedded;
    const gage = piece(lod, SigonsGage, BodyGloves);
    const visor = piece(lod, SigonsVisor, BodyHead, false);

    expect(lod.setStateOf(gage, wearer(visor)).wornMaskIncludingSelf).toBe(bit(lod, SigonsGage));
  });
});
