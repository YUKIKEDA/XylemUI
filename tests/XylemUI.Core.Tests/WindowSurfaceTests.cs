namespace XylemUI.Tests;

// The window's surface (row TH1, design §13.2, "根の箱" and "題名の棒"): what the root wrapper tells the host of the color behind
// the tree and of the title bar. Its key has no default.
[Collection(UiThreadGroup.Name)]
public sealed class WindowSurfaceTests(UiThreadFixture ui)
{
    /// <summary>
    /// The key of the window's surface has no default: a lookup outside a window throws, and finds no value.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A scope that provides nothing.
    ///
    /// Steps:
    /// - Look the key up with <c>Use</c> and with <c>TryUse</c> in the scope.
    ///
    /// Expected:
    /// - <c>Use</c> throws <see cref="InvalidOperationException"/>; <c>TryUse</c> returns <see langword="false"/>.
    /// </remarks>
    [Fact]
    public void Current_OutsideAWindow_HasNoValue() =>
        ui.Invoke(() =>
        {
            Exception? thrown = null;
            bool found = true;
            ScopeHandle scope = Scope.Create(() =>
            {
                thrown = Record.Exception(() => WindowSurface.Current.Use());
                found = WindowSurface.Current.TryUse(out _);
            });
            Scope.Dispose(scope);

            Assert.IsType<InvalidOperationException>(thrown);
            Assert.False(found);
        });

    /// <summary>
    /// The surface keeps the last color and the last title bar it was given, each apart from the other, and none before the first.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A new surface.
    ///
    /// Steps:
    /// - Read both values. Set the backdrop to #FAFAFA and read both. Set a dark title bar, then the backdrop to #292929, then a
    ///   light title bar, and read both after each.
    ///
    /// Expected:
    /// - (none, none); (#FAFAFA, none); (#FAFAFA, dark); (#292929, dark); (#292929, light).
    /// </remarks>
    [Fact]
    public void SetBackdropAndSetDarkTitleBar_KeepTheLastValueOfEach() =>
        ui.Invoke(() =>
        {
            var surface = new WindowSurface();
            var seen = new List<(Color? Backdrop, bool? Dark)> { (surface.Backdrop, surface.DarkTitleBar) };

            surface.SetBackdrop(Color.FromRgb(0xFAFAFA));
            seen.Add((surface.Backdrop, surface.DarkTitleBar));
            surface.SetDarkTitleBar(true);
            seen.Add((surface.Backdrop, surface.DarkTitleBar));
            surface.SetBackdrop(Color.FromRgb(0x292929));
            seen.Add((surface.Backdrop, surface.DarkTitleBar));
            surface.SetDarkTitleBar(false);
            seen.Add((surface.Backdrop, surface.DarkTitleBar));

            (Color? Backdrop, bool? Dark)[] expected =
            [
                (null, null),
                (Color.FromRgb(0xFAFAFA), null),
                (Color.FromRgb(0xFAFAFA), true),
                (Color.FromRgb(0x292929), true),
                (Color.FromRgb(0x292929), false),
            ];
            Assert.Equal(expected, seen);
        });

    /// <summary>
    /// The surface belongs to the UI thread: a call from another thread throws and changes nothing.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A new surface; the UI thread is the fixture's.
    ///
    /// Steps:
    /// - Set the backdrop and the title bar from a thread of the pool.
    ///
    /// Expected:
    /// - Both throw <see cref="InvalidOperationException"/>; the surface still has no value.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [Fact]
    public async Task SetBackdropAndSetDarkTitleBar_OffTheUiThread_Throw()
    {
        ui.Invoke(static () => Assert.True(UiThread.CheckAccess()));
        var surface = new WindowSurface();
        CancellationToken cancel = TestContext.Current.CancellationToken;

        // Method groups, not lambdas: the calls are what is tested off the UI thread (XYLEM100 reports a lambda).
        _ = await Assert.ThrowsAsync<InvalidOperationException>(() => Task.Run(SetBackdrop, cancel));
        _ = await Assert.ThrowsAsync<InvalidOperationException>(() => Task.Run(SetDarkTitleBar, cancel));

        Assert.Null(surface.Backdrop);
        Assert.Null(surface.DarkTitleBar);

        void SetBackdrop() => surface.SetBackdrop(Color.FromRgb(0x000000));
        void SetDarkTitleBar() => surface.SetDarkTitleBar(true);
    }
}
