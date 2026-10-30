namespace XylemUI.Tests;

// The text typed into a window and the press that keeps the focus (row P02b, design §4.9, "Text input on Windows" and "A press
// that keeps the focus"). The text is injected as TextInput, the entry of §12.4.
public sealed partial class KeyFocusTests
{
    /// <summary>
    /// Text goes down the capture phase from the root to the focused node, then up the bubble phase, with its text and target; a
    /// handler that stops it ends both phases, and the input reports it handled.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A parent box holds a focusable child; the root and the parent log each text in both phases, and the child logs its text
    ///   and target in the capture phase. The window is active, and the child has the focus.
    ///
    /// Steps:
    /// - Send the text "é"; then have the child's capture handler stop the event, and send it again.
    ///
    /// Expected:
    /// - The first logs root capture, parent capture, child "é" at the child, parent bubble, root bubble, and is not handled. The
    ///   second logs root capture, parent capture, child "é" at the child, and is handled.
    /// </remarks>
    [Fact]
    public void TextInput_CaptureThenBubble_StopPropagationEndsBothPhases() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            NodeHandle parent = scene.Place(scene.Root, 0, 0, 100, 100);
            NodeHandle child = scene.Focusable(parent, 10, 10, 50, 50);
            var log = new List<string>();
            bool stop = false;
            scene.LogText(scene.Root, "root", log);
            scene.LogText(parent, "parent", log);
            scene.Tree.AddTextHandler(
                child,
                EventPhase.Capture,
                (in TextInputEvent e) =>
                {
                    log.Add($"child {e.Text} at {(e.TargetNode == child ? "child" : "other")}");
                    if (stop)
                    {
                        e.StopPropagation();
                    }
                }
            );
            scene.Start();
            Assert.True(scene.Tree.Focus.Focus(child));

            Assert.False(scene.Type("é"));
            Assert.Equal(["root capture", "parent capture", "child é at child", "parent bubble", "root bubble"], log);
            log.Clear();
            stop = true;
            Assert.True(scene.Type("é"));
            Assert.Equal(["root capture", "parent capture", "child é at child"], log);
        });

    /// <summary>
    /// With no focused node, text goes from the root; a disabled node's handlers are not called; a handler whose scope is disposed
    /// is removed.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A focusable box with a text handler added in a scope, and a root that logs each text in the bubble phase. The window is
    ///   active.
    ///
    /// Steps:
    /// - Send "a" with no focus. Focus the box and send "b". Disable the box and send "c". Enable it, dispose the scope, and send
    ///   "d".
    ///
    /// Expected:
    /// - "a" reaches only the root; "b" reaches the box, then the root; "c" reaches only the root (a disabled node takes no focus,
    ///   so it goes from the root); "d" reaches only the root.
    /// </remarks>
    [Fact]
    public void TextInput_NoFocusDisabledOrDisposedScope_GoesFromTheRootOrSkipsTheNode() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            NodeHandle box = scene.Focusable(scene.Root, 10, 10, 50, 50);
            var log = new List<string>();
            scene.Tree.AddTextHandler(scene.Root, EventPhase.Bubble, (in TextInputEvent e) => log.Add($"root {e.Text}"));
            ScopeHandle scope = Scope.Create(() =>
                new Element(scene.Tree, box).OnTextInput(EventPhase.Bubble, (in TextInputEvent e) => log.Add($"box {e.Text}"))
            );
            scene.Start();

            _ = scene.Type("a");
            Assert.True(scene.Tree.Focus.Focus(box));
            _ = scene.Type("b");
            scene.Tree.SetDisabled(box, true);
            _ = scene.Type("c");
            scene.Tree.SetDisabled(box, false);
            Scope.Dispose(scope);
            Assert.True(scene.Tree.Focus.Focus(box));
            _ = scene.Type("d");

            Assert.Equal(["root a", "box b", "root b", "root c", "root d"], log);
        });

    /// <summary>
    /// A press that reaches a node that keeps the focus before a node that takes it leaves the focus where it is, even when that
    /// node takes the focus itself; a node that takes the focus inside it still takes it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A focusable field at (10, 10); a focusable button at (100, 10) that keeps the focus on a press, holding a plain box; and a
    ///   toolbar at (100, 60) that keeps the focus, holding a focusable box at (110, 70). The window is active, and the field has
    ///   the focus.
    ///
    /// Steps:
    /// - Press the button's box; press the button outside its box; press the toolbar outside its box; press the toolbar's box.
    ///
    /// Expected:
    /// - The field keeps the focus after the first three presses; the toolbar's focusable box has it after the last.
    /// </remarks>
    [Fact]
    public void KeepsFocusOnPress_PressOnTheNodeOrADescendant_LeavesTheFocus() =>
        ui.Invoke(() =>
        {
            var scene = new Scene();
            NodeHandle field = scene.Focusable(scene.Root, 10, 10, 50, 40);
            NodeHandle button = scene.Focusable(scene.Root, 100, 10, 80, 40);
            scene.Tree.SetKeepsFocusOnPress(button, true);
            NodeHandle inner = scene.Place(button, 0, 0, 20, 20);
            scene.Tree.SetBlocksPointer(inner, true);
            NodeHandle toolbar = scene.Place(scene.Root, 100, 60, 80, 40);
            scene.Tree.SetKeepsFocusOnPress(toolbar, true);
            scene.Tree.SetBlocksPointer(toolbar, true);
            NodeHandle item = scene.Focusable(toolbar, 10, 10, 20, 20);
            scene.Start();
            scene.Draw();
            Assert.True(scene.Tree.Focus.Focus(field));

            scene.Press(105, 15);
            scene.Release(105, 15);
            Assert.Equal(field, scene.Tree.Focus.Focused);
            scene.Press(170, 40);
            scene.Release(170, 40);
            Assert.Equal(field, scene.Tree.Focus.Focused);
            scene.Press(170, 90);
            scene.Release(170, 90);
            Assert.Equal(field, scene.Tree.Focus.Focused);
            scene.Press(115, 75);
            scene.Release(115, 75);
            Assert.Equal(item, scene.Tree.Focus.Focused);
        });

    private sealed partial class Scene
    {
        public bool Type(string text) => Input.Handle(new TextInput(text, TimeSpan.Zero));

        // Logs each text in both phases as "<name> capture" and "<name> bubble".
        public void LogText(NodeHandle node, string name, List<string> log)
        {
            Tree.AddTextHandler(node, EventPhase.Capture, (in TextInputEvent _) => log.Add($"{name} capture"));
            Tree.AddTextHandler(node, EventPhase.Bubble, (in TextInputEvent _) => log.Add($"{name} bubble"));
        }
    }
}
