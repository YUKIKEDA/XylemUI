using System.Globalization;
using System.Text;
using System.Xml.Linq;

namespace XylemUI.Tests;

// Taffy's XML layout tests (Fixtures/Taffy, Chrome's results, MIT) and XylemUI's in the same form (Fixtures/XylemUI, made from
// Chrome with Taffy's gentest): reads a test, builds the UI tree it describes, lays it out in the test's viewport, and compares
// the rounded rectangles with Chrome's (design §3.2, the reference of the layout tests).
internal static class TaffyFixture
{
    // Ahem: every character is a 10 x 10 square; a zero-width space is the only break opportunity (Taffy's test measure).
    private const float AhemSize = 10;
    private const char ZeroWidthSpace = '​';

    private static readonly string Directory = Path.Combine(AppContext.BaseDirectory, "Fixtures");
    private static readonly Dictionary<string, XElement> Tests = Load();

    public static IEnumerable<string> Names => Tests.Keys.Order(StringComparer.Ordinal);

    // Why a test is left out, or null when it is in the scope of the rows done so far (TaffyFixtureScope).
    public static string? ExclusionOf(string name) => TaffyFixtureScope.ExclusionOf(name, Tests[name]);

    // Lays the test out and returns the differences from Chrome's results, one line each; empty when it matches.
    public static IReadOnlyList<string> Run(string name)
    {
        XElement test = Tests[name];
        var tree = new UiTree();
        XElement rootElement = test.Element("input")!.Elements().First();
        NodeHandle root = Build(tree, rootElement);
        XElement viewport = test.Element("viewport")!;
        Invalidation.NotifyViewport(
            tree,
            root,
            new AvailableSize(ParseViewport(viewport.Attribute("width")?.Value), ParseViewport(viewport.Attribute("height")?.Value))
        );
        LayoutEngine.RunLayoutPass(tree);

        var differences = new List<string>();
        bool round = test.Attribute("use-rounding")?.Value != "false";
        Compare(tree, root, test.Element("expectations")!.Element("node")!, 0, 0, round, "root", differences);
        return differences;
    }

    private static Dictionary<string, XElement> Load()
    {
        var tests = new Dictionary<string, XElement>(StringComparer.Ordinal);
        foreach (string file in System.IO.Directory.EnumerateFiles(Directory, "*.xml", SearchOption.AllDirectories))
        {
            string group = Path.GetFileNameWithoutExtension(file);
            foreach (XElement test in XDocument.Load(file).Root!.Elements("test"))
            {
                tests.Add($"{group}/{test.Attribute("name")!.Value}", test);
            }
        }

        return tests;
    }

    private static NodeHandle Build(UiTree tree, XElement element)
    {
        LayoutStyle style = StyleOf(element);
        if (element.Elements().Any())
        {
            NodeHandle node = tree.CreateNode(style);
            foreach (XElement child in element.Elements())
            {
                tree.AppendChild(node, Build(tree, child));
            }

            return node;
        }

        string text = element.Value.Trim();
        return text.Length == 0 ? tree.CreateNode(style) : tree.CreateNode(style, AhemMeasure(text), AhemWidths(text));
    }

    // The content widths of Ahem text, as AhemMeasure gives them under a min-content and a max-content width; they do not
    // depend on the height.
    private static IntrinsicWidthsFunc AhemWidths(string text)
    {
        string[] runs = text.Split(ZeroWidthSpace);
        float longest = runs.Max(run => run.Length) * AhemSize;
        float total = runs.Sum(run => run.Length) * AhemSize;
        return _ => new IntrinsicWidths(longest, total, DependsOnHeight: false);
    }

    // Taffy's test measure for Ahem text: the min-content width is the longest run between break opportunities, the max-content
    // width all of them, and the height ten pixels a line at the width given.
    private static MeasureFunc AhemMeasure(string text)
    {
        string[] runs = text.Split(ZeroWidthSpace);
        int longest = runs.Max(run => run.Length);
        int total = runs.Sum(run => run.Length);
        return (known, available) =>
        {
            if (known.IsComplete)
            {
                return MeasureResult.Of(known.Width, known.Height);
            }

            float fromSpace = available.Width.Kind switch
            {
                AvailableSpaceKind.MinContent => longest * AhemSize,
                AvailableSpaceKind.MaxContent => total * AhemSize,
                _ => Math.Min(available.Width.Value, total * AhemSize),
            };
            float width = Math.Max(Maybe.Or(known.Width, fromSpace), longest * AhemSize);
            float height = known.Height;
            if (float.IsNaN(height))
            {
                int perLine = (int)Math.Floor(width / AhemSize);
                int lines = 1;
                int current = 0;
                foreach (string run in runs)
                {
                    if (current + run.Length > perLine)
                    {
                        if (current > 0)
                        {
                            lines++;
                        }

                        current = run.Length;
                    }
                    else
                    {
                        current += run.Length;
                    }
                }

                height = lines * AhemSize;
            }

            return MeasureResult.Of(width, height);
        };
    }

    private static LayoutStyle StyleOf(XElement e) =>
        new()
        {
            Algorithm = e.Attribute("display")?.Value is "block" or "flow-root"
                ? new LayoutAlgorithm.Block(
                    new BlockProps
                    {
                        JustifyItems = ParseAlignItems(e, "justify-items") ?? default,
                        AlignContent = ParseAlignContent(e, "align-content"),
                    }
                )
                : new LayoutAlgorithm.Flex(
                    new FlexProps
                    {
                        Direction = e.Attribute("flex-direction")?.Value switch
                        {
                            "column" => FlexDirection.Column,
                            "row-reverse" => FlexDirection.RowReverse,
                            "column-reverse" => FlexDirection.ColumnReverse,
                            _ => FlexDirection.Row,
                        },
                        Wrap = e.Attribute("flex-wrap")?.Value switch
                        {
                            "wrap" => FlexWrap.Wrap,
                            "wrap-reverse" => FlexWrap.WrapReverse,
                            _ => FlexWrap.NoWrap,
                        },
                        JustifyContent = ParseAlignContent(e, "justify-content"),
                        AlignItems = ParseAlignItems(e, "align-items") ?? default,
                        AlignContent = ParseAlignContent(e, "align-content"),
                        ColumnGap = ParseLengthPercentage(e.Attribute("column-gap")?.Value),
                        RowGap = ParseLengthPercentage(e.Attribute("row-gap")?.Value),
                    }
                ),
            Position = e.Attribute("position")?.Value == "absolute" ? Position.Absolute : Position.Relative,
            Inset = new(
                ParseLengthPercentageAuto(e, "left"),
                ParseLengthPercentageAuto(e, "top"),
                ParseLengthPercentageAuto(e, "right"),
                ParseLengthPercentageAuto(e, "bottom")
            ),
            AspectRatio = ParseFloat(e.Attribute("aspect-ratio")?.Value, float.NaN),
            Hidden = e.Attribute("display")?.Value == "none",
            AlignSelf = ParseAlignItems(e, "align-self"),
            JustifySelf = ParseAlignItems(e, "justify-self"),
            Width = ParseLength(e, "width"),
            Height = ParseLength(e, "height"),
            MinWidth = ParseLength(e, "min-width"),
            MinHeight = ParseLength(e, "min-height"),
            MaxWidth = ParseLength(e, "max-width"),
            MaxHeight = ParseLength(e, "max-height"),
            Padding = ParseInsets(e, "padding"),
            Border = ParseInsets(e, "border"),
            Margin = new(
                ParseLengthPercentageAuto(e, "margin-left", zero: true),
                ParseLengthPercentageAuto(e, "margin-top", zero: true),
                ParseLengthPercentageAuto(e, "margin-right", zero: true),
                ParseLengthPercentageAuto(e, "margin-bottom", zero: true)
            ),
            FlexGrow = ParseFloat(e.Attribute("flex-grow")?.Value, 0),
            FlexShrink = ParseFloat(e.Attribute("flex-shrink")?.Value, 1),
            FlexBasis = e.Attribute("flex-basis")?.Value == "content" ? Length.Content : ParseLength(e, "flex-basis"),
            OverflowX = ParseOverflow(e.Attribute("overflow-x")?.Value),
            OverflowY = ParseOverflow(e.Attribute("overflow-y")?.Value),
            ScrollbarWidth = ParseFloat(e.Attribute("scrollbar-width")?.Value, 0),
        };

    private static Overflow ParseOverflow(string? value) =>
        value switch
        {
            null or "visible" => Overflow.Visible,
            "clip" => Overflow.Clip,
            "hidden" => Overflow.Hidden,
            "scroll" => Overflow.Scroll,
            _ => throw new InvalidOperationException($"The overflow {value} is not read by these tests."),
        };

    private static Length ParseLength(XElement e, string name) =>
        e.Attribute(name)?.Value switch
        {
            null or "auto" => Length.Auto,
            "min-content" => Length.MinContent,
            "max-content" => Length.MaxContent,
            "fit-content" => Length.FitContent,
            "stretch" => Length.Stretch,
            string value when value.EndsWith('%') => Length.Percent(ParseFloat(value[..^1], 0) / 100),
            string value => Length.Px(ParsePx(value)),
        };

    // An absent inset is auto; an absent margin is 0.
    private static LengthPercentageAuto ParseLengthPercentageAuto(XElement e, string name, bool zero = false) =>
        e.Attribute(name)?.Value switch
        {
            null when zero => LengthPercentageAuto.Px(0),
            null or "auto" => LengthPercentageAuto.Auto,
            string value when value.EndsWith('%') => LengthPercentageAuto.Percent(ParseFloat(value[..^1], 0) / 100),
            string value => LengthPercentageAuto.Px(ParsePx(value)),
        };

    private static EdgeInsets<LengthPercentage> ParseInsets(XElement e, string prefix) =>
        new(
            ParseLengthPercentage(e.Attribute($"{prefix}-left")?.Value),
            ParseLengthPercentage(e.Attribute($"{prefix}-top")?.Value),
            ParseLengthPercentage(e.Attribute($"{prefix}-right")?.Value),
            ParseLengthPercentage(e.Attribute($"{prefix}-bottom")?.Value)
        );

    private static LengthPercentage ParseLengthPercentage(string? value) =>
        value switch
        {
            null => LengthPercentage.Zero,
            _ when value.EndsWith('%') => LengthPercentage.Percent(ParseFloat(value[..^1], 0) / 100),
            _ => LengthPercentage.Px(ParsePx(value)),
        };

    // "auto" (an item's alignment that follows its container) and an absent alignment are null.
    private static AlignItems? ParseAlignItems(XElement e, string name)
    {
        string? value = e.Attribute(name)?.Value;
        if (value is null or "auto")
        {
            return null;
        }

        (bool safe, string keyword) = SplitSafety(value);
        return new AlignItems(
            keyword switch
            {
                "normal" => AlignItemsKeyword.Normal,
                "start" => AlignItemsKeyword.Start,
                "end" => AlignItemsKeyword.End,
                "flex-start" => AlignItemsKeyword.FlexStart,
                "flex-end" => AlignItemsKeyword.FlexEnd,
                "center" => AlignItemsKeyword.Center,
                "baseline" => AlignItemsKeyword.Baseline,
                "stretch" => AlignItemsKeyword.Stretch,
                _ => throw new InvalidOperationException($"The alignment {value} is not read by these tests."),
            },
            safe
        );
    }

    private static AlignContent ParseAlignContent(XElement e, string name)
    {
        string? value = e.Attribute(name)?.Value;
        if (value is null)
        {
            return default;
        }

        (bool safe, string keyword) = SplitSafety(value);
        return new AlignContent(
            keyword switch
            {
                "normal" => AlignContentKeyword.Normal,
                "start" => AlignContentKeyword.Start,
                "end" => AlignContentKeyword.End,
                "flex-start" => AlignContentKeyword.FlexStart,
                "flex-end" => AlignContentKeyword.FlexEnd,
                "center" => AlignContentKeyword.Center,
                "stretch" => AlignContentKeyword.Stretch,
                "space-between" => AlignContentKeyword.SpaceBetween,
                "space-around" => AlignContentKeyword.SpaceAround,
                "space-evenly" => AlignContentKeyword.SpaceEvenly,
                _ => throw new InvalidOperationException($"The distribution {value} is not read by these tests."),
            },
            safe
        );
    }

    private static (bool Safe, string Keyword) SplitSafety(string value) =>
        value.StartsWith("safe ", StringComparison.Ordinal) ? (true, value[5..])
        : value.StartsWith("unsafe ", StringComparison.Ordinal) ? (false, value[7..])
        : (false, value);

    private static AvailableSpace ParseViewport(string? value) =>
        value switch
        {
            null or "max-content" => AvailableSpace.MaxContent,
            "min-content" => AvailableSpace.MinContent,
            _ => AvailableSpace.Definite(ParsePx(value)),
        };

    private static float ParsePx(string value) => ParseFloat(value.EndsWith("px", StringComparison.Ordinal) ? value[..^2] : value, 0);

    private static float ParseFloat(string? value, float fallback) =>
        value is null ? fallback : float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);

    // Taffy's round_layout: positions and sizes rounded on the cumulative position, so that adjacent boxes share an edge.
    private static void Compare(
        UiTree tree,
        NodeHandle node,
        XElement expected,
        float parentX,
        float parentY,
        bool round,
        string path,
        List<string> differences
    )
    {
        Rect rect = tree.GetRect(node);
        float x = parentX + rect.X;
        float y = parentY + rect.Y;
        (float X, float Y, float Width, float Height) actual = round
            ? (Round(x) - Round(parentX), Round(y) - Round(parentY), Round(x + rect.Width) - Round(x), Round(y + rect.Height) - Round(y))
            : (rect.X, rect.Y, rect.Width, rect.Height);
        (float X, float Y, float Width, float Height) want = (
            ParseFloat(expected.Attribute("x")!.Value, 0),
            ParseFloat(expected.Attribute("y")!.Value, 0),
            ParseFloat(expected.Attribute("width")!.Value, 0),
            ParseFloat(expected.Attribute("height")!.Value, 0)
        );
        if (!Near(actual.X, want.X) || !Near(actual.Y, want.Y) || !Near(actual.Width, want.Width) || !Near(actual.Height, want.Height))
        {
            differences.Add($"{path}: expected {Show(want)}, got {Show(actual)} (unrounded {Show((rect.X, rect.Y, rect.Width, rect.Height))})");
        }

        // Chrome's largest scroll position, on the scroll containers Taffy's gentest records it for (design §3.4.1).
        if (expected.Attribute("scroll_width") is { } scrollWidth)
        {
            Size max = tree.MaxScrollOffsetOf(node);
            Size want2 = new(ParseFloat(scrollWidth.Value, 0), ParseFloat(expected.Attribute("scroll_height")!.Value, 0));
            if (!Near(max.Width, want2.Width) || !Near(max.Height, want2.Height))
            {
                differences.Add($"{path}: expected the scroll range {want2.Width} x {want2.Height}, got {max.Width} x {max.Height}");
            }
        }

        int index = 0;
        NodeHandle child = tree.Nodes.GetRef(node).FirstChild;
        foreach (XElement expectedChild in expected.Elements("node"))
        {
            Compare(tree, child, expectedChild, x, y, round, $"{path}/{index}", differences);
            child = tree.Nodes.GetRef(child).NextSibling;
            index++;
        }
    }

    // Rust's f32::round: halves away from zero.
    private static float Round(float value) => MathF.Round(value, MidpointRounding.AwayFromZero);

    // Taffy's own tolerance: Chrome lays out in 1/64 px units and Taffy in floats, so unrounded sizes differ in the second
    // decimal (42.15625 and 42.176 in percentage_moderate_complexity).
    private static bool Near(float actual, float expected) => Math.Abs(actual - expected) < 0.1f;

    private static string Show((float X, float Y, float Width, float Height) r) =>
        new StringBuilder().Append(CultureInfo.InvariantCulture, $"({r.X}, {r.Y}, {r.Width} x {r.Height})").ToString();
}
