/**
 * Public surface of the TypeScript implementation, and deliberately small: the DTO you hand in,
 * the engine, and the result types. Everything that models the disassembly stays unexported —
 * naming it here would freeze a shape that exists to mirror the game rather than to be consumed.
 *
 * The C# assembly is sealed the same way, and the two are held together by the differential, so
 * anything added here has to be added there too.
 */

// The unit you hand in. An item and a player are the same struct in the game, so both are a
// Unit; a socket filler is another one nested in `items`.
export {
  MaxAffixSlots,
  UnitFlagExpansion,
  createUnit,
  unitFromJson,
  type Unit,
  type UnitSkill,
  type UnitStat,
  type UnitStatList,
} from './Stats/Unit.js';

// Constants you need to AUTHOR a record: which quality, which statlist flags, which state.
export { ItemRecordFlags } from './Stats/ItemRecord.js';
export { ItemStatListFlags, ItemStatListStates } from './Stats/ItemStatReader.js';
export { ItemQualityNo } from './Tooltip/ItemNameBuilder.js';

// The engine and what it gives back.
export {
  TooltipEngine,
  type ItemAppearance,
  type ItemRequirements,
  type Tooltip,
  type TooltipBreakdown,
  type TooltipOptions,
} from './Tooltip/TooltipEngine.js';

export {
  ItemTooltipColor,
  ItemTooltipKind,
  ItemTooltipSection,
  type ItemTooltipLine,
} from './Tooltip/ItemTooltip.js';

// What `TooltipEngine.ranges` gives back. `isPackedStat` is exported so a caller deciding which
// stats may be summed reads that rule from here rather than deriving its own.
export {
  RollSources,
  isPackedStat,
  type ItemRollRanges,
  type RolledLayerRange,
  type RolledStatRange,
} from './Stats/RolledRangeReconstructor.js';

// What `TooltipEngine.mergedStats` and `socketFillerStats` give back: an item's stats as TOTALS
// rather than as the statlist chain, which is the question a stored item answers.
export type { ItemMergedStats, MergedStat, MergedStatsOptions } from './Stats/MergedStats.js';

// The DTO for an identified set item. Only what the item document cannot say: which siblings the
// viewer holds, the two worn masks, whether the piece is equipped, and the full-set stat block.

// The game tables, for lookups this library does not do for you. Reachable from an engine as
// `engine.data` / `engine.items` / `engine.types`, or built directly from `D2DataFiles.load()`.
//
// The tables are public; the ENGINE is not. RecordSections, the composer and the description
// generator stay unexported, because those shapes exist to mirror the disassembly rather than to
// be consumed. The C# assembly is opened up exactly this far and no further.
//
// The two facades are kept in step deliberately — a divergence sweep found this file claiming
// parity it did not have, because `TooltipEngine.fromFiles`/`fromData` existed only in C#. If you
// add an entry point to one, add it to the other.
export { TxtFile } from './Data/TxtFile.js';
export { TblFile, TblStringTable } from './Data/TblFile.js';
export { AnimDataFile } from './Data/AnimDataFile.js';
export { D2DataFiles } from './Tables/TxtDataProviders.js';
export { ItemTable, ItemTier } from './Tables/ItemTable.js';
export { ItemTypeTree } from './Tables/ItemTypeTree.js';
export { ColorTable } from './Tables/ColorTable.js';
export { GemTable } from './Tables/GemTable.js';
export { MagicAffixTable } from './Tables/MagicAffixTable.js';
export { MissileTable } from './Tables/MissileTable.js';
export { SetTable, SetRecord, SetItemRecord } from './Tables/SetTable.js';
export { PropertiesTable } from './Tables/PropertiesTable.js';
export { SkillDamage } from './Tables/SkillDamage.js';

// Every table is walked as `rowCount` + `rowAt(index)`; these are what rowAt hands back. Peers of
// the C# records in TableRows.cs.
export type {
  CharacterClassRow,
  ColorRow,
  GemRow,
  ItemRow,
  ItemTypeRow,
  MonsterRow,
  MonsterTypeRow,
  SkillRow,
} from './Tables/TableRows.js';

// The four typed views D2DataFiles hands out. Reachable as values before this — `data.skills` and
// friends — but not NAMEABLE, so a consumer could use one and not write its type in a signature.
// All four are public in the C# assembly.
export {
  TxtCharacterClassTable,
  TxtItemStatCostTable,
  TxtMonsterTypeTable,
  TxtSkillTable,
} from './Tables/TxtDataProviders.js';

// Returned by PropertiesTable.rowAt / MissileTable.tryGetThrowDamage / SetTable's property
// accessors, and likewise unnameable until now.
export type { PropertiesTableRow } from './Tables/PropertiesTable.js';
export type { MissileThrowDamage } from './Tables/MissileTable.js';
export type { ItemTableRow } from './Tables/ItemTable.js';
export type { MagicAffixLocation } from './Tables/MagicAffixTable.js';
export type { SkillDamageRange } from './Tables/SkillDamage.js';
export type { AnimDataRecord } from './Data/AnimDataFile.js';
export type { ByteSource } from './Data/TxtDataSource.js';
