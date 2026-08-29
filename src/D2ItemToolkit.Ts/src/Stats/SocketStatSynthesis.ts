import type { D2DataFiles } from '../Tables/TxtDataProviders.js';
import type { ItemTable } from '../Tables/ItemTable.js';
import type { ItemTypeTree } from '../Tables/ItemTypeTree.js';
import { GemTable } from '../Tables/GemTable.js';
import { PropertyApplier, type ItemProperty } from './PropertyApplier.js';
import { ItemStatReader, ItemStatView } from './ItemStatReader.js';
import { ItemRecordReader } from './ItemRecord.js';
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
   * The union over every filler that carries no captured stats of its own. Fillers that DO carry
   * them are left alone: a server-side producer records the mods the engine already assigned, and
   * synthesising on top would count them twice.
   */
  contributions(host: Unit | null): Map<number, number> {
    const merged = new Map<number, number>();

    if (host === null || host === undefined) {
      return merged;
    }

    const slot = this.items.getInt(host.classId, 'gemapplytype');

    // 0x65c6f0 halts above two. Shipped data never does, but a caller's own tables might.
    if (slot < 0 || slot > 2) {
      return merged;
    }

    for (const filler of host.items) {
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
   * items.txt `gemapplytype` for this host — which of the three gems.txt mod columns applies
   * (0x65c6f0 halts above two). -1 when the host cannot take fillers at all.
   */
  slotFor(host: Unit | null): number {
    if (host === null) {
      return -1;
    }

    const slot = this.items.getInt(host.classId, 'gemapplytype');
    return slot >= 0 && slot <= 2 ? slot : -1;
  }

  /**
   * ONE filler's properties, so a caller can range or describe each socket separately rather than as
   * the union `contributions` returns.
   */
  fillerPropertiesOf(filler: Unit | null, slot: number): ItemProperty[] {
    const found: ItemProperty[] = [];

    if (slot < 0 || slot > 2) {
      return found;
    }

    const row = this.fillerRow(filler);
    if (row < 0) {
      return found;
    }

    for (const property of this.gems.properties(row, slot)) {
      if (property.propertyId < 0) {
        break;
      }

      found.push(property);
    }

    return found;
  }

  /**
   * The gem/rune properties every filler would apply, before any of them is rolled — the same
   * selection {@link contributions} applies, exposed so a range reconstruction can run them at both
   * ends instead of once.
   *
   * **No gems.txt cell actually rolls.** The three whose min differs from their max are `dmg-fire`,
   * `dmg-ltng` and `dmg-cold` on the Ral, Ort and Thul runes, and those are funcs 15 and 16 — the
   * two ENDS of a damage range, both fixed, read as separate parameters exactly as funcs 11 and 19
   * read theirs. So a gem or rune contributes no span at all; a socketed JEWEL does, but from its
   * own affixes rather than from here.
   */
  fillerProperties(host: Unit | null): ItemProperty[] {
    const found: ItemProperty[] = [];

    if (host === null) {
      return found;
    }

    const slot = this.items.getInt(host.classId, 'gemapplytype');
    if (slot < 0 || slot > 2) {
      return found;
    }

    for (const filler of host.items) {
      const row = this.fillerRow(filler);
      if (row < 0) {
        continue;
      }

      for (const property of this.gems.properties(row, slot)) {
        if (property.propertyId < 0) {
          break;
        }

        found.push(property);
      }
    }

    return found;
  }

  /**
   * The gems.txt row a filler applies from, or -1 when it carries its own stats, is not a gem or
   * rune, or has no row. The same three gates {@link contribution} applies.
   */
  private fillerRow(filler: Unit | null): number {
    if (
      filler === null ||
      ItemStatReader.reconstructView(filler, ItemStatView.modifiers()).size !== 0
    ) {
      return -1;
    }

    const identity = ItemRecordReader.readIdentity(filler);

    const primary = this.types.row(this.items.primaryTypeCode(identity.classId));
    const secondary = this.types.row(this.items.secondaryTypeCode(identity.classId));

    const gem = this.gemTypeRow >= 0 && this.types.isOfType(primary, secondary, this.gemTypeRow);
    const rune =
      !gem && this.runeTypeRow >= 0 && this.types.isOfType(primary, secondary, this.runeTypeRow);

    return gem || rune ? this.gems.rowForFillerClassId(identity.classId) : -1;
  }

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
