namespace XylemUI.Tests;

// The OS's appearance of one window (row TH1, design §13.2, "置き方"): a signal that changes only when the value does, provided around
// the root function, and refusing a value whose high contrast and high-contrast colors disagree.
[Collection(UiThreadGroup.Name)]
public sealed class WindowAppearanceTests(UiThreadFixture ui)
{
    private static readonly HighContrastColors Black = new(
        Color.FromRgb(0x000000),
        Color.FromRgb(0xFFFFFF),
        Color.FromRgb(0x75E9FC),
        Color.FromRgb(0xA6A6A6),
        Color.FromRgb(0x8EE3F0),
        Color.FromRgb(0x263B50),
        Color.FromRgb(0x000000),
        Color.FromRgb(0xFFFFFF)
    );

    /// <summary>
    /// <c>Update</c> changes the signal only when the value changes: the several notices one change of Windows sends run a reader
    /// once, and a change of any one member runs it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A window's appearance made with the default value, and an effect that records the value each time it runs.
    ///
    /// Steps:
    /// - Update with: the default again; dark; dark again; dark with the accent #0078D4; the same; dark with the accent #0078D5;
    ///   high contrast with the colors of the black theme; the same; the black theme with one color changed.
    ///
    /// Expected:
    /// - The effect ran 6 times: once at its start, then for dark, the first accent, the second accent, high contrast, and the
    ///   changed color; never for a value equal to the one before.
    /// </remarks>
    [Fact]
    public void Update_ChangesTheSignalOnlyWhenTheValueChanges() =>
        ui.Invoke(() =>
        {
            SystemAppearance light = default;
            var appearance = new WindowAppearance(light);
            var seen = new List<SystemAppearance>();
            ScopeHandle scope = Scope.Create(() => _ = Effect.Create(() => seen.Add(appearance.Value.Get())));
            var dark = new SystemAppearance(ColorScheme.Dark, false, null, null);
            SystemAppearance blue = dark with { Accent = Color.FromRgb(0x0078D4) };
            SystemAppearance otherBlue = dark with { Accent = Color.FromRgb(0x0078D5) };
            var contrast = new SystemAppearance(ColorScheme.Dark, true, null, Black);
            SystemAppearance otherContrast = contrast with { Contrast = Black with { Hotlight = Color.FromRgb(0xFFFF00) } };

            appearance.Update(light);
            appearance.Update(dark);
            appearance.Update(dark);
            appearance.Update(blue);
            appearance.Update(blue);
            appearance.Update(otherBlue);
            appearance.Update(contrast);
            appearance.Update(contrast);
            appearance.Update(otherContrast);
            Scope.Dispose(scope);

            SystemAppearance[] expected = [light, dark, blue, otherBlue, contrast, otherContrast];
            Assert.Equal(expected, seen);
        });

    /// <summary>
    /// A value whose high contrast and high-contrast colors disagree is refused when the appearance is made and when it is
    /// updated, and an update that is refused leaves the value as it was.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The two values that disagree: high contrast on with no colors, and off with the black theme's colors.
    ///
    /// Steps:
    /// - Make an appearance with each. Make one with the default value, and update it with each.
    ///
    /// Expected:
    /// - Each throws <see cref="ArgumentException"/> naming the parameter <c>value</c>; the made appearance still holds the default
    ///   value.
    /// </remarks>
    [Fact]
    public void ContrastColors_DisagreeingWithHighContrast_AreRefused() =>
        ui.Invoke(() =>
        {
            var onWithoutColors = new SystemAppearance(ColorScheme.Dark, true, null, null);
            var offWithColors = new SystemAppearance(ColorScheme.Dark, false, null, Black);
            SystemAppearance light = default;
            var appearance = new WindowAppearance(light);

            ArgumentException[] thrown =
            [
                Assert.Throws<ArgumentException>(() => new WindowAppearance(onWithoutColors)),
                Assert.Throws<ArgumentException>(() => new WindowAppearance(offWithColors)),
                Assert.Throws<ArgumentException>(() => appearance.Update(onWithoutColors)),
                Assert.Throws<ArgumentException>(() => appearance.Update(offWithColors)),
            ];

            Assert.All(thrown, exception => Assert.Equal("value", exception.ParamName));
            Assert.Equal(light, appearance.Value.Get());
        });
}
