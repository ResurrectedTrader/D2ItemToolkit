import { ItemStatReader } from '../../src/D2ItemToolkit.Ts/src/Stats/ItemStatReader.js';
import {
  DescStringIds,
  type ICharacterClassTable,
  type IGameTimeProvider,
  type IItemStatCostTable,
  type IMonsterTypeTable,
  type ISkillTable,
  type IStatValueSource,
  type IStringTable,
  type StatDescriptor,
} from '../../src/D2ItemToolkit.Ts/src/Types.js';

export class FakeStringTable implements IStringTable {
  readonly entries = new Map<number, string>();

  add(index: number, text: string): FakeStringTable {
    this.entries.set(index, text);
    return this;
  }

  /**
   * The punctuation the engine pulls from the .tbl rather than hardcoding.
   * Without these every line comes out empty, which is itself worth a test.
   */
  withPunctuation(): FakeStringTable {
    return this.add(DescStringIds.Space, ' ')
      .add(DescStringIds.Colon, ' to')
      .add(DescStringIds.Newline, '\n')
      .add(DescStringIds.ListComma, '\n')
      .add(DescStringIds.Percent, '%')
      .add(DescStringIds.Plus, '+')
      .add(DescStringIds.To, 'to');
  }

  getByIndex(index: number): string | null {
    return this.entries.get(index) ?? null;
  }
}

export class FakeStatCostTable implements IItemStatCostTable {
  readonly stats = new Map<number, StatDescriptor>();
  readonly order: number[] = [];
  readonly groups = new Map<number, readonly number[]>();
  readonly missing = new Set<number>();

  skillIdShift = 6;

  /** 0x4e4c76 returns 0 when a wOpBase reaches or exceeds this. */
  rowCount = 512;

  add(descriptor: StatDescriptor): FakeStatCostTable {
    this.stats.set(descriptor.statId, descriptor);
    this.order.push(descriptor.statId);
    return this;
  }

  addGroup(descGrp: number, ...statIds: number[]): FakeStatCostTable {
    this.groups.set(descGrp, statIds.slice());
    return this;
  }

  /** A stat id in the priority order with no backing row. */
  addMissing(statId: number): FakeStatCostTable {
    this.missing.add(statId);
    this.order.push(statId);
    return this;
  }

  tryGetStat(statId: number): StatDescriptor | null {
    if (this.missing.has(statId)) {
      return null;
    }

    return this.stats.get(statId) ?? null;
  }

  get statIdsByDescPriority(): readonly number[] {
    return this.order;
  }

  // The C# returns null for an unknown group; the engine's own guard tests null OR an empty
  // list, and Types.ts declares the return non-nullable, so an unknown group comes back empty.
  getStatsInDescGroup(descGrp: number): readonly number[] {
    return this.groups.get(descGrp) ?? [];
  }
}

export class FakeStatValues implements IStatValueSource {
  readonly baseStats = new Map<number, number>();
  readonly playerStats = new Map<number, number>();

  readonly itemTypes = new Set<number>();

  playerClass = -1;

  addItemType(itemTypeId: number): FakeStatValues {
    this.itemTypes.add(itemTypeId);
    return this;
  }

  isItemOfType(itemTypeId: number): boolean {
    return this.itemTypes.has(itemTypeId);
  }

  addBase(statId: number, value: number): FakeStatValues {
    this.baseStats.set(statId, value);
    return this;
  }

  addPlayer(statId: number, value: number): FakeStatValues {
    this.playerStats.set(statId, value);
    return this;
  }

  getBaseStatValue(statId: number, _layer: number): number {
    return this.baseStats.get(statId) ?? 0;
  }

  readonly itemStats = new Map<number, number>();

  addItemStat(statId: number, value: number): FakeStatValues {
    this.itemStats.set(statId, value);
    return this;
  }

  describedUnitIsItem = false;

  itemTableAllowsDurability = false;

  /** Distinct from GetItemStatValue(73): see the interface doc. */
  txtMaxDurability = 0;

  getTxtMaxDurability(): number {
    return this.txtMaxDurability;
  }

  getItemStatValue(statId: number): number {
    return this.itemStats.get(statId) ?? 0;
  }

  getPlayerStatValue(statId: number): number {
    return this.playerStats.get(statId) ?? 0;
  }
}

export class FakeSkillTable implements ISkillTable {
  readonly names = new Map<number, string>();
  readonly classes = new Map<number, number>();

  rowCount = 400;

  add(skillId: number, name: string, classId = -1): FakeSkillTable {
    this.names.set(skillId, name);
    this.classes.set(skillId, classId);
    return this;
  }

  skillExists(skillId: number): boolean {
    return this.names.has(skillId);
  }

  getSkillName(skillId: number): string | null {
    return this.names.get(skillId) ?? null;
  }

  getSkillClass(skillId: number): number {
    return this.classes.get(skillId) ?? -1;
  }
}

export class FakeClassTable implements ICharacterClassTable {
  readonly allSkills = new Map<number, string>();
  readonly tabs = new Map<number, string>();
  readonly classOnly = new Map<number, string>();

  addAllSkills(classId: number, text: string): FakeClassTable {
    this.allSkills.set(classId, text);
    return this;
  }

  addTab(classId: number, tabIndex: number, text: string): FakeClassTable {
    this.tabs.set(classId * 4 + tabIndex, text);
    return this;
  }

  addClassOnly(classId: number, text: string): FakeClassTable {
    this.classOnly.set(classId, text);
    return this;
  }

  getAllSkillsText(classId: number): string | null {
    return this.allSkills.get(classId) ?? null;
  }

  getSkillTabText(classId: number, tabIndex: number): string | null {
    return this.tabs.get(classId * 4 + tabIndex) ?? null;
  }

  getClassOnlyText(classId: number): string | null {
    return this.classOnly.get(classId) ?? null;
  }

  /** A class counts as present once it has any charstats string. */
  classExists(classId: number): boolean {
    return (
      this.allSkills.has(classId) ||
      this.classOnly.has(classId) ||
      this.tabs.has(classId * 4) ||
      this.tabs.has(classId * 4 + 1) ||
      this.tabs.has(classId * 4 + 2)
    );
  }
}

export class FakeMonsterTable implements IMonsterTypeTable {
  readonly types = new Map<number, string>();
  readonly monsters = new Map<number, string>();

  addType(id: number, name: string): FakeMonsterTable {
    this.types.set(id, name);
    return this;
  }

  addMonster(id: number, name: string): FakeMonsterTable {
    this.monsters.set(id, name);
    return this;
  }

  monsterTypeExists(monsterTypeId: number): boolean {
    return this.types.has(monsterTypeId);
  }

  monsterExists(monsterId: number): boolean {
    return this.monsters.has(monsterId);
  }

  getMonsterTypeName(monsterTypeId: number): string | null {
    return this.types.get(monsterTypeId) ?? null;
  }

  getMonsterName(monsterId: number): string | null {
    return this.monsters.get(monsterId) ?? null;
  }
}

export class FakeGameTime implements IGameTimeProvider {
  hasTime = true;
  degrees = 0;

  getTimeAngle(): number | null {
    return this.hasTime ? this.degrees : null;
  }
}

/** Packs a by-time stat value the way the game stores it. */
export const ByTime = {
  pack(period: number, low: number, high: number): number {
    return (period & 3) | (((low + 256) & 0x3ff) << 2) | (((high + 256) & 0x3ff) << 12);
  },
} as const;

export interface StatOptions {
  descVal?: number;
  priority?: number;
  strNeg?: number;
  str2?: number;
  valShift?: number;
}

export const Build = {
  /** The zeroed DTO C#'s `new StatDescriptor()` produces. */
  emptyStat(): StatDescriptor {
    return {
      statId: 0,
      descPriority: 0,
      descFunc: 0,
      descVal: 0,
      descStrPos: 0,
      descStrNeg: 0,
      descStr2: 0,
      descGrp: 0,
      descGrpFunc: 0,
      descGrpVal: 0,
      descGrpStrPos: 0,
      descGrpStrNeg: 0,
      descGrpStr2: 0,
      valShift: 0,
      op: 0,
      opParam: 0,
      opBase: 0,
    };
  },

  stat(
    statId: number,
    descFunc: number,
    strPos: number,
    options: StatOptions = {},
  ): StatDescriptor {
    const descriptor = Build.emptyStat();
    descriptor.statId = statId;
    descriptor.descFunc = descFunc;
    descriptor.descVal = options.descVal ?? 1;
    descriptor.descStrPos = strPos;
    descriptor.descStrNeg = options.strNeg ?? 0;
    descriptor.descStr2 = options.str2 ?? 0;
    descriptor.descPriority = options.priority ?? 0;
    descriptor.valShift = options.valShift ?? 0;
    return descriptor;
  },

  /** The packed (layer, stat) key ReconstructView produces. */
  entry(statId: number, value: number, layer = 0): readonly [number, number] {
    return [ItemStatReader.packStatKey(layer, statId), value];
  },
} as const;
