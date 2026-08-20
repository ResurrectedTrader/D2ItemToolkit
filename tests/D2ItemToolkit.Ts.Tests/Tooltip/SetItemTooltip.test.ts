import { describe, expect, it } from 'vitest';
import {
  ItemDescFunc,
  ItemDescriptionGenerator,
} from '../../../src/D2ItemToolkit.Ts/src/Description/ItemDescription.js';
import {
  ItemIdentity,
  ItemRecordReader,
} from '../../../src/D2ItemToolkit.Ts/src/Stats/ItemRecord.js';
import { PropertyApplier } from '../../../src/D2ItemToolkit.Ts/src/Stats/PropertyApplier.js';
import {
  ItemStatReader,
  ItemStatView,
} from '../../../src/D2ItemToolkit.Ts/src/Stats/ItemStatReader.js';
import { unitFromJson, type Unit } from '../../../src/D2ItemToolkit.Ts/src/Stats/Unit.js';
import { ItemTable } from '../../../src/D2ItemToolkit.Ts/src/Tables/ItemTable.js';
import { ItemTypeTree } from '../../../src/D2ItemToolkit.Ts/src/Tables/ItemTypeTree.js';
import { SetTable } from '../../../src/D2ItemToolkit.Ts/src/Tables/SetTable.js';
import { D2DataFiles } from '../../../src/D2ItemToolkit.Ts/src/Tables/TxtDataProviders.js';
import {
  type IItemTooltipSections,
  ItemQuality,
  ItemTooltipColor,
  ItemTooltipComposer,
  ItemTooltipContext,
  ItemTooltipFlags,
  ItemTooltipKind,
  ItemTooltipSection,
  type ItemTooltipLine,
} from '../../../src/D2ItemToolkit.Ts/src/Tooltip/ItemTooltip.js';
import {
  SetItemTooltipBuilder,
  selectSetBonusTiers,
  type SetItemTooltipContent,
  type SetItemTooltipInput,
  type SetPieceLine,
} from '../../../src/D2ItemToolkit.Ts/src/Tooltip/SetItemTooltip.js';
import { TooltipEngine } from '../../../src/D2ItemToolkit.Ts/src/Tooltip/TooltipEngine.js';
import { Build, FakeStatCostTable, FakeStringTable } from '../Fakes.js';

// SetItemTooltipTests.cs. ITEM_BuildSetItemTooltip 0x48d1d0.

const Data = D2DataFiles.load();
const Marker = ItemTooltipColor.Marker;

class FakeSections implements IItemTooltipSections {
  readonly texts = new Map<ItemTooltipSection, string | null>();
  readonly unmet = new Set<ItemTooltipSection>();

  lineTerminator: string | null = '\n';

  set(section: ItemTooltipSection, text: string | null): FakeSections {
    this.texts.set(section, text);
    return this;
  }

  unmeetable(section: ItemTooltipSection): FakeSections {
    this.unmet.add(section);
    return this;
  }

  getSection(section: ItemTooltipSection): string | null {
    return this.texts.get(section) ?? null;
  }

  isRequirementUnmet(section: ItemTooltipSection): boolean {
    return this.unmet.has(section);
  }
}

function setContext(options?: {
  flags?: number;
  weaponOrArmor?: boolean;
  shield?: boolean;
  shopMode?: number;
}): ItemTooltipContext {
  const context = new ItemTooltipContext();
  context.quality = ItemQuality.Set;
  context.flags = (options?.flags ?? 0) | ItemTooltipFlags.Identified;
  context.isWeaponOrArmorType = options?.weaponOrArmor ?? false;
  context.isShieldType = options?.shield ?? false;
  context.shopMode = options?.shopMode ?? 0;
  return context;
}

function composer(sections: FakeSections): ItemTooltipComposer {
  const stats = new FakeStatCostTable();
  stats.add(Build.stat(39, ItemDescFunc.PlusValueString, 101, { priority: 50 }));

  const strings = new FakeStringTable().withPunctuation().add(101, 'Fire Resist');

  return new ItemTooltipComposer(sections, new ItemDescriptionGenerator(stats, strings));
}

function content(options?: {
  pieces?: string[];
  owned?: boolean[];
  setName?: string;
  full?: string;
  partial?: string;
}): SetItemTooltipContent {
  const names = options?.pieces ?? [];
  const owned = options?.owned ?? [];

  const pieces: SetPieceLine[] = names.map((name, i) => ({
    text: name + '\n',
    owned: owned[i] ?? false,
  }));

  return {
    pieces,
    setName: options?.setName ?? 'Angelic Raiment\n',
    fullSetText: options?.full ?? '',
    partialText: options?.partial ?? '',
    transactionRefusedText: 'Item cannot be traded here.\n',
  };
}

const NoStats: readonly (readonly [number, number])[] = [];

describe('the set-item writer', () => {
  /**
   * Test 1. The piece list is appended in setitems.txt row order (0x48d88e-0x48d92a) and
   * D2WINFONT_DrawWideString steps the cursor UPWARDS (0x501c17), so the LAST row of setitems.txt
   * is the highest of the four on screen.
   */
  it('renders the piece list in reverse setitems order', () => {
    const sections = new FakeSections().set(ItemTooltipSection.ItemName, 'Angelic Halo');

    const lines = composer(sections).composeSetItem(
      setContext(),
      content({
        pieces: ['Angelic Sickle', 'Angelic Mantle', 'Angelic Halo', 'Angelic Wings'],
        owned: [false, false, true, true],
      }),
      NoStats,
    );

    const rows = composer(sections).render(lines).split('\n');

    expect(rows.slice(rows.length - 4)).toEqual([
      'Angelic Wings',
      'Angelic Halo',
      'Angelic Mantle',
      'Angelic Sickle',
    ]);
  });

  /**
   * Test 2. The set name is appended AFTER the list (0x48d958 versus 0x48d93b), so it sits one row
   * above it, gold.
   */
  it('puts the set name directly above the piece list in gold', () => {
    const sections = new FakeSections().set(ItemTooltipSection.ItemName, 'Angelic Halo');

    const display = composer(sections).composeSetItem(
      setContext(),
      content({ pieces: ['Angelic Sickle', 'Angelic Wings'] }),
      NoStats,
    );

    const name = display[display.length - 3] as ItemTooltipLine;
    expect(name.section).toBe(ItemTooltipSection.SetName);
    expect(name.color).toBe(ItemTooltipColor.Unique);
    expect(name.text).toBe('Angelic Raiment\n');

    expect((display[display.length - 2] as ItemTooltipLine).section).toBe(
      ItemTooltipSection.SetPieceList,
    );
    expect((display[display.length - 1] as ItemTooltipLine).section).toBe(
      ItemTooltipSection.SetPieceList,
    );
  });

  /**
   * Test 3. 0x48d9a9 appends str(3998) with no test in front of it, so there is always one blank
   * row above the set name — even with both bonus blocks empty.
   */
  it('always separates the set name from what is above it with one blank row', () => {
    const sections = new FakeSections().set(ItemTooltipSection.ItemName, 'Angelic Halo');

    const lines = composer(sections).composeSetItem(
      setContext(),
      content({ pieces: ['Angelic Sickle'] }),
      NoStats,
    );

    expect(composer(sections).render(lines)).toBe(
      'Angelic Halo\n\nAngelic Raiment\nAngelic Sickle',
    );
  });

  /**
   * Test 4. The SECOND blank comes from 0x48d97f, which sits inside the `var_3390 is non-empty`
   * test at 0x48d96a.
   */
  it('adds a second blank row only with a full-set block', () => {
    const sections = new FakeSections().set(ItemTooltipSection.ItemName, 'Halo');

    const without = composer(sections).composeSetItem(
      setContext(),
      content({ pieces: ['Sickle'] }),
      NoStats,
    );

    expect(
      composer(sections)
        .render(without)
        .split('\n')
        .filter(r => r.length === 0).length,
    ).toBe(1);

    const withFull = composer(sections).composeSetItem(
      setContext(),
      content({ pieces: ['Sickle'], full: '+10 to All Attributes\n' }),
      NoStats,
    );

    expect(composer(sections).render(withFull)).toBe(
      'Halo\n\n+10 to All Attributes\n\nAngelic Raiment\nSickle',
    );
  });

  /**
   * Test 5. `dwAnimMode == 1` at 0x48d870 is the whole gate on the full-set block, and the builder
   * folds it into an empty fullSetText.
   */
  it('suppresses the full-set block when the piece is not equipped', () => {
    const builder = realBuilder();

    const input: SetItemTooltipInput = {
      fullSetStats: [[ItemStatReader.packStatKey(0, 39), 25]],
      isEquipped: false,
    };

    const halo = angelicHaloIdentity();

    expect(builder.build(null, halo, null, new Map(), input)?.fullSetText).toBe('');

    expect(
      builder.build(null, halo, null, new Map(), { ...input, isEquipped: true })?.fullSetText,
    ).not.toBe('');
  });

  /**
   * Test 6. var_4F90 holds the ethereal/socketed text AND the modifier block, so 0x48d9e0 gives
   * the pair ONE AppendAsWideChar where the generic path spends two.
   */
  it('gives the ethereal text and the modifier block one marker', () => {
    const sections = new FakeSections()
      .set(ItemTooltipSection.ItemName, 'Halo')
      .set(ItemTooltipSection.EtherealSocketed, 'Socketed (2)\n');

    const lines = composer(sections).composeSetItem(
      setContext({ flags: ItemTooltipFlags.Socketed }),
      content({ pieces: ['Sickle'] }),
      [[ItemStatReader.packStatKey(0, 39), 25]],
    );

    const game = lines.filter(
      l =>
        l.emitsColorMarker &&
        (l.section === ItemTooltipSection.Modifiers ||
          l.section === ItemTooltipSection.EtherealSocketed),
    );

    expect(game).toHaveLength(1);
    expect(composer(sections).render(lines)).toContain('Socketed (2)');
    expect(composer(sections).render(lines)).toContain('Fire Resist');
  });

  /**
   * Test 6b. The buffer's gate is the SOCKETED flag alone (0x48d7e6), not the
   * ethereal-or-socketed test INV_FormatEtherealSocketedText itself makes.
   */
  it('drops the ethereal line on an unsocketed set item', () => {
    const sections = new FakeSections()
      .set(ItemTooltipSection.ItemName, 'Halo')
      .set(ItemTooltipSection.EtherealSocketed, 'Ethereal (Cannot Be Repaired)\n');

    const lines = composer(sections).composeSetItem(
      setContext({ flags: ItemTooltipFlags.Ethereal }),
      content({ pieces: ['Sickle'] }),
      NoStats,
    );

    expect(composer(sections).render(lines)).not.toContain('Ethereal');
  });

  /**
   * Test 7. 0x48d93b prepends a `ÿc2` to the whole assembled list, in front of the first piece's
   * own marker. AppendAsWideChar no-ops on an empty buffer (0x4521cd).
   */
  it('carries a redundant leading marker on the piece list unless it is empty', () => {
    const sections = new FakeSections().set(ItemTooltipSection.ItemName, 'Halo');

    const withPieces = composer(sections).renderWithColorCodes(
      composer(sections).composeSetItem(
        setContext(),
        content({ pieces: ['Sickle'], owned: [false] }),
        NoStats,
      ),
    );

    expect(withPieces.endsWith(Marker + '2' + Marker + '1Sickle')).toBe(true);

    const empty = composer(sections).renderWithColorCodes(
      composer(sections).composeSetItem(setContext(), content(), NoStats),
    );

    expect(empty.endsWith(Marker + '4Angelic Raiment')).toBe(true);
    expect(empty).not.toContain(Marker + '2' + Marker);
  });

  /** Test 8. 0x48d79a-0x48d7ae: flag 0x100 alone, and nothing else, reddens the name here. */
  it.each([
    [0, ItemTooltipColor.Set],
    [ItemTooltipFlags.Broken, ItemTooltipColor.Red],
  ])('colours the item name for flags %i', (flags, expected) => {
    const sections = new FakeSections().set(ItemTooltipSection.ItemName, 'Halo');

    const lines = composer(sections).composeSetItem(
      setContext({ flags }),
      content({ pieces: ['Sickle'] }),
      NoStats,
    );

    expect(lines.find(l => l.section === ItemTooltipSection.ItemName)?.color).toBe(expected);
  });

  /**
   * Test 9. 0x48d595-0x48d5ab reddens the class line only for a PLAYER of the wrong class; var_28
   * is zeroed at 0x48d2eb and never written again, so everything else is white.
   */
  it.each([
    [true, ItemTooltipColor.Red],
    [false, ItemTooltipColor.White],
  ])('colours the class restriction when unmet is %s', (unmet, expected) => {
    const sections = new FakeSections()
      .set(ItemTooltipSection.ItemName, 'Halo')
      .set(ItemTooltipSection.ClassRestriction, '(Paladin Only)\n');

    if (unmet) {
      sections.unmeetable(ItemTooltipSection.ClassRestriction);
    }

    const lines = composer(sections).composeSetItem(
      setContext(),
      content({ pieces: ['Sickle'] }),
      NoStats,
    );

    expect(lines.find(l => l.section === ItemTooltipSection.ClassRestriction)?.color).toBe(
      expected,
    );
  });

  /**
   * Test 10. LoadItemDesc truncates at 0x48ed12; ITEM_BuildSetItemTooltip runs from
   * MoveArgumentToEAX 0x48db0b straight to TEXT_CalcTextDimensions 0x48db1d over a 2048-WCHAR
   * buffer with no guard.
   */
  it('cuts nothing at 1023 characters', () => {
    const pieces: string[] = [];
    for (let i = 0; i < 6; ++i) {
      pieces.push(String.fromCharCode(0x61 + i).repeat(300));
    }

    const sections = new FakeSections().set(ItemTooltipSection.ItemName, 'Halo');

    const lines = composer(sections).composeSetItem(setContext(), content({ pieces }), NoStats);

    const rendered = composer(sections).render(
      lines,
      false,
      ItemTooltipComposer.UnlimitedTooltipLength,
    );

    expect(rendered.length).toBeGreaterThan(1800);
    for (const piece of pieces) {
      expect(rendered).toContain(piece);
    }

    // The budget is a knob, not a removal: the generic default still cuts.
    expect(composer(sections).render(lines).length).toBeLessThanOrEqual(
      ItemTooltipComposer.MaxTooltipLength,
    );
  });

  /**
   * Test 11. INV_FormatDefenseRangeText is reached only inside `IsOfType(item, 51)` (0x48d68a),
   * so a set BOOT never gets the Kick Damage line the generic path gives an Assassin.
   */
  it('gives a set boot no kick damage line', () => {
    const sections = new FakeSections()
      .set(ItemTooltipSection.ItemName, 'Boots')
      .set(ItemTooltipSection.SmiteOrKickDamage, 'Kick Damage: 3 to 8\n');

    const boots = composer(sections).composeSetItem(
      setContext({ weaponOrArmor: true }),
      content({ pieces: ['Sickle'] }),
      NoStats,
    );

    expect(composer(sections).render(boots)).not.toContain('Kick Damage');

    // The same buffer on a SHIELD is emitted, so the gate is the shield test and not a blanket
    // suppression.
    const shield = composer(sections).composeSetItem(
      setContext({ weaponOrArmor: true, shield: true }),
      content({ pieces: ['Sickle'] }),
      NoStats,
    );

    expect(composer(sections).render(shield)).toContain('Kick Damage');
  });

  /**
   * Test 12. There is no call site for any of these in the writer's 638 instructions, so a
   * provider that offers them must be ignored.
   */
  it('never emits the sections the writer does not call', () => {
    const sections = new FakeSections()
      .set(ItemTooltipSection.ItemName, 'Halo')
      .set(ItemTooltipSection.QuestUsage, 'Right click to open\n')
      .set(ItemTooltipSection.Unidentified, 'Unidentified\n')
      .set(ItemTooltipSection.SocketFillerDescription, 'Weapons: +1\n')
      .set(ItemTooltipSection.CharmDescription, 'Keep in inventory\n')
      .set(ItemTooltipSection.QuantityAndSpellDescription, 'Quantity: 20\n')
      .set(ItemTooltipSection.RuneLetters, "'RalOrt'\n");

    const rendered = composer(sections).render(
      composer(sections).composeSetItem(
        setContext({ weaponOrArmor: true, shield: true }),
        content({ pieces: ['Sickle'] }),
        NoStats,
      ),
    );

    expect(rendered).toBe('Halo\n\nAngelic Raiment\nSickle');
  });

  /** Test 13. `add func` 0 leaves v7 at -1 and neither arm runs (0x4e65a3). */
  it('selects no tier for add func zero', () => {
    expect(selectSetBonusTiers(0, 0, 0x3f, 0x3f)).toEqual([]);
  });

  /**
   * Test 14. `add func` 2 counts the mask through dword_6DBD90 and lights tiers 0..N-2 (0x4e65c7 /
   * 0x4e65ce), so six worn pieces still never reach STATE_ITEMSET6.
   */
  it.each([
    [0x00, []],
    [0x01, []],
    [0x03, [165]],
    [0x07, [165, 166]],
    [0x3f, [165, 166, 167, 168, 169]],
  ])('lights one tier fewer than the pieces worn for mask %i', (mask, expected) => {
    expect(selectSetBonusTiers(2, 0, mask, 0)).toEqual(expected);
  });

  /**
   * Test 15. `add func` 1 maps the WORN slot to a tier, collapsing over the gap this piece leaves
   * (0x4e662f).
   */
  it('maps each worn sibling to its own tier for add func one', () => {
    expect(selectSetBonusTiers(1, 2, 0, (1 << 0) | (1 << 4))).toEqual([165, 168]);

    // Self is skipped outright even when its own bit is set.
    expect(selectSetBonusTiers(1, 2, 0, (1 << 0) | (1 << 2))).toEqual([165]);
  });

  /**
   * Test 16. THE SPEC HAD THIS BACKWARDS. `docs/set-item-tooltip.md` §11 claimed an unequipped set
   * item with worn siblings still shows a green tier, and that `includeUnearned: true` is
   * therefore the right view. It is not: SKILLDESC_BuildStatBuffDesc reaches the tier through
   * GetStatList(item, state, 0) (0x4e60ff) and a zero mask sends that down the pMyLastList chain
   * at +0x3C (0x6257ef), while STATLIST_ToggleStateDisabled parks a disabled tier on +0x40 by
   * setting STATLIST_SET (0x6279e7) and re-attaching (0x626e67). A tier carrying the bit is
   * unreachable, so the writer emits nothing.
   */
  it('renders nothing for a tier still carrying STATLIST_SET', () => {
    const builder = realBuilder();

    const input: SetItemTooltipInput = {
      wornMaskIncludingSelf: (1 << 2) | (1 << 3),
      wornMaskExcludingSelf: 1 << 3,
    };

    // Tier 0 IS selected by the arithmetic...
    expect(selectSetBonusTiers(2, 2, input.wornMaskIncludingSelf as number, 0)).toEqual([165]);

    const disabled = angelicHaloRecord(0x40 | 0x2000);
    expect(
      builder.build(disabled, angelicHaloIdentity(), player(30), merged(disabled), input)
        ?.partialText,
    ).toBe('');

    const enabled = angelicHaloRecord(0x40);
    expect(
      builder.build(enabled, angelicHaloIdentity(), player(30), merged(enabled), input)
        ?.partialText,
    ).not.toBe('');
  });

  /**
   * Test 17. Row counts are post-splice — sets.txt loses its `Expansion` divider at pre-splice
   * body index 16 and setitems.txt at 62 — and the link at 0x63668d walks setitems.txt ascending.
   */
  it('links the shipped tables in ascending setitems order', () => {
    const sets = realSets();

    expect(sets.setCount).toBe(32);
    expect(sets.pieceCount).toBe(127);

    let linked = 0;
    for (let setId = 0; setId < sets.setCount; ++setId) {
      const set = sets.setAt(setId);
      expect(set).not.toBeNull();
      expect((set as NonNullable<typeof set>).setId).toBe(setId);
      const pieces = (set as NonNullable<typeof set>).pieces;
      expect(pieces.length).toBeLessThanOrEqual(SetTable.MaxPiecesPerSet);

      for (let i = 0; i < pieces.length; ++i) {
        expect((pieces[i] as (typeof pieces)[number]).setId).toBe(setId);
        expect((pieces[i] as (typeof pieces)[number]).slot).toBe(i);

        if (i > 0) {
          expect((pieces[i - 1] as (typeof pieces)[number]).setItemId).toBeLessThan(
            (pieces[i] as (typeof pieces)[number]).setItemId,
          );
        }
      }

      linked += pieces.length;
    }

    // Every shipped piece names a set that exists and fits, so nothing is dropped.
    expect(linked).toBe(127);
  });

  /**
   * Test 18. sets.txt `name` is a KEY. Three shipped sets resolve to a different display name,
   * which is what makes resolving by key rather than by value load-bearing.
   */
  it.each([
    [13, 'Angelical Raiment', 'Angelic Raiment'],
    [11, "Berserker's Garb", "Berserker's Arsenal"],
    [31, "McAuley's Folly", "Sander's Folly"],
  ])('resolves set %i by key, not by value', (setId, key, display) => {
    const set = realSets().setAt(setId);

    expect(set?.key).toBe(key);
    expect(set?.name).toBe(display);
  });

  /** Test 19. wsprintf 0x48d8dd with locale 10089, which is bare `%0`. */
  it('writes the piece line as the name verbatim', () => {
    expect(Data.strings.getByIndex(10089)).toBe('%0');

    // setitems.txt +0x24 is the `index` cell resolved through the string table, and for a piece
    // the key and the display name happen to agree — unlike a SET, where they do not.
    const sickle = realSets().pieceAt(AngelicSickleRow);
    expect(sickle?.key).toBe('Angelic Sickle');
    expect(sickle?.name).toBe('Angelic Sickle');
    expect(sickle?.addFunc).toBe(2);
    expect(sickle?.slot).toBe(0);

    const built = realBuilder().build(
      angelicHaloRecord(0x40),
      angelicHaloIdentity(),
      player(30),
      new Map(),
      {},
    );

    expect(built?.pieces.map(p => p.text)).toEqual([
      'Angelic Sickle\n',
      'Angelic Mantle\n',
      'Angelic Halo\n',
      'Angelic Wings\n',
    ]);
  });

  /**
   * Test 20. The whole thing, against the shipped extraction: a level-30 character wearing Angelic
   * Halo and Angelic Wings, ShopMode 0.
   *
   * docs/set-item-tooltip.md §9 omits the `Ring` row. GetItemName's set arm builds
   * `base + 3998 + str(setitems[+0x24])` (0x48ca1c), so the base type is a row of its own directly
   * under the set-item name.
   *
   * §9 also predates the derived block. Two of Angelical Raiment's four pieces are worn, so
   * ITEMMOD_ApplySetBonuses takes limit = 2 * min(2, 3) - 2 = 2 and applies PCode2a (`dex 10`) and
   * PCode2b (blank, skipped at 0x6601ca) — the gold `+10 to Dexterity` row and the second blank
   * that 0x48d96a gates on it.
   */
  it('renders Angelic Halo character for character', () => {
    const tooltip = TooltipEngine.embedded.renderSetItem(
      angelicHaloRecord(0x40),
      {
        ownedSetItemIds: [AngelicHaloRow, AngelicWingsRow],
        wornMaskIncludingSelf: (1 << 2) | (1 << 3),
        wornMaskExcludingSelf: 1 << 3,
        isEquipped: true,
      },
      playerRecord(30),
    );

    expect(tooltip.kind).toBe(ItemTooltipKind.IdentifiedSetItem);

    expect(tooltip.text).toBe(
      'Angelic Halo\n' +
        'Ring\n' +
        'Required Level: 12\n' +
        '+20 to Life\n' +
        'Replenish Life +6\n' +
        '+360 to Attack Rating (Based on Character Level)\n' +
        '\n' +
        '+10 to Dexterity\n' +
        '\n' +
        'Angelic Raiment\n' +
        'Angelic Wings\n' +
        'Angelic Halo\n' +
        'Angelic Mantle\n' +
        'Angelic Sickle',
    );

    expect(tooltip.coloredText).toBe(
      Marker +
        '2Angelic Halo\n' +
        Marker +
        '2Ring\n' +
        Marker +
        '0Required Level: 12\n' +
        Marker +
        '3+20 to Life\n' +
        Marker +
        '3Replenish Life +6\n' +
        Marker +
        '2+360 to Attack Rating (Based on Character Level)\n' +
        '\n' +
        Marker +
        '4+10 to Dexterity\n' +
        '\n' +
        Marker +
        '4Angelic Raiment\n' +
        Marker +
        '2Angelic Wings\n' +
        Marker +
        '2Angelic Halo\n' +
        Marker +
        '1Angelic Mantle\n' +
        Marker +
        '2' +
        Marker +
        '1Angelic Sickle',
    );
  });

  /**
   * render classifies and routes on its own, so a set item is no longer refused — the four of
   * sixty-two that used to throw now draw.
   */
  it('routes a set item through render', () => {
    const tooltip = TooltipEngine.embedded.render(angelicHaloRecord(0x40), playerRecord(30));

    expect(tooltip.kind).toBe(ItemTooltipKind.IdentifiedSetItem);

    // No siblings supplied, so every piece is red and no tier is selected.
    expect(tooltip.text).toBe(
      'Angelic Halo\n' +
        'Ring\n' +
        'Required Level: 12\n' +
        '+20 to Life\n' +
        'Replenish Life +6\n' +
        '\n' +
        'Angelic Raiment\n' +
        'Angelic Wings\n' +
        'Angelic Halo\n' +
        'Angelic Mantle\n' +
        'Angelic Sickle',
    );
  });

  it('derives the full set block from the WEARERS own statlist', () => {
    // SKILLDESC_AppendItemBuffTextAlt 0x4e6680 walks GetStatsByState(wearer, 165+k) for k 0..5
    // (0x4e66c9) and keeps the list whose stat 71 is this set's id (0x4e66d7). So it is NOT a
    // caller input: the rolled values are already on the wearer's chain, and a viewer record that
    // carries the chain needs no SetItemTooltipInput.fullSetStats.
    //
    // Angelic Raiment is sets.txt row 13, which is what stat 71 has to hold. The C# peer is
    // tests/D2ItemToolkit.Net.Tests/Tooltip/SetItemTooltipTests.cs.
    const wearerWith = (setId: number): Unit =>
      unitFromJson({
        unitType: 0,
        classId: 0,
        statsLists: [
          { stateNo: 0, flags: 2147483648, stats: [{ id: StatLevel, value: 30 }] },
          {
            stateNo: 166,
            flags: 64,
            stats: [
              { id: StatSetValue, value: setId },
              { id: StatHpRegen, value: 20 },
            ],
          },
        ],
      });

    const input = {
      ownedSetItemIds: [AngelicHaloRow, AngelicWingsRow],
      wornMaskIncludingSelf: (1 << 2) | (1 << 3),
      wornMaskExcludingSelf: 1 << 3,
      isEquipped: true,
    };

    expect(
      TooltipEngine.embedded.renderSetItem(
        angelicHaloRecord(0x40),
        input,
        wearerWith(AngelicRaimentSetId),
      ).text,
    ).toContain('Replenish Life +20');

    // A node for a DIFFERENT set on the same chain is skipped — that is what stat 71 is for when a
    // character wears two sets at once.
    expect(
      TooltipEngine.embedded.renderSetItem(
        angelicHaloRecord(0x40),
        input,
        wearerWith(AngelicRaimentSetId + 1),
      ).text,
    ).not.toContain('Replenish Life +20');
  });

  // ------------------------------------------ the derived set-bonus block, 0x660120

  /**
   * The headline case. ITEMMOD_ApplySetBonuses 0x660120 with four of five worn takes
   * n = min(4, nSetItems - 1) = 4 (0x6601ae-0x6601b5) and limit = 2n - 2 = 6 (0x6601b7), so the
   * walk at 0x6601c4 covers PCode2a..PCode4b — the 2-, 3- and 4-piece pairs — and 0x6601fc
   * withholds the FCode block because four is short of five.
   *
   * The buffer is APPEND order, which the description engine emits lowest-DescPriority first:
   * item_magicbonus 8, hpregen 56, item_fastergethitrate 139.
   */
  it('derives the three partial bonuses for four of five Tal Rashas', () => {
    expect(derivedFullSet(FourOfFiveMask)).toBe(
      '65% Better Chance of Getting Magic Items\n' +
        'Replenish Life +10\n' +
        '+25% Faster Hit Recovery\n',
    );

    // And on screen, reversed, gold.
    const tooltip = TooltipEngine.embedded.renderSetItem(
      talRashasCrestRecord(),
      wornInput(FourOfFiveMask),
      playerRecord(50),
    );

    expect(tooltip.coloredText).toContain(
      Marker +
        '4+25% Faster Hit Recovery\n' +
        Marker +
        '4Replenish Life +10\n' +
        Marker +
        '465% Better Chance of Getting Magic Items\n',
    );
  });

  /**
   * One piece worn gives limit = 2 * 1 - 2 = 0, and `test eax,eax / jle` at 0x6601c2 skips the
   * partial walk outright. Two pieces is the first mask that draws anything.
   */
  it('derives nothing from one worn piece', () => {
    expect(derivedFullSet(1 << 4)).toBe('');
    expect(derivedFullSet((1 << 0) | (1 << 4))).toBe('Replenish Life +10\n');
  });

  /**
   * The partial walk SKIPS a blank slot (0x6601ca) where the full walk BREAKS at one (0x660209).
   * Tal Rasha's has PCode2b, 3b and 4b blank, so three worn pieces must still reach PCode3a — a
   * walk that stopped at the first blank would show only PCode2a.
   */
  it('skips a blank partial slot rather than ending the walk', () => {
    expect(derivedFullSet((1 << 0) | (1 << 1) | (1 << 4))).toBe(
      '65% Better Chance of Getting Magic Items\nReplenish Life +10\n',
    );
  });

  /**
   * 0x6601fc compares the worn count against sets[+0x0C] itself, not against one less, so the
   * FCode block waits for the whole set. `state` (FCode6, func 24) writes stat 98, which
   * ItemStatCost.txt gives no `descfunc` — it renders nothing in either engine.
   */
  it('shows the full code block only when every piece is worn', () => {
    expect(derivedFullSet(FourOfFiveMask)).not.toContain('Sorceress');

    expect(derivedFullSet(0x1f)).toBe(
      '65% Better Chance of Getting Magic Items\n' +
        'All Resistances +50\n' +
        'Replenish Life +10\n' +
        '+150 to Life\n' +
        '+50 Defense vs. Missile\n' +
        '+150 Defense\n' +
        '+25% Faster Hit Recovery\n' +
        '+3 to Sorceress Skill Levels\n',
    );
  });

  /**
   * Precedence is supplied input, then the wearer's chain, then the derivation — the first two are
   * what the game itself reads (0x4e66c9), the third only reconstructs them.
   */
  it('prefers a supplied full set block over the derivation', () => {
    const supplied = realBuilder().build(
      talRashasCrestRecord(),
      ItemRecordReader.readIdentity(talRashasCrestRecord()),
      player(50),
      new Map(),
      {
        ...wornInput(FourOfFiveMask),
        fullSetStats: [[ItemStatReader.packStatKey(0, 39), 25]],
      },
    );

    expect(supplied?.fullSetText).toBe('Fire Resist +25%\n');

    // And so does the wearer's own STATE_ITEMSET list, which sits between the two.
    const wearer = unitFromJson({
      unitType: 0,
      classId: 0,
      statsLists: [
        { stateNo: 0, flags: 2147483648, stats: [{ id: StatLevel, value: 50 }] },
        {
          stateNo: 165,
          flags: 64,
          stats: [
            { id: StatSetValue, value: TalRashasSetId },
            { id: StatHpRegen, value: 7 },
          ],
        },
      ],
    });

    const fromWearer = realBuilder().build(
      talRashasCrestRecord(),
      ItemRecordReader.readIdentity(talRashasCrestRecord()),
      player(50),
      new Map(),
      wornInput(FourOfFiveMask),
      wearer,
    );

    expect(fromWearer?.fullSetText).toBe('Replenish Life +7\n');
  });

  /**
   * Why the derivation is sound at all: the applier rolls FMin..FMax, but shipped data barely
   * rolls. Counted over the 32 post-splice sets.txt rows and all sixteen property slots each —
   * eight partial at +0x10 and eight full at +0x90 — 220 slots carry a code, and only three have
   * Min != Max.
   */
  /**
   * The guard that stops a missing property func being SILENT. An unhandled func applies nothing,
   * so the stat never exists and the line simply is not drawn — nothing fails. The gems path has
   * had this since it was written; sets.txt did not, which is why funcs 21 and 22 were found by
   * reading the data rather than by a red test, after 9 of 32 sets had been silently dropping lines
   * like `+3 to Sorceress Skill Levels`.
   *
   * The C# peer is tests/D2ItemToolkit.Net.Tests/Tooltip/SetItemTooltipTests.cs.
   */
  it('reaches an implemented func for every shipped set property', () => {
    const applier = new PropertyApplier(
      Data,
      new ItemTable(Data.weapons, Data.armor, Data.misc),
      new ItemTypeTree(Data.itemTypes),
    );

    const sets = realSets();
    sets.resolvePropertyCodesWith(code => applier.properties.rowForCode(code));

    const item = new ItemIdentity();
    const stats = new Map<number, number>();
    let applied = 0;

    for (let setId = 0; setId < sets.setCount; ++setId) {
      for (const property of [...sets.partialProperties(setId), ...sets.fullProperties(setId)]) {
        if (property.propertyId < 0) {
          continue;
        }

        ++applied;
        // `push 4` at 0x6601df and 0x66021e — PROPMODE for a set bonus.
        applier.apply(4, item, property, stats);
      }
    }

    // The walk really reached the applier: a resolver that failed would leave every propertyId at
    // -1 and skip the body, and an empty unsupportedFunc would then prove nothing at all.
    expect(applied).toBe(220);
    expect(stats.size).toBeGreaterThan(0);

    expect([...applier.unsupportedFunc]).toEqual([]);

    // And nothing shipped takes func 11's item-level arms — Cow King's `gethit-skill` has max 5,
    // so its level is verbatim.
    expect([...applier.itemLevelDependent]).toEqual([]);
  });

  it('finds no shipped set property that is actually rolled', () => {
    // `Min !== Max` is NOT evidence of a roll. It only means that where the two columns are a
    // range, and Properties.txt decides that per func:
    //
    //   Vidala's Rig    FCode1 dmg-cold     15..20  func 15 coldmindam + 16 coldmaxdam
    //                                               -> "adds 15-20 cold damage", both ends real
    //   Cathan's Traps  PCode2a dmg-fire    15..20  func 15 firemindam + 16 firemaxdam, same
    //   Cow King's      FCode5 gethit-skill 25..5   func 11 item_skillongethit, where Min is the
    //                                               %% chance and Max the skill LEVEL — "25%% chance
    //                                               to cast level 5 when struck", not an inverted
    //                                               range
    //
    // So all 220 slots are deterministic and the derivation is exact for every one whose func is
    // implemented. This asserted "3 rolled" while that heuristic was believed.
    const sets = realSets();
    sets.resolvePropertyCodesWith(code => (code.length === 0 ? -1 : 0));

    let total = 0;
    const rolled: string[] = [];

    for (let setId = 0; setId < sets.setCount; ++setId) {
      for (const property of [...sets.partialProperties(setId), ...sets.fullProperties(setId)]) {
        if (property.propertyId < 0) {
          continue;
        }

        ++total;
        if (property.min !== property.max) {
          rolled.push(sets.setAt(setId)?.key ?? '');
        }
      }
    }

    expect(total).toBe(220);

    // Exactly three slots carry Min !== Max, and every one is a two-parameter property rather than
    // a range to roll — so all 220 are deterministic.
    expect(rolled).toEqual(["Vidala's Rig", "Cathan's Traps", "Cow King's Leathers"]);
  });

  /**
   * sets[+0x0C] is the count the link loop built (`inc` at 0x6366ff, capped at six by 0x6366df),
   * and the derivation feeds it straight into `min(count, nSetItems - 1)`. It is pieces.length and
   * nothing else — there is no separate column.
   */
  it('uses the linked piece count as the member count', () => {
    expect(realSets().setAt(TalRashasSetId)?.pieces.length).toBe(5);
    expect(realSets().pieceAt(TalRashasCrestRow)?.setId).toBe(TalRashasSetId);
    expect(realSets().pieceAt(TalRashasCrestRow)?.slot).toBe(4);
  });
});

// ---------------------------------------------------------------------- fixtures

const AngelicSickleRow = 50;
const AngelicHaloRow = 52;
const AngelicWingsRow = 53;

const StatMaxHp = 7;
const StatHpRegen = 74;
const StatToHitPerLevel = 224;
const StatLevel = 12;

/** itemstatcost `value`, post-splice row 71. */
const StatSetValue = 71;

/** sets.txt row for "Angelical Raiment" (docs/set-item-tooltip.md §9). */
const AngelicRaimentSetId = 13;

function realSets(): SetTable {
  return new SetTable(Data.sets, Data.setItems, Data.strings);
}

function realBuilder(): SetItemTooltipBuilder {
  return new SetItemTooltipBuilder(
    Data,
    realSets(),
    new ItemTable(Data.weapons, Data.armor, Data.misc),
    new ItemTypeTree(Data.itemTypes),
  );
}

/**
 * setitems.txt post-splice row 52: `Angelic Halo`, item `rin`, add func 2, prop1 regen 6, prop2
 * hp 20, aprop1a att/lvl 24. maxhp carries ValShift 8, so +20 is stored as 5120.
 */
function angelicHaloRecord(tierFlags: number): Unit {
  const items = new ItemTable(Data.weapons, Data.armor, Data.misc);

  return unitFromJson({
    unitType: 4,
    classId: items.classIdForCode('rin'),
    quality: 5,
    itemFlags: 16,
    fileIndex: AngelicHaloRow,
    statsLists: [
      {
        stateNo: 0,
        flags: 64,
        stats: [
          { id: StatHpRegen, value: 6 },
          { id: StatMaxHp, value: 20 << 8 },
        ],
      },
      { stateNo: 165, flags: tierFlags, stats: [{ id: StatToHitPerLevel, value: 24 }] },
    ],
  });
}

function playerRecord(level: number): Unit {
  return unitFromJson({
    unitType: 0,
    classId: 0,
    statsLists: [{ stateNo: 0, flags: 2147483648, stats: [{ id: StatLevel, value: level }] }],
  });
}

function player(level: number) {
  return ItemRecordReader.readViewer(playerRecord(level));
}

function angelicHaloIdentity() {
  return ItemRecordReader.readIdentity(angelicHaloRecord(0x40));
}

function merged(record: Unit): ReadonlyMap<number, number> {
  return ItemStatReader.reconstructView(record, ItemStatView.equipped());
}

/** setitems.txt post-splice row 80 — Tal Rasha's Horadric Crest, `xsk`, add func blank, slot 4. */
const TalRashasCrestRow = 80;

/** sets.txt post-splice row 19 — `Tal Rasha's Wrappings`, five members. */
const TalRashasSetId = 19;

/**
 * The mask for the Crest plus three siblings: bits 0, 1, 2 and 4, its own slot included
 * (ITEMS_GetEquippedSetItemsMask is asked with includeSelf = 1 at 0x66018b).
 */
const FourOfFiveMask = (1 << 0) | (1 << 1) | (1 << 2) | (1 << 4);

function talRashasCrestRecord(): Unit {
  const items = new ItemTable(Data.weapons, Data.armor, Data.misc);

  return unitFromJson({
    unitType: 4,
    classId: items.classIdForCode('xsk'),
    quality: 5,
    itemFlags: 16,
    fileIndex: TalRashasCrestRow,
    statsLists: [{ stateNo: 0, flags: 2147483648, stats: [{ id: 31, value: 100 }] }],
  });
}

function wornInput(mask: number): SetItemTooltipInput {
  return {
    wornMaskIncludingSelf: mask,
    wornMaskExcludingSelf: mask & ~(1 << 4),
    isEquipped: true,
  };
}

function derivedFullSet(mask: number): string {
  return (
    realBuilder().build(
      talRashasCrestRecord(),
      ItemRecordReader.readIdentity(talRashasCrestRecord()),
      player(50),
      new Map(),
      wornInput(mask),
    )?.fullSetText ?? ''
  );
}
