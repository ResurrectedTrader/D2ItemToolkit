import type { ItemRow } from './TableRows.js';
import type { TxtFile } from '../Data/TxtFile.js';

/** The `{ file, row }` pair C# returns through `out TxtFile file, out int row`. */
export interface ItemTableRow {
  file: TxtFile;
  row: number;
}

// The one table dwClassId indexes. TXT_AllocTxt_items compiles weapons (0x633351), then armor
// (0x63336d), then misc (0x63338c) and sums the three counts at 0x6333ab — so the order is
// weapons, armor, misc, NOT armor first.
//
// The three files do not share a schema (166 / 164 / 168 columns), so every read is by column
// NAME and an absent column yields the loader's default rather than a shifted value.
/**
 * The three item tiers. Elite is checked before Exceptional because a family whose three codes
 * are not distinct would otherwise report the lower tier.
 */
export enum ItemTier {
  Normal,
  Exceptional,
  Elite,
}

export class ItemTable {
  private readonly files: readonly (TxtFile | null)[];
  private readonly firstId: number[];
  private readonly total: number;

  constructor(weapons: TxtFile | null, armor: TxtFile | null, misc: TxtFile | null) {
    this.files = [weapons, armor, misc];
    this.firstId = new Array<number>(this.files.length).fill(0);

    let next = 0;
    for (let i = 0; i < this.files.length; ++i) {
      this.firstId[i] = next;
      const file = this.files[i];
      next += file === null || file === undefined ? 0 : file.rowCount;
    }

    this.total = next;
  }

  get rowCount(): number {
    return this.total;
  }

  // 0x6335fc: out of range returns nothing rather than clamping.
  tryResolve(classId: number): ItemTableRow | null {
    if (classId < 0 || classId >= this.total) {
      return null;
    }

    for (let i = this.files.length - 1; i >= 0; --i) {
      const file = this.files[i];
      const first = this.firstId[i] ?? 0;
      if (file !== null && file !== undefined && classId >= first) {
        return { file, row: classId - first };
      }
    }

    return null;
  }

  getString(classId: number, column: string): string {
    const at = this.tryResolve(classId);
    return at === null ? '' : at.file.getString(at.row, column);
  }

  getInt(classId: number, column: string): number {
    const at = this.tryResolve(classId);
    return at === null ? 0 : at.file.getInt(at.row, column);
  }

  /** The whole row, or null when `classId` is out of range. */
  rowAt(classId: number): ItemRow | null {
    if (classId < 0 || classId >= this.rowCount) {
      return null;
    }

    return {
      classId,
      code: this.code(classId),
      tier: this.tier(classId),
      requiredLevel: this.requiredLevel(classId),
      primaryTypeCode: this.primaryTypeCode(classId),
      secondaryTypeCode: this.secondaryTypeCode(classId),
    };
  }

  code(classId: number): string {
    return this.getString(classId, 'code');
  }

  /**
   * Which of the three tiers an item is, by matching its own `code` against the `normcode` /
   * `ubercode` / `ultracode` triple that names its family.
   *
   * NOT TRACED. Every other derivation in this library models a function in the 1.14d binary;
   * this one is a convenience over the shipped columns. It agrees with the data — armor splits
   * exactly 68/67/67 across 202 rows — but no disassembly backs the rule.
   *
   * Normal is the fallback, so the 153 rows that match nothing come back Normal rather than
   * throwing: all 151 misc rows (misc.txt has no such columns at all) plus Khalim's Flail `qf1`
   * and Khalim's Will `qf2`, whose normcode is `fla` and whose uber/ultra cells are empty.
   */
  tier(classId: number): ItemTier {
    const code = this.code(classId).trim();
    if (code.length === 0) {
      return ItemTier.Normal;
    }

    if (this.matches(classId, 'ultracode', code)) {
      return ItemTier.Elite;
    }

    return this.matches(classId, 'ubercode', code) ? ItemTier.Exceptional : ItemTier.Normal;
  }

  private matches(classId: number, column: string, code: string): boolean {
    return this.getString(classId, column).trim().toLowerCase() === code.toLowerCase();
  }

  requiredLevel(classId: number): number {
    return this.getInt(classId, 'levelreq');
  }

  // items.txt `type` and `type2`, the two codes IsOfType probes.
  primaryTypeCode(classId: number): string {
    return this.getString(classId, 'type');
  }

  secondaryTypeCode(classId: number): string {
    return this.getString(classId, 'type2');
  }

  classIdForCode(code: string | null | undefined): number {
    if (code === null || code === undefined || code.length === 0) {
      return -1;
    }

    // OrdinalIgnoreCase, matching the C# comparison.
    const wanted = code.toLowerCase();
    for (let classId = 0; classId < this.total; ++classId) {
      if (this.code(classId).toLowerCase() === wanted) {
        return classId;
      }
    }

    return -1;
  }
}
