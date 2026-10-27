using XylemUI;
using static XylemUI.Elements;
using static XylemUI.Primitives.Primitive;

namespace Counter;

/// <summary>
/// The first app of the component API (design §2.13, row V1d): a text that counts the clicks on a button.
/// </summary>
public static class CounterApp
{
    /// <summary>
    /// Gets the background of the button, which the tests read at its center.
    /// </summary>
    public static Color ButtonBackground { get; } = Color.FromRgb(0x3366CC);

    /// <summary>
    /// Makes the counter: a column of the text "clicks: N" and a button that adds one to N.
    /// </summary>
    /// <returns>The column.</returns>
    public static Element Counter()
    {
        var count = Signal.Create(0);

        return Column(
            Text((ref text) => text.Write($"clicks: {count.Get()}")),
            Button("+1", () => count.Set(count.Get() + 1))
                .Padding(8)
                .Background(ButtonBackground)
                .Foreground(Color.FromRgb(0xFFFFFF))
                .CornerRadius(4)
                .Hovered(new PaintStyle { Background = Color.FromRgb(0x4477DD) })
                .Pressed(new PaintStyle { Background = Color.FromRgb(0x2255AA) })
        );
    }
}
