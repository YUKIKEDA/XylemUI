using System.Numerics;

namespace XylemUI;

// The hit regions of the frame (design §5.0, "Hit regions and DrawingContext"; §7, "Hit testing"; row R4e).
public sealed partial class DisplayList
{
    // The hit regions of the frame, made with the first one, and how many are written; the innermost clip's layer open now.
    private HitTestEntry[] _hitEntries = [];
    private int _hitCount;
    private int _clipLayer = -1;

    /// <summary>
    /// Gets the hit regions written since the last <see cref="Reset"/>, the front-most last.
    /// </summary>
    internal ReadOnlySpan<HitTestEntry> HitEntries => _hitEntries.AsSpan(0, _hitCount);

    /// <summary>
    /// Appends the hit region of a node: a rectangle, given in logical pixels, with rounded corners, in the clips and the transform
    /// open now. Its edges are put where a command's are, and it is tested with the distance the renderer draws with, so a point
    /// hits it where a shape of the same rectangle covers half a pixel or more.
    /// </summary>
    /// <param name="node">The node the region belongs to.</param>
    /// <param name="x">The left edge, in logical pixels.</param>
    /// <param name="y">The top edge.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <param name="radii">The radius of each corner; when the two on a side sum past it, all are scaled down as CSS does.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// An edge is not a number, or the width, the height, or a radius is negative or not a number.
    /// </exception>
    internal void HitRegion(NodeHandle node, float x, float y, float width, float height, in CornerRadii radii)
    {
        if (BadEdge(x, y, width, height) is { } name)
        {
            throw new ArgumentOutOfRangeException(name, "The hit region's edges are numbers and its width and height are at or above 0.");
        }

        CheckRadius(radii.TopLeft, nameof(radii));
        CheckRadius(radii.TopRight, nameof(radii));
        CheckRadius(radii.BottomRight, nameof(radii));
        CheckRadius(radii.BottomLeft, nameof(radii));
        if (_hitCount == _hitEntries.Length)
        {
            Array.Resize(ref _hitEntries, Math.Max(16, _hitEntries.Length * 2));
            GrowthCount++;
        }

        Vector4 edges = Edges(x, y, width, height);
        _hitEntries[_hitCount++] = new HitTestEntry(
            node,
            edges.X,
            edges.Y,
            edges.Z,
            edges.W,
            Scaled(Fitted(radii, width, height), Scale),
            _state,
            _clipLayer
        );
    }

    /// <summary>
    /// Returns the front-most node whose hit region holds a pixel of the window, tested at the pixel's center, where the renderer
    /// takes the pixel's coverage (see <see cref="HitTestAt"/>).
    /// </summary>
    /// <param name="x">The pixel's column, in the window's client coordinates (physical pixels), as Win32 gives it: the pixel's left
    /// edge. Dividing by the scale and multiplying again could round it apart from the pixels drawn (design §5.0, "Hit regions and
    /// DrawingContext").</param>
    /// <param name="y">The pixel's row: its top edge.</param>
    /// <returns>The node, or <see langword="default"/> when no region holds the pixel.</returns>
    /// <remarks>
    /// An integer coordinate names a pixel's top-left corner. Tested as it is, the point on the right or bottom edge of a region
    /// would hit the pixel past the region, which is not drawn; the center is where the pixel is drawn or not (PR #277's review).
    /// </remarks>
    internal NodeHandle HitTest(int x, int y) => HitTestAt(new Vector2(x + 0.5f, y + 0.5f));

    /// <summary>
    /// Returns the front-most node whose hit region holds a point: inside the region's rounded rectangle, the rectangle of its
    /// state's clips, each of its rounded clips, and those of the clips' layers around it, by the distance the renderer draws with.
    /// </summary>
    /// <param name="physicalPoint">The point, in physical pixels of the window's client area.</param>
    /// <returns>The node, or <see langword="default"/> when no region holds the point.</returns>
    internal NodeHandle HitTestAt(Vector2 physicalPoint)
    {
        ReadOnlySpan<HitTestEntry> entries = HitEntries;
        for (int i = entries.Length - 1; i >= 0; i--)
        {
            if (Holds(entries[i], physicalPoint))
            {
                return entries[i].Node;
            }
        }

        return default;
    }

    // The name of the first argument of a rectangle that is not a number or, for a size, is negative; null when all are good.
    private static string? BadEdge(float x, float y, float width, float height) =>
        !float.IsFinite(x) ? nameof(x)
        : !float.IsFinite(y) ? nameof(y)
        : !float.IsFinite(width) || width < 0 ? nameof(width)
        : !float.IsFinite(height) || height < 0 ? nameof(height)
        : null;

    private bool Holds(in HitTestEntry entry, Vector2 point)
    {
        Vector2 local = point;
        if (entry.State >= 0)
        {
            StateRecord state = _scopes!.States[entry.State];
            if (!InClips(state, point))
            {
                return false;
            }

            // A transform that flattens the plane has no inverse, and nothing inside it is seen (row R4d).
            if (!state.Transform.IsIdentity)
            {
                if (!Matrix3x2.Invert(state.Transform, out Matrix3x2 inverse))
                {
                    return false;
                }

                local = Vector2.Transform(point, inverse);
            }
        }

        if (RoundedRectDistance(local, entry.Left, entry.Top, entry.Right - entry.Left, entry.Bottom - entry.Top, entry.Radii) > 0)
        {
            return false;
        }

        // A clip's layer draws its children with its own clip alone; the clips around it are applied when it is composited.
        for (int group = entry.Group; group >= 0; group = _scopes!.Records[group].ClipLayer)
        {
            int around = _scopes!.Records[group].State;
            if (around >= 0 && !InClips(_scopes.States[around], point))
            {
                return false;
            }
        }

        return true;
    }

    // Whether a physical point is inside a state's rectangle and each of its rounded clips, each taken in its own coordinates, as the
    // renderer's BeginState covers half a pixel or more.
    private bool InClips(in StateRecord state, Vector2 point)
    {
        Vector4 rect = state.ClipRect;
        if (MathF.Max(MathF.Max(rect.X - point.X, point.X - rect.Z), MathF.Max(rect.Y - point.Y, point.Y - rect.W)) > 0)
        {
            return false;
        }

        foreach (ClipCommand clip in _scopes!.Clips.AsSpan(state.ClipStart, state.ClipCount))
        {
            if (RoundedRectDistance(Vector2.Transform(point, clip.Inverse), clip.X, clip.Y, clip.Width, clip.Height, clip.Radii) > 0)
            {
                return false;
            }
        }

        return true;
    }

    // The signed distance to a rounded rectangle whose radii were scaled as CSS's: QuadShaders' RoundedRectDistance, the formula the
    // renderer draws shapes and clips with, so that a point hits where a shape covers half a pixel or more (distance at or below 0).
    private static float RoundedRectDistance(Vector2 p, float x, float y, float width, float height, in CornerRadii radii)
    {
        Vector2 halfSize = new(width * 0.5f, height * 0.5f);
        Vector2 fromCenter = p - (new Vector2(x, y) + halfSize);
        Vector2 edge = Vector2.Abs(fromCenter) - halfSize;
        Vector2 topLeft = -fromCenter - halfSize + new Vector2(radii.TopLeft);
        Vector2 bottomRight = fromCenter - halfSize + new Vector2(radii.BottomRight);
        bool falling = (topLeft.X > 0 && topLeft.Y > 0) || (bottomRight.X > 0 && bottomRight.Y > 0);
        Vector2 first = falling ? topLeft : new Vector2(fromCenter.X, -fromCenter.Y) - halfSize + new Vector2(radii.TopRight);
        Vector2 second = falling ? bottomRight : new Vector2(-fromCenter.X, fromCenter.Y) - halfSize + new Vector2(radii.BottomLeft);
        float d = CornerArc(MathF.Max(edge.X, edge.Y), first, falling ? radii.TopLeft : radii.TopRight);
        return CornerArc(d, second, falling ? radii.BottomRight : radii.BottomLeft);
    }

    private static float CornerArc(float d, Vector2 q, float radius) => q.X > 0 && q.Y > 0 && radius > 0 ? MathF.Max(d, q.Length() - radius) : d;
}
