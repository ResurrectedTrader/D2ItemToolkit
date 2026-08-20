import { describe as suite, expect, it } from 'vitest';
import {
  DamageStatIds,
  DamageStringIds,
  ItemDamageAggregate,
  UndeadDamageLine,
} from '../../../src/D2ItemToolkit.Ts/src/Tooltip/ItemDamageLines.js';
import { ItemDescriptionGenerator } from '../../../src/D2ItemToolkit.Ts/src/Description/ItemDescription.js';
import { FakeStatCostTable, FakeStatValues, FakeStringTable } from '../Fakes.js';

// =================================================================
// The undead line's guard reads one accessor and no other
// =================================================================

function undeadStrings(): FakeStringTable {
  return new FakeStringTable()
    .withPunctuation()
    .add(DamageStringIds.DamageToUndead, 'Damage to Undead');
}

suite('the undead damage line', () => {
  it('reads the items own stat list', () => {
    // 0x4e61ea: ITEM_GetMinimalStatValueShifted on the described item.
    const stats = new FakeStatCostTable();
    const values = new FakeStatValues().addItemType(UndeadDamageLine.BluntItemType);
    values.addItemStat(DamageStatIds.UndeadDamagePercent, 5);

    expect(new ItemDescriptionGenerator(stats, undeadStrings(), values).describe([])).toEqual([]);
  });

  it('ignores the viewers stats', () => {
    // A regression twice over: reading the player's list here suppresses the line on
    // any character wearing anything with stat 122.
    const stats = new FakeStatCostTable();
    const values = new FakeStatValues().addItemType(UndeadDamageLine.BluntItemType);
    values.addPlayer(DamageStatIds.UndeadDamagePercent, 5);

    expect(new ItemDescriptionGenerator(stats, undeadStrings(), values).describe([])).toHaveLength(
      1,
    );
  });

  it('ignores the merged list', () => {
    // A socketed gem's stat 122 must not suppress it either.
    const stats = new FakeStatCostTable();
    const values = new FakeStatValues().addItemType(UndeadDamageLine.BluntItemType);
    values.addBase(DamageStatIds.UndeadDamagePercent, 5);

    expect(new ItemDescriptionGenerator(stats, undeadStrings(), values).describe([])).toHaveLength(
      1,
    );
  });

  it('is suppressed outside the main stat block', () => {
    // 0x4e61d0: the caller flag. Set-bonus blocks pass 0.
    const stats = new FakeStatCostTable();
    const values = new FakeStatValues().addItemType(UndeadDamageLine.BluntItemType);

    const generator = new ItemDescriptionGenerator(
      stats,
      undeadStrings(),
      values,
      null,
      null,
      null,
      null,
      false,
    );

    expect(generator.describe([])).toEqual([]);
  });

  it('arrives pre joined and carries the inherent percent', () => {
    const stats = new FakeStatCostTable();
    const values = new FakeStatValues().addItemType(UndeadDamageLine.BluntItemType);

    const lines = new ItemDescriptionGenerator(stats, undeadStrings(), values).describe([]);

    expect(lines[0]!.text).toBe('+50% Damage to Undead\n');
    expect(lines[0]!.preJoined).toBe(true);
    expect(lines[0]!.statId).toBe(DamageStatIds.UndeadDamagePercent);
    expect(lines[0]!.value).toBe(UndeadDamageLine.InherentPercent);
  });

  it('is absent for an item that is not blunt', () => {
    const stats = new FakeStatCostTable();

    expect(
      new ItemDescriptionGenerator(stats, undeadStrings(), new FakeStatValues()).describe([]),
    ).toEqual([]);
  });
});

// =================================================================
// Damage aggregation core
// =================================================================

function damageStrings(): FakeStringTable {
  // Distinctive shapes so argument ORDER and COUNT are both observable.
  return new FakeStringTable()
    .withPunctuation()
    .add(DamageStringIds.FireSingle, 'F1[%d]')
    .add(DamageStringIds.FireRange, 'FR[%d|%d]')
    .add(DamageStringIds.PoisonSingle, 'P1[%d~%d]')
    .add(DamageStringIds.PoisonRange, 'PR[%d|%d~%d]')
    .add(DamageStringIds.PhysicalRange, 'PH[%d|%d]')
    .add(DamageStringIds.EnhancedDamage, 'Enhanced Damage');
}

function aggregate(values: FakeStatValues): ItemDamageAggregate {
  return new ItemDamageAggregate(damageStrings(), values);
}

suite('damage aggregation core', () => {
  it('formats a single value elemental line from the max not the min', () => {
    // 0x4e5ac2: the max is pushed before the comparison branch and is the only
    // argument on the single-value path. min=10 max=5 makes the two distinguishable.
    const values = new FakeStatValues()
      .addBase(DamageStatIds.FireMinDamage, 10)
      .addBase(DamageStatIds.FireMaxDamage, 5);

    expect(aggregate(values).tryDescribe(DamageStatIds.FireMinDamage)).toBe('F1[5]');
  });

  it('gives the poison strings an asymmetric trailing seconds argument', () => {
    // 0x4e5c2d: single is (max, seconds) with add esp,14h; range is
    // (min, max, seconds) with add esp,18h. length 25 / divisor 1 = 25 frames = 1s.
    // (25*256+128)>>8 = 25 and (25*512+128)>>8 = 50.
    const range = new FakeStatValues()
      .addBase(DamageStatIds.PoisonMinDamage, 256)
      .addBase(DamageStatIds.PoisonMaxDamage, 512)
      .addBase(DamageStatIds.PoisonLength, 25);

    expect(aggregate(range).tryDescribe(DamageStatIds.PoisonMinDamage)).toBe('PR[25|50~1]');

    const single = new FakeStatValues()
      .addBase(DamageStatIds.PoisonMinDamage, 512)
      .addBase(DamageStatIds.PoisonMaxDamage, 256)
      .addBase(DamageStatIds.PoisonLength, 25);

    // max, then seconds
    expect(aggregate(single).tryDescribe(DamageStatIds.PoisonMinDamage)).toBe('P1[25~1]');
  });

  it('clamps a non positive poison divisor to one', () => {
    // 0x4e5c39 jg keeps a divisor > 0; anything else has 1 written back.
    const values = new FakeStatValues()
      .addBase(DamageStatIds.PoisonMinDamage, 256)
      .addBase(DamageStatIds.PoisonMaxDamage, 512)
      .addBase(DamageStatIds.PoisonLength, 25);
    values.addItemStat(DamageStatIds.PoisonLengthDivisor, -5);

    // as if the divisor were 1
    expect(aggregate(values).tryDescribe(DamageStatIds.PoisonMinDamage)).toBe('PR[25|50~1]');
  });

  it('takes the poison divisor from the items own list', () => {
    // 0x4e4adf reads stat 326 off the described item, not the merged list. (0x4e4ad8 is
    // `push edx`, the unit argument; 0x4e4ae4 is the store of the returned divisor.)
    const values = new FakeStatValues()
      .addBase(DamageStatIds.PoisonMinDamage, 256)
      .addBase(DamageStatIds.PoisonMaxDamage, 512)
      .addBase(DamageStatIds.PoisonLength, 50)
      .addBase(DamageStatIds.PoisonLengthDivisor, 999); // merged list: ignored
    values.addItemStat(DamageStatIds.PoisonLengthDivisor, 2);

    // 50/2 = 25 frames
    expect(aggregate(values).tryDescribe(DamageStatIds.PoisonMinDamage)).toBe('PR[25|50~1]');
  });

  it('collects poison length even though its descfunc is blank', () => {
    // COLLECTION and EMISSION use different arrays, and only emission is descfunc-filtered.
    // SKILLDESC_BuildStatListDesc walks a table compiled into the binary — 0x4e49e3 loads
    // `offset unk_72CDD0` as an immediate, bounded by dword_72CDCC = 143, stride 0x10 —
    // whose rows select collector arms by stat id alone. Stat 59's poison-length read
    // (0x4e4ad9) and stat 326's divisor read (0x4e4ae4) are therefore LIVE, even though
    // poisonlength's descfunc cell is blank in shipped 1.14d data and it never appears in
    // the emission array (a pointer read at 0x4e6240, built by the descfunc filter at
    // 0x638530).
    //
    // Pinned because a comment here once said 0x72CDD0 "is not what is loaded", which reads
    // as a licence to drop those two reads. Doing so makes frames 0, so every poison item
    // in the game prints "+0 poison damage over 0 seconds" via string 3620 instead of its
    // real range via 3621.
    const values = new FakeStatValues()
      .addBase(DamageStatIds.PoisonMinDamage, 256)
      .addBase(DamageStatIds.PoisonMaxDamage, 512)
      .addBase(DamageStatIds.PoisonLength, 25);
    values.addItemStat(DamageStatIds.PoisonLengthDivisor, 1);

    // frames = 25/1; min = (25*256+128)>>8 = 25; max = (25*512+128)>>8 = 50; secs = 25/25.
    expect(aggregate(values).tryDescribe(DamageStatIds.PoisonMinDamage)).toBe('PR[25|50~1]');
  });

  it('prints the enhanced damage line from the min percent and suppresses the max', () => {
    // jpt_4E4A11: stat 18 is the value formatted at 0x4e5d8f; stat 17 only gates it.
    const values = new FakeStatValues()
      .addBase(DamageStatIds.ItemMinDamagePercent, 30)
      .addBase(DamageStatIds.ItemMaxDamagePercent, 70);

    const agg = aggregate(values);

    expect(agg.tryDescribe(DamageStatIds.ItemMinDamagePercent)).toBe('+30% Enhanced Damage');

    // handled, emits nothing
    expect(agg.tryDescribe(DamageStatIds.ItemMaxDamagePercent)).toBe('');
  });

  it('clears both latches on a degenerate physical range', () => {
    // 0x4e5d1a and 0x4e5d1d clear slot 5 AND slot 4.
    //
    // This drives the state machine directly. The clear is unobservable in practice
    // because min/max are never written back, so the 0x4e5d16 comparison is idempotent —
    // a later visit reaches the same `return 0` whether or not the slot was cleared — and
    // case 24 (descpriority 123) has already been consumed before 23/22/21 (124/126/127)
    // in the ascending walk. The pin is still worth keeping: it fixes the transition
    // itself, which is what a refactor would break.
    const values = new FakeStatValues()
      .addBase(DamageStatIds.MinDamage, 10)
      .addBase(DamageStatIds.MaxDamage, 10);

    const agg = aggregate(values);

    expect(agg.tryDescribe(DamageStatIds.MinDamage)).toBeNull();
    expect(agg.tryDescribe(DamageStatIds.SecondaryMaxDamage)).toBeNull();
  });

  it('suppresses every later damage stat once the physical range is printed', () => {
    const values = new FakeStatValues()
      .addBase(DamageStatIds.MinDamage, 5)
      .addBase(DamageStatIds.MaxDamage, 10);

    const agg = aggregate(values);

    expect(agg.tryDescribe(DamageStatIds.MinDamage)).toBe('PH[5|10]');

    for (const later of [
      DamageStatIds.MaxDamage,
      DamageStatIds.SecondaryMinDamage,
      DamageStatIds.SecondaryMaxDamage,
    ]) {
      expect(agg.tryDescribe(later)).toBe('');
    }
  });

  it('falls back to the secondary stats for physical damage', () => {
    // 0x4e4aff / 0x4e4b1c: stat 21 falls back to 23, stat 22 to 24.
    const values = new FakeStatValues()
      .addBase(DamageStatIds.SecondaryMinDamage, 5)
      .addBase(DamageStatIds.SecondaryMaxDamage, 10);

    expect(aggregate(values).tryDescribe(DamageStatIds.MinDamage)).toBe('PH[5|10]');
  });

  it('does not handle a half present pair at all', () => {
    // 0x4e4b53: strictly > 0 on BOTH halves. A lone half must fall through to its own
    // DescFunc line rather than being silently swallowed.
    const values = new FakeStatValues().addBase(DamageStatIds.FireMinDamage, 10);

    const agg = aggregate(values);

    expect(agg.tryDescribe(DamageStatIds.FireMinDamage)).toBeNull();
    expect(agg.tryDescribe(DamageStatIds.FireMaxDamage)).toBeNull();
  });

  it('leaves a stat outside the damage set to the DescFunc engine', () => {
    expect(aggregate(new FakeStatValues()).tryDescribe(39)).toBeNull();
  });
});
