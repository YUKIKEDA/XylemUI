using XylemUI.Testing.XUnit;
using static XylemUI.Elements;

namespace XylemUI.Testing.Tests;

// Actions with arguments made by their names (design §7, "Actions with arguments made by name", row KM1b). The source generator
// runs on this project, so these tests read what its generated code does at run time.
public sealed class ActionArgumentTests
{
    /// <summary>
    /// An action made by its name gets each argument in its property, read as the property's type, and the properties with no
    /// argument keep their defaults.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - <c>GoTo</c>, a record with init-only properties: <c>Line</c> (int, default 1), <c>Offset</c> (long), <c>Zoom</c> (double),
    ///   <c>Ratio</c> (float), <c>Select</c> (bool), <c>Label</c> (string, default "none"), <c>Where</c> (an enum, default
    ///   <c>Start</c>). <c>Scroll</c>, a class with a settable <c>Lines</c> (int).
    ///
    /// Steps:
    /// - Make <c>test::GoTo</c> with <c>line=42</c>, <c>OFFSET=-9000000000</c>, <c>zoom=1.5</c>, <c>ratio=0.25</c>,
    ///   <c>select=true</c>, <c>label=two words</c>, <c>where=end</c>. Make it with <c>line=7</c> alone. Make <c>test::Scroll</c>
    ///   with <c>lines=-3</c>.
    ///
    /// Expected:
    /// - The first has every value, the enum <c>End</c>; names are matched without case. The second has line 7 and the other
    ///   defaults. The third is a <c>Scroll</c> with -3. No error text.
    /// </remarks>
    [Fact]
    public void TryCreate_WithArguments_SetsThePropertiesFromTheirText()
    {
        Assert.True(
            ActionRegistry.TryCreate(
                "test::GoTo",
                [
                    new("line", "42"),
                    new("OFFSET", "-9000000000"),
                    new("zoom", "1.5"),
                    new("ratio", "0.25"),
                    new("select", "true"),
                    new("label", "two words"),
                    new("where", "end"),
                ],
                out IAction? action,
                out string? error
            )
        );
        Assert.Null(error);
        Assert.Equal(
            new GoTo
            {
                Line = 42,
                Offset = -9_000_000_000,
                Zoom = 1.5,
                Ratio = 0.25f,
                Select = true,
                Label = "two words",
                Where = Place.End,
            },
            Assert.IsType<GoTo>(action)
        );

        Assert.True(ActionRegistry.TryCreate("test::GoTo", [new("line", "7")], out action, out _));
        Assert.Equal(new GoTo { Line = 7 }, action);
        Assert.Equal("none", ((GoTo)action).Label);

        Assert.True(ActionRegistry.TryCreate("test::Scroll", [new("lines", "-3")], out action, out _));
        Assert.Equal(-3, Assert.IsType<Scroll>(action).Lines);
    }

    /// <summary>
    /// A name no action has, an argument the action does not have, and a value that is not of the argument's type are refused,
    /// with a text that says which.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The actions of the first test; <c>test::Generated</c> has no settable property.
    ///
    /// Steps:
    /// - Make <c>test::Missing</c>. Make <c>test::GoTo</c> with <c>column=1</c>; with <c>line=x</c>; with <c>line=1.5</c>; with
    ///   <c>zoom=1,5</c> (a comma is no decimal point in the invariant culture, and no thousands separator is read); with
    ///   <c>select=yes</c>; with <c>where=middle</c>. Make <c>test::Generated</c> with <c>amount=1</c>.
    ///
    /// Expected:
    /// - Each returns false with no action. The first error names <c>test::Missing</c>; the others name the action, the argument,
    ///   and the value.
    /// </remarks>
    [Fact]
    public void TryCreate_UnknownNameArgumentOrValue_IsRefused()
    {
        Assert.False(ActionRegistry.TryCreate("test::Missing", [], out IAction? action, out string? error));
        Assert.Null(action);
        Assert.Contains("test::Missing", error, StringComparison.Ordinal);

        foreach (
            (string name, string argument, string value) in new[]
            {
                ("test::GoTo", "column", "1"),
                ("test::GoTo", "line", "x"),
                ("test::GoTo", "line", "1.5"),
                ("test::GoTo", "zoom", "1,5"),
                ("test::GoTo", "select", "yes"),
                ("test::GoTo", "where", "middle"),
                ("test::Generated", "amount", "1"),
            }
        )
        {
            Assert.False(ActionRegistry.TryCreate(name, [new(argument, value)], out action, out error));
            Assert.Null(action);
            Assert.Contains(name, error, StringComparison.Ordinal);
            Assert.Contains(argument, error, StringComparison.Ordinal);
            Assert.Contains(value, error, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// A row bound by an action's name asks for the action with its arguments, and a name or an argument that is wrong is refused
    /// when the row is added.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An app whose root handles <c>GoTo</c> and records its line.
    ///
    /// Steps:
    /// - Bind <c>ctrl-g</c> to <c>test::GoTo</c> with <c>line=12</c>, and <c>ctrl-h</c> to <c>test::GoTo</c> with no argument;
    ///   press each. Bind a key to <c>test::Missing</c>, and to <c>test::GoTo</c> with <c>line=x</c>.
    ///
    /// Expected:
    /// - The handler records 12, then 1 (the default). The two wrong rows throw <see cref="FormatException"/> and are not added.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task BindByName_MakesTheActionWithItsArguments()
    {
        var lines = new List<int>();
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            Box().Width(10).Height(10).OnAction((in ActionEvent<GoTo> e) => lines.Add(e.Action.Line))
        );
        int rows = app.Keymap.Bindings.Count;
        _ = app.Keymap.BindByName("ctrl-g", "test::GoTo", [new("line", "12")]);
        _ = app.Keymap.BindByName("ctrl-h", "test::GoTo");

        Assert.True(app.Keyboard.Press(Key.G, KeyModifiers.Control));
        Assert.True(app.Keyboard.Press(Key.H, KeyModifiers.Control));
        Assert.Equal([12, 1], lines);

        _ = Assert.Throws<FormatException>(() => app.Keymap.BindByName("ctrl-j", "test::Missing"));
        _ = Assert.Throws<FormatException>(() => app.Keymap.BindByName("ctrl-j", "test::GoTo", [new("line", "x")]));
        Assert.Equal(rows + 2, app.Keymap.Bindings.Count);
    }

    internal enum Place
    {
        Start,
        End,
    }

    [Action("test::GoTo")]
    internal sealed record GoTo : IAction
    {
        public int Line { get; init; } = 1;

        public long Offset { get; init; }

        public double Zoom { get; init; }

        public float Ratio { get; init; }

        public bool Select { get; init; }

        public string Label { get; init; } = "none";

        public Place Where { get; init; }
    }

    [Action("test::Scroll")]
    internal sealed class Scroll : IAction
    {
        public int Lines { get; set; }
    }
}
