using System.Runtime.CompilerServices;

namespace XylemUI.Tests;

// Computed values that leave their sources when nothing reads them (S9, design §2.2.2, the ownership of computed values).
[Collection(UiThreadGroup.Name)]
public sealed class ComputedOwnershipTests(UiThreadFixture ui)
{
    /// <summary>
    /// When its last reader is disposed, a computed value leaves its source: setting the source no longer computes it, and a
    /// read computes it once, then returns the kept value while nothing changes.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A signal s = 1, a computed value c = s × 2 that counts its computations, and an effect that reads c.
    ///
    /// Steps:
    /// - Dispose the effect; set s to 2 and to 3; read c twice; set an unrelated signal; read c.
    ///
    /// Expected:
    /// - After the dispose, s and c have no observers. The sets compute nothing (1 computation so far).
    /// - The first read returns 6 and computes once; the second read computes nothing; after the unrelated set, the read
    ///   returns 6 without computing (c's own source did not change).
    /// </remarks>
    [Fact]
    public void LastReaderGone_LeavesTheSourceAndStillReadsRight() =>
        ui.Invoke(() =>
        {
            Signal<int> s = Signal.Create(1);
            Signal<int> other = Signal.Create(0);
            int computations = 0;
            Computed<int> c = Computed.Create(() =>
            {
                computations++;
                return s.Get() * 2;
            });
            Effect reader = Effect.Create(() => _ = c.Get());
            Assert.Equal((1, 1), (s.ObserverCount, c.ObserverCount));

            reader.Dispose();
            Assert.Equal((0, 0), (s.ObserverCount, c.ObserverCount));
            s.Set(2);
            s.Set(3);
            Assert.Equal(1, computations);

            Assert.Equal(6, c.Get());
            Assert.Equal(6, c.Get());
            Assert.Equal(2, computations);
            other.Set(1);
            Assert.Equal(6, c.Get());
            Assert.Equal(2, computations);
        });

    /// <summary>
    /// A computed value that nothing read for a while registers with its sources again when an effect reads it, and their
    /// changes run the effect with the new value.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - s = 1, c = s + 10, read once outside any effect (so it has computed but has no readers); then s set to 2.
    ///
    /// Steps:
    /// - Make an effect that records c; set s to 3.
    ///
    /// Expected:
    /// - The effect saw 12, then 13; s has one observer (c), and c has one (the effect).
    /// </remarks>
    [Fact]
    public void ReadAgainByAnEffect_RegistersAgain() =>
        ui.Invoke(() =>
        {
            Signal<int> s = Signal.Create(1);
            Computed<int> c = Computed.Create(() => s.Get() + 10);
            Assert.Equal(11, c.Get());
            Assert.Equal(0, s.ObserverCount);
            s.Set(2);

            var seen = new List<int>();
            using Effect effect = Effect.Create(() => seen.Add(c.Get()));
            s.Set(3);

            Assert.Equal([12, 13], seen);
            Assert.Equal((1, 1), (s.ObserverCount, c.ObserverCount));
        });

    /// <summary>
    /// Leaving the sources goes down a chain: when the effect at the end goes, every computed value in the chain leaves its
    /// source, and a read of the end of the chain still gives the current value.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - s = 1, c1 = s + 1, c2 = c1 × 10, an effect that reads c2.
    ///
    /// Steps:
    /// - Dispose the effect; set s to 5; read c2.
    ///
    /// Expected:
    /// - s, c1, and c2 have no observers; c2 reads 60.
    /// </remarks>
    [Fact]
    public void Chain_LeavesItsSourcesAllTheWayDown() =>
        ui.Invoke(() =>
        {
            Signal<int> s = Signal.Create(1);
            Computed<int> c1 = Computed.Create(() => s.Get() + 1);
            Computed<int> c2 = Computed.Create(() => c1.Get() * 10);
            Effect effect = Effect.Create(() => _ = c2.Get());

            effect.Dispose();
            s.Set(5);

            Assert.Equal((0, 0, 0), (s.ObserverCount, c1.ObserverCount, c2.ObserverCount));
            Assert.Equal(60, c2.Get());
        });

    /// <summary>
    /// A computed value that nothing reads and nothing refers to is collected, though the signal it read is still alive.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A signal kept alive by the test. In a method that is not inlined: a computed value of the signal, read by an effect
    ///   that is then disposed; only a weak reference to the computed value comes back.
    ///
    /// Steps:
    /// - Collect garbage twice, with the finalizers in between.
    ///
    /// Expected:
    /// - The weak reference is dead; the signal has no observers.
    /// </remarks>
    [Fact]
    public void Unreferenced_IsCollected() =>
        ui.Invoke(() =>
        {
            Signal<int> s = Signal.Create(1);
            WeakReference computed = ReadOnceAndForget(s);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            Assert.False(computed.IsAlive);
            Assert.Equal(0, s.ObserverCount);
            GC.KeepAlive(s);
        });

    /// <summary>
    /// An effect that starts and stops reading a computed value, over and over, allocates nothing in steady state: the edges'
    /// arrays are reused.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - s, c = s + 1, a flag, and an effect that reads c only while the flag is set. A warm-up of 100 toggles.
    ///
    /// Steps:
    /// - 1,000 times: set the flag on, then off (c registers with s, then leaves it). Measure the bytes allocated on this thread.
    ///
    /// Expected:
    /// - 0 bytes; s has no observers at the end.
    /// </remarks>
    [Fact]
    public void SteadyState_StartAndStopReading_AllocatesNothing() =>
        ui.Invoke(() =>
        {
            Signal<int> s = Signal.Create(0);
            Signal<bool> flag = Signal.Create(false);
            Computed<int> c = Computed.Create(() => s.Get() + 1);
            using Effect effect = Effect.Create(() =>
            {
                if (flag.Get())
                {
                    _ = c.Get();
                }
            });
            for (int i = 0; i < 100; i++)
            {
                flag.Set(true);
                flag.Set(false);
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1_000; i++)
            {
                flag.Set(true);
                flag.Set(false);
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.Equal(0, allocated);
            Assert.Equal(0, s.ObserverCount);
        });

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference ReadOnceAndForget(Signal<int> s)
    {
        Computed<int> c = Computed.Create(() => s.Get() * 2);
        Effect effect = Effect.Create(() => _ = c.Get());
        effect.Dispose();
        return new WeakReference(c);
    }
}
