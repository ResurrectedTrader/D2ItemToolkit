import type { TxtFile } from '../Data/TxtFile.js';
import type { TxtItemStatCostTable } from './TxtDataProviders.js';

const SetsPerProperty = 7;

/**
 * Properties.txt, compiled the way DATATBLS does it: a 46-byte record per row with seven
 * parallel sets. `stat<n>` is a NAME in the .txt and a resolved ItemStatCost row at runtime
 * (TXTFIELD_NAMETOWORD via pItemStatCostLinker), so it is resolved here too.
 */
export class PropertiesTable {
  static readonly SetsPerProperty = SetsPerProperty;

  private readonly rows: PropertiesTableRow[];
  // OrdinalIgnoreCase, matching the C# dictionary.
  private readonly byCode = new Map<string, number>();

  constructor(properties: TxtFile | null, statCost: TxtItemStatCostTable | null) {
    if (properties === null || properties === undefined) {
      this.rows = [];
      return;
    }

    this.rows = new Array<PropertiesTableRow>(properties.rowCount);

    for (let i = 0; i < properties.rowCount; ++i) {
      const row = new PropertiesTableRow();
      row.code = properties.getString(i, 'code');

      for (let set = 0; set < SetsPerProperty; ++set) {
        const suffix = String(set + 1);
        row.set[set] = PropertiesTable.int(properties, i, 'set' + suffix);
        row.val[set] = PropertiesTable.int(properties, i, 'val' + suffix);
        row.func[set] = PropertiesTable.int(properties, i, 'func' + suffix);
        row.stat[set] = PropertiesTable.resolveStat(properties, i, 'stat' + suffix, statCost);
      }

      this.rows[i] = row;

      const key = row.code.toLowerCase();
      if (row.code.length !== 0 && !this.byCode.has(key)) {
        this.byCode.set(key, i);
      }
    }
  }

  get rowCount(): number {
    return this.rows.length;
  }

  /** The C# indexer `Row this[int index]`: out of range is null, not a throw. */
  /** The whole row, or null when the index is out of range. The peer of C# `RowAt`. */
  rowAt(index: number): PropertiesTableRow | null {
    return this.getRow(index);
  }

  getRow(index: number): PropertiesTableRow | null {
    return index >= 0 && index < this.rows.length ? (this.rows[index] ?? null) : null;
  }

  /**
   * The compiled property id for a code, or -1. A -1 is what the loader writes for an
   * unresolved cell, and the appliers treat a negative id as "stop".
   */
  rowForCode(code: string | null | undefined): number {
    if (code === null || code === undefined || code.length === 0) {
      return -1;
    }

    const row = this.byCode.get(code.toLowerCase());
    return row === undefined ? -1 : row;
  }

  private static int(file: TxtFile, row: number, column: string): number {
    return file.hasColumn(column) ? file.getInt(row, column) : 0;
  }

  // An unresolvable or blank stat name compiles to -1, which ITEMMODS_AddPropertyToItemStatList
  // rejects when it fails to find an ItemStatCost record.
  private static resolveStat(
    file: TxtFile,
    row: number,
    column: string,
    statCost: TxtItemStatCostTable | null,
  ): number {
    if (statCost === null || statCost === undefined || !file.hasColumn(column)) {
      return -1;
    }

    const name = file.getString(row, column);
    return name.trim().length === 0 ? -1 : statCost.statIdForName(name.trim());
  }
}

/** One Properties.txt row — `PropertiesTable.Row` in the C#. */
export class PropertiesTableRow {
  code = '';
  readonly set: number[] = new Array<number>(SetsPerProperty).fill(0);
  readonly func: number[] = new Array<number>(SetsPerProperty).fill(0);
  readonly stat: number[] = new Array<number>(SetsPerProperty).fill(0);

  /**
   * `val<n>`, the record's +10+2n word. Only func 21 reads it (0x65fb7e passes it straight
   * through as the stat LAYER), and for the seven class-skill codes the cell is the class
   * number — `ama` 0 through `ass` 6.
   */
  readonly val: number[] = new Array<number>(SetsPerProperty).fill(0);
}
