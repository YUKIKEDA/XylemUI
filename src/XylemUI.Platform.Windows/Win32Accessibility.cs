using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace XylemUI.Platform.Windows;

/// <summary>
/// A window's UIA provider (design §4.4.1, "The shape of P02g"): AccessKit's Windows adapter, which the window gives its
/// <c>WM_GETOBJECT</c> and its keyboard focus. The first <c>WM_GETOBJECT</c> makes the tree's accessibility tree and hands AccessKit
/// all of it; then each frame hands it the nodes that changed. Until then neither tree is made. The nodes are written in AccessKit's
/// form: each node's transform is a move to its own origin in its parent's coordinates (plus the scroll offset, for a box that
/// scrolls), and its bounds are in its own coordinates, so that a move or a scroll changes that node alone; the window's node scales
/// by the window's DPI, as AccessKit takes physical pixels.
/// </summary>
internal sealed unsafe class Win32Accessibility : IAccessibilityBridge, IDisposable
{
    // The name of the toolkit the tree comes from, which UIA reads as its framework (UTF-8).
    private static ReadOnlySpan<byte> ToolkitName => "XylemUI"u8;

    private readonly Win32Window _window;
    private readonly UiTree _tree;
    private readonly nint _id;
    private readonly List<AccessibilityNodeId> _pending = [];
    private nint _adapter;
    private ulong _rootId;
    private bool _activating;
    private byte[] _text = new byte[256];
    private ulong[] _children = new ulong[16];
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="Win32Accessibility"/> class: the adapter of a window and its tree.
    /// </summary>
    /// <param name="window">The window, made and not yet destroyed.</param>
    /// <param name="tree">The window's tree.</param>
    public Win32Accessibility(Win32Window window, UiTree tree)
    {
        _window = window;
        _tree = tree;
        _id = NativeHandleTable.Register(this);
        byte focused = GetFocus() == window.Hwnd ? (byte)1 : (byte)0;
        _adapter = AccessKit.WindowsAdapterNew((nint)window.Hwnd, focused, &OnAction, _id);
        window.ScaleChanged += OnScaleChanged;
    }

    /// <summary>
    /// Gets the number of nodes the last update after the first handed AccessKit: the nodes that changed.
    /// </summary>
    internal int LastUpdateNodes { get; private set; }

    /// <summary>
    /// Answers the window's <c>WM_GETOBJECT</c>; the first that asks for the UIA provider makes the trees.
    /// </summary>
    /// <param name="wParam">The message's <c>wParam</c>.</param>
    /// <param name="lParam">The message's <c>lParam</c>.</param>
    /// <param name="result">The answer to return, when there is one.</param>
    /// <returns><see langword="true"/> when the adapter answered.</returns>
    public bool HandleGetObject(WPARAM wParam, LPARAM lParam, out LRESULT result)
    {
        result = 0;
        if (_adapter == 0)
        {
            return false;
        }

        AccessKit.OptionalLresult answer = AccessKit.WindowsAdapterHandleWmGetObject(_adapter, (nuint)wParam, (nint)lParam, &OnActivate, _id);
        if (!answer.HasValue)
        {
            return false;
        }

        result = answer.Value;
        return true;
    }

    /// <summary>
    /// Tells the adapter that the window took or lost the keyboard focus (<c>WM_SETFOCUS</c>, <c>WM_KILLFOCUS</c>).
    /// </summary>
    /// <param name="focused">Whether the window has the focus now.</param>
    public void WindowFocusChanged(bool focused)
    {
        if (_adapter != 0)
        {
            Raise(AccessKit.WindowsAdapterUpdateWindowFocusState(_adapter, focused ? (byte)1 : (byte)0));
        }
    }

    /// <inheritdoc/>
    public void NodesChanged(ReadOnlySpan<AccessibilityChange> changes)
    {
        // The activation hands AccessKit the whole tree that the start just made.
        if (_activating)
        {
            return;
        }

        foreach (AccessibilityChange change in changes)
        {
            if (change.Kind != AccessibilityChangeKind.Removed)
            {
                _pending.Add(change.Id);
            }
        }

        Push();
    }

    /// <summary>
    /// Does nothing: each update hands AccessKit the node that has the focus with the nodes that changed (<see cref="NodesChanged"/>),
    /// and a change of the focus also changes the Focused state of the node that had it or of the node that takes it. When neither
    /// has a node, AccessKit's focus stays the window's.
    /// </summary>
    /// <param name="node">The node that has the focus, or none.</param>
    public void NotifyFocusChange(AccessibilityNodeId node) { }

    /// <summary>
    /// Frees the adapter: assistive technology that asks the window again gets no provider.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _window.ScaleChanged -= OnScaleChanged;
        if (_tree.HasAccessibilityTree && ReferenceEquals(_tree.Accessibility.Bridge, this))
        {
            _tree.Accessibility.Bridge = null;
        }

        if (_adapter != 0)
        {
            AccessKit.WindowsAdapterFree(_adapter);
            _adapter = 0;
        }

        NativeHandleTable.Release(_id);
    }

    // AccessKit asks for the tree for the first time, on the window's thread, inside WM_GETOBJECT: the tree is made and given whole.
    // A null update tells AccessKit that the tree is not ready; it asks again on the next WM_GETOBJECT.
    [UnmanagedCallersOnly]
    private static nint OnActivate(nint userData)
    {
        try
        {
            return NativeHandleTable.TryResolve(userData, out Win32Accessibility? self) ? self.Activate() : 0;
        }
        catch (Exception exception)
        {
            Win32MessageLoop.ReportCallbackException(exception);
            return 0;
        }
    }

    // AccessKit asks for the update that NodesChanged or NotifyFocusChange gives it, only once it is active, so after the activation
    // gave it the window's node. It must not return null: an update that fails gives the focus to the window's node and no node.
    [UnmanagedCallersOnly]
    private static nint OnUpdate(nint userData)
    {
        ulong fallback = 0;
        try
        {
            if (NativeHandleTable.TryResolve(userData, out Win32Accessibility? self))
            {
                fallback = self._rootId;
                return self.MakeUpdate();
            }
        }
        catch (Exception exception)
        {
            Win32MessageLoop.ReportCallbackException(exception);
        }

        return AccessKit.TreeUpdateWithCapacityAndFocus(0, fallback);
    }

    // A request for an action, on any thread. The actions come with row P02g's third implementation PR (Issue #633); the nodes
    // declare none, so a request is let go.
    [UnmanagedCallersOnly]
    private static void OnAction(nint request, nint userData) => AccessKit.ActionRequestFree(request);

    private static byte RoleOf(AccessibilityNode node) =>
        node.Role switch
        {
            SemanticsRole.Window => AccessKit.RoleWindow,
            SemanticsRole.Dialog => AccessKit.RoleDialog,
            SemanticsRole.Group => AccessKit.RoleGroup,
            SemanticsRole.Text => AccessKit.RoleLabel,
            SemanticsRole.Hyperlink => AccessKit.RoleLink,
            SemanticsRole.Image => AccessKit.RoleImage,
            SemanticsRole.Button => AccessKit.RoleButton,
            SemanticsRole.CheckBox => AccessKit.RoleCheckBox,
            SemanticsRole.RadioButton => AccessKit.RoleRadioButton,
            SemanticsRole.ComboBox => AccessKit.RoleComboBox,
            SemanticsRole.Edit when (node.States & AccessibilityStates.Password) != 0 => AccessKit.RolePasswordInput,
            SemanticsRole.Edit when (node.States & AccessibilityStates.Multiline) != 0 => AccessKit.RoleMultilineTextInput,
            SemanticsRole.Edit => AccessKit.RoleTextInput,
            SemanticsRole.List => AccessKit.RoleList,
            SemanticsRole.ListItem => AccessKit.RoleListItem,
            SemanticsRole.Menu => AccessKit.RoleMenu,
            SemanticsRole.MenuBar => AccessKit.RoleMenuBar,
            SemanticsRole.MenuItem => AccessKit.RoleMenuItem,
            SemanticsRole.Slider => AccessKit.RoleSlider,
            SemanticsRole.ProgressBar => AccessKit.RoleProgressIndicator,
            SemanticsRole.ScrollViewer => AccessKit.RoleScrollView,
            SemanticsRole.Tab => AccessKit.RoleTabList,
            SemanticsRole.TabItem => AccessKit.RoleTab,
            SemanticsRole.ToolTip => AccessKit.RoleTooltip,
            SemanticsRole.Separator => AccessKit.RoleSplitter,
            _ => AccessKit.RoleUnknown,
        };

    private static void Raise(nint events)
    {
        if (events != 0)
        {
            AccessKit.WindowsQueuedEventsRaise(events);
        }
    }

    private nint Activate()
    {
        AccessibilityTree accessibility = _tree.Accessibility;
        accessibility.Bridge = this;
        _activating = true;
        try
        {
            accessibility.Start();
        }
        finally
        {
            _activating = false;
        }

        if (accessibility.Root is not { } root)
        {
            return 0;
        }

        _rootId = root.Id.Value;
        nint update = AccessKit.TreeUpdateWithCapacityAndFocus(0, FocusId(accessibility, root));
        nint info = AccessKit.TreeInfoNew(_rootId);

        // The name is a constant in the image: its address does not move.
        fixed (byte* name = ToolkitName)
        {
            AccessKit.TreeInfoSetToolkitName(info, name, (nuint)ToolkitName.Length);
        }

        AccessKit.TreeUpdateSetTreeInfo(update, info);
        PushTree(update, root);
        _pending.Clear();
        return update;
    }

    // Hands AccessKit the nodes that changed and the focus; AccessKit calls back for the update only while it is active.
    private void Push()
    {
        if (_adapter != 0)
        {
            Raise(AccessKit.WindowsAdapterUpdateIfActive(_adapter, &OnUpdate, _id));
        }

        _pending.Clear();
    }

    private nint MakeUpdate()
    {
        AccessibilityTree accessibility = _tree.Accessibility;
        AccessibilityNode? root = accessibility.Root;
        nint update = AccessKit.TreeUpdateWithCapacityAndFocus((nuint)_pending.Count, root is null ? 0 : FocusId(accessibility, root));
        int pushed = 0;
        foreach (AccessibilityNodeId id in _pending)
        {
            if (accessibility.Find(id) is { } node)
            {
                AccessKit.TreeUpdatePushNode(update, id.Value, MakeNode(node));
                pushed++;
            }
        }

        LastUpdateNodes = pushed;
        return update;
    }

    private ulong FocusId(AccessibilityTree accessibility, AccessibilityNode root) => (accessibility.Find(_tree.Focus.Focused) ?? root).Id.Value;

    private void PushTree(nint update, AccessibilityNode node)
    {
        AccessKit.TreeUpdatePushNode(update, node.Id.Value, MakeNode(node));
        foreach (AccessibilityNode child in node.Children)
        {
            PushTree(update, child);
        }
    }

    private nint MakeNode(AccessibilityNode node)
    {
        nint made = AccessKit.NodeNew(RoleOf(node));
        SetText(made, node.Name, TextProperty.Label);
        SetText(made, node.Description, TextProperty.Description);
        SetText(made, node.Value, TextProperty.Value);
        SetText(made, node.AutomationId, TextProperty.AuthorId);
        SetStates(made, node);
        if (node.Range is { } range)
        {
            AccessKit.NodeSetNumericValue(made, range.Value);
            AccessKit.NodeSetMinNumericValue(made, range.Minimum);
            AccessKit.NodeSetMaxNumericValue(made, range.Maximum);
        }

        if (node.Role == SemanticsRole.ScrollViewer)
        {
            Size max = node.ScrollRange;
            AccessKit.NodeSetScrollX(made, -node.ChildOffset.X);
            AccessKit.NodeSetScrollXMin(made, 0);
            AccessKit.NodeSetScrollXMax(made, max.Width);
            AccessKit.NodeSetScrollY(made, -node.ChildOffset.Y);
            AccessKit.NodeSetScrollYMin(made, 0);
            AccessKit.NodeSetScrollYMax(made, max.Height);
        }

        SetPlace(made, node);
        SetChildren(made, node);
        return made;
    }

    private static void SetStates(nint made, AccessibilityNode node)
    {
        AccessibilityStates states = node.States;
        if ((states & AccessibilityStates.Disabled) != 0)
        {
            AccessKit.NodeSetDisabled(made);
        }

        if ((states & AccessibilityStates.ReadOnly) != 0)
        {
            AccessKit.NodeSetReadOnly(made);
        }

        if ((states & AccessibilityStates.Required) != 0)
        {
            AccessKit.NodeSetRequired(made);
        }

        // A check box and a radio button are toggled either way; another role only when it says it is checked.
        bool toggles = node.Role is SemanticsRole.CheckBox or SemanticsRole.RadioButton;
        if ((states & AccessibilityStates.Mixed) != 0)
        {
            AccessKit.NodeSetToggled(made, AccessKit.ToggledMixed);
        }
        else if ((states & AccessibilityStates.Checked) != 0)
        {
            AccessKit.NodeSetToggled(made, AccessKit.ToggledTrue);
        }
        else if (toggles)
        {
            AccessKit.NodeSetToggled(made, AccessKit.ToggledFalse);
        }

        if ((states & AccessibilityStates.Expandable) != 0)
        {
            AccessKit.NodeSetExpanded(made, (states & AccessibilityStates.Expanded) != 0 ? (byte)1 : (byte)0);
        }

        // An item of a list is selected or not; another role only when it says it is selected.
        if ((states & AccessibilityStates.Selected) != 0)
        {
            AccessKit.NodeSetSelected(made, 1);
        }
        else if (node.Role == SemanticsRole.ListItem)
        {
            AccessKit.NodeSetSelected(made, 0);
        }
    }

    // The node's transform moves to its origin in its parent's coordinates, plus its children's offset (minus its scroll position),
    // which also moves its own bounds: the bounds take the offset back. The window's node scales by the window's DPI.
    private void SetPlace(nint made, AccessibilityNode node)
    {
        Rect bounds = node.Bounds;
        if (node.Parent is null)
        {
            double scale = _window.ScaleFactor;
            AccessKit.NodeSetTransform(made, new AccessKit.Affine(scale, 0, 0, scale, 0, 0));
            AccessKit.NodeSetBounds(made, new AccessKit.Rect(0, 0, bounds.Width, bounds.Height));
            return;
        }

        Vector2 offset = node.ChildOffset;
        AccessKit.NodeSetTransform(made, new AccessKit.Affine(1, 0, 0, 1, bounds.X + offset.X, bounds.Y + offset.Y));
        AccessKit.NodeSetBounds(made, new AccessKit.Rect(-offset.X, -offset.Y, bounds.Width - offset.X, bounds.Height - offset.Y));
    }

    private void SetChildren(nint made, AccessibilityNode node)
    {
        int count = node.Children.Count;
        if (count == 0)
        {
            return;
        }

        if (_children.Length < count)
        {
            _children = new ulong[Math.Max(count, _children.Length * 2)];
        }

        for (int i = 0; i < count; i++)
        {
            _children[i] = node.Children[i].Id.Value;
        }

        // AccessKit copies the ids before it returns; the array is pinned for the call.
        fixed (ulong* ids = _children)
        {
            AccessKit.NodeSetChildren(made, (nuint)count, ids);
        }
    }

    // Writes a text property in UTF-8, through a buffer that grows only for a longer text: a frame allocates nothing.
    private void SetText(nint made, string? text, TextProperty property)
    {
        if (text is null)
        {
            return;
        }

        int most = Encoding.UTF8.GetMaxByteCount(text.Length);
        if (_text.Length < most)
        {
            _text = new byte[Math.Max(most, _text.Length * 2)];
        }

        int length = Encoding.UTF8.GetBytes(text, _text);

        // AccessKit copies the text before it returns, and reads only the length given, which the buffer holds.
        fixed (byte* bytes = _text)
        {
            switch (property)
            {
                case TextProperty.Label:
                    AccessKit.NodeSetLabel(made, bytes, (nuint)length);
                    break;
                case TextProperty.Description:
                    AccessKit.NodeSetDescription(made, bytes, (nuint)length);
                    break;
                case TextProperty.Value:
                    AccessKit.NodeSetValue(made, bytes, (nuint)length);
                    break;
                default:
                    AccessKit.NodeSetAuthorId(made, bytes, (nuint)length);
                    break;
            }
        }
    }

    // The window moved to a monitor of another scale: its node, which scales the tree, is given again.
    private void OnScaleChanged(object? sender, EventArgs e)
    {
        if (_tree.HasAccessibilityTree && _tree.Accessibility.Root is { } root)
        {
            _pending.Add(root.Id);
            Push();
        }
    }

    private enum TextProperty
    {
        Label,
        Description,
        Value,
        AuthorId,
    }
}
