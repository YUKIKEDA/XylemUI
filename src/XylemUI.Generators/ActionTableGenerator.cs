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
/// found by reflection. The module initializer also registers the default key bindings of the assemblies the app references
/// (<c>XylemUI.DefaultKeyBindingsAttribute</c>, design §7, "The default keys and actions"), an assembly after the assemblies
/// it references.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class ActionTableGenerator : IIncrementalGenerator
{
    private const string ActionAttributeName = "XylemUI.ActionAttribute";
    private const string ActionTableAttributeName = "XylemUI.ActionTableAttribute";
    private const string ActionInterfaceName = "XylemUI.IAction";
    private const string DefaultKeyBindingsAttributeName = "XylemUI.DefaultKeyBindingsAttribute";
    private const string AddDefaultsName = "AddDefaults";

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

    private static readonly DiagnosticDescriptor UnsupportedArgument = new(
        "XYLEM006",
        "The action's property cannot be given by name",
        "'{0}.{1}' can be set and its type is none an argument can be written in (int, long, float, double, bool, string, or an enum), or it is init-only on a class that is no record; the action made by its name keeps the property's default",
        "XylemUI",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true
    );

    private static readonly DiagnosticDescriptor NoAddDefaults = new(
        "XYLEM007",
        "The class of the default key bindings has no AddDefaults method",
        "'{0}' is named by [assembly: DefaultKeyBindings] of '{1}' and has no accessible 'public static void AddDefaults(XylemUI.Keymap keymap)'; its default key bindings are not registered",
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

        var arguments = ImmutableArray.CreateBuilder<ArgumentInfo>();
        var unsupported = ImmutableArray.CreateBuilder<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (INamedTypeSymbol? t = type; t is not null && t.SpecialType != SpecialType.System_Object; t = t.BaseType)
        {
            foreach (IPropertySymbol property in t.GetMembers().OfType<IPropertySymbol>())
            {
                // The properties code outside the type can set: those are the action's arguments.
                if (
                    property.IsStatic
                    || property.IsIndexer
                    || property.DeclaredAccessibility != Accessibility.Public
                    || property.SetMethod is not { DeclaredAccessibility: Accessibility.Public } setter
                    || !seen.Add(property.Name)
                )
                {
                    continue;
                }

                // An init-only property is set with a `with` expression, which only a record has.
                bool useWith = setter.IsInitOnly;
                string? kind = ArgumentKind(property.Type);
                if (kind is null || (useWith && !type.IsRecord))
                {
                    unsupported.Add(property.Name);
                    continue;
                }

                ImmutableArray<string> members =
                    property.Type.TypeKind == TypeKind.Enum
                        ? property
                            .Type.GetMembers()
                            .OfType<IFieldSymbol>()
                            .Where(static f => f.HasConstantValue)
                            .Select(static f => f.Name)
                            .ToImmutableArray()
                        : ImmutableArray<string>.Empty;
                arguments.Add(
                    new ArgumentInfo(
                        property.Name,
                        kind,
                        property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        new EquatableArray<string>(members),
                        useWith
                    )
                );
            }
        }

        Location? location = type.Locations.FirstOrDefault();
        return new ActionInfo(
            name,
            type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            type.ToDisplayString(),
            isAction,
            hasConstructor,
            reachable,
            new EquatableArray<ArgumentInfo>(arguments.ToImmutable()),
            new EquatableArray<string>(unsupported.ToImmutable()),
            location is null ? null : LocationInfo.From(location)
        );
    }

    // The kind of a property's type an argument can be written in; null for the others.
    private static string? ArgumentKind(ITypeSymbol type) =>
        type.TypeKind == TypeKind.Enum
            ? "enum"
            : type.SpecialType switch
            {
                SpecialType.System_Int32 => "int",
                SpecialType.System_Int64 => "long",
                SpecialType.System_Single => "float",
                SpecialType.System_Double => "double",
                SpecialType.System_Boolean => "bool",
                SpecialType.System_String => "string",
                _ => null,
            };

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

        return new AssemblyInfo(
            Identifier(compilation.AssemblyName ?? "Assembly"),
            isApp,
            new EquatableArray<TableInfo>(tables.ToImmutable()),
            new EquatableArray<DefaultsInfo>(isApp ? ReadDefaults(compilation) : ImmutableArray<DefaultsInfo>.Empty)
        );
    }

    // The classes that add default key bindings, of the assemblies the app references and then of the app itself, an assembly
    // after the assemblies it references: the rows of a package that builds on another are added later, and win at the same depth.
    // Among assemblies that do not reference each other, the order is that of their names.
    private static ImmutableArray<DefaultsInfo> ReadDefaults(Compilation compilation)
    {
        var found = new List<(IAssemblySymbol Assembly, DefaultsInfo Info)>();
        foreach (IAssemblySymbol reference in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            AddDefaults(found, reference, compilation.Assembly);
        }

        var ordered = ImmutableArray.CreateBuilder<DefaultsInfo>();
        var remaining = found
            .OrderBy(static f => f.Assembly.Name, StringComparer.Ordinal)
            .ThenBy(static f => f.Info.TypeName, StringComparer.Ordinal)
            .ToList();
        var reach = new Dictionary<IAssemblySymbol, HashSet<IAssemblySymbol>>(SymbolEqualityComparer.Default);
        while (remaining.Count > 0)
        {
            int next = 0;
            for (int i = 0; i < remaining.Count; i++)
            {
                IAssemblySymbol candidate = remaining[i].Assembly;
                if (
                    !remaining.Any(other =>
                        !SymbolEqualityComparer.Default.Equals(other.Assembly, candidate) && Reaches(candidate, other.Assembly, reach)
                    )
                )
                {
                    next = i;
                    break;
                }
            }

            ordered.Add(remaining[next].Info);
            remaining.RemoveAt(next);
        }

        var own = new List<(IAssemblySymbol Assembly, DefaultsInfo Info)>();
        AddDefaults(own, compilation.Assembly, compilation.Assembly);
        ordered.AddRange(own.Select(static o => o.Info));
        return ordered.ToImmutable();
    }

    private static void AddDefaults(List<(IAssemblySymbol Assembly, DefaultsInfo Info)> found, IAssemblySymbol assembly, IAssemblySymbol app)
    {
        foreach (AttributeData attribute in assembly.GetAttributes())
        {
            if (
                attribute.AttributeClass?.ToDisplayString() != DefaultKeyBindingsAttributeName
                || attribute.ConstructorArguments.Length != 1
                || attribute.ConstructorArguments[0].Value is not INamedTypeSymbol type
            )
            {
                continue;
            }

            // The method the module initializer names: static, with one parameter, and one the app can call.
            bool callable =
                IsAccessible(type, assembly, app)
                && type.GetMembers(AddDefaultsName)
                    .OfType<IMethodSymbol>()
                    .Any(m =>
                        m.IsStatic
                        && m.ReturnsVoid
                        && m.Parameters.Length == 1
                        && m.Parameters[0].Type.ToDisplayString() == "XylemUI.Keymap"
                        && (
                            m.DeclaredAccessibility == Accessibility.Public
                            || (SymbolEqualityComparer.Default.Equals(assembly, app) && m.DeclaredAccessibility == Accessibility.Internal)
                        )
                    );
            found.Add(
                (
                    assembly,
                    new DefaultsInfo(type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), type.ToDisplayString(), assembly.Name, callable)
                )
            );
        }
    }

    private static bool IsAccessible(INamedTypeSymbol type, IAssemblySymbol assembly, IAssemblySymbol app)
    {
        bool same = SymbolEqualityComparer.Default.Equals(assembly, app);
        for (INamedTypeSymbol? t = type; t is not null; t = t.ContainingType)
        {
            if (t.IsGenericType || !(t.DeclaredAccessibility == Accessibility.Public || (same && t.DeclaredAccessibility == Accessibility.Internal)))
            {
                return false;
            }
        }

        return true;
    }

    // Whether an assembly references another, directly or through the assemblies it references.
    private static bool Reaches(IAssemblySymbol from, IAssemblySymbol to, Dictionary<IAssemblySymbol, HashSet<IAssemblySymbol>> reach)
    {
        if (!reach.TryGetValue(from, out HashSet<IAssemblySymbol>? seen))
        {
            seen = new HashSet<IAssemblySymbol>(SymbolEqualityComparer.Default);
            var stack = new Stack<IAssemblySymbol>();
            stack.Push(from);
            while (stack.Count > 0)
            {
                foreach (IModuleSymbol module in stack.Pop().Modules)
                {
                    foreach (IAssemblySymbol reference in module.ReferencedAssemblySymbols)
                    {
                        if (seen.Add(reference))
                        {
                            stack.Push(reference);
                        }
                    }
                }
            }

            reach.Add(from, seen);
        }

        return seen.Contains(to);
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

            foreach (string property in action.Unsupported)
            {
                output.ReportDiagnostic(Diagnostic.Create(UnsupportedArgument, location, action.DisplayName, property));
            }

            byName.Add(action.Name, action.DisplayName);
            valid.Add(action);
        }

        foreach (DefaultsInfo defaults in assembly.Defaults)
        {
            if (!defaults.Callable)
            {
                output.ReportDiagnostic(Diagnostic.Create(NoAddDefaults, Location.None, defaults.DisplayName, defaults.Assembly));
            }
        }

        // A library with no action has no table. An app has one even with none of its own: it calls the tables it references and
        // registers their default key bindings.
        if (valid.Count == 0 && !(assembly.IsApp && (assembly.Tables.Count > 0 || assembly.Defaults.Count > 0)))
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
                    .Append(
                        action.Arguments.Count > 0
                            ? "(), Set" + valid.IndexOf(action).ToString(System.Globalization.CultureInfo.InvariantCulture) + ");\n"
                            : "());\n"
                    );
            }

            _ = text.Append("        }\n");
        }

        for (int i = 0; i < valid.Count; i++)
        {
            if (valid[i].Arguments.Count > 0)
            {
                WriteSetter(text, valid[i], i);
            }
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

            // After the actions: a row may name one. In the order ReadDefaults gave, which the rows' precedence follows.
            foreach (DefaultsInfo defaults in assembly.Defaults)
            {
                if (defaults.Callable)
                {
                    _ = text.Append("            global::XylemUI.Keymap.RegisterDefaults(").Append(defaults.TypeName).Append(".AddDefaults);\n");
                }
            }

            _ = text.Append("        }\n");
        }

        _ = text.Append("    }\n}\n");
        output.AddSource("XylemUI.Actions.g.cs", text.ToString());
    }

    // The method that sets an action's property from an argument's name and its text. The name is compared without case; a
    // value that is not of the property's type, and a name that is no property, give false.
    private static void WriteSetter(StringBuilder text, ActionInfo action, int index)
    {
        const string Span = "global::System.ReadOnlySpan<char>";
        const string Same = ", global::System.StringComparison.OrdinalIgnoreCase)";
        const string Culture = "global::System.Globalization.CultureInfo.InvariantCulture";
        _ = text.Append("\n        private static bool Set").Append(index).Append("(ref ").Append(action.TypeName);
        _ = text.Append(" action, ").Append(Span).Append(" name, ").Append(Span).Append(" value)\n        {\n");
        foreach (ArgumentInfo argument in action.Arguments)
        {
            _ = text.Append("            if (global::System.MemoryExtensions.Equals(name, ");
            _ = text.Append(SymbolDisplay.FormatLiteral(argument.Name, quote: true)).Append(Same).Append(")\n            {\n");
            string parsed = "parsed";
            switch (argument.Kind)
            {
                case "string":
                    parsed = "value.ToString()";
                    break;
                case "bool":
                    _ = text.Append(
                        "                if (!bool.TryParse(value, out bool parsed))\n                {\n                    return false;\n                }\n\n"
                    );
                    break;
                case "enum":
                    _ = text.Append("                ").Append(argument.TypeName).Append(" parsed;\n");
                    string keyword = "if";
                    foreach (string member in argument.Members)
                    {
                        _ = text.Append("                ").Append(keyword).Append(" (global::System.MemoryExtensions.Equals(value, ");
                        _ = text.Append(SymbolDisplay.FormatLiteral(member, quote: true)).Append(Same).Append(")\n                {\n");
                        _ = text.Append("                    parsed = ")
                            .Append(argument.TypeName)
                            .Append('.')
                            .Append(member)
                            .Append(";\n                }\n");
                        keyword = "else if";
                    }

                    _ = text.Append(
                        argument.Members.Count > 0
                            ? "                else\n                {\n                    return false;\n                }\n\n"
                            : "                return false;\n"
                    );
                    break;
                default:
                    string styles = argument.Kind is "int" or "long" ? "Integer" : "Float";
                    _ = text.Append("                if (!")
                        .Append(argument.Kind)
                        .Append(".TryParse(value, global::System.Globalization.NumberStyles.");
                    _ = text.Append(styles).Append(", ").Append(Culture).Append(", out ").Append(argument.Kind);
                    _ = text.Append(" parsed))\n                {\n                    return false;\n                }\n\n");
                    break;
            }

            if (argument.Kind == "enum" && argument.Members.Count == 0)
            {
                _ = text.Append("            }\n\n");
                continue;
            }

            _ = text.Append(
                argument.UseWith
                    ? "                action = action with { " + argument.Name + " = " + parsed + " };\n"
                    : "                action." + argument.Name + " = " + parsed + ";\n"
            );
            _ = text.Append("                return true;\n            }\n\n");
        }

        _ = text.Append("            return false;\n        }\n");
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
        EquatableArray<ArgumentInfo> Arguments,
        EquatableArray<string> Unsupported,
        LocationInfo? Location
    );

    private sealed record ArgumentInfo(string Name, string Kind, string TypeName, EquatableArray<string> Members, bool UseWith);

    private sealed record TableInfo(string TypeName, string Assembly, EquatableArray<string> Names);

    private sealed record DefaultsInfo(string TypeName, string DisplayName, string Assembly, bool Callable);

    private sealed record AssemblyInfo(string Identifier, bool IsApp, EquatableArray<TableInfo> Tables, EquatableArray<DefaultsInfo> Defaults);
}
