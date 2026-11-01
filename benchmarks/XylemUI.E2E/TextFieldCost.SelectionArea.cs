using XylemUI.Platform.Windows;
using XylemUI.Primitives;
using XylemUI.Text;
using NodeHandle = XylemUI.Handle<XylemUI.UiNode>;

namespace XylemUI.E2E;

// The costs of a selection area (row P02b2, design §4.10, "Cost"), in the input report, with no window: an area around a column of
// 1,000 text leaves of "Item n" at 14 px in the Windows fonts, 4 px apart, pressed on the first leaf. Each sample is the input's
// delivery and the frame it asks for, split as the text field's are.
//
// - selection_area_drag_50_leaves_us: a move of the drag alternately onto the 51st leaf and back onto the first, so that each
//   move selects or clears 50 leaves.
// - selection_area_drag_gap_50_leaves_us: the same, the pointer in the gap below the 51st leaf, where the area takes the end of
//   the last leaf before the point from its walk of the display list's hit regions.
// - selection_area_select_all_1000_leaves_us: Control+A; a click on the first leaf after each, untimed, clears it.
// - selection_area_copy_1000_leaves_us: Control+C with every leaf selected, which builds the text for the clipboard.
// - selection_area_frame_1000_leaves_us: a frame that draws the area again with no change of the selection (the root marked to be
//   drawn), the draw stage of its 1,000 leaves, to read the drag's against.
internal static partial class TextFieldCost
{
    private const int AreaLeaves = 1_000;

    private static void RunSelectionArea(Report report)
    {
        using var scene = new AreaScene();
        scene.Press(scene.On(0));
        Write(report, "selection_area_drag_50_leaves", Measure(scene.Tree, scene.Scheduler, scene.DragOnLeaves, null));
        Write(report, "selection_area_drag_gap_50_leaves", Measure(scene.Tree, scene.Scheduler, scene.DragInGap, null));
        scene.Release();
        Write(report, "selection_area_select_all_1000_leaves", Measure(scene.Tree, scene.Scheduler, scene.SelectAll, scene.Clear));
        scene.SelectAll();
        scene.Scheduler.Run();
        Write(report, "selection_area_copy_1000_leaves", Measure(scene.Tree, scene.Scheduler, scene.Copy, scene.CheckCopy));
        Write(report, "selection_area_frame_1000_leaves", Measure(scene.Tree, scene.Scheduler, scene.Redraw, null));
        report.Number("selection_area_leaves", AreaLeaves);
    }

    private sealed class AreaScene : IDisposable
    {
        private readonly DirectWriteFontProvider _fonts = new();
        private readonly DirectWriteGlyphRasterizer _rasterizer = new();
        private readonly GlyphAtlas _atlas;
        private readonly TextContext _context;
        private readonly WindowInput _input;
        private readonly ScopeHandle _scope;
        private readonly NodeHandle[] _leaves = new NodeHandle[AreaLeaves];
        private readonly MemoryClipboard _clipboard = new();
        private TimeSpan _now = TimeSpan.FromSeconds(1);
        private int _moves;

        public AreaScene()
        {
            _atlas = new GlyphAtlas(_rasterizer);
            _context = new TextContext(_fonts);
            Tree = new UiTree(_context) { Clipboard = _clipboard };
            NodeHandle root = Tree.CreateNode(new LayoutStyle());
            var frames = new WindowFrames(Tree, root, new NoTarget(), _atlas, Scheduler, Color.FromRgb(0xFFFFFF));
            UiTree.Current = Tree;
            try
            {
                _scope = Scope.Create(() =>
                {
                    var items = new Element[AreaLeaves];
                    for (int i = 0; i < AreaLeaves; i++)
                    {
                        items[i] = Elements.Text($"Item {i}");
                        _leaves[i] = items[i].Node;
                    }

                    Tree.AppendChild(root, Primitive.SelectionArea(Elements.Column(items).Gap(4)).Node);
                });
            }
            finally
            {
                UiTree.Current = null;
            }

            // Tall enough for every leaf to be drawn, so that each has its hit region.
            frames.Resize(new Size(420, 25_000), 1);
            frames.RunFrame();
            _input = new WindowInput(Tree, frames, new NoPlatform());
            _input.SetActive(true);
        }

        public UiTree Tree { get; }

        public HeldFrames Scheduler { get; } = new();

        // A point on a leaf, 5 px into it, in the window (the area and the column are at its top left).
        public (int X, int Y) On(int leaf) => (10, (int)Tree.GetRect(_leaves[leaf]).Y + 5);

        // A point in the gap below a leaf.
        public (int X, int Y) Below(int leaf)
        {
            Rect rect = Tree.GetRect(_leaves[leaf]);
            return (10, (int)(rect.Y + rect.Height + 2));
        }

        public void Press((int X, int Y) at)
        {
            _input.Handle(Pointer(PointerInputKind.Down, at, PointerButton.Left, PointerButtons.Left));
            Scheduler.Run();
            if (Tree.Focus.Focused != Tree.Nodes.GetRef(Tree.Nodes.GetRef(_leaves[0]).Parent).Parent)
            {
                throw new InvalidOperationException("The press did not give the area the focus.");
            }
        }

        public void Release()
        {
            _input.Handle(Pointer(PointerInputKind.Up, On(0), PointerButton.Left, PointerButtons.None));
            Scheduler.Run();
        }

        // Delivers a move of the drag onto the 51st leaf or back onto the first, and checks that it moved the selection.
        public void DragOnLeaves() => Drag((_moves & 1) == 0 ? On(50) : On(0));

        public void DragInGap() => Drag((_moves & 1) == 0 ? Below(50) : On(0));

        public void SelectAll() => Key(KeyModifiers.Control, global::XylemUI.Key.A);

        // A click on the first leaf, with its frame, which clears the selection; checks that Control+A had selected the last leaf.
        public void Clear()
        {
            if (Selected(AreaLeaves - 1) == 0)
            {
                throw new InvalidOperationException("Control+A did not select the last leaf.");
            }

            Press(On(0));
            Release();
        }

        // Marks the root to be drawn, as a frame with no change of the selection draws the area.
        public void Redraw() => Invalidation.Invalidate(Tree, Tree.Root, ChangeKind.Color);

        public void Copy() => Key(KeyModifiers.Control, global::XylemUI.Key.C);

        public void CheckCopy()
        {
            if (_clipboard.Text is not { Length: > 5_000 })
            {
                throw new InvalidOperationException("The copy did not hold the 1,000 leaves.");
            }

            _clipboard.Text = null;
        }

        public void Dispose()
        {
            Scope.Dispose(_scope);
            _context.Dispose();
            _atlas.Dispose();
            _rasterizer.Dispose();
            _fonts.Dispose();
        }

        private void Drag((int X, int Y) at)
        {
            bool far = (_moves++ & 1) == 0;
            _input.Handle(Pointer(PointerInputKind.Move, at, PointerButton.None, PointerButtons.Left));
            if ((Selected(50) > 0) != far)
            {
                throw new InvalidOperationException("The last move of the drag did not move the selection.");
            }
        }

        // How many code units of a leaf are selected.
        private int Selected(int leaf) =>
            Tree.Nodes.GetRef(_leaves[leaf]).Text!.Selection is { IsEmpty: false } selection ? selection.End - selection.Start : 0;

        private void Key(KeyModifiers modifiers, Key key)
        {
            _ = _input.Handle(new KeyInput(KeyEventKind.Down, key, PhysicalKey.None, default, modifiers, false, Next()));
            _ = _input.Handle(new KeyInput(KeyEventKind.Up, key, PhysicalKey.None, default, modifiers, false, Next()));
        }

        private PointerInput Pointer(PointerInputKind kind, (int X, int Y) at, PointerButton button, PointerButtons buttons) =>
            new(kind, at.X, at.Y, button, buttons, KeyModifiers.None, kind == PointerInputKind.Down ? 1 : 0, Next(), PointerType.Mouse);

        private TimeSpan Next() => _now += TimeSpan.FromSeconds(1);
    }

    // The clipboard, in memory.
    private sealed class MemoryClipboard : IClipboardBackend
    {
        public string? Text { get; set; }

        public string? GetText() => Text;

        public bool HasText() => !string.IsNullOrEmpty(Text);

        public bool SetText(string text)
        {
            Text = text;
            return true;
        }
    }
}
