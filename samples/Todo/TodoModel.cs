using XylemUI;

namespace Todo;

/// <summary>
/// One item of the list: its number, its name, and whether it is done.
/// </summary>
/// <param name="Id">The number it was made with, which also keys its row.</param>
/// <param name="Name">The name it shows.</param>
/// <param name="Done">A value indicating whether it is done.</param>
public sealed record TodoItem(int Id, string Name, bool Done);

/// <summary>
/// Which items the list shows.
/// </summary>
public enum TodoFilter
{
    /// <summary>Every item.</summary>
    All,

    /// <summary>The items not done.</summary>
    Active,

    /// <summary>The items done.</summary>
    Done,
}

/// <summary>
/// The state of the todo list (row V3c): the items, the filter, the order, the name being typed (row P02b), and what the buttons
/// do with them. The list the rows show is a computed value of the first three.
/// </summary>
public sealed class TodoModel
{
    private readonly Signal<IReadOnlyList<TodoItem>> _items = Signal.Create<IReadOnlyList<TodoItem>>([]);
    private readonly Signal<TodoFilter> _filter = Signal.Create(TodoFilter.All);
    private readonly Signal<bool> _newestFirst = Signal.Create(false);
    private readonly Signal<TodoItem?> _pending = Signal.Create<TodoItem?>(null);
    private int _next = 1;

    /// <summary>
    /// Initializes a new instance of the <see cref="TodoModel"/> class, with no item, every item shown, and the oldest first.
    /// </summary>
    public TodoModel()
    {
        Shown = Computed.Create(Select);
        Left = Computed.Create(() => _items.Get().Count(item => !item.Done));
    }

    /// <summary>
    /// Gets the name being typed in the field, which <see cref="Add()"/> takes and empties.
    /// </summary>
    public Signal<string> Draft { get; } = Signal.Create(string.Empty);

    /// <summary>
    /// Gets every item, the oldest first.
    /// </summary>
    public IReadOnlySignal<IReadOnlyList<TodoItem>> Items => _items;

    /// <summary>
    /// Gets the filter.
    /// </summary>
    public IReadOnlySignal<TodoFilter> Filter => _filter;

    /// <summary>
    /// Gets a value indicating whether the newest item comes first.
    /// </summary>
    public IReadOnlySignal<bool> NewestFirst => _newestFirst;

    /// <summary>
    /// Gets the items the filter lets through, in the order chosen.
    /// </summary>
    public IReadOnlySignal<IReadOnlyList<TodoItem>> Shown { get; }

    /// <summary>
    /// Gets the number of items not done.
    /// </summary>
    public IReadOnlySignal<int> Left { get; }

    /// <summary>
    /// Adds an item named after the field's text, and empties the field (design §4.9, "Todo"). A text of spaces alone adds nothing
    /// and stays.
    /// </summary>
    public void Add()
    {
        if (Add(Draft.Get()))
        {
            Draft.Set(string.Empty);
        }
    }

    /// <summary>
    /// Adds an item, not done, named after a text without its leading and trailing white space.
    /// </summary>
    /// <param name="name">The text.</param>
    /// <returns>Whether the item was added: <see langword="false"/> when the text was white space alone.</returns>
    public bool Add(string name)
    {
        string trimmed = name.Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        _items.Set([.. _items.Get(), new TodoItem(_next++, trimmed, false)]);
        return true;
    }

    /// <summary>
    /// Marks an item done, or not done when it was.
    /// </summary>
    /// <param name="id">The item's number.</param>
    public void Toggle(int id) => _items.Set(_items.Get().Select(item => item.Id == id ? item with { Done = !item.Done } : item).ToList());

    /// <summary>
    /// Removes an item.
    /// </summary>
    /// <param name="id">The item's number.</param>
    public void Remove(int id) => _items.Set(_items.Get().Where(item => item.Id != id).ToList());

    /// <summary>
    /// Gets a value indicating whether the dialog that asks before an item is removed is open; the dialog writes false when Esc closes
    /// it.
    /// </summary>
    public Signal<bool> Confirming { get; } = Signal.Create(false);

    /// <summary>
    /// Gets the item the dialog asks about, or <see langword="null"/> before any was asked about.
    /// </summary>
    public IReadOnlySignal<TodoItem?> Pending => _pending;

    /// <summary>
    /// Asks before an item is removed: opens the dialog about it.
    /// </summary>
    /// <param name="id">The item's number.</param>
    public void AskToRemove(int id)
    {
        _pending.Set(_items.Get().FirstOrDefault(item => item.Id == id));
        Confirming.Set(_pending.Get() is not null);
    }

    /// <summary>
    /// Removes the item the dialog asked about, and closes the dialog.
    /// </summary>
    public void ConfirmRemove()
    {
        if (_pending.Get() is { } item)
        {
            Remove(item.Id);
        }

        Confirming.Set(false);
    }

    /// <summary>
    /// Closes the dialog and keeps the item.
    /// </summary>
    public void CancelRemove() => Confirming.Set(false);

    /// <summary>
    /// Shows the items a filter lets through.
    /// </summary>
    /// <param name="filter">The filter.</param>
    public void Show(TodoFilter filter) => _filter.Set(filter);

    /// <summary>
    /// Puts the newest item first, or the oldest first when the newest was.
    /// </summary>
    public void Reverse() => _newestFirst.Set(!_newestFirst.Get());

    private IReadOnlyList<TodoItem> Select()
    {
        IReadOnlyList<TodoItem> items = _items.Get();
        TodoFilter filter = _filter.Get();
        bool newestFirst = _newestFirst.Get();
        var shown = new List<TodoItem>(items.Count);
        for (int i = 0; i < items.Count; i++)
        {
            TodoItem item = items[newestFirst ? items.Count - 1 - i : i];
            if (filter == TodoFilter.All || item.Done == (filter == TodoFilter.Done))
            {
                shown.Add(item);
            }
        }

        return shown;
    }
}
