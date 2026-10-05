import { StringTable } from './StringTable.js';
import type { ByteSource } from './TxtDataSource.js';

const DESC_STR2_SENTINEL = 5382;
export const MISSING_STRING_KEY = 'strMissingString';

/**
 * Load order, from the pointer array at 0x1415747e0. vo.json is the 18th; the embedded copy leaves
 * it out — no id or key in it collides with any other file, so no lookup changes.
 */
export const HdStringFiles: readonly string[] = [
  'bnet.json',
  'item-gems.json',
  'item-modifiers.json',
  'item-nameaffixes.json',
  'item-names.json',
  'item-runes.json',
  'keybinds.json',
  'levels.json',
  'mercenaries.json',
  'monsters.json',
  'npcs.json',
  'objects.json',
  'quests.json',
  'shrines.json',
  'skills.json',
  'ui.json',
  'ui-controller.json',
  'vo.json',
  'commands.json',
];

/** 0x141574880, loaded FIRST when legacy graphics are on (0x14047776b), then the HD set on top. */
export const LegacyStringFiles: readonly string[] = [
  'bnet.json',
  'item-gems.json',
  'item-modifiers.json',
  'item-nameaffixes.json',
  'item-names.json',
  'item-runes.json',
  'keybinds.json',
  'levels.json',
  'mercenaries.json',
  'monsters.json',
  'npcs.json',
  'objects.json',
  'quests.json',
  'shrines.json',
  'skills.json',
  'ui.json',
  'vo.json',
];

/** off_141574770, indexed by the locale setting. */
export const ResurrectedLanguages: readonly string[] = [
  'enUS',
  'deDE',
  'esES',
  'frFR',
  'itIT',
  'koKR',
  'plPL',
  'ruRU',
  'zhCN',
  'zhTW',
  'esMX',
  'jaJP',
  'ptBR',
];

interface KeyNode {
  readonly id: number;
  readonly text: string;
}

interface Pending {
  readonly id: number;
  readonly key: string;
  readonly text: string;
}

class Table {
  private readonly byKey = new Map<string, KeyNode>();
  private readonly byId = new Map<number, string>();

  /**
   * sub_140476c60. Two passes per file: parse into a pending list, then insert. An entry without an
   * id, a string Key or the language field abandons the WHOLE file, pending entries included
   * (0x140476e86 / 0x140476ebd / 0x140476f38 all reach 0x1404773c3).
   */
  loadFile(bytes: Uint8Array | null, column: string, legacy: boolean): void {
    if (bytes === null) {
      return;
    }

    // TextDecoder drops the byte-order mark the shipped files carry.
    const document: unknown = JSON.parse(new TextDecoder('utf-8').decode(bytes));
    if (!Array.isArray(document)) {
      throw new Error('A D2R string file must be a JSON array.');
    }

    const pending: Pending[] = [];
    for (const entry of document as unknown[]) {
      if (typeof entry !== 'object' || entry === null) {
        return;
      }

      const record = entry as Record<string, unknown>;
      const id = record.id;
      const key = record.Key;
      const text = record[column];
      if (typeof id !== 'number' || typeof key !== 'string' || typeof text !== 'string') {
        return;
      }

      const item: Pending = {
        id: id & 0xffff,
        key,
        // 0x140476f77 rewrites every `ÿc` to U+E07E, the game's internal marker. This library spells
        // the marker `ÿc`, so the same normalisation runs the other way.
        text: text.replaceAll('\uE07E', '\u00FFc'),
      };

      // 0x140476f85-0x14047701b: the legacy table refuses an entry whose id OR key it already
      // holds, so the legacy files take precedence over the HD ones.
      if (legacy && (this.byId.has(item.id) || this.byKey.has(item.key))) {
        continue;
      }

      pending.push(item);
    }

    // 0x1404772e9: an entry whose id OR key is already held is skipped outright — the key node is
    // only created on a miss (0x1404773f0), and the id goes in with it — so the FIRST entry for a
    // key wins and a duplicate's id never enters the table.
    for (const item of pending) {
      if (this.byId.has(item.id) || this.byKey.has(item.key)) {
        continue;
      }

      this.byKey.set(item.key, { id: item.id, text: item.text });
      this.byId.set(item.id, item.text);
    }
  }

  textById(id: number): string | undefined {
    return this.byId.get(id);
  }

  textByKey(key: string): string | undefined {
    return this.byKey.get(key)?.text;
  }

  idOf(key: string): number {
    return this.byKey.get(key)?.id ?? -1;
  }
}

/** D2R's string table, built from the `lng/strings` JSON files. */
export class JsonStringTable extends StringTable {
  /** What render-time lookups read: legacy-first when legacy graphics are on. */
  private readonly text = new Table();

  /**
   * DATATBLS_LoadAllTxts runs once at startup (0x140063844), before the only writer of
   * g_IsRunningLecgacyGfx (0x14061d604), so every load-time key resolution — the type-24 field
   * resolver (0x1401f88f1) and the name loaders that test the same flag — reads the HD table
   * whatever the setting.
   */
  private readonly load: Table;

  private readonly missing: string;

  /**
   * The locale SETTING. Legacy graphics may read another column (enUS), but item names still take
   * this locale's possessive (sub_140478a70 is passed STRTABLE_GetLanguage).
   */
  readonly language: string;

  constructor(
    source: ByteSource,
    legacySource: ByteSource | null,
    legacy: boolean,
    language: string,
  ) {
    super();

    const locale = ResurrectedLanguages.indexOf(language);
    if (locale < 0) {
      throw new Error('Unknown D2R language: ' + language);
    }

    this.language = language;

    // 0x140476dbe: legacy mode reads enUS for every locale but 0-6 and 9.
    const column = !legacy || locale <= 6 || locale === 9 ? language : 'enUS';

    if (legacy) {
      if (legacySource === null) throw new Error('legacySource');
      for (const name of LegacyStringFiles) {
        this.text.loadFile(legacySource(name), column, true);
      }
    }

    for (const name of HdStringFiles) {
      this.text.loadFile(source(name), column, legacy);
    }

    if (legacy) {
      // The HD table's own load (sub_1404776E0(0)) never forces enUS.
      this.load = new Table();
      for (const name of HdStringFiles) {
        this.load.loadFile(source(name), language, false);
      }
    } else {
      this.load = this.text;
    }

    // Each table's load ends by copying its strMissingString into g_pStringTable (0x140477a5c), the
    // fallback of every by-index and by-key miss. The HD table loads after the legacy one
    // (0x140477ac5), so its text is the one that stays.
    this.missing = this.load.textByKey(MISSING_STRING_KEY) ?? '';
  }

  /** LANG_GetStringFromTblIndex 0x140477c70: a 16-bit id, or the missing-string text. */
  getByIndex(index: number): string {
    return this.text.textById(index & 0xffff) ?? this.missing;
  }

  /** The id a txt cell was given at load — from the HD table, even under legacy text. */
  getIndexByKey(key: string): number {
    return this.load.idOf(key);
  }

  /** DATATBLS_GetStringIdFromReferenceString 0x1401f88b0. A blank cell misses too. */
  resolveKey(key: string): number {
    const index = this.getIndexByKey(key);
    return index >= 0 ? index : DESC_STR2_SENTINEL;
  }

  /** LANG_GetWideStringFromKey 0x140477d70: exact, case-sensitive. */
  getByKey(key: string): string {
    return this.text.textByKey(key) ?? this.missing;
  }

  /** Whether getByKey finds the key rather than falling back. */
  override hasKey(key: string): boolean {
    return this.text.textByKey(key) !== undefined;
  }
}
