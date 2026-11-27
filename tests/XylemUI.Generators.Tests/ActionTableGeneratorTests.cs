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

    // A stand-in for XylemUI.Theme.Fluent, whose FluentRoot comes with row TH1's fourth PR: a library with a root wrapper and no
    // [assembly: WindowTheme] of its own.
    private const string ThemeLibrary = """
        using System;
        using XylemUI;

        namespace XylemUI.Theme.Fluent;

        public static class FluentRoot
        {
            public static Element Wrap(Func<Element> root) => root();
        }
        """;

    private const string AppMain = "public static class Program { public static void Main() { } }";

    private const string FluentTheme = "[assembly: XylemUI.WindowTheme(typeof(XylemUI.Theme.Fluent.FluentRoot))]\n";

    // A stand-in for XylemUI.Platform.Windows: a host that names its class with [assembly: PlatformHost]. The attribute is internal to
    // XylemUI.Core, so a host here is compiled under a name Core shows its internals to, as the Windows host is.
    private const string WindowsHost = """
        [assembly: XylemUI.PlatformHost(typeof(XylemUI.Platform.Windows.Win32Host))]

        namespace XylemUI.Platform.Windows;

        public static class Win32Host
        {
            public static void Register() { }
        }
        """;

    // What the module initializer calls for the host above.
    private const string RegisterWindowsHost = "            global::XylemUI.Platform.Windows.Win32Host.Register();\n";

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

        // An action with no public property is compared by its type alone: the table gives it no comparison.
        Assert.DoesNotContain("ActionRegistry.Compare", source, StringComparison.Ordinal);
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
    /// A library with no action gets no generated source. An app with none of its own still gets the module initializer, for the
    /// actions and the default key bindings of <c>XylemUI.Core</c>, which every app references.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A library with one class that carries no attribute; an app with a <c>Main</c> that references <c>XylemUI.Core</c> alone.
    ///
    /// Steps:
    /// - Run the generator on each.
    ///
    /// Expected:
    /// - No source and no diagnostic for the library. One source for the app, which compiles: its module initializer calls the
    ///   table of <c>XylemUI.Core</c> and then registers <c>CoreKeyBindings.AddDefaults</c>.
    /// </remarks>
    [Fact]
    public void Assembly_WithNothingToRegister_GetsNoSource()
    {
        Result library = Run("Demo.Lib", OutputKind.DynamicallyLinkedLibrary, "public sealed class Plain;");
        Assert.Empty(library.Sources);
        Assert.Empty(library.GeneratorDiagnostics);

        Result app = Run("Demo.App", OutputKind.ConsoleApplication, "public static class Program { public static void Main() { } }");
        Assert.Empty(app.GeneratorDiagnostics);
        Assert.Empty(app.CompilerErrors);
        string source = Assert.Single(app.Sources);
        int table = source.IndexOf("global::XylemUI.Generated.XylemUI_CoreActions.Register();", StringComparison.Ordinal);
        int defaults = source.IndexOf(
            "global::XylemUI.Keymap.RegisterDefaults(global::XylemUI.CoreKeyBindings.AddDefaults);",
            StringComparison.Ordinal
        );
        Assert.True(table > 0 && defaults > table, source);
    }

    /// <summary>
    /// An app's module initializer registers the default key bindings of the assemblies it references, an assembly after the
    /// assemblies it references, and its own last (design §7, "The default keys and actions"); a library gets no such code.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A library <c>Demo.Zeta</c> that names a class with <c>AddDefaults</c>; a library <c>Demo.Alpha</c> that references it and
    ///   names its own; a library <c>Demo.Beta</c> that references neither and names its own; an app that references the three,
    ///   in the order Alpha, Beta, Zeta, and names an internal class of its own.
    ///
    /// Steps:
    /// - Run the generator on <c>Demo.Alpha</c> and on the app.
    ///
    /// Expected:
    /// - The library gets no source. The app's source compiles and registers, in this order: <c>XylemUI.Core</c>'s (every other
    ///   assembly references it), <c>Demo.Beta</c>'s (it references none of the others, and its name is before Zeta's),
    ///   <c>Demo.Zeta</c>'s, <c>Demo.Alpha</c>'s (after Zeta, which it references, though its name is first), and the app's own.
    /// </remarks>
    [Fact]
    public void DefaultKeyBindings_OfReferencedAssemblies_AreRegisteredInDependencyOrder()
    {
        static string Library(string name, string uses) =>
            $$"""
                using XylemUI;

                [assembly: DefaultKeyBindings(typeof({{name}}Keys))]

                public static class {{name}}Keys
                {
                    public static void AddDefaults(Keymap keymap) { {{uses}} }
                }
                """;
        MetadataReference zeta = Run("Demo.Zeta", OutputKind.DynamicallyLinkedLibrary, Library("Zeta", string.Empty)).Emit();
        Result alphaResult = Run("Demo.Alpha", OutputKind.DynamicallyLinkedLibrary, Library("Alpha", "ZetaKeys.AddDefaults(keymap);"), zeta);
        Assert.Empty(alphaResult.Sources);
        Assert.Empty(alphaResult.CompilerErrors);
        MetadataReference alpha = alphaResult.Emit();
        MetadataReference beta = Run("Demo.Beta", OutputKind.DynamicallyLinkedLibrary, Library("Beta", string.Empty)).Emit();
        const string App = """
            using XylemUI;

            [assembly: DefaultKeyBindings(typeof(AppKeys))]

            internal static class AppKeys
            {
                internal static void AddDefaults(Keymap keymap) { }
            }

            public static class Program
            {
                public static void Main() { }
            }
            """;

        Result app = Run("Demo.App", OutputKind.ConsoleApplication, App, alpha, beta, zeta);

        Assert.Empty(app.GeneratorDiagnostics);
        Assert.Empty(app.CompilerErrors);
        string source = Assert.Single(app.Sources);
        int At(string type) => source.IndexOf($"global::XylemUI.Keymap.RegisterDefaults({type}.AddDefaults);", StringComparison.Ordinal);
        int[] at =
        [
            At("global::XylemUI.CoreKeyBindings"),
            At("global::BetaKeys"),
            At("global::ZetaKeys"),
            At("global::AlphaKeys"),
            At("global::AppKeys"),
        ];
        Assert.True(at[0] > 0, source);
        Assert.Equal(at.Order(), at);
    }

    /// <summary>
    /// A class named for the default key bindings that has no <c>AddDefaults</c> method the app can call is an error, and is not
    /// registered.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - For each row, a library that names a class: one with no <c>AddDefaults</c>; one whose <c>AddDefaults</c> is not static;
    ///   one whose <c>AddDefaults</c> takes no parameter; one whose <c>AddDefaults</c> takes a string; one whose <c>AddDefaults</c> is internal; an internal class. An app that
    ///   references the library.
    ///
    /// Steps:
    /// - Run the generator on each app.
    ///
    /// Expected:
    /// - One error XYLEM007 that names the class and the library, at no place in the app's source. The app's source does not
    ///   register the class, and still registers <c>XylemUI.Core</c>'s. A library that references the library gets no
    ///   diagnostic and no source: the error is the app's.
    /// </remarks>
    /// <param name="declaration">The class the library names.</param>
    [Theory]
    [InlineData("public static class BadKeys { }")]
    [InlineData("public sealed class BadKeys { public void AddDefaults(XylemUI.Keymap keymap) { } }")]
    [InlineData("public static class BadKeys { public static void AddDefaults() { } }")]
    [InlineData("public static class BadKeys { public static void AddDefaults(string keymap) { } }")]
    [InlineData("public static class BadKeys { internal static void AddDefaults(XylemUI.Keymap keymap) { } }")]
    [InlineData("internal static class BadKeys { public static void AddDefaults(XylemUI.Keymap keymap) { } }")]
    public void DefaultKeyBindings_WithNoMethodTheAppCanCall_IsAnError(string declaration)
    {
        MetadataReference library = Run(
                "Demo.Lib",
                OutputKind.DynamicallyLinkedLibrary,
                "[assembly: XylemUI.DefaultKeyBindings(typeof(BadKeys))]\n" + declaration
            )
            .Emit();

        Result app = Run("Demo.App", OutputKind.ConsoleApplication, "public static class Program { public static void Main() { } }", library);

        Diagnostic diagnostic = Assert.Single(app.GeneratorDiagnostics);
        Assert.Equal("XYLEM007", diagnostic.Id);
        Assert.Equal(LocationKind.None, diagnostic.Location.Kind);
        string message = diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture);
        Assert.Contains("BadKeys", message, StringComparison.Ordinal);
        Assert.Contains("Demo.Lib", message, StringComparison.Ordinal);
        string source = Assert.Single(app.Sources);
        Assert.DoesNotContain("BadKeys", source, StringComparison.Ordinal);
        Assert.Contains("global::XylemUI.CoreKeyBindings.AddDefaults", source, StringComparison.Ordinal);

        Result between = Run("Demo.Between", OutputKind.DynamicallyLinkedLibrary, "public sealed class Plain;", library);
        Assert.Empty(between.GeneratorDiagnostics);
        Assert.Empty(between.Sources);
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
    /// - The first: no diagnostic, no compiler error, and the table registers <c>demo::Jump</c> with <c>new global::Jump()</c>,
    ///   the setter of its init-only property, and a comparison of that property (design §7, "Menu items that name actions"). The
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
        Assert.Contains("ActionRegistry.Compare<global::Jump>(Same0);", jump.Sources[0], StringComparison.Ordinal);
        Assert.Contains("private static bool Same0(global::Jump left, global::Jump right) =>", jump.Sources[0], StringComparison.Ordinal);
        Assert.Contains(
            "global::System.Collections.Generic.EqualityComparer<int>.Default.Equals(left.Line, right.Line);",
            jump.Sources[0],
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

    /// <summary>
    /// An app's one <c>[assembly: WindowTheme]</c> makes its module initializer pass the named class's <c>Wrap</c> to
    /// <c>WindowRoot.SetWrapper</c> (design §13.2, "根で置くもの"), for a class of a library and for an internal class of the app.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A library <c>XylemUI.Theme.Fluent</c> with <c>public static class FluentRoot</c> and its
    ///   <c>public static Element Wrap(Func&lt;Element&gt; root)</c>, compiled. An app that references it and carries
    ///   <c>[assembly: XylemUI.WindowTheme(typeof(XylemUI.Theme.Fluent.FluentRoot))]</c>; another app that names its own internal
    ///   class <c>AppRoot</c> with an internal <c>Wrap</c>; a third whose <c>AppRoot.Wrap</c> takes a <c>Func&lt;Element&gt;?</c>,
    ///   a nullable annotation that the conversion to <c>Func&lt;Func&lt;Element&gt;, Element&gt;</c> allows.
    ///
    /// Steps:
    /// - Run the generator on each app; compile the results.
    ///
    /// Expected:
    /// - No diagnostic from the generator and none from the compiler. The first app's source has one
    ///   <c>global::XylemUI.WindowRoot.SetWrapper(global::XylemUI.Theme.Fluent.FluentRoot.Wrap);</c>, in the module initializer
    ///   after the default key bindings of <c>XylemUI.Core</c>; the second and the third app's have
    ///   <c>global::XylemUI.WindowRoot.SetWrapper(global::AppRoot.Wrap);</c>.
    /// </remarks>
    [Fact]
    public void WindowTheme_OfAnApp_InitializerSetsTheWrapper()
    {
        MetadataReference theme = Run("XylemUI.Theme.Fluent", OutputKind.DynamicallyLinkedLibrary, ThemeLibrary).Emit();
        const string OwnRoot = """
            [assembly: XylemUI.WindowTheme(typeof(AppRoot))]

            internal static class AppRoot
            {
                internal static XylemUI.Element Wrap(System.Func<XylemUI.Element> root) => root();
            }
            """;
        const string AnnotatedRoot = """
            [assembly: XylemUI.WindowTheme(typeof(AppRoot))]

            public static class AppRoot
            {
                public static XylemUI.Element Wrap(System.Func<XylemUI.Element>? root) => root!();
            }
            """;

        Result fluent = Run("Demo.App", OutputKind.ConsoleApplication, FluentTheme + AppMain, theme);

        Assert.Empty(fluent.GeneratorDiagnostics);
        Assert.Empty(fluent.CompilerErrors);
        string source = Assert.Single(fluent.Sources);
        const string SetWrapper = "            global::XylemUI.WindowRoot.SetWrapper(global::XylemUI.Theme.Fluent.FluentRoot.Wrap);\n";
        int initializer = source.IndexOf("internal static void RegisterAll()", StringComparison.Ordinal);
        int defaults = source.IndexOf("global::XylemUI.CoreKeyBindings.AddDefaults", StringComparison.Ordinal);
        int set = source.IndexOf(SetWrapper, StringComparison.Ordinal);
        Assert.True(initializer > 0 && defaults > initializer && set > defaults, source);
        Assert.Equal(set, source.LastIndexOf(SetWrapper, StringComparison.Ordinal));

        Result own = Run("Demo.Own", OutputKind.ConsoleApplication, OwnRoot + AppMain);
        Assert.Empty(own.GeneratorDiagnostics);
        Assert.Empty(own.CompilerErrors);
        Assert.Contains("global::XylemUI.WindowRoot.SetWrapper(global::AppRoot.Wrap);", Assert.Single(own.Sources), StringComparison.Ordinal);

        Result annotated = Run("Demo.Annotated", OutputKind.ConsoleApplication, AnnotatedRoot + AppMain);
        Assert.Empty(annotated.GeneratorDiagnostics);
        Assert.Empty(annotated.CompilerErrors);
        Assert.Contains("global::XylemUI.WindowRoot.SetWrapper(global::AppRoot.Wrap);", Assert.Single(annotated.Sources), StringComparison.Ordinal);
    }

    /// <summary>
    /// An app with two <c>[assembly: WindowTheme]</c> is the error XYLEM010, and its module initializer sets no wrapper: one
    /// wrapper owns a window's backdrop and title bar (design §13.2, "包みは 1 つ").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The library <c>XylemUI.Theme.Fluent</c> of the test above, compiled. An app that references it and carries, on its first
    ///   line, the attribute that names <c>FluentRoot</c> and, on its second, one that names its own <c>AppRoot</c>, which has a
    ///   valid <c>Wrap</c>.
    ///
    /// Steps:
    /// - Run the generator on the app; compile the result.
    ///
    /// Expected:
    /// - One diagnostic, XYLEM010 of severity Error, at the second attribute (line 1 counted from 0), whose message names
    ///   <c>XylemUI.Theme.Fluent.FluentRoot</c> and <c>AppRoot</c>. No compiler error. The app's source still registers the default
    ///   key bindings of <c>XylemUI.Core</c> and has no <c>SetWrapper</c>.
    /// </remarks>
    [Fact]
    public void WindowTheme_Twice_IsAnErrorAndSetsNoWrapper()
    {
        MetadataReference theme = Run("XylemUI.Theme.Fluent", OutputKind.DynamicallyLinkedLibrary, ThemeLibrary).Emit();
        const string Second = """
            [assembly: XylemUI.WindowTheme(typeof(AppRoot))]

            public static class AppRoot
            {
                public static XylemUI.Element Wrap(System.Func<XylemUI.Element> root) => root();
            }
            """;

        Result app = Run("Demo.App", OutputKind.ConsoleApplication, FluentTheme + Second + AppMain, theme);

        Diagnostic diagnostic = Assert.Single(app.GeneratorDiagnostics);
        Assert.Equal("XYLEM010", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.True(diagnostic.Location.IsInSource || diagnostic.Location.Kind == LocationKind.ExternalFile);
        Assert.Equal(1, diagnostic.Location.GetLineSpan().StartLinePosition.Line);
        string message = diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture);
        Assert.Contains("XylemUI.Theme.Fluent.FluentRoot", message, StringComparison.Ordinal);
        Assert.Contains("AppRoot", message, StringComparison.Ordinal);
        Assert.Empty(app.CompilerErrors);
        string source = Assert.Single(app.Sources);
        Assert.Contains("global::XylemUI.CoreKeyBindings.AddDefaults", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SetWrapper", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// A class named by an app's <c>[assembly: WindowTheme]</c> that has no <c>Wrap</c> the module initializer can pass to
    /// <c>WindowRoot.SetWrapper</c> is the error XYLEM011 at the attribute, and the windows are not wrapped.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - For each row, a library <c>Demo.Theme</c> with a public type <c>BadRoot</c>: a class with no <c>Wrap</c>; one whose
    ///   <c>Wrap</c> is not static; one whose <c>Wrap</c> returns nothing; one whose <c>Wrap</c> returns <c>object</c>; one whose
    ///   <c>Wrap</c> takes no parameter; one whose <c>Wrap</c> takes a second parameter; one whose <c>Wrap</c> takes an
    ///   <c>Action</c>; one whose <c>Wrap</c> takes a <c>Lazy&lt;Element&gt;</c>; one whose <c>Wrap</c> takes a
    ///   <c>Func&lt;Element?&gt;</c> (a <c>Func</c> of <c>Nullable&lt;Element&gt;</c>, since <c>Element</c> is a struct); one
    ///   whose <c>Wrap</c> takes its <c>Func&lt;Element&gt;</c> by <c>ref</c>, one by <c>in</c>, and one by <c>out</c>; one whose
    ///   <c>Wrap</c> is generic; one whose <c>Wrap</c> is internal; an interface whose <c>Wrap</c> is <c>static abstract</c>, and
    ///   one whose <c>Wrap</c> is <c>static virtual</c>. An app that references the library and carries
    ///   <c>[assembly: XylemUI.WindowTheme(typeof(BadRoot))]</c>.
    ///
    /// Steps:
    /// - Run the generator on the app; compile the result.
    ///
    /// Expected:
    /// - One diagnostic, XYLEM011 of severity Error, in the app's source, whose message names <c>BadRoot</c>. No compiler error.
    ///   The app's source still registers the default key bindings of <c>XylemUI.Core</c>, and has no <c>SetWrapper</c> and no
    ///   <c>BadRoot</c>.
    /// </remarks>
    /// <param name="declaration">The class the app names.</param>
    [Theory]
    [InlineData("public static class BadRoot { }")]
    [InlineData("public sealed class BadRoot { public XylemUI.Element Wrap(System.Func<XylemUI.Element> root) => root(); }")]
    [InlineData("public static class BadRoot { public static void Wrap(System.Func<XylemUI.Element> root) { } }")]
    [InlineData("public static class BadRoot { public static object Wrap(System.Func<XylemUI.Element> root) => root(); }")]
    [InlineData("public static class BadRoot { public static XylemUI.Element Wrap() => default; }")]
    [InlineData("public static class BadRoot { public static XylemUI.Element Wrap(System.Func<XylemUI.Element> root, int depth) => root(); }")]
    [InlineData("public static class BadRoot { public static XylemUI.Element Wrap(System.Action root) => default; }")]
    [InlineData("public static class BadRoot { public static XylemUI.Element Wrap(System.Lazy<XylemUI.Element> root) => root.Value; }")]
    [InlineData("public static class BadRoot { public static XylemUI.Element Wrap(System.Func<XylemUI.Element?> root) => default; }")]
    [InlineData("public static class BadRoot { public static XylemUI.Element Wrap(ref System.Func<XylemUI.Element> root) => root(); }")]
    [InlineData("public static class BadRoot { public static XylemUI.Element Wrap(in System.Func<XylemUI.Element> root) => root(); }")]
    [InlineData(
        "public static class BadRoot { public static XylemUI.Element Wrap(out System.Func<XylemUI.Element> root) { root = null!; return default; } }"
    )]
    [InlineData("public static class BadRoot { public static XylemUI.Element Wrap<T>(System.Func<XylemUI.Element> root) => root(); }")]
    [InlineData("public static class BadRoot { internal static XylemUI.Element Wrap(System.Func<XylemUI.Element> root) => root(); }")]
    [InlineData("public interface BadRoot { static abstract XylemUI.Element Wrap(System.Func<XylemUI.Element> root); }")]
    [InlineData("public interface BadRoot { static virtual XylemUI.Element Wrap(System.Func<XylemUI.Element> root) => root(); }")]
    public void WindowTheme_WithNoWrapTheAppCanPass_IsAnError(string declaration)
    {
        MetadataReference library = Run("Demo.Theme", OutputKind.DynamicallyLinkedLibrary, declaration).Emit();

        Result app = Run("Demo.App", OutputKind.ConsoleApplication, "[assembly: XylemUI.WindowTheme(typeof(BadRoot))]\n" + AppMain, library);

        Diagnostic diagnostic = Assert.Single(app.GeneratorDiagnostics);
        Assert.Equal("XYLEM011", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.True(diagnostic.Location.IsInSource || diagnostic.Location.Kind == LocationKind.ExternalFile);
        Assert.Contains("BadRoot", diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
        Assert.Empty(app.CompilerErrors);
        string source = Assert.Single(app.Sources);
        Assert.Contains("global::XylemUI.CoreKeyBindings.AddDefaults", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SetWrapper", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BadRoot", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// An app's <c>[assembly: WindowTheme]</c> whose argument names no class is the error XYLEM011 at the attribute, as a class
    /// without <c>Wrap</c> is, and is not skipped.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - For each row, an app with a valid class <c>AppRoot</c> and one attribute whose argument is <c>typeof(int[])</c>, an
    ///   array, or <c>null</c>.
    ///
    /// Steps:
    /// - Run the generator on the app; compile the result.
    ///
    /// Expected:
    /// - One diagnostic, XYLEM011, in the app's source, whose message names the argument (<c>int[]</c>, <c>null</c>). No
    ///   compiler error, and no <c>SetWrapper</c> in the app's source.
    /// </remarks>
    /// <param name="argument">The attribute's argument.</param>
    /// <param name="named">What the message names.</param>
    [Theory]
    [InlineData("typeof(int[])", "int[]")]
    [InlineData("null", "null")]
    public void WindowTheme_WithAnArgumentThatNamesNoClass_IsAnError(string argument, string named)
    {
        string source =
            "[assembly: XylemUI.WindowTheme("
            + argument
            + ")]\npublic static class AppRoot { public static XylemUI.Element Wrap(System.Func<XylemUI.Element> root) => root(); }\n"
            + AppMain;

        Result app = Run("Demo.App", OutputKind.ConsoleApplication, source);

        Diagnostic diagnostic = Assert.Single(app.GeneratorDiagnostics);
        Assert.Equal("XYLEM011", diagnostic.Id);
        Assert.True(diagnostic.Location.IsInSource || diagnostic.Location.Kind == LocationKind.ExternalFile);
        Assert.Contains("'" + named + "'", diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
        Assert.Empty(app.CompilerErrors);
        Assert.DoesNotContain("SetWrapper", Assert.Single(app.Sources), StringComparison.Ordinal);
    }

    /// <summary>
    /// An attribute whose argument names no class counts for XYLEM010: beside a valid one, the app has two, and no wrapper is set.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An app with a valid class <c>AppRoot</c>, the attribute that names it on line 0, and
    ///   <c>[assembly: XylemUI.WindowTheme(null)]</c> on line 1.
    ///
    /// Steps:
    /// - Run the generator on the app; compile the result.
    ///
    /// Expected:
    /// - Two diagnostics (design §13.2, "包みは 1 つ"): XYLEM010 at line 1, and XYLEM011 at line 1 whose message names
    ///   <c>null</c>. No compiler error, and no <c>SetWrapper</c> in the app's source.
    /// </remarks>
    [Fact]
    public void WindowTheme_ValidAndNull_IsTwoAndSetsNoWrapper()
    {
        const string App = """
            [assembly: XylemUI.WindowTheme(typeof(AppRoot))]
            [assembly: XylemUI.WindowTheme(null)]

            public static class AppRoot
            {
                public static XylemUI.Element Wrap(System.Func<XylemUI.Element> root) => root();
            }
            """;

        Result app = Run("Demo.App", OutputKind.ConsoleApplication, App + AppMain);

        Assert.Equal(2, app.GeneratorDiagnostics.Length);
        Diagnostic two = Assert.Single(app.GeneratorDiagnostics, static d => d.Id == "XYLEM010");
        Assert.Equal(1, two.Location.GetLineSpan().StartLinePosition.Line);
        Diagnostic noWrap = Assert.Single(app.GeneratorDiagnostics, static d => d.Id == "XYLEM011");
        Assert.Equal(1, noWrap.Location.GetLineSpan().StartLinePosition.Line);
        Assert.Contains("'null'", noWrap.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
        Assert.Empty(app.CompilerErrors);
        Assert.DoesNotContain("SetWrapper", Assert.Single(app.Sources), StringComparison.Ordinal);
    }

    /// <summary>
    /// When one of an app's two <c>[assembly: WindowTheme]</c> names a class without <c>Wrap</c>, both errors are reported:
    /// XYLEM010 at the second attribute, naming both classes, and XYLEM011 at the invalid one (design §13.2, "包みは 1 つ").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An app with a class <c>BadRoot</c> that has no <c>Wrap</c> and a valid class <c>AppRoot</c>, and the attributes that
    ///   name <c>BadRoot</c> on line 0 and <c>AppRoot</c> on line 1.
    ///
    /// Steps:
    /// - Run the generator on the app; compile the result.
    ///
    /// Expected:
    /// - Two diagnostics: XYLEM010 at line 1, whose message names <c>BadRoot</c> and <c>AppRoot</c>; XYLEM011 at line 0, whose
    ///   message names <c>BadRoot</c>. No compiler error, and no <c>SetWrapper</c> in the app's source.
    /// </remarks>
    [Fact]
    public void WindowTheme_TwiceWithOneInvalid_ReportsBothErrors()
    {
        const string App = """
            [assembly: XylemUI.WindowTheme(typeof(BadRoot))]
            [assembly: XylemUI.WindowTheme(typeof(AppRoot))]

            public static class BadRoot { }

            public static class AppRoot
            {
                public static XylemUI.Element Wrap(System.Func<XylemUI.Element> root) => root();
            }
            """;

        Result app = Run("Demo.App", OutputKind.ConsoleApplication, App + AppMain);

        Assert.Equal(2, app.GeneratorDiagnostics.Length);
        Diagnostic two = Assert.Single(app.GeneratorDiagnostics, static d => d.Id == "XYLEM010");
        Assert.Equal(1, two.Location.GetLineSpan().StartLinePosition.Line);
        string message = two.GetMessage(System.Globalization.CultureInfo.InvariantCulture);
        Assert.Contains("BadRoot", message, StringComparison.Ordinal);
        Assert.Contains("AppRoot", message, StringComparison.Ordinal);
        Diagnostic noWrap = Assert.Single(app.GeneratorDiagnostics, static d => d.Id == "XYLEM011");
        Assert.Equal(0, noWrap.Location.GetLineSpan().StartLinePosition.Line);
        Assert.Contains("BadRoot", noWrap.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
        Assert.Empty(app.CompilerErrors);
        Assert.DoesNotContain("SetWrapper", Assert.Single(app.Sources), StringComparison.Ordinal);
    }

    /// <summary>
    /// An app with three <c>[assembly: WindowTheme]</c> gets one XYLEM010, at the second attribute, that names every class.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An app with three valid classes, <c>FirstRoot</c>, <c>SecondRoot</c>, and <c>ThirdRoot</c>, and the attributes that name
    ///   them on lines 0, 1, and 2.
    ///
    /// Steps:
    /// - Run the generator on the app; compile the result.
    ///
    /// Expected:
    /// - One diagnostic, XYLEM010, at line 1, not line 2; its message names the three classes. No compiler error, and no
    ///   <c>SetWrapper</c> in the app's source.
    /// </remarks>
    [Fact]
    public void WindowTheme_ThreeTimes_IsOneErrorAtTheSecond()
    {
        const string App = """
            [assembly: XylemUI.WindowTheme(typeof(FirstRoot))]
            [assembly: XylemUI.WindowTheme(typeof(SecondRoot))]
            [assembly: XylemUI.WindowTheme(typeof(ThirdRoot))]

            public static class FirstRoot
            {
                public static XylemUI.Element Wrap(System.Func<XylemUI.Element> root) => root();
            }

            public static class SecondRoot
            {
                public static XylemUI.Element Wrap(System.Func<XylemUI.Element> root) => root();
            }

            public static class ThirdRoot
            {
                public static XylemUI.Element Wrap(System.Func<XylemUI.Element> root) => root();
            }
            """;

        Result app = Run("Demo.App", OutputKind.ConsoleApplication, App + AppMain);

        Diagnostic diagnostic = Assert.Single(app.GeneratorDiagnostics);
        Assert.Equal("XYLEM010", diagnostic.Id);
        Assert.Equal(1, diagnostic.Location.GetLineSpan().StartLinePosition.Line);
        string message = diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture);
        Assert.Contains("FirstRoot", message, StringComparison.Ordinal);
        Assert.Contains("SecondRoot", message, StringComparison.Ordinal);
        Assert.Contains("ThirdRoot", message, StringComparison.Ordinal);
        Assert.Empty(app.CompilerErrors);
        Assert.DoesNotContain("SetWrapper", Assert.Single(app.Sources), StringComparison.Ordinal);
    }

    /// <summary>
    /// An app whose module initializer has nothing to do but set its root wrapper still gets one: the wrapper does not lean on
    /// the default key bindings of <c>XylemUI.Core</c>.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An app compiled without <c>XylemUI.Core</c>, so that it references no action table and no default key bindings. Its own
    ///   source declares stand-ins of <c>XylemUI.WindowThemeAttribute</c>, <c>XylemUI.Element</c>, and
    ///   <c>XylemUI.WindowRoot.SetWrapper</c>, a valid <c>AppRoot</c>, and the attribute that names it.
    ///
    /// Steps:
    /// - Run the generator on the app; compile the result.
    ///
    /// Expected:
    /// - No diagnostic and no compiler error. One source, whose module initializer calls
    ///   <c>global::XylemUI.WindowRoot.SetWrapper(global::AppRoot.Wrap);</c> and registers no table and no default key bindings.
    /// </remarks>
    [Fact]
    public void WindowTheme_OfAnAppWithNothingElse_InitializerSetsTheWrapper()
    {
        const string App = """
            [assembly: XylemUI.WindowTheme(typeof(AppRoot))]

            namespace XylemUI
            {
                [System.AttributeUsage(System.AttributeTargets.Assembly, AllowMultiple = true)]
                public sealed class WindowThemeAttribute(System.Type type) : System.Attribute
                {
                    public System.Type Type { get; } = type;
                }

                public readonly struct Element { }

                public static class WindowRoot
                {
                    public static void SetWrapper(System.Func<System.Func<Element>, Element> wrapper) { }
                }
            }

            public static class AppRoot
            {
                public static XylemUI.Element Wrap(System.Func<XylemUI.Element> root) => root();
            }

            public static class Program
            {
                public static void Main() { }
            }
            """;
        CSharpCompilation compilation = CSharpCompilation.Create(
            "Demo.Bare",
            [CSharpSyntaxTree.ParseText(App, Options, cancellationToken: TestContext.Current.CancellationToken)],
            [.. Platform()],
            new CSharpCompilationOptions(OutputKind.ConsoleApplication, nullableContextOptions: NullableContextOptions.Enable)
        );
        GeneratorDriver driver = CSharpGeneratorDriver.Create([new ActionTableGenerator().AsSourceGenerator()], parseOptions: Options);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out Compilation updated,
            out ImmutableArray<Diagnostic> diagnostics,
            TestContext.Current.CancellationToken
        );

        Assert.Empty(diagnostics);
        ImmutableArray<Diagnostic> compiled = updated.GetDiagnostics(TestContext.Current.CancellationToken);
        Assert.DoesNotContain(compiled, static d => d.Severity == DiagnosticSeverity.Error);
        string source = Assert.Single(driver.GetRunResult().GeneratedTrees).GetText(TestContext.Current.CancellationToken).ToString();
        Assert.Contains("global::XylemUI.WindowRoot.SetWrapper(global::AppRoot.Wrap);", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".Register();", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RegisterDefaults", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// An <c>[assembly: WindowTheme]</c> in an assembly that is not an executable does nothing: no diagnostic, even for two or for
    /// a class without <c>Wrap</c>, no source, and no wrapper in an app that references the assembly (design §13.2, "包みは 1 つ").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A library <c>Demo.Lib</c> that carries two attributes, one that names its own valid <c>LibRoot</c> and one that names its
    ///   <c>BadRoot</c>, which has no <c>Wrap</c>. An app with no attribute that references it.
    ///
    /// Steps:
    /// - Run the generator on the library and on the app.
    ///
    /// Expected:
    /// - The library: no diagnostic, no compiler error, no source. The app: no diagnostic, and its one source has no
    ///   <c>SetWrapper</c>.
    /// </remarks>
    [Fact]
    public void WindowTheme_InALibrary_DoesNothing()
    {
        const string Library = """
            [assembly: XylemUI.WindowTheme(typeof(LibRoot))]
            [assembly: XylemUI.WindowTheme(typeof(BadRoot))]

            public static class LibRoot
            {
                public static XylemUI.Element Wrap(System.Func<XylemUI.Element> root) => root();
            }

            public static class BadRoot { }
            """;

        Result library = Run("Demo.Lib", OutputKind.DynamicallyLinkedLibrary, Library);

        Assert.Empty(library.GeneratorDiagnostics);
        Assert.Empty(library.CompilerErrors);
        Assert.Empty(library.Sources);
        Result app = Run("Demo.App", OutputKind.ConsoleApplication, AppMain, library.Emit());
        Assert.Empty(app.GeneratorDiagnostics);
        Assert.DoesNotContain("SetWrapper", Assert.Single(app.Sources), StringComparison.Ordinal);
    }

    /// <summary>
    /// An app that references a theme and carries no <c>[assembly: WindowTheme]</c> is not wrapped: referencing the theme, as an
    /// app that uses only its icons does, sets nothing (design §13.2, "根で置くもの").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The library <c>XylemUI.Theme.Fluent</c> of the tests above, compiled; an app with no attribute that references it and
    ///   calls nothing of it.
    ///
    /// Steps:
    /// - Run the generator on the app; compile the result.
    ///
    /// Expected:
    /// - No diagnostic and no compiler error; the app's one source has the module initializer, and no <c>SetWrapper</c> and no
    ///   <c>FluentRoot</c>.
    /// </remarks>
    [Fact]
    public void App_ReferencingAThemeWithoutTheAttribute_IsNotWrapped()
    {
        MetadataReference theme = Run("XylemUI.Theme.Fluent", OutputKind.DynamicallyLinkedLibrary, ThemeLibrary).Emit();

        Result app = Run("Demo.App", OutputKind.ConsoleApplication, AppMain, theme);

        Assert.Empty(app.GeneratorDiagnostics);
        Assert.Empty(app.CompilerErrors);
        string source = Assert.Single(app.Sources);
        Assert.Contains("[global::System.Runtime.CompilerServices.ModuleInitializer]", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SetWrapper", source, StringComparison.Ordinal);
        Assert.DoesNotContain("FluentRoot", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// A second run after an edit that touches neither an action nor the attribute gives an app's source with its wrapper from
    /// the cache.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An app with <c>[assembly: XylemUI.WindowTheme(typeof(AppRoot))]</c> and its own valid <c>AppRoot</c>, with the
    ///   generator's steps tracked.
    ///
    /// Steps:
    /// - Run the generator; add a syntax tree with an unrelated class; run it again.
    ///
    /// Expected:
    /// - The first run's source sets the wrapper. The second run's output steps are <c>Cached</c> or <c>Unchanged</c>, not
    ///   <c>Modified</c> or <c>New</c>.
    /// </remarks>
    [Fact]
    public void UnrelatedEdit_ToAnAppWithAWindowTheme_DoesNotWriteItAgain()
    {
        const string App = """
            [assembly: XylemUI.WindowTheme(typeof(AppRoot))]

            public static class AppRoot
            {
                public static XylemUI.Element Wrap(System.Func<XylemUI.Element> root) => root();
            }

            public static class Program
            {
                public static void Main() { }
            }
            """;
        CSharpCompilation compilation = Compile("Demo.App", OutputKind.ConsoleApplication, App);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new ActionTableGenerator().AsSourceGenerator()],
            parseOptions: Options,
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true)
        );
        driver = driver.RunGenerators(compilation, TestContext.Current.CancellationToken);
        string first = Assert.Single(driver.GetRunResult().GeneratedTrees).GetText(TestContext.Current.CancellationToken).ToString();
        Assert.Contains("global::XylemUI.WindowRoot.SetWrapper(global::AppRoot.Wrap);", first, StringComparison.Ordinal);
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

    /// <summary>
    /// An app that references one assembly with <c>[assembly: PlatformHost]</c> gets a module initializer that calls the named
    /// class's <c>Register</c> (design §6.0, "ホストの選び方"); a library that references the host gets no such code.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A library <c>XylemUI.Platform.Windows</c> that carries <c>[assembly: XylemUI.PlatformHost(typeof(Win32Host))]</c>, the
    ///   internal attribute of <c>XylemUI.Core</c>, and has <c>public static void Register()</c>, compiled. An app and a library
    ///   that reference it.
    ///
    /// Steps:
    /// - Run the generator on the host, on the app, and on the library; compile the results.
    ///
    /// Expected:
    /// - The host compiles with no error and gets no source. The app: no diagnostic, no compiler error, and one source whose
    ///   module initializer calls <c>global::XylemUI.Platform.Windows.Win32Host.Register();</c> once, after the default key bindings
    ///   of <c>XylemUI.Core</c>. The library: no diagnostic and no source.
    /// </remarks>
    [Fact]
    public void PlatformHost_OfAReferencedAssembly_InitializerCallsItsRegister()
    {
        Result hostResult = Run("XylemUI.Platform.Windows", OutputKind.DynamicallyLinkedLibrary, WindowsHost);
        Assert.Empty(hostResult.CompilerErrors);
        Assert.Empty(hostResult.Sources);
        MetadataReference host = hostResult.Emit();

        Result app = Run("Demo.App", OutputKind.ConsoleApplication, AppMain, host);

        Assert.Empty(app.GeneratorDiagnostics);
        Assert.Empty(app.CompilerErrors);
        string source = Assert.Single(app.Sources);
        int initializer = source.IndexOf("internal static void RegisterAll()", StringComparison.Ordinal);
        int defaults = source.IndexOf("global::XylemUI.CoreKeyBindings.AddDefaults", StringComparison.Ordinal);
        int register = source.IndexOf(RegisterWindowsHost, StringComparison.Ordinal);
        Assert.True(initializer > 0 && defaults > initializer && register > defaults, source);
        Assert.Equal(register, source.LastIndexOf(RegisterWindowsHost, StringComparison.Ordinal));

        Result between = Run("Demo.Between", OutputKind.DynamicallyLinkedLibrary, "public sealed class Plain;", host);
        Assert.Empty(between.GeneratorDiagnostics);
        Assert.Empty(between.Sources);
    }

    /// <summary>
    /// An app that references two assemblies with <c>[assembly: PlatformHost]</c> is the error XYLEM008, and its module initializer
    /// registers neither host (design §6.0, "ホストの選び方").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The host <c>XylemUI.Platform.Windows</c> of the test above, and a second host <c>XylemUI.Testing</c> (another name Core
    ///   shows its internals to) that names its <c>Demo.OtherHost</c>, which has a valid <c>Register</c>, both compiled. An app that
    ///   references both.
    ///
    /// Steps:
    /// - Run the generator on the app; compile the result.
    ///
    /// Expected:
    /// - One diagnostic, XYLEM008 of severity Error, at no place in the app's source, whose message names
    ///   <c>XylemUI.Platform.Windows.Win32Host</c> and <c>Demo.OtherHost</c>. No compiler error. The app's source still registers
    ///   the default key bindings of <c>XylemUI.Core</c>, and names neither host.
    /// </remarks>
    [Fact]
    public void PlatformHost_Twice_IsAnErrorAndRegistersNone()
    {
        const string OtherHost = """
            [assembly: XylemUI.PlatformHost(typeof(Demo.OtherHost))]

            namespace Demo;

            public static class OtherHost
            {
                public static void Register() { }
            }
            """;
        MetadataReference windows = Run("XylemUI.Platform.Windows", OutputKind.DynamicallyLinkedLibrary, WindowsHost).Emit();
        MetadataReference other = Run("XylemUI.Testing", OutputKind.DynamicallyLinkedLibrary, OtherHost).Emit();

        Result app = Run("Demo.App", OutputKind.ConsoleApplication, AppMain, windows, other);

        Diagnostic diagnostic = Assert.Single(app.GeneratorDiagnostics);
        Assert.Equal("XYLEM008", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(LocationKind.None, diagnostic.Location.Kind);
        string message = diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture);
        Assert.Contains("XylemUI.Platform.Windows.Win32Host", message, StringComparison.Ordinal);
        Assert.Contains("Demo.OtherHost", message, StringComparison.Ordinal);
        Assert.Empty(app.CompilerErrors);
        string source = Assert.Single(app.Sources);
        Assert.Contains("global::XylemUI.CoreKeyBindings.AddDefaults", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Win32Host", source, StringComparison.Ordinal);
        Assert.DoesNotContain("OtherHost", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// A class named by <c>[assembly: PlatformHost]</c> that has no <c>Register</c> the app can call is the error XYLEM009, and
    /// the host is not registered (design §6.0, "ホストの選び方").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - For each row, a host <c>XylemUI.Platform.Windows</c> whose attribute names, with <c>typeof(BadHost)</c>: a class with no
    ///   <c>Register</c>; one whose <c>Register</c> is not static; one whose <c>Register</c> takes a parameter; one whose
    ///   <c>Register</c> returns an int; one whose <c>Register</c> is generic; one whose <c>Register</c> is internal; an internal
    ///   class; an interface whose <c>Register</c> is <c>static abstract</c>, and one whose <c>Register</c> is
    ///   <c>static virtual</c>. Then an attribute whose argument names no class, <c>typeof(int[])</c> and <c>null</c>, beside a
    ///   valid <c>BadHost</c>. An app that references the host.
    ///
    /// Steps:
    /// - Run the generator on the app; compile the result.
    ///
    /// Expected:
    /// - One diagnostic, XYLEM009 of severity Error, at no place in the app's source, whose message names the argument
    ///   (<c>BadHost</c>, <c>int[]</c>, <c>null</c>) and the host <c>XylemUI.Platform.Windows</c>. No compiler error. The app's
    ///   source still registers the default key bindings of <c>XylemUI.Core</c>, names no <c>BadHost</c>, and calls no
    ///   <c>Register</c> but the one of <c>XylemUI.Core</c>'s action table.
    /// </remarks>
    /// <param name="argument">The attribute's argument.</param>
    /// <param name="declaration">The class the host declares.</param>
    /// <param name="named">What the message names.</param>
    [Theory]
    [InlineData("typeof(BadHost)", "public static class BadHost { }", "BadHost")]
    [InlineData("typeof(BadHost)", "public sealed class BadHost { public void Register() { } }", "BadHost")]
    [InlineData("typeof(BadHost)", "public static class BadHost { public static void Register(int code) { } }", "BadHost")]
    [InlineData("typeof(BadHost)", "public static class BadHost { public static int Register() => 0; }", "BadHost")]
    [InlineData("typeof(BadHost)", "public static class BadHost { public static void Register<T>() { } }", "BadHost")]
    [InlineData("typeof(BadHost)", "public static class BadHost { internal static void Register() { } }", "BadHost")]
    [InlineData("typeof(BadHost)", "internal static class BadHost { public static void Register() { } }", "BadHost")]
    [InlineData("typeof(BadHost)", "public interface BadHost { static abstract void Register(); }", "BadHost")]
    [InlineData("typeof(BadHost)", "public interface BadHost { static virtual void Register() { } }", "BadHost")]
    [InlineData("typeof(int[])", "public static class BadHost { public static void Register() { } }", "int[]")]
    [InlineData("null", "public static class BadHost { public static void Register() { } }", "null")]
    public void PlatformHost_WithNoRegisterTheAppCanCall_IsAnError(string argument, string declaration, string named)
    {
        MetadataReference host = Run(
                "XylemUI.Platform.Windows",
                OutputKind.DynamicallyLinkedLibrary,
                "[assembly: XylemUI.PlatformHost(" + argument + ")]\n" + declaration
            )
            .Emit();

        Result app = Run("Demo.App", OutputKind.ConsoleApplication, AppMain, host);

        Diagnostic diagnostic = Assert.Single(app.GeneratorDiagnostics);
        Assert.Equal("XYLEM009", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(LocationKind.None, diagnostic.Location.Kind);
        string message = diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture);
        Assert.Contains("'" + named + "'", message, StringComparison.Ordinal);
        Assert.Contains("XylemUI.Platform.Windows", message, StringComparison.Ordinal);
        Assert.Empty(app.CompilerErrors);
        string source = Assert.Single(app.Sources);
        Assert.Contains("global::XylemUI.CoreKeyBindings.AddDefaults", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BadHost", source, StringComparison.Ordinal);
        Assert.Equal(1, Count(source, ".Register();"));
        Assert.Contains("global::XylemUI.Generated.XylemUI_CoreActions.Register();", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// An app whose references carry no <c>[assembly: PlatformHost]</c> registers no host, also when one of them has a class with a
    /// <c>Register</c> method: the attribute names the host, not the method's name.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A library <c>XylemUI.Platform.Windows</c> with the <c>Win32Host</c> of the tests above and no attribute, compiled. An app
    ///   that references it, and an app that references <c>XylemUI.Core</c> alone.
    ///
    /// Steps:
    /// - Run the generator on each app; compile the results.
    ///
    /// Expected:
    /// - No diagnostic and no compiler error. Each app's one source registers the default key bindings of <c>XylemUI.Core</c>,
    ///   names no <c>Win32Host</c>, and calls no <c>Register</c> but the one of <c>XylemUI.Core</c>'s action table.
    /// </remarks>
    [Fact]
    public void App_WithNoPlatformHost_RegistersNone()
    {
        const string NoAttribute = """
            namespace XylemUI.Platform.Windows;

            public static class Win32Host
            {
                public static void Register() { }
            }
            """;
        MetadataReference library = Run("XylemUI.Platform.Windows", OutputKind.DynamicallyLinkedLibrary, NoAttribute).Emit();

        Result[] apps = [Run("Demo.App", OutputKind.ConsoleApplication, AppMain, library), Run("Demo.Bare", OutputKind.ConsoleApplication, AppMain)];

        foreach (Result app in apps)
        {
            Assert.Empty(app.GeneratorDiagnostics);
            Assert.Empty(app.CompilerErrors);
            string source = Assert.Single(app.Sources);
            Assert.Contains("global::XylemUI.CoreKeyBindings.AddDefaults", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Win32Host", source, StringComparison.Ordinal);
            Assert.Equal(1, Count(source, ".Register();"));
        }
    }

    /// <summary>
    /// An app whose module initializer has nothing to do but register its host still gets one: the host does not lean on the
    /// action tables or the default key bindings of <c>XylemUI.Core</c>.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A library <c>Demo.Host</c> compiled without <c>XylemUI.Core</c>, with a stand-in of <c>XylemUI.PlatformHostAttribute</c>,
    ///   a class <c>Demo.Host</c> with <c>public static void Register()</c>, and the attribute that names it. An app compiled
    ///   without <c>XylemUI.Core</c> that references the library.
    ///
    /// Steps:
    /// - Run the generator on the app; compile the result.
    ///
    /// Expected:
    /// - No diagnostic and no compiler error. One source, whose module initializer calls <c>global::Demo.Host.Register();</c> and
    ///   registers no table and no default key bindings.
    /// </remarks>
    [Fact]
    public void PlatformHost_OfAnAppWithNothingElse_InitializerRegistersIt()
    {
        const string Library = """
            [assembly: XylemUI.PlatformHost(typeof(Demo.Host))]

            namespace XylemUI
            {
                [System.AttributeUsage(System.AttributeTargets.Assembly)]
                public sealed class PlatformHostAttribute(System.Type host) : System.Attribute
                {
                    public System.Type Host { get; } = host;
                }
            }

            namespace Demo
            {
                public static class Host
                {
                    public static void Register() { }
                }
            }
            """;
        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable);
        CSharpCompilation libraryCompilation = CSharpCompilation.Create(
            "Demo.Host",
            [CSharpSyntaxTree.ParseText(Library, Options, cancellationToken: TestContext.Current.CancellationToken)],
            [.. Platform()],
            options
        );
        using var stream = new MemoryStream();
        Microsoft.CodeAnalysis.Emit.EmitResult emitted = libraryCompilation.Emit(stream, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        CSharpCompilation compilation = CSharpCompilation.Create(
            "Demo.Bare",
            [CSharpSyntaxTree.ParseText(AppMain, Options, cancellationToken: TestContext.Current.CancellationToken)],
            [.. Platform(), MetadataReference.CreateFromImage(stream.ToArray())],
            options.WithOutputKind(OutputKind.ConsoleApplication)
        );
        GeneratorDriver driver = CSharpGeneratorDriver.Create([new ActionTableGenerator().AsSourceGenerator()], parseOptions: Options);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out Compilation updated,
            out ImmutableArray<Diagnostic> diagnostics,
            TestContext.Current.CancellationToken
        );

        Assert.Empty(diagnostics);
        ImmutableArray<Diagnostic> compiled = updated.GetDiagnostics(TestContext.Current.CancellationToken);
        Assert.DoesNotContain(compiled, static d => d.Severity == DiagnosticSeverity.Error);
        string source = Assert.Single(driver.GetRunResult().GeneratedTrees).GetText(TestContext.Current.CancellationToken).ToString();
        Assert.Contains("[global::System.Runtime.CompilerServices.ModuleInitializer]", source, StringComparison.Ordinal);
        Assert.Contains("            global::Demo.Host.Register();\n", source, StringComparison.Ordinal);
        Assert.Equal(1, Count(source, ".Register();"));
        Assert.DoesNotContain("RegisterDefaults", source, StringComparison.Ordinal);
    }

    // How many times a text holds a part.
    private static int Count(string text, string part)
    {
        int count = 0;
        for (int at = text.IndexOf(part, StringComparison.Ordinal); at >= 0; at = text.IndexOf(part, at + part.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
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
        MetadataReference core = MetadataReference.CreateFromFile(typeof(IAction).Assembly.Location);
        return CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source, Options)],
            [.. Platform(), core, .. references],
            new CSharpCompilationOptions(kind, nullableContextOptions: NullableContextOptions.Enable)
        );
    }

    // The framework's reference assemblies of the running test.
    private static IEnumerable<MetadataReference> Platform()
    {
        string trusted = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!;
        return trusted
            .Split(Path.PathSeparator)
            .Where(static path =>
                Path.GetFileName(path) is "System.Runtime.dll" or "System.Private.CoreLib.dll" or "netstandard.dll" or "System.Collections.dll"
            )
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path));
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
