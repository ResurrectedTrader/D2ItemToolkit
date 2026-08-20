import { describe, expect, it } from 'vitest';
import { TblFile, TblStringTable } from '../../../src/D2ItemToolkit.Ts/src/Data/TblFile.js';
import {
  excelDirectory,
  globalDirectory,
  localeDirectory,
} from '../../../src/D2ItemToolkit.Ts/src/Data/TxtDataSource.js';
import { TxtFile } from '../../../src/D2ItemToolkit.Ts/src/Data/TxtFile.js';
import { DescStringIds } from '../../../src/D2ItemToolkit.Ts/src/Types.js';
import {
  D2DataFiles,
  TxtCharacterClassTable,
  TxtItemStatCostTable,
  TxtSkillTable,
} from '../../../src/D2ItemToolkit.Ts/src/Tables/TxtDataProviders.js';

/**
 * Builds a minimal in-memory .tbl: 21-byte header, one u16 index slot per key, then
 * 17-byte hash nodes, then NUL-terminated key and value blobs. Value for key K is "V:K".
 */
function synthTbl(
  keys: string[],
  overrideIndex = -1,
  overrideValue: string | null = null,
): TblFile {
  return TblFile.parse(synthTblBytes(keys, overrideIndex, overrideValue));
}

function synthTblBytes(
  keys: string[],
  overrideIndex: number,
  overrideValue: string | null,
): Uint8Array {
  const HeaderLength = 21;
  const NodeLength = 17;

  const indexBase = HeaderLength;
  const nodeBase = indexBase + keys.length * 2;
  const dataBase = nodeBase + keys.length * NodeLength;

  const encoder = new TextEncoder();
  const blob: number[] = [];
  const keyOffset = new Array<number>(keys.length);
  const valueOffset = new Array<number>(keys.length);
  const valueLength = new Array<number>(keys.length);

  for (let i = 0; i < keys.length; ++i) {
    keyOffset[i] = dataBase + blob.length;
    blob.push(...encoder.encode(keys[i] ?? ''));
    blob.push(0);

    valueOffset[i] = dataBase + blob.length;
    const value = i === overrideIndex ? overrideValue : 'V:' + (keys[i] ?? '');
    const encoded = encoder.encode(value ?? '');
    valueLength[i] = encoded.length;
    blob.push(...encoded);
    blob.push(0);
  }

  const bytes = new Uint8Array(dataBase + blob.length);
  const view = new DataView(bytes.buffer);
  view.setUint16(2, keys.length, true);
  view.setUint32(4, keys.length, true);

  for (let i = 0; i < keys.length; ++i) {
    view.setUint16(indexBase + i * 2, i, true);

    const at = nodeBase + i * NodeLength;
    bytes[at] = 1; // used
    view.setUint16(at + 1, i, true);
    view.setUint32(at + 7, keyOffset[i] ?? 0, true);
    view.setUint32(at + 11, valueOffset[i] ?? 0, true);

    // stringLength at +15, which the reader honours because the game does: shipped
    // tables always carry strlen + 1 here.
    view.setUint16(at + 15, (valueLength[i] ?? 0) + 1, true);
  }

  bytes.set(blob, dataBase);
  return bytes;
}

/** A .tbl whose index 5382 holds `sentinel`. */
function synthWithSentinel(sentinel: string): TblFile {
  const keys = new Array<string>(DescStringIds.DescStr2Sentinel + 1);
  for (let i = 0; i < keys.length; ++i) {
    keys[i] = 'k' + i.toString();
  }

  return synthTbl(keys, DescStringIds.DescStr2Sentinel, sentinel);
}

function resolveKey(strings: TblStringTable, key: string): number {
  return strings.resolveKey(key);
}

describe('TxtSkillTable', () => {
  it('yields the sentinel not null when there is no skilldesc file', () => {
    // SKILLDESC_GetStatNameString (0x4e6ce0) returns 5382 on every failure path — the
    // branches at 0x4e6ce3/0x4e6cf1/0x4e6d01/0x4e6d0c/0x4e6d14 and the fall-through all
    // reach `mov ax, 1506h` at 0x4e6d24 — and 5382 resolves to "an evil force". The
    // engine therefore never yields a null skill name.
    //
    // skilldesc.txt is ABSENT from some extractions. With null names, DescFunc 16 drops
    // every aura row, 24/27/28 blank theirs, and DescFunc 15 hands null to the formatter,
    // which throws and destroys the entire tooltip.
    const Sentinel = 'an evil force';

    // Stand the sentinel up at index 5382 in a table wide enough to hold it.
    const wide = new TblStringTable(synthWithSentinel(Sentinel), null, null);
    expect(wide.getByIndex(DescStringIds.DescStr2Sentinel)).toBe(Sentinel);

    const skills = TxtFile.parse(
      'skill\tcharclass\tskilldesc\r\n' + 'Fire Bolt\tsor\tfirebolt\r\n' + 'Nameless\tsor\t\r\n',
    );

    const table = new TxtSkillTable(skills, null, wide);

    expect(table.rowCount).toBe(2);
    expect(table.getSkillName(0)).toBe(Sentinel);
    expect(table.getSkillName(1)).toBe(Sentinel);

    // Out of range too, and existence is a pure range check (TXT_Skills_GetLine 0x45c4b0).
    expect(table.getSkillName(999)).toBe(Sentinel);
    expect(table.skillExists(1)).toBe(true);
    expect(table.skillExists(2)).toBe(false);
  });

  it('takes class codes from playerclass.txt in row order', () => {
    // skills.txt's `charclass` resolves against a linker built from playerclass.txt's
    // `Code` column in ROW ORDER: the descriptor at 0x615234 points `charclass` at
    // dword_96BC34, which 0x61282e creates from the `Code` descriptor at 0x6127ef right
    // before playerclass.txt is compiled at 0x612833. Hardcoding the list makes a reordered
    // or extended playerclass.txt silently misresolve.
    const strings = new TblStringTable(synthTbl(['index0', 'aKey']), null, null);

    // Deliberately NOT stock order, and with an extra class after the Expansion divider.
    const playerClass = TxtFile.parse(
      'Player Class\tCode\r\n' +
        'Necromancer\tnec\r\n' +
        'Amazon\tama\r\n' +
        'Expansion\t\r\n' +
        'Druid\tdru\r\n' +
        'Warrior\twar\r\n\r\n',
    );

    const skills = TxtFile.parse(
      'skill\tId\tcharclass\tskilldesc\r\n' +
        's0\t0\tnec\t\r\n' +
        's1\t1\tama\t\r\n' +
        's2\t2\tdru\t\r\n' +
        's3\t3\twar\t\r\n' +
        's4\t4\tnotaclass\t\r\n\r\n',
    );

    const table = new TxtSkillTable(skills, null, strings, playerClass);

    expect(table.getSkillClass(0)).toBe(0); // nec is row 0 here, not row 2
    expect(table.getSkillClass(1)).toBe(1);
    expect(table.getSkillClass(2)).toBe(2); // the Expansion divider consumed no id
    expect(table.getSkillClass(3)).toBe(3); // a class the stock list does not have
    expect(table.getSkillClass(4)).toBe(-1); // 0x6bd168: miss yields -1

    // The compare is CASE-SENSITIVE over four space-padded bytes: field type 0x0D copies at
    // most 4 bytes and pads with 0x20 (0x6bdc62, 0x6bdc7f, 0x6bdc9a, 0x6bdcb1), and
    // GetClassIdFromName compares that packed value as a raw DWORD (0x6bd155). So "Nec"
    // does NOT match Code "nec", and a 5-character cell matches on its first four.
    const cased = TxtFile.parse(
      'skill\tId\tcharclass\tskilldesc\r\n' +
        's0\t0\tNec\t\r\n' +
        's1\t1\tdruid\t\r\n' +
        's2\t2\twarrior\t\r\n\r\n',
    );

    const casedTable = new TxtSkillTable(cased, null, strings, playerClass);

    // Wrong case: "Nec " != "nec ".
    expect(casedTable.getSkillClass(0)).toBe(-1);

    // Truncation can turn a hit into a miss: "druid" packs to "drui", not "dru ".
    expect(casedTable.getSkillClass(1)).toBe(-1);

    // ...and a miss into a hit: "warrior" and Code "war" pack to "warr" and "war ", which
    // still differ — but a Code of "warrior" would pack to the same "warr" as the cell.
    expect(casedTable.getSkillClass(2)).toBe(-1);

    const longCode = TxtFile.parse('Player Class\tCode\r\n' + 'Warrior\twarrior\r\n\r\n');

    expect(new TxtSkillTable(cased, null, strings, longCode).getSkillClass(2)).toBe(0);

    // Omitting the file keeps the stock order, which is what shipped 1.14d data gives.
    const stock = new TxtSkillTable(skills, null, strings);
    expect(stock.getSkillClass(0)).toBe(2); // nec is 2 in stock order
    expect(stock.getSkillClass(1)).toBe(0);
    expect(stock.getSkillClass(2)).toBe(5);
    expect(stock.getSkillClass(3)).toBe(-1); // no "war" in stock data
  });
});

describe('table existence', () => {
  it('is a range check, not a content test', () => {
    // Every one of the engine's row accessors rejects ONLY an out-of-range id and
    // inspects no cell: INV_GetCharStatsTxtLine (0x4833e0) does `test eax,eax / jl` at
    // 0x4833e4 and `cmp eax, [ecx+0BC8h] / jge` at 0x4833f2; TXT_Skills_GetLine
    // (0x45c4b0) is the same shape. A content test drops DescFunc 13/14/27 (and 16/24/
    // 27/28) lines the engine emits for a row whose name cell happens to be blank.
    //
    // This defect appeared three times — SkillExists, MonsterTypeExists, ClassExists —
    // so pin the shape, not one instance.
    const strings = new TblStringTable(synthTbl(['k0', 'k1']), null, null);

    const charstats = TxtFile.parse(
      'class\tStrAllSkills\tStrSkillTab1\tStrSkillTab2\tStrSkillTab3\tStrClassOnly\r\n' +
        'Amazon\tk1\tk1\tk1\tk1\tk1\r\n' +
        '\tk1\tk1\tk1\tk1\tk1\r\n', // blank name, still a real record
    );

    const classes = new TxtCharacterClassTable(charstats, strings);
    expect(classes.classExists(0)).toBe(true);
    expect(classes.classExists(1)).toBe(true); // blank cell does NOT mean absent
    expect(classes.classExists(2)).toBe(false);
    expect(classes.classExists(-1)).toBe(false);

    const skills = TxtFile.parse('skill\tcharclass\tskilldesc\r\nFire Bolt\tsor\t\r\n\t\t\r\n');
    const skillTable = new TxtSkillTable(skills, null, strings);
    expect(skillTable.skillExists(1)).toBe(true);
    expect(skillTable.skillExists(2)).toBe(false);
  });
});

describe('TxtItemStatCostTable', () => {
  it('truncates loaded stat fields to the loaders widths', () => {
    // The loader stores each field with a plain move and no range check — 0x6bde5d dword,
    // 0x6bdeed word, 0x6bde06 byte — so the .txt value is TRUNCATED. Descriptors at
    // 0x637ec6 onwards make descpriority a WORD (read signed by the sort at 0x6379e1) and
    // descfunc/descval/dgrpfunc/dgrpval BYTEs.
    //
    // Not reachable with stock data (all 359 rows are byte-identical to itemstatcost.bin),
    // but the consequences are large: descpriority 40000 sorts FIRST as -25536 rather than
    // last, and descfunc 256 becomes 0, removing the row from the walked array entirely.
    const strings = new TblStringTable(synthTbl(['k0', 'k1']), null, null);

    const file = TxtFile.parse(
      'Stat\tdescpriority\tdescfunc\tdescval\tdgrp\tstuff\r\n' +
        'wide\t40000\t256\t258\t65536\t6\r\n',
    );

    const table = new TxtItemStatCostTable(file, strings);

    const stat = table.tryGetStat(0);
    expect(stat).not.toBeNull();

    expect(stat?.descPriority).toBe(-25536); // (short)40000
    expect(stat?.descFunc).toBe(0); // (byte)256
    expect(stat?.descVal).toBe(2); // (byte)258
    expect(stat?.descGrp).toBe(0); // (ushort)65536

    // descfunc truncated to 0 means the row never enters the emission list.
    expect(table.statIdsByDescPriority).toHaveLength(0);
  });
});

describe('key columns', () => {
  it('resolves an absent column to id zero, not the 5382 sentinel', () => {
    // The loader distinguishes an ABSENT column from a BLANK cell, and so must every
    // provider that reads a KEYTOWORD column:
    //   absent -> the defaults loop writes 0 (0x6bdfd4), so the engine resolves
    //             string.tbl[0], Warriv's Act 1 gossip;
    //   blank  -> the converter runs, STRTABLE_LookupString returns 0 (0x524d8b) and
    //             DATATBLS_LookupStringId substitutes 5382 (0x6117c6), "an evil force".
    //
    // itemstatcost got this right while charstats, MonType, monstats and skilldesc
    // resolved unconditionally, printing "an evil force" where the game prints the gossip.
    // All five now share TxtKeys.Id.
    const baseTable = synthTbl(['index0', 'aKey']);
    const strings = new TblStringTable(baseTable, null, null);

    // Column present but blank -> the sentinel.
    const blank = TxtFile.parse('class\tStrAllSkills\r\nama\t\r\n\r\n');
    expect(new TxtCharacterClassTable(blank, strings).getAllSkillsText(0)).toBe(
      strings.getByIndex(DescStringIds.DescStr2Sentinel),
    );

    // Column absent entirely -> id 0, i.e. the FIRST string in the table.
    const absent = TxtFile.parse('class\r\nama\r\n\r\n');
    expect(new TxtCharacterClassTable(absent, strings).getAllSkillsText(0)).toBe(
      strings.getByIndex(0),
    );

    // And those are genuinely different strings, or the assertions above prove nothing.
    expect(strings.getByIndex(0)).not.toBe(strings.getByIndex(DescStringIds.DescStr2Sentinel));

    // A total miss AND a blank cell both become the 5382 sentinel.
    expect(resolveKey(strings, 'nowhere')).toBe(DescStringIds.DescStr2Sentinel);
    expect(resolveKey(strings, '')).toBe(DescStringIds.DescStr2Sentinel);
  });
});

describe('D2DataFiles', () => {
  const data = D2DataFiles.load();

  it('has every data file present and at the right size', () => {
    const names = [...D2DataFiles.dataFileNames].sort();

    // Required rather than exhaustive: tables get added as writers are implemented, and a
    // fixed list would fail for the wrong reason.
    for (const required of [
      'excel.ItemStatCost.txt',
      'excel.ItemTypes.txt',
      'excel.armor.txt',
      'excel.weapons.txt',
      'excel.misc.txt',
      'excel.charstats.txt',
      'excel.skills.txt',
      'excel.skilldesc.txt',
      'excel.PlayerClass.txt',
      'excel.MonType.txt',
      'excel.monstats.txt',
      'excel.lowqualityitems.txt',
      'excel.UniqueItems.txt',
      'excel.SetItems.txt',
      'excel.MagicPrefix.txt',
      'excel.MagicSuffix.txt',
      'excel.automagic.txt',
      'excel.RarePrefix.txt',
      'excel.RareSuffix.txt',
      'locale.eng.string.tbl',
      'locale.eng.patchstring.tbl',
      'locale.eng.expansionstring.tbl',
    ]) {
      expect(names).toContain(required);
    }

    // These pin the extraction: they are the counts only Patch_D2.mpq's tables produce.
    expect(data.itemStatCost.rowCount).toBe(359);
    expect(data.skills.rowCount).toBe(357);
    expect(data.itemStatCost.skillIdShift).toBe(6);
    expect(data.itemStatCost.statIdsByDescPriority).toHaveLength(207);
  });

  /**
   * Row index IS the record id: the C++ producer emits the game's classId, so a single extra or
   * missing row silently renames every item after it. The expected counts are the record counts
   * in the shipped .bin files the game actually loads (DATATBLS_LoadFromBin), which are one less
   * than the .txt data row count because 0x6bd742 splices out the "Expansion" divider.
   */
  const compiledRowCounts: readonly [string, TxtFile | null, number][] = [
    ['itemtypes', data.itemTypes, 103],
    ['weapons', data.weapons, 306],
    ['armor', data.armor, 202],
    ['misc', data.misc, 151],
    ['uniqueitems', data.uniqueItems, 402],
    ['setitems', data.setItems, 127],
  ];

  for (const [name, file, expected] of compiledRowCounts) {
    it(`table ${name} has the compiled bin row count`, () => {
      expect(file?.rowCount).toBe(expected);
    });
  }

  it('reads the same tables from a directory as from data/', () => {
    const fromPaths = D2DataFiles.load(excelDirectory(), localeDirectory(), globalDirectory());

    expect(fromPaths.itemStatCost.rowCount).toBe(data.itemStatCost.rowCount);
    expect(fromPaths.skills.rowCount).toBe(data.skills.rowCount);
    expect(fromPaths.animDataBytes).not.toBeNull();
  });
});
