using System.Collections.Generic;
using Xunit;

namespace D2ItemToolkit.Tests
{
    public class RunesTableTests
    {
        private static readonly D2DataFiles Data = D2DataFiles.LoadEmbedded();

        [Fact]
        public void Runes_txt_is_loaded_from_the_embedded_resources()
        {
            Assert.NotNull(Data.Runes);
            Assert.Equal(169, Data.Runes.RowCount);
        }

        [Fact]
        public void Runeword_names_can_be_enumerated()
        {
            // The `Name` column is a string-table KEY, and it is the same key the game resolved to
            // an id at table-compile time — which is what a runeword item then carries in
            // MagicPrefix[0]. So a caller reaches the displayed name the same way the engine does.
            var names = new List<string>();

            for (int row = 0; row < Data.Runes.RowCount; ++row)
            {
                if (Data.Runes.GetInt(row, "complete") == 0)
                {
                    continue;
                }

                string key = Data.Runes.GetString(row, "Name").Trim();
                if (key.Length == 0)
                {
                    continue;
                }

                int id = Data.Strings.ResolveKey(key);
                string name = Data.Strings.GetByIndex(id);
                if (!string.IsNullOrEmpty(name))
                {
                    names.Add(name);
                }
            }

            // Ancients' Pledge, not Ancient's — the apostrophe is where the .tbl puts it, and the
            // whole point of resolving through the string table is that the file's own `Rune Name`
            // column is not the displayed text.
            Assert.Contains("Ancients' Pledge", names);
            Assert.Contains("Call to Arms", names);

            // 78 of the 169 rows are `complete` in shipped data, and every row resolves.
            Assert.Equal(78, names.Count);
        }

        [Fact]
        public void The_recipe_columns_are_readable()
        {
            int row = Data.Runes.FindRow("Name", "Runeword1");
            Assert.True(row >= 0);

            Assert.Equal("r08", Data.Runes.GetString(row, "Rune1").Trim());
            Assert.Equal("r09", Data.Runes.GetString(row, "Rune2").Trim());
            Assert.Equal("r07", Data.Runes.GetString(row, "Rune3").Trim());
        }
    }
}
