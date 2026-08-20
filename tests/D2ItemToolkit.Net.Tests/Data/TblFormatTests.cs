using System;
using Xunit;

namespace D2ItemToolkit.Tests
{
    public class TblFormatTests
    {
        [Fact]
        public void A_null_format_yields_an_empty_string()
        {
            // 0x5269e1 returns leaving the destination as the caller zeroed it, which is an
            // empty line rather than an absent one.
            Assert.Equal(string.Empty, TblFormat.Format(null, 1));
        }

        [Fact]
        public void An_empty_format_comes_back_unchanged()
        {
            Assert.Equal(string.Empty, TblFormat.Format(string.Empty, 1));
        }

        [Fact]
        public void Text_with_no_placeholders_is_passed_through()
        {
            Assert.Equal("Indestructible", TblFormat.Format("Indestructible"));
        }

        [Theory]
        [InlineData("%d", "7")]
        [InlineData("%u", "7")]
        public void Every_integer_specifier_substitutes(string format, string expected)
        {
            Assert.Equal(expected, TblFormat.Format(format, 7));
        }

        [Fact]
        public void Percent_i_is_not_a_valid_specifier()
        {
            // 0x526a99's jump table handles only \0, %, d, s and u; 'i' halts the game.
            Assert.Throws<FormatException>(() => TblFormat.Format("%i", 7));
        }

        [Fact]
        public void A_string_specifier_substitutes()
        {
            Assert.Equal("to Teleport", TblFormat.Format("to %s", "Teleport"));
        }

        [Fact]
        public void A_doubled_percent_becomes_a_literal_one()
        {
            Assert.Equal("5% Chance", TblFormat.Format("%d%% Chance", 5, 0));
        }

        [Fact]
        public void A_trailing_percent_is_left_alone()
        {
            Assert.Equal("Chance %", TblFormat.Format("Chance %"));
        }

        [Fact]
        public void An_unsupported_specifier_is_fatal()
        {
            // 0x526c66 calls ERROR_UnrecoverableInternalError_Halt then exit(-1).
            Assert.Throws<FormatException>(() => TblFormat.Format("%x %d", 7));
        }

        [Fact]
        public void A_placeholder_with_no_argument_left_is_left_alone()
        {
            Assert.Equal("7 %d", TblFormat.Format("%d %d", 7));
        }

        [Fact]
        public void A_null_argument_array_leaves_placeholders_alone()
        {
            Assert.Equal("%d", TblFormat.Format("%d", null));
        }

        [Fact]
        public void A_null_string_argument_is_surfaced_rather_than_emulating_a_fault()
        {
            // 0x526761 dereferences it with no guard when there is room left.
            Assert.Throws<FormatException>(() => TblFormat.Format("to %s", (object)null));
        }

        [Fact]
        public void A_non_integer_argument_uses_its_own_string_form()
        {
            Assert.Equal("value 2.5", TblFormat.Format("value %s", 2.5m.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        [Fact]
        public void Arguments_substitute_positionally_in_order()
        {
            Assert.Equal("Level 3 Teleport (13/20 Charges)",
                TblFormat.Format("Level %d %s (%d/%d Charges)", 3, "Teleport", 13, 20));
        }
    }
}
