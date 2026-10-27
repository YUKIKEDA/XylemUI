namespace XylemUI;

/// <summary>
/// A node of a window's tree, as the component API hands it around (design §2.13, "The component API"): the tree and the node.
/// The functions of <see cref="Elements"/> make one; the methods of <see cref="ElementExtensions"/> set its style and return it.
/// <see langword="default"/> is no node.
/// </summary>
public readonly struct Element : IEquatable<Element>
{
    internal Element(UiTree tree, NodeHandle node)
    {
        Tree = tree;
        Node = node;
    }

    /// <summary>
    /// Gets a value indicating whether this is no node.
    /// </summary>
    public bool IsNone => Tree is null;

    /// <summary>
    /// Gets the tree the node is in; <see langword="null"/> for no node.
    /// </summary>
    internal UiTree? Tree { get; }

    /// <summary>
    /// Gets the node.
    /// </summary>
    internal NodeHandle Node { get; }

    /// <summary>
    /// Returns whether two elements are the same node of the same tree.
    /// </summary>
    /// <param name="left">One element.</param>
    /// <param name="right">The other.</param>
    /// <returns>Whether they are the same node.</returns>
    public static bool operator ==(Element left, Element right) => left.Equals(right);

    /// <summary>
    /// Returns whether two elements are not the same node of the same tree.
    /// </summary>
    /// <param name="left">One element.</param>
    /// <param name="right">The other.</param>
    /// <returns>Whether they are different nodes.</returns>
    public static bool operator !=(Element left, Element right) => !left.Equals(right);

    /// <inheritdoc/>
    public bool Equals(Element other) => ReferenceEquals(Tree, other.Tree) && Node == other.Node;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Element other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Tree, Node);

    /// <summary>
    /// Returns the tree, or throws when this is no node.
    /// </summary>
    /// <exception cref="InvalidOperationException">This is no node (<see langword="default"/>).</exception>
    internal UiTree TreeOrThrow() => Tree ?? throw new InvalidOperationException("The element is no node (default).");
}
