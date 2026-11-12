namespace XylemUI;

/// <summary>
/// The names of the app's actions (design §7, "Shortcuts and actions"): each name gives a function that makes the action, and
/// each action type its name. A keymap and a menu refer to an action by its name.
/// </summary>
/// <remarks>
/// The source generator of <c>XylemUI.Generators</c> writes the calls of <see cref="Register{TAction}(string, Func{TAction}, ActionArgumentSetter{TAction}?)"/> for the types that carry
/// <see cref="ActionAttribute"/>, in the app's module initializer; nothing is found by reflection. The table belongs to the UI
/// thread's process: it is written before the app's first window, and read after.
/// </remarks>
public static class ActionRegistry
{
    private static readonly Lock Gate = new();
    private static readonly Dictionary<string, Func<IAction>> ByName = new(StringComparer.Ordinal);
    private static readonly Dictionary<Type, string> ByType = [];
    private static readonly Dictionary<string, Setter> Setters = new(StringComparer.Ordinal);
    private static readonly Dictionary<Type, Func<IAction, IAction, bool>> Comparers = [];

    private delegate bool Setter(ref IAction action, ReadOnlySpan<char> name, ReadOnlySpan<char> value);

    /// <summary>
    /// Adds an action type under its name.
    /// </summary>
    /// <typeparam name="TAction">The type of the action.</typeparam>
    /// <param name="name">The name, unique among the app's actions.</param>
    /// <param name="create">Makes an action of the type with its default arguments.</param>
    /// <exception cref="ArgumentException">The name is empty.</exception>
    /// <exception cref="InvalidOperationException">Another type has the name, or the type has another name.</exception>
    public static void Register<TAction>(string name, Func<TAction> create)
        where TAction : class, IAction => Register(name, create, null);

    /// <summary>
    /// Adds an action type under its name, with the function that gives it its arguments.
    /// </summary>
    /// <typeparam name="TAction">The type of the action.</typeparam>
    /// <param name="name">The name, unique among the app's actions.</param>
    /// <param name="create">Makes an action of the type with its default arguments.</param>
    /// <param name="set">Sets an argument of an action from its text; <see langword="null"/> for an action with none.</param>
    /// <exception cref="ArgumentException">The name is empty.</exception>
    /// <exception cref="InvalidOperationException">Another type has the name, or the type has another name.</exception>
    public static void Register<TAction>(string name, Func<TAction> create, ActionArgumentSetter<TAction>? set)
        where TAction : class, IAction
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(create);
        lock (Gate)
        {
            if (ByType.TryGetValue(typeof(TAction), out string? existing))
            {
                // The same table can be registered twice: by the app's module initializer and by a test's.
                if (existing == name)
                {
                    return;
                }

                throw new InvalidOperationException(
                    $"The action type '{typeof(TAction)}' is registered as '{existing}' and cannot also be '{name}'."
                );
            }

            if (ByName.ContainsKey(name))
            {
                throw new InvalidOperationException($"The action name '{name}' is registered for another type than '{typeof(TAction)}'.");
            }

            ByName.Add(name, create);
            ByType.Add(typeof(TAction), name);
            if (set is not null)
            {
                Setters.Add(
                    name,
                    (ref IAction action, ReadOnlySpan<char> argument, ReadOnlySpan<char> value) =>
                    {
                        var typed = (TAction)action;
                        bool done = set(ref typed, argument, value);
                        action = typed;
                        return done;
                    }
                );
            }
        }
    }

    /// <summary>
    /// Makes the action of a name, with its default arguments.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="action">The action; <see langword="null"/> when no type has the name.</param>
    /// <returns>Whether a type has the name.</returns>
    public static bool TryCreate(string name, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IAction? action)
    {
        ArgumentNullException.ThrowIfNull(name);
        Func<IAction>? create;
        lock (Gate)
        {
            _ = ByName.TryGetValue(name, out create);
        }

        action = create?.Invoke();
        return action is not null;
    }

    /// <summary>
    /// Makes the action of a name and gives it arguments (design §7, "Actions with arguments made by name"): each is the name
    /// of a property of the action, compared without case, and its value as text (a number in the invariant culture,
    /// <c>true</c> or <c>false</c>, the name of an enum's member, or the text itself).
    /// </summary>
    /// <param name="name">The action's name.</param>
    /// <param name="arguments">The arguments.</param>
    /// <param name="action">The action; <see langword="null"/> when it could not be made.</param>
    /// <param name="error">What was wrong: no such action, no such argument, or a value that is not of the argument's type.</param>
    /// <returns>Whether the action was made with every argument.</returns>
    public static bool TryCreate(
        string name,
        ReadOnlySpan<KeyValuePair<string, string>> arguments,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IAction? action,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(false)] out string? error
    )
    {
        ArgumentNullException.ThrowIfNull(name);
        Func<IAction>? create;
        Setter? set;
        lock (Gate)
        {
            _ = ByName.TryGetValue(name, out create);
            _ = Setters.TryGetValue(name, out set);
        }

        action = null;
        if (create is null)
        {
            error = $"No action is named '{name}'.";
            return false;
        }

        IAction made = create();
        foreach (KeyValuePair<string, string> argument in arguments)
        {
            if (set is null || !set(ref made, argument.Key, argument.Value))
            {
                error = $"The action '{name}' has no argument '{argument.Key}' that takes '{argument.Value}'.";
                return false;
            }
        }

        action = made;
        error = null;
        return true;
    }

    /// <summary>
    /// Gives an action type the function that compares the arguments of two of its actions (design §7, "Menu items that name
    /// actions"): the keys shown for an action are those of a row whose action has the same arguments. The source generator of
    /// <c>XylemUI.Generators</c> writes one for each action type with arguments, the public properties that can be set, and it
    /// compares them all; a type given none
    /// is compared by the type alone.
    /// </summary>
    /// <typeparam name="TAction">The type of the action.</typeparam>
    /// <param name="same">Returns whether two actions of the type have the same arguments.</param>
    public static void Compare<TAction>(Func<TAction, TAction, bool> same)
        where TAction : class, IAction
    {
        ArgumentNullException.ThrowIfNull(same);
        lock (Gate)
        {
            // The same table can be registered twice, as a name can.
            Comparers[typeof(TAction)] = (left, right) => same((TAction)left, (TAction)right);
        }
    }

    /// <summary>
    /// Returns whether two actions are of one type and have the same arguments; actions of a type that was given no comparison
    /// have.
    /// </summary>
    /// <param name="left">An action.</param>
    /// <param name="right">Another action.</param>
    /// <returns>Whether the keys of one are the keys of the other.</returns>
    internal static bool SameArguments(IAction left, IAction right)
    {
        Type type = left.GetType();
        if (type != right.GetType())
        {
            return false;
        }

        Func<IAction, IAction, bool>? same;
        lock (Gate)
        {
            _ = Comparers.TryGetValue(type, out same);
        }

        return same is null || same(left, right);
    }

    /// <summary>
    /// Gets the name of an action type.
    /// </summary>
    /// <param name="actionType">The type of the action.</param>
    /// <param name="name">The name; <see langword="null"/> when the type was not registered.</param>
    /// <returns>Whether the type was registered.</returns>
    public static bool TryGetName(Type actionType, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? name)
    {
        ArgumentNullException.ThrowIfNull(actionType);
        lock (Gate)
        {
            return ByType.TryGetValue(actionType, out name);
        }
    }
}

/// <summary>
/// Sets an argument of an action from its text (design §7, "Actions with arguments made by name"). The source generator of
/// <c>XylemUI.Generators</c> writes one for each action whose properties can be set.
/// </summary>
/// <typeparam name="TAction">The type of the action.</typeparam>
/// <param name="action">The action; a record is replaced by a copy with the argument set.</param>
/// <param name="name">The argument's name, compared with the property's without case.</param>
/// <param name="value">The value, as text.</param>
/// <returns>Whether the action has the argument and the text is a value of its type.</returns>
public delegate bool ActionArgumentSetter<TAction>(ref TAction action, ReadOnlySpan<char> name, ReadOnlySpan<char> value)
    where TAction : class, IAction;
