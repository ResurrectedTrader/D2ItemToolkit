import { describe, expect, it } from 'vitest';
import {
  DamageStatIds,
  DamageStringIds,
} from '../../../src/D2ItemToolkit.Ts/src/Tooltip/ItemDamageLines.js';
import {
  ItemDescFunc,
  ItemDescriptionGenerator,
} from '../../../src/D2ItemToolkit.Ts/src/Description/ItemDescription.js';
import {
  type IItemTooltipSections,
  ItemQuality,
  ItemTooltipColor,
  ItemTooltipComposer,
  ItemTooltipContext,
  ItemTooltipFlags,
  ItemTooltipKind,
  ItemTooltipLine,
  ItemTooltipSection,
} from '../../../src/D2ItemToolkit.Ts/src/Tooltip/ItemTooltip.js';
import { Build, FakeStatCostTable, FakeStatValues, FakeStringTable } from '../Fakes.js';

// ItemTooltipTests.cs and ItemTooltipCoverageTests.cs.

class FakeSections implements IItemTooltipSections {
  readonly texts = new Map<ItemTooltipSection, string | null>();

  readonly unmet = new Set<ItemTooltipSection>();

  set(section: ItemTooltipSection, text: string | null): FakeSections {
    this.texts.set(section, text);
    return this;
  }

  unmeetable(section: ItemTooltipSection): FakeSections {
    this.unmet.add(section);
    return this;
  }

  lineTerminator: string | null = '\n';

  getSection(section: ItemTooltipSection): string | null {
    const text = this.texts.get(section);
    return text === undefined ? null : text;
  }

  isRequirementUnmet(section: ItemTooltipSection): boolean {
    return this.unmet.has(section);
  }
}

function single(
  lines: readonly ItemTooltipLine[],
  predicate: (line: ItemTooltipLine) => boolean,
): ItemTooltipLine {
  const matched = lines.filter(predicate);
  expect(matched).toHaveLength(1);
  return matched[0] as ItemTooltipLine;
}

describe('ItemTooltipComposer', () => {
  function modifiers(): ItemDescriptionGenerator {
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(16, ItemDescFunc.PlusValuePercentString, 100, { priority: 100 }));
    stats.add(Build.stat(39, ItemDescFunc.PlusValueString, 101, { priority: 50 }));

    const strings = new FakeStringTable()
      .withPunctuation()
      .add(100, 'Enhanced Defense')
      .add(101, 'Fire Resist');

    return new ItemDescriptionGenerator(stats, strings);
  }

  function composer(sections: FakeSections): ItemTooltipComposer {
    return new ItemTooltipComposer(sections, modifiers());
  }

  function context(
    quality: ItemQuality = ItemQuality.Normal,
    flags: number = ItemTooltipFlags.None,
    forcesCrafted = false,
    unidentifiedInShop = false,
  ): ItemTooltipContext {
    const built = new ItemTooltipContext();
    built.quality = quality;
    built.flags = flags | ItemTooltipFlags.Identified;
    built.forcesCraftedColor = forcesCrafted;
    built.unidentifiedInShop = unidentifiedInShop;
    built.isWeaponOrArmorType = true;
    return built;
  }

  const SampleStats: readonly (readonly [number, number])[] = [
    [0x00000010, 180], // stat 16
    [0x00000027, 40], // stat 39
  ];

  const NoStats: readonly (readonly [number, number])[] = [];

  // =================================================================
  // Guard clauses
  // =================================================================

  it('rejects null sections', () => {
    expect(() => new ItemTooltipComposer(null, modifiers())).toThrow();
  });

  it('rejects a null modifier generator', () => {
    expect(() => new ItemTooltipComposer(new FakeSections(), null)).toThrow();
  });

  it('rejects a null context in compose', () => {
    expect(() => composer(new FakeSections()).compose(null, SampleStats)).toThrow();
  });

  it('rejects null stats in compose', () => {
    expect(() => composer(new FakeSections()).compose(context(), null)).toThrow();
  });

  it('rejects null lines in render', () => {
    expect(() => composer(new FakeSections()).render(null)).toThrow();
  });

  it('rejects a null context in resolveItemNameColor', () => {
    expect(() => ItemTooltipComposer.resolveItemNameColor(null)).toThrow();
  });

  // =================================================================
  // Section order
  // =================================================================

  it('lists sections in the order LoadItemDesc appends them', () => {
    const sections = new FakeSections()
      .set(ItemTooltipSection.ArmorClass, 'Defense: 445')
      .set(ItemTooltipSection.ItemName, 'Shaftstop')
      .set(ItemTooltipSection.RuneLetters, 'Enigma')
      .set(ItemTooltipSection.Unidentified, 'Mesh Armor')
      .set(ItemTooltipSection.RequiredLevel, 'Required Level: 38');

    const lines = composer(sections).compose(context(ItemQuality.Unique), SampleStats);

    // Display order is the reverse of append order.
    // A section's text carries its own terminator; the composer supplies one when the
    // provider omits it (GetItemName and the price line do, in the game).
    expect(lines.map(l => l.text)).toEqual([
      'Shaftstop\n',
      'Enigma\n',
      'Defense: 445\n',
      'Required Level: 38\n',
      'Mesh Armor\n',
      // The stat block is ONE buffer in the game, but LoadItemDesc drives it in
      // INLINE mode (0x48e92d pushes arg_4 = 1, reaching arg_14 at 0x4e62ec), so
      // every stat line is terminated with 3998 and the 3852 + 3995 separator is
      // never emitted. Without the terminator the whole block collapses onto one
      // rendered line and glues itself to the section below.
      '+40 Fire Resist\n',
      '+180% Enhanced Defense\n',
    ]);
  });

  it('omits an empty section', () => {
    const sections = new FakeSections()
      .set(ItemTooltipSection.ItemName, 'Shaftstop')
      .set(ItemTooltipSection.Unidentified, '')
      .set(ItemTooltipSection.BlockChance, null);

    const lines = composer(sections).compose(context(), SampleStats);

    expect(lines.map(l => l.section)).not.toContain(ItemTooltipSection.Unidentified);
    expect(lines.map(l => l.section)).not.toContain(ItemTooltipSection.BlockChance);
  });

  it('appends the stat block last', () => {
    const sections = new FakeSections().set(ItemTooltipSection.ItemName, 'Shaftstop');

    const lines = composer(sections).compose(context(), SampleStats);

    // Rendering is bottom-up, so the stat block is appended second and displayed last.
    expect(lines[lines.length - 1]?.section).toBe(ItemTooltipSection.Modifiers);
  });

  it('is just its sections when there are no stats', () => {
    const sections = new FakeSections().set(ItemTooltipSection.ItemName, 'Quilted Armor');

    const lines = composer(sections).compose(context(), NoStats);

    expect(lines).toHaveLength(1);
    expect(lines[0]?.text).toBe('Quilted Armor\n');
  });

  it('concatenates on render without inserting separators', () => {
    const sections = new FakeSections()
      .set(ItemTooltipSection.ItemName, 'Shaftstop')
      .set(ItemTooltipSection.Unidentified, 'Mesh Armor');

    const built = composer(sections);
    const lines = built.compose(context(), SampleStats);

    // 0x526700 is a plain concatenation; each writer terminates its own text, stat
    // lines included (inline mode). The assembled string ends UNTERMINATED because the
    // two writers that omit a trailing 3998 are the two appended last.
    expect(built.render(lines)).toBe(
      'Shaftstop\nMesh Armor\n+40 Fire Resist\n+180% Enhanced Defense',
    );
  });

  it('renders nothing as the empty string', () => {
    expect(composer(new FakeSections()).render([])).toBe('');
  });

  it('stringifies a line to its text', () => {
    const sections = new FakeSections().set(ItemTooltipSection.ItemName, 'Shaftstop');

    const lines = composer(sections).compose(context(), NoStats);

    expect(lines[0]?.toString()).toBe('Shaftstop\n');
  });

  // =================================================================
  // Section colours
  // =================================================================

  it.each([
    ItemTooltipSection.RequiredLevel,
    ItemTooltipSection.RequiredStrength,
    ItemTooltipSection.RequiredDexterity,
    ItemTooltipSection.ClassRestriction,
  ])('turns an unmet requirement red (%s)', section => {
    const sections = new FakeSections().set(section, 'requirement').unmeetable(section);

    const line = single(composer(sections).compose(context(), NoStats), l => l.section === section);

    expect(line.color).toBe(ItemTooltipColor.Red);
  });

  it.each([
    ItemTooltipSection.RequiredLevel,
    ItemTooltipSection.RequiredStrength,
    ItemTooltipSection.RequiredDexterity,
    ItemTooltipSection.ClassRestriction,
  ])('leaves a met requirement white (%s)', section => {
    const sections = new FakeSections().set(section, 'requirement');

    const line = single(composer(sections).compose(context(), NoStats), l => l.section === section);

    expect(line.color).toBe(ItemTooltipColor.White);
  });

  it.each([
    [ItemTooltipSection.ItemName, ItemTooltipColor.White],
    [ItemTooltipSection.EtherealSocketed, ItemTooltipColor.Magic],
    [ItemTooltipSection.Unidentified, ItemTooltipColor.Red],
    [ItemTooltipSection.RuneLetters, ItemTooltipColor.Unique],
    [ItemTooltipSection.ArmorClass, ItemTooltipColor.White],
    [ItemTooltipSection.AttackSpeed, ItemTooltipColor.White],
  ] as const)('gives section %s the colour LoadItemDesc gives it', (section, expected) => {
    const sections = new FakeSections().set(section, 'text');

    const line = single(composer(sections).compose(context(), NoStats), l => l.section === section);

    expect(line.color).toBe(expected);
  });

  // =================================================================
  // Stat block colour
  // =================================================================

  it.each([
    [ItemQuality.Magic, ItemTooltipColor.Magic],
    [ItemQuality.Set, ItemTooltipColor.Set],
    [ItemQuality.Rare, ItemTooltipColor.Rare],
    [ItemQuality.Unique, ItemTooltipColor.Unique],
    [ItemQuality.Crafted, ItemTooltipColor.Crafted],
    [ItemQuality.Tempered, ItemTooltipColor.Tempered],
    [ItemQuality.Normal, ItemTooltipColor.White],
    [ItemQuality.LowQuality, ItemTooltipColor.White],
    [ItemQuality.HighQuality, ItemTooltipColor.White],
  ] as const)('picks the stat block colour from quality %s', (quality, expected) => {
    expect(ItemTooltipComposer.resolveItemNameColor(context(quality))).toBe(expected);
  });

  it.each([
    ItemTooltipFlags.Socketed,
    ItemTooltipFlags.Ethereal,
    ItemTooltipFlags.Socketed | ItemTooltipFlags.Ethereal,
  ])('gives a socketed or ethereal plain item its own colour (%s)', flags => {
    expect(ItemTooltipComposer.resolveItemNameColor(context(ItemQuality.Normal, flags))).toBe(
      ItemTooltipColor.SocketedOrEthereal,
    );
  });

  it('does not let socketed override a quality colour', () => {
    expect(
      ItemTooltipComposer.resolveItemNameColor(
        context(ItemQuality.Unique, ItemTooltipFlags.Socketed),
      ),
    ).toBe(ItemTooltipColor.Unique);
  });

  it('forces white for an unidentified item in a shop', () => {
    expect(
      ItemTooltipComposer.resolveItemNameColor(
        context(ItemQuality.Unique, ItemTooltipFlags.None, false, true),
      ),
    ).toBe(ItemTooltipColor.White);
  });

  it('lets a forced crafted item code win over the shop override', () => {
    // 0x48ea0c runs after 0x48e8d7.
    expect(
      ItemTooltipComposer.resolveItemNameColor(
        context(ItemQuality.Unique, ItemTooltipFlags.None, true, true),
      ),
    ).toBe(ItemTooltipColor.Crafted);
  });

  it('lets broken win over everything', () => {
    // 0x48ebde is the last override applied.
    expect(
      ItemTooltipComposer.resolveItemNameColor(
        context(ItemQuality.Unique, ItemTooltipFlags.Broken, true),
      ),
    ).toBe(ItemTooltipColor.Red);
  });

  it('gives the stat block lines the literal magic colour', () => {
    const sections = new FakeSections().set(ItemTooltipSection.ItemName, 'Shaftstop');

    const lines = composer(sections).compose(context(ItemQuality.Unique), SampleStats);

    // 0x48ea1c appends the whole block with a literal 3; the quality colour goes to
    // the item name instead (0x48ebee).
    for (const line of lines.filter(l => l.section === ItemTooltipSection.Modifiers)) {
      expect(line.color).toBe(ItemTooltipColor.Magic);
    }
  });
});

describe('ItemTooltipComposer coverage', () => {
  function composer(sections: FakeSections): ItemTooltipComposer {
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(1, ItemDescFunc.PlusValueString, 100));

    const strings = new FakeStringTable().withPunctuation().add(100, 'to Strength');

    return new ItemTooltipComposer(sections, new ItemDescriptionGenerator(stats, strings));
  }

  function generic(): ItemTooltipContext {
    const context = new ItemTooltipContext();
    context.quality = ItemQuality.Unique;
    context.flags = ItemTooltipFlags.Identified;
    context.isWeaponOrArmorType = true;
    return context;
  }

  function line(
    text: string | null,
    section: ItemTooltipSection = ItemTooltipSection.ItemName,
    color: number = ItemTooltipColor.White,
    marker = true,
  ): ItemTooltipLine {
    const built = new ItemTooltipLine();
    built.text = text;
    built.section = section;
    built.color = color;
    built.emitsColorMarker = marker;
    return built;
  }

  const NoStats: readonly (readonly [number, number])[] = [];

  it('classifies every kind', () => {
    let context = generic();
    expect(ItemTooltipComposer.classify(context)).toBe(ItemTooltipKind.Generic);

    context = generic();
    context.isShopTransaction = true;
    expect(ItemTooltipComposer.classify(context)).toBe(ItemTooltipKind.ShopTransaction);

    context = generic();
    context.isTransmogrify = true;
    expect(ItemTooltipComposer.classify(context)).toBe(ItemTooltipKind.Transmogrify);

    context = generic();
    context.quality = ItemQuality.Set;
    expect(ItemTooltipComposer.classify(context)).toBe(ItemTooltipKind.IdentifiedSetItem);

    // Set but unidentified stays generic.
    context = generic();
    context.quality = ItemQuality.Set;
    context.flags = ItemTooltipFlags.None;
    expect(ItemTooltipComposer.classify(context)).toBe(ItemTooltipKind.Generic);

    context = generic();
    context.isBook = true;
    expect(ItemTooltipComposer.classify(context)).toBe(ItemTooltipKind.Book);

    expect(() => ItemTooltipComposer.classify(null)).toThrow();
  });

  it('rejects non generic items and null arguments in compose', () => {
    const built = composer(new FakeSections());

    const book = generic();
    book.isBook = true;

    expect(() => built.compose(book, NoStats)).toThrow(/Book/);

    expect(() => built.compose(null, NoStats)).toThrow();
    expect(() => built.compose(generic(), null)).toThrow();
  });

  it('rejects null dependencies', () => {
    const stats = new FakeStatCostTable();
    const generator = new ItemDescriptionGenerator(stats, new FakeStringTable());

    expect(() => new ItemTooltipComposer(null, generator)).toThrow();
    expect(() => new ItemTooltipComposer(new FakeSections(), null)).toThrow();
  });

  it('makes every section one line when the terminator is empty', () => {
    const sections = new FakeSections()
      .set(ItemTooltipSection.ItemName, 'Name')
      .set(ItemTooltipSection.EtherealSocketed, 'Eth\nSock');
    sections.lineTerminator = '';

    const built = composer(sections);
    const lines = built.compose(generic(), NoStats);

    expect(lines).toHaveLength(2);
    expect(single(lines, l => l.section === ItemTooltipSection.EtherealSocketed).text).toBe(
      'Eth\nSock',
    );

    // DropTrailingTerminator has nothing to strip.
    expect(built.render(lines)).toBe('NameEth\nSock');
  });

  it('leaves a string that does not end with the terminator alone', () => {
    const built = composer(new FakeSections());

    expect(built.render([line('no terminator here')])).toBe('no terminator here');
  });

  it('rejects null and accepts both shapes in render and renderWithColorCodes', () => {
    const built = composer(new FakeSections());

    expect(() => built.render(null)).toThrow();
    expect(() => built.renderWithColorCodes(null)).toThrow();

    const asArray = [line('A\n'), line('B\n')];
    expect(built.render(asArray)).toBe('A\nB');
    expect(built.render(new Set(asArray))).toBe('A\nB');
  });

  it('skips lines with no text on emit', () => {
    const built = composer(new FakeSections());

    const lines = [line('first\n'), line(null), line(''), line('last\n')];

    expect(built.render(lines)).toBe('first\nlast');
  });

  it('emits a marker on every row that has glyphs', () => {
    const built = composer(new FakeSections());
    const marker =
      ItemTooltipColor.Marker + ItemTooltipComposer.encodeColorDigit(ItemTooltipColor.Magic);

    // Same colour twice: BOTH state it. Stickiness would carry the second in APPEND order, but
    // reversing into display order breaks that, so a row that does not own the game's section
    // marker is re-anchored with the colour that was in force at it.
    const sticky = built.renderWithColorCodes([
      line('A\n', ItemTooltipSection.ItemName, ItemTooltipColor.Magic, false),
      line('B\n', ItemTooltipSection.ItemName, ItemTooltipColor.Magic),
    ]);

    expect(sticky).toBe(marker + 'A\n' + marker + 'B');

    // A re-anchored row with no glyphs gets nothing — a marker there would draw a colour code
    // instead of a blank line. A row that OWNS the section marker still gets it: that one is
    // AppendAsWideChar, which only checks the buffer is non-empty (0x4521cd).
    expect(
      built.renderWithColorCodes([
        line('A\n', ItemTooltipSection.ItemName, ItemTooltipColor.Magic, false),
        line('\n', ItemTooltipSection.ItemName, ItemTooltipColor.Magic, false),
        line('B\n', ItemTooltipSection.ItemName, ItemTooltipColor.Magic),
      ]),
    ).toBe(marker + 'A\n\n' + marker + 'B');

    // A re-anchored row that already opens with a marker states its own colour and takes no anchor
    // — this is the runeword name sitting above the base name.
    expect(
      built.renderWithColorCodes([
        line(marker + 'A\n', ItemTooltipSection.ItemName, ItemTooltipColor.Unique, false),
        line('B\n', ItemTooltipSection.ItemName, ItemTooltipColor.Magic),
      ]),
    ).toBe(marker + 'A\n' + marker + 'B');

    // Text that already opens with a marker does NOT suppress the composer's own. A marker in the
    // section TEXT was put there by a writer and says nothing about whether the line's colour has
    // been stated: INV_FormatBlockChanceText prepends colour 0 to the label buffer (0x485d0e) and
    // LoadItemDesc then prepends the section's (0x48eb80), so the game draws two. Suppressing here
    // swallowed one of them.
    const unique =
      ItemTooltipColor.Marker + ItemTooltipComposer.encodeColorDigit(ItemTooltipColor.Unique);
    expect(
      built.renderWithColorCodes([
        line(marker + 'A\n', ItemTooltipSection.ItemName, ItemTooltipColor.Unique),
      ]),
    ).toBe(unique + marker + 'A');

    // No marker string at all: the digit is still written.
    expect(
      built.renderWithColorCodes(
        [line('A\n', ItemTooltipSection.ItemName, ItemTooltipColor.Magic)],
        '',
      ),
    ).toBe('3A');

    // Null text with a marker requested still emits the marker and nothing else.
    expect(
      built.renderWithColorCodes([line(null, ItemTooltipSection.ItemName, ItemTooltipColor.Magic)]),
    ).toBe('');
  });

  it('emits the quest prefix once at the front', () => {
    const built = composer(new FakeSections());
    const quest =
      ItemTooltipColor.Marker + ItemTooltipComposer.encodeColorDigit(ItemTooltipColor.Unique);

    const colored = built.renderWithColorCodes(
      [line('Name\n', ItemTooltipSection.ItemName, ItemTooltipColor.Magic)],
      ItemTooltipColor.Marker,
      true,
    );

    // Display order puts it at the END, where it paints nothing but still spends budget.
    expect(colored.endsWith(quest)).toBe(true);

    // Render is the marker-free variant: the flag only costs budget there.
    expect(built.render([line('Name\n')], true)).toBe('Name');
  });

  it('encodes the colour digit unchecked', () => {
    expect(ItemTooltipComposer.encodeColorDigit(0)).toBe('0');
    expect(ItemTooltipComposer.encodeColorDigit(10)).toBe(':');
    expect(ItemTooltipComposer.encodeColorDigit(13)).toBe('=');
  });

  it('abandons a line that cannot even fit its marker', () => {
    const built = composer(new FakeSections());

    // Bottom leaves exactly 3 characters, so the line above cannot fit even its marker.
    const bottom = line(
      'x'.repeat(ItemTooltipComposer.MaxTooltipLength - 7) + '\n',
      ItemTooltipSection.EtherealSocketed,
      ItemTooltipColor.Magic,
    );

    const top = line('TOP\n', ItemTooltipSection.ItemName, ItemTooltipColor.Unique);

    const rendered = built.render([top, bottom]);

    expect(rendered).not.toContain('TOP');
    expect(rendered.startsWith('\n')).toBe(true);
  });

  it('draws a lone marker byte on the boundary row with one character left', () => {
    const built = composer(new FakeSections());

    const bottom = line(
      'x'.repeat(ItemTooltipComposer.MaxTooltipLength - 5) + '\n',
      ItemTooltipSection.EtherealSocketed,
      ItemTooltipColor.Magic,
    );

    const top = line('TOP\n', ItemTooltipSection.ItemName, ItemTooltipColor.Unique);

    const colored = built.renderWithColorCodes([top, bottom]);

    expect(colored).toContain(ItemTooltipColor.Marker.substring(0, 1) + '\n');
  });

  it('cuts rather than abandons the only line', () => {
    const built = composer(new FakeSections());

    const only = line(
      'x'.repeat(ItemTooltipComposer.MaxTooltipLength + 10) + '\n',
      ItemTooltipSection.ItemName,
      ItemTooltipColor.Unique,
    );

    // A single line longer than the budget is cut, not abandoned.
    const rendered = built.render([only]);
    expect(rendered).toHaveLength(ItemTooltipComposer.MaxTooltipLength - 3);
  });

  it('drops the fragment when a cut lands just after a complete marker', () => {
    const built = composer(new FakeSections());

    // Pad so the cut falls immediately after an embedded marker.
    const pad = ItemTooltipComposer.MaxTooltipLength - 3 - ItemTooltipColor.Marker.length;
    const text = 'x'.repeat(pad) + ItemTooltipColor.Marker + 'tail\n';

    const rendered = built.render([
      line(text, ItemTooltipSection.ItemName, ItemTooltipColor.Unique),
    ]);

    expect(rendered).not.toContain(ItemTooltipColor.Marker);
  });

  it('needs no pull back for a cut shorter than the marker', () => {
    const built = composer(new FakeSections());

    const bottom = line(
      'x'.repeat(ItemTooltipComposer.MaxTooltipLength - 5) + '\n',
      ItemTooltipSection.EtherealSocketed,
      ItemTooltipColor.Magic,
    );

    const top = line('TOP\n', ItemTooltipSection.ItemName, ItemTooltipColor.Unique);

    expect(built.render([top, bottom])).toContain('\n');
  });

  it('skips merging entirely when there is no terminator', () => {
    const sections = new FakeSections().set(ItemTooltipSection.ItemName, 'Name');
    sections.lineTerminator = null;

    const lines = composer(sections).compose(generic(), NoStats);

    expect(lines).toHaveLength(1);
    expect(lines[0]?.text).toBe('Name');
  });

  it('advances the merge scan past a terminated run', () => {
    const sections = new FakeSections()
      .set(ItemTooltipSection.EtherealSocketed, 'Eth\n')
      .set(ItemTooltipSection.Durability, 'Dur\n')
      .set(ItemTooltipSection.ItemName, 'Name\n');

    const built = composer(sections);
    const lines = built.compose(generic(), NoStats);

    expect(lines).toHaveLength(3);
    expect(built.render(lines)).toBe('Name\nDur\nEth');
  });

  it('falls back for empty text in lastEmbeddedColor', () => {
    const built = composer(new FakeSections());

    // The abandoned-line boundary row reads the colour of the line above it; an empty one
    // must fall back rather than throw.
    const bottom = line(
      'x'.repeat(ItemTooltipComposer.MaxTooltipLength - 3) + '\n',
      ItemTooltipSection.EtherealSocketed,
      ItemTooltipColor.Magic,
    );

    const middle = line('', ItemTooltipSection.Durability, ItemTooltipColor.Red);
    const top = line('TOP\n', ItemTooltipSection.ItemName, ItemTooltipColor.Unique);

    expect(built.renderWithColorCodes([top, middle, bottom])).not.toContain('TOP');
  });

  it('supplies a terminator for a section that has none', () => {
    const sections = new FakeSections()
      .set(ItemTooltipSection.ItemName, 'Name')
      .set(ItemTooltipSection.Durability, 'A\nB');

    const lines = composer(sections).compose(generic(), NoStats);

    const durability = lines.filter(l => l.section === ItemTooltipSection.Durability);

    expect(durability).toHaveLength(2);
    // Display order, so the second appended part comes first.
    expect(durability[0]?.text).toBe('B\n');
    expect(durability[1]?.text).toBe('A\n');
  });

  it('keeps a pre-joined modifier missing its terminator', () => {
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(DamageStatIds.FireMinDamage, ItemDescFunc.PlusValueString, 100));

    const strings = new FakeStringTable().withPunctuation().add(DamageStringIds.FireSingle, 'raw');

    const values = new FakeStatValues()
      .addBase(DamageStatIds.FireMinDamage, 10)
      .addBase(DamageStatIds.FireMaxDamage, 5);

    const sections = new FakeSections().set(ItemTooltipSection.ItemName, 'Name\n');
    const built = new ItemTooltipComposer(
      sections,
      new ItemDescriptionGenerator(stats, strings, values),
    );

    const lines = built.compose(generic(), [[DamageStatIds.FireMinDamage, 10]]);

    // Unterminated and merged into the following section.
    expect(
      lines.some(
        l => l.section === ItemTooltipSection.Modifiers && (l.text ?? '').startsWith('raw'),
      ),
    ).toBe(true);
  });

  it('picks the red colour for unmet requirements', () => {
    const sections = new FakeSections()
      .set(ItemTooltipSection.ItemName, 'Name\n')
      .set(ItemTooltipSection.RequiredLevel, 'Level 10\n')
      .set(ItemTooltipSection.RequiredStrength, 'Str 20\n')
      .set(ItemTooltipSection.RequiredDexterity, 'Dex 30\n')
      .set(ItemTooltipSection.ClassRestriction, 'Amazon Only\n')
      .unmeetable(ItemTooltipSection.RequiredLevel)
      .unmeetable(ItemTooltipSection.RequiredStrength)
      .unmeetable(ItemTooltipSection.RequiredDexterity)
      .unmeetable(ItemTooltipSection.ClassRestriction);

    const lines = composer(sections).compose(generic(), NoStats);

    for (const l of lines.filter(x => x.section !== ItemTooltipSection.ItemName)) {
      expect(l.color).toBe(ItemTooltipColor.Red);
    }
  });

  it('skips the weapon and armour block for a non weapon item', () => {
    const sections = new FakeSections()
      .set(ItemTooltipSection.ItemName, 'Name\n')
      .set(ItemTooltipSection.ArmorClass, 'Defense: 100\n')
      .set(ItemTooltipSection.WeaponDamage, 'Damage: 1-2\n')
      .set(ItemTooltipSection.CharmDescription, 'Charm\n');

    const context = generic();
    context.isWeaponOrArmorType = false;

    const lines = composer(sections).compose(context, NoStats);

    expect(lines.some(l => l.section === ItemTooltipSection.ArmorClass)).toBe(false);
    expect(lines.some(l => l.section === ItemTooltipSection.WeaponDamage)).toBe(false);
    expect(lines.some(l => l.section === ItemTooltipSection.CharmDescription)).toBe(true);
  });

  it('shows the transaction cost only while a page is open', () => {
    const sections = new FakeSections()
      .set(ItemTooltipSection.ItemName, 'Name\n')
      .set(ItemTooltipSection.TransactionCost, 'Cost: 5\n');

    const built = composer(sections);

    const closed = generic();
    closed.shopMode = 0;
    expect(
      built.compose(closed, NoStats).some(l => l.section === ItemTooltipSection.TransactionCost),
    ).toBe(false);

    const tooHigh = generic();
    tooHigh.shopMode = 10;
    expect(
      built.compose(tooHigh, NoStats).some(l => l.section === ItemTooltipSection.TransactionCost),
    ).toBe(false);

    const open = generic();
    open.shopMode = 4;
    expect(
      built.compose(open, NoStats).some(l => l.section === ItemTooltipSection.TransactionCost),
    ).toBe(true);
  });

  it('takes the unidentified section and no stat block for an unidentified item', () => {
    const sections = new FakeSections()
      .set(ItemTooltipSection.ItemName, 'Name\n')
      .set(ItemTooltipSection.Unidentified, 'Unidentified\n');

    const context = generic();
    context.flags = ItemTooltipFlags.None;

    const lines = composer(sections).compose(context, [[1, 5]]);

    expect(lines.some(l => l.section === ItemTooltipSection.Unidentified)).toBe(true);
    expect(lines.some(l => l.section === ItemTooltipSection.Modifiers)).toBe(false);
  });

  it('reaches every name colour override', () => {
    const sections = new FakeSections().set(ItemTooltipSection.ItemName, 'Name\n');
    const built = composer(sections);

    function nameColor(configure: (context: ItemTooltipContext) => void): number {
      const context = generic();
      configure(context);

      return single(built.compose(context, NoStats), l => l.section === ItemTooltipSection.ItemName)
        .color;
    }

    expect(nameColor(c => (c.quality = ItemQuality.Magic))).toBe(ItemTooltipColor.Magic);
    expect(
      nameColor(c => {
        c.quality = ItemQuality.Set;
        c.flags = ItemTooltipFlags.None;
      }),
    ).toBe(ItemTooltipColor.Set);
    expect(nameColor(c => (c.quality = ItemQuality.Rare))).toBe(ItemTooltipColor.Rare);
    expect(nameColor(c => (c.quality = ItemQuality.Unique))).toBe(ItemTooltipColor.Unique);
    expect(nameColor(c => (c.quality = ItemQuality.Crafted))).toBe(ItemTooltipColor.Crafted);
    expect(nameColor(c => (c.quality = ItemQuality.Tempered))).toBe(ItemTooltipColor.Tempered);

    expect(nameColor(c => (c.quality = ItemQuality.Normal))).toBe(ItemTooltipColor.White);

    expect(
      nameColor(c => {
        c.quality = ItemQuality.Normal;
        c.flags |= ItemTooltipFlags.Socketed;
      }),
    ).toBe(ItemTooltipColor.SocketedOrEthereal);

    expect(
      nameColor(c => {
        c.quality = ItemQuality.Normal;
        c.flags |= ItemTooltipFlags.Ethereal;
      }),
    ).toBe(ItemTooltipColor.SocketedOrEthereal);

    expect(nameColor(c => (c.unidentifiedInShop = true))).toBe(ItemTooltipColor.White);

    expect(nameColor(c => (c.forcesCraftedColor = true))).toBe(ItemTooltipColor.Crafted);

    expect(nameColor(c => (c.flags |= ItemTooltipFlags.Broken))).toBe(ItemTooltipColor.Red);
  });

  it('survives the empty name and cut paths with a null terminator', () => {
    const sections = new FakeSections().set(ItemTooltipSection.RuneLetters, "'Runes'");
    sections.lineTerminator = null;

    const built = composer(sections);
    const lines = built.compose(generic(), NoStats);

    expect(lines).toHaveLength(2);
    expect(lines[0]?.text).toBe('');

    // And a line longer than the budget still cuts without a terminator to re-attach.
    const cut = built.render([line('y'.repeat(ItemTooltipComposer.MaxTooltipLength + 5))]);

    expect(cut).toHaveLength(ItemTooltipComposer.MaxTooltipLength - 3);
  });

  it('does not trigger a merge on null line text', () => {
    const built = composer(new FakeSections());

    expect(built.render([line('top\n'), line(null), line('bottom\n')])).toBe('top\nbottom');
  });

  it('merges an unterminated run inside one section without a marker', () => {
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(DamageStatIds.FireMinDamage, ItemDescFunc.PlusValueString, 100));
    stats.add(Build.stat(39, ItemDescFunc.PlusValueString, 101, { priority: 90 }));

    const strings = new FakeStringTable()
      .withPunctuation()
      .add(DamageStringIds.FireSingle, 'raw')
      .add(101, 'Fire Resist');

    const values = new FakeStatValues()
      .addBase(DamageStatIds.FireMinDamage, 10)
      .addBase(DamageStatIds.FireMaxDamage, 5);

    const sections = new FakeSections().set(ItemTooltipSection.ItemName, 'Name\n');
    const built = new ItemTooltipComposer(
      sections,
      new ItemDescriptionGenerator(stats, strings, values),
    );

    const lines = built.compose(generic(), [
      [DamageStatIds.FireMinDamage, 10],
      [39, 30],
    ]);

    const merged = single(
      lines,
      l => l.section === ItemTooltipSection.Modifiers && (l.text ?? '').startsWith('raw'),
    );

    // Same section, so no marker is spliced in.
    expect(merged.text).not.toContain(ItemTooltipColor.Marker);
  });

  it('reserves one character for the digit even with a null marker', () => {
    const built = composer(new FakeSections());

    const bottom = line(
      'x'.repeat(ItemTooltipComposer.MaxTooltipLength - 2) + '\n',
      ItemTooltipSection.EtherealSocketed,
      ItemTooltipColor.Magic,
    );

    const colored = built.renderWithColorCodes(
      [line('TOP\n', ItemTooltipSection.ItemName), bottom],
      null,
    );

    expect(colored).not.toContain('TOP');
  });

  it('makes the boundary row just a terminator with no marker string', () => {
    const built = composer(new FakeSections());

    const bottom = line(
      'x'.repeat(ItemTooltipComposer.MaxTooltipLength - 3) + '\n',
      ItemTooltipSection.EtherealSocketed,
      ItemTooltipColor.Magic,
    );

    const colored = built.renderWithColorCodes(
      [line('TOP\n', ItemTooltipSection.ItemName), bottom],
      '',
    );

    expect(colored).not.toContain('TOP');

    // The boundary row stands in for the section the cut dropped, so it OWNS that section's marker
    // (AppendAsWideChar checks only that the buffer is non-empty, 0x4521cd). With no marker string
    // that is the bare digit and the terminator.
    expect(colored.startsWith('3\n')).toBe(true);
  });

  it('needs no adjustment for a cut that is shorter than a marker', () => {
    const built = composer(new FakeSections());

    // Leaves a cut of 2, shorter than the 3-character marker, so no pull-back applies.
    const bottom = line(
      'x'.repeat(ItemTooltipComposer.MaxTooltipLength - 9) + '\n',
      ItemTooltipSection.EtherealSocketed,
      ItemTooltipColor.Magic,
    );

    const top = line('TOPTEXT\n', ItemTooltipSection.ItemName, ItemTooltipColor.Unique);

    expect(built.render([top, bottom]).startsWith('TO\n')).toBe(true);
  });

  it('falls back on the boundary row when the line above it is empty', () => {
    const built = composer(new FakeSections());

    const bottom = line(
      'x'.repeat(ItemTooltipComposer.MaxTooltipLength - 10) + '\n',
      ItemTooltipSection.EtherealSocketed,
      ItemTooltipColor.Magic,
    );

    const middle = line('', ItemTooltipSection.Durability, ItemTooltipColor.Red);
    const top = line('TOP\n', ItemTooltipSection.ItemName, ItemTooltipColor.Unique);

    const rendered = built.render([top, middle, bottom]);

    expect(rendered).not.toContain('TOP');
    expect(rendered.startsWith('\n')).toBe(true);
  });

  it('turns a null modifier text into an empty terminated line', () => {
    const stats = new FakeStatCostTable();
    stats.add(Build.stat(1, ItemDescFunc.PlusValueString, 100));

    const strings = new FakeStringTable().withPunctuation().add(100, '');

    const sections = new FakeSections().set(ItemTooltipSection.ItemName, 'Name\n');
    const built = new ItemTooltipComposer(sections, new ItemDescriptionGenerator(stats, strings));

    const lines = built.compose(generic(), [[1, 5]]);

    expect(lines.some(l => l.section === ItemTooltipSection.Modifiers)).toBe(true);
  });
});
