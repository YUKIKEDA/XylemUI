using System.Globalization;

namespace XylemUI.Tools.CldrTableGen;

/// <summary>
/// Turns the condition of a plural rule into a C# expression over the operands (design §4.11, "Plural rules": the tool turns the
/// rules into methods, and no rule text is read at run time). The syntax is the one of UTS #35 part 3 (CLDR 49), "Plural rules
/// syntax":
/// <code>
/// condition     = and_condition ('or' and_condition)*
/// and_condition = relation ('and' relation)*
/// relation      = expr ('=' | '!=') range_list
/// expr          = operand ('%' value)?
/// range_list    = (range | value) (',' range_list)*
/// range         = value '..' value
/// </code>
/// The older words (<c>is</c>, <c>in</c>, <c>within</c>, <c>not</c>) are not in CLDR 49's data, and the tool stops on them.
/// </summary>
internal static class PluralRuleCompiler
{
    /// <summary>
    /// Returns the C# expression of a condition. The operands are fields of a <c>PluralOperands</c> named <c>operands</c>; a range
    /// is <c>PluralOperands.InRange</c>, which holds only for whole values, so that <c>n = 2..4</c> does not hold for 2.5.
    /// </summary>
    /// <param name="condition">The condition, without its samples (<c>n % 10 = 1 and n % 100 != 11</c>).</param>
    /// <returns>The expression.</returns>
    public static string Compile(string condition)
    {
        List<string> tokens = Tokenize(condition);
        int position = 0;
        string expression = Condition(tokens, ref position);
        if (position != tokens.Count)
        {
            throw Malformed(condition);
        }

        return expression;

        string Condition(List<string> list, ref int at)
        {
            var parts = new List<string> { And(list, ref at) };
            while (at < list.Count && list[at] == "or")
            {
                at++;
                parts.Add(And(list, ref at));
            }

            return parts.Count == 1 ? parts[0] : string.Join(" || ", parts);
        }

        string And(List<string> list, ref int at)
        {
            var parts = new List<string> { Relation(list, ref at) };
            while (at < list.Count && list[at] == "and")
            {
                at++;
                parts.Add(Relation(list, ref at));
            }

            return string.Join(" && ", parts);
        }

        string Relation(List<string> list, ref int at)
        {
            string operand = Operand(Next(list, ref at));
            if (at < list.Count && list[at] == "%")
            {
                at++;
                operand = "(" + operand + " % " + Value(Next(list, ref at)) + ")";
            }

            string relation = Next(list, ref at);
            if (relation is not ("=" or "!="))
            {
                throw Malformed(condition);
            }

            var items = new List<string>();
            while (true)
            {
                string low = Value(Next(list, ref at));
                if (at < list.Count && list[at] == "..")
                {
                    at++;
                    string high = Value(Next(list, ref at));
                    items.Add("PluralOperands.InRange(" + operand + ", " + low + ", " + high + ")");
                }
                else
                {
                    items.Add(operand + " == " + low);
                }

                if (at < list.Count && list[at] == ",")
                {
                    at++;
                    continue;
                }

                break;
            }

            if (relation == "=")
            {
                return items.Count == 1 ? items[0] : "(" + string.Join(" || ", items) + ")";
            }

            return items.Count == 1 && !items[0].StartsWith("PluralOperands.", StringComparison.Ordinal)
                ? items[0].Replace(" == ", " != ", StringComparison.Ordinal)
                : "!(" + string.Join(" || ", items) + ")";
        }

        string Next(List<string> list, ref int at) => at < list.Count ? list[at++] : throw Malformed(condition);
    }

    private static string Operand(string token) =>
        token switch
        {
            "n" => "operands.N",
            "i" => "operands.I",
            "v" => "operands.V",
            "w" => "operands.W",
            "f" => "operands.F",
            "t" => "operands.T",

            // e is the older name of c, the exponent of a compact decimal (UTS #35 part 3, "Plural Operand Meanings").
            "c" or "e" => "operands.E",
            _ => throw new InvalidDataException("The plural operand " + token + " is not one the tool knows."),
        };

    private static string Value(string token)
    {
        if (
            token.Length == 0
            || !token.All(char.IsAsciiDigit)
            || !int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out int value)
        )
        {
            throw new InvalidDataException("The plural rule value " + token + " is not a whole number of an int.");
        }

        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static List<string> Tokenize(string condition)
    {
        var tokens = new List<string>();
        int i = 0;
        while (i < condition.Length)
        {
            char c = condition[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
            }
            else if (char.IsAsciiLetterLower(c))
            {
                int start = i;
                while (i < condition.Length && char.IsAsciiLetterLower(condition[i]))
                {
                    i++;
                }

                tokens.Add(condition[start..i]);
            }
            else if (char.IsAsciiDigit(c))
            {
                int start = i;
                while (i < condition.Length && char.IsAsciiDigit(condition[i]))
                {
                    i++;
                }

                tokens.Add(condition[start..i]);
            }
            else if (c == '.' && i + 1 < condition.Length && condition[i + 1] == '.')
            {
                tokens.Add("..");
                i += 2;
            }
            else if (c == '!' && i + 1 < condition.Length && condition[i + 1] == '=')
            {
                tokens.Add("!=");
                i += 2;
            }
            else if (c is '=' or '%' or ',')
            {
                tokens.Add(condition[i..(i + 1)]);
                i++;
            }
            else
            {
                throw Malformed(condition);
            }
        }

        // Words other than the operands and the two conjunctions are the older syntax.
        foreach (string token in tokens)
        {
            if (char.IsAsciiLetterLower(token[0]) && token.Length > 1 && token is not ("and" or "or"))
            {
                throw Malformed(condition);
            }
        }

        return tokens;
    }

    private static InvalidDataException Malformed(string condition) =>
        new("The plural rule \"" + condition + "\" is not in the syntax the tool reads.");
}
