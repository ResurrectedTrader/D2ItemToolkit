using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace D2ItemToolkit
{
    /// <summary>D2R's string table, built from the <c>lng/strings</c> JSON files.</summary>
    public sealed class JsonStringTable : StringTable
    {
        // Load order, from the pointer array at 0x1415747e0. vo.json is the 18th; the embedded copy
        // leaves it out — no id or key in it collides with any other file, so no lookup changes.
        public static readonly string[] HdFiles =
        {
            "bnet.json", "item-gems.json", "item-modifiers.json", "item-nameaffixes.json",
            "item-names.json", "item-runes.json", "keybinds.json", "levels.json", "mercenaries.json",
            "monsters.json", "npcs.json", "objects.json", "quests.json", "shrines.json", "skills.json",
            "ui.json", "ui-controller.json", "vo.json", "commands.json",
        };

        // 0x141574880, loaded FIRST when legacy graphics are on (0x14047776b), then HdFiles on top.
        public static readonly string[] LegacyFiles =
        {
            "bnet.json", "item-gems.json", "item-modifiers.json", "item-nameaffixes.json",
            "item-names.json", "item-runes.json", "keybinds.json", "levels.json", "mercenaries.json",
            "monsters.json", "npcs.json", "objects.json", "quests.json", "shrines.json", "skills.json",
            "ui.json", "vo.json",
        };

        // off_141574770, indexed by the locale setting.
        public static readonly string[] Languages =
        {
            "enUS", "deDE", "esES", "frFR", "itIT", "koKR", "plPL", "ruRU", "zhCN", "zhTW", "esMX",
            "jaJP", "ptBR",
        };

        public const string MissingStringKey = "strMissingString";

        // What render-time lookups read: legacy-first when legacy graphics are on.
        private readonly Table _text = new Table();

        // DATATBLS_LoadAllTxts runs once at startup (0x140063844), before the only writer of
        // g_IsRunningLecgacyGfx (0x14061d604), so every load-time key resolution — the type-24
        // field resolver (0x1401f88f1) and the name loaders that test the same flag — reads the HD
        // table whatever the setting.
        private readonly Table _load;

        private readonly string _missing;

        /// <summary>
        /// The locale SETTING. Legacy graphics may read another column (enUS), but item names
        /// still take this locale's possessive (sub_140478a70 is passed STRTABLE_GetLanguage).
        /// </summary>
        public string Language { get; private set; }

        /// <param name="source">Returns a file's bytes by name, or null when absent.</param>
        /// <param name="legacySource">The strings-legacy directory, read only when <paramref name="legacy"/>.</param>
        /// <param name="legacy">Legacy graphics on: strings-legacy is loaded first and wins.</param>
        /// <param name="language">A locale column, one of <see cref="Languages"/>.</param>
        public JsonStringTable(
            Func<string, byte[]> source,
            Func<string, byte[]> legacySource,
            bool legacy,
            string language)
        {
            if (source == null) throw new ArgumentNullException("source");

            int locale = Array.IndexOf(Languages, language);
            if (locale < 0)
            {
                throw new ArgumentException("Unknown D2R language: " + language, "language");
            }

            Language = language;

            // 0x140476dbe: legacy mode reads enUS for every locale but 0-6 and 9.
            string column = !legacy || locale <= 6 || locale == 9 ? Languages[locale] : "enUS";

            if (legacy)
            {
                if (legacySource == null) throw new ArgumentNullException("legacySource");
                foreach (string name in LegacyFiles)
                {
                    _text.LoadFile(legacySource(name), column, true);
                }
            }

            foreach (string name in HdFiles)
            {
                _text.LoadFile(source(name), column, legacy);
            }

            if (legacy)
            {
                // The HD table's own load (sub_1404776E0(0)) never forces enUS.
                _load = new Table();
                foreach (string name in HdFiles)
                {
                    _load.LoadFile(source(name), language, false);
                }
            }
            else
            {
                _load = _text;
            }

            // Each table's load ends by copying its strMissingString into g_pStringTable
            // (0x140477a5c), the fallback of every by-index and by-key miss. The HD table loads
            // after the legacy one (0x140477ac5), so its text is the one that stays.
            _missing = _load.Text(MissingStringKey) ?? string.Empty;
        }

        private sealed class Table
        {
            private struct KeyNode
            {
                public ushort Id;
                public string Text;
            }

            private struct Pending
            {
                public ushort Id;
                public string Key;
                public string Text;
            }

            private readonly Dictionary<string, KeyNode> _byKey =
                new Dictionary<string, KeyNode>(StringComparer.Ordinal);

            private readonly Dictionary<ushort, string> _byId = new Dictionary<ushort, string>();

            // sub_140476c60. Two passes per file: parse into a pending list, then insert. An entry
            // without an id, a string Key or the language field abandons the WHOLE file, pending
            // entries included (0x140476e86 / 0x140476ebd / 0x140476f38 all reach 0x1404773c3).
            public void LoadFile(byte[] bytes, string column, bool legacy)
            {
                if (bytes == null)
                {
                    return;
                }

                // The shipped files carry a UTF-8 byte-order mark, which the reader rejects.
                int start = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF
                    ? 3
                    : 0;

                var pending = new List<Pending>();
                using (JsonDocument document = JsonDocument.Parse(
                    new ReadOnlyMemory<byte>(bytes, start, bytes.Length - start),
                    new JsonDocumentOptions { AllowTrailingCommas = true }))
                {
                    if (document.RootElement.ValueKind != JsonValueKind.Array)
                    {
                        throw new InvalidDataException("A D2R string file must be a JSON array.");
                    }

                    foreach (JsonElement entry in document.RootElement.EnumerateArray())
                    {
                        JsonElement idElement, keyElement, textElement;
                        if (entry.ValueKind != JsonValueKind.Object ||
                            !entry.TryGetProperty("id", out idElement) ||
                            !entry.TryGetProperty("Key", out keyElement) ||
                            keyElement.ValueKind != JsonValueKind.String ||
                            !entry.TryGetProperty(column, out textElement) ||
                            textElement.ValueKind != JsonValueKind.String)
                        {
                            return;
                        }

                        var item = new Pending
                        {
                            Id = unchecked((ushort)idElement.GetInt32()),
                            Key = keyElement.GetString() ?? string.Empty,
                            // 0x140476f77 rewrites every `ÿc` to U+E07E, the game's internal marker.
                            // This library spells the marker `ÿc`, so the same normalisation runs
                            // the other way.
                            Text = (textElement.GetString() ?? string.Empty).Replace("", "ÿc"),
                        };

                        // 0x140476f85-0x14047701b: the legacy table refuses an entry whose id OR key
                        // it already holds, so the legacy files take precedence over the HD ones.
                        if (legacy && (_byId.ContainsKey(item.Id) || _byKey.ContainsKey(item.Key)))
                        {
                            continue;
                        }

                        pending.Add(item);
                    }
                }

                // 0x1404772e9: an entry whose id OR key is already held is skipped outright — the
                // key node is only created on a miss (0x1404773f0), and the id goes in with it — so
                // the FIRST entry for a key wins and a duplicate's id never enters the table.
                foreach (Pending item in pending)
                {
                    if (_byId.ContainsKey(item.Id) || _byKey.ContainsKey(item.Key))
                    {
                        continue;
                    }

                    _byKey[item.Key] = new KeyNode { Id = item.Id, Text = item.Text };
                    _byId[item.Id] = item.Text;
                }
            }

            public string Text(ushort id)
            {
                string text;
                return _byId.TryGetValue(id, out text) ? text : null;
            }

            public string Text(string key)
            {
                KeyNode node;
                return key != null && _byKey.TryGetValue(key, out node) ? node.Text : null;
            }

            public int IdOf(string key)
            {
                KeyNode node;
                return key != null && _byKey.TryGetValue(key, out node) ? node.Id : -1;
            }
        }

        /// <summary>LANG_GetStringFromTblIndex 0x140477c70: a 16-bit id, or the missing-string text.</summary>
        public override string GetByIndex(int index)
        {
            return _text.Text(unchecked((ushort)index)) ?? _missing;
        }

        /// <summary>The id a txt cell was given at load — from the HD table, even under legacy text.</summary>
        public override int GetIndexByKey(string key)
        {
            return _load.IdOf(key);
        }

        /// <summary>DATATBLS_GetStringIdFromReferenceString 0x1401f88b0. A blank cell misses too.</summary>
        public override int ResolveKey(string key)
        {
            int index = GetIndexByKey(key ?? string.Empty);
            return index >= 0 ? index : DescStringIds.DescStr2Sentinel;
        }

        /// <summary>LANG_GetWideStringFromKey 0x140477d70: exact, case-sensitive.</summary>
        public override string GetByKey(string key)
        {
            return _text.Text(key) ?? _missing;
        }

        /// <summary>Whether <see cref="GetByKey"/> finds the key rather than falling back.</summary>
        public override bool HasKey(string key)
        {
            return _text.Text(key) != null;
        }
    }
}
