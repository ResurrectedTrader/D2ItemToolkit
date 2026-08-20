import { ItemIdentity, ItemUnit, type ItemViewer } from '../Stats/ItemRecord.js';
import { ItemStatReader } from '../Stats/ItemStatReader.js';
import type { ItemTable } from '../Tables/ItemTable.js';
import { MagicAffixTable } from '../Tables/MagicAffixTable.js';
import type { D2DataFiles, TxtSkillTable } from '../Tables/TxtDataProviders.js';
import type { TxtFile } from '../Data/TxtFile.js';
import { ItemQualityNo } from './ItemNameBuilder.js';
import { Int32 } from '../Types.js';

/**
 * ITEM_CalcRequiredLevel 0x62b5b0. Everything it reads is either in the record or in the excel
 * tables, so the level is derived here rather than supplied by the producer.
 */
export class RequiredLevelCalculator {
  private static readonly StatLevelRequirement = 92; // item_levelreq
  private static readonly StatSingleSkill = 107; // item_singleskill
  private static readonly StatNonClassSkill = 97; // item_nonclassskill

  private static readonly CraftedBase = 10;
  private static readonly CraftedPerAffix = 3;
  private static readonly OffClassSkillPenalty = 6;
  private static readonly LastPlayerClass = 6;

  private readonly _data: D2DataFiles;
  private readonly _items: ItemTable;
  private readonly _affixes: MagicAffixTable;

  constructor(data: D2DataFiles, items: ItemTable) {
    this._data = data;
    this._items = items;
    this._affixes = new MagicAffixTable(data);
  }

  /**
   * `socketUnits` carries the fillers as whole units, which is what the recursion at
   * 0x62b901 needs. `sockets` is the classId-only view kept for callers that have nothing
   * richer: it yields the same answer for gems and runes, whose items.txt levelreq is their
   * only contribution, but misses a magic or rare JEWEL's affix requirement.
   */
  calculate(
    item: ItemIdentity,
    viewer: ItemViewer | null,
    stats: Map<number, number> | null,
    socketUnits: ItemUnit[] | null,
    sockets: Map<number, number> | null = null,
  ): number {
    let result = this.fromQuality(item, viewer);

    // items.txt levelreq raises the floor for every quality (0x62b8d0).
    const baseRequirement = this._items.requiredLevel(item.classId);
    if (baseRequirement > result) {
      result = baseRequirement;
    }

    // 0x62b901 recurses the WHOLE calculation into every socketed item, so a filler's own
    // quality affixes and its stats 107/97/92 all reach the host.
    for (const filler of RequiredLevelCalculator.fillers(socketUnits, sockets)) {
      const required = this.calculate(filler.identity, viewer, filler.stats, filler.sockets);
      if (required > result) {
        result = required;
      }
    }

    result = this.raiseForSkills(
      result,
      stats,
      RequiredLevelCalculator.StatSingleSkill,
      viewer,
      false,
    );
    result = this.raiseForSkills(
      result,
      stats,
      RequiredLevelCalculator.StatNonClassSkill,
      viewer,
      true,
    );

    result = Int32.of(
      result + RequiredLevelCalculator.stat(stats, RequiredLevelCalculator.StatLevelRequirement),
    );

    return result <= 0 ? 0 : result;
  }

  private static fillers(
    socketUnits: ItemUnit[] | null,
    sockets: Map<number, number> | null,
  ): ItemUnit[] {
    if (socketUnits !== null) {
      return socketUnits;
    }

    const degraded: ItemUnit[] = [];
    if (sockets !== null) {
      for (const [, value] of sockets) {
        const identity = new ItemIdentity();
        identity.classId = Int32.of(value);
        degraded.push(new ItemUnit(identity));
      }
    }

    return degraded;
  }

  private fromQuality(item: ItemIdentity, viewer: ItemViewer | null): number {
    switch (item.quality) {
      case ItemQualityNo.Magic:
        return this.magic(item, viewer);

      case ItemQualityNo.Set:
        return RequiredLevelCalculator.tableRequirement(this._data.setItems, item.fileIndex);

      case ItemQualityNo.Rare:
        return this.rare(item, viewer);

      case ItemQualityNo.Unique:
        return this.unique(item, viewer);

      case ItemQualityNo.Craft:
        return this.crafted(item, viewer);

      default:
        return 0;
    }
  }

  // 0x62b5f2. Only affix slot 0 is consulted, plus the automagic affix, and eax is zeroed at
  // 0x62b630 so the three folds start from 0.
  private magic(item: ItemIdentity, viewer: ItemViewer | null): number {
    let result = this._affixes.raiseLevelRequirement(0, item.magicPrefix[0] ?? 0, viewer);
    result = this._affixes.raiseLevelRequirement(result, item.magicSuffix[0] ?? 0, viewer);
    return this._affixes.raiseLevelRequirement(result, item.autoAffix, viewer);
  }

  // 0x62b651. All three prefix and suffix slots, then the automagic affix.
  private rare(item: ItemIdentity, viewer: ItemViewer | null): number {
    let result = 0;

    for (let slot = 0; slot < ItemIdentity.MaxAffixSlots; ++slot) {
      result = this._affixes.raiseLevelRequirement(result, item.magicPrefix[slot] ?? 0, viewer);
      result = this._affixes.raiseLevelRequirement(result, item.magicSuffix[slot] ?? 0, viewer);
    }

    return this._affixes.raiseLevelRequirement(result, item.autoAffix, viewer);
  }

  // 0x62b76b. The affix maximum plus 10, plus 3 for every affix row that resolves, capped one
  // below the class-0 maximum level from experience.txt (0x62b848 reads class 0 unconditionally).
  private crafted(item: ItemIdentity, viewer: ItemViewer | null): number {
    let result = 0;
    let bonus = RequiredLevelCalculator.CraftedBase;

    for (let slot = 0; slot < ItemIdentity.MaxAffixSlots; ++slot) {
      result = this._affixes.raiseLevelRequirement(result, item.magicPrefix[slot] ?? 0, viewer);
      result = this._affixes.raiseLevelRequirement(result, item.magicSuffix[slot] ?? 0, viewer);

      if (this.resolves(item.magicPrefix[slot] ?? 0)) {
        bonus += RequiredLevelCalculator.CraftedPerAffix;
      }

      if (this.resolves(item.magicSuffix[slot] ?? 0)) {
        bonus += RequiredLevelCalculator.CraftedPerAffix;
      }
    }

    result = Int32.of(result + bonus);

    const cap = Int32.of(this.maxCharacterLevel() - 1);
    return result > cap ? cap : result;
  }

  // 0x62b859. A classic-format unique shows no level requirement to a viewer without the
  // expansion flag (0x2000000 tested at 0x62b877).
  private unique(item: ItemIdentity, viewer: ItemViewer | null): number {
    if (item.fileIndex < 0) {
      return 0;
    }

    if (viewer !== null && !viewer.isExpansion && item.format === 0) {
      return 0;
    }

    return RequiredLevelCalculator.tableRequirement(this._data.uniqueItems, item.fileIndex);
  }

  private static tableRequirement(table: TxtFile | null, fileIndex: number): number {
    if (
      table === null ||
      fileIndex < 0 ||
      fileIndex >= table.rowCount ||
      !table.hasColumn('lvl req')
    ) {
      return 0;
    }

    // Read as a signed 16-bit field and discarded when negative (0x62b8b3).
    const required = (table.getInt(fileIndex, 'lvl req') << 16) >> 16;
    return required >= 0 ? required : 0;
  }

  // 0x62b927 / 0x62b984. The stat LAYER is the skill id. A granted skill from another class
  // costs six extra levels unless the viewer is a player of that very class.
  private raiseForSkills(
    running: number,
    stats: Map<number, number> | null,
    statId: number,
    viewer: ItemViewer | null,
    offClass: boolean,
  ): number {
    const skills: TxtSkillTable | null = this._data.skills;
    if (stats === null || skills === null) {
      return running;
    }

    for (const [key] of stats) {
      const unpacked = ItemStatReader.unpackStatKey(key);
      if (unpacked.stat !== statId) {
        continue;
      }

      let required = skills.requiredLevel(unpacked.layer);
      if (required < 0) {
        continue;
      }

      if (offClass) {
        const skillClass = skills.getSkillClass(unpacked.layer);
        const ownClass =
          viewer !== null &&
          viewer.isPlayer &&
          skillClass >= 0 &&
          skillClass <= RequiredLevelCalculator.LastPlayerClass &&
          viewer.classId === skillClass;

        if (!ownClass) {
          required = Int32.of(required + RequiredLevelCalculator.OffClassSkillPenalty);
        }
      }

      if (required > running) {
        running = required;
      }
    }

    return running;
  }

  private resolves(affixId: number): boolean {
    return this._affixes.tryResolve(affixId) !== null;
  }

  // experience.txt row 0 is the MaxLvl row; column 0 is the Amazon.
  private maxCharacterLevel(): number {
    const experience: TxtFile | null = this._data.experience;
    if (experience === null || experience.rowCount === 0 || !experience.hasColumn('Amazon')) {
      return RequiredLevelCalculator.DefaultMaxLevel;
    }

    const max = experience.getInt(0, 'Amazon');
    return max > 0 ? max : RequiredLevelCalculator.DefaultMaxLevel;
  }

  private static readonly DefaultMaxLevel = 99;

  private static stat(stats: Map<number, number> | null, statId: number): number {
    if (stats === null) {
      return 0;
    }

    return stats.get(ItemStatReader.packStatKey(0, statId)) ?? 0;
  }
}
