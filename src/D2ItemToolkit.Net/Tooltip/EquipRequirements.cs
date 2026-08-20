using System.Collections.Generic;

namespace D2ItemToolkit
{
    /// <summary>
    /// ITEM_CheckEquipRequirements 0x62eaf0 — the three met flags LoadItemDesc colours its
    /// requirement lines with, plus the class comparison it does inline at 0x48e4a6.
    ///
    /// LoadItemDesc passes bCheckSockets = 0 (0x48e534), so the ITEMS_SumSocketedItemStats branch is
    /// unreachable from a tooltip and is not modelled here.
    /// </summary>
    internal sealed class EquipRequirements
    {
        public const int NoClassRestriction = 7;

        private const int StatStrength = 0;
        private const int StatDexterity = 2;
        private const int StatRequirementPercent = 91;
        private const int EtherealDiscount = 10;

        private readonly ItemTable _items;
        private readonly TxtFile _itemTypes;
        private readonly TxtSkillTable _skills;
        private readonly RequiredLevelCalculator _level;

        public EquipRequirements(D2DataFiles data, ItemTable items)
        {
            _items = items;
            _itemTypes = data.ItemTypes;
            _skills = data.Skills;
            _level = new RequiredLevelCalculator(data, items);
        }

        /// <summary>
        /// The displayed requirement: base + D2ApplyPercent(base, stat 91, 100), less 10 when
        /// ethereal. The identical expression drives the number at 0x48e65f and the comparison at
        /// 0x62eb8c, so a line can never show a value the check disagrees with.
        /// </summary>
        public int Requirement(ItemIdentity item, string column, IDictionary<int, int> stats)
        {
            int required = _items.GetInt(item.ClassId, column);
            if (required <= 0)
            {
                return 0;
            }

            // Both sites skip D2ApplyPercent entirely when the percent is zero (0x48e651).
            int percent = Stat(stats, StatRequirementPercent);
            int total = percent != 0 ? required + ApplyPercent(required, percent) : required;

            if (item.Has(ItemRecordFlags.Ethereal))
            {
                total -= EtherealDiscount;
            }

            return total;
        }

        /// <summary>
        /// 0x62ebcf. A viewer with no strength at all fails, and otherwise the check is a plain
        /// greater-or-equal against the same total the line displays.
        /// </summary>
        public bool MetStrength(
            ItemIdentity item, ItemViewer viewer, IDictionary<int, int> stats)
        {
            return MetAttribute(
                Requirement(item, "reqstr", stats), Attribute(viewer, StatStrength));
        }

        public bool MetDexterity(
            ItemIdentity item, ItemViewer viewer, IDictionary<int, int> stats)
        {
            return MetAttribute(
                Requirement(item, "reqdex", stats), Attribute(viewer, StatDexterity));
        }

        private static bool MetAttribute(int required, int available)
        {
            return available > 0 && available >= required;
        }

        /// <summary>
        /// 0x62ec88. Level uses the player's own level rather than an attribute stat.
        /// </summary>
        public bool MetLevel(
            ItemIdentity item,
            ItemViewer viewer,
            IDictionary<int, int> stats,
            IList<ItemUnit> socketUnits,
            IDictionary<int, uint> sockets)
        {
            int required = _level.Calculate(item, viewer, stats, socketUnits, sockets);
            return (viewer == null ? 0 : viewer.Level) >= required;
        }

        /// <summary>
        /// 0x48e4a6 compares the player unit's class id straight against the restriction with no
        /// unit-type test, so a non-player viewer whose class id happens to match reads as met.
        /// </summary>
        public bool MetClass(ItemIdentity item, ItemViewer viewer)
        {
            int restriction = ClassRestriction(item);
            if (restriction == NoClassRestriction)
            {
                return true;
            }

            return (viewer == null ? -1 : viewer.ClassId) == restriction;
        }

        /// <summary>
        /// TXT_ItemTypes_GetClass 0x62c0b0: the PRIMARY type row's Class column as a byte, with 7
        /// meaning unrestricted. Anything at or above 7 collapses to 7 (0x62c0ef).
        /// </summary>
        public int ClassRestriction(ItemIdentity item)
        {
            if (_itemTypes == null || _skills == null)
            {
                return NoClassRestriction;
            }

            int row = RowFor(_items.PrimaryTypeCode(item.ClassId));
            if (row < 0 || !_itemTypes.HasColumn("Class"))
            {
                return NoClassRestriction;
            }

            string code = _itemTypes.GetString(row, "Class");
            if (string.IsNullOrEmpty(code.Trim()))
            {
                return NoClassRestriction;
            }

            int classId = _skills.ClassIdForCode(code);
            return classId >= 0 && classId < NoClassRestriction ? classId : NoClassRestriction;
        }

        private int RowFor(string code)
        {
            if (string.IsNullOrEmpty(code) || !_itemTypes.HasColumn("Code"))
            {
                return -1;
            }

            for (int row = 0; row < _itemTypes.RowCount; ++row)
            {
                if (string.Equals(
                        _itemTypes.GetString(row, "Code").Trim(), code.Trim(),
                        System.StringComparison.OrdinalIgnoreCase))
                {
                    return row;
                }
            }

            return -1;
        }

        private static int Attribute(ItemViewer viewer, int statId)
        {
            if (viewer == null)
            {
                return 0;
            }

            return statId == StatStrength ? viewer.Strength : viewer.Dexterity;
        }

        private static int Stat(IDictionary<int, int> stats, int statId)
        {
            int value;
            return stats != null
                   && stats.TryGetValue(ItemStatReader.PackStatKey(0, statId), out value)
                ? value
                : 0;
        }

        private static int ApplyPercent(int value, int percent)
        {
            return (int)((long)value * percent / 100);
        }
    }
}
