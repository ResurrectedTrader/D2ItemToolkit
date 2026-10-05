import type { TxtFile } from '../Data/TxtFile.js';
import type { ItemParamLinker } from './ItemParamLinker.js';
import type { PropertiesTable } from './PropertiesTable.js';

const EntriesPerGroup = 8;

/**
 * What a property cell links to, as sub_140214F40 writes it: a Properties.txt row (kind 0) or a
 * PropertyGroups.txt row (kind 1). An unresolved cell is stored as 0xFFFF:FFFF (0x140214f75), so
 * `row` is -1 and `kind` 0xFFFF.
 */
export interface PropertyRef {
  row: number;
  kind: number;
}

export const PropertyRefKind = {
  Property: 0,
  Group: 1,
  Unresolved: 0xffff,
} as const;

function unresolved(): PropertyRef {
  return { row: -1, kind: PropertyRefKind.Unresolved };
}

/** One PropertyGroups.txt entry — `PropertyGroupsTable.Entry` in the C#. */
export class PropertyGroupEntry {
  prop: PropertyRef = unresolved();
  parMin = 0;
  parMax = 0;
  modMin = 0;
  modMax = 0;
  chance = 0;

  /** The draw weight: a blank or non-positive chance counts as 1 (0x14028a050). */
  get weight(): number {
    return this.chance < 1 ? 1 : this.chance;
  }
}

/** One PropertyGroups.txt row — `PropertyGroupsTable.Row` in the C#. */
export class PropertyGroupRow {
  code = '';
  pickMode = 0;
  readonly entries: PropertyGroupEntry[] = [];
}

/**
 * PropertyGroups.txt, compiled as DATATBLS_LoadPropertiesTxt does it (record 0xC8, field table
 * 0x14021c53d..0x14021ce3c): a key, a pick mode, and eight {prop, parMin, parMax, modMin, modMax,
 * chance} entries. D2R only; 1.14d has no such table and gets an empty one.
 */
export class PropertyGroupsTable {
  static readonly EntriesPerGroup = EntriesPerGroup;

  private readonly rows: PropertyGroupRow[];
  // OrdinalIgnoreCase, matching the C# dictionary.
  private readonly byCode = new Map<string, number>();

  constructor(groups: TxtFile | null, properties: PropertiesTable | null);
  /** @internal The C# overload taking the linker is `internal`. */
  constructor(
    groups: TxtFile | null,
    properties: PropertiesTable | null,
    paramLinker: ItemParamLinker | null,
  );
  constructor(
    groups: TxtFile | null,
    properties: PropertiesTable | null,
    paramLinker: ItemParamLinker | null = null,
  ) {
    if (groups === null || groups === undefined) {
      this.rows = [];
      return;
    }

    this.rows = new Array<PropertyGroupRow>(groups.rowCount);

    for (let i = 0; i < groups.rowCount; ++i) {
      const code = groups.getString(i, 'code');
      const key = code.toLowerCase();
      if (code.length !== 0 && !this.byCode.has(key)) {
        this.byCode.set(key, i);
      }
    }

    for (let i = 0; i < groups.rowCount; ++i) {
      const row = new PropertyGroupRow();
      row.code = groups.getString(i, 'code');
      row.pickMode = groups.getInt(i, 'pickmode') & 0xff;

      for (let k = 0; k < EntriesPerGroup; ++k) {
        const n = String(k + 1);
        const entry = new PropertyGroupEntry();
        entry.prop = PropertyGroupsTable.link(groups.getString(i, 'prop' + n), properties, this);

        // 0x14021cf7b..0x14021cfd4: a group entry may name only an EARLIER group.
        if (entry.prop.kind === PropertyRefKind.Group && entry.prop.row >= i) {
          entry.prop.row = -1;
        }

        entry.parMin = PropertyGroupsTable.parseParam(
          groups.getString(i, 'parmin' + n),
          paramLinker,
        );
        entry.parMax = PropertyGroupsTable.parseParam(
          groups.getString(i, 'parmax' + n),
          paramLinker,
        );
        entry.modMin = groups.getInt(i, 'modmin' + n);
        entry.modMax = groups.getInt(i, 'modmax' + n);
        entry.chance = groups.getInt(i, 'chance' + n);
        row.entries.push(entry);
      }

      this.rows[i] = row;
    }
  }

  get rowCount(): number {
    return this.rows.length;
  }

  /** The C# indexer: out of range is null, not a throw. */
  getRow(row: number): PropertyGroupRow | null {
    return row >= 0 && row < this.rows.length ? (this.rows[row] ?? null) : null;
  }

  rowForCode(code: string | null | undefined): number {
    if (code === null || code === undefined || code.length === 0) {
      return -1;
    }

    return this.byCode.get(code.toLowerCase()) ?? -1;
  }

  /**
   * sub_140214F40: Properties.txt first (0x140214f8c), PropertyGroups.txt only on a miss
   * (0x140214fb9).
   */
  static link(
    code: string | null | undefined,
    properties: PropertiesTable | null,
    groups: PropertyGroupsTable | null,
  ): PropertyRef {
    if (code === null || code === undefined || code.length === 0) {
      return unresolved();
    }

    const row = properties === null ? -1 : properties.rowForCode(code);
    if (row >= 0) {
      return { row, kind: PropertyRefKind.Property };
    }

    const group = groups === null ? -1 : groups.rowForCode(code);
    return group >= 0 ? { row: group, kind: PropertyRefKind.Group } : unresolved();
  }

  /**
   * DATATBLS_ItemParamLinker 0x140214e40, the same linker every property table's params go
   * through. Without one, a number is atoi'd and a name is 0.
   */
  private static parseParam(cell: string, paramLinker: ItemParamLinker | null): number {
    if (paramLinker !== null) {
      return paramLinker.resolve(cell);
    }

    const first = cell.charCodeAt(0);
    const isDigit = (c: number): boolean => c >= 48 && c <= 57;
    if (cell.length === 0 || (cell[0] !== '-' && !isDigit(first))) {
      return 0;
    }

    const negative = cell[0] === '-';
    let value = 0;
    for (let i = negative ? 1 : 0; i < cell.length && isDigit(cell.charCodeAt(i)); ++i) {
      value = (Math.imul(value, 10) + (cell.charCodeAt(i) - 48)) | 0;
    }

    return negative ? -value | 0 : value;
  }
}
