import type { TxtFile } from '../Data/TxtFile.js';
import type { ItemIdentity, ItemViewer } from '../Stats/ItemRecord.js';
import { ItemStatOps } from '../Stats/ItemStatOps.js';
import { ItemStatReader } from '../Stats/ItemStatReader.js';
import type { ItemTable } from '../Tables/ItemTable.js';
import { ItemTypeTree } from '../Tables/ItemTypeTree.js';
import type { D2DataFiles } from '../Tables/TxtDataProviders.js';

const Int32MinValue = -2147483648;

/**
 * D2R's mastery terms on requirements: SKILLS_GetWeaponMasteryBonus 0x14024d610 with nType 6
 * (stat 203, the str/dex percent) and the stat-209 tail of ITEMS_GetRequiredLevel
 * (0x140228913-0x140228aa0). Every caller passes no skill, so the skill is the viewer's last-used
 * one.
 */
export class WeaponMastery {
  static readonly StatItemRequirementPercent = 203;
  static readonly StatItemLevelRequirementPercent = 209;

  private static readonly BrokenOrUnequippable = 0x4100;
  private static readonly RangedSkill = 2; // `rng` in the range linker filled at 0x1402009d3

  private readonly _items: ItemTable;
  private readonly _types: ItemTypeTree | null;
  private readonly _skills: TxtFile | null;

  constructor(data: D2DataFiles, items: ItemTable) {
    this._items = items;
    this._types = data.itemTypes === null ? null : new ItemTypeTree(data.itemTypes);
    this._skills = data.skillRows;
  }

  /** The stat-203 percent for this item and viewer; 0 when none applies. */
  requirementPercent(item: ItemIdentity, viewer: ItemViewer | null): number {
    const types = this._types;
    if (viewer === null || types === null) {
      return 0;
    }

    const primary = this.primary(types, item);
    const secondary = this.secondary(types, item);

    // SKILLS_CalculateThrowingMasteryValues 0x14024d380: once engaged it returns even a 0, and it
    // matches the RAW 16-bit layer — no condition bits stripped, no 0-is-any — so Levitate's
    // `weap | 1 << 14` (16429) never matches an item type.
    if (this.throwingArmEngages(types, primary, viewer)) {
      let thrown = Int32MinValue;
      for (const [key, value] of viewer.stats) {
        if (
          ItemStatReader.statFromKey(key) === WeaponMastery.StatItemRequirementPercent &&
          types.isOfType(primary, secondary, ItemStatReader.layerFromKey(key) & 0xffff) &&
          value > thrown
        ) {
          thrown = value;
        }
      }

      return thrown === Int32MinValue ? 0 : thrown;
    }

    return this.best(types, WeaponMastery.StatItemRequirementPercent, primary, secondary, viewer);
  }

  /**
   * The stat-209 term: the computed level plus a percent of it, with no clamp afterwards. No
   * shipped row grants the stat. Its throwing arm never engages — the switch has no case 7
   * (0x14024d4a4).
   */
  applyLevelPercent(item: ItemIdentity, viewer: ItemViewer | null, level: number): number {
    const types = this._types;
    if (viewer === null || types === null) {
      return level;
    }

    const percent = this.best(
      types,
      WeaponMastery.StatItemLevelRequirementPercent,
      this.primary(types, item),
      this.secondary(types, item),
      viewer,
    );
    return percent === 0
      ? level
      : (level + ItemStatOps.applyPercentOverflowSafe(level, percent)) | 0;
  }

  // The generic arm (0x14024d6c4-0x14024d7a2): the best entry whose layer applies — low 14 bits
  // an item type the item is (0 = any), bits 14-15 a dual-melee condition (2 = only while
  // dual-wielding melee, 1 = only while not). INT_MIN start, so none is 0.
  private best(
    types: ItemTypeTree,
    statId: number,
    primary: number,
    secondary: number,
    viewer: ItemViewer,
  ): number {
    const dualMelee = this.dualMeleeEquipped(viewer);
    let best = Int32MinValue;

    for (const [key, value] of viewer.stats) {
      if (ItemStatReader.statFromKey(key) !== statId) {
        continue;
      }

      const layer = ItemStatReader.layerFromKey(key) & 0xffff;
      const condition = layer >> 14;
      if ((condition === 2 && !dualMelee) || (condition === 1 && dualMelee)) {
        continue;
      }

      const type = layer & 0x3fff;
      if ((type === 0 || types.isOfType(primary, secondary, type)) && value > best) {
        best = value;
      }
    }

    return best === Int32MinValue ? 0 : best;
  }

  /**
   * The throwing arm's gate: a throwable PRIMARY type (ITEMS_CheckIfThrowable 0x140228c10), and a
   * last-used skill whose itypea1 is-a `thro` (0x14024d49b) with range `rng`.
   */
  private throwingArmEngages(types: ItemTypeTree, primary: number, viewer: ItemViewer): boolean {
    const skills = this._skills;
    const skill = viewer.lastUsedSkill;
    if (skill < 0 || skills === null || skill >= skills.rowCount) {
      return false;
    }

    if (!types.isThrowable(primary)) {
      return false;
    }

    const itype = types.row(skills.getString(skill, 'itypea1').trim());
    if (itype <= 0 || !types.isUnder(itype, types.row('thro'))) {
      return false;
    }

    return (
      WeaponMastery.rangeOf(skills.getString(skill, 'range').trim()) === WeaponMastery.RangedSkill
    );
  }

  // The range linker, in its fill order at 0x1402009d3: none, h2h, rng, both, loc.
  private static rangeOf(code: string): number {
    switch (code.toLowerCase()) {
      case 'h2h':
        return 1;
      case 'rng':
        return 2;
      case 'both':
        return 3;
      case 'loc':
        return 4;
      default:
        return 0;
    }
  }

  /**
   * UNITS_Has_Two_Melee_Equipped 0x140239c30 over the viewer's worn body slots 0..10. A one-handed
   * weapon class (1hs, 1ht, ht1) is matched by being, or not being, the LEFT-hand weapon; anything
   * else by items `component` 5 or 6. Both matches must be `mele` and neither broken nor flagged
   * 0x4000.
   *
   * The left-hand weapon is inv->dwLeftItemGUID (0x14023fbb0), which no record carries: it is taken
   * as body slot 5 when that is a weapon, else 4 — what the GUID maintenance gives for every state
   * but two weapons equipped while broken and repaired in place.
   */
  dualMeleeEquipped(viewer: ItemViewer | null): boolean {
    const types = this._types;
    if (viewer === null || types === null) {
      return false;
    }

    const body = viewer.body;
    const leftHand = body[5] ?? null;
    const rightHand = body[4] ?? null;
    const left = this.isA(types, leftHand, 'weap')
      ? leftHand
      : this.isA(types, rightHand, 'weap')
        ? rightHand
        : null;

    let other: ItemIdentity | null = null; // INVENTORY_GetCompositItem(inv, 6) 0x14023fd90
    let hand: ItemIdentity | null = null; // the inline component-5 walk
    for (let slot = 0; slot <= 10; ++slot) {
      const item = body[slot] ?? null;
      if (item === null) {
        continue;
      }

      const weaponClass = this._items.getString(item.classId, 'wclass').trim().toLowerCase();
      const oneHanded = weaponClass === '1hs' || weaponClass === '1ht' || weaponClass === 'ht1';
      const component = this._items.getInt(item.classId, 'component');

      if (other === null && (oneHanded ? item !== left : component === 6)) {
        other = item;
      }

      if (hand === null && (oneHanded ? item === left : component === 5)) {
        hand = item;
      }
    }

    return this.usable(types, other) && this.usable(types, hand);
  }

  private usable(types: ItemTypeTree, item: ItemIdentity | null): boolean {
    return (
      item !== null &&
      this.isA(types, item, 'mele') &&
      (item.flags & WeaponMastery.BrokenOrUnequippable) === 0
    );
  }

  private isA(types: ItemTypeTree, item: ItemIdentity | null, code: string): boolean {
    return (
      item !== null &&
      types.isOfType(this.primary(types, item), this.secondary(types, item), types.row(code))
    );
  }

  private primary(types: ItemTypeTree, item: ItemIdentity): number {
    return types.row(this._items.primaryTypeCode(item.classId));
  }

  private secondary(types: ItemTypeTree, item: ItemIdentity): number {
    return types.row(this._items.secondaryTypeCode(item.classId));
  }
}
