using System.Collections.Generic;

namespace D2ItemToolkit
{
    /// <summary>
    /// ITEM_CalcWeaponAttackSpeed 0x62a710.
    ///
    /// The animation name is built by COMPOSIT_BuildCofPath 0x64f5b0 with bFullPath = 0 and
    /// bResolveWeaponClass = 0 (both pushed as 0 at 0x62a78c/0x62a78e), so no .COF file is opened.
    /// 0x64f5b0 switches on the UNIT TYPE, pushed from *pUnit at 0x62a79f, and the two branches that
    /// matter build the name from different tables:
    ///
    ///   unit type 0, player (0x64f859)
    ///       PlrType[classId].Token + PlrMode[7].Token + ItemsTxt[classId].wclass
    ///     The weapon class is re-derived only `if (a9)` (0x64f879) and a9 is 0 here, so *a7 keeps
    ///     the caller's items.txt wclass. e.g. a Paladin swinging a one-handed sword: "PAA11HS".
    ///
    ///   unit type 1, monster — which is what a MERCENARY is (0x64f6db)
    ///       MonStats[classId].Code + MonMode[7].token + MonStats2[MonStatsEx].BaseW
    ///     Here the weapon class IS re-derived, unconditionally for this mode, and the item's own
    ///     wclass is overwritten. e.g. an Act 2 mercenary: "GUSChth" — whatever it is holding.
    ///
    /// Unit type 2 is objects (0x64f5d7); any other value falls out of the switch at 0x64f5d1 with
    /// the name buffer never written, so there is nothing to model.
    ///
    /// Mode 7 is untouched by either substitution table: the player's (dword_745904, count 2)
    /// rewrites modes 18 and 19, the monster's (dword_745918, count 1) rewrites mode 13, all three
    /// to 'gh  '.
    /// </summary>
    internal sealed class AttackSpeedCalculator
    {
        /// <summary>
        /// The mode pushed at 0x62a7a2. It indexes PlrMode for a player and MonMode for a monster,
        /// and the two files disagree about row 7: PlrMode row 7 is Attack1 ("A1"), MonMode row 7 is
        /// Cast ("SC"). A monster therefore looks its attack speed up under its CAST animation.
        /// </summary>
        public const int AttackMode = 7;

        /// <summary>0x62a7c5: a missing AnimData record makes the whole function return 45.</summary>
        public const int MissingAnimationSpeed = 45;

        // The unit types COMPOSIT_BuildCofPath 0x64f5b0 has branches for.
        private const int UnitTypePlayer = 0;
        private const int UnitTypeMonster = 1;

        // 'hth ' — the literal COMPOSIT_ResolveWeaponClass falls back to at 0x64f0a2 and 0x64f0d2,
        // and COMPOSIT_BuildCofPath writes directly at 0x64f758 when it skips the resolve entirely.
        private const string HandToHandWeaponClass = "hth";

        // 0x62a7ff reads stat 68, which ItemStatCost row 68 names `attackrate` — NOT
        // `velocitypercent`, which is row 67. The two sit next to each other and the wrong one
        // would still produce plausible numbers, so the id is what matters here, not the name.
        private const int StatAttackRate = 68;
        private const int StatFasterAttackRate = 93;

        private readonly ItemTable _items;
        private readonly AnimDataFile _animData;
        private readonly TxtFile _playerTypes;
        private readonly TxtFile _playerModes;
        private readonly TxtFile _monsterStats;
        private readonly TxtFile _monsterStats2;
        private readonly TxtFile _monsterModes;

        public AttackSpeedCalculator(D2DataFiles data, ItemTable items)
        {
            _items = items;
            _animData = data.AnimData;
            _playerTypes = data.PlayerTypes;
            _playerModes = data.PlayerModes;
            _monsterStats = data.MonsterStats;
            _monsterStats2 = data.MonsterStats2;
            _monsterModes = data.MonsterModes;
        }

        public bool CanCalculate
        {
            get { return _animData != null && _animData.RowCount > 0; }
        }

        /// <summary>
        /// The animation name, or null when the tables cannot supply one.
        /// </summary>
        public string AnimationName(ItemIdentity item, ItemViewer viewer)
        {
            if (!CanCalculate || viewer == null)
            {
                return null;
            }

            switch (viewer.UnitType)
            {
                case UnitTypePlayer:
                    return PlayerAnimationName(item, viewer);

                case UnitTypeMonster:
                    return MonsterAnimationName(viewer);

                default:
                    return null;
            }
        }

        // 0x64f859. TxtGetPlrTypeModeLine 0x65b480 indexes ONE array holding PlrType followed by
        // PlrMode (concatenated at 0x65ae91/0x65aeaa), selector 0 for the type and 1 for the mode.
        private string PlayerAnimationName(ItemIdentity item, ItemViewer viewer)
        {
            string token = Token(_playerTypes, viewer.ClassId);
            string mode = Token(_playerModes, AttackMode);
            string weaponClass = Trim(_items.GetString(item.ClassId, "wclass"));

            if (token == null || mode == null || weaponClass == null)
            {
                return null;
            }

            return token + mode + weaponClass;
        }

        // 0x64f6db. The monstats record is off_744304[670] + 424 * classId and the token is the
        // DWORD at +16, which the monstats field table registers as `Code`. TxtGetMonModeLine
        // 0x65b500 points both of its selectors at the SAME monmode array (0x65b19f/0x65b1a4), so
        // the mode token is monmode +32, `token`.
        private string MonsterAnimationName(ItemViewer viewer)
        {
            if (_monsterStats == null
                || viewer.ClassId < 0
                || viewer.ClassId >= _monsterStats.RowCount)
            {
                return null;
            }

            string token = Trim(_monsterStats.GetString(viewer.ClassId, "Code"));
            string mode = Token(_monsterModes, AttackMode);

            if (token == null || mode == null)
            {
                return null;
            }

            return token + mode + MonsterWeaponClass(viewer.ClassId);
        }

        /// <summary>
        /// COMPOSIT_ResolveWeaponClass 0x64f060 case 1. The item is not consulted at all: the class
        /// is monstats2's `BaseW` (+16), reached through the monstats `MonStatsEx` link (+24,
        /// TXT_MonStats_GetMonStats2 0x451fe0).
        ///
        /// The 'hth ' arm at 0x64f0cd only fires for mode 0 or 12 with monstats2 flag bit 16 clear,
        /// and 0x64f730 skips the call entirely under the same condition, so mode 7 never reaches
        /// either. What DOES remain reachable is the missing-record arm at 0x64f09b — though not
        /// with shipped data: all 734 monstats.txt rows resolve MonStatsEx to a monstats2 row.
        /// </summary>
        private string MonsterWeaponClass(int classId)
        {
            if (_monsterStats2 == null)
            {
                return HandToHandWeaponClass;
            }

            string link = _monsterStats.GetString(classId, "MonStatsEx");
            int row = link.Length == 0 ? -1 : _monsterStats2.FindRow("Id", link);

            return row < 0
                ? HandToHandWeaponClass
                : Trim(_monsterStats2.GetString(row, "BaseW"));
        }

        /// <summary>
        /// Returns false when the speed cannot be derived at all (no viewer or no tables); a missing
        /// AnimData record is NOT a failure — it yields 45, exactly as the binary does.
        /// </summary>
        public bool TryCalculate(
            ItemIdentity item, ItemViewer viewer, IDictionary<int, int> stats, out int speed)
        {
            speed = 0;

            string name = AnimationName(item, viewer);
            if (name == null)
            {
                return false;
            }

            AnimDataFile.Record record;
            if (!_animData.TryGet(name, out record))
            {
                speed = MissingAnimationSpeed;
                return true;
            }

            // 0x62a7df halts on a zero frame count rather than dividing by it.
            if (record.FramesPerDirection == 0)
            {
                return false;
            }

            int rate = Stat(stats, StatFasterAttackRate) + 100 + Stat(stats, StatAttackRate);
            int divisor = record.AnimationSpeed * rate / 100;
            if (divisor == 0)
            {
                return false;
            }

            speed = (record.FramesPerDirection << 8) / divisor;
            return true;
        }

        private static string Token(TxtFile table, int row)
        {
            if (table == null || row < 0 || row >= table.RowCount || !table.HasColumn("Token"))
            {
                return null;
            }

            return Trim(table.GetString(row, "Token"));
        }

        // COMPOSIT_BuildCofPath turns each SPACE into a NUL as it copies the three code bytes
        // (0x64f908 for the player, 0x64f78a for the monster), so a code shorter than three
        // characters simply ends early.
        private static string Trim(string code)
        {
            if (code == null)
            {
                return null;
            }

            int length = 0;
            while (length < code.Length && length < 3 && code[length] != ' ')
            {
                ++length;
            }

            return code.Substring(0, length);
        }

        private static int Stat(IDictionary<int, int> stats, int statId)
        {
            int value;
            return stats != null
                   && stats.TryGetValue(ItemStatReader.PackStatKey(0, statId), out value)
                ? value
                : 0;
        }
    }
}
