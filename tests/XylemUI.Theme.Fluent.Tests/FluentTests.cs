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
// tokens the button's styles name (react-button's useButtonStyles.styles.ts lines 40 to 75, 151 to 166, 322 to 369, and 420 to 430);
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
    /// The button's padding, border width, and corners are the theme's: Fluent's medium button.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A wrapped app, light, of a button around a box of 8 x 8.
    ///
    /// Steps:
    /// - Pump; read the button's rectangle, the content's place in it, and the button's corner radius.
    ///
    /// Expected:
    /// - 34 x 20: 12 of padding (spacingHorizontalM) and 1 of border (strokeWidthThin) on the left and right, 5 of padding
    ///   (buttonSpacingMedium) and 1 of border on the top and bottom; the content at 13, 6 in it; the radius 4 (borderRadiusMedium).
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Button_PaddingBorderAndCorners_AreTheThemes()
    {
        Element button = default;
        Element content = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(
            () => Row(button = Fluent.Button(content = Box().Width(8).Height(8), () => { })),
            Options(Light)
        );
        _ = app.PumpAndSettle();
        Rect outer = app.Tree.GetRect(button.Node);
        Rect inner = app.Tree.GetRect(content.Node);

        Assert.Equal((34f, 20f), (outer.Width, outer.Height));
        Assert.Equal((13f, 6f), (inner.X - outer.X, inner.Y - outer.Y));
        Assert.Equal(new CornerRadii(4), Visual(app, button).Radii);
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
    /// - A wrapped app that asks for the light theme from a signal, of a button around a box of 8 x 8, settled.
    ///
    /// Steps:
    /// - Set the dark theme; read whether a layout is queued and the button's background; pump. Set the dark theme with
    ///   spacingHorizontalM 20; read whether a layout is queued; pump; read the button's size. Set that theme with strokeWidthThin
    ///   3; read whether a layout is queued; pump; read the size.
    ///
    /// Expected:
    /// - No layout queued and the background #292929; a layout queued, then 50 x 20; a layout queued, then 54 x 24.
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
                return Row(button = Fluent.Button(Box().Width(8).Height(8), () => { }));
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
        Assert.Equal((50f, 20f), (padded.Width, padded.Height));
        Assert.True(borderLayout);
        Assert.Equal((54f, 24f), (bordered.Width, bordered.Height));
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
    /// - A wrapped app of 1,600 x 800 that asks for a theme from a signal: 25 rows of 40 buttons, every fifth primary, around boxes
    ///   of 8 x 8; an effect that counts the changes of the theme it reads. The light and the dark theme are made before.
    ///
    /// Steps:
    /// - Under <c>PerformanceAssert.DoesNotAllocate</c>, 100 runs, its unmeasured window and the windows it runs again included:
    ///   set the dark theme on an even index and the light one on an odd index (each run changes the theme, since the indices of
    ///   the runs alternate), and pump.
    ///
    /// Expected:
    /// - 0 bytes in every window; the effect counted one change for each run, after the first value.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Button_ThousandButtons_LightDarkSwitch_AllocatesNothing()
    {
        FluentTheme light = FluentTheme.Create(ThemeVariant.Light, Density.Standard, null);
        FluentTheme dark = FluentTheme.Create(ThemeVariant.Dark, Density.Standard, null);
        Signal<FluentTheme> window = Signal.Create(light);
        int changes = 0;
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
                return ThousandButtons(out _);
            },
            Options(Light) with
            {
                Width = 1600,
                Height = 800,
            }
        );
        _ = app.PumpAndSettle();

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
    /// - The same 1,000 buttons in a wrapped app of 1,600 x 800, light, settled; the centers of the first two buttons.
    ///
    /// Steps:
    /// - Under <c>PerformanceAssert.DoesNotAllocate</c>, 1,000 runs, its unmeasured window and the windows it runs again included:
    ///   move the pointer to the first button's center on an even index and to the second's on an odd index, and pump. Then move
    ///   it to the first button and pump.
    ///
    /// Expected:
    /// - 0 bytes in every window; one frame was presented for each run; at the end the first button is hovered and the second is
    ///   not.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Button_HoverFrames_AllocateNothing()
    {
        Element[] firstTwo = [];
        await using HeadlessApp app = await HeadlessApp.StartAsync(
            () => ThousandButtons(out firstTwo),
            Options(Light) with
            {
                Width = 1600,
                Height = 800,
            }
        );
        _ = app.PumpAndSettle();
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
    /// A standard, a primary, and a disabled button match their golden in each variant (design §13.2, "確かめ方").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A wrapped app of 280 x 72 on WARP, of a row with a padding of 16 and a gap of 8: a standard button "Save", a primary button
    ///   "Send", and a disabled standard button "Off". The OS's appearance is light, dark, or Windows' black high-contrast theme,
    ///   with no accent color.
    ///
    /// Steps:
    /// - Pump; compare the frame with the variant's golden.
    ///
    /// Expected:
    /// - The frame matches fluent-button-light.png, fluent-button-dark.png, or fluent-button-high-contrast.png.
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
        await using HeadlessApp app = await HeadlessApp.StartAsync(
            () =>
                Row(
                        Fluent.Button(Elements.Text("Save"), () => { }),
                        Fluent.Button(Elements.Text("Send"), () => { }, ButtonAppearance.Primary),
                        Fluent.Button(Elements.Text("Off"), () => { }).Disabled()
                    )
                    .Padding(16)
                    .Gap(8),
            Options(os) with
            {
                Width = 280,
                Height = 72,
                Renderer = () => new WarpRenderer(),
            }
        );
        _ = app.PumpAndSettle();

        await app.MatchesGoldenAsync(golden);
    }

    // 25 rows of 40 buttons around boxes of 8 x 8, every fifth primary, in a column; the first two buttons are handed out.
    private static Element ThousandButtons(out Element[] firstTwo)
    {
        var rows = new Element[25];
        var cells = new Element[40];
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

        return Column(rows);
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
