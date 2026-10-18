namespace XylemUI.Tests;

// Dependency edges and their upkeep (S6a, design §2.2.2, the diff of dependency edges).
[Collection(UiThreadGroup.Name)]
public sealed class ReactiveDependencyTests(UiThreadFixture ui)
{
    private static long _sink;

    /// <summary>
    /// When a branch changes what an effect reads, only the edges from the changed position on are relinked.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Signals shared, flag, a, b. Effect E1 reads shared, flag, then a or b by flag; effect E2 reads shared after E1, so E1
    ///   is shared's observer 0 and E2 is observer 1.
    ///
    /// Steps:
    /// - Set flag so that E1 reads b instead of a.
    /// - Set a; set b.
    ///
    /// Expected:
    /// - E1 is still shared's observer 0 (relinking every edge would have moved it behind E2).
    /// - E1's sources are shared, flag, b; a has no observers and setting it does not run E1; setting b does.
    /// </remarks>
    [Fact]
    public void BranchChange_RelinksOnlyFromTheChangedPosition() =>
        ui.Invoke(() =>
        {
            Signal<int> shared = Signal.Create(0);
            Signal<bool> flag = Signal.Create(true);
            Signal<int> a = Signal.Create(0);
            Signal<int> b = Signal.Create(0);
            int runs = 0;
            using Effect e1 = Effect.Create(() =>
            {
                runs++;
                _ = shared.Get();
                _ = flag.Get() ? a.Get() : b.Get();
            });
            using Effect e2 = Effect.Create(() => _ = shared.Get());

            flag.Set(false);

            Assert.Same(e1, shared.Observers[0].Observer);
            Assert.Same(e2, shared.Observers[1].Observer);
            Assert.Equal(3, e1.SourceCount);
            Assert.Same(shared, e1.Sources[0].Source);
            Assert.Same(flag, e1.Sources[1].Source);
            Assert.Same(b, e1.Sources[2].Source);
            Assert.Equal(0, a.ObserverCount);
            Assert.Equal(1, b.ObserverCount);

            int runsBefore = runs;
            a.Set(1);
            Assert.Equal(runsBefore, runs);
            b.Set(1);
            Assert.Equal(runsBefore + 1, runs);
        });

    /// <summary>
    /// Removing an edge from the middle of a source's observers keeps every other edge's slot right.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A signal read by three effects, E1, E2, E3 (observers 0, 1, 2).
    ///
    /// Steps:
    /// - Dispose E1; set the signal.
    ///
    /// Expected:
    /// - The signal has two observers, E3 moved into slot 0, and each remaining effect's edge points back at its slot.
    /// - E2 and E3 both ran again.
    /// </remarks>
    [Fact]
    public void EdgeRemoval_SwapsTheLastEdgeInAndKeepsSlots() =>
        ui.Invoke(() =>
        {
            Signal<int> source = Signal.Create(0);
            int runs2 = 0;
            int runs3 = 0;
            var e1 = Effect.Create(() => _ = source.Get());
            using Effect e2 = Effect.Create(() =>
            {
                _ = source.Get();
                runs2++;
            });
            using Effect e3 = Effect.Create(() =>
            {
                _ = source.Get();
                runs3++;
            });

            e1.Dispose();
            source.Set(1);

            Assert.Equal(2, source.ObserverCount);
            Assert.Same(e3, source.Observers[0].Observer);
            Assert.Same(e2, source.Observers[1].Observer);
            foreach (ObserverEdge edge in source.Observers)
            {
                Assert.Same(source, edge.Observer.Sources[edge.SlotInObserver].Source);
            }

            Assert.Equal(0, e3.Sources[0].SlotInSource);
            Assert.Equal(1, e2.Sources[0].SlotInSource);
            Assert.Equal((2, 2), (runs2, runs3));
        });

    /// <summary>
    /// In steady state, a set and the flush it causes allocate nothing.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A signal, a computed value of it, and an effect that reads both and adds to a static field. Warmed up with 100 sets.
    ///
    /// Steps:
    /// - Set the signal 10,000 times, measuring the bytes allocated on this thread.
    ///
    /// Expected:
    /// - 0 bytes, and the effect ran for every set.
    /// </remarks>
    [Fact]
    public void SteadyState_SetAndFlush_AllocateNothing() =>
        ui.Invoke(() =>
        {
            const int Sets = 10_000;
            Signal<int> source = Signal.Create(0);
            Computed<int> doubled = Computed.Create(() => source.Get() * 2);
            int runs = 0;
            using Effect effect = Effect.Create(() =>
            {
                _sink += source.Get() + doubled.Get();
                runs++;
            });
            for (int i = 1; i <= 100; i++)
            {
                source.Set(i);
            }

            int runsBefore = runs;
            SteadyStateAllocation.AssertNone(Sets, i => source.Set(101 + i));

            Assert.True(runs >= runsBefore + Sets);
        });

    /// <summary>
    /// A computed value that reads itself throws instead of recursing.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A computed value whose function reads the computed value itself.
    ///
    /// Steps:
    /// - Read it.
    ///
    /// Expected:
    /// - The read throws <see cref="InvalidOperationException"/>, which the computed value keeps and throws again.
    /// </remarks>
    [Fact]
    public void Computed_ReadsItself_Throws() =>
        ui.Invoke(() =>
        {
            Computed<int>? self = null;
            self = Computed.Create(() => self!.Get() + 1);

            InvalidOperationException first = Assert.Throws<InvalidOperationException>(() => self.Get());
            InvalidOperationException second = Assert.Throws<InvalidOperationException>(() => self.Get());
            Assert.Same(first, second);
        });
}
