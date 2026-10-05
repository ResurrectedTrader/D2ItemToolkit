namespace D2ItemToolkit
{
    /// <summary>Which game's description engine and tables a tooltip is rendered with.</summary>
    public enum GameVariant
    {
        /// <summary>Diablo II: Lord of Destruction 1.14d.</summary>
        Lod114d = 0,

        /// <summary>
        /// Diablo II: Resurrected — <c>excel/base/</c>, the tables the game loads for an item of
        /// version 1 or 2 (0x1401f8620). Same engine as <see cref="ReignOfTheWarlock"/>.
        /// </summary>
        Resurrected = 1,

        /// <summary>
        /// Diablo II: Resurrected — Reign of the Warlock: <c>excel/</c>, the tables the game loads
        /// for an item of version 3.
        /// </summary>
        ReignOfTheWarlock = 2,
    }

    /// <summary>Options that only exist for the Diablo II: Resurrected variants.</summary>
    public sealed class ResurrectedTextOptions
    {
        public static readonly ResurrectedTextOptions Default = new ResurrectedTextOptions();

        /// <summary>
        /// The strings shown with legacy graphics on: <c>strings-legacy</c> loaded over the HD set
        /// (0x1404776e0). A handful of modifier strings differ, e.g. "Quantity: %d".
        /// </summary>
        public bool LegacyGraphics { get; set; }

        /// <summary>A D2R locale column such as <c>enUS</c> or <c>deDE</c>.</summary>
        public string Language { get; set; } = "enUS";
    }
}
