using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace XylemUI.Analyzers.Tests;

// XYLEM120 (design §2.2, "Analyzers"): an effect, a cleanup, or an element made after an await.
public sealed class AfterAwaitAnalyzerTests
{
    /// <summary>
    /// <c>Effect.Create</c>, <c>Scope.OnCleanup</c>, and a function that makes an element, written after the first <c>await</c> of
    /// an async method, an async lambda, an async local function, or an <c>await foreach</c>, are reported at the call.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Cases, each the body of an async method unless it says otherwise: <c>Effect.Create</c> after an await;
    ///   <c>Scope.OnCleanup</c> after one; <c>Elements.Box()</c> after one; the app's own static function that returns an
    ///   <c>Element</c> after one; <c>Box()</c> in the body of an <c>await foreach</c>; a call after an await inside an async
    ///   lambda of a synchronous method; one inside an async local function of a synchronous method.
    ///
    /// Steps:
    /// - Run the analyzer on the case.
    ///
    /// Expected:
    /// - One XYLEM120, at the call written in the case, and its message names the method.
    /// </remarks>
    [Theory]
    [InlineData("async Task M() { await Task.Yield(); _ = Effect.Create(() => { }); }", "Effect.Create(() => { })", "Effect.Create")]
    [InlineData("async Task M() { await Task.Yield(); Scope.OnCleanup(() => { }); }", "Scope.OnCleanup(() => { })", "Scope.OnCleanup")]
    [InlineData("async Task M() { await Task.Yield(); _ = Elements.Box(); }", "Elements.Box()", "Elements.Box")]
    [InlineData("async Task M() { await Task.Yield(); _ = Card(); }", "Card()", "Code.Card")]
    [InlineData("async Task M() { await foreach (int i in Items()) { _ = Box(); } }", "Box()", "Elements.Box")]
    [InlineData("void M() { Func<Task> f = async () => { await Task.Yield(); _ = Box(); }; }", "Box()", "Elements.Box")]
    [InlineData("void M() { async Task Local() { await Task.Yield(); _ = Box(); } _ = Local(); }", "Box()", "Elements.Box")]
    public async Task Call_AfterTheFirstAwait_IsReported(string member, string call, string method)
    {
        ImmutableArray<Diagnostic> diagnostics = await Harness.DiagnoseAsync(new AfterAwaitAnalyzer(), Code(member));

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal("XYLEM120", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal(call, Harness.At(diagnostic));
        Assert.Contains($"'{method}'", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    /// <summary>
    /// What the rule does not look at is not reported: calls before the first await, the operand of the await, calls in a lambda
    /// given to <c>Scope.Run</c> or any other lambda, a method that is not async, extension methods that need a scope, and other
    /// static methods.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Cases: <c>Box()</c> before the await; <c>Box()</c> in the await's operand; <c>Scope.Run</c> with a lambda that makes a box
    ///   after the await; another lambda that makes one after the await; a loop whose <c>Box()</c> is written before the await
    ///   in it; a synchronous method; <c>For</c> on a column made before the await (an extension method); a static method that
    ///   returns no element; an extension method called as a static method (<c>ElementExtensions.Width(box, 1)</c>); a generic
    ///   method whose type argument is <c>Element</c> (<c>Same(box)</c>).
    ///
    /// Steps:
    /// - Run the analyzer on the case.
    ///
    /// Expected:
    /// - No diagnostic.
    /// </remarks>
    [Theory]
    [InlineData("async Task M() { _ = Box(); await Task.Yield(); }")]
    [InlineData("async Task M() { await Wrap(Box()); }")]
    [InlineData("async Task M(ScopeHandle scope) { await Task.Yield(); _ = Scope.Run(scope, () => _ = Box()); }")]
    [InlineData("async Task M() { await Task.Yield(); Func<Element> later = () => Box(); }")]
    [InlineData("async Task M() { for (int i = 0; i < 2; i++) { _ = Box(); await Task.Yield(); } }")]
    [InlineData("void M() { _ = Box(); }")]
    [InlineData(
        "async Task M(IReadOnlySignal<IReadOnlyList<int>> items) { Element column = Column(); await Task.Yield(); _ = column.For(items, i => i, _ => Box()); }"
    )]
    [InlineData("async Task M() { await Task.Yield(); _ = Signal.Create(1); }")]
    [InlineData("async Task M() { Element box = Box(); await Task.Yield(); _ = ElementExtensions.Width(box, 1); }")]
    [InlineData("async Task M() { Element box = Box(); await Task.Yield(); _ = Same(box); }")]
    public async Task Call_TheRuleDoesNotLookAt_IsNotReported(string member)
    {
        Assert.Empty(await Harness.DiagnoseAsync(new AfterAwaitAnalyzer(), Code(member)));
    }

    private static string Code(string member) =>
        $$"""
            using System;
            using System.Collections.Generic;
            using System.Threading.Tasks;
            using XylemUI;
            using static XylemUI.Elements;

            namespace Demo;

            public static class Code
            {
                public static Element Card() => Box();

                private static {{member}}

                private static Task Wrap(Element element) => Task.CompletedTask;

                private static T Same<T>(T value) => value;

                private static async IAsyncEnumerable<int> Items()
                {
                    await Task.Yield();
                    yield return 1;
                }
            }
            """;
}
