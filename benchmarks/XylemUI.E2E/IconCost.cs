using System.Diagnostics;
using XylemUI.Platform.Windows;
using XylemUI.Primitives;
using XylemUI.Text;
using XylemUI.Theme.Fluent;
using static XylemUI.Elements;
using static XylemUI.Primitives.Primitive;
using NodeHandle = XylemUI.Handle<XylemUI.UiNode>;

namespace XylemUI.E2E;

// The app of 100 icons of the acceptance of row IC1 (design §5.0.4, "Icons"): 50 Regular and 50 Filled glyphs, of 16, 20, and 24
// px, picked from the two maps with a fixed seed (724), drawn as a wrapped row in a window of 420 x 360 with no GPU. This scene is
// the only code of the executable that reads FluentIcons, so the fonts the build kept in it hold these 100 glyphs. It reports their
// sizes, and the time to make the tree and lay out and draw its first frame, which registers the two fonts.
internal static class IconCost
{
    public static int Run()
    {
        UiThread.Initialize();
        using var report = new Report("icons");
        report.Number("regular_bytes", ResourceLength("XylemUI.Icons.Regular.ttf"));
        report.Number("filled_bytes", ResourceLength("XylemUI.Icons.Filled.ttf"));
        using var fonts = new DirectWriteFontProvider();
        var atlas = new GlyphAtlas(static () => new DirectWriteGlyphRasterizer());
        var tree = new UiTree(new TextContext(fonts));
        NodeHandle root = tree.CreateNode(new LayoutStyle());
        var frames = new WindowFrames(tree, root, new NoTarget(), atlas, new NoScheduler(), default);
        long start = Stopwatch.GetTimestamp();
        UiTree.Current = tree;
        try
        {
            _ = Scope.Create(() => tree.AppendChild(root, Icons().Node));
        }
        finally
        {
            UiTree.Current = null;
        }

        frames.Resize(new Size(420, 360), 1);
        frames.RunFrame();
        report.Number("first_frame_ms", Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        return 0;
    }

    private static Element Icons()
    {
        IconGlyph[] glyphs =
        [
            FluentIcons.Target24Regular,
            FluentIcons.DocumentLink20Regular,
            FluentIcons.TextColumnTwoRight20Regular,
            FluentIcons.ArrowMinimize24Regular,
            FluentIcons.TableResizeColumn24Regular,
            FluentIcons.BuildingDesktop16Regular,
            FluentIcons.SportHockey24Regular,
            FluentIcons.DocumentDatabase24Regular,
            FluentIcons.CloudArrowUp24Regular,
            FluentIcons.LaptopCheckmark20Regular,
            FluentIcons.TrayItemRemove24Regular,
            FluentIcons.Eye24Regular,
            FluentIcons.BuildingGovernmentSearch20Regular,
            FluentIcons.TicketHorizontal20Regular,
            FluentIcons.ClockBill24Regular,
            FluentIcons.SplitVertical24Regular,
            FluentIcons.ReOrderVertical24Regular,
            FluentIcons.ImageAltText24Regular,
            FluentIcons.DeviceMeetingRoom24Regular,
            FluentIcons.WeatherRainShowersNight20Regular,
            FluentIcons.Fps24020Regular,
            FluentIcons.Hexagon24Regular,
            FluentIcons.DocumentFlowchart20Regular,
            FluentIcons.QuizNew24Regular,
            FluentIcons.PersonPill24Regular,
            FluentIcons.EmojiMultiple20Regular,
            FluentIcons.LocationArrowLeft16Regular,
            FluentIcons.Fingerprint24Regular,
            FluentIcons.Megaphone16Regular,
            FluentIcons.Apps24Regular,
            FluentIcons.KeyboardLayoutResize24Regular,
            FluentIcons.CommentDismiss20Regular,
            FluentIcons.ArrowWrapOff20Regular,
            FluentIcons.ShapeExclude20Regular,
            FluentIcons.CodeCsRectangle16Regular,
            FluentIcons.WindowAd20Regular,
            FluentIcons.PresenceOof24Regular,
            FluentIcons.NotebookSync24Regular,
            FluentIcons.CalendarMultiple20Regular,
            FluentIcons.LinkAdd24Regular,
            FluentIcons.StreamInputOutput20Regular,
            FluentIcons.ArrowCurveDownRight20Regular,
            FluentIcons.EmojiMeh20Regular,
            FluentIcons.PeopleStar20Regular,
            FluentIcons.PrintAdd20Regular,
            FluentIcons.VideoPerson16Regular,
            FluentIcons.LineDashes20Regular,
            FluentIcons.ChannelShare16Regular,
            FluentIcons.BuildingGovernment16Regular,
            FluentIcons.Bookmark16Regular,
            FluentIcons.UsbOff24Filled,
            FluentIcons.HomeAdd24Filled,
            FluentIcons.HomeDatabase20Filled,
            FluentIcons.Pair16Filled,
            FluentIcons.CellularData524Filled,
            FluentIcons.TextQuote16Filled,
            FluentIcons.ArrowSyncDismiss24Filled,
            FluentIcons.DocumentTableSearch24Filled,
            FluentIcons.TableStackAbove24Filled,
            FluentIcons.ArrowClockwiseDashes24Filled,
            FluentIcons.LayoutColumnFourFocusCenterRight24Filled,
            FluentIcons.Gauge20Filled,
            FluentIcons.DocumentTableCube20Filled,
            FluentIcons.CircleSparkle20Filled,
            FluentIcons.TableCellCrossBrush24Filled,
            FluentIcons.BookDatabase24Filled,
            FluentIcons.CloudOff16Filled,
            FluentIcons.DocumentCopy20Filled,
            FluentIcons.CheckboxChecked24Filled,
            FluentIcons.Snooze16Filled,
            FluentIcons.PauseCircle20Filled,
            FluentIcons.DocumentLink24Filled,
            FluentIcons.PenDismiss20Filled,
            FluentIcons.ArrowReply16Filled,
            FluentIcons.VehicleCarProfile20Filled,
            FluentIcons.ClockSparkle24Filled,
            FluentIcons.TextParagraph24Filled,
            FluentIcons.StreetSign20Filled,
            FluentIcons.DocumentTableArrowRight20Filled,
            FluentIcons.BuildingDesktop20Filled,
            FluentIcons.MegaphoneLoud16Filled,
            FluentIcons.DataSunburst20Filled,
            FluentIcons.SportBaseball24Filled,
            FluentIcons.WeatherRainShowersDay24Filled,
            FluentIcons.Shifts20Filled,
            FluentIcons.FolderLightning16Filled,
            FluentIcons.StickerAdd24Filled,
            FluentIcons.PresenterOff20Filled,
            FluentIcons.CreditCardClock24Filled,
            FluentIcons.BorderTopBottom24Filled,
            FluentIcons.ArrowReplyAll16Filled,
            FluentIcons.DesktopTower20Filled,
            FluentIcons.Rss16Filled,
            FluentIcons.TargetArrow20Filled,
            FluentIcons.TableOffsetLessThanOrEqualTo20Filled,
            FluentIcons.DocumentCode16Filled,
            FluentIcons.ChannelAlert24Filled,
            FluentIcons.DatabaseArrowUp20Filled,
            FluentIcons.Resize24Filled,
            FluentIcons.LayoutRowTwoSplitBottom24Filled,
        ];

        // Five rows of 20: a row of 20 icons of at most 24 px fits the window's width.
        var rows = new Element[5];
        for (int r = 0; r < rows.Length; r++)
        {
            var row = new Element[20];
            for (int i = 0; i < row.Length; i++)
            {
                row[i] = Icon(glyphs[(r * 20) + i]);
            }

            rows[r] = Row(row);
        }

        return Column(rows);
    }

    private static long ResourceLength(string name)
    {
        using Stream? stream = typeof(IconCost).Assembly.GetManifestResourceStream(name);
        return stream?.Length ?? -1;
    }

    private sealed class NoTarget : IRenderTarget
    {
        public void Draw(ReadOnlySpan<DisplayList> lists, Color clearColor, GlyphAtlas atlas, TextureCache images) { }

        public void Present() { }
    }

    private sealed class NoScheduler : IFrameScheduler
    {
        public void RequestFrame(Action callback) { }

        public void CancelFrame() { }
    }
}
