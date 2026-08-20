using System;
using System.Collections.Generic;
using System.IO;

namespace D2ItemToolkit
{
    /// <summary>
    /// AnimData.D2, looked up the way ANIMDATA_GetRecordByNameHash does it (0x66a8f0): the name is
    /// upper-cased, hashed by summing its bytes into a byte, and matched EXACTLY over eight bytes
    /// including the NUL padding. 256 buckets, each a count followed by that many 160-byte records.
    /// </summary>
    public sealed class AnimDataFile
    {
        public const int BucketCount = 256;
        public const int RecordSize = 160;
        public const int NameLength = 8;

        private const int FramesOffset = 8;
        private const int SpeedOffset = 12;

        private readonly Dictionary<string, Record> _records =
            new Dictionary<string, Record>(StringComparer.Ordinal);

        public struct Record
        {
            public int FramesPerDirection;
            public int AnimationSpeed;
        }

        private AnimDataFile()
        {
        }

        public int RowCount { get { return _records.Count; } }

        public static AnimDataFile Parse(byte[] bytes)
        {
            var file = new AnimDataFile();
            if (bytes == null)
            {
                return file;
            }

            int at = 0;
            for (int bucket = 0; bucket < BucketCount; ++bucket)
            {
                if (at + 4 > bytes.Length)
                {
                    throw new InvalidDataException(
                        "AnimData.D2 ends inside the block count for bucket " + bucket + ".");
                }

                int count = ReadInt(bytes, at);
                at += 4;

                if (count < 0 || at + ((long)count * RecordSize) > bytes.Length)
                {
                    throw new InvalidDataException(
                        "AnimData.D2 bucket " + bucket + " claims " + count + " records, which "
                        + "runs past the end of the file.");
                }

                for (int i = 0; i < count; ++i, at += RecordSize)
                {
                    string name = ReadName(bytes, at);
                    if (name.Length == 0)
                    {
                        continue;
                    }

                    var record = new Record();
                    record.FramesPerDirection = ReadInt(bytes, at + FramesOffset);
                    record.AnimationSpeed = ReadInt(bytes, at + SpeedOffset);

                    // Duplicate names exist; the scan returns the FIRST match in bucket order.
                    if (!file._records.ContainsKey(name))
                    {
                        file._records[name] = record;
                    }
                }
            }

            return file;
        }

        public bool TryGet(string name, out Record record)
        {
            record = new Record();

            if (string.IsNullOrEmpty(name) || name.Length > NameLength)
            {
                return false;
            }

            return _records.TryGetValue(Upper(name), out record);
        }

        /// <summary>
        /// The bucket a name lands in: an unsigned byte sum over the upper-cased name (0x66a926).
        /// Exposed because it is the only part of the lookup that is not an ordinary dictionary hit.
        /// </summary>
        public static int Hash(string name)
        {
            byte sum = 0;
            string upper = Upper(name);

            for (int i = 0; i < upper.Length; ++i)
            {
                unchecked
                {
                    sum += (byte)upper[i];
                }
            }

            return sum;
        }

        private static string Upper(string name)
        {
            // 0x66a8ff folds only a-z; every other byte passes through untouched.
            char[] chars = name.ToCharArray();
            for (int i = 0; i < chars.Length; ++i)
            {
                if (chars[i] >= 'a' && chars[i] <= 'z')
                {
                    chars[i] = (char)(chars[i] - 32);
                }
            }

            return new string(chars);
        }

        private static string ReadName(byte[] bytes, int at)
        {
            int length = 0;
            while (length < NameLength && bytes[at + length] != 0)
            {
                ++length;
            }

            var chars = new char[length];
            for (int i = 0; i < length; ++i)
            {
                chars[i] = (char)bytes[at + i];
            }

            return new string(chars);
        }

        private static int ReadInt(byte[] bytes, int at)
        {
            return bytes[at]
                   | (bytes[at + 1] << 8)
                   | (bytes[at + 2] << 16)
                   | (bytes[at + 3] << 24);
        }
    }
}
