namespace XylemUI;

/// <summary>
/// Reads signals for the accessibility tree and marks a node when one of them changes (design §4.4.1, "Relations" and "The nodes of a
/// draw function"): the active descendant's signal, and the signals a draw function of nodes reads. It runs only in the accessibility
/// stage, so it reads nothing, and no signal knows of it, while no assistive technology reads the window.
/// </summary>
/// <param name="tree">The tree of the node.</param>
/// <param name="node">The node it marks.</param>
/// <param name="body">What it runs, whose reads it records.</param>
internal sealed class AccessibilityWatch(UiTree tree, NodeHandle node, Action body) : Computation
{
    /// <inheritdoc />
    private protected override bool IsLive => !IsDisposed;

    /// <summary>
    /// Runs the body and records the signals it reads, in place of those it read before.
    /// </summary>
    internal void Track() => Run();

    /// <summary>
    /// Stops it: it no longer reads its signals, and a change of them marks nothing.
    /// </summary>
    internal void Stop() => DisposeSources();

    /// <inheritdoc />
    internal override void MarkDirty()
    {
        if (!IsDisposed)
        {
            tree.MarkAccessibilitySelf(node);
        }
    }

    /// <inheritdoc />
    private protected override void Execute() => body();

    /// <inheritdoc />
    private protected override void OnException(Exception exception) => ReactiveRuntime.ReportException(exception);
}
