/**
 * The game's localised string table. Ids are the 1.14d composite numbering, which D2R keeps: its
 * JSON `id` column carries the same values the .tbl cascade produced.
 */
export abstract class StringTable {
  abstract getByIndex(index: number): string | null;

  abstract getIndexByKey(key: string): number;

  /** What the excel loader stores for a string-key cell. */
  abstract resolveKey(key: string): number;

  /** The text for a key, as the game's by-key lookup returns it. */
  abstract getByKey(key: string): string | null;

  /** Whether the by-key lookup finds the key rather than falling back. */
  hasKey(key: string): boolean {
    return this.getIndexByKey(key) >= 0;
  }
}
