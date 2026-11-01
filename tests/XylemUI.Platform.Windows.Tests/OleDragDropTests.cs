using TerraFX.Interop.Windows;
using XylemUI.Platform.Windows;
using XylemUI.TestSupport;
using static TerraFX.Interop.Windows.Windows;

namespace XylemUI.Platform.Windows.Tests;

// OLE's drag and drop of a window (row DD1, design §7, "Drag and drop"): the shell's data object with the files and the text, the
// window's drop target, and the drop source's answers.
[Collection(UiThreadGroup.Name)]
public sealed unsafe class OleDragDropTests(UiThreadFixture ui)
{
    /// <summary>
    /// A data object made of files and text gives back the files as <see cref="DroppedFiles"/> (<c>CF_HDROP</c> first), text alone as
    /// <see cref="DroppedText"/>; with neither, none is made.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The UI thread, in a single-threaded apartment.
    ///
    /// Steps:
    /// - Make data objects of two paths (one with a space) and a text; of the text alone; of nothing; read each back.
    ///
    /// Expected:
    /// - The two paths; the text; no data object for nothing.
    /// </remarks>
    [Fact]
    public void DataObject_RoundTripsFilesAndText() =>
        ui.Invoke(() =>
        {
            object? files = Read(new ExternalDragData([@"C:\a b\x.txt", @"D:\y"], "text"));
            object? text = Read(new ExternalDragData(null, "日本語 and text"));
            IDataObject* none = Win32Window.CreateDataObject(new ExternalDragData(null, null));

            Assert.Equal([@"C:\a b\x.txt", @"D:\y"], Assert.IsType<DroppedFiles>(files).Paths);
            Assert.Equal("日本語 and text", Assert.IsType<DroppedText>(text).Text);
            Assert.True(none is null);
        });

    /// <summary>
    /// The window's drop target hands a drag from the OS to the input: the value read from the data object, the point in the client
    /// area, the effects allowed, and the modifiers of the key state; the effect the input chose comes back, limited to the allowed.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A hidden window whose drops go to a recorder that answers Link to every call.
    ///
    /// Steps:
    /// - Enter with a data object of one path at the client point (10, 20) with Control held and Copy and Move allowed; move to
    ///   (11, 20); leave; enter again and drop at (12, 20).
    ///
    /// Expected:
    /// - The recorder's calls: an enter of the path at (10, 20) with Copy and Move and Control, a move to (11, 20), a leave, an enter,
    ///   a drop at (12, 20). Each effect returned is 0, since Link is not allowed.
    /// </remarks>
    [Fact]
    public void DropTarget_HandsTheDragToTheInput() =>
        ui.Invoke(() =>
        {
            using var window = new Win32Window("drop", 200, 100);
            var recorder = new Recorder();
            window.ExternalDrops = recorder;
            IDataObject* data = Win32Window.CreateDataObject(new ExternalDragData([@"C:\x"], null));
            try
            {
                uint allowed = (uint)(DragEffects.Copy | DragEffects.Move);
                var effects = new List<uint>
                {
                    window.DropEnter(data, Ole.Control, Screen(window, 10, 20), allowed),
                    window.DropOver(Ole.Control, Screen(window, 11, 20), allowed),
                };
                window.DropLeave();
                effects.Add(window.DropEnter(data, 0, Screen(window, 12, 20), allowed));
                effects.Add(window.DropDrop(data, 0, Screen(window, 12, 20), allowed));

                Assert.Equal(
                    [@"enter C:\x 10 20 Copy, Move Control", "over 11 20 Control", "leave", @"enter C:\x 12 20 Copy, Move None", "drop 12 20 None"],
                    recorder.Calls
                );
                Assert.Equal([0u, 0u, 0u, 0u], effects);
            }
            finally
            {
                Com.Release(ref data);
            }
        });

    /// <summary>
    /// The drop source cancels on Esc, drops once the left button is up, and goes on while it is down.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - None.
    ///
    /// Steps:
    /// - Ask with Esc and the left button down; with no button; with the left button down.
    ///
    /// Expected:
    /// - <c>DRAGDROP_S_CANCEL</c>, <c>DRAGDROP_S_DROP</c>, <c>S_OK</c>.
    /// </remarks>
    [Fact]
    public void DropSource_CancelsOnEsc_DropsOnTheRelease() =>
        Assert.Equal(
            [Ole.DragDropCancel, Ole.DragDropDrop, S.S_OK],
            [OleDropSource.Continue(true, Ole.LeftButton), OleDropSource.Continue(false, 0), OleDropSource.Continue(false, Ole.LeftButton)]
        );

    private static object? Read(ExternalDragData data)
    {
        IDataObject* made = Win32Window.CreateDataObject(data);
        Assert.True(made is not null);
        try
        {
            return Win32Window.ReadDropped(made);
        }
        finally
        {
            Com.Release(ref made);
        }
    }

    private static POINTL Screen(Win32Window window, int x, int y)
    {
        var point = new POINT { x = x, y = y };
        _ = ClientToScreen((HWND)window.Handle, &point);
        return new POINTL { x = point.x, y = point.y };
    }

    // Records each call of a drop target and answers Link.
    private sealed class Recorder : IExternalDropTarget
    {
        public List<string> Calls { get; } = [];

        public DragEffects DropEnter(object value, int x, int y, DragEffects allowed, KeyModifiers modifiers)
        {
            Calls.Add($"enter {string.Join('|', ((DroppedFiles)value).Paths)} {x} {y} {allowed} {modifiers}");
            return DragEffects.Link;
        }

        public DragEffects DropOver(int x, int y, KeyModifiers modifiers)
        {
            Calls.Add($"over {x} {y} {modifiers}");
            return DragEffects.Link;
        }

        public void DropLeave() => Calls.Add("leave");

        public DragEffects Drop(int x, int y, KeyModifiers modifiers)
        {
            Calls.Add($"drop {x} {y} {modifiers}");
            return DragEffects.Link;
        }
    }
}
