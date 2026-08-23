import { describe, expect, it } from 'vitest';
import { D2DataFiles } from '../../../src/D2ItemToolkit.Ts/src/Tables/TxtDataProviders.js';
import type { TxtFile } from '../../../src/D2ItemToolkit.Ts/src/Data/TxtFile.js';

describe('qualityitems.txt', () => {
  const data = D2DataFiles.load();
  const quality = data.qualityItems as TxtFile;

  it('is loaded from the embedded data', () => {
    expect(data.qualityItems).not.toBeNull();
    expect(quality.rowCount).toBe(8);
  });

  it('carries the ranges its modifiers roll within', () => {
    // lowqualityitems.txt names the inferior prefixes and nothing else; this file is the superior
    // counterpart and does carry ranges, which is what makes a superior item's modifiers
    // attributable to a row.
    expect(quality.getInt(0, 'nummods')).toBe(1);
    expect(quality.getString(0, 'mod1code').trim()).toBe('att');
    expect(quality.getInt(0, 'mod1min')).toBe(1);
    expect(quality.getInt(0, 'mod1max')).toBe(3);

    // The aggregate columns restate the same range per damage kind, and stay 0 where the row's
    // mods do not touch that kind.
    expect(quality.getInt(0, 'ToHitMin')).toBe(1);
    expect(quality.getInt(0, 'ToHitMax')).toBe(3);
    expect(quality.getInt(0, 'Dam%Max')).toBe(0);
    expect(quality.getInt(0, 'AC%Max')).toBe(0);
  });

  it('gates rows by item type', () => {
    // Row 0 is attack rating, which is a weapon-only roll: armour and the armour-shaped slots are
    // 0, so picking a row for a superior item means honouring these columns.
    expect(quality.getInt(0, 'weapon')).toBe(1);
    expect(quality.getInt(0, 'armor')).toBe(0);
    expect(quality.getInt(0, 'boots')).toBe(0);

    // Row 2 is the armour-class roll, gated the other way round.
    expect(quality.getString(2, 'mod1code').trim()).toBe('ac%');
    expect(quality.getInt(2, 'weapon')).toBe(0);
    expect(quality.getInt(2, 'armor')).toBe(1);
    expect(quality.getInt(2, 'shield')).toBe(1);
  });

  it('has two-mod rows that fill both slots', () => {
    // Row 3 is the attack-rating-and-damage superior, so a superior item can carry two modifiers
    // from a single row rather than two rows.
    expect(quality.getInt(3, 'nummods')).toBe(2);
    expect(quality.getString(3, 'mod1code').trim()).toBe('att');
    expect(quality.getString(3, 'mod2code').trim()).toBe('dmg%');
    expect(quality.getInt(3, 'mod2min')).toBe(5);
    expect(quality.getInt(3, 'mod2max')).toBe(15);
  });
});
