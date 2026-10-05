import type { TxtFile } from '../Data/TxtFile.js';

/** What the linker reads off the data; `D2DataFiles` satisfies it. */
export interface ItemParamLinkerSource {
  readonly isResurrected: boolean;
  readonly skillRows: TxtFile | null;
  readonly monTypeRows: TxtFile | null;
  readonly states: TxtFile | null;
}

/**
 * How a property PARAM cell — uniqueitems `par`, runes `T1Param`, sets/setitems, propertygroups
 * `parmin`/`parmax` — becomes the number the property functions see.
 *
 * D2R, DATATBLS_ItemParamLinker 0x140214e40: a cell starting with `-` or a digit is atoi'd
 * (0x140214efb); anything else is a NAME tried against three linkers in order — skills `skill`
 * (tables +0x11C8, 0x1401fd5c5), montype `type` (+0x1400, 0x140270a65), states `state` (+0x2A8,
 * 0x1402020a9) — and a miss on all three is 0 (0x140214f0d). FOG_GetRowFromTxt 0x1407622c0 folds
 * ASCII to lower case before hashing, so the match ignores case.
 *
 * 1.14d keeps its old reading — a whole-cell integer, else 0 — because its linker is UNTRACED:
 * there is no 1.14d database, and 192 shipped 1.14d cells name a skill (4 only ignoring case) that
 * this therefore leaves at 0.
 */
export class ItemParamLinker {
  private readonly resurrected: boolean;
  private readonly names: Map<string, number>[];

  constructor(data: ItemParamLinkerSource) {
    this.resurrected = data.isResurrected;
    this.names = this.resurrected
      ? [
          ItemParamLinker.keys(data.skillRows, 'skill'),
          ItemParamLinker.keys(data.monTypeRows, 'type'),
          ItemParamLinker.keys(data.states, 'state'),
        ]
      : [];
  }

  resolve(cell: string | null | undefined): number {
    if (cell === null || cell === undefined || cell.length === 0) {
      return 0;
    }

    if (!this.resurrected) {
      // C# `int.TryParse` on the trimmed cell: whole-cell, and 0 outside Int32.
      const trimmed = cell.trim();
      if (!/^[+-]?\d+$/.test(trimmed)) {
        return 0;
      }

      const value = Number(trimmed);
      return value >= -2147483648 && value <= 2147483647 ? value : 0;
    }

    const first = cell.charCodeAt(0);
    if (cell[0] === '-' || (first >= 48 && first <= 57)) {
      return ItemParamLinker.atoi(cell);
    }

    const key = ItemParamLinker.asciiLower(cell);
    for (const names of this.names) {
      const row = names.get(key);
      if (row !== undefined) {
        return row;
      }
    }

    return 0;
  }

  private static atoi(cell: string): number {
    const negative = cell[0] === '-';
    let value = 0;
    for (let i = negative ? 1 : 0; i < cell.length; ++i) {
      const c = cell.charCodeAt(i);
      if (c < 48 || c > 57) {
        break;
      }

      value = (Math.imul(value, 10) + (c - 48)) | 0;
    }

    return negative ? -value | 0 : value;
  }

  // byte_141578F50: ASCII A-Z to a-z, every other character unchanged.
  private static asciiLower(text: string): string {
    return text.replace(/[A-Z]/g, c => String.fromCharCode(c.charCodeAt(0) + 32));
  }

  // First occurrence wins, as a linker keeps the row a key was first registered at.
  private static keys(table: TxtFile | null, column: string): Map<string, number> {
    const keys = new Map<string, number>();
    if (table === null || !table.hasColumn(column)) {
      return keys;
    }

    for (let row = 0; row < table.rowCount; ++row) {
      const key = ItemParamLinker.asciiLower(table.getString(row, column));
      if (key.length !== 0 && !keys.has(key)) {
        keys.set(key, row);
      }
    }

    return keys;
  }
}
