using XylemUI;
using XylemUI.Primitives;
using static XylemUI.Elements;
using static XylemUI.Primitives.Primitive;

namespace Todo;

/// <summary>
/// The todo list of row V3c (design §2.7, "How it is checked"): a heading with the number of items and of the items left, a field
/// and the button that add an item of the name typed (row P02b, design §4.9, "Todo"), the buttons that filter the list and reverse
/// its order, and the items in a scroll view, each a button that marks it done and one that removes it.
/// </summary>
public static class TodoApp
{
    /// <summary>
    /// Gets the background of the buttons, which the tests read and the startup measurement watches for.
    /// </summary>
    public static Color ButtonBackground { get; } = Color.FromRgb(0x3366CC);

    /// <summary>
    /// Gets the background of the filter button chosen.
    /// </summary>
    public static Color ChosenBackground { get; } = Color.FromRgb(0x1F3F7F);

    /// <summary>
    /// Gets the color of an item's text when it is not done.
    /// </summary>
    public static Color TextColor { get; } = Color.FromRgb(0xFFFFFF);

    /// <summary>
    /// Gets the color of an item's text when it is done.
    /// </summary>
    public static Color DoneColor { get; } = Color.FromRgb(0xA0A0A0);

    /// <summary>
    /// Gets the background of the field where a name is typed.
    /// </summary>
    public static Color FieldBackground { get; } = Color.FromRgb(0x202838);

    /// <summary>
    /// Gets the placeholder of the field, shown while it is empty.
    /// </summary>
    public static string Placeholder => "What needs doing?";

    /// <summary>
    /// Gets the height of the scroll view that holds the items.
    /// </summary>
    public static float ListHeight => 200;

    /// <summary>
    /// Makes the list with a state of its own.
    /// </summary>
    /// <returns>The list's column.</returns>
    public static Element Todo() => Todo(new TodoModel());

    /// <summary>
    /// Makes the list over a state the caller holds: a column of the heading, the field with its button, the buttons, and the
    /// items in a scroll view. Enter in the field and the button both add; the button keeps the focus in the field. The column is a
    /// selection area (row P02b2, design §4.10): a drag selects the heading's and the items' text across them, and Control+C copies
    /// it.
    /// </summary>
    /// <param name="model">The state.</param>
    /// <returns>The list's column.</returns>
    public static Element Todo(TodoModel model) =>
        SelectionArea(
            Column(
                    Row(
                            Text((ref text) => text.Write($"todos: {model.Items.Get().Count}")),
                            Text((ref text) => text.Write($"left: {model.Left.Get()}"))
                        )
                        .Gap(16),
                    Row(
                            TextField(model.Draft, new TextFieldOptions { Placeholder = Placeholder, OnSubmit = _ => model.Add() })
                                .Width(260)
                                .Padding(8, 6)
                                .Background(FieldBackground)
                                .Foreground(TextColor)
                                .CornerRadius(4),
                            Styled(Text("Add"), model.Add).KeepsFocusOnPress()
                        )
                        .Gap(6),
                    Row(
                            FilterButton(model, TodoFilter.All, "All"),
                            FilterButton(model, TodoFilter.Active, "Active"),
                            FilterButton(model, TodoFilter.Done, "Done"),
                            Styled(Text((ref text) => text.Write(model.NewestFirst.Get() ? "Newest first" : "Oldest first")), model.Reverse)
                        )
                        .Gap(6),
                    ScrollView(
                            Column().Gap(4).For(model.Shown, item => item.Id, item => Row(ItemButton(model, item), RemoveButton(model, item)).Gap(4))
                        )
                        .Height(ListHeight)
                )
                .Gap(8)
                .Padding(12)
        );

    // The button that marks an item done: its name after a mark, gray when done.
    private static Element ItemButton(TodoModel model, IReadOnlySignal<TodoItem> item) =>
        Styled(
                Text(
                    (ref text) =>
                    {
                        TodoItem current = item.Get();
                        text.Write($"{(current.Done ? "[x]" : "[ ]")} {current.Name}");
                    }
                ),
                () => model.Toggle(item.Get().Id)
            )
            .Foreground(Computed.Create(() => item.Get().Done ? DoneColor : TextColor));

    private static Element RemoveButton(TodoModel model, IReadOnlySignal<TodoItem> item) => Styled(Text("×"), () => model.Remove(item.Get().Id));

    // A filter's button: chosen, it has a darker background.
    private static Element FilterButton(TodoModel model, TodoFilter filter, string label) =>
        Styled(Text(label), () => model.Show(filter))
            .Background(Computed.Create(() => (Paint)(model.Filter.Get() == filter ? ChosenBackground : ButtonBackground)));

    private static Element Styled(Element content, Action onClick) =>
        Button(content, onClick)
            .Padding(8, 6)
            .Background(ButtonBackground)
            .Foreground(TextColor)
            .CornerRadius(4)
            .Hovered(new PaintStyle { Background = Color.FromRgb(0x4477DD) })
            .Pressed(new PaintStyle { Background = Color.FromRgb(0x2255AA) });
}
