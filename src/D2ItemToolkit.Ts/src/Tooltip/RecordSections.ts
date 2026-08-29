import { AttackSpeedCalculator } from './AttackSpeedCalculator.js';
import { EquipRequirements } from './EquipRequirements.js';
import { GemTable } from '../Tables/GemTable.js';
import {
  type ItemDescriptionLine,
  ItemDescriptionGenerator,
} from '../Description/ItemDescription.js';
import { ItemDamageKind, type ItemDamageRange } from './ItemDamage.js';
import { ItemNameBuilder } from './ItemNameBuilder.js';
import {
  type ItemIdentity,
  ItemRecordFlags,
  type ItemUnit,
  type ItemViewer,
} from '../Stats/ItemRecord.js';
import { ItemStatReader, sortByKey } from '../Stats/ItemStatReader.js';
import type { ItemTable } from '../Tables/ItemTable.js';
import {
  type IItemTooltipSections,
  ItemTooltipColor,
  ItemTooltipContext,
  ItemTooltipSection,
} from './ItemTooltip.js';
import type { ItemTypeTree } from '../Tables/ItemTypeTree.js';
import { type MissileThrowDamage, MissileTable } from '../Tables/MissileTable.js';
import { type ItemProperty, PropertyApplier } from '../Stats/PropertyApplier.js';
import { RequiredLevelCalculator } from './RequiredLevelCalculator.js';
import { SkillDamage } from '../Tables/SkillDamage.js';
import { SynthesisedStatValues } from '../Stats/SynthesisedStatValues.js';
import type { TxtFile } from '../Data/TxtFile.js';
import { type D2DataFiles, TxtKeys } from '../Tables/TxtDataProviders.js';
import { DescStringIds, isNullOrEmpty } from '../Types.js';

// Locale ids the section writers emit.
export const SectionStringIds = {
  Socketed: 3453, // "Socketed"
  DurabilityLabel: 3457, // "Durability:"
  RequiredStrength: 3458, // "Required Strength:"
  RequiredDexterity: 3459, // "Required Dexterity:"
  ArmorClass: 3461, // "Defense:"
  Of: 3463, // "of"
  To: 3464, // "to"
  SmiteDamage: 3468, // "Smite Damage:"
  RequiredLevel: 3469, // "Required Level:"
  EtherealCannotBeRepaired: 22745,
  KickDamage: 21782,
  OneHandDamage: 3465, // "One-Hand Damage:"
  TwoHandDamage: 3466, // "Two-Hand Damage:"
  ThrowDamage: 3467, // "Throw Damage:"
  BlockChance: 11018, // "Chance to Block: " (trailing space)
  QuantityLabel: 3462, // "Quantity:"
  Dash: 3996, // "-"
  CharmDescription: 20438,
  Unidentified: 3455, // 0xD7F at 0x48e943
  ElixirPlus: 4002, // prefixed to a POSITIVE elixir value only
  RunewordOpen: 20506,

  // 0x48ec9a / 0x48ecc4: the two quest-usage lines, for `box ` and `bkd ` respectively.
  RightClickToOpen: 2204,
  RightClickToRead: 2205,

  // INV_ShowBookTooltip 0x48d08c pushes 0x89B and 0x48d0a8 pushes 0x89E.
  RightClickToUse: 2203,
  InsertScrolls: 2206,

  // INV_FormatSocketFillerDesc appends 11080 after the four blocks (0x48661f); each block ends
  // with 3852 (0x4e64f2).
  SocketFillerClose: 11080,
  SocketFillerBlockClose: 3852,

  // word_721E88 holds 4088..4093 at stride 6. Bucket 0 IS reachable: a viewer-less tooltip
  // takes the offset 5 that dword_722078[-2] supplies, and speed 27 then indexes one past
  // dword_721F10's 90 entries onto dword_722078[0] = 0 (0x486283).
  FirstSpeedWord: 4088,
} as const;

const StatSockets = 194;
const StatDurability = 72;
const StatMaxDurability = 73;
const StatMaxDurabilityPercent = 75;
const StatArmorClass = 31;
const StatQuestDifficulty = 356;
const StatIndestructible = 152;
const StatToBlock = 20;
const StatMinDamage = 21;
const StatMaxDamage = 22;
const StatSecondaryMinDamage = 23;
const StatSecondaryMaxDamage = 24;
// 18 is item_mindamage_percent and 17 item_maxdamage_percent — that way round, per
// D2StatList.h; they have been transposed in this file once before.
const StatMinDamagePercent = 18;
const StatMaxDamagePercent = 17;

const StatThrowMinDamage = 159;
const StatThrowMaxDamage = 160;

const StatQuantity = 70;
const StatValue = 71;
const StatFasterAttackRate = 93;
const StatDamageByTime = 272;
const StatDamagePercentByTime = 273;

const MaxBlockChance = 75;

const PaladinClass = 3;

// TXT_ItemTypes_GetClass returns the class index and the gate compares it against 3;
// itemtypes.txt carries the code rather than the index, and locale 10917+3 is
// "(Paladin Only)", which fixes 3 = Paladin.
const PaladinClassCode = 'pal';
const AssassinClass = 6;

const BarbarianClass = 4;

const StatHitPoints = 6;
const StatMana = 8;
const StatManaRecovery = 26;
const StatHpRegen = 74;

// 0x1506 at 0x4863b1 — the compiled id for a blank spelldescstr cell, which suppresses the
// whole section.
const NoSpellDescString = 5382;

// 0x48e9b0: eleven items.txt code dwords, plus IsOfType(item, 74 rune), force the NAME
// colour to 8. The codes are compared as four-byte little-endian dwords, so they are the
// `code` cell padded to four characters with spaces.
const RuneColorCodes: readonly string[] = [
  'ceh ',
  'bet ',
  'tes ',
  'fed ',
  'toa ',
  'dhn ',
  'bey ',
  'mbr ',
  'pk1 ',
  'pk2 ',
  'pk3 ',
];

const WirtsLegCode = 'leg ';

const HoradricCubeCode = 'box ';
const CairnStonesKeyCode = 'bkd ';

interface ElixirAttribute {
  fileIndex: number;
  positiveString: number;
  negativeString: number;
}

// unk_72D6C0, six 16-byte entries counted by dword_72D720. The positive and negative string
// ids are identical in every shipped entry, so the sign only chooses whether locale 4002 is
// prefixed — never a different word.
const ElixirTable: readonly ElixirAttribute[] = [
  { fileIndex: 0, positiveString: 3498, negativeString: 3498 }, // strength
  { fileIndex: 1, positiveString: 3500, negativeString: 3500 }, // energy
  { fileIndex: 2, positiveString: 3499, negativeString: 3499 }, // dexterity
  { fileIndex: 3, positiveString: 3501, negativeString: 3501 }, // vitality
  { fileIndex: 9, positiveString: 3502, negativeString: 3502 }, // maxmana
  { fileIndex: 7, positiveString: 3503, negativeString: 3503 }, // maxhp
];

// byte_62A618 and byte_62A668, indexed by class id. The stored byte selects the jump target:
// 0 -> 1.5x, 1 -> 2x, 2 -> unchanged.
const HealingPotionClassIndex: readonly number[] = [0, 2, 2, 0, 1, 2, 0];
const ManaPotionClassIndex: readonly number[] = [0, 1, 1, 0, 2, 1, 0];

// unk order at 0x4e693d / 0x4e699e / 0x4e69ff / 0x4e6a60.
const SocketFillerBlocks: readonly (readonly [number, number])[] = [
  [11074, 2],
  [11073, 1],
  [11076, 1],
  [11075, 0],
];

// unk_721EB0, scanned in order; first match wins. Six bytes per entry: an itemtypes ROW at
// +0 and a locale id at +4, terminated by hitting dword_721F0A. Rows resolved by code here:
// 26 staf, 28 axe, 30 swor, 32 knif, 38 tpot, 44 jave, 33 spea, 27 bow, 34 pole, 35 xbow,
// 67 h2h, 88 h2h2, 68 orb, 25 wand, 57 blun.
const WeaponClassWords: readonly (readonly [string, number])[] = [
  ['staf', 4085],
  ['axe', 4078],
  ['swor', 4079],
  ['knif', 4080],
  ['tpot', 4081],
  ['jave', 4082],
  ['spea', 4083],
  ['bow', 4084],
  ['pole', 4086],
  ['xbow', 4087],
  ['h2h', 21258],
  ['h2h2', 21258],
  ['orb', 4085],
  ['wand', 4085],
  ['blun', 4077],
];

// dword_721F10, indexed by 5*(speed-10) + a per-class offset. Buckets are 1..5.
const SpeedBuckets: readonly number[] = [
  1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 2, 1, 1, 2, 1, 2, 2, 1, 2, 1, 2, 2, 2, 2, 2, 3,
  2, 2, 3, 2, 3, 3, 2, 3, 2, 3, 3, 3, 3, 2, 4, 3, 3, 4, 3, 4, 4, 3, 4, 3, 4, 4, 4, 4, 3, 5, 4, 4, 5,
  4, 5, 5, 4, 5, 4, 5, 5, 5, 5, 4, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5,
];

// dword_722078, indexed by classId*2 + (bow or crossbow ? 1 : 0).
const ClassSpeedOffset: readonly number[] = [0, 2, 1, 4, 1, 4, 0, 3, 0, 3, 1, 4, 0, 3];

// dword_722078 is indexed by 2*classId, and with no player unit the class id is -1
// (0x486274). Index -2 and -1 read the last two dwords of dword_721F10, which are both 5, so
// a viewer-less tooltip behaves as though the offset were 5.
const NoViewerSpeedOffset = 5;

/** C# `string.IsNullOrEmpty`. */

// Builds the 18 buffers from a v2 record plus the embedded tables. Writers not yet implemented
// return null, which the composer treats as "section does not apply".
export class RecordSections implements IItemTooltipSections {
  private readonly data: D2DataFiles;
  private readonly items: ItemTable;
  private readonly types: ItemTypeTree;
  private readonly item: ItemIdentity;
  private readonly viewer: ItemViewer | null;
  private readonly clientPlayer: ItemViewer | null;

  // GetDificulity 0x48cb38 is game state, and it arrives through createContext because that is the
  // only entry point that has it. getSection(ItemName) reads it for the quest colour, so
  // createContext must run first — every path builds the context before composing, and a caller
  // that skips it gets difficulty 0, which is what a viewerless render meant anyway.
  private difficulty = 0;
  private readonly stats: Map<number, number>;
  private readonly names: ItemNameBuilder;
  private readonly sockets: Map<number, number>;

  // The `base` source group on its own. INV_CalcWeaponDamageRange decides its pModified flag by
  // comparing the BASE stat against the merged one (0x485300), so both are needed.
  private readonly baseStats: Map<number, number>;
  private readonly gemTable: GemTable;
  private readonly requiredLevel: RequiredLevelCalculator;
  private readonly propertyApplier: PropertyApplier;
  private readonly skillDamage: SkillDamage;
  private readonly requirements: EquipRequirements;
  private readonly attackSpeed: AttackSpeedCalculator;
  private readonly missiles: MissileTable;
  private readonly socketUnits: ItemUnit[] | null;

  constructor(
    data: D2DataFiles | null,
    items: ItemTable | null,
    types: ItemTypeTree | null,
    item: ItemIdentity | null,
    viewer: ItemViewer | null,
    stats: Map<number, number> | null,
    // Explicit rather than optional: each of these degrades the output SILENTLY when it
    // is missing. No baseStats and every damage, defense and durability number gets a
    // spurious colour-3 marker, because BaseStat() reads 0 and everything looks modified.
    // No sockets and the name loses its "Gemmed" prefix; no socketUnits and a socketed
    // jewel's affix requirement drops out of the required level.
    sockets: Map<number, number> | null,
    baseStats: Map<number, number> | null,
    socketUnits: ItemUnit[] | null,
    // INV_FormatAttackSpeedText 0x486201 and 0x486250 ignore the tooltip's own unit and read
    // GetPlayerUnit_0 (0x463de0, the client player) instead, so a mercenary's weapon is still
    // timed against the CHARACTER — both the frame lookup and the speed bucket's class offset.
    // Null falls back to the viewer, which is right whenever they are the same unit, and that is
    // every case but a merc panel.
    clientPlayer: ItemViewer | null = null,
  ) {
    if (data === null) throw new Error('data');
    if (items === null) throw new Error('items');
    if (types === null) throw new Error('types');
    if (item === null) throw new Error('item');

    this.data = data;
    this.items = items;
    this.types = types;
    this.item = item;
    this.viewer = viewer;
    this.clientPlayer = clientPlayer ?? viewer;
    this.stats = stats ?? new Map<number, number>();
    this.names = new ItemNameBuilder(data, items, types);
    this.sockets = sockets ?? new Map<number, number>();
    this.baseStats = baseStats ?? new Map<number, number>();
    this.socketUnits = socketUnits;
    this.missiles = new MissileTable(data.missiles, data.elementTypes);
    this.gemTable = new GemTable(data.gems, items);
    this.propertyApplier = new PropertyApplier(data, items, types);
    this.skillDamage = new SkillDamage(data.skillRows);
    this.gemTable.resolvePropertyCodesWith(code =>
      this.propertyApplier.properties.rowForCode(code),
    );
    this.requiredLevel = new RequiredLevelCalculator(data, items);
    this.requirements = new EquipRequirements(data, items);
    this.attackSpeed = new AttackSpeedCalculator(data, items);
  }

  get lineTerminator(): string | null {
    return this.data.strings.getByIndex(DescStringIds.Newline);
  }

  /** D2DataFiles.CreateGenerator, which the TypeScript port keeps on the consumer side. */
  private createGenerator(values: SynthesisedStatValues): ItemDescriptionGenerator {
    return new ItemDescriptionGenerator(
      this.data.itemStatCost,
      this.data.strings,
      values,
      this.data.skills,
      this.data.classes,
      this.data.monsterTypes,
      null,
      true,
    );
  }

  /**
   * The generator the composer's Modifiers block has to be built with.
   * SKILLDESC_BuildStatListDesc 0x4e49c0 walks the described UNIT'S statlists, so the damage
   * aggregate, the undead line and the never-breaks gate all need the same stats these
   * sections see. A generator built without them degrades paired damage into one line per
   * stat and drops the other two lines entirely.
   *
   * @param modifierStats The ItemStatView.Modifiers() set. Required, not optional: pass the full
   * stats here and base stats reach the damage aggregate and the 23/24 suppression, which the temp
   * list the engine builds at 0x4e612b never contains.
   */
  createModifierGenerator(modifierStats: Map<number, number> | null): ItemDescriptionGenerator {
    return this.createGenerator(
      new SynthesisedStatValues(
        modifierStats ?? this.stats,
        this.item,
        this.viewer,
        this.items,
        this.types,
        this.stats,
      ),
    );
  }

  /**
   * The composer's context for this item. `difficulty` is GetDificulity() (0x48cb38), the one
   * input that is game state rather than unit state.
   */
  createContext(difficulty = 0): ItemTooltipContext {
    this.difficulty = difficulty;

    const context = new ItemTooltipContext();
    context.quality = this.item.quality;
    context.flags = this.item.flags | 0;

    context.isWeaponOrArmorType =
      this.types.isOfType(this.primaryType(), this.secondaryType(), this.types.row('weap')) ||
      this.types.isOfType(this.primaryType(), this.secondaryType(), this.types.row('armo'));

    // Only ITEM_BuildSetItemTooltip reads this — 0x48d681 wraps its smite and block lines in one
    // IsOfType(item, 51).
    context.isShieldType = this.types.isOfType(
      this.primaryType(),
      this.secondaryType(),
      this.types.row('shld'),
    );

    context.forcesCraftedColor = this.forcesRuneColor();

    // 0x48e44c `cmp eax, 12h` on the items row's own wType WORD (+0x11E) — an exact
    // compare, not an IsOfType walk — then 0x48e451 diverts to INV_ShowBookTooltip and
    // 0x48e45c returns, so the generic tooltip is never built for a tome. Only `tbk` and
    // `ibk` reach itemtypes row 18.
    context.isBook = this.primaryType() === this.types.row('book');

    // items.txt nQuest +0x12A and nQuestDiffCheck +0x12B (0x48cb0b / 0x48cb19).
    context.isQuestItem = this.items.getInt(this.item.classId, 'quest') !== 0;
    context.isWirtsLeg = this.paddedCode(this.item.classId) === WirtsLegCode;
    return context;
  }

  /**
   * 0x48ec3f. Runs AFTER the whole tooltip is assembled and PREPENDS to the finished buffer,
   * so it is the bottom row on screen — hence its place at the head of the append order.
   *
   * The gates are `quest != 0` (items +0x12A), code not `leg `, ShopMode exactly 0, and the
   * unit's dwMode 0 (in inventory). Twenty-four other quest items pass the outer gate but
   * fall to the colour-only branch at 0x48ece5 and emit no line.
   */
  private questUsage(shopMode = 0, inInventory = true): string | null {
    if (this.items.getInt(this.item.classId, 'quest') === 0 || shopMode !== 0 || !inInventory) {
      return null;
    }

    const code = this.paddedCode(this.item.classId);
    if (code === WirtsLegCode) {
      return null;
    }

    if (code === HoradricCubeCode) {
      return this.str(SectionStringIds.RightClickToOpen) + this.terminator;
    }

    if (code === CairnStonesKeyCode) {
      return this.str(SectionStringIds.RightClickToRead) + this.terminator;
    }

    return null;
  }

  private forcesRuneColor(): boolean {
    const code = this.paddedCode(this.item.classId);

    for (const forced of RuneColorCodes) {
      if (code === forced) {
        return true;
      }
    }

    return this.types.isOfType(this.primaryType(), this.secondaryType(), this.types.row('rune'));
  }

  private paddedCode(classId: number): string {
    const code = this.items.code(classId);
    return code.length >= 4 ? code.substring(0, 4) : code.padEnd(4, ' ');
  }

  /**
   * DELIBERATE DEVIATION, and the only one in this class.
   *
   * ITEM_CheckEquipRequirements 0x62eaf0 reads the viewer's attributes through
   * GetStatUnsignedValue, which returns 0 for a null unit (0x625483). Strength and dexterity are
   * then gated on `value > 0` (0x62ebd5 / 0x62ec31), so a null unit reports BOTH as unmet, and
   * level compares against 0 too (0x62eca1) — the game would paint all three red.
   *
   * That branch is not reachable in the game: LoadItemDesc resolves its unit from GetPlayerUnit
   * (0x48dee0) and only ever draws the local player's tooltip, so "no viewer" is a concept this
   * library invented. Painting a requirement red when nobody has been asked to meet it states
   * something false, so a viewerless render leaves them white. Pass a viewer to get the game's
   * answer.
   */
  isRequirementUnmet(section: ItemTooltipSection): boolean {
    if (this.viewer === null) {
      return false;
    }

    switch (section) {
      case ItemTooltipSection.RequiredLevel:
        return !this.requirements.metLevel(
          this.item,
          this.viewer,
          this.stats,
          this.socketUnits,
          this.sockets,
        );
      case ItemTooltipSection.RequiredStrength:
        return !this.requirements.metStrength(this.item, this.viewer, this.stats);
      case ItemTooltipSection.RequiredDexterity:
        return !this.requirements.metDexterity(this.item, this.viewer, this.stats);
      case ItemTooltipSection.ClassRestriction:
        return !this.requirements.metClass(this.item, this.viewer);
      default:
        return false;
    }
  }

  getSection(section: ItemTooltipSection): string | null {
    switch (section) {
      case ItemTooltipSection.ItemName: {
        // Kept separate so a null name stays null: C# concatenates null as empty, JavaScript
        // stringifies it to "null", and a book's ItemName really is null.
        const name = this.names.build(this.item, this.sockets.size);
        return name === null ? null : this.questNameColorPrefix() + name;
      }

      case ItemTooltipSection.Unidentified:
        return this.unidentified();

      case ItemTooltipSection.Modifiers:
        return this.elixirDescription();

      case ItemTooltipSection.EtherealSocketed:
        return this.etherealSocketed();
      case ItemTooltipSection.Durability:
        return this.durability();
      case ItemTooltipSection.RequiredLevel:
        return this.requiredLevelLine();
      case ItemTooltipSection.RequiredStrength:
        return this.requirement('reqstr', SectionStringIds.RequiredStrength);
      case ItemTooltipSection.RequiredDexterity:
        return this.requirement('reqdex', SectionStringIds.RequiredDexterity);
      case ItemTooltipSection.ArmorClass:
        return this.armorClass();
      case ItemTooltipSection.SmiteOrKickDamage:
        return this.smiteOrKick();
      case ItemTooltipSection.WeaponDamage:
        return this.weaponDamage();
      case ItemTooltipSection.BlockChance:
        return this.blockChance();
      case ItemTooltipSection.ClassRestriction:
        return this.classRestriction();
      case ItemTooltipSection.QuantityAndSpellDescription:
        return this.quantityAndSpellDescription();
      case ItemTooltipSection.CharmDescription:
        return this.charmDescription();
      case ItemTooltipSection.QuestUsage:
        return this.questUsage();
      case ItemTooltipSection.BookQuantity:
        return this.bookQuantity();
      case ItemTooltipSection.BookRightClickToUse:
        return this.bookUsageLine(SectionStringIds.RightClickToUse);
      case ItemTooltipSection.BookInsertScrolls:
        return this.bookUsageLine(SectionStringIds.InsertScrolls);
      case ItemTooltipSection.RuneLetters:
        return this.runeLetters();
      case ItemTooltipSection.AttackSpeed:
        return this.attackSpeedLine();
      case ItemTooltipSection.SocketFillerDescription:
        return this.socketFillerDescription();
      default:
        return null;
    }
  }

  /**
   * GetItemName's tail, 0x48cb0b-0x48ce6d. Gated on items.txt `quest` (+0x12A); with
   * `questdiffcheck` (+0x12B) set and stat 356 below the current difficulty it prepends colour 1
   * (0x48cb50), otherwise colour 4 unless the code is `leg ` (0x48ce59 compares the dword
   * 0x2067656C — Wirt's Leg).
   *
   * AppendAsWideChar PREPENDS, so this lands at the START of the name buffer and LoadItemDesc then
   * stacks the section's own v105 marker in front of it. Both are in the string the game draws,
   * which is why this is text rather than a section colour.
   */
  private questNameColorPrefix(): string {
    if (this.items.getInt(this.item.classId, 'quest') === 0) {
      return '';
    }

    if (
      this.items.getInt(this.item.classId, 'questdiffcheck') !== 0 &&
      this.stat(StatQuestDifficulty) < this.difficulty
    ) {
      return ItemTooltipColor.Marker + '1';
    }

    return this.paddedCode(this.item.classId) === WirtsLegCode ? '' : ItemTooltipColor.Marker + '4';
  }

  private str(id: number): string {
    return this.data.strings.getByIndex(id) ?? '';
  }

  private get space(): string {
    return this.str(DescStringIds.Space);
  }

  private get terminator(): string {
    return this.str(DescStringIds.Newline);
  }

  private stat(statId: number): number {
    return this.stats.get(ItemStatReader.packStatKey(0, statId)) ?? 0;
  }

  // 0x484b10. Both halves are optional; the ", " separator is an ASCII literal, not a locale
  // string. Socket count is truncated to a byte at 0x484c2a.
  private etherealSocketed(): string | null {
    const ethereal = this.item.has(ItemRecordFlags.Ethereal);
    const socketed = this.item.has(ItemRecordFlags.Socketed);

    if (!ethereal && !socketed) {
      return null;
    }

    let text = '';

    if (ethereal) {
      text += this.str(SectionStringIds.EtherealCannotBeRepaired);
    }

    if (socketed) {
      if (ethereal) {
        text += ', ';
      }

      text +=
        this.str(SectionStringIds.Socketed) +
        this.space +
        '(' +
        String(this.stat(StatSockets) & 0xff) +
        ')';
    }

    return text + this.terminator;
  }

  // 0x484e90. Gates from ITEM_CheckIfItemHasDurability (0x629930).
  private durability(): string | null {
    if (this.items.getInt(this.item.classId, 'nodurability') !== 0) {
      return null;
    }

    if (this.items.getInt(this.item.classId, 'durability') <= 0) {
      return null;
    }

    if (this.stat(StatIndestructible) > 0) {
      return null;
    }

    const max = this.stat(StatMaxDurability);
    if (max <= 0) {
      return null;
    }

    if (this.isThrowable()) {
      return null;
    }

    // 0x484f0b: STATLIST_GetStatBonusFromLists is merged-minus-base (0x625570), and the
    // marker goes on the MAX number alone (0x484fc6) — the current value never carries one.
    const marker = this.bonus(StatMaxDurabilityPercent) !== 0 ? ItemTooltipColor.Marker + '3' : '';

    return (
      this.str(SectionStringIds.DurabilityLabel) +
      this.space +
      String(this.stat(StatDurability)) +
      this.space +
      this.str(SectionStringIds.Of) +
      this.space +
      marker +
      String(max) +
      this.terminator
    );
  }

  // 0x484ff0, called only when ITEM_GetRequiredLevel returns more than 1 (0x48e565 `jle`).
  private requiredLevelLine(): string | null {
    // 0x48e54f: the caller wraps the whole block in CheckItemFlag(item, 0x10 IDENTIFIED).
    if (!this.item.has(ItemRecordFlags.Identified)) {
      return null;
    }

    const level = this.requiredLevel.calculate(
      this.item,
      this.viewer,
      this.stats,
      this.socketUnits,
      this.sockets,
    );
    if (level <= 1) {
      return null;
    }

    return this.str(SectionStringIds.RequiredLevel) + this.space + String(level) + this.terminator;
  }

  // 0x4850a0 / 0x485170. The caller skips the section when the BASE requirement is 0
  // (0x48e6a2 / 0x48e6c6), and the total shares EquipRequirements' expression so the number
  // and the met flag can never disagree.
  private requirement(column: string, labelId: number): string | null {
    if (this.items.getInt(this.item.classId, column) <= 0) {
      return null;
    }

    const total = this.requirements.requirement(this.item, column, this.stats);
    if (total <= 0) {
      return null;
    }

    return this.str(labelId) + this.space + String(total) + this.terminator;
  }

  // 0x485ee0. The by-time contributions are already folded into the runtime value when the
  // producer supplies one; otherwise the plain merged stat.
  private armorClass(): string | null {
    const armor = this.stat(StatArmorClass);
    if (armor <= 0) {
      return null;
    }

    if (!this.types.isOfType(this.primaryType(), this.secondaryType(), this.types.row('armo'))) {
      return null;
    }

    // 0x485fb1: SERVER_GetUnitStat reads the item's BASE stat 31 and any difference from
    // the merged value sets the flag the marker at 0x4860de depends on.
    const marker = this.baseStat(StatArmorClass) !== armor ? ItemTooltipColor.Marker + '3' : '';

    return (
      this.str(SectionStringIds.ArmorClass) + this.space + marker + String(armor) + this.terminator
    );
  }

  // 0x485d40. Shield gives Smite for a Paladin, boots give Kick for an Assassin. The class
  // gate is the caller's, and it must also require a PLAYER — LoadItemDesc omits that check.
  private smiteOrKick(): string | null {
    if (this.viewer === null || !this.viewer.isPlayer) {
      return null;
    }

    let label: number;
    let extraMin = 0;
    let extraMax = 0;

    if (this.types.isOfType(this.primaryType(), this.secondaryType(), this.types.row('shld'))) {
      if (this.viewer.classId !== PaladinClass) {
        return null;
      }

      // 0x48e768/0x48e778: a class-restricted shield whose class is not Paladin is
      // refused outright. `head` (Voodoo Heads) is Equiv1=shld with Class=nec, so all
      // fifteen ne* rows are shields the game will not smite with — without this a
      // Paladin sees "Smite Damage: 0 to 0" on every shrunken head.
      const restriction = this.types.classCode(this.primaryType());
      if (
        restriction.length !== 0 &&
        restriction.toLowerCase() !== PaladinClassCode.toLowerCase()
      ) {
        return null;
      }

      label = SectionStringIds.SmiteDamage;

      // 0x485df1: both halves come from SKILL_CalcMin/MaxDamage for Holy Shield at the
      // player's skill level, shifted back down by 8.
      const holy = this.holyShieldDamage();
      extraMin = holy.min;
      extraMax = holy.max;
    } else if (
      this.types.isOfType(this.primaryType(), this.secondaryType(), this.types.row('boot'))
    ) {
      if (this.viewer.classId !== AssassinClass) {
        return null;
      }

      label = SectionStringIds.KickDamage;
    } else {
      return null;
    }

    const min = this.items.getInt(this.item.classId, 'mindam') + extraMin;
    const max = this.items.getInt(this.item.classId, 'maxdam') + extraMax;

    return (
      this.str(label) +
      this.space +
      String(min) +
      this.space +
      this.str(SectionStringIds.To) +
      this.space +
      String(max) +
      this.terminator
    );
  }

  // 0x485410. Two-handed weapons use stats 23/24 and label 3466; one-handed use 21/22 and
  // 3465. A throwable weapon also gets a throw line (stats 159/160).
  //
  // OPEN, and UNTRACED. INV_CalcWeaponDamageRange 0x485240 does three things this does not: it
  // takes *pMax as MAX(mergedMax, mergedMin), then adds stat 272 and a percent of the running
  // total from stat 273, and it reads the merged pair off the UNIT after temporarily attaching the
  // item to it (STATLIST_SetItemStatActive 0x4852a1, restored at 0x4852cb / 0x4852d8). Here the
  // pair is read straight off the item, and 272/273 only ever feed damageIsModified. Whether any
  // of the three moves a shipped item is uncounted.
  private weaponDamage(): string | null {
    if (!this.types.isOfType(this.primaryType(), this.secondaryType(), this.types.row('weap'))) {
      return null;
    }

    // 0x485459 tests tpot FIRST and takes an arm that writes the buffer outright, so a
    // throwing potion gets ONE line and none of the ordinary damage or throw text.
    if (this.types.isOfType(this.primaryType(), this.secondaryType(), this.types.row('tpot'))) {
      return this.throwingPotionDamage();
    }

    // 0x48e704 / 0x48e716: the gate is GetTxtMinDamage >= 0 AND GetTxtMaxDamage >= 0, which
    // read the item's own stat 21 and 22. ZERO PASSES — a weapon with no damage stats still
    // gets a line, and the min+1 clamp turns it into "0 to 1". Only a NEGATIVE value skips it.
    if (this.stat(StatMinDamage) < 0 || this.stat(StatMaxDamage) < 0) {
      return null;
    }

    let text = this.barbarianDualWield() ? this.dualWieldDamage() : this.singleDamageLine();

    if (this.isThrowable()) {
      // 0x485ab6: the throw block has no min+1 clamp either.
      const throwLine = this.damageLine(
        SectionStringIds.ThrowDamage,
        StatThrowMinDamage,
        StatThrowMaxDamage,
        false,
        true,
      );

      if (throwLine !== null) {
        // Appended after, so the reversal puts Throw Damage ABOVE the other line.
        text = (text ?? '') + throwLine;
      }
    }

    return text;
  }

  /**
   * 0x48545f. The numbers come from the item's missiles.txt record, not from its stats, and
   * the elemental type picks a colour for BOTH numbers (jump table 0x4854d0). The label gets
   * an explicit colour 0 of its own (0x4854af), and the "to max" half is dropped outright
   * when the two ends agree (0x4855bd).
   */
  private throwingPotionDamage(): string | null {
    const damage: MissileThrowDamage | null = this.missiles.tryGetThrowDamage(
      this.items.getInt(this.item.classId, 'missiletype'),
    );
    if (damage === null) {
      return null;
    }

    const marker = ItemTooltipColor.Marker + String.fromCharCode(0x30 + damage.color);

    let text =
      ItemTooltipColor.Marker +
      '0' +
      this.str(SectionStringIds.ThrowDamage) +
      this.space +
      marker +
      String(damage.min);

    if (damage.min !== damage.max) {
      text += this.space + this.str(SectionStringIds.To) + this.space + marker + String(damage.max);
    }

    return text + this.terminator;
  }

  /**
   * BARBARIAN_CheckItemData_b1or2Handed_isTrue 0x62a1e0: a PLAYER (dwUnitType 0) of class 4
   * holding an item whose items.txt `1or2handed` byte (+0x13D) is set. `2handed` is not
   * consulted, and neither is anything about what else is equipped.
   */
  private barbarianDualWield(): boolean {
    return (
      this.viewer !== null &&
      this.viewer.isPlayer &&
      this.viewer.classId === BarbarianClass &&
      this.items.getInt(this.item.classId, '1or2handed') !== 0
    );
  }

  /**
   * 0x485669 onwards. TWO-HAND comes first, then one-hand, each with its own colour 0 prepend
   * (0x4858c3 / 0x4858d0) and its own terminator. Note what is ABSENT: this path never applies
   * the `max = min + 1` clamp that the single-line path does at 0x485931, so a dual-wielding
   * Barbarian can be shown a weapon whose min and max are equal.
   */
  private dualWieldDamage(): string | null {
    const twoHand = this.damageLine(
      SectionStringIds.TwoHandDamage,
      StatSecondaryMinDamage,
      StatSecondaryMaxDamage,
      false,
    );

    const oneHand = this.damageLine(
      SectionStringIds.OneHandDamage,
      StatMinDamage,
      StatMaxDamage,
      false,
    );

    const marker = ItemTooltipColor.Marker + '0';

    return (twoHand === null ? '' : marker + twoHand) + (oneHand === null ? '' : marker + oneHand);
  }

  // 0x4858f1: which pair to read comes from IsTwoHanded, i.e. the items.txt `2handed` column.
  private singleDamageLine(): string | null {
    const twoHanded = this.items.getInt(this.item.classId, '2handed') !== 0;

    return this.damageLine(
      twoHanded ? SectionStringIds.TwoHandDamage : SectionStringIds.OneHandDamage,
      twoHanded ? StatSecondaryMinDamage : StatMinDamage,
      twoHanded ? StatSecondaryMaxDamage : StatMaxDamage,
      true,
    );
  }

  /**
   * One line's numbers, with no formatting. `damageLine` writes these and `TooltipEngine.damage`
   * returns them, so the string and the API cannot disagree about a value.
   */
  private damageValues(
    kind: ItemDamageKind,
    minStat: number,
    maxStat: number,
    clampMax: boolean,
    throwShape: boolean,
  ): ItemDamageRange {
    const min = this.stat(minStat);
    let max = this.stat(maxStat);

    // 0x485931, single-line path only.
    // The `min + 1` is itself int32: at int.MaxValue it wraps to int.MinValue, so the clamp does
    // NOT fire and the max is left alone (0x485931).
    if (clampMax && max <= ((min + 1) | 0)) {
      max = (min + 1) | 0;
    }

    const modified = throwShape
      ? this.throwDamageIsModified(minStat, maxStat)
      : this.damageIsModified(minStat, maxStat);

    return { kind, min, max, modified };
  }

  /**
   * The same routing `weaponDamage` performs, collecting numbers instead of writing text. Both walk
   * the same gates and the same `damageValues`, and a test asserts the numbers here are the numbers
   * in the rendered line, so the two cannot drift apart silently.
   */
  weaponDamageValues(): ItemDamageRange[] {
    const lines: ItemDamageRange[] = [];

    if (!this.types.isOfType(this.primaryType(), this.secondaryType(), this.types.row('weap'))) {
      return lines;
    }

    // 0x485459 takes the tpot arm outright, so such an item has no other damage line.
    if (this.types.isOfType(this.primaryType(), this.secondaryType(), this.types.row('tpot'))) {
      const potion = this.missiles.tryGetThrowDamage(
        this.items.getInt(this.item.classId, 'missiletype'),
      );

      if (potion !== null) {
        lines.push({
          kind: ItemDamageKind.ThrowingPotion,
          min: potion.min,
          max: potion.max,
          modified: false,
        });
      }

      return lines;
    }

    if (this.stat(StatMinDamage) < 0 || this.stat(StatMaxDamage) < 0) {
      return lines;
    }

    // DISPLAY order, which is the reverse of the order the buffers are written in. The throw line
    // is appended last (0x485ab6) so it ends up on TOP; the dual-wield pair is written two-hand
    // first (0x4856a2 before 0x4857c5) so ONE-HAND ends up above it.
    if (this.isThrowable()) {
      lines.push(
        this.damageValues(
          ItemDamageKind.Throw,
          StatThrowMinDamage,
          StatThrowMaxDamage,
          false,
          true,
        ),
      );
    }

    if (this.barbarianDualWield()) {
      lines.push(
        this.damageValues(ItemDamageKind.OneHand, StatMinDamage, StatMaxDamage, false, false),
      );
      lines.push(
        this.damageValues(
          ItemDamageKind.TwoHand,
          StatSecondaryMinDamage,
          StatSecondaryMaxDamage,
          false,
          false,
        ),
      );

      return lines;
    }

    const twoHanded = this.items.getInt(this.item.classId, '2handed') !== 0;

    lines.push(
      this.damageValues(
        twoHanded ? ItemDamageKind.TwoHand : ItemDamageKind.OneHand,
        twoHanded ? StatSecondaryMinDamage : StatMinDamage,
        twoHanded ? StatSecondaryMaxDamage : StatMaxDamage,
        true,
        false,
      ),
    );

    return lines;
  }

  private static kindOf(labelId: number): ItemDamageKind {
    if (labelId === SectionStringIds.TwoHandDamage) {
      return ItemDamageKind.TwoHand;
    }
    if (labelId === SectionStringIds.ThrowDamage) {
      return ItemDamageKind.Throw;
    }

    return ItemDamageKind.OneHand;
  }

  private damageLine(
    labelId: number,
    minStat: number,
    maxStat: number,
    clampMax: boolean,
    throwShape = false,
  ): string | null {
    const values = this.damageValues(
      RecordSections.kindOf(labelId),
      minStat,
      maxStat,
      clampMax,
      throwShape,
    );

    const min = values.min;
    const max = values.max;

    // The throw block does NOT share the 1H/2H emission shape. 0x485a97 puts an explicit
    // colour 0 on the label, and 0x485afd / 0x485b7c mark BOTH numbers rather than relying
    // on the marker staying in force from the min. Its flag is also pre-seeded at
    // 0x485a14-0x485a54 from STATLIST_GetStatBonusFromLists on stats 18, 17, 159 and 160,
    // where the 1H/2H flag is zeroed at 0x485662 and never gets those terms.
    if (throwShape) {
      const throwMarker = ItemTooltipColor.Marker + (values.modified ? '3' : '0');

      return (
        ItemTooltipColor.Marker +
        '0' +
        this.str(labelId) +
        this.space +
        throwMarker +
        String(min) +
        this.space +
        this.str(SectionStringIds.To) +
        this.space +
        throwMarker +
        String(max) +
        this.terminator
      );
    }

    // 0x4856f5 / 0x485818 / 0x485984 prepend colour 3 to the number buffer before the MIN is
    // appended, and only then — STRING_CopyCharToWCharWithSetMaxSize overwrites that buffer
    // before the max, so the max never carries a marker of its own. But a colour code stays in
    // force until the next one, so the visible result is the LABEL in the section colour and
    // the whole numeric run — min, "to" and max — in colour 3.
    const marker = values.modified ? ItemTooltipColor.Marker + '3' : '';

    return (
      this.str(labelId) +
      this.space +
      marker +
      String(min) +
      this.space +
      this.str(SectionStringIds.To) +
      this.space +
      String(max) +
      this.terminator
    );
  }

  /**
   * INV_CalcWeaponDamageRange's pModified out-param. Set when the BASE stat is below the merged
   * one on either end (0x485300 compares SERVER_GetUnitStat against GetStatUnsignedValue), or
   * when either by-time damage stat contributes anything (0x485372 / 0x4853eb).
   */
  private damageIsModified(minStat: number, maxStat: number): boolean {
    return (
      this.baseStat(minStat) < this.stat(minStat) ||
      this.baseStat(maxStat) < this.stat(maxStat) ||
      this.stat(StatDamageByTime) !== 0 ||
      this.stat(StatDamagePercentByTime) !== 0
    );
  }

  /**
   * The throw line's flag is the 1H/2H one PLUS a pre-seed: 0x485a14-0x485a54 sets it when
   * STATLIST_GetStatBonusFromLists returns non-zero for any of stats 18, 17, 159 or 160, and
   * INV_CalcWeaponDamageRange only ever sets the flag afterwards (0x485305 / 0x485377 /
   * 0x4853f0), never clears it.
   *
   * The pre-seed is mostly redundant against a faithfully merged stat set, because
   * ItemStatCost row 18 carries op 13 with `op stat3 = item_throw_mindamage`, so an ED
   * bonus already moves the merged 159/160. It is load-bearing only where that product
   * truncates to zero — a Throwing Knife with a +10%/+11% prefix. It is NOT redundant here
   * yet, because op 13 is not re-applied when the captured lists are merged.
   */
  private throwDamageIsModified(minStat: number, maxStat: number): boolean {
    return (
      this.damageIsModified(minStat, maxStat) ||
      this.bonus(StatMinDamagePercent) !== 0 ||
      this.bonus(StatMaxDamagePercent) !== 0 ||
      this.bonus(minStat) !== 0 ||
      this.bonus(maxStat) !== 0
    );
  }

  private baseStat(statId: number): number {
    return this.baseStats.get(ItemStatReader.packStatKey(0, statId)) ?? 0;
  }

  // STATLIST_GetStatBonusFromLists 0x625560 returns the merged value MINUS the base one.
  private bonus(statId: number): number {
    return this.stat(statId) - this.baseStat(statId);
  }

  // 0x485be0. total = merged stat 20 + CharStats.BlockFactor + Holy Shield, CAPPED AT 75
  // (0x485c65). The newline is part of the "%d%%\n" format, not locale 3998, and locale 11018
  // already ends with a space.
  private blockChance(): string | null {
    if (!this.types.isOfType(this.primaryType(), this.secondaryType(), this.types.row('shld'))) {
      return null;
    }

    let total = this.stat(StatToBlock);

    if (
      this.viewer !== null &&
      this.viewer.isPlayer &&
      this.data.charStats !== null &&
      this.viewer.classId >= 0 &&
      this.viewer.classId < this.data.charStats.rowCount
    ) {
      // `| 0` on every accumulation: C# `int` wraps at 32 bits and the 75-cap below is a SIGNED
      // compare, so a total that overflows goes negative and fails the cap rather than hitting
      // it. A JS double would sail past both.
      total = (total + this.data.charStats.getInt(this.viewer.classId, 'BlockFactor')) | 0;
      total = (total + this.holyShieldBlockBonus()) | 0;
    }

    if (total > MaxBlockChance) {
      total = MaxBlockChance;
    } else if (total === 0) {
      return null;
    }

    // 0x485cd7 reads items.txt nBlock (+0x111) and colours the NUMBER buffer 3 when the
    // total beats it; 0x485d0e then prepends an explicit colour 0 to the LABEL buffer,
    // which is why the game emits two markers for this one section.
    const numberMarker =
      total > this.items.getInt(this.item.classId, 'block') ? ItemTooltipColor.Marker + '3' : '';

    return (
      ItemTooltipColor.Marker +
      '0' +
      this.str(SectionStringIds.BlockChance) +
      numberMarker +
      String(total) +
      this.str(DescStringIds.Percent) +
      this.terminator
    );
  }

  // ItemTypes `Class` restricts the item to one character class; the text is that class's
  // charstats StrClassOnly.
  private classRestriction(): string | null {
    const row = this.primaryType();
    if (row < 0 || this.data.itemTypes === null) {
      return null;
    }

    const code = this.data.itemTypes.getString(row, 'Class');
    if (isNullOrEmpty(code.trim())) {
      return null;
    }

    const classId = this.data.skills.classIdForCode(code);
    if (classId < 0) {
      return null;
    }

    const text = this.data.classes.getClassOnlyText(classId);
    return isNullOrEmpty(text) ? null : text + this.terminator;
  }

  // AppendQuanity 0x486100 for the quantity line, then INV_FormatItemStatCostText 0x486370 for
  // the spelldesc. Both write to the SAME buffer (var_1434 at 0x48e91c and 0x48e972) and every
  // spelldesc arm uses STRING_CopyWideString, so a spelldesc REPLACES the quantity line
  // outright rather than appending to it.
  //
  // (INV_FormatQuantityText 0x484db0 builds similar text into a buffer LoadItemDesc overwrites
  // at 0x48e9a5, so its output is dead in 1.14d.)
  private quantityAndSpellDescription(): string | null {
    return this.spellDescription() ?? this.quantityLine();
  }

  // 0x486160: a stackable item shows the line even at quantity 0, because the gate is
  // `stat 70 > 0 OR maxstack > 0`.
  /**
   * The book tooltip's quantity. `AppendQuanity` is called at 0x48d07d with none of the
   * identified / not-socketed gating the generic path applies at 0x48e8ef / 0x48e90d, so a
   * tome shows its count whatever its flags.
   */
  private bookQuantity(): string | null {
    const quantity = this.stat(StatQuantity);

    if (quantity <= 0 && this.items.getInt(this.item.classId, 'maxstack') <= 0) {
      return null;
    }

    return (
      this.str(SectionStringIds.QuantityLabel) + this.space + String(quantity) + this.terminator
    );
  }

  /**
   * 0x48d082 tests ShopMode for EXACTLY zero, so both usage lines vanish in any shop mode.
   */
  private bookUsageLine(stringId: number, shopMode = 0): string | null {
    return shopMode !== 0 ? null : this.str(stringId) + this.terminator;
  }

  private quantityLine(): string | null {
    // 0x48e8ef / 0x48e90d: AppendQuanity runs only for an IDENTIFIED, NOT-SOCKETED item.
    // The spelldesc that may replace its buffer (0x48e978) is reached either way.
    if (!this.item.has(ItemRecordFlags.Identified) || this.item.has(ItemRecordFlags.Socketed)) {
      return null;
    }

    const quantity = this.stat(StatQuantity);

    if (quantity <= 0 && this.items.getInt(this.item.classId, 'maxstack') <= 0) {
      return null;
    }

    return (
      this.str(SectionStringIds.QuantityLabel) + this.space + String(quantity) + this.terminator
    );
  }

  private spellDescription(): string | null {
    const mode = this.items.getInt(this.item.classId, 'spelldesc');

    // 0x48638b, and 0x4863a2 needs a player unit before any arm runs.
    if (mode === 0 || this.viewer === null) {
      return null;
    }

    const file = this.fileFor(this.item.classId);
    const row = this.rowFor(this.item.classId);
    if (file === null || row < 0) {
      return null;
    }

    const stringId = TxtKeys.id(file, row, 'spelldescstr', this.data.strings);
    if (stringId === NoSpellDescString) {
      return null;
    }

    const template = this.str(stringId);

    switch (mode) {
      case 1:
        // 0x4863eb: the string alone.
        return template + this.terminator;

      case 2: {
        // 0x48642f then the stat1 switch at 0x48644d scales it per class.
        const value = RecordSections.trySpellDescValue(file, row);
        if (value === null) {
          return null;
        }

        return (
          template +
          this.space +
          String(this.potionValueForClass(file, row, value)) +
          this.terminator
        );
      }

      case 3: {
        // 0x4864d0: the same value with NO class scaling.
        const value = RecordSections.trySpellDescValue(file, row);
        if (value === null) {
          return null;
        }

        return template + this.space + String(value) + this.terminator;
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

  /**
   * The value behind a spelldesc is `calc1` (+164), NOT `spelldesccalc` (+184) — 0x48642f and
   * 0x4864d0 both read offset 164. It is a calc EXPRESSION in general, evaluated by
   * ITEMS_SearchItemCodeTable through SKILLS_CompileSkillFormula, but every shipped row holds
   * a plain integer. A non-literal is refused rather than approximated.
   */
  private static trySpellDescValue(file: TxtFile, row: number): number | null {
    if (!file.hasColumn('calc1')) {
      return null;
    }

    const cell = file.getString(row, 'calc1').trim();
    if (cell.length === 0 || !/^[+-]?\d+$/.test(cell)) {
      return null;
    }

    const value = Number.parseInt(cell, 10);
    return Number.isNaN(value) ? null : value | 0;
  }

  // ITEMS_ModifyPotionValueByDifficulty 0x62a5d0 and
  // ITEMS_ModifyPotionSellValueByDifficulty 0x62a620. The names are misleading: neither reads
  // the difficulty. Both are per-CLASS multipliers picked by stat1 — the healing family
  // (hitpoints 6, hpregen 74) takes the first, the mana family (mana 8, manarecovery 26) the
  // second. The result is the familiar rule that a Barbarian gets double from healing potions
  // and single from mana, while the casters get the reverse.
  private potionValueForClass(file: TxtFile, row: number, value: number): number {
    const stat = this.data.itemStatCost.statIdForName(file.getString(row, 'stat1').trim());

    const healing = stat === StatHitPoints || stat === StatHpRegen;
    const mana = stat === StatMana || stat === StatManaRecovery;

    if (!healing && !mana) {
      return value; // 0x48644d default: no scaling at all
    }

    const viewer = this.viewer as ItemViewer;

    // A non-player viewer takes the jz at 0x62a5dd / 0x62a62d: doubled for the healing
    // family, unchanged for the mana family.
    if (!viewer.isPlayer || viewer.classId < 0 || viewer.classId > 6) {
      return healing ? value * 2 : value;
    }

    const index = healing
      ? (HealingPotionClassIndex[viewer.classId] as number)
      : (ManaPotionClassIndex[viewer.classId] as number);

    switch (index) {
      case 0:
        return (value >> 1) + value; // 1.5x
      case 1:
        return value * 2;
      default:
        return value;
    }
  }

  // 0x485dd2 / 0x485dda: the skill must exist on the viewer AND unit state 101 must be up.
  private holyShieldUp(): boolean {
    return (
      this.viewer !== null &&
      this.viewer.activeStates.has(SkillDamage.HolyShieldState) &&
      this.viewer.skillLevel(SkillDamage.HolyShieldSkillId) > 0
    );
  }

  private holyShieldDamage(): { min: number; max: number } {
    if (!this.holyShieldUp()) {
      return { min: 0, max: 0 };
    }

    const viewer = this.viewer as ItemViewer;

    const shifted = this.skillDamage.tryCalcDamage(
      SkillDamage.HolyShieldSkillId,
      viewer.skillLevel(SkillDamage.HolyShieldSkillId),
    );
    if (shifted === null) {
      return { min: 0, max: 0 };
    }

    // 0x485e04 / 0x485e10 take the >> 8 of whatever the calc returned.
    return { min: shifted.min >> 8, max: shifted.max >> 8 };
  }

  // 0x485c58.
  private holyShieldBlockBonus(): number {
    return this.holyShieldUp()
      ? this.skillDamage.paramWithDiminishing(
          SkillDamage.HolyShieldSkillId,
          (this.viewer as ItemViewer).skillLevel(SkillDamage.HolyShieldSkillId),
        )
      : 0;
  }

  /**
   * SKILLDESC_BuildStatBuffDesc 0x4e60dc returns to SKILLDESC_BuildChargeSkillDesc 0x4e5e90
   * BEFORE it builds anything, so for an elixir this text REPLACES the whole modifiers block
   * rather than joining it.
   *
   * The gate is `ITEM_GetItemData_wType(item) == 11` — an exact match on the PRIMARY type row,
   * not an equivalence walk, so a type merely descended from `elix` would not qualify.
   */
  private elixirDescription(): string | null {
    if (this.primaryType() !== this.types.row('elix')) {
      return null;
    }

    let text = '';

    for (const entry of ElixirTable) {
      // 0x4e5f15: the item's fileIndex picks the attribute. The six entries are distinct,
      // so at most one line is ever produced.
      if (entry.fileIndex !== this.item.fileIndex) {
        continue;
      }

      let value = this.stat(StatValue);

      // 0x4e5f41 / 0x4e5f4e / 0x4e5f5b: stat ids 6..11 are the 8-bit fixed-point ones
      // (life and mana), tested as three disjoint pairs.
      if (entry.fileIndex >= 6 && entry.fileIndex <= 11) {
        value >>= 8;
      }

      // 0x4e5f7d: a zero writes nothing at all.
      if (value === 0) {
        continue;
      }

      const name = this.str(value > 0 ? entry.positiveString : entry.negativeString);

      // 0x4e5fe5: only a positive value gets locale 4002 in front of the digits.
      const amount =
        value > 0 ? this.str(SectionStringIds.ElixirPlus) + String(value) : String(value);

      text += name + this.space + amount + this.terminator;
    }

    return text.length === 0 ? null : text;
  }

  // 0x48e943, the else of CheckItemFlag(item, 0x10) at 0x48e8ef. Mutually exclusive with the
  // Modifiers block, which is the identified arm of the same branch.
  private unidentified(): string | null {
    if (this.item.has(ItemRecordFlags.Identified)) {
      return null;
    }

    return this.str(SectionStringIds.Unidentified) + this.terminator;
  }

  // 0x48e5f0: locale 20438 for item type 13, "charm".
  private charmDescription(): string | null {
    if (!this.types.isOfType(this.primaryType(), this.secondaryType(), this.types.row('char'))) {
      return null;
    }

    const text = this.str(SectionStringIds.CharmDescription);
    return isNullOrEmpty(text) ? null : text + this.terminator;
  }

  // INV_FormatRunewordName 0x486670, gated by ITEM_GetItemsTxt_bHasInv at 0x48e5a6. It never
  // looks up a runeword name and never tests the runeword FLAG — any item holding runes gets
  // their letters, which is why a plain socketed sword shows 'RalOrt' too. Only rows that pass
  // IsOfType(rune) contribute, so gems are skipped.
  private runeLetters(): string | null {
    if (!this.hasInventory() || this.sockets.size === 0) {
      return null;
    }

    let letters = '';

    for (const socket of this.sockets) {
      const classId = socket[1] | 0;

      if (
        !this.types.isOfType(
          this.types.row(this.items.primaryTypeCode(classId)),
          this.types.row(this.items.secondaryTypeCode(classId)),
          this.types.row('rune'),
        )
      ) {
        continue;
      }

      const letter = this.gemLetter(classId);
      if (!isNullOrEmpty(letter)) {
        letters += letter;
      }
    }

    // The opening string, the apostrophe and the newline are all inside the "wrote at least
    // one letter" branch at 0x48673b, so a rune-free socketed item writes nothing at all.
    if (letters.length === 0) {
      return null;
    }

    return this.str(SectionStringIds.RunewordOpen) + letters + "'" + this.terminator;
  }

  // ITEM_GetItemsTxt_bHasInv 0x629900 reads the items.txt "hasinv" column.
  private hasInventory(): boolean {
    return this.items.getInt(this.item.classId, 'hasinv') !== 0;
  }

  // 0x4861d0, gated by IsOfType(item, weap) at 0x48e6f3.
  private attackSpeedLine(): string | null {
    if (!this.types.isOfType(this.primaryType(), this.secondaryType(), this.types.row('weap'))) {
      return null;
    }

    const speed = this.attackSpeed.tryCalculate(this.item, this.clientPlayer, this.stats);
    if (speed === null) {
      return null;
    }

    // word_721E88 holds 4088..4093 at stride 6.
    const speedWord = SectionStringIds.FirstSpeedWord + this.speedBucket(speed);

    let text = '';

    // When no weapon-class row matches, the class prefix and BOTH separators are skipped and
    // only the speed word is written (0x4862bb).
    const weaponClass = this.weaponClassName();
    if (weaponClass !== null) {
      text += weaponClass + this.space + this.str(SectionStringIds.Dash) + this.space;
    }

    const word = this.str(speedWord);
    text += word;

    // 0x486224 / 0x4862ff: a faster-attack-rate BONUS colours the speed word, and the prepend
    // lands on the word only — after the class prefix was already appended.
    //
    // STATLIST_GetStatBonusFromLists 0x625560 is merged MINUS base, not the merged total, so an
    // item whose whole attack rate came from its own base array would not be coloured. No shipped
    // weapon carries a base stat 93, which is why the difference is invisible against the corpus —
    // but the predicate is the bonus, not the value.
    if (this.bonus(StatFasterAttackRate) !== 0) {
      const at = text.length - word.length;
      text = text.substring(0, at) + ItemTooltipColor.Marker + '3' + text.substring(at);
    }

    return text + this.terminator;
  }

  // INV_FormatSocketFillerDesc 0x4865d0 -> SKILLDESC_BuildMagicAffixDesc 0x4e6850. What a LOOSE
  // gem or rune will do once socketed. These stats are on no statlist anywhere: the game
  // synthesises them onto a temporary list tagged 0x40, renders, and frees it (0x4e6811).
  //
  // The four blocks are (label, destination slot) = (11074, 2), (11073, 1), (11076, 1),
  // (11075, 0). Slot 1 really is read twice and slot 3 never exists — reproduce it.
  private socketFillerDescription(): string | null {
    if (!this.types.isOfType(this.primaryType(), this.secondaryType(), this.types.row('sock'))) {
      return null;
    }

    const gem = this.types.isOfType(
      this.primaryType(),
      this.secondaryType(),
      this.types.row('gem'),
    );
    const rune =
      !gem && this.types.isOfType(this.primaryType(), this.secondaryType(), this.types.row('rune'));

    const row = this.gemTable.rowForFillerClassId(this.item.classId);

    // SKILLDESC_BuildMagicAffixDesc empties the buffer at 0x4e68bc and returns at 0x4e6a7a
    // for a `sock` item that is neither gem nor rune — a JEWEL. The 11080 tail that
    // INV_FormatSocketFillerDesc appends at 0x48661f is UNCONDITIONAL, so the jewel still
    // gets that one line.
    if ((!gem && !rune) || row < 0) {
      return this.str(SectionStringIds.SocketFillerClose) + this.terminator;
    }

    const propMode = gem ? PropertyApplier.PropModeGem : PropertyApplier.PropModeRune;

    let text = '';

    for (const block of SocketFillerBlocks) {
      text += this.terminator;
      text += this.socketFillerBlock(row, block[1], propMode, this.str(block[0]) + this.space);

      // 0x4e681c-0x4e6836: after each block SKILLDESC_FormatMagicSuffixDesc strips ONE
      // trailing newline from the whole buffer, which is what keeps the blocks from
      // ending up separated by blank lines.
      if (text.length > 0 && text[text.length - 1] === '\n') {
        text = text.substring(0, text.length - 1);
      }
    }

    return (
      text +
      this.terminator +
      this.terminator +
      this.str(SectionStringIds.SocketFillerClose) +
      this.terminator
    );
  }

  private socketFillerBlock(gemRow: number, slot: number, propMode: number, label: string): string {
    let stats = new Map<number, number>();

    for (const property of this.gemTable.properties(gemRow, slot) as Iterable<ItemProperty>) {
      // 0x66004f: the walk stops at the first entry with no property, it does not skip it.
      if (property.propertyId < 0) {
        break;
      }

      this.propertyApplier.apply(propMode, this.item, property, stats);
    }

    if (stats.size === 0) {
      return '';
    }

    stats = sortByKey(stats);

    // The generator must see the synthesised stats through IStatValueSource too: the paired
    // damage lines are collected from the unit's statlist (0x4e49c0), not from the packed set.
    const values = new SynthesisedStatValues(stats, this.item, this.viewer, this.items, this.types);

    const lines: ItemDescriptionLine[] = [];
    for (const line of this.createGenerator(values).describe(stats)) {
      if (!isNullOrEmpty(line.text)) {
        lines.push(line);
      }
    }

    if (lines.length === 0) {
      return '';
    }

    // Gems and runes do NOT join the same way. SKILLDESC_BuildMagicAffixDesc 0x4e6850 sends
    // gems to 0x4e67d0, which pushes 0 at 0x4e67f3, and runes to 0x4e6720, which pushes 1 at
    // 0x4e6755 — the same slot, reaching BuildStatBuffDesc as a8 (ebp+0x1C). a8 == 1
    // terminates every line with 3998; a8 == 0 puts 3852 + 3995 (", ") before each line
    // after the first and terminates nothing, so the whole block is ONE line. Only visible
    // on a filler with two independent stats in a slot, i.e. the five Skulls.
    const inlineMode = propMode !== PropertyApplier.PropModeGem;

    return this.appendStatBuffText(this.createGenerator(values).join(lines, inlineMode), label);
  }

  /**
   * SKILLDESC_AppendStatBuffText 0x4e6410. It does NOT simply prepend the label. It scans back
   * from the second-to-last character for a newline; finding none it prefixes the label and
   * stops, and finding one it splices the label in before the FINAL line, strips that line's
   * trailing newline and closes with locale 3852.
   */
  private appendStatBuffText(description: string, label: string): string {
    if (description.length === 0) {
      return '';
    }

    // 0x4e6470: the scan starts at len - 2, so a description that is one line plus its
    // terminator never finds a split point.
    let at = description.length - 2;
    while (at > 0 && description[at] !== '\n') {
      --at;
    }

    // 0x4e6496 and 0x4e64b9 converge: label first, then the description untouched, and no 3852.
    if (at <= 0) {
      return label + description;
    }

    const head = description.substring(0, at);
    let tail = description.substring(at + 1);

    // 0x4e652a strips one trailing newline from the final line before 3852 goes on.
    if (tail.endsWith('\n')) {
      tail = tail.substring(0, tail.length - 1);
    }

    return (
      head +
      this.terminator +
      label +
      tail +
      this.str(SectionStringIds.SocketFillerBlockClose) +
      this.terminator
    );
  }

  private gemLetter(classId: number): string | null {
    return this.gemTable.letter(this.gemTable.rowForRuneClassId(classId));
  }

  private weaponClassName(): string | null {
    const type = this.primaryType();

    for (const entry of WeaponClassWords) {
      if (this.types.isOfType(type, this.secondaryType(), this.types.row(entry[0]))) {
        return this.str(entry[1]);
      }
    }

    return null;
  }

  // 0x48622f / 0x48623d bracket the table: 28 and over is bucket 5 outright, under 10 is
  // bucket 1, and only 10..27 index dword_721F10.
  private speedBucket(speed: number): number {
    if (speed >= 28) {
      return 5;
    }

    if (speed < 10) {
      return 1;
    }

    const classId = this.viewerClassId();
    const offset =
      classId < 0
        ? NoViewerSpeedOffset
        : (ClassSpeedOffset[classId * 2 + (this.rangedWeapon() ? 1 : 0)] as number);

    const index = 5 * (speed - 10) + offset;

    // dword_722078 sits immediately past dword_721F10's 90 dwords, so the one index the
    // table cannot hold — offset 5 with speed 27 — reads the class-offset table's first
    // entry instead (0x486283). That is a 0, and word_721E88[0] is locale 4088.
    return index < SpeedBuckets.length
      ? (SpeedBuckets[index] as number)
      : (ClassSpeedOffset[index - SpeedBuckets.length] as number);
  }

  // v5 at 0x48626b: crossbow (35) OR bow (27), via the full two-type test.
  private rangedWeapon(): boolean {
    return (
      this.types.isOfType(this.primaryType(), this.secondaryType(), this.types.row('xbow')) ||
      this.types.isOfType(this.primaryType(), this.secondaryType(), this.types.row('bow'))
    );
  }

  private viewerClassId(): number {
    // 0x486250 reads GetPlayerUnit_0 again for the bucket's class offset — the CLIENT player, not
    // whoever the tooltip is for.
    const classId =
      this.clientPlayer !== null && this.clientPlayer.isPlayer ? this.clientPlayer.classId : -1;
    return classId >= 0 && classId <= 6 ? classId : -1;
  }

  private fileFor(classId: number): TxtFile | null {
    const resolved = this.items.tryResolve(classId);
    return resolved === null ? null : resolved.file;
  }

  private rowFor(classId: number): number {
    const resolved = this.items.tryResolve(classId);
    return resolved === null ? -1 : resolved.row;
  }

  private isThrowable(): boolean {
    const row = this.primaryType();
    if (row < 0 || this.data.itemTypes === null) {
      return false;
    }

    return this.data.itemTypes.getInt(row, 'Throwable') !== 0;
  }

  private primaryType(): number {
    return this.types.row(this.items.primaryTypeCode(this.item.classId));
  }

  private secondaryType(): number {
    return this.types.row(this.items.secondaryTypeCode(this.item.classId));
  }
}
