using System.Text;
using Xunit;

namespace D2ItemToolkit.Tests
{
    /// <summary>The D2R string table's load rules (sub_1404776e0 / sub_140476c60).</summary>
    public class JsonStringTableTests
    {
        private static byte[] Json(string text)
        {
            return Encoding.UTF8.GetBytes(text);
        }

        private static JsonStringTable Table(string hd, string legacy = null, bool legacyMode = false)
        {
            return new JsonStringTable(
                name => name == "ui.json" ? Json(hd) : null,
                name => name == "ui.json" && legacy != null ? Json(legacy) : null,
                legacyMode,
                "enUS");
        }

        [Fact]
        public void The_first_entry_for_a_key_wins_and_a_duplicates_id_is_dropped()
        {
            JsonStringTable table = Table(
                "[{\"id\":10,\"Key\":\"k\",\"enUS\":\"first\"}," +
                "{\"id\":11,\"Key\":\"k\",\"enUS\":\"second\"}," +
                "{\"id\":12,\"Key\":\"strMissingString\",\"enUS\":\"Missing string\"}]");

            Assert.Equal(10, table.ResolveKey("k"));
            Assert.Equal("first", table.GetByKey("k"));
            Assert.Equal("Missing string", table.GetByIndex(11));
        }

        [Fact]
        public void A_missing_key_resolves_to_5382_and_a_missing_id_to_the_missing_string()
        {
            JsonStringTable table = Table(
                "[{\"id\":12,\"Key\":\"strMissingString\",\"enUS\":\"Missing string\"}]");

            Assert.Equal(5382, table.ResolveKey("nope"));
            Assert.Equal(5382, table.ResolveKey(string.Empty));
            Assert.Equal("Missing string", table.GetByIndex(999));
            Assert.Equal("Missing string", table.GetByKey("nope"));
        }

        [Fact]
        public void Keys_are_case_sensitive()
        {
            JsonStringTable table = Table("[{\"id\":10,\"Key\":\"Abc\",\"enUS\":\"x\"}]");
            Assert.Equal(10, table.ResolveKey("Abc"));
            Assert.Equal(5382, table.ResolveKey("abc"));
        }

        [Fact]
        public void Legacy_strings_load_first_and_win()
        {
            JsonStringTable table = Table(
                "[{\"id\":10,\"Key\":\"q\",\"enUS\":\"Quantity: %d of %d\"}]",
                "[{\"id\":10,\"Key\":\"q\",\"enUS\":\"Quantity: %d\"}]",
                legacyMode: true);

            Assert.Equal("Quantity: %d", table.GetByIndex(10));
        }

        [Fact]
        public void Legacy_text_keeps_the_hd_ids_and_the_hd_missing_string()
        {
            // The txts resolve their keys before g_IsRunningLecgacyGfx is first set (0x140063844 vs
            // 0x14061d604), so through the HD table; the HD table loads last, so its
            // strMissingString is the miss fallback (0x140477ac5).
            JsonStringTable table = Table(
                "[{\"id\":12,\"Key\":\"strMissingString\",\"enUS\":\"HD missing\"}," +
                "{\"id\":27574,\"Key\":\"terror\",\"enUS\":\"HD text\"}]",
                "[{\"id\":12,\"Key\":\"strMissingString\",\"enUS\":\"Legacy missing\"}," +
                "{\"id\":27446,\"Key\":\"terror\",\"enUS\":\"Legacy text\"}]",
                legacyMode: true);

            Assert.Equal(27574, table.ResolveKey("terror"));
            Assert.Equal(27574, table.GetIndexByKey("terror"));
            Assert.Equal("HD missing", table.GetByIndex(27574));
            Assert.Equal("Legacy text", table.GetByIndex(27446));
            Assert.Equal("Legacy text", table.GetByKey("terror"));
            Assert.True(table.HasKey("terror"));
            Assert.False(table.HasKey("nope"));
            Assert.Equal("HD missing", table.GetByKey("nope"));
            Assert.Equal("Legacy missing", table.GetByKey("strMissingString"));
        }

        [Fact]
        public void An_entry_without_the_language_abandons_the_whole_file()
        {
            JsonStringTable table = Table(
                "[{\"id\":10,\"Key\":\"a\",\"enUS\":\"a\"},{\"id\":11,\"Key\":\"b\"}]");

            Assert.Equal(5382, table.ResolveKey("a"));
        }

        [Fact]
        public void The_authoring_colour_escape_becomes_the_game_escape()
        {
            JsonStringTable table = Table("[{\"id\":10,\"Key\":\"c\",\"enUS\":\"\\ue07e4Gold\"}]");
            Assert.Equal("\u00FFc4Gold", table.GetByIndex(10));
        }

        [Fact]
        public void The_embedded_tables_resolve_the_compiled_itemstatcost_ids()
        {
            // Spot values the shipped itemstatcost.bin carries for row 0 (strength).
            D2DataFiles data = D2DataFiles.LoadEmbedded(GameVariant.ReignOfTheWarlock);
            Assert.Equal("%+d to Strength", data.Strings.GetByIndex(data.ItemStatCost.RowAt(0).DescStrPos));
        }
    }
}
