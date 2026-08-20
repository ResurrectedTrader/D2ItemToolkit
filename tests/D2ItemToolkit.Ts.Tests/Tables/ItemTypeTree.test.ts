import { describe, expect, it } from 'vitest';
import { ItemTypeTree } from '../../../src/D2ItemToolkit.Ts/src/Tables/ItemTypeTree.js';
import { D2DataFiles } from '../../../src/D2ItemToolkit.Ts/src/Tables/TxtDataProviders.js';

// Ported from ItemTypeTreeTests.cs.

const ItemTypes = D2DataFiles.load().itemTypes;
const Tree = new ItemTypeTree(ItemTypes);

// Resolved by CODE, never by a hard-coded index: the engine's 45/50/57 are just where these
// codes land in the compiled table, and the row count differs between shipped versions.
const Blunt = Tree.row('blun');
const Weapon = Tree.row('weap');
const AnyArmor = Tree.row('armo');

describe('ItemTypeTree', () => {
  it('the codes the engine tests all resolve', () => {
    expect(Blunt >= 0).toBe(true);
    expect(Weapon >= 0).toBe(true);
    expect(AnyArmor >= 0).toBe(true);
    expect(Tree.row('nosuchcode')).toBe(-1);
    expect(Tree.row(null)).toBe(-1);
    expect(Tree.rowCount).toBe(ItemTypes?.rowCount);
  });

  it('a type is under itself', () => {
    expect(Tree.isUnder(Blunt, Blunt)).toBe(true);
  });

  it('the blunt closure reaches every leaf through both hops', () => {
    // One hop: Equiv1 = blun.
    for (const code of ['club', 'hamm', 'mace']) {
      expect(Tree.isUnder(Tree.row(code), Blunt), code).toBe(true);
    }

    // Two hops: scep/wand/staf -> rod -> blun. This is the case a naive
    // direct-children test gets wrong.
    expect(Tree.isUnder(Tree.row('rod'), Blunt)).toBe(true);
    for (const code of ['scep', 'wand', 'staf']) {
      expect(Tree.isUnder(Tree.row(code), Blunt), code).toBe(true);
    }

    // Edged weapons are not blunt.
    for (const code of ['swor', 'axe', 'bow', 'helm']) {
      expect(Tree.isUnder(Tree.row(code), Blunt), code).toBe(false);
    }
  });

  it('the closure is transitive up to the roots', () => {
    expect(Tree.isUnder(Tree.row('club'), Tree.row('mele'))).toBe(true);
    expect(Tree.isUnder(Tree.row('club'), Weapon)).toBe(true);
    expect(Tree.isUnder(Tree.row('helm'), AnyArmor)).toBe(true);
  });

  it('a second type is only consulted when it is positive', () => {
    const club = Tree.row('club');
    const sword = Tree.row('swor');

    expect(Tree.isOfType(sword, club, Blunt)).toBe(true);
    expect(Tree.isOfType(sword, -1, Blunt)).toBe(false);

    // Row 0 is never retried: the game requires the second type to be > 0.
    expect(Tree.isOfType(sword, 0, Blunt)).toBe(false);

    // A hit on the first type short-circuits.
    expect(Tree.isOfType(club, -1, Blunt)).toBe(true);
  });

  it('out of range rows are not under anything', () => {
    expect(Tree.isUnder(-1, Blunt)).toBe(false);
    expect(Tree.isUnder(999, Blunt)).toBe(false);
    expect(Tree.isUnder(Blunt, -1)).toBe(false);
    expect(Tree.isUnder(Blunt, 999)).toBe(false);
  });

  it('some rows declare a second parent so the walk is a dag', () => {
    const types = D2DataFiles.load().itemTypes;

    let withEquiv2 = 0;
    for (let row = 0; row < (types?.rowCount ?? 0); ++row) {
      if (types?.getString(row, 'Equiv2').trim().length !== 0) {
        ++withEquiv2;
      }
    }

    expect(withEquiv2, 'no row has Equiv2; a chain walk would suffice').toBeGreaterThan(0);
  });

  it('reads Throwable and Class off the row itself, with no equivalence walk', () => {
    // ITEMS_CheckItemTypeIfThrowable and TXT_ItemTypes_GetClass both read the primary row's own
    // column (+0x11E / +0x21), so a child of a throwable parent is not itself throwable and a
    // class-restricted child does not restrict its parent.
    const thrown = Tree.row('thro');
    const weapon = Tree.row('weap');

    expect(Tree.isUnder(thrown, weapon)).toBe(true);
    expect(Tree.isThrowable(thrown)).toBe(true);
    expect(Tree.isThrowable(weapon)).toBe(false);

    expect(Tree.classCode(Tree.row('abow'))).toBe('ama');
    expect(Tree.classCode(Tree.row('bow'))).toBe('');

    expect(Tree.isThrowable(-1)).toBe(false);
    expect(Tree.isThrowable(999)).toBe(false);
    expect(Tree.classCode(-1)).toBe('');
    expect(Tree.classCode(999)).toBe('');
  });
});
