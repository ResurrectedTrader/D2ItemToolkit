import { AnimDataFile } from '../Data/AnimDataFile.js';
import { embeddedFiles, embeddedSource, hasEmbeddedData } from '../Data/EmbeddedData.js';
import { TblFile, TblStringTable } from '../Data/TblFile.js';
import { TxtFile } from '../Data/TxtFile.js';
import { crtQsort } from './CrtQsort.js';
import type { CharacterClassRow, MonsterRow, MonsterTypeRow, SkillRow } from './TableRows.js';
import {
  DescStringIds,
  type ICharacterClassTable,
  type IItemStatCostTable,
  type IItemStatOpTable,
  type IMonsterTypeTable,
  type ISkillTable,
  type ItemStatOpEntry,
  type StatDescriptor,
} from '../Types.js';
// Aliased because `load`'s own parameters are named excelDirectory/localeDirectory/globalDirectory,
// matching the C# signature, and would otherwise shadow these.
import {
  type ByteSource,
  directorySource,
  excelDirectory as defaultExcelDirectory,
  globalDirectory as defaultGlobalDirectory,
  listDirectory,
  localeDirectory as defaultLocaleDirectory,
} from '../Data/TxtDataSource.js';

export class D2DataFiles {
  readonly strings: TblStringTable;
  readonly itemStatCost: TxtItemStatCostTable;
  readonly skills: TxtSkillTable;
  readonly classes: TxtCharacterClassTable;
  readonly monsterTypes: TxtMonsterTypeTable;
  readonly itemTypes: TxtFile | null;

  // colors.txt. The ROW INDEX is the palette-shift value everything else stores; our .txt copies
  // still hold the 4-char `code`, so ColorTable turns one into the other.
  readonly colors: TxtFile | null;
  readonly weapons: TxtFile | null;
  readonly armor: TxtFile | null;
  readonly misc: TxtFile | null;
  readonly uniqueItems: TxtFile | null;
  readonly setItems: TxtFile | null;

  // setitems.txt on its own is only half the set data: the piece list, the set name and the
  // full-set properties all live here, and TXT_AllocTxt_setitems links the two at 0x63668d.
  readonly sets: TxtFile | null;

  // The runtime affix arrays are CONCATENATIONS, and 1-based:
  //   magic = [MagicSuffix][MagicPrefix][automagic]   stride 144, TXT_magicaffixes_GetLine 0x633ee0
  //   rare  = [RareSuffix][RarePrefix]                stride  72, TXT_RareAffixes_GetLine 0x634260
  readonly magicSuffix: TxtFile | null;
  readonly magicPrefix: TxtFile | null;
  readonly autoMagic: TxtFile | null;
  readonly rareSuffix: TxtFile | null;
  readonly rarePrefix: TxtFile | null;
  readonly lowQualityItems: TxtFile | null;

  /**
   * The superior counterpart of lowqualityitems.txt. Unlike that file it carries the rolled
   * ranges, so a superior item's modifiers can be attributed to a row.
   */
  readonly qualityItems: TxtFile | null;
  readonly charStats: TxtFile | null;
  readonly gems: TxtFile | null;
  readonly runes: TxtFile | null;

  /**
   * Holds the fixed mods a crafted recipe adds on top of its random affixes, with their ranges —
   * the only place those ranges are recorded.
   */
  readonly cubeMain: TxtFile | null;
  readonly experience: TxtFile | null;
  readonly properties: TxtFile | null;
  readonly skillRows: TxtFile | null;
  readonly playerTypes: TxtFile | null;
  readonly playerModes: TxtFile | null;

  // The monster half of COMPOSIT_BuildCofPath 0x64f5b0, which a MERCENARY viewer takes.
  // monstats.txt is already read into monsterTypes for its NameStr; the animation name needs
  // `Code` (+16) and the `MonStatsEx` link (+24) off the same rows, so the raw file is kept too
  // rather than widening that table.
  readonly monsterStats: TxtFile | null;
  readonly monsterStats2: TxtFile | null;

  // TxtGetMonModeLine 0x65b500 selectors 0 and 1 both index this one table (0x65b19f and 0x65b1a4
  // assign the same pointer), unlike the player's, which is PlrType and PlrMode concatenated.
  readonly monsterModes: TxtFile | null;

  // The throwing-potion damage arm (0x485410) reads a missiles.txt record; EType is a
  // linker field over elemtypes.txt `code`, whose ROW INDEX is the stored value (0x612993).
  readonly missiles: TxtFile | null;
  readonly elementTypes: TxtFile | null;

  /** AnimData.D2's parser lives outside this slice, so the raw bytes are kept for it. */
  readonly animDataBytes: Uint8Array | null;

  private parsedAnimData: AnimDataFile | null | undefined;

  /** The C# builds this eagerly in the constructor; the bytes above are parsed on first read. */
  get animData(): AnimDataFile | null {
    if (this.parsedAnimData === undefined) {
      this.parsedAnimData =
        this.animDataBytes === null ? null : AnimDataFile.parse(this.animDataBytes);
    }

    return this.parsedAnimData;
  }

  static load(): D2DataFiles;
  static load(
    excelDirectory: string,
    localeDirectory: string,
    globalDirectory?: string | null,
  ): D2DataFiles;
  static load(
    excelDirectory?: string,
    localeDirectory?: string,
    globalDirectory: string | null = null,
  ): D2DataFiles {
    if (excelDirectory === undefined && localeDirectory === undefined) {
      // The embedded archive is the C# `LoadEmbedded` equivalent and needs no filesystem, so it
      // works from a published install and in a browser. The directory fallback only matters if
      // the archive was never generated.
      if (hasEmbeddedData()) {
        return D2DataFiles.build(
          embeddedSource('excel'),
          embeddedSource('locale/eng'),
          embeddedSource('global'),
        );
      }

      return D2DataFiles.build(
        directorySource(defaultExcelDirectory()),
        directorySource(defaultLocaleDirectory()),
        directorySource(defaultGlobalDirectory()),
      );
    }

    if (excelDirectory === undefined) throw new Error('excelDirectory');
    if (localeDirectory === undefined) throw new Error('localeDirectory');

    return D2DataFiles.build(
      directorySource(excelDirectory),
      directorySource(localeDirectory),
      directorySource(globalDirectory),
    );
  }

  /** The names under `data/`, in the dotted form the C# assembly resources carry. */
  static get dataFileNames(): readonly string[] {
    const embedded = hasEmbeddedData();

    // The directory is resolved INSIDE the non-embedded branch. Passing it as an argument evaluated
    // it unconditionally, which in a browser means calling into the filesystem stub even though the
    // embedded answer was already available.
    const files = (tree: string, directory: () => string): readonly string[] =>
      embedded ? embeddedFiles(tree) : listDirectory(directory());

    const names: string[] = [];

    for (const name of files('excel', defaultExcelDirectory)) {
      names.push('excel.' + name);
    }

    for (const name of files('locale/eng', defaultLocaleDirectory)) {
      names.push('locale.eng.' + name);
    }

    for (const name of files('global', defaultGlobalDirectory)) {
      names.push('global.' + name);
    }

    return names;
  }

  static build(excel: ByteSource, locale: ByteSource, global: ByteSource): D2DataFiles {
    return new D2DataFiles(excel, locale, global);
  }

  private constructor(excel: ByteSource, locale: ByteSource, global: ByteSource) {
    const strings = new TblStringTable(
      parseTbl(locale('string.tbl')),
      parseTbl(locale('patchstring.tbl')),
      parseTbl(locale('expansionstring.tbl')),
    );

    this.strings = strings;
    this.itemStatCost = new TxtItemStatCostTable(required(excel, 'ItemStatCost.txt'), strings);
    this.skills = new TxtSkillTable(
      required(excel, 'skills.txt'),
      optional(excel, 'skilldesc.txt'),
      strings,
      optional(excel, 'PlayerClass.txt'),
    );
    this.classes = new TxtCharacterClassTable(required(excel, 'charstats.txt'), strings);
    this.monsterTypes = new TxtMonsterTypeTable(
      optional(excel, 'MonType.txt'),
      optional(excel, 'monstats.txt'),
      strings,
    );
    this.itemTypes = optional(excel, 'ItemTypes.txt');
    this.weapons = optional(excel, 'weapons.txt');
    this.armor = optional(excel, 'armor.txt');
    this.misc = optional(excel, 'misc.txt');
    this.uniqueItems = optional(excel, 'UniqueItems.txt');
    this.setItems = optional(excel, 'SetItems.txt');
    this.sets = optional(excel, 'sets.txt');
    this.magicSuffix = optional(excel, 'MagicSuffix.txt');
    this.magicPrefix = optional(excel, 'MagicPrefix.txt');
    this.autoMagic = optional(excel, 'automagic.txt');
    this.rareSuffix = optional(excel, 'RareSuffix.txt');
    this.rarePrefix = optional(excel, 'RarePrefix.txt');
    this.lowQualityItems = optional(excel, 'lowqualityitems.txt');
    this.qualityItems = optional(excel, 'qualityitems.txt');
    this.charStats = optional(excel, 'charstats.txt');
    this.gems = optional(excel, 'gems.txt');
    this.runes = optional(excel, 'Runes.txt');
    this.cubeMain = optional(excel, 'cubemain.txt');
    this.colors = optional(excel, 'colors.txt');
    this.experience = optional(excel, 'Experience.txt');
    this.properties = optional(excel, 'Properties.txt');
    this.skillRows = optional(excel, 'skills.txt');
    this.playerTypes = optional(excel, 'PlrType.txt');
    this.playerModes = optional(excel, 'PlrMode.txt');
    this.monsterStats = optional(excel, 'monstats.txt');
    this.monsterStats2 = optional(excel, 'monstats2.txt');
    this.monsterModes = optional(excel, 'MonMode.txt');
    this.missiles = optional(excel, 'Missiles.txt');
    this.elementTypes = optional(excel, 'ElemTypes.txt');

    this.animDataBytes = global('AnimData.D2');
  }
}

function parseTbl(bytes: Uint8Array | null): TblFile | null {
  return bytes === null ? null : TblFile.parse(bytes);
}

function optional(source: ByteSource, name: string): TxtFile | null {
  const bytes = source(name);
  return bytes === null ? null : TxtFile.load(bytes);
}

function required(source: ByteSource, name: string): TxtFile {
  const file = optional(source, name);
  if (file === null) {
    throw new Error('Required data file not found: ' + name);
  }

  return file;
}

export const TxtKeys = {
  // The loader DISTINGUISHES an absent column from a blank cell, and so must every provider:
  //   absent -> the defaults loop writes 0 (0x6bdfd4), so the engine resolves string.tbl[0];
  //   blank  -> the converter runs and DATATBLS_LookupStringId substitutes 5382 (0x6117c6).
  // Resolving unconditionally prints "an evil force" where the game prints Warriv gossip.
  id(file: TxtFile, row: number, column: string, strings: TblStringTable): number {
    return file.hasColumn(column) ? strings.resolveKey(file.getString(row, column)) : 0;
  },

  text(file: TxtFile, row: number, column: string, strings: TblStringTable): string | null {
    return strings.getByIndex(TxtKeys.id(file, row, column, strings));
  },
};

export class TxtItemStatCostTable implements IItemStatCostTable, IItemStatOpTable {
  private readonly _stats: StatDescriptor[];
  private readonly _byDescPriority: number[];
  private readonly _groups: Map<number, number[]>;
  private readonly _skillIdShift: number;

  // The row index IS the stat id, so a name lookup is how every other table's "stat" column
  // resolves (TXTFIELD_NAMETOWORD through pItemStatCostLinker).
  private readonly _byName = new Map<string, number>();

  private static readonly OpStatColumns = ['op stat1', 'op stat2', 'op stat3'];

  private readonly _opEntries: readonly ItemStatOpEntry[];

  get percentOfBaseEntries(): readonly ItemStatOpEntry[] {
    return this._opEntries;
  }

  statIdForName(name: string | null): number {
    if (name === null || name.length === 0) {
      return -1;
    }

    const id = this._byName.get(name.toLowerCase());
    return id === undefined ? -1 : id;
  }

  constructor(file: TxtFile, strings: TblStringTable) {
    for (let row = 0; row < file.rowCount; ++row) {
      const name = file.getString(row, 'Stat');
      const key = name.toLowerCase();
      if (name.length !== 0 && !this._byName.has(key)) {
        this._byName.set(key, row);
      }
    }

    // op 13 only. The other ops either cannot fire on an item's statlist (owner-type gates
    // at 0x626259 onward) or are unreachable with shipped data — 6/7 need act and
    // period-of-day and their only two users are unspawnable.
    const ops: ItemStatOpEntry[] = [];
    for (let row = 0; row < file.rowCount; ++row) {
      if (file.getInt(row, 'op') !== 13) {
        continue;
      }

      for (const column of TxtItemStatCostTable.OpStatColumns) {
        const target = file.getString(row, column);
        const targetRow = this._byName.get(target.toLowerCase());
        if (target.length !== 0 && targetRow !== undefined) {
          ops.push({ percentStat: row, targetStat: targetRow });
        }
      }
    }

    this._opEntries = ops;

    this._stats = new Array<StatDescriptor>(file.rowCount);

    for (let row = 0; row < file.rowCount; ++row) {
      // Each field is TRUNCATED to the width the loader stores it in, with no range check.
      // The widths bite: descpriority 40000 becomes int16 -25536 and sorts FIRST; descfunc
      // 256 becomes 0, so the row never enters the walked array at all (0x638530).
      // descval and dgrpval are NOT defaulted to 1 — no hook field (0x637f0c).
      const stat: StatDescriptor = {
        statId: row,

        descPriority: toInt16(file.getInt(row, 'descpriority')),
        descFunc: toByte(file.getInt(row, 'descfunc')),

        descVal: toByte(file.getInt(row, 'descval')),
        descGrpVal: toByte(file.getInt(row, 'dgrpval')),

        descStrPos: keyId(file, row, 'descstrpos', strings),
        descStrNeg: keyId(file, row, 'descstrneg', strings),
        descStr2: keyId(file, row, 'descstr2', strings),

        descGrp: toUInt16(file.getInt(row, 'dgrp')),
        descGrpFunc: toByte(file.getInt(row, 'dgrpfunc')),
        descGrpStrPos: keyId(file, row, 'dgrpstrpos', strings),
        descGrpStrNeg: keyId(file, row, 'dgrpstrneg', strings),
        descGrpStr2: keyId(file, row, 'dgrpstr2', strings),

        valShift: toByte(file.getInt(row, 'ValShift')),
        op: toByte(file.getInt(row, 'op')),
        opParam: toByte(file.getInt(row, 'op param')),
        opBase: resolveOpBase(file, row),
      };

      // Frozen because `tryGetStat` hands out the table's OWN instance, and the table lives for the
      // life of the process. Without this, a caller who mutated a descriptor permanently changed
      // how every later render described that stat, on a shared engine that never healed. Nothing
      // inside writes to a descriptor after this point — only reads and comparisons.
      //
      // The C# does the same thing differently, because it can: there the public accessor returns a
      // copy and the engine's own path keeps the live object via an internal interface. TypeScript
      // has no `internal`, so freezing is both cheaper (no per-lookup allocation) and stronger — a
      // stray write throws here rather than silently landing on a copy.
      this._stats[row] = Object.freeze(stat);
    }

    const described: StatDescriptor[] = [];
    for (const stat of this._stats) {
      if (stat.descFunc !== 0) {
        described.push(stat);
      }
    }

    // 0x63851c builds this array in ascending row order, then 0x638571 qsorts it. The comparator
    // has no tie-break, so the CRT's own permutation is part of the output and a stable or
    // differently-pivoting sort gives the wrong order within a tie group.
    crtQsort(described, comparePriorityOnly);

    this._byDescPriority = new Array<number>(described.length);
    for (let i = 0; i < described.length; ++i) {
      this._byDescPriority[i] = (described[i] as StatDescriptor).statId;
    }

    this._groups = buildGroups(this._stats);

    const stuff = file.getInt(0, 'stuff');
    this._skillIdShift = stuff >= 1 && stuff <= 8 ? stuff : 6;
  }

  /** The descriptor for a stat, or null when the id is out of range. */
  rowAt(statId: number): StatDescriptor | null {
    return this.tryGetStat(statId);
  }

  tryGetStat(statId: number): StatDescriptor | null {
    if (statId < 0 || statId >= this._stats.length) {
      return null;
    }

    return this._stats[statId] ?? null;
  }

  get rowCount(): number {
    return this._stats.length;
  }

  get statIdsByDescPriority(): readonly number[] {
    return this._byDescPriority;
  }

  get skillIdShift(): number {
    return this._skillIdShift;
  }

  getStatsInDescGroup(descGrp: number): readonly number[] {
    return this._groups.get(descGrp) ?? [];
  }
}

function keyId(file: TxtFile, row: number, column: string, strings: TblStringTable): number {
  return TxtKeys.id(file, row, column, strings);
}

// SORT_ItemDescPriority 0x6379d0 — a signed 16-bit compare of the priority word alone, returning
// -1/0/1. There is deliberately no tie-break here: ties fall out of crtQsort's permutation, which
// is what the game actually shows. Adding one (stat id, say) reorders 63 of the 207 entries and is
// visible on Call to Arms and Gheed's Fortune.
function comparePriorityOnly(a: StatDescriptor, b: StatDescriptor): number {
  return a.descPriority < b.descPriority ? -1 : a.descPriority > b.descPriority ? 1 : 0;
}

// Name lookup only; a miss gives 0xFFFF, which SKILLDESC_CalcStatGroupValue treats as
// out of range and bails on (0x4e4c76, unsigned).
function resolveOpBase(file: TxtFile, row: number): number {
  const UnresolvedOpBase = 0xffff;

  const text = file.getString(row, 'op base');
  if (text.length === 0) {
    return UnresolvedOpBase;
  }

  const found = file.findRow('Stat', text);
  return found >= 0 ? found : UnresolvedOpBase;
}

function buildGroups(stats: readonly StatDescriptor[]): Map<number, number[]> {
  const members = new Map<number, number[]>();
  for (const stat of stats) {
    if (stat.descGrp === 0) {
      continue;
    }

    let list = members.get(stat.descGrp);
    if (list === undefined) {
      list = [];
      members.set(stat.descGrp, list);
    }

    list.push(stat.statId);
  }

  return members;
}

function toInt16(value: number): number {
  return (value << 16) >> 16;
}

function toUInt16(value: number): number {
  return value & 0xffff;
}

function toByte(value: number): number {
  return value & 0xff;
}

export class TxtSkillTable implements ISkillTable {
  private readonly _names: (string | null)[];
  private readonly _classes: number[];
  private readonly _requiredLevels: number[];
  private readonly _maxLevels: number[];
  private readonly _sentinel: string | null;
  private readonly _classCodes: readonly string[];

  constructor(
    skills: TxtFile,
    skillDesc: TxtFile | null,
    strings: TblStringTable,
    playerClass: TxtFile | null = null,
  ) {
    this._classCodes = buildClassCodes(playerClass);

    this._names = new Array<string | null>(skills.rowCount).fill(null);
    this._classes = new Array<number>(skills.rowCount).fill(0);
    this._requiredLevels = new Array<number>(skills.rowCount).fill(0);

    const hasReqLevel = skills.hasColumn('reqlevel');
    for (let row = 0; row < skills.rowCount; ++row) {
      this._requiredLevels[row] = hasReqLevel ? skills.getInt(row, 'reqlevel') : 0;
    }

    this._maxLevels = new Array<number>(skills.rowCount).fill(0);
    const hasMaxLevel = skills.hasColumn('maxlvl');
    for (let row = 0; row < skills.rowCount; ++row) {
      this._maxLevels[row] = hasMaxLevel ? skills.getInt(row, 'maxlvl') : 0;
    }

    this._sentinel = strings.getByIndex(DescStringIds.DescStr2Sentinel);

    for (let row = 0; row < skills.rowCount; ++row) {
      this._classes[row] = this.resolveClass(skills.getString(row, 'charclass'));
      this._names[row] = this._sentinel;

      if (skillDesc === null) {
        continue;
      }

      const descKey = skills.getString(row, 'skilldesc');
      if (descKey.length === 0) {
        continue;
      }

      const descRow = skillDesc.findRow('skilldesc', descKey);
      if (descRow < 0) {
        continue;
      }

      const name = TxtKeys.text(skillDesc, descRow, 'str name', strings);

      if (name !== null) {
        this._names[row] = name;
      }
    }
  }

  // CASE-SENSITIVE over exactly four space-padded bytes: field type 0x0D copies at most 4
  // bytes and pads with 0x20 (0x6bdc62 onwards), then GetClassIdFromName compares the packed
  // value as a raw DWORD (0x6bd155). A miss is -1 (0x6bd168), which costs DescFunc 28 its
  // clamp and DescFunc 27 its "(Class Only)" suffix.
  // The playerclass Code -> class id mapping, exposed for callers that need to resolve a
  // class code from another table (ItemTypes `Class`, for one).
  classIdForCode(code: string | null): number {
    return this.resolveClass(code);
  }

  private resolveClass(code: string | null): number {
    const packed = packClassCode(code);
    for (let i = 0; i < this._classCodes.length; ++i) {
      if (packed === packClassCode(this._classCodes[i] ?? null)) {
        return i;
      }
    }

    return -1;
  }

  get rowCount(): number {
    return this._names.length;
  }

  /** The whole row, or null when the id is out of range. */
  rowAt(skillId: number): SkillRow | null {
    if (!this.skillExists(skillId)) {
      return null;
    }

    return {
      skillId,
      name: this.getSkillName(skillId),
      classId: this.getSkillClass(skillId),
      requiredLevel: this.requiredLevel(skillId),
    };
  }

  skillExists(skillId: number): boolean {
    return skillId >= 0 && skillId < this.rowCount;
  }

  getSkillName(skillId: number): string | null {
    return skillId >= 0 && skillId < this._names.length
      ? (this._names[skillId] ?? null)
      : this._sentinel;
  }

  getSkillClass(skillId: number): number {
    return skillId >= 0 && skillId < this._classes.length ? (this._classes[skillId] as number) : -1;
  }

  /**
   * skills.txt "reqlevel" (+0x174). Out-of-range ids return -1: the caller at 0x62b952 tests
   * the id against the record count and skips it, so a bad id contributes nothing.
   */
  requiredLevel(skillId: number): number {
    return skillId >= 0 && skillId < this._requiredLevels.length
      ? (this._requiredLevels[skillId] as number)
      : -1;
  }

  /**
   * SKILL_GetMaxLevelForSkill 0x4aa8b0 — skills.txt "maxlvl" (+0x12C), falling back to **20** both
   * when the column is non-positive and when the id is out of range (0x4aa8d9). The fallback is the
   * value, not an error code, so this never returns -1.
   */
  maxLevel(skillId: number): number {
    const fallback = 20;

    if (skillId < 0 || skillId >= this._maxLevels.length) {
      return fallback;
    }

    const value = this._maxLevels[skillId] as number;
    return value > 0 ? value : fallback;
  }
}

const StockClassCodes: readonly string[] = ['ama', 'sor', 'nec', 'pal', 'bar', 'dru', 'ass'];

function buildClassCodes(playerClass: TxtFile | null): readonly string[] {
  if (playerClass === null || !playerClass.hasColumn('Code')) {
    return StockClassCodes;
  }

  const codes = new Array<string>(playerClass.rowCount);
  for (let row = 0; row < codes.length; ++row) {
    codes[row] = playerClass.getString(row, 'Code');
  }

  return codes;
}

function packClassCode(code: string | null): string {
  if (code === null || code.length === 0) {
    return '    ';
  }

  return code.length >= 4 ? code.substring(0, 4) : code.padEnd(4, ' ');
}

export class TxtCharacterClassTable implements ICharacterClassTable {
  /** charstats.txt carries three tab-name columns; getSkillTabText bounds on this. */
  static readonly SkillTabsPerClass = 3;

  private readonly _allSkills: (string | null)[];
  private readonly _skillTabs: (string | null)[][];
  private readonly _classOnly: (string | null)[];

  /** charstats.txt rows, so a caller can iterate the classes. */
  get rowCount(): number {
    return this._allSkills.length;
  }

  constructor(file: TxtFile, strings: TblStringTable) {
    this._allSkills = new Array<string | null>(file.rowCount).fill(null);
    this._skillTabs = new Array<(string | null)[]>(file.rowCount);
    this._classOnly = new Array<string | null>(file.rowCount).fill(null);

    for (let row = 0; row < file.rowCount; ++row) {
      this._allSkills[row] = text(file, row, 'StrAllSkills', strings);
      this._classOnly[row] = text(file, row, 'StrClassOnly', strings);
      this._skillTabs[row] = [
        text(file, row, 'StrSkillTab1', strings),
        text(file, row, 'StrSkillTab2', strings),
        text(file, row, 'StrSkillTab3', strings),
      ];
    }
  }

  /** The whole row, or null when the id is out of range. */
  rowAt(classId: number): CharacterClassRow | null {
    if (!this.classExists(classId)) {
      return null;
    }

    const tabs: (string | null)[] = [];
    for (let tab = 0; tab < TxtCharacterClassTable.SkillTabsPerClass; ++tab) {
      tabs.push(this.getSkillTabText(classId, tab));
    }

    return {
      classId,
      allSkillsText: this.getAllSkillsText(classId),
      classOnlyText: this.getClassOnlyText(classId),
      skillTabTexts: tabs,
    };
  }

  classExists(classId: number): boolean {
    return classId >= 0 && classId < this._allSkills.length;
  }

  getAllSkillsText(classId: number): string | null {
    return classId >= 0 && classId < this._allSkills.length
      ? (this._allSkills[classId] ?? null)
      : null;
  }

  getSkillTabText(classId: number, tabIndex: number): string | null {
    if (classId < 0 || classId >= this._skillTabs.length || tabIndex < 0 || tabIndex > 2) {
      return null;
    }

    return (this._skillTabs[classId] as (string | null)[])[tabIndex] ?? null;
  }

  getClassOnlyText(classId: number): string | null {
    return classId >= 0 && classId < this._classOnly.length
      ? (this._classOnly[classId] ?? null)
      : null;
  }
}

function text(file: TxtFile, row: number, column: string, strings: TblStringTable): string | null {
  return TxtKeys.text(file, row, column, strings);
}

export class TxtMonsterTypeTable implements IMonsterTypeTable {
  private readonly _typeNames: (string | null)[];
  private readonly _monsterNames: (string | null)[];

  /** MonType.txt rows. */
  get monsterTypeCount(): number {
    return this._typeNames.length;
  }

  /** monstats.txt rows. */
  get monsterCount(): number {
    return this._monsterNames.length;
  }

  constructor(monType: TxtFile | null, monStats: TxtFile | null, strings: TblStringTable) {
    const typeRows = monType === null ? 0 : monType.rowCount;
    this._typeNames = new Array<string | null>(typeRows).fill(null);

    for (let row = 0; row < typeRows; ++row) {
      this._typeNames[row] = TxtKeys.text(monType as TxtFile, row, 'strplur', strings);
    }

    const monsterRows = monStats === null ? 0 : monStats.rowCount;
    this._monsterNames = new Array<string | null>(monsterRows).fill(null);

    for (let row = 0; row < monsterRows; ++row) {
      this._monsterNames[row] = TxtKeys.text(monStats as TxtFile, row, 'NameStr', strings);
    }
  }

  /** A MonType.txt row, or null when the id is out of range. */
  monsterTypeAt(monsterTypeId: number): MonsterTypeRow | null {
    return monsterTypeId < 0 || monsterTypeId >= this.monsterTypeCount
      ? null
      : { monsterTypeId, name: this.getMonsterTypeName(monsterTypeId) };
  }

  /** A monstats.txt row, or null when the id is out of range. */
  monsterAt(monsterId: number): MonsterRow | null {
    return monsterId < 0 || monsterId >= this.monsterCount
      ? null
      : { monsterId, name: this.getMonsterName(monsterId) };
  }

  monsterTypeExists(_monsterTypeId: number): boolean {
    return this._typeNames.length > 0;
  }

  getMonsterTypeName(monsterTypeId: number): string | null {
    if (this._typeNames.length === 0) {
      return null;
    }

    if (monsterTypeId < 0 || monsterTypeId >= this._typeNames.length) {
      return this._typeNames[0] ?? null;
    }

    return this._typeNames[monsterTypeId] ?? null;
  }

  // TXT_MonStats_GetLine does a plain range check, so this is one. It used to consult a boolean[]
  // that was set true for every row in range, which held no information.
  monsterExists(monsterId: number): boolean {
    return monsterId >= 0 && monsterId < this._monsterNames.length;
  }

  getMonsterName(monsterId: number): string | null {
    return monsterId >= 0 && monsterId < this._monsterNames.length
      ? (this._monsterNames[monsterId] ?? null)
      : null;
  }
}
