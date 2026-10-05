namespace D2ItemToolkit
{
    /// <summary>
    /// The game's localised string table. Ids are the 1.14d composite numbering, which D2R keeps:
    /// its JSON <c>id</c> column carries the same values the .tbl cascade produced.
    /// </summary>
    public abstract class StringTable : IStringTable
    {
        public abstract string GetByIndex(int index);

        public abstract int GetIndexByKey(string key);

        /// <summary>What the excel loader stores for a string-key cell.</summary>
        public abstract int ResolveKey(string key);

        /// <summary>The text for a key, as the game's by-key lookup returns it.</summary>
        public abstract string GetByKey(string key);

        /// <summary>Whether the by-key lookup finds the key rather than falling back.</summary>
        public virtual bool HasKey(string key)
        {
            return GetIndexByKey(key) >= 0;
        }
    }
}
