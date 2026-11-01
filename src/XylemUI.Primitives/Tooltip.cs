using System.Numerics;
using System.Runtime.CompilerServices;
using static XylemUI.Elements;
using NodeHandle = XylemUI.Handle<XylemUI.UiNode>;

namespace XylemUI.Primitives;

/// <summary>
/// When a tooltip shows and hides, where it goes, and how it moves (design §13.1, "Tooltip").
/// </summary>
public sealed record TooltipOptions
{
    /// <summary>
    /// Gets how long the pointer stays over the anchor before the tooltip shows; 400 ms by default, Windows' hover time.
    /// </summary>
    public TimeSpan ShowDelay { get; init; } = TimeSpan.FromMilliseconds(400);

    /// <summary>
    /// Gets how soon after another tooltip closed this one shows at once; 100 ms by default.
    /// </summary>
    public TimeSpan BetweenShowDelay { get; init; } = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Gets how long the pointer is away from both the anchor and the tooltip before the tooltip hides; 100 ms by default.
    /// </summary>
    public TimeSpan HideDelay { get; init; } = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Gets where the tooltip goes against its anchor; <see langword="null"/>, the default, puts it 20 px below the pointer.
    /// </summary>
    public PopupPlacement? Placement { get; init; }

    /// <summary>
    /// Gets how the tooltip comes in and leaves; <see langword="null"/> for at once.
    /// </summary>
    public ShowMotion? Motion { get; init; }
}

/// <content>
/// The tooltip (design §13.1, "Tooltip").
/// </content>
public static partial class Primitive
{
    // The tooltip shown in each window, and when the last one closed.
    private static readonly ConditionalWeakTable<UiTree, TooltipWindow> Tooltips = [];

    /// <summary>
    /// Shows a tooltip for the node (design §13.1, "Tooltip"): after the pointer stays over it for <see cref="TooltipOptions.ShowDelay"/>,
    /// or at once when another tooltip closed shortly before, and at once on a long press of a touch or a pen. One tooltip shows in a
    /// window at a time. The pointer can move into it; it hides <see cref="TooltipOptions.HideDelay"/> after the pointer left both the
    /// node and the tooltip, on any press, on a wheel over either, on Esc, and when another popup opens. The content is made each
    /// time it shows. A disabled node shows none, and the focus shows none.
    /// </summary>
    /// <param name="anchor">The node the tooltip is for.</param>
    /// <param name="content">The tooltip's content: its look, made each time it shows.</param>
    /// <param name="options">When it shows and hides, where it goes, and how it moves; the defaults when <see langword="null"/>.</param>
    /// <returns>The same node, for the next call of the chain.</returns>
    /// <exception cref="InvalidOperationException">There is no current scope.</exception>
    public static Element Tooltip(this Element anchor, Func<Element> content, TooltipOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        UiTree tree = anchor.TreeOrThrow();
        RequireScope(nameof(Tooltip));
        var tooltip = new TooltipState(tree, anchor.Node, content, options ?? new TooltipOptions());
        return anchor
            .On(PointerEventKind.Enter, EventPhase.Bubble, tooltip.AnchorEnter)
            .On(PointerEventKind.Leave, EventPhase.Bubble, tooltip.AnchorLeave)
            .On(PointerEventKind.Move, EventPhase.Bubble, tooltip.AnchorMove)
            .On(PointerEventKind.Down, EventPhase.Bubble, tooltip.Down)
            .On(PointerEventKind.Up, EventPhase.Bubble, tooltip.Up)
            .On(PointerEventKind.Wheel, EventPhase.Bubble, tooltip.Wheel);
    }

    private sealed class TooltipWindow
    {
        public TooltipState? Shown { get; set; }

        public long? LastClose { get; set; }
    }

    // One tooltip: its popup, transient (design §7.3, "Nesting"), and the one timer that waits to show it, to hide it, or for a long
    // press. The timer runs on the window's clock and posts to the UI thread, as the caret's blink does; no frame runs while it waits.
    private sealed class TooltipState
    {
        private const float LongPressSlop = 18;

        private static readonly TimeSpan LongPress = TimeSpan.FromMilliseconds(500);
        private static readonly SendOrPostCallback Fire = static state => ((TooltipState)state!).Elapsed();

        private readonly UiTree _tree;
        private readonly Func<Element> _content;
        private readonly TooltipOptions _options;
        private readonly Signal<bool> _open = Signal.Create(false);
        private readonly PopupBlock _block;
        private readonly TooltipWindow _window;
        private readonly SynchronizationContext? _ui = SynchronizationContext.Current;
        private ITimer? _timer;
        private Waiting _waiting;
        private bool _overAnchor;
        private bool _overTip;
        private bool _pressing;
        private bool _shownByPress;
        private Vector2 _pointer;
        private Vector2 _pressedAt;

        public TooltipState(UiTree tree, NodeHandle anchor, Func<Element> content, TooltipOptions options)
        {
            _tree = tree;
            _content = content;
            _options = options;
            _window = Tooltips.GetValue(tree, static _ => new TooltipWindow());
            _block = PopupBlock.Create(tree, anchor, _open, Render, new PopupOptions { IsTransient = true, Motion = options.Motion });

            // The popup closes itself too (a press outside, Esc, a scroll, another popup opening): it writes false to the signal.
            _ = Effect.Create(() =>
            {
                if (!_open.Get())
                {
                    Closed();
                }
            });
            Scope.OnCleanup(() =>
            {
                _timer?.Dispose();
                _timer = null;
                if (_window.Shown == this)
                {
                    _window.Shown = null;
                }
            });
        }

        private enum Waiting : byte
        {
            None,
            Show,
            Hide,
            LongPress,
        }

        // The pointer came over the anchor without a button down (a mouse, or a pen that hovers): the tooltip shows after the delay,
        // or at once when another shows or closed shortly before; a hide that waited stops.
        public void AnchorEnter(in PointerEvent e)
        {
            if (e.PointerType == PointerType.Touch || e.Buttons != PointerButtons.None)
            {
                return;
            }

            _overAnchor = true;
            _pointer = e.Position;
            if (_open.Peek())
            {
                Stop(Waiting.Hide);
                return;
            }

            if (_window.Shown is not null || (_window.LastClose is { } closed && _tree.Time.GetElapsedTime(closed) <= _options.BetweenShowDelay))
            {
                Show(_pointer);
                return;
            }

            Wait(Waiting.Show, _options.ShowDelay);
        }

        public void AnchorLeave(in PointerEvent e)
        {
            if (e.PointerType == PointerType.Touch)
            {
                return;
            }

            _overAnchor = false;
            Stop(Waiting.Show);
            LeftBoth();
        }

        // The pointer's place, where the tooltip shows; a touch or a pen that moves past the slop is no long press.
        public void AnchorMove(in PointerEvent e)
        {
            if (_pressing)
            {
                if (Vector2.Distance(e.Position, _pressedAt) > LongPressSlop)
                {
                    _pressing = false;
                    Stop(Waiting.LongPress);
                }

                return;
            }

            if (e.Buttons == PointerButtons.None)
            {
                _pointer = e.Position;
            }
        }

        // Any press hides the tooltip; a touch's or a pen's starts a long press.
        public void Down(in PointerEvent e)
        {
            Hide();
            if (e.PointerType is PointerType.Touch or PointerType.Pen)
            {
                _pressing = true;
                _pressedAt = e.Position;
                Wait(Waiting.LongPress, LongPress);
            }
        }

        // The release ends a long press, and hides the tooltip it showed.
        public void Up(in PointerEvent e)
        {
            if (!_pressing && !_shownByPress)
            {
                return;
            }

            _pressing = false;
            Stop(Waiting.LongPress);
            if (_shownByPress)
            {
                Hide();
            }
        }

        public void Wheel(in PointerEvent e) => Hide();

        // The tooltip's content, in a box that follows the pointer over it.
        private Element Render() =>
            Box(_content())
                .On(PointerEventKind.Enter, EventPhase.Bubble, TipEnter)
                .On(PointerEventKind.Leave, EventPhase.Bubble, TipLeave)
                .On(PointerEventKind.Down, EventPhase.Bubble, Down)
                .On(PointerEventKind.Wheel, EventPhase.Bubble, Wheel);

        private void TipEnter(in PointerEvent e)
        {
            _overTip = true;
            Stop(Waiting.Hide);
        }

        private void TipLeave(in PointerEvent e)
        {
            _overTip = false;
            LeftBoth();
        }

        private void LeftBoth()
        {
            if (!_overAnchor && !_overTip && _open.Peek() && !_shownByPress)
            {
                Wait(Waiting.Hide, _options.HideDelay);
            }
        }

        // Shows the tooltip at a point of the window, or against the anchor with a placement given; the one shown elsewhere hides.
        private void Show(Vector2 at)
        {
            Stop(_waiting);
            if (_window.Shown is { } other && other != this)
            {
                other.Hide();
            }

            PopupPlacement placement = _options.Placement ?? PopupPlacement.AtPoint(at + new Vector2(0, 20));
            _block.Options = _block.Options with { Placement = placement };
            _window.Shown = this;
            _open.Set(true);
        }

        private void Hide()
        {
            Stop(_waiting);
            _shownByPress = false;
            if (_open.Peek())
            {
                _open.Set(false);
            }
        }

        // The popup closed, by the tooltip or by itself: the window remembers when, for the next tooltip's delay.
        private void Closed()
        {
            _overTip = false;
            _shownByPress = false;
            if (_window.Shown == this)
            {
                _window.Shown = null;
                _window.LastClose = _tree.Time.GetTimestamp();
            }
        }

        private void Wait(Waiting waiting, TimeSpan delay)
        {
            if (_ui is null)
            {
                return;
            }

            _waiting = waiting;
            if (_timer is null)
            {
                _timer = _tree.Time.CreateTimer(static state => ((TooltipState)state!).Tick(), this, delay, Timeout.InfiniteTimeSpan);
            }
            else
            {
                _ = _timer.Change(delay, Timeout.InfiniteTimeSpan);
            }
        }

        private void Stop(Waiting waiting)
        {
            if (waiting != Waiting.None && _waiting == waiting)
            {
                _waiting = Waiting.None;
                _ = _timer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            }
        }

        // The timer may fire on another thread: its work runs on the UI thread.
        private void Tick()
        {
            if (SynchronizationContext.Current != _ui)
            {
                _ui!.Post(Fire, this);
                return;
            }

            Elapsed();
        }

        private void Elapsed()
        {
            Waiting waiting = _waiting;
            _waiting = Waiting.None;
            switch (waiting)
            {
                case Waiting.Show when _overAnchor:
                    Show(_pointer);
                    break;
                case Waiting.Hide when !_overAnchor && !_overTip:
                    Hide();
                    break;
                case Waiting.LongPress when _pressing:
                    Show(_pressedAt);
                    _shownByPress = true;
                    break;
            }
        }
    }
}
