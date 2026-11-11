namespace XylemUI;

/// <summary>
/// The names of the app's actions (design §7, "Shortcuts and actions"): each name gives a function that makes the action, and
/// each action type its name. A keymap and a menu refer to an action by its name.
/// </summary>
/// <remarks>
/// The source generator of <c>XylemUI.Generators</c> writes the calls of <see cref="Register{TAction}"/> for the types that carry
/// <see cref="ActionAttribute"/>, in the app's module initializer; nothing is found by reflection. The table belongs to the UI
/// thread's process: it is written before the app's first window, and read after.
/// </remarks>
public static class ActionRegistry
{
    private static readonly Lock Gate = new();
    private static readonly Dictionary<string, Func<IAction>> ByName = new(StringComparer.Ordinal);
    private static readonly Dictionary<Type, string> ByType = [];

    /// <summary>
    /// Adds an action type under its name.
    /// </summary>
    /// <typeparam name="TAction">The type of the action.</typeparam>
    /// <param name="name">The name, unique among the app's actions.</param>
    /// <param name="create">Makes an action of the type with its default arguments.</param>
    /// <exception cref="ArgumentException">The name is empty.</exception>
    /// <exception cref="InvalidOperationException">Another type has the name, or the type has another name.</exception>
    public static void Register<TAction>(string name, Func<TAction> create)
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
