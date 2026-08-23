import { ItemRecordFlags, type ItemIdentity } from './ItemRecord.js';
import { ItemStatReader } from './ItemStatReader.js';
import type { ItemTable } from '../Tables/ItemTable.js';
import type { ItemTypeTree } from '../Tables/ItemTypeTree.js';
import { PropertiesTable } from '../Tables/PropertiesTable.js';
import type {
  D2DataFiles,
  TxtItemStatCostTable,
  TxtSkillTable,
} from '../Tables/TxtDataProviders.js';
import { Int32, type StatDescriptor } from '../Types.js';

/**
 * Which end of a ranged property to resolve to. A record carries no item seed, so a range cannot
 * be reproduced — but both ends can be, and the pair is the range.
 */
export enum RollEnd {
  Low = 0,
  High = 1,
}

/** One gem/rune mod: gems.txt's {code, param, min, max} quadruple. */
export interface ItemProperty {
  propertyId: number;
  param: number;
  min: number;
  max: number;
}

/**
 * ITEMMOD_ApplyPropertyToUnitStatsExpansion 0x65fd70 and the handlers behind
 * dword_7462F8 (0x65eb30..0x65fae0), ported from D2Common/src/Items/ItemMods.cpp whose own
 * dispatch table cites the same address.
 *
 * Every func any shipped table reaches is implemented — the gem and rune codes, and the affix,
 * unique, set, runeword and cube ones a roll-range reconstruction needs. Func 9 is the only
 * non-null handler left, and no code in any of the ten source tables carries it; it reports itself
 * through {@link PropertyApplier.unsupportedFunc} rather than applying nothing silently.
 */
export class PropertyApplier {
  // PROPMODE_*, from D2StatList.h. The gem and rune paths are the only ones used here.
  static readonly PropModeGem = 2;
  static readonly PropModeRune = 5;

  private static readonly StatMinDamage = 21;
  private static readonly StatMaxDamage = 22;
  private static readonly StatSecondaryMinDamage = 23;
  private static readonly StatSecondaryMaxDamage = 24;
  // 0x11 and 0x12 in D2StatList.h — MAX comes first.
  private static readonly StatMaxDamagePercent = 17;
  private static readonly StatMinDamagePercent = 18;
  private static readonly StatThrowMinDamage = 159;
  private static readonly StatThrowMaxDamage = 160;
  private static readonly StatPoisonMaxDamage = 58;
  private static readonly StatPoisonCount = 326;
  private static readonly StatIndestructible = 152;
  private static readonly StatNumSockets = 194;

  // Func 10 packs a skill-tab param as class * 8 + tab (0x65f434 divides by 3, 0x65f43b scales by
  // 8): three tabs per class, but an eight-wide stride between classes.
  private static readonly SkillTabsPerClass = 3;
  private static readonly SkillTabStride = 8;

  // Funcs 11 and 19 share one skill/level packing. Func 11 hardcodes both (0x65f565, 0x65f568);
  // func 19 reads them from the compiled table (0x65f82e, 0x65f841), where they hold these values.
  private static readonly SkillIdShift = 6;
  private static readonly SkillLevelMask = (1 << PropertyApplier.SkillIdShift) - 1;

  private readonly _properties: PropertiesTable;
  private readonly _statCost: TxtItemStatCostTable;
  private readonly _items: ItemTable;
  private readonly _types: ItemTypeTree;
  private readonly _skills: TxtSkillTable | null;
  private readonly _end: RollEnd;

  constructor(
    data: D2DataFiles,
    items: ItemTable,
    types: ItemTypeTree,
    end: RollEnd = RollEnd.Low,
  ) {
    this._properties = new PropertiesTable(data.properties, data.itemStatCost);
    this._statCost = data.itemStatCost;
    this._items = items;
    this._types = types;
    this._skills = data.skills;
    this._end = end;
  }

  get properties(): PropertiesTable {
    return this._properties;
  }

  /** Func codes reached that this port does not implement. */
  readonly unsupportedFunc = new Set<number>();

  /**
   * Properties the game resolves from the ITEM's own level, reported only when the record carries
   * none ({@link IUnit.itemLevel} being -1): funcs 11 and 19 with a non-positive max (0x65f4de /
   * 0x65f514 / 0x65f70a / 0x65f75b), and func 14's MaxSock tier (0x62bc81). Those land on the
   * game's floor instead of the real value.
   *
   * No shipped gems.txt or sets.txt property takes any of those arms — Cow King's `gethit-skill`
   * has max 5 — so this stays empty on the rendering path against stock data, and a test asserts
   * it. The roll-range reconstruction does reach them.
   */
  readonly itemLevelDependent = new Set<number>();

  /**
   * Applies one property's seven sets. Set 0's return value is threaded into every later set
   * as nValue, which each handler reads as "already rolled, do not roll again" (0x65fdfb).
   */
  apply(
    propMode: number,
    item: ItemIdentity,
    property: ItemProperty,
    into: Map<number, number>,
  ): void {
    const row = this._properties.getRow(property.propertyId);
    if (property.propertyId < 0 || row === null) {
      return;
    }

    let carried = 0;

    for (let set = 0; set < PropertiesTable.SetsPerProperty; ++set) {
      const func = row.func[set] ?? 0;
      if (func <= 0 || func >= PropertyApplier.HandlerCount) {
        break;
      }

      // nPropMode is deliberately not threaded past here. It selects WHICH properties get applied
      // and from where — the switch at ItemMods.cpp:2362, which the caller has already done by
      // enumerating the gems.txt or sets.txt row — not how one property behaves. Exactly one
      // handler in the 0x65eb30..0x65fae0 table looks at it at all: func 1 gates its "enhanced"
      // reset on `cmp ecx, 1` (0x65eb59), and that reset rewrites an existing statlist entry
      // rather than the temp list a description builds. None of the modes reaching this port is 1
      // anyway — gem 2, rune 5, set bonus 4 (0x6601df).
      const result = this.dispatch(
        func,
        item,
        property,
        row.set[set] ?? 0,
        row.stat[set] ?? 0,
        row.val[set] ?? 0,
        carried,
        into,
      );

      if (set === 0) {
        carried = result;
      }
    }
  }

  // dword_745B54 is 37; slots 25..35 are null and 36 is the uber handler.
  private static readonly HandlerCount = 37;

  private dispatch(
    func: number,
    item: ItemIdentity,
    property: ItemProperty,
    nSet: number,
    statId: number,
    nVal: number,
    carried: number,
    into: Map<number, number>,
  ): number {
    switch (func) {
      // 1 and 2 differ only in the nType == 1 gate on the "enhanced" reset, and that reset
      // targets an existing statlist entry, not the temp list a description builds — so on
      // the gem path (nType 2) neither reaches it.
      case 1:
      case 2:
        return this.addRolled(property, nSet, statId, 0, into);

      // 3 and 4 keep an already-rolled value instead of rolling again.
      case 3:
      case 4:
        return this.addRolled(property, nSet, statId, carried, into);

      case 5:
        return this.minDamage(item, property, nSet, carried, into);

      case 6:
        return this.maxDamage(item, property, nSet, carried, into);

      case 7:
        return this.enhancedDamage(item, property, nSet, carried, into);

      case 8:
        return this.addRolled(property, nSet, statId, carried, into);

      case 15:
        // Fixed to nMin, and routed through func 5 when it lands on min damage.
        if (statId === PropertyApplier.StatMinDamage) {
          this.minDamage(item, property, nSet, property.min, into);
        } else {
          this.addStat(nSet, statId, property.min, into);
        }

        return property.min;

      case 16:
        // Fixed to nMax — note nMax, not nMin.
        if (statId === PropertyApplier.StatMaxDamage) {
          this.maxDamage(item, property, nSet, property.max, into);
        } else {
          this.addStat(nSet, statId, property.max, into);
        }

        return property.max;

      case 17:
        return this.fixedOrRolled(item, property, nSet, statId, into);

      case 20:
        // Indestructible is a flag, written unshifted and unconditionally at value 1.
        this.addStat(0, PropertyApplier.StatIndestructible, 1, into);
        return 1;

      case 21: {
        // 0x65fb50. Same shape as func 1 except that the stat LAYER, which func 1 pushes as a
        // literal 0 (0x65eb83), comes from Properties.txt `val<n>` (0x65fb86) — the class number
        // for the seven `ama`..`ass` codes. It rolls unconditionally rather than honouring a
        // carried value (0x65fb66).
        const rolled = this.roll(property);
        this.addStat(nSet, statId, rolled, into, nVal);
        return rolled;
      }

      case 22: {
        // 0x65fbf0, and the layer is the property's own param truncated to a word
        // (`movzx edx, word ptr [esi+4]`, 0x65fc1b) — the skill id behind `oskill`.
        const rolled = this.roll(property);
        this.addStat(nSet, statId, rolled, into, property.param & 0xffff);
        return rolled;
      }

      case 11: {
        // ITEMPROP_AddSkillCharges 0x65f470. The property is (skill, chance, level): param is the
        // skill id, min the % chance (defaulted to 5 when not positive, 0x65f4af), and max the
        // LEVEL. The stat carries the pair packed into its LAYER — `(level & 0x3F) + (skill << 6)`
        // at 0x65f54f — with the chance as the value, which is the same encoding a captured
        // charged-skill stat arrives in.
        const skill = property.param;
        if (skill < 0) {
          return 0; // 0x65f4a1 bounds the skill against the skills table
        }

        const chance = property.min > 0 ? property.min : 5;

        // max > 0 is the whole computation (0x65f50c falls straight through to the add). The other
        // two arms derive the level from the ITEM's level against skills.txt reqlevel — 0x65f4de
        // for max === 0, 0x65f514 for max < 0 — which is exact when the capture recorded one;
        // without it the game's own floor of 1 (0x65f54d) is used and the property is reported
        // rather than mis-levelled. Func 19 derives it identically, so both share one helper.
        let level = property.max;
        if (level <= 0) {
          let derived = this.skillLevelFromItemLevel(item, skill, property.max);
          if (derived < 0) {
            this.itemLevelDependent.add(property.propertyId);
            derived = 1;
          }

          level = derived;
        }

        this.addStat(
          nSet,
          statId,
          chance,
          into,
          (level & PropertyApplier.SkillLevelMask) + (skill << PropertyApplier.SkillIdShift),
        );
        return chance;
      }

      case 24: {
        // ITEMPROP_AddStat_WithLayerFromParam4 0x65f390: the rolled add with the layer taken from
        // the property's own param (`a4[1]` at 0x65f39e), unmasked — unlike func 22, which
        // truncates it to a word.
        const value = carried !== 0 ? carried : this.roll(property);
        this.addStat(nSet, statId, value, into, property.param);
        return value;
      }

      case 13: {
        // ITEMPROP_AddStat_WithMaxDurabilityReset 0x65fc90 — `dur%`, stat 75. Reached by every
        // superior item through qualityitems.txt, which is four of that file's eight rows. Rolls
        // UNCONDITIONALLY onto layer 0 (0x65fcc7 has no carried check), which is the whole handler
        // as far as stock data goes.
        //
        // Two arms are deliberately not modelled. The handler resets stat 72 to GetTxtMaxDurability
        // 0x625e00 (0x65fcfe), but that reads ItemStatCost[73]'s MinAccr — blank in shipped data, so
        // 0 — and the write is gated on > 0 (0x65fcf6), so it cannot fire. And the propMode === 1
        // "enhanced" maximise at 0x65fcb0 is the same arm func 1 has, which no mode reaching this
        // port takes.
        const value = this.roll(property);
        this.addStat(nSet, statId, value, into);
        return value;
      }

      case 10: {
        // ITEMPROP_AddClassSkillBonus 0x65f3f0 — `skilltab`, stat 188. The same carried-or-roll
        // tail as func 24, but the LAYER re-packs the param: it is a tab index over the seven
        // classes' three tabs each, and 0x65f433..0x65f43b divides by 3 to give class*8 + tab — an
        // 8-wide stride over a 3-wide field. `idiv` truncates toward zero and leaves the remainder
        // signed, which is what Int32.div and the `%` operator already do.
        const value = carried !== 0 ? carried : this.roll(property);
        const layer =
          Int32.div(property.param, PropertyApplier.SkillTabsPerClass) *
            PropertyApplier.SkillTabStride +
          (property.param % PropertyApplier.SkillTabsPerClass);
        this.addStat(nSet, statId, value, into, layer);
        return value;
      }

      case 12: {
        // ITEMPROP_AddStat_RandLevelAsLayer 0x65fc40 — `skill-rand`, stat 107. The roll lands in
        // the LAYER and the property's own param is the VALUE (0x65fc72 pushes param into the
        // value slot, 0x65fc76 the roll into the layer slot). It rolls unconditionally, ignoring
        // what set 0 carried (0x65fc63 has no carried check at all).
        //
        // Ormus' Robes is the only shipped user: par=3, min=36, max=60 — "+3 to" a rolled skill id
        // in 36..60, the twenty-five sorceress skills.
        const rolledLayer = this.roll(property);
        this.addStat(nSet, statId, property.param, into, rolledLayer);
        return property.param;
      }

      case 36: {
        // ITEMPROP_AddStat_LayerFromRoll_ValueFromParam7 0x65fba0 — `randclassskill`, stat 83.
        // Func 12 with the value taken from Properties.txt `val<n>` instead of the param
        // (`movsx edx, [ebp+arg_10]`, 0x65fbcb).
        //
        // Hellfire Torch is the only shipped user: val1=3, min=0, max=6 — "+3" to a rolled class.
        const rolledLayer = this.roll(property);
        this.addStat(nSet, statId, nVal, into, rolledLayer);
        return nVal;
      }

      case 23:
        // ITEMPROP_ApplyEthereal 0x65fd20. Writes no stat — its Properties.txt `stat1` is blank —
        // it flips the ethereal flag and applies the ethereal bonus. An already-ethereal item
        // returns 0 (0x65fd3e), as does one with no durability (0x65fd48); otherwise 1
        // (0x65fd50). Only the return matters here, because set 0's return becomes the carried
        // value for every later set, and a captured item's ethereality is already in its flags.
        // The handler's null-unit guard (0x65fd2f) is the parameter type's job here.
        return item.has(ItemRecordFlags.Ethereal) ? 0 : 1;

      case 18: {
        // ITEMPROP_AddTimedStat 0x65f870 — the `*<thing>/time` family, stats 268..303. The value is
        // a PACKED TRIPLE rather than a magnitude: param clamped to 0..3, then min and max each
        // biased by +256 and clamped to 0..0x3FF, laid out as `param + 4 * ((max << 10) + min)`
        // (0x65f934..0x65f93d). So a by-time stat carries its own two ends and needs no roll.
        const mode = property.param <= 0 ? 0 : Math.min(property.param, 3);
        const low = PropertyApplier.clampTimedBound(property.min);
        const high = PropertyApplier.clampTimedBound(property.max);

        // Unlike every addStatToItem func this one calls D2AddStatToStatsListEx directly
        // (0x65f947), so the value is stored UNSHIFTED and always SET. Every stat it can reach has
        // ValShift 0 in shipped data, so the distinction is unobservable — it is modelled because
        // the packing would be corrupted by a shift if it ever were not.
        this.setRawStat(statId, mode + 4 * ((high << 10) + low), 0, into);
        return high;
      }

      case 19: {
        // ITEMPROP_AddSkillOnEvent 0x65f6a0 — `charged`, stat 204. The value packs the charge pair,
        // `(maxCharges << 8) + current` (0x65f84b), and the layer packs the skill and its level
        // exactly as func 11 does.
        const skill = property.param;
        if (skill < 0) {
          return 0; // 0x65f6da bounds the skill against the skills table
        }

        // max > 0 is the level outright (0x65f759). The other two arms derive it from the ITEM's
        // level — 0x65f70a for max === 0, 0x65f75b for max < 0 — which is exact when the capture
        // recorded one and otherwise falls to the game's own floor of 1, reported rather than
        // silently mis-levelled.
        let level = property.max;
        if (level <= 0) {
          let derived = this.skillLevelFromItemLevel(item, skill, property.max);
          if (derived < 0) {
            this.itemLevelDependent.add(property.propertyId);
            derived = 1;
          }

          level = derived;
        }

        // min === 0 defaults the charge count to 5 (0x65f7a8); min < 0 scales it by the level,
        // `|min| + (|min| * level) / 8` (0x65f7b1..0x65f7c2); otherwise min is it. Then clamped to
        // 1..255 (0x65f7c4..0x65f7dd).
        let maxCharges: number;
        if (property.min === 0) {
          maxCharges = 5;
        } else if (property.min < 0) {
          const magnitude = -property.min;
          maxCharges = magnitude + Int32.div(magnitude * level, 8);
        } else {
          maxCharges = property.min;
        }

        maxCharges = Math.min(Math.max(maxCharges, 1), 255);

        // The CURRENT count is drawn off the item seed:
        // `rand(maxCharges - maxCharges / 8) + maxCharges / 8 + 1` (0x65f7ec..0x65f80e), so it
        // spans maxCharges/8 + 1 .. maxCharges. A record has no seed, so this resolves to one end
        // under the same policy as roll().
        const floor = Int32.div(maxCharges, 8) + 1;
        const current = this._end === RollEnd.High ? maxCharges : floor;

        this.setRawStat(
          statId,
          (maxCharges << 8) + (current & 0xff),
          (skill << PropertyApplier.SkillIdShift) + (level & PropertyApplier.SkillLevelMask),
          into,
        );
        return maxCharges;
      }

      case 14: {
        // ITEMPROP_SetSockets 0x65f590 — `sock`, stat 194. Capped first by the item's own
        // footprint, `min(6, invwidth * invheight)` (0x65f5cb sets 6, 0x65f5e5 multiplies), and a
        // zero footprint writes nothing (0x65f5f0). The handler's null-unit guard (0x65f5a8) is
        // the parameter type's job here.
        const footprint =
          this._items.getInt(item.classId, 'invwidth') *
          this._items.getInt(item.classId, 'invheight');
        if (footprint <= 0) {
          return 0;
        }

        let cap = Math.min(footprint, 6);

        // ITEM_GetMaxSockCount 0x62bc20 narrows that to
        // `min(gemsockets, MaxSock1|MaxSock25|MaxSock40)`, choosing the tier by ITEM LEVEL — <= 25,
        // <= 40, else (0x62bc81/0x62bc8c).
        //
        // The gemsockets half is applied unconditionally, INCLUDING zero: min(gemsockets, tier) is
        // 0 whenever gemsockets is 0 whatever the tier, so a base that takes no sockets at all —
        // boots, gloves, belts, rings — ends with cap 0 and writes nothing, which is the 0x65f679
        // `test esi, esi` arm falling through to return 0.
        const gemSockets = this._items.getInt(item.classId, 'gemsockets');
        if (gemSockets < cap) {
          cap = gemSockets;
        }

        // The tier half needs the item level. With one recorded this is exact; without, it is
        // reported and left off rather than guessed, which can only WIDEN the result — never move
        // it onto a count no item level could reach.
        const tier = this.maxSocketsForLevel(item);
        if (tier < 0) {
          this.itemLevelDependent.add(property.propertyId);
        } else if (tier < cap) {
          cap = tier;
        }

        // carried wins only when POSITIVE here (0x65f618 tests > 0, not !== 0), and a non-positive
        // roll falls back to the property's param (0x65f634).
        let sockets = carried > 0 ? carried : this.roll(property);
        if (sockets <= 0) {
          sockets = property.param;
        }

        sockets = Math.min(Math.max(sockets, 1), cap);
        if (sockets <= 0) {
          return 0;
        }

        // STATLIST_SetUnitStat at 0x65f667, so this SETS rather than adds, and the 0x800 socketed
        // flag it also raises (0x65f659) is already in a captured record's own flags.
        this.setRawStat(PropertyApplier.StatNumSockets, sockets, 0, into);
        return sockets;
      }

      default:
        this.unsupportedFunc.add(func);
        return 0;
    }
  }

  /**
   * The ItemTypes half of ITEM_GetMaxSockCount 0x62bc20, or -1 when the record carries no item
   * level. The row is the item's PRIMARY type (`ITEM_GetItemData_wType` at 0x62bc32), with no
   * equivalence walk.
   */
  private maxSocketsForLevel(item: ItemIdentity): number {
    if (item.itemLevel < 0) {
      return -1;
    }

    return this._types.maxSockets(
      this._types.row(this._items.primaryTypeCode(item.classId)),
      item.itemLevel,
    );
  }

  /**
   * The skill level funcs 11 and 19 derive when a property's max is non-positive. Both compute it
   * identically: max === 0 steps every four levels above the skill's requirement
   * (0x65f70a..0x65f725), max < 0 divides the remaining levels by |max| first
   * (0x65f75b..0x65f790). Returns -1 when the item level is absent, so the caller reports the
   * property instead of inventing a level.
   */
  private skillLevelFromItemLevel(item: ItemIdentity, skill: number, max: number): number {
    if (item.itemLevel < 0) {
      return -1;
    }

    const required = this.skillRequiredLevel(skill);

    if (max === 0) {
      // `(ilvl - req) / 4 + 1`, the divide truncating toward zero (`and edx, 3` then `sar eax, 2`
      // at 0x65f71a).
      const raw = Int32.div(item.itemLevel - required, 4) + 1;

      // Clamped against the skill's own maxlvl, and note the comparison uses the FLOORED value
      // while the result keeps the raw one (0x65f72e..0x65f748).
      const floored = raw < 1 ? 1 : raw;
      const ceiling = this._skills === null ? 20 : this._skills.maxLevel(skill);

      return floored >= ceiling ? ceiling : floored;
    }

    // 99 - req floored at 1, divided by |max| and floored at 1 again, then used as the step size
    // over the levels above the requirement (0x65f763..0x65f790).
    let span = 99 - required;
    if (span < 1) {
      span = 1;
    }

    let step = Int32.div(span, -max);
    if (step < 1) {
      step = 1;
    }

    const level = Int32.div(item.itemLevel - required, step);

    // Floored at 1, as the other arm is (0x65f797).
    return level < 1 ? 1 : level;
  }

  private skillRequiredLevel(skill: number): number {
    if (this._skills === null) {
      return 0;
    }

    // Out-of-range ids give -1, which would inflate `ilvl - req`; the handlers bound the skill
    // first, so this only guards a caller that did not.
    const required = this._skills.requiredLevel(skill);
    return required < 0 ? 0 : required;
  }

  // 0x65f8c6..0x65f8d8 and 0x65f8e1..0x65f90f: bias by +256, floor a non-positive result at 0,
  // then cap at the 10-bit field width.
  private static clampTimedBound(bound: number): number {
    const biased = bound + 256;
    if (biased <= 0) {
      return 0;
    }

    return Math.min(biased, 0x3ff);
  }

  /**
   * The D2AddStatToStatsListEx path funcs 18 and 19 take directly, bypassing
   * ITEMMOD_AddStatToItem: no nValShift on the value, always a SET, and none of AddStatToItem's
   * poison-count side effect.
   */
  private setRawStat(
    statId: number,
    value: number,
    layer: number,
    into: Map<number, number>,
  ): void {
    if (value === 0 || statId < 0) {
      return;
    }

    const descriptor: StatDescriptor | null = this._statCost.tryGetStat(statId);
    if (descriptor === null) {
      return;
    }

    into.set(ItemStatReader.packStatKey(layer, statId), value);
  }

  // The shared tail of funcs 1..4 and 8: roll unless a value was carried in, then add.
  private addRolled(
    property: ItemProperty,
    nSet: number,
    statId: number,
    carried: number,
    into: Map<number, number>,
  ): number {
    const value = carried !== 0 ? carried : this.roll(property);
    this.addStat(nSet, statId, value, into);
    return value;
  }

  /**
   * The min/max normalisation every handler shares — ITEMMOD_RollRandomValue 0x65e9e0, which
   * SWAPS a reversed pair (0x65e9f6) rather than rejecting it, making the range inclusive of both
   * ends. A genuine roll needs the ITEM SEED (0x65ea08), which a record does not carry, so a
   * ranged property resolves to the END this applier was built for and is reported through
   * {@link PropertyApplier.rolledRanges}.
   */
  private roll(property: ItemProperty): number {
    let min = property.min;
    let max = property.max;

    if (max === min) {
      return min;
    }

    if (max < min) {
      max = property.min;
      min = property.max;
    }

    if (min < max) {
      this.rolledRanges.add(property.propertyId);
    }

    return this._end === RollEnd.High ? max : min;
  }

  /** Property ids whose value depends on the item seed and so is only the low end. */
  readonly rolledRanges = new Set<number>();

  // ITEMMODS_PropertyFunc05. On a GEM the weapon-type test is false, so all three damage stats
  // are written; the per-stat floor keeps base + value at 1 or more.
  private minDamage(
    item: ItemIdentity,
    property: ItemProperty,
    nSet: number,
    carried: number,
    into: Map<number, number>,
  ): number {
    const value = carried !== 0 ? carried : this.roll(property);
    const weapon = this.isWeapon(item);

    const oneHand = this._items.getInt(item.classId, 'mindam');
    const twoHand = this._items.getInt(item.classId, '2handmindam');
    const missile = this._items.getInt(item.classId, 'minmisdam');

    if (!weapon || oneHand !== 0 || twoHand === 0) {
      this.addFloored(nSet, PropertyApplier.StatMinDamage, value, oneHand, 1, into);
    }

    if (!weapon || twoHand !== 0 || oneHand === 0) {
      this.addFloored(nSet, PropertyApplier.StatSecondaryMinDamage, value, twoHand, 1, into);
    }

    if (!weapon || this.isThrowable(item)) {
      this.addFloored(nSet, PropertyApplier.StatThrowMinDamage, value, missile, 1, into);
    }

    return value;
  }

  // ITEMMODS_PropertyFunc06. Same shape as func 5 but the floor is -base, not 1 - base.
  private maxDamage(
    item: ItemIdentity,
    property: ItemProperty,
    nSet: number,
    carried: number,
    into: Map<number, number>,
  ): number {
    const value = carried !== 0 ? carried : this.roll(property);
    const weapon = this.isWeapon(item);

    const oneHand = this._items.getInt(item.classId, 'maxdam');
    const twoHand = this._items.getInt(item.classId, '2handmaxdam');
    const missile = this._items.getInt(item.classId, 'maxmisdam');

    if (!weapon || oneHand !== 0 || twoHand === 0) {
      this.addFloored(nSet, PropertyApplier.StatMaxDamage, value, oneHand, 0, into);
    }

    if (!weapon || twoHand !== 0 || oneHand === 0) {
      this.addFloored(nSet, PropertyApplier.StatSecondaryMaxDamage, value, twoHand, 0, into);
    }

    if (!weapon || this.isThrowable(item)) {
      this.addFloored(nSet, PropertyApplier.StatThrowMaxDamage, value, missile, 0, into);
    }

    return value;
  }

  // `floorAt` is 1 for the min-damage family and 0 for the max-damage family: func 5 clamps to
  // `1 - base` and func 6 to `-base`, which is the one place the two are not mirror images.
  private addFloored(
    nSet: number,
    statId: number,
    value: number,
    baseDamage: number,
    floorAt: number,
    into: Map<number, number>,
  ): void {
    let result = value;

    if (baseDamage !== 0 && baseDamage + value <= 0) {
      result = Int32.of(floorAt - baseDamage);
    }

    if (result !== 0) {
      this.addStat(nSet, statId, result, into);
    }
  }

  // ITEMMODS_PropertyFunc07. Enhanced damage is a percentage pair, except that on a weapon
  // where the percentage would round away to nothing it degrades into a flat +1 max damage.
  private enhancedDamage(
    item: ItemIdentity,
    property: ItemProperty,
    nSet: number,
    carried: number,
    into: Map<number, number>,
  ): number {
    const value = carried !== 0 ? carried : this.roll(property);

    const oneHand = this._items.getInt(item.classId, 'maxdam');
    const twoHand = this._items.getInt(item.classId, '2handmaxdam');
    const maxDamage = twoHand > oneHand ? twoHand : oneHand;

    // `long` in the C#, so the multiply cannot overflow before the divide truncates.
    const bonus = (BigInt(value) * BigInt(maxDamage)) / 100n;

    if (this.isWeapon(item) && BigInt(maxDamage) + bonus <= BigInt(maxDamage)) {
      return this.maxDamage(item, property, nSet, 1, into);
    }

    this.addStat(nSet, PropertyApplier.StatMinDamagePercent, value, into);
    this.addStat(nSet, PropertyApplier.StatMaxDamagePercent, value, into);
    return value;
  }

  // ITEMMODS_PropertyFunc17: the param takes precedence, otherwise roll.
  private fixedOrRolled(
    item: ItemIdentity,
    property: ItemProperty,
    nSet: number,
    statId: number,
    into: Map<number, number>,
  ): number {
    let value = property.param;
    if (value === 0) {
      value = this.roll(property);
      if (value === 0) {
        return 0;
      }
    }

    if (statId === PropertyApplier.StatMaxDamage) {
      this.maxDamage(item, property, nSet, value, into);
    } else {
      this.addStat(nSet, statId, value, into);
    }

    return value;
  }

  /**
   * ITEMMODS_AddPropertyToItemStatList 0x65ea50. A zero value writes nothing, an unknown stat
   * writes nothing, and the value is stored SHIFTED LEFT by nValShift — the description engine
   * shifts it back, so an unshifted value would render as a fraction of itself.
   */
  private addStat(
    nSet: number,
    statId: number,
    value: number,
    into: Map<number, number>,
    layer = 0,
  ): void {
    if (value === 0 || statId < 0) {
      return;
    }

    const descriptor: StatDescriptor | null = this._statCost.tryGetStat(statId);
    if (descriptor === null) {
      return;
    }

    const key = ItemStatReader.packStatKey(layer, statId);
    const shifted = value << descriptor.valShift;

    // nSet selects SET over ADD (0x65eac6 versus 0x65eb0a).
    if (nSet !== 0) {
      into.set(key, shifted);
    } else {
      const existing = into.get(key);
      into.set(key, existing === undefined ? shifted : Int32.of(existing + shifted));
    }

    if (statId !== PropertyApplier.StatPoisonMaxDamage) {
      return;
    }

    // Poison damage drags a duration of 1 along with it or the description reads
    // "over 0 seconds".
    const countKey = ItemStatReader.packStatKey(0, PropertyApplier.StatPoisonCount);
    if (nSet !== 0) {
      if (!into.has(countKey)) {
        into.set(countKey, 1);
      }
    } else {
      const existing = into.get(countKey);
      into.set(countKey, existing === undefined ? 1 : Int32.of(existing + 1));
    }
  }

  private isWeapon(item: ItemIdentity): boolean {
    return this.isOfType(item, 'weap');
  }

  // ITEMS_CheckItemTypeIfThrowable reads the PRIMARY type row's Throwable column directly —
  // no equivalence walk, unlike the weapon test.
  private isThrowable(item: ItemIdentity): boolean {
    const row = this._types.row(this._items.primaryTypeCode(item.classId));
    return this._types.isThrowable(row);
  }

  private isOfType(item: ItemIdentity, code: string): boolean {
    return this._types.isOfType(
      this._types.row(this._items.primaryTypeCode(item.classId)),
      this._types.row(this._items.secondaryTypeCode(item.classId)),
      this._types.row(code),
    );
  }
}
