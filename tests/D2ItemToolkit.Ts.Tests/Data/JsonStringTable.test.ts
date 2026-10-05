import { describe, expect, it } from 'vitest';
import { GameVariant } from '../../../src/D2ItemToolkit.Ts/src/Data/GameVariant.js';
import { JsonStringTable } from '../../../src/D2ItemToolkit.Ts/src/Data/JsonStringTable.js';
import { D2DataFiles } from '../../../src/D2ItemToolkit.Ts/src/Tables/TxtDataProviders.js';

/** The D2R string table's load rules (sub_1404776e0 / sub_140476c60). */
function json(text: string): Uint8Array {
  return new TextEncoder().encode(text);
}

function table(hd: string, legacy: string | null = null, legacyMode = false): JsonStringTable {
  return new JsonStringTable(
    name => (name === 'ui.json' ? json(hd) : null),
    name => (name === 'ui.json' && legacy !== null ? json(legacy) : null),
    legacyMode,
    'enUS',
  );
}

describe('JsonStringTable', () => {
  it("the first entry for a key wins and a duplicate's id is dropped", () => {
    const strings = table(
      '[{"id":10,"Key":"k","enUS":"first"},' +
        '{"id":11,"Key":"k","enUS":"second"},' +
        '{"id":12,"Key":"strMissingString","enUS":"Missing string"}]',
    );

    expect(strings.resolveKey('k')).toBe(10);
    expect(strings.getByKey('k')).toBe('first');
    expect(strings.getByIndex(11)).toBe('Missing string');
  });

  it('a missing key resolves to 5382 and a missing id to the missing string', () => {
    const strings = table('[{"id":12,"Key":"strMissingString","enUS":"Missing string"}]');

    expect(strings.resolveKey('nope')).toBe(5382);
    expect(strings.resolveKey('')).toBe(5382);
    expect(strings.getByIndex(999)).toBe('Missing string');
    expect(strings.getByKey('nope')).toBe('Missing string');
  });

  it('keys are case sensitive', () => {
    const strings = table('[{"id":10,"Key":"Abc","enUS":"x"}]');
    expect(strings.resolveKey('Abc')).toBe(10);
    expect(strings.resolveKey('abc')).toBe(5382);
  });

  it('legacy strings load first and win', () => {
    const strings = table(
      '[{"id":10,"Key":"q","enUS":"Quantity: %d of %d"}]',
      '[{"id":10,"Key":"q","enUS":"Quantity: %d"}]',
      true,
    );

    expect(strings.getByIndex(10)).toBe('Quantity: %d');
  });

  it('legacy text keeps the HD ids and the HD missing string', () => {
    // The txts resolve their keys before g_IsRunningLecgacyGfx is first set (0x140063844 vs
    // 0x14061d604), so through the HD table; the HD table loads last, so its strMissingString is
    // the miss fallback (0x140477ac5).
    const strings = table(
      '[{"id":12,"Key":"strMissingString","enUS":"HD missing"},' +
        '{"id":27574,"Key":"terror","enUS":"HD text"}]',
      '[{"id":12,"Key":"strMissingString","enUS":"Legacy missing"},' +
        '{"id":27446,"Key":"terror","enUS":"Legacy text"}]',
      true,
    );

    expect(strings.resolveKey('terror')).toBe(27574);
    expect(strings.getIndexByKey('terror')).toBe(27574);
    expect(strings.getByIndex(27574)).toBe('HD missing');
    expect(strings.getByIndex(27446)).toBe('Legacy text');
    expect(strings.getByKey('terror')).toBe('Legacy text');
    expect(strings.hasKey('terror')).toBe(true);
    expect(strings.hasKey('nope')).toBe(false);
    expect(strings.getByKey('nope')).toBe('HD missing');
    expect(strings.getByKey('strMissingString')).toBe('Legacy missing');
  });

  it('an entry without the language abandons the whole file', () => {
    const strings = table('[{"id":10,"Key":"a","enUS":"a"},{"id":11,"Key":"b"}]');

    expect(strings.resolveKey('a')).toBe(5382);
  });

  it('the authoring colour escape becomes the game escape', () => {
    const strings = table('[{"id":10,"Key":"c","enUS":"\\ue07e4Gold"}]');
    expect(strings.getByIndex(10)).toBe('ÿc4Gold');
  });

  it('the embedded tables resolve the compiled itemstatcost ids', () => {
    // Spot values the shipped itemstatcost.bin carries for row 0 (strength).
    const data = D2DataFiles.loadEmbedded(GameVariant.ReignOfTheWarlock);
    const strength = data.itemStatCost.rowAt(0);
    expect(strength).not.toBeNull();
    expect(data.strings.getByIndex(strength?.descStrPos ?? -1)).toBe('%+d to Strength');
  });
});
