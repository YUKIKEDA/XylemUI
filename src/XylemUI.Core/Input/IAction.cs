namespace XylemUI;

/// <summary>
/// An action: a named thing the user asks an app to do, apart from the key or the menu item that asks for it (design §7,
/// "Shortcuts and actions"). A type that implements it is a class, so that dispatching one allocates nothing; its fields are the
/// action's arguments.
/// </summary>
/// <remarks>
/// A handler added with <see cref="ElementExtensions.OnAction{TAction}"/> receives the action when it is dispatched from the
/// focused node. <see cref="ActionAttribute"/> gives the type the name a keymap and a menu refer to it by.
/// </remarks>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1040:Avoid empty interfaces",
    Justification = "The interface marks the types that are dispatched; a handler is found by the action's own type."
)]
public interface IAction;

/// <summary>
/// Names the table of an assembly's actions that the source generator of <c>XylemUI.Generators</c> wrote, and the names in it. An
/// app's generated module initializer calls the table of each assembly it references, and the generator reads the names to refuse
/// one that two assemblies give. Code does not write this attribute.
/// </summary>
/// <param name="table">The generated class, whose static <c>Register</c> method adds the assembly's actions.</param>
/// <param name="names">The names of the assembly's actions.</param>
[AttributeUsage(AttributeTargets.Assembly)]
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public sealed class ActionTableAttribute(Type table, string[] names) : Attribute
{
    /// <summary>
    /// Gets the generated class.
    /// </summary>
    public Type Table { get; } = table;

    /// <summary>
    /// Gets the names of the assembly's actions.
    /// </summary>
    public IReadOnlyList<string> Names { get; } = names;
}

/// <summary>
/// Names the class of a package that adds the package's default key bindings (design §7, "The default keys and actions"): a
/// public static class with a method <c>public static void AddDefaults(Keymap keymap)</c>. The source generator of
/// <c>XylemUI.Generators</c> reads it from the assemblies an app references and registers the methods in the app's module
/// initializer, a package after the packages it references.
/// </summary>
/// <param name="type">The class with the <c>AddDefaults</c> method.</param>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class DefaultKeyBindingsAttribute(Type type) : Attribute
{
    /// <summary>
    /// Gets the class with the <c>AddDefaults</c> method.
    /// </summary>
    public Type Type { get; } = type;
}

/// <summary>
/// Gives an action its name (design §7, "Shortcuts and actions"). The source generator of <c>XylemUI.Generators</c> reads it and
/// adds the type to the table <see cref="ActionRegistry"/> is filled from. The type's public properties that can be set are the
/// action's arguments: a keymap's row sets them by their names, and the generator writes the comparison of them that finds the
/// keys of an action with arguments (design §7, "Menu items that name actions"), for a class and for a record alike. A type
/// without the attribute is compared by its type alone.
/// </summary>
/// <param name="name">The name, as <c>editor::Save</c>: unique among the actions of an app.</param>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ActionAttribute(string name) : Attribute
{
    /// <summary>
    /// Gets the name of the action.
    /// </summary>
    public string Name { get; } = name;
}
