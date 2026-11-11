using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace XylemUI.Generators.Tests;

// The generator of the action table (design §7, "Shortcuts and actions", row KM1a), run through the compiler's API on sources
// written here. What the generated table does in a running app is checked where an app uses it (XylemUI.Testing.Tests).
public sealed class ActionTableGeneratorTests
{
    private const string TwoActions = """
        using XylemUI;

        namespace Demo;

        [Action("demo::Open")]
        public sealed class Open : IAction;

        [Action("demo::Close")]
        internal sealed class Close : IAction
        {
            internal Close() { }
        }
        """;

    // One language version for the sources here and the generated ones: a compilation refuses trees of two.
    private static readonly CSharpParseOptions Options = new(LanguageVersion.Preview);

    /// <summary>
    /// A library's table is a public class with a <c>Register</c> method that registers each action under its name, an assembly
    /// attribute that names the table and its names, and no module initializer.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A library <c>Demo.Lib</c> with a public action <c>demo::Open</c> and an internal one <c>demo::Close</c> whose constructor
    ///   is internal.
    ///
    /// Steps:
    /// - Run the generator; compile the result.
    ///
    /// Expected:
    /// - No diagnostic from the generator and none from the compiler. The generated source declares
    ///   <c>public static class Demo_LibActions</c>, registers both names with <c>new global::Demo.Open()</c> and
    ///   <c>new global::Demo.Close()</c>, carries the assembly attribute with the two names, and has no <c>ModuleInitializer</c>.
    /// </remarks>
    [Fact]
    public void Library_WithActions_GetsAPublicTableAndNoInitializer()
    {
        Result result = Run("Demo.Lib", OutputKind.DynamicallyLinkedLibrary, TwoActions);

        Assert.Empty(result.GeneratorDiagnostics);
        Assert.Empty(result.CompilerErrors);
        string source = Assert.Single(result.Sources);
        Assert.Contains("public static class Demo_LibActions", source, StringComparison.Ordinal);
        Assert.Contains("Register<global::Demo.Open>(\"demo::Open\", static () => new global::Demo.Open());", source, StringComparison.Ordinal);
        Assert.Contains("Register<global::Demo.Close>(\"demo::Close\", static () => new global::Demo.Close());", source, StringComparison.Ordinal);
        Assert.Contains(
            "[assembly: global::XylemUI.ActionTableAttribute(typeof(global::XylemUI.Generated.Demo_LibActions), new string[] { \"demo::Close\", \"demo::Open\" })]",
            source,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("ModuleInitializer", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// An app's table has a module initializer that calls the table of each library it references and then its own, and an app
    /// with no action of its own still calls the libraries'.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The library of the first test, compiled with its generated table. An app that references it with one action
    ///   <c>app::Quit</c>; another app that references it with no action.
    ///
    /// Steps:
    /// - Run the generator on each app; compile the results.
    ///
    /// Expected:
    /// - No diagnostic. The first app's initializer calls <c>global::XylemUI.Generated.Demo_LibActions.Register();</c> and then
    ///   <c>Register();</c>. The second app's table is internal, has no <c>Register</c> method of its own, and its initializer
    ///   calls the library's.
    /// </remarks>
    [Fact]
    public void App_ReferencingALibrary_InitializerCallsEveryTable()
    {
        MetadataReference library = Run("Demo.Lib", OutputKind.DynamicallyLinkedLibrary, TwoActions).Emit();
        const string Quit = """
            using XylemUI;

            [Action("app::Quit")]
            public sealed class Quit : IAction;

            public static class Program
            {
                public static void Main() { }
            }
            """;
        const string Empty = """
            public static class Program
            {
                public static void Main() { }
            }
            """;

        Result withAction = Run("Demo.App", OutputKind.ConsoleApplication, Quit, library);
        Assert.Empty(withAction.GeneratorDiagnostics);
        Assert.Empty(withAction.CompilerErrors);
        string source = Assert.Single(withAction.Sources);
        int library1 = source.IndexOf("global::XylemUI.Generated.Demo_LibActions.Register();", StringComparison.Ordinal);
        int own = source.IndexOf("            Register();", StringComparison.Ordinal);
        Assert.True(library1 > 0 && own > library1, source);
        Assert.Contains("[global::System.Runtime.CompilerServices.ModuleInitializer]", source, StringComparison.Ordinal);

        Result withNone = Run("Demo.Bare", OutputKind.ConsoleApplication, Empty, library);
        Assert.Empty(withNone.GeneratorDiagnostics);
        Assert.Empty(withNone.CompilerErrors);
        string bare = Assert.Single(withNone.Sources);
        Assert.Contains("internal static class Demo_BareActions", bare, StringComparison.Ordinal);
        Assert.Contains("global::XylemUI.Generated.Demo_LibActions.Register();", bare, StringComparison.Ordinal);
        Assert.DoesNotContain("public static void Register()", bare, StringComparison.Ordinal);
    }

    /// <summary>
    /// An assembly with no action and no table to call gets no generated source.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A library with one class that carries no attribute; an app with a <c>Main</c> and no reference to a library with a table.
    ///
    /// Steps:
    /// - Run the generator on each.
    ///
    /// Expected:
    /// - No source and no diagnostic for either.
    /// </remarks>
    [Fact]
    public void Assembly_WithNothingToRegister_GetsNoSource()
    {
        Result library = Run("Demo.Lib", OutputKind.DynamicallyLinkedLibrary, "public sealed class Plain;");
        Assert.Empty(library.Sources);
        Assert.Empty(library.GeneratorDiagnostics);

        Result app = Run("Demo.App", OutputKind.ConsoleApplication, "public static class Program { public static void Main() { } }");
        Assert.Empty(app.Sources);
        Assert.Empty(app.GeneratorDiagnostics);
    }

    /// <summary>
    /// Each mistake in an action's declaration is an error at the type, and the type is left out of the table.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A library with one valid action and one invalid one, for each row: a class that does not implement <c>IAction</c>
    ///   (XYLEM001); one whose only constructor takes a parameter (XYLEM002); one whose constructor without parameters is private
    ///   (XYLEM002); one with the name of the valid action (XYLEM003); one with an empty name (XYLEM004); a private nested class
    ///   (XYLEM005); an abstract class (XYLEM005); a generic class (XYLEM005).
    ///
    /// Steps:
    /// - Run the generator on each library.
    ///
    /// Expected:
    /// - One diagnostic with the row's id and severity Error, located in the source; the generated table registers
    ///   <c>demo::Good</c> and does not mention the invalid type.
    /// </remarks>
    /// <param name="declaration">The invalid declaration.</param>
    /// <param name="id">The diagnostic's id.</param>
    [Theory]
    [InlineData("[Action(\"demo::Zap\")] public sealed class Zap;", "XYLEM001")]
    [InlineData("[Action(\"demo::Zap\")] public sealed class Zap : IAction { public Zap(int amount) { } }", "XYLEM002")]
    [InlineData("[Action(\"demo::Zap\")] public sealed class Zap : IAction { private Zap() { } }", "XYLEM002")]
    [InlineData("[Action(\"demo::Good\")] public sealed class Zap : IAction;", "XYLEM003")]
    [InlineData("[Action(\"\")] public sealed class Zap : IAction;", "XYLEM004")]
    [InlineData("public static class Outer { [Action(\"demo::Zap\")] private sealed class Zap : IAction; }", "XYLEM005")]
    [InlineData("[Action(\"demo::Zap\")] public abstract class Zap : IAction;", "XYLEM005")]
    [InlineData("[Action(\"demo::Zap\")] public sealed class Zap<T> : IAction;", "XYLEM005")]
    public void InvalidAction_IsAnErrorAndIsLeftOut(string declaration, string id)
    {
        string source = "using XylemUI;\n[Action(\"demo::Good\")] public sealed class Good : IAction;\n" + declaration;

        Result result = Run("Demo.Lib", OutputKind.DynamicallyLinkedLibrary, source);

        Diagnostic diagnostic = Assert.Single(result.GeneratorDiagnostics);
        Assert.Equal(id, diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.True(diagnostic.Location.IsInSource || diagnostic.Location.Kind == LocationKind.ExternalFile);
        string table = Assert.Single(result.Sources);
        Assert.Contains("\"demo::Good\", static () => new global::Good()", table, StringComparison.Ordinal);
        Assert.DoesNotContain("Zap", table, StringComparison.Ordinal);
    }

    /// <summary>
    /// A name that a referenced library's table already has is an error in the assembly that gives it again.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The library of the first test (<c>demo::Open</c>, <c>demo::Close</c>), compiled. An app that references it and declares an
    ///   action named <c>demo::Open</c>.
    ///
    /// Steps:
    /// - Run the generator on the app.
    ///
    /// Expected:
    /// - One XYLEM003 whose message names <c>demo::Open</c> and the library <c>Demo.Lib</c>; the app's table does not register the
    ///   name.
    /// </remarks>
    [Fact]
    public void Name_OfAReferencedLibrarysAction_IsADuplicate()
    {
        MetadataReference library = Run("Demo.Lib", OutputKind.DynamicallyLinkedLibrary, TwoActions).Emit();
        const string App = """
            using XylemUI;

            [Action("demo::Open")]
            public sealed class OpenAgain : IAction;

            public static class Program
            {
                public static void Main() { }
            }
            """;

        Result result = Run("Demo.App", OutputKind.ConsoleApplication, App, library);

        Diagnostic diagnostic = Assert.Single(result.GeneratorDiagnostics);
        Assert.Equal("XYLEM003", diagnostic.Id);
        string message = diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture);
        Assert.Contains("demo::Open", message, StringComparison.Ordinal);
        Assert.Contains("Demo.Lib", message, StringComparison.Ordinal);
        Assert.DoesNotContain("OpenAgain", Assert.Single(result.Sources), StringComparison.Ordinal);
    }

    /// <summary>
    /// A record with <c>[Action]</c> is in the table as a class is, and a record whose only constructor takes parameters is the
    /// error of a class without a constructor without parameters.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A library with <c>[Action("demo::Jump")] public sealed record Jump : IAction { public int Line { get; init; } }</c>; another
    ///   with <c>[Action("demo::Go")] public sealed record Go(int Line) : IAction;</c>.
    ///
    /// Steps:
    /// - Run the generator on each; compile the first.
    ///
    /// Expected:
    /// - The first: no diagnostic, no compiler error, and the table registers <c>demo::Jump</c> with <c>new global::Jump()</c> and
    ///   the setter of its init-only property. The
    ///   second: one XYLEM002 at the record, and no generated source.
    /// </remarks>
    [Fact]
    public void Record_WithTheAttribute_IsRegisteredOrRefusedAsAClassIs()
    {
        Result jump = Run(
            "Demo.Lib",
            OutputKind.DynamicallyLinkedLibrary,
            "using XylemUI;\n[Action(\"demo::Jump\")] public sealed record Jump : IAction { public int Line { get; init; } }"
        );
        Assert.Empty(jump.GeneratorDiagnostics);
        Assert.Empty(jump.CompilerErrors);
        Assert.Contains(
            "Register<global::Jump>(\"demo::Jump\", static () => new global::Jump(), Set0);",
            Assert.Single(jump.Sources),
            StringComparison.Ordinal
        );

        Result go = Run(
            "Demo.Lib",
            OutputKind.DynamicallyLinkedLibrary,
            "using XylemUI;\n[Action(\"demo::Go\")] public sealed record Go(int Line) : IAction;"
        );
        Assert.Equal("XYLEM002", Assert.Single(go.GeneratorDiagnostics).Id);
        Assert.Empty(go.Sources);
    }

    /// <summary>
    /// A name that two referenced libraries give is an error in the assembly that references both, though neither action is its
    /// own.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Two libraries, <c>Demo.First</c> and <c>Demo.Second</c>, each with an action named <c>shared::Open</c>, compiled. An app
    ///   with no action that references both, and an app that references only the first.
    ///
    /// Steps:
    /// - Run the generator on each app.
    ///
    /// Expected:
    /// - The app with both: one XYLEM003, with no source location, whose message names <c>shared::Open</c>, <c>Demo.First</c>, and
    ///   <c>Demo.Second</c>. The app with one: no diagnostic.
    /// </remarks>
    [Fact]
    public void Name_OfTwoReferencedLibraries_IsADuplicate()
    {
        const string Library = "using XylemUI;\n[Action(\"shared::Open\")] public sealed class Open : IAction;";
        const string App = "public static class Program { public static void Main() { } }";
        MetadataReference first = Run("Demo.First", OutputKind.DynamicallyLinkedLibrary, Library).Emit();
        MetadataReference second = Run("Demo.Second", OutputKind.DynamicallyLinkedLibrary, Library).Emit();

        Result both = Run("Demo.App", OutputKind.ConsoleApplication, App, first, second);
        Diagnostic diagnostic = Assert.Single(both.GeneratorDiagnostics);
        Assert.Equal("XYLEM003", diagnostic.Id);
        Assert.Equal(LocationKind.None, diagnostic.Location.Kind);
        string message = diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture);
        Assert.Contains("shared::Open", message, StringComparison.Ordinal);
        Assert.Contains("Demo.First", message, StringComparison.Ordinal);
        Assert.Contains("Demo.Second", message, StringComparison.Ordinal);

        Assert.Empty(Run("Demo.One", OutputKind.ConsoleApplication, App, first).GeneratorDiagnostics);
    }

    /// <summary>
    /// An action whose properties can be set gets a method that sets them from text: a record's init-only properties with a
    /// <c>with</c> expression, a class's settable ones by assignment; an action with none gets no such method.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A library with a record <c>Jump</c> (init-only <c>Line</c>, an int, and <c>Mode</c>, an enum of <c>Fast</c> and
    ///   <c>Slow</c>), a class <c>Scroll</c> (settable <c>Lines</c>, a double; a read-only <c>Kind</c>; a private-set
    ///   <c>Hidden</c>), and a class <c>Plain</c> with no property.
    ///
    /// Steps:
    /// - Run the generator; compile the result.
    ///
    /// Expected:
    /// - No diagnostic and no compiler error. <c>Jump</c> and <c>Scroll</c> are registered with a setter and <c>Plain</c>
    ///   without. The source has <c>action = action with { Line = parsed };</c>, <c>parsed = global::Mode.Fast;</c>,
    ///   <c>action.Lines = parsed;</c>, and reads the numbers with the invariant culture; it mentions neither <c>Kind</c> nor
    ///   <c>Hidden</c>.
    /// </remarks>
    [Fact]
    public void Action_WithPropertiesThatCanBeSet_GetsASetterFromText()
    {
        const string Source = """
            using XylemUI;

            public enum Mode { Fast, Slow }

            [Action("demo::Jump")]
            public sealed record Jump : IAction
            {
                public int Line { get; init; }
                public Mode Mode { get; init; }
            }

            [Action("demo::Scroll")]
            public sealed class Scroll : IAction
            {
                public double Lines { get; set; }
                public string Kind => "scroll";
                public int Hidden { get; private set; }
            }

            [Action("demo::Plain")]
            public sealed class Plain : IAction;
            """;

        Result result = Run("Demo.Lib", OutputKind.DynamicallyLinkedLibrary, Source);

        Assert.Empty(result.GeneratorDiagnostics);
        Assert.Empty(result.CompilerErrors);
        string table = Assert.Single(result.Sources);
        Assert.Contains("Register<global::Jump>(\"demo::Jump\", static () => new global::Jump(), Set0);", table, StringComparison.Ordinal);
        Assert.Contains("Register<global::Plain>(\"demo::Plain\", static () => new global::Plain());", table, StringComparison.Ordinal);
        Assert.Contains("Register<global::Scroll>(\"demo::Scroll\", static () => new global::Scroll(), Set2);", table, StringComparison.Ordinal);
        Assert.Contains("action = action with { Line = parsed };", table, StringComparison.Ordinal);
        Assert.Contains("parsed = global::Mode.Fast;", table, StringComparison.Ordinal);
        Assert.Contains("action.Lines = parsed;", table, StringComparison.Ordinal);
        Assert.Contains("global::System.Globalization.CultureInfo.InvariantCulture", table, StringComparison.Ordinal);
        Assert.DoesNotContain("Kind", table, StringComparison.Ordinal);
        Assert.DoesNotContain("Hidden", table, StringComparison.Ordinal);
    }

    /// <summary>
    /// A property that can be set and that an argument cannot give is a warning at the action, and the action is registered
    /// without it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A library with one action, for each row: a class with a settable property of a type no argument is written in
    ///   (<c>System.DateTime</c>); a class with an init-only int (only a record's can be set with <c>with</c>).
    ///
    /// Steps:
    /// - Run the generator on each.
    ///
    /// Expected:
    /// - One XYLEM006 of severity Warning that names the property; the action is registered with no setter.
    /// </remarks>
    /// <param name="property">The property's declaration.</param>
    [Theory]
    [InlineData("public System.DateTime When { get; set; }")]
    [InlineData("public int When { get; init; }")]
    public void Property_AnArgumentCannotGive_IsAWarning(string property)
    {
        string source = "using XylemUI;\n[Action(\"demo::Zap\")] public sealed class Zap : IAction { " + property + " }";

        Result result = Run("Demo.Lib", OutputKind.DynamicallyLinkedLibrary, source);

        Diagnostic diagnostic = Assert.Single(result.GeneratorDiagnostics);
        Assert.Equal("XYLEM006", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("Zap.When", diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
        Assert.Contains(
            "Register<global::Zap>(\"demo::Zap\", static () => new global::Zap());",
            Assert.Single(result.Sources),
            StringComparison.Ordinal
        );
    }

    /// <summary>
    /// A second run after an edit that does not touch an action gives the same table from the cache.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The library of the first test, with the generator's steps tracked.
    ///
    /// Steps:
    /// - Run the generator; add a syntax tree with an unrelated class; run it again.
    ///
    /// Expected:
    /// - The second run's output step for the table is <c>Cached</c> or <c>Unchanged</c>, not <c>Modified</c> or <c>New</c>.
    /// </remarks>
    [Fact]
    public void UnrelatedEdit_DoesNotWriteTheTableAgain()
    {
        CSharpCompilation compilation = Compile("Demo.Lib", OutputKind.DynamicallyLinkedLibrary, TwoActions);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new ActionTableGenerator().AsSourceGenerator()],
            parseOptions: Options,
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true)
        );
        driver = driver.RunGenerators(compilation, TestContext.Current.CancellationToken);
        CSharpCompilation edited = compilation.AddSyntaxTrees(
            CSharpSyntaxTree.ParseText("public sealed class Unrelated;", Options, cancellationToken: TestContext.Current.CancellationToken)
        );

        driver = driver.RunGenerators(edited, TestContext.Current.CancellationToken);

        GeneratorRunResult run = Assert.Single(driver.GetRunResult().Results);
        IncrementalStepRunReason[] reasons =
        [
            .. run.TrackedOutputSteps.SelectMany(static step => step.Value).SelectMany(static step => step.Outputs).Select(static o => o.Reason),
        ];
        Assert.NotEmpty(reasons);
        Assert.All(
            reasons,
            static reason => Assert.True(reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged, reason.ToString())
        );
    }

    private static Result Run(string assemblyName, OutputKind kind, string source, params MetadataReference[] references)
    {
        CSharpCompilation compilation = Compile(assemblyName, kind, source, references);
        GeneratorDriver driver = CSharpGeneratorDriver.Create([new ActionTableGenerator().AsSourceGenerator()], parseOptions: Options);
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out Compilation updated,
            out ImmutableArray<Diagnostic> diagnostics,
            TestContext.Current.CancellationToken
        );
        GeneratorDriverRunResult run = driver.GetRunResult();
        return new Result(
            updated,
            diagnostics,
            [.. run.GeneratedTrees.Select(static tree => tree.GetText().ToString())],
            [.. updated.GetDiagnostics(TestContext.Current.CancellationToken).Where(static d => d.Severity == DiagnosticSeverity.Error)]
        );
    }

    private static CSharpCompilation Compile(string assemblyName, OutputKind kind, string source, params MetadataReference[] references)
    {
        // The reference assemblies of the running test: the framework's, and XylemUI.Core with the attribute and the registry.
        string trusted = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!;
        IEnumerable<MetadataReference> platform = trusted
            .Split(Path.PathSeparator)
            .Where(static path =>
                Path.GetFileName(path) is "System.Runtime.dll" or "System.Private.CoreLib.dll" or "netstandard.dll" or "System.Collections.dll"
            )
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path));
        MetadataReference core = MetadataReference.CreateFromFile(typeof(IAction).Assembly.Location);
        return CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source, Options)],
            [.. platform, core, .. references],
            new CSharpCompilationOptions(kind, nullableContextOptions: NullableContextOptions.Enable)
        );
    }

    private sealed record Result(
        Compilation Compilation,
        ImmutableArray<Diagnostic> GeneratorDiagnostics,
        ImmutableArray<string> Sources,
        ImmutableArray<Diagnostic> CompilerErrors
    )
    {
        // The compiled assembly as a reference, with the generated table and its assembly attribute in it.
        public PortableExecutableReference Emit()
        {
            using var stream = new MemoryStream();
            Microsoft.CodeAnalysis.Emit.EmitResult emitted = Compilation.Emit(stream);
            Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
            return MetadataReference.CreateFromImage(stream.ToArray());
        }
    }
}
