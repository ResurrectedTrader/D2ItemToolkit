/** Which game's description engine and tables a tooltip is rendered with. */
export enum GameVariant {
  /** Diablo II: Lord of Destruction 1.14d. */
  Lod114d = 0,

  /**
   * Diablo II: Resurrected — `excel/base/`, the tables the game loads for an item of version 1 or 2
   * (0x1401f8620). Same engine as {@link GameVariant.ReignOfTheWarlock}.
   */
  Resurrected = 1,

  /** Diablo II: Resurrected — Reign of the Warlock: `excel/`, loaded for an item of version 3. */
  ReignOfTheWarlock = 2,
}

/** Options that only exist for the Diablo II: Resurrected variants. */
export interface ResurrectedTextOptions {
  /**
   * The strings shown with legacy graphics on: `strings-legacy` loaded over the HD set
   * (0x1404776e0). A handful of modifier strings differ, e.g. "Quantity: %d".
   */
  readonly legacyGraphics?: boolean;

  /** A D2R locale column such as `enUS` or `deDE`. */
  readonly language?: string;
}
