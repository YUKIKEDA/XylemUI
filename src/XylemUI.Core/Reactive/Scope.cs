using System.Runtime.CompilerServices;

namespace XylemUI;

/// <summary>
/// The ownership tree (design §2.3): effects, child scopes, and cleanups made while a scope is current belong to it, and are
/// disposed with it, children first in LIFO order, then its own cleanups in LIFO order.
/// </summary>
/// <remarks>
/// <para>
/// The current scope is implicit: <see cref="Create"/> makes the new scope current while its body runs, and an effect is the
/// current scope while it runs (an effect owns what it makes and disposes it before each new run and when it is disposed).
/// Code outside any scope has no current scope; an effect made there belongs to nothing and is stopped by its own
/// <see cref="Effect.Dispose"/>.
/// </para>
/// <para>
/// A cleanup that throws does not stop the others: every cleanup runs, and the exceptions are thrown together as one
/// <see cref="AggregateException"/> at the end. Scopes live in an arena (design §2.5), so creating and disposing them in steady
/// state allocates nothing beyond the delegates passed in. All members belong to the UI thread.
/// </para>
/// </remarks>
public static class Scope
{
    private static readonly Arena<ScopeData> Scopes = new();
    private static readonly Arena<CleanupEntry> Cleanups = new();

    /// <summary>
    /// Gets the number of live scopes, effects' scopes included.
    /// </summary>
    internal static int LiveCount => Scopes.Count;

    /// <summary>
    /// Gets the scope that owns what is made now: the scope whose <see cref="Create"/> body runs, or the running effect's;
    /// <see langword="default"/> outside any scope. It does not flow across an <c>await</c>: keep it before the
    /// <c>await</c> and pass it to <see cref="Run"/> after.
    /// </summary>
    public static ScopeHandle Current => new(CurrentRef);

    /// <summary>
    /// Gets or sets the current scope's handle in the arena (<see cref="Current"/>).
    /// </summary>
    internal static ScopeRef CurrentRef { get; set; }

    /// <summary>
    /// Creates a scope owned by the current one and runs <paramref name="body"/> with it as the current scope. If
    /// <paramref name="body"/> throws, the scope is disposed and the exception goes on; if cleanups also throw, an
    /// <see cref="AggregateException"/> carries the body's exception first, then theirs.
    /// </summary>
    /// <param name="body">The code whose effects, scopes, and cleanups belong to the new scope.</param>
    /// <param name="owner">The name <see cref="ScopeTracker"/> shows for the scope; the caller's name by default.</param>
    /// <returns>The scope. Pass it to <see cref="Dispose"/> to dispose what it owns.</returns>
    /// <exception cref="InvalidOperationException">
    /// The calling thread is not the UI thread, or a computed value is computing (a computed value has no side effects).
    /// </exception>
    public static ScopeHandle Create(Action body, [CallerMemberName] string owner = "")
    {
        ArgumentNullException.ThrowIfNull(body);
        UiThread.VerifyAccess();
        VerifyNotInComputed();
        ScopeRef scope = Allocate(CurrentRef, null, owner);
        ScopeRef previous = CurrentRef;
        CurrentRef = scope;
        try
        {
            body();
        }
        catch (Exception bodyError) when (bodyError is not InvariantViolationException)
        {
            CurrentRef = previous;
            List<Exception>? errors = null;
            DisposeScope(scope, ref errors);
            if (errors is null)
            {
                throw;
            }

            errors.Insert(0, bodyError);
            throw new AggregateException(errors);
        }
        finally
        {
            CurrentRef = previous;
        }

        return new ScopeHandle(scope);
    }

    /// <summary>
    /// Runs <paramref name="body"/> with <paramref name="scope"/> as the current scope, so that what it makes belongs to that
    /// scope (SolidJS <c>runWithOwner</c>). A scope disposed meanwhile is normal after an <c>await</c> (the part of the screen
    /// went away): then <paramref name="body"/> does not run.
    /// </summary>
    /// <param name="scope">The scope, from <see cref="Current"/> or <see cref="Create"/>.</param>
    /// <param name="body">The code whose effects, scopes, and cleanups belong to <paramref name="scope"/>.</param>
    /// <returns><see langword="false"/> when <paramref name="scope"/> was disposed and <paramref name="body"/> did not run.</returns>
    /// <exception cref="InvalidOperationException">
    /// The calling thread is not the UI thread, or a computed value is computing (a computed value has no side effects).
    /// </exception>
    public static bool Run(ScopeHandle scope, Action body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (!TryEnter(scope.Value, out ScopeRef previous))
        {
            return false;
        }

        try
        {
            body();
        }
        finally
        {
            Exit(previous);
        }

        return true;
    }

    /// <summary>
    /// Makes <paramref name="scope"/> the current scope, as <see cref="Run"/> does around its body, for a caller on the frame path
    /// that calls a delegate with arguments a lambda could not capture (an input handler's <c>in</c> event, design §2.13). Pair it
    /// with <see cref="Exit"/> in a <c>finally</c>.
    /// </summary>
    /// <param name="scope">The scope.</param>
    /// <param name="previous">The scope that was current, for <see cref="Exit"/>.</param>
    /// <returns><see langword="false"/> when <paramref name="scope"/> was disposed; the current scope is then unchanged.</returns>
    /// <exception cref="InvalidOperationException">
    /// The calling thread is not the UI thread, or a computed value is computing.
    /// </exception>
    internal static bool TryEnter(ScopeRef scope, out ScopeRef previous)
    {
        UiThread.VerifyAccess();
        VerifyNotInComputed();
        previous = CurrentRef;
        if (!Scopes.IsAlive(scope) || Scopes.GetRef(scope).Disposing)
        {
            return false;
        }

        CurrentRef = scope;
        return true;
    }

    /// <summary>
    /// Makes <paramref name="previous"/> current again after <see cref="TryEnter"/>.
    /// </summary>
    /// <param name="previous">The scope <see cref="TryEnter"/> gave.</param>
    internal static void Exit(ScopeRef previous) => CurrentRef = previous;

    /// <summary>
    /// Registers a cleanup with the current scope, or with the running effect. It runs when that scope is disposed, or before
    /// the effect runs again.
    /// </summary>
    /// <param name="cleanup">The cleanup. A <see langword="static"/> lambda allocates nothing.</param>
    /// <exception cref="InvalidOperationException">
    /// The calling thread is not the UI thread, there is no current scope (also after the running effect disposed itself), or a
    /// computed value is computing.
    /// </exception>
    public static void OnCleanup(Action cleanup)
    {
        ArgumentNullException.ThrowIfNull(cleanup);
        UiThread.VerifyAccess();
        VerifyNotInComputed();
        ScopeRef scope = CurrentRef;
        if (!Scopes.IsAlive(scope))
        {
            ThrowHelper.ThrowNoCurrentScope();
        }

        Handle<CleanupEntry> older = Scopes.GetRef(scope).LastCleanup;
        Handle<CleanupEntry> entry = Cleanups.Allocate(new CleanupEntry(cleanup, older));
        Scopes.GetRef(scope).LastCleanup = entry;
    }

    /// <summary>
    /// Disposes a scope: its children in LIFO order (each with what it owns), then its cleanups in LIFO order. A scope that
    /// was already disposed is left alone.
    /// </summary>
    /// <param name="scope">The scope, from <see cref="Create"/>.</param>
    /// <exception cref="InvalidOperationException">The calling thread is not the UI thread.</exception>
    /// <exception cref="AggregateException">One or more cleanups threw; every cleanup still ran.</exception>
    public static void Dispose(ScopeHandle scope) => DisposeRef(scope.Value);

    /// <summary>
    /// Disposes the scope with this handle in the arena (<see cref="Dispose(ScopeHandle)"/>).
    /// </summary>
    internal static void DisposeRef(ScopeRef scope)
    {
        UiThread.VerifyAccess();
        if (Scopes.IsAlive(scope))
        {
            DisposeAndThrow(scope);
        }
    }

    /// <summary>
    /// Makes the scope of a new effect, owned by the current scope.
    /// </summary>
    internal static ScopeRef AllocateForEffect(Effect effect, string owner) => Allocate(CurrentRef, effect, owner);

    /// <summary>
    /// Disposes what an effect's scope owns, keeping the scope, before the effect runs again. Exceptions of its cleanups are
    /// returned together, or <see langword="null"/>.
    /// </summary>
    internal static AggregateException? ClearForRerun(ScopeRef scope)
    {
        List<Exception>? errors = null;
        ref ScopeData data = ref Scopes.GetRef(scope);
        data.Disposing = true;
        DisposeContents(scope, ref errors);
        Scopes.GetRef(scope).Disposing = false;
        return errors is null ? null : new AggregateException(errors);
    }

    /// <summary>
    /// Disposes an effect's scope when the effect is disposed; the exceptions of its cleanups are thrown together.
    /// </summary>
    internal static void DisposeForEffect(ScopeRef scope)
    {
        if (Scopes.IsAlive(scope))
        {
            DisposeAndThrow(scope);
        }
    }

    /// <summary>
    /// Returns whether a handle points to a live scope.
    /// </summary>
    internal static bool IsAlive(ScopeRef scope) => Scopes.IsAlive(scope);

    /// <summary>
    /// Returns whether a scope is still alive (not disposed).
    /// </summary>
    internal static bool IsAlive(ScopeHandle scope) => Scopes.IsAlive(scope.Value);

    /// <summary>
    /// Returns a copy of a live scope's data, for <see cref="ScopeTracker"/> and tests.
    /// </summary>
    internal static ScopeData Get(ScopeRef scope) => Scopes.GetRef(scope);

    private static void VerifyNotInComputed()
    {
        if (ReactiveRuntime.Current is { } running && running is not Effect)
        {
            ThrowHelper.ThrowScopeInsideComputed();
        }
    }

    private static ScopeRef Allocate(ScopeRef parent, Effect? effect, string owner)
    {
        // A parent that is gone (an effect that disposed itself while it runs) or being disposed takes no new children: what is
        // made then belongs to nothing.
        if (!Scopes.IsAlive(parent) || Scopes.GetRef(parent).Disposing)
        {
            parent = default;
        }

        ScopeRef scope = Scopes.Allocate(new ScopeData { Parent = parent, Effect = effect });
        if (!parent.IsNone)
        {
            ScopeRef older = Scopes.GetRef(parent).LastChild;
            Scopes.GetRef(scope).OlderSibling = older;
            if (!older.IsNone)
            {
                Scopes.GetRef(older).NewerSibling = scope;
            }

            Scopes.GetRef(parent).LastChild = scope;
        }

        if (ScopeTracker.IsEnabled)
        {
            Scopes.GetRef(scope).TrackerId = ScopeTracker.Add(owner, effect is not null, !parent.IsNone);
        }

        return scope;
    }

    private static void DisposeAndThrow(ScopeRef scope)
    {
        List<Exception>? errors = null;
        DisposeScope(scope, ref errors);
        if (errors is not null)
        {
            throw new AggregateException(errors);
        }
    }

    // Children, LIFO; then cleanups, LIFO; then the effect, the link from the parent, and the slot. A cleanup can make scopes and
    // grow the arena, so no ref into it is held across a call out.
    private static void DisposeScope(ScopeRef scope, ref List<Exception>? errors)
    {
        ref ScopeData data = ref Scopes.GetRef(scope);
        if (data.Disposing)
        {
            return;
        }

        data.Disposing = true;
        DisposeContents(scope, ref errors);
        ScopeData final = Scopes.GetRef(scope);
        final.Effect?.StopForDispose();
        Unlink(scope, final);
        if (final.TrackerId != 0)
        {
            ScopeTracker.Remove(final.TrackerId);
        }

        Scopes.Free(scope);
    }

    private static void DisposeContents(ScopeRef scope, ref List<Exception>? errors)
    {
        while (true)
        {
            ScopeRef child = Scopes.GetRef(scope).LastChild;
            if (child.IsNone)
            {
                break;
            }

            DisposeScope(child, ref errors);
        }

        while (true)
        {
            Handle<CleanupEntry> handle = Scopes.GetRef(scope).LastCleanup;
            if (handle.IsNone)
            {
                break;
            }

            CleanupEntry entry = Cleanups.GetRef(handle);
            Scopes.GetRef(scope).LastCleanup = entry.Older;
            Cleanups.Free(handle);
            try
            {
                entry.Run();
            }
            catch (Exception exception) when (exception is not InvariantViolationException)
            {
                (errors ??= []).Add(exception);
            }
        }
    }

    private static void Unlink(ScopeRef scope, in ScopeData data)
    {
        if (!data.OlderSibling.IsNone)
        {
            Scopes.GetRef(data.OlderSibling).NewerSibling = data.NewerSibling;
        }

        if (!data.NewerSibling.IsNone)
        {
            Scopes.GetRef(data.NewerSibling).OlderSibling = data.OlderSibling;
        }
        else if (Scopes.IsAlive(data.Parent) && Scopes.GetRef(data.Parent).LastChild == scope)
        {
            Scopes.GetRef(data.Parent).LastChild = data.OlderSibling;
        }
    }
}
