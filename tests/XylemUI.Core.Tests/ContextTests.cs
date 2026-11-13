namespace XylemUI.Tests;

// The reactive context (RC1, design §2.13): a value provided around a body, found through the scopes, nearest first.
[Collection(UiThreadGroup.Name)]
public sealed class ContextTests(UiThreadFixture ui)
{
    /// <summary>
    /// A value provided around a body is found by a lookup inside the body, also from scopes nested in it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A key with no default.
    ///
    /// Steps:
    /// - In a root scope, provide "a" around a body that looks the key up directly and from a scope nested two deep.
    ///
    /// Expected:
    /// - Both lookups return "a".
    /// </remarks>
    [Fact]
    public void Use_InsideTheBody_ReturnsTheProvidedValue() =>
        ui.Invoke(() =>
        {
            var key = new Context<string>();
            string? direct = null;
            string? nested = null;
            ScopeHandle root = Scope.Create(() =>
                _ = key.Provide(
                    "a",
                    () =>
                    {
                        direct = key.Use();
                        _ = Scope.Create(() => _ = Scope.Create(() => nested = key.Use()));
                        return default;
                    }
                )
            );

            Assert.Equal("a", direct);
            Assert.Equal("a", nested);
            Scope.Dispose(root);
        });

    /// <summary>
    /// When values of one key are nested, the nearest wins, and the outer value is back after the inner body.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A key with no default.
    ///
    /// Steps:
    /// - Provide "outer" around a body that provides "inner" around a second body. Look the key up in the second body, and in the
    ///   first body after the second returned.
    ///
    /// Expected:
    /// - The second body sees "inner"; the first body sees "outer".
    /// </remarks>
    [Fact]
    public void Use_NestedValuesOfOneKey_ReturnsTheNearest() =>
        ui.Invoke(() =>
        {
            var key = new Context<string>();
            string? inner = null;
            string? after = null;
            ScopeHandle root = Scope.Create(() =>
                _ = key.Provide(
                    "outer",
                    () =>
                    {
                        _ = key.Provide(
                            "inner",
                            () =>
                            {
                                inner = key.Use();
                                return default;
                            }
                        );
                        after = key.Use();
                        return default;
                    }
                )
            );

            Assert.Equal("inner", inner);
            Assert.Equal("outer", after);
            Scope.Dispose(root);
        });

    /// <summary>
    /// Two keys of the same type hold two values: the key is the object, not the type.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Two keys of <see cref="int"/>, with no default.
    ///
    /// Steps:
    /// - Provide 1 for the first key around a body that provides 2 for the second key, and look both up inside.
    ///
    /// Expected:
    /// - The first key gives 1 and the second gives 2.
    /// </remarks>
    [Fact]
    public void Use_TwoKeysOfOneType_KeepsTheirValuesApart() =>
        ui.Invoke(() =>
        {
            var first = new Context<int>();
            var second = new Context<int>();
            (int First, int Second) seen = default;
            ScopeHandle root = Scope.Create(() =>
                _ = first.Provide(
                    1,
                    () =>
                        second.Provide(
                            2,
                            () =>
                            {
                                seen = (first.Use(), second.Use());
                                return default;
                            }
                        )
                )
            );

            Assert.Equal((1, 2), seen);
            Scope.Dispose(root);
        });

    /// <summary>
    /// Where no value is provided, a key with a default gives the default; a key without one throws from <c>Use</c> and returns
    /// <see langword="false"/> from <c>TryUse</c>.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A key with the default 7 and a key with no default. Nothing is provided.
    ///
    /// Steps:
    /// - In a root scope, call <c>Use</c> and <c>TryUse</c> on both.
    ///
    /// Expected:
    /// - The first key gives 7 from both, <c>TryUse</c> returning <see langword="true"/>. The second key throws
    ///   <see cref="InvalidOperationException"/> from <c>Use</c> and returns <see langword="false"/> from <c>TryUse</c>.
    /// </remarks>
    [Fact]
    public void Use_NothingProvided_GivesTheDefaultOrThrows() =>
        ui.Invoke(() =>
        {
            var withDefault = new Context<int>(7);
            var without = new Context<int>();
            ScopeHandle root = Scope.Create(() =>
            {
                Assert.Equal(7, withDefault.Use());
                Assert.True(withDefault.TryUse(out int value));
                Assert.Equal(7, value);
                _ = Assert.Throws<InvalidOperationException>(() => without.Use());
                Assert.False(without.TryUse(out _));
            });
            Scope.Dispose(root);
        });

    /// <summary>
    /// A provided value reaches only what the body makes: code of the same scope before and after the body does not see it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A key with the default "default".
    ///
    /// Steps:
    /// - In a root scope, look the key up, provide "a" around an empty body, and look the key up again.
    ///
    /// Expected:
    /// - Both lookups give "default".
    /// </remarks>
    [Fact]
    public void Provide_OutsideTheBody_DoesNotReach() =>
        ui.Invoke(() =>
        {
            var key = new Context<string>("default");
            string? before = null;
            string? after = null;
            ScopeHandle root = Scope.Create(() =>
            {
                before = key.Use();
                _ = key.Provide("a", static () => default);
                after = key.Use();
            });

            Assert.Equal("default", before);
            Assert.Equal("default", after);
            Scope.Dispose(root);
        });

    /// <summary>
    /// <c>Provide</c> returns the element its body made, and the scope it made goes with the scope it was called in.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A tree to build in, and a key with no default.
    ///
    /// Steps:
    /// - In a root scope, provide a value around a body that makes a column, then dispose the root scope.
    ///
    /// Expected:
    /// - <c>Provide</c> returned the body's element. While the root lives, there is one more scope than the root; after the
    ///   dispose, the number of live scopes is what it was at the start.
    /// </remarks>
    [Fact]
    public void Provide_ReturnsTheElementOfTheBody_AndItsScopeGoesWithTheParent() =>
        ui.Invoke(() =>
        {
            var key = new Context<int>();
            UiTree.Current = new UiTree();
            try
            {
                int live = Scope.LiveCount;
                Element made = default;
                Element returned = default;
                ScopeHandle root = Scope.Create(() =>
                    returned = key.Provide(
                        1,
                        () =>
                        {
                            made = Elements.Column();
                            return made;
                        }
                    )
                );

                Assert.Equal(made, returned);
                Assert.Equal(live + 2, Scope.LiveCount);
                Scope.Dispose(root);
                Assert.Equal(live, Scope.LiveCount);
            }
            finally
            {
                UiTree.Current = null;
            }
        });

    /// <summary>
    /// A value's place is free again after its scope is disposed: provided values do not pile up.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A key of a reference type, and an object held only weakly by the test.
    ///
    /// Steps:
    /// - Provide the object around an empty body in a root scope, dispose the root scope, and collect garbage.
    ///
    /// Expected:
    /// - The object was collected: neither the key nor the scopes keep it.
    /// </remarks>
    [Fact]
    public void Provide_ScopeDisposed_DropsTheValue() =>
        ui.Invoke(() =>
        {
            var key = new Context<object>();
            WeakReference weak = ProvideAndDispose(key);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            Assert.False(weak.IsAlive);
            GC.KeepAlive(key);
        });

    /// <summary>
    /// When the body throws, the exception goes on and the scope of the value is disposed.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A key with the default 0.
    ///
    /// Steps:
    /// - In a root scope, provide 1 around a body that registers a cleanup and throws. Then look the key up in the root scope.
    ///
    /// Expected:
    /// - The body's <see cref="InvalidOperationException"/> comes out of <c>Provide</c>, the cleanup ran, the root scope is
    ///   current again (the lookup gives 0), and only the root scope is left alive.
    /// </remarks>
    [Fact]
    public void Provide_BodyThrows_DisposesItsScope() =>
        ui.Invoke(() =>
        {
            var key = new Context<int>(0);
            int live = Scope.LiveCount;
            bool cleaned = false;
            int after = -1;
            ScopeHandle root = Scope.Create(() =>
            {
                InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                    key.Provide(
                        1,
                        () =>
                        {
                            Scope.OnCleanup(() => cleaned = true);
                            throw new InvalidOperationException("body");
                        }
                    )
                );
                Assert.Equal("body", error.Message);
                after = key.Use();
            });

            Assert.True(cleaned);
            Assert.Equal(0, after);
            Assert.Equal(live + 1, Scope.LiveCount);
            Scope.Dispose(root);
        });

    /// <summary>
    /// An effect looks a key up from where it was made, on its first run and on every later run.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A key with the default "default", and a signal the effect reads.
    ///
    /// Steps:
    /// - Provide "a" around a body that makes an effect which reads the signal and looks the key up. Then set the signal from
    ///   outside every scope, so that the effect runs again.
    ///
    /// Expected:
    /// - The effect saw "a" both times.
    /// </remarks>
    [Fact]
    public void Use_InsideAnEffect_LooksUpFromWhereTheEffectWasMade() =>
        ui.Invoke(() =>
        {
            var key = new Context<string>("default");
            Signal<int> signal = Signal.Create(0);
            var seen = new List<string>();
            ScopeHandle root = Scope.Create(() =>
                _ = key.Provide(
                    "a",
                    () =>
                    {
                        _ = Effect.Create(() =>
                        {
                            _ = signal.Get();
                            seen.Add(key.Use());
                        });
                        return default;
                    }
                )
            );

            signal.Set(1);

            Assert.Equal(["a", "a"], seen);
            Scope.Dispose(root);
        });

    /// <summary>
    /// After an <c>await</c>, a lookup inside <see cref="Scope.Run"/> with the scope kept before finds the value.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A key with no default; "a" provided around a body that keeps <see cref="Scope.Current"/>.
    ///
    /// Steps:
    /// - After the body returned, outside every scope, look the key up inside <see cref="Scope.Run"/> with the kept scope.
    ///
    /// Expected:
    /// - The lookup gives "a".
    /// </remarks>
    [Fact]
    public void Use_InsideRunWithAKeptScope_FindsTheValue() =>
        ui.Invoke(() =>
        {
            var key = new Context<string>();
            ScopeHandle kept = default;
            ScopeHandle root = Scope.Create(() =>
                _ = key.Provide(
                    "a",
                    () =>
                    {
                        kept = Scope.Current;
                        return default;
                    }
                )
            );
            string? seen = null;

            bool ran = Scope.Run(kept, () => seen = key.Use());

            Assert.True(ran);
            Assert.Equal("a", seen);
            Scope.Dispose(root);
        });

    /// <summary>
    /// With no current scope, <c>Use</c> and <c>TryUse</c> throw, also for a key with a default: a default there would hide a
    /// value an ancestor provided.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A key with the default 7. No scope is current.
    ///
    /// Steps:
    /// - Call <c>Use</c> and <c>TryUse</c>.
    ///
    /// Expected:
    /// - Both throw <see cref="InvalidOperationException"/>.
    /// </remarks>
    [Fact]
    public void Use_NoCurrentScope_Throws() =>
        ui.Invoke(() =>
        {
            var key = new Context<int>(7);

            Assert.True(Scope.Current.IsNone);
            _ = Assert.Throws<InvalidOperationException>(() => key.Use());
            _ = Assert.Throws<InvalidOperationException>(() => key.TryUse(out _));
        });

    /// <summary>
    /// An effect made with no current scope has a scope of its own, but a lookup inside it throws as with no scope, and so does
    /// a lookup from a scope made inside that effect.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A key with the default 7. No scope is current.
    ///
    /// Steps:
    /// - Make an effect that records the exception of <c>Use</c>, and of <c>Use</c> inside a scope it makes.
    ///
    /// Expected:
    /// - Both lookups threw <see cref="InvalidOperationException"/>.
    /// </remarks>
    [Fact]
    public void Use_InsideAnEffectMadeWithNoScope_Throws() =>
        ui.Invoke(() =>
        {
            var key = new Context<int>(7);
            Exception? direct = null;
            Exception? nested = null;

            Effect effect = Effect.Create(() =>
            {
                direct = Record.Exception(() => key.Use());
                _ = Scope.Create(() => nested = Record.Exception(() => key.Use()));
            });

            _ = Assert.IsType<InvalidOperationException>(direct);
            _ = Assert.IsType<InvalidOperationException>(nested);
            effect.Dispose();
        });

    /// <summary>
    /// A value provided inside an effect made with no current scope is found inside it; only a lookup that finds nothing
    /// throws.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Two keys with the defaults "p" and "q". No scope is current.
    ///
    /// Steps:
    /// - Make an effect that provides "a" for the first key around a body that looks up the first key, and records the exception
    ///   of looking up the second.
    ///
    /// Expected:
    /// - The first lookup gives "a"; the second throws <see cref="InvalidOperationException"/>.
    /// </remarks>
    [Fact]
    public void Use_InsideAnEffectMadeWithNoScope_FindsWhatWasProvidedInside() =>
        ui.Invoke(() =>
        {
            var provided = new Context<string>("p");
            var other = new Context<string>("q");
            string? found = null;
            Exception? missing = null;

            Effect effect = Effect.Create(() =>
                _ = provided.Provide(
                    "a",
                    () =>
                    {
                        found = provided.Use();
                        missing = Record.Exception(() => other.Use());
                        return default;
                    }
                )
            );

            Assert.Equal("a", found);
            _ = Assert.IsType<InvalidOperationException>(missing);
            effect.Dispose();
        });

    /// <summary>
    /// A scope made under a parent that is being disposed belongs to nothing: a lookup in it that finds no value throws.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A key with the default 7, provided as 1 around a body that registers a cleanup. The cleanup makes a scope and records
    ///   the exception of a lookup inside it.
    ///
    /// Steps:
    /// - Dispose the root scope, so that the cleanup runs while its scope is being disposed.
    ///
    /// Expected:
    /// - The lookup threw <see cref="InvalidOperationException"/>: it gave neither 1 nor the default.
    /// </remarks>
    [Fact]
    public void Use_InsideAScopeMadeUnderADisposedParent_Throws() =>
        ui.Invoke(() =>
        {
            var key = new Context<int>(7);
            Exception? error = null;
            ScopeHandle kept = default;
            ScopeHandle root = Scope.Create(() =>
                _ = key.Provide(
                    1,
                    () =>
                    {
                        kept = Scope.Current;
                        Scope.OnCleanup(() =>
                        {
                            var previous = Scope.CurrentRef;
                            Scope.CurrentRef = kept.Value;
                            try
                            {
                                ScopeHandle made = Scope.Create(() => error = Record.Exception(() => key.Use()));
                                Scope.Dispose(made);
                            }
                            finally
                            {
                                Scope.CurrentRef = previous;
                            }
                        });
                        return default;
                    }
                )
            );

            Scope.Dispose(root);

            _ = Assert.IsType<InvalidOperationException>(error);
        });

    /// <summary>
    /// A root scope made with no current scope is not such an effect: a lookup in it gives the key's default.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A key with the default 7. No scope is current.
    ///
    /// Steps:
    /// - Make a root scope and, inside it, an effect; look the key up in both.
    ///
    /// Expected:
    /// - Both give 7.
    /// </remarks>
    [Fact]
    public void Use_InsideARootScope_GivesTheDefault() =>
        ui.Invoke(() =>
        {
            var key = new Context<int>(7);
            int inScope = 0;
            int inEffect = 0;
            ScopeHandle root = Scope.Create(() =>
            {
                inScope = key.Use();
                _ = Effect.Create(() => inEffect = key.Use());
            });

            Assert.Equal(7, inScope);
            Assert.Equal(7, inEffect);
            Scope.Dispose(root);
        });

    /// <summary>
    /// While a computed value computes, <c>Use</c> and <c>TryUse</c> throw: a computed value has no scope of its own, so the
    /// lookup would start from whoever reads it first.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A key with "a" provided around a body that makes two computed values, one calling <c>Use</c> and one <c>TryUse</c>, and
    ///   reads both.
    ///
    /// Steps:
    /// - Read each computed value inside the body.
    ///
    /// Expected:
    /// - Each read throws <see cref="InvalidOperationException"/>.
    /// </remarks>
    [Fact]
    public void Use_InsideAComputed_Throws() =>
        ui.Invoke(() =>
        {
            var key = new Context<string>();
            Exception? use = null;
            Exception? tryUse = null;
            ScopeHandle root = Scope.Create(() =>
                _ = key.Provide(
                    "a",
                    () =>
                    {
                        Computed<string> first = Computed.Create(() => key.Use());
                        Computed<bool> second = Computed.Create(() => key.TryUse(out _));
                        use = Record.Exception(() => first.Get());
                        tryUse = Record.Exception(() => second.Get());
                        return default;
                    }
                )
            );

            _ = Assert.IsType<InvalidOperationException>(use);
            _ = Assert.IsType<InvalidOperationException>(tryUse);
            Scope.Dispose(root);
        });

    /// <summary>
    /// <c>Provide</c>, <c>Use</c>, and <c>TryUse</c> throw on a thread that is not the UI thread; the constructor of a key does
    /// not.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The test's own thread, which is not the UI thread.
    ///
    /// Steps:
    /// - Make a key with a default, and call <c>Provide</c>, <c>Use</c>, and <c>TryUse</c> on it.
    ///
    /// Expected:
    /// - The constructor returns; the three calls throw <see cref="InvalidOperationException"/>.
    /// </remarks>
    [Fact]
    public void Calls_OffTheUiThread_Throw()
    {
        _ = ui;
        var key = new Context<int>(7);

        _ = Assert.Throws<InvalidOperationException>(() => key.Provide(1, static () => default));
        _ = Assert.Throws<InvalidOperationException>(() => key.Use());
        _ = Assert.Throws<InvalidOperationException>(() => key.TryUse(out _));
    }

    private static WeakReference ProvideAndDispose(Context<object> key)
    {
        object value = new();
        ScopeHandle root = Scope.Create(() => _ = key.Provide(value, static () => default));
        Scope.Dispose(root);
        return new WeakReference(value);
    }
}
