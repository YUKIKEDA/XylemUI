namespace XylemUI.Text.Tests;

// The links in a text leaf (row T12, design §4.8 and §7, "Links in text"): the enter and the leave of the link under the mouse,
// its hovered look, the hand cursor before the leaf's, the click of a press and a release over it in place of the leaf's click,
// a touch's tap, and no allocation. The text is 14 px: "nn" (0 to 17.08), "nnnnnn" in a link (to 68.32), "nn"; a line is 20.272
// high.
[Collection(UiThreadGroup.Name)]
public sealed class TextLinkTests(UiThreadFixture ui) : IDisposable
{
    private static readonly SpanStyle Hovered = new(Decoration: new TextDecoration(DecorationLines.Underline));
    private readonly Font _font = TestFont.Open();
    private readonly List<string> _log = [];

    public void Dispose() => _font.Dispose();

    /// <summary>
    /// A mouse that moves over a link enters it, takes the hand cursor, and draws the link in its hovered look; one that moves off
    /// it leaves it, and the look and the leaf's cursor come back. Moves inside the link send nothing more.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The text, its leaf with the I-beam cursor, its link with an enter, a leave, and an underline while hovered.
    ///
    /// Steps:
    /// - Move the mouse to (5, 10), (25, 10), (30, 10), then (5, 10); draw after each.
    ///
    /// Expected:
    /// - At (5, 10) the I-beam and nothing logged. At (25, 10) "enter" with the leaf as its target, the hand, the hovered
    ///   link, and an underline drawn. At (30, 10) nothing more. At (5, 10) "leave", the I-beam, no hovered link, no line.
    /// </remarks>
    [Fact]
    public void Hover_OverALink_EntersItAndTakesTheHand() =>
        ui.Invoke(() =>
        {
            using var scene = new TextScene(_font);
            Element text = Mount(scene, cursor: CursorKind.IBeam);
            TextRanges ranges = scene.Leaf(text).Ranges!;

            scene.Mouse(PointerInputKind.Move, 5, 10);
            Assert.Equal(CursorKind.IBeam, scene.Pointer.Cursor);
            Assert.Empty(_log);

            scene.Mouse(PointerInputKind.Move, 25, 10);
            scene.Draw();
            Assert.Equal(["enter " + text.Node], _log);
            Assert.Equal(CursorKind.Hand, scene.Pointer.Cursor);
            Assert.Equal(0, ranges.HoveredLink);
            Assert.Contains(scene.Commands(), c => c.Tag == CommandTag.Line);

            scene.Mouse(PointerInputKind.Move, 30, 10);
            Assert.Single(_log);

            scene.Mouse(PointerInputKind.Move, 5, 10);
            scene.Draw();
            Assert.Equal(["enter " + text.Node, "leave " + text.Node], _log);
            Assert.Equal(CursorKind.IBeam, scene.Pointer.Cursor);
            Assert.Equal(-1, ranges.HoveredLink);
            Assert.DoesNotContain(scene.Commands(), c => c.Tag == CommandTag.Line);
        });

    /// <summary>
    /// A press and a release of the left button over the same link click it, and the leaf's click is not sent; a release off the
    /// link does not click it, and the leaf's click is sent as it is for any press and release over the leaf.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The text, its leaf with a click handler, its link with a click.
    ///
    /// Steps:
    /// - Press at (20, 10), release at (30, 10). Press at (20, 10), release at (5, 10).
    ///
    /// Expected:
    /// - "click", then "leaf click".
    /// </remarks>
    [Fact]
    public void Click_PressAndReleaseOverTheLink_ClicksItInPlaceOfTheLeaf() =>
        ui.Invoke(() =>
        {
            using var scene = new TextScene(_font);
            _ = Mount(scene, onLeafClick: () => _log.Add("leaf click"));

            scene.Mouse(PointerInputKind.Move, 20, 10);
            scene.Mouse(PointerInputKind.Down, 20, 10, PointerButton.Left);
            scene.Mouse(PointerInputKind.Move, 30, 10);
            scene.Mouse(PointerInputKind.Up, 30, 10, PointerButton.Left);
            scene.Mouse(PointerInputKind.Down, 20, 10, PointerButton.Left);
            scene.Mouse(PointerInputKind.Up, 5, 10, PointerButton.Left);

            Assert.Equal(["click", "leaf click"], _log.Where(l => l.Contains("click", StringComparison.Ordinal)));
        });

    /// <summary>
    /// A touch's tap on a link clicks it, with no enter and no leave; a touch that moves past the slop does not.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The text and its link.
    ///
    /// Steps:
    /// - Touch down and up at (25, 10). Touch down at (25, 10), move to (50, 10) on the link, past the slop of 18, and up there.
    ///
    /// Expected:
    /// - "click" alone.
    /// </remarks>
    [Fact]
    public void Tap_OnALink_ClicksIt() =>
        ui.Invoke(() =>
        {
            using var scene = new TextScene(_font);
            _ = Mount(scene);

            scene.Touch(PointerInputKind.Down, 25, 10);
            scene.Touch(PointerInputKind.Up, 25, 10);
            scene.Touch(PointerInputKind.Down, 25, 10);
            scene.Touch(PointerInputKind.Move, 50, 10);
            scene.Touch(PointerInputKind.Up, 50, 10);

            Assert.Equal(["click"], _log);
        });

    /// <summary>
    /// A builder that writes the text again while its link is hovered, with another look for another range, keeps the link
    /// hovered and drawn in its hovered look.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A text "nn", a link "nn" with an underline while hovered, and "nn" in a range whose color a signal gives.
    ///
    /// Steps:
    /// - Move onto the link; draw. Set the signal to another color; draw.
    ///
    /// Expected:
    /// - The link is still the hovered one, and an underline is drawn.
    /// </remarks>
    [Fact]
    public void Rewrite_WhileHovered_KeepsTheHoveredLook() =>
        ui.Invoke(() =>
        {
            using var scene = new TextScene(_font);
            var color = Signal.Create(Color.FromRgb(0xFF0000));
            PointerHandler none = (in PointerEvent e) => { };
            Element text = default;
            scene.Mount(() =>
                text = Elements.Text(
                    (ref w) =>
                    {
                        w.Write("nn".AsSpan());
                        using (w.Link(none, hovered: Hovered))
                        {
                            w.Write("nn".AsSpan());
                        }

                        using (w.Style(new SpanStyle(Color: color.Get())))
                        {
                            w.Write("nn".AsSpan());
                        }
                    }
                )
            );

            scene.Mouse(PointerInputKind.Move, 25, 10);
            scene.Draw();
            color.Set(Color.FromRgb(0x0000FF));
            scene.Draw();

            Assert.Equal(0, scene.Leaf(text).Ranges!.HoveredLink);
            Assert.Contains(scene.Commands(), c => c.Tag == CommandTag.Line);
        });

    /// <summary>
    /// Hovering a link and moving off it, and clicking it, allocate nothing once the text is drawn.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The text and its link, whose handlers record nothing; warmed up with the iteration.
    ///
    /// Steps:
    /// - 1,000 iterations in the windows of <c>SteadyStateAllocation</c>: move onto the link, press and release on it, draw, move
    ///   off it, draw.
    ///
    /// Expected:
    /// - 0 bytes in every window.
    /// </remarks>
    [Fact]
    public void SteadyState_HoverAndClick_AllocateNothing() =>
        ui.Invoke(() =>
        {
            using var scene = new TextScene(_font);
            PointerHandler none = (in PointerEvent e) => { };
            scene.Mount(() =>
                Elements.Text(
                    (ref w) =>
                    {
                        w.Write("nn".AsSpan());
                        using (w.Link(none, none, none, Hovered))
                        {
                            w.Write("nn".AsSpan());
                        }
                    }
                )
            );

            void Iteration(int index)
            {
                scene.Mouse(PointerInputKind.Move, 25, 10);
                scene.Mouse(PointerInputKind.Down, 25, 10, PointerButton.Left);
                scene.Mouse(PointerInputKind.Up, 25, 10, PointerButton.Left);
                scene.Draw();
                scene.Mouse(PointerInputKind.Move, 5, 10);
                scene.Draw();
            }

            for (int i = 0; i < 50; i++)
            {
                Iteration(i);
            }

            XylemUI.Tests.SteadyStateAllocation.AssertNone(1_000, Iteration);
        });

    // The text of the tests: "nn", a link "nnnnnn", "nn".
    private Element Mount(TextScene scene, CursorKind? cursor = null, Action? onLeafClick = null)
    {
        Element text = default;
        scene.Mount(() =>
        {
            text = Elements.Text(
                (ref w) =>
                {
                    w.Write("nn".AsSpan());
                    using (
                        w.Link(
                            (in PointerEvent e) => _log.Add("click"),
                            (in PointerEvent e) => _log.Add("enter " + e.Target.Node),
                            (in PointerEvent e) => _log.Add("leave " + e.Target.Node),
                            Hovered
                        )
                    )
                    {
                        w.Write("nnnnnn".AsSpan());
                    }

                    w.Write("nn".AsSpan());
                }
            );
            if (cursor is { } kind)
            {
                text = text.Cursor(kind);
            }

            if (onLeafClick is not null)
            {
                text = text.OnClick(onLeafClick);
            }

            return text;
        });
        return text;
    }
}
