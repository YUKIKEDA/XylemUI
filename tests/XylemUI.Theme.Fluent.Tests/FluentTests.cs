using System.Numerics;
using XylemUI.Primitives;
using XylemUI.Testing.D3D11;
using XylemUI.Testing.XUnit;
using static XylemUI.Elements;
using static XylemUI.Theme.Fluent.Tests.FluentHeadless;

namespace XylemUI.Theme.Fluent.Tests;

// The Fluent button (design §13.2, "部品がテーマに付いていく形" and "TH1 のボタンの見た目の種類"): a primitive button with one effect
// that writes the theme's look, in headless apps wrapped by FluentRoot. The expected colors are those of the token files at
// microsoft/fluentui 1f59fc2 (packages/tokens/src/alias/lightColor.ts, darkColor.ts, and highContrastColor.ts), read by hand, for the
// tokens the button's styles name (react-button's useButtonStyles.styles.ts lines 40 to 75, 105 to 114, 151 to 166, 322 to 369, and
// 420 to 430);
// the brand steps of the measured blue are those of docs/benchmarks/th1-accent-ramp.md.
public sealed class FluentTests
{
    private static readonly Color Transparent = new(0, 0, 0, 0);
    private static readonly Color Red = Color.FromRgb(0xFF0000);
    private static readonly Color Green = Color.FromRgb(0x00FF00);

    // Windows' black high-contrast theme, for the golden image.
    private static readonly HighContrastColors Black = new(
        Window: Color.FromRgb(0x000000),
        WindowText: Color.FromRgb(0xFFFFFF),
        Hotlight: Color.FromRgb(0x75E9FC),
        GrayText: Color.FromRgb(0xA6A6A6),
        Highlight: Color.FromRgb(0x8EE3F0),
        HighlightText: Color.FromRgb(0x263B50),
        ButtonFace: Color.FromRgb(0x000000),
        ButtonText: Color.FromRgb(0xFFFFFF)
    );

    /// <summary>
    /// Each appearance takes the tokens of its variant: the neutral button the neutral background, border, and text, the primary one
    /// the brand steps the accent color makes, each with its hovered, pressed, and disabled looks.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Light, dark, and high contrast (with eight OS colors that differ from each other), each with the measured blue as the OS's
    ///   accent; the standard and the primary appearance.
    ///
    /// Steps:
    /// - Start a wrapped app of one button of the appearance; read its background, border, and text, and the background, border,
    ///   and text of its hovered, pressed, and disabled looks.
    ///
    /// Expected:
    /// - Standard, light: #ffffff #d1d1d1 #242424; hovered #f5f5f5 #c7c7c7 #242424; pressed #e0e0e0 #b3b3b3 #242424; disabled
    ///   #f0f0f0 #e0e0e0 #bdbdbd.
    /// - Standard, dark: #292929 #666666 #ffffff; #3d3d3d #757575 #ffffff; #1f1f1f #6b6b6b #ffffff; #141414 #424242 #5c5c5c.
    /// - Standard, high contrast: window, window text, window text; highlight, highlight, highlight text (twice); window, gray text,
    ///   gray text.
    /// - Primary, light: the blue's steps 80, 70, and 40 (#006cc0, #005ea8, #00396b) under white text with a transparent border;
    ///   disabled #f0f0f0, transparent, #bdbdbd. Dark: steps 70, 80, and 40 (#005ea8, #006cc0, #00396b); disabled #141414,
    ///   transparent, #5c5c5c.
    /// - Primary, high contrast: button face, window text, button text; highlight (hovered and pressed), window text, button text;
    ///   window, window text, gray text.
    /// </remarks>
    /// <param name="variant">The variant of the OS's appearance.</param>
    /// <param name="appearance">The button's appearance.</param>
    /// <returns>A task that completes when the test ends.</returns>
    [UiTheory]
    [InlineData(ThemeVariant.Light, ButtonAppearance.Standard)]
    [InlineData(ThemeVariant.Dark, ButtonAppearance.Standard)]
    [InlineData(ThemeVariant.HighContrast, ButtonAppearance.Standard)]
    [InlineData(ThemeVariant.Light, ButtonAppearance.Primary)]
    [InlineData(ThemeVariant.Dark, ButtonAppearance.Primary)]
    [InlineData(ThemeVariant.HighContrast, ButtonAppearance.Primary)]
    public async Task Button_EachAppearance_TakesTheTokensOfTheVariant(ThemeVariant variant, ButtonAppearance appearance)
    {
        SystemAppearance os = variant switch
        {
            ThemeVariant.Light => Light with { Accent = Blue },
            ThemeVariant.Dark => Dark with { Accent = Blue },
            _ => HighContrast with { Accent = Blue },
        };
        Element button = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() => button = Fluent.Button(Box(), () => { }, appearance), Options(os));
        _ = app.PumpAndSettle();

        HighContrastColors hc = DarkContrast;
        Color[] expected = (variant, appearance) switch
        {
            (ThemeVariant.Light, ButtonAppearance.Standard) => Rgb([
                0xFFFFFF,
                0xD1D1D1,
                0x242424,
                0xF5F5F5,
                0xC7C7C7,
                0x242424,
                0xE0E0E0,
                0xB3B3B3,
                0x242424,
                0xF0F0F0,
                0xE0E0E0,
                0xBDBDBD,
            ]),
            (ThemeVariant.Dark, ButtonAppearance.Standard) => Rgb([
                0x292929,
                0x666666,
                0xFFFFFF,
                0x3D3D3D,
                0x757575,
                0xFFFFFF,
                0x1F1F1F,
                0x6B6B6B,
                0xFFFFFF,
                0x141414,
                0x424242,
                0x5C5C5C,
            ]),
            (ThemeVariant.HighContrast, ButtonAppearance.Standard) =>
            [
                hc.Window,
                hc.WindowText,
                hc.WindowText,
                hc.Highlight,
                hc.Highlight,
                hc.HighlightText,
                hc.Highlight,
                hc.Highlight,
                hc.HighlightText,
                hc.Window,
                hc.GrayText,
                hc.GrayText,
            ],
            (ThemeVariant.Light, ButtonAppearance.Primary) => Primary(0x006CC0, 0x005EA8, 0x00396B, 0xF0F0F0, 0xBDBDBD),
            (ThemeVariant.Dark, ButtonAppearance.Primary) => Primary(0x005EA8, 0x006CC0, 0x00396B, 0x141414, 0x5C5C5C),
            _ =>
            [
                hc.ButtonFace,
                hc.WindowText,
                hc.ButtonText,
                hc.Highlight,
                hc.WindowText,
                hc.ButtonText,
                hc.Highlight,
                hc.WindowText,
                hc.ButtonText,
                hc.Window,
                hc.WindowText,
                hc.GrayText,
            ],
        };
        Assert.Equal(expected, Looks(app, button));
    }

    /// <summary>
    /// The button's padding, border width, and corners are the theme's, and its smallest width is 96: Fluent's medium button.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A wrapped app, light, of a row of two buttons: one around a box of 8 x 8, one around a box of 100 x 8.
    ///
    /// Steps:
    /// - Pump; read each button's rectangle, the content's place in it, and the first button's corner radius.
    ///
    /// Expected:
    /// - The first is 96 x 20: its padding and border would give 34 (12 of padding (spacingHorizontalM) and 1 of border
    ///   (strokeWidthThin) on the left and right), and the smallest width (minWidth 96) widens it; 20 is 5 of padding
    ///   (buttonSpacingMedium) and 1 of border on the top and bottom around 8. The content is centered at 44, 6 in it ((96 - 8) / 2
    ///   across; 6 down, where the content box starts); the radius is 4 (borderRadiusMedium).
    /// - The second is 126 x 20, wider than the smallest width, its content at 13, 6: no free space to center in.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Button_PaddingBorderCornersAndMinWidth_AreTheThemes()
    {
        Element narrow = default;
        Element narrowContent = default;
        Element wide = default;
        Element wideContent = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(
            () =>
                Row(
                    narrow = Fluent.Button(narrowContent = Box().Width(8).Height(8), () => { }),
                    wide = Fluent.Button(wideContent = Box().Width(100).Height(8), () => { })
                ),
            Options(Light)
        );
        _ = app.PumpAndSettle();
        Rect narrowOuter = app.Tree.GetRect(narrow.Node);
        Rect wideOuter = app.Tree.GetRect(wide.Node);

        Assert.Equal((96f, 20f), (narrowOuter.Width, narrowOuter.Height));
        Assert.Equal((44f, 6f), PlaceInButton(app, narrow, narrowContent));
        Assert.Equal(new CornerRadii(4), Visual(app, narrow).Radii);
        Assert.Equal((126f, 20f), (wideOuter.Width, wideOuter.Height));
        Assert.Equal((13f, 6f), PlaceInButton(app, wide, wideContent));
    }

    /// <summary>
    /// A button that its parent makes taller or wider than its content keeps the content in the middle, as Fluent's button does.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A wrapped app, light, of a column 200 wide holding a row and a button around a box of 8 x 8: the row holds a box of
    ///   10 x 60 and a button around a box of 8 x 8, which the row stretches to 60 high; the column stretches the second button to
    ///   200 wide.
    ///
    /// Steps:
    /// - Pump; read each button's rectangle and its content's place in it.
    ///
    /// Expected:
    /// - The row's button is 96 x 60, its content at 44, 26: the content box is 48 high after the padding and the border (6 on
    ///   each side), and (48 - 8) / 2 = 20 lies above the content. A percentage height in a flex item the row stretches resolves
    ///   against the stretched height, as in Chrome (Taffy's fixture blockflex_percentage_height_in_stretched_flex_item,
    ///   tests/XylemUI.Core.Tests/Fixtures/Taffy/blockflex.xml).
    /// - The column's button is 200 x 20, its content at 96, 6 ((200 - 8) / 2 across).
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Button_StretchedByItsParent_KeepsTheContentInTheMiddle()
    {
        Element tall = default;
        Element tallContent = default;
        Element wide = default;
        Element wideContent = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(
            () =>
                Column(
                        Row(Box().Width(10).Height(60), tall = Fluent.Button(tallContent = Box().Width(8).Height(8), () => { })),
                        wide = Fluent.Button(wideContent = Box().Width(8).Height(8), () => { })
                    )
                    .Width(200),
            Options(Light)
        );
        _ = app.PumpAndSettle();
        Rect tallOuter = app.Tree.GetRect(tall.Node);
        Rect wideOuter = app.Tree.GetRect(wide.Node);

        Assert.Equal((96f, 60f), (tallOuter.Width, tallOuter.Height));
        Assert.Equal((44f, 26f), PlaceInButton(app, tall, tallContent));
        Assert.Equal((200f, 20f), (wideOuter.Width, wideOuter.Height));
        Assert.Equal((96f, 6f), PlaceInButton(app, wide, wideContent));
    }

    /// <summary>
    /// The focus-visible state of each appearance takes Fluent's focus border, colorStrokeFocus2, in each variant.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Light, dark, and high contrast (with eight OS colors that differ from each other); a wrapped app of a standard and a
    ///   primary button.
    ///
    /// Steps:
    /// - Pump; read the border's paint of each button's focus-visible look, and whether the look sets anything else.
    ///
    /// Expected:
    /// - Black (#000000) in light, white (#ffffff) in dark, and the OS's highlight color in high contrast, for both buttons; the
    ///   look sets no background, color of text, opacity, or transform.
    /// </remarks>
    /// <param name="variant">The variant of the OS's appearance.</param>
    /// <returns>A task that completes when the test ends.</returns>
    [UiTheory]
    [InlineData(ThemeVariant.Light)]
    [InlineData(ThemeVariant.Dark)]
    [InlineData(ThemeVariant.HighContrast)]
    public async Task Button_FocusVisible_TakesTheFocusStroke(ThemeVariant variant)
    {
        SystemAppearance os = variant switch
        {
            ThemeVariant.Light => Light,
            ThemeVariant.Dark => Dark,
            _ => HighContrast,
        };
        Element standard = default;
        Element primary = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(
            () => Row(standard = Fluent.Button(Box(), () => { }), primary = Fluent.Button(Box(), () => { }, ButtonAppearance.Primary)),
            Options(os)
        );
        _ = app.PumpAndSettle();

        Color expected = variant switch
        {
            ThemeVariant.Light => Color.FromRgb(0x000000),
            ThemeVariant.Dark => Color.FromRgb(0xFFFFFF),
            _ => DarkContrast.Highlight,
        };
        PaintStyle? focusLook = new PaintStyle { BorderPaint = expected };
        Assert.Equal(focusLook, Visual(app, standard).States!.Focus!.FocusVisible);
        Assert.Equal(focusLook, Visual(app, primary).States!.Focus!.FocusVisible);
    }

    /// <summary>
    /// A value the app sets in the style wins over the theme's, also after the theme changes; a look set with the method chain after
    /// the button is made gives way to the theme's at the next change.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A wrapped app, light, of two buttons: one with a style of a red background and a green hovered background; one whose color
    ///   of text is set red with the method chain after it is made.
    ///
    /// Steps:
    /// - Pump; read the first button's background, hovered look, and border, and the second's color of text. Set the OS's
    ///   appearance to dark; pump; read the same.
    ///
    /// Expected:
    /// - Light: red, the style's hovered look, #d1d1d1; red. Dark: red, the style's hovered look, #666666 (the border is still the
    ///   theme's); white, the theme's.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Button_StyleValues_WinOverTheTheme_AndTheChainGivesWay()
    {
        var hovered = new PaintStyle { Background = Green };
        var style = new ButtonStyle { Background = Red, Hovered = hovered };
        Element styled = default;
        Element chained = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(
            () =>
            {
                styled = Fluent.Button(Box(), () => { }, style: style);
                chained = Fluent.Button(Box(), () => { }).Foreground(Red);
                return Column(styled, chained);
            },
            Options(Light)
        );
        _ = app.PumpAndSettle();
        (Paint?, PaintStyle?, Paint?, Color?) light = Read(app, styled, chained);

        app.SetSystemAppearance(Dark);
        _ = app.PumpAndSettle();
        (Paint?, PaintStyle?, Paint?, Color?) dark = Read(app, styled, chained);

        Assert.Equal(((Paint?)Red, (PaintStyle?)hovered, (Paint?)Fill(0xD1D1D1), (Color?)Red), light);
        Assert.Equal(((Paint?)Red, (PaintStyle?)hovered, (Paint?)Fill(0x666666), (Color?)Color.FromRgb(0xFFFFFF)), dark);
    }

    /// <summary>
    /// A button has one effect for its whole look: it makes one scope more than the primitive button.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A wrapped app whose root function counts the live scopes (<c>Scope.LiveCount</c>) around a primitive button and around a
    ///   Fluent button, each of an empty box, with nothing else made between the counts.
    ///
    /// Steps:
    /// - Start the app.
    ///
    /// Expected:
    /// - The Fluent button made exactly one scope more than the primitive one.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Button_HasOneEffect()
    {
        int primitive = -1;
        int fluent = -1;
        await using HeadlessApp app = await HeadlessApp.StartAsync(
            () =>
            {
                int before = Scope.LiveCount;
                Element plain = Primitive.Button(Box(), () => { });
                int middle = Scope.LiveCount;
                Element themed = Fluent.Button(Box(), () => { });
                int after = Scope.LiveCount;
                primitive = middle - before;
                fluent = after - middle;
                return Column(plain, themed);
            },
            Options(Light)
        );
        _ = app.PumpAndSettle();

        Assert.Equal(primitive + 1, fluent);
    }

    /// <summary>
    /// A change of the colors alone lays nothing out; a theme whose padding or border width differs lays the button out again.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A wrapped app that asks for the light theme from a signal, of a button around a box of 80 x 8, settled: wider than the
    ///   smallest width of 96, so that the padding and the border show in the button's width.
    ///
    /// Steps:
    /// - Set the dark theme; read whether a layout is queued and the button's background; pump. Set the dark theme with
    ///   spacingHorizontalM 20; read whether a layout is queued; pump; read the button's size. Set that theme with strokeWidthThin
    ///   3; read whether a layout is queued; pump; read the size.
    ///
    /// Expected:
    /// - No layout queued and the background #292929; a layout queued, then 122 x 20; a layout queued, then 126 x 24.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Button_ColorChange_LaysNothingOut_ALayoutValueChange_Does()
    {
        FluentTheme dark = FluentTheme.Create(ThemeVariant.Dark, Density.Standard, null);
        FluentTheme wider = dark with { Spacing = dark.Spacing with { SpacingHorizontalM = 20 } };
        FluentTheme thicker = wider with { Sizes = new FluentSizes { StrokeWidths = wider.Sizes.StrokeWidths with { StrokeWidthThin = 3 } } };
        Signal<FluentTheme> window = Signal.Create(FluentTheme.Create(ThemeVariant.Light, Density.Standard, null));
        Element button = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(
            () =>
            {
                FluentTheme.RequestForWindow(window);
                return Row(button = Fluent.Button(Box().Width(80).Height(8), () => { }));
            },
            Options(Light)
        );
        _ = app.PumpAndSettle();

        window.Set(dark);
        bool colorLayout = app.Tree.LayoutRoots.Count > 0;
        Paint? background = Visual(app, button).Background;
        _ = app.PumpAndSettle();
        window.Set(wider);
        bool paddingLayout = app.Tree.LayoutRoots.Count > 0;
        _ = app.PumpAndSettle();
        Rect padded = app.Tree.GetRect(button.Node);
        window.Set(thicker);
        bool borderLayout = app.Tree.LayoutRoots.Count > 0;
        _ = app.PumpAndSettle();
        Rect bordered = app.Tree.GetRect(button.Node);

        Assert.False(colorLayout);
        Assert.Equal(Fill(0x292929), background);
        Assert.True(paddingLayout);
        Assert.Equal((122f, 20f), (padded.Width, padded.Height));
        Assert.True(borderLayout);
        Assert.Equal((126f, 24f), (bordered.Width, bordered.Height));
    }

    /// <summary>
    /// A change of the OS's appearance gives every button the look of the new theme, wherever it is in the tree.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A wrapped app, light, of a standard button, a primary button, and a standard button two columns down.
    ///
    /// Steps:
    /// - Set the OS's appearance to dark; pump; read the three backgrounds.
    ///
    /// Expected:
    /// - #292929, brandWeb's step 70 #115ea3, and #292929.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Button_OsAppearanceChange_EveryButtonTakesTheNewTheme()
    {
        Element[] buttons = new Element[3];
        await using HeadlessApp app = await HeadlessApp.StartAsync(
            () =>
            {
                buttons[0] = Fluent.Button(Box(), () => { });
                buttons[1] = Fluent.Button(Box(), () => { }, ButtonAppearance.Primary);
                buttons[2] = Fluent.Button(Box(), () => { });
                return Column(buttons[0], buttons[1], Column(Column(buttons[2])));
            },
            Options(Light)
        );
        _ = app.PumpAndSettle();

        app.SetSystemAppearance(Dark);
        _ = app.PumpAndSettle();

        Paint?[] expected = [Fill(0x292929), Fill(0x115EA3), Fill(0x292929)];
        Assert.Equal(expected, buttons.Select(b => Visual(app, b).Background));
    }

    /// <summary>
    /// A change of the theme keeps the focus, the text of a field, and the position of a scroll container: nothing is made again.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A wrapped app, light, of a button, a text field of "draft", and a scroll container 100 high around a box 400 high.
    ///
    /// Steps:
    /// - Focus the button; pump. Scroll the container to 40; pump. Set the OS's appearance to dark; pump.
    ///
    /// Expected:
    /// - The button has the focus and is #292929; the field's text is "draft" and its node is still the column's second child; the
    ///   scroll position is 0, 40.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Button_ThemeChange_KeepsFocusTextAndScroll()
    {
        Signal<string> text = Signal.Create("draft");
        var scroll = new ScrollState();
        Element button = default;
        Element field = default;
        Element column = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(
            () =>
            {
                button = Fluent.Button(Box().Width(8).Height(8), () => { });
                field = Primitive.TextField(text);
                return column = Column(button, field, ScrollView(Box().Width(50).Height(400), scroll).Height(100));
            },
            Options(Light)
        );
        Assert.True(app.Focus(button));
        _ = app.PumpAndSettle();
        scroll.ScrollTo(new Vector2(0, 40));
        _ = app.PumpAndSettle();

        app.SetSystemAppearance(Dark);
        _ = app.PumpAndSettle();

        Assert.Equal(button.Node, app.Tree.Focus.Focused);
        Assert.Equal(Fill(0x292929), Visual(app, button).Background);
        Assert.Equal("draft", text.Get());
        Assert.Equal(field.Node, app.Tree.Nodes.GetRef(app.Tree.Nodes.GetRef(column.Node).FirstChild).NextSibling);
        Assert.Equal(new Vector2(0, 40), scroll.Offset.Get());
    }

    /// <summary>
    /// The button refuses an appearance out of its enum and a missing click handler, before it makes a node.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A wrapped app, light.
    ///
    /// Steps:
    /// - In the root function, make a box, then a button of it with <c>(ButtonAppearance)2</c>, then one with no handler; return a
    ///   column of the box.
    ///
    /// Expected:
    /// - <see cref="ArgumentOutOfRangeException"/> naming <c>appearance</c>, then <see cref="ArgumentNullException"/> naming
    ///   <c>onClick</c>; the box has no parent after both.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Button_AppearanceOutOfEnumOrNoHandler_Throws()
    {
        string? appearance = null;
        string? handler = null;
        bool orphan = false;
        await using HeadlessApp app = await HeadlessApp.StartAsync(
            () =>
            {
                Element content = Box();
                ArgumentException outOfEnum = Assert.Throws<ArgumentOutOfRangeException>(() =>
                    Fluent.Button(content, () => { }, (ButtonAppearance)2)
                );
                ArgumentException noHandler = Assert.Throws<ArgumentNullException>(() => Fluent.Button(content, null!));
                appearance = outOfEnum.ParamName;
                handler = noHandler.ParamName;
                orphan = content.Tree!.Nodes.GetRef(content.Node).Parent.IsNone;
                return Column(content);
            },
            Options(Light)
        );
        _ = app.PumpAndSettle();

        Assert.Equal(("appearance", "onClick"), (appearance, handler));
        Assert.True(orphan);
    }

    /// <summary>
    /// Switching 1,000 buttons between light and dark, with the frame that follows, allocates nothing (design §11.3).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A wrapped app of 1,920 x 1,000 that asks for a theme from a signal: 50 rows of 20 buttons, every fifth primary, around
    ///   boxes of 8 x 8, each button 96 x 20 with its centering row; an effect that counts the changes of the theme it reads. The
    ///   light and the dark theme are made before.
    ///
    /// Steps:
    /// - Pump; read the right and bottom edges of the last button. Under <c>PerformanceAssert.DoesNotAllocate</c>, 100 runs, its
    ///   unmeasured window and the windows it runs again included: set the dark theme on an even index and the light one on an odd
    ///   index (each run changes the theme, since the indices of the runs alternate), and pump.
    ///
    /// Expected:
    /// - The last button ends at 1,920, 1,000: every button is inside the window. 0 bytes in every window; the effect counted one
    ///   change for each run, after the first value.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Button_ThousandButtons_LightDarkSwitch_AllocatesNothing()
    {
        FluentTheme light = FluentTheme.Create(ThemeVariant.Light, Density.Standard, null);
        FluentTheme dark = FluentTheme.Create(ThemeVariant.Dark, Density.Standard, null);
        Signal<FluentTheme> window = Signal.Create(light);
        int changes = 0;
        Element last = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(
            () =>
            {
                FluentTheme.RequestForWindow(window);
                IReadOnlySignal<FluentTheme> theme = FluentTheme.Current.Use();
                _ = Effect.Create(() =>
                {
                    _ = theme.Get();
                    changes++;
                });
                return ThousandButtons(out _, out last);
            },
            Options(Light) with
            {
                Width = 1920,
                Height = 1000,
            }
        );
        _ = app.PumpAndSettle();
        Assert.Equal((1920f, 1000f), FarCorner(app, last));

        int runs = PerformanceAssert.DoesNotAllocate(
            100,
            index =>
            {
                window.Set((index & 1) == 0 ? dark : light);
                _ = app.PumpAndSettle();
            }
        );

        Assert.Equal(runs + 1, changes);
    }

    /// <summary>
    /// A frame that moves the hover between two of 1,000 buttons allocates nothing (design §11.3, "ホバーの出入り").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The same 1,000 buttons in a wrapped app of 1,920 x 1,000, light, settled; the centers of the first two buttons, where the
    ///   pointer lies over each button's content, inside its centering row.
    ///
    /// Steps:
    /// - Read the right and bottom edges of the last button. Under <c>PerformanceAssert.DoesNotAllocate</c>, 1,000 runs, its
    ///   unmeasured window and the windows it runs again included: move the pointer to the first button's center on an even index
    ///   and to the second's on an odd index, and pump. Then move it to the first button and pump.
    ///
    /// Expected:
    /// - The last button ends at 1,920, 1,000. 0 bytes in every window; one frame was presented for each run; at the end the first
    ///   button is hovered and the second is not.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Button_HoverFrames_AllocateNothing()
    {
        Element[] firstTwo = [];
        Element last = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(
            () => ThousandButtons(out firstTwo, out last),
            Options(Light) with
            {
                Width = 1920,
                Height = 1000,
            }
        );
        _ = app.PumpAndSettle();
        Assert.Equal((1920f, 1000f), FarCorner(app, last));
        (int X, int Y) a = app.CenterOf(firstTwo[0]);
        (int X, int Y) b = app.CenterOf(firstTwo[1]);
        int presents = app.PresentCount;

        int runs = PerformanceAssert.DoesNotAllocate(
            1_000,
            index =>
            {
                (int x, int y) = (index & 1) == 0 ? a : b;
                app.Pointer.Move(x, y);
                _ = app.PumpAndSettle();
            }
        );
        int presented = app.PresentCount - presents;
        app.Pointer.Move(a.X, a.Y);
        _ = app.PumpAndSettle();

        Assert.Equal(runs, presented);
        Assert.True((app.Tree.Nodes.GetRef(firstTwo[0].Node).Pseudo & PseudoState.Hovered) != 0);
        Assert.True((app.Tree.Nodes.GetRef(firstTwo[1].Node).Pseudo & PseudoState.Hovered) == 0);
    }

    /// <summary>
    /// A frame that moves the focus with Tab between two of 1,000 buttons allocates nothing: the focus-visible look comes and goes
    /// (design §11.3, §13.2 "TH1 のボタンの値").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The same 1,000 buttons in a wrapped app of 1,920 x 1,000, light, settled, with the first button focused and pumped.
    ///
    /// Steps:
    /// - Read the right and bottom edges of the last button. Warm up once: press Tab and pump, press Shift+Tab and pump (each
    ///   button's focus styles are made on the first run of its effect, and the first moves of the focus are not the steady
    ///   state). Under <c>PerformanceAssert.DoesNotAllocate</c>, 1,000 runs, its unmeasured window and the windows it runs again
    ///   included: press Tab and pump, then press Shift+Tab and pump. Each run starts and ends with the first button focused,
    ///   whatever its index.
    ///
    /// Expected:
    /// - The last button ends at 1,920, 1,000. 0 bytes in every window; two frames were presented for each run; at the end the
    ///   first button has the focus and is focus-visible, and the second is not focus-visible.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Button_TabFocusFrames_AllocateNothing()
    {
        Element[] firstTwo = [];
        Element last = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(
            () => ThousandButtons(out firstTwo, out last),
            Options(Light) with
            {
                Width = 1920,
                Height = 1000,
            }
        );
        _ = app.PumpAndSettle();
        Assert.Equal((1920f, 1000f), FarCorner(app, last));
        Assert.True(app.Focus(firstTwo[0]));
        _ = app.PumpAndSettle();
        TabAndBack(app);
        Assert.Equal(firstTwo[0].Node, app.Tree.Focus.Focused);
        int presents = app.PresentCount;

        int runs = PerformanceAssert.DoesNotAllocate(1_000, _ => TabAndBack(app));
        int presented = app.PresentCount - presents;

        Assert.Equal(runs * 2, presented);
        Assert.Equal(firstTwo[0].Node, app.Tree.Focus.Focused);
        Assert.True((app.Tree.Nodes.GetRef(firstTwo[0].Node).Pseudo & PseudoState.FocusVisible) != 0);
        Assert.True((app.Tree.Nodes.GetRef(firstTwo[1].Node).Pseudo & PseudoState.FocusVisible) == 0);
    }

    /// <summary>
    /// The text of a button's content takes the theme's Body1Strong from the row around it, Fluent's medium button's text: weight
    /// 600, size 14, and a line of 20 px; a text's own field keeps its value, an icon keeps its own style, and a theme of other
    /// typography gives the text its values (design §13.2, "文字のスタイル").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A wrapped app, light, that asks for the light theme from a signal, of a button around a row of the icon Add24Regular, a
    ///   text with no field of its own, and a text of its own weight 700; the light theme with Body1Strong 16 / 22 / 800 made
    ///   before.
    ///
    /// Steps:
    /// - Pump; read the three styles. Set the theme with the other typography; pump; read them again.
    ///
    /// Expected:
    /// - Light: the text 600, 14, and 20 / 14; the bold text 700, 14, and 20 / 14; the icon 400, 24, and 1 (its own line, a multiple
    ///   of its size). The other typography: the text 800, 16, and 22 / 16; the bold text 700, 16, and 22 / 16; the icon as before.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Button_TextOfTheContent_TakesBody1Strong_AndFollowsTheTheme()
    {
        FluentTheme light = FluentTheme.Create(ThemeVariant.Light, Density.Standard, null);
        FluentTheme other = light with
        {
            Typography = light.Typography with { Body1Strong = new FluentTextStyle(16, 22, new XylemUI.Text.FontWeight(800)) },
        };
        Signal<FluentTheme> window = Signal.Create(light);
        Element icon = default;
        Element text = default;
        Element bold = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(
            () =>
            {
                FluentTheme.RequestForWindow(window);
                return Row(
                    Fluent.Button(
                        Row(
                            icon = Primitive.Icon(FluentIcons.Add24Regular),
                            text = Elements.Text("Add"),
                            bold = Elements.Text("Bold").FontWeight(XylemUI.Text.FontWeight.Bold)
                        ),
                        () => { }
                    )
                );
            },
            Options(Light)
        );
        _ = app.PumpAndSettle();
        (int, float, float)[] before = [Typography(app, text), Typography(app, bold), Typography(app, icon)];

        window.Set(other);
        _ = app.PumpAndSettle();
        (int, float, float)[] after = [Typography(app, text), Typography(app, bold), Typography(app, icon)];

        Assert.Equal([(600, 14f, 20f / 14f), (700, 14f, 20f / 14f), (400, 24f, 1f)], before);
        Assert.Equal([(800, 16f, 22f / 16f), (700, 16f, 22f / 16f), (400, 24f, 1f)], after);
    }

    /// <summary>
    /// A standard, a primary, and a disabled button match their golden in each variant (design §13.2, "確かめ方").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A wrapped app of 336 x 72 on WARP (three buttons of the smallest width 96, the gaps, and the padding), of a row with a
    ///   padding of 16 and a gap of 8: a standard button "Save", a primary button
    ///   "Send", and a disabled standard button "Off". The OS's appearance is light, dark, or Windows' black high-contrast theme,
    ///   with no accent color.
    ///
    /// Steps:
    /// - Pump; read the three labels' styles; compare the frame with the variant's golden.
    ///
    /// Expected:
    /// - Each label has the theme's Body1Strong: weight 600, size 14, and a line of 20 px (20 / 14). The frame matches
    ///   fluent-button-light.png, fluent-button-dark.png, or fluent-button-high-contrast.png.
    /// </remarks>
    /// <param name="variant">The variant of the OS's appearance.</param>
    /// <param name="golden">The golden's file name.</param>
    /// <returns>A task that completes when the test ends.</returns>
    [UiTheory]
    [InlineData(ThemeVariant.Light, "fluent-button-light.png")]
    [InlineData(ThemeVariant.Dark, "fluent-button-dark.png")]
    [InlineData(ThemeVariant.HighContrast, "fluent-button-high-contrast.png")]
    public async Task Button_EachVariant_MatchesItsGolden(ThemeVariant variant, string golden)
    {
        SystemAppearance os = variant switch
        {
            ThemeVariant.Light => Light,
            ThemeVariant.Dark => Dark,
            _ => new SystemAppearance(ColorScheme.Dark, true, null, Black),
        };
        Element[] labels = new Element[3];
        await using HeadlessApp app = await HeadlessApp.StartAsync(
            () =>
                Row(
                        Fluent.Button(labels[0] = Elements.Text("Save"), () => { }),
                        Fluent.Button(labels[1] = Elements.Text("Send"), () => { }, ButtonAppearance.Primary),
                        Fluent.Button(labels[2] = Elements.Text("Off"), () => { }).Disabled()
                    )
                    .Padding(16)
                    .Gap(8),
            Options(os) with
            {
                Width = 336,
                Height = 72,
                Renderer = () => new WarpRenderer(),
            }
        );
        _ = app.PumpAndSettle();

        // The golden's labels are drawn in Body1Strong (design §13.2, "文字のスタイル"): the images hold its line of 20 px.
        Assert.All(labels, label => Assert.Equal((600, 14f, 20f / 14f), Typography(app, label)));
        await app.MatchesGoldenAsync(golden);
    }

    // 50 rows of 20 buttons around boxes of 8 x 8, every fifth primary, in a column: each button is 96 x 20 (the smallest width),
    // so the buttons fill a window of 1,920 x 1,000 exactly. The first two buttons and the last are handed out.
    private static Element ThousandButtons(out Element[] firstTwo, out Element last)
    {
        var rows = new Element[50];
        var cells = new Element[20];
        firstTwo = new Element[2];
        for (int row = 0; row < rows.Length; row++)
        {
            for (int cell = 0; cell < cells.Length; cell++)
            {
                ButtonAppearance appearance = cell % 5 == 4 ? ButtonAppearance.Primary : ButtonAppearance.Standard;
                cells[cell] = Fluent.Button(Box().Width(8).Height(8), static () => { }, appearance);
            }

            rows[row] = Row(cells);
            if (row == 0)
            {
                firstTwo[0] = cells[0];
                firstTwo[1] = cells[1];
            }
        }

        last = cells[^1];
        return Column(rows);
    }

    // Moves the focus to the next button with Tab and back with Shift+Tab, pumping after each key, so that each frame draws one
    // move of the focus-visible look.
    private static void TabAndBack(HeadlessApp app)
    {
        _ = app.Keyboard.Press(Key.Tab);
        _ = app.PumpAndSettle();
        _ = app.Keyboard.Press(Key.Tab, KeyModifiers.Shift);
        _ = app.PumpAndSettle();
    }

    // The right and bottom edges of the last of the 1,000 buttons in the window: its rectangle is its row's, and the row's is the
    // column's, which the root box holds at 0, 0.
    private static (float Right, float Bottom) FarCorner(HeadlessApp app, Element last)
    {
        Rect cell = app.Tree.GetRect(last.Node);
        Rect row = app.Tree.GetRect(app.Tree.Nodes.GetRef(last.Node).Parent);
        return (row.X + cell.X + cell.Width, row.Y + cell.Y + cell.Height);
    }

    // The background, border, and text of a button, then the same of its hovered, pressed, and disabled looks.
    private static Color[] Looks(HeadlessApp app, Element button)
    {
        NodeVisual visual = Visual(app, button);
        PaintStyle hovered = visual.States!.Hovered!.Value;
        PaintStyle pressed = visual.States.Pressed!.Value;
        PaintStyle disabled = visual.States.Disabled!.Value;
        return
        [
            visual.Background!.Value.Color,
            visual.BorderPaint!.Value.Color,
            visual.Foreground!.Value,
            hovered.Background!.Value.Color,
            hovered.BorderPaint!.Value.Color,
            hovered.Foreground!.Value,
            pressed.Background!.Value.Color,
            pressed.BorderPaint!.Value.Color,
            pressed.Foreground!.Value,
            disabled.Background!.Value.Color,
            disabled.BorderPaint!.Value.Color,
            disabled.Foreground!.Value,
        ];
    }

    // The expected looks of a primary button: the brand steps of its background, hovered, and pressed, under white text and a
    // transparent border, and the disabled background and text of the variant with a transparent border.
    private static Color[] Primary(uint rest, uint hovered, uint pressed, uint disabledBackground, uint disabledText)
    {
        Color white = Color.FromRgb(0xFFFFFF);
        return
        [
            Color.FromRgb(rest),
            Transparent,
            white,
            Color.FromRgb(hovered),
            Transparent,
            white,
            Color.FromRgb(pressed),
            Transparent,
            white,
            Color.FromRgb(disabledBackground),
            Transparent,
            Color.FromRgb(disabledText),
        ];
    }

    private static Color[] Rgb(uint[] values) => Array.ConvertAll(values, Color.FromRgb);

    // The weight, size, and line height (a multiple of the size) of a text's style.
    private static (int Weight, float Size, float LineHeight) Typography(HeadlessApp app, Element text)
    {
        TextStyle style = app.Tree.Nodes.GetRef(text.Node).Text!.Style;
        return (style.Font.Weight.Value, style.Size, style.LineHeight);
    }

    // The content's place in its button: the content's rectangle is the centering row's, and the row's is the button's (each
    // rectangle is in its parent's coordinates). It checks that the row is the content's parent and the button the row's.
    private static (float X, float Y) PlaceInButton(HeadlessApp app, Element button, Element content)
    {
        Handle<UiNode> row = app.Tree.Nodes.GetRef(content.Node).Parent;
        Assert.Equal(button.Node, app.Tree.Nodes.GetRef(row).Parent);
        Rect inRow = app.Tree.GetRect(content.Node);
        Rect rowInButton = app.Tree.GetRect(row);
        return (rowInButton.X + inRow.X, rowInButton.Y + inRow.Y);
    }

    private static (Paint? Background, PaintStyle? Hovered, Paint? Border, Color? ChainedForeground) Read(
        HeadlessApp app,
        Element styled,
        Element chained
    )
    {
        NodeVisual visual = Visual(app, styled);
        return (visual.Background, visual.States!.Hovered, visual.BorderPaint, Visual(app, chained).Foreground);
    }
}
