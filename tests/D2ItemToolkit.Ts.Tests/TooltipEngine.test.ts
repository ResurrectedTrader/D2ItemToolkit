import { describe, expect, it } from 'vitest';
import { ItemRecordReader } from '../../src/D2ItemToolkit.Ts/src/Stats/ItemRecord.js';
import { ArgumentNullException } from '../../src/D2ItemToolkit.Ts/src/Types.js';
import {
  ItemQualityNo,
  ItemRecordFlags,
  ItemStatListFlags,
  ItemStatListStates,
  ColorTable,
  D2DataFiles,
  ItemTier,
  ItemTooltipColor,
  ItemTooltipKind,
  ItemTooltipSection,
  TooltipEngine,
  createUnit,
  unitFromJson,
  type ItemTooltipLine,
  type TooltipOptions,
  type Unit,
} from '../../src/D2ItemToolkit.Ts/src/index.js';

/**
 * The public facade, exercised the way a consumer would: a Unit in, a Tooltip out, with
 * nothing internal named. The C# counterpart is tests/D2ItemToolkit.Net.Tests/TooltipEngineTests.cs
 * and pins the same strings — these two drifting apart is exactly what the differential cannot
 * catch, because the corpus does not go through the facade.
 */

// A magic Large Shield with one socketed sapphire. The affix indices are 1-based into the
// CONCATENATED [magicsuffix][magicprefix][automagic] array, so 962 is past the 747 suffix rows
// and lands in the prefix table.
const ItemJson = `{
  "unitType": 4, "classId": 330, "quality": 4, "itemFlags": 16,
  "magicPrefix": [ 962, 0, 0 ], "magicSuffix": [ 121, 0, 0 ],
  "statsLists": [
    { "stateNo": 0, "flags": 2147483648,
      "stats": [ { "id": 31, "value": 120 }, { "id": 72, "value": 40 },
                 { "id": 73, "value": 62 } ] },
    { "stateNo": 0, "flags": 64,
      "stats": [ { "id": 16, "value": 150 }, { "id": 39, "value": 25 },
                 { "id": 80, "value": 30 } ] } ],
  "items": [
    { "unitType": 4, "classId": 604,
      "statsLists": [ { "stateNo": 0, "flags": 64,
                        "stats": [ { "id": 39, "value": 38 } ] } ] } ]
}`;

const PlayerJson = `{
  "unitType": 0, "classId": 3,
  "statsLists": [
    { "stateNo": 0, "flags": 2147483648,
      "stats": [ { "id": 12, "value": 30 }, { "id": 0, "value": 60 },
                 { "id": 2, "value": 55 } ] } ] }`;

function item(): Unit {
  return unitFromJson(ItemJson);
}

function player(): Unit {
  return unitFromJson(PlayerJson);
}

function textOf(lines: readonly ItemTooltipLine[]): string[] {
  return lines.map(line => (line.text ?? '').replace(/\n/g, ''));
}

describe('the facade', () => {
  it('produces the whole tooltip from two records', () => {
    const tip = TooltipEngine.embedded.render(item(), player());

    expect(tip.kind).toBe(ItemTooltipKind.Generic);
    expect(tip.text).toBe(
      'Vigorous Large Shield of Absorption\n' +
        'Defense: ÿc3300\n' +
        'ÿc0Chance to Block: ÿc330%\n' +
        'Smite Damage: 2 to 4\n' +
        'Durability: 40 of 62\n' +
        'Required Strength: 34\n' +
        'Required Level: 24\n' +
        '+150% Enhanced Defense\n' +
        'Fire Resist +63%\n' +
        '30% Better Chance of Getting Magic Items',
    );
  });

  it('merges the socketed gem into the resist line', () => {
    // 25 on the item, 38 on the filler. The engine SUMS them into one line rather than listing
    // them separately, which is what makes includeSockets worth having.
    expect(TooltipEngine.embedded.render(item(), player()).text).toContain('Fire Resist +63%');
  });

  it('drops only the filler contribution when sockets are excluded', () => {
    const text = TooltipEngine.embedded.render(item(), player(), { sockets: 'excluded' }).text;

    expect(text).toContain('Fire Resist +25%');
    expect(text).not.toContain('Fire Resist +63%');

    // The base sections are unaffected — the gem contributed nothing to them.
    expect(text).toContain('Defense: ÿc3300');
    expect(text).toContain('Durability: 40 of 62');
  });

  it('prepends the per-line marker only in coloredText', () => {
    const tip = TooltipEngine.embedded.render(item(), player());

    // Two markers on the block line, and that is the game: INV_FormatBlockChanceText prepends
    // colour 0 to its own label buffer (0x485d0e) and LoadItemDesc prepends the section's on top
    // (0x48eb80). The composer used to swallow its own whenever the text already began with a
    // marker, which lost one of the pair.
    expect(tip.coloredText).toBe(
      'ÿc3Vigorous Large Shield of Absorption\n' +
        'ÿc0Defense: ÿc3300\n' +
        'ÿc0ÿc0Chance to Block: ÿc330%\n' +
        'ÿc0Smite Damage: 2 to 4\n' +
        'ÿc0Durability: 40 of 62\n' +
        'ÿc0Required Strength: 34\n' +
        'ÿc0Required Level: 24\n' +
        'ÿc3+150% Enhanced Defense\n' +
        'ÿc3Fire Resist +63%\n' +
        'ÿc330% Better Chance of Getting Magic Items',
    );

    // text keeps markers a writer embedded in its OWN text, and drops only the per-line ones —
    // the game embeds those too.
    expect(tip.text.startsWith('Vigorous Large Shield of Absorption\nDefense: ÿc3300')).toBe(true);
  });

  it('makes the viewer optional', () => {
    // GetStatUnsignedValue returns 0 for a null unit (0x625483) rather than halting, so a
    // viewerless render still produces the whole tooltip.
    const tip = TooltipEngine.embedded.render(item());

    expect(tip.kind).toBe(ItemTooltipKind.Generic);
    expect(tip.text).toContain('Vigorous Large Shield of Absorption');
  });

  it('paints no requirement red without a viewer', () => {
    // The binary would paint all three red here — GetStatUnsignedValue reads 0 off a null unit and
    // 0x62ebd5 / 0x62ec31 gate on `> 0` — but the game never reaches that branch, and red asserts
    // a viewer failed a check nobody ran. See RecordSections.isRequirementUnmet.
    const tip = TooltipEngine.embedded.render(item());

    const gated = [
      ItemTooltipSection.RequiredLevel,
      ItemTooltipSection.RequiredStrength,
      ItemTooltipSection.RequiredDexterity,
      ItemTooltipSection.ClassRestriction,
    ];

    for (const line of tip.lines) {
      if (gated.includes(line.section)) {
        expect(line.color).toBe(ItemTooltipColor.White);
      }
    }

    // The lines are really there, or the loop above proves nothing.
    expect(tip.text).toContain('Required Strength: 34');
    expect(tip.text).toContain('Required Level: 24');
  });

  it('still reddens a requirement the supplied viewer falls short of', () => {
    // The deviation is scoped to a MISSING viewer. Supply one and 0x62eaf0's answer stands.
    const weakling = unitFromJson({
      unitType: 0,
      classId: 3,
      level: 1,
      statsLists: [
        {
          stateNo: 0,
          flags: 2147483648,
          stats: [
            { id: 0, value: 10 },
            { id: 2, value: 10 },
            { id: 12, value: 1 },
          ],
        },
      ],
    });

    const tip = TooltipEngine.embedded.render(item(), weakling);

    const strength = tip.lines.find(l => l.section === ItemTooltipSection.RequiredStrength);
    const level = tip.lines.find(l => l.section === ItemTooltipSection.RequiredLevel);

    expect(strength?.color).toBe(ItemTooltipColor.Red);
    expect(level?.color).toBe(ItemTooltipColor.Red);
  });
});

// The game never draws these separately, so what is pinned here is that each source selects the
// right stats and that the lines come out of the same traced writers.
describe('the breakdown', () => {
  it('gives the base array alone', () => {
    expect(textOf(TooltipEngine.embedded.breakdown(item(), player()).base)).toEqual([
      '+120 Defense',
    ]);
  });

  it('excludes both the base array and the fillers from magic', () => {
    // ItemStatView.itemOnly() would be wrong here: it requires EXTENDED *or* MAGIC, so it drags
    // "+120 Defense" in with it.
    expect(textOf(TooltipEngine.embedded.breakdown(item(), player()).magic)).toEqual([
      '+150% Enhanced Defense',
      'Fire Resist +25%',
      '30% Better Chance of Getting Magic Items',
    ]);
  });

  it('gives only what the filler contributes', () => {
    // 38, not the merged 63 — the item's own 25 belongs to magic.
    expect(textOf(TooltipEngine.embedded.breakdown(item(), player()).sockets)).toEqual([
      'Fire Resist +38%',
    ]);
  });

  it('is empty on an item that is not part of a set', () => {
    expect(TooltipEngine.embedded.breakdown(item(), player()).setBonuses).toHaveLength(0);
  });

  it('shows an earned set tier', () => {
    // An unearned tier keeps STATLIST_SET (0x2000); earning it clears the bit, so 0x40 alone on
    // state 165 is exactly an earned tier.
    const record = item();
    record.statsLists.push({
      stateNo: ItemStatListStates.ItemSet1,
      flags: ItemStatListFlags.Magic,
      stats: [{ id: 39, value: 11 }],
    });

    expect(textOf(TooltipEngine.embedded.breakdown(record, player()).setBonuses)).toEqual([
      'Fire Resist +11%',
    ]);
  });

  it('excludes an unearned set tier', () => {
    const record = item();
    record.statsLists.push({
      stateNo: ItemStatListStates.ItemSet1,
      flags: (ItemStatListFlags.Magic | ItemStatListFlags.Set) >>> 0,
      stats: [{ id: 39, value: 11 }],
    });

    expect(TooltipEngine.embedded.breakdown(record, player()).setBonuses).toHaveLength(0);
  });
});

// The C# counterpart is TooltipEngineTests.The_tables_are_reachable_for_lookups_the_library_does_not_do.
describe('the game tables', () => {
  it('are reachable for lookups the library does not do', () => {
    const engine = TooltipEngine.embedded;

    const classId = engine.items.classIdForCode('lrg');

    // A raw cell, the way a consumer wanting its own lookup would read one. tryResolve says which
    // of the three item files a classId lands in, and at what row.
    const at = engine.items.tryResolve(classId);
    expect(at).not.toBeNull();
    expect(at?.file.getString(at.row, 'invfile')).toBe('invlrg');

    // The same cell through the classId-indexed view.
    expect(engine.items.getString(classId, 'invfile')).toBe('invlrg');

    expect(engine.items.primaryTypeCode(classId)).toBe('shie');
    expect(engine.types.row('gem')).toBeGreaterThanOrEqual(0);
    expect(new ColorTable(engine.data.colors).rowCount).toBe(21);
  });
});

// The C# counterparts live in TooltipEngineTests.cs and assert the same things.
describe('requirements, tier and the type tree', () => {
  it('gives the same strength number to every viewer', () => {
    const engine = TooltipEngine.embedded;
    const reqstr = engine.items.getInt(engine.items.classIdForCode('lrg'), 'reqstr');
    expect(reqstr).toBeGreaterThan(0);

    expect(engine.requirements(item()).strength).toBe(reqstr);
    expect(engine.requirements(item(), player()).strength).toBe(reqstr);
  });

  it('folds dexterity the same way as strength', () => {
    const engine = TooltipEngine.embedded;

    // Scimitar: reqdex 21, and no reqstr percent stat on a bare record, so the fold is the
    // identity and the number is the table's.
    const scimitar = unitFromJson(
      '{ "unitType": 4, "classId": ' +
        String(engine.items.classIdForCode('scm')) +
        ', "quality": 2, "itemFlags": 16, "statsLists": [] }',
    );

    expect(engine.requirements(scimitar).dexterity).toBe(21);
  });

  it('agrees with what the tooltip prints for the required level', () => {
    // The rendered tooltip for this fixture says "Required Level: 24", so the structured answer
    // has to agree — one of them being wrong would otherwise go unnoticed.
    expect(TooltipEngine.embedded.requirements(item(), player()).level).toBe(24);
    expect(TooltipEngine.embedded.render(item(), player()).text).toContain('Required Level: 24');
  });

  it('reports the item type class restriction, or none', () => {
    const engine = TooltipEngine.embedded;

    // A Large Shield is not class-restricted. 7 is NoClassRestriction.
    expect(engine.requirements(item()).classRestriction).toBe(7);

    // The restriction is a property of the itemtype, not the item: `pala` carries Class `pal`.
    expect(engine.types.classCode(engine.types.row('pala'))).toBe('pal');
  });

  it('does depend on the viewer for whether a requirement is met', () => {
    const engine = TooltipEngine.embedded;

    // The fixture player has 60 strength; lrg needs 34.
    expect(engine.requirements(item(), player()).metStrength).toBe(true);

    // With no viewer at all the stats read as 0, so nothing is met (0x625483).
    expect(engine.requirements(item()).metStrength).toBe(false);
  });

  it.each([
    ['cap', ItemTier.Normal],
    ['xap', ItemTier.Exceptional],
    ['uap', ItemTier.Elite],
    ['lrg', ItemTier.Normal],
  ])('reads %s as the right tier', (code, expected) => {
    const engine = TooltipEngine.embedded;
    expect(engine.items.tier(engine.items.classIdForCode(code))).toBe(expected);
  });

  it.each([['qf1'], ['qf2'], ['gpv'], ['r01']])(
    'falls back to Normal for %s, which matches no tier code',
    code => {
      // 153 shipped rows are in this position — all 151 misc rows plus the two Khalim quest
      // weapons. Normal is a deliberate fallback, not a classification.
      const engine = TooltipEngine.embedded;
      expect(engine.items.tier(engine.items.classIdForCode(code))).toBe(ItemTier.Normal);
    },
  );

  it('exposes both of an item type codes', () => {
    const engine = TooltipEngine.embedded;
    const gem = engine.items.classIdForCode('gpv');

    expect(engine.items.primaryTypeCode(engine.items.classIdForCode('lrg'))).toBe('shie');

    // A perfect amethyst carries both: `gema` (amethyst) and `gem4` (perfect). The two axes are
    // what make type2 worth reading — colour and grade are separate hierarchies.
    expect(engine.items.primaryTypeCode(gem)).toBe('gema');
    expect(engine.items.secondaryTypeCode(gem)).toBe('gem4');
    expect(engine.items.primaryTypeCode(engine.items.classIdForCode('gpr'))).toBe('gemr');
  });

  it('includes the type itself and everything under it in descendants', () => {
    const types = TooltipEngine.embedded.types;

    const gem = types.row('gem');
    const under = types.descendants(gem);

    expect(under).toContain(gem); // reflexive
    expect(under).toContain(types.row('gem4'));
    expect(under).not.toContain(types.row('rune')); // runes live under `sock`

    // descendants and isUnder read the same closure, so they cannot disagree.
    for (let row = 0; row < types.rowCount; ++row) {
      expect(types.isUnder(row, gem)).toBe(under.includes(row));
    }
  });

  it('finds every item at or below a type', () => {
    const engine = TooltipEngine.embedded;
    const swords = engine.classIdsOfType('swor');

    expect(swords.length).toBeGreaterThan(0);
    expect(swords).toContain(engine.items.classIdForCode('ssd'));
    expect(swords).not.toContain(engine.items.classIdForCode('lrg'));
    expect(swords).not.toContain(engine.items.classIdForCode('gpv'));

    for (const classId of swords) {
      expect(
        engine.types.isOfType(
          engine.types.row(engine.items.primaryTypeCode(classId)),
          engine.types.row(engine.items.secondaryTypeCode(classId)),
          engine.types.row('swor'),
        ),
      ).toBe(true);
    }
  });

  it('yields nothing rather than everything for an unknown type code', () => {
    expect(TooltipEngine.embedded.classIdsOfType('zzzz')).toEqual([]);
    expect(TooltipEngine.embedded.types.descendants(-1)).toEqual([]);
  });
});

// The C# counterparts are the ShortAffixUnit tests in TooltipEngineTests.cs.
describe('the affix lists', () => {
  function withAffixes(prefix: readonly number[], suffix: readonly number[]): Unit {
    return createUnit({
      unitType: 4,
      classId: 330,
      quality: ItemQualityNo.Magic,
      itemFlags: ItemRecordFlags.Identified,
      magicPrefix: prefix,
      magicSuffix: suffix,
    });
  }

  it('reads a list shorter than three slots as zero-filled', () => {
    // The game struct is wMagicPrefix[3], but the contract is a list, so a caller need not pad.
    const engine = TooltipEngine.embedded;

    const oneSlot = engine.render(withAffixes([962], [121])).text;
    const threeSlots = engine.render(withAffixes([962, 0, 0], [121, 0, 0])).text;

    expect(oneSlot).toBe(threeSlots);
    expect(oneSlot).toContain('Vigorous Large Shield of Absorption');
  });

  it('ignores slots past the third', () => {
    const engine = TooltipEngine.embedded;

    expect(engine.render(withAffixes([962, 0, 0, 999, 999], [121, 0, 0, 999])).text).toBe(
      engine.render(withAffixes([962, 0, 0], [121, 0, 0])).text,
    );
  });

  it('treats an empty list as no affixes', () => {
    const engine = TooltipEngine.embedded;

    expect(engine.render(withAffixes([], [])).text).toBe(
      engine.render(withAffixes([0, 0, 0], [0, 0, 0])).text,
    );
  });
});

// A wearer's MERGED stats. Its statlist chain is structural — states, but pre-gear attribute
// values — so requirement checks read the merged set instead. C# counterparts in
// TooltipEngineTests.cs.
describe('merged wearer stats', () => {
  it('overwrite the chain rather than adding to it', () => {
    // 60 strength on the chain, 90 merged (60 base + 30 from gear the chain cannot see). Summing
    // would give 150 and let the wearer equip things they cannot.
    const viewer = unitFromJson(`{
      "unitType": 0, "classId": 3,
      "statsLists": [ { "stateNo": 0, "flags": 2147483648,
        "stats": [ { "id": 0, "value": 60 }, { "id": 12, "value": 30 } ] } ],
      "stats": [ { "id": 0, "value": 90 }, { "id": 12, "value": 40 } ] }`);

    const read = ItemRecordReader.readViewer(viewer);

    expect(read.strength).toBe(90);
    expect(read.level).toBe(40);
  });

  it('leave the chain values standing when absent', () => {
    const viewer = unitFromJson(`{
      "unitType": 0, "classId": 3,
      "statsLists": [ { "stateNo": 0, "flags": 2147483648,
        "stats": [ { "id": 0, "value": 60 } ] } ] }`);

    expect(ItemRecordReader.readViewer(viewer).strength).toBe(60);
  });

  it('cannot supply active states', () => {
    // A state is a statlist node carrying its own dwStateNo. Merged values have no provenance, so
    // nothing can recover it — do not "fix" this by inventing a synthetic state.
    const merged = unitFromJson(
      '{ "unitType": 0, "classId": 3, "stats": [ { "id": 0, "value": 90 } ] }',
    );

    expect(ItemRecordReader.readViewer(merged).activeStates.has(101)).toBe(false);

    const withChain = unitFromJson(`{
      "unitType": 0, "classId": 3,
      "statsLists": [ { "stateNo": 101, "flags": 64, "stats": [] } ],
      "stats": [ { "id": 0, "value": 90 } ] }`);

    expect(ItemRecordReader.readViewer(withChain).activeStates.has(101)).toBe(true);
  });

  it('narrow a value past int32 to the game own bits', () => {
    // Experience at level 99 is ~3.52 billion: past int32, inside uint32.
    const experience = 3520485421;

    const viewer = unitFromJson(
      `{ "unitType": 0, "classId": 3, "stats": [ { "id": 13, "value": ${String(experience)} } ] }`,
    );

    expect(viewer.stats).toHaveLength(1);
    expect(viewer.stats[0]?.value).toBe(experience | 0);
  });

  it('reach the requirement checks', () => {
    const engine = TooltipEngine.embedded;

    const chain = `"statsLists": [ { "stateNo": 0, "flags": 2147483648,
      "stats": [ { "id": 0, "value": 20 }, { "id": 12, "value": 30 } ] } ]`;

    const weak = unitFromJson(`{ "unitType": 0, "classId": 3, ${chain} }`);
    const geared = unitFromJson(
      `{ "unitType": 0, "classId": 3, ${chain},
         "stats": [ { "id": 0, "value": 60 }, { "id": 12, "value": 30 } ] }`,
    );

    expect(engine.requirements(item(), weak).metStrength).toBe(false);
    expect(engine.requirements(item(), geared).metStrength).toBe(true);
  });

  it('are empty on an item', () => {
    // `stats` at the top level is a WEARER field. On an item the same key appears only inside
    // each statlist node, a different nesting that must not be picked up.
    expect(item().stats).toHaveLength(0);
  });
});

// C# counterparts in TooltipEngineTests.cs. These pin what a ~1M-assertion divergence sweep
// found between the two engines on surfaces the shared corpus never reaches.
describe('parity with the C# engine', () => {
  it('wraps socket contributions at int32 like every other sum', () => {
    // The game stores stats as int32 and its sums wrap. Two fillers each carrying int.MaxValue
    // must fold to -2, not to a 64-bit total. This was the ONE accumulation site in the tree
    // without Int32.of, so it disagreed with C#'s int arithmetic.
    const item = unitFromJson(`{
      "unitType": 4, "classId": 330, "quality": 2, "itemFlags": 16,
      "statsLists": [],
      "items": [
        { "unitType": 4, "classId": 604, "statsLists": [
          { "stateNo": 0, "flags": 64, "stats": [ { "id": 39, "value": 2147483647 } ] } ] },
        { "unitType": 4, "classId": 604, "statsLists": [
          { "stateNo": 0, "flags": 64, "stats": [ { "id": 39, "value": 2147483647 } ] } ] } ] }`);

    expect(textOf(TooltipEngine.embedded.breakdown(item).sockets)).toContain('Fire Resist -2%');
  });

  it('does not change a rendered tooltip when the options object changes', () => {
    // Lines are composed eagerly, so every knob is baked in at render time; the tooltip closes
    // over none of them.
    const options: TooltipOptions = { sockets: 'merged' };

    const tip = TooltipEngine.embedded.render(item(), player(), options);
    const before = tip.text;
    const beforeColored = tip.coloredText;

    options.sockets = 'separated';
    options.ranges = {};

    expect(tip.text).toBe(before);
    expect(tip.coloredText).toBe(beforeColored);
  });

  it('treats null options as the defaults', () => {
    // A default parameter only fires for `undefined`, so an explicit null used to fall through
    // to a field access and throw where C# accepted it.
    expect(TooltipEngine.embedded.render(item(), player(), null).text).toBe(
      TooltipEngine.embedded.render(item(), player()).text,
    );

    expect(TooltipEngine.embedded.breakdown(item(), player(), null).magic.length).toBeGreaterThan(
      0,
    );
  });

  it('can be built over tables the caller supplies', () => {
    // The portable form: hand over a D2DataFiles from wherever — a modded extraction, fetched
    // bytes, a bundled archive — rather than going through the filesystem. This pair existed only
    // in C# until a parity sweep found the gap.
    const supplied = TooltipEngine.fromData(D2DataFiles.load());

    expect(supplied.render(item(), player()).text).toBe(
      TooltipEngine.embedded.render(item(), player()).text,
    );

    expect(supplied).not.toBe(TooltipEngine.embedded);
    expect(supplied.data).not.toBe(TooltipEngine.embedded.data);
  });

  it('rejects a null table set', () => {
    expect(() => TooltipEngine.fromData(null as unknown as D2DataFiles)).toThrow(
      ArgumentNullException,
    );
  });

  it('rejects a null item on every entry point', () => {
    const engine = TooltipEngine.embedded;
    const nothing = null as unknown as Unit;

    expect(() => engine.render(nothing)).toThrow(ArgumentNullException);
    expect(() => engine.appearance(nothing)).toThrow(ArgumentNullException);
    expect(() => engine.requirements(nothing)).toThrow(ArgumentNullException);
    expect(() => engine.breakdown(nothing)).toThrow(ArgumentNullException);
  });
});

describe('the graphics index', () => {
  it('is read off the document', () => {
    // bInvGfxIdx. Only rings, amulets, jewels and charms have a non-zero itemtypes VarInvGfx, so
    // this is the one field that decides rin1 from rin5 — and nothing else in the document
    // implies it.
    expect(unitFromJson('{ "classId": 1, "gfxIndex": 4, "statsLists": [] }').gfxIndex).toBe(4);
  });

  it('defaults to zero rather than a negative sentinel', () => {
    // 0 is a REAL variant (the first), and the producer emits the field unconditionally, so
    // absence means "the first one" rather than "unknown" — unlike fileIndex, where -1 is the
    // sentinel.
    expect(unitFromJson('{ "classId": 1 }').gfxIndex).toBe(0);
    expect(createUnit().gfxIndex).toBe(0);
  });
});

describe('building a record in code', () => {
  it('renders the same as the parsed one', () => {
    const record = createUnit({
      unitType: 4,
      classId: 330,
      quality: ItemQualityNo.Magic,
      itemFlags: ItemRecordFlags.Identified,
      magicPrefix: [962, 0, 0],
      magicSuffix: [121, 0, 0],
      statsLists: [
        {
          stateNo: 0,
          flags: ItemStatListFlags.Extended,
          stats: [
            { id: 31, value: 120 },
            { id: 72, value: 40 },
            { id: 73, value: 62 },
          ],
        },
        {
          stateNo: 0,
          flags: ItemStatListFlags.Magic,
          stats: [
            { id: 16, value: 150 },
            { id: 39, value: 25 },
            { id: 80, value: 30 },
          ],
        },
      ],
      items: [
        createUnit({
          unitType: 4,
          classId: 604,
          statsLists: [
            { stateNo: 0, flags: ItemStatListFlags.Magic, stats: [{ id: 39, value: 38 }] },
          ],
        }),
      ],
    });

    expect(TooltipEngine.embedded.render(record, player()).text).toBe(
      TooltipEngine.embedded.render(item(), player()).text,
    );
  });
});
