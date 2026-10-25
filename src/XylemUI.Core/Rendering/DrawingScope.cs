namespace XylemUI;

/// <summary>
/// A scope that a <see cref="DrawingContext"/> push started (design §5.0, "The API of layers, clips, transforms, and DrawingContext"):
/// <see cref="Dispose"/> ends it, so that <c>using</c> pops what was pushed. It allocates nothing, as Avalonia's <c>PushedState</c>.
/// </summary>
public readonly ref struct DrawingScope
{
    private readonly DisplayList? _list;

    internal DrawingScope(DisplayList list) => _list = list;

    /// <summary>
    /// Ends the scope: pops the group, the clip, or the transform it started.
    /// </summary>
    /// <exception cref="InvalidOperationException">The scope was not returned by a push of a <see cref="DrawingContext"/>.</exception>
    public void Dispose()
    {
        if (_list is null)
        {
            throw new InvalidOperationException("The scope was not returned by a push of a DrawingContext.");
        }

        _list.Pop();
    }
}
