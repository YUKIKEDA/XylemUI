using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace XylemUI.Analyzers.Tests;

// XYLEM111 (design §2.2, "Analyzers"): an async lambda given to Effect.Create or Computed.Create.
public sealed class AsyncTrackedAnalyzerTests
{
    /// <summary>
    /// An async lambda or async anonymous method written as the argument of <c>Effect.Create</c> or <c>Computed.Create</c> is
    /// reported at its <c>async</c>.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Cases: <c>Effect.Create(async () =&gt; ...)</c> that awaits and then reads a signal; <c>Effect.Create</c> with an async
    ///   anonymous method; <c>Computed.Create(async () =&gt; ...)</c>, whose value is a <c>Task&lt;int&gt;</c>.
    ///
    /// Steps:
    /// - Run the analyzer on a method whose body is the case.
    ///
    /// Expected:
    /// - One XYLEM111, at the <c>async</c> keyword, and its message names the method.
    /// </remarks>
    [Theory]
    [InlineData("_ = Effect.Create(async () => { await Task.Yield(); _ = count.Get(); });", "Effect.Create")]
    [InlineData("_ = Effect.Create(async delegate { await Task.Yield(); _ = count.Get(); });", "Effect.Create")]
    [InlineData("_ = Computed.Create(async () => { await Task.Yield(); return count.Get(); });", "Computed.Create")]
    public async Task AsyncLambda_GivenToCreate_IsReported(string body, string method)
    {
        ImmutableArray<Diagnostic> diagnostics = await Harness.DiagnoseAsync(new AsyncTrackedAnalyzer(), Code(body));

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal("XYLEM111", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal("async", Harness.At(diagnostic));
        Assert.Contains($"'{method}'", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    /// <summary>
    /// What the rule does not look at is not reported: an async method given as a method group, a synchronous lambda that calls an
    /// async method, a synchronous lambda, and an async lambda given to another method.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Cases: <c>Effect.Create(RunAsync)</c> with an async void method; <c>Effect.Create(() =&gt; _ = LoadAsync())</c>;
    ///   <c>Computed.Create(() =&gt; count.Get())</c>; <c>Task.Run(async () =&gt; ...)</c>.
    ///
    /// Steps:
    /// - Run the analyzer on a method whose body is the case.
    ///
    /// Expected:
    /// - No diagnostic.
    /// </remarks>
    [Theory]
    [InlineData("_ = Effect.Create(RunAsync);")]
    [InlineData("_ = Effect.Create(() => _ = LoadAsync());")]
    [InlineData("_ = Computed.Create(() => count.Get());")]
    [InlineData("_ = Task.Run(async () => await LoadAsync());")]
    public async Task Function_TheRuleDoesNotLookAt_IsNotReported(string body)
    {
        Assert.Empty(await Harness.DiagnoseAsync(new AsyncTrackedAnalyzer(), Code(body)));
    }

    private static string Code(string body) =>
        $$"""
            using System;
            using System.Threading.Tasks;
            using XylemUI;

            namespace Demo;

            public static class Code
            {
                public static void Run(Signal<int> count)
                {
                    {{body}}
                }

                private static async void RunAsync() => await Task.Yield();

                private static Task LoadAsync() => Task.CompletedTask;
            }
            """;
}
