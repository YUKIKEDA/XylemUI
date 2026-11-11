using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace XylemUI.Generators;

/// <summary>
/// Writes the table of an assembly's actions (design §7, "Shortcuts and actions"): for each class that carries
/// <c>XylemUI.ActionAttribute</c>, a call of <c>ActionRegistry.Register</c> with its name and a function that makes it. The table of
/// a library is a public method the apps that reference it call; an app's table also has a module initializer that calls its own
/// method and those of the assemblies it references, so that the names are known before the app's first line runs and nothing is
/// found by reflection.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class ActionTableGenerator : IIncrementalGenerator
{
    private const string ActionAttributeName = "XylemUI.ActionAttribute";
    private const string ActionTableAttributeName = "XylemUI.ActionTableAttribute";
    private const string ActionInterfaceName = "XylemUI.IAction";

    private static readonly DiagnosticDescriptor NotAnAction = new(
        "XYLEM001",
        "The type with [Action] is not an action",
        "'{0}' carries [Action] and does not implement XylemUI.IAction",
        "XylemUI",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    private static readonly DiagnosticDescriptor NoConstructor = new(
        "XYLEM002",
        "The action cannot be made by its name",
        "'{0}' carries [Action] and has no constructor without parameters that the assembly can call; an action made by its name starts with its default arguments",
        "XylemUI",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    private static readonly DiagnosticDescriptor DuplicateName = new(
        "XYLEM003",
        "Two actions have one name",
        "The action name '{0}' is given to '{1}' and to '{2}'; a name refers to one action",
        "XylemUI",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    private static readonly DiagnosticDescriptor EmptyName = new(
        "XYLEM004",
        "The action has no name",
        "'{0}' carries [Action] with an empty name",
        "XylemUI",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    private static readonly DiagnosticDescriptor NotReachable = new(
        "XYLEM005",
        "The action cannot be reached from the table",
        "'{0}' carries [Action] and is private, abstract, generic, or nested in a type the table cannot name",
        "XylemUI",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    /// <inheritdoc/>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<ActionInfo> actions = context.SyntaxProvider.ForAttributeWithMetadataName(
            ActionAttributeName,
            static (node, _) => IsClassOrRecord(node),
            static (attribute, _) => Read(attribute)
        );

        IncrementalValueProvider<AssemblyInfo> assembly = context.CompilationProvider.Select(static (compilation, _) => ReadAssembly(compilation));

        context.RegisterSourceOutput(actions.Collect().Combine(assembly), static (output, pair) => Write(output, pair.Left, pair.Right));
    }

    // A record is a class too, and is not a ClassDeclarationSyntax; an action with arguments is naturally one.
    private static bool IsClassOrRecord(SyntaxNode node) => node is ClassDeclarationSyntax or RecordDeclarationSyntax;

    private static ActionInfo Read(GeneratorAttributeSyntaxContext context)
    {
        var type = (INamedTypeSymbol)context.TargetSymbol;
        AttributeData attribute = context.Attributes[0];
        string name = attribute.ConstructorArguments.Length == 1 && attribute.ConstructorArguments[0].Value is string value ? value : string.Empty;
        bool isAction = type.AllInterfaces.Any(static i => i.ToDisplayString() == ActionInterfaceName);

        // The table is a class of the same assembly: it can call an internal constructor, and name a type that is internal.
        bool hasConstructor = type.InstanceConstructors.Any(static c =>
            c.Parameters.Length == 0 && c.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal
        );
        bool reachable = !type.IsAbstract && !type.IsGenericType;
        for (INamedTypeSymbol? t = type; t is not null && reachable; t = t.ContainingType)
        {
            reachable =
                t.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal
                && (ReferenceEquals(t, type) || !t.IsGenericType);
        }

        Location? location = type.Locations.FirstOrDefault();
        return new ActionInfo(
            name,
            type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            type.ToDisplayString(),
            isAction,
            hasConstructor,
            reachable,
            location is null ? null : LocationInfo.From(location)
        );
    }

    private static AssemblyInfo ReadAssembly(Compilation compilation)
    {
        bool isApp =
            compilation.Options.OutputKind is OutputKind.ConsoleApplication or OutputKind.WindowsApplication or OutputKind.WindowsRuntimeApplication;
        var tables = ImmutableArray.CreateBuilder<TableInfo>();
        foreach (IAssemblySymbol reference in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            foreach (AttributeData attribute in reference.GetAttributes())
            {
                if (attribute.AttributeClass?.ToDisplayString() != ActionTableAttributeName || attribute.ConstructorArguments.Length != 2)
                {
                    continue;
                }

                if (attribute.ConstructorArguments[0].Value is not INamedTypeSymbol table)
                {
                    continue;
                }

                ImmutableArray<string> names =
                    attribute.ConstructorArguments[1].Kind == TypedConstantKind.Array
                        ? attribute.ConstructorArguments[1].Values.Select(static v => v.Value as string ?? string.Empty).ToImmutableArray()
                        : ImmutableArray<string>.Empty;
                tables.Add(
                    new TableInfo(table.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), reference.Name, new EquatableArray<string>(names))
                );
            }
        }

        return new AssemblyInfo(Identifier(compilation.AssemblyName ?? "Assembly"), isApp, new EquatableArray<TableInfo>(tables.ToImmutable()));
    }

    private static void Write(SourceProductionContext output, ImmutableArray<ActionInfo> actions, AssemblyInfo assembly)
    {
        var valid = new List<ActionInfo>();
        var byName = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (TableInfo referenced in assembly.Tables)
        {
            foreach (string name in referenced.Names)
            {
                string owner = "an action of " + referenced.Assembly;
                if (byName.TryGetValue(name, out string? first))
                {
                    // Two of the assemblies this one references give the name: neither is this assembly's, so the error has no
                    // place in its source. Without it the app would build and throw in its module initializer.
                    output.ReportDiagnostic(Diagnostic.Create(DuplicateName, Location.None, name, first, owner));
                }
                else
                {
                    byName.Add(name, owner);
                }
            }
        }

        foreach (ActionInfo action in actions.OrderBy(static a => a.DisplayName, StringComparer.Ordinal))
        {
            Location location = action.Location?.ToLocation() ?? Location.None;
            if (!action.IsAction)
            {
                output.ReportDiagnostic(Diagnostic.Create(NotAnAction, location, action.DisplayName));
                continue;
            }

            if (!action.Reachable)
            {
                output.ReportDiagnostic(Diagnostic.Create(NotReachable, location, action.DisplayName));
                continue;
            }

            if (!action.HasConstructor)
            {
                output.ReportDiagnostic(Diagnostic.Create(NoConstructor, location, action.DisplayName));
                continue;
            }

            if (action.Name.Length == 0)
            {
                output.ReportDiagnostic(Diagnostic.Create(EmptyName, location, action.DisplayName));
                continue;
            }

            if (byName.TryGetValue(action.Name, out string? other))
            {
                output.ReportDiagnostic(Diagnostic.Create(DuplicateName, location, action.Name, other, action.DisplayName));
                continue;
            }

            byName.Add(action.Name, action.DisplayName);
            valid.Add(action);
        }

        // A library with no action has no table. An app has one even with none of its own: it calls the tables it references.
        if (valid.Count == 0 && !(assembly.IsApp && assembly.Tables.Count > 0))
        {
            return;
        }

        string table = assembly.Identifier + "Actions";
        var text = new StringBuilder();
        _ = text.Append("// <auto-generated/>\n");
        _ = text.Append("#nullable enable\n\n");
        if (valid.Count > 0)
        {
            _ = text.Append("[assembly: global::XylemUI.ActionTableAttribute(typeof(global::XylemUI.Generated.")
                .Append(table)
                .Append("), new string[] { ");
            _ = text.Append(string.Join(", ", valid.Select(static a => SymbolDisplay.FormatLiteral(a.Name, quote: true))));
            _ = text.Append(" })]\n\n");
        }

        _ = text.Append("namespace XylemUI.Generated\n{\n");
        _ = text.Append("    /// <summary>The actions of this assembly, by their names (XylemUI.Generators).</summary>\n");
        _ = text.Append("    [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]\n");
        _ = text.Append("    [global::System.CodeDom.Compiler.GeneratedCode(\"XylemUI.Generators\", \"1\")]\n");
        _ = text.Append("    ").Append(valid.Count > 0 ? "public" : "internal").Append(" static class ").Append(table).Append("\n    {\n");
        if (valid.Count > 0)
        {
            _ = text.Append("        /// <summary>Adds the actions of this assembly to <see cref=\"global::XylemUI.ActionRegistry\"/>.</summary>\n");
            _ = text.Append("        public static void Register()\n        {\n");
            foreach (ActionInfo action in valid)
            {
                _ = text.Append("            global::XylemUI.ActionRegistry.Register<").Append(action.TypeName).Append(">(");
                _ = text.Append(SymbolDisplay.FormatLiteral(action.Name, quote: true))
                    .Append(", static () => new ")
                    .Append(action.TypeName)
                    .Append("());\n");
            }

            _ = text.Append("        }\n");
        }

        if (assembly.IsApp)
        {
            if (valid.Count > 0)
            {
                _ = text.Append('\n');
            }

            _ = text.Append("        [global::System.Runtime.CompilerServices.ModuleInitializer]\n");
            _ = text.Append("        internal static void RegisterAll()\n        {\n");
            foreach (TableInfo reference in assembly.Tables.OrderBy(static t => t.TypeName, StringComparer.Ordinal))
            {
                _ = text.Append("            ").Append(reference.TypeName).Append(".Register();\n");
            }

            if (valid.Count > 0)
            {
                _ = text.Append("            Register();\n");
            }

            _ = text.Append("        }\n");
        }

        _ = text.Append("    }\n}\n");
        output.AddSource("XylemUI.Actions.g.cs", text.ToString());
    }

    // The assembly's name as a C# identifier: the table of each assembly has its own type name.
    private static string Identifier(string assemblyName)
    {
        var text = new StringBuilder(assemblyName.Length);
        foreach (char c in assemblyName)
        {
            _ = text.Append(char.IsLetterOrDigit(c) ? c : '_');
        }

        if (text.Length == 0 || char.IsDigit(text[0]))
        {
            _ = text.Insert(0, '_');
        }

        return text.ToString();
    }

    private sealed record ActionInfo(
        string Name,
        string TypeName,
        string DisplayName,
        bool IsAction,
        bool HasConstructor,
        bool Reachable,
        LocationInfo? Location
    );

    private sealed record TableInfo(string TypeName, string Assembly, EquatableArray<string> Names);

    private sealed record AssemblyInfo(string Identifier, bool IsApp, EquatableArray<TableInfo> Tables);
}
