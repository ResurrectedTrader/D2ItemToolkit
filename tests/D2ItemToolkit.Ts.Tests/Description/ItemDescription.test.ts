import { describe as suite, expect, it } from 'vitest';
import {
  ByTimeValue,
  ItemDescFunc,
  ItemDescriptionGenerator,
  ItemDescriptionLine,
  PeriodOfDay,
  TblFormat,
  type ItemDescriptionLine as Line,
} from '../../../src/D2ItemToolkit.Ts/src/Description/ItemDescription.js';
import { DescStringIds, type IGameTimeProvider } from '../../../src/D2ItemToolkit.Ts/src/Types.js';
import {
  Build,
  ByTime,
  FakeClassTable,
  FakeGameTime,
  FakeMonsterTable,
  FakeSkillTable,
  FakeStatCostTable,
  FakeStatValues,
  FakeStringTable,
} from '../Fakes.js';

/**
 * Behaviour is asserted against the disassembly rather
 * than from community DescFunc tables. Where a test looks surprising it is usually
 * because the community table is wrong; those cases name the address.
 */

function gen(
  stats: FakeStatCostTable,
  strings: FakeStringTable,
  values: FakeStatValues | null = null,
  skills: FakeSkillTable | null = null,
  classes: FakeClassTable | null = null,
  monsters: FakeMonsterTable | null = null,
  byTime: IGameTimeProvider | null = null,
): ItemDescriptionGenerator {
  return new ItemDescriptionGenerator(stats, strings, values, skills, classes, monsters, byTime);
}

function one(
  generator: ItemDescriptionGenerator,
  ...entries: readonly (readonly [number, number])[]
): string {
  const lines = generator.describe(entries);
  expect(lines).toHaveLength(1);
  return lines[0]!.text;
}

/**
 * The engine returned success but produced no text: the caller appends the empty
 * buffer, so this is a blank tooltip row, not an absent line.
 */
function assertBlank(
  generator: ItemDescriptionGenerator,
  ...entries: readonly (readonly [number, number])[]
): void {
  const lines = generator.describe(entries);
  expect(lines).toHaveLength(1);
  expect(lines[0]!.isBlank).toBe(true);
}

function all(
  generator: ItemDescriptionGenerator,
  ...entries: readonly (readonly [number, number])[]
): readonly Line[] {
  return generator.describe(entries);
}

function texts(lines: readonly Line[]): string[] {
  return lines.map(l => l.text);
}

// =================================================================
// Guard clauses
// =================================================================

suite('guard clauses', () => {
  it('ctor rejects a null stat table', () => {
    expect(() => new ItemDescriptionGenerator(null, new FakeStringTable())).toThrow();
  });

  it('ctor rejects a null string table', () => {
    expect(() => new ItemDescriptionGenerator(new FakeStatCostTable(), null)).toThrow();
  });

  it('describe rejects null stats', () => {
    expect(() => gen(new FakeStatCostTable(), new FakeStringTable()).describe(null)).toThrow();
  });

  it('join rejects null lines', () => {
    expect(() => gen(new FakeStatCostTable(), new FakeStringTable()).join(null)).toThrow();
  });
});

// =================================================================
// Selection, suppression and ordering
// =================================================================

suite('selection, suppression and ordering', () => {
  it('stats print in the order the table hands them back', () => {
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(1, ItemDescFunc.PlusValueString, 100, { priority: 10 }));
    stats.add(Build.stat(2, ItemDescFunc.PlusValueString, 101, { priority: 90 }));

    const strings = new FakeStringTable()
      .withPunctuation()
      .add(100, 'to Strength')
      .add(101, 'to Energy');

    const lines = all(gen(stats, strings), Build.entry(2, 5), Build.entry(1, 10));

    expect(texts(lines)).toEqual(['+10 to Strength', '+5 to Energy']);
    expect(lines[0]!.descPriority).toBe(10);
  });

  it('a zero valued stat is skipped', () => {
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(1, ItemDescFunc.PlusValueString, 100));

    expect(
      all(
        gen(stats, new FakeStringTable().withPunctuation().add(100, 'to Strength')),
        Build.entry(1, 0),
      ),
    ).toEqual([]);
  });

  it('a stat absent from the item is skipped', () => {
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(1, ItemDescFunc.PlusValueString, 100));
    stats.add(Build.stat(2, ItemDescFunc.PlusValueString, 101));

    const strings = new FakeStringTable()
      .withPunctuation()
      .add(100, 'to Strength')
      .add(101, 'to Energy');

    expect(one(gen(stats, strings), Build.entry(1, 10))).toBe('+10 to Strength');
  });

  it('a stat with no table row is skipped', () => {
    const stats = new FakeStatCostTable();
    stats.addMissing(7);

    expect(all(gen(stats, new FakeStringTable()), Build.entry(7, 10))).toEqual([]);
  });

  it('a stat with desc func zero is skipped', () => {
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(72, 0, 100)); // durability: the tooltip prints it elsewhere

    expect(
      all(gen(stats, new FakeStringTable().add(100, 'Durability')), Build.entry(72, 40)),
    ).toEqual([]);
  });

  it('secondary min damage is suppressed when min damage is present', () => {
    // SKILLDESC_BuildStatBuffDesc 0x4e62d2.
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(21, ItemDescFunc.PlusValueString, 100));
    stats.add(Build.stat(23, ItemDescFunc.PlusValueString, 101));

    const strings = new FakeStringTable()
      .withPunctuation()
      .add(100, 'Min Damage')
      .add(101, 'Secondary Min Damage');

    const values = new FakeStatValues().addBase(21, 5);

    const lines = all(gen(stats, strings, values), Build.entry(21, 5), Build.entry(23, 7));

    expect(texts(lines)).toEqual(['+5 Min Damage']);
  });

  it('secondary max damage is suppressed when max damage is present', () => {
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(22, ItemDescFunc.PlusValueString, 100));
    stats.add(Build.stat(24, ItemDescFunc.PlusValueString, 101));

    const strings = new FakeStringTable()
      .withPunctuation()
      .add(100, 'Max Damage')
      .add(101, 'Secondary Max Damage');

    const values = new FakeStatValues().addBase(22, 5);

    expect(all(gen(stats, strings, values), Build.entry(22, 5), Build.entry(24, 7))).toHaveLength(
      1,
    );
  });

  it('secondary damage prints when the primary is absent', () => {
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(23, ItemDescFunc.PlusValueString, 101));

    const strings = new FakeStringTable().withPunctuation().add(101, 'Secondary Min Damage');

    expect(one(gen(stats, strings), Build.entry(23, 7))).toBe('+7 Secondary Min Damage');
  });

  it('a stat present at several layers prints once per layer in layer order', () => {
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(107, ItemDescFunc.Skill, 0));

    const strings = new FakeStringTable().withPunctuation();
    const skills = new FakeSkillTable().add(1, 'Fire Bolt').add(2, 'Teleport');

    const lines = all(
      gen(stats, strings, null, skills),
      Build.entry(107, 3, 2),
      Build.entry(107, 1, 1),
    );

    expect(texts(lines)).toEqual(['+1 to Fire Bolt', '+3 to Teleport']);
  });

  it('an empty stat set yields no lines', () => {
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(1, ItemDescFunc.PlusValueString, 100));

    expect(gen(stats, new FakeStringTable()).describe([])).toEqual([]);
  });

  it('join separates lines the way the game does', () => {
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(1, ItemDescFunc.PlusValueString, 100));
    stats.add(Build.stat(2, ItemDescFunc.PlusValueString, 101));

    const strings = new FakeStringTable()
      .withPunctuation()
      .add(100, 'to Strength')
      .add(101, 'to Energy');

    const generator = gen(stats, strings);
    const lines = all(generator, Build.entry(1, 10), Build.entry(2, 5));

    // Inline mode is the default and what the item tooltip uses: 3998 after every
    // line, no separator.
    expect(generator.join(lines)).toBe('+10 to Strength\n+5 to Energy\n');

    // Block mode is the other shape, for callers that pass arg_14 == 0: 3852 + 3995
    // before each line after the first, nothing terminating the last.
    expect(generator.join(lines, false)).toBe('+10 to Strength\n +5 to Energy');
  });

  it('join of nothing is empty', () => {
    expect(gen(new FakeStatCostTable(), new FakeStringTable().withPunctuation()).join([])).toBe('');
  });
});

// =================================================================
// DescVal placement
// =================================================================

suite('DescVal placement', () => {
  it.each([
    [1, '+10 to Strength'],
    [2, 'to Strength +10'],
  ])('desc val %i decides where the number goes', (descVal, expected) => {
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(1, ItemDescFunc.PlusValueString, 100, { descVal }));

    expect(
      one(
        gen(stats, new FakeStringTable().withPunctuation().add(100, 'to Strength')),
        Build.entry(1, 10),
      ),
    ).toBe(expected);
  });

  it.each([[0], [7]])('desc func 1 with desc val %i yields a blank row', descVal => {
    // 0x4e4f5d: eight arms leave the buffer empty rather than copying the string.
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(1, ItemDescFunc.PlusValueString, 100, { descVal }));

    assertBlank(
      gen(stats, new FakeStringTable().withPunctuation().add(100, 'to Strength')),
      Build.entry(1, 10),
    );
  });
});

// =================================================================
// Strings, signs and value computation
// =================================================================

suite('strings, signs and value computation', () => {
  it('a negative value uses the negative string', () => {
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(1, ItemDescFunc.ValueString, 100, { strNeg: 101 }));

    const strings = new FakeStringTable()
      .withPunctuation()
      .add(100, 'Faster Cast Rate')
      .add(101, 'Slower Cast Rate');

    const generator = gen(stats, strings);
    expect(one(generator, Build.entry(1, -10))).toBe('-10 Slower Cast Rate');
    expect(one(generator, Build.entry(1, 10))).toBe('10 Faster Cast Rate');
  });

  it('a negative value does not fall back to the positive string', () => {
    // 0x4e4e43 selects DescStrNeg unconditionally. A blank DescStrNeg means a blank
    // text part, not a reuse of DescStrPos.
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(1, ItemDescFunc.ValueString, 100));

    expect(
      one(
        gen(stats, new FakeStringTable().withPunctuation().add(100, 'to Strength')),
        Build.entry(1, -10),
      ),
    ).toBe('-10 ');
  });

  it('the plus sign appears only for a strictly positive value', () => {
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(1, ItemDescFunc.PlusValueString, 100));

    const generator = gen(stats, new FakeStringTable().withPunctuation().add(100, 'to Strength'));

    expect(one(generator, Build.entry(1, 10))).toBe('+10 to Strength');
    expect(one(generator, Build.entry(1, -10))).toBe('-10 ');
  });

  it('a stat with no string index still prints its number', () => {
    // Str(0) resolves to an empty entry, not a missing one, and the engine emits the
    // number and separator regardless.
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(1, ItemDescFunc.PlusValueString, 0));

    expect(one(gen(stats, new FakeStringTable().withPunctuation()), Build.entry(1, 10))).toBe(
      '+10 ',
    );
  });

  it('a string index the table does not have still prints its number', () => {
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(1, ItemDescFunc.PlusValueString, 999));

    expect(one(gen(stats, new FakeStringTable().withPunctuation()), Build.entry(1, 10))).toBe(
      '+10 ',
    );
  });

  it('valShift scales the displayed value', () => {
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(7, ItemDescFunc.PlusValueString, 100, { valShift: 8 }));

    const lines = all(
      gen(stats, new FakeStringTable().withPunctuation().add(100, 'to Life')),
      Build.entry(7, 40 << 8),
    );

    expect(lines[0]!.text).toBe('+40 to Life');
    expect(lines[0]!.value).toBe(40);
  });

  it.each([[2], [3], [4], [5]])('op %i scales the value against a player stat', op => {
    // SKILLDESC_CalcStatGroupValue 0x4e4cad.
    const stats = new FakeStatCostTable();
    const perLevel = Build.stat(1, ItemDescFunc.PlusValueString, 100);
    perLevel.op = op;
    perLevel.opParam = 3; // >> 3
    perLevel.opBase = 12; // character level
    stats.add(perLevel);
    stats.add(Build.stat(12, 0, 0)); // the op base row, ValShift 0

    const values = new FakeStatValues().addPlayer(12, 40);
    const strings = new FakeStringTable().withPunctuation().add(100, 'to Life');

    // (2 * 40) >> 3 = 10
    expect(one(gen(stats, strings, values), Build.entry(1, 2))).toBe('+10 to Life');
  });

  it('op scaling honours the op base val shift', () => {
    const stats = new FakeStatCostTable();
    const perLevel = Build.stat(1, ItemDescFunc.PlusValueString, 100);
    perLevel.op = 2;
    perLevel.opParam = 0;
    perLevel.opBase = 12;
    stats.add(perLevel);
    stats.add(Build.stat(12, 0, 0, { valShift: 2 })); // player stat is in quarters

    const values = new FakeStatValues().addPlayer(12, 40);

    // 2 * (40 >> 2) = 20
    expect(
      one(
        gen(stats, new FakeStringTable().withPunctuation().add(100, 'to Life'), values),
        Build.entry(1, 2),
      ),
    ).toBe('+20 to Life');
  });

  it('op scaling yields zero when there is no value source', () => {
    const stats = new FakeStatCostTable();
    const perLevel = Build.stat(1, ItemDescFunc.PlusValueString, 100);
    perLevel.op = 2;
    perLevel.opBase = 12;
    stats.add(perLevel);
    stats.add(Build.stat(12, 0, 0));

    // GetStatUnsignedValue returns 0 for a null unit, so the multiply still happens
    // and yields 0. DescFunc 1 uses the strict sign test, so zero gets no plus.
    expect(
      one(
        gen(stats, new FakeStringTable().withPunctuation().add(100, 'to Life')),
        Build.entry(1, 2),
      ),
    ).toBe('0 to Life');
  });

  it('op scaling yields zero when the op base row is missing', () => {
    const stats = new FakeStatCostTable();
    const perLevel = Build.stat(1, ItemDescFunc.PlusValueString, 100);
    perLevel.op = 2;
    perLevel.opBase = 999;
    stats.add(perLevel);

    const values = new FakeStatValues().addPlayer(999, 40);

    // 0x4e4c88 returns 0 outright for an out-of-range op base.
    expect(
      one(
        gen(stats, new FakeStringTable().withPunctuation().add(100, 'to Life'), values),
        Build.entry(1, 2),
      ),
    ).toBe('0 to Life');
  });

  it.each([[0], [1], [6], [13]])('op %i does not scale', op => {
    const stats = new FakeStatCostTable();
    const descriptor = Build.stat(1, ItemDescFunc.PlusValueString, 100);
    descriptor.op = op;
    descriptor.opParam = 3;
    descriptor.opBase = 12;
    stats.add(descriptor);
    stats.add(Build.stat(12, 0, 0));

    const values = new FakeStatValues().addPlayer(12, 40);

    expect(
      one(
        gen(stats, new FakeStringTable().withPunctuation().add(100, 'to Life'), values),
        Build.entry(1, 2),
      ),
    ).toBe('+2 to Life');
  });

  it('missing punctuation strings degrade to unseparated text', () => {
    // The sign and separator come from the .tbl, so an incomplete table runs the
    // pieces together rather than throwing. A real MPQ always has 3995/4001/4002.
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(1, ItemDescFunc.PlusValueString, 100));

    expect(one(gen(stats, new FakeStringTable().add(100, 'to Strength')), Build.entry(1, 10))).toBe(
      '10to Strength',
    );
  });
});

// =================================================================
// Every DescFunc
// =================================================================

suite('every DescFunc', () => {
  it.each([
    [ItemDescFunc.PlusValueString, 10, '+10 String'],
    [ItemDescFunc.PlusValueString, -10, '-10 '],
    [ItemDescFunc.ValuePercentString, 10, '10% String'],
    [ItemDescFunc.ValueString, 10, '10 String'],
    [ItemDescFunc.PlusValuePercentString, 10, '+10% String'],
    [ItemDescFunc.PlusValuePercentString, -10, '-10% '],
    [ItemDescFunc.ValueFramesPercentString, 128, '100% String'],
    [ItemDescFunc.ValueFramesPercentString, 64, '50% String'],
    [ItemDescFunc.StaleNegated25, 10, '10 String'],
    [ItemDescFunc.StaleNegated26, 10, '10 String'],
  ])('simple desc func %i with %i', (func, value, expected) => {
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(1, func, 100));

    expect(
      one(
        gen(stats, new FakeStringTable().withPunctuation().add(100, 'String')),
        Build.entry(1, value),
      ),
    ).toBe(expected);
  });

  it.each([
    [ItemDescFunc.NegatedValuePercentString, -25, '+25% '],
    [ItemDescFunc.NegatedValuePercentString, 25, '-25% String'],
  ])('desc func 20 negates and keeps the percent (%i, %i)', (func, value, expected) => {
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(1, func, 100));

    expect(
      one(
        gen(stats, new FakeStringTable().withPunctuation().add(100, 'String')),
        Build.entry(1, value),
      ),
    ).toBe(expected);
  });

  it('desc func 21 also emits a percent despite what the community table says', () => {
    // 20 and 21 both fall into the 4/8 path at 0x4e5031.
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(1, ItemDescFunc.NegatedValuePercentStringString2, 100));

    // The string is selected from the ORIGINAL negative value, so DescStrNeg (blank
    // here) applies; and 0x4e5948 emits the DescStr2 separator with no zero check.
    expect(
      one(
        gen(stats, new FakeStringTable().withPunctuation().add(100, 'String')),
        Build.entry(1, -25),
      ),
    ).toBe('+25%  ');
  });

  it('desc func 12 prints the string alone when the value is one', () => {
    // 0x4e4f05: DescFunc 12 with a value of exactly 1 omits the number.
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(1, ItemDescFunc.PlusValueStringSuppressOne, 100));

    const generator = gen(
      stats,
      new FakeStringTable().withPunctuation().add(100, 'Indestructible'),
    );

    expect(one(generator, Build.entry(1, 1))).toBe(' Indestructible');
    expect(one(generator, Build.entry(1, 2))).toBe('+2 Indestructible');
  });

  it.each([
    [ItemDescFunc.PlusValueStringString2, 10, '+10 String Second'],
    [ItemDescFunc.ValuePercentStringString2, 10, '10% String Second'],
    [ItemDescFunc.PlusValuePercentStringString2, 10, '+10% String Second'],
    [ItemDescFunc.ValueStringString2, 10, '10 String Second'],
    [ItemDescFunc.ValueFramesPercentStringString2, 128, '100% String Second'],
    [ItemDescFunc.NegatedValuePercentStringString2, -10, '+10%  Second'],
  ])('the desc funcs that take a second string (%i)', (func, value, expected) => {
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(1, func, 100, { str2: 101 }));

    const strings = new FakeStringTable().withPunctuation().add(100, 'String').add(101, 'Second');

    expect(one(gen(stats, strings), Build.entry(1, value))).toBe(expected);
  });

  it.each([
    [ItemDescFunc.PlusValueString],
    [ItemDescFunc.ValuePercentString],
    [ItemDescFunc.RawFormat],
  ])('desc func %i outside 6 to 10 and 21 ignores its second string', func => {
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(1, func, 100, { str2: 101 }));

    const strings = new FakeStringTable().withPunctuation().add(100, 'String').add(101, 'Second');

    expect(one(gen(stats, strings), Build.entry(1, 10))).not.toContain('Second');
  });

  it('a second string of 5382 is replaced by string 11091', () => {
    const stats = new FakeStatCostTable();
    stats.add(
      Build.stat(1, ItemDescFunc.ValueStringString2, 100, { str2: DescStringIds.DescStr2Sentinel }),
    );

    const strings = new FakeStringTable()
      .withPunctuation()
      .add(100, 'String')
      .add(DescStringIds.DescStr2Override, 'Replacement');

    expect(one(gen(stats, strings), Build.entry(1, 10))).toBe('10 String Replacement');
  });

  it('a second string index of zero is omitted', () => {
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(1, ItemDescFunc.ValueStringString2, 100, { str2: 0 }));

    expect(
      one(
        gen(stats, new FakeStringTable().withPunctuation().add(100, 'String')),
        Build.entry(1, 10),
      ),
    ).toBe('10 String ');
  });

  it('a second string the table does not have is omitted', () => {
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(1, ItemDescFunc.ValueStringString2, 100, { str2: 999 }));

    expect(
      one(
        gen(stats, new FakeStringTable().withPunctuation().add(100, 'String')),
        Build.entry(1, 10),
      ),
    ).toBe('10 String ');
  });

  it.each([
    [-5], // value <= 0
    [4], // 2500/4 = 625 > 30 -> per second string
    [100], // 2500/100 = 25, not > 30
  ])('desc func 11 always produces a repair line (%i)', value => {
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(252, ItemDescFunc.RepairDurability, 100));

    const strings = new FakeStringTable()
      .withPunctuation()
      .add(DescStringIds.RepairSingleCount, 'Repairs %d Durability in 25 Seconds')
      .add(DescStringIds.RepairCountAndSeconds, 'Repairs %d Durability per Second');

    expect(one(gen(stats, strings), Build.entry(252, value))).toMatch(/^Repairs /);
  });

  it('desc func 11 uses 25 for a non positive rate', () => {
    // A stored zero never reaches the formatter, so the non-positive arm is only
    // reachable via a negative value or one that shifts down to zero.
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(252, ItemDescFunc.RepairDurability, 100, { valShift: 4 }));

    const strings = new FakeStringTable()
      .withPunctuation()
      .add(DescStringIds.RepairSingleCount, 'Repairs %d Durability');

    expect(one(gen(stats, strings), Build.entry(252, 1))).toBe('Repairs 25 Durability');
  });

  it('desc func 11 switches string when the rate exceeds thirty seconds', () => {
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(252, ItemDescFunc.RepairDurability, 100));

    const strings = new FakeStringTable()
      .withPunctuation()
      .add(DescStringIds.RepairSingleCount, 'SLOW %d')
      .add(DescStringIds.RepairCountAndSeconds, 'FAST %d');

    // 2500/4 = 625 > 30
    expect(one(gen(stats, strings), Build.entry(252, 4))).toBe('FAST 1');
    // 2500/100 = 25, not > 30
    expect(one(gen(stats, strings), Build.entry(252, 100))).toBe('SLOW 1');
  });

  it('desc func 13 reads charstats and ignores desc str pos', () => {
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(83, ItemDescFunc.ClassAllSkills, 100));

    const strings = new FakeStringTable().withPunctuation().add(100, 'IGNORED');
    const classes = new FakeClassTable().addAllSkills(3, 'to Paladin Skill Levels');

    expect(one(gen(stats, strings, null, null, classes), Build.entry(83, 2, 3))).toBe(
      '+2 to Paladin Skill Levels',
    );
  });

  it('desc func 13 honours desc val 2', () => {
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(83, ItemDescFunc.ClassAllSkills, 100, { descVal: 2 }));

    const classes = new FakeClassTable().addAllSkills(3, 'Paladin Skill Levels');

    expect(
      one(
        gen(stats, new FakeStringTable().withPunctuation(), null, null, classes),
        Build.entry(83, 2, 3),
      ),
    ).toBe('Paladin Skill Levels +2');
  });

  it('desc func 13 drops a zero value', () => {
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(83, ItemDescFunc.ClassAllSkills, 100));

    const classes = new FakeClassTable().addAllSkills(3, 'to Paladin Skill Levels');

    // A zero entry never reaches the formatter, so drive it through a shift instead.
    const shifted = Build.stat(84, ItemDescFunc.ClassAllSkills, 100, { valShift: 8 });
    stats.add(shifted);

    expect(
      all(
        gen(stats, new FakeStringTable().withPunctuation(), null, null, classes),
        Build.entry(84, 1, 3),
      ),
    ).toEqual([]);
  });

  it('desc func 13 drops the line with no class table', () => {
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(83, ItemDescFunc.ClassAllSkills, 100));

    expect(all(gen(stats, new FakeStringTable().withPunctuation()), Build.entry(83, 2, 3))).toEqual(
      [],
    );
  });

  it('desc func 13 drops the line for an unknown class', () => {
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(83, ItemDescFunc.ClassAllSkills, 100));

    const classes = new FakeClassTable().addAllSkills(3, 'to Paladin Skill Levels');

    expect(
      all(
        gen(stats, new FakeStringTable().withPunctuation(), null, null, classes),
        Build.entry(83, 2, 5),
      ),
    ).toEqual([]);
  });

  it('desc func 14 unpacks the class from the layer not a flat tab id', () => {
    // 0x4e5280: tab = layer & 7, class = layer >> 3.
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(188, ItemDescFunc.SkillTab, 100));

    const classes = new FakeClassTable()
      .addTab(3, 1, '+%d to Combat Skills')
      .addClassOnly(3, '(Paladin Only)');

    const layer = (3 << 3) | 1;

    expect(
      one(
        gen(stats, new FakeStringTable().withPunctuation(), null, null, classes),
        Build.entry(188, 2, layer),
      ),
    ).toBe('+2 to Combat Skills (Paladin Only)');
  });

  it('desc func 14 rejects a tab index above two', () => {
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(188, ItemDescFunc.SkillTab, 100));

    const classes = new FakeClassTable().addTab(3, 3, '+%d to Nothing');

    expect(
      all(
        gen(stats, new FakeStringTable().withPunctuation(), null, null, classes),
        Build.entry(188, 2, (3 << 3) | 3),
      ),
    ).toEqual([]);
  });

  it('desc func 14 drops the line with no class table', () => {
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(188, ItemDescFunc.SkillTab, 100));

    expect(
      all(gen(stats, new FakeStringTable().withPunctuation()), Build.entry(188, 2, (3 << 3) | 1)),
    ).toEqual([]);
  });

  it('desc func 14 still prints when the tab text is missing', () => {
    // 0x4e528d tests the charstats ROW and the tab index only; the tab text itself is
    // never pointer-checked.
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(188, ItemDescFunc.SkillTab, 100));

    const classes = new FakeClassTable().addClassOnly(3, '(Paladin Only)');

    expect(
      one(
        gen(stats, new FakeStringTable().withPunctuation(), null, null, classes),
        Build.entry(188, 2, (3 << 3) | 1),
      ),
    ).toBe(' (Paladin Only)');
  });

  it('desc func 14 omits a missing class only suffix', () => {
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(188, ItemDescFunc.SkillTab, 100));

    const classes = new FakeClassTable().addTab(3, 1, '+%d to Combat Skills');

    expect(
      one(
        gen(stats, new FakeStringTable().withPunctuation(), null, null, classes),
        Build.entry(188, 2, (3 << 3) | 1),
      ),
    ).toBe('+2 to Combat Skills ');
  });

  it('desc func 15 unpacks the skill and level from the layer', () => {
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(198, ItemDescFunc.SkillOnEvent, 100));

    const strings = new FakeStringTable()
      .withPunctuation()
      .add(100, '%d%% Chance to cast level %d %s on striking');
    const skills = new FakeSkillTable().add(56, 'Frost Nova');

    expect(one(gen(stats, strings, null, skills), Build.entry(198, 5, (56 << 6) | 3))).toBe(
      '5% Chance to cast level 3 Frost Nova on striking',
    );
  });

  it('desc func 15 honours a non default skill id shift', () => {
    const stats = new FakeStatCostTable();
    stats.skillIdShift = 8;
    stats.add(Build.stat(198, ItemDescFunc.SkillOnEvent, 100));

    const strings = new FakeStringTable().withPunctuation().add(100, '%d%% cast level %d %s');
    const skills = new FakeSkillTable().add(56, 'Frost Nova');

    expect(one(gen(stats, strings, null, skills), Build.entry(198, 5, (56 << 8) | 3))).toBe(
      '5% cast level 3 Frost Nova',
    );
  });

  it.each([
    [0], // skill id 0 is rejected
    [500], // beyond SkillCount
  ])('desc func 15 rejects an out of range skill id (%i)', skillId => {
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(198, ItemDescFunc.SkillOnEvent, 100));

    const strings = new FakeStringTable().withPunctuation().add(100, '%d %d %s');
    const skills = new FakeSkillTable();
    skills.rowCount = 400;

    expect(all(gen(stats, strings, null, skills), Build.entry(198, 5, (skillId << 6) | 3))).toEqual(
      [],
    );
  });

  it('desc func 15 drops the line with no skill table', () => {
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(198, ItemDescFunc.SkillOnEvent, 100));

    expect(
      all(
        gen(stats, new FakeStringTable().withPunctuation().add(100, '%d %d %s')),
        Build.entry(198, 5, (56 << 6) | 3),
      ),
    ).toEqual([]);
  });

  it('desc func 16 treats the layer as a bare skill id', () => {
    // 0x4e533e: unlike 15 and 24, there is no shift here.
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(148, ItemDescFunc.SkillAura, 100));

    const strings = new FakeStringTable()
      .withPunctuation()
      .add(100, 'Level %d %s Aura When Equipped');
    const skills = new FakeSkillTable().add(120, 'Holy Freeze');

    expect(one(gen(stats, strings, null, skills), Build.entry(148, 3, 120))).toBe(
      'Level 3 Holy Freeze Aura When Equipped',
    );
  });

  it('desc func 16 drops the line for an unknown skill', () => {
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(148, ItemDescFunc.SkillAura, 100));

    const strings = new FakeStringTable().withPunctuation().add(100, 'Level %d %s');

    expect(all(gen(stats, strings, null, new FakeSkillTable()), Build.entry(148, 3, 120))).toEqual(
      [],
    );
  });

  it('desc func 16 drops the line with no skill table', () => {
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(148, ItemDescFunc.SkillAura, 100));

    expect(
      all(
        gen(stats, new FakeStringTable().withPunctuation().add(100, 'Level %d %s')),
        Build.entry(148, 3, 120),
      ),
    ).toEqual([]);
  });

  it('desc func 19 formats the string with the value', () => {
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(1, ItemDescFunc.RawFormat, 100));

    const strings = new FakeStringTable()
      .withPunctuation()
      .add(100, 'Adds %d poison damage over 3 seconds');

    expect(one(gen(stats, strings), Build.entry(1, 7))).toBe('Adds 7 poison damage over 3 seconds');
  });

  it('desc func 22 appends the monster type', () => {
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(179, ItemDescFunc.MonsterTypeDamage, 100));

    const strings = new FakeStringTable().withPunctuation().add(100, 'Damage');
    const monsters = new FakeMonsterTable().addType(4, 'Undead');

    expect(one(gen(stats, strings, null, null, null, monsters), Build.entry(179, 50, 4))).toBe(
      '+50% Damage to Undead',
    );
  });

  it('desc func 22 still prints when the monster type is unknown', () => {
    // GetMonTypeLine returning 0 skips the suffix but keeps the line (0x4e5578).
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(179, ItemDescFunc.MonsterTypeDamage, 100));

    const strings = new FakeStringTable().withPunctuation().add(100, 'Damage');

    expect(
      one(gen(stats, strings, null, null, null, new FakeMonsterTable()), Build.entry(179, 50, 4)),
    ).toBe('+50% Damage');
  });

  it('desc func 22 still prints when its string is missing', () => {
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(179, ItemDescFunc.MonsterTypeDamage, 0));

    const monsters = new FakeMonsterTable().addType(4, 'Undead');

    expect(
      one(
        gen(stats, new FakeStringTable().withPunctuation(), null, null, null, monsters),
        Build.entry(179, 50, 4),
      ),
    ).toBe('+50%  to Undead');
  });

  it('desc func 23 names a single monster', () => {
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(341, ItemDescFunc.MonsterDamage, 100));

    const strings = new FakeStringTable().withPunctuation().add(100, 'Damage to');
    const monsters = new FakeMonsterTable().addMonster(9, 'Fallen');

    expect(one(gen(stats, strings, null, null, null, monsters), Build.entry(341, 50, 9))).toBe(
      '50% Damage to Fallen',
    );
  });

  it('desc func 23 drops the line for an unknown monster', () => {
    // TXT_MonStats_GetLine returning 0 drops the whole line (0x4e55c0).
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(341, ItemDescFunc.MonsterDamage, 100));

    const strings = new FakeStringTable().withPunctuation().add(100, 'Damage to');

    expect(
      all(gen(stats, strings, null, null, null, new FakeMonsterTable()), Build.entry(341, 50, 9)),
    ).toEqual([]);
  });

  it('desc func 23 drops the line with no monster table', () => {
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(341, ItemDescFunc.MonsterDamage, 100));

    expect(
      all(
        gen(stats, new FakeStringTable().withPunctuation().add(100, 'Damage to')),
        Build.entry(341, 50, 9),
      ),
    ).toEqual([]);
  });

  it('desc func 23 still prints when its string is missing', () => {
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(341, ItemDescFunc.MonsterDamage, 0));

    const monsters = new FakeMonsterTable().addMonster(9, 'Fallen');

    expect(
      one(
        gen(stats, new FakeStringTable().withPunctuation(), null, null, null, monsters),
        Build.entry(341, 50, 9),
      ),
    ).toBe('50%  Fallen');
  });

  it('desc func 24 builds the charge line from locale strings', () => {
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(204, ItemDescFunc.Charges, 100));

    const strings = new FakeStringTable()
      .withPunctuation()
      .add(DescStringIds.Level, 'Level')
      .add(100, '(%d/%d Charges)');
    const skills = new FakeSkillTable().add(54, 'Teleport');

    const value = (20 << 8) | 13;

    expect(one(gen(stats, strings, null, skills), Build.entry(204, value, (54 << 6) | 3))).toBe(
      'Level 3 Teleport (13/20 Charges)',
    );
  });

  it('desc func 24 drops the line for an unknown skill', () => {
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(204, ItemDescFunc.Charges, 100));

    const strings = new FakeStringTable()
      .withPunctuation()
      .add(DescStringIds.Level, 'Level')
      .add(100, '(%d/%d Charges)');

    assertBlank(
      gen(stats, strings, null, new FakeSkillTable()),
      Build.entry(204, (20 << 8) | 13, (54 << 6) | 3),
    );
  });

  it('desc func 24 drops the line with no skill table', () => {
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(204, ItemDescFunc.Charges, 100));

    const strings = new FakeStringTable().withPunctuation().add(100, '(%d/%d Charges)');

    assertBlank(gen(stats, strings), Build.entry(204, (20 << 8) | 13, (54 << 6) | 3));
  });

  it('desc func 27 composes the class only skill line', () => {
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(107, ItemDescFunc.SkillClassOnly, 100));

    const strings = new FakeStringTable().withPunctuation();
    const skills = new FakeSkillTable().add(54, 'Teleport', 1);
    const classes = new FakeClassTable().addClassOnly(1, '(Sorceress Only)');

    expect(one(gen(stats, strings, null, skills, classes), Build.entry(107, 2, 54))).toBe(
      '+2 to Teleport (Sorceress Only)',
    );
  });

  it('desc func 27 keeps a partial line for a class less skill', () => {
    // SKILLS_GetCharClassFromSkillId_Validated failing drops the line (0x4e580b).
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(107, ItemDescFunc.SkillClassOnly, 100));

    const skills = new FakeSkillTable().add(54, 'Teleport');
    const classes = new FakeClassTable().addClassOnly(1, '(Sorceress Only)');

    expect(
      one(
        gen(stats, new FakeStringTable().withPunctuation(), null, skills, classes),
        Build.entry(107, 2, 54),
      ),
    ).toBe('+2 to Teleport ');
  });

  it('desc func 27 keeps a partial line when the class id is above six', () => {
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(107, ItemDescFunc.SkillClassOnly, 100));

    const skills = new FakeSkillTable().add(54, 'Teleport', 7);
    const classes = new FakeClassTable().addClassOnly(7, '(Nobody Only)');

    // 0x4e5812: the line survives with its trailing separator.
    expect(
      one(
        gen(stats, new FakeStringTable().withPunctuation(), null, skills, classes),
        Build.entry(107, 2, 54),
      ),
    ).toBe('+2 to Teleport ');
  });

  it('desc func 27 omits a missing class only suffix', () => {
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(107, ItemDescFunc.SkillClassOnly, 100));

    const skills = new FakeSkillTable().add(54, 'Teleport', 1);

    expect(
      one(
        gen(stats, new FakeStringTable().withPunctuation(), null, skills, new FakeClassTable()),
        Build.entry(107, 2, 54),
      ),
    ).toBe('+2 to Teleport ');
  });

  it('desc func 27 drops an unknown skill', () => {
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(107, ItemDescFunc.SkillClassOnly, 100));

    assertBlank(
      gen(
        stats,
        new FakeStringTable().withPunctuation(),
        null,
        new FakeSkillTable(),
        new FakeClassTable(),
      ),
      Build.entry(107, 2, 54),
    );
  });

  it('desc func 27 drops the line without a skill or class table', () => {
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(107, ItemDescFunc.SkillClassOnly, 100));

    assertBlank(gen(stats, new FakeStringTable().withPunctuation()), Build.entry(107, 2, 54));
  });

  it('desc func 27 drops the line when the to string is missing', () => {
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(107, ItemDescFunc.SkillClassOnly, 100));

    const strings = new FakeStringTable()
      .add(DescStringIds.Space, ' ')
      .add(DescStringIds.Plus, '+');
    const skills = new FakeSkillTable().add(54, 'Teleport', 1);
    const classes = new FakeClassTable().addClassOnly(1, '(Sorceress Only)');

    assertBlank(gen(stats, strings, null, skills, classes), Build.entry(107, 2, 54));
  });

  it('desc func 28 names the skill', () => {
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(97, ItemDescFunc.Skill, 100));

    const skills = new FakeSkillTable().add(54, 'Teleport');

    expect(
      one(
        gen(stats, new FakeStringTable().withPunctuation(), null, skills),
        Build.entry(97, 2, 54),
      ),
    ).toBe('+2 to Teleport');
  });

  it('desc func 28 clamps to three for the viewers own class', () => {
    // 0x4e5889: the famous +skills cap on class-specific items.
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(97, ItemDescFunc.Skill, 100));

    const skills = new FakeSkillTable().add(54, 'Teleport', 1);
    const values = new FakeStatValues();
    values.playerClass = 1;

    const lines = all(
      gen(stats, new FakeStringTable().withPunctuation(), values, skills),
      Build.entry(97, 6, 54),
    );

    expect(lines[0]!.text).toBe('+3 to Teleport');
    expect(lines[0]!.value).toBe(3);
  });

  it('desc func 28 does not clamp for another class', () => {
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(97, ItemDescFunc.Skill, 100));

    const skills = new FakeSkillTable().add(54, 'Teleport', 1);
    const values = new FakeStatValues();
    values.playerClass = 3;

    expect(
      one(
        gen(stats, new FakeStringTable().withPunctuation(), values, skills),
        Build.entry(97, 6, 54),
      ),
    ).toBe('+6 to Teleport');
  });

  it('desc func 28 does not clamp at or below three', () => {
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(97, ItemDescFunc.Skill, 100));

    const skills = new FakeSkillTable().add(54, 'Teleport', 1);
    const values = new FakeStatValues();
    values.playerClass = 1;

    expect(
      one(
        gen(stats, new FakeStringTable().withPunctuation(), values, skills),
        Build.entry(97, 3, 54),
      ),
    ).toBe('+3 to Teleport');
  });

  it('desc func 28 drops an unknown skill', () => {
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(97, ItemDescFunc.Skill, 100));

    assertBlank(
      gen(stats, new FakeStringTable().withPunctuation(), null, new FakeSkillTable()),
      Build.entry(97, 2, 54),
    );
  });

  it('desc func 28 drops the line with no skill table', () => {
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(97, ItemDescFunc.Skill, 100));

    assertBlank(gen(stats, new FakeStringTable().withPunctuation()), Build.entry(97, 2, 54));
  });

  it('desc func 28 drops the line when the to string is missing', () => {
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(97, ItemDescFunc.Skill, 100));

    const strings = new FakeStringTable()
      .add(DescStringIds.Space, ' ')
      .add(DescStringIds.Plus, '+');
    const skills = new FakeSkillTable().add(54, 'Teleport');

    assertBlank(gen(stats, strings, null, skills), Build.entry(97, 2, 54));
  });

  it('desc func 28 keeps a row that exists with an empty name', () => {
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(97, ItemDescFunc.Skill, 100));

    const skills = new FakeSkillTable().add(54, '');

    // 0x4e58ba tests the pointer, so an empty entry is not a missing one.
    expect(
      one(
        gen(stats, new FakeStringTable().withPunctuation(), null, skills),
        Build.entry(97, 2, 54),
      ),
    ).toBe('+2 to ');
  });

  it('desc func 22 prints without a monster table at all', () => {
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(179, ItemDescFunc.MonsterTypeDamage, 100));

    const strings = new FakeStringTable().withPunctuation().add(100, 'Damage');

    expect(one(gen(stats, strings), Build.entry(179, 50, 4))).toBe('+50% Damage');
  });

  it('a line stringifies to its text', () => {
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(1, ItemDescFunc.PlusValueString, 100));

    const lines = all(
      gen(stats, new FakeStringTable().withPunctuation().add(100, 'to Strength')),
      Build.entry(1, 10),
    );

    expect(lines[0]!.toString()).toBe('+10 to Strength');
  });

  it('an unknown desc func prints nothing', () => {
    // The engine's default arm returns 0.
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(1, 99, 100));

    expect(
      all(
        gen(stats, new FakeStringTable().withPunctuation().add(100, 'Mystery')),
        Build.entry(1, 10),
      ),
    ).toEqual([]);
  });
});

// =================================================================
// DescFunc 17 and 18, by time
// =================================================================

suite('DescFunc 17 and 18, by time', () => {
  it('a by time value unpacks into a period and two bounds', () => {
    const v = ByTimeValue.unpack(ByTime.pack(2, -30, 70));

    expect(v.period).toBe(2);
    expect(v.low).toBe(-30);
    expect(v.high).toBe(70);
  });

  it.each([
    [0, 0, 70], // at the peak: the high bound
    [0, 180, -30], // opposite the peak: the low bound
    [0, 90, 20], // quarter turn: midway
  ])('a by time value interpolates across the day (%i, %i)', (period, degrees, expected) => {
    const v = ByTimeValue.unpack(ByTime.pack(period, -30, 70));
    expect(v.interpolate(degrees)).toBe(expected);
  });

  it('a by time angle beyond half a turn folds back', () => {
    // 0x65ca7b: distance > 180 becomes 360 - distance.
    const v = ByTimeValue.unpack(ByTime.pack(0, -30, 70));
    expect(v.interpolate(270)).toBe(v.interpolate(90));
  });

  it('desc func 17 prefixes the period name and the interpolated value', () => {
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(1, ItemDescFunc.ValueStringByTime, 100));

    // The name is indexed by the packed period through the permuted table at
    // 0x6DBD88, so period 1 resolves to string 21237.
    const strings = new FakeStringTable()
      .withPunctuation()
      .add(100, 'to Strength')
      .add(PeriodOfDay[1]!, 'Dawn');

    const time = new FakeGameTime();
    time.degrees = 90; // the peak for period 1

    expect(
      one(
        gen(stats, strings, null, null, null, null, time),
        Build.entry(1, ByTime.pack(1, -30, 70)),
      ),
    ).toBe('Dawn\n+70 to Strength');
  });

  it('desc func 18 adds a percent', () => {
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(1, ItemDescFunc.ValuePercentStringByTime, 100));

    const strings = new FakeStringTable()
      .withPunctuation()
      .add(100, 'Enhanced Defense')
      .add(PeriodOfDay[0]!, 'Dusk');

    const time = new FakeGameTime();

    expect(
      one(
        gen(stats, strings, null, null, null, null, time),
        Build.entry(1, ByTime.pack(0, -30, 70)),
      ),
    ).toBe('Dusk\n+70% Enhanced Defense');
  });

  it('desc func 17 shows the low bound when there is no current act', () => {
    // 0x4e53c0: with no act the interpolation is bypassed entirely.
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(1, ItemDescFunc.ValueStringByTime, 100));

    const strings = new FakeStringTable()
      .withPunctuation()
      .add(100, 'to Strength')
      .add(PeriodOfDay[0]!, 'Dusk');

    const time = new FakeGameTime();
    time.hasTime = false;
    time.degrees = 0;

    expect(
      one(
        gen(stats, strings, null, null, null, null, time),
        Build.entry(1, ByTime.pack(0, 40, 70)),
      ),
    ).toBe('Dusk\n+40 to Strength');
  });

  it('desc func 17 works without a time provider', () => {
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(1, ItemDescFunc.ValueStringByTime, 100));

    const strings = new FakeStringTable()
      .withPunctuation()
      .add(100, 'to Strength')
      .add(PeriodOfDay[0]!, 'Dusk');

    expect(one(gen(stats, strings), Build.entry(1, ByTime.pack(0, 40, 70)))).toBe(
      'Dusk\n+40 to Strength',
    );
  });

  it('desc func 17 omits a missing period name', () => {
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(1, ItemDescFunc.ValueStringByTime, 100));

    expect(
      one(
        gen(stats, new FakeStringTable().withPunctuation().add(100, 'to Strength')),
        Build.entry(1, ByTime.pack(0, 40, 70)),
      ),
    ).toBe('\n+40 to Strength');
  });

  it('desc func 17 leaves the number out when only the adjusted value is negative', () => {
    // 0x4e5436: adjusted < 0 while the raw stat is >= 0 leaves the digits empty.
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(1, ItemDescFunc.ValueStringByTime, 100));

    const strings = new FakeStringTable().withPunctuation().add(100, 'to Strength');

    const time = new FakeGameTime();
    time.degrees = 180; // opposite the peak, so the low bound applies

    expect(
      one(
        gen(stats, strings, null, null, null, null, time),
        Build.entry(1, ByTime.pack(0, -30, 70)),
      ),
    ).toBe('\n to Strength');
  });

  it('desc func 17 with an unusual desc val keeps only the period name', () => {
    // 0x4e54a3: DescVal other than 1 or 2 leaves the value part empty, but the
    // period name and separator have already been written.
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(1, ItemDescFunc.ValueStringByTime, 100, { descVal: 0 }));

    const strings = new FakeStringTable()
      .withPunctuation()
      .add(100, 'to Strength')
      .add(PeriodOfDay[0]!, 'Dusk');

    expect(one(gen(stats, strings), Build.entry(1, ByTime.pack(0, 40, 70)))).toBe('Dusk\n');
  });

  it('the time provider is queried once per line', () => {
    // DRLGENV_GetPeriodOfDayFromAct is called once (0x65ca4a). Querying twice lets a
    // live provider return two different angles while formatting a single line.
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(1, ItemDescFunc.ValueStringByTime, 100));

    const strings = new FakeStringTable()
      .withPunctuation()
      .add(100, 'to Strength')
      .add(PeriodOfDay[0]!, 'Dusk');

    const time = new CountingGameTime();
    time.degrees = 90;

    new ItemDescriptionGenerator(stats, strings, null, null, null, null, time).describe([
      Build.entry(1, ByTime.pack(0, -30, 70)),
    ]);

    expect(time.calls).toBe(1);
  });
});

class CountingGameTime implements IGameTimeProvider {
  calls = 0;
  degrees = 0;

  getTimeAngle(): number | null {
    ++this.calls;
    return this.degrees;
  }
}

// =================================================================
// DescGrp
// =================================================================

function resistTable(
  descGrpFunc: number = ItemDescFunc.PlusValueString,
  descGrpVal = 1,
  grpStrPos = 200,
): FakeStatCostTable {
  const stats = new FakeStatCostTable();

  for (const statId of [39, 41, 43, 45]) {
    const descriptor = Build.stat(statId, ItemDescFunc.PlusValueString, 100 + statId);
    descriptor.descGrp = 1;
    descriptor.descGrpFunc = descGrpFunc;
    descriptor.descGrpVal = descGrpVal;
    descriptor.descGrpStrPos = grpStrPos;
    stats.add(descriptor);
  }

  stats.addGroup(1, 39, 41, 43, 45);
  return stats;
}

function resistStrings(): FakeStringTable {
  return new FakeStringTable()
    .withPunctuation()
    .add(139, 'Fire Resist')
    .add(141, 'Lightning Resist')
    .add(143, 'Cold Resist')
    .add(145, 'Poison Resist')
    .add(200, 'to All Resistances');
}

function resistValues(...values: number[]): FakeStatValues {
  const source = new FakeStatValues();
  const ids = [39, 41, 43, 45];
  for (let i = 0; i < values.length; ++i) {
    source.addBase(ids[i]!, values[i]!);
  }

  return source;
}

suite('DescGrp', () => {
  it('a complete group at one value prints once from its lowest member', () => {
    const lines = all(
      gen(resistTable(), resistStrings(), resistValues(30, 30, 30, 30)),
      Build.entry(39, 30),
      Build.entry(41, 30),
      Build.entry(43, 30),
      Build.entry(45, 30),
    );

    expect(lines).toHaveLength(1);
    expect(lines[0]!.text).toBe('+30 to All Resistances');
    expect(lines[0]!.isGroup).toBe(true);
    expect(lines[0]!.statId).toBe(39); // the lowest id in the group emits
  });

  it('a group with a member at a different value prints individually', () => {
    const lines = all(
      gen(resistTable(), resistStrings(), resistValues(30, 30, 30, 15)),
      Build.entry(39, 30),
      Build.entry(41, 30),
      Build.entry(43, 30),
      Build.entry(45, 15),
    );

    expect(lines).toHaveLength(4);
    for (const line of lines) {
      expect(line.isGroup).toBe(false);
    }
  });

  it('a group with an absent member prints individually', () => {
    // GetBaseStatValue returns 0 for the absent member, which breaks the equality.
    const lines = all(
      gen(resistTable(), resistStrings(), resistValues(30, 30, 30)),
      Build.entry(39, 30),
      Build.entry(41, 30),
      Build.entry(43, 30),
    );

    expect(lines).toHaveLength(3);
    for (const line of lines) {
      expect(line.isGroup).toBe(false);
    }
  });

  it('a group falls apart without a value source', () => {
    const lines = all(
      gen(resistTable(), resistStrings()),
      Build.entry(39, 30),
      Build.entry(41, 30),
      Build.entry(43, 30),
      Build.entry(45, 30),
    );

    expect(lines).toHaveLength(4);
  });

  it('a group the table does not know prints individually', () => {
    const stats = resistTable();
    stats.groups.clear();

    const lines = all(
      gen(stats, resistStrings(), resistValues(30, 30, 30, 30)),
      Build.entry(39, 30),
      Build.entry(41, 30),
      Build.entry(43, 30),
      Build.entry(45, 30),
    );

    expect(lines).toHaveLength(4);
  });

  it('an empty group prints individually', () => {
    const stats = resistTable();
    stats.addGroup(1);

    const lines = all(
      gen(stats, resistStrings(), resistValues(30, 30, 30, 30)),
      Build.entry(39, 30),
    );

    expect(lines).toHaveLength(1);
    expect(lines[0]!.isGroup).toBe(false);
  });

  it('a group naming a member with no row prints individually', () => {
    const stats = resistTable();
    stats.addGroup(1, 39, 41, 43, 45, 999);

    const lines = all(
      gen(stats, resistStrings(), resistValues(30, 30, 30, 30)),
      Build.entry(39, 30),
    );

    expect(lines).toHaveLength(1);
    expect(lines[0]!.isGroup).toBe(false);
  });

  it('a group with no group desc func prints nothing for its primary', () => {
    // The engine reads DescGrpFunc once grouped; a zero there yields no line.
    const lines = all(
      gen(resistTable(0), resistStrings(), resistValues(30, 30, 30, 30)),
      Build.entry(39, 30),
      Build.entry(41, 30),
      Build.entry(43, 30),
      Build.entry(45, 30),
    );

    expect(lines).toEqual([]);
  });

  it('a group whose string is missing still prints its number', () => {
    const lines = all(
      gen(
        resistTable(ItemDescFunc.PlusValueString, 1, 0),
        resistStrings(),
        resistValues(30, 30, 30, 30),
      ),
      Build.entry(39, 30),
      Build.entry(41, 30),
      Build.entry(43, 30),
      Build.entry(45, 30),
    );

    expect(lines).toHaveLength(1);
    expect(lines[0]!.text).toBe('+30 ');
  });

  it('a group honours its own desc val', () => {
    const lines = all(
      gen(
        resistTable(ItemDescFunc.PlusValueString, 2),
        resistStrings(),
        resistValues(30, 30, 30, 30),
      ),
      Build.entry(39, 30),
      Build.entry(41, 30),
      Build.entry(43, 30),
      Build.entry(45, 30),
    );

    expect(lines).toHaveLength(1);
    expect(lines[0]!.text).toBe('to All Resistances +30');
  });

  it('a group uses its own second string', () => {
    const stats = resistTable(ItemDescFunc.ValueStringString2);
    for (const descriptor of stats.stats.values()) {
      descriptor.descGrpStr2 = 201;
    }

    const strings = resistStrings().add(201, '(group)');

    const lines = all(
      gen(stats, strings, resistValues(30, 30, 30, 30)),
      Build.entry(39, 30),
      Build.entry(41, 30),
      Build.entry(43, 30),
      Build.entry(45, 30),
    );

    expect(lines[0]!.text).toBe('30 to All Resistances (group)');
  });

  it('a grouped negative value uses the group negative string', () => {
    const stats = resistTable();
    for (const descriptor of stats.stats.values()) {
      descriptor.descGrpStrNeg = 202;
    }

    const strings = resistStrings().add(202, 'from All Resistances');

    const lines = all(
      gen(stats, strings, resistValues(-30, -30, -30, -30)),
      Build.entry(39, -30),
      Build.entry(41, -30),
      Build.entry(43, -30),
      Build.entry(45, -30),
    );

    expect(lines[0]!.text).toBe('-30 from All Resistances');
  });
});

// =================================================================
// Regressions: accessor per guard, never-breaks, separators, the 511 cap
// =================================================================

suite('regressions', () => {
  it('op scaling reads the viewers stats not the items', () => {
    // 0x4e4c93: GetStatUnsignedValue(GetPlayerUnit(), ...). The counterpart to the
    // undead guard: here the PLAYER is correct and the item would be wrong.
    const stats = new FakeStatCostTable();
    const perLevel = Build.stat(1, ItemDescFunc.PlusValueString, 100);
    perLevel.op = 2;
    perLevel.opParam = 0;
    perLevel.opBase = 12;
    stats.add(perLevel);
    stats.add(Build.stat(12, 0, 0));

    const values = new FakeStatValues();
    values.addPlayer(12, 3);
    values.addItemStat(12, 99); // must be ignored

    const strings = new FakeStringTable().withPunctuation().add(100, 'to Life');

    const lines = new ItemDescriptionGenerator(stats, strings, values).describe([
      Build.entry(1, 2),
    ]);

    expect(lines[0]!.text).toBe('+6 to Life'); // 2 * 3
  });

  it('the secondary damage suppression reads the merged list at layer zero', () => {
    // 0x4e62e3: STATLIST_GetBaseStatValue(mergedList, 21, 0).
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(23, ItemDescFunc.PlusValueString, 100));

    const strings = new FakeStringTable().withPunctuation().add(100, 'Secondary Min');

    const suppressed = new FakeStatValues().addBase(21, 5);
    expect(
      new ItemDescriptionGenerator(stats, strings, suppressed).describe([Build.entry(23, 7)]),
    ).toEqual([]);

    // The item's own list and the player's must not drive it.
    const notSuppressed = new FakeStatValues();
    notSuppressed.addItemStat(21, 5);
    notSuppressed.addPlayer(21, 5);
    expect(
      new ItemDescriptionGenerator(stats, strings, notSuppressed).describe([Build.entry(23, 7)]),
    ).toHaveLength(1);
  });

  it.each([
    [true, true, 0, 0, true], // all five terms hold
    [false, true, 0, 0, false], // not an item
    [true, false, 0, 0, false], // table forbids durability
    [true, true, 1, 0, false], // indestructible
    [true, true, 0, 5, false], // has max durability
  ])(
    'the never breaks tail line needs all five terms (%s, %s, %i, %i)',
    (isItem, tableAllows, indestructible, maxDurability, expected) => {
      // 0x4e636a-0x4e63a4.
      const stats = new FakeStatCostTable();
      const strings = new FakeStringTable()
        .withPunctuation()
        .add(DescStringIds.NeverBreaks, 'Cannot Be Broken');

      const values = new FakeStatValues();
      values.describedUnitIsItem = isItem;
      values.itemTableAllowsDurability = tableAllows;
      values.addItemStat(152, indestructible);
      values.txtMaxDurability = maxDurability; // 0x4e63a4 uses its own accessor

      const lines = new ItemDescriptionGenerator(stats, strings, values).describe([]);

      expect(lines.some(l => l.text === 'Cannot Be Broken')).toBe(expected);
    },
  );

  it('the never breaks line survives an empty string entry', () => {
    // 0x4e63b2-0x4e63e0 never tests the pointer, so an empty entry still emits the
    // row — and therefore its separator. Dropping it loses that separator.
    const stats = new FakeStatCostTable();
    const strings = new FakeStringTable().withPunctuation().add(DescStringIds.NeverBreaks, '');

    const values = new FakeStatValues();
    values.describedUnitIsItem = true;
    values.itemTableAllowsDurability = true;

    const lines = new ItemDescriptionGenerator(stats, strings, values).describe([]);

    expect(lines).toHaveLength(1);
    expect(lines[0]!.isBlank).toBe(true);
  });

  it('the max durability test uses its own accessor', () => {
    // 0x4e63a4 GetTxtMaxDurability is NOT GetItemStatValue(73): it requires stat 73 in
    // the base array first. Reusing the min-clamped read suppresses the line where the
    // game emits it.
    const stats = new FakeStatCostTable();
    const strings = new FakeStringTable()
      .withPunctuation()
      .add(DescStringIds.NeverBreaks, 'Cannot Be Broken');

    const values = new FakeStatValues();
    values.describedUnitIsItem = true;
    values.itemTableAllowsDurability = true;
    values.txtMaxDurability = 0; // the accessor the tail actually consults
    values.addItemStat(73, 250); // must not be consulted

    expect(new ItemDescriptionGenerator(stats, strings, values).describe([])).toHaveLength(1);
  });

  it('a formatted integer is capped at eight characters', () => {
    // UTF8_ConvertToWideChar terminates at index 8 (0x526320).
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(1, ItemDescFunc.ValueString, 100));

    const strings = new FakeStringTable().withPunctuation().add(100, 'Big');

    const lines = new ItemDescriptionGenerator(stats, strings).describe([
      Build.entry(1, 1234567890),
    ]);

    expect(lines[0]!.text).toBe('12345678 Big');
  });

  it('a pre joined line takes no separator in either mode', () => {
    // 0x4e620e and 0x4e5e18 append directly and never set the emitted latch.
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(1, ItemDescFunc.PlusValueString, 100));

    const strings = new FakeStringTable().withPunctuation().add(100, 'to Strength');
    const generator = new ItemDescriptionGenerator(stats, strings);

    const preJoined = new ItemDescriptionLine();
    preJoined.text = 'PRE';
    preJoined.preJoined = true;

    const normal = new ItemDescriptionLine();
    normal.text = 'NORMAL';

    const lines = [preJoined, normal];

    // Block mode: no separator before PRE, and PRE does not make NORMAL emit one.
    expect(generator.join(lines, false)).toBe('PRENORMAL');

    // Inline mode (the default, and what the item tooltip uses): PRE carries its own
    // terminator, NORMAL gets one.
    expect(generator.join(lines)).toBe('PRENORMAL\n');
  });

  it('a placeholder skill name keeps the line rather than dropping it', () => {
    // The four skill DescFuncs test the RESOLVED POINTER from GetLocaleString, not the
    // string id (0x4e534a test eax,eax / 0x4e534c jz). SKILLDESC_GetStatNameString
    // returns the sentinel id 5382 on failure, but that is an ordinary string.tbl
    // index holding placeholder text, so the engine KEEPS the line and prints it.
    //
    // The contract on ISkillTable.GetSkillName briefly said to collapse 5382 to null.
    // That would drop rows the engine emits; this pin makes the difference visible.
    const Placeholder = 'an evil force';

    const stats = new FakeStatCostTable();
    stats.add(Build.stat(148, ItemDescFunc.SkillAura, 100));

    const strings = new FakeStringTable()
      .withPunctuation()
      .add(100, 'Level %d %s Aura When Equipped');

    const resolves = new FakeSkillTable().add(120, Placeholder);
    const kept = new ItemDescriptionGenerator(stats, strings, null, resolves).describe([
      Build.entry(148, 3, 120),
    ]);

    expect(kept).toHaveLength(1);
    expect(kept[0]!.text).toBe('Level 3 ' + Placeholder + ' Aura When Equipped');

    // Only a genuinely absent string drops the row.
    const dropped = new ItemDescriptionGenerator(
      stats,
      strings,
      null,
      new FakeSkillTable(),
    ).describe([Build.entry(148, 3, 120)]);

    expect(dropped).toEqual([]);
  });

  it('a stat descriptor does not default DescVal to one', () => {
    // The loader has no default hook for descval (0x637f0c) or dgrpval (0x637ff4), and
    // nothing in TXT_AllocTxt_itemstatcost sets one. A row that omits the column
    // arrives as 0, which takes the DescVal-other path — string alone or a blank line —
    // not the number-first shape a 1 produces. Baking 1 into the struct silently
    // changed the shape of every such row for any implementer filling a DTO.
    const descriptor = Build.emptyStat();

    expect(descriptor.descVal).toBe(0);
    expect(descriptor.descGrpVal).toBe(0);
  });

  it('the 511 cap is applied before the zero filter', () => {
    // STATLIST_GetItemStatBonusValues copies EVERY matching (layer, value) pair, zeros
    // included, and stops at 511 (0x626174 / 0x626177); the consumer skips zeros
    // afterwards at 0x4e628b / 0x4e6295. Filtering first lets non-zero entries past the
    // cap that the game had already discarded.
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(1, ItemDescFunc.PlusValueString, 100));

    const strings = new FakeStringTable().withPunctuation().add(100, 'to Strength');

    // 511 zero-valued layers, then one non-zero. The game's copy loop fills its buffer
    // with the zeros and never reaches the last pair, so nothing is described.
    const entries: (readonly [number, number])[] = [];
    for (let layer = 0; layer < 511; ++layer) {
      entries.push(Build.entry(1, 0, layer));
    }

    entries.push(Build.entry(1, 7, 511));

    expect(new ItemDescriptionGenerator(stats, strings).describe(entries)).toEqual([]);

    // One fewer zero and the non-zero pair fits inside the cap.
    entries.shift();
    expect(new ItemDescriptionGenerator(stats, strings).describe(entries)).toHaveLength(1);
  });
});

// =================================================================
// TblFormat
// =================================================================

suite('TblFormat', () => {
  it('a null format yields an empty string', () => {
    // 0x5269e1 returns leaving the destination as the caller zeroed it, which is an
    // empty line rather than an absent one.
    expect(TblFormat.format(null, 1)).toBe('');
  });

  it('an empty format comes back unchanged', () => {
    expect(TblFormat.format('', 1)).toBe('');
  });

  it('text with no placeholders is passed through', () => {
    expect(TblFormat.format('Indestructible')).toBe('Indestructible');
  });

  it.each([
    ['%d', '7'],
    ['%u', '7'],
  ])('every integer specifier substitutes (%s)', (format, expected) => {
    expect(TblFormat.format(format, 7)).toBe(expected);
  });

  it('percent i is not a valid specifier', () => {
    // 0x526a99's jump table handles only \0, %, d, s and u; 'i' halts the game.
    expect(() => TblFormat.format('%i', 7)).toThrow();
  });

  it('a string specifier substitutes', () => {
    expect(TblFormat.format('to %s', 'Teleport')).toBe('to Teleport');
  });

  it('a doubled percent becomes a literal one', () => {
    expect(TblFormat.format('%d%% Chance', 5, 0)).toBe('5% Chance');
  });

  it('a trailing percent is left alone', () => {
    expect(TblFormat.format('Chance %')).toBe('Chance %');
  });

  it('an unsupported specifier is fatal', () => {
    // 0x526c66 calls ERROR_UnrecoverableInternalError_Halt then exit(-1).
    expect(() => TblFormat.format('%x %d', 7)).toThrow();
  });

  it('a placeholder with no argument left is left alone', () => {
    expect(TblFormat.format('%d %d', 7)).toBe('7 %d');
  });

  it('an empty argument list leaves placeholders alone', () => {
    // The C# passes a null `params object[]`; a TypeScript rest parameter can only be
    // empty, and the engine's `args == null || nextArg >= args.Length` treats the two
    // identically.
    expect(TblFormat.format('%d')).toBe('%d');
  });

  it('a null string argument is surfaced rather than emulating a fault', () => {
    // 0x526761 dereferences it with no guard when there is room left.
    expect(() => TblFormat.format('to %s', null)).toThrow();
  });

  it('a non integer argument uses its own string form', () => {
    expect(TblFormat.format('value %s', '2.5')).toBe('value 2.5');
  });

  it('arguments substitute positionally in order', () => {
    expect(TblFormat.format('Level %d %s (%d/%d Charges)', 3, 'Teleport', 13, 20)).toBe(
      'Level 3 Teleport (13/20 Charges)',
    );
  });

  it('a trailing percent escapes the length budget', () => {
    // 0x526c46 copies the one-character "%" with an unbudgeted copy, so the result can
    // reach exactly maxLength where every other path stops one short.
    expect(TblFormat.formatBounded('abc%', 4)).toBe('abc%');
  });

  it('literal text is budgeted as it is appended', () => {
    // 0x526a4c admits one literal at a time while written < maxLength. Relying on a
    // final truncate instead lets the trailing-% path return an unbounded string.
    expect(TblFormat.formatBounded('abcdefghij%', 5)).toBe('abcd');
  });

  it('the surviving length is one below the budget', () => {
    // 0x526bda overwrites the last character written.
    expect(TblFormat.formatBounded('abcdefgh', 5)).toBe('abcd');
  });

  it('a conversion that would not fit is dropped and formatting stops', () => {
    // 0x526b13: admission needs len + written + 1 < maxLength, and on failure the
    // number is not emitted at all and the remainder of the format is abandoned.
    expect(TblFormat.formatBounded('ab%dcd', 4, 99)).toBe('ab');
  });

  it('a null string argument with no room left truncates instead of throwing', () => {
    // 0x52675c tests n before dereferencing, so n == 0 is safe.
    expect(TblFormat.formatBounded('abc%s', 4, null)).toBe('abc');
  });

  it('a null string argument with room left is surfaced', () => {
    expect(() => TblFormat.formatBounded('a%s', 64, null)).toThrow();
  });

  it('a doubled percent consumes an argument', () => {
    // 0x526bb4 advances the vararg cursor on the shared tail, so anything after a %%
    // shifts by one: the %% eats the 7 and the %d gets the 8.
    expect(TblFormat.format('%%%d', 7, 8)).toBe('%8');
  });

  it('an unrecognised specifier past the budget truncates rather than halting', () => {
    // 0x526a6d dominates the jump table at 0x526a99, so once the budget is spent the
    // engine returns WITHOUT inspecting the specifier — the halt at 0x526c66 is
    // unreachable there. "%%" is the way to land exactly on the limit: it appends
    // after the re-test, so the NEXT conversion meets an exhausted budget.
    expect(TblFormat.formatBounded('AB%%%x', 3, 1)).toBe('AB');
  });

  it('a null string argument past the budget truncates rather than throwing', () => {
    // Same gate. With the re-test hoisted, `room` can never go negative, so the
    // `room == 0` guard still catches every case it is meant to.
    expect(TblFormat.formatBounded('AB%%%s', 3, 1, null)).toBe('AB');
  });
});
