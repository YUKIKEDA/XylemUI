namespace XylemUI;

/// <summary>
/// The condition of a key binding (design §7, "Shortcuts and actions"): which contexts of the nodes from the root to the focused
/// node the binding applies in.
/// </summary>
/// <remarks>
/// The grammar and its meaning are GPUI's (<c>crates/gpui/src/keymap/context.rs</c>): a name (<c>Editor</c>),
/// <c>key == value</c>, <c>key != value</c>, <c>!</c>, <c>&amp;&amp;</c>, <c>||</c>, parentheses, and <c>parent &gt; child</c>.
/// <c>&gt;</c> binds loosest, then <c>||</c>, then <c>&amp;&amp;</c>, then the comparisons, then <c>!</c>. A name and a comparison
/// are about the deepest context of the stack. <c>!</c> is true when its operand is true of no node of the whole stack.
/// <c>parent &gt; child</c> finds the first node from the root where <c>parent</c> is true and is true when <c>child</c> is true of
/// the stack below that node.
/// </remarks>
internal abstract class KeyContextPredicate
{
    /// <summary>
    /// Reads a condition from its text form.
    /// </summary>
    /// <param name="text">The text, as <c>Editor &amp;&amp; mode == full</c>.</param>
    /// <returns>The condition.</returns>
    /// <exception cref="FormatException">The text is not a condition.</exception>
    public static KeyContextPredicate Parse(ReadOnlySpan<char> text)
    {
        ReadOnlySpan<char> rest = text;
        KeyContextPredicate predicate = ParseDescendant(ref rest, text);
        rest = rest.TrimStart();
        return rest.IsEmpty ? predicate : throw Error(text, $"unexpected '{rest}'");
    }

    /// <summary>
    /// Returns how deep in a stack of contexts the condition is true: the length of the longest prefix of the stack it is true
    /// of. A binding whose condition is true deeper wins.
    /// </summary>
    /// <param name="contexts">The contexts from the root to the focused node.</param>
    /// <returns>The depth, from 1; 0 when the condition is true of no prefix.</returns>
    public int Depth(ReadOnlySpan<KeyContext> contexts)
    {
        for (int depth = contexts.Length; depth > 0; depth--)
        {
            if (Evaluate(contexts[..depth], contexts))
            {
                return depth;
            }
        }

        return 0;
    }

    /// <summary>
    /// Returns whether the condition is true of a stack of contexts.
    /// </summary>
    /// <param name="contexts">The contexts from the root to a node; empty gives false.</param>
    /// <param name="all">The whole stack, which <c>!</c> looks through.</param>
    /// <returns>Whether the condition holds.</returns>
    public abstract bool Evaluate(ReadOnlySpan<KeyContext> contexts, ReadOnlySpan<KeyContext> all);

    /// <summary>
    /// Takes a name off the start of a text: letters, digits, <c>_</c>, and <c>-</c>.
    /// </summary>
    /// <param name="text">The text; the name is removed from it.</param>
    /// <returns>The name; empty when the text does not start with one.</returns>
    internal static ReadOnlySpan<char> TakeIdentifier(ref ReadOnlySpan<char> text)
    {
        int length = 0;
        while (length < text.Length && (char.IsLetterOrDigit(text[length]) || text[length] is '_' or '-'))
        {
            length++;
        }

        ReadOnlySpan<char> name = text[..length];
        text = text[length..];
        return name;
    }

    private static KeyContextPredicate ParseDescendant(ref ReadOnlySpan<char> rest, ReadOnlySpan<char> all)
    {
        KeyContextPredicate left = ParseOr(ref rest, all);
        while (true)
        {
            rest = rest.TrimStart();
            if (rest.IsEmpty || rest[0] != '>')
            {
                return left;
            }

            rest = rest[1..];
            left = new Descendant(left, ParseOr(ref rest, all));
        }
    }

    private static KeyContextPredicate ParseOr(ref ReadOnlySpan<char> rest, ReadOnlySpan<char> all)
    {
        KeyContextPredicate left = ParseAnd(ref rest, all);
        while (true)
        {
            rest = rest.TrimStart();
            if (!rest.StartsWith("||"))
            {
                return left;
            }

            rest = rest[2..];
            left = new Or(left, ParseAnd(ref rest, all));
        }
    }

    private static KeyContextPredicate ParseAnd(ref ReadOnlySpan<char> rest, ReadOnlySpan<char> all)
    {
        KeyContextPredicate left = ParseComparison(ref rest, all);
        while (true)
        {
            rest = rest.TrimStart();
            if (!rest.StartsWith("&&"))
            {
                return left;
            }

            rest = rest[2..];
            left = new And(left, ParseComparison(ref rest, all));
        }
    }

    // A name, a comparison of a name with a value, a negation, or a condition in parentheses.
    private static KeyContextPredicate ParseComparison(ref ReadOnlySpan<char> rest, ReadOnlySpan<char> all)
    {
        rest = rest.TrimStart();
        bool named = !rest.IsEmpty && rest[0] is not '!' and not '(';
        ReadOnlySpan<char> start = rest;
        KeyContextPredicate left = ParsePrimary(ref rest, all);
        ReadOnlySpan<char> after = rest.TrimStart();
        bool equal = after.StartsWith("==");
        if (!equal && !after.StartsWith("!="))
        {
            return left;
        }

        // Both sides of a comparison are names: `!a == b` and `(a) == b` are refused, as GPUI refuses them.
        if (!named)
        {
            throw Error(all, "the two sides of '==' and '!=' are names");
        }

        ReadOnlySpan<char> key = TakeIdentifier(ref start);
        rest = after[2..].TrimStart();
        ReadOnlySpan<char> value = TakeIdentifier(ref rest);
        if (value.IsEmpty)
        {
            throw Error(all, $"'{key}' has no value to compare with");
        }

        return new Compare(key.ToString(), value.ToString(), equal);
    }

    private static KeyContextPredicate ParsePrimary(ref ReadOnlySpan<char> rest, ReadOnlySpan<char> all)
    {
        rest = rest.TrimStart();
        if (rest.IsEmpty)
        {
            throw Error(all, "a name is missing");
        }

        // "!=" here would be a comparison with no name before it.
        if (rest[0] == '!' && !rest.StartsWith("!="))
        {
            rest = rest[1..];
            return new Not(ParsePrimary(ref rest, all));
        }

        if (rest[0] == '(')
        {
            rest = rest[1..];
            KeyContextPredicate inner = ParseDescendant(ref rest, all);
            rest = rest.TrimStart();
            if (rest.IsEmpty || rest[0] != ')')
            {
                throw Error(all, "')' is missing");
            }

            rest = rest[1..];
            return inner;
        }

        ReadOnlySpan<char> name = TakeIdentifier(ref rest);
        return name.IsEmpty ? throw Error(all, $"unexpected '{rest}'") : new Name(name.ToString());
    }

    private static FormatException Error(ReadOnlySpan<char> text, string what) =>
        new($"'{text}' is not a key context condition: {what}. Write it as 'Editor && mode == full'.");

    private sealed class Name(string name) : KeyContextPredicate
    {
        public override bool Evaluate(ReadOnlySpan<KeyContext> contexts, ReadOnlySpan<KeyContext> all) => !contexts.IsEmpty && contexts[^1].Has(name);
    }

    private sealed class Compare(string key, string value, bool equal) : KeyContextPredicate
    {
        // A context without the key is equal to no value and unequal to every value.
        public override bool Evaluate(ReadOnlySpan<KeyContext> contexts, ReadOnlySpan<KeyContext> all) =>
            !contexts.IsEmpty && (contexts[^1].Value(key) == value) == equal;
    }

    private sealed class Not(KeyContextPredicate inner) : KeyContextPredicate
    {
        public override bool Evaluate(ReadOnlySpan<KeyContext> contexts, ReadOnlySpan<KeyContext> all)
        {
            if (contexts.IsEmpty)
            {
                return false;
            }

            for (int length = 1; length <= all.Length; length++)
            {
                if (inner.Evaluate(all[..length], all))
                {
                    return false;
                }
            }

            return true;
        }
    }

    private sealed class And(KeyContextPredicate left, KeyContextPredicate right) : KeyContextPredicate
    {
        public override bool Evaluate(ReadOnlySpan<KeyContext> contexts, ReadOnlySpan<KeyContext> all) =>
            left.Evaluate(contexts, all) && right.Evaluate(contexts, all);
    }

    private sealed class Or(KeyContextPredicate left, KeyContextPredicate right) : KeyContextPredicate
    {
        public override bool Evaluate(ReadOnlySpan<KeyContext> contexts, ReadOnlySpan<KeyContext> all) =>
            left.Evaluate(contexts, all) || right.Evaluate(contexts, all);
    }

    private sealed class Descendant(KeyContextPredicate parent, KeyContextPredicate child) : KeyContextPredicate
    {
        public override bool Evaluate(ReadOnlySpan<KeyContext> contexts, ReadOnlySpan<KeyContext> all)
        {
            for (int length = 1; length < contexts.Length; length++)
            {
                if (parent.Evaluate(contexts[..length], all))
                {
                    ReadOnlySpan<KeyContext> below = contexts[length..];
                    return child.Evaluate(below, below);
                }
            }

            return false;
        }
    }
}
