using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace D2ItemToolkit
{
    public sealed class TblFile
    {
        private const int HeaderLength = 21;
        private const int NodeLength = 17;

        private readonly string[] _byIndex;
        private readonly Dictionary<string, int> _indexByKey;
        private readonly Dictionary<int, string> _corrupt;

        private TblFile(
            string[] byIndex, Dictionary<string, int> indexByKey, Dictionary<int, string> corrupt)
        {
            _byIndex = byIndex;
            _indexByKey = indexByKey;
            _corrupt = corrupt;
        }

        public static TblFile Parse(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException("bytes");
            if (bytes.Length < HeaderLength)
            {
                throw new InvalidDataException("Not a .tbl file: shorter than the 21 byte header.");
            }

            int elementCount = BitConverter.ToUInt16(bytes, 2);
            int hashTableSize = checked((int)BitConverter.ToUInt32(bytes, 4));

            int indexBase = HeaderLength;
            int nodeBase = indexBase + (elementCount * 2);

            if (nodeBase + (hashTableSize * NodeLength) > bytes.Length)
            {
                throw new InvalidDataException(
                    "Not a .tbl file: the hash table runs past the end of the data.");
            }

            var byIndex = new string[elementCount];
            var indexByKey = new Dictionary<string, int>(StringComparer.Ordinal);

            var corrupt = new Dictionary<int, string>();

            for (int id = 0; id < elementCount; ++id)
            {
                int node = BitConverter.ToUInt16(bytes, indexBase + (id * 2));
                if (node >= hashTableSize)
                {
                    corrupt[id] =
                        "Corrupt .tbl: index " + id + " points at hash node " + node +
                        ", outside the " + hashTableSize + "-slot table. The game halts here " +
                        "(internal error 0x102 at 0x52495a).";
                    continue;
                }

                int at = nodeBase + (node * NodeLength);
                if (bytes[at] != 1)
                {
                    corrupt[id] =
                        "Corrupt .tbl: index " + id + " points at hash node " + node +
                        " whose used byte is " + bytes[at] + ", not 1. The game halts here " +
                        "(internal error 0x107 at 0x524999).";
                    continue;
                }

                byIndex[id] = ReadCString(
                    bytes,
                    checked((int)BitConverter.ToUInt32(bytes, at + 11)),
                    BitConverter.ToUInt16(bytes, at + 15));

                string key = ReadCString(
                    bytes, checked((int)BitConverter.ToUInt32(bytes, at + 7)), int.MaxValue);
                if (!string.IsNullOrEmpty(key))
                {
                    if (!indexByKey.ContainsKey(key))
                    {
                        indexByKey.Add(key, id);
                    }
                }
            }

            return new TblFile(byIndex, indexByKey, corrupt);
        }

        // Throws for an index whose hash node is in a state STRTABLE_GetStringByIndex halts on. The
        // check lives HERE, not in Parse: the load pass validates nothing (it walks the hash table
        // sequentially by slot, 0x525b9c-0x525bda, reading only the string offset and length), so a
        // corrupt node only matters if something asks for that index.
        public string GetByIndex(int index)
        {
            if (_corrupt.Count != 0)
            {
                string reason;
                if (_corrupt.TryGetValue(index, out reason))
                {
                    throw new InvalidDataException(reason);
                }
            }

            return index >= 0 && index < _byIndex.Length ? _byIndex[index] : null;
        }

        // Exhaustive, built from nodeForIndex. The game instead probes and gives up after
        // hashMaxTries (0x524c85 / 0x524cdc), so a pathological table could hide a key from the game
        // that this still finds. Unreachable with shipped data.
        public int GetIndexByKey(string key)
        {
            int index;
            return key != null && _indexByKey.TryGetValue(key, out index) ? index : -1;
        }

        // maxBytes bounds the scan, because the game bounds it too: the load pass passes the node's
        // stringLength (node+15) to UNICODE_GetWideCharCount (0x525bb4 / 0x525bbd) and the decode
        // pass uses that count + 1 as its limit (0x525c60 / 0x525c64), stopping at limit - 1. So the
        // game yields min(NUL scan, stringLength). Pass int.MaxValue for the key, which has no
        // length field. Shipped tables always have stringLength == strlen + 1.
        private static string ReadCString(byte[] bytes, int offset, int maxBytes)
        {
            if (offset < 0 || offset >= bytes.Length)
            {
                return null;
            }

            int limit = bytes.Length;
            if (maxBytes < limit - offset)
            {
                limit = offset + maxBytes;
            }

            int end = offset;
            while (end < limit && bytes[end] != 0)
            {
                ++end;
            }

            return Encoding.UTF8.GetString(bytes, offset, end - offset);
        }
    }

    public sealed class TblStringTable : IStringTable
    {
        public const int PatchBase = 10000;

        public const int ExpansionBase = 20000;

        private readonly TblFile _base;
        private readonly TblFile _patch;
        private readonly TblFile _expansion;

        public TblStringTable(TblFile baseTable, TblFile patchTable, TblFile expansionTable)
        {
            _base = baseTable;
            _patch = patchTable;
            _expansion = expansionTable;
        }

        // GetLocaleString (0x524a30) is a CASCADE, not a partition, and the details matter:
        //  * the range tests use the LOW 16 BITS, unsigned (0x524a33);
        //  * with no expansionstring table the id is REWRITTEN to 11078 (0x524a44) and re-tested;
        //  * the base table is asked for the id UNCHANGED (0x524ab8), not id - 10000.
        public string GetByIndex(int index)
        {
            int id = index;

            if (unchecked((ushort)id) >= ExpansionBase)
            {
                if (_expansion != null)
                {
                    string fromExpansion = Lookup(_expansion, id - ExpansionBase);
                    if (fromExpansion != null)
                    {
                        return fromExpansion;
                    }
                }
                else
                {
                    id = MissingStringId;
                }
            }

            if (_patch != null && unchecked((ushort)id) >= PatchBase)
            {
                string fromPatch = Lookup(_patch, id - PatchBase);
                if (fromPatch != null)
                {
                    return fromPatch;
                }
            }

            return Lookup(_base, id);
        }

        public const int MissingStringId = 11078;

        private const int OutOfRangeSubstitute = 500;

        private static string Lookup(TblFile table, int index)
        {
            if (table == null)
            {
                return null;
            }

            string text = table.GetByIndex(index);
            return text ?? Substitute(table);
        }

        // 0x524943 / 0x524946 / 0x524948: an out-of-range INDEX resolves to index 500, not null.
        // That substitution belongs to the range test alone — a corrupt NODE halts instead.
        private static string Substitute(TblFile table)
        {
            return table == null ? null : table.GetByIndex(OutOfRangeSubstitute);
        }

        // PATCH FIRST, then expansion, then base (0x524d93 / 0x524dc4 / 0x524de7). Searching base
        // first produced 44 wrong fields against the shipped itemstatcost.bin.
        public int GetIndexByKey(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return -1;
            }

            int index = _patch == null ? -1 : _patch.GetIndexByKey(key);
            if (index >= 0)
            {
                return index + PatchBase;
            }

            index = _expansion == null ? -1 : _expansion.GetIndexByKey(key);
            if (index >= 0)
            {
                return index + ExpansionBase;
            }

            return _base == null ? -1 : _base.GetIndexByKey(key);
        }

        // `> 0`, not `>= 0`: a base hit at index 0 is indistinguishable from a miss, because
        // STRTABLE_LookupString returns 0 for both and DATATBLS_LookupStringId then substitutes
        // 5382 (0x6117c6). Never collapse that sentinel to null — it resolves to real text.
        public int ResolveKey(string key)
        {
            int index = string.IsNullOrEmpty(key) ? -1 : GetIndexByKey(key);
            return index > 0 ? index : DescStringIds.DescStr2Sentinel;
        }
    }
}
