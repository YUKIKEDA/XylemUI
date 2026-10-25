using System.Numerics;
using System.Runtime.CompilerServices;
using XylemUI.Text;

namespace XylemUI;

// The clips and transforms of a display list (row R4d, design §5.0, "The API of layers, clips, transforms, and DrawingContext").
public sealed partial class DisplayList
{
    /// <summary>
    /// Starts a clip: the commands written until the matching <see cref="Pop"/> draw only inside the rectangle, given in logical
    /// pixels, with its corners rounded by <paramref name="radii"/> (fitted as CSS fits them). The clip is in the coordinates of the
    /// transform pushed before it, and takes the clips around it together with it. A rectangle with no radius whose axes are the
    /// screen's clips on its edges, which lie on pixels as a rectangle's own do (design §6.1.1, "Pixel snapping"); a rounded clip,
    /// or one that a transform turns, takes each pixel's coverage by the distance to its edge, with an antialiased edge. Past
    /// <see cref="MaxRoundedClips"/> rounded clips, the commands are drawn into a layer that is composited with the clips around it.
    /// A scope with no command is left out.
    /// </summary>
    /// <param name="x">The left edge, in logical pixels.</param>
    /// <param name="y">The top edge.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <param name="radii">The radius of each corner.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// An edge is not a number, or the width, the height, or a radius is negative or not a number.
    /// </exception>
    public void PushClip(float x, float y, float width, float height, in CornerRadii radii)
    {
        bool numbers = float.IsFinite(x) && float.IsFinite(y) && float.IsFinite(width) && float.IsFinite(height);
        if (!numbers || width < 0 || height < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "The clip's edges are numbers and its width and height are at or above 0.");
        }

        CheckRadius(radii.TopLeft, nameof(radii));
        CheckRadius(radii.TopRight, nameof(radii));
        CheckRadius(radii.BottomRight, nameof(radii));
        CheckRadius(radii.BottomLeft, nameof(radii));

        // The rectangle in the commands' coordinates: physical pixels, or before the renderer's transform when there is one.
        float scale = Scale;
        Vector4 edges = Edges(x, y, width, height);
        CornerRadii fitted = Scaled(Fitted(radii, width, height), scale);
        bool rounded = fitted.TopLeft > 0 || fitted.TopRight > 0 || fitted.BottomRight > 0 || fitted.BottomLeft > 0;
        Matrix3x2 physical = Physical;
        bool straight = !_transformed || (physical.M12 == 0 && physical.M21 == 0);

        // The clips of the new state: the rectangle the straight ones make, and the others, with those that contain one another taken
        // together (Skia's ClipStack, ClipStack.cpp lines 654 to 710).
        Vector4 rect = _clipRect;
        Span<ClipCommand> clips = stackalloc ClipCommand[MaxRoundedClips + 1];
        int count = CopyClips(clips);
        if (!rounded && straight)
        {
            rect = Intersection(rect, _transformed ? Bounds(physical, edges) : edges);
            count = DropContaining(clips[..count], rect);
        }
        else
        {
            Matrix3x2.Invert(physical, out Matrix3x2 inverse);
            var clip = new ClipCommand(inverse, edges.X, edges.Y, edges.Z - edges.X, edges.W - edges.Y, fitted, Spans(physical), _opacityDepth);
            count = Merged(clips, count, clip, rect);
        }

        // Past MaxRoundedClips, a layer: its children are drawn with the new clip alone, and the layer with the clips around it.
        bool layered = count > MaxRoundedClips;
        if (layered)
        {
            PushGroup(1, clip: true);
            clips[0] = clips[count - 1] with { Groups = _opacityDepth };
            count = 1;
        }

        ScopeStreams scopes = _scopes ??= new ScopeStreams();
        ref StateFrame frame = ref PushStateFrame(scopes);
        frame.Layered = layered;
        _clipRect = rect;
        WriteState(ref frame, scopes, physical, clips[..count]);
        UpdateMode();
    }

    /// <summary>
    /// Starts a transform: the positions of the commands written until the matching <see cref="Pop"/>, in logical pixels, are
    /// transformed by <paramref name="transform"/> and then by the transforms pushed before it. A transform that only moves by
    /// whole physical pixels keeps the edges of rectangles on pixels and ClearType text; any other draws its text in grayscale, at
    /// the larger of its scales (design §5.0.3, "ClearType"). The identity records nothing.
    /// </summary>
    /// <param name="transform">The transform, an affine one of System.Numerics.</param>
    /// <exception cref="ArgumentOutOfRangeException">An element of the matrix is not a number.</exception>
    public void PushTransform(in Matrix3x2 transform)
    {
        bool numbers =
            float.IsFinite(transform.M11)
            && float.IsFinite(transform.M12)
            && float.IsFinite(transform.M21)
            && float.IsFinite(transform.M22)
            && float.IsFinite(transform.M31)
            && float.IsFinite(transform.M32);
        if (!numbers)
        {
            throw new ArgumentOutOfRangeException(nameof(transform), "The transform's elements are numbers.");
        }

        ScopeStreams scopes = _scopes ??= new ScopeStreams();
        ref StateFrame frame = ref PushStateFrame(scopes);
        if (transform.IsIdentity)
        {
            return;
        }

        // The transform pushed last is the first to move a point (System.Numerics multiplies a row vector on the left).
        Matrix3x2 total = transform * _transform;
        _transform = total;
        bool wasTransformed = _transformed;
        if (total.M11 == 1 && total.M12 == 0 && total.M21 == 0 && total.M22 == 1)
        {
            // Only a move: the commands are moved as they are written, on pixels when the move is by whole pixels.
            _shift = new Vector2(total.M31 * Scale, total.M32 * Scale);
            _snap = _shift.X == MathF.Round(_shift.X) && _shift.Y == MathF.Round(_shift.Y);
            _transformed = false;
            _textScale = 1;
        }
        else
        {
            _shift = default;
            _snap = false;
            _transformed = true;

            // The text is drawn at the larger of the scales along the two axes (Flutter's text_contents.cc lines 216 to 235).
            float scaleX = MathF.Sqrt((total.M11 * total.M11) + (total.M12 * total.M12));
            float scaleY = MathF.Sqrt((total.M21 * total.M21) + (total.M22 * total.M22));
            _textScale = MathF.Max(scaleX, scaleY);
        }

        // A move inside no transform changes nothing the renderer applies; any other change starts a state.
        if (_transformed || wasTransformed)
        {
            Span<ClipCommand> clips = stackalloc ClipCommand[MaxRoundedClips];
            int count = CopyClips(clips);
            WriteState(ref frame, scopes, Physical, clips[..count]);
        }

        UpdateMode();
    }

    internal ReadOnlySpan<StateRecord> States => _scopes is null ? default : _scopes.States.AsSpan(0, _stateRecords);

    internal ReadOnlySpan<ClipCommand> Clips => _scopes is null ? default : _scopes.Clips.AsSpan(0, _clipRecords);

    // The renderer's transform of the commands' positions to physical pixels: the logical transform with its move in physical
    // pixels (the positions are the logical ones times the scale), or the identity when it only moves and the commands are moved.
    private Matrix3x2 Physical =>
        _transformed
            ? new Matrix3x2(_transform.M11, _transform.M12, _transform.M21, _transform.M22, _transform.M31 * Scale, _transform.M32 * Scale)
            : Matrix3x2.Identity;

    private void PopState()
    {
        ScopeStreams scopes = _scopes!;
        _scopeDepth--;
        StateFrame frame = scopes.StateFrames[--_stateDepth];
        _transform = frame.Transform;
        _shift = frame.Shift;
        _snap = frame.Snap;
        _transformed = frame.Transformed;
        _textScale = frame.TextScale;
        _state = frame.State;
        _clipRect = frame.ClipRect;
        UpdateMode();
        if (frame.Recorded)
        {
            if (CommandCount - 1 == frame.Command)
            {
                // Nothing was drawn in the scope: it leaves the list.
                CommandCount--;
                _usedColors--;
                _stateRecords = frame.Record;
                _clipRecords = frame.ClipStart;
            }
            else
            {
                AppendPop();
            }
        }

        if (frame.Layered)
        {
            PopGroup();
        }
    }

    private ref StateFrame PushStateFrame(ScopeStreams scopes)
    {
        PushKind(scopes, ScopeKind.State);
        if (_stateDepth == scopes.StateFrames.Length)
        {
            Array.Resize(ref scopes.StateFrames, scopes.StateFrames.Length * 2);
            GrowthCount++;
        }

        ref StateFrame frame = ref scopes.StateFrames[_stateDepth++];
        frame = new StateFrame
        {
            Transform = _transform,
            Shift = _shift,
            Snap = _snap,
            Transformed = _transformed,
            TextScale = _textScale,
            State = _state,
            ClipRect = _clipRect,
        };
        return ref frame;
    }

    private void PushKind(ScopeStreams scopes, ScopeKind kind)
    {
        if (_scopeDepth == scopes.Kinds.Length)
        {
            Array.Resize(ref scopes.Kinds, scopes.Kinds.Length * 2);
            GrowthCount++;
        }

        scopes.Kinds[_scopeDepth++] = kind;
    }

    // A state: the transform, the rectangle, and the clips the commands after it are drawn with, and a PushState command for it.
    private void WriteState(ref StateFrame frame, ScopeStreams scopes, in Matrix3x2 physical, ReadOnlySpan<ClipCommand> clips)
    {
        frame.Recorded = true;
        frame.Command = CommandCount;
        frame.Record = _stateRecords;
        frame.ClipStart = _clipRecords;
        _state = AppendState(scopes, physical, _textScale, clips);
    }

    private int AppendState(ScopeStreams scopes, in Matrix3x2 physical, float textScale, ReadOnlySpan<ClipCommand> clips)
    {
        if (CommandCount == _commands.Length)
        {
            Grow();
        }

        if (_stateRecords == scopes.States.Length)
        {
            Array.Resize(ref scopes.States, scopes.States.Length * 2);
            GrowthCount++;
        }

        while (_clipRecords + clips.Length > scopes.Clips.Length)
        {
            Array.Resize(ref scopes.Clips, scopes.Clips.Length * 2);
            GrowthCount++;
        }

        Vector4 bounds = _clipRect;
        foreach (ClipCommand clip in clips)
        {
            Matrix3x2.Invert(clip.Inverse, out Matrix3x2 forward);
            bounds = Intersection(bounds, Bounds(forward, new Vector4(clip.X, clip.Y, clip.X + clip.Width, clip.Y + clip.Height)));
        }

        clips.CopyTo(scopes.Clips.AsSpan(_clipRecords));
        scopes.States[_stateRecords] = new StateRecord
        {
            Transform = physical,
            TextScale = textScale,
            ClipRect = _clipRect,
            Bounds = bounds,
            ClipStart = _clipRecords,
            ClipCount = clips.Length,
        };
        _clipRecords += clips.Length;
        _commands[CommandCount++] = CommandTag.PushState;
        _colors[_usedColors++] = 0;
        return _stateRecords++;
    }

    private void AppendPop()
    {
        if (CommandCount == _commands.Length)
        {
            Grow();
        }

        _commands[CommandCount++] = CommandTag.Pop;
        _colors[_usedColors++] = 0;
    }

    // The clips the current state applies per pixel, copied into `clips`; their count.
    private int CopyClips(Span<ClipCommand> clips)
    {
        if (_state < 0)
        {
            return 0;
        }

        StateRecord state = _scopes!.States[_state];
        _scopes.Clips.AsSpan(state.ClipStart, state.ClipCount).CopyTo(clips);
        return state.ClipCount;
    }

    // The clips with `clip` added, less those that contain another: a clip that contains `clip`, or the rectangle, is dropped, and
    // `clip` is not added when one of them contains it or it contains the rectangle. Only clips in the same coordinates are
    // compared, and only by their rectangles' corners, so two that contain one another may both be kept, never the wrong one.
    private static int Merged(Span<ClipCommand> clips, int count, in ClipCommand clip, Vector4 rect)
    {
        if (clip.Inverse.IsIdentity && float.IsFinite(rect.X) && float.IsFinite(rect.Z) && Contains(clip, rect))
        {
            return count;
        }

        foreach (ClipCommand other in clips[..count])
        {
            if (other.Inverse == clip.Inverse && Contains(clip, RectOf(other)))
            {
                return count;
            }
        }

        int kept = 0;
        foreach (ClipCommand other in clips[..count])
        {
            if (other.Inverse != clip.Inverse || !Contains(other, RectOf(clip)))
            {
                clips[kept++] = other;
            }
        }

        clips[kept] = clip;
        return kept + 1;
    }

    // A clip's rectangle as left, top, right, and bottom.
    private static Vector4 RectOf(in ClipCommand clip) => new(clip.X, clip.Y, clip.X + clip.Width, clip.Y + clip.Height);

    // The clips less those in physical pixels that contain the rectangle; their count.
    private static int DropContaining(Span<ClipCommand> clips, Vector4 rect)
    {
        int kept = 0;
        foreach (ClipCommand clip in clips)
        {
            if (!(clip.Inverse.IsIdentity && float.IsFinite(rect.X) && float.IsFinite(rect.Z) && Contains(clip, rect)))
            {
                clips[kept++] = clip;
            }
        }

        return kept;
    }

    // Whether the clip contains the rectangle (left, top, right, bottom) in its own coordinates: its four corners lie inside, which
    // is enough as both are convex.
    private static bool Contains(in ClipCommand clip, Vector4 rect) =>
        Holds(clip, rect.X, rect.Y) && Holds(clip, rect.Z, rect.Y) && Holds(clip, rect.Z, rect.W) && Holds(clip, rect.X, rect.W);

    private static bool Holds(in ClipCommand clip, float x, float y)
    {
        float right = clip.X + clip.Width;
        float bottom = clip.Y + clip.Height;
        if (x < clip.X || x > right || y < clip.Y || y > bottom)
        {
            return false;
        }

        CornerRadii r = clip.Radii;
        return InsideCorner(x - (clip.X + r.TopLeft), y - (clip.Y + r.TopLeft), r.TopLeft, -1, -1)
            && InsideCorner(x - (right - r.TopRight), y - (clip.Y + r.TopRight), r.TopRight, 1, -1)
            && InsideCorner(x - (right - r.BottomRight), y - (bottom - r.BottomRight), r.BottomRight, 1, 1)
            && InsideCorner(x - (clip.X + r.BottomLeft), y - (bottom - r.BottomLeft), r.BottomLeft, -1, 1);
    }

    // Whether a point, from the center of a corner's arc, is inside the arc: only a point past the center toward the corner can be
    // outside it.
    private static bool InsideCorner(float dx, float dy, float radius, float signX, float signY) =>
        radius <= 0 || dx * signX <= 0 || dy * signY <= 0 || (dx * dx) + (dy * dy) <= radius * radius;

    private static Vector4 Intersection(Vector4 a, Vector4 b) =>
        new(MathF.Max(a.X, b.X), MathF.Max(a.Y, b.Y), MathF.Min(a.Z, b.Z), MathF.Min(a.W, b.W));

    // The bounds (left, top, right, bottom) of a rectangle given the same way, transformed.
    private static Vector4 Bounds(in Matrix3x2 transform, Vector4 rect)
    {
        Vector2 a = Vector2.Transform(new Vector2(rect.X, rect.Y), transform);
        Vector2 b = Vector2.Transform(new Vector2(rect.Z, rect.Y), transform);
        Vector2 c = Vector2.Transform(new Vector2(rect.Z, rect.W), transform);
        Vector2 d = Vector2.Transform(new Vector2(rect.X, rect.W), transform);
        return new Vector4(
            MathF.Min(MathF.Min(a.X, b.X), MathF.Min(c.X, d.X)),
            MathF.Min(MathF.Min(a.Y, b.Y), MathF.Min(c.Y, d.Y)),
            MathF.Max(MathF.Max(a.X, b.X), MathF.Max(c.X, d.X)),
            MathF.Max(MathF.Max(a.Y, b.Y), MathF.Max(c.Y, d.Y))
        );
    }

    // The physical pixels a unit of a transform's coordinates spans: the square root of its area's scale.
    private static float Spans(in Matrix3x2 transform) => MathF.Sqrt(MathF.Abs(transform.GetDeterminant()));

    // A rectangle given in logical pixels, in the commands' coordinates (left, top, right, bottom): moved by the scopes' move, and with
    // each edge on the nearest pixel while the scopes keep the edges on pixels.
    private Vector4 Edges(float x, float y, float width, float height)
    {
        float scale = Scale;
        return _snap
            ? new Vector4(
                RoundToPixel(x * scale) + _shift.X,
                RoundToPixel(y * scale) + _shift.Y,
                RoundToPixel((x + width) * scale) + _shift.X,
                RoundToPixel((y + height) * scale) + _shift.Y
            )
            : new Vector4((x * scale) + _shift.X, (y * scale) + _shift.Y, ((x + width) * scale) + _shift.X, ((y + height) * scale) + _shift.Y);
    }

    private void UpdateMode() =>
        _mode =
            (_opacityDepth > 0 ? InGroup : 0) | (_shift != default ? Shifted : 0) | (_transformed ? Transformed : 0) | (_state >= 0 ? Clipped : 0);

    // The rectangle FillRect just wrote, inside a group, a clip, or a transform: moved by the scopes' move, on pixels while they keep
    // edges there, and added to its group's bounds. In groups alone it stays as written, as R4c wrote it.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void RewriteLastRect(float x, float y, float width, float height)
    {
        if (_mode == InGroup)
        {
            // One call a rectangle, as R4c's AccumulateLastRect: through the general path, a rectangle in a group took 21 ns to
            // write against 15 ns (fill-rect --grouped, row R4d).
            ReadOnlySpan<float> written = _rects.AsSpan(_usedRects - FloatsPerRect, FloatsPerRect);
            Accumulate(written[0], written[1], written[0] + written[2], written[1] + written[3]);
            return;
        }

        if ((_mode & (Shifted | Transformed)) != 0)
        {
            Vector4 edges = Edges(x, y, width, height);
            Span<float> rect = _rects.AsSpan(_usedRects - FloatsPerRect, FloatsPerRect);
            rect[0] = edges.X;
            rect[1] = edges.Y;
            rect[2] = edges.Z - edges.X;
            rect[3] = edges.W - edges.Y;
        }

        if (_opacityDepth > 0)
        {
            AccumulateLastRect();
        }
    }

    // A Shape or Shadow command inside a scope: AppendShape with the rectangle of Edges.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private ref ShapeRecord AppendShapeInScope(CommandTag tag, float x, float y, float width, float height, Color color)
    {
        if (_mode == InGroup)
        {
            return ref AppendShape(tag, x, y, width, height, color);
        }

        if (CommandCount == _commands.Length)
        {
            Grow();
        }

        Vector4 edges = Edges(x, y, width, height);
        _commands[CommandCount++] = tag;
        Span<float> rect = _rects.AsSpan(_usedRects, FloatsPerRect);
        rect[0] = edges.X;
        rect[1] = edges.Y;
        rect[2] = edges.Z - edges.X;
        rect[3] = edges.W - edges.Y;
        _usedRects += FloatsPerRect;
        _colors[_usedColors++] = color.Packed;
        ShapeStreams streams = _shapeStreams ??= new ShapeStreams();
        if (_usedShapes == streams.Records.Length)
        {
            Array.Resize(ref streams.Records, streams.Records.Length * 2);
            GrowthCount++;
        }

        return ref streams.Records[_usedShapes++];
    }

    // A run of glyphs given in physical pixels, inside a scope: moved by the nearest whole pixels to the scopes' move; inside a
    // transform the renderer applies, in a state of its own whose text scale is 1, as the glyphs were drawn at the run's size.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void GlyphRunInScope(ReadOnlySpan<GlyphInstance> glyphs, Color color, GlyphCoverage coverage)
    {
        if (coverage == GlyphCoverage.Rgb && TextIsGray)
        {
            coverage = GlyphCoverage.Gray;
        }

        Span<GlyphInstance> destination = ReserveGlyphs(glyphs.Length);
        glyphs.CopyTo(destination);
        int dx = (int)MathF.Round(_shift.X);
        int dy = (int)MathF.Round(_shift.Y);
        if (dx != 0 || dy != 0)
        {
            foreach (ref GlyphInstance glyph in destination)
            {
                glyph = glyph with { X = glyph.X + dx, Y = glyph.Y + dy };
            }
        }

        if (!_transformed || _textScale == 1)
        {
            EndGlyphRunInScope(glyphs.Length, color, coverage, -1);
            return;
        }

        int state = _state;
        float textScale = _textScale;
        Span<ClipCommand> clips = stackalloc ClipCommand[MaxRoundedClips];
        int count = CopyClips(clips);
        _textScale = 1;
        _state = AppendState(_scopes!, Physical, 1, clips[..count]);
        EndGlyphRunInScope(glyphs.Length, color, coverage, -1);
        AppendPop();
        _state = state;
        _textScale = textScale;
    }

    // Whether text is drawn in grayscale: inside a layer, an opacity below 1, or a transform that is not a move by whole physical
    // pixels (design §5.0.3, "ClearType").
    private bool TextIsGray => _opacityDepth > 0 || _transformed || !_snap;

    // EndGlyphRun inside a scope: the glyphs' boxes, in the text's pixels, go to the group's bounds through the state.
    private void EndGlyphRunInScope(int count, Color color, GlyphCoverage coverage, int paint)
    {
        if (_opacityDepth > 0)
        {
            MarkText();
            float unit = 1 / _textScale;
            foreach (GlyphInstance glyph in _glyphs.AsSpan(GlyphCount, count))
            {
                if (glyph.Width > 0)
                {
                    AccumulateLocal(glyph.X * unit, glyph.Y * unit, (glyph.X + glyph.Width) * unit, (glyph.Y + glyph.Height) * unit);
                }
            }
        }

        AppendGlyphRun(count, color, coverage, paint);
    }

    // A child's bounds in the commands' coordinates, added to the innermost group through the renderer's transform and cut to the
    // clips (the group's layer holds only what its children can draw).
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void AccumulateLocal(float left, float top, float right, float bottom)
    {
        Vector4 bounds = new(left, top, right, bottom);
        if (_state >= 0)
        {
            StateRecord state = _scopes!.States[_state];
            if (_transformed)
            {
                bounds = Bounds(state.Transform, bounds);
            }

            bounds = Intersection(bounds, state.Bounds);
            if (bounds.Z <= bounds.X || bounds.W <= bounds.Y)
            {
                return;
            }
        }

        Accumulate(bounds.X, bounds.Y, bounds.Z, bounds.W);
    }

    // A glyph that ResolveGlyphs placed, in the text's pixels of `state` (or physical pixels, for none), in physical pixels and cut
    // to the state's clips; empty when the clips hide it.
    private Vector4 GlyphBounds(in GlyphInstance glyph, int state)
    {
        var bounds = new Vector4(glyph.X, glyph.Y, glyph.X + glyph.Width, glyph.Y + glyph.Height);
        if (state < 0)
        {
            return bounds;
        }

        StateRecord record = _scopes!.States[state];
        bounds = Intersection(Bounds(record.Transform, bounds / record.TextScale), record.Bounds);
        return bounds;
    }

    // A clip or a transform pushed and not popped: what its push changed, which Pop restores, and its PushState command and records
    // when it wrote one. A clip past MaxRoundedClips is Layered: its Pop also ends the group of its layer.
    private struct StateFrame
    {
        public Matrix3x2 Transform;
        public Vector2 Shift;
        public bool Snap;
        public bool Transformed;
        public float TextScale;
        public int State;
        public Vector4 ClipRect;
        public bool Recorded;
        public bool Layered;
        public int Command;
        public int Record;
        public int ClipStart;
    }

    // The kinds of scopes Pop ends.
    private enum ScopeKind : byte
    {
        Group,
        State,
    }
}
