import {
  ItemRecordFlags,
  type ItemIdentity,
  type ItemUnit,
  type ItemViewer,
} from '../Stats/ItemRecord.js';
import { ItemStatReader } from '../Stats/ItemStatReader.js';
import type { ItemTable } from '../Tables/ItemTable.js';
import { RequiredLevelCalculator } from './RequiredLevelCalculator.js';
import type { D2DataFiles, TxtSkillTable } from '../Tables/TxtDataProviders.js';
import type { TxtFile } from '../Data/TxtFile.js';

/**
 * ITEM_CheckEquipRequirements 0x62eaf0 — the three met flags LoadItemDesc colours its
 * requirement lines with, plus the class comparison it does inline at 0x48e4a6.
 *
 * LoadItemDesc passes bCheckSockets = 0 (0x48e534), so the ITEMS_SumSocketedItemStats branch is
 * unreachable from a tooltip and is not modelled here.
 */
export class EquipRequirements {
  static readonly NoClassRestriction = 7;

  private static readonly StatStrength = 0;
  private static readonly StatDexterity = 2;
  private static readonly StatRequirementPercent = 91;
  private static readonly EtherealDiscount = 10;

  private readonly _items: ItemTable;
  private readonly _itemTypes: TxtFile | null;
  private readonly _skills: TxtSkillTable | null;
  private readonly _level: RequiredLevelCalculator;

  constructor(data: D2DataFiles, items: ItemTable) {
    this._items = items;
    this._itemTypes = data.itemTypes;
    this._skills = data.skills;
    this._level = new RequiredLevelCalculator(data, items);
  }

  /**
   * The displayed requirement: base + D2ApplyPercent(base, stat 91, 100), less 10 when
   * ethereal. The identical expression drives the number at 0x48e65f and the comparison at
   * 0x62eb8c, so a line can never show a value the check disagrees with.
   */
  requirement(item: ItemIdentity, column: string, stats: Map<number, number> | null): number {
    const required = this._items.getInt(item.classId, column);
    if (required <= 0) {
      return 0;
    }

    // Both sites skip D2ApplyPercent entirely when the percent is zero (0x48e651).
    //
    // The outer add is int32 and WRAPS. That matters beyond the number: the caller writes nothing
    // at all when the total is <= 0 (0x4850fb), so an overflow that lands negative drops the whole
    // Required Strength line rather than printing a large one. A JS double would print it.
    const percent = EquipRequirements.stat(stats, EquipRequirements.StatRequirementPercent);
    let total =
      percent !== 0 ? (required + EquipRequirements.applyPercent(required, percent)) | 0 : required;

    if (item.has(ItemRecordFlags.Ethereal)) {
      total = (total - EquipRequirements.EtherealDiscount) | 0;
    }

    return total;
  }

  /**
   * 0x62ebcf. A viewer with no strength at all fails, and otherwise the check is a plain
   * greater-or-equal against the same total the line displays.
   */
  metStrength(
    item: ItemIdentity,
    viewer: ItemViewer | null,
    stats: Map<number, number> | null,
  ): boolean {
    return EquipRequirements.metAttribute(
      this.requirement(item, 'reqstr', stats),
      EquipRequirements.attribute(viewer, EquipRequirements.StatStrength),
    );
  }

  metDexterity(
    item: ItemIdentity,
    viewer: ItemViewer | null,
    stats: Map<number, number> | null,
  ): boolean {
    return EquipRequirements.metAttribute(
      this.requirement(item, 'reqdex', stats),
      EquipRequirements.attribute(viewer, EquipRequirements.StatDexterity),
    );
  }

  private static metAttribute(required: number, available: number): boolean {
    return available > 0 && available >= required;
  }

  /**
   * 0x62ec88. Level uses the player's own level rather than an attribute stat.
   */
  metLevel(
    item: ItemIdentity,
    viewer: ItemViewer | null,
    stats: Map<number, number> | null,
    socketUnits: ItemUnit[] | null,
    sockets: Map<number, number> | null,
  ): boolean {
    const required = this._level.calculate(item, viewer, stats, socketUnits, sockets);
    return (viewer === null ? 0 : viewer.level) >= required;
  }

  /**
   * 0x48e4a6 compares the player unit's class id straight against the restriction with no
   * unit-type test, so a non-player viewer whose class id happens to match reads as met.
   */
  metClass(item: ItemIdentity, viewer: ItemViewer | null): boolean {
    const restriction = this.classRestriction(item);
    if (restriction === EquipRequirements.NoClassRestriction) {
      return true;
    }

    return (viewer === null ? -1 : viewer.classId) === restriction;
  }

  /**
   * TXT_ItemTypes_GetClass 0x62c0b0: the PRIMARY type row's Class column as a byte, with 7
   * meaning unrestricted. Anything at or above 7 collapses to 7 (0x62c0ef).
   */
  classRestriction(item: ItemIdentity): number {
    const itemTypes = this._itemTypes;
    const skills = this._skills;
    if (itemTypes === null || skills === null) {
      return EquipRequirements.NoClassRestriction;
    }

    const row = this.rowFor(this._items.primaryTypeCode(item.classId));
    if (row < 0 || !itemTypes.hasColumn('Class')) {
      return EquipRequirements.NoClassRestriction;
    }

    const code = itemTypes.getString(row, 'Class');
    if (code.trim().length === 0) {
      return EquipRequirements.NoClassRestriction;
    }

    const classId = skills.classIdForCode(code);
    return classId >= 0 && classId < EquipRequirements.NoClassRestriction
      ? classId
      : EquipRequirements.NoClassRestriction;
  }

  private rowFor(code: string): number {
    const itemTypes = this._itemTypes;
    if (itemTypes === null || code.length === 0 || !itemTypes.hasColumn('Code')) {
      return -1;
    }

    for (let row = 0; row < itemTypes.rowCount; ++row) {
      // OrdinalIgnoreCase, matching the C# comparison.
      if (itemTypes.getString(row, 'Code').trim().toLowerCase() === code.trim().toLowerCase()) {
        return row;
      }
    }

    return -1;
  }

  private static attribute(viewer: ItemViewer | null, statId: number): number {
    if (viewer === null) {
      return 0;
    }

    return statId === EquipRequirements.StatStrength ? viewer.strength : viewer.dexterity;
  }

  private static stat(stats: Map<number, number> | null, statId: number): number {
    if (stats === null) {
      return 0;
    }

    return stats.get(ItemStatReader.packStatKey(0, statId)) ?? 0;
  }

  private static applyPercent(value: number, percent: number): number {
    return Number(BigInt.asIntN(32, (BigInt(value) * BigInt(percent)) / 100n));
  }
}
