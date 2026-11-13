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

    // The effects held under each paused scope, in the order they were queued (Pause, Resume).
    private static readonly Dictionary<ScopeRef, List<Effect>> Held = [];
    private static readonly Arena<CleanupEntry> Cleanups = new();

    // The provided values of every scope, linked by index; slot 0 is never used, so that 0 is "none". An index needs no
    // generation: an entry is freed when the scope that provided it is disposed, which is after every scope under it.
    private static ContextEntry[] _contexts = new ContextEntry[16];
    private static int _contextCount = 1;
    private static int _freeContext;

    /// <summary>
    /// Gets the number of paused scopes: 0 keeps the effects' run free of the walk up their scopes.
    /// </summary>
    internal static int PausedCount { get; private set; }

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
            DisposeAfterBodyThrew(scope, bodyError);
            throw;
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
    /// <param name="scope">The scope's handle in the arena.</param>
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
    /// <param name="effect">The effect.</param>
    /// <param name="owner">The name <see cref="ScopeTracker"/> shows for the scope.</param>
    /// <returns>The new scope's handle in the arena.</returns>
    internal static ScopeRef AllocateForEffect(Effect effect, string owner) => Allocate(CurrentRef, effect, owner);

    /// <summary>
    /// Disposes what an effect's scope owns, keeping the scope, before the effect runs again.
    /// </summary>
    /// <param name="scope">The effect's scope.</param>
    /// <returns>The exceptions of its cleanups together, or <see langword="null"/> when none threw.</returns>
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
    /// <param name="scope">The effect's scope.</param>
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
    /// <param name="scope">The scope's handle in the arena.</param>
    /// <returns>Whether the scope is alive.</returns>
    internal static bool IsAlive(ScopeRef scope) => Scopes.IsAlive(scope);

    /// <summary>
    /// Pauses a scope (design §2.7, a <c>Show</c>'s exit): the effects under it that their sources mark are held, not run, so the
    /// subtree computes nothing while it leaves. Its nodes and their look stay as they are.
    /// </summary>
    /// <param name="scope">The scope.</param>
    internal static void Pause(ScopeHandle scope)
    {
        UiThread.VerifyAccess();
        ref ScopeData data = ref Scopes.GetRef(scope.Value);
        if (!data.Paused)
        {
            data.Paused = true;
            PausedCount++;
        }
    }

    /// <summary>
    /// Resumes a paused scope: the effects held under it run, those whose sources changed while it was paused (design §2.7, "Show
    /// revival semantics").
    /// </summary>
    /// <param name="scope">The scope.</param>
    internal static void Resume(ScopeHandle scope)
    {
        UiThread.VerifyAccess();
        ref ScopeData data = ref Scopes.GetRef(scope.Value);
        if (!data.Paused)
        {
            return;
        }

        data.Paused = false;
        PausedCount--;
        if (Held.Remove(scope.Value, out List<Effect>? held))
        {
            ReactiveRuntime.EnterBatch();
            try
            {
                foreach (Effect effect in held)
                {
                    effect.Release();
                }
            }
            finally
            {
                ReactiveRuntime.ExitBatch();
            }
        }
    }

    /// <summary>
    /// Holds a queued effect when a scope above it is paused.
    /// </summary>
    /// <param name="scope">The effect's own scope.</param>
    /// <param name="effect">The effect.</param>
    /// <returns><see langword="true"/> when it is held, and is not to run now.</returns>
    internal static bool TryHold(ScopeRef scope, Effect effect)
    {
        for (ScopeRef at = scope; !at.IsNone && Scopes.IsAlive(at); at = Scopes.GetRef(at).Parent)
        {
            if (Scopes.GetRef(at).Paused)
            {
                if (!Held.TryGetValue(at, out List<Effect>? held))
                {
                    held = [];
                    Held[at] = held;
                }

                held.Add(effect);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns whether a scope is still alive (not disposed).
    /// </summary>
    /// <param name="scope">The scope.</param>
    /// <returns>Whether the scope is alive.</returns>
    internal static bool IsAlive(ScopeHandle scope) => Scopes.IsAlive(scope.Value);

    /// <summary>
    /// Returns a copy of a live scope's data, for <see cref="ScopeTracker"/> and tests.
    /// </summary>
    /// <param name="scope">The scope's handle in the arena; the scope is alive.</param>
    /// <returns>The copy.</returns>
    internal static ScopeData Get(ScopeRef scope) => Scopes.GetRef(scope);

    /// <summary>
    /// Makes a scope owned by the current one, without running anything in it.
    /// </summary>
    /// <param name="owner">The name <see cref="ScopeTracker"/> shows for the scope.</param>
    /// <returns>The new scope's handle in the arena.</returns>
    internal static ScopeRef AllocateOwned(string owner)
    {
        UiThread.VerifyAccess();
        VerifyNotInComputed();
        return Allocate(CurrentRef, null, owner);
    }

    /// <summary>
    /// Puts a value of a context key on a scope, in front of the values it reads from above.
    /// </summary>
    /// <param name="scope">The scope.</param>
    /// <param name="key">The key.</param>
    /// <param name="slot">The slot of the value in the key's store.</param>
    internal static void PutContext(ScopeRef scope, IContextStore key, int slot)
    {
        int index = _freeContext;
        if (index != 0)
        {
            _freeContext = _contexts[index].Older;
        }
        else
        {
            if (_contextCount == _contexts.Length)
            {
                Array.Resize(ref _contexts, _contextCount * 2);
            }

            index = _contextCount++;
        }

        ref ScopeData data = ref Scopes.GetRef(scope);
        _contexts[index] = new ContextEntry
        {
            Key = key,
            Slot = slot,
            Older = data.Contexts,
        };
        data.Contexts = index;
    }

    /// <summary>
    /// Finds the nearest value of a context key from the current scope.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="slot">The slot of the value in the key's store.</param>
    /// <returns>Whether a value is provided.</returns>
    /// <exception cref="InvalidOperationException">
    /// The calling thread is not the UI thread; a computed value is computing; or there is no current scope, also inside an
    /// effect that was made with no current scope.
    /// </exception>
    internal static bool FindContext(IContextStore key, out int slot)
    {
        UiThread.VerifyAccess();
        if (ReactiveRuntime.Current is { } running && running is not Effect)
        {
            ThrowHelper.ThrowContextInsideComputed();
        }

        ScopeRef scope = CurrentRef;
        if (!Scopes.IsAlive(scope))
        {
            ThrowHelper.ThrowContextWithNoScope();
        }

        ref ScopeData data = ref Scopes.GetRef(scope);
        ContextEntry[] contexts = _contexts;
        for (int index = data.Contexts; index != 0; index = contexts[index].Older)
        {
            if (ReferenceEquals(contexts[index].Key, key))
            {
                slot = contexts[index].Slot;
                return true;
            }
        }

        // A value provided inside what belongs to nothing was found above. Without one, a default would hide a value that an
        // ancestor provided, as it would with no current scope.
        if (data.Unowned)
        {
            ThrowHelper.ThrowContextWithNoScope();
        }

        slot = 0;
        return false;
    }

    // Frees the values the scope itself provided: the entries in front of the parent's list.
    private static void ReleaseContexts(in ScopeData data)
    {
        int end = Scopes.IsAlive(data.Parent) ? Scopes.GetRef(data.Parent).Contexts : 0;
        int index = data.Contexts;
        while (index != end)
        {
            if (index == 0)
            {
                Invariant.Fail("A scope's provided values do not end at its parent's.");
            }

            ContextEntry released = _contexts[index];
            _contexts[index] = new ContextEntry { Older = _freeContext };
            _freeContext = index;
            released.Key.Release(released.Slot);
            index = released.Older;
        }
    }

    /// <summary>
    /// Disposes a scope whose body threw. If its cleanups throw too, an <see cref="AggregateException"/> carries the body's
    /// exception first, then theirs; otherwise it returns and the caller rethrows.
    /// </summary>
    /// <param name="scope">The scope.</param>
    /// <param name="bodyError">The exception of the body.</param>
    internal static void DisposeAfterBodyThrew(ScopeRef scope, Exception bodyError)
    {
        List<Exception>? errors = null;
        DisposeScope(scope, ref errors);
        if (errors is not null)
        {
            errors.Insert(0, bodyError);
            throw new AggregateException(errors);
        }
    }

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
        // made then belongs to nothing. So does an effect made with no current scope. A scope made with no current scope is a
        // root (a window's), which belongs to itself.
        bool unowned = parent.IsNone && effect is not null;
        if (!parent.IsNone && (!Scopes.IsAlive(parent) || Scopes.GetRef(parent).Disposing))
        {
            parent = default;
            unowned = true;
        }

        ScopeRef scope = Scopes.Allocate(
            new ScopeData
            {
                Parent = parent,
                Effect = effect,
                Unowned = unowned,
            }
        );
        if (!parent.IsNone)
        {
            ref readonly ScopeData above = ref Scopes.GetRef(parent);
            ref ScopeData made = ref Scopes.GetRef(scope);
            made.Contexts = above.Contexts;
            made.Unowned = above.Unowned;
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
        ReleaseContexts(final);
        Unlink(scope, final);
        if (final.Paused)
        {
            PausedCount--;
            _ = Held.Remove(scope);
        }

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
