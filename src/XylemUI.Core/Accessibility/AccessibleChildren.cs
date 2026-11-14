namespace XylemUI;

/// <summary>
/// Adds the nodes of the accessibility tree that a node drawn by a draw function holds: the points and bars of a chart (design
/// §4.4.1, "The nodes of a draw function"). It is called while assistive technology reads the window, when the node's own node is made
/// again; a signal it reads makes the node again when it changes.
/// </summary>
/// <param name="children">What takes the nodes.</param>
/// <param name="bounds">The node's box, in its own coordinates.</param>
public delegate void AccessibleChildrenFunc(AccessibleChildren children, Rect bounds);

/// <summary>
/// What takes the nodes of the accessibility tree that a draw function adds (<see cref="ElementExtensions.AccessibleChildren"/>). The
/// nodes take no action of assistive technology: a node that moves among them with keys holds the focus itself and points at the
/// one that is active by its key (<c>ActiveDescendant</c> with a signal of a key).
/// </summary>
public sealed class AccessibleChildren
{
    private readonly List<Entry> _entries = [];
    private readonly List<int> _open = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="AccessibleChildren"/> class, which the accessibility tree hands a draw function.
    /// </summary>
    internal AccessibleChildren() { }

    /// <summary>
    /// Gets the number of nodes added.
    /// </summary>
    internal int Count => _entries.Count;

    /// <summary>
    /// Returns a node added, in the order added: a node before the nodes it holds.
    /// </summary>
    /// <param name="index">The index.</param>
    /// <returns>The node.</returns>
    internal Entry this[int index] => _entries[index];

    /// <summary>
    /// Adds a node with no children of its own, such as a point: under the node <see cref="Begin"/> opened last, if one is open.
    /// </summary>
    /// <param name="key">The key that keeps the node the same node when it is made again; unique in the node that draws.</param>
    /// <param name="bounds">The node's box, in the coordinates of the node that draws.</param>
    /// <param name="info">What the node is to assistive technology.</param>
    public void Add(int key, Rect bounds, AccessibilityInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        _entries.Add(new Entry(key, bounds, info, OpenParent()));
    }

    /// <summary>
    /// Adds a node that holds the nodes added until <see cref="End"/>, such as a series.
    /// </summary>
    /// <param name="key">The key that keeps the node the same node when it is made again; unique in the node that draws.</param>
    /// <param name="bounds">The node's box, in the coordinates of the node that draws.</param>
    /// <param name="info">What the node is to assistive technology.</param>
    public void Begin(int key, Rect bounds, AccessibilityInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        int parent = OpenParent();
        _open.Add(_entries.Count);
        _entries.Add(new Entry(key, bounds, info, parent));
    }

    /// <summary>
    /// Closes the node <see cref="Begin"/> opened last.
    /// </summary>
    /// <exception cref="InvalidOperationException">No node is open.</exception>
    public void End()
    {
        if (_open.Count == 0)
        {
            throw new InvalidOperationException("End closes the node that Begin opened, and none is open.");
        }

        _open.RemoveAt(_open.Count - 1);
    }

    /// <summary>
    /// Forgets the nodes added, before the draw function is called again.
    /// </summary>
    internal void Clear()
    {
        _entries.Clear();
        _open.Clear();
    }

    // The node that holds the next one: the one Begin opened last, or -1 for the node that draws.
    private int OpenParent() => _open.Count == 0 ? -1 : _open[^1];

    /// <summary>
    /// A node a draw function added.
    /// </summary>
    /// <param name="Key">The node's key.</param>
    /// <param name="Bounds">The node's box, in the coordinates of the node that draws.</param>
    /// <param name="Info">What the node is to assistive technology.</param>
    /// <param name="Parent">The index of the node that holds it, or -1 for the node that draws.</param>
    internal readonly record struct Entry(int Key, Rect Bounds, AccessibilityInfo Info, int Parent);
}
