using NodeHandle = XylemUI.Handle<XylemUI.UiNode>;

namespace XylemUI.Text.Tests;

// The fonts an app registers from bytes (design §4.5, "App fonts", row IC1). The test fonts are the bundled fallback font renamed
// (Fonts/README.md): "XylemUI Test App" at weights 400 and 700, ASCII only. A registered font stays for the process, so the tests
// register the same files again; a second registration of a file adds equal faces, which the matching ranks after the first.
// "n" is 618 units wide in them and 610 in the test font, of 1000 units per em.
[Collection(UiThreadGroup.Name)]
public sealed class AppFontsTests(UiThreadFixture ui)
{
    private const string Family = "XylemUI Test App";
    private const float Size = 20;

    private static readonly byte[] Regular = Read("XylemUITestApp-Regular.ttf");
    private static readonly byte[] Bold = Read("XylemUITestApp-Bold.ttf");

    // Each registered by one test alone, which measures a text in its family before registering it.
    private static readonly byte[] Gabriola = Read("XylemUITestGabriola.ttf");
    private static readonly byte[] Late = Read("XylemUITestLate.ttf");

    /// <summary>
    /// A registered file's family, weight, slant, and stretch are read from its tables, and the face nearest the weight asked for is
    /// found, by CSS's order.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The regular (400) and bold (700) test app fonts are registered.
    ///
    /// Steps:
    /// - Find the family, in other case, at 400, 700, 600, 300, and 500.
    ///
    /// Expected:
    /// - Each face is "XylemUI Test App", upright, of normal stretch; 400 and 300 find the regular face, 700 and 600 the bold one
    ///   (above 500 the heavier first), and 500 the regular one (from 400 to 500, the lighter after the weights up to 500).
    /// </remarks>
    [Fact]
    public void Add_TwoWeights_FindsTheNearest() =>
        ui.Invoke(() =>
        {
            AppFonts.Add(Regular);
            AppFonts.Add(Bold);

            AppFontFace? regular = AppFontSet.Find("xylemui test app", FontWeight.Normal, FontSlant.Normal, FontStretch.Normal);
            Assert.NotNull(regular);
            Assert.Equal(Family, regular.Family);
            Assert.Equal(400, regular.Weight.Value);
            Assert.Equal(FontSlant.Normal, regular.Slant);
            Assert.Equal(FontStretch.Normal, regular.Stretch);
            Assert.Equal(700, Weight(700));
            Assert.Equal(700, Weight(600));
            Assert.Equal(400, Weight(300));
            Assert.Equal(400, Weight(500));
            Assert.True(AppFontSet.HasFamily(Family));
            Assert.False(AppFontSet.HasFamily("XylemUI No Such Family"));
        });

    /// <summary>
    /// Every face of a font collection (a <c>.ttc</c> file) is registered, each over the same bytes.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The test collection: two faces of "XylemUI Test Collection", at 400 and 700.
    ///
    /// Steps:
    /// - Register it once.
    ///
    /// Expected:
    /// - One file more, two faces more; finding the family at 400 and 700 gives face 0 and face 1 of that file.
    /// </remarks>
    [Fact]
    public void Add_Collection_RegistersEachFace() =>
        ui.Invoke(() =>
        {
            int files = AppFontSet.Generation;
            int faces = AppFontSet.Faces.Count;

            AppFonts.Add(Read("XylemUITestCollection.ttc"));

            Assert.Equal(files + 1, AppFontSet.Generation);
            Assert.Equal(faces + 2, AppFontSet.Faces.Count);
            AppFontFace regular = AppFontSet.Find("XylemUI Test Collection", FontWeight.Normal, FontSlant.Normal, FontStretch.Normal)!;
            AppFontFace bold = AppFontSet.Find("XylemUI Test Collection", FontWeight.Bold, FontSlant.Normal, FontStretch.Normal)!;
            Assert.Equal((0, 400), (regular.Font.FaceIndex, regular.Weight.Value));
            Assert.Equal((1, 700), (bold.Font.FaceIndex, bold.Weight.Value));
            Assert.Same(regular.File, bold.File);
        });

    /// <summary>
    /// Bytes that are not a font file are refused, and nothing is registered.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - None.
    ///
    /// Steps:
    /// - Register 16 bytes of text, then an empty array.
    ///
    /// Expected:
    /// - Each throws <see cref="ArgumentException"/>, and the number of files registered does not change.
    /// </remarks>
    [Fact]
    public void Add_BytesNotAFont_Throws() =>
        ui.Invoke(() =>
        {
            int before = AppFontSet.Generation;

            _ = Assert.Throws<ArgumentException>(() => AppFonts.Add("not a font file!"u8.ToArray()));
            _ = Assert.Throws<ArgumentException>(() => AppFonts.Add([]));
            Assert.Equal(before, AppFontSet.Generation);
        });

    /// <summary>
    /// Registering checks the UI thread, as every public entry does (design §2.12).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The tests' UI thread is recorded.
    ///
    /// Steps:
    /// - Register the regular test app font from a thread of the pool.
    ///
    /// Expected:
    /// - It throws <see cref="InvalidOperationException"/>, and the number of files registered does not change.
    /// </remarks>
    [Fact]
    public void Add_OffTheUiThread_Throws()
    {
        ui.Invoke(() => { });
        int before = AppFontSet.Generation;

        // The test calls the font registration off the UI thread on purpose, which is what XYLEM100 reports.
#pragma warning disable XYLEM100
        _ = Assert.Throws<InvalidOperationException>(() =>
            Task.Run(() => AppFonts.Add(Regular), TestContext.Current.CancellationToken).GetAwaiter().GetResult()
        );
#pragma warning restore XYLEM100
        Assert.Equal(before, AppFontSet.Generation);
    }

    /// <summary>
    /// In the test mode, a registered family is drawn with the registered font, and the characters it lacks with the test font.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The regular test app font is registered; a test font provider.
    ///
    /// Steps:
    /// - Split "Ab日本cd" asked for in "XylemUI Test App", and "Ab" asked for in no family.
    ///
    /// Expected:
    /// - Three runs: "Ab" in the registered font, "日本" in the test font, "cd" in the registered font. With no family, one run in
    ///   the test font.
    /// </remarks>
    [Fact]
    public void TestFontProvider_RegisteredFamily_TakesItsFontWhereItHasTheCharacters() =>
        ui.Invoke(() =>
        {
            AppFonts.Add(Regular);
            Font app = AppFontSet.Find(Family, FontWeight.Normal, FontSlant.Normal, FontStretch.Normal)!.Font;
            using var provider = new TestFontProvider();
            var request = new FontRequest(Family, FontWeight.Normal, FontSlant.Normal, FontStretch.Normal);

            FontMatch first = provider.ResolveFont("Ab日本cd", 0, TestFont.Japanese, request);
            FontMatch second = provider.ResolveFont("Ab日本cd", 2, TestFont.Japanese, request);
            FontMatch third = provider.ResolveFont("Ab日本cd", 4, TestFont.Japanese, request);
            FontMatch plain = provider.ResolveFont("Ab", 0, TestFont.Japanese, FontRequest.Default);

            Assert.Same(app, first.Font);
            Assert.Equal(2, first.Length);
            Assert.NotSame(app, second.Font);
            Assert.Equal(2, second.Length);
            Assert.Same(app, third.Font);
            Assert.Equal(2, third.Length);
            Assert.NotSame(app, plain.Font);
            Assert.Equal(2, plain.Length);
        });

    /// <summary>
    /// A text leaf measured before its family was registered is measured again with the registered font after it is, without a
    /// change of its own.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A test font provider; a tree whose text leaf "nn" asks for "Gabriola" at 20 px, laid out once. No test of this process
    ///   registers that family before, so the leaf is measured with the test font: 24.4 wide (2 × 610 / 1000 × 20).
    ///
    /// Steps:
    /// - Register the test font named "Gabriola"; run the layout pass.
    ///
    /// Expected:
    /// - Registering marks the leaf's layout and asks the tree for a frame; after the pass, the leaf is 24.72 wide
    ///   (2 × 618 / 1000 × 20), the registered font's width.
    /// </remarks>
    [Fact]
    public void Add_TextMeasuredBefore_IsMeasuredAgainWithTheRegisteredFont() =>
        ui.Invoke(() =>
        {
            using var context = new TextContext(new TestFontProvider());
            var tree = new UiTree(context);
            int frames = 0;
            tree.FrameNeeded = () => frames++;
            NodeHandle root = tree.CreateNode(new LayoutStyle { Algorithm = LayoutAlgorithm.Flex.Row });
            var font = new FontRequest("Gabriola", FontWeight.Normal, FontSlant.Normal, FontStretch.Normal);
            NodeHandle leaf = tree.CreateTextNode(new LayoutStyle(), new TextStyle(font, Size, TestFont.Japanese, float.NaN));
            tree.SetText(leaf, "nn");
            tree.AppendChild(root, leaf);
            Invalidation.NotifyWindowResized(tree, root, new Size(400, 100));
            LayoutEngine.RunLayoutPass(tree);
            tree.ClearFrameRequest();
            Assert.Equal(24.4f, tree.GetRect(leaf).Width, 3);

            AppFonts.Add(Gabriola);

            DirtyState dirty = tree.Nodes.GetRef(leaf).Dirty;
            Assert.NotEqual(DirtyFlags.None, dirty.SelfDirty & DirtyFlags.Layout);
            Assert.True(frames > 0);
            LayoutEngine.RunLayoutPass(tree);
            Assert.Equal(24.72f, tree.GetRect(leaf).Width, 3);
        });

    /// <summary>
    /// A draw function's text, which no mark reaches, is shaped again with a font registered since it was laid out.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A test font provider; a text layout of "nn" asking for "XylemUI Test Late" at 20 px, laid out once. No test of this
    ///   process registers that family before, so the text is laid out with the test font: 24.4 wide.
    ///
    /// Steps:
    /// - Register the test font "XylemUI Test Late"; lay the text out again at the same width.
    ///
    /// Expected:
    /// - The second layout is 24.72 wide, the registered font's width.
    /// </remarks>
    [Fact]
    public void Add_DrawFunctionText_IsShapedAgainAtItsNextLayout() =>
        ui.Invoke(() =>
        {
            using var context = new TextContext(new TestFontProvider());
            var font = new FontRequest("XylemUI Test Late", FontWeight.Normal, FontSlant.Normal, FontStretch.Normal);
            var layout = new TextLayout("nn", new TextStyle(font, Size, TestFont.Japanese, float.NaN));
            Assert.Equal(24.4f, LayoutEngine.LayOutText(context, layout.Leaf, 400).Width, 3);

            AppFonts.Add(Late);

            Assert.Equal(24.72f, LayoutEngine.LayOutText(context, layout.Leaf, 400).Width, 3);
        });

    private static int Weight(int asked) => AppFontSet.Find(Family, new FontWeight(asked), FontSlant.Normal, FontStretch.Normal)!.Weight.Value;

    private static byte[] Read(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fonts", name));
}
