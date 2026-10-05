using System.Collections.Generic;

namespace D2ItemToolkit
{
    /// <summary>
    /// D2R's mastery terms on requirements: SKILLS_GetWeaponMasteryBonus 0x14024d610 with nType 6
    /// (stat 203, the str/dex percent) and the stat-209 tail of ITEMS_GetRequiredLevel
    /// (0x140228913-0x140228aa0). Every caller passes no skill, so the skill is the viewer's
    /// last-used one.
    /// </summary>
    internal sealed class WeaponMastery
    {
        public const int StatItemRequirementPercent = 203;
        public const int StatItemLevelRequirementPercent = 209;

        private const uint BrokenOrUnequippable = 0x4100;
        private const int RangedSkill = 2;   // `rng` in the range linker filled at 0x1402009d3

        private readonly ItemTable _items;
        private readonly ItemTypeTree _types;
        private readonly TxtFile _skills;

        public WeaponMastery(D2DataFiles data, ItemTable items)
        {
            _items = items;
            _types = data.ItemTypes == null ? null : new ItemTypeTree(data.ItemTypes);
            _skills = data.SkillRows;
        }

        /// <summary>The stat-203 percent for this item and viewer; 0 when none applies.</summary>
        public int RequirementPercent(ItemIdentity item, ItemViewer viewer)
        {
            if (viewer == null || _types == null)
            {
                return 0;
            }

            int primary = Primary(item);
            int secondary = Secondary(item);

            // SKILLS_CalculateThrowingMasteryValues 0x14024d380: once engaged it returns even a 0,
            // and it matches the RAW 16-bit layer — no condition bits stripped, no 0-is-any — so
            // Levitate's `weap | 1 << 14` (16429) never matches an item type.
            if (ThrowingArmEngages(primary, viewer))
            {
                int thrown = int.MinValue;
                foreach (KeyValuePair<int, int> stat in viewer.Stats)
                {
                    if (ItemStatReader.StatFromKey(stat.Key) == StatItemRequirementPercent
                        && _types.IsOfType(primary, secondary, ItemStatReader.LayerFromKey(stat.Key) & 0xFFFF)
                        && stat.Value > thrown)
                    {
                        thrown = stat.Value;
                    }
                }

                return thrown == int.MinValue ? 0 : thrown;
            }

            return Best(StatItemRequirementPercent, primary, secondary, viewer);
        }

        /// <summary>
        /// The stat-209 term: the computed level plus a percent of it, with no clamp afterwards. No
        /// shipped row grants the stat. Its throwing arm never engages — the switch has no case 7
        /// (0x14024d4a4).
        /// </summary>
        public int ApplyLevelPercent(ItemIdentity item, ItemViewer viewer, int level)
        {
            if (viewer == null || _types == null)
            {
                return level;
            }

            int percent = Best(StatItemLevelRequirementPercent, Primary(item), Secondary(item), viewer);
            return percent == 0 ? level : level + ItemStatOps.ApplyPercentOverflowSafe(level, percent);
        }

        // The generic arm (0x14024d6c4-0x14024d7a2): the best entry whose layer applies — low 14
        // bits an item type the item is (0 = any), bits 14-15 a dual-melee condition (2 = only
        // while dual-wielding melee, 1 = only while not). INT_MIN start, so none is 0.
        private int Best(int statId, int primary, int secondary, ItemViewer viewer)
        {
            bool dualMelee = DualMeleeEquipped(viewer);
            int best = int.MinValue;

            foreach (KeyValuePair<int, int> stat in viewer.Stats)
            {
                if (ItemStatReader.StatFromKey(stat.Key) != statId)
                {
                    continue;
                }

                int layer = ItemStatReader.LayerFromKey(stat.Key) & 0xFFFF;
                int condition = layer >> 14;
                if ((condition == 2 && !dualMelee) || (condition == 1 && dualMelee))
                {
                    continue;
                }

                int type = layer & 0x3FFF;
                if ((type == 0 || _types.IsOfType(primary, secondary, type)) && stat.Value > best)
                {
                    best = stat.Value;
                }
            }

            return best == int.MinValue ? 0 : best;
        }

        /// <summary>
        /// The throwing arm's gate: a throwable PRIMARY type (ITEMS_CheckIfThrowable 0x140228c10),
        /// and a last-used skill whose itypea1 is-a `thro` (0x14024d49b) with range `rng`.
        /// </summary>
        private bool ThrowingArmEngages(int primary, ItemViewer viewer)
        {
            if (viewer.LastUsedSkill < 0 || _skills == null || viewer.LastUsedSkill >= _skills.RowCount)
            {
                return false;
            }

            if (!_types.IsThrowable(primary))
            {
                return false;
            }

            int itype = _types.Row(_skills.GetString(viewer.LastUsedSkill, "itypea1").Trim());
            if (itype <= 0 || !_types.IsUnder(itype, _types.Row("thro")))
            {
                return false;
            }

            return RangeOf(_skills.GetString(viewer.LastUsedSkill, "range").Trim()) == RangedSkill;
        }

        // The range linker, in its fill order at 0x1402009d3: none, h2h, rng, both, loc.
        private static int RangeOf(string code)
        {
            switch (code.ToLowerInvariant())
            {
                case "h2h": return 1;
                case "rng": return 2;
                case "both": return 3;
                case "loc": return 4;
                default: return 0;
            }
        }

        /// <summary>
        /// UNITS_Has_Two_Melee_Equipped 0x140239c30 over the viewer's worn body slots 0..10. A
        /// one-handed weapon class (1hs, 1ht, ht1) is matched by being, or not being, the LEFT-hand
        /// weapon; anything else by items `component` 5 or 6. Both matches must be `mele` and
        /// neither broken nor flagged 0x4000.
        ///
        /// The left-hand weapon is inv->dwLeftItemGUID (0x14023fbb0), which no record carries: it
        /// is taken as body slot 5 when that is a weapon, else 4 — what the GUID maintenance gives
        /// for every state but two weapons equipped while broken and repaired in place.
        /// </summary>
        public bool DualMeleeEquipped(ItemViewer viewer)
        {
            if (viewer == null || _types == null)
            {
                return false;
            }

            ItemIdentity left = IsA(viewer.Body[5], "weap") ? viewer.Body[5]
                : IsA(viewer.Body[4], "weap") ? viewer.Body[4]
                : null;

            ItemIdentity other = null;     // INVENTORY_GetCompositItem(inv, 6) 0x14023fd90
            ItemIdentity hand = null;      // the inline component-5 walk
            for (int slot = 0; slot <= 10; ++slot)
            {
                ItemIdentity item = viewer.Body[slot];
                if (item == null)
                {
                    continue;
                }

                string weaponClass = _items.GetString(item.ClassId, "wclass").Trim().ToLowerInvariant();
                bool oneHanded = weaponClass == "1hs" || weaponClass == "1ht" || weaponClass == "ht1";
                int component = _items.GetInt(item.ClassId, "component");

                if (other == null && (oneHanded ? item != left : component == 6))
                {
                    other = item;
                }

                if (hand == null && (oneHanded ? item == left : component == 5))
                {
                    hand = item;
                }
            }

            return Usable(other) && Usable(hand);
        }

        private bool Usable(ItemIdentity item)
        {
            return item != null && IsA(item, "mele") && ((uint)item.Flags & BrokenOrUnequippable) == 0;
        }

        private bool IsA(ItemIdentity item, string code)
        {
            return item != null && _types.IsOfType(Primary(item), Secondary(item), _types.Row(code));
        }

        private int Primary(ItemIdentity item)
        {
            return _types.Row(_items.PrimaryTypeCode(item.ClassId));
        }

        private int Secondary(ItemIdentity item)
        {
            return _types.Row(_items.SecondaryTypeCode(item.ClassId));
        }
    }
}
