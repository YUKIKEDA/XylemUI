using static XylemUI.Elements;

namespace XylemUI.Primitives;

/// <summary>
/// Makes the headless primitives (design §13.1): parts with behavior and state but no look, which the app or a theme styles with
/// the method chain. Write <c>using static XylemUI.Primitives.Primitive;</c> to call them by name.
/// </summary>
public static class Primitive
{
    /// <summary>
    /// Makes a button: a box that holds the content, takes clicks, and has the pressed pseudo-state while the left button is down on
    /// it (design §2.13). It sets no background and no color of text; the keyboard's Enter and Space and the focus come with row K1.
    /// </summary>
    /// <param name="content">The content: a node of the current tree with no parent.</param>
    /// <param name="onClick">What a click runs, in the scope current now (<see cref="ElementExtensions.OnClick"/>).</param>
    /// <returns>The button.</returns>
    /// <exception cref="InvalidOperationException">No window's root function is running, or there is no current scope.</exception>
    /// <exception cref="ArgumentException">The content is no node, is of another tree, or has a parent.</exception>
    public static Element Button(Element content, Action onClick)
    {
        ArgumentNullException.ThrowIfNull(onClick);
        return Box(content).OnClick(onClick);
    }

    /// <summary>
    /// Makes a button whose content is a text (<see cref="Elements.Text(string)"/>).
    /// </summary>
    /// <param name="label">The text.</param>
    /// <param name="onClick">What a click runs, in the scope current now.</param>
    /// <returns>The button.</returns>
    /// <exception cref="InvalidOperationException">No window's root function is running, or there is no current scope.</exception>
    public static Element Button(string label, Action onClick)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(onClick);
        return Button(Elements.Text(label), onClick);
    }
}
