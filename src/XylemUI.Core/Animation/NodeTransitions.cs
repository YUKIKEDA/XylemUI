using System.Numerics;

namespace XylemUI;

/// <summary>
/// The paint values of one node that follow their changes over a duration (design §2.14, "Transitions"): each value the node draws
/// with, its look's or its pseudo-state's, moves from the one on the screen to the new one along a curve, with the duration started
/// again when the target changes. The frame resolves the values and hands them here when it draws the node.
/// </summary>
/// <param name="tree">The node's tree.</param>
/// <param name="node">The node.</param>
internal sealed class NodeTransitions(UiTree tree, NodeHandle node) : Ticking
{
    private static readonly Func<Paint?, Paint?, float, Paint?> LerpPaint = static (a, b, t) => Lerp(a, b, t);

    private static readonly Func<Color?, Color?, float, Color?> LerpForeground = static (a, b, t) =>
        a is { } from && b is { } to ? Lerp(from, to, t) : (t < 0.5f ? a : b);

    private static readonly Func<float, float, float, float> LerpFloat = static (a, b, t) => a + ((b - a) * t);

    private static readonly Func<Matrix3x2?, Matrix3x2?, float, Matrix3x2?> LerpTransform = static (a, b, t) =>
        Matrix3x2.Lerp(a ?? Matrix3x2.Identity, b ?? Matrix3x2.Identity, t);

    private static readonly Func<CornerRadii, CornerRadii, float, CornerRadii> LerpRadii = static (a, b, t) =>
        new(
            a.TopLeft + ((b.TopLeft - a.TopLeft) * t),
            a.TopRight + ((b.TopRight - a.TopRight) * t),
            a.BottomRight + ((b.BottomRight - a.BottomRight) * t),
            a.BottomLeft + ((b.BottomLeft - a.BottomLeft) * t)
        );

    private static readonly Func<BoxShadow?, BoxShadow?, float, BoxShadow?> LerpShadow = static (a, b, t) => Lerp(a, b, t);

    private TransitionSlot<Paint?> _background;
    private TransitionSlot<Paint?> _borderPaint;
    private TransitionSlot<Color?> _foreground;
    private TransitionSlot<float> _opacity;
    private TransitionSlot<Matrix3x2?> _transform;
    private TransitionSlot<CornerRadii> _radii;
    private TransitionSlot<BoxShadow?> _shadow;
    private bool _drawn;

    /// <summary>
    /// The paint values a transition can move.
    /// </summary>
    internal enum Kind
    {
        /// <summary>The background.</summary>
        Background,

        /// <summary>The border's paint.</summary>
        BorderPaint,

        /// <summary>The color of the text.</summary>
        Foreground,

        /// <summary>The opacity.</summary>
        Opacity,

        /// <summary>The transform.</summary>
        Transform,

        /// <summary>The corner radii.</summary>
        CornerRadius,

        /// <summary>The shadow.</summary>
        Shadow,
    }

    /// <summary>
    /// Makes a value follow its changes over a duration along a curve.
    /// </summary>
    /// <param name="kind">The value.</param>
    /// <param name="duration">The duration; 0 for none.</param>
    /// <param name="curve">The curve.</param>
    internal void Set(Kind kind, TimeSpan duration, Curve curve)
    {
        switch (kind)
        {
            case Kind.Background:
                _background.Configure(duration, curve);
                break;
            case Kind.BorderPaint:
                _borderPaint.Configure(duration, curve);
                break;
            case Kind.Foreground:
                _foreground.Configure(duration, curve);
                break;
            case Kind.Opacity:
                _opacity.Configure(duration, curve);
                break;
            case Kind.Transform:
                _transform.Configure(duration, curve);
                break;
            case Kind.CornerRadius:
                _radii.Configure(duration, curve);
                break;
            default:
                _shadow.Configure(duration, curve);
                break;
        }
    }

    /// <summary>
    /// Replaces the values a frame resolved for the node with the ones on their way to them, and puts the node in the window's list
    /// while one moves.
    /// </summary>
    /// <param name="now">The frame's time.</param>
    /// <param name="background">The background.</param>
    /// <param name="borderPaint">The border's paint.</param>
    /// <param name="foreground">The color of the text.</param>
    /// <param name="opacity">The opacity.</param>
    /// <param name="transform">The transform.</param>
    /// <param name="radii">The corner radii.</param>
    /// <param name="shadow">The shadow.</param>
    internal void Apply(
        TimeSpan now,
        ref Paint? background,
        ref Paint? borderPaint,
        ref Color? foreground,
        ref float opacity,
        ref Matrix3x2? transform,
        ref CornerRadii radii,
        ref BoxShadow? shadow
    )
    {
        bool reduce = tree.Animations.ReduceMotion;
        background = _background.Apply(background, now, reduce, LerpPaint);
        borderPaint = _borderPaint.Apply(borderPaint, now, reduce, LerpPaint);
        foreground = _foreground.Apply(foreground, now, reduce, LerpForeground);
        opacity = _opacity.Apply(opacity, now, reduce, LerpFloat);
        transform = _transform.Apply(transform, now, reduce, LerpTransform);
        radii = _radii.Apply(radii, now, reduce, LerpRadii);
        shadow = _shadow.Apply(shadow, now, reduce, LerpShadow);
        _drawn = true;
        if (Running)
        {
            tree.Animations.Add(this);
        }
    }

    /// <inheritdoc/>
    internal override bool Tick(TimeSpan now)
    {
        if (!Running || !tree.Nodes.IsAlive(node))
        {
            return false;
        }

        // A node not drawn since the last move (it or an ancestor became hidden) would never move its values on, and the list would
        // ask for frames that change nothing: its values go to their targets, and it leaves the list.
        if (!_drawn)
        {
            Finish();
            return false;
        }

        // The node is drawn again in this frame, which moves its values on (Apply).
        _drawn = false;
        Invalidation.Invalidate(tree, node, ChangeKind.Color);
        return true;
    }

    private void Finish()
    {
        _background.Finish();
        _borderPaint.Finish();
        _foreground.Finish();
        _opacity.Finish();
        _transform.Finish();
        _radii.Finish();
        _shadow.Finish();
    }

    private bool Running =>
        _background.Running
        || _borderPaint.Running
        || _foreground.Running
        || _opacity.Running
        || _transform.Running
        || _radii.Running
        || _shadow.Running;

    // Each channel, alpha too, rounded.
    private static Color Lerp(Color a, Color b, float t) =>
        new(
            (byte)MathF.Round(a.R + ((b.R - a.R) * t)),
            (byte)MathF.Round(a.G + ((b.G - a.G) * t)),
            (byte)MathF.Round(a.B + ((b.B - a.B) * t)),
            (byte)MathF.Round(a.A + ((b.A - a.A) * t))
        );

    // Two solid colors mix; none is the other's color with no alpha, so a background fades in or out (Flutter's Color.lerp). A
    // gradient on either side changes halfway: a gradient brush cannot be changed, and mixing two would allocate one each frame.
    private static Paint? Lerp(Paint? a, Paint? b, float t)
    {
        if (a?.Gradient is not null || b?.Gradient is not null)
        {
            return t < 0.5f ? a : b;
        }

        Color to = b?.Color ?? a!.Value.Color with { A = 0 };
        Color from = a?.Color ?? to with { A = 0 };
        return new Paint(Lerp(from, to, t));
    }

    // Two shadows of the same kind mix; with none, or an inset and an outer one, it changes halfway.
    private static BoxShadow? Lerp(BoxShadow? a, BoxShadow? b, float t)
    {
        if (a is not { } from || b is not { } to || from.Inset != to.Inset)
        {
            return t < 0.5f ? a : b;
        }

        return new BoxShadow(
            Lerp(from.Color, to.Color, t),
            Vector2.Lerp(from.Offset, to.Offset, t),
            from.BlurRadius + ((to.BlurRadius - from.BlurRadius) * t),
            from.Spread + ((to.Spread - from.Spread) * t),
            to.Inset
        );
    }
}

/// <summary>
/// One value of a node's transitions: the value on the screen, and the move from it to the last target.
/// </summary>
/// <typeparam name="T">The value's type.</typeparam>
internal struct TransitionSlot<T>
{
    private bool _configured;
    private TimeSpan _duration;
    private Curve _curve;
    private bool _seen;
    private T _shown;
    private T _from;
    private T _to;
    private TimeSpan _start;

    /// <summary>
    /// Gets a value indicating whether the value is moving.
    /// </summary>
    public bool Running { get; private set; }

    /// <summary>
    /// Makes the value follow its changes.
    /// </summary>
    /// <param name="duration">The duration; 0 for none.</param>
    /// <param name="curve">The curve.</param>
    public void Configure(TimeSpan duration, Curve curve)
    {
        _configured = duration > TimeSpan.Zero;
        _duration = duration;
        _curve = curve;
        if (!_configured)
        {
            Running = false;
        }
    }

    /// <summary>
    /// Ends the move: the value on the screen is the last target.
    /// </summary>
    public void Finish()
    {
        if (Running)
        {
            Running = false;
            _shown = _to;
        }
    }

    /// <summary>
    /// Returns the value to draw for a target: the target itself the first time and when the value does not follow; otherwise the
    /// value on its way from the one on the screen when the target changed.
    /// </summary>
    /// <param name="target">The value the node resolves to now.</param>
    /// <param name="now">The frame's time.</param>
    /// <param name="reduce">Whether the OS asks for less motion: the value then jumps.</param>
    /// <param name="lerp">How two values mix.</param>
    /// <returns>The value to draw.</returns>
    public T Apply(T target, TimeSpan now, bool reduce, Func<T, T, float, T> lerp)
    {
        if (!_configured)
        {
            return target;
        }

        if (!_seen)
        {
            _seen = true;
            _shown = target;
            _to = target;
            return target;
        }

        if (!EqualityComparer<T>.Default.Equals(target, _to))
        {
            _to = target;
            if (reduce)
            {
                Running = false;
                _shown = target;
                return target;
            }

            // From the value on the screen, with the duration started again (Flutter's implicit animations, Avalonia's Transitions).
            _from = _shown;
            _start = now;
            Running = true;
        }

        if (Running)
        {
            double progress = (now - _start) / _duration;
            if (progress >= 1)
            {
                Running = false;
                _shown = _to;
            }
            else
            {
                _shown = lerp(_from, _to, _curve.Transform((float)progress));
            }
        }

        return _shown;
    }
}
