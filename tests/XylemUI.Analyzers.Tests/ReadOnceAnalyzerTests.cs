using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace XylemUI.Analyzers.Tests;

// XYLEM110 (design §2.2, "Analyzers"): a signal read once for an argument whose API has a reactive overload, and its code fix.
public sealed class ReadOnceAnalyzerTests
{
    // An API of the app's own with a reactive pair, and a type with a Get that is not a signal's.
    private const string Prelude = """
        using System;
        using XylemUI;
        using static XylemUI.Elements;

        namespace Demo;

        public static class Labels
        {
            public static Element Label(string text) => Text(text);

            public static Element Label(Func<string> text) => Text(text);

            public static Element Count(string text) => Text(text);

            public static Element Count(Func<int> count) => Text(() => count().ToString());
        }

        public sealed class Store
        {
            public int Get() => 1;
        }

        """;

    /// <summary>
    /// A <c>Get()</c> written directly in the argument of each of the six reactive pairs of Core, read through each of the three
    /// signal types, and in the argument of an app's own pair, is reported at the argument.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Cases: <c>Text(count.Get().ToString())</c>; <c>Text</c> of an interpolated string with a read of a computed value;
    ///   <c>Image(source.Get())</c>; <c>Background(new Paint(color.Get()))</c> (the read inside an object creation);
    ///   <c>Foreground(color.Get())</c>; <c>Disabled(flag.Get())</c> through <c>IReadOnlySignal&lt;bool&gt;</c>;
    ///   <c>Accessible(info.Get())</c>; the app's <c>Label(count.Get().ToString())</c>, whose pair takes <c>Func&lt;string&gt;</c>.
    ///
    /// Steps:
    /// - Run the analyzer on a method whose body is the case.
    ///
    /// Expected:
    /// - One XYLEM110, at the argument written in the case, and its message names the method.
    /// </remarks>
    [Theory]
    [InlineData("_ = Text(count.Get().ToString());", "count.Get().ToString()", "Text")]
    [InlineData("_ = Text($\"n {total.Get()}\");", "$\"n {total.Get()}\"", "Text")]
    [InlineData("_ = Image(source.Get());", "source.Get()", "Image")]
    [InlineData("_ = Box().Background(new Paint(color.Get()));", "new Paint(color.Get())", "Background")]
    [InlineData("_ = Box().Foreground(color.Get());", "color.Get()", "Foreground")]
    [InlineData("_ = Box().Disabled(flag.Get());", "flag.Get()", "Disabled")]
    [InlineData("_ = Box().Accessible(info.Get());", "info.Get()", "Accessible")]
    [InlineData("_ = Labels.Label(count.Get().ToString());", "count.Get().ToString()", "Label")]
    public async Task Get_InTheArgumentOfAPairsNonReactiveSide_IsReported(string body, string argument, string method)
    {
        ImmutableArray<Diagnostic> diagnostics = await Harness.DiagnoseAsync(new ReadOnceAnalyzer(), Code(body));

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal("XYLEM110", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal(argument, Harness.At(diagnostic));
        Assert.Contains($"'{method}'", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    /// <summary>
    /// A read the rule does not look at is not reported: the reactive forms, a value kept in a variable, an API without a reactive
    /// overload, and a <c>Get</c> that is not a signal's.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Cases: <c>Text(() =&gt; ...)</c>; <c>Text</c> with a text builder that reads the signal; a string read into a variable and
    ///   then passed; <c>Width(size.Get())</c> and <c>TestId(name.Get())</c>, which have no reactive overload;
    ///   <c>Background(color)</c> with the signal itself; <c>Text</c> of another type's <c>Get()</c>; a read outside any call;
    ///   an app's <c>Count(string)</c> whose other overload takes <c>Func&lt;int&gt;</c>, a function of another type.
    ///
    /// Steps:
    /// - Run the analyzer on a method whose body is the case.
    ///
    /// Expected:
    /// - No diagnostic.
    /// </remarks>
    [Theory]
    [InlineData("_ = Text(() => count.Get().ToString());")]
    [InlineData("_ = Text((ref NodeTextWriter text) => text.Write(count.Get()));")]
    [InlineData("string s = count.Get().ToString(); _ = Text(s);")]
    [InlineData("_ = Box().Width(size.Get());")]
    [InlineData("_ = Box().TestId(name.Get());")]
    [InlineData("_ = Box().Background(paint);")]
    [InlineData("_ = Text(other.Get().ToString());")]
    [InlineData("int n = count.Get();")]
    [InlineData("_ = Labels.Count(name.Get());")]
    public async Task Get_TheRuleDoesNotLookAt_IsNotReported(string body)
    {
        Assert.Empty(await Harness.DiagnoseAsync(new ReadOnceAnalyzer(), Code(body)));
    }

    /// <summary>
    /// The code fix writes the argument of <c>Text(string)</c> as a lambda, and the result compiles and is not reported again.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - <c>_ = Text(count.Get().ToString());</c>.
    ///
    /// Steps:
    /// - Apply the one fix the provider offers for the XYLEM110 in a workspace; run the analyzer on the result.
    ///
    /// Expected:
    /// - One fix offered. The line reads <c>_ = Text(() =&gt; count.Get().ToString());</c>, the result has no compiler error, and the
    ///   analyzer reports nothing on it.
    /// </remarks>
    [Fact]
    public async Task Fix_OfText_PassesTheArgumentAsAFunction()
    {
        (string source, int offered, ImmutableArray<Diagnostic> errors) = await Harness.FixAsync(
            new ReactiveTextCodeFixProvider(),
            Code("_ = Text(count.Get().ToString());"),
            new ReadOnceAnalyzer()
        );

        Assert.Equal(1, offered);
        Assert.Contains("_ = Text(() => count.Get().ToString());", source, StringComparison.Ordinal);
        Assert.Empty(errors);
        Assert.Empty(await Harness.DiagnoseAsync(new ReadOnceAnalyzer(), source));
    }

    /// <summary>
    /// The code fix is offered for <c>Text(string)</c> alone: an XYLEM110 on another pair gets no fix.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - <c>_ = Box().Foreground(color.Get());</c>, whose reactive overload takes <c>IReadOnlySignal&lt;Color&gt;</c>, to which a
    ///   lambda does not convert.
    ///
    /// Steps:
    /// - Ask the provider for fixes for the XYLEM110.
    ///
    /// Expected:
    /// - No fix offered.
    /// </remarks>
    [Fact]
    public async Task Fix_OfAnotherPair_IsNotOffered()
    {
        (_, int offered, _) = await Harness.FixAsync(
            new ReactiveTextCodeFixProvider(),
            Code("_ = Box().Foreground(color.Get());"),
            new ReadOnceAnalyzer()
        );

        Assert.Equal(0, offered);
    }

    private static string Code(string body) =>
        Prelude
        + $$"""
            public static class Code
            {
                public static void Run(
                    Signal<int> count,
                    Computed<int> total,
                    Signal<ImageSource> source,
                    Signal<Color> color,
                    IReadOnlySignal<bool> flag,
                    Signal<AccessibilityInfo> info,
                    Signal<float> size,
                    Signal<string> name,
                    IReadOnlySignal<Paint> paint,
                    Store other
                )
                {
                    {{body}}
                }
            }
            """;
}
