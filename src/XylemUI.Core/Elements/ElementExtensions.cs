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
        if (Scope.CurrentRef.IsNone)
        {
            throw new InvalidOperationException("A handler is added inside a scope, which owns it; there is no current scope.");
        }

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

    // A text node's own color, or the color another node gives the text nodes below it (design §5.0, "The color of text").
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
