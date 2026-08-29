using System;
using System.Collections.Generic;
using System.Text;

namespace D2ItemToolkit
{
    // Locale ids the section writers emit.
    internal static class SectionStringIds
    {
        public const int Socketed = 3453;           // "Socketed"
        public const int DurabilityLabel = 3457;    // "Durability:"
        public const int RequiredStrength = 3458;   // "Required Strength:"
        public const int RequiredDexterity = 3459;  // "Required Dexterity:"
        public const int ArmorClass = 3461;         // "Defense:"
        public const int Of = 3463;                 // "of"
        public const int To = 3464;                 // "to"
        public const int SmiteDamage = 3468;        // "Smite Damage:"
        public const int RequiredLevel = 3469;      // "Required Level:"
        public const int EtherealCannotBeRepaired = 22745;
        public const int KickDamage = 21782;
        public const int OneHandDamage = 3465;      // "One-Hand Damage:"
        public const int TwoHandDamage = 3466;      // "Two-Hand Damage:"
        public const int ThrowDamage = 3467;        // "Throw Damage:"
        public const int BlockChance = 11018;       // "Chance to Block: " (trailing space)
        public const int QuantityLabel = 3462;      // "Quantity:"
        public const int Dash = 3996;               // "-"
        public const int CharmDescription = 20438;
        public const int Unidentified = 3455;       // 0xD7F at 0x48e943
        public const int ElixirPlus = 4002;         // prefixed to a POSITIVE elixir value only
        public const int RunewordOpen = 20506;

        // 0x48ec9a / 0x48ecc4: the two quest-usage lines, for `box ` and `bkd ` respectively.
        public const int RightClickToOpen = 2204;
        public const int RightClickToRead = 2205;

        // INV_ShowBookTooltip 0x48d08c pushes 0x89B and 0x48d0a8 pushes 0x89E.
        public const int RightClickToUse = 2203;
        public const int InsertScrolls = 2206;

        // INV_FormatSocketFillerDesc appends 11080 after the four blocks (0x48661f); each block ends
        // with 3852 (0x4e64f2).
        public const int SocketFillerClose = 11080;
        public const int SocketFillerBlockClose = 3852;

        // word_721E88 holds 4088..4093 at stride 6. Bucket 0 IS reachable: a viewer-less tooltip
        // takes the offset 5 that dword_722078[-2] supplies, and speed 27 then indexes one past
        // dword_721F10's 90 entries onto dword_722078[0] = 0 (0x486283).
        public const int FirstSpeedWord = 4088;
    }

    // Builds the 18 buffers from a v2 record plus the embedded tables. Writers not yet implemented
    // return null, which the composer treats as "section does not apply".
    internal sealed class RecordSections : IItemTooltipSections
    {
        private const int StatSockets = 194;
        private const int StatDurability = 72;
        private const int StatMaxDurability = 73;
        private const int StatMaxDurabilityPercent = 75;
        private const int StatArmorClass = 31;
        private const int StatQuestDifficulty = 356;
        private const int StatIndestructible = 152;
        private const int StatToBlock = 20;
        private const int StatMinDamage = 21;
        private const int StatMaxDamage = 22;
        private const int StatSecondaryMinDamage = 23;
        private const int StatSecondaryMaxDamage = 24;
        // 18 is item_mindamage_percent and 17 item_maxdamage_percent — that way round, per
        // D2StatList.h; they have been transposed in this file once before.
        private const int StatMinDamagePercent = 18;
        private const int StatMaxDamagePercent = 17;

        private const int StatThrowMinDamage = 159;
        private const int StatThrowMaxDamage = 160;

        private const int StatQuantity = 70;
        private const int StatValue = 71;
        private const int StatFasterAttackRate = 93;
        private const int StatDamageByTime = 272;
        private const int StatDamagePercentByTime = 273;

        private const int MaxBlockChance = 75;

        private const int PaladinClass = 3;

        // TXT_ItemTypes_GetClass returns the class index and the gate compares it against 3;
        // itemtypes.txt carries the code rather than the index, and locale 10917+3 is
        // "(Paladin Only)", which fixes 3 = Paladin.
        private const string PaladinClassCode = "pal";
        private const int AssassinClass = 6;

        private readonly D2DataFiles _data;
        private readonly ItemTable _items;
        private readonly ItemTypeTree _types;
        private readonly ItemIdentity _item;
        private readonly ItemViewer _viewer;
        private readonly IDictionary<int, int> _stats;
        private readonly ItemNameBuilder _names;
        private readonly IDictionary<int, uint> _sockets;

        // The `base` source group on its own. INV_CalcWeaponDamageRange decides its pModified flag by
        // comparing the BASE stat against the merged one (0x485300), so both are needed.
        private readonly IDictionary<int, int> _baseStats;
        private readonly GemTable _gemTable;
        private readonly RequiredLevelCalculator _requiredLevel;
        private readonly PropertyApplier _propertyApplier;
        private readonly SkillDamage _skillDamage;
        private readonly EquipRequirements _requirements;
        private readonly AttackSpeedCalculator _attackSpeed;

        private readonly ItemViewer _clientPlayer;

        // GetDificulity 0x48cb38 is game state, and it arrives through CreateContext because that
        // is the only entry point that has it. GetSection(ItemName) reads it for the quest colour,
        // so CreateContext must run first — every path builds the context before composing, and a
        // caller that skips it gets difficulty 0, which is what a viewerless render meant anyway.
        private int _difficulty;
        private readonly MissileTable _missiles;
        private readonly IList<ItemUnit> _socketUnits;

        public RecordSections(
            D2DataFiles data,
            ItemTable items,
            ItemTypeTree types,
            ItemIdentity item,
            ItemViewer viewer,
            IDictionary<int, int> stats,
            // Explicit rather than optional: each of these degrades the output SILENTLY when it
            // is missing. No baseStats and every damage, defense and durability number gets a
            // spurious colour-3 marker, because BaseStat() reads 0 and everything looks modified.
            // No sockets and the name loses its "Gemmed" prefix; no socketUnits and a socketed
            // jewel's affix requirement drops out of the required level.
            IDictionary<int, uint> sockets,
            IDictionary<int, int> baseStats,
            IList<ItemUnit> socketUnits,
            // INV_FormatAttackSpeedText 0x486201 and 0x486250 ignore the tooltip's own unit and
            // read GetPlayerUnit_0 (0x463de0, the client player) instead, so a mercenary's weapon
            // is still timed against the CHARACTER — both the frame lookup and the speed bucket's
            // class offset. Null falls back to the viewer, which is right whenever they are the
            // same unit, and that is every case but a merc panel.
            ItemViewer clientPlayer = null)
        {
            if (data == null) throw new ArgumentNullException("data");
            if (items == null) throw new ArgumentNullException("items");
            if (types == null) throw new ArgumentNullException("types");
            if (item == null) throw new ArgumentNullException("item");

            _data = data;
            _items = items;
            _types = types;
            _item = item;
            _viewer = viewer;
            _clientPlayer = clientPlayer ?? viewer;
            _stats = stats ?? new Dictionary<int, int>();
            _names = new ItemNameBuilder(data, items, types);
            _sockets = sockets ?? new SortedDictionary<int, uint>();
            _baseStats = baseStats ?? new Dictionary<int, int>();
            _socketUnits = socketUnits;
            _missiles = new MissileTable(data.Missiles, data.ElementTypes);
            _gemTable = new GemTable(data.Gems, items);
            _propertyApplier = new PropertyApplier(data, items, types);
            _skillDamage = new SkillDamage(data.SkillRows);
            _gemTable.ResolvePropertyCodesWith(_propertyApplier.Properties.RowForCode);
            _requiredLevel = new RequiredLevelCalculator(data, items);
            _requirements = new EquipRequirements(data, items);
            _attackSpeed = new AttackSpeedCalculator(data, items);
        }

        public string LineTerminator
        {
            get { return _data.Strings.GetByIndex(DescStringIds.Newline); }
        }

        /// <summary>
        /// The generator the composer's Modifiers block has to be built with.
        /// SKILLDESC_BuildStatListDesc 0x4e49c0 walks the described UNIT'S statlists, so the damage
        /// aggregate, the undead line and the never-breaks gate all need the same stats these
        /// sections see. A generator built without them degrades paired damage into one line per
        /// stat and drops the other two lines entirely.
        /// </summary>
        /// <param name="modifierStats">
        /// The ItemStatView.Modifiers() set. Required, not optional: pass the full stats here and
        /// base stats reach the damage aggregate and the 23/24 suppression, which the temp list
        /// the engine builds at 0x4e612b never contains.
        /// </param>
        public ItemDescriptionGenerator CreateModifierGenerator(
            IDictionary<int, int> modifierStats)
        {
            return _data.CreateGenerator(
                new SynthesisedStatValues(
                    modifierStats ?? _stats, _item, _viewer, _items, _types, _stats));
        }

        /// <summary>
        /// The composer's context for this item. `difficulty` is GetDificulity() (0x48cb38), the one
        /// input that is game state rather than unit state.
        /// </summary>
        public ItemTooltipContext CreateContext(int difficulty = 0)
        {
            var context = new ItemTooltipContext();
            context.Quality = (ItemQuality)_item.Quality;
            context.Flags = unchecked((ItemTooltipFlags)(uint)_item.Flags);

            context.IsWeaponOrArmorType =
                _types.IsOfType(PrimaryType(), SecondaryType(), _types.Row("weap"))
                || _types.IsOfType(PrimaryType(), SecondaryType(), _types.Row("armo"));

            // Only ITEM_BuildSetItemTooltip reads this — 0x48d681 wraps its smite and block lines
            // in one IsOfType(item, 51).
            context.IsShieldType =
                _types.IsOfType(PrimaryType(), SecondaryType(), _types.Row("shld"));

            context.ForcesCraftedColor = ForcesRuneColor();

            // 0x48e44c `cmp eax, 12h` on the items row's own wType WORD (+0x11E) — an exact
            // compare, not an IsOfType walk — then 0x48e451 diverts to INV_ShowBookTooltip and
            // 0x48e45c returns, so the generic tooltip is never built for a tome. Only `tbk` and
            // `ibk` reach itemtypes row 18.
            context.IsBook = PrimaryType() == _types.Row("book");

            // items.txt nQuest +0x12A and nQuestDiffCheck +0x12B (0x48cb0b / 0x48cb19).
            _difficulty = difficulty;

            context.IsQuestItem = _items.GetInt(_item.ClassId, "quest") != 0;
            context.IsWirtsLeg = string.Equals(
                PaddedCode(_item.ClassId), WirtsLegCode, StringComparison.Ordinal);
            return context;
        }

        // 0x48e9b0: eleven items.txt code dwords, plus IsOfType(item, 74 rune), force the NAME
        // colour to 8. The codes are compared as four-byte little-endian dwords, so they are the
        // `code` cell padded to four characters with spaces.
        private static readonly string[] RuneColorCodes =
        {
            "ceh ", "bet ", "tes ", "fed ", "toa ", "dhn ", "bey ", "mbr ", "pk1 ", "pk2 ", "pk3 ",
        };

        private const string WirtsLegCode = "leg ";

        private const string HoradricCubeCode = "box ";
        private const string CairnStonesKeyCode = "bkd ";

        /// <summary>
        /// 0x48ec3f. Runs AFTER the whole tooltip is assembled and PREPENDS to the finished buffer,
        /// so it is the bottom row on screen — hence its place at the head of the append order.
        ///
        /// The gates are `quest != 0` (items +0x12A), code not `leg `, ShopMode exactly 0, and the
        /// unit's dwMode 0 (in inventory). Twenty-four other quest items pass the outer gate but
        /// fall to the colour-only branch at 0x48ece5 and emit no line.
        /// </summary>
        private string QuestUsage(int shopMode = 0, bool inInventory = true)
        {
            if (_items.GetInt(_item.ClassId, "quest") == 0
                || shopMode != 0
                || !inInventory)
            {
                return null;
            }

            string code = PaddedCode(_item.ClassId);
            if (string.Equals(code, WirtsLegCode, StringComparison.Ordinal))
            {
                return null;
            }

            if (string.Equals(code, HoradricCubeCode, StringComparison.Ordinal))
            {
                return Str(SectionStringIds.RightClickToOpen) + Terminator;
            }

            if (string.Equals(code, CairnStonesKeyCode, StringComparison.Ordinal))
            {
                return Str(SectionStringIds.RightClickToRead) + Terminator;
            }

            return null;
        }

        private bool ForcesRuneColor()
        {
            string code = PaddedCode(_item.ClassId);

            foreach (string forced in RuneColorCodes)
            {
                if (string.Equals(code, forced, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return _types.IsOfType(PrimaryType(), SecondaryType(), _types.Row("rune"));
        }

        private string PaddedCode(int classId)
        {
            string code = _items.Code(classId) ?? string.Empty;
            return code.Length >= 4 ? code.Substring(0, 4) : code.PadRight(4, ' ');
        }

        /// <summary>
        /// DELIBERATE DEVIATION, and the only one in this class.
        ///
        /// ITEM_CheckEquipRequirements 0x62eaf0 reads the viewer's attributes through
        /// GetStatUnsignedValue, which returns 0 for a null unit (0x625483). Strength and dexterity
        /// are then gated on `value > 0` (0x62ebd5 / 0x62ec31), so a null unit reports BOTH as
        /// unmet, and level compares against 0 too (0x62eca1) — the game would paint all three red.
        ///
        /// That branch is not reachable in the game: LoadItemDesc resolves its unit from
        /// GetPlayerUnit (0x48dee0) and only ever draws the local player's tooltip, so "no viewer"
        /// is a concept this library invented. Painting a requirement red when nobody has been
        /// asked to meet it states something false, so a viewerless render leaves them white. Pass
        /// a viewer to get the game's answer.
        /// </summary>
        public bool IsRequirementUnmet(ItemTooltipSection section)
        {
            if (_viewer == null)
            {
                return false;
            }

            switch (section)
            {
                case ItemTooltipSection.RequiredLevel:
                    return !_requirements.MetLevel(_item, _viewer, _stats, _socketUnits, _sockets);
                case ItemTooltipSection.RequiredStrength:
                    return !_requirements.MetStrength(_item, _viewer, _stats);
                case ItemTooltipSection.RequiredDexterity:
                    return !_requirements.MetDexterity(_item, _viewer, _stats);
                case ItemTooltipSection.ClassRestriction:
                    return !_requirements.MetClass(_item, _viewer);
                default:
                    return false;
            }
        }

        public string GetSection(ItemTooltipSection section)
        {
            switch (section)
            {
                case ItemTooltipSection.ItemName:
                    return QuestNameColorPrefix()
                           + _names.Build(_item, _sockets.Count);

                case ItemTooltipSection.Unidentified:
                    return Unidentified();

                case ItemTooltipSection.Modifiers:
                    return ElixirDescription();

                case ItemTooltipSection.EtherealSocketed:
                    return EtherealSocketed();
                case ItemTooltipSection.Durability:
                    return Durability();
                case ItemTooltipSection.RequiredLevel:
                    return RequiredLevel();
                case ItemTooltipSection.RequiredStrength:
                    return Requirement("reqstr", SectionStringIds.RequiredStrength);
                case ItemTooltipSection.RequiredDexterity:
                    return Requirement("reqdex", SectionStringIds.RequiredDexterity);
                case ItemTooltipSection.ArmorClass:
                    return ArmorClass();
                case ItemTooltipSection.SmiteOrKickDamage:
                    return SmiteOrKick();
                case ItemTooltipSection.WeaponDamage:
                    return WeaponDamage();
                case ItemTooltipSection.BlockChance:
                    return BlockChance();
                case ItemTooltipSection.ClassRestriction:
                    return ClassRestriction();
                case ItemTooltipSection.QuantityAndSpellDescription:
                    return QuantityAndSpellDescription();
                case ItemTooltipSection.CharmDescription:
                    return CharmDescription();
                case ItemTooltipSection.QuestUsage:
                    return QuestUsage();
                case ItemTooltipSection.BookQuantity:
                    return BookQuantity();
                case ItemTooltipSection.BookRightClickToUse:
                    return BookUsageLine(SectionStringIds.RightClickToUse);
                case ItemTooltipSection.BookInsertScrolls:
                    return BookUsageLine(SectionStringIds.InsertScrolls);
                case ItemTooltipSection.RuneLetters:
                    return RuneLetters();
                case ItemTooltipSection.AttackSpeed:
                    return AttackSpeed();
                case ItemTooltipSection.SocketFillerDescription:
                    return SocketFillerDescription();
                default:
                    return null;
            }
        }

        /// <summary>
        /// GetItemName's tail, 0x48cb0b-0x48ce6d. Gated on items.txt `quest` (+0x12A); with
        /// `questdiffcheck` (+0x12B) set and stat 356 below the current difficulty it prepends
        /// colour 1 (0x48cb50), otherwise colour 4 unless the code is `leg ` (0x48ce59 compares the
        /// dword 0x2067656C — Wirt's Leg).
        ///
        /// AppendAsWideChar PREPENDS, so this lands at the START of the name buffer and LoadItemDesc
        /// then stacks the section's own v105 marker in front of it. Both are in the string the game
        /// draws, which is why this is text rather than a section colour.
        /// </summary>
        private string QuestNameColorPrefix()
        {
            if (_items.GetInt(_item.ClassId, "quest") == 0)
            {
                return string.Empty;
            }

            if (_items.GetInt(_item.ClassId, "questdiffcheck") != 0
                && Stat(StatQuestDifficulty) < _difficulty)
            {
                return ItemTooltipColor.Marker + "1";
            }

            return string.Equals(
                       PaddedCode(_item.ClassId), WirtsLegCode, StringComparison.Ordinal)
                ? string.Empty
                : ItemTooltipColor.Marker + "4";
        }

        private string Str(int id)
        {
            return _data.Strings.GetByIndex(id) ?? string.Empty;
        }

        private string Space { get { return Str(DescStringIds.Space); } }

        private string Terminator { get { return Str(DescStringIds.Newline); } }

        private int Stat(int statId)
        {
            int value;
            return _stats.TryGetValue(ItemStatReader.PackStatKey(0, statId), out value) ? value : 0;
        }

        // 0x484b10. Both halves are optional; the ", " separator is an ASCII literal, not a locale
        // string. Socket count is truncated to a byte at 0x484c2a.
        private string EtherealSocketed()
        {
            bool ethereal = _item.Has(ItemRecordFlags.Ethereal);
            bool socketed = _item.Has(ItemRecordFlags.Socketed);

            if (!ethereal && !socketed)
            {
                return null;
            }

            var text = new StringBuilder();

            if (ethereal)
            {
                text.Append(Str(SectionStringIds.EtherealCannotBeRepaired));
            }

            if (socketed)
            {
                if (ethereal)
                {
                    text.Append(", ");
                }

                text.Append(Str(SectionStringIds.Socketed))
                    .Append(Space)
                    .Append('(')
                    .Append(Stat(StatSockets) & 0xFF)
                    .Append(')');
            }

            return text.Append(Terminator).ToString();
        }

        // 0x484e90. Gates from ITEM_CheckIfItemHasDurability (0x629930).
        private string Durability()
        {
            if (_items.GetInt(_item.ClassId, "nodurability") != 0)
            {
                return null;
            }

            if (_items.GetInt(_item.ClassId, "durability") <= 0)
            {
                return null;
            }

            if (Stat(StatIndestructible) > 0)
            {
                return null;
            }

            int max = Stat(StatMaxDurability);
            if (max <= 0)
            {
                return null;
            }

            if (IsThrowable())
            {
                return null;
            }

            // 0x484f0b: STATLIST_GetStatBonusFromLists is merged-minus-base (0x625570), and the
            // marker goes on the MAX number alone (0x484fc6) — the current value never carries one.
            string marker = Bonus(StatMaxDurabilityPercent) != 0
                ? ItemTooltipColor.Marker + "3"
                : string.Empty;

            return Str(SectionStringIds.DurabilityLabel) + Space + Stat(StatDurability)
                   + Space + Str(SectionStringIds.Of) + Space + marker + max + Terminator;
        }

        // 0x484ff0, called only when ITEM_GetRequiredLevel returns more than 1 (0x48e565 `jle`).
        private string RequiredLevel()
        {
            // 0x48e54f: the caller wraps the whole block in CheckItemFlag(item, 0x10 IDENTIFIED).
            if (!_item.Has(ItemRecordFlags.Identified))
            {
                return null;
            }

            int level = _requiredLevel.Calculate(_item, _viewer, _stats, _socketUnits, _sockets);
            if (level <= 1)
            {
                return null;
            }

            return Str(SectionStringIds.RequiredLevel) + Space + level + Terminator;
        }

        // 0x4850a0 / 0x485170. The caller skips the section when the BASE requirement is 0
        // (0x48e6a2 / 0x48e6c6), and the total shares EquipRequirements' expression so the number
        // and the met flag can never disagree.
        private string Requirement(string column, int labelId)
        {
            if (_items.GetInt(_item.ClassId, column) <= 0)
            {
                return null;
            }

            int total = _requirements.Requirement(_item, column, _stats);
            if (total <= 0)
            {
                return null;
            }

            return Str(labelId) + Space + total + Terminator;
        }

        // 0x485ee0. The by-time contributions are already folded into the runtime value when the
        // producer supplies one; otherwise the plain merged stat.
        private string ArmorClass()
        {
            int armor = Stat(StatArmorClass);
            if (armor <= 0)
            {
                return null;
            }

            if (!_types.IsOfType(PrimaryType(), SecondaryType(), _types.Row("armo")))
            {
                return null;
            }

            // 0x485fb1: SERVER_GetUnitStat reads the item's BASE stat 31 and any difference from
            // the merged value sets the flag the marker at 0x4860de depends on.
            string marker = BaseStat(StatArmorClass) != armor
                ? ItemTooltipColor.Marker + "3"
                : string.Empty;

            return Str(SectionStringIds.ArmorClass) + Space + marker + armor + Terminator;
        }

        // 0x485d40. Shield gives Smite for a Paladin, boots give Kick for an Assassin. The class
        // gate is the caller's, and it must also require a PLAYER — LoadItemDesc omits that check.
        private string SmiteOrKick()
        {
            if (_viewer == null || !_viewer.IsPlayer)
            {
                return null;
            }

            int label;
            int extraMin = 0;
            int extraMax = 0;

            if (_types.IsOfType(PrimaryType(), SecondaryType(), _types.Row("shld")))
            {
                if (_viewer.ClassId != PaladinClass)
                {
                    return null;
                }

                // 0x48e768/0x48e778: a class-restricted shield whose class is not Paladin is
                // refused outright. `head` (Voodoo Heads) is Equiv1=shld with Class=nec, so all
                // fifteen ne* rows are shields the game will not smite with — without this a
                // Paladin sees "Smite Damage: 0 to 0" on every shrunken head.
                string restriction = _types.ClassCode(PrimaryType());
                if (restriction.Length != 0
                    && !string.Equals(restriction, PaladinClassCode, StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                label = SectionStringIds.SmiteDamage;

                // 0x485df1: both halves come from SKILL_CalcMin/MaxDamage for Holy Shield at the
                // player's skill level, shifted back down by 8.
                HolyShieldDamage(out extraMin, out extraMax);
            }
            else if (_types.IsOfType(PrimaryType(), SecondaryType(), _types.Row("boot")))
            {
                if (_viewer.ClassId != AssassinClass)
                {
                    return null;
                }

                label = SectionStringIds.KickDamage;
            }
            else
            {
                return null;
            }

            int min = _items.GetInt(_item.ClassId, "mindam") + extraMin;
            int max = _items.GetInt(_item.ClassId, "maxdam") + extraMax;

            return Str(label) + Space + min + Space + Str(SectionStringIds.To) + Space + max
                   + Terminator;
        }

        // 0x485410. Two-handed weapons use stats 23/24 and label 3466; one-handed use 21/22 and
        // 3465. A throwable weapon also gets a throw line (stats 159/160).
        //
        // OPEN, and UNTRACED. INV_CalcWeaponDamageRange 0x485240 does three things this does not:
        // it takes *pMax as MAX(mergedMax, mergedMin), then adds stat 272 and a percent of the
        // running total from stat 273, and it reads the merged pair off the UNIT after temporarily
        // attaching the item to it (STATLIST_SetItemStatActive 0x4852a1, restored at 0x4852cb /
        // 0x4852d8). Here the pair is read straight off the item, and 272/273 only ever feed
        // DamageIsModified. Whether any of the three moves a shipped item is uncounted.
        private string WeaponDamage()
        {
            if (!_types.IsOfType(PrimaryType(), SecondaryType(), _types.Row("weap")))
            {
                return null;
            }

            // 0x485459 tests tpot FIRST and takes an arm that writes the buffer outright, so a
            // throwing potion gets ONE line and none of the ordinary damage or throw text.
            if (_types.IsOfType(PrimaryType(), SecondaryType(), _types.Row("tpot")))
            {
                return ThrowingPotionDamage();
            }

            // 0x48e704 / 0x48e716: the gate is GetTxtMinDamage >= 0 AND GetTxtMaxDamage >= 0, which
            // read the item's own stat 21 and 22. ZERO PASSES — a weapon with no damage stats still
            // gets a line, and the min+1 clamp turns it into "0 to 1". Only a NEGATIVE value skips it.
            if (Stat(StatMinDamage) < 0 || Stat(StatMaxDamage) < 0)
            {
                return null;
            }

            string text = BarbarianDualWield()
                ? DualWieldDamage()
                : SingleDamageLine();

            if (IsThrowable())
            {
                // 0x485ab6: the throw block has no min+1 clamp either.
                string throwLine = DamageLine(
                    SectionStringIds.ThrowDamage, StatThrowMinDamage, StatThrowMaxDamage, false,
                    throwShape: true);

                if (throwLine != null)
                {
                    // Appended after, so the reversal puts Throw Damage ABOVE the other line.
                    text = (text ?? string.Empty) + throwLine;
                }
            }

            return text;
        }

        /// <summary>
        /// The same routing <see cref="WeaponDamage"/> performs, collecting numbers instead of
        /// writing text. Both walk the same gates and the same
        /// <see cref="DamageValues"/>, and a test asserts the numbers here are the numbers in the
        /// rendered line, so the two cannot drift apart silently.
        /// </summary>
        internal List<ItemDamageRange> WeaponDamageValues()
        {
            var lines = new List<ItemDamageRange>();

            if (!_types.IsOfType(PrimaryType(), SecondaryType(), _types.Row("weap")))
            {
                return lines;
            }

            // 0x485459 takes the tpot arm outright, so such an item has no other damage line.
            if (_types.IsOfType(PrimaryType(), SecondaryType(), _types.Row("tpot")))
            {
                MissileThrowDamage potion;
                if (_missiles.TryGetThrowDamage(
                        _items.GetInt(_item.ClassId, "missiletype"), out potion))
                {
                    lines.Add(new ItemDamageRange(
                        ItemDamageKind.ThrowingPotion, potion.Min, potion.Max, false));
                }

                return lines;
            }

            if (Stat(StatMinDamage) < 0 || Stat(StatMaxDamage) < 0)
            {
                return lines;
            }

            // DISPLAY order, which is the reverse of the order the buffers are written in. The
            // throw line is appended last (0x485ab6) so it ends up on TOP; the dual-wield pair is
            // written two-hand first (0x4856a2 before 0x4857c5) so ONE-HAND ends up above it.
            if (IsThrowable())
            {
                lines.Add(DamageValues(
                    ItemDamageKind.Throw, StatThrowMinDamage, StatThrowMaxDamage, false, true));
            }

            if (BarbarianDualWield())
            {
                lines.Add(DamageValues(
                    ItemDamageKind.OneHand, StatMinDamage, StatMaxDamage, false, false));
                lines.Add(DamageValues(
                    ItemDamageKind.TwoHand,
                    StatSecondaryMinDamage, StatSecondaryMaxDamage, false, false));

                return lines;
            }

            bool twoHanded = _items.GetInt(_item.ClassId, "2handed") != 0;

            lines.Add(DamageValues(
                twoHanded ? ItemDamageKind.TwoHand : ItemDamageKind.OneHand,
                twoHanded ? StatSecondaryMinDamage : StatMinDamage,
                twoHanded ? StatSecondaryMaxDamage : StatMaxDamage,
                true,
                false));

            return lines;
        }

        private static ItemDamageKind KindOf(int labelId)
        {
            if (labelId == SectionStringIds.TwoHandDamage) return ItemDamageKind.TwoHand;
            if (labelId == SectionStringIds.ThrowDamage) return ItemDamageKind.Throw;

            return ItemDamageKind.OneHand;
        }

        /// <summary>
        /// 0x48545f. The numbers come from the item's missiles.txt record, not from its stats, and
        /// the elemental type picks a colour for BOTH numbers (jump table 0x4854d0). The label gets
        /// an explicit colour 0 of its own (0x4854af), and the "to max" half is dropped outright
        /// when the two ends agree (0x4855bd).
        /// </summary>
        private string ThrowingPotionDamage()
        {
            MissileThrowDamage damage;
            if (!_missiles.TryGetThrowDamage(
                    _items.GetInt(_item.ClassId, "missiletype"), out damage))
            {
                return null;
            }

            string marker = ItemTooltipColor.Marker + (char)('0' + damage.Color);

            var text = new StringBuilder();
            text.Append(ItemTooltipColor.Marker).Append('0')
                .Append(Str(SectionStringIds.ThrowDamage))
                .Append(Space).Append(marker).Append(damage.Min);

            if (damage.Min != damage.Max)
            {
                text.Append(Space).Append(Str(SectionStringIds.To))
                    .Append(Space).Append(marker).Append(damage.Max);
            }

            return text.Append(Terminator).ToString();
        }

        /// <summary>
        /// BARBARIAN_CheckItemData_b1or2Handed_isTrue 0x62a1e0: a PLAYER (dwUnitType 0) of class 4
        /// holding an item whose items.txt `1or2handed` byte (+0x13D) is set. `2handed` is not
        /// consulted, and neither is anything about what else is equipped.
        /// </summary>
        private bool BarbarianDualWield()
        {
            return _viewer != null
                   && _viewer.IsPlayer
                   && _viewer.ClassId == BarbarianClass
                   && _items.GetInt(_item.ClassId, "1or2handed") != 0;
        }

        private const int BarbarianClass = 4;

        /// <summary>
        /// 0x485669 onwards. TWO-HAND comes first, then one-hand, each with its own colour 0 prepend
        /// (0x4858c3 / 0x4858d0) and its own terminator. Note what is ABSENT: this path never applies
        /// the `max = min + 1` clamp that the single-line path does at 0x485931, so a dual-wielding
        /// Barbarian can be shown a weapon whose min and max are equal.
        /// </summary>
        private string DualWieldDamage()
        {
            string twoHand = DamageLine(
                SectionStringIds.TwoHandDamage,
                StatSecondaryMinDamage, StatSecondaryMaxDamage, false);

            string oneHand = DamageLine(
                SectionStringIds.OneHandDamage, StatMinDamage, StatMaxDamage, false);

            string marker = ItemTooltipColor.Marker + "0";

            return (twoHand == null ? string.Empty : marker + twoHand)
                   + (oneHand == null ? string.Empty : marker + oneHand);
        }

        // 0x4858f1: which pair to read comes from IsTwoHanded, i.e. the items.txt `2handed` column.
        private string SingleDamageLine()
        {
            bool twoHanded = _items.GetInt(_item.ClassId, "2handed") != 0;

            return DamageLine(
                twoHanded ? SectionStringIds.TwoHandDamage : SectionStringIds.OneHandDamage,
                twoHanded ? StatSecondaryMinDamage : StatMinDamage,
                twoHanded ? StatSecondaryMaxDamage : StatMaxDamage,
                true);
        }

        /// <summary>
        /// One line's numbers, with no formatting. <see cref="DamageLine"/> writes these and
        /// <see cref="TooltipEngine.Damage"/> returns them, so the string and the API cannot
        /// disagree about a value.
        /// </summary>
        private ItemDamageRange DamageValues(
            ItemDamageKind kind, int minStat, int maxStat, bool clampMax, bool throwShape)
        {
            int min = Stat(minStat);
            int max = Stat(maxStat);

            // 0x485931, single-line path only.
            if (clampMax && max <= min + 1)
            {
                max = min + 1;
            }

            bool modified = throwShape
                ? ThrowDamageIsModified(minStat, maxStat)
                : DamageIsModified(minStat, maxStat);

            return new ItemDamageRange(kind, min, max, modified);
        }

        private string DamageLine(
            int labelId, int minStat, int maxStat, bool clampMax, bool throwShape = false)
        {
            ItemDamageRange values = DamageValues(
                KindOf(labelId), minStat, maxStat, clampMax, throwShape);

            int min = values.Min;
            int max = values.Max;

            // The throw block does NOT share the 1H/2H emission shape. 0x485a97 puts an explicit
            // colour 0 on the label, and 0x485afd / 0x485b7c mark BOTH numbers rather than relying
            // on the marker staying in force from the min. Its flag is also pre-seeded at
            // 0x485a14-0x485a54 from STATLIST_GetStatBonusFromLists on stats 18, 17, 159 and 160,
            // where the 1H/2H flag is zeroed at 0x485662 and never gets those terms.
            if (throwShape)
            {
                string throwMarker = ItemTooltipColor.Marker + (values.Modified ? "3" : "0");

                return ItemTooltipColor.Marker + "0" + Str(labelId) + Space
                       + throwMarker + min + Space + Str(SectionStringIds.To) + Space
                       + throwMarker + max + Terminator;
            }

            // 0x4856f5 / 0x485818 / 0x485984 prepend colour 3 to the number buffer before the MIN is
            // appended, and only then — STRING_CopyCharToWCharWithSetMaxSize overwrites that buffer
            // before the max, so the max never carries a marker of its own. But a colour code stays in
            // force until the next one, so the visible result is the LABEL in the section colour and
            // the whole numeric run — min, "to" and max — in colour 3.
            string marker = values.Modified ? ItemTooltipColor.Marker + "3" : string.Empty;

            return Str(labelId) + Space + marker + min + Space + Str(SectionStringIds.To)
                   + Space + max + Terminator;
        }

        /// <summary>
        /// INV_CalcWeaponDamageRange's pModified out-param. Set when the BASE stat is below the merged
        /// one on either end (0x485300 compares SERVER_GetUnitStat against GetStatUnsignedValue), or
        /// when either by-time damage stat contributes anything (0x485372 / 0x4853eb).
        /// </summary>
        private bool DamageIsModified(int minStat, int maxStat)
        {
            return BaseStat(minStat) < Stat(minStat)
                   || BaseStat(maxStat) < Stat(maxStat)
                   || Stat(StatDamageByTime) != 0
                   || Stat(StatDamagePercentByTime) != 0;
        }

        /// <summary>
        /// The throw line's flag is the 1H/2H one PLUS a pre-seed: 0x485a14-0x485a54 sets it when
        /// STATLIST_GetStatBonusFromLists returns non-zero for any of stats 18, 17, 159 or 160, and
        /// INV_CalcWeaponDamageRange only ever sets the flag afterwards (0x485305 / 0x485377 /
        /// 0x4853f0), never clears it.
        ///
        /// The 18/17 halves of that pre-seed are DEAD, in the engine as much as here, and are kept
        /// only because the binary evaluates them. Both rows carry op 13, and once an op-13 stat has
        /// landed on a non-zero target the engine refuses to store it in an ITEM's FullStats
        /// (0x626821 tests dwOwnerType == UNIT_ITEM; 0x626847 then skips the write at 0x626868), so
        /// STATLIST_GetStatBonusFromLists reads 0 − 0 for them on any weapon.
        ///
        /// The pre-seed is NOT load-bearing for a Throwing Knife whose +10% truncates to zero -
        /// the game leaves that line unmarked too. The 159/160 halves are TARGETS rather than op
        /// rows, and those do still fire.
        /// </summary>
        private bool ThrowDamageIsModified(int minStat, int maxStat)
        {
            return DamageIsModified(minStat, maxStat)
                   || Bonus(StatMinDamagePercent) != 0
                   || Bonus(StatMaxDamagePercent) != 0
                   || Bonus(minStat) != 0
                   || Bonus(maxStat) != 0;
        }

        private int BaseStat(int statId)
        {
            int value;
            return _baseStats.TryGetValue(ItemStatReader.PackStatKey(0, statId), out value)
                ? value
                : 0;
        }

        // STATLIST_GetStatBonusFromLists 0x625560 returns the merged value MINUS the base one.
        //
        // For an op-13 stat on an ITEM this is structurally 0 — the engine never stores those in
        // FullStats (0x626821 / 0x626847), which ItemStatOps.Resolve reproduces by dropping them
        // after the fold. Stats 16, 17, 18, 75 and 94 are the op-13 rows; 93 and the damage targets
        // are not, and those still report a real bonus.
        private int Bonus(int statId)
        {
            return Stat(statId) - BaseStat(statId);
        }

        // 0x485be0. total = merged stat 20 + CharStats.BlockFactor + Holy Shield, CAPPED AT 75
        // (0x485c65). The newline is part of the "%d%%\n" format, not locale 3998, and locale 11018
        // already ends with a space.
        private string BlockChance()
        {
            if (!_types.IsOfType(PrimaryType(), SecondaryType(), _types.Row("shld")))
            {
                return null;
            }

            int total = Stat(StatToBlock);

            if (_viewer != null && _viewer.IsPlayer && _data.CharStats != null
                && _viewer.ClassId >= 0 && _viewer.ClassId < _data.CharStats.RowCount)
            {
                total += _data.CharStats.GetInt(_viewer.ClassId, "BlockFactor");
                total += HolyShieldBlockBonus();
            }

            if (total > MaxBlockChance)
            {
                total = MaxBlockChance;
            }
            else if (total == 0)
            {
                return null;
            }

            // 0x485cd7 reads items.txt nBlock (+0x111) and colours the NUMBER buffer 3 when the
            // total beats it; 0x485d0e then prepends an explicit colour 0 to the LABEL buffer,
            // which is why the game emits two markers for this one section.
            string numberMarker = total > _items.GetInt(_item.ClassId, "block")
                ? ItemTooltipColor.Marker + "3"
                : string.Empty;

            return ItemTooltipColor.Marker + "0" + Str(SectionStringIds.BlockChance)
                   + numberMarker + total + Str(DescStringIds.Percent) + Terminator;
        }

        // ItemTypes `Class` restricts the item to one character class; the text is that class's
        // charstats StrClassOnly.
        private string ClassRestriction()
        {
            int row = PrimaryType();
            if (row < 0 || _data.ItemTypes == null)
            {
                return null;
            }

            string code = _data.ItemTypes.GetString(row, "Class");
            if (string.IsNullOrEmpty(code.Trim()))
            {
                return null;
            }

            int classId = _data.Skills.ClassIdForCode(code);
            if (classId < 0)
            {
                return null;
            }

            string text = _data.Classes.GetClassOnlyText(classId);
            return string.IsNullOrEmpty(text) ? null : text + Terminator;
        }

        // AppendQuanity 0x486100 for the quantity line, then INV_FormatItemStatCostText 0x486370 for
        // the spelldesc. Both write to the SAME buffer (var_1434 at 0x48e91c and 0x48e972) and every
        // spelldesc arm uses STRING_CopyWideString, so a spelldesc REPLACES the quantity line
        // outright rather than appending to it.
        //
        // (INV_FormatQuantityText 0x484db0 builds similar text into a buffer LoadItemDesc overwrites
        // at 0x48e9a5, so its output is dead in 1.14d.)
        private string QuantityAndSpellDescription()
        {
            return SpellDescription() ?? QuantityLine();
        }

        // 0x486160: a stackable item shows the line even at quantity 0, because the gate is
        // `stat 70 > 0 OR maxstack > 0`.
        /// <summary>
        /// The book tooltip's quantity. `AppendQuanity` is called at 0x48d07d with none of the
        /// identified / not-socketed gating the generic path applies at 0x48e8ef / 0x48e90d, so a
        /// tome shows its count whatever its flags.
        /// </summary>
        private string BookQuantity()
        {
            int quantity = Stat(StatQuantity);

            if (quantity <= 0 && _items.GetInt(_item.ClassId, "maxstack") <= 0)
            {
                return null;
            }

            return Str(SectionStringIds.QuantityLabel) + Space + quantity + Terminator;
        }

        /// <summary>
        /// 0x48d082 tests ShopMode for EXACTLY zero, so both usage lines vanish in any shop mode.
        /// </summary>
        // No shopMode parameter: the gate it modelled (0x48d082 suppresses both usage lines outside
        // a shop) is applied by ItemTooltipComposer.ComposeBook, which skips these sections
        // entirely when ShopMode is non-zero. Nothing ever passed a value.
        private string BookUsageLine(int stringId)
        {
            return Str(stringId) + Terminator;
        }

        private string QuantityLine()
        {
            // 0x48e8ef / 0x48e90d: AppendQuanity runs only for an IDENTIFIED, NOT-SOCKETED item.
            // The spelldesc that may replace its buffer (0x48e978) is reached either way.
            if (!_item.Has(ItemRecordFlags.Identified) || _item.Has(ItemRecordFlags.Socketed))
            {
                return null;
            }

            int quantity = Stat(StatQuantity);

            if (quantity <= 0 && _items.GetInt(_item.ClassId, "maxstack") <= 0)
            {
                return null;
            }

            return Str(SectionStringIds.QuantityLabel) + Space + quantity + Terminator;
        }

        // 0x1506 at 0x4863b1 — the compiled id for a blank spelldescstr cell, which suppresses the
        // whole section.
        private const int NoSpellDescString = 5382;

        private string SpellDescription()
        {
            int mode = _items.GetInt(_item.ClassId, "spelldesc");

            // 0x48638b, and 0x4863a2 needs a player unit before any arm runs.
            if (mode == 0 || _viewer == null)
            {
                return null;
            }

            TxtFile file = FileFor(_item.ClassId);
            int row = RowFor(_item.ClassId);
            if (file == null || row < 0)
            {
                return null;
            }

            int stringId = TxtKeys.Id(file, row, "spelldescstr", _data.Strings);
            if (stringId == NoSpellDescString)
            {
                return null;
            }

            string template = Str(stringId);

            switch (mode)
            {
                case 1:
                    // 0x4863eb: the string alone.
                    return template + Terminator;

                case 2:
                {
                    // 0x48642f then the stat1 switch at 0x48644d scales it per class.
                    int value;
                    if (!TrySpellDescValue(file, row, out value))
                    {
                        return null;
                    }

                    return template + Space + PotionValueForClass(file, row, value) + Terminator;
                }

                case 3:
                {
                    // 0x4864d0: the same value with NO class scaling.
                    int value;
                    if (!TrySpellDescValue(file, row, out value))
                    {
                        return null;
                    }

                    return template + Space + value + Terminator;
                }

                default:
                    // Mode 4 (0x48651e) feeds the value through UNICODE_FormatWideString, so the
                    // locale string is a template rather than a prefix. No shipped row uses mode 3 or
                    // 4 — only 1 and 2 appear in misc.txt — so the substitution style is unverified
                    // and this returns nothing rather than guessing at it. Anything above 4 falls
                    // through the switch at 0x4863e4 and writes nothing either.
                    return null;
            }
        }

        /// <summary>
        /// The value behind a spelldesc is `calc1` (+164), NOT `spelldesccalc` (+184) — 0x48642f and
        /// 0x4864d0 both read offset 164. It is a calc EXPRESSION in general, evaluated by
        /// ITEMS_SearchItemCodeTable through SKILLS_CompileSkillFormula, but every shipped row holds
        /// a plain integer. A non-literal is refused rather than approximated.
        /// </summary>
        private static bool TrySpellDescValue(TxtFile file, int row, out int value)
        {
            value = 0;

            if (!file.HasColumn("calc1"))
            {
                return false;
            }

            string cell = file.GetString(row, "calc1").Trim();
            return cell.Length != 0
                   && int.TryParse(
                       cell, System.Globalization.NumberStyles.Integer,
                       System.Globalization.CultureInfo.InvariantCulture, out value);
        }

        // ITEMS_ModifyPotionValueByDifficulty 0x62a5d0 and
        // ITEMS_ModifyPotionSellValueByDifficulty 0x62a620. The names are misleading: neither reads
        // the difficulty. Both are per-CLASS multipliers picked by stat1 — the healing family
        // (hitpoints 6, hpregen 74) takes the first, the mana family (mana 8, manarecovery 26) the
        // second. The result is the familiar rule that a Barbarian gets double from healing potions
        // and single from mana, while the casters get the reverse.
        private int PotionValueForClass(TxtFile file, int row, int value)
        {
            int stat = _data.ItemStatCost.StatIdForName(file.GetString(row, "stat1").Trim());

            bool healing = stat == StatHitPoints || stat == StatHpRegen;
            bool mana = stat == StatMana || stat == StatManaRecovery;

            if (!healing && !mana)
            {
                return value;   // 0x48644d default: no scaling at all
            }

            // A non-player viewer takes the jz at 0x62a5dd / 0x62a62d: doubled for the healing
            // family, unchanged for the mana family.
            if (!_viewer.IsPlayer || _viewer.ClassId < 0 || _viewer.ClassId > 6)
            {
                return healing ? value * 2 : value;
            }

            int index = healing
                ? HealingPotionClassIndex[_viewer.ClassId]
                : ManaPotionClassIndex[_viewer.ClassId];

            switch (index)
            {
                case 0: return (value >> 1) + value;   // 1.5x
                case 1: return value * 2;
                default: return value;
            }
        }

        private const int StatHitPoints = 6;
        private const int StatMana = 8;
        private const int StatManaRecovery = 26;
        private const int StatHpRegen = 74;

        // byte_62A618 and byte_62A668, indexed by class id. The stored byte selects the jump target:
        // 0 -> 1.5x, 1 -> 2x, 2 -> unchanged.
        private static readonly byte[] HealingPotionClassIndex = { 0, 2, 2, 0, 1, 2, 0 };
        private static readonly byte[] ManaPotionClassIndex = { 0, 1, 1, 0, 2, 1, 0 };

        // 0x485dd2 / 0x485dda: the skill must exist on the viewer AND unit state 101 must be up.
        private bool HolyShieldUp()
        {
            return _viewer != null
                   && _viewer.ActiveStates.Contains(SkillDamage.HolyShieldState)
                   && _viewer.SkillLevel(SkillDamage.HolyShieldSkillId) > 0;
        }

        private void HolyShieldDamage(out int min, out int max)
        {
            min = 0;
            max = 0;

            if (!HolyShieldUp())
            {
                return;
            }

            int shiftedMin;
            int shiftedMax;
            if (!_skillDamage.TryCalcDamage(
                    SkillDamage.HolyShieldSkillId, _viewer.SkillLevel(SkillDamage.HolyShieldSkillId),
                    out shiftedMin, out shiftedMax))
            {
                return;
            }

            // 0x485e04 / 0x485e10 take the >> 8 of whatever the calc returned.
            min = shiftedMin >> 8;
            max = shiftedMax >> 8;
        }

        // 0x485c58.
        private int HolyShieldBlockBonus()
        {
            return HolyShieldUp()
                ? _skillDamage.ParamWithDiminishing(
                    SkillDamage.HolyShieldSkillId, _viewer.SkillLevel(SkillDamage.HolyShieldSkillId))
                : 0;
        }

        /// <summary>
        /// SKILLDESC_BuildStatBuffDesc 0x4e60dc returns to SKILLDESC_BuildChargeSkillDesc 0x4e5e90
        /// BEFORE it builds anything, so for an elixir this text REPLACES the whole modifiers block
        /// rather than joining it.
        ///
        /// The gate is `ITEM_GetItemData_wType(item) == 11` — an exact match on the PRIMARY type row,
        /// not an equivalence walk, so a type merely descended from `elix` would not qualify.
        /// </summary>
        private string ElixirDescription()
        {
            if (PrimaryType() != _types.Row("elix"))
            {
                return null;
            }

            var text = new StringBuilder();

            foreach (ElixirAttribute entry in ElixirTable)
            {
                // 0x4e5f15: the item's fileIndex picks the attribute. The six entries are distinct,
                // so at most one line is ever produced.
                if (entry.FileIndex != _item.FileIndex)
                {
                    continue;
                }

                int value = Stat(StatValue);

                // 0x4e5f41 / 0x4e5f4e / 0x4e5f5b: stat ids 6..11 are the 8-bit fixed-point ones
                // (life and mana), tested as three disjoint pairs.
                if (entry.FileIndex >= 6 && entry.FileIndex <= 11)
                {
                    value >>= 8;
                }

                // 0x4e5f7d: a zero writes nothing at all.
                if (value == 0)
                {
                    continue;
                }

                string name = Str(value > 0 ? entry.PositiveString : entry.NegativeString);

                // 0x4e5fe5: only a positive value gets locale 4002 in front of the digits.
                string amount = value > 0
                    ? Str(SectionStringIds.ElixirPlus) + value
                    : value.ToString(System.Globalization.CultureInfo.InvariantCulture);

                text.Append(name).Append(Space).Append(amount).Append(Terminator);
            }

            return text.Length == 0 ? null : text.ToString();
        }

        private struct ElixirAttribute
        {
            public int FileIndex;
            public int PositiveString;
            public int NegativeString;

            public ElixirAttribute(int fileIndex, int positive, int negative)
            {
                FileIndex = fileIndex;
                PositiveString = positive;
                NegativeString = negative;
            }
        }

        // unk_72D6C0, six 16-byte entries counted by dword_72D720. The positive and negative string
        // ids are identical in every shipped entry, so the sign only chooses whether locale 4002 is
        // prefixed — never a different word.
        private static readonly ElixirAttribute[] ElixirTable =
        {
            new ElixirAttribute(0, 3498, 3498),   // strength
            new ElixirAttribute(1, 3500, 3500),   // energy
            new ElixirAttribute(2, 3499, 3499),   // dexterity
            new ElixirAttribute(3, 3501, 3501),   // vitality
            new ElixirAttribute(9, 3502, 3502),   // maxmana
            new ElixirAttribute(7, 3503, 3503),   // maxhp
        };

        // 0x48e943, the else of CheckItemFlag(item, 0x10) at 0x48e8ef. Mutually exclusive with the
        // Modifiers block, which is the identified arm of the same branch.
        private string Unidentified()
        {
            if (_item.Has(ItemRecordFlags.Identified))
            {
                return null;
            }

            return Str(SectionStringIds.Unidentified) + Terminator;
        }

        // 0x48e5f0: locale 20438 for item type 13, "charm".
        private string CharmDescription()
        {
            if (!_types.IsOfType(PrimaryType(), SecondaryType(), _types.Row("char")))
            {
                return null;
            }

            string text = Str(SectionStringIds.CharmDescription);
            return string.IsNullOrEmpty(text) ? null : text + Terminator;
        }

        // INV_FormatRunewordName 0x486670, gated by ITEM_GetItemsTxt_bHasInv at 0x48e5a6. It never
        // looks up a runeword name and never tests the runeword FLAG — any item holding runes gets
        // their letters, which is why a plain socketed sword shows 'RalOrt' too. Only rows that pass
        // IsOfType(rune) contribute, so gems are skipped.
        private string RuneLetters()
        {
            if (!HasInventory() || _sockets.Count == 0)
            {
                return null;
            }

            var letters = new StringBuilder();

            foreach (KeyValuePair<int, uint> socket in _sockets)
            {
                int classId = (int)socket.Value;

                if (!_types.IsOfType(
                        _types.Row(_items.PrimaryTypeCode(classId)),
                        _types.Row(_items.SecondaryTypeCode(classId)),
                        _types.Row("rune")))
                {
                    continue;
                }

                string letter = GemLetter(classId);
                if (!string.IsNullOrEmpty(letter))
                {
                    letters.Append(letter);
                }
            }

            // The opening string, the apostrophe and the newline are all inside the "wrote at least
            // one letter" branch at 0x48673b, so a rune-free socketed item writes nothing at all.
            if (letters.Length == 0)
            {
                return null;
            }

            return Str(SectionStringIds.RunewordOpen) + letters + "'" + Terminator;
        }

        // ITEM_GetItemsTxt_bHasInv 0x629900 reads the items.txt "hasinv" column.
        private bool HasInventory()
        {
            return _items.GetInt(_item.ClassId, "hasinv") != 0;
        }

        // 0x4861d0, gated by IsOfType(item, weap) at 0x48e6f3.
        private string AttackSpeed()
        {
            if (!_types.IsOfType(PrimaryType(), SecondaryType(), _types.Row("weap")))
            {
                return null;
            }

            int speed;
            if (!_attackSpeed.TryCalculate(_item, _clientPlayer, _stats, out speed))
            {
                return null;
            }

            // word_721E88 holds 4088..4093 at stride 6.
            int speedWord = SectionStringIds.FirstSpeedWord + SpeedBucket(speed);

            var text = new StringBuilder();

            // When no weapon-class row matches, the class prefix and BOTH separators are skipped and
            // only the speed word is written (0x4862bb).
            string weaponClass = WeaponClassName();
            if (weaponClass != null)
            {
                text.Append(weaponClass).Append(Space)
                    .Append(Str(SectionStringIds.Dash)).Append(Space);
            }

            text.Append(Str(speedWord));

            // 0x486224 / 0x4862ff: a faster-attack-rate BONUS colours the speed word, and the
            // prepend lands on the word only — after the class prefix was already appended.
            //
            // STATLIST_GetStatBonusFromLists 0x625560 is merged MINUS base, not the merged total,
            // so an item whose whole attack rate came from its own base array would not be
            // coloured. No shipped weapon carries a base stat 93, which is why the difference is
            // invisible against the corpus — but the predicate is the bonus, not the value.
            if (Bonus(StatFasterAttackRate) != 0)
            {
                text.Insert(text.Length - Str(speedWord).Length, ItemTooltipColor.Marker + "3");
            }

            return text.Append(Terminator).ToString();
        }

        // INV_FormatSocketFillerDesc 0x4865d0 -> SKILLDESC_BuildMagicAffixDesc 0x4e6850. What a LOOSE
        // gem or rune will do once socketed. These stats are on no statlist anywhere: the game
        // synthesises them onto a temporary list tagged 0x40, renders, and frees it (0x4e6811).
        //
        // The four blocks are (label, destination slot) = (11074, 2), (11073, 1), (11076, 1),
        // (11075, 0). Slot 1 really is read twice and slot 3 never exists — reproduce it.
        private string SocketFillerDescription()
        {
            if (!_types.IsOfType(PrimaryType(), SecondaryType(), _types.Row("sock")))
            {
                return null;
            }

            bool gem = _types.IsOfType(PrimaryType(), SecondaryType(), _types.Row("gem"));
            bool rune = !gem && _types.IsOfType(PrimaryType(), SecondaryType(), _types.Row("rune"));

            int row = _gemTable.RowForFillerClassId(_item.ClassId);

            // SKILLDESC_BuildMagicAffixDesc empties the buffer at 0x4e68bc and returns at 0x4e6a7a
            // for a `sock` item that is neither gem nor rune — a JEWEL. The 11080 tail that
            // INV_FormatSocketFillerDesc appends at 0x48661f is UNCONDITIONAL, so the jewel still
            // gets that one line.
            if ((!gem && !rune) || row < 0)
            {
                return Str(SectionStringIds.SocketFillerClose) + Terminator;
            }

            int propMode = gem ? PropertyApplier.PropModeGem : PropertyApplier.PropModeRune;

            var text = new StringBuilder();

            foreach (KeyValuePair<int, int> block in SocketFillerBlocks)
            {
                text.Append(Terminator);
                text.Append(SocketFillerBlock(row, block.Value, propMode, Str(block.Key) + Space));

                // 0x4e681c-0x4e6836: after each block SKILLDESC_FormatMagicSuffixDesc strips ONE
                // trailing newline from the whole buffer, which is what keeps the blocks from
                // ending up separated by blank lines.
                if (text.Length > 0 && text[text.Length - 1] == '\n')
                {
                    text.Length -= 1;
                }
            }

            return text.Append(Terminator).Append(Terminator)
                       .Append(Str(SectionStringIds.SocketFillerClose)).Append(Terminator)
                       .ToString();
        }

        // unk order at 0x4e693d / 0x4e699e / 0x4e69ff / 0x4e6a60.
        private static readonly KeyValuePair<int, int>[] SocketFillerBlocks =
        {
            new KeyValuePair<int, int>(11074, 2),
            new KeyValuePair<int, int>(11073, 1),
            new KeyValuePair<int, int>(11076, 1),
            new KeyValuePair<int, int>(11075, 0),
        };

        private string SocketFillerBlock(int gemRow, int slot, int propMode, string label)
        {
            var stats = new SortedDictionary<int, int>();

            foreach (ItemProperty property in _gemTable.Properties(gemRow, slot))
            {
                // 0x66004f: the walk stops at the first entry with no property, it does not skip it.
                if (property.PropertyId < 0)
                {
                    break;
                }

                _propertyApplier.Apply(propMode, _item, property, stats);
            }

            if (stats.Count == 0)
            {
                return string.Empty;
            }

            // The generator must see the synthesised stats through IStatValueSource too: the paired
            // damage lines are collected from the unit's statlist (0x4e49c0), not from the packed set.
            var values = new SynthesisedStatValues(stats, _item, _viewer, _items, _types);

            var lines = new List<ItemDescriptionLine>();
            foreach (ItemDescriptionLine line in _data.CreateGenerator(values).Describe(stats))
            {
                if (!string.IsNullOrEmpty(line.Text))
                {
                    lines.Add(line);
                }
            }

            if (lines.Count == 0)
            {
                return string.Empty;
            }

            // Gems and runes do NOT join the same way. SKILLDESC_BuildMagicAffixDesc 0x4e6850 sends
            // gems to 0x4e67d0, which pushes 0 at 0x4e67f3, and runes to 0x4e6720, which pushes 1 at
            // 0x4e6755 — the same slot, reaching BuildStatBuffDesc as a8 (ebp+0x1C). a8 == 1
            // terminates every line with 3998; a8 == 0 puts 3852 + 3995 (", ") before each line
            // after the first and terminates nothing, so the whole block is ONE line. Only visible
            // on a filler with two independent stats in a slot, i.e. the five Skulls.
            bool inlineMode = propMode != PropertyApplier.PropModeGem;

            return AppendStatBuffText(
                _data.CreateGenerator(values).Join(lines, inlineMode), label);
        }

        /// <summary>
        /// SKILLDESC_AppendStatBuffText 0x4e6410. It does NOT simply prepend the label. It scans back
        /// from the second-to-last character for a newline; finding none it prefixes the label and
        /// stops, and finding one it splices the label in before the FINAL line, strips that line's
        /// trailing newline and closes with locale 3852.
        /// </summary>
        private string AppendStatBuffText(string description, string label)
        {
            if (description.Length == 0)
            {
                return string.Empty;
            }

            // 0x4e6470: the scan starts at len - 2, so a description that is one line plus its
            // terminator never finds a split point.
            int at = description.Length - 2;
            while (at > 0 && description[at] != '\n')
            {
                --at;
            }

            // 0x4e6496 and 0x4e64b9 converge: label first, then the description untouched, and no 3852.
            if (at <= 0)
            {
                return label + description;
            }

            string head = description.Substring(0, at);
            string tail = description.Substring(at + 1);

            // 0x4e652a strips one trailing newline from the final line before 3852 goes on.
            if (tail.EndsWith("\n", StringComparison.Ordinal))
            {
                tail = tail.Substring(0, tail.Length - 1);
            }

            return head + Terminator + label + tail
                   + Str(SectionStringIds.SocketFillerBlockClose) + Terminator;
        }

        private string GemLetter(int classId)
        {
            return _gemTable.Letter(_gemTable.RowForRuneClassId(classId));
        }

        private string WeaponClassName()
        {
            int type = PrimaryType();

            foreach (KeyValuePair<string, int> entry in WeaponClassWords)
            {
                if (_types.IsOfType(type, SecondaryType(), _types.Row(entry.Key)))
                {
                    return Str(entry.Value);
                }
            }

            return null;
        }

        // unk_721EB0, scanned in order; first match wins. Six bytes per entry: an itemtypes ROW at
        // +0 and a locale id at +4, terminated by hitting dword_721F0A. Rows resolved by code here:
        // 26 staf, 28 axe, 30 swor, 32 knif, 38 tpot, 44 jave, 33 spea, 27 bow, 34 pole, 35 xbow,
        // 67 h2h, 88 h2h2, 68 orb, 25 wand, 57 blun.
        private static readonly KeyValuePair<string, int>[] WeaponClassWords =
        {
            new KeyValuePair<string, int>("staf", 4085),
            new KeyValuePair<string, int>("axe", 4078),
            new KeyValuePair<string, int>("swor", 4079),
            new KeyValuePair<string, int>("knif", 4080),
            new KeyValuePair<string, int>("tpot", 4081),
            new KeyValuePair<string, int>("jave", 4082),
            new KeyValuePair<string, int>("spea", 4083),
            new KeyValuePair<string, int>("bow", 4084),
            new KeyValuePair<string, int>("pole", 4086),
            new KeyValuePair<string, int>("xbow", 4087),
            new KeyValuePair<string, int>("h2h", 21258),
            new KeyValuePair<string, int>("h2h2", 21258),
            new KeyValuePair<string, int>("orb", 4085),
            new KeyValuePair<string, int>("wand", 4085),
            new KeyValuePair<string, int>("blun", 4077),
        };

        // dword_721F10, indexed by 5*(speed-10) + a per-class offset. Buckets are 1..5.
        private static readonly byte[] SpeedBuckets =
        {
            1,1,1,1,1, 1,1,1,1,1, 1,1,1,1,1, 1,1,2,1,1, 2,1,2,2,1,
            2,1,2,2,2, 2,2,3,2,2, 3,2,3,3,2, 3,2,3,3,3, 3,2,4,3,3,
            4,3,4,4,3, 4,3,4,4,4, 4,3,5,4,4, 5,4,5,5,4, 5,4,5,5,5,
            5,4,5,5,5, 5,5,5,5,5, 5,5,5,5,5,
        };

        // dword_722078, indexed by classId*2 + (bow or crossbow ? 1 : 0).
        private static readonly byte[] ClassSpeedOffset = { 0, 2, 1, 4, 1, 4, 0, 3, 0, 3, 1, 4, 0, 3 };

        // 0x48622f / 0x48623d bracket the table: 28 and over is bucket 5 outright, under 10 is
        // bucket 1, and only 10..27 index dword_721F10.
        private int SpeedBucket(int speed)
        {
            if (speed >= 28)
            {
                return 5;
            }

            if (speed < 10)
            {
                return 1;
            }

            int classId = ViewerClassId();
            int offset = classId < 0
                ? NoViewerSpeedOffset
                : ClassSpeedOffset[(classId * 2) + (RangedWeapon() ? 1 : 0)];

            int index = (5 * (speed - 10)) + offset;

            // dword_722078 sits immediately past dword_721F10's 90 dwords, so the one index the
            // table cannot hold — offset 5 with speed 27 — reads the class-offset table's first
            // entry instead (0x486283). That is a 0, and word_721E88[0] is locale 4088.
            return index < SpeedBuckets.Length
                ? SpeedBuckets[index]
                : ClassSpeedOffset[index - SpeedBuckets.Length];
        }

        // v5 at 0x48626b: crossbow (35) OR bow (27), via the full two-type test.
        private bool RangedWeapon()
        {
            return _types.IsOfType(PrimaryType(), SecondaryType(), _types.Row("xbow"))
                   || _types.IsOfType(PrimaryType(), SecondaryType(), _types.Row("bow"));
        }

        // dword_722078 is indexed by 2*classId, and with no player unit the class id is -1
        // (0x486274). Index -2 and -1 read the last two dwords of dword_721F10, which are both 5, so
        // a viewer-less tooltip behaves as though the offset were 5.
        private const int NoViewerSpeedOffset = 5;

        private int ViewerClassId()
        {
            int classId = _clientPlayer != null && _clientPlayer.IsPlayer ? _clientPlayer.ClassId : -1;
            return classId >= 0 && classId <= 6 ? classId : -1;
        }

        private TxtFile FileFor(int classId)
        {
            TxtFile file;
            return _items.TryResolve(classId, out file, out _) ? file : null;
        }

        private int RowFor(int classId)
        {
            int row;
            return _items.TryResolve(classId, out _, out row) ? row : -1;
        }

        private bool IsThrowable()
        {
            int row = PrimaryType();
            if (row < 0 || _data.ItemTypes == null)
            {
                return false;
            }

            return _data.ItemTypes.GetInt(row, "Throwable") != 0;
        }

        private int PrimaryType()
        {
            return _types.Row(_items.PrimaryTypeCode(_item.ClassId));
        }

        private int SecondaryType()
        {
            return _types.Row(_items.SecondaryTypeCode(_item.ClassId));
        }
    }
}
