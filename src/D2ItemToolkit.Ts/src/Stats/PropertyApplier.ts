import type { ItemIdentity } from './ItemRecord.js';
import { ItemStatReader } from './ItemStatReader.js';
import type { ItemTable } from '../Tables/ItemTable.js';
import type { ItemTypeTree } from '../Tables/ItemTypeTree.js';
import { PropertiesTable } from '../Tables/PropertiesTable.js';
import type { D2DataFiles, TxtItemStatCostTable } from '../Tables/TxtDataProviders.js';
import { Int32, type StatDescriptor } from '../Types.js';

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
 * Only the funcs a gems.txt mod code can actually reach are implemented; the rest report
 * themselves through {@link PropertyApplier.unsupportedFunc} rather than silently applying nothing.
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

  private readonly _properties: PropertiesTable;
  private readonly _statCost: TxtItemStatCostTable;
  private readonly _items: ItemTable;
  private readonly _types: ItemTypeTree;

  constructor(data: D2DataFiles, items: ItemTable, types: ItemTypeTree) {
    this._properties = new PropertiesTable(data.properties, data.itemStatCost);
    this._statCost = data.itemStatCost;
    this._items = items;
    this._types = types;
  }

  get properties(): PropertiesTable {
    return this._properties;
  }

  /** Func codes reached that this port does not implement. */
  readonly unsupportedFunc = new Set<number>();

  /**
   * Properties whose skill LEVEL the game derives from the item's own level (func 11 with a
   * non-positive max, 0x65f4de / 0x65f514). The record carries no item level, so those land on the
   * game's floor of 1 instead of the real value. No shipped gems.txt or sets.txt property takes
   * that arm — Cow King's `gethit-skill` has max 5 — so this stays empty against stock data, and a
   * test asserts it.
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
        // for max === 0, 0x65f514 for max < 0 — and the record carries no item level, so they
        // cannot be reproduced. The game's own floor in both is 1 (0x65f54d), which is what is used
        // instead, and the property is reported rather than silently mis-levelled.
        let level = property.max;
        if (level <= 0) {
          this.itemLevelDependent.add(property.propertyId);
          level = 1;
        }

        this.addStat(nSet, statId, chance, into, (level & 0x3f) + (skill << 6));
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

      default:
        this.unsupportedFunc.add(func);
        return 0;
    }
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
   * The min/max normalisation every handler shares. A genuine range needs the ITEM SEED
   * (SEED_RollLimitedRandomNumber), which a record does not carry, so a ranged property
   * resolves to its LOW end here and is reported through {@link PropertyApplier.rolledRanges}.
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

    return min;
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
