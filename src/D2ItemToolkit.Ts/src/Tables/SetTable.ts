import type { TblStringTable } from '../Data/TblFile.js';
import type { TxtFile } from '../Data/TxtFile.js';
import type { ItemProperty } from '../Stats/PropertyApplier.js';
import { TxtKeys } from './TxtDataProviders.js';

/** One setitems.txt record, 440 bytes in the game. */
export class SetItemRecord {
  constructor(
    /** +0x00, and the row index — the two are the same thing (0x636690). */
    readonly setItemId: number,
    /** The `index` cell, which is also the display-name key. */
    readonly key: string,
    /** +0x2C. */
    readonly setId: number,
    /** +0x87, read at 0x4e659f and 0x663a5d. 0 none, 1 per-piece, 2 progressive. */
    readonly addFunc: number,
    /** +0x24, defaulted to 5383 at 0x6366b9. */
    readonly nameStringId: number,
    /** The resolved display name. */
    readonly name: string | null,
  ) {}

  /**
   * +0x2E — this piece's index INSIDE its set, 0..5, and the bit
   * ITEMS_GetEquippedSetItemsMask sets for it (0x62a474). -1 when the link loop dropped it.
   */
  slot = -1;
}

/** One sets.txt record, 296 bytes in the game. */
export class SetRecord {
  constructor(
    /** The post-splice row index, which is what setitems.txt +0x2C stores. */
    readonly setId: number,
    /** The `index` cell — the key setitems.txt `set` resolves against. */
    readonly key: string,
    /** +0x02, read at 0x48d3b1 and handed to GetLocaleString. */
    readonly nameStringId: number,
    /** The resolved display name. It is NOT the key: `Angelical Raiment` -> `Angelic Raiment`. */
    readonly name: string | null,
    /**
     * pSetItem[] at +0x110, in the order the link loop appended it — ascending setitems.txt
     * row. Never longer than six.
     */
    readonly pieces: SetItemRecord[],
  ) {}
}

/**
 * sets.txt and setitems.txt as TXT_AllocTxt_setitems compiles them, including the LINK it
 * builds between the two at 0x63668d-0x63670d.
 *
 * Record sizes are read off the accessors: GetSetsLine 0x483410 does `imul eax, 128h`
 * (296 bytes) and GetSetItemsLine 0x483440 `imul eax, 1B8h` (440). Both counts are POST-SPLICE —
 * {@link TxtFile} drops the `Expansion` divider row the way
 * STRUCT_CreateBinFieldExcelAndFillData does at 0x6bd640 — so a row index here is the index the
 * binary uses.
 */
export class SetTable {
  /**
   * 0x6366b9: when STRTABLE_LookupString finds no entry for the `index` cell the compiled
   * record keeps 1507h instead, which is "an evil force".
   */
  static readonly MissingSetItemNameStringId = 5383;

  /**
   * `cmp dword ptr [eax+0Ch], 6 / jge` at 0x6366df — a seventh member is silently dropped, and
   * pSetItem[] is exactly six pointers wide (0x128 - 0x110).
   */
  static readonly MaxPiecesPerSet = 6;

  /**
   * Eight quadruples at +0x10 and eight more at +0x90. The field table lays PCode2a at offset
   * 0x10 (0x634e7e) and FCode1 at 0x90 (0x63533e), four bytes to a cell, so each property is
   * sixteen bytes and each block is 0x80 — which is exactly the stride both walks in
   * ITEMMOD_ApplySetBonuses use (`add edi, 10h` at 0x6601ec and 0x660228).
   */
  static readonly PropertiesPerBlock = 8;

  private readonly setRecords: SetRecord[] = [];
  private readonly pieceRecords: SetItemRecord[] = [];
  private readonly setsTxt: TxtFile | null;
  private propertyIds: ((code: string) => number) | null = null;

  constructor(sets: TxtFile | null, setItems: TxtFile | null, strings: TblStringTable) {
    this.setsTxt = sets;

    const setCount = sets === null ? 0 : sets.rowCount;
    const pieceCount = setItems === null ? 0 : setItems.rowCount;

    const members: SetItemRecord[][] = [];

    for (let row = 0; row < setCount; ++row) {
      const table = sets as TxtFile;
      members.push([]);

      // +0x02 is filled through DATATBLS_LookupStringId (0x634e14), the same converter every
      // other key column uses, so a miss substitutes 5382 rather than 5383.
      const nameId = TxtKeys.id(table, row, 'name', strings);

      this.setRecords.push(
        new SetRecord(
          row,
          table.getString(row, 'index'),
          nameId,
          strings.getByIndex(nameId),
          members[row] as SetItemRecord[],
        ),
      );
    }

    // Ascending setitems.txt row order IS pSetItem[] order: the loop assigns wSetItemId = i
    // (0x636690) and appends in the same pass.
    for (let row = 0; row < pieceCount; ++row) {
      const table = setItems as TxtFile;
      const key = table.getString(row, 'index');

      let nameId = strings.getIndexByKey(key);
      if (nameId <= 0) {
        nameId = SetTable.MissingSetItemNameStringId;
      }

      const setId = SetTable.setIdForKey(sets, table.getString(row, 'set'));

      const piece = new SetItemRecord(
        row,
        key,
        setId,
        table.getInt(row, 'add func'),
        nameId,
        strings.getByIndex(nameId),
      );

      this.pieceRecords.push(piece);

      // 0x6366c3 / 0x6366d1 / 0x6366df: in range, and the set not already full.
      const list = setId < 0 || setId >= setCount ? null : (members[setId] as SetItemRecord[]);
      if (list === null || list.length >= SetTable.MaxPiecesPerSet) {
        continue;
      }

      // +0x2E is the set's CURRENT member count at the moment of the append (0x6366f4), i.e.
      // this piece's slot inside pSetItem[].
      piece.slot = list.length;
      list.push(piece);
    }
  }

  /**
   * The `set` cell is a linker key over sets.txt `index` (field type 0x0D), not a row number, so
   * the compiled +0x2C is whatever row carries that index.
   */
  private static setIdForKey(sets: TxtFile | null, key: string): number {
    return sets === null || key.length === 0 ? -1 : sets.findRow('index', key);
  }

  /** 32 with shipped data; the `Expansion` divider is spliced out. */
  get setCount(): number {
    return this.setRecords.length;
  }

  /** 127 with shipped data. */
  get pieceCount(): number {
    return this.pieceRecords.length;
  }

  /** GetSetsLine 0x483410 — null outside the record count. */
  setAt(setId: number): SetRecord | null {
    return setId >= 0 && setId < this.setRecords.length
      ? (this.setRecords[setId] as SetRecord)
      : null;
  }

  /** GetSetItemsLine 0x483440 — null outside the record count. */
  pieceAt(setItemId: number): SetItemRecord | null {
    return setItemId >= 0 && setItemId < this.pieceRecords.length
      ? (this.pieceRecords[setItemId] as SetItemRecord)
      : null;
  }

  /**
   * The `PCode*`/`FCode*` cells hold property NAMES; the loader resolves them to Properties.txt
   * rows at compile time through pPropertiesLinker, exactly as the gems.txt mod codes are, so the
   * resolver is injected the same way `GemTable.resolvePropertyCodesWith` injects it.
   */
  resolvePropertyCodesWith(resolver: (code: string) => number): void {
    this.propertyIds = resolver;
  }

  /**
   * The eight PARTIAL quadruples at +0x10, in record order: PCode2a, PCode2b, PCode3a, PCode3b,
   * PCode4a, PCode4b, PCode5a, PCode5b.
   *
   * All eight are yielded because the walk at 0x6601c4 SKIPS a slot whose code is negative (`jl`
   * at 0x6601ca lands past the call, not past the loop) instead of stopping — a set with a blank
   * `b` slot still reaches the next tier's `a`.
   */
  partialProperties(setId: number): ItemProperty[] {
    const properties: ItemProperty[] = [];

    for (let slot = 0; slot < SetTable.PropertiesPerBlock; ++slot) {
      properties.push(
        this.property(setId, 'P', String(2 + Math.floor(slot / 2)) + (slot % 2 === 0 ? 'a' : 'b')),
      );
    }

    return properties;
  }

  /**
   * The eight FULL-SET quadruples at +0x90, FCode1..FCode8. Note the asymmetry with
   * {@link SetTable.partialProperties}: this walk BREAKS at the first negative code (`jl` at
   * 0x660209 jumps to the epilogue), so a caller must stop rather than skip.
   */
  fullProperties(setId: number): ItemProperty[] {
    const properties: ItemProperty[] = [];

    for (let slot = 0; slot < SetTable.PropertiesPerBlock; ++slot) {
      properties.push(this.property(setId, 'F', String(slot + 1)));
    }

    return properties;
  }

  private property(setId: number, prefix: string, suffix: string): ItemProperty {
    const table = this.setsTxt;

    if (table === null || setId < 0 || setId >= this.setRecords.length) {
      return { propertyId: -1, param: 0, min: 0, max: 0 };
    }

    const code = SetTable.cell(table, setId, prefix + 'Code' + suffix);

    return {
      propertyId: this.propertyIds === null ? -1 : this.propertyIds(code),
      param: SetTable.intCell(table, setId, prefix + 'Param' + suffix),
      min: SetTable.intCell(table, setId, prefix + 'Min' + suffix),
      max: SetTable.intCell(table, setId, prefix + 'Max' + suffix),
    };
  }

  private static cell(table: TxtFile, row: number, column: string): string {
    return table.hasColumn(column) ? table.getString(row, column) : '';
  }

  private static intCell(table: TxtFile, row: number, column: string): number {
    return table.hasColumn(column) ? table.getInt(row, column) : 0;
  }
}
