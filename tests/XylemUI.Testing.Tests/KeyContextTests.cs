namespace XylemUI.Testing.Tests;

// The contexts of nodes and the conditions of key bindings (design §7, "Shortcuts and actions", row KM1b). The grammar and its
// meaning are GPUI's, so the expected values of the evaluation rows are the assertions of GPUI's own tests
// (zed v1.25.3, crates/gpui/src/keymap/context.rs, test_child_operator and test_not_operator), row for row.
public sealed class KeyContextTests
{
    /// <summary>
    /// A context's text gives its names and its values, and a key with a value is also a name.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - None.
    ///
    /// Steps:
    /// - Parse <c>Editor mode = full  vim_mode=normal</c> and the empty text.
    ///
    /// Expected:
    /// - The first has the names <c>Editor</c>, <c>mode</c>, and <c>vim_mode</c>, not <c>Pane</c>; the value of <c>mode</c> is
    ///   <c>full</c>, of <c>vim_mode</c> <c>normal</c>, and of <c>Editor</c> and <c>Pane</c> none. The empty text has no name.
    /// </remarks>
    [Fact]
    public void KeyContext_Parse_GivesNamesAndValues()
    {
        KeyContext context = KeyContext.Parse("Editor mode = full  vim_mode=normal");

        Assert.True(context.Has("Editor"));
        Assert.True(context.Has("mode"));
        Assert.True(context.Has("vim_mode"));
        Assert.False(context.Has("Pane"));
        Assert.Equal("full", context.Value("mode"));
        Assert.Equal("normal", context.Value("vim_mode"));
        Assert.Null(context.Value("Editor"));
        Assert.Null(context.Value("Pane"));
        Assert.False(KeyContext.Parse(string.Empty).Has("Editor"));
    }

    /// <summary>
    /// Text that is not a context or not a condition throws <see cref="FormatException"/>.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - None.
    ///
    /// Steps:
    /// - Parse each context text and each condition text.
    ///
    /// Expected:
    /// - Each throws <see cref="FormatException"/>. The conditions include GPUI's <c>c == !d</c> (the sides of a comparison are
    ///   names).
    /// </remarks>
    [Fact]
    public void Parse_NotAContextOrACondition_Throws()
    {
        foreach (string context in new[] { "mode =", "= full", "Editor && Pane", "a, b" })
        {
            _ = Assert.Throws<FormatException>(() => KeyContext.Parse(context));
        }

        foreach (string condition in new[] { string.Empty, "a ==", "c == !d", "!a == b", "(a) == b", "(a", "a b", "a &&", "a > ", "== b", "a | b" })
        {
            _ = Assert.Throws<FormatException>(() => KeyContextPredicate.Parse(condition));
        }
    }

    /// <summary>
    /// A condition is true of a stack of contexts exactly where GPUI's is.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A stack of contexts from the root down, written with <c>|</c> between them; the empty text is the empty stack.
    ///
    /// Steps:
    /// - Parse the condition and evaluate it on the stack.
    ///
    /// Expected:
    /// - The value of the row, which is the assertion of GPUI's test for the same condition and stack.
    /// </remarks>
    /// <param name="condition">The condition.</param>
    /// <param name="stack">The contexts from the root down.</param>
    /// <param name="expected">Whether the condition holds.</param>
    [Theory]
    [InlineData("parent > child", "parent|child", true)]
    [InlineData("parent > child", "grandparent|parent|child", true)]
    [InlineData("parent > child", "other|child", false)]
    [InlineData("parent > child", "parent|other|child", true)]
    [InlineData("parent > child", "", false)]
    [InlineData("parent > child", "child", false)]
    [InlineData("parent > child", "parent", false)]
    [InlineData("child > child", "child", false)]
    [InlineData("child > child", "child|child", true)]
    [InlineData("!editor", "workspace", true)]
    [InlineData("!editor", "editor", false)]
    [InlineData("!editor", "editor|workspace", false)]
    [InlineData("!editor", "workspace|editor", false)]
    [InlineData("!editor && workspace", "workspace", true)]
    [InlineData("!editor && workspace", "editor|workspace", false)]
    [InlineData("!(mode == full)", "mode = full", false)]
    [InlineData("!(mode == full)", "mode = partial", true)]
    [InlineData("!(parent > child)", "parent", true)]
    [InlineData("!(parent > child)", "child", true)]
    [InlineData("!(parent > child)", "parent|child", false)]
    [InlineData("parent > !child", "parent", false)]
    [InlineData("parent > !child", "child", false)]
    [InlineData("parent > !child", "parent|child", false)]
    [InlineData("!!editor", "editor", true)]
    [InlineData("!!editor", "workspace", false)]
    [InlineData("Pane > (Pane > Editor)", "Workspace|Pane|Editor", false)]
    [InlineData("Workspace > Pane > Editor", "Workspace|Pane|Editor", true)]
    [InlineData("(Pane > Pane) > Editor", "Workspace|Pane|Editor", false)]
    [InlineData("Pane > !Workspace", "Pane|Editor", true)]
    [InlineData("Pane > !Workspace", "Pane|Workspace", false)]
    [InlineData("!Workspace", "Workspace", false)]
    [InlineData("!Workspace", "Pane", true)]
    [InlineData("!Workspace", "Editor", true)]
    [InlineData("!Workspace", "Workspace|Pane|Editor", false)]
    public void Evaluate_OnAStack_IsWhatGpuiGives(string condition, string stack, bool expected)
    {
        KeyContext[] contexts = Stack(stack);

        Assert.Equal(expected, KeyContextPredicate.Parse(condition).Evaluate(contexts, contexts));
    }

    /// <summary>
    /// The operators bind in GPUI's order (<c>&gt;</c> loosest, then <c>||</c>, then <c>&amp;&amp;</c>, then the comparisons, then
    /// <c>!</c>), and a comparison with a missing key is unequal.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - One context with the names <c>a</c> and <c>c</c> and the value <c>mode = full</c>; the stack <c>Workspace|Pane</c> for the
    ///   rows with <c>&gt;</c>.
    ///
    /// Steps:
    /// - Evaluate each condition.
    ///
    /// Expected:
    /// - <c>a || b &amp;&amp; d</c> is true (<c>&amp;&amp;</c> first); <c>(a || b) &amp;&amp; d</c> is false; <c>b &amp;&amp; d || c</c> is
    ///   true; <c>mode == full &amp;&amp; a</c> is true; <c>mode != full || b</c> is false; <c>other != x</c> is true and
    ///   <c>other == x</c> false (no such key); <c>!b &amp;&amp; a</c> is true (<c>!</c> takes <c>b</c> alone).
    ///   <c>Workspace &gt; Pane || Other</c> is true and <c>Other || Workspace &gt; Pane</c> is true: each side of <c>&gt;</c> is a
    ///   whole <c>||</c>.
    /// </remarks>
    [Fact]
    public void Parse_Operators_BindInGpuisOrder()
    {
        KeyContext[] one = [KeyContext.Parse("a c mode = full")];
        bool On(string condition) => KeyContextPredicate.Parse(condition).Evaluate(one, one);

        Assert.True(On("a || b && d"));
        Assert.False(On("(a || b) && d"));
        Assert.True(On("b && d || c"));
        Assert.True(On("mode == full && a"));
        Assert.False(On("mode != full || b"));
        Assert.True(On("other != x"));
        Assert.False(On("other == x"));
        Assert.True(On("!b && a"));

        KeyContext[] two = Stack("Workspace|Pane");
        Assert.True(KeyContextPredicate.Parse("Workspace > Pane || Other").Evaluate(two, two));
        Assert.True(KeyContextPredicate.Parse("Other || Workspace > Pane").Evaluate(two, two));
    }

    /// <summary>
    /// The depth of a condition is the length of the longest prefix of the stack it is true of.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The stack <c>Workspace|Pane|Editor</c>.
    ///
    /// Steps:
    /// - Read the depth of each condition.
    ///
    /// Expected:
    /// - <c>Editor</c> 3, <c>Pane</c> 2, <c>Workspace</c> 1, <c>Other</c> 0, <c>Workspace &gt; Editor</c> 3,
    ///   <c>Workspace &gt; Pane</c> 2, and <c>!Other</c> 3.
    /// </remarks>
    /// <param name="condition">The condition.</param>
    /// <param name="depth">The depth.</param>
    [Theory]
    [InlineData("Editor", 3)]
    [InlineData("Pane", 2)]
    [InlineData("Workspace", 1)]
    [InlineData("Other", 0)]
    [InlineData("Workspace > Editor", 3)]
    [InlineData("Workspace > Pane", 2)]
    [InlineData("!Other", 3)]
    public void Depth_IsTheLongestPrefixTheConditionHoldsOf(string condition, int depth)
    {
        Assert.Equal(depth, KeyContextPredicate.Parse(condition).Depth(Stack("Workspace|Pane|Editor")));
    }

    private static KeyContext[] Stack(string text) => text.Length == 0 ? [] : [.. text.Split('|').Select(static part => KeyContext.Parse(part))];
}
