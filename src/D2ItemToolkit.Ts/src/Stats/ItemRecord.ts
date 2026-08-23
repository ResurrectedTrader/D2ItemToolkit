import { ItemStatListFlags, ItemStatReader, ItemStatView } from './ItemStatReader.js';
import { Int32 } from '../Types.js';
import { MaxAffixSlots, type Unit } from './Unit.js';

export enum ItemRecordFlags {
  None = 0,
  Identified = 0x00000010,
  Broken = 0x00000100,
  Socketed = 0x00000800,
  Named = 0x00008000,
  Personalized = 0x01000000,
  Ethereal = 0x00400000,
  Runeword = 0x04000000,
}

// What the item IS, as opposed to what stats it carries. These sit at the TOP of the unit
// document, beside its stat lists.
export class ItemIdentity {
  classId = -1;
  code = '';
  quality = 0;
  flags: number = ItemRecordFlags.None;
  fileIndex = -1;

  /**
   * dwItemLevel, or -1 when the capture did not record it. See {@link IUnit.itemLevel} for what
   * depends on it; every reader of this must handle -1, because most captures have none and the
   * property handlers that want it report themselves instead of guessing.
   */
  itemLevel = -1;
  rarePrefix = 0;
  rareSuffix = 0;
  autoAffix = 0;

  // wItemFormat (+0x30). 0 is a classic item; ITEM_CalcRequiredLevel hides a classic unique's
  // level requirement from a non-expansion viewer (0x62b877).
  format = 0;

  static readonly MaxAffixSlots = 3;
  readonly magicPrefix: number[] = new Array<number>(ItemIdentity.MaxAffixSlots).fill(0);
  readonly magicSuffix: number[] = new Array<number>(ItemIdentity.MaxAffixSlots).fill(0);
  earLevel = 0;
  playerName = '';

  /** bInvGfxIdx — see Unit.gfxIndex. */
  gfxIndex = 0;

  has(flag: ItemRecordFlags): boolean {
    return (this.flags & flag) !== 0;
  }
}

/**
 * A whole item unit — identity, its OWN stats, and whatever it contains. The unit document is
 * self-similar, so a socket filler has exactly this shape too, which is what
 * ITEM_CalcRequiredLevel's recursion at 0x62b901 walks.
 */
export class ItemUnit {
  readonly identity: ItemIdentity;
  readonly stats: Map<number, number>;
  readonly items: ItemUnit[] | null;

  constructor(
    identity: ItemIdentity,
    stats: Map<number, number> | null = null,
    items: ItemUnit[] | null = null,
  ) {
    this.identity = identity;
    this.stats = stats ?? new Map<number, number>();
    this.items = items;
  }
}

export class ItemViewer {
  unitType = -1;
  classId = -1;

  // Derived from the viewer's own stat lists, not stated: level is stat 12, strength 0,
  // dexterity 2 — exactly what STATLIST_UnitGetStatValue reads.
  level = 0;
  strength = 0;
  dexterity = 0;

  static readonly UnitFlagExpansion = 0x02000000;

  /**
   * dwFlagEx verbatim. UNITFLAGEX_ISEXPANSION (0x2000000) is the only bit the description
   * engine reads (0x62b877); an absent field defaults to having it, because an expansion
   * character is the normal case and a missing flag should not silently hide unique level
   * requirements.
   */
  flagsEx: number = ItemViewer.UnitFlagExpansion;

  get isExpansion(): boolean {
    return (this.flagsEx & ItemViewer.UnitFlagExpansion) !== 0;
  }

  /** The unit's own skills and their bonused levels, by skill id. */
  readonly skills = new Map<number, number>();

  /**
   * The viewer's merged stats, packed layer-major. The op 2-5 scaling reads the PLAYER, not
   * the item: SKILLDESC_CalcStatGroupValue 0x4e4c50 calls
   * GetStatUnsignedValue(GetPlayerUnit(), opBase, 0) at 0x4e4c93/0x4e4c99. `opBase` is 12
   * (level) on every shipped row, but it is a column, so the lookup has to be by stat id.
   */
  readonly stats = new Map<number, number>();

  /**
   * Layer 0 of the named stat, or 0 when absent. GetStatUnsignedValue 0x625483 returns 0 for
   * a null unit rather than halting, so a viewer-less tooltip scales by zero and still emits
   * the line — the zero filter at 0x4e628b tests the STORED value, ahead of the scaling call.
   */
  stat(statId: number): number {
    return this.stats.get(ItemStatReader.packStatKey(0, statId)) ?? 0;
  }

  skillLevel(skillId: number): number {
    return this.skills.get(skillId) ?? 0;
  }

  /**
   * A state is a stat list carrying its own dwStateNo, so this is read off the stat lists
   * rather than stated (0x485dda tests state 101 for Holy Shield).
   */
  readonly activeStates = new Set<number>();

  // LoadItemDesc gates Smite and Kick on dwClassId alone (0x48e75c / 0x48e7c7) without
  // checking dwUnitType, so a monster with class id 3 or 6 false-positives on a mercenary
  // tooltip. Consumers should require IsPlayer rather than reproduce that.
  get isPlayer(): boolean {
    return this.unitType === 0;
  }
}

const StatStrength = 0;
const StatDexterity = 2;
const StatLevel = 12;

export class ItemRecordReader {
  static readIdentity(record: Unit): ItemIdentity {
    const identity = new ItemIdentity();
    identity.classId = record.classId;
    identity.code = record.code;
    identity.quality = record.quality;
    identity.flags = record.itemFlags >>> 0;
    identity.fileIndex = record.fileIndex;
    identity.itemLevel = record.itemLevel;
    identity.rarePrefix = record.rarePrefix;
    identity.rareSuffix = record.rareSuffix;
    identity.autoAffix = record.autoAffix;
    identity.format = record.format;
    identity.earLevel = record.earLevel;
    identity.playerName = record.playerName;
    identity.gfxIndex = record.gfxIndex;

    for (let i = 0; i < MaxAffixSlots; ++i) {
      identity.magicPrefix[i] = record.magicPrefix[i] ?? 0;
      identity.magicSuffix[i] = record.magicSuffix[i] ?? 0;
    }

    return identity;
  }

  /**
   * The record's socket fillers as whole units, recursively. Array position is the socket
   * index; each filler's stats are its OWN lists only, which is what GetStatUnsignedValue
   * reads when ITEM_CalcRequiredLevel recurses into it (0x62b901).
   */
  static readSocketUnits(record: Unit): ItemUnit[] {
    const units: ItemUnit[] = [];

    for (const socket of record.items) {
      units.push(
        new ItemUnit(
          ItemRecordReader.readIdentity(socket),
          ItemStatReader.reconstructView(socket, ItemStatView.itemOnly()),
          ItemRecordReader.readSocketUnits(socket),
        ),
      );
    }

    return units;
  }

  /**
   * A player is a unit document of the same shape as an item, so its attributes are not
   * special fields — they are ordinary stats on its own stat lists, exactly as
   * STATLIST_UnitGetStatValue reads them. Whether Holy Shield is up falls out the same way: a
   * state is a stat list carrying its own dwStateNo (0x485dda tests state 101).
   */
  static readViewer(player: Unit): ItemViewer {
    const viewer = new ItemViewer();
    viewer.unitType = player.unitType;
    viewer.classId = player.classId;

    const stats = new Map<number, number>();
    for (const group of ItemStatReader.enumerateOwnGroups(player)) {
      // On pMyStats rather than pMyLastList, so it is not contributing.
      if ((group.flags & ItemStatListFlags.Set) !== 0) {
        continue;
      }

      viewer.activeStates.add(group.stateNo);

      for (const [key, value] of group.enumerateStats()) {
        const existing = stats.get(key);
        stats.set(key, existing === undefined ? value : Int32.of(existing + value));
      }
    }

    // The merged values land LAST and by assignment. A wearer's chain is structural: it says
    // which states are active, but its attribute values are pre-gear, because
    // STATLIST_CalcFullStatFromChildren does the folding and the capture cannot re-send every
    // equipped piece inside the player document. GetStat reads the folded result, and that is
    // what the requirement checks compare against.
    //
    // Assignment, not accumulation: these are already totals. Adding them to the chain sum would
    // count the kit twice. Absent, the chain values stand.
    for (const stat of player.stats) {
      stats.set(ItemStatReader.packStatKey(stat.layer ?? 0, stat.id), stat.value);
    }

    for (const [key, value] of stats) {
      viewer.stats.set(key, value);
    }

    viewer.level = viewerStat(stats, StatLevel);
    viewer.strength = viewerStat(stats, StatStrength);
    viewer.dexterity = viewerStat(stats, StatDexterity);

    // A skill LEVEL is the one thing a stat capture cannot reach: SKILLS_GetSkillLevel reads
    // pSkill->nSkillLevel off the SKILL list.
    for (const skill of player.skills) {
      viewer.skills.set(skill.skill, skill.level);
    }

    viewer.flagsEx = player.flagsEx;

    return viewer;
  }
}

function viewerStat(stats: ReadonlyMap<number, number>, statId: number): number {
  return stats.get(ItemStatReader.packStatKey(0, statId)) ?? 0;
}
