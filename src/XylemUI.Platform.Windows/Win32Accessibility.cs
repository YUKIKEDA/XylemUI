using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;
using NodeHandle = XylemUI.Handle<XylemUI.UiNode>;

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

    // The id of the window's node in an update that failed before the tree had one: no node of the tree has it (they count up
    // from 1).
    private const ulong FallbackRootId = ulong.MaxValue;

    private readonly Win32Window _window;
    private readonly UiTree _tree;
    private readonly nint _id;
    private readonly List<AccessibilityNodeId> _pending = [];
    private nint _adapter;
    private ulong _rootId;
    private bool _activating;
    private bool _asked;
    private bool _whole;
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
        _adapter = AccessKit.WindowsAdapterNew((nint)window.Hwnd, focused, &OnAction, (nint)window.Hwnd);
        window.ScaleChanged += OnScaleChanged;
    }

    /// <summary>
    /// Gets the number of nodes the last update after the first handed AccessKit: the nodes that changed.
    /// </summary>
    internal int LastUpdateNodes { get; private set; }

    /// <summary>
    /// Gets or sets the window's input, which routes the clicks of assistive technology as a key's click on a button is routed.
    /// </summary>
    internal WindowInput? Input { get; set; }

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

        // A tree that AccessKit has only as its placeholder, or that an update that failed left as one node, is given whole first:
        // outside AccessKit's handling of the message, which must not be entered again from inside it.
        StartIfAsked();
        if (_whole)
        {
            Push();
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
    /// Makes the trees and hands AccessKit the whole tree when assistive technology asked for it before the tree had a root, which
    /// AccessKit then answered with a placeholder. The window calls it once its tree has a root.
    /// </summary>
    public void StartIfAsked()
    {
        if (_asked && _adapter != 0 && !_tree.Root.IsNone)
        {
            _asked = false;
            _ = StartTree();
            _whole = true;
            Push();
        }
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

        // The requests posted and not yet delivered are freed: no action comes once the adapter is freed.
        FreePostedActions(_window.Hwnd);
        NativeHandleTable.Release(_id);
    }

    /// <summary>
    /// Frees the requests for actions posted to a window and not yet delivered (<see cref="Win32Window.WmAccessKitAction"/>).
    /// </summary>
    /// <param name="hwnd">The window.</param>
    internal static void FreePostedActions(HWND hwnd)
    {
        MSG message;
        while (PeekMessageW(&message, hwnd, Win32Window.WmAccessKitAction, Win32Window.WmAccessKitAction, (uint)PM.PM_REMOVE) != BOOL.FALSE)
        {
            AccessKit.ActionRequestFree(message.lParam);
        }
    }

    /// <summary>
    /// Carries out a request for an action on the UI thread, posted by <see cref="OnAction"/>, and frees it (design §4.4.1,
    /// "States and actions"). A request for a node no longer in the tree, or one the node does not take, does nothing.
    /// </summary>
    /// <param name="request">The request; it is freed whatever happens.</param>
    public void Perform(nint request)
    {
        try
        {
            Perform((AccessKit.ActionRequest*)request);
        }
        finally
        {
            AccessKit.ActionRequestFree(request);
        }
    }

    // AccessKit asks for the tree for the first time, on the window's thread, inside WM_GETOBJECT: the tree is made and given whole.
    // A null update tells AccessKit that the tree is not ready: it answers with a placeholder and does not call this again, so a
    // failure is recorded as a question, which the next WM_GETOBJECT answers with the whole tree (StartIfAsked).
    [UnmanagedCallersOnly]
    private static nint OnActivate(nint userData)
    {
        if (!NativeHandleTable.TryResolve(userData, out Win32Accessibility? self))
        {
            return 0;
        }

        try
        {
            return self.Activate();
        }
        catch (Exception exception)
        {
            Win32MessageLoop.ReportCallbackException(exception);
            self._asked = true;
            return 0;
        }
    }

    // AccessKit asks for the update that NodesChanged gives it, only once it is active or has its placeholder. It must not return
    // null, and an update it cannot take ends the process in its Rust code: an update that fails is the tree's information and the
    // window's node alone, valid in either state, and the next update gives the whole tree again.
    [UnmanagedCallersOnly]
    private static nint OnUpdate(nint userData)
    {
        if (!NativeHandleTable.TryResolve(userData, out Win32Accessibility? self))
        {
            return OneNodeTree(FallbackRootId);
        }

        try
        {
            return self.MakeUpdate();
        }
        catch (Exception exception)
        {
            Win32MessageLoop.ReportCallbackException(exception);
            self._whole = true;
            return OneNodeTree(self._rootId != 0 ? self._rootId : FallbackRootId);
        }
    }

    // A tree of the window's node alone, with its information: an update AccessKit takes whatever it had before.
    private static nint OneNodeTree(ulong root)
    {
        nint update = AccessKit.TreeUpdateWithCapacityAndFocus(1, root);
        AccessKit.TreeUpdateSetTreeInfo(update, AccessKit.TreeInfoNew(root));
        AccessKit.TreeUpdatePushNode(update, root, AccessKit.NodeNew(AccessKit.RoleWindow));
        return update;
    }

    // A request for an action, on the thread UIA calls on, which may not be the UI thread: the handle table has no lock, so the
    // request goes to the window's queue as it is (userData is the window), and the window procedure carries it out on the UI
    // thread. A request the window cannot take is freed here. Nothing here throws a managed exception.
    [UnmanagedCallersOnly]
    private static void OnAction(nint request, nint userData)
    {
        if (PostMessageW((HWND)userData, Win32Window.WmAccessKitAction, 0, request) == BOOL.FALSE)
        {
            AccessKit.ActionRequestFree(request);
        }
    }

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
            SemanticsRole.List when (node.States & AccessibilityStates.SelectableList) != 0 => AccessKit.RoleListBox,
            SemanticsRole.List => AccessKit.RoleList,
            SemanticsRole.ListItem when node.Parent is { } list && (list.States & AccessibilityStates.SelectableList) != 0 =>
                AccessKit.RoleListBoxOption,
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

    // A window shown before its tree has a root (DesktopWindow shows it, then mounts and sizes it) can be asked already: Windows'
    // own clients send WM_GETOBJECT for the window's client area too. The answer is then that the tree is not ready, and AccessKit
    // gives a placeholder; StartIfAsked hands it the whole tree once the root is there.
    private nint Activate()
    {
        if (_tree.Root.IsNone)
        {
            _asked = true;
            return 0;
        }

        return MakeWholeTree(StartTree());
    }

    private AccessibilityNode StartTree()
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

        return accessibility.Root!;
    }

    private nint MakeWholeTree(AccessibilityNode root)
    {
        AccessibilityTree accessibility = _tree.Accessibility;
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
        // The first update after a placeholder is the whole tree, with the tree's information.
        if (_whole)
        {
            _whole = false;
            return MakeWholeTree(_tree.Accessibility.Root!);
        }

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

        foreach (AccessibilityNode link in node.Links)
        {
            PushTree(update, link);
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

        if ((node.States & AccessibilityStates.ClipsChildren) != 0)
        {
            Size max = node.ScrollRange;
            AccessKit.NodeSetClipsChildren(made);
            AccessKit.NodeSetScrollX(made, -node.ChildOffset.X);
            AccessKit.NodeSetScrollXMin(made, 0);
            AccessKit.NodeSetScrollXMax(made, max.Width);
            AccessKit.NodeSetScrollY(made, -node.ChildOffset.Y);
            AccessKit.NodeSetScrollYMin(made, 0);
            AccessKit.NodeSetScrollYMax(made, max.Height);
        }

        if (node.PositionInSet >= 0)
        {
            AccessKit.NodeSetPositionInSet(made, (nuint)node.PositionInSet);
        }

        if (node.SizeOfSet >= 0)
        {
            AccessKit.NodeSetSizeOfSet(made, (nuint)node.SizeOfSet);
        }

        SetActions(made, node);
        SetPlace(made, node);
        SetChildren(made, node);
        return made;
    }

    // The actions a node takes (design §4.4.1, "States and actions"): Focus for a node that takes the focus, Click for one with
    // a click handler of its own and for a link, SetValue for a text field, Expand and Collapse for a popup's anchor, and
    // ScrollIntoView for every node.
    private static void SetActions(nint made, AccessibilityNode node)
    {
        AccessibilityStates states = node.States;
        if ((states & AccessibilityStates.Focusable) != 0)
        {
            AccessKit.NodeAddAction(made, AccessKit.ActionFocus);
        }

        if ((states & AccessibilityStates.Clickable) != 0)
        {
            AccessKit.NodeAddAction(made, AccessKit.ActionClick);
        }

        if (node.Role == SemanticsRole.Edit)
        {
            AccessKit.NodeAddAction(made, AccessKit.ActionSetValue);
        }

        if ((states & AccessibilityStates.Expandable) != 0)
        {
            AccessKit.NodeAddAction(made, AccessKit.ActionExpand);
            AccessKit.NodeAddAction(made, AccessKit.ActionCollapse);
        }

        AccessKit.NodeAddAction(made, AccessKit.ActionScrollIntoView);
    }

    private void Perform(AccessKit.ActionRequest* request)
    {
        if (!_tree.HasAccessibilityTree || _tree.Accessibility.Find(new AccessibilityNodeId(request->TargetNode)) is not { } node)
        {
            return;
        }

        NodeHandle handle = node.Node;
        if (!_tree.Nodes.IsAlive(handle))
        {
            return;
        }

        var now = TimeSpan.FromMilliseconds(Environment.TickCount64);
        switch (request->Action)
        {
            case AccessKit.ActionClick when node.LinkIndex >= 0:
                Input?.ClickLinkFromOutside(handle, node.LinkIndex, now);
                break;
            case AccessKit.ActionClick
                when (node.States & (AccessibilityStates.Clickable | AccessibilityStates.Disabled)) == AccessibilityStates.Clickable:
                Input?.ClickFromKey(handle, now);
                break;
            case AccessKit.ActionFocus:
                _ = _tree.Focus.Focus(handle);
                break;
            case AccessKit.ActionExpand:
                _ = _tree.SetPopupOpen(handle, open: true);
                break;
            case AccessKit.ActionCollapse:
                _ = _tree.SetPopupOpen(handle, open: false);
                break;
            case AccessKit.ActionScrollIntoView:
                _tree.Focus.BringIntoView(handle);
                break;
            case AccessKit.ActionSetValue when request->Data.HasValue != 0 && request->Data.Value.Tag == AccessKit.ActionDataValue:
                if (node.Role == SemanticsRole.Edit && (node.States & (AccessibilityStates.ReadOnly | AccessibilityStates.Disabled)) == 0)
                {
                    string value = Marshal.PtrToStringUTF8((nint)request->Data.Value.Value.Text) ?? string.Empty;
                    _ = _tree.SetAccessibilityValueFromOutside(handle, value);
                }

                break;
            default:
                break;
        }
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
        int count = node.Children.Count + node.Links.Count;
        if (count == 0)
        {
            return;
        }

        if (_children.Length < count)
        {
            _children = new ulong[Math.Max(count, _children.Length * 2)];
        }

        for (int i = 0; i < node.Children.Count; i++)
        {
            _children[i] = node.Children[i].Id.Value;
        }

        for (int i = 0; i < node.Links.Count; i++)
        {
            _children[node.Children.Count + i] = node.Links[i].Id.Value;
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
