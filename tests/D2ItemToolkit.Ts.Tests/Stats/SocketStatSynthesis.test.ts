import { describe, expect, it } from 'vitest';
import { TooltipEngine, unitFromJson, type Unit } from '../../../src/D2ItemToolkit.Ts/src/index.js';
import { SocketStatSynthesis } from '../../../src/D2ItemToolkit.Ts/src/Stats/SocketStatSynthesis.js';

/**
 * A client capture hands over gems and runes with an EMPTY stat chain: every caller of the
 * assignment (ITEM_ApplySocketableAndEquipStats 0x4c0cf0) lives in D2Common/D2Game, and the client
 * is handed the host's already-merged stats in the item packet. So the host's blue block has to be
 * rebuilt from gems.txt, which is what SocketStatSynthesis does.
 *
 * The two shields below are the exact items from a real capture that first exposed this, and the
 * expected lines are the ones the GAME drew for them. The C# counterpart is
 * tests/D2ItemToolkit.Net.Tests/Stats/SocketStatSynthesisTests.cs and pins the same strings.
 */
describe('socket stat synthesis', () => {
  const classId = (code: string): number => TooltipEngine.embedded.items.classIdForCode(code);

  /** An item with fillers that carry no statlist at all — the captured shape. */
  const host = (hostCode: string, ...fillerCodes: string[]): Unit =>
    unitFromJson({
      unitType: 4,
      classId: classId(hostCode),
      quality: 2,
      itemFlags: 2064,
      statsLists: [],
      items: fillerCodes.map(code => ({ unitType: 4, classId: classId(code) })),
    });

  const render = (item: Unit): string => TooltipEngine.embedded.render(item).text;

  it('gives a shield the shield-slot mods of its runes', () => {
    // Hyperion, gemapplytype 2 -> the `shield` array. Ko is dex 10 (twice) and Mal is red-mag 7,
    // which is character for character what the game drew for this item.
    const text = render(host('urg', 'r18', 'r18', 'r23'));

    expect(text).toContain('+20 to Dexterity');
    expect(text).toContain('Magic Damage Reduced by 7');
  });

  it('gives body armor the helm-slot mods of its runes', () => {
    // Wire Fleece, gemapplytype 1 -> the `helm` array, which is what body armor uses too.
    // Shael balance2 20, Thul res-cold 30, Lem gold% 50.
    const text = render(host('utu', 'r13', 'r10', 'r20'));

    expect(text).toContain('+20% Faster Hit Recovery');
    expect(text).toContain('Cold Resist +30%');
    expect(text).toContain('50% Extra Gold from Monsters');
  });

  it('takes the slot from the HOST, not the filler', () => {
    // Ko is dex 10 in all three arrays, so it proves nothing on its own. Thul does: cold damage in
    // a weapon (gemapplytype 0), cold RESIST in armour. Same rune, same document, two different
    // lines — that is ITEM_GetItemsTxt_bGemApplyType(host) at 0x4c0dee.
    const weapon = render(host('ssd', 'r10'));
    const armor = render(host('utu', 'r10'));

    expect(weapon).toContain('Adds 3-14 cold damage');
    expect(weapon).not.toContain('Cold Resist');

    expect(armor).toContain('Cold Resist +30%');
    expect(armor).not.toContain('cold damage');
  });

  it('leaves a filler that already carries stats alone', () => {
    // A server-side producer records the mods the engine assigned. Synthesising on top of those
    // would count the gem twice, so a filler with a chain of its own is not touched.
    const item = unitFromJson({
      unitType: 4,
      classId: classId('urg'),
      quality: 2,
      itemFlags: 2064,
      statsLists: [],
      items: [
        {
          unitType: 4,
          classId: classId('r18'),
          statsLists: [{ stateNo: 0, flags: 64, stats: [{ id: 2, value: 10 }] }],
        },
      ],
    });

    const text = TooltipEngine.embedded.render(item).text;

    expect(text).toContain('+10 to Dexterity');
    expect(text).not.toContain('+20 to Dexterity');
  });

  it('discards an equipped set item’s fillers, as the recalc does', () => {
    // ITEM_RecalcAllEquippedItems 0x4c1350 ends with a loop over the eleven body slots that fires
    // only for quality 5 (0x4c15ec-0x4c162b). It calls STATLIST_RemoveFromOwnerAndRecalc
    // (0x4c1658), which detaches the item's whole stat list (0x6277fa -> STATLIST_DetachAndRecalc),
    // then rebuilds with ITEM_ApplySocketableAndEquipStats(wearer, THE SET ITEM, 0) at 0x4c1661 —
    // a2 is the set item, not a filler, so both IsOfType gates fail (0x4c0d30 / 0x4c0da3) and it
    // lands on ITEM_ProcessSetItemEquip. The fillers are never re-applied.
    //
    // A real capture is what caught it: Tal Rasha's Horadric Crest with an Um in it draws
    // `All Resistances +15` — its own set property alone — while a runeword shield in the same
    // snapshot draws all three of its runes' mods.
    const worn = unitFromJson({
      unitType: 4,
      classId: classId('xsk'),
      quality: 5,
      itemFlags: 2064,
      fileIndex: 80,
      statsLists: [],
      items: [{ unitType: 4, classId: classId('r22') }],
    });

    expect(TooltipEngine.embedded.renderSetItem(worn, { isEquipped: true }).text).not.toContain(
      'All Resistances',
    );

    // Not equipped, so the loop never ran and Um's helm mod is still on it.
    expect(TooltipEngine.embedded.renderSetItem(worn, { isEquipped: false }).text).toContain(
      'All Resistances +15',
    );
  });

  it('loses fillers that way only for a SET item', () => {
    // 0x4c1614 gates the loop on GetItemQuality == 5. A normal-quality host keeps its fillers
    // however it is carried — which is why the two runeword items in the same capture render their
    // runes and the set item does not.
    expect(SocketStatSynthesis.fillersAreDiscardedByRecalc(host('urg', 'r18'), true)).toBe(false);

    expect(
      SocketStatSynthesis.fillersAreDiscardedByRecalc(
        unitFromJson({ unitType: 4, quality: 5, itemFlags: 16 }),
        true,
      ),
    ).toBe(true);

    // 0x4c1618 / 0x4c1628 exclude a broken item and flag 0x4000.
    expect(
      SocketStatSynthesis.fillersAreDiscardedByRecalc(
        unitFromJson({ unitType: 4, quality: 5, itemFlags: 272 }),
        true,
      ),
    ).toBe(false);
  });

  it('never synthesises for a jewel', () => {
    // 0x4c0da3 tests type 74 `rune` and 0x4c0d30 type 20 `gem`; a jewel matches neither and falls
    // through to ITEM_ProcessSetItemEquip (0x4c0e06). gems.txt has no row for it either way, so
    // there is nothing to synthesise even if the gate let it through.
    const text = render(host('lrg', 'jew'));

    expect(text).not.toContain('Dexterity');
    expect(text).not.toContain('Defense: ÿc3');
  });

  it('excludes the synthesised stats when sockets are excluded', () => {
    const text = TooltipEngine.embedded.render(host('urg', 'r18', 'r18', 'r23'), null, {
      includeSockets: false,
    }).text;

    expect(text).not.toContain('Dexterity');
    expect(text).not.toContain('Magic Damage Reduced');
  });

  it('attributes them to the sockets in the breakdown', () => {
    const breakdown = TooltipEngine.embedded.breakdown(host('urg', 'r18', 'r18', 'r23'));
    const socketText = breakdown.sockets.map(line => (line.text ?? '').replace('\n', ''));

    expect(socketText).toContain('+20 to Dexterity');
    expect(socketText).toContain('Magic Damage Reduced by 7');
  });
});
