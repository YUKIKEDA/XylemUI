namespace XylemUI.Tests;

// The OS's appearance (row TH1, design §13.2, "OS の見た目"): its key has no default, and its default value is the one of a host
// that does not read the OS.
[Collection(UiThreadGroup.Name)]
public sealed class SystemAppearanceTests(UiThreadFixture ui)
{
    /// <summary>
    /// The key of the OS's appearance has no default: a lookup outside a window throws, and finds no value.
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
                thrown = Record.Exception(() => SystemAppearance.Current.Use());
                found = SystemAppearance.Current.TryUse(out _);
            });
            Scope.Dispose(scope);

            Assert.IsType<InvalidOperationException>(thrown);
            Assert.False(found);
        });

    /// <summary>
    /// The default value is what a host that does not read the OS gives (design §13.2, "macOS と Linux"): light, with high
    /// contrast off, no accent color, and no high-contrast colors.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - None.
    ///
    /// Steps:
    /// - Read the members of <c>default(SystemAppearance)</c>.
    ///
    /// Expected:
    /// - Light, <see langword="false"/>, <see langword="null"/>, <see langword="null"/>.
    /// </remarks>
    [Fact]
    public void Default_IsLight_WithNoHighContrastAndNoAccent()
    {
        SystemAppearance value = default;

        Assert.Equal(ColorScheme.Light, value.Scheme);
        Assert.False(value.HighContrast);
        Assert.Null(value.Accent);
        Assert.Null(value.Contrast);
    }
}
