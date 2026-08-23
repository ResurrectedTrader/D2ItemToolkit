using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace D2ItemToolkit
{
    /// <summary>
    /// A captured <c>D2UnitStrc</c>, as the engine reads it. An item and a player are the same
    /// struct in the game, so both are an <see cref="IUnit"/>; a socket filler is another one
    /// nested in <see cref="Items"/>, and its POSITION is the socket index.
    ///
    /// This is the contract, not a container: every public entry point takes it, so a consumer
    /// that already holds unit state in its own shape can implement this over that shape instead
    /// of copying into <see cref="Unit"/>. <see cref="Unit"/> is simply the implementation that
    /// deserialises from the producer's JSON.
    ///
    /// It carries no classification. The engine derives base / item-mod / set-tier from
    /// <see cref="IUnitStatList.Flags"/> and <see cref="IUnitStatList.StateNo"/>, and a `source`
    /// member here would invite filtering on something the engine does not filter on.
    /// </summary>
    public interface IUnit
    {
        /// <summary>4 is UNIT_ITEM; 0 is a player.</summary>
        int UnitType { get; }

        int ClassId { get; }

        /// <summary>
        /// items.txt szCode. Optional — the engine resolves everything from
        /// <see cref="ClassId"/>, so this is only here so a consumer can validate its own table
        /// ordering rather than trust it.
        /// </summary>
        string Code { get; }

        /// <summary>dwQualityNo: 1 inferior … 9 tempered.</summary>
        int Quality { get; }

        ItemRecordFlags ItemFlags { get; }

        /// <summary>
        /// dwFileIndex, overloaded by quality: a lowqualityitems row, a UniqueItems row (or -1), a
        /// SetItems row, a monstats row for a body part, or a character class for an ear.
        /// </summary>
        int FileIndex { get; }

        /// <summary>
        /// dwItemLevel (+0x2C). **-1 means the capture did not record it**, which is not the same as
        /// 0 — the game's own floor for a spawned item is 1.
        ///
        /// Nothing the tooltip WRITERS do needs this; it is here for the property handlers that
        /// derive a value from it and otherwise have to report themselves instead: the skill level
        /// behind funcs 11 and 19 when a property's max is non-positive (0x65f4de, 0x65f514,
        /// 0x65f70a, 0x65f75b), and func 14's socket cap, which picks MaxSock1/MaxSock25/MaxSock40
        /// by level (0x62bc81, 0x62bc8c).
        /// </summary>
        int ItemLevel { get; }

        int RarePrefix { get; }
        int RareSuffix { get; }
        int AutoAffix { get; }

        /// <summary>wItemFormat (+0x30). 0 is a classic item.</summary>
        int Format { get; }

        /// <summary>
        /// Three slots, 1-based indices into the CONCATENATED
        /// <c>[magicsuffix][magicprefix][automagic]</c> arrays — so an index past the suffix rows
        /// lands in the prefix table. On a runeword <c>MagicPrefix[0]</c> is not an affix index at
        /// all but a locale string id taken from runes.txt (0x639c63).
        /// </summary>
        IReadOnlyList<int> MagicPrefix { get; }

        IReadOnlyList<int> MagicSuffix { get; }

        int EarLevel { get; }
        string PlayerName { get; }

        /// <summary>
        /// bInvGfxIdx — which of the random inventory graphics this instance rolled, 0-based. Only
        /// meaningful for item types with a non-zero itemtypes.txt VarInvGfx (rings, amulets,
        /// jewels, charms), where the sprite is `code` plus the 1-based index: rin1..rin5.
        /// </summary>
        int GfxIndex { get; }

        /// <summary>
        /// dwFlagEx, on a viewer. UNITFLAGEX_ISEXPANSION (0x2000000) is the only bit the engine
        /// reads (0x62b877). An implementation that cannot supply it should return
        /// <see cref="Unit.UnitFlagExpansion"/> rather than 0 — an expansion character is the
        /// normal case, and 0 silently hides a classic unique's level requirement.
        /// </summary>
        uint FlagsEx { get; }

        /// <summary>Both statlist chains, flattened.</summary>
        IReadOnlyList<IUnitStatList> StatsLists { get; }

        /// <summary>
        /// A WEARER's already-merged stat values — what GetStat reads off FullStats, so they carry
        /// the gear contributions the raw chain does not. Empty on an item, and empty on a viewer
        /// whose capture did not supply them.
        ///
        /// These are the values requirement checks compare against. <see cref="StatsLists"/> on a
        /// wearer is the STRUCTURAL chain: it says which states are active, but its attribute
        /// values are pre-gear. So the two are not alternatives — ItemRecordReader.ReadViewer
        /// takes states from the chain and values from here, and these OVERWRITE rather than add,
        /// because summing an already-merged value into a chain total double-counts the kit.
        ///
        /// Values are the game's own int32. A producer may widen them to fit unsigned stats into
        /// JSON — experience at level 99 is ~3.52 billion, past int32 but inside uint32 — and the
        /// reader narrows them back unchecked, which restores the exact 32 bits the game holds.
        /// </summary>
        IReadOnlyList<IUnitStat> Stats { get; }

        /// <summary>
        /// The units this one contains — one relation, not two. On an ITEM those are its socket
        /// fillers, in socket-ordinal order, and position IS the socket index. On a WEARER they are
        /// the items it carries, which is what lets the library derive set state instead of asking
        /// a caller for masks. That is ONE field carrying TWO relations, and a reader that recurses
        /// — as a socket reader must — has to tell them apart: run a wearer through it and every
        /// carried item's stats fold into the wearer's own. <see cref="ItemStatReader.EnumerateOwnGroups"/>
        /// exists for exactly that.
        ///
        /// One level only. A filler is a socket of a carried item, so it sits one deeper than
        /// anything reached by scanning a wearer, and no set item is socketable — so a wearer scan
        /// never has to filter fillers out.
        ///
        /// The Horadric Cube is the case that does not fit: it is an item that holds items rather
        /// than fillers, and one held there is at the same depth a filler would be. Nothing models
        /// cube contents today; if that changes, this field means two relations and the scans below
        /// need a rule they currently do without.
        /// </summary>
        IReadOnlyList<IUnit> Items { get; }

        /// <summary>
        /// pItemData's item location: 0 ground, 1 equipped, 2 belt, 3 inventory, 4 store, 5 trade,
        /// 6 cube, 7 stash. -1 when the capture did not record one.
        ///
        /// This is NOT the unit's dwAnimMode, which is what the full-set block is really gated on
        /// (0x48d870). A capture cannot reach animMode, so <see cref="TooltipEngine.SetStateOf"/>
        /// substitutes `Location == 1` — deliberately, and the only option on the wire. Whether the
        /// two always agree is untraced.
        /// </summary>
        int Location { get; }

        /// <summary>
        /// The grid column, and the EQUIP LOCATION 1..12 when <see cref="Location"/> is equipped.
        /// One field carrying both is what the capture puts on the wire, not a modelling choice.
        ///
        /// 11 and 12 are the alternate weapon set (ITEMMODE_GetAlternateBodyLoc 0x55f240), which is
        /// why the value matters here at all: INVENTORY_PlaceItemInGrid stamps the grid type as
        /// `(bodyLoc >= 11) ? 4 : 3` (0x63b1e2), and a set piece on swap is therefore OWNED but
        /// lights no worn bit.
        /// </summary>
        int X { get; }


        /// <summary>
        /// A viewer's skills and their BONUSED levels. This is the one thing a stat capture cannot
        /// reach — SKILLS_GetSkillLevel reads it off the skill list (0x485df1 passes bBonus = 1).
        /// </summary>
        IReadOnlyList<IUnitSkill> Skills { get; }
    }

    /// <summary>
    /// One statlist node. <see cref="Flags"/> and <see cref="StateNo"/> are copied verbatim and
    /// already say which chain the node was on, which is why neither is interpreted here.
    /// </summary>
    public interface IUnitStatList
    {
        /// <summary>dwStateNo. 165-170 are the set tiers, 171 a runeword.</summary>
        int StateNo { get; }

        /// <summary>dwFlags. See <see cref="ItemStatListFlags"/>.</summary>
        uint Flags { get; }

        IReadOnlyList<IUnitStat> Stats { get; }
    }

    /// <summary>
    /// One stat. The value is RAW — pre nValShift, pre op resolution — which is what makes a
    /// capture stable across wearers. Shift and resolve at display time.
    /// </summary>
    public interface IUnitStat
    {
        int Id { get; }
        int Value { get; }

        /// <summary>Omitted from the document when zero.</summary>
        int Layer { get; }
    }

    public interface IUnitSkill
    {
        int Skill { get; }
        int Level { get; }
    }

    /// <summary>Convenience over <see cref="IUnit"/>, since C# 7.3 has no default interface members.</summary>
    public static class UnitExtensions
    {
        public static bool Has(this IUnit unit, ItemRecordFlags flag)
        {
            return unit != null && (unit.ItemFlags & flag) != 0;
        }
    }

    /// <summary>
    /// The <see cref="IUnit"/> the producer's JSON deserialises into, and the one to build when
    /// you are constructing a unit in code. Mutable on purpose — the engine only ever reads.
    /// </summary>
    public sealed class Unit : IUnit
    {
        /// <summary>UNITFLAGEX_ISEXPANSION.</summary>
        public const uint UnitFlagExpansion = 0x02000000;

        public const int MaxAffixSlots = 3;

        public int UnitType { get; set; }
        public int ClassId { get; set; }
        // Null-coercing setters: JsonConverter<T>.HandleNull is false for reference types, so an
        // explicit `"code": null` bypasses every converter and would land a null here. The engine
        // dereferences these unguarded, so a null becomes a NullReferenceException deep in a
        // writer rather than anything a caller can act on. Absent and null mean the same thing.
        private string _code = string.Empty;

        public string Code
        {
            get { return _code; }
            set { _code = value ?? string.Empty; }
        }
        public int Quality { get; set; }
        public ItemRecordFlags ItemFlags { get; set; }

        /// <summary>
        /// NARROWED, not parsed as an int. `dwFileIndex` is a DWORD, so the -1 that means "no row"
        /// serialises as 4294967295 — a value a producer really does emit and a plain
        /// <c>int</c> property rejects with a JsonException. The narrowing restores the exact 32
        /// bits, the way <see cref="ItemFlags"/> and <see cref="FlagsEx"/> already survive theirs.
        /// </summary>
        [JsonConverter(typeof(Int32NarrowingConverter))]
        public int FileIndex { get; set; }

        /// <summary>dwItemLevel. -1 when the capture did not record it — see <see cref="IUnit"/>.</summary>
        public int ItemLevel { get; set; }

        /// <summary>pItemData item location. -1 when absent — see <see cref="IUnit.Location"/>.</summary>
        public int Location { get; set; }

        /// <summary>Grid column, or the equip location 1..12 when equipped.</summary>
        public int X { get; set; }

        public int RarePrefix { get; set; }
        public int RareSuffix { get; set; }
        public int AutoAffix { get; set; }
        public int Format { get; set; }
        public int EarLevel { get; set; }
        private string _playerName = string.Empty;

        public string PlayerName
        {
            get { return _playerName; }
            set { _playerName = value ?? string.Empty; }
        }
        public int GfxIndex { get; set; }
        public uint FlagsEx { get; set; }

        // A list, not int[MaxAffixSlots]: the game struct really is wMagicPrefix[3], but nothing
        // downstream requires exactly three — ReadIdentity reads up to MaxAffixSlots and treats a
        // short list as zeros. The converter still pads to three so a consumer indexing this
        // directly cannot run off the end of a truncated document.
        private List<int> _magicPrefix = new List<int>(new int[MaxAffixSlots]);
        private List<int> _magicSuffix = new List<int>(new int[MaxAffixSlots]);

        [JsonConverter(typeof(AffixTripleConverter))]
        public List<int> MagicPrefix
        {
            get { return _magicPrefix; }
            set { _magicPrefix = value ?? new List<int>(new int[MaxAffixSlots]); }
        }

        [JsonConverter(typeof(AffixTripleConverter))]
        public List<int> MagicSuffix
        {
            get { return _magicSuffix; }
            set { _magicSuffix = value ?? new List<int>(new int[MaxAffixSlots]); }
        }

        private List<UnitStatList> _statsLists = new List<UnitStatList>();

        public List<UnitStatList> StatsLists
        {
            get { return _statsLists; }
            set { _statsLists = value ?? new List<UnitStatList>(); }
        }

        /// <summary>A wearer's merged values. See <see cref="IUnit.Stats"/>.</summary>
        private List<UnitStat> _stats = new List<UnitStat>();

        [JsonConverter(typeof(MergedStatsConverter))]
        public List<UnitStat> Stats
        {
            get { return _stats; }
            set { _stats = value ?? new List<UnitStat>(); }
        }

        private List<Unit> _items = new List<Unit>();
        private List<UnitSkill> _skills = new List<UnitSkill>();

        /// <summary>
        /// Null ELEMENTS are coerced to a default unit rather than dropped. `"items": [null]` is
        /// legal JSON that ten reader sites would otherwise dereference — every entry point threw
        /// where the TypeScript peer, whose reader maps a null to a default unit, rendered happily.
        /// Coercing rather than removing keeps POSITION meaning the socket index, which dropping
        /// would renumber.
        /// </summary>
        public List<Unit> Items
        {
            get { return _items; }
            set { _items = Denulled(value); }
        }

        private static List<Unit> Denulled(List<Unit> units)
        {
            if (units == null)
            {
                return new List<Unit>();
            }

            for (int at = 0; at < units.Count; ++at)
            {
                if (units[at] == null)
                {
                    units[at] = new Unit();
                }
            }

            return units;
        }

        public List<UnitSkill> Skills
        {
            get { return _skills; }
            set { _skills = value ?? new List<UnitSkill>(); }
        }

        public Unit()
        {
            // Absent is not zero for these five. A missing classId or fileIndex means "no such
            // row", a missing itemLevel means "not captured" where 0 would read as a real level
            // below the game's own floor of 1, and a missing flagsEx means expansion — 0 would read
            // as classic and hide a unique's level requirement.
            UnitType = -1;
            ClassId = -1;
            FileIndex = -1;
            ItemLevel = -1;
            Location = -1;
            FlagsEx = UnitFlagExpansion;
            Code = string.Empty;
            PlayerName = string.Empty;
        }

        // The concrete collections stay strongly typed so a caller can Add to them; IReadOnlyList
        // is covariant, so the interface view costs no copy.
        IReadOnlyList<int> IUnit.MagicPrefix { get { return MagicPrefix; } }

        IReadOnlyList<int> IUnit.MagicSuffix { get { return MagicSuffix; } }

        IReadOnlyList<IUnitStatList> IUnit.StatsLists { get { return StatsLists; } }

        IReadOnlyList<IUnitStat> IUnit.Stats { get { return Stats; } }

        IReadOnlyList<IUnit> IUnit.Items { get { return Items; } }

        IReadOnlyList<IUnitSkill> IUnit.Skills { get { return Skills; } }

        public bool Has(ItemRecordFlags flag)
        {
            return (ItemFlags & flag) != 0;
        }

        /// <summary>Parses one unit document in the producer's capture format.</summary>
        public static Unit FromJson(string json)
        {
            return UnitJson.Read(json);
        }

        public static Unit FromJson(JsonElement element)
        {
            return UnitJson.Read(element);
        }

        /// <summary>Re-emits any unit in the capture format, not just this one.</summary>
        public string ToJson()
        {
            return UnitJson.Write(this);
        }
    }

    public sealed class UnitStatList : IUnitStatList
    {
        public int StateNo { get; set; }
        public uint Flags { get; set; }

        private List<UnitStat> _stats = new List<UnitStat>();

        public List<UnitStat> Stats
        {
            get { return _stats; }
            set { _stats = value ?? new List<UnitStat>(); }
        }

        public UnitStatList()
        {
        }

        public UnitStatList(int stateNo, uint flags)
        {
            StateNo = stateNo;
            Flags = flags;
        }

        IReadOnlyList<IUnitStat> IUnitStatList.Stats { get { return Stats; } }

        public UnitStatList Add(int id, int value, int layer = 0)
        {
            Stats.Add(new UnitStat(id, value, layer));
            return this;
        }
    }

    public sealed class UnitStat : IUnitStat
    {
        public int Id { get; set; }

        public int Value { get; set; }

        /// <summary>Omitted from the document when zero, matching the producer.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public int Layer { get; set; }

        public UnitStat()
        {
        }

        public UnitStat(int id, int value, int layer = 0)
        {
            Id = id;
            Value = value;
            Layer = layer;
        }
    }

    public sealed class UnitSkill : IUnitSkill
    {
        public int Skill { get; set; }
        public int Level { get; set; }

        public UnitSkill()
        {
            // Skill id 0 is Attack, a REAL skill, so absent cannot read as 0.
            Skill = -1;
        }

        public UnitSkill(int skill, int level)
        {
            Skill = skill;
            Level = level;
        }
    }
}
