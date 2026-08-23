import {
  DamageStatIds,
  ItemDamageAggregate,
  UndeadDamageLine,
} from '../Tooltip/ItemDamageLines.js';
import { ItemStatReader } from '../Stats/ItemStatReader.js';
import {
  DescStringIds,
  Int32,
  type ICharacterClassTable,
  type IGameTimeProvider,
  type IItemStatCostTable,
  type IMonsterTypeTable,
  type ISkillTable,
  type IStatValueSource,
  type IStringTable,
  type StatDescriptor,
} from '../Types.js';

/**
 * The one DescStringIds member Types.ts does not carry, because it is an array rather than a
 * scalar id. Indexed by the packed period through the permuted table at 0x6DBD88.
 */
export const PeriodOfDay: readonly number[] = [21235, 21237, 21234, 21236];

export class ItemDescriptionLine {
  text = '';

  statId = 0;

  /**
   * The stat's layer — the skill, class or tab the line is about. Carried alongside statId so a
   * caller can match a rendered line back to one stat KEY rather than to a stat id that several
   * skills share.
   */
  layer = 0;

  value = 0;

  descPriority = 0;
  isGroup = false;

  /**
   * The line speaks for MORE than the one stat in statId — a DescGrp variant ("+2 to all
   * Attributes") or a min-max damage line ("Adds 1-4 cold damage", which is coldmindam and
   * coldmaxdam together). A single stat's roll range is the wrong thing to show against one of
   * these, since the reader cannot tell which half it belongs to.
   */
  aggregated = false;

  /**
   * Every stat this line displays a number for, in the order the numbers appear. Null means just
   * statId. This is what lets a roll range be shown against an aggregated line as the composite it
   * is — "Adds 1-4 cold damage" spans two stats and so wants two spans — rather than being
   * suppressed for want of a single answer.
   */
  shownStats: number[] | null = null;

  preJoined = false;

  get isBlank(): boolean {
    return this.text.length === 0;
  }

  toString(): string {
    return this.text;
  }
}

export const ItemDescFunc = {
  PlusValueString: 1,
  ValuePercentString: 2,
  ValueString: 3,
  PlusValuePercentString: 4,
  ValueFramesPercentString: 5,
  PlusValueStringString2: 6,
  ValuePercentStringString2: 7,
  PlusValuePercentStringString2: 8,
  ValueStringString2: 9,
  ValueFramesPercentStringString2: 10,
  RepairDurability: 11,
  PlusValueStringSuppressOne: 12,
  ClassAllSkills: 13,
  SkillTab: 14,
  SkillOnEvent: 15,
  SkillAura: 16,
  ValueStringByTime: 17,
  ValuePercentStringByTime: 18,
  RawFormat: 19,
  NegatedValuePercentString: 20,
  NegatedValuePercentStringString2: 21,
  MonsterTypeDamage: 22,
  MonsterDamage: 23,
  Charges: 24,
  StaleNegated25: 25,
  StaleNegated26: 26,
  SkillClassOnly: 27,
  Skill: 28,
} as const;

export class ByTimeValue {
  period = 0;

  low = 0;

  high = 0;

  static unpack(value: number): ByTimeValue {
    const unpacked = new ByTimeValue();
    unpacked.period = value & 3;
    unpacked.low = ((value >> 2) & 0x3ff) - 256;
    unpacked.high = ((value >> 12) & 0x3ff) - 256;
    return unpacked;
  }

  interpolate(degrees: number): number {
    let distance = Math.abs(degrees - Int32.mul(90, this.period));
    distance = Int32.mul(15, Int32.div(distance + 7, 15));

    if (distance <= 0) {
      distance = 0;
    } else if (distance >= 359) {
      distance = 359;
    }

    if (distance > 180) {
      distance = 360 - distance;
    }

    return this.high - Int32.div(Int32.mul(distance, this.high - this.low), 180);
  }
}

enum DescValFallback {
  StringOnly,
  Empty,
}

interface FormatContext {
  func: number;
  descVal: number;
  value: number;
  layer: number;
  strPos: number;
  strNeg: number;

  rawStrPos: number;
  str2: number;
  text: string | null;
}

export class ItemDescriptionGenerator {
  private static readonly SuppressedBy: readonly (readonly [number, number])[] = [
    [23, 21],
    [24, 22],
  ];

  private static readonly Stat122 = 122;
  private static readonly ItemType57 = 57;

  private static readonly StatIndestructible = 152;

  private static readonly StatMaxDurability = 73;

  private static readonly MaxEntriesPerStat = 511;

  private readonly stats: IItemStatCostTable;
  private readonly strings: IStringTable;
  private readonly values: IStatValueSource | null;
  private readonly skills: ISkillTable | null;
  private readonly classes: ICharacterClassTable | null;
  private readonly monsters: IMonsterTypeTable | null;
  private readonly time: IGameTimeProvider | null;

  private readonly isMainStatBlock: boolean;

  constructor(
    stats: IItemStatCostTable | null | undefined,
    strings: IStringTable | null | undefined,
    values: IStatValueSource | null = null,
    skills: ISkillTable | null = null,
    classes: ICharacterClassTable | null = null,
    monsters: IMonsterTypeTable | null = null,
    time: IGameTimeProvider | null = null,
    isMainStatBlock = true,
  ) {
    if (stats === null || stats === undefined) throw new Error('stats');
    if (strings === null || strings === undefined) throw new Error('strings');

    this.isMainStatBlock = isMainStatBlock;

    this.stats = stats;
    this.strings = strings;
    this.values = values;
    this.skills = skills;
    this.classes = classes;
    this.monsters = monsters;
    this.time = time;
  }

  describe(
    packedStats: Iterable<readonly [number, number]> | null | undefined,
  ): readonly ItemDescriptionLine[] {
    if (packedStats === null || packedStats === undefined) throw new Error('packedStats');

    const byStat = new Map<number, [number, number][]>();

    for (const entry of packedStats) {
      const statId = ItemStatReader.statFromKey(entry[0]);
      const layer = ItemStatReader.layerFromKey(entry[0]);

      let entries = byStat.get(statId);
      if (entries === undefined) {
        entries = [];
        byStat.set(statId, entries);
      }

      entries.push([layer, entry[1]]);
    }

    const lines: ItemDescriptionLine[] = [];

    const undead = UndeadDamageLine.build(this.strings, this.values, this.isMainStatBlock);
    if (undead !== null && undead.length !== 0) {
      const undeadLine = new ItemDescriptionLine();
      undeadLine.text = undead;
      undeadLine.statId = DamageStatIds.UndeadDamagePercent;
      undeadLine.value = UndeadDamageLine.InherentPercent;
      undeadLine.preJoined = true;
      lines.push(undeadLine);
    }

    const damage = new ItemDamageAggregate(this.strings, this.values);

    for (const statId of this.stats.statIdsByDescPriority) {
      const entries = byStat.get(statId);
      if (entries === undefined) {
        continue;
      }

      const descriptor = this.stats.tryGetStat(statId);

      entries.sort(ItemDescriptionGenerator.compareByLayer);

      // 511 per stat (0x4e6261 / 0x626177), applied BEFORE the zero-value filter, not after
      // (0x4e628b / 0x4e6295).
      if (entries.length > ItemDescriptionGenerator.MaxEntriesPerStat) {
        entries.length = ItemDescriptionGenerator.MaxEntriesPerStat;
      }

      for (const entry of entries) {
        if (entry[1] === 0) {
          continue; // 0x4e628b / 0x4e6295: skipped AFTER the 511 cap, not before
        }

        const aggregated = damage.tryDescribe(statId);
        if (aggregated !== null) {
          if (aggregated.length === 0) {
            continue; // suppression only: the game emits nothing at all
          }

          const damageLine = new ItemDescriptionLine();
          damageLine.text = aggregated;
          damageLine.statId = statId;
          damageLine.layer = entry[0];
          damageLine.value = entry[1];
          damageLine.preJoined = true;
          damageLine.aggregated = ItemDamageAggregate.showsSeveralValues(statId);
          damageLine.shownStats = ItemDamageAggregate.statsShownBy(statId);
          lines.push(damageLine);
          continue;
        }

        if (descriptor === null || descriptor.descFunc === 0) {
          continue;
        }

        const line = this.describeEntry(descriptor, entry[0], entry[1]);
        if (line === null) {
          continue;
        }

        if (this.isSuppressedByAnotherStat(statId)) {
          continue;
        }

        lines.push(line);
      }
    }

    this.appendNeverBreaksLine(lines);
    return lines;
  }

  private appendNeverBreaksLine(lines: ItemDescriptionLine[]): void {
    if (
      this.values === null ||
      !this.values.describedUnitIsItem ||
      !this.values.itemTableAllowsDurability ||
      this.values.getItemStatValue(ItemDescriptionGenerator.StatIndestructible) > 0 ||
      this.values.getTxtMaxDurability() !== 0
    ) {
      return;
    }

    const line = new ItemDescriptionLine();
    line.text = ItemDescriptionGenerator.nz(this.str(DescStringIds.NeverBreaks));
    line.statId = ItemDescriptionGenerator.StatMaxDurability;
    lines.push(line);
  }

  // INLINE mode is the default and what the item tooltip uses (0x48e92d pushes arg_4 = 1):
  // string 3998 terminates EVERY line and no separator is inserted. Block mode instead puts
  // 3852 + 3995 BEFORE each line after the first and terminates nothing. A PreJoined line is
  // appended raw and skips the terminator either way (0x4e62ad).
  join(lines: Iterable<ItemDescriptionLine> | null | undefined, inlineMode = true): string {
    if (lines === null || lines === undefined) throw new Error('lines');

    let builder = '';

    if (inlineMode) {
      const terminator = ItemDescriptionGenerator.nz(this.str(DescStringIds.Newline));
      for (const line of lines) {
        builder += line.text;

        if (!line.preJoined) {
          builder += terminator;
        }
      }

      return builder;
    }

    const separator =
      ItemDescriptionGenerator.nz(this.str(DescStringIds.ListComma)) +
      ItemDescriptionGenerator.nz(this.str(DescStringIds.Space));
    let first = true;

    for (const line of lines) {
      if (line.preJoined) {
        builder += line.text;
        continue;
      }

      if (!first) {
        builder += separator;
      }

      builder += line.text;
      first = false;
    }

    return builder;
  }

  private static compareByLayer(
    a: readonly [number, number],
    b: readonly [number, number],
  ): number {
    return a[0] < b[0] ? -1 : a[0] > b[0] ? 1 : 0;
  }

  private isSuppressedByAnotherStat(statId: number): boolean {
    if (this.values === null) {
      return false;
    }

    for (let i = 0; i < ItemDescriptionGenerator.SuppressedBy.length; ++i) {
      const pair = ItemDescriptionGenerator.SuppressedBy[i];
      if (
        pair !== undefined &&
        pair[0] === statId &&
        this.values.getBaseStatValue(pair[1], 0) !== 0
      ) {
        return true;
      }
    }

    return false;
  }

  /** The C# `bool TryComputeValue(..., out int result)` pair; null is the false return. */
  private tryComputeValue(
    descriptor: StatDescriptor,
    statId: number,
    storedValue: number,
  ): number | null {
    let value = storedValue;

    if (descriptor.op >= 2 && descriptor.op <= 5) {
      if (descriptor.opBase >= this.stats.rowCount) {
        return null;
      }

      const opBase = this.stats.tryGetStat(descriptor.opBase);
      if (opBase === null) {
        return null;
      }

      const scale =
        this.values === null
          ? 0
          : this.values.getPlayerStatValue(descriptor.opBase) >> opBase.valShift;
      value = Int32.mul(value, scale) >> descriptor.opParam;
    }

    value = descriptor.valShift > 0 ? value >> descriptor.valShift : value;

    if (
      statId === ItemDescriptionGenerator.Stat122 &&
      this.values !== null &&
      this.values.isItemOfType(ItemDescriptionGenerator.ItemType57)
    ) {
      // int32: the C# `neg`/add wraps, so int.MaxValue + 50 lands NEGATIVE and the line reads
      // as a penalty rather than a bonus (0x4e4cc1).
      value = (value + 50) | 0;
    }

    return value;
  }

  /** The C# `bool IsGrouped(..., out bool isPrimary)` pair; null is the false return. */
  private isGrouped(descriptor: StatDescriptor, value: number): { isPrimary: boolean } | null {
    if (descriptor.descGrp === 0) {
      return null;
    }

    const members: readonly number[] | null | undefined = this.stats.getStatsInDescGroup(
      descriptor.descGrp,
    );
    if (members === null || members === undefined || members.length === 0) {
      return null;
    }

    let lowest = 0x7fffffff;

    for (const memberStatId of members) {
      if (memberStatId < lowest) {
        lowest = memberStatId;
      }

      const member = this.stats.tryGetStat(memberStatId);
      if (member === null) {
        return null;
      }

      const memberStored = this.values === null ? 0 : this.values.getBaseStatValue(memberStatId, 0);

      const memberValue = this.tryComputeValue(member, memberStatId, memberStored) ?? 0;

      if (memberValue !== value) {
        return null;
      }
    }

    return { isPrimary: descriptor.statId === lowest };
  }

  private describeEntry(
    descriptor: StatDescriptor,
    layer: number,
    storedValue: number,
  ): ItemDescriptionLine | null {
    const value = this.tryComputeValue(descriptor, descriptor.statId, storedValue) ?? 0;

    const group = this.isGrouped(descriptor, value);
    const grouped = group !== null;

    if (group !== null && !group.isPrimary) {
      return null; // another member of the group prints on its behalf
    }

    const c: FormatContext = {
      func: grouped ? descriptor.descGrpFunc : descriptor.descFunc,
      descVal: grouped ? descriptor.descGrpVal : descriptor.descVal,
      strPos: grouped ? descriptor.descGrpStrPos : descriptor.descStrPos,
      rawStrPos: descriptor.descStrPos,
      strNeg: grouped ? descriptor.descGrpStrNeg : descriptor.descStrNeg,
      str2: grouped ? descriptor.descGrpStr2 : descriptor.descStr2,
      value,
      layer,
      text: null,
    };

    c.text = this.str(value < 0 ? c.strNeg : c.strPos);

    let text = this.format(c);
    if (text === null) {
      return null; // the engine returned 0: no row at all
    }

    text = this.appendDescStr2(text, c.func, c.str2);

    const line = new ItemDescriptionLine();
    line.text = text;
    line.statId = descriptor.statId;
    line.layer = layer;
    line.value = c.value;
    line.descPriority = descriptor.descPriority;
    line.isGroup = grouped;
    line.aggregated = grouped;

    // A DescGrp line prints ONE number for the whole group, so every member shares it and shares
    // its span. Naming them all lets the formatter see they agree and collapse to a single span
    // rather than repeating it four times.
    if (grouped) {
      const members = this.stats.getStatsInDescGroup(descriptor.descGrp);
      if (members.length !== 0) {
        line.shownStats = [...members];
      }
    }

    return line;
  }

  private appendDescStr2(text: string, func: number, str2: number): string {
    const wanted =
      func >= ItemDescFunc.PlusValueStringString2 &&
      (func <= ItemDescFunc.ValueFramesPercentStringString2 ||
        func === ItemDescFunc.NegatedValuePercentStringString2);

    if (!wanted) {
      return text;
    }

    const id = str2 === DescStringIds.DescStr2Sentinel ? DescStringIds.DescStr2Override : str2;
    return (
      text +
      ItemDescriptionGenerator.nz(this.str(DescStringIds.Space)) +
      ItemDescriptionGenerator.nz(this.str(id))
    );
  }

  private format(c: FormatContext): string | null {
    switch (c.func) {
      case ItemDescFunc.PlusValueString:
      case ItemDescFunc.PlusValueStringString2:
        return this.place(c.descVal, this.signed(c.value), c.text, DescValFallback.Empty);

      case ItemDescFunc.PlusValueStringSuppressOne:
        return this.place(
          c.descVal,
          c.value > 0 && c.value <= 1 ? '' : this.signed(c.value),
          c.text,
          DescValFallback.Empty,
        );

      case ItemDescFunc.StaleNegated25:
      case ItemDescFunc.StaleNegated26: {
        const staleDigits = ItemDescriptionGenerator.number(c.value);
        // `neg` is int32: negating int.MinValue yields int.MinValue, not a positive.
        c.value = -c.value | 0;
        return this.place(
          c.descVal,
          (c.value > 0 ? ItemDescriptionGenerator.nz(this.str(DescStringIds.Plus)) : '') +
            staleDigits,
          c.text,
          DescValFallback.Empty,
        );
      }

      case ItemDescFunc.ValuePercentString:
      case ItemDescFunc.ValuePercentStringString2:
        return this.place(
          c.descVal,
          ItemDescriptionGenerator.number(c.value) + this.percent(),
          c.text,
          DescValFallback.StringOnly,
        );

      case ItemDescFunc.ValueString:
      case ItemDescFunc.ValueStringString2:
        return this.place(
          c.descVal,
          ItemDescriptionGenerator.number(c.value),
          c.text,
          DescValFallback.StringOnly,
        );

      case ItemDescFunc.PlusValuePercentString:
      case ItemDescFunc.PlusValuePercentStringString2:
        return this.place(
          c.descVal,
          this.signedIncludingZero(c.value) + this.percent(),
          c.text,
          DescValFallback.StringOnly,
        );

      case ItemDescFunc.ValueFramesPercentString:
      case ItemDescFunc.ValueFramesPercentStringString2:
        return this.place(
          c.descVal,
          ItemDescriptionGenerator.number(Int32.div(Int32.mul(100, c.value), 128)) + this.percent(),
          c.text,
          DescValFallback.StringOnly,
        );

      case ItemDescFunc.NegatedValuePercentString:
      case ItemDescFunc.NegatedValuePercentStringString2:
        // `neg` is int32: negating int.MinValue yields int.MinValue, not a positive.
        c.value = -c.value | 0;
        return this.place(
          c.descVal,
          this.signedIncludingZero(c.value) + this.percent(),
          c.text,
          DescValFallback.StringOnly,
        );

      case ItemDescFunc.RepairDurability:
        return this.formatRepair(c);

      case ItemDescFunc.ClassAllSkills:
        return this.formatClassAllSkills(c);

      case ItemDescFunc.SkillTab:
        return this.formatSkillTab(c);

      case ItemDescFunc.SkillOnEvent:
        return this.formatSkillOnEvent(c);

      case ItemDescFunc.SkillAura:
        return this.formatSkillAura(c);

      case ItemDescFunc.ValueStringByTime:
      case ItemDescFunc.ValuePercentStringByTime:
        return this.formatByTime(c);

      case ItemDescFunc.RawFormat:
        return TblFormat.formatBounded(c.text, TblFormat.DefaultMaxLength, c.value);

      case ItemDescFunc.MonsterTypeDamage:
        return this.formatMonsterType(c);

      case ItemDescFunc.MonsterDamage:
        return this.formatMonster(c);

      case ItemDescFunc.Charges:
        return this.formatCharges(c);

      case ItemDescFunc.SkillClassOnly:
        return this.formatSkillClassOnly(c);

      case ItemDescFunc.Skill:
        return this.formatSkill(c);

      default:
        return null; // 0x4e4eca: unknown func returns 0
    }
  }

  private formatRepair(c: FormatContext): string {
    if (c.value <= 0) {
      return TblFormat.formatBounded(
        this.str(DescStringIds.RepairSingleCount),
        TblFormat.ShortMaxLength,
        25,
      );
    }

    const seconds = Int32.div(2500, c.value);
    if (seconds > 30) {
      return TblFormat.formatBounded(
        this.str(DescStringIds.RepairCountAndSeconds),
        TblFormat.ShortMaxLength,
        1,
        Int32.div(seconds + 12, 25),
      );
    }

    return TblFormat.formatBounded(
      this.str(DescStringIds.RepairSingleCount),
      TblFormat.ShortMaxLength,
      1,
    );
  }

  private formatClassAllSkills(c: FormatContext): string | null {
    if (c.value === 0) {
      return null; // 0x4e51fc
    }

    if (this.classes === null || !this.classes.classExists(c.layer)) {
      return null; // 0x4e521a, missing charstats row
    }

    return this.place(
      c.descVal,
      this.signed(c.value),
      this.classes.getAllSkillsText(c.layer),
      DescValFallback.Empty,
    );
  }

  private formatSkillTab(c: FormatContext): string | null {
    const tabIndex = c.layer & 7;
    const classId = c.layer >> 3;

    if (this.classes === null || !this.classes.classExists(classId) || tabIndex > 2) {
      return null; // 0x4e528d / 0x4e5296
    }

    return (
      TblFormat.formatBounded(
        this.classes.getSkillTabText(classId, tabIndex),
        TblFormat.DefaultMaxLength,
        c.value,
      ) +
      ItemDescriptionGenerator.nz(this.str(DescStringIds.Space)) +
      ItemDescriptionGenerator.nz(this.classes.getClassOnlyText(classId))
    );
  }

  private formatSkillOnEvent(c: FormatContext): string | null {
    const skillId = c.layer >> this.stats.skillIdShift;
    const level = c.layer & ((1 << this.stats.skillIdShift) - 1);

    if (this.skills === null || skillId <= 0 || skillId >= this.skills.rowCount) {
      return null; // 0x4e52f2 / 0x4e52fe
    }

    return TblFormat.formatBounded(
      this.str(c.rawStrPos),
      TblFormat.DefaultMaxLength,
      c.value,
      0,
      level,
      this.skills.getSkillName(skillId),
    );
  }

  private formatSkillAura(c: FormatContext): string | null {
    const skillName = this.skills === null ? null : this.skills.getSkillName(c.layer);

    if (skillName === null) {
      return null; // 0x4e534c
    }

    return TblFormat.formatBounded(c.text, TblFormat.DefaultMaxLength, c.value, skillName);
  }

  private formatCharges(c: FormatContext): string {
    const skillId = c.layer >> this.stats.skillIdShift;
    const level = c.layer & ((1 << this.stats.skillIdShift) - 1);

    const skillName = this.skills === null ? null : this.skills.getSkillName(skillId);
    if (skillName === null) {
      return ''; // 0x4e567d returns 1 with an empty buffer
    }

    const space = ItemDescriptionGenerator.nz(this.str(DescStringIds.Space));

    let builder = '';
    builder += ItemDescriptionGenerator.nz(this.str(DescStringIds.Level));
    builder += space;
    builder += ItemDescriptionGenerator.number(level);
    builder += space;
    builder += skillName;
    builder += space;
    builder += TblFormat.formatBounded(
      c.text,
      TblFormat.ShortMaxLength,
      c.value & 0xff,
      c.value >> 8,
    );

    return builder;
  }

  private formatSkillClassOnly(c: FormatContext): string {
    const to = this.str(DescStringIds.To);
    if (to === null) {
      return ''; // 0x4e5780 tests the pointer
    }

    if (c.value === 0) {
      return ''; // 0x4e5788
    }

    const skills = this.skills;
    const skillName = skills === null ? null : skills.getSkillName(c.layer);
    if (skillName === null || skills === null) {
      return ''; // 0x4e57a3 tests the pointer
    }

    const space = ItemDescriptionGenerator.nz(this.str(DescStringIds.Space));
    const head = this.signed(c.value) + space + to + space + skillName + space;

    const classId = skills.getSkillClass(c.layer);
    if (classId < 0 || classId > 6 || this.classes === null || !this.classes.classExists(classId)) {
      return head;
    }

    return head + ItemDescriptionGenerator.nz(this.classes.getClassOnlyText(classId));
  }

  private formatSkill(c: FormatContext): string {
    if (c.value === 0) {
      return ''; // 0x4e5843
    }

    if (this.skills === null || !this.skills.skillExists(c.layer)) {
      return ''; // 0x4e5858
    }

    const playerClass = this.values === null ? -1 : this.values.playerClass;
    if (this.skills.getSkillClass(c.layer) === playerClass && c.value > 3) {
      c.value = 3;
    }

    const to = this.str(DescStringIds.To);
    if (to === null) {
      return ''; // 0x4e589f tests the pointer
    }

    const skillName = this.skills.getSkillName(c.layer);
    if (skillName === null) {
      return ''; // 0x4e58ba tests the pointer
    }

    const space = ItemDescriptionGenerator.nz(this.str(DescStringIds.Space));
    return this.signed(c.value) + space + to + space + skillName;
  }

  private formatByTime(c: FormatContext): string {
    const packed = ByTimeValue.unpack(c.value);

    const degrees = this.time === null ? null : this.time.getTimeAngle();

    const adjusted = degrees !== null ? packed.interpolate(degrees) : packed.low;

    let builder = '';
    builder += ItemDescriptionGenerator.nz(this.str(PeriodOfDay[packed.period] ?? 0));
    builder += ItemDescriptionGenerator.nz(this.str(DescStringIds.Newline));

    let num: string;
    if (adjusted >= 0) {
      num =
        ItemDescriptionGenerator.nz(this.str(DescStringIds.Plus)) +
        ItemDescriptionGenerator.number(adjusted);
    } else if (c.value < 0) {
      num = ItemDescriptionGenerator.number(adjusted);
    } else {
      num = '';
    }

    if (c.func === ItemDescFunc.ValuePercentStringByTime) {
      num += this.percent();
    }

    c.value = adjusted;
    builder += ItemDescriptionGenerator.nz(
      this.place(c.descVal, num, c.text, DescValFallback.Empty),
    );
    return builder;
  }

  private formatMonsterType(c: FormatContext): string | null {
    const head = this.place(
      c.descVal,
      this.signedIncludingZero(c.value) + this.percent(),
      c.text,
      DescValFallback.StringOnly,
    );

    if (this.monsters === null || !this.monsters.monsterTypeExists(c.layer)) {
      return head;
    }

    return (
      ItemDescriptionGenerator.nz(head) +
      ItemDescriptionGenerator.nz(this.str(DescStringIds.Colon)) +
      ItemDescriptionGenerator.nz(this.str(DescStringIds.Space)) +
      ItemDescriptionGenerator.nz(this.monsters.getMonsterTypeName(c.layer))
    );
  }

  private formatMonster(c: FormatContext): string | null {
    if (this.monsters === null || !this.monsters.monsterExists(c.layer)) {
      return null;
    }

    const head = this.place(
      c.descVal,
      ItemDescriptionGenerator.number(c.value) + this.percent(),
      c.text,
      DescValFallback.StringOnly,
    );

    return (
      ItemDescriptionGenerator.nz(head) +
      ItemDescriptionGenerator.nz(this.str(DescStringIds.Space)) +
      ItemDescriptionGenerator.nz(this.monsters.getMonsterName(c.layer))
    );
  }

  private place(
    descVal: number,
    num: string,
    text: string | null,
    fallback: DescValFallback,
  ): string | null {
    if (descVal === 1) {
      return (
        num +
        ItemDescriptionGenerator.nz(this.str(DescStringIds.Space)) +
        ItemDescriptionGenerator.nz(text)
      );
    }

    if (descVal === 2) {
      return (
        ItemDescriptionGenerator.nz(text) +
        ItemDescriptionGenerator.nz(this.str(DescStringIds.Space)) +
        num
      );
    }

    return fallback === DescValFallback.StringOnly ? text : '';
  }

  private static number(value: number): string {
    return TblFormat.formatNumber(value);
  }

  private signed(value: number): string {
    return value > 0
      ? ItemDescriptionGenerator.nz(this.str(DescStringIds.Plus)) +
          ItemDescriptionGenerator.number(value)
      : ItemDescriptionGenerator.number(value);
  }

  private signedIncludingZero(value: number): string {
    return value >= 0
      ? ItemDescriptionGenerator.nz(this.str(DescStringIds.Plus)) +
          ItemDescriptionGenerator.number(value)
      : ItemDescriptionGenerator.number(value);
  }

  private percent(): string {
    return ItemDescriptionGenerator.nz(this.str(DescStringIds.Percent));
  }

  private str(index: number): string | null {
    return this.strings.getByIndex(index);
  }

  private static nz(text: string | null): string {
    return text ?? '';
  }
}

export class TblFormat {
  // 8, not 9: the engine printf-formats with width 10 then converts with a limit of 9
  // (0x4e4e55 / 0x4e4e65), and UTF8_ConvertToWideChar decrements that limit first (0x52634f).
  static readonly MaxNumberChars = 8;

  static formatNumber(value: number): string {
    const text = String(value);
    return text.length > TblFormat.MaxNumberChars
      ? text.substring(0, TblFormat.MaxNumberChars)
      : text;
  }

  static readonly DefaultMaxLength = 0x100;

  static readonly ShortMaxLength = 0x80;

  static format(format: string | null | undefined, ...args: unknown[]): string {
    return TblFormat.formatBounded(format, TblFormat.DefaultMaxLength, ...args);
  }

  // UNICODE_FormatWideString (0x5269d0). Survivors are maxLength - 1, because the last slot is
  // overwritten with NUL (0x526bda). The budget is re-tested ABOVE the specifier jump table
  // (0x526a6d dominates 0x526a99), so once it is spent an unrecognised specifier truncates
  // instead of reaching the halt at 0x526c66. Specifier set is exactly NUL, %, d, s, u.
  static formatBounded(
    format: string | null | undefined,
    maxLength: number,
    ...args: unknown[]
  ): string {
    if (format === null || format === undefined || format.length === 0) {
      return '';
    }

    let builder = '';
    let nextArg = 0;

    for (let i = 0; i < format.length; ++i) {
      const c = format[i] ?? '';

      if (c !== '%') {
        if (builder.length >= maxLength) {
          return TblFormat.truncate(builder, maxLength);
        }

        builder += c;
        continue;
      }

      if (builder.length >= maxLength) {
        return TblFormat.truncate(builder, maxLength);
      }

      if (i + 1 >= format.length) {
        builder += c;
        return builder;
      }

      const spec = format[i + 1] ?? '';
      ++i;

      if (spec === '%') {
        builder += '%';
        if (nextArg < args.length) {
          ++nextArg;
        }

        continue;
      }

      if (spec !== 'd' && spec !== 'u' && spec !== 's') {
        throw new Error(
          "Unsupported format specifier '%" + spec + "'. The game halts on this (0x526c66).",
        );
      }

      if (nextArg >= args.length) {
        builder += '%';
        builder += spec;
        continue;
      }

      const arg = args[nextArg++];
      const room = maxLength - builder.length - 1;

      if (spec === 's') {
        const text = typeof arg === 'string' ? arg : null;

        if (text === null) {
          if (room === 0) {
            return TblFormat.truncate(builder, maxLength);
          }

          throw new Error('A %s argument was null. The game dereferences it (0x526761).');
        }

        if (text.length === 0) {
          return TblFormat.truncate(builder, maxLength);
        }

        if (text.length >= room) {
          if (room > 0) {
            builder += text.substring(0, room);
          }

          return TblFormat.truncate(builder, maxLength);
        }

        builder += text;
        continue;
      }

      const num = spec === 'u' ? TblFormat.unsigned(arg) : TblFormat.signed(arg);
      if (num.length >= room) {
        return TblFormat.truncate(builder, maxLength);
      }

      builder += num;
    }

    return TblFormat.truncate(builder, maxLength);
  }

  private static truncate(builder: string, maxLength: number): string {
    const cap = maxLength - 1;
    if (cap >= 0 && builder.length > cap) {
      return builder.substring(0, cap);
    }

    return builder;
  }

  private static signed(arg: unknown): string {
    if (arg === null || arg === undefined) {
      return '0';
    }

    if (typeof arg === 'number') {
      return String(arg);
    }

    return String(arg);
  }

  private static unsigned(arg: unknown): string {
    if (arg === null || arg === undefined) {
      return '0';
    }

    if (typeof arg === 'number') {
      return String(arg >>> 0);
    }

    return String(arg);
  }
}
