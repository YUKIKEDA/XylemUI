using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace XylemUI.Analyzers.Tests;

// The code fix of the compiler's CS0118 on Text(...) inside the namespace XylemUI or one under it (design §2.2, "Analyzers";
// §2.13, "Calling Text from inside a namespace XylemUI.*").
public sealed class ElementsTextCodeFixProviderTests
{
    /// <summary>
    /// Inside a namespace under <c>XylemUI</c>, a bare <c>Text("a")</c> finds the namespace <c>XylemUI.Text</c> and fails with
    /// CS0118; the fix writes <c>Elements.Text("a")</c>, which compiles.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A file in the namespace <c>XylemUI.Demo</c> with <c>using static XylemUI.Elements;</c> whose method returns
    ///   <c>Text("a")</c>, compiled against <c>XylemUI.Core</c> and <c>XylemUI.Text</c>.
    ///
    /// Steps:
    /// - Apply the one fix the provider offers for the compiler's CS0118 in a workspace.
    ///
    /// Expected:
    /// - One fix offered. The method returns <c>Elements.Text("a")</c>, and the result has no compiler error.
    /// </remarks>
    [Fact]
    public async Task Fix_InAXylemNamespace_CallsElementsText()
    {
        const string Source = """
            using XylemUI;
            using static XylemUI.Elements;

            namespace XylemUI.Demo;

            public static class Code
            {
                public static Element Run() => Text("a");
            }
            """;

        (string source, int offered, ImmutableArray<Diagnostic> errors) = await Harness.FixAsync(new ElementsTextCodeFixProvider(), Source, null);

        Assert.Equal(1, offered);
        Assert.Contains("public static Element Run() => Elements.Text(\"a\");", source, StringComparison.Ordinal);
        Assert.Empty(errors);
    }

    /// <summary>
    /// The fix is offered only where the design says: not for the same CS0118 outside the namespace <c>XylemUI</c>.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A namespace <c>Demo.Text</c> of the app's own, and a file in <c>Demo.Something</c> with
    ///   <c>using static XylemUI.Elements;</c> whose method returns <c>Text("a")</c>: the compiler reports CS0118 for
    ///   <c>Demo.Text</c>.
    ///
    /// Steps:
    /// - Ask the provider for fixes for the CS0118.
    ///
    /// Expected:
    /// - No fix offered.
    /// </remarks>
    [Fact]
    public async Task Fix_OutsideAXylemNamespace_IsNotOffered()
    {
        const string Source = """
            using XylemUI;
            using static XylemUI.Elements;

            namespace Demo.Text
            {
                public static class Marker;
            }

            namespace Demo.Something
            {
                public static class Code
                {
                    public static Element Run() => Text("a");
                }
            }
            """;

        (_, int offered, ImmutableArray<Diagnostic> errors) = await Harness.FixAsync(new ElementsTextCodeFixProvider(), Source, null);

        Assert.Contains(errors, static error => error.Id == "CS0118");
        Assert.Equal(0, offered);
    }
}
