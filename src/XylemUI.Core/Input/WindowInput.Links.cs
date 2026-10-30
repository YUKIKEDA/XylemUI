using System.Numerics;

namespace XylemUI;

// The links in the text of a leaf (design §4.8 and §7, "Links in text", row T12): which one a pointer is over, its enter and its
// leave, its hovered look, the hand cursor, and its click.
internal sealed partial class WindowInput
{
    // The link the pointer is over: in the text of the node hit, while no other node holds the pointer's capture, the link of the
    // cluster whose box holds the point (Flutter's RenderParagraph.hitTestChildren). -1 when none.
    private int LinkAt(PointerState pointer, in PointerInput input)
    {
        NodeHandle hit = pointer.Hit;
        if (!pointer.Inside || hit.IsNone || !_tree.Nodes.IsAlive(hit))
        {
            return -1;
        }

        if (_tree.Nodes.GetRef(hit).Text is not { Ranges: { HasLinks: true } ranges } leaf || ranges.HitBuild != _frames.Builds)
        {
            return -1;
        }

        if (CapturedNode(pointer) is { IsNone: false } captured && captured != hit)
        {
            return -1;
        }

        Vector2 local = _frames.List.PointInRegion(ranges.HitEntry, new Vector2(input.X, input.Y)) / _frames.Scale;
        int cluster = TextCaret.ClusterAt(leaf, local.X - leaf.PaddingBorder.Left, local.Y - leaf.PaddingBorder.Top);
        if (cluster < 0)
        {
            return -1;
        }

        foreach (PaintRun run in ranges.Paint)
        {
            if (cluster < run.Start + run.Length)
            {
                return run.Link;
            }
        }

        return -1;
    }

    // Finds the pointer's link after its node hit changed. When it is another one, a pointer that hovers leaves the one before:
    // its leave, and its look back. Returns whether the pointer entered a link, whose enter comes after the nodes' (Flutter's
    // MouseTracker sends the enters from the outermost annotation in, and the leaves from the innermost out).
    private bool FindLink(PointerState pointer, in PointerInput input)
    {
        // A tree with no link, under a pointer over none, has nothing to find (the review of PR #426: the click was 0.03 µs longer).
        if (_tree.LinkLeaves == 0 && pointer.Link < 0)
        {
            return false;
        }

        int link = LinkAt(pointer, input);
        NodeHandle leaf = link >= 0 ? pointer.Hit : default;
        if (leaf == pointer.LinkLeaf && link == pointer.Link)
        {
            return false;
        }

        NodeHandle before = pointer.LinkLeaf;
        PointerHandler? onLeave = pointer.LinkLeave;
        pointer.LinkLeaf = leaf;
        pointer.Link = link;
        pointer.LinkLeave = link >= 0 ? RangesOf(leaf)!.Links[link].OnLeave : null;
        if (!pointer.Hovers)
        {
            return false;
        }

        if (!before.IsNone && _tree.Nodes.IsAlive(before))
        {
            if (RangesOf(before) is { } ranges && ranges.HoveredLink >= 0 && !HoveredByAnother(pointer, before, ranges.HoveredLink))
            {
                Repaint(before, ranges, ranges.HoveredLink);
                ranges.HoveredLink = -1;
            }

            CallLink(onLeave, PointerEventKind.Leave, before, input);
        }

        return link >= 0;
    }

    // The hovered look of the pointer's link, and its enter; none when a node's enter removed the leaf or wrote it again without
    // the link.
    private void EnterLink(PointerState pointer, in PointerInput input)
    {
        NodeHandle leaf = pointer.LinkLeaf;
        if (pointer.Link < 0 || RangesOf(leaf) is not { } ranges || pointer.Link >= ranges.Links.Length)
        {
            return;
        }

        ranges.HoveredLink = pointer.Link;
        Repaint(leaf, ranges, pointer.Link);
        CallLink(ranges.Links[pointer.Link].OnEnter, PointerEventKind.Enter, leaf, input);
    }

    // Whether a pointer other than this one is over the link.
    private bool HoveredByAnother(PointerState pointer, NodeHandle leaf, int link)
    {
        for (int p = 0; _livePointers > 1 && p < _pointers.Length; p++)
        {
            PointerState other = _pointers[p];
            if (other != pointer && other.Live && other.Hovers && other.LinkLeaf == leaf && other.Link == link)
            {
                return true;
            }
        }

        return false;
    }

    // At a release of the left button: when the press and the release were over the same link, and a touch did not move past the
    // slop, the link's click, in place of the node's (design §4.8, as GPUI's InteractiveText.on_click). Returns whether it was.
    private bool ClickLink(PointerState pointer, bool tapped, in PointerInput input)
    {
        NodeHandle pressed = pointer.PressedLinkLeaf;
        int link = pointer.PressedLink;
        pointer.PressedLinkLeaf = default;
        pointer.PressedLink = -1;
        if (link < 0 || !tapped || pressed != pointer.LinkLeaf || link != pointer.Link || RangesOf(pressed) is not { } ranges)
        {
            return false;
        }

        CallLink(ranges.Links[link].OnClick, PointerEventKind.Click, pressed, input);
        return true;
    }

    private TextRanges? RangesOf(NodeHandle leaf) => _tree.Nodes.IsAlive(leaf) ? _tree.Nodes.GetRef(leaf).Text?.Ranges : null;

    // A change of the hovered link draws the leaf again when the link has a hovered look; the layout stays.
    private void Repaint(NodeHandle leaf, TextRanges ranges, int link)
    {
        if (ranges.Links[link].Hovered != default)
        {
            Invalidation.Invalidate(_tree, leaf, ChangeKind.Color);
        }
    }

    // Calls a link's handler with the leaf as the target and the current target. Its exception goes to the handler of unhandled
    // exceptions, as a node's does.
    private void CallLink(PointerHandler? handler, PointerEventKind kind, NodeHandle leaf, in PointerInput input)
    {
        if (handler is null)
        {
            return;
        }

        float scale = _frames.Scale;
        var physical = new Vector2(input.X, input.Y);
        Vector2 local =
            RangesOf(leaf) is { } ranges && ranges.HitBuild == _frames.Builds
                ? _frames.List.PointInRegion(ranges.HitEntry, physical) / scale
                : new Vector2(float.NaN);
        var e = new PointerEvent(this, kind, leaf, leaf, physical / scale, local, input, _dispatch);
        try
        {
            handler(in e);
        }
        catch (Exception exception) when (exception is not InvariantViolationException)
        {
            ReactiveRuntime.ReportException(exception);
        }
    }
}
