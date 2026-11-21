namespace XylemUI.Testing;

/// <summary>
/// The window a <see cref="HeadlessApp"/> stands for, and how its virtual time moves (design §12.2, §12.4).
/// </summary>
public sealed record HeadlessOptions
{
    /// <summary>
    /// Gets the width of the window's client area in logical pixels; 800 by default.
    /// </summary>
    public float Width { get; init; } = 800;

    /// <summary>
    /// Gets the height of the window's client area in logical pixels; 600 by default.
    /// </summary>
    public float Height { get; init; } = 600;

    /// <summary>
    /// Gets the number of physical pixels to a logical one; 1 by default.
    /// </summary>
    public float Scale { get; init; } = 1;

    /// <summary>
    /// Gets how far one frame of <see cref="HeadlessApp.PumpAndSettle"/> advances the clock; 1/60 of a second by default.
    /// </summary>
    public TimeSpan FrameInterval { get; init; } = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 60);

    /// <summary>
    /// Gets what makes the renderer that draws the app's frames into pixels (design §12.3, "The plug-in"), such as
    /// <c>XylemUI.Testing.D3D11</c>'s WARP renderer; <see langword="null"/> by default, for an app that draws nothing. The app
    /// disposes the renderer.
    /// </summary>
    public Func<HeadlessRenderer>? Renderer { get; init; }

    /// <summary>
    /// Gets a value indicating whether the app acts as if the OS asked for less motion: animations jump to their end values (design
    /// §2.14, "Reduced motion"); <see langword="false"/> by default.
    /// </summary>
    public bool ReduceMotion { get; init; }

    /// <summary>
    /// Gets a value indicating whether a text field's caret blinks, every 530 ms of the virtual clock (design §4.9, "The caret");
    /// <see langword="false"/> by default, where the caret is always drawn: a blinking caret keeps a timer that
    /// <see cref="HeadlessApp.PumpAndSettle"/> waits for and that fails the end of the test (design §12.2). The caret's width and the
    /// selection's colors are always Windows' defaults (1, and the selection's colors of Windows 11's default theme).
    /// </summary>
    public bool CaretBlink { get; init; }

    /// <summary>
    /// Gets a value indicating whether animated images (GIF and WebP) play, on the virtual clock (design §5.0.4, "Headless");
    /// <see langword="false"/> by default, where an animated image shows its first frame: an image that plays keeps a timer that
    /// <see cref="HeadlessApp.PumpAndSettle"/> waits for, and one that plays without end never settles.
    /// </summary>
    public bool ImageAnimation { get; init; }

    /// <summary>
    /// Gets the most iterations <see cref="HeadlessApp.PumpAndSettle"/> runs before it throws; 1,000 by default.
    /// </summary>
    public int MaxIterations { get; init; } = 1_000;

    /// <summary>
    /// Gets a value indicating whether access keys are always underlined, as Windows' "underline access keys" setting
    /// (<c>SPI_GETKEYBOARDCUES</c>) says (design §13.1, "Access keys"); <see langword="false"/> by default, as Windows' is.
    /// </summary>
    public bool KeyboardCues { get; init; }

    /// <summary>
    /// Gets the keymap of the app's window (design §7, "Shortcuts and actions"); <see langword="null"/>, the default, gives the app
    /// an empty keymap of its own, so that the rows a test adds do not reach another test. A test of an app's own rows passes
    /// <see cref="XylemUI.Keymap.Default"/>.
    /// </summary>
    public Keymap? Keymap { get; init; }

    /// <summary>
    /// Gets the CLDR tables of the locales the app supports, as <c>WindowOptions.SupportedLocales</c> of a desktop window (design
    /// §4.11, "Where locales are declared").
    /// </summary>
    public IReadOnlyList<LocaleData> SupportedLocales { get; init; } = [];

    /// <summary>
    /// Gets what chooses the app's locale, as <c>WindowOptions.ResolveLocale</c> of a desktop window; <see langword="null"/> for
    /// <see cref="Localization.Resolve"/>. The function does not change the lists it is given: the second is the app's own.
    /// </summary>
    public Func<IReadOnlyList<Locale>, IReadOnlyList<Locale>, Locale>? ResolveLocale { get; init; }

    /// <summary>
    /// Gets the tags of the languages the app sees as the OS's, most preferred first; none by default, so that a test does not
    /// depend on the machine it runs on: the app's locale is then the locale of the first of <see cref="SupportedLocales"/>, or
    /// undetermined. <see cref="HeadlessApp.SetSystemLocales"/> changes them while the app runs.
    /// </summary>
    public IReadOnlyList<string> SystemLocales { get; init; } = [];

    /// <summary>
    /// Gets a value indicating whether the app sees the OS asking for times in 24-hour form when it starts: the first value of
    /// <see cref="Localization.Use24HourFormat"/> (design §4.11, "The key of the 24-hour form"); <see langword="false"/> by default,
    /// so that a test does not depend on the machine it runs on. <see cref="HeadlessApp.SetUse24HourFormat"/> changes it while the
    /// app runs.
    /// </summary>
    public bool Use24HourFormat { get; init; }

    /// <summary>
    /// Gets the appearance the app sees as the OS's (design §13.2, "ヘッドレス"): by default light, with high contrast off and no
    /// accent color, so that a test does not depend on the machine it runs on. <see cref="HeadlessApp.SetSystemAppearance"/>
    /// changes it while the app runs. A value whose high-contrast colors are there while its high contrast is off, or missing
    /// while it is on, makes <see cref="HeadlessApp.StartAsync"/> throw <see cref="ArgumentException"/>.
    /// </summary>
    public SystemAppearance SystemAppearance { get; init; }

    /// <summary>
    /// Gets the theme's function that wraps the app's root function, as the one an app's <c>[assembly: WindowTheme]</c> sets for
    /// its desktop windows (design §13.2, "ヘッドレスとテスト"), such as <c>FluentRoot.Wrap</c>: it runs inside the locale and the
    /// OS's appearance, is given a function that runs the root function, calls it exactly once, and returns the window's node; a
    /// second call, or none, makes the start throw <see cref="InvalidOperationException"/>. <see langword="null"/>, the default,
    /// runs the root function alone. The headless app never reads the wrapper of <see cref="WindowRoot"/>.
    /// </summary>
    public Func<Func<Element>, Element>? RootWrapper { get; init; }
}
