import type { AnimDataFile, AnimDataRecord } from '../Data/AnimDataFile.js';
import type { ItemIdentity, ItemViewer } from '../Stats/ItemRecord.js';
import { ItemStatReader } from '../Stats/ItemStatReader.js';
import type { ItemTable } from '../Tables/ItemTable.js';
import type { D2DataFiles } from '../Tables/TxtDataProviders.js';
import type { TxtFile } from '../Data/TxtFile.js';
import { Int32 } from '../Types.js';

/**
 * ITEM_CalcWeaponAttackSpeed 0x62a710.
 *
 * The animation name is built by COMPOSIT_BuildCofPath 0x64f5b0 with bFullPath = 0 and
 * bResolveWeaponClass = 0 (both pushed as 0 at 0x62a78c/0x62a78e), so no .COF file is opened.
 * 0x64f5b0 switches on the UNIT TYPE, pushed from *pUnit at 0x62a79f, and the two branches that
 * matter build the name from different tables:
 *
 *   unit type 0, player (0x64f859)
 *       PlrType[classId].Token + PlrMode[7].Token + ItemsTxt[classId].wclass
 *     The weapon class is re-derived only `if (a9)` (0x64f879) and a9 is 0 here, so *a7 keeps the
 *     caller's items.txt wclass. e.g. a Paladin swinging a one-handed sword: "PAA11HS".
 *
 *   unit type 1, monster — which is what a MERCENARY is (0x64f6db)
 *       MonStats[classId].Code + MonMode[7].token + MonStats2[MonStatsEx].BaseW
 *     Here the weapon class IS re-derived, unconditionally for this mode, and the item's own
 *     wclass is overwritten. e.g. an Act 2 mercenary: "GUSChth" — whatever it is holding.
 *
 * Unit type 2 is objects (0x64f5d7); any other value falls out of the switch at 0x64f5d1 with the
 * name buffer never written, so there is nothing to model.
 *
 * Mode 7 is untouched by either substitution table: the player's (dword_745904, count 2) rewrites
 * modes 18 and 19, the monster's (dword_745918, count 1) rewrites mode 13, all three to 'gh  '.
 */
export class AttackSpeedCalculator {
  /**
   * The mode pushed at 0x62a7a2. It indexes PlrMode for a player and MonMode for a monster, and
   * the two files disagree about row 7: PlrMode row 7 is Attack1 ("A1"), MonMode row 7 is Cast
   * ("SC"). A monster therefore looks its attack speed up under its CAST animation.
   */
  static readonly AttackMode = 7;

  /** 0x62a7c5: a missing AnimData record makes the whole function return 45. */
  static readonly MissingAnimationSpeed = 45;

  // The unit types COMPOSIT_BuildCofPath 0x64f5b0 has branches for.
  private static readonly UnitTypePlayer = 0;
  private static readonly UnitTypeMonster = 1;

  // 'hth ' — the literal COMPOSIT_ResolveWeaponClass falls back to at 0x64f0a2 and 0x64f0d2, and
  // COMPOSIT_BuildCofPath writes directly at 0x64f758 when it skips the resolve entirely.
  private static readonly HandToHandWeaponClass = 'hth';

  // 0x62a7ff reads stat 68, which ItemStatCost row 68 names `attackrate` — NOT
  // `velocitypercent`, which is row 67. The two sit next to each other and the wrong one would
  // still produce plausible numbers, so the id is what matters here, not the name.
  private static readonly StatAttackRate = 68;
  private static readonly StatFasterAttackRate = 93;

  private readonly _items: ItemTable;
  private readonly _animData: AnimDataFile | null;
  private readonly _playerTypes: TxtFile | null;
  private readonly _playerModes: TxtFile | null;
  private readonly _monsterStats: TxtFile | null;
  private readonly _monsterStats2: TxtFile | null;
  private readonly _monsterModes: TxtFile | null;

  constructor(data: D2DataFiles, items: ItemTable) {
    this._items = items;
    this._animData = data.animData;
    this._playerTypes = data.playerTypes;
    this._playerModes = data.playerModes;
    this._monsterStats = data.monsterStats;
    this._monsterStats2 = data.monsterStats2;
    this._monsterModes = data.monsterModes;
  }

  get canCalculate(): boolean {
    return this._animData !== null && this._animData.rowCount > 0;
  }

  /**
   * The animation name, or null when the tables cannot supply one.
   */
  animationName(item: ItemIdentity, viewer: ItemViewer | null): string | null {
    if (!this.canCalculate || viewer === null) {
      return null;
    }

    switch (viewer.unitType) {
      case AttackSpeedCalculator.UnitTypePlayer:
        return this.playerAnimationName(item, viewer);

      case AttackSpeedCalculator.UnitTypeMonster:
        return this.monsterAnimationName(viewer);

      default:
        return null;
    }
  }

  // 0x64f859. TxtGetPlrTypeModeLine 0x65b480 indexes ONE array holding PlrType followed by
  // PlrMode (concatenated at 0x65ae91/0x65aeaa), selector 0 for the type and 1 for the mode.
  private playerAnimationName(item: ItemIdentity, viewer: ItemViewer): string | null {
    const token = AttackSpeedCalculator.token(this._playerTypes, viewer.classId);
    const mode = AttackSpeedCalculator.token(this._playerModes, AttackSpeedCalculator.AttackMode);
    const weaponClass = AttackSpeedCalculator.trim(this._items.getString(item.classId, 'wclass'));

    if (token === null || mode === null || weaponClass === null) {
      return null;
    }

    return token + mode + weaponClass;
  }

  // 0x64f6db. The monstats record is off_744304[670] + 424 * classId and the token is the DWORD
  // at +16, which the monstats field table registers as `Code`. TxtGetMonModeLine 0x65b500 points
  // both of its selectors at the SAME monmode array (0x65b19f/0x65b1a4), so the mode token is
  // monmode +32, `token`.
  private monsterAnimationName(viewer: ItemViewer): string | null {
    const stats = this._monsterStats;
    if (stats === null || viewer.classId < 0 || viewer.classId >= stats.rowCount) {
      return null;
    }

    const token = AttackSpeedCalculator.trim(stats.getString(viewer.classId, 'Code'));
    const mode = AttackSpeedCalculator.token(this._monsterModes, AttackSpeedCalculator.AttackMode);

    if (token === null || mode === null) {
      return null;
    }

    return token + mode + this.monsterWeaponClass(viewer.classId);
  }

  /**
   * COMPOSIT_ResolveWeaponClass 0x64f060 case 1. The item is not consulted at all: the class is
   * monstats2's `BaseW` (+16), reached through the monstats `MonStatsEx` link (+24,
   * TXT_MonStats_GetMonStats2 0x451fe0).
   *
   * The 'hth ' arm at 0x64f0cd only fires for mode 0 or 12 with monstats2 flag bit 16 clear, and
   * 0x64f730 skips the call entirely under the same condition, so mode 7 never reaches either.
   * What DOES remain reachable is the missing-record arm at 0x64f09b — though not with shipped
   * data: all 734 monstats.txt rows resolve MonStatsEx to a monstats2 row.
   */
  private monsterWeaponClass(classId: number): string {
    const stats = this._monsterStats;
    const stats2 = this._monsterStats2;
    if (stats === null || stats2 === null) {
      return AttackSpeedCalculator.HandToHandWeaponClass;
    }

    const link = stats.getString(classId, 'MonStatsEx');
    const row = link.length === 0 ? -1 : stats2.findRow('Id', link);

    return row < 0
      ? AttackSpeedCalculator.HandToHandWeaponClass
      : AttackSpeedCalculator.trimCode(stats2.getString(row, 'BaseW'));
  }

  /**
   * Returns null when the speed cannot be derived at all (no viewer or no tables) — the C#
   * `bool` plus `out int speed`; a missing AnimData record is NOT a failure, it yields 45,
   * exactly as the binary does.
   */
  tryCalculate(
    item: ItemIdentity,
    viewer: ItemViewer | null,
    stats: Map<number, number> | null,
  ): number | null {
    const name = this.animationName(item, viewer);
    if (name === null) {
      return null;
    }

    const record: AnimDataRecord | null = (this._animData as AnimDataFile).tryGet(name);
    if (record === null) {
      return AttackSpeedCalculator.MissingAnimationSpeed;
    }

    // 0x62a7df halts on a zero frame count rather than dividing by it.
    if (record.framesPerDirection === 0) {
      return null;
    }

    const rate = Int32.of(
      AttackSpeedCalculator.stat(stats, AttackSpeedCalculator.StatFasterAttackRate) +
        100 +
        AttackSpeedCalculator.stat(stats, AttackSpeedCalculator.StatAttackRate),
    );
    const divisor = Int32.div(Int32.mul(record.animationSpeed, rate), 100);
    if (divisor === 0) {
      return null;
    }

    return Int32.div(record.framesPerDirection << 8, divisor);
  }

  private static token(table: TxtFile | null, row: number): string | null {
    if (table === null || row < 0 || row >= table.rowCount || !table.hasColumn('Token')) {
      return null;
    }

    return AttackSpeedCalculator.trim(table.getString(row, 'Token'));
  }

  // COMPOSIT_BuildCofPath turns each SPACE into a NUL as it copies the three code bytes
  // (0x64f908 for the player, 0x64f78a for the monster), so a code shorter than three characters
  // simply ends early.
  private static trim(code: string | null): string | null {
    return code === null ? null : AttackSpeedCalculator.trimCode(code);
  }

  private static trimCode(code: string): string {
    let length = 0;
    while (length < code.length && length < 3 && code[length] !== ' ') {
      ++length;
    }

    return code.substring(0, length);
  }

  private static stat(stats: Map<number, number> | null, statId: number): number {
    if (stats === null) {
      return 0;
    }

    return stats.get(ItemStatReader.packStatKey(0, statId)) ?? 0;
  }
}
