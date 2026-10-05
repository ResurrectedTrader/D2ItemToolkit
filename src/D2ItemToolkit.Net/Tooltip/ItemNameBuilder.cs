using System;
using System.Text;

namespace D2ItemToolkit
{
    // GetItemName 0x48c060. Locale ids it uses; the format strings are POSITIONAL (%0 %1 %2),
    // not printf.
    internal static class NameStringIds
    {
        public const int SuperiorFormat = 1711;     // "%0 %1"
        public const int LowQualityFormat = 1712;   // "%0 %1"
        public const int MagicFormat = 1714;        // "%0 %1 %2"
        public const int GemmedFormat = 1715;       // "%0 %1"
        public const int RareFormat = 1718;         // "%0 %1"
        public const int Superior = 1727;           // "Superior"
        public const int Gemmed = 1728;             // "Gemmed"
        public const int BodyPartFormat = 1716;     // "%0 %1"
        public const int SetItemFormat = 10089;     // "%0"

        // The quality-2 ear arm at 0x48c2b3.
        public const int EarHardcore = 5126;        // extra line when the Named flag is set
        public const int EarLevelLabel = 4141;      // 0x102D

        // INV_GetInventoryPageName 0x484a70, indexed by the ear's fileIndex (the dead player's
        // class). Anything at 7 or above HALTS the game rather than falling back.
        public static readonly int[] ClassName = { 4011, 4010, 4009, 4008, 4007, 10097, 10098 };

        // D2R's ear switch gains the Warlock (27567); 8 and above append nothing.
        public static readonly int[] ResurrectedClassName =
        {
            4011, 4010, 4009, 4008, 4007, 10097, 10098, 27567,
        };

        // ITEMS_GetName 0x1401587ff: a one-affix magic name has its own format, by key.
        public const string MagicPrefixOnlyKey = "ItemNameMagicFormatPrefixOnly";
        public const string MagicSuffixOnlyKey = "ItemNameMagicFormatSuffixOnly";

        // D2R's per-table miss ids for the name keys resolved after load (0x140215973 and
        // siblings): affix, low-quality and rune names fall to strMissingString, unique and set
        // `index` cells to 5383.
        public const int MissingString = 11078;
        public const int MissingIndexName = 5383;

        // 0x48c542: tome versus scroll, by magic suffix. Suffix 0 and 1 are the only ones handled;
        // anything else leaves the switch having written NOTHING.
        public const int TomeFirst = 2199;
        public const int ScrollFirst = 2200;
        public const int TomeSecond = 2201;
        public const int ScrollSecond = 2202;
    }

    public static class ItemQualityNo
    {
        public const int Inferior = 1;
        public const int Normal = 2;
        public const int Superior = 3;
        public const int Magic = 4;
        public const int Set = 5;
        public const int Rare = 6;
        public const int Unique = 7;
        public const int Craft = 8;
        public const int Tempered = 9;
    }

    internal sealed class ItemNameBuilder
    {
        private readonly D2DataFiles _data;
        private readonly ItemTable _items;
        private readonly ItemTypeTree _types;
        private readonly bool _resurrected;

        // The D2R locale setting, which picks the possessive form.
        private readonly string _language;

        public ItemNameBuilder(D2DataFiles data, ItemTable items, ItemTypeTree types = null)
        {
            if (data == null) throw new ArgumentNullException("data");
            if (items == null) throw new ArgumentNullException("items");

            _data = data;
            _items = items;
            _types = types ?? (data.ItemTypes == null ? null : new ItemTypeTree(data.ItemTypes));
            _resurrected = data.IsResurrected;
            _language = data.ResurrectedLanguage ?? "enUS";
        }

        // Returns the name, or null when the arm writes nothing. Runeword handling and the two-line
        // runeword form are not modelled here; the quest COLOUR is the composer's, not the text's.
        //
        // `filledSockets` is ITEM_ItemsInItem(pInventory) (0x48c4b5), which only the normal arm
        // reads.
        public string Build(ItemIdentity item, int filledSockets = 0)
        {
            if (item == null)
            {
                return null;
            }

            string baseName = BaseName(item.ClassId);

            string name = Arm(item, baseName, filledSockets);

            // 0x48caff: INV_FormatPlayerNameOnItem rewrites the WHOLE buffer, whichever arm built
            // it — including the unidentified one, which reaches the tail through 0x48ce54.
            name = PersonalizeWholeName(item, name);

            // 0x14015847c: items.txt ShowLevel appends " (N)", with a level below 1 written back
            // as 1. No shipped row sets it.
            if (_resurrected && name != null && _items.GetInt(item.ClassId, "ShowLevel") != 0)
            {
                name += Str(DescStringIds.Space)
                        + CFormat.Sprintf("(%i)", item.ItemLevel < 1 ? 1 : item.ItemLevel);
            }

            return name;
        }

        private string Arm(ItemIdentity item, string baseName, int filledSockets)
        {
            // 0x48c10b/0x48c11a: the runeword flag is tested FIRST — before the identified test at
            // 0x48c1ea and before the quality jump table at 0x48c209 — so neither applies here and
            // a runeword is never "Superior" or "Gemmed".
            //
            // wMagicPrefix[0] is not an affix index on a runeword. ITEM_DeserializeFromBitBuffer
            // 0x62d1ea stores 16 bits straight into it, sourced from runes.txt +0x82, which
            // TXT_AllocTxt_runes 0x639c63 fills with STRTABLE_LookupString of the `Name` column.
            // That is a locale id in GetLocaleString's own space, so it resolves with GetByIndex
            // rather than through the affix tables (0x48c17a/0x48c17f/0x48c181).
            if (item.Has(ItemRecordFlags.Runeword))
            {
                // 0x140157a4a: D2R drops the base line for a UI preview item (flag bit 31).
                string runeword = ItemTooltipColor.Marker + "4" + Str(item.MagicPrefix[0]);
                if (_resurrected && ((uint)item.Flags & PreviewFlag) != 0)
                {
                    return runeword;
                }

                return Strip(baseName) + Str(DescStringIds.Newline) + runeword;
            }

            // 0x48c1f1: unidentified items show the base name only, whatever the quality.
            if (!item.Has(ItemRecordFlags.Identified))
            {
                return Strip(baseName);
            }

            switch (item.Quality)
            {
                case ItemQualityNo.Inferior:
                    return LowQuality(item, baseName);

                case ItemQualityNo.Superior:
                    return Format2(
                        NameStringIds.SuperiorFormat, Str(NameStringIds.Superior), baseName);

                case ItemQualityNo.Magic:
                    return Magic(item, baseName);

                case ItemQualityNo.Set:
                    return Set(item, baseName);

                case ItemQualityNo.Rare:
                case ItemQualityNo.Craft:
                case ItemQualityNo.Tempered:
                    return Rare(item, baseName);

                case ItemQualityNo.Unique:
                    return Unique(item, baseName);

                default:
                    return Normal(item, baseName, filledSockets);
            }
        }

        /// <summary>
        /// INV_FormatPlayerNameOnItem 0x484c90. It needs the PERSONALIZED flag (0x1000000) and a
        /// quality OUTSIDE 5..9 (0x484cb8, an unsigned `quality - 5 &lt;= 4` skip) — set, rare,
        /// unique, crafted and tempered personalise inside their own arms instead, through
        /// INV_FormatPlayerNameWithBase. The budget is 512 wide characters, not the ear's 100.
        /// </summary>
        private string PersonalizeWholeName(ItemIdentity item, string name)
        {
            if (name == null || !item.Has(ItemRecordFlags.Personalized))
            {
                return name;
            }

            if (item.Quality >= ItemQualityNo.Set && item.Quality <= ItemQualityNo.Tempered)
            {
                return name;
            }

            return Possessive(item.PlayerName, name, WholeNameBudget);
        }

        private const uint PreviewFlag = 0x80000000;

        private const int WholeNameBudget = 512;

        /// <summary>
        /// INV_FormatPlayerNameWithBase 0x484d30, the form the set, unique and rare arms call on
        /// one PIECE of the name. It has no quality test of its own and its RESULT flag is the
        /// personalized flag alone (0x484d94 versus 0x484da0) — the arms branch on that, not on
        /// whether the possessive fitted the budget.
        /// </summary>
        private bool TryPersonalizePart(ItemIdentity item, string part, out string named)
        {
            if (!item.Has(ItemRecordFlags.Personalized))
            {
                named = part;
                return false;
            }

            named = Possessive(item.PlayerName, part, WholeNameBudget);
            return true;
        }

        private string Normal(ItemIdentity item, string baseName, int filledSockets)
        {
            // The quality-2 arm tries four branches in this order (0x48c26e, 0x48c27e, 0x48c45a),
            // and only the last one reaches the socketed/plain fallback.
            if (IsOfType(item, "scro") || IsOfType(item, "book"))
            {
                return TomeOrScroll(item);
            }

            if (IsOfType(item, "play"))
            {
                return Ear(item, baseName);
            }

            if (IsOfType(item, "body"))
            {
                return MonsterBodyPart(item, baseName);
            }

            // 0x48c4b5: a three-way gate — the SOCKETED flag, a non-null pInventory, and
            // ITEM_ItemsInItem above zero. An EMPTY socketed item keeps its plain base name.
            if (item.Has(ItemRecordFlags.Socketed) && filledSockets > 0)
            {
                return Format2(NameStringIds.GemmedFormat, Str(NameStringIds.Gemmed), baseName);
            }

            return Strip(baseName);
        }

        /// <summary>
        /// 0x48c464 — a monster's body part. The item's fileIndex is a monstats row, and format 1716
        /// pairs that creature's NameStr with the part's own base name.
        ///
        /// The misc.txt `name` column labels these rows "Not used", but `namestr` resolves to real
        /// localised names ("Heart", "Brain", ...), so the arm produces sensible output. Whether such an
        /// item ever spawns in 1.14d is a separate question and not established here.
        /// </summary>
        private string MonsterBodyPart(ItemIdentity item, string baseName)
        {
            if (_data.MonsterTypes == null || !_data.MonsterTypes.MonsterExists(item.FileIndex))
            {
                return baseName;
            }

            string monster = _data.MonsterTypes.GetMonsterName(item.FileIndex);

            return string.IsNullOrEmpty(monster)
                ? baseName
                : Format2(NameStringIds.BodyPartFormat, monster, baseName);
        }

        /// <summary>
        /// 0x48c542. A tome and a scroll of the same spell differ only by the item's PRIMARY type
        /// being "book", and the spell comes from magic suffix slot 0. A suffix above 1 writes
        /// nothing at all — the switch breaks out with the buffer untouched.
        /// </summary>
        private string TomeOrScroll(ItemIdentity item)
        {
            // D2R decides by the PRIMARY type word == 18 (0x14015788b), not an IsOfType walk; on
            // shipped data the two agree.
            bool tome = _resurrected
                ? _types != null && _types.Row(_items.PrimaryTypeCode(item.ClassId)) == _types.Row("book")
                : IsOfType(item, "book");

            switch (item.MagicSuffix[0])
            {
                case 0:
                    return Str(tome ? NameStringIds.TomeFirst : NameStringIds.ScrollFirst);

                case 1:
                    return Str(tome ? NameStringIds.TomeSecond : NameStringIds.ScrollSecond);

                default:
                    return null;
            }
        }

        /// <summary>
        /// 0x48c2b3 — a player's ear. Four appended lines, which the bottom-up renderer then shows in
        /// reverse, so the possessive name ends up on top:
        ///
        ///     [locale 5126 when the Named flag is set]
        ///     locale 4141 + " " + earLevel
        ///     the dead player's class name, from fileIndex
        ///     "&lt;playerName&gt;'s &lt;base&gt;"
        /// </summary>
        private string Ear(ItemIdentity item, string baseName)
        {
            var text = new StringBuilder();
            string newline = Str(DescStringIds.Newline);

            // 0x48c346: the Named flag prepends an extra line ahead of everything else.
            if (item.Has(ItemRecordFlags.Named))
            {
                text.Append(Str(NameStringIds.EarHardcore)).Append(newline);
            }

            // D2R prints the level with snprintf into 3 bytes, so two characters at most.
            string level = item.EarLevel.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (_resurrected && level.Length > 2)
            {
                level = level.Substring(0, 2);
            }

            text.Append(Str(NameStringIds.EarLevelLabel))
                .Append(Str(DescStringIds.Space))
                .Append(level)
                .Append(newline);

            // 0x48c3b9: the ear's fileIndex IS the dead player's class.
            int[] classNames = _resurrected ? NameStringIds.ResurrectedClassName : NameStringIds.ClassName;
            if (item.FileIndex >= 0 && item.FileIndex < classNames.Length)
            {
                text.Append(Str(classNames[item.FileIndex])).Append(newline);
            }

            return text.Append(Possessive(item.PlayerName, baseName, EarBudget)).ToString();
        }

        // The ear arm's own call passes 100 (0x48c440), unlike the two personalisation helpers.
        private const int EarBudget = 100;

        /// <summary>
        /// UNICODE_FormatPossessiveName 0x5272b0 for language code 0. The suffix is the dword
        /// 0x00207327 stored at 0x52737f — apostrophe, 's', space. The other twelve language cases
        /// are NOT transcribed; French for instance prefixes " d'" (0x00276420 at 0x527467).
        ///
        /// When the two names together would not fit the caller's 100 wide characters it drops the
        /// possessive and yields the base name alone (0x5272f6).
        /// </summary>
        private string Possessive(string owner, string baseName, int budget)
        {
            if (_resurrected)
            {
                return ResurrectedPossessive(owner ?? string.Empty, baseName);
            }

            if (string.IsNullOrEmpty(owner))
            {
                return baseName;
            }

            // 0x5272e1: len(base) + len(owner) + 5 against the budget.
            if (baseName.Length + owner.Length + 5 > budget)
            {
                return baseName;
            }

            return owner + "'s " + baseName;
        }

        private const int ResurrectedPossessiveBudget = 0x400;

        /// <summary>
        /// sub_140478a70, switched on STRTABLE_GetLanguage for the locale SETTING (jump table
        /// 0x140478b31). The tests are on the owner's raw first or last byte, case-sensitive. One
        /// 1024-byte budget for every caller, and no empty-owner shortcut.
        /// </summary>
        private string ResurrectedPossessive(string owner, string baseName)
        {
            if (Utf8Length(baseName) + Utf8Length(owner) + 5 > ResurrectedPossessiveBudget)
            {
                return Utf8Length(baseName) < ResurrectedPossessiveBudget ? baseName : string.Empty;
            }

            char last = owner.Length == 0 ? '\0' : owner[owner.Length - 1];
            char first = owner.Length == 0 ? '\0' : owner[0];

            switch (_language)
            {
                case "enUS":
                    return owner + (last == 's' ? "' " : "'s ") + baseName;
                case "deDE":
                    return owner + (last == 's' || last == 'z' ? "' " : "s ") + baseName;
                case "esES":
                case "esMX":
                    return baseName + " de " + owner;
                case "frFR":
                    // " d'" carries no trailing space (0x1416480a0).
                    return baseName + ("aeiou".IndexOf(first) >= 0 && first != '\0' ? " d'" : " de ") + owner;
                case "itIT":
                    return baseName + " di " + owner;
                case "jaJP":
                    return owner + baseName;
                case "plPL":
                    // No space before the bracket and one after it (0x140478ed0-0x140478fdb).
                    return baseName + "(" + owner + ") ";
                default:
                    return "(" + owner + ") " + baseName;
            }
        }

        private static int Utf8Length(string text)
        {
            return Encoding.UTF8.GetByteCount(text ?? string.Empty);
        }

        /// <summary>
        /// D2R strips a leading grammar tag such as "[fs]" from a name before showing it: the last
        /// ']' with a '[' three characters before it. No enUS name carries one.
        /// </summary>
        private string Strip(string name)
        {
            if (!_resurrected || string.IsNullOrEmpty(name))
            {
                return name;
            }

            int close = name.LastIndexOf(']');
            return close >= 3 && name[close - 3] == '[' ? name.Substring(close + 1) : name;
        }

        private bool IsOfType(ItemIdentity item, string code)
        {
            if (_types == null)
            {
                return false;
            }

            return _types.IsOfType(
                _types.Row(_items.PrimaryTypeCode(item.ClassId)),
                _types.Row(_items.SecondaryTypeCode(item.ClassId)),
                _types.Row(code));
        }

        // 0x48c210. A null lowqualityitems row writes NOTHING (0x48c220) — reachable, since
        // dwFileIndex is 3 bits against only 4 rows.
        private string LowQuality(ItemIdentity item, string baseName)
        {
            TxtFile table = _data.LowQualityItems;
            if (table == null || item.FileIndex < 0 || item.FileIndex >= table.RowCount)
            {
                return null;
            }

            string prefix = NameText(table, item.FileIndex, "Name", NameStringIds.MissingString);
            return Format2(NameStringIds.LowQualityFormat, prefix, baseName);
        }

        // 0x48cba9. Prefix and suffix index the CONCATENATED magic affix array, 1-based.
        private string Magic(ItemIdentity item, string baseName)
        {
            string prefix = MagicAffix(item.MagicPrefix[0]);
            string suffix = MagicAffix(item.MagicSuffix[0]);

            // ITEMS_GetName 0x1401587ff-0x140158b06: a one-affix name has its own two-slot format,
            // so D2R has no stray space. "Has" is the record lookup succeeding (id in range), not
            // the text being non-empty.
            if (_resurrected)
            {
                bool hasPrefix = MagicAffixExists(item.MagicPrefix[0]);
                bool hasSuffix = MagicAffixExists(item.MagicSuffix[0]);

                if (hasPrefix && !hasSuffix)
                {
                    return ResurrectedFormat(
                        _data.Strings.GetByKey(NameStringIds.MagicPrefixOnlyKey), prefix, baseName);
                }

                if (hasSuffix && !hasPrefix)
                {
                    return ResurrectedFormat(
                        _data.Strings.GetByKey(NameStringIds.MagicSuffixOnlyKey), baseName, suffix);
                }
            }

            return Format3(NameStringIds.MagicFormat, prefix, baseName, suffix);
        }

        private bool MagicAffixExists(int id)
        {
            int count = 0;
            foreach (TxtFile table in new[] { _data.MagicSuffix, _data.MagicPrefix, _data.AutoMagic })
            {
                count += table == null ? 0 : table.RowCount;
            }

            return id > 0 && id <= count;
        }

        /// <summary>
        /// The name text of a key cell. D2R resolves these after load with its own miss ids
        /// (strMissingString for affix and low-quality names, 5383 for unique and set `index`)
        /// where 1.14d's converter gives 5382.
        /// </summary>
        private string NameText(TxtFile table, int row, string column, int resurrectedMiss)
        {
            if (!_resurrected)
            {
                return TxtKeys.Text(table, row, column, _data.Strings);
            }

            if (!table.HasColumn(column))
            {
                return Str(0);
            }

            int id = _data.Strings.GetIndexByKey(table.GetString(row, column));
            return Str(id >= 0 ? id : resurrectedMiss);
        }

        // 0x48c5c1. Rare, crafted and tempered are byte-for-byte identical arms. The base name is
        // FIRST, then a newline, then the two affixes.
        private string Rare(ItemIdentity item, string baseName)
        {
            string first = RareAffix(item.RarePrefix);
            string second = RareAffix(item.RareSuffix);

            // 0x48c8ea: the affix line — not the base name above it — is what gets personalised.
            string affixes;
            TryPersonalizePart(item, Format2(NameStringIds.RareFormat, first, second), out affixes);

            return Strip(baseName) + Str(DescStringIds.Newline) + affixes;
        }

        // 0x48ca1c. Base name, newline, then the set item's own name wrapped in format 10089.
        private string Set(ItemIdentity item, string baseName)
        {
            TxtFile table = _data.SetItems;
            if (table == null || item.FileIndex < 0 || item.FileIndex >= table.RowCount)
            {
                return null;
            }

            string setName = NameText(table, item.FileIndex, "index", NameStringIds.MissingIndexName);
            if (string.IsNullOrEmpty(setName))
            {
                return _resurrected ? Strip(baseName) + Str(DescStringIds.Newline) : null;
            }

            baseName = Strip(baseName);

            // 0x48cae3: when INV_FormatPlayerNameWithBase succeeds its text REPLACES the 10089
            // wrapper rather than being wrapped by it.
            string named;
            string tail = TryPersonalizePart(item, setName, out named)
                ? named
                : Format1(NameStringIds.SetItemFormat, setName);

            return baseName + Str(DescStringIds.Newline) + tail;
        }

        // 0x48c920. `SkipName` suppresses the base-name line; there is no format wrapper.
        private string Unique(ItemIdentity item, string baseName)
        {
            TxtFile table = _data.UniqueItems;
            baseName = Strip(baseName);
            if (table == null || item.FileIndex < 0 || item.FileIndex >= table.RowCount)
            {
                return baseName;
            }

            // D2R has no empty-name fallback to the base (0x140158383).
            string uniqueName = Strip(
                NameText(table, item.FileIndex, "index", NameStringIds.MissingIndexName));
            if (!_resurrected && string.IsNullOrEmpty(uniqueName))
            {
                return baseName;
            }

            // 0x48c9e1: the unique name alone is personalised, whether or not SkipName suppressed
            // the base line above it.
            string named;
            TryPersonalizePart(item, uniqueName, out named);

            if (_items.GetInt(item.ClassId, "SkipName") != 0)
            {
                return named;
            }

            return baseName + Str(DescStringIds.Newline) + named;
        }

        private string BaseName(int classId)
        {
            TxtFile file;
            int row;
            if (!_items.TryResolve(classId, out file, out row))
            {
                return string.Empty;
            }

            return TxtKeys.Text(file, row, "namestr", _data.Strings) ?? string.Empty;
        }

        // TXT_magicaffixes_GetLine 0x633ee0: 1-based over [MagicSuffix][MagicPrefix][automagic].
        private string MagicAffix(int id)
        {
            if (id <= 0)
            {
                return string.Empty;
            }

            int at = id - 1;

            foreach (TxtFile table in new[] { _data.MagicSuffix, _data.MagicPrefix, _data.AutoMagic })
            {
                if (table == null)
                {
                    continue;
                }

                if (at < table.RowCount)
                {
                    return NameText(table, at, "Name", NameStringIds.MissingString) ?? string.Empty;
                }

                at -= table.RowCount;
            }

            return string.Empty;
        }

        // TXT_RareAffixes_GetLine 0x634260: 1-based over [RareSuffix][RarePrefix].
        private string RareAffix(int id)
        {
            if (id <= 0)
            {
                return string.Empty;
            }

            int at = id - 1;

            foreach (TxtFile table in new[] { _data.RareSuffix, _data.RarePrefix })
            {
                if (table == null)
                {
                    continue;
                }

                if (at < table.RowCount)
                {
                    return NameText(table, at, "name", NameStringIds.MissingString) ?? string.Empty;
                }

                at -= table.RowCount;
            }

            return string.Empty;
        }

        private string Str(int id)
        {
            return _data.Strings.GetByIndex(id) ?? string.Empty;
        }

        private string Format1(int formatId, string a)
        {
            return _resurrected
                ? ResurrectedFormat(Str(formatId), a)
                : Positional(Str(formatId), a, null, null);
        }

        private string Format2(int formatId, string a, string b)
        {
            return _resurrected
                ? ResurrectedFormat(Str(formatId), a, b)
                : Positional(Str(formatId), a, b, null);
        }

        private string Format3(int formatId, string a, string b, string c)
        {
            return _resurrected
                ? ResurrectedFormat(Str(formatId), a, b, c)
                : Positional(Str(formatId), a, b, c);
        }

        /// <summary>
        /// sub_140251970, the name formatter with its grammar header. A format "a0n1:%0 %1" names
        /// an ADJECTIVE argument (the digit after the first 'a' before the colon) and a NOUN (after
        /// the first 'n'): the noun's leading 4-character tag ("[fs]", default "[ms]") is dropped
        /// and selects the adjective's variant — the text after that tag up to the next '['. An
        /// adjective without the tag, or not opening with '[', is used whole, tags and all, which
        /// is how the game prints "de la ballena [fs]Corona". Then argument i replaces the FIRST
        /// %i, and a space straight after the marker is dropped when the text so far ends in one.
        ///
        /// The game works on UTF-8 bytes; every byte this inspects ('[', ']', ':', '%', space and
        /// the tag letters) is ASCII on the item path, so chars give the same result. The 399/1023
        /// byte caps are beyond any name. For an empty argument substituted at position 0 the game
        /// tests a stack byte it never wrote (rsp+0x2F); only a magic item with no affix reaches
        /// that, and it is taken as "not a space".
        /// </summary>
        private static string ResurrectedFormat(string format, params string[] args)
        {
            if (format == null)
            {
                return string.Empty;
            }

            // The argument list ends at the first null; an empty string still counts.
            int count = 0;
            while (count < args.Length && args[count] != null)
            {
                ++count;
            }

            var slots = new string[Math.Max(count, 10)];
            for (int i = 0; i < slots.Length; ++i)
            {
                slots[i] = i < count ? args[i] : string.Empty;
            }

            string text = format;
            int colon = format.IndexOf(':');
            if (colon >= 0)
            {
                int adjective = HeaderIndex(format, 'a', colon);
                int noun = HeaderIndex(format, 'n', colon);

                if (adjective < 0)
                {
                    // 0x140251cb3: a noun alone loses its first four characters unconditionally.
                    if (noun >= 0 && noun < slots.Length)
                    {
                        slots[noun] = slots[noun].Length > 4 ? slots[noun].Substring(4) : string.Empty;
                    }
                }
                else if (adjective < slots.Length)
                {
                    string tag = DefaultGenderTag;
                    bool search = true;

                    if (noun >= 0 && noun < slots.Length)
                    {
                        // 0x140251b0f: only a noun opening with '[' carries a tag.
                        if (slots[noun].StartsWith("[", StringComparison.Ordinal))
                        {
                            tag = slots[noun].Substring(0, Math.Min(4, slots[noun].Length));
                            slots[noun] = slots[noun].Length > 4 ? slots[noun].Substring(4) : string.Empty;
                        }

                        // 0x140251be3: with a noun, the adjective must open with '[' to be split.
                        search = slots[adjective].StartsWith("[", StringComparison.Ordinal);
                    }

                    int at = search ? slots[adjective].IndexOf(tag, StringComparison.Ordinal) : -1;
                    if (at >= 0)
                    {
                        int start = at + 4;
                        int end = slots[adjective].IndexOf('[', start);
                        slots[adjective] = end < 0
                            ? slots[adjective].Substring(start)
                            : slots[adjective].Substring(start, end - start);
                    }
                }

                text = format.Substring(colon + 1);
            }

            for (int i = 0; i < count; ++i)
            {
                string marker = "%" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                int at = text.IndexOf(marker, StringComparison.Ordinal);
                if (at < 0)
                {
                    continue;
                }

                string head = text.Substring(0, at) + slots[i];
                string tail = text.Substring(at + marker.Length);
                if (head.Length != 0 && head[head.Length - 1] == ' '
                    && tail.StartsWith(" ", StringComparison.Ordinal))
                {
                    tail = tail.Substring(1);
                }

                text = head + tail;
            }

            return text;
        }

        // dword_14163c950: the tag a noun without one is taken to have.
        private const string DefaultGenderTag = "[ms]";

        // The digit after the first `letter` of the WHOLE format, if that letter sits before the
        // colon (0x140251a9b / 0x140251ac3); -1 otherwise.
        private static int HeaderIndex(string format, char letter, int colon)
        {
            int at = format.IndexOf(letter);
            if (at < 0 || at >= colon || at + 1 >= format.Length)
            {
                return -1;
            }

            return format[at + 1] - '0';
        }

        // The engine's POSITIONAL formatter: %0, %1, %2 select arguments. A missing argument leaves
        // an empty slot, which is why a magic item with one affix renders with a doubled space.
        // Internal because wsprintf 0x48be80 is the SAME routine the set-item piece list writes
        // through at 0x48d8dd, and two copies of it would be two things to keep in step.
        internal static string Positional(string format, string a, string b, string c)
        {
            if (string.IsNullOrEmpty(format))
            {
                return string.Empty;
            }

            var text = new StringBuilder();

            for (int i = 0; i < format.Length; ++i)
            {
                if (format[i] != '%' || i + 1 >= format.Length)
                {
                    text.Append(format[i]);
                    continue;
                }

                char which = format[i + 1];
                switch (which)
                {
                    case '0':
                        text.Append(a ?? string.Empty);
                        ++i;
                        break;
                    case '1':
                        text.Append(b ?? string.Empty);
                        ++i;
                        break;
                    case '2':
                        text.Append(c ?? string.Empty);
                        ++i;
                        break;
                    default:
                        text.Append(format[i]);
                        break;
                }
            }

            return text.ToString();
        }
    }
}
