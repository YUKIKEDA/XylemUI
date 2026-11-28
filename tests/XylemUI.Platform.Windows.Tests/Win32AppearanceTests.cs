namespace XylemUI.Platform.Windows.Tests;

// How the Windows host makes the OS's appearance from what it reads (row TH1, design §13.2, "Windows で読むもの"): light or dark
// from UISettings' foreground by Microsoft's IsColorLight, from the theme's window text while high contrast is on, the accent color
// as read, and the eight system colors of high contrast. No test changes a setting of Windows: the values stand for what is read.
[Collection(UiThreadGroup.Name)]
public sealed class Win32AppearanceTests(UiThreadFixture ui)
{
    // The colors of the dark contrast theme that spike #888 applied (docs/benchmarks/th1-os-appearance.md, "(c)").
    private static readonly HighContrastColors DarkTheme = new(
        Color.FromRgb(0x2D3236),
        Color.FromRgb(0xFFFFFF),
        Color.FromRgb(0x70EBDE),
        Color.FromRgb(0xA6A6A6),
        Color.FromRgb(0xA1BFDE),
        Color.FromRgb(0x212D3B),
        Color.FromRgb(0x2D3236),
        Color.FromRgb(0xB6F6F0)
    );

    /// <summary>
    /// With high contrast off, the scheme is dark when UISettings' foreground is light by <c>IsColorLight</c>
    /// (<c>5 G + 2 R + B &gt; 1,024</c>), light otherwise and when no foreground was read; the accent color is the one read, and
    /// there are no high-contrast colors.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The cases: the foregrounds spike #888 read, #FFFFFF (dark mode) and #000000 (light mode); #0CC800, whose sum is 1,024
    ///   itself; #0CC801, whose sum is 1,025; and none, as when UISettings cannot be made. Each with the accent #0078D4, and the
    ///   last also with none.
    ///
    /// Steps:
    /// - Make the appearance from each foreground and accent, with no high-contrast colors.
    ///
    /// Expected:
    /// - Dark, light, light, dark, light, light; high contrast off and no colors in each; the accent as given.
    /// </remarks>
    /// <param name="foreground">The foreground as 0xRRGGBB; -1 for none.</param>
    /// <param name="withAccent">Whether the accent #0078D4 was read.</param>
    /// <param name="dark">Whether the scheme is dark.</param>
    [Theory]
    [InlineData(0xFFFFFF, true, true)]
    [InlineData(0x000000, true, false)]
    [InlineData(0x0CC800, true, false)]
    [InlineData(0x0CC801, true, true)]
    [InlineData(-1, true, false)]
    [InlineData(-1, false, false)]
    public void FromValues_HighContrastOff_TheForegroundChoosesTheScheme(int foreground, bool withAccent, bool dark)
    {
        Color? text = foreground < 0 ? null : Color.FromRgb((uint)foreground);
        Color? accent = withAccent ? Color.FromRgb(0x0078D4) : null;

        SystemAppearance appearance = Win32Appearance.FromValues(text, accent, null);

        var expected = new SystemAppearance(dark ? ColorScheme.Dark : ColorScheme.Light, false, accent, null);
        Assert.Equal(expected, appearance);
    }

    /// <summary>
    /// With high contrast on, the scheme comes from the theme's window text, not from UISettings' foreground, and the appearance
    /// holds the theme's colors and the accent color read.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The dark contrast theme of spike #888, whose window text is #FFFFFF, with the foreground it read, #B6F6F0, and its accent
    ///   #A1BFDE. A light theme, the same colors with the window text #000000, with the foreground #FFFFFF.
    ///
    /// Steps:
    /// - Make the appearance from each.
    ///
    /// Expected:
    /// - Dark, then light, while the light theme's foreground alone would read dark; high contrast on, the theme's colors, and the
    ///   accent #A1BFDE in both.
    /// </remarks>
    [Fact]
    public void FromValues_HighContrastOn_TheWindowTextChoosesTheScheme()
    {
        Color accent = Color.FromRgb(0xA1BFDE);
        HighContrastColors lightTheme = DarkTheme with { WindowText = Color.FromRgb(0x000000) };

        SystemAppearance dark = Win32Appearance.FromValues(Color.FromRgb(0xB6F6F0), accent, DarkTheme);
        SystemAppearance light = Win32Appearance.FromValues(Color.FromRgb(0xFFFFFF), accent, lightTheme);

        Assert.Equal(new SystemAppearance(ColorScheme.Dark, true, accent, DarkTheme), dark);
        Assert.Equal(new SystemAppearance(ColorScheme.Light, true, accent, lightTheme), light);
    }

    /// <summary>
    /// The eight high-contrast colors are read from the system colors the design names, each into its member, from the
    /// <c>COLORREF</c>'s <c>0x00BBGGRR</c>.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A table from the indices winuser.h gives (<c>COLOR_WINDOW</c> 5, <c>COLOR_WINDOWTEXT</c> 8, <c>COLOR_HOTLIGHT</c> 26,
    ///   <c>COLOR_GRAYTEXT</c> 17, <c>COLOR_HIGHLIGHT</c> 13, <c>COLOR_HIGHLIGHTTEXT</c> 14, <c>COLOR_3DFACE</c> 15,
    ///   <c>COLOR_BTNTEXT</c> 18) to the <c>COLORREF</c>s of spike #888's dark contrast theme; an index not in it throws.
    ///
    /// Steps:
    /// - Read the colors through the table.
    ///
    /// Expected:
    /// - The theme's colors, each in its member; each index was asked once.
    /// </remarks>
    [Fact]
    public void ReadContrastColors_TakesEachSystemColorIntoItsMember()
    {
        var table = new Dictionary<int, uint>
        {
            [5] = 0x36322D,
            [8] = 0xFFFFFF,
            [26] = 0xDEEB70,
            [17] = 0xA6A6A6,
            [13] = 0xDEBFA1,
            [14] = 0x3B2D21,
            [15] = 0x36322D,
            [18] = 0xF0F6B6,
        };
        var asked = new List<int>();

        HighContrastColors colors = Win32Appearance.ReadContrastColors(index =>
        {
            asked.Add(index);
            return table[index];
        });

        Assert.Equal(DarkTheme, colors);
        Assert.Equal([5, 8, 26, 17, 13, 14, 15, 18], asked);
    }

    /// <summary>
    /// The real reading on this machine gives an appearance the window core takes: its high contrast and its colors agree, the
    /// accent color read is opaque, and a second reading with no change of Windows between gives the same value.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The UI thread of the tests, in the single-threaded apartment, where UISettings is activated; Windows' settings are not
    ///   changed.
    ///
    /// Steps:
    /// - Read the appearance twice, the second from the first.
    ///
    /// Expected:
    /// - High contrast is on exactly when there are high-contrast colors; an accent color read has the alpha 255; the two readings
    ///   are equal.
    /// </remarks>
    [Fact]
    public void Read_OnThisMachine_GivesAnAppearanceThatAgrees() =>
        ui.Invoke(() =>
        {
            SystemAppearance first = Win32Appearance.Read(default);
            SystemAppearance second = Win32Appearance.Read(first);

            Assert.Equal(first.HighContrast, first.Contrast.HasValue);
            Assert.True(first.Accent is null || first.Accent.Value.A == 255);
            Assert.Equal(first, second);
        });
}
