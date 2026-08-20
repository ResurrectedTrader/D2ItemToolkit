import type { TxtFile } from '../Data/TxtFile.js';
import type { D2DataFiles, TxtSkillTable } from './TxtDataProviders.js';
import type { ItemViewer } from '../Stats/ItemRecord.js';

/** The `{ table, row }` pair C# returns through `out TxtFile table, out int row`. */
export interface MagicAffixLocation {
  table: TxtFile;
  row: number;
}

/**
 * TXT_magicaffixes_GetLine 0x633ee0. The three affix files are compiled into ONE array in the
 * order [MagicSuffix][MagicPrefix][automagic] and addressed 1-based, so id 1 is the first
 * SUFFIX row and an id past the suffix count spills into the prefixes.
 */
export class MagicAffixTable {
  static readonly NoClass = 0xff;

  private readonly tables: readonly (TxtFile | null)[];
  private readonly skills: TxtSkillTable | null;

  constructor(data: D2DataFiles) {
    this.tables = [data.magicSuffix, data.magicPrefix, data.autoMagic];
    this.skills = data.skills;
  }

  /**
   * How many 1-based affix ids `tryResolve` will accept — the CONCATENATED length of
   * [MagicSuffix][MagicPrefix][automagic], which is the array the game indexes. Iterate `1..rowCount`
   * inclusive, since 0 is "no affix".
   */
  get rowCount(): number {
    let total = 0;
    for (const table of this.tables) {
      if (table !== null) {
        total += table.rowCount;
      }
    }

    return total;
  }

  tryResolve(id: number): MagicAffixLocation | null {
    if (id <= 0) {
      return null;
    }

    let at = id - 1;

    for (const candidate of this.tables) {
      if (candidate === null || candidate === undefined) {
        continue;
      }

      if (at < candidate.rowCount) {
        return { table: candidate, row: at };
      }

      at -= candidate.rowCount;
    }

    return null;
  }

  /**
   * ITEMS_nullsub 0x628830 — despite the name, the level-requirement fold. Takes the running
   * maximum and raises it to this affix's requirement, preferring classlevelreq when the
   * affix is restricted to the viewer's own class.
   */
  raiseLevelRequirement(running: number, id: number, viewer: ItemViewer | null): number {
    const at = this.tryResolve(id);
    if (at === null) {
      return running;
    }

    // nClass is 0xFF when the affix has no class restriction; the compiler writes that for a
    // blank "class" cell, so a missing column reads as unrestricted here.
    const restrictedTo = MagicAffixTable.classCode(at.table, at.row, this.skills);

    const required =
      restrictedTo !== MagicAffixTable.NoClass &&
      viewer !== null &&
      viewer !== undefined &&
      restrictedTo === viewer.classId
        ? at.table.getInt(at.row, 'classlevelreq')
        : at.table.getInt(at.row, 'levelreq');

    return running <= required ? required : running;
  }

  private static classCode(table: TxtFile, row: number, skills: TxtSkillTable | null): number {
    if (skills === null || skills === undefined || !table.hasColumn('class')) {
      return MagicAffixTable.NoClass;
    }

    const code = table.getString(row, 'class');
    if (code.length === 0) {
      return MagicAffixTable.NoClass;
    }

    const classId = skills.classIdForCode(code);
    return classId < 0 ? MagicAffixTable.NoClass : classId;
  }
}
