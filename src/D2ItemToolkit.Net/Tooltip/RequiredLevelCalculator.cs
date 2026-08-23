using System.Collections.Generic;

namespace D2ItemToolkit
{
    /// <summary>
    /// ITEM_CalcRequiredLevel 0x62b5b0. Everything it reads is either in the record or in the excel
    /// tables, so the level is derived here rather than supplied by the producer.
    /// </summary>
    internal sealed class RequiredLevelCalculator
    {
        private const int StatLevelRequirement = 92;    // item_levelreq
        private const int StatSingleSkill = 107;         // item_singleskill
        private const int StatNonClassSkill = 97;        // item_nonclassskill

        private const int CraftedBase = 10;
        private const int CraftedPerAffix = 3;
        private const int OffClassSkillPenalty = 6;
        private const int LastPlayerClass = 6;

        private readonly D2DataFiles _data;
        private readonly ItemTable _items;
        private readonly MagicAffixTable _affixes;

        public RequiredLevelCalculator(D2DataFiles data, ItemTable items)
        {
            _data = data;
            _items = items;
            _affixes = new MagicAffixTable(data);
        }

        /// <summary>
        /// `socketUnits` carries the fillers as whole units, which is what the recursion at
        /// 0x62b901 needs. `sockets` is the classId-only view kept for callers that have nothing
        /// richer: it yields the same answer for gems and runes, whose items.txt levelreq is their
        /// only contribution, but misses a magic or rare JEWEL's affix requirement.
        /// </summary>
        public int Calculate(
            ItemIdentity item,
            ItemViewer viewer,
            IDictionary<int, int> stats,
            IList<ItemUnit> socketUnits,
            IDictionary<int, uint> sockets = null)
        {
            int result = FromQuality(item, viewer);

            // items.txt levelreq raises the floor for every quality (0x62b8d0).
            int baseRequirement = _items.RequiredLevel(item.ClassId);
            if (baseRequirement > result)
            {
                result = baseRequirement;
            }

            // 0x62b901 recurses the WHOLE calculation into every socketed item, so a filler's own
            // quality affixes and its stats 107/97/92 all reach the host.
            foreach (ItemUnit filler in Fillers(socketUnits, sockets))
            {
                int required = Calculate(filler.Identity, viewer, filler.Stats, filler.Items);
                if (required > result)
                {
                    result = required;
                }
            }

            result = RaiseForSkills(result, stats, StatSingleSkill, viewer, false);
            result = RaiseForSkills(result, stats, StatNonClassSkill, viewer, true);

            result += Stat(stats, StatLevelRequirement);

            return result <= 0 ? 0 : result;
        }

        private static IEnumerable<ItemUnit> Fillers(
            IList<ItemUnit> socketUnits, IDictionary<int, uint> sockets)
        {
            if (socketUnits != null)
            {
                return socketUnits;
            }

            var degraded = new List<ItemUnit>();
            if (sockets != null)
            {
                foreach (KeyValuePair<int, uint> socket in sockets)
                {
                    var identity = new ItemIdentity();
                    identity.ClassId = (int)socket.Value;
                    degraded.Add(new ItemUnit(identity));
                }
            }

            return degraded;
        }

        private int FromQuality(ItemIdentity item, ItemViewer viewer)
        {
            switch (item.Quality)
            {
                case ItemQualityNo.Magic:
                    return Magic(item, viewer);

                case ItemQualityNo.Set:
                    return TableRequirement(_data.SetItems, item.FileIndex);

                case ItemQualityNo.Rare:
                    return Rare(item, viewer);

                case ItemQualityNo.Unique:
                    return Unique(item, viewer);

                case ItemQualityNo.Craft:
                    return Crafted(item, viewer);

                default:
                    return 0;
            }
        }

        // 0x62b5f2. Only affix slot 0 is consulted, plus the automagic affix, and eax is zeroed at
        // 0x62b630 so the three folds start from 0.
        private int Magic(ItemIdentity item, ItemViewer viewer)
        {
            int result = _affixes.RaiseLevelRequirement(0, item.MagicPrefix[0], viewer);
            result = _affixes.RaiseLevelRequirement(result, item.MagicSuffix[0], viewer);
            return _affixes.RaiseLevelRequirement(result, item.AutoAffix, viewer);
        }

        // 0x62b651. All three prefix and suffix slots, then the automagic affix.
        private int Rare(ItemIdentity item, ItemViewer viewer)
        {
            int result = 0;

            for (int slot = 0; slot < ItemIdentity.MaxAffixSlots; ++slot)
            {
                result = _affixes.RaiseLevelRequirement(result, item.MagicPrefix[slot], viewer);
                result = _affixes.RaiseLevelRequirement(result, item.MagicSuffix[slot], viewer);
            }

            return _affixes.RaiseLevelRequirement(result, item.AutoAffix, viewer);
        }

        // 0x62b76b. The affix maximum plus 10, plus 3 for every affix row that resolves, capped one
        // below the class-0 maximum level from experience.txt (0x62b848 reads class 0 unconditionally).
        private int Crafted(ItemIdentity item, ItemViewer viewer)
        {
            int result = 0;
            int bonus = CraftedBase;

            for (int slot = 0; slot < ItemIdentity.MaxAffixSlots; ++slot)
            {
                result = _affixes.RaiseLevelRequirement(result, item.MagicPrefix[slot], viewer);
                result = _affixes.RaiseLevelRequirement(result, item.MagicSuffix[slot], viewer);

                if (Resolves(item.MagicPrefix[slot]))
                {
                    bonus += CraftedPerAffix;
                }

                if (Resolves(item.MagicSuffix[slot]))
                {
                    bonus += CraftedPerAffix;
                }
            }

            result += bonus;

            int cap = MaxCharacterLevel() - 1;
            return result > cap ? cap : result;
        }

        // 0x62b859. A classic-format unique shows no level requirement to a viewer without the
        // expansion flag (0x2000000 tested at 0x62b877).
        private int Unique(ItemIdentity item, ItemViewer viewer)
        {
            if (item.FileIndex < 0)
            {
                return 0;
            }

            if (viewer != null && !viewer.IsExpansion && item.Format == 0)
            {
                return 0;
            }

            return TableRequirement(_data.UniqueItems, item.FileIndex);
        }

        private static int TableRequirement(TxtFile table, int fileIndex)
        {
            if (table == null || fileIndex < 0 || fileIndex >= table.RowCount
                || !table.HasColumn("lvl req"))
            {
                return 0;
            }

            // Read as a signed 16-bit field and discarded when negative (0x62b8b3).
            int required = (short)table.GetInt(fileIndex, "lvl req");
            return required >= 0 ? required : 0;
        }

        // 0x62b927 / 0x62b984. The stat LAYER is the skill id. A granted skill from another class
        // costs six extra levels unless the viewer is a player of that very class.
        private int RaiseForSkills(
            int running, IDictionary<int, int> stats, int statId, ItemViewer viewer, bool offClass)
        {
            if (stats == null || _data.Skills == null)
            {
                return running;
            }

            foreach (KeyValuePair<int, int> entry in stats)
            {
                int layer;
                int stat;
                ItemStatReader.UnpackStatKey(entry.Key, out layer, out stat);
                if (stat != statId)
                {
                    continue;
                }

                int required = _data.Skills.RequiredLevel(layer);
                if (required < 0)
                {
                    continue;
                }

                if (offClass)
                {
                    int skillClass = _data.Skills.GetSkillClass(layer);
                    bool ownClass = viewer != null
                                    && viewer.IsPlayer
                                    && skillClass >= 0
                                    && skillClass <= LastPlayerClass
                                    && viewer.ClassId == skillClass;

                    if (!ownClass)
                    {
                        required += OffClassSkillPenalty;
                    }
                }

                if (required > running)
                {
                    running = required;
                }
            }

            return running;
        }

        private bool Resolves(int affixId)
        {
            return _affixes.TryResolve(affixId, out _, out _);
        }

        // experience.txt row 0 is the MaxLvl row; column 0 is the Amazon.
        private int MaxCharacterLevel()
        {
            TxtFile experience = _data.Experience;
            if (experience == null || experience.RowCount == 0 || !experience.HasColumn("Amazon"))
            {
                return DefaultMaxLevel;
            }

            int max = experience.GetInt(0, "Amazon");
            return max > 0 ? max : DefaultMaxLevel;
        }

        private const int DefaultMaxLevel = 99;

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
