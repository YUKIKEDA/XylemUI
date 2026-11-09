using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using XylemUI.Primitives;
using XylemUI.Testing.D3D11;
using XylemUI.Testing.XUnit;
using XylemUI.Text;
using static XylemUI.Elements;
using static XylemUI.Primitives.Primitive;

namespace XylemUI.Theme.Fluent.Tests;

// The Fluent icons (design §5.0.4, "Icons", row IC1): the glyphs this project's IL reads are kept by XylemUI.Theme.Fluent.targets and
// written into this assembly, registered at the first glyph read, drawn as text of their size in the color of the text, and left out
// of the accessibility tree unless labeled. The fonts are Fluent UI System Icons 1.1.344 (vendor/versions.lock): 500 units per em,
// an ascent of 500 and a descent of 0, so a glyph's line is as high as its size; "add_24_regular" is U+F10A, a plus whose strokes
// cross at the middle of the em.
public sealed class FluentIconsTests
{
    private const int HomeRegular24 = 0xF481;
    private static readonly Color Red = Color.FromRgb(0xFF0000);

    /// <summary>
    /// The build keeps the glyphs this assembly reads, and not the others.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The tests' UI thread, where a glyph is read. This assembly reads <c>FluentIcons.Add24Regular</c> and
    ///   <c>FluentIcons.Delete24Filled</c>, and never reads home_24_regular.
    ///
    /// Steps:
    /// - Open the two fonts kept in this assembly.
    ///
    /// Expected:
    /// - The Regular font has the glyph of Add24Regular and not that of home_24_regular (U+F481); the Filled font has the glyph of
    ///   Delete24Filled; each font is far smaller than the whole one (under 20 KB, of 2.8 and 2.5 MB).
    /// </remarks>
    [UiFact]
    public void KeptFonts_HoldTheGlyphsThisAssemblyReads()
    {
        using var regular = new Font(Resource("XylemUI.Icons.Regular.ttf"));
        using var filled = new Font(Resource("XylemUI.Icons.Filled.ttf"));

        Assert.True(regular.HasGlyph(FluentIcons.Add24Regular.CodePoint));
        Assert.False(regular.HasGlyph(HomeRegular24));
        Assert.True(filled.HasGlyph(FluentIcons.Delete24Filled.CodePoint));
        Assert.InRange(regular.Data.Length, 1, 20_000);
        Assert.InRange(filled.Data.Length, 1, 20_000);
    }

    /// <summary>
    /// The first glyph read registers its font, under the family the glyph names, with the code point and size of its name.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The tests' UI thread.
    ///
    /// Steps:
    /// - Read <c>FluentIcons.Add24Regular</c> and <c>FluentIcons.Delete24Filled</c>.
    ///
    /// Expected:
    /// - Their families are "FluentSystemIcons-Regular" and "FluentSystemIcons-Filled", both registered; Add24Regular is U+F10A at
    ///   24.
    /// </remarks>
    [UiFact]
    public void FirstGlyph_RegistersItsFont()
    {
        IconGlyph add = FluentIcons.Add24Regular;
        IconGlyph delete = FluentIcons.Delete24Filled;

        Assert.Equal(("FluentSystemIcons-Regular", 0xF10A, 24f), (add.Family, add.CodePoint, add.Size));
        Assert.Equal("FluentSystemIcons-Filled", delete.Family);
        Assert.True(AppFontSet.HasFamily(add.Family));
        Assert.True(AppFontSet.HasFamily(delete.Family));
    }

    /// <summary>
    /// Registering checks the UI thread, and says how to keep the glyphs when no loaded assembly holds a font.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The tests' UI thread is recorded.
    ///
    /// Steps:
    /// - Register the Regular font from a thread of the pool; register a style no assembly holds ("Light") on the UI thread.
    ///
    /// Expected:
    /// - Both throw <see cref="InvalidOperationException"/>; the second names XylemUI.Theme.Fluent.targets.
    /// </remarks>
    [UiFact]
    public void Register_OffTheUiThreadOrWithNoFont_Throws()
    {
        _ = Assert.Throws<InvalidOperationException>(() =>
            Task.Run(() => FluentIconFonts.Register("Regular"), TestContext.Current.CancellationToken).GetAwaiter().GetResult()
        );
        InvalidOperationException missing = Assert.Throws<InvalidOperationException>(() => FluentIconFonts.Register("Light"));
        Assert.Contains("XylemUI.Theme.Fluent.targets", missing.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// An icon is a square of the size its glyph was drawn for unless given one, and draws in the color it is given.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A headless app of 200 x 100 on WARP: a column of a row of the icon Add24Regular, and a row of the same icon at 48 in red
    ///   (each in a row of its own, so that neither is stretched to the other's height).
    ///
    /// Steps:
    /// - Read the two icons' boxes; read the pixel at the middle of the second.
    ///
    /// Expected:
    /// - 24 x 24 and 48 x 48: a line as high as the size, not the font's line with its gap; the pixel where the plus's strokes
    ///   cross is red.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Icon_TakesItsGlyphsSize_AndItsColor()
    {
        await using HeadlessApp app = await HeadlessApp.StartAsync(
            () => Column(Row(Icon(FluentIcons.Add24Regular).TestId("small")), Row(Icon(FluentIcons.Add24Regular, 48, Red).TestId("large"))),
            new HeadlessOptions
            {
                Width = 200,
                Height = 100,
                Renderer = () => new WarpRenderer(),
            }
        );
        _ = app.PumpAndSettle();
        Rect small = app.Tree.GetRect(app.Find("small").Node);
        Rect large = app.Tree.GetRect(app.Find("large").Node);

        Assert.Equal((24f, 24f), (small.Width, small.Height));
        Assert.Equal((48f, 48f), (large.Width, large.Height));
        Color[] frame = app.Capture(out int width, out _);
        (int x, int y) = app.CenterOf(app.Find("large"));
        Assert.Equal(Red, frame[(y * width) + x]);
    }

    /// <summary>
    /// An icon with no label is left out of the name of the button it is in; one with a label is an image of that name.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A column of a button holding a row of the icon Add24Regular and the text "Add", and the icon Delete24Filled labeled
    ///   "Delete".
    ///
    /// Steps:
    /// - Read the buttons and the images of the accessibility tree.
    ///
    /// Expected:
    /// - One button, named "Add"; one image, named "Delete".
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Icon_WithoutLabelIsExcluded_WithLabelIsAnImage()
    {
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            Column(Button(Row(Icon(FluentIcons.Add24Regular), Elements.Text("Add")), () => { }), Icon(FluentIcons.Delete24Filled, label: "Delete"))
        );
        _ = app.PumpAndSettle();

        Assert.Equal(["Add"], app.FindByRole(SemanticsRole.Button).Select(b => app.AccessibilityOf(b).Name));
        Assert.Equal(["Delete"], app.FindByRole(SemanticsRole.Image).Select(i => app.AccessibilityOf(i).Name));
    }

    /// <summary>
    /// The tool keeps every glyph when asked to, and a second run over the same assembly writes nothing.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A copy of this assembly, which holds the kept fonts; the tool this project's build ran.
    ///
    /// Steps:
    /// - Run the tool on the copy with keep all; run it again.
    ///
    /// Expected:
    /// - After the first run the copy holds the whole fonts (as long as the files of vendor/out/fluent); the first run says it wrote
    ///   them, the second that they were already there.
    /// </remarks>
    [Fact]
    public void Tool_KeepAll_WritesTheWholeFonts_Once()
    {
        string root = RepositoryRoot();
        string fluent = Path.Combine(root, "vendor", "out", "fluent");
        string directory = Directory.CreateTempSubdirectory("xylemui-icons-").FullName;
        try
        {
            string copy = Path.Combine(directory, "Copy.dll");
            File.Copy(typeof(FluentIconsTests).Assembly.Location, copy);
            string references = Path.Combine(directory, "references.txt");
            File.WriteAllText(references, string.Empty);

            string first = RunTool(root, "keep", copy, references, fluent, "true");
            string second = RunTool(root, "keep", copy, references, fluent, "true");

            Assert.Contains("written into", first, StringComparison.Ordinal);
            Assert.Contains("already in", second, StringComparison.Ordinal);
            Assert.Equal(
                new FileInfo(Path.Combine(fluent, "FluentSystemIcons-Regular.ttf")).Length,
                ResourceLength(copy, "XylemUI.Icons.Regular.ttf")
            );
            Assert.Equal(new FileInfo(Path.Combine(fluent, "FluentSystemIcons-Filled.ttf")).Length, ResourceLength(copy, "XylemUI.Icons.Filled.ttf"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static byte[] Resource(string name)
    {
        using Stream stream = typeof(FluentIconsTests).Assembly.GetManifestResourceStream(name)!;
        byte[] data = new byte[stream.Length];
        stream.ReadExactly(data);
        return data;
    }

    // The length of an embedded resource of an assembly file, read from its metadata without loading it (ECMA-335, II.24.2.4).
    private static unsafe long ResourceLength(string path, string name)
    {
        using FileStream stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        MetadataReader metadata = pe.GetMetadataReader();
        foreach (ManifestResourceHandle handle in metadata.ManifestResources)
        {
            ManifestResource resource = metadata.GetManifestResource(handle);
            if (metadata.GetString(resource.Name) == name)
            {
                PEMemoryBlock block = pe.GetSectionData(pe.PEHeaders.CorHeader!.ResourcesDirectory.RelativeVirtualAddress + (int)resource.Offset);
                return *(uint*)block.Pointer;
            }
        }

        return -1;
    }

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? at = new(AppContext.BaseDirectory); at is not null; at = at.Parent)
        {
            if (File.Exists(Path.Combine(at.FullName, "XylemUI.slnx")))
            {
                return at.FullName;
            }
        }

        throw new InvalidOperationException("The tests run outside the repository.");
    }

    private static string RunTool(string root, params string[] arguments)
    {
#if DEBUG
        const string Configuration = "Debug";
#else
        const string Configuration = "Release";
#endif
        string tool = Path.Combine(root, "tools", "FluentIconsTool", "bin", Configuration, "net10.0", "FluentIconsTool.dll");
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("exec");
        start.ArgumentList.Add(tool);
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start)!;
        string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, output);
        return output;
    }
}
