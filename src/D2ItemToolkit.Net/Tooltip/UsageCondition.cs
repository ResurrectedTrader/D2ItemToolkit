using System;
using System.Globalization;

namespace D2ItemToolkit
{
    /// <summary>
    /// D2R's items.txt <c>UsageConditionCalc</c>, evaluated for the name colour (ITEMS_GetName
    /// 0x14015926f: a result of 0 reddens the name). The game compiles these with its general calc
    /// engine; this covers the grammar the shipped cells use — integers, parentheses, <c>?:</c>
    /// and the two conditions the Worldstone Shards test — and reports anything else as
    /// unevaluable rather than guessing.
    ///
    /// sub_1402790e0 evaluates the conditions: <c>cond('Difficulty', x)</c> is the game's
    /// difficulty equal to x (normal 0, nightmare 1, hell 2, linked at 0x140278ea6), and
    /// <c>cond('IsDesecratedZonesEnabled')</c> asks the game.
    /// </summary>
    internal sealed class UsageCondition
    {
        private readonly string _text;
        private readonly int _difficulty;
        private readonly bool _desecratedZones;
        private int _at;

        private UsageCondition(string text, int difficulty, bool desecratedZones)
        {
            _text = text;
            _difficulty = difficulty;
            _desecratedZones = desecratedZones;
        }

        public static bool TryEvaluate(
            string expression, int difficulty, bool desecratedZones, out int value)
        {
            value = 0;
            if (string.IsNullOrEmpty(expression))
            {
                return false;
            }

            // The cells carry spreadsheet quoting, which the compiled .bin shows the calc
            // compiler accepts.
            if (expression.Length >= 2 && expression[0] == '"'
                && expression[expression.Length - 1] == '"')
            {
                expression = expression.Substring(1, expression.Length - 2);
            }

            var parser = new UsageCondition(expression, difficulty, desecratedZones);
            try
            {
                value = parser.Expression();
                parser.SkipSpace();
                return parser._at == parser._text.Length;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        private int Expression()
        {
            int condition = Primary();
            SkipSpace();
            if (!Accept('?'))
            {
                return condition;
            }

            int whenTrue = Expression();
            Expect(':');
            int whenFalse = Expression();
            return condition != 0 ? whenTrue : whenFalse;
        }

        private int Primary()
        {
            SkipSpace();
            if (Accept('('))
            {
                int value = Expression();
                Expect(')');
                return value;
            }

            if (_at < _text.Length && char.IsDigit(_text[_at]))
            {
                int start = _at;
                while (_at < _text.Length && char.IsDigit(_text[_at]))
                {
                    ++_at;
                }

                int value;
                if (!int.TryParse(
                        _text.Substring(start, _at - start), NumberStyles.None, CultureInfo.InvariantCulture,
                        out value))
                {
                    throw new FormatException("Number out of range.");
                }

                return value;
            }

            string name = Identifier();
            if (!string.Equals(name, "cond", StringComparison.OrdinalIgnoreCase))
            {
                throw new FormatException("Unsupported calc function: " + name);
            }

            Expect('(');
            string condition = Quoted();
            string argument = null;
            if (Accept(','))
            {
                SkipSpace();
                argument = Identifier();
            }

            Expect(')');
            return Condition(condition, argument);
        }

        private int Condition(string name, string argument)
        {
            if (string.Equals(name, "IsDesecratedZonesEnabled", StringComparison.OrdinalIgnoreCase)
                && argument == null)
            {
                return _desecratedZones ? 1 : 0;
            }

            if (string.Equals(name, "Difficulty", StringComparison.OrdinalIgnoreCase)
                && argument != null)
            {
                int wanted;
                if (string.Equals(argument, "normal", StringComparison.OrdinalIgnoreCase)) wanted = 0;
                else if (string.Equals(argument, "nightmare", StringComparison.OrdinalIgnoreCase)) wanted = 1;
                else if (string.Equals(argument, "hell", StringComparison.OrdinalIgnoreCase)) wanted = 2;
                else throw new FormatException("Unknown difficulty: " + argument);

                return _difficulty == wanted ? 1 : 0;
            }

            throw new FormatException("Unsupported condition: " + name);
        }

        private string Identifier()
        {
            int start = _at;
            while (_at < _text.Length && (char.IsLetterOrDigit(_text[_at]) || _text[_at] == '_'))
            {
                ++_at;
            }

            if (_at == start)
            {
                throw new FormatException("Expected an identifier.");
            }

            return _text.Substring(start, _at - start);
        }

        private string Quoted()
        {
            SkipSpace();
            Expect('\'');
            int start = _at;
            while (_at < _text.Length && _text[_at] != '\'')
            {
                ++_at;
            }

            string value = _text.Substring(start, _at - start);
            Expect('\'');
            return value;
        }

        private void SkipSpace()
        {
            while (_at < _text.Length && char.IsWhiteSpace(_text[_at]))
            {
                ++_at;
            }
        }

        private bool Accept(char c)
        {
            SkipSpace();
            if (_at < _text.Length && _text[_at] == c)
            {
                ++_at;
                return true;
            }

            return false;
        }

        private void Expect(char c)
        {
            if (!Accept(c))
            {
                throw new FormatException("Expected '" + c + "'.");
            }
        }
    }
}
