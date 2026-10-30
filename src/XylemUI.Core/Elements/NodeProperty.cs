using System.Numerics;

namespace XylemUI;

/// <summary>
/// A property of a node that a signal can drive (design §2.4, the binding layer): its name and how a value is written to the node.
/// <see cref="ElementExtensions.Bind{T}"/> is the one path from a signal to a node; the overloads that take a signal call it.
/// </summary>
/// <typeparam name="T">The value's type.</typeparam>
public sealed class NodeProperty<T>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NodeProperty{T}"/> class.
    /// </summary>
    /// <param name="name">The property's name.</param>
    /// <param name="setter">Writes a value to a node of a tree.</param>
    internal NodeProperty(string name, Action<UiTree, NodeHandle, T> setter)
    {
        Name = name;
        Setter = setter;
    }

    /// <summary>
    /// Gets the property's name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets what writes a value to a node: the tree's setter, which changes nothing for an equal value and marks the stage the change
    /// needs otherwise (design §2.4).
    /// </summary>
    internal Action<UiTree, NodeHandle, T> Setter { get; }
}

/// <summary>
/// The properties of a node that a signal can drive (design §2.4).
/// </summary>
public static class NodeProperties
{
    /// <summary>
    /// Gets the background (<see cref="ElementExtensions.Background(Element, Paint)"/>).
    /// </summary>
    public static NodeProperty<Paint> Background { get; } = new(nameof(Background), static (tree, node, value) => tree.SetBackground(node, value));

    /// <summary>
    /// Gets the paint of the border (<see cref="ElementExtensions.Border"/>).
    /// </summary>
    public static NodeProperty<Paint> BorderPaint { get; } = new(nameof(BorderPaint), static (tree, node, value) => tree.SetBorderPaint(node, value));

    /// <summary>
    /// Gets the color of text (<see cref="ElementExtensions.Foreground(Element, Color)"/>): a text node's own, or the one another node
    /// gives the text nodes below it.
    /// </summary>
    public static NodeProperty<Color> Foreground { get; } =
        new(nameof(Foreground), static (tree, node, value) => ElementExtensions.WriteForeground(tree, node, value));

    /// <summary>
    /// Gets whether the node itself is disabled (<see cref="ElementExtensions.Disabled(Element, bool)"/>), which its descendants
    /// take on.
    /// </summary>
    public static NodeProperty<bool> Disabled { get; } = new(nameof(Disabled), static (tree, node, value) => tree.SetDisabled(node, value));

    /// <summary>
    /// Gets the opacity of the node and its subtree (<see cref="ElementExtensions.Opacity"/>).
    /// </summary>
    public static NodeProperty<float> Opacity { get; } = new(nameof(Opacity), static (tree, node, value) => tree.SetOpacity(node, value));

    /// <summary>
    /// Gets the transform of the node and its subtree (<see cref="ElementExtensions.Transform"/>).
    /// </summary>
    public static NodeProperty<Matrix3x2> Transform { get; } = new(nameof(Transform), static (tree, node, value) => tree.SetTransform(node, value));

    /// <summary>
    /// Gets the radii of the box's corners (<see cref="ElementExtensions.CornerRadius"/>).
    /// </summary>
    public static NodeProperty<CornerRadii> CornerRadius { get; } =
        new(nameof(CornerRadius), static (tree, node, value) => tree.SetCornerRadii(node, value));

    /// <summary>
    /// Gets the shadow under the box (<see cref="ElementExtensions.Shadow"/>); <see langword="null"/> for none.
    /// </summary>
    public static NodeProperty<BoxShadow?> Shadow { get; } = new(nameof(Shadow), static (tree, node, value) => tree.SetShadow(node, value));

    /// <summary>
    /// Gets the text of a text node (<see cref="Elements.Text(string)"/>).
    /// </summary>
    public static NodeProperty<string> Text { get; } = new(nameof(Text), static (tree, node, value) => tree.SetText(node, value));
}
