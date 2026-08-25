import { describe, expect, it } from 'vitest';
import {
  ItemRecordFlags,
  ItemRecordReader,
} from '../../../src/D2ItemToolkit.Ts/src/Stats/ItemRecord.js';
import { RollSources } from '../../../src/D2ItemToolkit.Ts/src/Stats/RolledRangeReconstructor.js';
import { createUnit, type Unit } from '../../../src/D2ItemToolkit.Ts/src/Stats/Unit.js';
import { ItemTable } from '../../../src/D2ItemToolkit.Ts/src/Tables/ItemTable.js';
import { D2DataFiles } from '../../../src/D2ItemToolkit.Ts/src/Tables/TxtDataProviders.js';
import {
  ItemTooltipColor,
  ItemTooltipSection,
} from '../../../src/D2ItemToolkit.Ts/src/Tooltip/ItemTooltip.js';
import { TooltipEngine } from '../../../src/D2ItemToolkit.Ts/src/Tooltip/TooltipEngine.js';
import type { TxtFile } from '../../../src/D2ItemToolkit.Ts/src/Data/TxtFile.js';

/**
 * Deriving a set item's tooltip state from what the VIEWER carries, instead of asking the caller
 * for two bit masks it cannot compute without the disassembly.
 *
 * The distinction these tests exist for: GetSetItem 0x486770 accepts inventory grid types 1, 3 AND
 * 4 (0x4867d4), so a piece on the alternate weapon set is OWNED and colours green — but
 * ITEMS_GetEquippedSetItemsMask requires type 3 alone (0x62a3f0), so it lights no bit and raises no
 * bonus tier. A caller that treated "equipped" as one thing would light one tier too many, which is
 * the failure this API removes.
 */
const Data = D2DataFiles.load();
const Engine = TooltipEngine.embedded;
const Items = new ItemTable(Data.weapons, Data.armor, Data.misc);
const setItems = Data.setItems as TxtFile;

// The Angelic Raiment pieces, setitems.txt rows. Four pieces over four body slots, which is what
// lets a worn/swapped distinction be built at all.
const AngelicSickle = 50; // sbr, a sword
const AngelicMantle = 51; // rng, body armour
const AngelicHalo = 52; // rin, a ring
const AngelicWings = 53; // amu, an amulet

// A piece of a DIFFERENT set, to prove the scan filters by set id. Arctic HORN and not Arctic
// Binding: Binding is slot 2 inside Arctic Gear, the same slot Angelic Halo holds, so a stray bit
// would OR into one already set and the test could not see the filter go.
const ArcticHorn = 54; // swb, a bow — slot 0

const QualitySet = 5;
const LocationEquipped = 1;
const LocationInventory = 3;

// Body locations. 11 and 12 are the alternate weapon set (0x55f240).
const BodyRightArm = 4;
const BodyTorso = 3;
const BodyRightArmSwap = 11;
const BodyLeftArmSwap = 12;

function piece(setItemRow: number, location: number, x = 0): Unit {
  const code = setItems.getString(setItemRow, 'item').trim();
  const classId = Items.classIdForCode(code);
  expect(classId, code).toBeGreaterThanOrEqual(0);

  return createUnit({
    unitType: 4,
    quality: QualitySet,
    itemFlags: ItemRecordFlags.Identified,
    fileIndex: setItemRow,
    classId,
    location,
    x,
  });
}

function wearer(...carried: Unit[]): Unit {
  return createUnit({ unitType: 0, items: carried });
}

/** The bit a piece contributes, which is its slot INSIDE the set (0x62a474). */
function bit(setItemRow: number): number {
  return 1 << (Engine.sets.pieceAt(setItemRow)?.slot ?? 0);
}

describe('set state derived from the viewer', () => {
  it('counts the hovered piece itself even when the viewer lists nothing', () => {
    // Nothing obliges a caller to repeat the hovered item inside the viewer's items — "what else
    // the player is carrying" is the natural reading of a list passed alongside it. So the hovered
    // piece's own state comes from the ITEM. Here it is identified and in the inventory, which is
    // where GetSetItem looks, so its own row is owned; it is not worn, so no bit lights.
    const state = Engine.setStateOf(piece(AngelicHalo, LocationInventory), wearer());

    expect(state.ownedSetItemIds).toEqual([AngelicHalo]);
    expect(state.wornMaskIncludingSelf).toBe(0);
    expect(state.wornMaskExcludingSelf).toBe(0);
    expect(state.isEquipped).toBe(false);
  });

  it('lights the hovered piece bit without it being listed', () => {
    // The regression this guards: taking the mask only from the viewer's list dropped the hovered
    // piece's own tier whenever the caller did not repeat it.
    const halo = piece(AngelicHalo, LocationEquipped, 6);

    const listed = Engine.setStateOf(
      halo,
      wearer(halo, piece(AngelicMantle, LocationEquipped, BodyTorso)),
    );
    const unlisted = Engine.setStateOf(
      halo,
      wearer(piece(AngelicMantle, LocationEquipped, BodyTorso)),
    );

    expect(unlisted.wornMaskIncludingSelf).toBe(bit(AngelicHalo) | bit(AngelicMantle));
    expect(unlisted.wornMaskIncludingSelf).toBe(listed.wornMaskIncludingSelf);
    expect(unlisted.wornMaskExcludingSelf).toBe(listed.wornMaskExcludingSelf);
  });

  it('owns carried siblings whether or not they are worn', () => {
    const halo = piece(AngelicHalo, LocationEquipped, 6);

    const state = Engine.setStateOf(
      halo,
      wearer(
        halo,
        piece(AngelicWings, LocationInventory),
        piece(AngelicMantle, LocationEquipped, BodyTorso),
      ),
    );

    expect([...(state.ownedSetItemIds ?? [])].sort((a, b) => a - b)).toEqual([
      AngelicMantle,
      AngelicHalo,
      AngelicWings,
    ]);
  });

  it('ignores a piece of another set', () => {
    const halo = piece(AngelicHalo, LocationEquipped, 6);

    const state = Engine.setStateOf(halo, wearer(halo, piece(ArcticHorn, LocationEquipped, 8)));

    expect([...(state.ownedSetItemIds ?? [])]).toEqual([AngelicHalo]);
    expect(state.wornMaskIncludingSelf).toBe(bit(AngelicHalo));
  });

  it('lights a worn sibling’s slot bit', () => {
    const halo = piece(AngelicHalo, LocationEquipped, 6);

    const state = Engine.setStateOf(
      halo,
      wearer(halo, piece(AngelicMantle, LocationEquipped, BodyTorso)),
    );

    expect(state.wornMaskIncludingSelf).toBe(bit(AngelicHalo) | bit(AngelicMantle));

    // Excluding-self differs in exactly the hovered piece's bit, never in any other.
    expect(state.wornMaskExcludingSelf).toBe(bit(AngelicMantle));
  });

  it('owns a piece on the alternate weapon set but lights no bit for it', () => {
    // THE case this API exists for. The sickle sits on body location 11, so
    // INVENTORY_PlaceItemInGrid stamps grid type 4 (0x63b1e2): GetSetItem still takes it, so it
    // colours green, but the worn mask does not, so it raises no tier.
    const halo = piece(AngelicHalo, LocationEquipped, 6);

    const state = Engine.setStateOf(
      halo,
      wearer(halo, piece(AngelicSickle, LocationEquipped, BodyRightArmSwap)),
    );

    expect(state.ownedSetItemIds).toContain(AngelicSickle);
    expect(state.wornMaskIncludingSelf).toBe(bit(AngelicHalo));

    // The same sickle in the ACTIVE weapon slot does light its bit — so the difference is the body
    // location and nothing else about the record.
    const active = Engine.setStateOf(
      halo,
      wearer(halo, piece(AngelicSickle, LocationEquipped, BodyRightArm)),
    );

    expect(active.wornMaskIncludingSelf).toBe(bit(AngelicHalo) | bit(AngelicSickle));
  });

  it('owns both swap slots but lights no bit for either', () => {
    // The boundary is `bodyLoc >= 11`, so BOTH 11 and 12 are the swap pair. Testing 11 alone left
    // `>= 11` narrowable to `=== 11` — which makes a set shield or off-hand on the swap bar light a
    // bit and raise a tier — and to `>= 10`, which stops a worn GLOVE granting its own. Both
    // survived the whole suite until this case existed.
    const halo = piece(AngelicHalo, LocationEquipped, 6);

    for (const swap of [BodyRightArmSwap, BodyLeftArmSwap]) {
      const state = Engine.setStateOf(
        halo,
        wearer(halo, piece(AngelicSickle, LocationEquipped, swap)),
      );

      expect(state.ownedSetItemIds, String(swap)).toContain(AngelicSickle);
      expect(state.wornMaskIncludingSelf, String(swap)).toBe(bit(AngelicHalo));
    }

    // ...and the slot BELOW the boundary is worn, which is the other half of `>= 11`.
    const BodyGloves = 10;

    expect(
      Engine.setStateOf(halo, wearer(halo, piece(AngelicSickle, LocationEquipped, BodyGloves)))
        .wornMaskIncludingSelf,
    ).toBe(bit(AngelicHalo) | bit(AngelicSickle));
  });

  it('grants no bonus for an unequippable piece', () => {
    // The mask's OTHER refusal, flag 0x4000 (0x62a446). Dropping it from the mask left every test
    // green, because only the Broken half was ever exercised.
    const CannotEquip = 0x4000;

    const halo = piece(AngelicHalo, LocationEquipped, 6);
    const mantle = piece(AngelicMantle, LocationEquipped, BodyTorso);
    const blocked = { ...mantle, itemFlags: mantle.itemFlags | CannotEquip };

    const state = Engine.setStateOf(halo, wearer(halo, blocked));

    expect(state.ownedSetItemIds).toContain(AngelicMantle);
    expect(state.wornMaskIncludingSelf).toBe(bit(AngelicHalo));
  });

  it('owns an inventory piece but lights no bit for it', () => {
    const halo = piece(AngelicHalo, LocationEquipped, 6);

    const state = Engine.setStateOf(halo, wearer(halo, piece(AngelicWings, LocationInventory)));

    expect(state.ownedSetItemIds).toContain(AngelicWings);
    expect(state.wornMaskIncludingSelf).toBe(bit(AngelicHalo));
  });

  it('does not own an unidentified sibling', () => {
    // GetSetItem requires CheckItemFlag 0x10 (0x4867a2). Every set item drops unidentified, so a
    // sibling just picked up is the normal case — the game paints its row red.
    const halo = piece(AngelicHalo, LocationEquipped, 6);
    const unidentified = { ...piece(AngelicWings, LocationInventory), itemFlags: 0 };

    const state = Engine.setStateOf(halo, wearer(halo, unidentified));

    expect(state.ownedSetItemIds).not.toContain(AngelicWings);
  });

  it('owns a broken piece but grants no bonus for it', () => {
    // The mask refuses flag 0x100 and flag 0x4000 (0x62a446). A shield at zero durability is still
    // carried — and still drawn red by name — but contributes no tier.
    const halo = piece(AngelicHalo, LocationEquipped, 6);
    const mantle = piece(AngelicMantle, LocationEquipped, BodyTorso);
    const broken = { ...mantle, itemFlags: mantle.itemFlags | ItemRecordFlags.Broken };

    const state = Engine.setStateOf(halo, wearer(halo, broken));

    expect(state.ownedSetItemIds).toContain(AngelicMantle);
    expect(state.wornMaskIncludingSelf).toBe(bit(AngelicHalo));
    expect(Engine.earnedSetIdsOf(wearer(halo, broken))).toEqual([]);
  });

  it('does not own a piece on the trade page', () => {
    // GetSetItem walks pages 0 / 3 / 4 / 0xFF (0x4867b3-0x4867bf), which excludes trade. Ground and
    // store are not in the viewer's chain at all.
    const halo = piece(AngelicHalo, LocationEquipped, 6);

    for (const location of [0, 4, 5]) {
      const state = Engine.setStateOf(halo, wearer(halo, piece(AngelicWings, location)));
      expect(state.ownedSetItemIds, String(location)).not.toContain(AngelicWings);
    }
  });

  it('counts two copies of one piece once', () => {
    // The game ORs `1 << slot` (0x62a474), so a second Angelic Halo in the other ring slot lights
    // no new bit. Counting units instead earned a tier off one duplicated piece.
    const halo = piece(AngelicHalo, LocationEquipped, 6);
    const second = piece(AngelicHalo, LocationEquipped, 7);

    const state = Engine.setStateOf(halo, wearer(halo, second));

    expect(state.wornMaskIncludingSelf).toBe(bit(AngelicHalo));
    expect(Engine.earnedSetIdsOf(wearer(halo, second))).toEqual([]);
  });

  it('never turns a viewer’s carried gear into the viewer’s own stats', () => {
    // `items` means socket fillers on an item and carried gear on a wearer, and the stat reader
    // RECURSES for the former. Reading a wearer through that recursion folded every carried item's
    // stats into the viewer's attributes — a +10 strength charm in the backpack met a requirement
    // the character did not.
    const halo = piece(AngelicHalo, LocationEquipped, 6);
    const carried = {
      ...piece(AngelicWings, LocationInventory),
      statsLists: [{ stateNo: 0, flags: 64, stats: [{ id: 0, value: 100 }] }],
    };

    const player = createUnit({
      unitType: 0,
      statsLists: [
        {
          stateNo: 0,
          flags: 0x80000000,
          stats: [
            { id: 0, value: 20 },
            { id: 12, value: 40 },
          ],
        },
      ],
      items: [halo, carried],
    });

    expect(ItemRecordReader.readViewer(player).strength).toBe(20);
  });

  it('takes isEquipped from the hovered item’s own location', () => {
    expect(Engine.setStateOf(piece(AngelicHalo, LocationEquipped, 6), wearer()).isEquipped).toBe(
      true,
    );

    expect(Engine.setStateOf(piece(AngelicHalo, LocationInventory), wearer()).isEquipped).toBe(
      false,
    );
  });
});

describe('what the derivation changes about the rendered tooltip', () => {
  it('colours owned pieces differently from the rest', () => {
    // The payoff: render derives the state rather than passing an empty input, so every sibling painted red.
    const halo = piece(AngelicHalo, LocationEquipped, 6);
    const player = wearer(halo, piece(AngelicMantle, LocationEquipped, BodyTorso));

    const pieces = Engine.render(halo, player).lines.filter(
      l => l.section === ItemTooltipSection.SetPieceList,
    );

    expect(pieces.length).toBe(4);

    // Two owned, two not — told apart by COLOUR, which is the whole point of ownedSetItemIds.
    // Green is 2 and red is 1 (0x48d8fb / 0x48d902).
    expect(pieces.filter(l => l.color === ItemTooltipColor.Set).length).toBe(2);
    expect(pieces.filter(l => l.color === ItemTooltipColor.Red).length).toBe(2);

    // With no wearer at all every piece is unowned.
    const alone = Engine.render(halo).lines.filter(
      l => l.section === ItemTooltipSection.SetPieceList && l.color === ItemTooltipColor.Red,
    );
    expect(alone.length).toBe(4);
  });

  it('reaches the full-set block from the plain render', () => {
    // It could not before: render passed isEquipped = false, and buildFullSet returns immediately
    // on that, so the block was dead on the convenience path no matter what the wearer carried.
    const halo = piece(AngelicHalo, LocationEquipped, 6);

    const player = wearer(
      halo,
      piece(AngelicMantle, LocationEquipped, BodyTorso),
      piece(AngelicWings, LocationEquipped, 5),
      piece(AngelicSickle, LocationEquipped, BodyRightArm),
    );

    expect(
      Engine.render(halo, player).lines.some(l => l.section === ItemTooltipSection.FullSetBonus),
    ).toBe(true);

    // Same item, same wearer, but the piece itself is in the inventory: the game suppresses the
    // block outright, and so does this.
    const loose = piece(AngelicHalo, LocationInventory);
    expect(
      Engine.render(loose, player).lines.some(l => l.section === ItemTooltipSection.FullSetBonus),
    ).toBe(false);
  });
});

describe('the same derivation behind ranges', () => {
  it('needs two worn pieces to earn a set', () => {
    const halo = piece(AngelicHalo, LocationEquipped, 6);

    expect(Engine.earnedSetIdsOf(wearer(halo))).toEqual([]);

    const setId = Engine.sets.pieceAt(AngelicHalo)?.setId ?? -1;

    expect(
      Engine.earnedSetIdsOf(wearer(halo, piece(AngelicMantle, LocationEquipped, BodyTorso))),
    ).toEqual([setId]);
  });

  it('uses the worn predicate and not the owned one', () => {
    // A second piece carried rather than worn does not earn the tier, and neither does one on
    // weapon swap — the same distinction the masks make, which is why both share one walk.
    const halo = piece(AngelicHalo, LocationEquipped, 6);

    expect(Engine.earnedSetIdsOf(wearer(halo, piece(AngelicWings, LocationInventory)))).toEqual([]);

    expect(
      Engine.earnedSetIdsOf(wearer(halo, piece(AngelicSickle, LocationEquipped, BodyRightArmSwap))),
    ).toEqual([]);
  });

  it('folds in the tiers the viewer has earned', () => {
    const halo = piece(AngelicHalo, LocationEquipped, 6);
    const player = wearer(halo, piece(AngelicMantle, LocationEquipped, BodyTorso));

    const alone = Engine.rangesForViewer(halo, null);
    const earned = Engine.rangesForViewer(halo, player);

    expect(earned.stats.length).toBeGreaterThan(alone.stats.length);

    expect(earned.stats.some(r => (r.sources & RollSources.SetBonus) !== 0)).toBe(true);
    expect(alone.stats.some(r => (r.sources & RollSources.SetBonus) !== 0)).toBe(false);
  });
});
