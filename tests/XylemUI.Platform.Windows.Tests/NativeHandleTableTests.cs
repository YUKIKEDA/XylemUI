namespace XylemUI.Platform.Windows.Tests;

[Collection(UiThreadGroup.Name)]
public sealed class NativeHandleTableTests(UiThreadFixture ui)
{
    /// <summary>
    /// A registered id resolves to the same instance after a compacting collection has moved the heap.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An object registered in the table; only the table and the test hold it.
    ///
    /// Steps:
    /// - Force a blocking, compacting gen-2 collection.
    /// - Resolve the id.
    ///
    /// Expected:
    /// - The id resolves, and the result is the same instance that was registered.
    /// </remarks>
    [Fact]
    public void TryResolve_AfterCompactingCollection_ReturnsSameInstance() =>
        ui.Invoke(() =>
        {
            var target = new object();
            nint id = NativeHandleTable.Register(target);
            try
            {
                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);

                Assert.True(NativeHandleTable.TryResolve(id, out object? resolved));
                Assert.Same(target, resolved);
            }
            finally
            {
                NativeHandleTable.Release(id);
            }
        });

    /// <summary>
    /// A released id never resolves again, also after its slot is reused by a new registration.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An object registered in the table.
    ///
    /// Steps:
    /// - Release the id.
    /// - Register a second object, which reuses the free slot.
    /// - Resolve both ids.
    ///
    /// Expected:
    /// - The released id fails to resolve.
    /// - The second id differs from the first and resolves to the second object.
    /// </remarks>
    [Fact]
    public void TryResolve_ReleasedIdAfterSlotReuse_Fails() =>
        ui.Invoke(() =>
        {
            nint first = NativeHandleTable.Register(new object());
            NativeHandleTable.Release(first);

            var second = new object();
            nint secondId = NativeHandleTable.Register(second);
            try
            {
                Assert.NotEqual(first, secondId);
                Assert.False(NativeHandleTable.TryResolve(first, out object? _));
                Assert.True(NativeHandleTable.TryResolve(secondId, out object? resolved));
                Assert.Same(second, resolved);
            }
            finally
            {
                NativeHandleTable.Release(secondId);
            }
        });

    /// <summary>
    /// Zero, which is the GWLP_USERDATA default, and an id of another type do not resolve.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A string registered in the table.
    ///
    /// Steps:
    /// - Resolve zero as an object.
    /// - Resolve the string's id as a <see cref="Win32Window"/>.
    ///
    /// Expected:
    /// - Both fail.
    /// </remarks>
    [Fact]
    public void TryResolve_ZeroOrWrongType_Fails() =>
        ui.Invoke(() =>
        {
            nint id = NativeHandleTable.Register("not a window");
            try
            {
                Assert.False(NativeHandleTable.TryResolve(0, out object? _));
                Assert.False(NativeHandleTable.TryResolve(id, out Win32Window? _));
            }
            finally
            {
                NativeHandleTable.Release(id);
            }
        });
}
