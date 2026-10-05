using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace D2ItemToolkit
{
    /// <summary>
    /// The C runtime's printf, as D2R's string formatting reaches it (SStrPrintf 0x14014f1f0 and
    /// the descfunc writers). D2R moved the signs and percent signs INTO its strings — "Fire Absorb
    /// %+d%%" — which the 1.14d formatter (0x5269d0, `%d %s %u` only) has no notion of.
    /// </summary>
    internal static class CFormat
    {
        public static string Sprintf(string format, params object[] args)
        {
            if (string.IsNullOrEmpty(format))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(format.Length + 16);
            int nextArg = 0;

            for (int i = 0; i < format.Length; ++i)
            {
                char c = format[i];
                if (c != '%')
                {
                    builder.Append(c);
                    continue;
                }

                ++i;

                bool leftAlign = false, plus = false, space = false, zero = false, alternate = false;
                for (; i < format.Length; ++i)
                {
                    char flag = format[i];
                    if (flag == '-') leftAlign = true;
                    else if (flag == '+') plus = true;
                    else if (flag == ' ') space = true;
                    else if (flag == '0') zero = true;
                    else if (flag == '#') alternate = true;
                    else break;
                }

                int width = ReadNumber(format, ref i);
                int precision = -1;
                if (i < format.Length && format[i] == '.')
                {
                    ++i;
                    precision = ReadNumber(format, ref i);
                }

                // A conversion still open at the NUL writes nothing: the loop just ends
                // (0x140b477d5) and the a3 == 0 callers skip the deferred pass (0x140b47c29).
                if (i >= format.Length)
                {
                    break;
                }

                char spec = format[i];
                string body;
                switch (spec)
                {
                    case '%':
                        builder.Append('%');
                        continue;

                    case 'd':
                    case 'i':
                    {
                        long value = Convert.ToInt64(Next(args, ref nextArg), CultureInfo.InvariantCulture);
                        string digits = Digits((ulong)Math.Abs(value), precision, 10, false);
                        string sign = value < 0 ? "-" : plus ? "+" : space ? " " : string.Empty;
                        body = Pad(sign, digits, width, leftAlign, zero && precision < 0);
                        break;
                    }

                    case 'u':
                    case 'x':
                    case 'X':
                    {
                        uint value = unchecked((uint)Convert.ToInt64(
                            Next(args, ref nextArg), CultureInfo.InvariantCulture));
                        int radix = spec == 'u' ? 10 : 16;
                        string digits = Digits(value, precision, radix, spec == 'X');
                        string prefix = alternate && radix == 16 && value != 0
                            ? (spec == 'X' ? "0X" : "0x")
                            : string.Empty;
                        body = Pad(prefix, digits, width, leftAlign, zero && precision < 0);
                        break;
                    }

                    case 'c':
                    {
                        object arg = Next(args, ref nextArg);
                        string text = arg is char ? arg.ToString()
                            : ((char)Convert.ToInt32(arg, CultureInfo.InvariantCulture)).ToString();
                        body = Pad(string.Empty, text, width, leftAlign, false);
                        break;
                    }

                    case 's':
                    {
                        string text = Next(args, ref nextArg) as string ?? "(null)";
                        if (precision >= 0 && text.Length > precision)
                        {
                            text = text.Substring(0, precision);
                        }

                        body = Pad(string.Empty, text, width, leftAlign, false);
                        break;
                    }

                    default:
                        // sub_140b47700 prints an unknown conversion character as itself.
                        body = spec.ToString();
                        break;
                }

                builder.Append(body);
            }

            return builder.ToString();
        }

        /// <summary>
        /// The positional wrappers (sub_14060cb00 and siblings): <c>%0</c>, <c>%1</c>, ... become
        /// <c>%d</c> or <c>%s</c> by the argument's type and pick arguments by index. A format with no
        /// markers is plain printf; one that names only SOME of the arguments writes nothing.
        /// </summary>
        public static string Positional(string format, params object[] args)
        {
            if (string.IsNullOrEmpty(format))
            {
                return string.Empty;
            }

            var order = new List<object>();
            var seen = new HashSet<int>();
            var rewritten = new StringBuilder(format.Length);
            for (int i = 0; i < format.Length; ++i)
            {
                char c = format[i];
                if (c == '%' && i + 1 < format.Length)
                {
                    char next = format[i + 1];
                    if (next == '%')
                    {
                        rewritten.Append("%%");
                        ++i;
                        continue;
                    }

                    if (next >= '0' && next <= '9')
                    {
                        int index = next - '0';
                        object arg = args != null && index < args.Length ? args[index] : null;
                        rewritten.Append(arg is string ? "%s" : "%d");
                        order.Add(arg);
                        seen.Add(index);
                        ++i;
                        continue;
                    }
                }

                rewritten.Append(c);
            }

            if (seen.Count == 0)
            {
                return Sprintf(format, args);
            }

            int count = args == null ? 0 : args.Length;
            for (int index = 0; index < count; ++index)
            {
                if (!seen.Contains(index))
                {
                    return string.Empty;
                }
            }

            return Sprintf(rewritten.ToString(), order.ToArray());
        }

        /// <summary>
        /// sub_14060d840, descfunc 15's (value, level, name) wrapper. Each of <c>%0</c>, <c>%1</c>,
        /// <c>%2</c> is found by its own scan, and only its first occurrence is rewritten (to
        /// <c>d</c>, <c>d</c>, <c>s</c>); the arguments are passed in the order the markers appear.
        /// <c>%0</c> without both others, or <c>%1</c>/<c>%2</c> without <c>%0</c>, writes nothing.
        /// </summary>
        public static string PositionalValueLevelName(string format, int value, int level, string name)
        {
            return PositionalWrapper(format, "dds", false, value, level, name);
        }

        /// <summary>
        /// The descfunc positional wrappers share one shape. sub_14060cb00 (d d, func 11's repair
        /// string), sub_14060cea0 (d s, funcs 16 and 28), sub_14060d840 (d d s, func 15),
        /// sub_14060de20 (d s s, func 27) and sub_14060e430 (d s d d, func 24).
        ///
        /// Each scans for its markers, rewrites the first copy of each marker to its fixed type,
        /// and passes the arguments in the order the markers appear. If <c>%0</c> is present and
        /// any other marker is missing, nothing is written. With no <c>%0</c>, the format is plain
        /// printf when no other marker is present either, and nothing otherwise. sub_14060e430
        /// also writes nothing unless <c>%0</c> and <c>%1</c> both precede <c>%2</c> and
        /// <c>%2</c> precedes <c>%3</c> (0x14060e6ca). The format is cut to 1023 bytes and the
        /// output to 255.
        /// </summary>
        public static string PositionalWrapper(
            string format, string types, bool chargesOrder, params object[] args)
        {
            if (string.IsNullOrEmpty(format))
            {
                return string.Empty;
            }

            char[] text = Bounded(format, 0x400).ToCharArray();
            var positions = new int[types.Length];
            for (int k = 0; k < types.Length; ++k)
            {
                positions[k] = FindPositionalMarker(text, (char)('0' + k));
            }

            object[] ordered;
            if (positions[0] >= 0)
            {
                for (int k = 1; k < positions.Length; ++k)
                {
                    if (positions[k] < 0)
                    {
                        return string.Empty;
                    }
                }

                if (chargesOrder
                    && !(positions[0] < positions[2] && positions[1] < positions[2] && positions[2] < positions[3]))
                {
                    return string.Empty;
                }

                var byPosition = new SortedList<int, object>();
                for (int k = 0; k < positions.Length; ++k)
                {
                    text[positions[k]] = types[k];
                    byPosition.Add(positions[k], args[k]);
                }

                ordered = new object[byPosition.Count];
                byPosition.Values.CopyTo(ordered, 0);
            }
            else
            {
                for (int k = 1; k < positions.Length; ++k)
                {
                    if (positions[k] >= 0)
                    {
                        return string.Empty;
                    }
                }

                ordered = args;
            }

            return Bounded(Sprintf(new string(text), ordered), 0x100);
        }

        // A '%' toggles "inside a conversion"; flag and length characters (' ' # + - . I L h j l
        // q t w z, the bitmasks at 0x14060d8ab-0x14060d8bf) keep it open, anything else closes it.
        // The marker is the wanted digit met while open.
        private static int FindPositionalMarker(char[] text, char digit)
        {
            const string KeepsOpen = " #+-.ILhjlqtwz";

            bool open = false;
            for (int i = 0; i < text.Length; ++i)
            {
                char c = text[i];
                if (c == '%')
                {
                    open = !open;
                }
                else if (KeepsOpen.IndexOf(c) < 0)
                {
                    if (open && c == digit)
                    {
                        return i;
                    }

                    open = false;
                }
            }

            return -1;
        }

        public static int Utf8Length(string text)
        {
            return Encoding.UTF8.GetByteCount(text ?? string.Empty);
        }

        /// <summary>
        /// What fits a C buffer of <paramref name="size"/> bytes: at most size - 1 bytes of the
        /// UTF-8 text, cut mid-character if that is where the limit falls.
        /// </summary>
        public static string Bounded(string text, int size)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text ?? string.Empty);
            return bytes.Length < size ? text ?? string.Empty : Encoding.UTF8.GetString(bytes, 0, size - 1);
        }

        private static object Next(object[] args, ref int nextArg)
        {
            if (args == null || nextArg >= args.Length)
            {
                throw new FormatException("More format specifiers than arguments.");
            }

            return args[nextArg++];
        }

        private static int ReadNumber(string format, ref int i)
        {
            int value = 0;
            while (i < format.Length && format[i] >= '0' && format[i] <= '9')
            {
                value = (value * 10) + (format[i] - '0');
                ++i;
            }

            return value;
        }

        private static string Digits(ulong value, int precision, int radix, bool upper)
        {
            // C: a zero value with an explicit zero precision prints no digits at all.
            if (value == 0 && precision == 0)
            {
                return string.Empty;
            }

            string digits = radix == 10
                ? value.ToString(CultureInfo.InvariantCulture)
                : value.ToString(upper ? "X" : "x", CultureInfo.InvariantCulture);

            return precision > digits.Length ? new string('0', precision - digits.Length) + digits : digits;
        }

        private static string Pad(string prefix, string digits, int width, bool leftAlign, bool zero)
        {
            int length = prefix.Length + digits.Length;
            if (length >= width)
            {
                return prefix + digits;
            }

            string fill = new string(zero && !leftAlign ? '0' : ' ', width - length);
            if (leftAlign)
            {
                return prefix + digits + fill;
            }

            return zero ? prefix + fill + digits : fill + prefix + digits;
        }
    }
}
