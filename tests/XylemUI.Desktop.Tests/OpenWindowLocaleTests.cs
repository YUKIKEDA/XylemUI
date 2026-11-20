using TerraFX.Interop.Windows;
using XylemUI.Platform.Windows;
using static TerraFX.Interop.Windows.Windows;
using static XylemUI.Elements;

namespace XylemUI.Desktop.Tests;

// The locale keys of a real window (row LC2, design §4.11, "Distribution" and "The key of the 24-hour form"), opened as XylemApp.Run
// opens it (XylemApp.OpenWindow, through TestWindow): the tables of the declared locales resolved against Windows' languages, and
// Windows' 24-hour form, which the first mount takes from the host and a WM_SETTINGCHANGE reads again (design §6.0, "4a の形",
// "PR 5 までの OS の設定"). The readers of Win32Window stand for Windows' settings, so that the test does not change them.
[Collection(UiThreadGroup.Name)]
public sealed class OpenWindowLocaleTests(UiThreadFixture ui)
{
    /// <summary>
    /// A window opened as <c>XylemApp.Run</c> opens it provides the tables of its resolved locale and Windows' 24-hour form at its
    /// root, and a change of the 24-hour form that a <c>WM_SETTINGCHANGE</c> reads reaches the key.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Readers set by the test: Windows' languages are "ja-JP", and its time format is in 24-hour form. A hidden window on WARP
    ///   that declares en and ja, whose root function keeps the tables key's and the 24-hour key's signals. The readers are put
    ///   back after the test.
    ///
    /// Steps:
    /// - Read the two keys. Make the time format's reader return the 12-hour form, send <c>WM_SETTINGCHANGE</c> to the window, and
    ///   read the 24-hour key again.
    ///
    /// Expected:
    /// - The tables are <see cref="CldrLocales.Ja"/> itself, and the 24-hour form is on; after the message it is off.
    /// </remarks>
    [Fact]
    public void OpenWindow_ProvidesTheTablesAndWindows24HourForm() =>
        ui.Invoke(() =>
        {
            Func<string[]> previousLanguages = Win32Window.UiLanguagesReader;
            Func<bool, bool> previousTimeFormat = Win32Window.Use24HourFormatReader;
            try
            {
                bool use24HourForm = true;
                Win32Window.UiLanguagesReader = static () => ["ja-JP"];
                Win32Window.Use24HourFormatReader = _ => use24HourForm;
                IReadOnlySignal<LocaleData>? data = null;
                IReadOnlySignal<bool>? use24HourFormat = null;
                using var window = new TestWindow(
                    () =>
                    {
                        data = Localization.Data.Use();
                        use24HourFormat = Localization.Use24HourFormat.Use();
                        return Box();
                    },
                    new WindowOptions("Locale keys", 200, 200) { SupportedLocales = [CldrLocales.En, CldrLocales.Ja] },
                    useWarp: true
                );
                bool before = use24HourFormat!.Get();

                use24HourForm = false;
                _ = SendMessageW(window.Window.Hwnd, WM.WM_SETTINGCHANGE, default, default);

                Assert.Same(CldrLocales.Ja, data!.Get());
                Assert.True(before);
                Assert.False(use24HourFormat.Get());
            }
            finally
            {
                Win32Window.UiLanguagesReader = previousLanguages;
                Win32Window.Use24HourFormatReader = previousTimeFormat;
            }
        });

    /// <summary>
    /// Opening a window refuses supported locales that hold a <see langword="null"/>, where it refuses the options' size, before
    /// the window is opened.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Window options whose supported locales are the en tables and a <see langword="null"/>.
    ///
    /// Steps:
    /// - Open a hidden window on WARP with them.
    ///
    /// Expected:
    /// - It throws <see cref="ArgumentException"/> whose parameter is the options.
    /// </remarks>
    [Fact]
    public void OpenWindow_ANullSupportedLocale_Throws() =>
        ui.Invoke(() =>
        {
            var options = new WindowOptions("Null locale", 200, 200) { SupportedLocales = [CldrLocales.En, null!] };

            ArgumentException thrown = Assert.Throws<ArgumentException>(() => new TestWindow(() => Box(), options, useWarp: true));

            Assert.Equal("options", thrown.ParamName);
        });
}
