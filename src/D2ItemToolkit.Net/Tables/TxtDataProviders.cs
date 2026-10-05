using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace D2ItemToolkit
{
    public sealed class D2DataFiles
    {
        // Private, so the compiler stops emitting a public parameterless one. Every property below
        // is `private set`, so a consumer-constructed instance was empty, and handing it to
        // TooltipEngine.FromData failed with an ArgumentNullException naming an internal parameter.
        // Build it through LoadEmbedded or Load.
        private D2DataFiles()
        {
        }

        public StringTable Strings { get; private set; }
        public TxtItemStatCostTable ItemStatCost { get; private set; }
        public TxtSkillTable Skills { get; private set; }
        public TxtCharacterClassTable Classes { get; private set; }
        public TxtMonsterTypeTable MonsterTypes { get; private set; }
        public TxtFile ItemTypes { get; private set; }
        public TxtFile Weapons { get; private set; }
        public TxtFile Armor { get; private set; }
        public TxtFile Misc { get; private set; }
        public TxtFile UniqueItems { get; private set; }
        public TxtFile SetItems { get; private set; }

        // setitems.txt on its own is only half the set data: the piece list, the set name and the
        // full-set properties all live here, and TXT_AllocTxt_setitems links the two at 0x63668d.
        public TxtFile Sets { get; private set; }

        // The runtime affix arrays are CONCATENATIONS, and 1-based:
        //   magic = [MagicSuffix][MagicPrefix][automagic]   stride 144, TXT_magicaffixes_GetLine 0x633ee0
        //   rare  = [RareSuffix][RarePrefix]                stride  72, TXT_RareAffixes_GetLine 0x634260
        public TxtFile MagicSuffix { get; private set; }
        public TxtFile MagicPrefix { get; private set; }
        public TxtFile AutoMagic { get; private set; }
        public TxtFile RareSuffix { get; private set; }
        public TxtFile RarePrefix { get; private set; }
        public TxtFile LowQualityItems { get; private set; }

        // The superior counterpart of lowqualityitems.txt. Unlike that file it carries the rolled
        // ranges, so a superior item's modifiers can be attributed to a row.
        public TxtFile QualityItems { get; private set; }
        public TxtFile CharStats { get; private set; }
        public TxtFile Gems { get; private set; }
        public TxtFile Runes { get; private set; }

        // Holds the fixed mods a crafted recipe adds on top of its random affixes, with their
        // ranges — the only place those ranges are recorded.
        public TxtFile CubeMain { get; private set; }

        // colors.txt. The ROW INDEX is the palette-shift value stored in the compiled tables; our
        // .txt copies still hold the 4-char `code`, so this is what turns one into the other.
        public TxtFile Colors { get; private set; }
        public TxtFile Experience { get; private set; }
        public TxtFile Properties { get; private set; }

        // D2R only: the kind-1 targets of a property cell, sub_140214F40 0x140214fb9.
        internal TxtFile PropertyGroups { get; private set; }
        public TxtFile SkillRows { get; private set; }

        /// <summary>MonType.txt as rows — the second name table of DATATBLS_ItemParamLinker.</summary>
        internal TxtFile MonTypeRows { get; private set; }

        /// <summary>states.txt; D2R only, for DATATBLS_ItemParamLinker's third name table.</summary>
        internal TxtFile States { get; private set; }

        /// <summary>DATATBLS_ItemParamLinker 0x140214e40: how a property param cell becomes a number.</summary>
        internal ItemParamLinker ParamLinker
        {
            get { return _paramLinker ?? (_paramLinker = new ItemParamLinker(this)); }
        }

        private ItemParamLinker _paramLinker;
        public TxtFile PlayerTypes { get; private set; }
        public TxtFile PlayerModes { get; private set; }

        // The monster half of COMPOSIT_BuildCofPath 0x64f5b0, which a MERCENARY viewer takes.
        // monstats.txt is already read into MonsterTypes for its NameStr; the animation name needs
        // `Code` (+16) and the `MonStatsEx` link (+24) off the same rows, so the raw file is kept
        // too rather than widening that table.
        public TxtFile MonsterStats { get; private set; }
        public TxtFile MonsterStats2 { get; private set; }

        // TxtGetMonModeLine 0x65b500 selectors 0 and 1 both index this one table (0x65b19f and
        // 0x65b1a4 assign the same pointer), unlike the player's, which is PlrType and PlrMode
        // concatenated.
        public TxtFile MonsterModes { get; private set; }

        // The throwing-potion damage arm (0x485410) reads a missiles.txt record; EType is a
        // linker field over elemtypes.txt `code`, whose ROW INDEX is the stored value (0x612993).
        public TxtFile Missiles { get; private set; }
        public TxtFile ElementTypes { get; private set; }

        // D2R only: the belt-size line reads numboxes off this, 0x1401d584f.
        public TxtFile Belts { get; private set; }
        public AnimDataFile AnimData { get; private set; }

        /// <summary>The game this data set describes; the engine branches on it.</summary>
        public GameVariant Variant { get; private set; }

        /// <summary>The D2R locale (enUS, deDE, ...); null for 1.14d.</summary>
        public string ResurrectedLanguage
        {
            get
            {
                JsonStringTable table = Strings as JsonStringTable;
                return table == null ? null : table.Language;
            }
        }

        public bool IsResurrected
        {
            get { return Variant != GameVariant.Lod114d; }
        }

        public static D2DataFiles LoadEmbedded()
        {
            return LoadEmbedded(GameVariant.Lod114d);
        }

        public static D2DataFiles LoadEmbedded(
            GameVariant variant, ResurrectedTextOptions options = null)
        {
            switch (variant)
            {
                case GameVariant.Lod114d:
                    return Build(
                        GameVariant.Lod114d,
                        name => Resource("excel." + name),
                        TblStrings(name => Resource("locale.eng." + name)),
                        name => Resource("global." + name));
                case GameVariant.ReignOfTheWarlock:
                case GameVariant.Resurrected:
                    // The embedded base/ holds only the tables that differ from excel/; the rest
                    // are byte-identical, so falling through to excel/ reads the same bytes.
                    bool isBase = variant == GameVariant.Resurrected;
                    return Build(
                        variant,
                        name => (isBase ? Resource("d2r.excel.base." + name) : null) ??
                                Resource("d2r.excel." + name),
                        JsonStrings(
                            name => Resource("d2r.strings." + name),
                            name => Resource("d2r.strings-legacy." + name),
                            options),
                        name => Resource("d2r.global." + name));
                default:
                    throw new ArgumentOutOfRangeException("variant");
            }
        }

        /// <summary>
        /// A D2R extraction: <paramref name="excelDirectory"/> is <c>data/global/excel</c> (or its
        /// <c>base</c> subdirectory) and <paramref name="stringsDirectory"/> is
        /// <c>data/local/lng/strings</c>, with <c>strings-legacy</c> beside it.
        /// </summary>
        public static D2DataFiles LoadResurrected(
            GameVariant variant,
            string excelDirectory,
            string stringsDirectory,
            string legacyStringsDirectory = null,
            string globalDirectory = null,
            ResurrectedTextOptions options = null)
        {
            if (variant == GameVariant.Lod114d) throw new ArgumentOutOfRangeException("variant");
            if (excelDirectory == null) throw new ArgumentNullException("excelDirectory");
            if (stringsDirectory == null) throw new ArgumentNullException("stringsDirectory");

            return Build(
                variant,
                name => ReadIfPresent(excelDirectory, name),
                JsonStrings(
                    name => ReadIfPresent(stringsDirectory, name),
                    name => legacyStringsDirectory == null
                        ? null
                        : ReadIfPresent(legacyStringsDirectory, name),
                    options),
                name => globalDirectory == null ? null : ReadIfPresent(globalDirectory, name));
        }

        private static Func<StringTable> TblStrings(Func<string, byte[]> locale)
        {
            return () => new TblStringTable(
                ParseTbl(locale("string.tbl")),
                ParseTbl(locale("patchstring.tbl")),
                ParseTbl(locale("expansionstring.tbl")));
        }

        private static Func<StringTable> JsonStrings(
            Func<string, byte[]> strings,
            Func<string, byte[]> legacyStrings,
            ResurrectedTextOptions options)
        {
            ResurrectedTextOptions settings = options ?? ResurrectedTextOptions.Default;
            return () => new JsonStringTable(
                strings, legacyStrings, settings.LegacyGraphics, settings.Language ?? "enUS");
        }

        public static IEnumerable<string> EmbeddedResourceNames
        {
            get
            {
                foreach (string name in
                    typeof(D2DataFiles).GetTypeInfo().Assembly.GetManifestResourceNames())
                {
                    if (name.StartsWith(ResourcePrefix, StringComparison.Ordinal))
                    {
                        yield return name.Substring(ResourcePrefix.Length);
                    }
                }
            }
        }

        private const string ResourcePrefix = "D2ItemToolkit.Data.";

        // D2R ships its tables in lower case, 1.14d in mixed case; the game matches either way.
        private static readonly Lazy<Dictionary<string, string>> ResourceNames =
            new Lazy<Dictionary<string, string>>(() =>
            {
                var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string name in
                    typeof(D2DataFiles).GetTypeInfo().Assembly.GetManifestResourceNames())
                {
                    names[name] = name;
                }

                return names;
            });

        private static byte[] Resource(string suffix)
        {
            Assembly assembly = typeof(D2DataFiles).GetTypeInfo().Assembly;

            string name;
            if (!ResourceNames.Value.TryGetValue(ResourcePrefix + suffix, out name))
            {
                return null;
            }

            using (Stream stream = assembly.GetManifestResourceStream(name))
            {
                if (stream == null)
                {
                    return null;
                }

                var buffer = new byte[stream.Length];
                int read = 0;
                while (read < buffer.Length)
                {
                    int got = stream.Read(buffer, read, buffer.Length - read);
                    if (got == 0)
                    {
                        break;
                    }

                    read += got;
                }

                return buffer;
            }
        }

        public static D2DataFiles Load(
            string excelDirectory, string localeDirectory, string globalDirectory = null)
        {
            if (excelDirectory == null) throw new ArgumentNullException("excelDirectory");
            if (localeDirectory == null) throw new ArgumentNullException("localeDirectory");

            return Build(
                GameVariant.Lod114d,
                name => ReadIfPresent(excelDirectory, name),
                TblStrings(name => ReadIfPresent(localeDirectory, name)),
                name => globalDirectory == null ? null : ReadIfPresent(globalDirectory, name));
        }

        private static D2DataFiles Build(
            GameVariant variant,
            Func<string, byte[]> excel,
            Func<StringTable> loadStrings,
            Func<string, byte[]> global)
        {
            StringTable strings = loadStrings();

            var data = new D2DataFiles();
            data.Variant = variant;
            data.Strings = strings;
            data.ItemStatCost = new TxtItemStatCostTable(
                Required(excel, variant, "ItemStatCost.txt"), strings);
            data.Skills = new TxtSkillTable(
                Required(excel, variant, "skills.txt"),
                Optional(excel, variant, "skilldesc.txt"),
                strings,
                Optional(excel, variant, "PlayerClass.txt"));
            data.Classes = new TxtCharacterClassTable(
                Required(excel, variant, "charstats.txt"), strings);
            data.MonsterTypes = new TxtMonsterTypeTable(
                Optional(excel, variant, "MonType.txt"),
                Optional(excel, variant, "monstats.txt"),
                strings);
            data.ItemTypes = Optional(excel, variant, "ItemTypes.txt");
            data.Weapons = Optional(excel, variant, "weapons.txt");
            data.Armor = Optional(excel, variant, "armor.txt");
            data.Misc = Optional(excel, variant, "misc.txt");
            data.UniqueItems = Optional(excel, variant, "UniqueItems.txt");
            data.SetItems = Optional(excel, variant, "SetItems.txt");
            data.Sets = Optional(excel, variant, "sets.txt");
            data.MagicSuffix = Optional(excel, variant, "MagicSuffix.txt");
            data.MagicPrefix = Optional(excel, variant, "MagicPrefix.txt");
            data.AutoMagic = Optional(excel, variant, "automagic.txt");
            data.RareSuffix = Optional(excel, variant, "RareSuffix.txt");
            data.RarePrefix = Optional(excel, variant, "RarePrefix.txt");
            data.LowQualityItems = Optional(excel, variant, "lowqualityitems.txt");
            data.QualityItems = Optional(excel, variant, "qualityitems.txt");
            data.CharStats = Optional(excel, variant, "charstats.txt");
            data.Gems = Optional(excel, variant, "gems.txt");
            data.Runes = Optional(excel, variant, "Runes.txt");
            data.CubeMain = Optional(excel, variant, "cubemain.txt");
            data.Colors = Optional(excel, variant, "colors.txt");
            data.Experience = Optional(excel, variant, "Experience.txt");
            data.Properties = Optional(excel, variant, "Properties.txt");
            data.PropertyGroups = Optional(excel, variant, "propertygroups.txt");
            data.SkillRows = Optional(excel, variant, "skills.txt");
            data.MonTypeRows = Optional(excel, variant, "MonType.txt");
            data.States = Optional(excel, variant, "states.txt");
            data.PlayerTypes = Optional(excel, variant, "PlrType.txt");
            data.PlayerModes = Optional(excel, variant, "PlrMode.txt");
            data.MonsterStats = Optional(excel, variant, "monstats.txt");
            data.MonsterStats2 = Optional(excel, variant, "monstats2.txt");
            data.MonsterModes = Optional(excel, variant, "MonMode.txt");
            data.Missiles = Optional(excel, variant, "Missiles.txt");
            data.ElementTypes = Optional(excel, variant, "ElemTypes.txt");
            data.Belts = Optional(excel, variant, "belts.txt");

            byte[] animData = global("AnimData.D2");
            data.AnimData = animData == null ? null : AnimDataFile.Parse(animData);

            return data;
        }

        private static TblFile ParseTbl(byte[] bytes)
        {
            return bytes == null ? null : TblFile.Parse(bytes);
        }

        private static TxtFile Optional(
            Func<string, byte[]> source, GameVariant variant, string name)
        {
            byte[] bytes = source(name);
            return bytes == null ? null : TxtFile.Load(bytes, variant);
        }

        private static TxtFile Required(
            Func<string, byte[]> source, GameVariant variant, string name)
        {
            TxtFile file = Optional(source, variant, name);
            if (file == null)
            {
                throw new FileNotFoundException("Required data file not found: " + name);
            }

            return file;
        }

        internal ItemDescriptionGenerator CreateGenerator(
            IStatValueSource values = null,
            IGameTimeProvider time = null,
            bool isMainStatBlock = true)
        {
            return new ItemDescriptionGenerator(
                ItemStatCost, Strings, values, Skills, Classes, MonsterTypes, time, isMainStatBlock,
                IsResurrected);
        }

        // Extractions vary in case, so fall back to a case-insensitive scan of the directory.
        private static byte[] ReadIfPresent(string directory, string name)
        {
            string exact = Path.Combine(directory, name);
            if (File.Exists(exact))
            {
                return File.ReadAllBytes(exact);
            }

            if (!Directory.Exists(directory))
            {
                return null;
            }

            foreach (string candidate in Directory.GetFiles(directory))
            {
                if (string.Equals(Path.GetFileName(candidate), name, StringComparison.OrdinalIgnoreCase))
                {
                    return File.ReadAllBytes(candidate);
                }
            }

            return null;
        }
    }

    internal static class TxtKeys
    {
        // The loader DISTINGUISHES an absent column from a blank cell, and so must every provider:
        //   absent -> the defaults loop writes 0 (0x6bdfd4), so the engine resolves string.tbl[0];
        //   blank  -> the converter runs and DATATBLS_LookupStringId substitutes 5382 (0x6117c6).
        // Resolving unconditionally prints "an evil force" where the game prints Warriv gossip.
        internal static int Id(TxtFile file, int row, string column, StringTable strings)
        {
            return file.HasColumn(column)
                ? strings.ResolveKey(file.GetString(row, column))
                : 0;
        }

        internal static string Text(
            TxtFile file, int row, string column, StringTable strings)
        {
            return strings.GetByIndex(Id(file, row, column, strings));
        }
    }

    public sealed class TxtItemStatCostTable : IItemStatCostTable, IItemStatOpTable
    {
        private readonly StatDescriptor[] _stats;
        private readonly int[] _byDescPriority;
        private readonly Dictionary<int, int[]> _groups;
        private readonly int _skillIdShift;

        // The row index IS the stat id, so a name lookup is how every other table's "stat" column
        // resolves (TXTFIELD_NAMETOWORD through pItemStatCostLinker).
        private readonly Dictionary<string, int> _byName =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private static readonly string[] OpStatColumns = { "op stat1", "op stat2", "op stat3" };

        private readonly IReadOnlyList<ItemStatOpEntry> _opEntries;

        public IReadOnlyList<ItemStatOpEntry> PercentOfBaseEntries { get { return _opEntries; } }

        private readonly IReadOnlyList<ItemStatLevelOpEntry> _levelOpEntries;

        internal IReadOnlyList<ItemStatLevelOpEntry> LevelScaledEntries { get { return _levelOpEntries; } }

        public int StatIdForName(string name)
        {
            int id;
            return !string.IsNullOrEmpty(name) && _byName.TryGetValue(name, out id) ? id : -1;
        }

        public TxtItemStatCostTable(TxtFile file, StringTable strings)
        {
            if (file == null) throw new ArgumentNullException("file");
            if (strings == null) throw new ArgumentNullException("strings");

            for (int row = 0; row < file.RowCount; ++row)
            {
                string name = file.GetString(row, "Stat");
                if (name.Length != 0 && !_byName.ContainsKey(name))
                {
                    _byName.Add(name, row);
                }
            }

            // op 13 here. The other ops either cannot fire on an item's statlist (owner-type gates
            // at 0x626259 onward) or are unreachable with shipped data — 6/7 need act and
            // period-of-day and their only two users are unspawnable — except D2R's ops 4/5, which
            // fire on the unit the item is attached to and are collected separately below.
            var ops = new List<ItemStatOpEntry>();
            for (int row = 0; row < file.RowCount; ++row)
            {
                if (file.GetInt(row, "op") != 13)
                {
                    continue;
                }

                foreach (string column in OpStatColumns)
                {
                    string target = file.GetString(row, column);
                    int targetRow;
                    if (target.Length != 0 && _byName.TryGetValue(target, out targetRow))
                    {
                        ops.Add(new ItemStatOpEntry(row, targetRow));
                    }
                }
            }

            _opEntries = ops;

            // Ops 4 and 5: the rows DATATBLS_LoadItemStatCostTxt marks for re-evaluation when the
            // list is attached to a unit (+0x4F, 0x14021bc6d-0x14021bc79).
            var levelOps = new List<ItemStatLevelOpEntry>();
            for (int row = 0; row < file.RowCount; ++row)
            {
                int op = file.GetInt(row, "op");
                if (op != 4 && op != 5)
                {
                    continue;
                }

                int opBase;
                if (!_byName.TryGetValue(file.GetString(row, "op base"), out opBase))
                {
                    continue;
                }

                foreach (string column in OpStatColumns)
                {
                    string target = file.GetString(row, column);
                    int targetRow;
                    if (target.Length != 0 && _byName.TryGetValue(target, out targetRow))
                    {
                        levelOps.Add(new ItemStatLevelOpEntry(
                            row, targetRow, op, file.GetInt(row, "op param"), opBase,
                            file.GetInt(opBase, "ValShift")));
                    }
                }
            }

            _levelOpEntries = levelOps;

            _stats = new StatDescriptor[file.RowCount];

            for (int row = 0; row < file.RowCount; ++row)
            {
                var stat = new StatDescriptor();
                stat.StatId = row;

                // Each field is TRUNCATED to the width the loader stores it in, with no range check.
                // The widths bite: descpriority 40000 becomes int16 -25536 and sorts FIRST; descfunc
                // 256 becomes 0, so the row never enters the walked array at all (0x638530).
                // descval and dgrpval are NOT defaulted to 1 — no hook field (0x637f0c).
                stat.DescPriority = unchecked((short)file.GetInt(row, "descpriority"));
                stat.DescFunc = unchecked((byte)file.GetInt(row, "descfunc"));

                stat.DescVal = unchecked((byte)file.GetInt(row, "descval"));
                stat.DescGrpVal = unchecked((byte)file.GetInt(row, "dgrpval"));

                stat.DescStrPos = KeyId(file, row, "descstrpos", strings);
                stat.DescStrNeg = KeyId(file, row, "descstrneg", strings);
                stat.DescStr2 = KeyId(file, row, "descstr2", strings);

                stat.DescGrp = unchecked((ushort)file.GetInt(row, "dgrp"));
                stat.DescGrpFunc = unchecked((byte)file.GetInt(row, "dgrpfunc"));
                stat.DescGrpStrPos = KeyId(file, row, "dgrpstrpos", strings);
                stat.DescGrpStrNeg = KeyId(file, row, "dgrpstrneg", strings);
                stat.DescGrpStr2 = KeyId(file, row, "dgrpstr2", strings);

                stat.ValShift = unchecked((byte)file.GetInt(row, "ValShift"));
                stat.Op = unchecked((byte)file.GetInt(row, "op"));
                stat.OpParam = unchecked((byte)file.GetInt(row, "op param"));
                stat.OpBase = ResolveOpBase(file, row);

                _stats[row] = stat;
            }

            var described = new List<StatDescriptor>();
            foreach (StatDescriptor stat in _stats)
            {
                if (stat.DescFunc != 0)
                {
                    described.Add(stat);
                }
            }

            // 0x63851c builds this array in ascending row order, then 0x638571 qsorts it. The
            // comparator has no tie-break, so the CRT's own permutation is part of the output and
            // a stable or differently-pivoting sort gives the wrong order within a tie group.
            StatDescriptor[] ordered = described.ToArray();
            CrtQsort.Sort(ordered, ComparePriorityOnly);

            _byDescPriority = new int[ordered.Length];
            for (int i = 0; i < ordered.Length; ++i)
            {
                _byDescPriority[i] = ordered[i].StatId;
            }

            _groups = BuildGroups(_stats);

            int stuff = file.GetInt(0, "stuff");
            _skillIdShift = stuff >= 1 && stuff <= 8 ? stuff : 6;
        }

        private static int KeyId(TxtFile file, int row, string column, StringTable strings)
        {
            return TxtKeys.Id(file, row, column, strings);
        }

        // SORT_ItemDescPriority 0x6379d0 — a signed 16-bit compare of the priority word alone,
        // returning -1/0/1. There is deliberately no tie-break here: ties fall out of CrtQsort's
        // permutation, which is what the game actually shows. Adding one (stat id, say) reorders
        // 63 of the 207 entries and is visible on Call to Arms and Gheed's Fortune.
        private static int ComparePriorityOnly(StatDescriptor a, StatDescriptor b)
        {
            return a.DescPriority < b.DescPriority ? -1 : a.DescPriority > b.DescPriority ? 1 : 0;
        }

        // Name lookup only; a miss gives 0xFFFF, which SKILLDESC_CalcStatGroupValue treats as
        // out of range and bails on (0x4e4c76, unsigned).
        private static int ResolveOpBase(TxtFile file, int row)
        {
            const int UnresolvedOpBase = 0xFFFF;

            string text = file.GetString(row, "op base");
            if (text.Length == 0)
            {
                return UnresolvedOpBase;
            }

            int found = file.FindRow("Stat", text);
            return found >= 0 ? found : UnresolvedOpBase;
        }

        private static Dictionary<int, int[]> BuildGroups(StatDescriptor[] stats)
        {
            var members = new Dictionary<int, List<int>>();
            foreach (StatDescriptor stat in stats)
            {
                if (stat.DescGrp == 0)
                {
                    continue;
                }

                List<int> list;
                if (!members.TryGetValue(stat.DescGrp, out list))
                {
                    list = new List<int>();
                    members.Add(stat.DescGrp, list);
                }

                list.Add(stat.StatId);
            }

            var result = new Dictionary<int, int[]>(members.Count);
            foreach (KeyValuePair<int, List<int>> pair in members)
            {
                result.Add(pair.Key, pair.Value.ToArray());
            }

            return result;
        }

        /// <summary>The descriptor for a stat, or null when the id is out of range.</summary>
        public StatDescriptor RowAt(int statId)
        {
            StatDescriptor descriptor;
            return TryGetStat(statId, out descriptor) ? descriptor : null;
        }

        /// <summary>
        /// The descriptor for a stat, as a COPY — see <see cref="StatDescriptor.Copy"/> for why.
        /// Read it freely; writing to it changes nothing in the table.
        /// </summary>
        public bool TryGetStat(int statId, out StatDescriptor descriptor)
        {
            StatDescriptor live;
            if (!TryGetLiveStat(statId, out live))
            {
                descriptor = null;
                return false;
            }

            descriptor = live.Copy();
            return true;
        }

        // The engine's own path: the live instance, no allocation. Explicit implementation of an
        // internal interface, so it cannot be reached from outside the assembly even though the
        // signature matches the public method above.
        bool IItemStatCostTable.TryGetStat(int statId, out StatDescriptor descriptor)
        {
            return TryGetLiveStat(statId, out descriptor);
        }

        private bool TryGetLiveStat(int statId, out StatDescriptor descriptor)
        {
            if (statId < 0 || statId >= _stats.Length)
            {
                descriptor = null;
                return false;
            }

            descriptor = _stats[statId];
            return descriptor != null;
        }

        public int RowCount { get { return _stats.Length; } }

        public IReadOnlyList<int> StatIdsByDescPriority { get { return _byDescPriority; } }

        public int SkillIdShift { get { return _skillIdShift; } }

        public IReadOnlyList<int> GetStatsInDescGroup(int descGrp)
        {
            int[] members;
            return _groups.TryGetValue(descGrp, out members) ? members : new int[0];
        }
    }

    public sealed class TxtSkillTable : ISkillTable, ISkillItemProcSource
    {
        private readonly string[] _names;
        private readonly SkillItemProc[] _itemProcs;
        private readonly int[] _classes;
        private readonly int[] _requiredLevels;
        private readonly int[] _maxLevels;
        private readonly string _sentinel;
        private readonly string[] _classCodes;

        public TxtSkillTable(
            TxtFile skills, TxtFile skillDesc, StringTable strings, TxtFile playerClass = null)
        {
            if (skills == null) throw new ArgumentNullException("skills");
            if (strings == null) throw new ArgumentNullException("strings");

            _classCodes = BuildClassCodes(playerClass);

            _names = new string[skills.RowCount];
            _itemProcs = new SkillItemProc[skills.RowCount];
            _classes = new int[skills.RowCount];
            _requiredLevels = new int[skills.RowCount];

            bool hasReqLevel = skills.HasColumn("reqlevel");
            for (int row = 0; row < skills.RowCount; ++row)
            {
                _requiredLevels[row] = hasReqLevel ? skills.GetInt(row, "reqlevel") : 0;
            }

            _maxLevels = new int[skills.RowCount];
            bool hasMaxLevel = skills.HasColumn("maxlvl");
            for (int row = 0; row < skills.RowCount; ++row)
            {
                _maxLevels[row] = hasMaxLevel ? skills.GetInt(row, "maxlvl") : 0;
            }

            _sentinel = strings.GetByIndex(DescStringIds.DescStr2Sentinel);

            for (int row = 0; row < skills.RowCount; ++row)
            {
                _classes[row] = ResolveClass(skills.GetString(row, "charclass"));
                _names[row] = _sentinel;

                if (skillDesc == null)
                {
                    continue;
                }

                string descKey = skills.GetString(row, "skilldesc");
                if (descKey.Length == 0)
                {
                    continue;
                }

                int descRow = skillDesc.FindRow("skilldesc", descKey);
                if (descRow < 0)
                {
                    continue;
                }

                _itemProcs[row] = SkillItemProc.Read(skills, row, skillDesc, descRow, strings);
                string name = TxtKeys.Text(skillDesc, descRow, "str name", strings);

                if (name != null)
                {
                    _names[row] = name;
                }
            }
        }

        private static readonly string[] StockClassCodes =
        {
            "ama", "sor", "nec", "pal", "bar", "dru", "ass",
        };

        private static string[] BuildClassCodes(TxtFile playerClass)
        {
            if (playerClass == null || !playerClass.HasColumn("Code"))
            {
                return StockClassCodes;
            }

            var codes = new string[playerClass.RowCount];
            for (int row = 0; row < codes.Length; ++row)
            {
                codes[row] = playerClass.GetString(row, "Code");
            }

            return codes;
        }

        // CASE-SENSITIVE over exactly four space-padded bytes: field type 0x0D copies at most 4
        // bytes and pads with 0x20 (0x6bdc62 onwards), then GetClassIdFromName compares the packed
        // value as a raw DWORD (0x6bd155). A miss is -1 (0x6bd168), which costs DescFunc 28 its
        // clamp and DescFunc 27 its "(Class Only)" suffix.
        // The playerclass Code -> class id mapping, exposed for callers that need to resolve a
        // class code from another table (ItemTypes `Class`, for one).
        public int ClassIdForCode(string code)
        {
            return ResolveClass(code);
        }

        private int ResolveClass(string code)
        {
            string packed = PackClassCode(code);
            for (int i = 0; i < _classCodes.Length; ++i)
            {
                if (string.Equals(packed, PackClassCode(_classCodes[i]), StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        private static string PackClassCode(string code)
        {
            if (string.IsNullOrEmpty(code))
            {
                return "    ";
            }

            return code.Length >= 4 ? code.Substring(0, 4) : code.PadRight(4, ' ');
        }

        public int RowCount { get { return _names.Length; } }

        /// <summary>The whole row, or null when the id is out of range.</summary>
        public SkillRow RowAt(int skillId)
        {
            if (!SkillExists(skillId))
            {
                return null;
            }

            return new SkillRow(
                skillId, GetSkillName(skillId), GetSkillClass(skillId), RequiredLevel(skillId));
        }

        public bool SkillExists(int skillId)
        {
            return skillId >= 0 && skillId < RowCount;
        }

        SkillItemProc ISkillItemProcSource.GetItemProc(int skillId)
        {
            return skillId >= 0 && skillId < _itemProcs.Length ? _itemProcs[skillId] : null;
        }

        public string GetSkillName(int skillId)
        {
            return skillId >= 0 && skillId < _names.Length ? _names[skillId] : _sentinel;
        }

        public int GetSkillClass(int skillId)
        {
            return skillId >= 0 && skillId < _classes.Length ? _classes[skillId] : -1;
        }

        /// <summary>
        /// skills.txt "reqlevel" (+0x174). Out-of-range ids return -1: the caller at 0x62b952 tests
        /// the id against the record count and skips it, so a bad id contributes nothing.
        /// </summary>
        public int RequiredLevel(int skillId)
        {
            return skillId >= 0 && skillId < _requiredLevels.Length ? _requiredLevels[skillId] : -1;
        }

        /// <summary>
        /// SKILL_GetMaxLevelForSkill 0x4aa8b0 — skills.txt "maxlvl" (+0x12C), falling back to **20**
        /// both when the column is non-positive and when the id is out of range (0x4aa8d9). The
        /// fallback is the value, not an error code, so this never returns -1.
        /// </summary>
        public int MaxLevel(int skillId)
        {
            const int Default = 20;

            if (skillId < 0 || skillId >= _maxLevels.Length)
            {
                return Default;
            }

            return _maxLevels[skillId] > 0 ? _maxLevels[skillId] : Default;
        }
    }

    public sealed class TxtCharacterClassTable : ICharacterClassTable
    {
        /// <summary>charstats.txt carries three tab-name columns; GetSkillTabText bounds on this.</summary>
        public const int SkillTabsPerClass = 3;

        private readonly string[] _allSkills;
        private readonly string[][] _skillTabs;
        private readonly string[] _classOnly;

        /// <summary>charstats.txt rows, so a caller can iterate the classes.</summary>
        public int RowCount
        {
            get { return _allSkills.Length; }
        }

        public TxtCharacterClassTable(TxtFile file, StringTable strings)
        {
            if (file == null) throw new ArgumentNullException("file");
            if (strings == null) throw new ArgumentNullException("strings");

            _allSkills = new string[file.RowCount];
            _skillTabs = new string[file.RowCount][];
            _classOnly = new string[file.RowCount];

            for (int row = 0; row < file.RowCount; ++row)
            {
                _allSkills[row] = Text(file, row, "StrAllSkills", strings);
                _classOnly[row] = Text(file, row, "StrClassOnly", strings);
                _skillTabs[row] = new[]
                {
                    Text(file, row, "StrSkillTab1", strings),
                    Text(file, row, "StrSkillTab2", strings),
                    Text(file, row, "StrSkillTab3", strings),
                };
            }
        }

        private static string Text(TxtFile file, int row, string column, StringTable strings)
        {
            return TxtKeys.Text(file, row, column, strings);
        }

        /// <summary>The whole row, or null when the id is out of range.</summary>
        public CharacterClassRow RowAt(int classId)
        {
            if (!ClassExists(classId))
            {
                return null;
            }

            var tabs = new string[SkillTabsPerClass];
            for (int tab = 0; tab < tabs.Length; ++tab)
            {
                tabs[tab] = GetSkillTabText(classId, tab);
            }

            return new CharacterClassRow(
                classId, GetAllSkillsText(classId), GetClassOnlyText(classId), tabs);
        }

        public bool ClassExists(int classId)
        {
            return classId >= 0 && classId < _allSkills.Length;
        }

        public string GetAllSkillsText(int classId)
        {
            return classId >= 0 && classId < _allSkills.Length ? _allSkills[classId] : null;
        }

        public string GetSkillTabText(int classId, int tabIndex)
        {
            if (classId < 0 || classId >= _skillTabs.Length || tabIndex < 0 || tabIndex > 2)
            {
                return null;
            }

            return _skillTabs[classId][tabIndex];
        }

        public string GetClassOnlyText(int classId)
        {
            return classId >= 0 && classId < _classOnly.Length ? _classOnly[classId] : null;
        }
    }

    public sealed class TxtMonsterTypeTable : IMonsterTypeTable
    {
        private readonly string[] _typeNames;
        private readonly string[] _monsterNames;

        /// <summary>MonType.txt rows.</summary>
        public int MonsterTypeCount
        {
            get { return _typeNames.Length; }
        }

        /// <summary>monstats.txt rows.</summary>
        public int MonsterCount
        {
            get { return _monsterNames.Length; }
        }

        public TxtMonsterTypeTable(TxtFile monType, TxtFile monStats, StringTable strings)
        {
            if (strings == null) throw new ArgumentNullException("strings");

            int typeRows = monType == null ? 0 : monType.RowCount;
            _typeNames = new string[typeRows];

            for (int row = 0; row < typeRows; ++row)
            {
                _typeNames[row] = TxtKeys.Text(monType, row, "strplur", strings);
            }

            int monsterRows = monStats == null ? 0 : monStats.RowCount;
            _monsterNames = new string[monsterRows];

            for (int row = 0; row < monsterRows; ++row)
            {
                _monsterNames[row] = TxtKeys.Text(monStats, row, "NameStr", strings);
            }
        }

        /// <summary>A MonType.txt row, or null when the id is out of range.</summary>
        public MonsterTypeRow MonsterTypeAt(int monsterTypeId)
        {
            return monsterTypeId < 0 || monsterTypeId >= MonsterTypeCount
                ? null
                : new MonsterTypeRow(monsterTypeId, GetMonsterTypeName(monsterTypeId));
        }

        /// <summary>A monstats.txt row, or null when the id is out of range.</summary>
        public MonsterRow MonsterAt(int monsterId)
        {
            return monsterId < 0 || monsterId >= MonsterCount
                ? null
                : new MonsterRow(monsterId, GetMonsterName(monsterId));
        }

        public bool MonsterTypeExists(int monsterTypeId)
        {
            return _typeNames.Length > 0;
        }

        public string GetMonsterTypeName(int monsterTypeId)
        {
            if (_typeNames.Length == 0)
            {
                return null;
            }

            if (monsterTypeId < 0 || monsterTypeId >= _typeNames.Length)
            {
                return _typeNames[0];
            }

            return _typeNames[monsterTypeId];
        }

        // TXT_MonStats_GetLine does a plain range check, so this is one.
        public bool MonsterExists(int monsterId)
        {
            return monsterId >= 0 && monsterId < _monsterNames.Length;
        }

        public string GetMonsterName(int monsterId)
        {
            return monsterId >= 0 && monsterId < _monsterNames.Length ? _monsterNames[monsterId] : null;
        }
    }
}
