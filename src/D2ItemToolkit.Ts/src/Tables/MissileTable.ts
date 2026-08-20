import type { TxtFile } from '../Data/TxtFile.js';
import { Int32 } from '../Types.js';

export interface MissileThrowDamage {
  min: number;
  max: number;

  color: number;
}

/**
 * The slice of missiles.txt the throwing-potion damage arm reads (0x485410). The record is 420
 * bytes; the fields used here are dwMinDamage +0xB0, dwMaxDamage +0xB4, nElemType +0xE4,
 * dwElemMin +0xE8, dwElemMax +0xEC, dwElemLen +0x11C and nHitShift +0x196.
 */
export class MissileTable {
  private readonly missiles: TxtFile | null;
  private readonly elementTypes: TxtFile | null;

  constructor(missiles: TxtFile | null, elementTypes: TxtFile | null) {
    this.missiles = missiles ?? null;
    this.elementTypes = elementTypes ?? null;
  }

  /** Rows in missiles.txt; 0 when the file was not supplied. */
  get rowCount(): number {
    return this.missiles === null ? 0 : this.missiles.rowCount;
  }

  tryGetThrowDamage(missileId: number): MissileThrowDamage | null {
    const missiles = this.missiles;
    if (missiles === null || missileId < 0 || missileId >= missiles.rowCount) {
      return null;
    }

    const hitShift = missiles.getInt(missileId, 'HitShift');
    const elementType = this.elementType(missiles.getString(missileId, 'EType'));

    // GetMinDamage/GetMinElemDamage 0x64af20 / 0x64b100 with level 1:
    // SKILLS_GetValueByLevelBreakpoints returns 0 below level 2 (0x644b7b), and every
    // shipped potion missile has DmgSymPerCalc/EDmgSymPerCalc = -1, so no calc runs.
    const min = missiles.getInt(missileId, 'MinDamage') << hitShift;
    const max = missiles.getInt(missileId, 'MaxDamage') << hitShift;
    let elementMin = missiles.getInt(missileId, 'EMin') << hitShift;
    let elementMax = missiles.getInt(missileId, 'Emax') << hitShift;

    if (elementType === MissileTable.ElementPoison) {
      // 0x4854e7-0x485515: the elemental halves are spread over the cloud's duration,
      // GetElementalLength at level 1 being plain ELen (0x64b2ca).
      let divisor = Int32.div(missiles.getInt(missileId, 'ELen'), 25);
      if (divisor <= 0) {
        divisor = 1;
      }

      elementMin = Int32.div(elementMin, divisor);
      elementMax = Int32.div(elementMax, divisor);
    }

    const damage: MissileThrowDamage = {
      min: (min + elementMin) >> 8,
      max: (max + elementMax) >> 8,
      color: MissileTable.elementColor(elementType),
    };

    // 0x48555c: max is raised to min, never the other way round.
    if (damage.max <= damage.min) {
      damage.max = damage.min;
    }

    return damage;
  }

  private static readonly ElementPoison = 5;

  // The jump table at 0x4854d0, indexed by elemType - 1. Magic (3) and everything outside
  // 1..5 take the default arm, which leaves the colour at 0.
  private static elementColor(elementType: number): number {
    switch (elementType) {
      case 1:
        return 1; // fire
      case 2:
        return 4; // lightning
      case 4:
        return 3; // cold
      case MissileTable.ElementPoison:
        return 2;
      default:
        return 0;
    }
  }

  private elementType(code: string): number {
    if (this.elementTypes === null || code.length === 0) {
      return 0;
    }

    const row = this.elementTypes.findRow('Code', code);
    return row < 0 ? 0 : row;
  }
}
