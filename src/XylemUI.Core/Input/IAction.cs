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
/// Gives an action its name (design §7, "Shortcuts and actions"). The source generator of <c>XylemUI.Generators</c> reads it and
/// adds the type to the table <see cref="ActionRegistry"/> is filled from.
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
