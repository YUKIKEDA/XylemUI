using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace XylemUI.Analyzers.Tests;

// Runs the analyzers and the code fixes through the compiler's API on sources the tests write, compiled against XylemUI.Core (design
// §2.2, "Analyzers"): no analyzer test package, as XylemUI.Generators.Tests runs the generator.
internal static class Harness
{
    // One language version for every source, the repository's (C# 14).
    internal static readonly CSharpParseOptions Parse = new(LanguageVersion.Preview);

    private static readonly CSharpCompilationOptions Options = new(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable);

    // The framework's assemblies and XylemUI.Core with the assemblies it references, from the test's own output.
    internal static ImmutableArray<MetadataReference> References { get; } = MakeReferences();

    /// <summary>
    /// Returns the diagnostics an analyzer reports on a source, after checking that the source compiles without errors.
    /// </summary>
    internal static async Task<ImmutableArray<Diagnostic>> DiagnoseAsync(DiagnosticAnalyzer analyzer, string source)
    {
        CSharpCompilation compilation = Compile(source);
        Assert.Empty(Errors(compilation));
        ImmutableArray<Diagnostic> diagnostics = await compilation
            .WithAnalyzers([analyzer])
            .GetAnalyzerDiagnosticsAsync(TestContext.Current.CancellationToken);
        return diagnostics.Sort(static (left, right) => left.Location.SourceSpan.Start.CompareTo(right.Location.SourceSpan.Start));
    }

    /// <summary>
    /// Returns the source text a diagnostic points at.
    /// </summary>
    internal static string At(Diagnostic diagnostic) => diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan);

    internal static CSharpCompilation Compile(string source) =>
        CSharpCompilation.Create("Demo", [CSharpSyntaxTree.ParseText(source, Parse)], References, Options);

    internal static ImmutableArray<Diagnostic> Errors(Compilation compilation) =>
        [.. compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(static d => d.Severity == DiagnosticSeverity.Error)];

    /// <summary>
    /// Applies the one fix a provider offers for the first diagnostic it can fix, in a workspace, and returns the fixed source and
    /// the compiler's errors on it.
    /// </summary>
    /// <param name="provider">The code fix provider.</param>
    /// <param name="source">The source.</param>
    /// <param name="analyzer">The analyzer that reports the diagnostic, or <see langword="null"/> for a compiler diagnostic.</param>
    internal static async Task<(string Source, int Offered, ImmutableArray<Diagnostic> Errors)> FixAsync(
        CodeFixProvider provider,
        string source,
        DiagnosticAnalyzer? analyzer
    )
    {
        CancellationToken cancellation = TestContext.Current.CancellationToken;
        using var workspace = new AdhocWorkspace();
        ProjectId project = ProjectId.CreateNewId();
        DocumentId documentId = DocumentId.CreateNewId(project);
        Solution solution = workspace
            .CurrentSolution.AddProject(project, "Demo", "Demo", LanguageNames.CSharp)
            .WithProjectCompilationOptions(project, Options)
            .WithProjectParseOptions(project, Parse)
            .AddMetadataReferences(project, References)
            .AddDocument(documentId, "Demo.cs", SourceText.From(source));
        Document document = solution.GetDocument(documentId)!;
        Compilation compilation = (await document.Project.GetCompilationAsync(cancellation))!;
        ImmutableArray<Diagnostic> diagnostics = analyzer is null
            ? compilation.GetDiagnostics(cancellation)
            : await compilation.WithAnalyzers([analyzer]).GetAnalyzerDiagnosticsAsync(cancellation);
        Diagnostic diagnostic = diagnostics.First(d => provider.FixableDiagnosticIds.Contains(d.Id));

        var actions = new List<CodeAction>();
        await provider.RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic, (action, _) => actions.Add(action), cancellation));
        if (actions.Count == 0)
        {
            return (source, 0, Errors(compilation));
        }

        ImmutableArray<CodeActionOperation> operations = await actions[0].GetOperationsAsync(cancellation);
        Solution changed = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution;
        Document fixedDocument = changed.GetDocument(documentId)!;
        string text = (await fixedDocument.GetTextAsync(cancellation)).ToString();
        Compilation fixedCompilation = (await fixedDocument.Project.GetCompilationAsync(cancellation))!;
        return (text, actions.Count, Errors(fixedCompilation));
    }

    private static ImmutableArray<MetadataReference> MakeReferences()
    {
        // The framework's reference set: every assembly of the runtime's own folder, so that Task, ThreadPool, and Thread resolve.
        string runtime = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        string trusted = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!;
        IEnumerable<MetadataReference> platform = trusted
            .Split(Path.PathSeparator)
            .Where(path => string.Equals(Path.GetDirectoryName(path), runtime, StringComparison.OrdinalIgnoreCase))
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path));
        IEnumerable<MetadataReference> xylem = new[] { "XylemUI.Core.dll", "XylemUI.Text.dll", "XylemUI.Imaging.dll" }.Select(
            static name => (MetadataReference)MetadataReference.CreateFromFile(Path.Combine(AppContext.BaseDirectory, name))
        );
        return [.. platform, .. xylem];
    }
}
