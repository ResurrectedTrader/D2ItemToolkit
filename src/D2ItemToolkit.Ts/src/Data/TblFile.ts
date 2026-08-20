const HEADER_LENGTH = 21;
const NODE_LENGTH = 17;

// 0x524943 / 0x524946 / 0x524948: an out-of-range INDEX resolves to index 500, not null.
const OUT_OF_RANGE_SUBSTITUTE = 500;

// DescStringIds.DescStr2Sentinel. Duplicated rather than imported: Types.ts is a leaf and
// importing it here would make the two modules mutually dependent for one constant.
const DESC_STR2_SENTINEL = 5382;

/**
 * A `.tbl` string table. Index lookups go through the hash table exactly as
 * STRTABLE_GetStringByIndex does, including the two states it halts on.
 */
export class TblFile {
  private readonly byIndex: (string | null)[];
  private readonly indexByKey: Map<string, number>;
  private readonly corrupt: Map<number, string>;

  private constructor(
    byIndex: (string | null)[],
    indexByKey: Map<string, number>,
    corrupt: Map<number, string>,
  ) {
    this.byIndex = byIndex;
    this.indexByKey = indexByKey;
    this.corrupt = corrupt;
  }

  static parse(bytes: Uint8Array): TblFile {
    if (bytes.length < HEADER_LENGTH) {
      throw new Error('Not a .tbl file: shorter than the 21 byte header.');
    }

    const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);

    const elementCount = view.getUint16(2, true);
    const hashTableSize = view.getUint32(4, true);

    const indexBase = HEADER_LENGTH;
    const nodeBase = indexBase + elementCount * 2;

    if (nodeBase + hashTableSize * NODE_LENGTH > bytes.length) {
      throw new Error('Not a .tbl file: the hash table runs past the end of the data.');
    }

    const byIndex: (string | null)[] = new Array<string | null>(elementCount).fill(null);
    const indexByKey = new Map<string, number>();
    const corrupt = new Map<number, string>();

    for (let id = 0; id < elementCount; ++id) {
      const node = view.getUint16(indexBase + id * 2, true);
      if (node >= hashTableSize) {
        corrupt.set(
          id,
          `Corrupt .tbl: index ${id} points at hash node ${node}, outside the ` +
            `${hashTableSize}-slot table. The game halts here (internal error 0x102 at 0x52495a).`,
        );
        continue;
      }

      const at = nodeBase + node * NODE_LENGTH;
      if (bytes[at] !== 1) {
        corrupt.set(
          id,
          `Corrupt .tbl: index ${id} points at hash node ${node} whose used byte is ` +
            `${bytes[at]}, not 1. The game halts here (internal error 0x107 at 0x524999).`,
        );
        continue;
      }

      byIndex[id] = readCString(
        bytes,
        view.getUint32(at + 11, true),
        view.getUint16(at + 15, true),
      );

      const key = readCString(bytes, view.getUint32(at + 7, true), Number.MAX_SAFE_INTEGER);
      if (key !== null && key.length !== 0 && !indexByKey.has(key)) {
        indexByKey.set(key, id);
      }
    }

    return new TblFile(byIndex, indexByKey, corrupt);
  }

  /**
   * Throws for an index whose hash node is in a state STRTABLE_GetStringByIndex halts on. The
   * check lives HERE, not in parse: the load pass validates nothing — it walks the hash table
   * sequentially by slot (0x525b9c-0x525bda), reading only the string offset and length — so a
   * corrupt node only matters if something asks for that index.
   */
  getByIndex(index: number): string | null {
    const reason = this.corrupt.get(index);
    if (reason !== undefined) {
      throw new Error(reason);
    }

    return index >= 0 && index < this.byIndex.length ? (this.byIndex[index] ?? null) : null;
  }

  getIndexByKey(key: string | null): number {
    if (key === null) {
      return -1;
    }

    const index = this.indexByKey.get(key);
    return index === undefined ? -1 : index;
  }
}

/**
 * `maxBytes` bounds the scan because the game bounds it too: the load pass passes the node's
 * stringLength (node+15) to UNICODE_GetWideCharCount (0x525bb4 / 0x525bbd) and the decode pass
 * uses that count + 1 as its limit (0x525c60 / 0x525c64), stopping at limit - 1. So the game
 * yields min(NUL scan, stringLength). Pass MAX_SAFE_INTEGER for the key, which has no length
 * field. Shipped tables always have stringLength == strlen + 1.
 */
function readCString(bytes: Uint8Array, offset: number, maxBytes: number): string | null {
  if (offset < 0 || offset >= bytes.length) {
    return null;
  }

  let limit = bytes.length;
  if (maxBytes < limit - offset) {
    limit = offset + maxBytes;
  }

  let end = offset;
  while (end < limit && bytes[end] !== 0) {
    ++end;
  }

  return new TextDecoder('utf-8').decode(bytes.subarray(offset, end));
}

/**
 * GetLocaleString 0x524a30 is a CASCADE, not a partition, and the details matter:
 *  - the range tests use the LOW 16 BITS, unsigned (0x524a33);
 *  - with no expansionstring table the id is REWRITTEN to 11078 (0x524a44) and re-tested;
 *  - the base table is asked for the id UNCHANGED (0x524ab8), not id - 10000.
 */
export class TblStringTable {
  static readonly PatchBase = 10000;
  static readonly ExpansionBase = 20000;
  static readonly MissingStringId = 11078;

  constructor(
    private readonly base: TblFile | null,
    private readonly patch: TblFile | null,
    private readonly expansion: TblFile | null,
  ) {}

  getByIndex(index: number): string | null {
    let id = index;

    if ((id & 0xffff) >= TblStringTable.ExpansionBase) {
      if (this.expansion !== null) {
        const fromExpansion = lookup(this.expansion, id - TblStringTable.ExpansionBase);
        if (fromExpansion !== null) {
          return fromExpansion;
        }
      } else {
        id = TblStringTable.MissingStringId;
      }
    }

    if (this.patch !== null && (id & 0xffff) >= TblStringTable.PatchBase) {
      const fromPatch = lookup(this.patch, id - TblStringTable.PatchBase);
      if (fromPatch !== null) {
        return fromPatch;
      }
    }

    return lookup(this.base, id);
  }

  /**
   * PATCH FIRST, then expansion, then base (0x524d93 / 0x524dc4 / 0x524de7). Searching base first
   * produced 44 wrong fields against the shipped itemstatcost.bin.
   */
  getIndexByKey(key: string): number {
    if (key.length === 0) {
      return -1;
    }

    const fromPatch = this.patch === null ? -1 : this.patch.getIndexByKey(key);
    if (fromPatch >= 0) {
      return fromPatch + TblStringTable.PatchBase;
    }

    const fromExpansion = this.expansion === null ? -1 : this.expansion.getIndexByKey(key);
    if (fromExpansion >= 0) {
      return fromExpansion + TblStringTable.ExpansionBase;
    }

    return this.base === null ? -1 : this.base.getIndexByKey(key);
  }

  /**
   * `> 0`, not `>= 0`: a base hit at index 0 is indistinguishable from a miss, because the
   * converter writes 0 for both.
   *
   * The sentinel is fixed here rather than passed in, matching the C#. Making it a caller's
   * argument turned an invariant the engine relies on into a convention every call site has to
   * remember — and a caller that passed a different one would silently produce a stat line the
   * game never emits.
   */
  resolveKey(key: string): number {
    const index = key.length === 0 ? -1 : this.getIndexByKey(key);
    return index > 0 ? index : DESC_STR2_SENTINEL;
  }
}

function lookup(table: TblFile | null, index: number): string | null {
  if (table === null) {
    return null;
  }

  const text = table.getByIndex(index);
  return text ?? table.getByIndex(OUT_OF_RANGE_SUBSTITUTE);
}
