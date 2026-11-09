using System.Diagnostics;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using AssemblyDefinition = Mono.Cecil.AssemblyDefinition;
using DefaultAssemblyResolver = Mono.Cecil.DefaultAssemblyResolver;
using EmbeddedResource = Mono.Cecil.EmbeddedResource;
using ManifestResourceAttributes = Mono.Cecil.ManifestResourceAttributes;
using ReaderParameters = Mono.Cecil.ReaderParameters;
using Resource = Mono.Cecil.Resource;
using WriterParameters = Mono.Cecil.WriterParameters;

namespace XylemUI.Tools.FluentIcons;

/// <summary>
/// Keeps the Fluent glyphs an app uses and writes them into the app's assembly (design §5.0.4, "Keeping the glyphs an app uses",
/// "How they are embedded"; spike #716). It runs after the compiler wrote the assembly and before ILC reads it.
/// </summary>
internal static class Keeper
{
    /// <summary>
    /// The assembly and the type whose getters are the icons.
    /// </summary>
    public const string ThemeAssembly = "XylemUI.Theme.Fluent";

    private const string IconsNamespace = "XylemUI.Theme.Fluent";
    private const string IconsType = "FluentIcons";

    /// <summary>
    /// Returns the name of the resource that holds a style's kept font: the name Theme.Fluent looks for.
    /// </summary>
    /// <param name="style">The style.</param>
    /// <returns>The name.</returns>
    public static string ResourceName(string style) => $"XylemUI.Icons.{style}.ttf";

    /// <summary>
    /// Finds the icons, keeps their glyphs, and writes the fonts into the assembly unless the same fonts are there.
    /// </summary>
    /// <param name="assembly">The assembly the compiler wrote.</param>
    /// <param name="references">The assemblies it references.</param>
    /// <param name="directory">The Fluent directory.</param>
    /// <param name="keepAll">Whether to keep every glyph (Flutter's <c>--no-tree-shake-icons</c>), for an app that makes its glyphs
    /// in ways the IL does not show.</param>
    public static void Run(string assembly, IReadOnlyList<string> references, string directory, bool keepAll)
    {
        var watch = Stopwatch.StartNew();
        SortedSet<string> used = keepAll ? [] : Find(assembly, references);
        var fonts = new List<(string Name, byte[] Data)>();
        var counts = new List<string>();
        foreach (string style in Icons.Styles)
        {
            Icons icons = Icons.Read(directory, style);
            byte[] font = File.ReadAllBytes(Icons.FontPath(directory, style));
            if (keepAll)
            {
                fonts.Add((ResourceName(style), font));
                counts.Add($"{style} all ({font.Length} bytes)");
                continue;
            }

            var codePoints = used.Where(icons.ByProperty.ContainsKey).Select(property => icons.ByProperty[property].CodePoint).ToList();
            if (codePoints.Count == 0)
            {
                continue;
            }

            byte[] kept = Subset.Keep(font, codePoints);
            fonts.Add((ResourceName(style), kept));
            counts.Add($"{style} {codePoints.Count} ({kept.Length} bytes)");
        }

        bool written = Write(assembly, references, fonts);
        string what = counts.Count == 0 ? "no icon" : string.Join(", ", counts);
        Console.WriteLine(
            $"FluentIconsTool: {what}; {(written ? "written into" : "already in")} {Path.GetFileName(assembly)} ({watch.ElapsedMilliseconds} ms)"
        );
    }

    /// <summary>
    /// Returns the properties of <c>FluentIcons</c> whose getters the IL of an assembly or of the assemblies it references calls:
    /// each method body of each assembly that is the theme or references it is read, and each method token of a call is resolved.
    /// </summary>
    /// <param name="assembly">The app's assembly.</param>
    /// <param name="references">The assemblies it references.</param>
    /// <returns>The property names.</returns>
    public static SortedSet<string> Find(string assembly, IEnumerable<string> references)
    {
        var used = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string path in references.Prepend(assembly).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            using FileStream stream = File.OpenRead(path);
            using var pe = new PEReader(stream);
            if (!pe.HasMetadata)
            {
                continue;
            }

            MetadataReader metadata = pe.GetMetadataReader();
            bool isTheme = metadata.IsAssembly && metadata.GetString(metadata.GetAssemblyDefinition().Name) == ThemeAssembly;
            if (!isTheme && !metadata.AssemblyReferences.Any(r => metadata.GetString(metadata.GetAssemblyReference(r).Name) == ThemeAssembly))
            {
                continue;
            }

            foreach (MethodDefinitionHandle handle in metadata.MethodDefinitions)
            {
                MethodDefinition method = metadata.GetMethodDefinition(handle);
                if (method.RelativeVirtualAddress == 0)
                {
                    continue;
                }

                BlobReader il = pe.GetMethodBody(method.RelativeVirtualAddress).GetILReader();
                while (il.RemainingBytes > 0)
                {
                    OpCode op = OpCodeTable.Read(ref il);
                    if (op.OperandType == OperandType.InlineMethod)
                    {
                        if (Getter(metadata, MetadataTokens.EntityHandle(il.ReadInt32())) is { } property)
                        {
                            _ = used.Add(property);
                        }
                    }
                    else
                    {
                        OpCodeTable.SkipOperand(ref il, op);
                    }
                }
            }
        }

        return used;
    }

    // The property of FluentIcons whose getter a method token names, or null.
    private static string? Getter(MetadataReader metadata, EntityHandle token)
    {
        StringHandle name;
        EntityHandle parent;
        if (token.Kind == HandleKind.MemberReference)
        {
            MemberReference member = metadata.GetMemberReference((MemberReferenceHandle)token);
            name = member.Name;
            parent = member.Parent;
        }
        else if (token.Kind == HandleKind.MethodDefinition)
        {
            MethodDefinition method = metadata.GetMethodDefinition((MethodDefinitionHandle)token);
            name = method.Name;
            parent = method.GetDeclaringType();
        }
        else
        {
            return null;
        }

        string methodName = metadata.GetString(name);
        if (!methodName.StartsWith("get_", StringComparison.Ordinal))
        {
            return null;
        }

        (string ns, string type) = parent.Kind switch
        {
            HandleKind.TypeReference => (
                metadata.GetString(metadata.GetTypeReference((TypeReferenceHandle)parent).Namespace),
                metadata.GetString(metadata.GetTypeReference((TypeReferenceHandle)parent).Name)
            ),
            HandleKind.TypeDefinition => (
                metadata.GetString(metadata.GetTypeDefinition((TypeDefinitionHandle)parent).Namespace),
                metadata.GetString(metadata.GetTypeDefinition((TypeDefinitionHandle)parent).Name)
            ),
            _ => (string.Empty, string.Empty),
        };
        return ns == IconsNamespace && type == IconsType ? methodName[4..] : null;
    }

    // Writes the fonts as resources, replacing ones of the same name that differ; returns whether the assembly was written. A
    // second build, where the compiler did not run again, finds the same fonts and leaves the assembly as it is.
    // Writing the assembly again resolves the types its metadata names (an attribute's enum argument), so Cecil looks for the
    // assemblies it references where the compiler found them.
    private static bool Write(string assembly, IReadOnlyList<string> references, List<(string Name, byte[] Data)> fonts)
    {
        using var resolver = new DefaultAssemblyResolver();
        foreach (string? directory in references.Select(Path.GetDirectoryName).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (directory is not null)
            {
                resolver.AddSearchDirectory(directory);
            }
        }

        var parameters = new ReaderParameters
        {
            ReadWrite = true,
            ReadSymbols = File.Exists(Path.ChangeExtension(assembly, ".pdb")),
            AssemblyResolver = resolver,
        };
        using AssemblyDefinition definition = AssemblyDefinition.ReadAssembly(assembly, parameters);
        Mono.Collections.Generic.Collection<Resource> resources = definition.MainModule.Resources;
        bool changed = false;
        foreach (string style in Icons.Styles)
        {
            string name = ResourceName(style);
            EmbeddedResource? old = resources.OfType<EmbeddedResource>().FirstOrDefault(r => r.Name == name);
            byte[]? data = fonts.FirstOrDefault(f => f.Name == name).Data;
            if (old is not null && data is not null && old.GetResourceData().AsSpan().SequenceEqual(data))
            {
                continue;
            }

            if (old is not null)
            {
                _ = resources.Remove(old);
                changed = true;
            }

            if (data is not null)
            {
                resources.Add(new EmbeddedResource(name, ManifestResourceAttributes.Public, data));
                changed = true;
            }
        }

        if (changed)
        {
            definition.Write(new WriterParameters { WriteSymbols = parameters.ReadSymbols });
        }

        return changed;
    }
}
