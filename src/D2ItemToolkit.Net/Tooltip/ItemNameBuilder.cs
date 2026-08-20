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

        public ItemNameBuilder(D2DataFiles data, ItemTable items, ItemTypeTree types = null)
        {
            if (data == null) throw new ArgumentNullException("data");
            if (items == null) throw new ArgumentNullException("items");

            _data = data;
            _items = items;
            _types = types ?? (data.ItemTypes == null ? null : new ItemTypeTree(data.ItemTypes));
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
            return PersonalizeWholeName(item, name);
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
                return baseName + Str(DescStringIds.Newline)
                       + ItemTooltipColor.Marker + "4" + Str(item.MagicPrefix[0]);
            }

            // 0x48c1f1: unidentified items show the base name only, whatever the quality.
            if (!item.Has(ItemRecordFlags.Identified))
            {
                return baseName;
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
        private static string PersonalizeWholeName(ItemIdentity item, string name)
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

        private const int WholeNameBudget = 512;

        /// <summary>
        /// INV_FormatPlayerNameWithBase 0x484d30, the form the set, unique and rare arms call on
        /// one PIECE of the name. It has no quality test of its own and its RESULT flag is the
        /// personalized flag alone (0x484d94 versus 0x484da0) — the arms branch on that, not on
        /// whether the possessive fitted the budget.
        /// </summary>
        private static bool TryPersonalizePart(ItemIdentity item, string part, out string named)
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

            return baseName;
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
            bool tome = IsOfType(item, "book");

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

            text.Append(Str(NameStringIds.EarLevelLabel))
                .Append(Str(DescStringIds.Space))
                .Append(item.EarLevel)
                .Append(newline);

            // 0x48c3b9: the ear's fileIndex IS the dead player's class.
            if (item.FileIndex >= 0 && item.FileIndex < NameStringIds.ClassName.Length)
            {
                text.Append(Str(NameStringIds.ClassName[item.FileIndex])).Append(newline);
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
        private static string Possessive(string owner, string baseName, int budget)
        {
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

            string prefix = TxtKeys.Text(table, item.FileIndex, "Name", _data.Strings);
            return Format2(NameStringIds.LowQualityFormat, prefix, baseName);
        }

        // 0x48cba9. Prefix and suffix index the CONCATENATED magic affix array, 1-based.
        private string Magic(ItemIdentity item, string baseName)
        {
            string prefix = MagicAffix(item.MagicPrefix[0]);
            string suffix = MagicAffix(item.MagicSuffix[0]);

            return Format3(NameStringIds.MagicFormat, prefix, baseName, suffix);
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

            return baseName + Str(DescStringIds.Newline) + affixes;
        }

        // 0x48ca1c. Base name, newline, then the set item's own name wrapped in format 10089.
        private string Set(ItemIdentity item, string baseName)
        {
            TxtFile table = _data.SetItems;
            if (table == null || item.FileIndex < 0 || item.FileIndex >= table.RowCount)
            {
                return null;
            }

            string setName = TxtKeys.Text(table, item.FileIndex, "index", _data.Strings);
            if (string.IsNullOrEmpty(setName))
            {
                return null;
            }

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
            if (table == null || item.FileIndex < 0 || item.FileIndex >= table.RowCount)
            {
                return baseName;
            }

            string uniqueName = TxtKeys.Text(table, item.FileIndex, "index", _data.Strings);
            if (string.IsNullOrEmpty(uniqueName))
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
                    return TxtKeys.Text(table, at, "Name", _data.Strings) ?? string.Empty;
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
                    return TxtKeys.Text(table, at, "name", _data.Strings) ?? string.Empty;
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
            return Positional(Str(formatId), a, null, null);
        }

        private string Format2(int formatId, string a, string b)
        {
            return Positional(Str(formatId), a, b, null);
        }

        private string Format3(int formatId, string a, string b, string c)
        {
            return Positional(Str(formatId), a, b, c);
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
