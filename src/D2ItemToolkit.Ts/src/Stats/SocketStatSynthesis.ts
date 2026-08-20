import type { D2DataFiles } from '../Tables/TxtDataProviders.js';
import type { ItemTable } from '../Tables/ItemTable.js';
import type { ItemTypeTree } from '../Tables/ItemTypeTree.js';
import { GemTable } from '../Tables/GemTable.js';
import { PropertyApplier } from './PropertyApplier.js';
import { ItemStatReader, ItemStatView } from './ItemStatReader.js';
import { ItemRecordFlags, ItemRecordReader } from './ItemRecord.js';
import { ItemQualityNo } from '../Tooltip/ItemNameBuilder.js';
import type { Unit } from './Unit.js';
import { Int32 } from '../Types.js';

/**
 * What a socketed gem or rune gives its host, rebuilt from gems.txt.
 *
 * ITEM_ApplySocketableAndEquipStats 0x4c0cf0 is the whole rule. For a filler of type 20 (`gem`) it
 * calls ApplyRuneAndGemStats(2, NULL, filler, gemsRow, applyType, 0) at 0x4c0d99; for type 74
 * (`rune`), ApplyRuneAndGemStats(5, host, filler, gemsRow, applyType, 0) at 0x4c0df9. Anything
 * else — a jewel — falls through to ITEM_ProcessSetItemEquip and gets no gems.txt properties at
 * all (0x4c0e06).
 *
 * `applyType` is the HOST's items.txt `gemapplytype` (ITEM_GetItemsTxt_bGemApplyType 0x629a40 →
 * TXT_Items_GetGemApplyType 0x629a00). It selects which of the three property arrays on the gems
 * record is read: ITEMMOD_GetMaxLevelAtIndex 0x65c6d0 builds {+0x30, +0x60, +0x90} and indexes it,
 * i.e. 0 weapon, 1 helm, 2 shield — the slot argument `GemTable.properties` already takes. Three or
 * above halts the game (0x65c6f0).
 *
 * The walk takes at most three properties and STOPS at the first with no property rather than
 * skipping it (0x66004f), and the item threaded into the property funcs is the FILLER, not the
 * host — ApplyRuneAndGemStats loads `esi` from its pItem argument at 0x660057. That is the same
 * call shape RecordSections already uses for a loose filler's own description; the only difference
 * here is that one slot is chosen instead of all four blocks being walked.
 *
 * WHY THIS EXISTS. Every caller of the assignment lives in D2Common/D2Game. A client-side capture
 * reads D2Client's unit tables, and the client is handed the HOST's already-computed stats in the
 * item packet — it never instantiates the filler's mods. So a gem or rune arrives with an empty
 * stat chain and its contribution has to be synthesised. A jewel does not: it is a magic item with
 * rolled affixes of its own, which the capture carries, and gems.txt has no row for it either way.
 */
export class SocketStatSynthesis {
  private readonly items: ItemTable;
  private readonly types: ItemTypeTree;
  private readonly gems: GemTable;
  private readonly applier: PropertyApplier;

  private readonly gemTypeRow: number;
  private readonly runeTypeRow: number;

  constructor(data: D2DataFiles, items: ItemTable, types: ItemTypeTree) {
    this.items = items;
    this.types = types;
    this.gems = new GemTable(data.gems, items);
    this.applier = new PropertyApplier(data, items, types);
    this.gems.resolvePropertyCodesWith(code => this.applier.properties.rowForCode(code));

    this.gemTypeRow = types.row('gem');
    this.runeTypeRow = types.row('rune');
  }

  /**
   * True when the host is an EQUIPPED SET item, whose fillers the game has thrown away.
   *
   * ITEM_RecalcAllEquippedItems 0x4c1350 ends with a loop over the eleven body slots that fires only
   * for `GetItemQuality == 5` and neither flag 0x100 nor 0x4000 (0x4c15ec-0x4c162b). For each such
   * item it calls STATLIST_RemoveFromOwnerAndRecalc (0x4c1658), which detaches the item's whole stat
   * list (0x6277fa takes item+0x5C and hands it to STATLIST_DetachAndRecalc), and then rebuilds with
   * ITEM_ApplySocketableAndEquipStats(wearer, THE SET ITEM, 0) at 0x4c1661 — where a2 is the set
   * item rather than a filler, so `IsOfType(a2, 20)` and `IsOfType(a2, 74)` both fail (0x4c0d30 /
   * 0x4c0da3) and it lands on ITEM_ProcessSetItemEquip (0x4c0e06), which only touches set states and
   * the set-bonus list.
   *
   * Nothing re-applies the fillers. The mods are gone until the item is socketed again, and a recalc
   * runs on every equip, unequip and stat change — so for a worn set item this is the steady state,
   * not a race.
   *
   * A real capture is what exposed it: Tal Rasha's Horadric Crest with an Um in it draws
   * `All Resistances +15`, its own set property alone, while a runeword shield in the same snapshot
   * draws all three of its runes' mods. Quality 2 skips the loop.
   */
  static fillersAreDiscardedByRecalc(host: Unit | null, equipped: boolean): boolean {
    // 0x4000 has no name in ItemRecordFlags because nothing else reads it; 0x4c1618 and 0x4c1628
    // test it beside the broken flag as the loop's two exclusions.
    const CannotEquip = 0x4000;

    return (
      equipped &&
      host !== null &&
      host.quality === ItemQualityNo.Set &&
      (host.itemFlags & (ItemRecordFlags.Broken | CannotEquip)) === 0
    );
  }

  /**
   * The union over every filler that carries no captured stats of its own. Fillers that DO carry
   * them are left alone: a server-side producer records the mods the engine already assigned, and
   * synthesising on top would count them twice.
   */
  contributions(host: Unit | null, hostIsEquipped = false): Map<number, number> {
    const merged = new Map<number, number>();

    if (
      host === null ||
      host === undefined ||
      SocketStatSynthesis.fillersAreDiscardedByRecalc(host, hostIsEquipped)
    ) {
      return merged;
    }

    const slot = this.items.getInt(host.classId, 'gemapplytype');

    // 0x65c6f0 halts above two. Shipped data never does, but a caller's own tables might.
    if (slot < 0 || slot > 2) {
      return merged;
    }

    for (const filler of ItemStatReader.enumerateSockets(host)) {
      // One level only: a jewel cannot itself hold sockets, so vanilla never nests further, and
      // the game applies the filler to its immediate host.
      for (const [key, value] of this.contribution(filler, slot)) {
        const existing = merged.get(key);
        merged.set(key, existing === undefined ? value : Int32.of(existing + value));
      }
    }

    return merged;
  }

  /**
   * One filler's contribution to a host with this gemapplytype, or empty when the filler already
   * carries stats, is not a gem or rune, or has no gems.txt row.
   */
  contribution(filler: Unit | null, slot: number): Map<number, number> {
    const stats = new Map<number, number>();

    if (filler === null || filler === undefined || slot < 0 || slot > 2) {
      return stats;
    }

    if (ItemStatReader.reconstructView(filler, ItemStatView.modifiers()).size !== 0) {
      return stats;
    }

    const identity = ItemRecordReader.readIdentity(filler);

    const primary = this.types.row(this.items.primaryTypeCode(identity.classId));
    const secondary = this.types.row(this.items.secondaryTypeCode(identity.classId));

    const gem = this.gemTypeRow >= 0 && this.types.isOfType(primary, secondary, this.gemTypeRow);
    const rune =
      !gem && this.runeTypeRow >= 0 && this.types.isOfType(primary, secondary, this.runeTypeRow);

    if (!gem && !rune) {
      return stats;
    }

    const row = this.gems.rowForFillerClassId(identity.classId);
    if (row < 0) {
      return stats;
    }

    const propMode = gem ? PropertyApplier.PropModeGem : PropertyApplier.PropModeRune;

    for (const property of this.gems.properties(row, slot)) {
      if (property.propertyId < 0) {
        break;
      }

      this.applier.apply(propMode, identity, property, stats);
    }

    return stats;
  }
}
