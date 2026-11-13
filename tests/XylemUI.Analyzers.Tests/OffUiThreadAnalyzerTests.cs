using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace XylemUI.Analyzers.Tests;

// XYLEM100 (design §2.2, "Analyzers"): a UI-thread-only API called in a lambda that Task.Run, the thread pool, or a new thread runs.
public sealed class OffUiThreadAnalyzerTests
{
    // A type of the app's with the attribute on a constructor, one accessor, a virtual method, and an interface's method.
    private const string Prelude = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using XylemUI;

        namespace Demo;

        public interface IWorker
        {
            [UiThreadOnly]
            void Work();
        }

        public class Widget : IWorker
        {
            [UiThreadOnly]
            public Widget() { }

            public Widget(int size) => Size = size;

            public int Size { get; [UiThreadOnly] set; }

            public int Count { [UiThreadOnly] get; set; }

            [UiThreadOnly]
            public virtual void Paint() { }

            public void Work() { }
        }

        public sealed class Derived : Widget
        {
            public override void Paint() { }
        }

        """;

    /// <summary>
    /// A call of a member that carries <c>[UiThreadOnly]</c>, in the body of a lambda written as the argument of each of the four
    /// ways to start work on another thread, is reported at the call.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Cases: <c>Task.Run</c> with a lambda that sets a signal; <c>Task.Run</c> with an anonymous method; <c>ThreadPool.QueueUserWorkItem</c>;
    ///   <c>ThreadPool.UnsafeQueueUserWorkItem</c>; <c>new Thread</c>; a read of <c>IReadOnlySignal&lt;T&gt;.Get</c> (the attribute on
    ///   the interface's member); <c>Computed&lt;T&gt;.Get</c> (a generic type's member); the setter of <c>ImageCache.MaximumCount</c>
    ///   (the attribute on the property); a constructor of the app's own; the setter of a property whose setter alone carries it; the getter of one whose getter
    ///   alone carries it;
    ///   an interface method of the app's own; <c>Scope.Create</c> in a block body, whose own lambda argument is not looked into.
    ///
    /// Steps:
    /// - Run the analyzer on a method whose body is the case.
    ///
    /// Expected:
    /// - One XYLEM100, at the call written in the case.
    /// </remarks>
    [Theory]
    [InlineData("Task.Run(() => signal.Set(1));", "signal.Set(1)")]
    [InlineData("Task.Run(delegate { signal.Set(1); });", "signal.Set(1)")]
    [InlineData("ThreadPool.QueueUserWorkItem(_ => signal.Set(1));", "signal.Set(1)")]
    [InlineData("ThreadPool.UnsafeQueueUserWorkItem(_ => signal.Set(1), null);", "signal.Set(1)")]
    [InlineData("new Thread(() => signal.Set(1)).Start();", "signal.Set(1)")]
    [InlineData("Task.Run(() => read.Get());", "read.Get()")]
    [InlineData("Task.Run(() => computed.Get());", "computed.Get()")]
    [InlineData("Task.Run(() => ImageCache.MaximumCount = 5);", "ImageCache.MaximumCount")]
    [InlineData("Task.Run(() => new Widget());", "new Widget()")]
    [InlineData("Task.Run(() => widget.Size = 3);", "widget.Size")]
    [InlineData("Task.Run(() => widget.Count);", "widget.Count")]
    [InlineData("Task.Run(() => worker.Work());", "worker.Work()")]
    [InlineData("Task.Run(() => { _ = Scope.Create(() => { }); });", "Scope.Create(() => { })")]
    public async Task Call_OfAUiThreadOnlyMemberInALambdaOnAnotherThread_IsReported(string body, string call)
    {
        ImmutableArray<Diagnostic> diagnostics = await Harness.DiagnoseAsync(new OffUiThreadAnalyzer(), Code(body));

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal("XYLEM100", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal(call, Harness.At(diagnostic));
    }

    /// <summary>
    /// What the rule does not look at is not reported: the forms the design lists as not seen, members without the attribute, and
    /// the members the attribute is not inherited by.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Cases: a method group (<c>Task.Run(Work)</c>); a delegate kept in a variable; a lambda in parentheses; a lambda inside
    ///   the body; a local function inside the body; <c>Task.Factory.StartNew</c>; <c>ContinueWith</c>; a read of the property
    ///   whose setter alone carries the attribute; the setter of a property whose getter alone carries it; a constructor without it; an override of a method that carries it; a call of
    ///   the class's method that implements an interface method that carries it; a signal set outside any lambda.
    ///
    /// Steps:
    /// - Run the analyzer on a method whose body is the case.
    ///
    /// Expected:
    /// - No diagnostic.
    /// </remarks>
    [Theory]
    [InlineData("Task.Run(Work);")]
    [InlineData("Action work = () => signal.Set(1); Task.Run(work);")]
    [InlineData("Task.Run((() => signal.Set(1)));")]
    [InlineData("Task.Run(() => { Action later = () => signal.Set(1); });")]
    [InlineData("Task.Run(() => { void Later() => signal.Set(1); });")]
    [InlineData("Task.Factory.StartNew(() => signal.Set(1));")]
    [InlineData("Task.CompletedTask.ContinueWith(_ => signal.Set(1));")]
    [InlineData("Task.Run(() => widget.Size);")]
    [InlineData("Task.Run(() => widget.Count = 3);")]
    [InlineData("Task.Run(() => new Widget(3));")]
    [InlineData("Task.Run(() => derived.Paint());")]
    [InlineData("Task.Run(() => widget.Work());")]
    [InlineData("signal.Set(1);")]
    public async Task Call_TheRuleDoesNotLookAt_IsNotReported(string body)
    {
        Assert.Empty(await Harness.DiagnoseAsync(new OffUiThreadAnalyzer(), Code(body)));
    }

    /// <summary>
    /// The same member called twice in one body is reported at each call, and a call in a lambda inside the body is not.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A lambda given to <c>Task.Run</c> that sets a signal, makes a lambda that sets it, and sets it again.
    ///
    /// Steps:
    /// - Run the analyzer.
    ///
    /// Expected:
    /// - Two XYLEM100, at <c>signal.Set(1)</c> and <c>signal.Set(3)</c>; none at <c>signal.Set(2)</c>.
    /// </remarks>
    [Fact]
    public async Task Body_WithTwoCallsAndANestedLambda_ReportsTheBodysOwnCalls()
    {
        ImmutableArray<Diagnostic> diagnostics = await Harness.DiagnoseAsync(
            new OffUiThreadAnalyzer(),
            Code("Task.Run(() => { signal.Set(1); Action back = () => signal.Set(2); signal.Set(3); });")
        );

        Assert.Equal(["signal.Set(1)", "signal.Set(3)"], diagnostics.Select(Harness.At));
    }

    private static string Code(string body) =>
        Prelude
        + $$"""
            public static class Code
            {
                public static void Run(Signal<int> signal, IReadOnlySignal<int> read, Computed<int> computed, Widget widget, Derived derived, IWorker worker)
                {
                    {{body}}
                }

                private static void Work() { }
            }
            """;
}
