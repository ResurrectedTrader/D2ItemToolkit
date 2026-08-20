using System.Collections.Generic;

namespace D2ItemToolkit
{
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
    /// Only the funcs a gems.txt mod code can actually reach are implemented; the rest report
    /// themselves through <see cref="UnsupportedFunc"/> rather than silently applying nothing.
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

        private readonly PropertiesTable _properties;
        private readonly TxtItemStatCostTable _statCost;
        private readonly ItemTable _items;
        private readonly ItemTypeTree _types;

        public PropertyApplier(D2DataFiles data, ItemTable items, ItemTypeTree types)
        {
            _properties = new PropertiesTable(data.Properties, data.ItemStatCost);
            _statCost = data.ItemStatCost;
            _items = items;
            _types = types;
        }

        public PropertiesTable Properties { get { return _properties; } }

        /// <summary>Func codes reached that this port does not implement.</summary>
        public readonly SortedSet<int> UnsupportedFunc = new SortedSet<int>();

        /// <summary>
        /// Properties whose skill LEVEL the game derives from the item's own level (func 11 with a
        /// non-positive max, 0x65f4de / 0x65f514). The record carries no item level, so those land
        /// on the game's floor of 1 instead of the real value. No shipped gems.txt or sets.txt
        /// property takes that arm — Cow King's `gethit-skill` has max 5 — so this stays empty
        /// against stock data, and a test asserts it.
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
                    // skills.txt reqlevel — 0x65f4de for max == 0, 0x65f514 for max < 0 — and the
                    // record carries no item level, so they cannot be reproduced. The game's own
                    // floor in both is 1 (0x65f54d), which is what is used instead, and the
                    // property is reported rather than silently mis-levelled.
                    int level = property.Max;
                    if (level <= 0)
                    {
                        ItemLevelDependent.Add(property.PropertyId);
                        level = 1;
                    }

                    AddStat(nSet, statId, chance, (level & 0x3F) + (skill << 6), into);
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

                default:
                    UnsupportedFunc.Add(func);
                    return 0;
            }
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
        /// resolves to its LOW end here and is reported through <see cref="RolledRanges"/>.
        /// </summary>
        private int Roll(ItemProperty property)
        {
            int min = property.Min;
            int max = property.Max;

            if (max == min)
            {
                return min;
            }

            if (max < min)
            {
                max = property.Min;
                min = property.Max;
            }

            if (min < max)
            {
                RolledRanges.Add(property.PropertyId);
            }

            return min;
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
