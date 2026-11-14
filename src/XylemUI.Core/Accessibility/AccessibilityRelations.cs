namespace XylemUI;

/// <summary>
/// The relations a node holds to other nodes (design §4.4.1, "Relations"): kept for the nodes that have one only, apart from
/// <see cref="UiNode"/>, until the node is removed. A node that a relation names is kept as its handle, so that a node made later
/// or one that leaves the tree and comes back is found again.
/// </summary>
internal sealed class AccessibilityRelations
{
    /// <summary>
    /// Gets the nodes that name the node, in the order given; <see langword="null"/> for none.
    /// </summary>
    public List<NodeHandle>? LabelledBy { get; private set; }

    /// <summary>
    /// Gets the nodes that describe the node, in the order given; <see langword="null"/> for none.
    /// </summary>
    public List<NodeHandle>? DescribedBy { get; private set; }

    /// <summary>
    /// Gets the nodes the node controls, in the order given; <see langword="null"/> for none.
    /// </summary>
    public List<NodeHandle>? Controls { get; private set; }

    /// <summary>
    /// Gets or sets the node that holds the node's error message; <c>default</c> for none.
    /// </summary>
    public NodeHandle ErrorMessage { get; set; }

    /// <summary>
    /// Gets or sets the signal of the node's active descendant, an element; <see langword="null"/> for none.
    /// </summary>
    public IReadOnlySignal<Element>? ActiveElement { get; set; }

    /// <summary>
    /// Gets or sets the signal of the key of the node's active descendant among the nodes its draw function adds;
    /// <see langword="null"/> for none.
    /// </summary>
    public IReadOnlySignal<int?>? ActiveKey { get; set; }

    /// <summary>
    /// Gets or sets the active descendant the accessibility tree's watch read last: an element's node; <c>default</c> for none.
    /// </summary>
    public NodeHandle ReadElement { get; set; }

    /// <summary>
    /// Gets or sets the active descendant the accessibility tree's watch read last: a key; <see langword="null"/> for none.
    /// </summary>
    public int? ReadKey { get; set; }

    /// <summary>
    /// Gets or sets the active descendant the accessibility tree keeps in its reverse table for this node; <c>default</c> for none.
    /// </summary>
    public NodeHandle NotedActive { get; set; }

    /// <summary>
    /// Returns whether a relation of the node names another node now: the error message, a node of a relation that holds several, or
    /// the active descendant the tree keeps.
    /// </summary>
    /// <param name="target">The node.</param>
    /// <returns>Whether one names it.</returns>
    public bool Names(NodeHandle target) =>
        ErrorMessage == target
        || NotedActive == target
        || (LabelledBy?.Contains(target) ?? false)
        || (DescribedBy?.Contains(target) ?? false)
        || (Controls?.Contains(target) ?? false);

    /// <summary>
    /// Adds a node to a relation that holds several, after those it holds.
    /// </summary>
    /// <param name="kind">The relation.</param>
    /// <param name="target">The node.</param>
    public void Add(AccessibilityRelation kind, NodeHandle target)
    {
        switch (kind)
        {
            case AccessibilityRelation.LabelledBy:
                (LabelledBy ??= []).Add(target);
                break;
            case AccessibilityRelation.DescribedBy:
                (DescribedBy ??= []).Add(target);
                break;
            default:
                (Controls ??= []).Add(target);
                break;
        }
    }
}
