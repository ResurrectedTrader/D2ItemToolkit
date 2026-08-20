using System.Collections.Generic;

namespace D2ItemToolkit.Tests
{
    internal sealed class FakeStringTable : IStringTable
    {
        public readonly Dictionary<int, string> Entries = new Dictionary<int, string>();

        public FakeStringTable Add(int index, string text)
        {
            Entries[index] = text;
            return this;
        }

        /// <summary>
        /// The punctuation the engine pulls from the .tbl rather than hardcoding.
        /// Without these every line comes out empty, which is itself worth a test.
        /// </summary>
        public FakeStringTable WithPunctuation()
        {
            return Add(DescStringIds.Space, " ")
                .Add(DescStringIds.Colon, " to")
                .Add(DescStringIds.Newline, "\n")
                .Add(DescStringIds.ListComma, "\n")
                .Add(DescStringIds.Percent, "%")
                .Add(DescStringIds.Plus, "+")
                .Add(DescStringIds.To, "to");
        }

        public string GetByIndex(int index)
        {
            string text;
            return Entries.TryGetValue(index, out text) ? text : null;
        }
    }

    internal sealed class FakeStatCostTable : IItemStatCostTable
    {
        public readonly Dictionary<int, StatDescriptor> Stats = new Dictionary<int, StatDescriptor>();
        public readonly List<int> Order = new List<int>();
        public readonly Dictionary<int, IReadOnlyList<int>> Groups = new Dictionary<int, IReadOnlyList<int>>();
        public readonly HashSet<int> Missing = new HashSet<int>();

        public int SkillIdShift { get; set; }

        /// <summary>0x4e4c76 returns 0 when a wOpBase reaches or exceeds this.</summary>
        public int RowCount { get; set; }

        public FakeStatCostTable()
        {
            SkillIdShift = 6;
            RowCount = 512;
        }

        public FakeStatCostTable Add(StatDescriptor descriptor)
        {
            Stats[descriptor.StatId] = descriptor;
            Order.Add(descriptor.StatId);
            return this;
        }

        public FakeStatCostTable AddGroup(int descGrp, params int[] statIds)
        {
            Groups[descGrp] = new List<int>(statIds);
            return this;
        }

        /// <summary>A stat id in the priority order with no backing row.</summary>
        public FakeStatCostTable AddMissing(int statId)
        {
            Missing.Add(statId);
            Order.Add(statId);
            return this;
        }

        public bool TryGetStat(int statId, out StatDescriptor descriptor)
        {
            if (Missing.Contains(statId))
            {
                descriptor = null;
                return false;
            }

            return Stats.TryGetValue(statId, out descriptor);
        }

        public IReadOnlyList<int> StatIdsByDescPriority
        {
            get { return Order; }
        }

        public IReadOnlyList<int> GetStatsInDescGroup(int descGrp)
        {
            IReadOnlyList<int> members;
            return Groups.TryGetValue(descGrp, out members) ? members : null;
        }
    }

    internal sealed class FakeStatValues : IStatValueSource
    {
        public readonly Dictionary<int, int> BaseStats = new Dictionary<int, int>();
        public readonly Dictionary<int, int> PlayerStats = new Dictionary<int, int>();

        public readonly HashSet<int> ItemTypes = new HashSet<int>();

        public int PlayerClass { get; set; }

        public FakeStatValues()
        {
            PlayerClass = -1;
        }

        public FakeStatValues AddItemType(int itemTypeId)
        {
            ItemTypes.Add(itemTypeId);
            return this;
        }

        public bool IsItemOfType(int itemTypeId)
        {
            return ItemTypes.Contains(itemTypeId);
        }

        public FakeStatValues AddBase(int statId, int value)
        {
            BaseStats[statId] = value;
            return this;
        }

        public FakeStatValues AddPlayer(int statId, int value)
        {
            PlayerStats[statId] = value;
            return this;
        }

        public int GetBaseStatValue(int statId, int layer)
        {
            int value;
            return BaseStats.TryGetValue(statId, out value) ? value : 0;
        }

        public readonly Dictionary<int, int> ItemStats = new Dictionary<int, int>();

        public FakeStatValues AddItemStat(int statId, int value)
        {
            ItemStats[statId] = value;
            return this;
        }

        public bool DescribedUnitIsItem { get; set; }

        public bool ItemTableAllowsDurability { get; set; }

        /// <summary>Distinct from GetItemStatValue(73): see the interface doc.</summary>
        public int TxtMaxDurability { get; set; }

        public int GetTxtMaxDurability()
        {
            return TxtMaxDurability;
        }

        public int GetItemStatValue(int statId)
        {
            int value;
            return ItemStats.TryGetValue(statId, out value) ? value : 0;
        }

        public int GetPlayerStatValue(int statId)
        {
            int value;
            return PlayerStats.TryGetValue(statId, out value) ? value : 0;
        }
    }

    internal sealed class FakeSkillTable : ISkillTable
    {
        public readonly Dictionary<int, string> Names = new Dictionary<int, string>();
        public readonly Dictionary<int, int> Classes = new Dictionary<int, int>();

        public int RowCount { get; set; }

        public FakeSkillTable()
        {
            RowCount = 400;
        }

        public FakeSkillTable Add(int skillId, string name, int classId = -1)
        {
            Names[skillId] = name;
            Classes[skillId] = classId;
            return this;
        }

        public bool SkillExists(int skillId)
        {
            return Names.ContainsKey(skillId);
        }

        public string GetSkillName(int skillId)
        {
            string name;
            return Names.TryGetValue(skillId, out name) ? name : null;
        }

        public int GetSkillClass(int skillId)
        {
            int classId;
            return Classes.TryGetValue(skillId, out classId) ? classId : -1;
        }
    }

    internal sealed class FakeClassTable : ICharacterClassTable
    {
        public readonly Dictionary<int, string> AllSkills = new Dictionary<int, string>();
        public readonly Dictionary<int, string> Tabs = new Dictionary<int, string>();
        public readonly Dictionary<int, string> ClassOnly = new Dictionary<int, string>();

        public FakeClassTable AddAllSkills(int classId, string text)
        {
            AllSkills[classId] = text;
            return this;
        }

        public FakeClassTable AddTab(int classId, int tabIndex, string text)
        {
            Tabs[classId * 4 + tabIndex] = text;
            return this;
        }

        public FakeClassTable AddClassOnly(int classId, string text)
        {
            ClassOnly[classId] = text;
            return this;
        }

        public string GetAllSkillsText(int classId)
        {
            string text;
            return AllSkills.TryGetValue(classId, out text) ? text : null;
        }

        public string GetSkillTabText(int classId, int tabIndex)
        {
            string text;
            return Tabs.TryGetValue(classId * 4 + tabIndex, out text) ? text : null;
        }

        public string GetClassOnlyText(int classId)
        {
            string text;
            return ClassOnly.TryGetValue(classId, out text) ? text : null;
        }

        /// <summary>A class counts as present once it has any charstats string.</summary>
        public bool ClassExists(int classId)
        {
            return AllSkills.ContainsKey(classId)
                   || ClassOnly.ContainsKey(classId)
                   || Tabs.ContainsKey(classId * 4)
                   || Tabs.ContainsKey(classId * 4 + 1)
                   || Tabs.ContainsKey(classId * 4 + 2);
        }
    }

    internal sealed class FakeMonsterTable : IMonsterTypeTable
    {
        public readonly Dictionary<int, string> Types = new Dictionary<int, string>();
        public readonly Dictionary<int, string> Monsters = new Dictionary<int, string>();

        public FakeMonsterTable AddType(int id, string name)
        {
            Types[id] = name;
            return this;
        }

        public FakeMonsterTable AddMonster(int id, string name)
        {
            Monsters[id] = name;
            return this;
        }

        public bool MonsterTypeExists(int monsterTypeId)
        {
            return Types.ContainsKey(monsterTypeId);
        }

        public bool MonsterExists(int monsterId)
        {
            return Monsters.ContainsKey(monsterId);
        }

        public string GetMonsterTypeName(int monsterTypeId)
        {
            string name;
            return Types.TryGetValue(monsterTypeId, out name) ? name : null;
        }

        public string GetMonsterName(int monsterId)
        {
            string name;
            return Monsters.TryGetValue(monsterId, out name) ? name : null;
        }
    }

    internal sealed class FakeGameTime : IGameTimeProvider
    {
        public bool HasTime = true;
        public int Degrees;

        public bool TryGetTimeAngle(out int degrees)
        {
            degrees = Degrees;
            return HasTime;
        }
    }

    /// <summary>Packs a by-time stat value the way the game stores it.</summary>
    internal static class ByTime
    {
        public static int Pack(int period, int low, int high)
        {
            return (period & 3)
                   | (((low + 256) & 0x3FF) << 2)
                   | (((high + 256) & 0x3FF) << 12);
        }
    }

    internal static class Build
    {
        public static StatDescriptor Stat(int statId, int descFunc, int strPos, int descVal = 1,
            int priority = 0, int strNeg = 0, int str2 = 0, int valShift = 0)
        {
            var descriptor = new StatDescriptor();
            descriptor.StatId = statId;
            descriptor.DescFunc = descFunc;
            descriptor.DescVal = descVal;
            descriptor.DescStrPos = strPos;
            descriptor.DescStrNeg = strNeg;
            descriptor.DescStr2 = str2;
            descriptor.DescPriority = priority;
            descriptor.ValShift = valShift;
            return descriptor;
        }

        /// <summary>The packed (layer, stat) key ReconstructView produces.</summary>
        public static KeyValuePair<int, int> Entry(int statId, int value, int layer = 0)
        {
            return new KeyValuePair<int, int>(ItemStatReader.PackStatKey(layer, statId), value);
        }
    }
}

