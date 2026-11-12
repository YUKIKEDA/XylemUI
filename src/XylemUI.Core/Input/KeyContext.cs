namespace XylemUI;

/// <summary>
/// What a node calls itself for the key bindings (design §7, "Shortcuts and actions"): names, as <c>Editor</c>, and values, as
/// <c>mode = full</c>. A binding's condition is checked against the contexts of the nodes from the root to the focused node.
/// </summary>
/// <remarks>
/// The text form is GPUI's: names and <c>key = value</c> pairs, separated by spaces.
/// </remarks>
internal sealed class KeyContext
{
    private readonly string[] _names;
    private readonly (string Key, string Value)[] _values;

    private KeyContext(string[] names, (string Key, string Value)[] values)
    {
        _names = names;
        _values = values;
    }

    /// <summary>
    /// Reads a context from its text form.
    /// </summary>
    /// <param name="text">The text, as <c>Editor mode = full</c>.</param>
    /// <returns>The context.</returns>
    /// <exception cref="FormatException">The text is not a context.</exception>
    public static KeyContext Parse(ReadOnlySpan<char> text)
    {
        var names = new List<string>();
        var values = new List<(string, string)>();
        ReadOnlySpan<char> rest = text.Trim();
        while (!rest.IsEmpty)
        {
            ReadOnlySpan<char> name = KeyContextPredicate.TakeIdentifier(ref rest);
            if (name.IsEmpty)
            {
                throw new FormatException($"'{text}' is not a key context; write names and 'key = value' pairs, as 'Editor mode = full'.");
            }

            rest = rest.TrimStart();
            if (!rest.IsEmpty && rest[0] == '=')
            {
                rest = rest[1..].TrimStart();
                ReadOnlySpan<char> value = KeyContextPredicate.TakeIdentifier(ref rest);
                if (value.IsEmpty)
                {
                    throw new FormatException($"'{text}' is not a key context; '{name}' has no value after '='.");
                }

                values.Add((name.ToString(), value.ToString()));
                rest = rest.TrimStart();
            }
            else
            {
                names.Add(name.ToString());
            }
        }

        return new KeyContext([.. names], [.. values]);
    }

    /// <summary>
    /// Makes one context of a part's and the app's on the same node: the names of both, and the values of both, the app's value
    /// for a key both have.
    /// </summary>
    /// <param name="part">The part's context; <see langword="null"/> for none.</param>
    /// <param name="app">The app's context; <see langword="null"/> for none.</param>
    /// <returns>The context; <see langword="null"/> when both are.</returns>
    public static KeyContext? Join(KeyContext? part, KeyContext? app)
    {
        if (part is null || app is null)
        {
            return part ?? app;
        }

        // The app's values first: the value of a key is the first one found (Value).
        return new KeyContext([.. part._names, .. app._names], [.. app._values, .. part._values]);
    }

    /// <summary>
    /// Returns whether the context has a name.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <returns>Whether the node calls itself that.</returns>
    public bool Has(string name)
    {
        foreach (string own in _names)
        {
            if (own == name)
            {
                return true;
            }
        }

        // A key with a value is also a name: `mode` is true of a context with `mode = full` (GPUI).
        foreach ((string key, _) in _values)
        {
            if (key == name)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Gets the value of a key.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <returns>The value; <see langword="null"/> when the context has no such key.</returns>
    public string? Value(string key)
    {
        foreach ((string own, string value) in _values)
        {
            if (own == key)
            {
                return value;
            }
        }

        return null;
    }
}
