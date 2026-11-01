using System.Numerics;
using XylemUI.Text;

namespace XylemUI;

/// <summary>
/// Sets the style, the look, and the input of a node (design §2.13, "The component API"). Each writes to the node's tree at once and
/// returns the same element; lengths are in logical pixels. The overloads that take a signal call <see cref="Bind{T}"/>.
/// </summary>
public static class ElementExtensions
{
    /// <summary>
    /// Sets the width of the node's border box.
    /// </summary>
    /// <param name="element">The node.</param>
    /// <param name="width">The width, finite and at or above 0.</param>
    /// <returns>The same node, for the next call of the chain.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="width"/> is negative or not a finite number.</exception>
    public static Element Width(this Element element, float width) =>
        Layout(element, style => style with { Width = Length.Px(Check(width, nameof(width))) });

    /// <summary>
    /// Sets the height of the node's border box.
    /// </summary>
    /// <param name="element">The node.</param>
    /// <param name="height">The height, finite and at or above 0.</param>
    /// <returns>The same node, for the next call of the chain.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="height"/> is negative or not a finite number.</exception>
    public static Element Height(this Element element, float height) =>
        Layout(element, style => style with { Height = Length.Px(Check(height, nameof(height))) });

    /// <summary>
    /// Sets the padding on all four sides.
    /// </summary>
    /// <param name="element">The node.</param>
    /// <param name="all">The padding, finite and at or above 0.</param>
    /// <returns>The same node, for the next call of the chain.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="all"/> is negative or not a finite number.</exception>
    public static Element Padding(this Element element, float all) => Padding(element, all, all);

    /// <summary>
    /// Sets the padding on the left and right, and on the top and bottom.
    /// </summary>
    /// <param name="element">The node.</param>
    /// <param name="horizontal">The left and right padding, finite and at or above 0.</param>
    /// <param name="vertical">The top and bottom padding, finite and at or above 0.</param>
    /// <returns>The same node, for the next call of the chain.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A padding is negative or not a finite number.</exception>
    public static Element Padding(this Element element, float horizontal, float vertical)
    {
        float h = Check(horizontal, nameof(horizontal));
        float v = Check(vertical, nameof(vertical));
        return Layout(element, style => style with { Padding = EdgeInsets.Px(h, v, h, v) });
    }

    /// <summary>
    /// Sets the gap between the children of a column or a row, in both directions.
    /// </summary>
    /// <param name="element">The column or the row.</param>
    /// <param name="gap">The gap, finite and at or above 0.</param>
    /// <returns>The same node, for the next call of the chain.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="gap"/> is negative or not a finite number.</exception>
    /// <exception cref="InvalidOperationException">The node is not a column or a row.</exception>
    public static Element Gap(this Element element, float gap)
    {
        float g = Check(gap, nameof(gap));
        return Layout(
            element,
            style =>
                style.Algorithm is LayoutAlgorithm.Flex flex
                    ? style with
                    {
                        Algorithm = new LayoutAlgorithm.Flex(flex.Props with { RowGap = LengthPercentage.Px(g), ColumnGap = LengthPercentage.Px(g) }),
                    }
                    : throw new InvalidOperationException("Only a column or a row has a gap between its children.")
        );
    }

    /// <summary>
    /// Sets the border on all four sides: its width, which the layout reads, and its paint.
    /// </summary>
    /// <param name="element">The node.</param>
    /// <param name="width">The width, finite and at or above 0.</param>
    /// <param name="paint">The paint.</param>
    /// <returns>The same node, for the next call of the chain.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="width"/> is negative or not a finite number.</exception>
    public static Element Border(this Element element, float width, Paint paint)
    {
        float w = Check(width, nameof(width));
        Layout(element, style => style with { Border = EdgeInsets.All(w) });
        element.Tree!.SetBorderPaint(element.Node, paint);
        return element;
    }

    /// <summary>
    /// Sets the size of a text node's text.
    /// </summary>
    /// <param name="element">The text node.</param>
    /// <param name="size">The size, finite and greater than 0.</param>
    /// <returns>The same node, for the next call of the chain.</returns>
    /// <exception cref="InvalidOperationException">The node is not a text node.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="size"/> is out of its range.</exception>
    public static Element FontSize(this Element element, float size) => TextStyle(element, style => style with { Size = size });

    /// <summary>
    /// Sets the language of a text node's text, which chooses the fonts (design §4.5).
    /// </summary>
    /// <param name="element">The text node.</param>
    /// <param name="language">The language.</param>
    /// <returns>The same node, for the next call of the chain.</returns>
    /// <exception cref="InvalidOperationException">The node is not a text node.</exception>
    /// <exception cref="ArgumentException"><paramref name="language"/> is the default value.</exception>
    public static Element Language(this Element element, TextLanguage language) => TextStyle(element, style => style with { Language = language });

    /// <summary>
    /// Sets the background.
    /// </summary>
    /// <param name="element">The node.</param>
    /// <param name="paint">The paint: a color or a gradient.</param>
    /// <returns>The same node, for the next call of the chain.</returns>
    public static Element Background(this Element element, Paint paint)
    {
        element.TreeOrThrow().SetBackground(element.Node, paint);
        return element;
    }

    /// <summary>
    /// Sets the background from a signal, written again whenever it changes.
    /// </summary>
    /// <param name="element">The node.</param>
    /// <param name="paint">The signal.</param>
    /// <returns>The same node, for the next call of the chain.</returns>
    /// <exception cref="InvalidOperationException">There is no current scope to own the effect.</exception>
    public static Element Background(this Element element, IReadOnlySignal<Paint> paint) => Bind(element, NodeProperties.Background, paint);

    /// <summary>
    /// Sets the radius of the four corners, which the shadow, the background, the border, and the hit region take.
    /// </summary>
    /// <param name="element">The node.</param>
    /// <param name="radius">The radius.</param>
    /// <returns>The same node, for the next call of the chain.</returns>
    public static Element CornerRadius(this Element element, float radius)
    {
        element.TreeOrThrow().SetCornerRadii(element.Node, new CornerRadii(radius));
        return element;
    }

    /// <summary>
    /// Sets the shadow under the box.
    /// </summary>
    /// <param name="element">The node.</param>
    /// <param name="shadow">The shadow.</param>
    /// <returns>The same node, for the next call of the chain.</returns>
    public static Element Shadow(this Element element, BoxShadow shadow)
    {
        element.TreeOrThrow().SetShadow(element.Node, shadow);
        return element;
    }

    /// <summary>
    /// Sets the color of text: a text node's own, or, for another node, the color the text nodes below it take when they have none of
    /// their own (design §5.0, "The color of text").
    /// </summary>
    /// <param name="element">The node.</param>
    /// <param name="color">The color.</param>
    /// <returns>The same node, for the next call of the chain.</returns>
    public static Element Foreground(this Element element, Color color)
    {
        WriteForeground(element.TreeOrThrow(), element.Node, color);
        return element;
    }

    /// <summary>
    /// Sets the color of text from a signal, written again whenever it changes.
    /// </summary>
    /// <param name="element">The node.</param>
    /// <param name="color">The signal.</param>
    /// <returns>The same node, for the next call of the chain.</returns>
    /// <exception cref="InvalidOperationException">There is no current scope to own the effect.</exception>
    public static Element Foreground(this Element element, IReadOnlySignal<Color> color) => Bind(element, NodeProperties.Foreground, color);

    /// <summary>
    /// Sets the opacity of the node and its subtree.
    /// </summary>
    /// <param name="element">The node.</param>
    /// <param name="opacity">The opacity, from 0 to 1.</param>
    /// <returns>The same node, for the next call of the chain.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="opacity"/> is not a number from 0 to 1.</exception>
    public static Element Opacity(this Element element, float opacity)
    {
        element.TreeOrThrow().SetOpacity(element.Node, opacity);
        return element;
    }

    /// <summary>
    /// Sets the transform of the node and its subtree, about the center of its border box. It changes no layout.
    /// </summary>
    /// <param name="element">The node.</param>
    /// <param name="transform">The transform.</param>
    /// <returns>The same node, for the next call of the chain.</returns>
    /// <exception cref="ArgumentOutOfRangeException">An element of <paramref name="transform"/> is not a finite number.</exception>
    public static Element Transform(this Element element, Matrix3x2 transform)
    {
        element.TreeOrThrow().SetTransform(element.Node, transform);
        return element;
    }

    /// <summary>
    /// Sets what the hovered state lays over the node's look.
    /// </summary>
    /// <param name="element">The node.</param>
    /// <param name="style">The values that replace the node's while the pointer is over it.</param>
    /// <returns>The same node, for the next call of the chain.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The style's opacity or transform is out of its range.</exception>
    public static Element Hovered(this Element element, PaintStyle style)
    {
        element.TreeOrThrow().SetHoveredStyle(element.Node, style);
        return element;
    }

    /// <summary>
    /// Sets what the pressed state lays over the node's hovered look.
    /// </summary>
    /// <param name="element">The node.</param>
    /// <param name="style">The values that replace the node's while the left button is down on it.</param>
    /// <returns>The same node, for the next call of the chain.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The style's opacity or transform is out of its range.</exception>
    public static Element Pressed(this Element element, PaintStyle style)
    {
        element.TreeOrThrow().SetPressedStyle(element.Node, style);
        return element;
    }

    /// <summary>
    /// Sets what the open state lays over the node's pressed look, under the focus states (design §2.13): while a popup opened against
    /// the node is open, a tooltip aside, from its opening to the start of its close (design §7.3, "The anchor's Open"). A menu's
    /// anchor, a menu bar's item, and a select's box show with it that their popup is open, while the focus is in the popup.
    /// </summary>
    /// <param name="element">The node.</param>
    /// <param name="style">The values that replace the node's while a popup is open against it.</param>
    /// <returns>The same node, for the next call of the chain.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The style's opacity or transform is out of its range.</exception>
    public static Element Open(this Element element, PaintStyle style)
    {
        element.TreeOrThrow().SetOpenStyle(element.Node, style);
        return element;
    }

    /// <summary>
    /// Sets what the focus-within state lays over the node's pressed look: while the node or one of its descendants has the focus.
    /// </summary>
    /// <param name="element">The node.</param>
    /// <param name="style">The values that replace the node's while the focus is in its subtree.</param>
    /// <returns>The same node, for the next call of the chain.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The style's opacity or transform is out of its range.</exception>
    public static Element FocusWithin(this Element element, PaintStyle style)
    {
        element.TreeOrThrow().SetFocusWithinStyle(element.Node, style);
        return element;
    }

    /// <summary>
    /// Sets what the focused state lays over the node's focus-within look: while the node has the focus and the window is active.
    /// </summary>
    /// <param name="element">The node.</param>
    /// <param name="style">The values that replace the node's while it has the focus.</param>
    /// <returns>The same node, for the next call of the chain.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The style's opacity or transform is out of its range.</exception>
    public static Element Focused(this Element element, PaintStyle style)
    {
        element.TreeOrThrow().SetFocusedStyle(element.Node, style);
        return element;
    }

    /// <summary>
    /// Sets what the focus-visible state lays over the node's focused look: while the node has the focus and the last input was the
    /// keyboard, the state that shows a focus ring (design §7, "Keys and the focus"). A press clears it; the next key brings it back.
    /// </summary>
    /// <param name="element">The node.</param>
    /// <param name="style">The values that replace the node's while its focus shows.</param>
    /// <returns>The same node, for the next call of the chain.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The style's opacity or transform is out of its range.</exception>
    public static Element FocusVisible(this Element element, PaintStyle style)
    {
        element.TreeOrThrow().SetFocusVisibleStyle(element.Node, style);
        return element;
    }

    /// <summary>
    /// Sets the node's draw function, called after its box and before its children each time the window draws (design §5.0). An
    /// exception it throws goes to <see cref="ReactiveRuntime.UnhandledExceptionHandler"/>, and the rest of the frame is drawn.
    /// </summary>
    /// <param name="element">The node.</param>
    /// <param name="draw">The draw function.</param>
    /// <returns>The same node, for the next call of the chain.</returns>
    public static Element Draw(this Element element, DrawFunc draw)
    {
        ArgumentNullException.ThrowIfNull(draw);
        element.TreeOrThrow().SetDraw(element.Node, draw);
        return element;
    }

    /// <summary>
    /// Makes one of the node's paint values follow its changes over a duration (design §2.14, "Transitions"): when the value the
    /// node draws with changes, from a binding or from a pseudo-state's look, it moves there from the value on the screen along the
    /// curve, with the duration started again. The values are the background, the border's paint, the color of the text, the
    /// opacity, the transform, the corner radii, and the shadow; a layout value moves with an <see cref="Animation"/> bound to it.
    /// </summary>
    /// <typeparam name="T">The value's type.</typeparam>
    /// <param name="element">The node.</param>
    /// <param name="property">The value, one of <see cref="NodeProperties"/>' paint values.</param>
    /// <param name="duration">The duration of a move; 0 stops the value following.</param>
    /// <param name="curve">The curve of a move; <see cref="Curve.Linear"/> when <see langword="null"/>.</param>
    /// <returns>The same node, for the next call of the chain.</returns>
    /// <exception cref="ArgumentException">The property is not a paint value.</exception>
    public static Element Transition<T>(this Element element, NodeProperty<T> property, TimeSpan duration, Curve? curve = null)
    {
        ArgumentNullException.ThrowIfNull(property);
        ArgumentOutOfRangeException.ThrowIfLessThan(duration, TimeSpan.Zero);
        NodeTransitions.Kind kind = property switch
        {
            _ when ReferenceEquals(property, NodeProperties.Background) => NodeTransitions.Kind.Background,
            _ when ReferenceEquals(property, NodeProperties.BorderPaint) => NodeTransitions.Kind.BorderPaint,
            _ when ReferenceEquals(property, NodeProperties.Foreground) => NodeTransitions.Kind.Foreground,
            _ when ReferenceEquals(property, NodeProperties.Opacity) => NodeTransitions.Kind.Opacity,
            _ when ReferenceEquals(property, NodeProperties.Transform) => NodeTransitions.Kind.Transform,
            _ when ReferenceEquals(property, NodeProperties.CornerRadius) => NodeTransitions.Kind.CornerRadius,
            _ when ReferenceEquals(property, NodeProperties.Shadow) => NodeTransitions.Kind.Shadow,
            _ => throw new ArgumentException(
                $"A transition moves a paint value; {property.Name} is not one (bind an Animation to it instead).",
                nameof(property)
            ),
        };
        element.TreeOrThrow().SetTransition(element.Node, kind, duration, curve ?? Curve.Linear);
        return element;
    }

    /// <summary>
    /// Gives the node an ID that the tests find it by (<c>XylemUI.Testing</c>'s <c>Find</c>, design §12.5, "The keys"). Only the nodes
    /// given one hold it, and it goes when the node is removed.
    /// </summary>
    /// <param name="element">The node.</param>
    /// <param name="id">The ID; it replaces the one the node had.</param>
    /// <returns>The same node, for the next call of the chain.</returns>
    public static Element TestId(this Element element, string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        element.TreeOrThrow().SetTestId(element.Node, id);
        return element;
    }

    /// <summary>
    /// Sets the cursor over the node and its descendants that set none.
    /// </summary>
    /// <param name="element">The node.</param>
    /// <param name="cursor">The cursor.</param>
    /// <returns>The same node, for the next call of the chain.</returns>
    public static Element Cursor(this Element element, CursorKind cursor)
    {
        element.TreeOrThrow().SetCursor(element.Node, cursor);
        return element;
    }

    /// <summary>
    /// Sets what the node does with the content that overflows it on each axis (design §3.4.1, "The scroll container"). A node
    /// whose overflow is not <see cref="XylemUI.Overflow.Visible"/> clips its children to its padding box; one that is
    /// <see cref="XylemUI.Overflow.Hidden"/> or <see cref="XylemUI.Overflow.Scroll"/> on an axis scrolls, and the wheel scrolls
    /// the axes that are <see cref="XylemUI.Overflow.Scroll"/>.
    /// </summary>
    /// <param name="element">The node.</param>
    /// <param name="x">The overflow of the width.</param>
    /// <param name="y">The overflow of the height.</param>
    /// <returns>The same node, for the next call of the chain.</returns>
    /// <exception cref="ArgumentOutOfRangeException">An overflow is not a value of <see cref="XylemUI.Overflow"/>.</exception>
    /// <remarks>
    /// Inside this class the method's name is found before the type's, so the values are written as <c>XylemUI.Overflow</c>
    /// (CS0119, design §3.4.1, "The names").
    /// </remarks>
    public static Element Overflow(this Element element, Overflow x, Overflow y)
    {
        if (!Enum.IsDefined(x) || !Enum.IsDefined(y))
        {
            throw new ArgumentOutOfRangeException(!Enum.IsDefined(x) ? nameof(x) : nameof(y), "The overflow is a value of Overflow.");
        }

        return Layout(element, style => style with { OverflowX = x, OverflowY = y });
    }

    /// <summary>
    /// Attaches a scroll state to the node: the wheel and <see cref="XylemUI.ScrollState.ScrollTo"/> write its position, and the
    /// layout its range (design §3.4.1). A state attached to another node is moved to this one.
    /// </summary>
    /// <param name="element">The node.</param>
    /// <param name="state">The state.</param>
    /// <returns>The same node, for the next call of the chain.</returns>
    public static Element ScrollState(this Element element, ScrollState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        element.TreeOrThrow().SetScrollState(element.Node, state);
        return element;
    }

    /// <summary>
    /// Makes the node take the pointer with no handler, so that it does not reach the nodes behind it (design §7, "Pointer input").
    /// </summary>
    /// <param name="element">The node.</param>
    /// <returns>The same node, for the next call of the chain.</returns>
    public static Element BlocksPointer(this Element element)
    {
        element.TreeOrThrow().SetBlocksPointer(element.Node, true);
        return element;
    }

    /// <summary>
    /// Sets whether the node and its subtree take the pointer (design §7.3, "Passing the pointer"): with false, none of them writes a
    /// hit region, a draw function's regions included, and the pointer reaches the nodes behind them (Avalonia's
    /// <c>IsHitTestVisible</c>, Flutter's <c>IgnorePointer</c>).
    /// </summary>
    /// <param name="element">The node.</param>
    /// <param name="visible">Whether the subtree takes the pointer.</param>
    /// <returns>The same node, for the next call of the chain.</returns>
    public static Element HitTestVisible(this Element element, bool visible)
    {
        element.TreeOrThrow().SetHitTestVisible(element.Node, visible);
        return element;
    }

    /// <summary>
    /// Opens a popup against the node while <paramref name="isOpen"/> is true (design §7.3): <paramref name="render"/> runs in a scope
    /// of its own under the current one, and its node is drawn over the window in a render root of its own, placed against this
    /// node. When the signal turns false, the popup leaves and its scope is disposed; a popup that closes itself (a press outside it,
    /// Esc, the window's deactivation, a scroll of the node's scroll containers) writes false to the signal. The keys of the popup's
    /// focused node go on to this node and its ancestors; the pointer's events stop at the popup's root.
    /// </summary>
    /// <param name="anchor">The node the popup is placed against.</param>
    /// <param name="isOpen">Whether the popup is open.</param>
    /// <param name="render">What the popup shows: a node of the same tree with no parent.</param>
    /// <param name="options">How it is placed and closed; the defaults when <see langword="null"/>.</param>
    /// <returns>The same node, for the next call of the chain.</returns>
    /// <exception cref="InvalidOperationException">There is no current scope, or <paramref name="render"/> returns no node of the tree
    /// with no parent.</exception>
    public static Element Popup(this Element anchor, Signal<bool> isOpen, Func<Element> render, PopupOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(isOpen);
        ArgumentNullException.ThrowIfNull(render);
        UiTree tree = anchor.TreeOrThrow();
        if (Scope.CurrentRef.IsNone)
        {
            throw new InvalidOperationException("Popup is made inside a scope, which owns its content; there is no current scope.");
        }

        _ = PopupBlock.Create(tree, anchor.Node, isOpen, render, options ?? new PopupOptions());
        return anchor;
    }

    /// <summary>
    /// Sets whether the node takes the focus (design §7, "Keys and the focus"): a press on it or on a descendant that takes none
    /// gives it the focus, Tab stops at it, and <see cref="Focus"/> can give it the focus, while neither it nor an ancestor is
    /// hidden. A node does not take the focus by default. A focused node that stops taking it loses the focus.
    /// </summary>
    /// <param name="element">The node.</param>
    /// <param name="focusable">Whether the node takes the focus.</param>
    /// <returns>The same node, for the next call of the chain.</returns>
    public static Element Focusable(this Element element, bool focusable = true)
    {
        element.TreeOrThrow().SetFocusable(element.Node, focusable);
        return element;
    }

    /// <summary>
    /// Sets whether a press on the node leaves the focus where it is (design §4.9, "A press that keeps the focus"): the press walks
    /// up from the pressed node, and when it reaches this node before a node that takes the focus, the focus does not move, even when
    /// this node takes the focus itself. A toolbar's button with it leaves the focus in a text field; a node that takes the focus
    /// inside it still takes it. Off by default: a pressed button takes the focus, as on Windows.
    /// </summary>
    /// <param name="element">The node.</param>
    /// <param name="keeps">Whether a press keeps the focus.</param>
    /// <returns>The same node, for the next call of the chain.</returns>
    public static Element KeepsFocusOnPress(this Element element, bool keeps = true)
    {
        element.TreeOrThrow().SetKeepsFocusOnPress(element.Node, keeps);
        return element;
    }

    /// <summary>
    /// Keeps the text of the node's subtree out of the selection of a selection area around it (design §4.10, Flutter's
    /// <c>SelectionContainer.disabled</c>): a drag across it selects none of it, a copy leaves it out, and its text shows no I-beam.
    /// </summary>
    /// <param name="element">The node.</param>
    /// <returns>The same node, for the next call of the chain.</returns>
    public static Element Unselectable(this Element element)
    {
        element.TreeOrThrow().SetUnselectable(element.Node);
        return element;
    }

    /// <summary>
    /// Sets whether the node itself is disabled (design §7, "Disabled"). A disabled node and its descendants have the disabled state:
    /// their handlers of the pointer, the keys, and the focus are not called, they take no focus, and Tab skips them. They still take
    /// the pointer from the nodes behind them; the nearest ancestor that is not disabled gets its route, its hover, its press, and its
    /// cursor. A focused node in the subtree loses the focus. Disabling an ancestor again does not enable a descendant marked itself.
    /// </summary>
    /// <param name="element">The node.</param>
    /// <param name="disabled">Whether the node is disabled.</param>
    /// <returns>The same node, for the next call of the chain.</returns>
    public static Element Disabled(this Element element, bool disabled = true)
    {
        element.TreeOrThrow().SetDisabled(element.Node, disabled);
        return element;
    }

    /// <summary>
    /// Sets whether the node itself is disabled from a signal, written again whenever it changes.
    /// </summary>
    /// <param name="element">The node.</param>
    /// <param name="disabled">The signal.</param>
    /// <returns>The same node, for the next call of the chain.</returns>
    /// <exception cref="InvalidOperationException">There is no current scope to own the effect.</exception>
    public static Element Disabled(this Element element, IReadOnlySignal<bool> disabled) => Bind(element, NodeProperties.Disabled, disabled);

    /// <summary>
    /// Sets what the disabled state lays over every other look of the node: while it or an ancestor is disabled.
    /// </summary>
    /// <param name="element">The node.</param>
    /// <param name="style">The values that replace the node's while it is disabled.</param>
    /// <returns>The same node, for the next call of the chain.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The style's opacity or transform is out of its range.</exception>
    public static Element Disabled(this Element element, PaintStyle style)
    {
        element.TreeOrThrow().SetDisabledStyle(element.Node, style);
        return element;
    }

    /// <summary>
    /// Sets the node's place in the order of Tab (design §7.1): a smaller index comes first, and the nodes of the same index come in
    /// the order of the tree. 0 by default; a negative index puts a node before the ones that set none.
    /// </summary>
    /// <param name="element">The node.</param>
    /// <param name="index">The index.</param>
    /// <returns>The same node, for the next call of the chain.</returns>
    public static Element TabIndex(this Element element, int index)
    {
        element.TreeOrThrow().SetTabIndex(element.Node, index);
        return element;
    }

    /// <summary>
    /// Gives the node the focus (design §7, "Keys and the focus"). It changes whether the focus ring shows no more than the last input
    /// did. While the window is not active, the node takes the focus when the window is active again. A call from a handler of a
    /// focus event moves the focus after the events being sent.
    /// </summary>
    /// <param name="element">The node.</param>
    /// <returns><see langword="true"/> when the node takes the focus; <see langword="false"/>, with nothing changed, when it is not
    /// focusable, not in the window's tree, or hidden with itself or an ancestor.</returns>
    public static bool Focus(this Element element) => element.TreeOrThrow().Focus.Focus(element.Node);

    /// <summary>
    /// Returns a signal of whether the node has the focus: the value of its focused state, <see langword="false"/> while the window is
    /// not active. It is made the first time and dropped with the node.
    /// </summary>
    /// <param name="element">The node.</param>
    /// <returns>The signal.</returns>
    public static IReadOnlySignal<bool> IsFocused(this Element element) => element.TreeOrThrow().IsFocusedSignal(element.Node);

    /// <summary>
    /// Adds a handler of a kind of key event, in a phase of its route from the focused node (design §7, "Keys and the focus"). It runs
    /// in the current scope; once the scope is disposed it is not called, and the handler is removed (design §2.13).
    /// </summary>
    /// <param name="element">The node.</param>
    /// <param name="kind">The kind of event.</param>
    /// <param name="phase">The phase of the route.</param>
    /// <param name="handler">The handler.</param>
    /// <returns>The same node, for the next call of the chain.</returns>
    /// <exception cref="InvalidOperationException">There is no current scope.</exception>
    public static Element On(this Element element, KeyEventKind kind, EventPhase phase, KeyHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        UiTree tree = element.TreeOrThrow();
        NodeHandle node = element.Node;
        RequireScope();
        tree.AddKeyHandler(node, kind, phase, handler);
        Scope.OnCleanup(() =>
        {
            if (tree.Nodes.IsAlive(node))
            {
                _ = tree.RemoveKeyHandler(node, kind, phase, handler);
            }
        });
        return element;
    }

    /// <summary>
    /// Adds a handler of the text typed into the window, in a phase of its route from the focused node (design §4.9, "Text input on
    /// Windows"). It runs in the current scope; once the scope is disposed it is not called, and the handler is removed (design §2.13).
    /// </summary>
    /// <param name="element">The node.</param>
    /// <param name="phase">The phase of the route.</param>
    /// <param name="handler">The handler.</param>
    /// <returns>The same node, for the next call of the chain.</returns>
    /// <exception cref="InvalidOperationException">There is no current scope.</exception>
    public static Element OnTextInput(this Element element, EventPhase phase, TextInputHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        UiTree tree = element.TreeOrThrow();
        NodeHandle node = element.Node;
        RequireScope();
        tree.AddTextHandler(node, phase, handler);
        Scope.OnCleanup(() =>
        {
            if (tree.Nodes.IsAlive(node))
            {
                _ = tree.RemoveTextHandler(node, phase, handler);
            }
        });
        return element;
    }

    /// <summary>
    /// Adds a handler of a kind of focus event, in a phase of its route from the node that took or lost the focus (design §7, "Keys
    /// and the focus"); <see cref="FocusEvent.Target"/> tells the node's own change from a descendant's. It runs in the current scope;
    /// once the scope is disposed it is not called, and the handler is removed (design §2.13).
    /// </summary>
    /// <param name="element">The node.</param>
    /// <param name="kind">The kind of event.</param>
    /// <param name="phase">The phase of the route.</param>
    /// <param name="handler">The handler.</param>
    /// <returns>The same node, for the next call of the chain.</returns>
    /// <exception cref="InvalidOperationException">There is no current scope.</exception>
    public static Element On(this Element element, FocusEventKind kind, EventPhase phase, FocusHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        UiTree tree = element.TreeOrThrow();
        NodeHandle node = element.Node;
        RequireScope();
        tree.AddFocusHandler(node, kind, phase, handler);
        Scope.OnCleanup(() =>
        {
            if (tree.Nodes.IsAlive(node))
            {
                _ = tree.RemoveFocusHandler(node, kind, phase, handler);
            }
        });
        return element;
    }

    /// <summary>
    /// Adds a handler of clicks: the left button went down and up over the node or a descendant. It runs in the current scope; once
    /// the scope is disposed it is not called (design §2.13).
    /// </summary>
    /// <param name="element">The node.</param>
    /// <param name="handler">The handler.</param>
    /// <returns>The same node, for the next call of the chain.</returns>
    /// <exception cref="InvalidOperationException">There is no current scope.</exception>
    public static Element OnClick(this Element element, Action handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return On(element, PointerEventKind.Click, EventPhase.Bubble, (in PointerEvent _) => handler());
    }

    /// <summary>
    /// Adds a handler of a kind of pointer event, in a phase of its route (design §7, "Pointer input"). It runs in the current scope;
    /// once the scope is disposed it is not called, and the handler is removed (design §2.13).
    /// </summary>
    /// <param name="element">The node.</param>
    /// <param name="kind">The kind of event.</param>
    /// <param name="phase">The phase of a routed event; an enter and a leave call the handlers of either phase.</param>
    /// <param name="handler">The handler.</param>
    /// <returns>The same node, for the next call of the chain.</returns>
    /// <exception cref="InvalidOperationException">There is no current scope.</exception>
    public static Element On(this Element element, PointerEventKind kind, EventPhase phase, PointerHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        UiTree tree = element.TreeOrThrow();
        NodeHandle node = element.Node;
        RequireScope();
        tree.AddPointerHandler(node, kind, phase, handler);
        Scope.OnCleanup(() =>
        {
            if (tree.Nodes.IsAlive(node))
            {
                _ = tree.RemovePointerHandler(node, kind, phase, handler);
            }
        });
        return element;
    }

    /// <summary>
    /// Drives a property of the node from a signal (design §2.4): an effect of the current scope writes the signal's value to the node
    /// now and again whenever it changes, before the layout. Disposing the scope stops it.
    /// </summary>
    /// <typeparam name="T">The value's type.</typeparam>
    /// <param name="element">The node.</param>
    /// <param name="property">The property.</param>
    /// <param name="value">The signal.</param>
    /// <returns>The same node, for the next call of the chain.</returns>
    /// <exception cref="InvalidOperationException">There is no current scope to own the effect.</exception>
    public static Element Bind<T>(this Element element, NodeProperty<T> property, IReadOnlySignal<T> value)
    {
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(value);
        UiTree tree = element.TreeOrThrow();
        NodeHandle node = element.Node;
        if (Scope.CurrentRef.IsNone)
        {
            throw new InvalidOperationException("A binding is made inside a scope, which owns its effect; there is no current scope.");
        }

        Action<UiTree, NodeHandle, T> setter = property.Setter;
        _ = Effect.Create(() =>
        {
            T current = value.Get();
            if (tree.Nodes.IsAlive(node))
            {
                setter(tree, node, current);
            }
        });
        return element;
    }

    /// <summary>
    /// Sets a text node's own color, or the color another node gives the text nodes below it (design §5.0, "The color of text").
    /// </summary>
    /// <param name="tree">The tree the node is in.</param>
    /// <param name="node">The node.</param>
    /// <param name="color">The color.</param>
    internal static void WriteForeground(UiTree tree, NodeHandle node, Color color)
    {
        if (tree.Nodes.GetRef(node).Text is not null)
        {
            tree.SetTextColor(node, color);
        }
        else
        {
            tree.SetForeground(node, color);
        }
    }

    /// <summary>
    /// Fills an empty box with a row for each item, and keeps the rows in the items' order as the items change (design §2.7, "The
    /// shape of V3"). A row is found again by its item's key: a key that stays keeps its row, with its focus and its state, and the
    /// row's signal takes the item's new value; a key that comes makes a row in a scope of its own; a key that goes disposes its row
    /// and what the row made. Rows that change places move with the fewest moves (the longest increasing subsequence). A key given
    /// twice maps to the rows of that key in order. The rows are the box's only children.
    /// </summary>
    /// <typeparam name="T">The type of an item.</typeparam>
    /// <typeparam name="TKey">The type of an item's key.</typeparam>
    /// <param name="container">The box, with no child.</param>
    /// <param name="items">The items.</param>
    /// <param name="key">The key of an item.</param>
    /// <param name="render">Makes a row from its item's signal: a node of the box's tree with no parent. A render that throws is
    /// reported to <see cref="ReactiveRuntime.UnhandledExceptionHandler"/>, and its row is an empty hidden node.</param>
    /// <returns>The box, for the next call of the chain.</returns>
    /// <exception cref="ArgumentException">The box has children, or a <c>For</c> fills it already.</exception>
    /// <exception cref="InvalidOperationException">There is no current scope.</exception>
    public static Element For<T, TKey>(
        this Element container,
        IReadOnlySignal<IReadOnlyList<T>> items,
        Func<T, TKey> key,
        Func<IReadOnlySignal<T>, Element> render
    )
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(render);
        StartFor(container, items, key);
        ForBlock<T, TKey>.Create(container.Tree!, container.Node, items, key, render, null);
        return container;
    }

    /// <summary>
    /// Fills an empty box with a row for each item, as <see cref="For{T, TKey}(Element, IReadOnlySignal{IReadOnlyList{T}}, Func{T, TKey}, Func{IReadOnlySignal{T}, Element})"/>
    /// does, and gives each row the signal of its place in the items too, which changes as the rows move.
    /// </summary>
    /// <typeparam name="T">The type of an item.</typeparam>
    /// <typeparam name="TKey">The type of an item's key.</typeparam>
    /// <param name="container">The box, with no child.</param>
    /// <param name="items">The items.</param>
    /// <param name="key">The key of an item.</param>
    /// <param name="render">Makes a row from its item's signal and its place's signal.</param>
    /// <returns>The box, for the next call of the chain.</returns>
    /// <exception cref="ArgumentException">The box has children, or a <c>For</c> fills it already.</exception>
    /// <exception cref="InvalidOperationException">There is no current scope.</exception>
    public static Element For<T, TKey>(
        this Element container,
        IReadOnlySignal<IReadOnlyList<T>> items,
        Func<T, TKey> key,
        Func<IReadOnlySignal<T>, IReadOnlySignal<int>, Element> render
    )
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(render);
        StartFor(container, items, key);
        ForBlock<T, TKey>.Create(container.Tree!, container.Node, items, key, null, render);
        return container;
    }

    // The checks of For, and the mark that the box takes no second one.
    private static void StartFor<T, TKey>(Element container, IReadOnlySignal<IReadOnlyList<T>> items, Func<T, TKey> key)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(key);
        UiTree tree = container.TreeOrThrow();
        RequireScope();
        if (!tree.Nodes.GetRef(container.Node).FirstChild.IsNone || !tree.ForContainers.Add(container.Node))
        {
            throw new ArgumentException("For fills a box with no child, and only one For fills a box.", nameof(container));
        }
    }

    private static void RequireScope()
    {
        if (Scope.CurrentRef.IsNone)
        {
            throw new InvalidOperationException("A handler is added inside a scope, which owns it; there is no current scope.");
        }
    }

    private static Element Layout(Element element, Func<LayoutStyle, LayoutStyle> change)
    {
        UiTree tree = element.TreeOrThrow();
        tree.SetStyle(element.Node, change(tree.Nodes.GetRef(element.Node).Layout.Style));
        return element;
    }

    private static Element TextStyle(Element element, Func<TextStyle, TextStyle> change)
    {
        UiTree tree = element.TreeOrThrow();
        if (tree.Nodes.GetRef(element.Node).Text is not { } leaf)
        {
            throw new InvalidOperationException("Only a text node has a text style.");
        }

        tree.SetTextStyle(element.Node, change(leaf.Style));
        return element;
    }

    private static float Check(float length, string name) =>
        float.IsFinite(length) && length >= 0
            ? length
            : throw new ArgumentOutOfRangeException(name, length, "A length is a finite number at or above 0.");
}
