using System.Numerics;

namespace XylemUI;

/// <summary>
/// The kinds of calls a drop target gets (design §7, "Drag and drop").
/// </summary>
internal enum DropCall : byte
{
    /// <summary>
    /// A drag came over the target.
    /// </summary>
    Enter,

    /// <summary>
    /// A drag over the target moved, or its effect changed.
    /// </summary>
    Over,

    /// <summary>
    /// A drag left the target, or ended over it without a drop.
    /// </summary>
    Leave,

    /// <summary>
    /// The value was dropped on the target.
    /// </summary>
    Drop,
}

/// <summary>
/// What a node made a drag source with (design §7, "Drag and drop"): its options, the scope its callbacks run in, and the popup of
/// its preview.
/// </summary>
/// <param name="options">The options.</param>
/// <param name="scope">The scope the callbacks run in.</param>
internal abstract class DragSourceEntry(DragSourceOptions options, ScopeRef scope)
{
    /// <summary>
    /// Gets the options.
    /// </summary>
    public DragSourceOptions Options { get; } = options;

    /// <summary>
    /// Gets or sets the signal that opens the preview's popup, or <see langword="null"/> with no preview.
    /// </summary>
    public Signal<bool>? PreviewOpen { get; set; }

    /// <summary>
    /// Gets or sets the preview's popup block, or <see langword="null"/> with no preview.
    /// </summary>
    public PopupBlock? Preview { get; set; }

    /// <summary>
    /// Returns the value a drag starts with, read in the source's scope; <see langword="null"/> when the read threw, which was
    /// reported.
    /// </summary>
    /// <returns>The value, boxed.</returns>
    public object? Take()
    {
        ScopeRef previous = default;
        if (!scope.IsNone && !Scope.TryEnter(scope, out previous))
        {
            return null;
        }

        try
        {
            return Read();
        }
        catch (Exception exception) when (exception is not InvariantViolationException)
        {
            ReactiveRuntime.ReportException(exception);
            return null;
        }
        finally
        {
            if (!scope.IsNone)
            {
                Scope.Exit(previous);
            }
        }
    }

    /// <summary>
    /// Returns what the value is outside the app, read in the source's scope; <see langword="null"/> with no
    /// <see cref="DragSourceOptions.External"/>, when it returns none, or when it threw, which was reported.
    /// </summary>
    /// <returns>The data, or <see langword="null"/>.</returns>
    public ExternalDragData? ReadExternal()
    {
        if (Options.External is not { } external)
        {
            return null;
        }

        ScopeRef previous = default;
        bool entered = !scope.IsNone && Scope.TryEnter(scope, out previous);
        try
        {
            return external();
        }
        catch (Exception exception) when (exception is not InvariantViolationException)
        {
            ReactiveRuntime.ReportException(exception);
            return null;
        }
        finally
        {
            if (entered)
            {
                Scope.Exit(previous);
            }
        }
    }

    /// <summary>
    /// Runs <see cref="DragSourceOptions.Ended"/> with the effect, in the source's scope when it is still there.
    /// </summary>
    /// <param name="effect">The effect the drop chose, or <see cref="DragEffects.None"/>.</param>
    public void End(DragEffects effect)
    {
        if (Options.Ended is not { } ended)
        {
            return;
        }

        ScopeRef previous = default;
        bool entered = !scope.IsNone && Scope.TryEnter(scope, out previous);
        try
        {
            ended(effect);
        }
        catch (Exception exception) when (exception is not InvariantViolationException)
        {
            ReactiveRuntime.ReportException(exception);
        }
        finally
        {
            if (entered)
            {
                Scope.Exit(previous);
            }
        }
    }

    /// <summary>
    /// Reads the value.
    /// </summary>
    /// <returns>The value, boxed.</returns>
    protected abstract object Read();
}

/// <summary>
/// A drag source of values of type <typeparamref name="T"/>.
/// </summary>
/// <typeparam name="T">The type of the values.</typeparam>
/// <param name="value">What reads the value when a drag starts.</param>
/// <param name="options">The options.</param>
/// <param name="scope">The scope the callbacks run in.</param>
internal sealed class DragSourceEntry<T>(Func<T> value, DragSourceOptions options, ScopeRef scope) : DragSourceEntry(options, scope)
    where T : notnull
{
    /// <inheritdoc/>
    protected override object Read() => value();
}

/// <summary>
/// What a node made a drop target with (design §7, "Drag and drop"): which values it takes and what it does with them.
/// </summary>
/// <param name="scope">The scope the callbacks run in.</param>
internal abstract class DropTargetEntry(ScopeRef scope)
{
    /// <summary>
    /// Gets the effects the target accepts.
    /// </summary>
    public abstract DragEffects Accepts { get; }

    /// <summary>
    /// Returns whether the target takes a value now: its type matches and <see cref="DropTargetOptions{T}.CanDrop"/> says so.
    /// </summary>
    /// <param name="value">The value dragged.</param>
    /// <returns>Whether it takes the value.</returns>
    public bool Takes(object value)
    {
        ScopeRef previous = default;
        if (!scope.IsNone && !Scope.TryEnter(scope, out previous))
        {
            return false;
        }

        try
        {
            return Test(value);
        }
        catch (Exception exception) when (exception is not InvariantViolationException)
        {
            ReactiveRuntime.ReportException(exception);
            return false;
        }
        finally
        {
            if (!scope.IsNone)
            {
                Scope.Exit(previous);
            }
        }
    }

    /// <summary>
    /// Calls one of the target's handlers in its scope; an exception goes to the handler of unhandled exceptions.
    /// </summary>
    /// <param name="call">Which handler.</param>
    /// <param name="value">The value dragged.</param>
    /// <param name="position">Where the pointer is, in logical pixels from the target's top left.</param>
    /// <param name="effect">The effect a drop chooses now.</param>
    /// <param name="modifiers">The modifier keys held.</param>
    public void Send(DropCall call, object value, Vector2 position, DragEffects effect, KeyModifiers modifiers)
    {
        ScopeRef previous = default;
        if (!scope.IsNone && !Scope.TryEnter(scope, out previous))
        {
            return;
        }

        try
        {
            Call(call, value, position, effect, modifiers);
        }
        catch (Exception exception) when (exception is not InvariantViolationException)
        {
            ReactiveRuntime.ReportException(exception);
        }
        finally
        {
            if (!scope.IsNone)
            {
                Scope.Exit(previous);
            }
        }
    }

    /// <summary>
    /// Returns whether the target takes the value, outside the scope's handling.
    /// </summary>
    /// <param name="value">The value dragged.</param>
    /// <returns>Whether it takes the value.</returns>
    protected abstract bool Test(object value);

    /// <summary>
    /// Calls one of the handlers, outside the scope's handling.
    /// </summary>
    /// <param name="call">Which handler.</param>
    /// <param name="value">The value dragged.</param>
    /// <param name="position">Where the pointer is.</param>
    /// <param name="effect">The effect.</param>
    /// <param name="modifiers">The modifier keys.</param>
    protected abstract void Call(DropCall call, object value, Vector2 position, DragEffects effect, KeyModifiers modifiers);
}

/// <summary>
/// A drop target of values of type <typeparamref name="T"/>.
/// </summary>
/// <typeparam name="T">The type it takes.</typeparam>
/// <param name="options">The options.</param>
/// <param name="scope">The scope the callbacks run in.</param>
internal sealed class DropTargetEntry<T>(DropTargetOptions<T> options, ScopeRef scope) : DropTargetEntry(scope)
    where T : notnull
{
    /// <inheritdoc/>
    public override DragEffects Accepts => options.Accepts;

    /// <inheritdoc/>
    protected override bool Test(object value) => value is T typed && (options.CanDrop?.Invoke(typed) ?? true);

    /// <inheritdoc/>
    protected override void Call(DropCall call, object value, Vector2 position, DragEffects effect, KeyModifiers modifiers)
    {
        DragHandler<T>? handler = call switch
        {
            DropCall.Enter => options.Enter,
            DropCall.Over => options.Over,
            DropCall.Leave => options.Leave,
            _ => options.Drop,
        };
        if (handler is not null && value is T typed)
        {
            var e = new DragEvent<T>(typed, position, effect, modifiers);
            handler(in e);
        }
    }
}

/// <summary>
/// The source of a drag from outside the app (design §7, "Drag and drop", "From the OS"): its value, and the effects the OS's source
/// allows; it has no preview and no end of its own.
/// </summary>
/// <param name="value">The value.</param>
/// <param name="allowed">The effects allowed.</param>
internal sealed class ExternalSourceEntry(object value, DragEffects allowed) : DragSourceEntry(new DragSourceOptions { Allowed = allowed }, default)
{
    /// <inheritdoc/>
    protected override object Read() => value;
}
