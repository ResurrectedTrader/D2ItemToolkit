import { TblFormat } from '../Description/ItemDescription.js';
import { DescStringIds, Int32, type IStatValueSource, type IStringTable } from '../Types.js';

// Mirrors SKILLDESC_BuildStatListDesc (0x4e49c0), which collects every damage kind into a
// 42-int struct, and SKILLDESC_BuildStatDescription (0x4e5a20), which the emission loop
// consults at 0x4e62a6 AHEAD of the DescFunc engine. A non-zero return means "handled, do not
// run DescFunc" — which is why sixteen stat ids fold into one "Adds X-Y" line.
//
// COLLECTION AND EMISSION USE DIFFERENT ARRAYS. Collection walks a table compiled into the
// binary (immediate at 0x4e49e3, 143 rows, stride 0x10) and dereferences only each row's
// first dword, the stat id — it never looks at descfunc. Emission walks the descfunc-FILTERED
// array built at 0x638530. So stat 59 is collected but never emitted, and the poison length
// and divisor reads below are live even though poisonlength ships with a blank descfunc.

export const DamageStatIds = {
  MinDamage: 21,
  MaxDamage: 22,
  SecondaryMinDamage: 23,
  SecondaryMaxDamage: 24,

  ItemMaxDamagePercent: 17,
  ItemMinDamagePercent: 18,

  FireMinDamage: 48,
  FireMaxDamage: 49,
  LightningMinDamage: 50,
  LightningMaxDamage: 51,
  MagicMinDamage: 52,
  MagicMaxDamage: 53,
  ColdMinDamage: 54,
  ColdMaxDamage: 55,
  PoisonMinDamage: 57,
  PoisonMaxDamage: 58,
  PoisonLength: 59,

  PoisonLengthDivisor: 326,

  UndeadDamagePercent: 122,
} as const;

export const DamageStringIds = {
  EnhancedDamage: 10023,

  PhysicalRange: 3623,

  FireSingle: 3612,
  FireRange: 3613,
  ColdSingle: 3614,
  ColdRange: 3615,
  LightningSingle: 3616,
  LightningRange: 3617,
  MagicSingle: 3618,
  MagicRange: 3619,
  PoisonSingle: 3620,
  PoisonRange: 3621,

  DamageToUndead: 3554,
} as const;

class DamagePair {
  min = 0;
  max = 0;

  bothPresent = false;

  singleStringId = 0;
  rangeStringId = 0;
}

export class ItemDamageAggregate {
  private readonly physical = new DamagePair();
  private readonly enhanced = new DamagePair();
  private readonly fire = new DamagePair();
  private readonly cold = new DamagePair();
  private readonly lightning = new DamagePair();
  private readonly magic = new DamagePair();
  private readonly poison = new DamagePair();

  private poisonLength = 0;
  private poisonDivisor = 0;

  private physicalEmitted = false;

  private readonly strings: IStringTable;

  constructor(
    strings: IStringTable | null | undefined,
    values: IStatValueSource | null | undefined,
  ) {
    if (strings === null || strings === undefined) throw new Error('strings');

    this.strings = strings;

    this.fire.singleStringId = DamageStringIds.FireSingle;
    this.fire.rangeStringId = DamageStringIds.FireRange;
    this.cold.singleStringId = DamageStringIds.ColdSingle;
    this.cold.rangeStringId = DamageStringIds.ColdRange;
    this.lightning.singleStringId = DamageStringIds.LightningSingle;
    this.lightning.rangeStringId = DamageStringIds.LightningRange;
    this.magic.singleStringId = DamageStringIds.MagicSingle;
    this.magic.rangeStringId = DamageStringIds.MagicRange;
    this.poison.singleStringId = DamageStringIds.PoisonSingle;
    this.poison.rangeStringId = DamageStringIds.PoisonRange;

    if (values === null || values === undefined) {
      return;
    }

    this.physical.min = values.getBaseStatValue(DamageStatIds.MinDamage, 0);
    if (this.physical.min === 0) {
      this.physical.min = values.getBaseStatValue(DamageStatIds.SecondaryMinDamage, 0);
    }

    this.physical.max = values.getBaseStatValue(DamageStatIds.MaxDamage, 0);
    if (this.physical.max === 0) {
      this.physical.max = values.getBaseStatValue(DamageStatIds.SecondaryMaxDamage, 0);
    }

    this.enhanced.min = values.getBaseStatValue(DamageStatIds.ItemMinDamagePercent, 0);
    this.enhanced.max = values.getBaseStatValue(DamageStatIds.ItemMaxDamagePercent, 0);

    this.fire.min = values.getBaseStatValue(DamageStatIds.FireMinDamage, 0);
    this.fire.max = values.getBaseStatValue(DamageStatIds.FireMaxDamage, 0);

    this.lightning.min = values.getBaseStatValue(DamageStatIds.LightningMinDamage, 0);
    this.lightning.max = values.getBaseStatValue(DamageStatIds.LightningMaxDamage, 0);

    this.magic.min = values.getBaseStatValue(DamageStatIds.MagicMinDamage, 0);
    this.magic.max = values.getBaseStatValue(DamageStatIds.MagicMaxDamage, 0);

    this.cold.min = values.getBaseStatValue(DamageStatIds.ColdMinDamage, 0);
    this.cold.max = values.getBaseStatValue(DamageStatIds.ColdMaxDamage, 0);

    this.poison.min = values.getBaseStatValue(DamageStatIds.PoisonMinDamage, 0);
    this.poison.max = values.getBaseStatValue(DamageStatIds.PoisonMaxDamage, 0);
    this.poisonLength = values.getBaseStatValue(DamageStatIds.PoisonLength, 0);
    this.poisonDivisor = values.getItemStatValue(DamageStatIds.PoisonLengthDivisor);

    ItemDamageAggregate.setLatch(this.physical);
    ItemDamageAggregate.setLatch(this.enhanced);
    ItemDamageAggregate.setLatch(this.cold);
    ItemDamageAggregate.setLatch(this.lightning);
    ItemDamageAggregate.setLatch(this.fire);
    ItemDamageAggregate.setLatch(this.poison);
    ItemDamageAggregate.setLatch(this.magic);
  }

  // Strictly greater than zero on BOTH halves (the seven `jle` pairs at 0x4e4b53-0x4e4bce).
  private static setLatch(pair: DamagePair): void {
    pair.bothPresent = pair.min > 0 && pair.max > 0;
  }

  /**
   * The C# `bool TryDescribe(int, out string)` pair. A string — INCLUDING the empty string —
   * means handled; null means not handled and the caller runs the DescFunc engine.
   */
  /**
   * Whether the aggregated line for this stat shows TWO numbers rather than one — "Adds 1-4 cold
   * damage" against "+175% Enhanced Damage".
   *
   * Only the enhanced-damage line is single-valued: it prints the MIN half alone and the max half
   * emits nothing at all (0x4e5aa4 returns the latch), so one roll span sits against it
   * unambiguously. A min-max line's span would belong to neither of the two numbers on it, which
   * is why those are left un-annotated.
   */
  static showsSeveralValues(statId: number): boolean {
    return statId !== DamageStatIds.ItemMinDamagePercent;
  }

  /**
   * The stats whose numbers an aggregated damage line prints, in print order — the min/max pair for
   * "Adds 1-4 cold damage", and the min half alone for "+175% Enhanced Damage". Null when the stat
   * drives no aggregated line.
   */
  static statsShownBy(statId: number): number[] | null {
    switch (statId) {
      case DamageStatIds.ItemMinDamagePercent:
        return [DamageStatIds.ItemMinDamagePercent];

      // The physical line prefers the one-hand pair and falls back to the secondary, and
      // tryDescribePhysical has already chosen by the time this is asked — so both pairs are named
      // and a stat with no span simply contributes nothing.
      case DamageStatIds.MinDamage:
      case DamageStatIds.MaxDamage:
        return [DamageStatIds.MinDamage, DamageStatIds.MaxDamage];

      case DamageStatIds.SecondaryMinDamage:
      case DamageStatIds.SecondaryMaxDamage:
        return [DamageStatIds.SecondaryMinDamage, DamageStatIds.SecondaryMaxDamage];

      case DamageStatIds.FireMinDamage:
      case DamageStatIds.FireMaxDamage:
        return [DamageStatIds.FireMinDamage, DamageStatIds.FireMaxDamage];

      case DamageStatIds.LightningMinDamage:
      case DamageStatIds.LightningMaxDamage:
        return [DamageStatIds.LightningMinDamage, DamageStatIds.LightningMaxDamage];

      case DamageStatIds.MagicMinDamage:
      case DamageStatIds.MagicMaxDamage:
        return [DamageStatIds.MagicMinDamage, DamageStatIds.MagicMaxDamage];

      case DamageStatIds.ColdMinDamage:
      case DamageStatIds.ColdMaxDamage:
        return [DamageStatIds.ColdMinDamage, DamageStatIds.ColdMaxDamage];

      // Poison prints its two damage ends and a duration, but the duration is a divisor rather than
      // a rolled magnitude, so only the pair is named.
      case DamageStatIds.PoisonMinDamage:
      case DamageStatIds.PoisonMaxDamage:
        return [DamageStatIds.PoisonMinDamage, DamageStatIds.PoisonMaxDamage];

      default:
        return null;
    }
  }

  tryDescribe(statId: number): string | null {
    switch (statId) {
      case DamageStatIds.ItemMaxDamagePercent:
        if (!this.enhanced.bothPresent) {
          return null;
        }

        return ''; // 0x4e5aa4: returns the latch, emitting nothing

      case DamageStatIds.ItemMinDamagePercent:
        if (!this.enhanced.bothPresent) {
          return null;
        }

        return (
          this.str(DescStringIds.Plus) +
          TblFormat.formatNumber(this.enhanced.min) +
          this.str(DescStringIds.Percent) +
          this.str(DescStringIds.Space) +
          this.str(DamageStringIds.EnhancedDamage)
        );

      case DamageStatIds.MinDamage:
      case DamageStatIds.MaxDamage:
      case DamageStatIds.SecondaryMinDamage:
        return this.tryDescribePhysical();

      case DamageStatIds.SecondaryMaxDamage:
        if (this.physicalEmitted || this.physical.bothPresent) {
          return '';
        }

        return null;

      case DamageStatIds.FireMinDamage:
        return this.tryDescribeElemental(this.fire);
      case DamageStatIds.FireMaxDamage:
        return ItemDamageAggregate.suppress(this.fire);

      case DamageStatIds.LightningMinDamage:
        return this.tryDescribeElemental(this.lightning);
      case DamageStatIds.LightningMaxDamage:
        return ItemDamageAggregate.suppress(this.lightning);

      case DamageStatIds.MagicMinDamage:
        return this.tryDescribeElemental(this.magic);
      case DamageStatIds.MagicMaxDamage:
        return ItemDamageAggregate.suppress(this.magic);

      case DamageStatIds.ColdMinDamage:
        return this.tryDescribeElemental(this.cold);
      case DamageStatIds.ColdMaxDamage:
        return ItemDamageAggregate.suppress(this.cold);

      case DamageStatIds.PoisonMinDamage:
        return this.tryDescribePoison();
      case DamageStatIds.PoisonMaxDamage:
      case DamageStatIds.PoisonLength:
        return ItemDamageAggregate.suppress(this.poison);

      default:
        return null;
    }
  }

  // Stateful: once the range line is emitted the latch makes every later physical stat return
  // "handled with no text" (0x4e5d06 -> 0x4e5e0a). A degenerate min >= max clears BOTH the
  // emitted flag and the pair latch (0x4e5d1a / 0x4e5d1d) and returns not-handled, which is
  // what lets stat 24 fall through to its own DescFunc line.
  private tryDescribePhysical(): string | null {
    if (this.physicalEmitted) {
      return ''; // already printed: silent skip
    }

    if (!this.physical.bothPresent) {
      return null; // fall through to the per-stat DescFunc lines
    }

    if (this.physical.min >= this.physical.max) {
      this.physicalEmitted = false;
      this.physical.bothPresent = false;
      return null;
    }

    const text = this.format(DamageStringIds.PhysicalRange, this.physical.min, this.physical.max);
    this.physicalEmitted = true;
    return text;
  }

  // min < max is a RANGE; otherwise a single value printed from the MAX, not the min — the max
  // is pushed before the comparison and is the sole argument on that path (0x4e5abf-0x4e5ac2).
  private tryDescribeElemental(pair: DamagePair): string | null {
    if (!pair.bothPresent) {
      return null;
    }

    return pair.min >= pair.max
      ? this.format(pair.singleStringId, pair.max)
      : this.format(pair.rangeStringId, pair.min, pair.max);
  }

  // Frames are divided by stat 326 (clamped to 1 and WRITTEN BACK, 0x4e5c41), the damage is
  // scaled by frames and rounded with (x + 0x80) >> 8, and the seconds argument is
  // frames / 25 truncating toward zero. The scaled values are written back too (0x4e5c92 /
  // 0x4e5c95), so a second visit to the same stat re-scales what is already scaled.
  private tryDescribePoison(): string | null {
    if (!this.poison.bothPresent) {
      return null;
    }

    if (this.poisonDivisor <= 0) {
      this.poisonDivisor = 1; // 0x4e5c41 writes the clamp back
    }

    const frames = Int32.div(this.poisonLength, this.poisonDivisor);
    const min = (Int32.mul(frames, this.poison.min) + 128) >> 8;
    const max = (Int32.mul(frames, this.poison.max) + 128) >> 8;
    const seconds = Int32.div(frames, 25);

    const text =
      min >= max
        ? this.format(this.poison.singleStringId, max, seconds)
        : this.format(this.poison.rangeStringId, min, max, seconds);

    this.poisonLength = frames;
    this.poison.min = min;
    this.poison.max = max;

    return text;
  }

  private static suppress(pair: DamagePair): string | null {
    return pair.bothPresent ? '' : null;
  }

  private format(stringId: number, ...args: unknown[]): string {
    return TblFormat.formatBounded(this.str(stringId), TblFormat.DefaultMaxLength, ...args);
  }

  private str(index: number): string {
    return this.strings.getByIndex(index) ?? '';
  }
}

export class UndeadDamageLine {
  // itemtypes.txt row 57 "Blunt". IsOfType (0x629bb0) probes a precomputed Equiv1/Equiv2
  // closure matrix, so the whole subtree qualifies: Club 29, Hammer 31, Mace 36, and via
  // "Staves And Rods" 55 also Scepter 24, WAND 25 and STAFF 26. A miss on the items.txt first
  // type is retried against the second (0x629c27 onwards) — implementors of IsItemOfType must
  // test both or they drop the line.
  static readonly BluntItemType = 57;

  static readonly InherentPercent = 50;

  static build(
    strings: IStringTable | null | undefined,
    values: IStatValueSource | null | undefined,
    isMainStatBlock: boolean,
  ): string | null {
    if (strings === null || strings === undefined) throw new Error('strings');

    if (!isMainStatBlock) {
      return null; // 0x4e61d7: set-bonus blocks never repeat this line
    }

    if (
      values === null ||
      values === undefined ||
      !values.isItemOfType(UndeadDamageLine.BluntItemType)
    ) {
      return null;
    }

    if (values.getItemStatValue(DamageStatIds.UndeadDamagePercent) !== 0) {
      return null;
    }

    return (
      UndeadDamageLine.nz(strings.getByIndex(DescStringIds.Plus)) +
      String(UndeadDamageLine.InherentPercent) +
      UndeadDamageLine.nz(strings.getByIndex(DescStringIds.Percent)) +
      UndeadDamageLine.nz(strings.getByIndex(DescStringIds.Space)) +
      UndeadDamageLine.nz(strings.getByIndex(DamageStringIds.DamageToUndead)) +
      UndeadDamageLine.nz(strings.getByIndex(DescStringIds.Newline))
    );
  }

  private static nz(text: string | null): string {
    return text ?? '';
  }
}
