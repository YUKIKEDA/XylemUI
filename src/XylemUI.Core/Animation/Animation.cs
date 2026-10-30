namespace XylemUI;

/// <summary>
/// A value that the caller moves over time, as a signal (design §2.14, "Declarations"): Flutter's <c>AnimationController</c>.
/// Bind <see cref="Value"/> to a node's property, or read it in an effect; set it directly to follow a finger, then
/// <see cref="Fling"/> on release.
/// </summary>
/// <remarks>
/// <para>
/// It moves on the frames of the window whose root function made it, at the frames' time (<see cref="WindowFrames.FrameTime"/>), and
/// asks for frames only while it moves. A move starts on the next frame. It belongs to the scope that made it and stops when the
/// scope is disposed.
/// </para>
/// <para>
/// A move along a curve has its own duration, which <see cref="Forward"/> and <see cref="Reverse"/> shorten by the part of 0 to 1
/// already behind (as Flutter's <c>forward</c> does); a spring carries its speed, so a new target from <see cref="SpringTo"/> or
/// <see cref="Fling"/> starts from the value and the speed of now. When the OS asks for less motion, every move jumps to its end.
/// </para>
/// </remarks>
public sealed class Animation
{
    private readonly UiTree _tree;
    private readonly Signal<float> _value;
    private readonly Driver _driver;

    // The move: along a curve (from, to, duration, curve), or by a spring (target, spring); the start is the first frame's time.
    private bool _spring;
    private float _from;
    private float _to;
    private TimeSpan _length;
    private Curve _moveCurve;
    private Spring _springConfig;
    private bool _started;
    private TimeSpan _start;
    private TimeSpan _last;
    private float _velocity;

    private Animation(UiTree tree, TimeSpan duration, Curve curve, float value)
    {
        _tree = tree;
        Duration = duration;
        Curve = curve;
        _moveCurve = curve;
        _value = Signal.Create(value);
        _driver = new Driver(this);
    }

    /// <summary>
    /// Gets the value, which moves as the animation runs.
    /// </summary>
    public IReadOnlySignal<float> Value => _value;

    /// <summary>
    /// Gets the duration of a move from 0 to 1.
    /// </summary>
    public TimeSpan Duration { get; }

    /// <summary>
    /// Gets the curve of a move.
    /// </summary>
    public Curve Curve { get; }

    /// <summary>
    /// Gets a value indicating whether the value is moving.
    /// </summary>
    public bool IsAnimating => _driver.Slot >= 0;

    /// <summary>
    /// Makes an animation at a value, owned by the current scope, on the frames of the current window's tree.
    /// </summary>
    /// <param name="duration">The duration of a move from 0 to 1; above 0.</param>
    /// <param name="curve">The curve of a move; <see cref="Curve.Linear"/> when <see langword="null"/>.</param>
    /// <param name="value">The value it starts at.</param>
    /// <returns>The animation.</returns>
    /// <exception cref="InvalidOperationException">No window's root function is running, or there is no current scope.</exception>
    public static Animation Create(TimeSpan duration, Curve? curve = null, float value = 0)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(duration, TimeSpan.Zero);
        UiTree tree = Elements.CurrentTree();
        if (Scope.CurrentRef.IsNone)
        {
            throw new InvalidOperationException("An animation is made inside a scope, which owns it; there is no current scope.");
        }

        var animation = new Animation(tree, duration, curve ?? Curve.Linear, value);
        Scope.OnCleanup(animation.Stop);
        return animation;
    }

    /// <summary>
    /// Moves the value to 1 along the curve, in the part of <see cref="Duration"/> that the distance left takes.
    /// </summary>
    public void Forward() => MoveTo(1, Scale(1));

    /// <summary>
    /// Moves the value to 0 along the curve, in the part of <see cref="Duration"/> that the distance left takes.
    /// </summary>
    public void Reverse() => MoveTo(0, Scale(0));

    /// <summary>
    /// Moves the value to a target along a curve, in a duration.
    /// </summary>
    /// <param name="target">The target.</param>
    /// <param name="duration">The duration; <see cref="Duration"/> when <see langword="null"/>. Above 0.</param>
    /// <param name="curve">The curve; <see cref="Curve"/> when <see langword="null"/>.</param>
    public void AnimateTo(float target, TimeSpan? duration = null, Curve? curve = null)
    {
        TimeSpan length = duration ?? Duration;
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(length, TimeSpan.Zero);
        MoveTo(target, length, curve);
    }

    /// <summary>
    /// Sets the value at once and stops the move: what a gesture does to follow the finger.
    /// </summary>
    /// <param name="value">The value.</param>
    public void Set(float value)
    {
        Stop();
        _velocity = 0;
        _value.Set(value);
    }

    /// <summary>
    /// Lets a spring carry the value from a speed: to 1 when the speed is 0 or more, to 0 when it is less (Flutter's <c>fling</c>).
    /// </summary>
    /// <param name="velocity">The speed, in units of the value a second, such as a finger's on release.</param>
    /// <param name="spring">The spring; <see cref="Spring.Default"/> when <see langword="null"/>.</param>
    public void Fling(float velocity, Spring? spring = null)
    {
        _velocity = velocity;
        StartSpring(velocity >= 0 ? 1 : 0, spring ?? Spring.Default);
    }

    /// <summary>
    /// Lets a spring carry the value to a target from the value and the speed of now.
    /// </summary>
    /// <param name="target">The target.</param>
    /// <param name="spring">The spring; <see cref="Spring.Default"/> when <see langword="null"/>.</param>
    public void SpringTo(float target, Spring? spring = null) => StartSpring(target, spring ?? Spring.Default);

    /// <summary>
    /// Stops the move where the value is.
    /// </summary>
    public void Stop()
    {
        UiThread.VerifyAccess();
        _tree.Animations.Remove(_driver);
    }

    private TimeSpan Scale(float target)
    {
        float left = Math.Clamp(MathF.Abs(target - _value.Peek()), 0, 1);
        return Duration * left;
    }

    private void MoveTo(float target, TimeSpan length, Curve? curve = null)
    {
        UiThread.VerifyAccess();
        if (_tree.Animations.ReduceMotion || length <= TimeSpan.Zero)
        {
            Set(target);
            return;
        }

        _spring = false;
        _from = _value.Peek();
        _to = target;
        _length = length;
        _moveCurve = curve ?? Curve;
        Begin();
    }

    private void StartSpring(float target, Spring spring)
    {
        UiThread.VerifyAccess();
        if (_tree.Animations.ReduceMotion)
        {
            Set(target);
            return;
        }

        // A spring that already moves goes on from this frame's time, with its speed; anything else starts on the next frame.
        bool moving = _spring && IsAnimating;
        _spring = true;
        _to = target;
        _springConfig = spring;
        Begin(restart: !moving);
    }

    // The move starts on the next frame, at its time, unless it goes on from where it is.
    private void Begin(bool restart = true)
    {
        if (restart)
        {
            _started = false;
        }

        _tree.Animations.Add(_driver);
    }

    private bool Tick(TimeSpan now)
    {
        if (!_started)
        {
            _started = true;
            _start = now;
            _last = now;
        }

        float before = _value.Peek();
        float value;
        bool running;
        if (_spring)
        {
            value = before;
            _springConfig.Step(ref value, ref _velocity, _to, (now - _last).TotalSeconds);
            running = !Spring.IsSettled(value, _velocity, _to);
            if (!running)
            {
                value = _to;
                _velocity = 0;
            }
        }
        else
        {
            double progress = (now - _start) / _length;
            running = progress < 1;
            value = running ? _from + ((_to - _from) * _moveCurve.Transform((float)progress)) : _to;
            double seconds = (now - _last).TotalSeconds;
            _velocity = running && seconds > 0 ? (float)((value - before) / seconds) : 0;
        }

        _last = now;
        _value.Set(value);
        return running;
    }

    // The animation's place in the window's list: a separate object, as the list's type is internal.
    private sealed class Driver(Animation animation) : Ticking
    {
        internal override bool Tick(TimeSpan now) => animation.Tick(now);
    }
}
