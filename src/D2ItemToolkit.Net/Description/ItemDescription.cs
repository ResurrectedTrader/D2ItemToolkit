using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace D2ItemToolkit
{

    public sealed class StatDescriptor
    {
        /// <summary>
        /// A field-by-field copy. Every field here is public and mutable, and the table holds ONE
        /// instance per stat for the life of the process — so handing a consumer the live object let
        /// them permanently change how every later render described that stat, on a shared engine
        /// that never healed. <see cref="TxtItemStatCostTable.TryGetStat"/> therefore returns a copy;
        /// the engine's own hot path goes through the internal interface and keeps the live one.
        /// </summary>
        internal StatDescriptor Copy()
        {
            return (StatDescriptor)MemberwiseClone();
        }

        public int StatId;

        public int DescPriority;

        public int DescFunc;    // +0x36

        public int DescVal;

        public int DescStrPos;  // +0x38
        public int DescStrNeg;  // +0x3A
        public int DescStr2;    // +0x3C

        public int DescGrp;

        public int DescGrpFunc; // +0x40

        public int DescGrpVal;
        public int DescGrpStrPos;  // +0x42
        public int DescGrpStrNeg;  // +0x44
        public int DescGrpStr2;    // +0x46

        public int ValShift;

        public int Op;

        public int OpParam;  // +0x55
        public int OpBase;   // +0x56
    }

    internal interface IItemStatCostTable
    {
        bool TryGetStat(int statId, out StatDescriptor descriptor);

        int RowCount { get; }

        IReadOnlyList<int> StatIdsByDescPriority { get; }

        IReadOnlyList<int> GetStatsInDescGroup(int descGrp);

        int SkillIdShift { get; }
    }

    internal interface IStringTable
    {
        string GetByIndex(int index);
    }

    internal interface IStatValueSource
    {
        int GetBaseStatValue(int statId, int layer);

        int GetPlayerStatValue(int statId);

        int GetItemStatValue(int statId);

        int PlayerClass { get; }

        bool IsItemOfType(int itemTypeId);

        bool DescribedUnitIsItem { get; }

        bool ItemTableAllowsDurability { get; }

        int GetTxtMaxDurability();
    }

    internal interface ISkillTable
    {
        int RowCount { get; }

        bool SkillExists(int skillId);

        string GetSkillName(int skillId);

        int GetSkillClass(int skillId);
    }

    internal interface ICharacterClassTable
    {
        string GetAllSkillsText(int classId);

        string GetSkillTabText(int classId, int tabIndex);

        string GetClassOnlyText(int classId);

        bool ClassExists(int classId);
    }

    internal interface IMonsterTypeTable
    {
        bool MonsterTypeExists(int monsterTypeId);

        string GetMonsterTypeName(int monsterTypeId);

        bool MonsterExists(int monsterId);

        string GetMonsterName(int monsterId);
    }

    internal interface IGameTimeProvider
    {
        bool TryGetTimeAngle(out int degrees);
    }


    internal static class DescStringIds
    {
        public const int Space = 3995;              // " "
        public const int Colon = 3997;              // ":" (key "colon"), DescFunc 22
        public const int Newline = 3998;            // "\n" (key "newline") - the line terminator
        public const int ListComma = 3852;          // "," (key "KeyComma") - block-mode separator
        public const int Percent = 4001;            // "%"
        public const int Plus = 4002;               // "+"
        public const int To = 4003;                 // "to", DescFunc 27 and 28
        public const int DescStr2Override = 11091;  // used when DescStr2 == 5382

        public const int RepairSingleCount = 21241;

        public const int RepairCountAndSeconds = 21242;

        public const int Level = 21249;             // DescFunc 24

        public const int NeverBreaks = 21240;

        // DATATBLS_LookupStringId turns ANY unresolved key into 5382 (0x6117c6), and 5382 resolves
        // to real text ("an evil force"). So providers must never collapse it to null — doing so
        // drops rows the engine emits.
        public const int DescStr2Sentinel = 5382;

        public static readonly int[] PeriodOfDay = { 21235, 21237, 21234, 21236 };
    }


    internal sealed class ItemDescriptionLine
    {
        public string Text;

        public int StatId;

        /// <summary>
        /// The stat's layer — the skill, class or tab the line is about. Carried alongside
        /// <see cref="StatId"/> so a caller can match a rendered line back to one stat KEY rather
        /// than to a stat id that several skills share.
        /// </summary>
        public int Layer;

        public int Value;

        public int DescPriority;
        public bool IsGroup;

        /// <summary>
        /// The line speaks for MORE than the one stat in <see cref="StatId"/> — a DescGrp variant
        /// ("+2 to all Attributes") or an aggregated damage line ("Adds 1-4 cold damage", which is
        /// coldmindam and coldmaxdam together).
        /// </summary>
        public bool Aggregated;

        /// <summary>
        /// Every stat this line displays a number for, in the order the numbers appear. Null means
        /// just <see cref="StatId"/>. This is what lets a roll range be shown against an aggregated
        /// line as the composite it is — "Adds 1-4 cold damage" spans two stats and so wants two
        /// spans — rather than being suppressed for want of a single answer.
        /// </summary>
        public int[] ShownStats;

        public bool PreJoined;

        public bool IsBlank
        {
            get { return string.IsNullOrEmpty(Text); }
        }

        public override string ToString()
        {
            return Text;
        }
    }

    internal static class ItemDescFunc
    {
        public const int PlusValueString = 1;
        public const int ValuePercentString = 2;
        public const int ValueString = 3;
        public const int PlusValuePercentString = 4;
        public const int ValueFramesPercentString = 5;
        public const int PlusValueStringString2 = 6;
        public const int ValuePercentStringString2 = 7;
        public const int PlusValuePercentStringString2 = 8;
        public const int ValueStringString2 = 9;
        public const int ValueFramesPercentStringString2 = 10;
        public const int RepairDurability = 11;
        public const int PlusValueStringSuppressOne = 12;
        public const int ClassAllSkills = 13;
        public const int SkillTab = 14;
        public const int SkillOnEvent = 15;
        public const int SkillAura = 16;
        public const int ValueStringByTime = 17;
        public const int ValuePercentStringByTime = 18;
        public const int RawFormat = 19;
        public const int NegatedValuePercentString = 20;
        public const int NegatedValuePercentStringString2 = 21;
        public const int MonsterTypeDamage = 22;
        public const int MonsterDamage = 23;
        public const int Charges = 24;
        public const int StaleNegated25 = 25;
        public const int StaleNegated26 = 26;
        public const int SkillClassOnly = 27;
        public const int Skill = 28;
    }

    internal struct ByTimeValue
    {
        public int Period;

        public int Low;

        public int High;

        public static ByTimeValue Unpack(int value)
        {
            var unpacked = new ByTimeValue();
            unpacked.Period = value & 3;
            unpacked.Low = ((value >> 2) & 0x3FF) - 256;
            unpacked.High = ((value >> 12) & 0x3FF) - 256;
            return unpacked;
        }

        public int Interpolate(int degrees)
        {
            int distance = Math.Abs(degrees - 90 * Period);
            distance = 15 * ((distance + 7) / 15);

            if (distance <= 0)
            {
                distance = 0;
            }
            else if (distance >= 359)
            {
                distance = 359;
            }

            if (distance > 180)
            {
                distance = 360 - distance;
            }

            return High - distance * (High - Low) / 180;
        }
    }


    internal sealed class ItemDescriptionGenerator
    {

        private static readonly int[,] SuppressedBy = { { 23, 21 }, { 24, 22 } };

        private const int Stat122 = 122;
        private const int ItemType57 = 57;

        private const int StatIndestructible = 152;

        private const int StatMaxDurability = 73;

        private const int MaxEntriesPerStat = 511;

        private readonly IItemStatCostTable _stats;
        private readonly IStringTable _strings;
        private readonly IStatValueSource _values;
        private readonly ISkillTable _skills;
        private readonly ICharacterClassTable _classes;
        private readonly IMonsterTypeTable _monsters;
        private readonly IGameTimeProvider _time;

        private readonly bool _isMainStatBlock;

        public ItemDescriptionGenerator(
            IItemStatCostTable stats,
            IStringTable strings,
            IStatValueSource values = null,
            ISkillTable skills = null,
            ICharacterClassTable classes = null,
            IMonsterTypeTable monsters = null,
            IGameTimeProvider time = null,
            bool isMainStatBlock = true)
        {
            if (stats == null) throw new ArgumentNullException("stats");
            if (strings == null) throw new ArgumentNullException("strings");

            _isMainStatBlock = isMainStatBlock;

            _stats = stats;
            _strings = strings;
            _values = values;
            _skills = skills;
            _classes = classes;
            _monsters = monsters;
            _time = time;
        }

        public IReadOnlyList<ItemDescriptionLine> Describe(IEnumerable<KeyValuePair<int, int>> packedStats)
        {
            if (packedStats == null) throw new ArgumentNullException("packedStats");

            var byStat = new Dictionary<int, List<KeyValuePair<int, int>>>();

            foreach (KeyValuePair<int, int> entry in packedStats)
            {
                int statId = ItemStatReader.StatFromKey(entry.Key);
                int layer = ItemStatReader.LayerFromKey(entry.Key);

                List<KeyValuePair<int, int>> entries;
                if (!byStat.TryGetValue(statId, out entries))
                {
                    entries = new List<KeyValuePair<int, int>>();
                    byStat[statId] = entries;
                }

                entries.Add(new KeyValuePair<int, int>(layer, entry.Value));
            }

            var lines = new List<ItemDescriptionLine>();

            string undead = UndeadDamageLine.Build(_strings, _values, _isMainStatBlock);
            if (!string.IsNullOrEmpty(undead))
            {
                var undeadLine = new ItemDescriptionLine();
                undeadLine.Text = undead;
                undeadLine.StatId = DamageStatIds.UndeadDamagePercent;
                undeadLine.Value = UndeadDamageLine.InherentPercent;
                undeadLine.PreJoined = true;
                lines.Add(undeadLine);
            }

            var damage = new ItemDamageAggregate(_strings, _values);

            foreach (int statId in _stats.StatIdsByDescPriority)
            {
                List<KeyValuePair<int, int>> entries;
                if (!byStat.TryGetValue(statId, out entries))
                {
                    continue;
                }

                StatDescriptor descriptor;
                bool hasDescriptor = _stats.TryGetStat(statId, out descriptor);

                entries.Sort(CompareByLayer);

                // 511 per stat (0x4e6261 / 0x626177), applied BEFORE the zero-value filter, not after
                // (0x4e628b / 0x4e6295).
                if (entries.Count > MaxEntriesPerStat)
                {
                    entries.RemoveRange(MaxEntriesPerStat, entries.Count - MaxEntriesPerStat);
                }

                foreach (KeyValuePair<int, int> entry in entries)
                {
                    if (entry.Value == 0)
                    {
                        continue; // 0x4e628b / 0x4e6295: skipped AFTER the 511 cap, not before
                    }

                    string aggregated;
                    if (damage.TryDescribe(statId, out aggregated))
                    {
                        if (string.IsNullOrEmpty(aggregated))
                        {
                            continue; // suppression only: the game emits nothing at all
                        }

                        var damageLine = new ItemDescriptionLine();
                        damageLine.Text = aggregated;
                        damageLine.StatId = statId;
                        // entry.Key IS the layer — `entries` is built as (layer, value) above.
                        // Decoding it again gave `layer >> 16`, which is 0 for every layer the
                        // 16-bit pack can hold, so this always reported 0 where the TypeScript
                        // peer reported the real layer.
                        damageLine.Layer = entry.Key;
                        damageLine.Value = entry.Value;
                        damageLine.PreJoined = true;
                        damageLine.Aggregated = ItemDamageAggregate.ShowsSeveralValues(statId);
                        damageLine.ShownStats = ItemDamageAggregate.StatsShownBy(statId);
                        lines.Add(damageLine);
                        continue;
                    }

                    if (!hasDescriptor || descriptor.DescFunc == 0)
                    {
                        continue;
                    }

                    ItemDescriptionLine line = DescribeEntry(descriptor, entry.Key, entry.Value);
                    if (line == null)
                    {
                        continue;
                    }

                    if (IsSuppressedByAnotherStat(statId))
                    {
                        continue;
                    }

                    lines.Add(line);
                }
            }

            AppendNeverBreaksLine(lines);
            return lines;
        }

        private void AppendNeverBreaksLine(List<ItemDescriptionLine> lines)
        {
            if (_values == null
                || !_values.DescribedUnitIsItem
                || !_values.ItemTableAllowsDurability
                || _values.GetItemStatValue(StatIndestructible) > 0
                || _values.GetTxtMaxDurability() != 0)
            {
                return;
            }

            var line = new ItemDescriptionLine();
            line.Text = Nz(Str(DescStringIds.NeverBreaks));
            line.StatId = StatMaxDurability;
            lines.Add(line);
        }

        // INLINE mode is the default and what the item tooltip uses (0x48e92d pushes arg_4 = 1):
        // string 3998 terminates EVERY line and no separator is inserted. Block mode instead puts
        // 3852 + 3995 BEFORE each line after the first and terminates nothing. A PreJoined line is
        // appended raw and skips the terminator either way (0x4e62ad).
        public string Join(IEnumerable<ItemDescriptionLine> lines, bool inlineMode = true)
        {
            if (lines == null) throw new ArgumentNullException("lines");

            var builder = new StringBuilder();

            if (inlineMode)
            {
                string terminator = Str(DescStringIds.Newline);
                foreach (ItemDescriptionLine line in lines)
                {
                    builder.Append(line.Text);

                    if (!line.PreJoined)
                    {
                        builder.Append(terminator);
                    }
                }

                return builder.ToString();
            }

            string separator = Str(DescStringIds.ListComma) + Str(DescStringIds.Space);
            bool first = true;

            foreach (ItemDescriptionLine line in lines)
            {
                if (line.PreJoined)
                {
                    builder.Append(line.Text);
                    continue;
                }

                if (!first)
                {
                    builder.Append(separator);
                }

                builder.Append(line.Text);
                first = false;
            }

            return builder.ToString();
        }

        private static int CompareByLayer(KeyValuePair<int, int> a, KeyValuePair<int, int> b)
        {
            return a.Key.CompareTo(b.Key);
        }

        private bool IsSuppressedByAnotherStat(int statId)
        {
            if (_values == null)
            {
                return false;
            }

            for (int i = 0; i < SuppressedBy.GetLength(0); ++i)
            {
                if (SuppressedBy[i, 0] == statId
                    && _values.GetBaseStatValue(SuppressedBy[i, 1], 0) != 0)
                {
                    return true;
                }
            }

            return false;
        }


        private bool TryComputeValue(StatDescriptor descriptor, int statId, int storedValue, out int result)
        {
            result = 0;
            int value = storedValue;

            if (descriptor.Op >= 2 && descriptor.Op <= 5)
            {
                if (descriptor.OpBase >= _stats.RowCount)
                {
                    return false;
                }

                StatDescriptor opBase;
                if (!_stats.TryGetStat(descriptor.OpBase, out opBase))
                {
                    return false;
                }

                int scale = _values == null
                    ? 0
                    : _values.GetPlayerStatValue(descriptor.OpBase) >> opBase.ValShift;
                value = (value * scale) >> descriptor.OpParam;
            }

            value = descriptor.ValShift > 0 ? value >> descriptor.ValShift : value;

            if (statId == Stat122 && _values != null && _values.IsItemOfType(ItemType57))
            {
                value += 50;
            }

            result = value;
            return true;
        }


        private bool IsGrouped(StatDescriptor descriptor, int value, out bool isPrimary)
        {
            isPrimary = false;

            if (descriptor.DescGrp == 0)
            {
                return false;
            }

            IReadOnlyList<int> members = _stats.GetStatsInDescGroup(descriptor.DescGrp);
            if (members == null || members.Count == 0)
            {
                return false;
            }

            int lowest = int.MaxValue;

            foreach (int memberStatId in members)
            {
                if (memberStatId < lowest)
                {
                    lowest = memberStatId;
                }

                StatDescriptor member;
                if (!_stats.TryGetStat(memberStatId, out member))
                {
                    return false;
                }

                int memberStored = _values == null ? 0 : _values.GetBaseStatValue(memberStatId, 0);

                int memberValue;
                if (!TryComputeValue(member, memberStatId, memberStored, out memberValue))
                {
                    memberValue = 0;
                }

                if (memberValue != value)
                {
                    return false;
                }
            }

            isPrimary = descriptor.StatId == lowest;
            return true;
        }

        private ItemDescriptionLine DescribeEntry(StatDescriptor descriptor, int layer, int storedValue)
        {
            int value;
            if (!TryComputeValue(descriptor, descriptor.StatId, storedValue, out value))
            {
                value = 0;
            }

            bool isPrimary;
            bool grouped = IsGrouped(descriptor, value, out isPrimary);

            if (grouped && !isPrimary)
            {
                return null; // another member of the group prints on its behalf
            }

            var c = new FormatContext();
            c.Func = grouped ? descriptor.DescGrpFunc : descriptor.DescFunc;
            c.DescVal = grouped ? descriptor.DescGrpVal : descriptor.DescVal;
            c.StrPos = grouped ? descriptor.DescGrpStrPos : descriptor.DescStrPos;
            c.RawStrPos = descriptor.DescStrPos;
            c.StrNeg = grouped ? descriptor.DescGrpStrNeg : descriptor.DescStrNeg;
            c.Str2 = grouped ? descriptor.DescGrpStr2 : descriptor.DescStr2;
            c.Value = value;
            c.Layer = layer;

            c.Text = Str(value < 0 ? c.StrNeg : c.StrPos);

            string text = Format(c);
            if (text == null)
            {
                return null; // the engine returned 0: no row at all
            }

            text = AppendDescStr2(text, c.Func, c.Str2);


            var line = new ItemDescriptionLine();
            line.Text = text;
            line.StatId = descriptor.StatId;
            line.Layer = c.Layer;
            line.Value = c.Value;
            line.DescPriority = descriptor.DescPriority;
            line.IsGroup = grouped;
            line.Aggregated = grouped;

            // A DescGrp line prints ONE number for the whole group, so every member shares it and
            // shares its span. Naming them all lets the formatter see they agree and collapse to a
            // single span rather than repeating it four times.
            if (grouped)
            {
                IReadOnlyList<int> members = _stats.GetStatsInDescGroup(descriptor.DescGrp);
                if (members != null && members.Count != 0)
                {
                    var shown = new int[members.Count];
                    for (int at = 0; at < members.Count; ++at)
                    {
                        shown[at] = members[at];
                    }

                    line.ShownStats = shown;
                }
            }

            return line;
        }

        private string AppendDescStr2(string text, int func, int str2)
        {
            bool wanted = func >= ItemDescFunc.PlusValueStringString2
                          && (func <= ItemDescFunc.ValueFramesPercentStringString2
                              || func == ItemDescFunc.NegatedValuePercentStringString2);

            if (!wanted)
            {
                return text;
            }

            int id = str2 == DescStringIds.DescStr2Sentinel ? DescStringIds.DescStr2Override : str2;
            return text + Str(DescStringIds.Space) + Str(id);
        }

        private sealed class FormatContext
        {
            public int Func;
            public int DescVal;
            public int Value;
            public int Layer;
            public int StrPos;
            public int StrNeg;

            public int RawStrPos;
            public int Str2;
            public string Text;
        }

        private enum DescValFallback
        {
            StringOnly,
            Empty,
        }

        private string Format(FormatContext c)
        {
            switch (c.Func)
            {
                case ItemDescFunc.PlusValueString:
                case ItemDescFunc.PlusValueStringString2:
                    return Place(c.DescVal, Signed(c.Value), c.Text, DescValFallback.Empty);

                case ItemDescFunc.PlusValueStringSuppressOne:
                    return Place(c.DescVal,
                        c.Value > 0 && c.Value <= 1 ? string.Empty : Signed(c.Value),
                        c.Text, DescValFallback.Empty);

                case ItemDescFunc.StaleNegated25:
                case ItemDescFunc.StaleNegated26:
                {
                    string staleDigits = Number(c.Value);
                    c.Value = -c.Value;
                    return Place(c.DescVal,
                        (c.Value > 0 ? Str(DescStringIds.Plus) : string.Empty) + staleDigits,
                        c.Text, DescValFallback.Empty);
                }

                case ItemDescFunc.ValuePercentString:
                case ItemDescFunc.ValuePercentStringString2:
                    return Place(c.DescVal, Number(c.Value) + Percent(), c.Text, DescValFallback.StringOnly);

                case ItemDescFunc.ValueString:
                case ItemDescFunc.ValueStringString2:
                    return Place(c.DescVal, Number(c.Value), c.Text, DescValFallback.StringOnly);

                case ItemDescFunc.PlusValuePercentString:
                case ItemDescFunc.PlusValuePercentStringString2:
                    return Place(c.DescVal, SignedIncludingZero(c.Value) + Percent(), c.Text,
                        DescValFallback.StringOnly);

                case ItemDescFunc.ValueFramesPercentString:
                case ItemDescFunc.ValueFramesPercentStringString2:
                    return Place(c.DescVal, Number(100 * c.Value / 128) + Percent(), c.Text,
                        DescValFallback.StringOnly);

                case ItemDescFunc.NegatedValuePercentString:
                case ItemDescFunc.NegatedValuePercentStringString2:
                    c.Value = -c.Value;
                    return Place(c.DescVal, SignedIncludingZero(c.Value) + Percent(), c.Text,
                        DescValFallback.StringOnly);

                case ItemDescFunc.RepairDurability:
                    return FormatRepair(c);

                case ItemDescFunc.ClassAllSkills:
                    return FormatClassAllSkills(c);

                case ItemDescFunc.SkillTab:
                    return FormatSkillTab(c);

                case ItemDescFunc.SkillOnEvent:
                    return FormatSkillOnEvent(c);

                case ItemDescFunc.SkillAura:
                    return FormatSkillAura(c);

                case ItemDescFunc.ValueStringByTime:
                case ItemDescFunc.ValuePercentStringByTime:
                    return FormatByTime(c);

                case ItemDescFunc.RawFormat:
                    return TblFormat.FormatBounded(c.Text, TblFormat.DefaultMaxLength, c.Value);

                case ItemDescFunc.MonsterTypeDamage:
                    return FormatMonsterType(c);

                case ItemDescFunc.MonsterDamage:
                    return FormatMonster(c);

                case ItemDescFunc.Charges:
                    return FormatCharges(c);

                case ItemDescFunc.SkillClassOnly:
                    return FormatSkillClassOnly(c);

                case ItemDescFunc.Skill:
                    return FormatSkill(c);

                default:
                    return null; // 0x4e4eca: unknown func returns 0
            }
        }


        private string FormatRepair(FormatContext c)
        {
            if (c.Value <= 0)
            {
                return TblFormat.FormatBounded(Str(DescStringIds.RepairSingleCount),
                    TblFormat.ShortMaxLength, 25);
            }

            int seconds = 2500 / c.Value;
            if (seconds > 30)
            {
                return TblFormat.FormatBounded(Str(DescStringIds.RepairCountAndSeconds),
                    TblFormat.ShortMaxLength, 1, (seconds + 12) / 25);
            }

            return TblFormat.FormatBounded(Str(DescStringIds.RepairSingleCount),
                TblFormat.ShortMaxLength, 1);
        }


        private string FormatClassAllSkills(FormatContext c)
        {
            if (c.Value == 0)
            {
                return null; // 0x4e51fc
            }

            if (_classes == null || !_classes.ClassExists(c.Layer))
            {
                return null; // 0x4e521a, missing charstats row
            }

            return Place(c.DescVal, Signed(c.Value), _classes.GetAllSkillsText(c.Layer),
                DescValFallback.Empty);
        }

        private string FormatSkillTab(FormatContext c)
        {
            int tabIndex = c.Layer & 7;
            int classId = c.Layer >> 3;

            if (_classes == null || !_classes.ClassExists(classId) || tabIndex > 2)
            {
                return null; // 0x4e528d / 0x4e5296
            }

            return TblFormat.FormatBounded(_classes.GetSkillTabText(classId, tabIndex),
                       TblFormat.DefaultMaxLength, c.Value)
                   + Str(DescStringIds.Space)
                   + Nz(_classes.GetClassOnlyText(classId));
        }


        private string FormatSkillOnEvent(FormatContext c)
        {
            int skillId = c.Layer >> _stats.SkillIdShift;
            int level = c.Layer & ((1 << _stats.SkillIdShift) - 1);

            if (_skills == null || skillId <= 0 || skillId >= _skills.RowCount)
            {
                return null; // 0x4e52f2 / 0x4e52fe
            }

            return TblFormat.FormatBounded(Str(c.RawStrPos), TblFormat.DefaultMaxLength,
                c.Value, 0, level, _skills.GetSkillName(skillId));
        }

        private string FormatSkillAura(FormatContext c)
        {
            string skillName = _skills == null ? null : _skills.GetSkillName(c.Layer);

            if (skillName == null)
            {
                return null; // 0x4e534c
            }

            return TblFormat.FormatBounded(c.Text, TblFormat.DefaultMaxLength, c.Value, skillName);
        }

        private string FormatCharges(FormatContext c)
        {
            int skillId = c.Layer >> _stats.SkillIdShift;
            int level = c.Layer & ((1 << _stats.SkillIdShift) - 1);

            string skillName = _skills == null ? null : _skills.GetSkillName(skillId);
            if (skillName == null)
            {
                return string.Empty; // 0x4e567d returns 1 with an empty buffer
            }

            string space = Str(DescStringIds.Space);

            var builder = new StringBuilder();
            builder.Append(Str(DescStringIds.Level));
            builder.Append(space);
            builder.Append(Number(level));
            builder.Append(space);
            builder.Append(skillName);
            builder.Append(space);
            builder.Append(TblFormat.FormatBounded(c.Text, TblFormat.ShortMaxLength,
                c.Value & 0xFF, c.Value >> 8));

            return builder.ToString();
        }

        private string FormatSkillClassOnly(FormatContext c)
        {
            string to = Str(DescStringIds.To);
            if (to == null)
            {
                return string.Empty; // 0x4e5780 tests the pointer
            }

            if (c.Value == 0)
            {
                return string.Empty; // 0x4e5788
            }

            string skillName = _skills == null ? null : _skills.GetSkillName(c.Layer);
            if (skillName == null)
            {
                return string.Empty; // 0x4e57a3 tests the pointer
            }

            string space = Str(DescStringIds.Space);
            string head = Signed(c.Value) + space + to + space + skillName + space;

            int classId = _skills.GetSkillClass(c.Layer);
            if (classId < 0 || classId > 6 || _classes == null || !_classes.ClassExists(classId))
            {
                return head;
            }

            return head + Nz(_classes.GetClassOnlyText(classId));
        }

        private string FormatSkill(FormatContext c)
        {
            if (c.Value == 0)
            {
                return string.Empty; // 0x4e5843
            }

            if (_skills == null || !_skills.SkillExists(c.Layer))
            {
                return string.Empty; // 0x4e5858
            }

            int playerClass = _values == null ? -1 : _values.PlayerClass;
            if (_skills.GetSkillClass(c.Layer) == playerClass && c.Value > 3)
            {
                c.Value = 3;
            }

            string to = Str(DescStringIds.To);
            if (to == null)
            {
                return string.Empty; // 0x4e589f tests the pointer
            }

            string skillName = _skills.GetSkillName(c.Layer);
            if (skillName == null)
            {
                return string.Empty; // 0x4e58ba tests the pointer
            }

            string space = Str(DescStringIds.Space);
            return Signed(c.Value) + space + to + space + skillName;
        }


        private string FormatByTime(FormatContext c)
        {
            ByTimeValue packed = ByTimeValue.Unpack(c.Value);

            int degrees = 0;
            bool hasTime = _time != null && _time.TryGetTimeAngle(out degrees);

            int adjusted = hasTime ? packed.Interpolate(degrees) : packed.Low;

            var builder = new StringBuilder();
            builder.Append(Str(DescStringIds.PeriodOfDay[packed.Period]));
            builder.Append(Str(DescStringIds.Newline));

            string number;
            if (adjusted >= 0)
            {
                number = Str(DescStringIds.Plus) + Number(adjusted);
            }
            else if (c.Value < 0)
            {
                number = Number(adjusted);
            }
            else
            {
                number = string.Empty;
            }

            if (c.Func == ItemDescFunc.ValuePercentStringByTime)
            {
                number += Percent();
            }

            c.Value = adjusted;
            builder.Append(Place(c.DescVal, number, c.Text, DescValFallback.Empty));
            return builder.ToString();
        }


        private string FormatMonsterType(FormatContext c)
        {
            string head = Place(c.DescVal, SignedIncludingZero(c.Value) + Percent(), c.Text,
                DescValFallback.StringOnly);

            if (_monsters == null || !_monsters.MonsterTypeExists(c.Layer))
            {
                return head;
            }

            return head + Str(DescStringIds.Colon) + Str(DescStringIds.Space)
                   + _monsters.GetMonsterTypeName(c.Layer);
        }

        private string FormatMonster(FormatContext c)
        {
            if (_monsters == null || !_monsters.MonsterExists(c.Layer))
            {
                return null;
            }

            string head = Place(c.DescVal, Number(c.Value) + Percent(), c.Text,
                DescValFallback.StringOnly);

            return head + Str(DescStringIds.Space) + _monsters.GetMonsterName(c.Layer);
        }


        private string Place(int descVal, string number, string text, DescValFallback fallback)
        {
            if (descVal == 1)
            {
                return number + Str(DescStringIds.Space) + text;
            }

            if (descVal == 2)
            {
                return text + Str(DescStringIds.Space) + number;
            }

            return fallback == DescValFallback.StringOnly ? text : string.Empty;
        }

        private static string Number(int value)
        {
            return TblFormat.FormatNumber(value);
        }

        private string Signed(int value)
        {
            return value > 0 ? Str(DescStringIds.Plus) + Number(value) : Number(value);
        }

        private string SignedIncludingZero(int value)
        {
            return value >= 0 ? Str(DescStringIds.Plus) + Number(value) : Number(value);
        }

        private string Percent()
        {
            return Str(DescStringIds.Percent);
        }

        private string Str(int index)
        {
            return _strings.GetByIndex(index);
        }

        private static string Nz(string text)
        {
            return text ?? string.Empty;
        }
    }


    internal static class TblFormat
    {
        // 8, not 9: the engine printf-formats with width 10 then converts with a limit of 9
        // (0x4e4e55 / 0x4e4e65), and UTF8_ConvertToWideChar decrements that limit first (0x52634f).
        public const int MaxNumberChars = 8;

        public static string FormatNumber(int value)
        {
            string text = value.ToString(CultureInfo.InvariantCulture);
            return text.Length > MaxNumberChars ? text.Substring(0, MaxNumberChars) : text;
        }

        public const int DefaultMaxLength = 0x100;

        public const int ShortMaxLength = 0x80;

        public static string Format(string format, params object[] args)
        {
            return FormatBounded(format, DefaultMaxLength, args);
        }

        // UNICODE_FormatWideString (0x5269d0). Survivors are maxLength - 1, because the last slot is
        // overwritten with NUL (0x526bda). The budget is re-tested ABOVE the specifier jump table
        // (0x526a6d dominates 0x526a99), so once it is spent an unrecognised specifier truncates
        // instead of reaching the halt at 0x526c66. Specifier set is exactly NUL, %, d, s, u.
        public static string FormatBounded(string format, int maxLength, params object[] args)
        {
            if (string.IsNullOrEmpty(format))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(format.Length + 16);
            int nextArg = 0;

            for (int i = 0; i < format.Length; ++i)
            {
                char c = format[i];

                if (c != '%')
                {
                    if (builder.Length >= maxLength)
                    {
                        return Truncate(builder, maxLength);
                    }

                    builder.Append(c);
                    continue;
                }

                if (builder.Length >= maxLength)
                {
                    return Truncate(builder, maxLength);
                }

                if (i + 1 >= format.Length)
                {
                    builder.Append(c);
                    return builder.ToString();
                }

                char spec = format[i + 1];
                ++i;

                if (spec == '%')
                {
                    builder.Append('%');
                    if (args != null && nextArg < args.Length)
                    {
                        ++nextArg;
                    }

                    continue;
                }

                if (spec != 'd' && spec != 'u' && spec != 's')
                {
                    throw new FormatException(
                        "Unsupported format specifier '%" + spec +
                        "'. The game halts on this (0x526c66).");
                }

                if (args == null || nextArg >= args.Length)
                {
                    builder.Append('%');
                    builder.Append(spec);
                    continue;
                }

                object arg = args[nextArg++];
                int room = maxLength - builder.Length - 1;

                if (spec == 's')
                {
                    string text = arg as string;

                    if (text == null)
                    {
                        if (room == 0)
                        {
                            return Truncate(builder, maxLength);
                        }

                        throw new FormatException(
                            "A %s argument was null. The game dereferences it (0x526761).");
                    }

                    if (text.Length == 0)
                    {
                        return Truncate(builder, maxLength);
                    }

                    if (text.Length >= room)
                    {
                        if (room > 0)
                        {
                            builder.Append(text, 0, room);
                        }

                        return Truncate(builder, maxLength);
                    }

                    builder.Append(text);
                    continue;
                }

                string number = spec == 'u' ? Unsigned(arg) : Signed(arg);
                if (number.Length >= room)
                {
                    return Truncate(builder, maxLength);
                }

                builder.Append(number);
            }

            return Truncate(builder, maxLength);
        }

        private static string Truncate(StringBuilder builder, int maxLength)
        {
            int cap = maxLength - 1;
            if (cap >= 0 && builder.Length > cap)
            {
                return builder.ToString(0, cap);
            }

            return builder.ToString();
        }

        private static string Signed(object arg)
        {
            if (arg == null)
            {
                return "0";
            }

            if (arg is int)
            {
                return ((int)arg).ToString(CultureInfo.InvariantCulture);
            }

            return arg.ToString();
        }

        private static string Unsigned(object arg)
        {
            if (arg == null)
            {
                return "0";
            }

            if (arg is int)
            {
                return unchecked((uint)(int)arg).ToString(CultureInfo.InvariantCulture);
            }

            return arg.ToString();
        }
    }
}

