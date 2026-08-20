import type { ItemIdentity, ItemViewer } from './ItemRecord.js';
import { ItemStatReader } from './ItemStatReader.js';
import type { ItemTable } from '../Tables/ItemTable.js';
import type { ItemTypeTree } from '../Tables/ItemTypeTree.js';
import type { IStatValueSource } from '../Types.js';

const StatMaxDurability = 73;

/**
 * An {@link IStatValueSource} over a stat set that was built rather than captured.
 *
 * SKILLDESC_BuildStatListDesc (0x4e49c0) collects the damage kinds by walking the UNIT'S OWN
 * statlists, which includes the temporary 0x40 list a socket-filler description attaches. So the
 * same synthesised stats have to be visible here, not only in the packed set handed to
 * Describe — the aggregate reads exclusively through this interface, and with no source the
 * paired damage lines silently degrade into one line per stat.
 */
export class SynthesisedStatValues implements IStatValueSource {
  private readonly stats: ReadonlyMap<number, number>;
  private readonly unitStats: ReadonlyMap<number, number>;
  private readonly item: ItemIdentity | null;
  private readonly viewer: ItemViewer | null;
  private readonly items: ItemTable | null;
  private readonly types: ItemTypeTree | null;
  private readonly unitIsItem: boolean;

  /**
   * @param stats Describe scope: the temp list the engine builds at 0x4e612b. The damage
   * aggregate (0x4e49c0) and the 23/24 suppression pair (0x4e62d2) both read it, not the unit.
   * @param unitStats Unit scope: every list on the item, which is what GetTxtMaxDurability
   * 0x625e00 and the never-breaks gate query. Defaults to `stats`.
   * @param unitIsItem False only for the full-set bonus block, whose described unit is the PLAYER
   * (SKILLDESC_AppendItemBuffTextAlt passes a1 at 0x4e670c). The never-breaks tail tests
   * `*v8 == 4` at 0x4e6375, so a player-scoped block cannot reach it.
   */
  constructor(
    stats: ReadonlyMap<number, number> | null | undefined,
    item: ItemIdentity | null,
    viewer: ItemViewer | null,
    items: ItemTable | null,
    types: ItemTypeTree | null,
    unitStats: ReadonlyMap<number, number> | null = null,
    unitIsItem = true,
  ) {
    this.stats = stats ?? new Map<number, number>();
    this.unitStats = unitStats ?? this.stats;
    this.item = item;
    this.viewer = viewer;
    this.items = items;
    this.types = types;
    this.unitIsItem = unitIsItem;
  }

  getBaseStatValue(statId: number, layer: number): number {
    return this.stats.get(ItemStatReader.packStatKey(layer, statId)) ?? 0;
  }

  getItemStatValue(statId: number): number {
    return this.unitStats.get(ItemStatReader.packStatKey(0, statId)) ?? 0;
  }

  /**
   * The VIEWER's stat, not the item's. SKILLDESC_CalcStatGroupValue 0x4e4c50 scales an
   * op 2-5 stat by `GetStatUnsignedValue(GetPlayerUnit(), opBase, 0)` (0x4e4c93/0x4e4c99),
   * and GetPlayerUnit 0x463dd0 returns the local client player — categorically not the item
   * being described. Returning 0 here made every "(Based on Character Level)" modifier
   * render its number as 0.
   *
   * No viewer stays 0: GetStatUnsignedValue 0x625483 returns 0 for a null unit rather than
   * halting, so the line is still emitted with a zero value.
   */
  getPlayerStatValue(statId: number): number {
    return this.viewer === null ? 0 : this.viewer.stat(statId);
  }

  get playerClass(): number {
    return this.viewer === null ? -1 : this.viewer.classId;
  }

  isItemOfType(itemTypeId: number): boolean {
    if (this.types === null || this.items === null || this.item === null) {
      return false;
    }

    return this.types.isOfType(
      this.types.row(this.items.primaryTypeCode(this.item.classId)),
      this.types.row(this.items.secondaryTypeCode(this.item.classId)),
      itemTypeId,
    );
  }

  get describedUnitIsItem(): boolean {
    return this.unitIsItem;
  }

  get itemTableAllowsDurability(): boolean {
    if (this.items === null || this.item === null) {
      return false;
    }

    return (
      this.items.getInt(this.item.classId, 'nodurability') === 0 &&
      this.items.getInt(this.item.classId, 'durability') !== 0
    );
  }

  /**
   * Despite the name, GetTxtMaxDurability 0x625e00 reads the item's STAT 73 (record 73 of
   * the 324-byte ItemStatCost array, 0x5C64/0x144 at 0x625e21), not an items.txt column.
   * The never-breaks gate depends on the difference: it wants a table durability with no
   * stat behind it.
   */
  getTxtMaxDurability(): number {
    return this.getItemStatValue(StatMaxDurability);
  }
}
