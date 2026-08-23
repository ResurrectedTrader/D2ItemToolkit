using System.Collections.Generic;

namespace D2ItemToolkit
{
    /// <summary>
    /// Which end of a ranged property to resolve to. A record carries no item seed, so a range
    /// cannot be reproduced — but both ends can be, and the pair is the range.
    /// </summary>
    internal enum RollEnd
    {
        Low = 0,
        High = 1,
    }

    /// <summary>One gem/rune mod: gems.txt's {code, param, min, max} quadruple.</summary>
    public struct ItemProperty
    {
        public int PropertyId;
        public int Param;
        public int Min;
        public int Max;
    }

    /// <summary>
    /// ITEMMOD_ApplyPropertyToUnitStatsExpansion 0x65fd70 and the handlers behind
    /// dword_7462F8 (0x65eb30..0x65fae0), ported from D2Common/src/Items/ItemMods.cpp whose own
    /// dispatch table cites the same address.
    ///
    /// Every func any shipped table reaches is implemented — the gem and rune codes, and the
    /// affix, unique, set, runeword and cube ones a roll-range reconstruction needs. Func 9 is the
    /// only non-null handler left, and no code in any of the ten source tables carries it; it
    /// reports itself through <see cref="UnsupportedFunc"/> rather than applying nothing silently.
    /// </summary>
    internal sealed class PropertyApplier
    {
        // PROPMODE_*, from D2StatList.h. The gem and rune paths are the only ones used here.
        public const int PropModeGem = 2;
        public const int PropModeRune = 5;

        private const int StatMinDamage = 21;
        private const int StatMaxDamage = 22;
        private const int StatSecondaryMinDamage = 23;
        private const int StatSecondaryMaxDamage = 24;
        // 0x11 and 0x12 in D2StatList.h — MAX comes first.
        private const int StatMaxDamagePercent = 17;
        private const int StatMinDamagePercent = 18;
        private const int StatThrowMinDamage = 159;
        private const int StatThrowMaxDamage = 160;
        private const int StatPoisonMaxDamage = 58;
        private const int StatPoisonCount = 326;
        private const int StatIndestructible = 152;
        private const int StatNumSockets = 194;

        // Func 10 packs a skill-tab param as class * 8 + tab (0x65f434 divides by 3, 0x65f43b
        // scales by 8): three tabs per class, but an eight-wide stride between classes.
        private const int SkillTabsPerClass = 3;
        private const int SkillTabStride = 8;

        // Funcs 11 and 19 share one skill/level packing. Func 11 hardcodes both (0x65f565,
        // 0x65f568); func 19 reads them from the compiled table (0x65f82e, 0x65f841), where they
        // hold these same values.
        private const int SkillIdShift = 6;
        private const int SkillLevelMask = (1 << SkillIdShift) - 1;

        private readonly PropertiesTable _properties;
        private readonly TxtItemStatCostTable _statCost;
        private readonly ItemTable _items;
        private readonly ItemTypeTree _types;
        private readonly TxtSkillTable _skills;

        private readonly RollEnd _end;

        public PropertyApplier(
            D2DataFiles data, ItemTable items, ItemTypeTree types, RollEnd end = RollEnd.Low)
        {
            _properties = new PropertiesTable(data.Properties, data.ItemStatCost);
            _statCost = data.ItemStatCost;
            _items = items;
            _types = types;
            _skills = data.Skills;
            _end = end;
        }

        public PropertiesTable Properties { get { return _properties; } }

        /// <summary>Func codes reached that this port does not implement.</summary>
        public readonly SortedSet<int> UnsupportedFunc = new SortedSet<int>();

        /// <summary>
        /// Properties the game resolves from the ITEM's own level, reported only when the record
        /// carries none (<see cref="IUnit.ItemLevel"/> being -1): funcs 11 and 19 with a
        /// non-positive max (0x65f4de / 0x65f514 / 0x65f70a / 0x65f75b), and func 14's MaxSock tier
        /// (0x62bc81). Those land on the game's floor instead of the real value.
        ///
        /// No shipped gems.txt or sets.txt property takes any of those arms — Cow King's
        /// `gethit-skill` has max 5 — so this stays empty on the rendering path against stock data,
        /// and a test asserts it. The roll-range reconstruction does reach them.
        /// </summary>
        public readonly SortedSet<int> ItemLevelDependent = new SortedSet<int>();

        /// <summary>
        /// Applies one property's seven sets. Set 0's return value is threaded into every later set
        /// as nValue, which each handler reads as "already rolled, do not roll again" (0x65fdfb).
        /// </summary>
        public void Apply(
            int propMode, ItemIdentity item, ItemProperty property, IDictionary<int, int> into)
        {
            PropertiesTable.Row row = _properties[property.PropertyId];
            if (property.PropertyId < 0 || row == null)
            {
                return;
            }

            int carried = 0;

            for (int set = 0; set < PropertiesTable.SetsPerProperty; ++set)
            {
                int func = row.Func[set];
                if (func <= 0 || func >= HandlerCount)
                {
                    break;
                }

                // nPropMode is deliberately not threaded past here. It selects WHICH properties get
                // applied and from where — the switch at ItemMods.cpp:2362, which the caller has
                // already done by enumerating the gems.txt or sets.txt row — not how one property
                // behaves. Exactly one handler in the 0x65eb30..0x65fae0 table looks at it at all:
                // func 1 gates its "enhanced" reset on `cmp ecx, 1` (0x65eb59), and that reset
                // rewrites an existing statlist entry rather than the temp list a description
                // builds. None of the modes reaching this port is 1 anyway — gem 2, rune 5,
                // set bonus 4 (0x6601df).
                int result = Dispatch(
                    func, item, property, row.Set[set], row.Stat[set], row.Val[set], carried, into);

                if (set == 0)
                {
                    carried = result;
                }
            }
        }

        // dword_745B54 is 37; slots 25..35 are null and 36 is the uber handler.
        private const int HandlerCount = 37;

        private int Dispatch(
            int func,
            ItemIdentity item,
            ItemProperty property,
            int nSet,
            int statId,
            int nVal,
            int carried,
            IDictionary<int, int> into)
        {
            switch (func)
            {
                // 1 and 2 differ only in the nType == 1 gate on the "enhanced" reset, and that reset
                // targets an existing statlist entry, not the temp list a description builds — so on
                // the gem path (nType 2) neither reaches it.
                case 1:
                case 2:
                    return AddRolled(property, nSet, statId, 0, into);

                // 3 and 4 keep an already-rolled value instead of rolling again.
                case 3:
                case 4:
                    return AddRolled(property, nSet, statId, carried, into);

                case 5:
                    return MinDamage(item, property, nSet, carried, into);

                case 6:
                    return MaxDamage(item, property, nSet, carried, into);

                case 7:
                    return EnhancedDamage(item, property, nSet, carried, into);

                case 8:
                    return AddRolled(property, nSet, statId, carried, into);

                case 15:
                    // Fixed to nMin, and routed through func 5 when it lands on min damage.
                    if (statId == StatMinDamage)
                    {
                        MinDamage(item, property, nSet, property.Min, into);
                    }
                    else
                    {
                        AddStat(nSet, statId, property.Min, into);
                    }

                    return property.Min;

                case 16:
                    // Fixed to nMax — note nMax, not nMin.
                    if (statId == StatMaxDamage)
                    {
                        MaxDamage(item, property, nSet, property.Max, into);
                    }
                    else
                    {
                        AddStat(nSet, statId, property.Max, into);
                    }

                    return property.Max;

                case 17:
                    return FixedOrRolled(item, property, nSet, statId, into);

                case 20:
                    // Indestructible is a flag, written unshifted and unconditionally at value 1.
                    AddStat(0, StatIndestructible, 1, into);
                    return 1;

                case 21:
                {
                    // 0x65fb50. Same shape as func 1 except that the stat LAYER, which func 1
                    // pushes as a literal 0 (0x65eb83), comes from Properties.txt `val<n>`
                    // (0x65fb86) — the class number for the seven `ama`..`ass` codes. It rolls
                    // unconditionally rather than honouring a carried value (0x65fb66).
                    int rolled = Roll(property);
                    AddStat(nSet, statId, rolled, nVal, into);
                    return rolled;
                }

                case 22:
                {
                    // 0x65fbf0, and the layer is the property's own param truncated to a word
                    // (`movzx edx, word ptr [esi+4]`, 0x65fc1b) — the skill id behind `oskill`.
                    int rolled = Roll(property);
                    AddStat(nSet, statId, rolled, property.Param & 0xFFFF, into);
                    return rolled;
                }

                case 11:
                {
                    // ITEMPROP_AddSkillCharges 0x65f470. The property is (skill, chance, level):
                    // param is the skill id, min the % chance (defaulted to 5 when not positive,
                    // 0x65f4af), and max the LEVEL. The stat carries the pair packed into its
                    // LAYER — `(level & 0x3F) + (skill << 6)` at 0x65f54f — with the chance as the
                    // value, which is the same encoding a captured charged-skill stat arrives in.
                    int skill = property.Param;
                    if (skill < 0)
                    {
                        return 0; // 0x65f4a1 bounds the skill against the skills table
                    }

                    int chance = property.Min > 0 ? property.Min : 5;

                    // max > 0 is the whole computation (0x65f50c falls straight through to the
                    // add). The other two arms derive the level from the ITEM's level against
                    // skills.txt reqlevel — 0x65f4de for max == 0, 0x65f514 for max < 0 — which is
                    // exact when the capture recorded one; without it the game's own floor of 1
                    // (0x65f54d) is used and the property is reported rather than mis-levelled.
                    // Func 19 derives it identically, so both share one helper.
                    int level = property.Max;
                    if (level <= 0)
                    {
                        int derived = SkillLevelFromItemLevel(item, skill, property.Max);
                        if (derived < 0)
                        {
                            ItemLevelDependent.Add(property.PropertyId);
                            derived = 1;
                        }

                        level = derived;
                    }

                    AddStat(
                        nSet, statId, chance,
                        (level & SkillLevelMask) + (skill << SkillIdShift), into);
                    return chance;
                }

                case 24:
                {
                    // ITEMPROP_AddStat_WithLayerFromParam4 0x65f390: AddRolled with the layer taken
                    // from the property's own param (`a4[1]` at 0x65f39e), unmasked — unlike func
                    // 22, which truncates it to a word.
                    int value = carried != 0 ? carried : Roll(property);
                    AddStat(nSet, statId, value, property.Param, into);
                    return value;
                }

                case 13:
                {
                    // ITEMPROP_AddStat_WithMaxDurabilityReset 0x65fc90 — `dur%`, stat 75. Reached
                    // by every superior item through qualityitems.txt, which is four of that
                    // file's eight rows. Rolls UNCONDITIONALLY onto layer 0 (0x65fcc7 has no
                    // carried check), which is the whole handler as far as stock data goes.
                    //
                    // Two arms are deliberately not modelled. The handler resets stat 72 to
                    // GetTxtMaxDurability 0x625e00 (0x65fcfe), but that reads ItemStatCost[73]'s
                    // MinAccr — blank in shipped data, so 0 — and the write is gated on > 0
                    // (0x65fcf6), so it cannot fire. And the propMode == 1 "enhanced" maximise at
                    // 0x65fcb0 is the same arm func 1 has, which no mode reaching this port takes.
                    int value = Roll(property);
                    AddStat(nSet, statId, value, into);
                    return value;
                }

                case 10:
                {
                    // ITEMPROP_AddClassSkillBonus 0x65f3f0 — `skilltab`, stat 188. The same
                    // carried-or-roll tail as func 24, but the LAYER re-packs the param: it is a
                    // tab index over the seven classes' three tabs each, and 0x65f433..0x65f43b
                    // divides by 3 to give class*8 + tab — an 8-wide stride over a 3-wide field.
                    // `idiv` truncates toward zero and leaves the remainder signed, which is what
                    // C# `/` and `%` already do, so a negative param translates directly.
                    int value = carried != 0 ? carried : Roll(property);
                    int layer = (property.Param / SkillTabsPerClass) * SkillTabStride
                        + (property.Param % SkillTabsPerClass);
                    AddStat(nSet, statId, value, layer, into);
                    return value;
                }

                case 12:
                {
                    // ITEMPROP_AddStat_RandLevelAsLayer 0x65fc40 — `skill-rand`, stat 107. The
                    // roll lands in the LAYER and the property's own param is the VALUE
                    // (0x65fc72 pushes param into the value slot, 0x65fc76 the roll into the
                    // layer slot). It rolls unconditionally, ignoring what set 0 carried
                    // (0x65fc63 has no carried check at all).
                    //
                    // Ormus' Robes is the only shipped user: par=3, min=36, max=60 — "+3 to" a
                    // rolled skill id in 36..60, the twenty-five sorceress skills.
                    int rolledLayer = Roll(property);
                    AddStat(nSet, statId, property.Param, rolledLayer, into);
                    return property.Param;
                }

                case 36:
                {
                    // ITEMPROP_AddStat_LayerFromRoll_ValueFromParam7 0x65fba0 — `randclassskill`,
                    // stat 83. Func 12 with the value taken from Properties.txt `val<n>` instead
                    // of the param (`movsx edx, [ebp+arg_10]`, 0x65fbcb).
                    //
                    // Hellfire Torch is the only shipped user: val1=3, min=0, max=6 — "+3" to a
                    // rolled class in 0..6.
                    int rolledLayer = Roll(property);
                    AddStat(nSet, statId, nVal, rolledLayer, into);
                    return nVal;
                }

                case 23:
                    // ITEMPROP_ApplyEthereal 0x65fd20. Writes no stat — its Properties.txt `stat1`
                    // is blank — it flips the ethereal flag and applies the ethereal bonus. An
                    // already-ethereal item returns 0 (0x65fd3e), as does one with no durability
                    // (0x65fd48); otherwise 1 (0x65fd50). Only the return matters here, because
                    // set 0's return becomes the carried value for every later set, and a captured
                    // item's ethereality is already in its flags and its stats.
                    return item != null && !item.Has(ItemRecordFlags.Ethereal) ? 1 : 0;

                case 18:
                {
                    // ITEMPROP_AddTimedStat 0x65f870 — the `*<thing>/time` family, stats 268..303.
                    // The value is a PACKED TRIPLE rather than a magnitude: param clamped to
                    // 0..3, then min and max each biased by +256 and clamped to 0..0x3FF, laid out
                    // as `param + 4 * ((max << 10) + min)` (0x65f934..0x65f93d). So a by-time stat
                    // carries its own two ends, which is why it needs no roll.
                    int mode = property.Param <= 0 ? 0 : (property.Param > 3 ? 3 : property.Param);
                    int low = ClampTimedBound(property.Min);
                    int high = ClampTimedBound(property.Max);
                    int packed = mode + 4 * ((high << 10) + low);

                    // Unlike every AddStatToItem func this one calls D2AddStatToStatsListEx
                    // directly (0x65f947), so the value is stored UNSHIFTED and always SET. Every
                    // stat it can reach has ValShift 0 in shipped data, so the distinction is
                    // unobservable — it is modelled because the packing would be corrupted by a
                    // shift if it ever were not.
                    SetRawStat(statId, packed, 0, into);
                    return high;
                }

                case 19:
                {
                    // ITEMPROP_AddSkillOnEvent 0x65f6a0 — `charged`, stat 204. The value packs the
                    // charge pair, `(maxCharges << 8) + current` (0x65f84b), and the layer packs
                    // the skill and its level exactly as func 11 does (0x65f82e/0x65f841 read the
                    // shift and mask from the compiled table where func 11 hardcodes 6 and 0x3F).
                    int skill = property.Param;
                    if (skill < 0)
                    {
                        return 0; // 0x65f6da bounds the skill against the skills table
                    }

                    // max > 0 is the level outright (0x65f759). The other two arms derive it from
                    // the ITEM's level — 0x65f70a for max == 0, 0x65f75b for max < 0 — which is
                    // exact when the capture recorded one and otherwise falls to the game's own
                    // floor of 1, reported rather than silently mis-levelled.
                    int level = property.Max;
                    if (level <= 0)
                    {
                        int derived = SkillLevelFromItemLevel(item, skill, property.Max);
                        if (derived < 0)
                        {
                            ItemLevelDependent.Add(property.PropertyId);
                            derived = 1;
                        }

                        level = derived;
                    }

                    // min == 0 defaults the charge count to 5 (0x65f7a8); min < 0 scales it by the
                    // level, `|min| + (|min| * level) / 8` (0x65f7b1..0x65f7c2); otherwise min is
                    // it. Then clamped to 1..255 (0x65f7c4..0x65f7dd).
                    int maxCharges;
                    if (property.Min == 0)
                    {
                        maxCharges = 5;
                    }
                    else if (property.Min < 0)
                    {
                        int magnitude = -property.Min;
                        maxCharges = magnitude + magnitude * level / 8;
                    }
                    else
                    {
                        maxCharges = property.Min;
                    }

                    maxCharges = maxCharges < 1 ? 1 : (maxCharges > 255 ? 255 : maxCharges);

                    // The CURRENT charge count is drawn off the item seed:
                    // `rand(maxCharges - maxCharges / 8) + maxCharges / 8 + 1` (0x65f7ec..0x65f80e),
                    // so it spans maxCharges/8 + 1 .. maxCharges. A record has no seed, so this
                    // resolves to one end under the same policy as Roll.
                    int floor = maxCharges / 8 + 1;
                    int current = _end == RollEnd.High ? maxCharges : floor;

                    SetRawStat(
                        statId,
                        (maxCharges << 8) + (current & 0xFF),
                        (skill << SkillIdShift) + (level & SkillLevelMask),
                        into);
                    return maxCharges;
                }

                case 14:
                {
                    // ITEMPROP_SetSockets 0x65f590 — `sock`, stat 194. Capped first by the item's
                    // own footprint, `min(6, invwidth * invheight)` (0x65f5cb sets 6, 0x65f5e5
                    // multiplies), and a zero footprint writes nothing (0x65f5f0).
                    if (item == null)
                    {
                        return 0; // 0x65f5a8 — a null unit writes nothing
                    }

                    int width = _items.GetInt(item.ClassId, "invwidth");
                    int height = _items.GetInt(item.ClassId, "invheight");
                    int footprint = width * height;
                    if (footprint <= 0)
                    {
                        return 0;
                    }

                    int cap = footprint < 6 ? footprint : 6;

                    // ITEM_GetMaxSockCount 0x62bc20 narrows that to
                    // `min(gemsockets, MaxSock1|MaxSock25|MaxSock40)`, choosing the tier by ITEM
                    // LEVEL — <= 25, <= 40, else (0x62bc81/0x62bc8c).
                    //
                    // The gemsockets half is applied unconditionally, INCLUDING zero:
                    // min(gemsockets, tier) is 0 whenever gemsockets is 0 whatever the tier, so a
                    // base that takes no sockets at all — boots, gloves, belts, rings — ends with
                    // cap 0 and writes nothing, which is the 0x65f679 `test esi, esi` arm falling
                    // through to return 0.
                    int gemSockets = _items.GetInt(item.ClassId, "gemsockets");
                    if (gemSockets < cap)
                    {
                        cap = gemSockets;
                    }

                    // The tier half needs the item level. With one recorded this is exact; without,
                    // it is reported and left off rather than guessed, which can only WIDEN the
                    // result — never move it onto a count no item level could reach.
                    int tier = MaxSocketsForLevel(item);
                    if (tier < 0)
                    {
                        ItemLevelDependent.Add(property.PropertyId);
                    }
                    else if (tier < cap)
                    {
                        cap = tier;
                    }

                    // carried wins only when POSITIVE here (0x65f618 tests > 0, not != 0), and a
                    // non-positive roll falls back to the property's param (0x65f634).
                    int sockets = carried > 0 ? carried : Roll(property);
                    if (sockets <= 0)
                    {
                        sockets = property.Param;
                    }

                    if (sockets < 1)
                    {
                        sockets = 1;
                    }

                    if (sockets > cap)
                    {
                        sockets = cap;
                    }

                    if (sockets <= 0)
                    {
                        return 0;
                    }

                    // STATLIST_SetUnitStat at 0x65f667, so this SETS rather than adds, and the
                    // 0x800 socketed flag it also raises (0x65f659) is already in the record's own
                    // flags for a captured item.
                    SetRawStat(StatNumSockets, sockets, 0, into);
                    return sockets;
                }

                default:
                    UnsupportedFunc.Add(func);
                    return 0;
            }
        }

        /// <summary>
        /// The ItemTypes half of ITEM_GetMaxSockCount 0x62bc20, or -1 when the record carries no
        /// item level. The row is the item's PRIMARY type (`ITEM_GetItemData_wType` at 0x62bc32),
        /// with no equivalence walk.
        /// </summary>
        private int MaxSocketsForLevel(ItemIdentity item)
        {
            if (item.ItemLevel < 0 || _types == null)
            {
                return -1;
            }

            return _types.MaxSockets(_types.Row(_items.PrimaryTypeCode(item.ClassId)), item.ItemLevel);
        }

        /// <summary>
        /// The skill level funcs 11 and 19 derive when a property's max is non-positive. Both
        /// compute it identically: max == 0 steps every four levels above the skill's requirement
        /// (0x65f70a..0x65f725), max &lt; 0 divides the remaining levels by |max| first
        /// (0x65f75b..0x65f790). Returns -1 when the item level is absent, so the caller reports
        /// the property instead of inventing a level.
        /// </summary>
        private int SkillLevelFromItemLevel(ItemIdentity item, int skill, int max)
        {
            if (item.ItemLevel < 0)
            {
                return -1;
            }

            int required = SkillRequiredLevel(skill);

            if (max == 0)
            {
                // `(ilvl - req) / 4 + 1`, the divide truncating toward zero (`and edx, 3` then
                // `sar eax, 2` at 0x65f71a).
                int raw = (item.ItemLevel - required) / 4 + 1;

                // Clamped against the skill's own maxlvl, and note the comparison uses the
                // FLOORED value while the result keeps the raw one (0x65f72e..0x65f748).
                int floored = raw < 1 ? 1 : raw;
                int ceiling = _skills == null ? 20 : _skills.MaxLevel(skill);

                return floored >= ceiling ? ceiling : floored;
            }

            // 99 - req floored at 1, divided by |max| and floored at 1 again, then used as the step
            // size over the levels above the requirement (0x65f763..0x65f790).
            int span = 99 - required;
            if (span < 1)
            {
                span = 1;
            }

            int step = span / -max;
            if (step < 1)
            {
                step = 1;
            }

            int level = (item.ItemLevel - required) / step;

            // Floored at 1, as the other arm is (0x65f797).
            return level < 1 ? 1 : level;
        }

        private int SkillRequiredLevel(int skill)
        {
            if (_skills == null)
            {
                return 0;
            }

            // Out-of-range ids give -1, which would inflate `ilvl - req`; the handlers bound the
            // skill first, so this only guards a caller that did not.
            int required = _skills.RequiredLevel(skill);
            return required < 0 ? 0 : required;
        }

        // 0x65f8c6..0x65f8d8 and 0x65f8e1..0x65f90f: bias by +256, floor a non-positive result at
        // 0, then cap at the 10-bit field width.
        private static int ClampTimedBound(int bound)
        {
            int biased = bound + 256;
            if (biased <= 0)
            {
                return 0;
            }

            return biased > 0x3FF ? 0x3FF : biased;
        }

        /// <summary>
        /// The D2AddStatToStatsListEx path funcs 18 and 19 take directly, bypassing
        /// ITEMMOD_AddStatToItem: no nValShift on the value, always a SET, and none of
        /// AddStatToItem's poison-count side effect.
        /// </summary>
        private void SetRawStat(int statId, int value, int layer, IDictionary<int, int> into)
        {
            if (value == 0 || statId < 0)
            {
                return;
            }

            // 0x65f882 bounds the stat against ItemStatCost before the list is touched; only the
            // existence of the row matters here, since this path applies no nValShift.
            if (!_statCost.TryGetStat(statId, out _))
            {
                return;
            }

            into[ItemStatReader.PackStatKey(layer, statId)] = value;
        }

        // The shared tail of funcs 1..4 and 8: roll unless a value was carried in, then add.
        private int AddRolled(
            ItemProperty property, int nSet, int statId, int carried, IDictionary<int, int> into)
        {
            int value = carried != 0 ? carried : Roll(property);
            AddStat(nSet, statId, value, into);
            return value;
        }

        /// <summary>
        /// The min/max normalisation every handler shares. A genuine range needs the ITEM SEED
        /// (SEED_RollLimitedRandomNumber), which a record does not carry, so a ranged property
        /// resolves to one END of its range here and is reported through <see cref="RolledRanges"/>.
        ///
        /// Which end is a construction choice, not a claim about the game: an applier built with
        /// <see cref="RollEnd.Low"/> — the default, and what every render uses — reproduces the
        /// existing behaviour, and one built with <see cref="RollEnd.High"/> exists so the two can
        /// be diffed into a range. It is deliberately not settable after construction: one applier
        /// is shared across renders, and a switchable end would make that shared state.
        ///
        /// This is the ONLY place a range is consumed. Handlers that read Min and Max as separate
        /// parameters rather than as a range — func 11's chance and skill level, for one — do not
        /// come through here and are unaffected by the end.
        /// </summary>
        private int Roll(ItemProperty property)
        {
            int min = property.Min;
            int max = property.Max;

            if (max == min)
            {
                return min;
            }

            // Inverted ranges are real in shipped data — Cow King's Leathers carries FMin5/FMax5 as
            // 25/5 — so normalise before picking an end.
            if (max < min)
            {
                max = property.Min;
                min = property.Max;
            }

            if (min < max)
            {
                RolledRanges.Add(property.PropertyId);
            }

            return _end == RollEnd.High ? max : min;
        }

        /// <summary>Property ids whose value depends on the item seed and so is only the low end.</summary>
        public readonly SortedSet<int> RolledRanges = new SortedSet<int>();

        // ITEMMODS_PropertyFunc05. On a GEM the weapon-type test is false, so all three damage stats
        // are written; the per-stat floor keeps base + value at 1 or more.
        private int MinDamage(
            ItemIdentity item, ItemProperty property, int nSet, int carried,
            IDictionary<int, int> into)
        {
            int value = carried != 0 ? carried : Roll(property);
            bool weapon = IsWeapon(item);

            int oneHand = _items.GetInt(item.ClassId, "mindam");
            int twoHand = _items.GetInt(item.ClassId, "2handmindam");
            int missile = _items.GetInt(item.ClassId, "minmisdam");

            if (!weapon || oneHand != 0 || twoHand == 0)
            {
                AddFloored(nSet, StatMinDamage, value, oneHand, 1, into);
            }

            if (!weapon || twoHand != 0 || oneHand == 0)
            {
                AddFloored(nSet, StatSecondaryMinDamage, value, twoHand, 1, into);
            }

            if (!weapon || IsThrowable(item))
            {
                AddFloored(nSet, StatThrowMinDamage, value, missile, 1, into);
            }

            return value;
        }

        // ITEMMODS_PropertyFunc06. Same shape as func 5 but the floor is -base, not 1 - base.
        private int MaxDamage(
            ItemIdentity item, ItemProperty property, int nSet, int carried,
            IDictionary<int, int> into)
        {
            int value = carried != 0 ? carried : Roll(property);
            bool weapon = IsWeapon(item);

            int oneHand = _items.GetInt(item.ClassId, "maxdam");
            int twoHand = _items.GetInt(item.ClassId, "2handmaxdam");
            int missile = _items.GetInt(item.ClassId, "maxmisdam");

            if (!weapon || oneHand != 0 || twoHand == 0)
            {
                AddFloored(nSet, StatMaxDamage, value, oneHand, 0, into);
            }

            if (!weapon || twoHand != 0 || oneHand == 0)
            {
                AddFloored(nSet, StatSecondaryMaxDamage, value, twoHand, 0, into);
            }

            if (!weapon || IsThrowable(item))
            {
                AddFloored(nSet, StatThrowMaxDamage, value, missile, 0, into);
            }

            return value;
        }

        // `floorAt` is 1 for the min-damage family and 0 for the max-damage family: func 5 clamps to
        // `1 - base` and func 6 to `-base`, which is the one place the two are not mirror images.
        private void AddFloored(
            int nSet, int statId, int value, int baseDamage, int floorAt, IDictionary<int, int> into)
        {
            int result = value;

            if (baseDamage != 0 && baseDamage + value <= 0)
            {
                result = floorAt - baseDamage;
            }

            if (result != 0)
            {
                AddStat(nSet, statId, result, into);
            }
        }

        // ITEMMODS_PropertyFunc07. Enhanced damage is a percentage pair, except that on a weapon
        // where the percentage would round away to nothing it degrades into a flat +1 max damage.
        private int EnhancedDamage(
            ItemIdentity item, ItemProperty property, int nSet, int carried,
            IDictionary<int, int> into)
        {
            int value = carried != 0 ? carried : Roll(property);

            int oneHand = _items.GetInt(item.ClassId, "maxdam");
            int twoHand = _items.GetInt(item.ClassId, "2handmaxdam");
            int maxDamage = twoHand > oneHand ? twoHand : oneHand;

            long bonus = (long)value * maxDamage / 100;

            if (IsWeapon(item) && maxDamage + bonus <= maxDamage)
            {
                return MaxDamage(item, property, nSet, 1, into);
            }

            AddStat(nSet, StatMinDamagePercent, value, into);
            AddStat(nSet, StatMaxDamagePercent, value, into);
            return value;
        }

        // ITEMMODS_PropertyFunc17: the param takes precedence, otherwise roll.
        private int FixedOrRolled(
            ItemIdentity item, ItemProperty property, int nSet, int statId,
            IDictionary<int, int> into)
        {
            int value = property.Param;
            if (value == 0)
            {
                value = Roll(property);
                if (value == 0)
                {
                    return 0;
                }
            }

            if (statId == StatMaxDamage)
            {
                MaxDamage(item, property, nSet, value, into);
            }
            else
            {
                AddStat(nSet, statId, value, into);
            }

            return value;
        }

        /// <summary>
        /// ITEMMODS_AddPropertyToItemStatList 0x65ea50. A zero value writes nothing, an unknown stat
        /// writes nothing, and the value is stored SHIFTED LEFT by nValShift — the description engine
        /// shifts it back, so an unshifted value would render as a fraction of itself.
        /// </summary>
        private void AddStat(int nSet, int statId, int value, IDictionary<int, int> into)
        {
            AddStat(nSet, statId, value, 0, into);
        }

        private void AddStat(
            int nSet, int statId, int value, int layer, IDictionary<int, int> into)
        {
            if (value == 0 || statId < 0)
            {
                return;
            }

            StatDescriptor descriptor;
            if (!_statCost.TryGetStat(statId, out descriptor))
            {
                return;
            }

            int key = ItemStatReader.PackStatKey(layer, statId);
            int shifted = value << descriptor.ValShift;

            // nSet selects SET over ADD (0x65eac6 versus 0x65eb0a).
            if (nSet != 0)
            {
                into[key] = shifted;
            }
            else
            {
                int existing;
                into[key] = into.TryGetValue(key, out existing) ? existing + shifted : shifted;
            }

            if (statId != StatPoisonMaxDamage)
            {
                return;
            }

            // Poison damage drags a duration of 1 along with it or the description reads
            // "over 0 seconds".
            int countKey = ItemStatReader.PackStatKey(0, StatPoisonCount);
            if (nSet != 0)
            {
                if (!into.ContainsKey(countKey))
                {
                    into[countKey] = 1;
                }
            }
            else
            {
                int existing;
                into[countKey] = into.TryGetValue(countKey, out existing) ? existing + 1 : 1;
            }
        }

        private bool IsWeapon(ItemIdentity item)
        {
            return IsOfType(item, "weap");
        }

        // ITEMS_CheckItemTypeIfThrowable reads the PRIMARY type row's Throwable column directly —
        // no equivalence walk, unlike the weapon test.
        private bool IsThrowable(ItemIdentity item)
        {
            int row = _types.Row(_items.PrimaryTypeCode(item.ClassId));
            return _types.IsThrowable(row);
        }

        private bool IsOfType(ItemIdentity item, string code)
        {
            return _types.IsOfType(
                _types.Row(_items.PrimaryTypeCode(item.ClassId)),
                _types.Row(_items.SecondaryTypeCode(item.ClassId)),
                _types.Row(code));
        }
    }
}
