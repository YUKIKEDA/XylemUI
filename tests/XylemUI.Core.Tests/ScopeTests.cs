namespace XylemUI.Tests;

// The ownership tree (S9, design §2.3): disposal order, exceptions of cleanups, effects as owners, the tracker.
[Collection(UiThreadGroup.Name)]
public sealed class ScopeTests(UiThreadFixture ui)
{
    private static long _sink;

    /// <summary>
    /// Disposing a scope disposes its children first, newest first, each with what it owns, and then runs its own cleanups,
    /// newest first.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A root scope that registers r1, makes child A (cleanups a1, a2), registers r2, and makes child B (cleanup b1, then a
    ///   grandchild B1 with cleanup b11).
    ///
    /// Steps:
    /// - Dispose the root.
    ///
    /// Expected:
    /// - The cleanups ran in the order b11, b1, a2, a1, r2, r1, and no scope is left alive.
    /// </remarks>
    [Fact]
    public void Dispose_ChildrenLifoThenCleanupsLifo() =>
        ui.Invoke(() =>
        {
            var order = new List<string>();
            int live = Scope.LiveCount;
            ScopeHandle root = Scope.Create(() =>
            {
                Scope.OnCleanup(() => order.Add("r1"));
                _ = Scope.Create(() =>
                {
                    Scope.OnCleanup(() => order.Add("a1"));
                    Scope.OnCleanup(() => order.Add("a2"));
                });
                Scope.OnCleanup(() => order.Add("r2"));
                _ = Scope.Create(() =>
                {
                    Scope.OnCleanup(() => order.Add("b1"));
                    _ = Scope.Create(() => Scope.OnCleanup(() => order.Add("b11")));
                });
            });

            Scope.Dispose(root);

            Assert.Equal(["b11", "b1", "a2", "a1", "r2", "r1"], order);
            Assert.Equal(live, Scope.LiveCount);
            Assert.False(Scope.IsAlive(root));
        });

    /// <summary>
    /// A cleanup that throws does not stop the others: all of them run, and the exceptions come together at the end.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A scope with cleanups c1, c2 (throws "two"), c3 (throws "three"), registered in that order.
    ///
    /// Steps:
    /// - Dispose it.
    ///
    /// Expected:
    /// - <see cref="AggregateException"/> with "three" then "two" (the order they ran); c1 ran; the scope is disposed.
    /// </remarks>
    [Fact]
    public void Dispose_CleanupThrows_RunsTheRestAndAggregates() =>
        ui.Invoke(() =>
        {
            bool c1 = false;
            ScopeHandle scope = Scope.Create(() =>
            {
                Scope.OnCleanup(() => c1 = true);
                Scope.OnCleanup(() => throw new InvalidOperationException("two"));
                Scope.OnCleanup(() => throw new InvalidOperationException("three"));
            });

            AggregateException error = Assert.Throws<AggregateException>(() => Scope.Dispose(scope));

            Assert.Equal(["three", "two"], error.InnerExceptions.Select(e => e.Message));
            Assert.True(c1);
            Assert.False(Scope.IsAlive(scope));
        });

    /// <summary>
    /// An effect owns what it makes while it runs: before it runs again, its cleanups run and its child scopes are disposed.
    /// An effect made inside a scope is disposed with the scope.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A signal; a scope that makes an effect, which reads the signal, makes a child scope, and registers a cleanup.
    ///
    /// Steps:
    /// - Set the signal; then dispose the scope; then set the signal again.
    ///
    /// Expected:
    /// - After the set: the first run's cleanup ran once and its child scope is disposed; the effect ran twice and has a new
    ///   child scope.
    /// - After the dispose: the second run's cleanup ran and its child is disposed; the last set does not run the effect.
    /// </remarks>
    [Fact]
    public void Effect_OwnsWhatItMakesAndIsDisposedWithItsScope() =>
        ui.Invoke(() =>
        {
            Signal<int> value = Signal.Create(0);
            var children = new List<ScopeHandle>();
            int runs = 0;
            int cleanups = 0;
            ScopeHandle scope = Scope.Create(() =>
                Effect.Create(() =>
                {
                    runs++;
                    _ = value.Get();
                    children.Add(Scope.Create(() => { }));
                    Scope.OnCleanup(() => cleanups++);
                })
            );

            value.Set(1);
            Assert.Equal((2, 1), (runs, cleanups));
            Assert.False(Scope.IsAlive(children[0]));
            Assert.True(Scope.IsAlive(children[1]));

            Scope.Dispose(scope);
            Assert.Equal(2, cleanups);
            Assert.False(Scope.IsAlive(children[1]));

            value.Set(2);
            Assert.Equal(2, runs);
            Assert.Equal(0, value.ObserverCount);
        });

    /// <summary>
    /// An effect made outside any scope belongs to nothing and is stopped by its own Dispose, which disposes what it owns.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - No current scope. An effect that reads a signal and registers a cleanup.
    ///
    /// Steps:
    /// - Dispose the effect; set the signal.
    ///
    /// Expected:
    /// - The cleanup ran once; the effect ran only once; the signal has no observers; no scope is left alive.
    /// </remarks>
    [Fact]
    public void Effect_OutsideAnyScope_DisposeStopsItAndRunsItsCleanups() =>
        ui.Invoke(() =>
        {
            Signal<int> value = Signal.Create(0);
            int live = Scope.LiveCount;
            int runs = 0;
            int cleanups = 0;
            Effect effect = Effect.Create(() =>
            {
                runs++;
                _ = value.Get();
                Scope.OnCleanup(() => cleanups++);
            });

            effect.Dispose();
            value.Set(1);

            Assert.Equal((1, 1), (runs, cleanups));
            Assert.Equal(0, value.ObserverCount);
            Assert.Equal(live, Scope.LiveCount);
        });

    /// <summary>
    /// When the body of Create throws, the scope is disposed with what it made so far, and the exception goes on.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A body that registers a cleanup, makes a child scope, and throws.
    ///
    /// Steps:
    /// - Call Create with it.
    ///
    /// Expected:
    /// - The body's exception; the cleanup ran; no scope is left alive.
    /// </remarks>
    [Fact]
    public void Create_BodyThrows_DisposesTheScope() =>
        ui.Invoke(() =>
        {
            int live = Scope.LiveCount;
            bool cleaned = false;

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                Scope.Create(() =>
                {
                    Scope.OnCleanup(() => cleaned = true);
                    _ = Scope.Create(() => { });
                    throw new InvalidOperationException("body");
                })
            );

            Assert.Equal("body", error.Message);
            Assert.True(cleaned);
            Assert.Equal(live, Scope.LiveCount);
        });

    /// <summary>
    /// A cleanup needs a current scope, and a computed value makes no scopes, effects, or cleanups (it has no side effects).
    /// Disposing a scope twice, or a stale handle, does nothing.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - No current scope.
    ///
    /// Steps:
    /// - Call OnCleanup; read computed values that call OnCleanup, Scope.Create, and Effect.Create; dispose a scope twice and
    ///   dispose default.
    ///
    /// Expected:
    /// - <see cref="InvalidOperationException"/> from OnCleanup and from each Get; the disposes do nothing.
    /// </remarks>
    [Fact]
    public void Misuse_Throws_AndRepeatedDisposeDoesNothing() =>
        ui.Invoke(() =>
        {
            Assert.Throws<InvalidOperationException>(() => Scope.OnCleanup(() => { }));
            Assert.Throws<InvalidOperationException>(() =>
                Computed
                    .Create(() =>
                    {
                        Scope.OnCleanup(() => { });
                        return 0;
                    })
                    .Get()
            );
            Assert.Throws<InvalidOperationException>(() => Computed.Create(() => Scope.Create(() => { }).Index).Get());
            Assert.Throws<InvalidOperationException>(() => Computed.Create(() => Effect.Create(() => { }).SourceCount).Get());

            ScopeHandle scope = Scope.Create(() => { });
            Scope.Dispose(scope);
            Scope.Dispose(scope);
            Scope.Dispose(default);
            Assert.False(Scope.IsAlive(scope));
        });

    /// <summary>
    /// The tracker, while enabled, lists the live scopes and effects with their owners and whether they belong to a scope; a
    /// disposed one leaves the list, and nothing is recorded while it is off.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The tracker enabled with stack traces.
    ///
    /// Steps:
    /// - Make an effect outside any scope, and a scope (owner "screen") that makes an effect; list; dispose both; list. Then
    ///   disable the tracker, make a scope, and list.
    ///
    /// Expected:
    /// - The first list: the effect (owner: this test's name, IsEffect, not owned), the scope ("screen", not an effect, not
    ///   owned), and its effect (IsEffect, owned), in that order, with stack traces.
    /// - The second and third lists are empty.
    /// </remarks>
    [Fact]
    public void Tracker_ListsLiveScopesAndUnownedEffects() =>
        ui.Invoke(() =>
        {
            ScopeTracker.IsEnabled = true;
            ScopeTracker.CaptureStackTrace = true;
            try
            {
                Effect loose = Effect.Create(() => { });
                ScopeHandle screen = Scope.Create(() => Effect.Create(() => { }), owner: "screen");

                TrackedScope[] live = List();
                loose.Dispose();
                Scope.Dispose(screen);
                TrackedScope[] after = List();
                ScopeTracker.IsEnabled = false;
                ScopeHandle untracked = Scope.Create(() => { });
                TrackedScope[] off = List();
                Scope.Dispose(untracked);

                Assert.Equal(
                    [
                        (nameof(Tracker_ListsLiveScopesAndUnownedEffects), true, false),
                        ("screen", false, false),
                        (nameof(Tracker_ListsLiveScopesAndUnownedEffects), true, true),
                    ],
                    live.Select(s => (s.Owner, s.IsEffect, s.IsOwned))
                );
                Assert.All(live, s => Assert.False(string.IsNullOrEmpty(s.StackTrace)));
                Assert.Empty(after);
                Assert.Empty(off);
            }
            finally
            {
                ScopeTracker.IsEnabled = false;
                ScopeTracker.CaptureStackTrace = false;
            }
        });

    /// <summary>
    /// Creating and disposing scopes in steady state allocates nothing beyond the delegates passed in: with static lambdas,
    /// nothing at all.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Static lambdas only. A warm-up of 100 rounds, so that the arenas have grown.
    ///
    /// Steps:
    /// - 1,000 rounds: create a scope that registers two cleanups and makes a child scope with one cleanup, then dispose it.
    ///   Measure the bytes allocated on this thread.
    ///
    /// Expected:
    /// - 0 bytes; the cleanups ran 3,000 times.
    /// </remarks>
    [Fact]
    public void SteadyState_CreateAndDispose_AllocatesNothing() =>
        ui.Invoke(() =>
        {
            for (int i = 0; i < 100; i++)
            {
                Round();
            }

            long ran = _sink;
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1_000; i++)
            {
                Round();
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.Equal(0, allocated);
            Assert.Equal(ran + 3_000, _sink);
        });

    private static void Round()
    {
        ScopeHandle scope = Scope.Create(static () =>
        {
            Scope.OnCleanup(static () => _sink++);
            _ = Scope.Create(static () => Scope.OnCleanup(static () => _sink++));
            Scope.OnCleanup(static () => _sink++);
        });
        Scope.Dispose(scope);
    }

    private static TrackedScope[] List()
    {
        var scopes = new List<TrackedScope>();
        ScopeTracker.ForEachActiveScope(scopes.Add);
        return [.. scopes];
    }
}
