namespace XylemUI.Text.Tests;

// The new glyphs of a frame rasterized together, on worker threads from ParallelThreshold new glyphs on (design §5.0.3, "Glyphs
// seen for the first time", roadmap row T5), with the test rasterizer. The same with DirectWrite is in
// tests/XylemUI.Platform.Windows.Tests/ParallelGlyphTests.cs.
public sealed class GlyphAtlasManyTests
{
    private const uint Ink = 0xFF202020;

    /// <summary>
    /// Four threads put the glyphs on the same pages, in the same regions, as one thread.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The test font and two atlases made with a function that creates test rasterizers: one with 1 thread, one with 4. Glyphs 40
    ///   and 41 are color glyphs (41 with a layer in the text's color); glyph 50 is 300 px square, larger than a plot.
    ///
    /// Steps:
    /// - Ask each atlas, with one GetMany, for 300 glyphs: glyphs 2 to 61 at 4 subpixel steps, and in Gray and ClearType, and the
    ///   space, in the order of a line.
    /// - Ask for them again in the next frame.
    ///
    /// Expected:
    /// - The regions, the found flags, and every page of every kind are the same for the two atlases; the space is found with no
    ///   ink, and glyph 50 is not found; the 4-thread atlas started its workers.
    /// - The second time, only the 4 keys of glyph 50 are measured again (a refusal lasts one frame; RasterizeCount counts them), and
    ///   the results are the same.
    /// </remarks>
    [Fact]
    public void GetMany_FourThreads_SamePagesAndRegionsAsOne()
    {
        using Font font = TestFont.Open();
        GlyphRequest[] requests = Requests(font);
        using var serial = new GlyphAtlas(() => NewRasterizer(), threads: 1);
        var made = new List<TestRasterizer>();
        using var parallel = new GlyphAtlas(() => NewRasterizer(made), threads: 4);

        (AtlasGlyph[] Glyphs, bool[] Found) one = GetMany(serial, requests);
        (AtlasGlyph[] Glyphs, bool[] Found) four = GetMany(parallel, requests);

        Assert.NotNull(parallel.RunningWorkers);
        Assert.Equal(one.Found, four.Found);
        Assert.Equal(one.Glyphs, four.Glyphs);
        int space = Array.FindIndex(requests, r => r.Key.GlyphId == TestRasterizer.Space);
        Assert.True(one.Found[space] && one.Glyphs[space].Width == 0);
        Assert.False(one.Found[Array.FindIndex(requests, r => r.Key.GlyphId == 50)]);
        AssertSamePages(serial, parallel);

        parallel.BeginFrame();
        int rasterized = parallel.RasterizeCount;
        made.ForEach(rasterizer => rasterizer.Measured.Clear());
        (AtlasGlyph[] Glyphs, bool[] Found) again = GetMany(parallel, requests);
        Assert.Equal(rasterized + 4, parallel.RasterizeCount);
        Assert.Equal(4, made.Sum(rasterizer => rasterizer.Measured.Count));
        Assert.All(made.SelectMany(rasterizer => rasterizer.Measured), key => Assert.Equal(50u, key.GlyphId));
        Assert.Equal(four.Glyphs, again.Glyphs);
        Assert.Equal(four.Found, again.Found);
    }

    /// <summary>
    /// Fewer new glyphs than the threshold are rasterized on the calling thread; from the threshold on, on the workers.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The test font and an atlas made with a function that creates test rasterizers, with 4 threads; glyph 2 is in it already.
    ///
    /// Steps:
    /// - Ask with one GetMany for glyph 2 and <paramref name="count"/> new glyphs from glyph 3 on.
    ///
    /// Expected:
    /// - 1 new glyph (below <see cref="GlyphAtlas.ParallelThreshold"/>, 2): measured on the calling thread, and no workers.
    /// - 2 and 4 new glyphs: the workers started, and the glyphs were measured on as many threads, one each, the calling thread one of
    ///   them.
    /// </remarks>
    /// <param name="count">The number of new glyphs.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void GetMany_NewGlyphs_GoToTheWorkersFromTheThreshold(int count)
    {
        using Font font = TestFont.Open();
        var rasterizers = new List<TestRasterizer>();
        using var atlas = new GlyphAtlas(() => NewRasterizer(rasterizers), threads: 4);
        Assert.True(atlas.TryGet(new GlyphKey(font, 2, 24, 0), out _));
        GlyphRequest[] requests =
        [
            new(new GlyphKey(font, 2, 24, 0), Ink),
            .. Enumerable.Range(3, count).Select(g => new GlyphRequest(new GlyphKey(font, (uint)g, 24, 0), Ink)),
        ];
        rasterizers[0].MeasureThreads.Clear();

        _ = GetMany(atlas, requests);

        int[] threads = [.. rasterizers.SelectMany(r => r.MeasureThreads)];
        Assert.Equal(count, threads.Length);
        if (count < GlyphAtlas.ParallelThreshold)
        {
            Assert.Null(atlas.RunningWorkers);
            Assert.All(threads, thread => Assert.Equal(Environment.CurrentManagedThreadId, thread));
        }
        else
        {
            Assert.NotNull(atlas.RunningWorkers);
            Assert.Equal(count, threads.Distinct().Count());
            Assert.Contains(Environment.CurrentManagedThreadId, threads);
        }
    }

    /// <summary>
    /// An atlas made with one rasterizer rasterizes every new glyph on the calling thread.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The test font and an atlas made with one test rasterizer.
    ///
    /// Steps:
    /// - Ask with one GetMany for the 300 glyphs of <see cref="GetMany_FourThreads_SamePagesAndRegionsAsOne"/>.
    ///
    /// Expected:
    /// - No workers; every Measure on the calling thread.
    /// </remarks>
    [Fact]
    public void GetMany_OneRasterizer_StaysOnTheCallingThread()
    {
        using Font font = TestFont.Open();
        TestRasterizer rasterizer = NewRasterizer();
        using var atlas = new GlyphAtlas(rasterizer);

        _ = GetMany(atlas, Requests(font));

        Assert.Null(atlas.RunningWorkers);
        Assert.NotEmpty(rasterizer.MeasureThreads);
        Assert.All(rasterizer.MeasureThreads, thread => Assert.Equal(Environment.CurrentManagedThreadId, thread));
    }

    /// <summary>
    /// Dispose ends the worker threads and disposes the rasterizers the atlas created; an atlas made with one rasterizer leaves it
    /// to the caller.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The test font; an atlas made with a function that creates test rasterizers, with 4 threads, whose workers have started; an
    ///   atlas made with one test rasterizer.
    ///
    /// Steps:
    /// - Dispose both atlases, twice; then ask the first for a glyph.
    ///
    /// Expected:
    /// - The 3 worker threads are background threads, alive before and ended after; the 4 rasterizers the first atlas created are
    ///   disposed; the second atlas's rasterizer is not.
    /// - GetMany after Dispose throws <see cref="ObjectDisposedException"/>.
    /// </remarks>
    [Fact]
    public void Dispose_EndsTheWorkersAndDisposesTheCreatedRasterizers()
    {
        using Font font = TestFont.Open();
        var rasterizers = new List<TestRasterizer>();
        var atlas = new GlyphAtlas(() => NewRasterizer(rasterizers), threads: 4);
        _ = GetMany(atlas, Requests(font));
        GlyphWorkers workers = Assert.IsType<GlyphWorkers>(atlas.RunningWorkers);
        Assert.Equal(3, workers.Threads.Count);
        Assert.All(workers.Threads, thread => Assert.True(thread.IsAlive && thread.IsBackground));
        TestRasterizer own = NewRasterizer();
        var single = new GlyphAtlas(own);

        atlas.Dispose();
        atlas.Dispose();
        single.Dispose();
        single.Dispose();

        Assert.All(workers.Threads, thread => Assert.False(thread.IsAlive));
        Assert.Equal(4, rasterizers.Count);
        Assert.All(rasterizers, rasterizer => Assert.True(rasterizer.IsDisposed));
        Assert.False(own.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => GetMany(atlas, [new(new GlyphKey(font, 2, 24, 0), Ink)]));
    }

    /// <summary>
    /// When the worker threads cannot be started, the glyphs are rasterized on the calling thread, and the start is not tried again.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The test font and an atlas made with a function that creates test rasterizers, with 4 threads, whose worker start fails
    ///   (returns no workers, as when <see cref="Thread.Start()"/> throws <see cref="ThreadStartException"/> or
    ///   <see cref="OutOfMemoryException"/>).
    ///
    /// Steps:
    /// - Ask with one GetMany for the 300 glyphs of <see cref="GetMany_FourThreads_SamePagesAndRegionsAsOne"/>, and compare with an
    ///   atlas with 1 thread; in the next frame, ask for 4 new glyphs; dispose the atlas.
    ///
    /// Expected:
    /// - The same regions and pages as the atlas with 1 thread.
    /// - No workers; the start tried once, in the first frame; every Measure on the calling thread; after Dispose, the 4
    ///   rasterizers it created are disposed.
    /// </remarks>
    [Fact]
    public void GetMany_WorkersCannotStart_RasterizesOnTheCallingThread()
    {
        using Font font = TestFont.Open();
        GlyphRequest[] requests = Requests(font);
        var rasterizers = new List<TestRasterizer>();
        int starts = 0;
        var atlas = new GlyphAtlas(() => NewRasterizer(rasterizers), threads: 4)
        {
            StartWorkers = _ =>
            {
                starts++;
                return null;
            },
        };
        using var serial = new GlyphAtlas(() => NewRasterizer(), threads: 1);

        (AtlasGlyph[] Glyphs, bool[] Found) failed = GetMany(atlas, requests);
        Assert.Equal(GetMany(serial, requests).Glyphs, failed.Glyphs);
        AssertSamePages(serial, atlas);
        atlas.BeginFrame();
        _ = GetMany(atlas, [.. Enumerable.Range(130, 4).Select(g => new GlyphRequest(new GlyphKey(font, (uint)g, 24, 0), Ink))]);
        atlas.Dispose();

        Assert.Null(atlas.RunningWorkers);
        Assert.Equal(1, starts);
        Assert.All(rasterizers.SelectMany(r => r.MeasureThreads), thread => Assert.Equal(Environment.CurrentManagedThreadId, thread));
        Assert.Equal(4, rasterizers.Count);
        Assert.All(rasterizers, rasterizer => Assert.True(rasterizer.IsDisposed));
    }

    /// <summary>
    /// When a rasterizer for a worker cannot be made, the glyphs are rasterized on the calling thread, the rasterizers made for the
    /// workers are disposed, and they are not made again.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The test font and an atlas made with a function that creates test rasterizers, with 4 threads, that throws
    ///   <see cref="InvalidOperationException"/> on its third call (the second worker's rasterizer), as DirectWrite's factory or
    ///   loader can.
    ///
    /// Steps:
    /// - Ask with one GetMany for the 300 glyphs of <see cref="GetMany_FourThreads_SamePagesAndRegionsAsOne"/>, and compare with an
    ///   atlas with 1 thread; in the next frame, ask for 4 new glyphs; dispose the atlas.
    ///
    /// Expected:
    /// - GetMany does not throw; the same regions and pages as the atlas with 1 thread; no workers; every Measure on the calling
    ///   thread.
    /// - The function was called 3 times; the first worker's rasterizer was disposed in the first GetMany, the atlas's own one at
    ///   Dispose.
    /// </remarks>
    [Fact]
    public void GetMany_WorkerRasterizerCannotBeMade_RasterizesOnTheCallingThread()
    {
        using Font font = TestFont.Open();
        GlyphRequest[] requests = Requests(font);
        var rasterizers = new List<TestRasterizer>();
        int calls = 0;
        var atlas = new GlyphAtlas(
            () => ++calls == 3 ? throw new InvalidOperationException("The test factory fails its third rasterizer.") : NewRasterizer(rasterizers),
            threads: 4
        );
        using var serial = new GlyphAtlas(() => NewRasterizer(), threads: 1);

        (AtlasGlyph[] Glyphs, bool[] Found) made = GetMany(atlas, requests);
        Assert.Equal(GetMany(serial, requests).Glyphs, made.Glyphs);
        AssertSamePages(serial, atlas);
        Assert.Null(atlas.RunningWorkers);
        Assert.Equal([false, true], rasterizers.Select(rasterizer => rasterizer.IsDisposed));
        atlas.BeginFrame();
        _ = GetMany(atlas, [.. Enumerable.Range(130, 4).Select(g => new GlyphRequest(new GlyphKey(font, (uint)g, 24, 0), Ink))]);
        atlas.Dispose();

        Assert.Equal(3, calls);
        Assert.All(rasterizers.SelectMany(r => r.MeasureThreads), thread => Assert.Equal(Environment.CurrentManagedThreadId, thread));
        Assert.All(rasterizers, rasterizer => Assert.True(rasterizer.IsDisposed));
    }

    /// <summary>
    /// A rasterizer that throws on a worker makes GetMany throw once every worker has finished, and leaves none of the call's glyphs
    /// cached.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The test font and an atlas made with a function that creates test rasterizers, with 4 threads, each told to fail on glyph 7.
    ///
    /// Steps:
    /// - Ask with one GetMany for glyphs 2 to 21 at 24 px; then, with the rasterizers told to fail on nothing, for the same glyphs.
    ///
    /// Expected:
    /// - The first call throws <see cref="InvalidOperationException"/>, and none of the 20 glyphs is in the atlas after it.
    /// - The second call rasterizes all 20 again, and finds them.
    /// </remarks>
    [Fact]
    public void GetMany_RasterizerThrowsOnAWorker_ThrowsAndCachesNothing()
    {
        using Font font = TestFont.Open();
        var rasterizers = new List<TestRasterizer>();
        using var atlas = new GlyphAtlas(
            () =>
            {
                TestRasterizer rasterizer = NewRasterizer(rasterizers);
                rasterizer.RasterizeFails = 7;
                return rasterizer;
            },
            threads: 4
        );
        GlyphRequest[] requests = [.. Enumerable.Range(2, 20).Select(g => new GlyphRequest(new GlyphKey(font, (uint)g, 24, 0), Ink))];

        Assert.Throws<InvalidOperationException>(() => GetMany(atlas, requests));
        Assert.All(requests, request => Assert.Equal(GlyphLookup.Missing, atlas.Lookup(request.Key, Ink, out _)));

        rasterizers.ForEach(rasterizer => rasterizer.RasterizeFails = null);
        int rasterized = atlas.RasterizeCount;
        (_, bool[] found) = GetMany(atlas, requests);
        Assert.Equal(rasterized + 20, atlas.RasterizeCount);
        Assert.All(found, Assert.True);
    }

    /// <summary>
    /// Lookup finds a glyph in the atlas without rasterizing one that is not.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The test font and an empty atlas made with one test rasterizer.
    ///
    /// Steps:
    /// - Look up glyph 2 at 24 px; get it with GetMany; look it up again.
    ///
    /// Expected:
    /// - Missing first, with nothing measured; Found after, with the region GetMany returned.
    /// </remarks>
    [Fact]
    public void Lookup_MissingGlyph_IsNotRasterized()
    {
        using Font font = TestFont.Open();
        TestRasterizer rasterizer = NewRasterizer();
        using var atlas = new GlyphAtlas(rasterizer);
        var key = new GlyphKey(font, 2, 24, 0);

        Assert.Equal(GlyphLookup.Missing, atlas.Lookup(key, Ink, out _));
        Assert.Empty(rasterizer.Measured);
        (AtlasGlyph[] glyphs, _) = GetMany(atlas, [new(key, Ink)]);
        Assert.Equal(GlyphLookup.Found, atlas.Lookup(key, Ink, out AtlasGlyph found));
        Assert.Equal(glyphs[0], found);
    }

    /// <summary>
    /// A glyph the atlas refused is refused again in the same frame without being rasterized, and tried again in the next.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The test font and an atlas of one 64 px page of 32 px plots (a page limit of 2) made with a function that creates test
    ///   rasterizers, with 4 threads; glyphs 2 to 9 at 24 px fill its 2 pages; glyph 30 at 40 px is larger than a plot.
    ///
    /// Steps:
    /// - Ask with GetMany for glyph 56 at 24 px (no room) and glyph 30; look both up; ask for them again.
    /// - Start a frame and look them up.
    ///
    /// Expected:
    /// - Neither is found; both look up as Refused; the second GetMany rasterizes nothing; RefusedCount rose by 1 (glyph 56).
    /// - In the next frame both look up as Missing.
    /// </remarks>
    [Fact]
    public void GetMany_RefusedGlyph_IsNotRasterizedAgainInTheFrame()
    {
        using Font font = TestFont.Open();
        using var atlas = new GlyphAtlas(() => NewRasterizer(), pageSize: 64, plotSize: 32, maxPages: 1, threads: 4);
        _ = GetMany(atlas, [.. Enumerable.Range(2, 8).Select(g => new GlyphRequest(new GlyphKey(font, (uint)g, 24, 0), Ink))]);
        Assert.Equal(2, atlas.GetPageCount(GlyphCoverage.Gray));
        var full = new GlyphKey(font, 56, 24, 0);
        var large = new GlyphKey(font, 30, 40, 0);
        GlyphRequest[] requests = [new(full, Ink), new(large, Ink)];
        int refused = atlas.RefusedCount;

        (_, bool[] found) = GetMany(atlas, requests);
        int rasterized = atlas.RasterizeCount;
        Assert.Equal(GlyphLookup.Refused, atlas.Lookup(full, Ink, out _));
        Assert.Equal(GlyphLookup.Refused, atlas.Lookup(large, Ink, out _));
        (_, bool[] again) = GetMany(atlas, requests);

        Assert.Equal([false, false], found);
        Assert.Equal([false, false], again);
        Assert.Equal(rasterized, atlas.RasterizeCount);
        Assert.Equal(refused + 1, atlas.RefusedCount);
        atlas.BeginFrame();
        Assert.Equal(GlyphLookup.Missing, atlas.Lookup(full, Ink, out _));
        Assert.Equal(GlyphLookup.Missing, atlas.Lookup(large, Ink, out _));
    }

    /// <summary>
    /// A frame whose glyphs are all in the atlas allocates nothing in GetMany, with the workers started.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The test font and an atlas made with a function that creates test rasterizers, with 4 threads, that holds the glyphs of
    ///   <see cref="GetMany_FourThreads_SamePagesAndRegionsAsOne"/> but glyph 50, which it refuses (its workers started), and the
    ///   result spans.
    ///
    /// Steps:
    /// - 100 frames, in the windows of <c>SteadyStateAllocation</c>: start a frame and ask for the 296 glyphs with GetMany.
    ///
    /// Expected:
    /// - 0 bytes on this thread in every window, and nothing rasterized. A failure lists each window's bytes, the methods compiled
    ///   on this thread, and the collections (Issue #92).
    /// </remarks>
    [Fact]
    public void GetMany_EveryGlyphCached_AllocatesNothing()
    {
        using Font font = TestFont.Open();
        GlyphRequest[] requests = [.. Requests(font).Where(r => r.Key.GlyphId != 50)];
        using var atlas = new GlyphAtlas(() => NewRasterizer(), threads: 4);
        var glyphs = new AtlasGlyph[requests.Length];
        bool[] found = new bool[requests.Length];
        atlas.GetMany(requests, glyphs, found);
        Assert.NotNull(atlas.RunningWorkers);
        int rasterized = atlas.RasterizeCount;

        XylemUI.Tests.SteadyStateAllocation.AssertNone(
            100,
            _ =>
            {
                atlas.BeginFrame();
                atlas.GetMany(requests, glyphs, found);
            }
        );
        Assert.Equal(rasterized, atlas.RasterizeCount);
    }

    // Glyphs 2 to 61 at 24 px, each at the 4 subpixel steps in Gray and ClearType, in the order of a line, then the space.
    private static GlyphRequest[] Requests(Font font)
    {
        var requests = new List<GlyphRequest>();
        for (uint glyph = 2; glyph < 62; glyph++)
        {
            for (int step = 0; step < GlyphPosition.SubpixelSteps; step++)
            {
                GlyphCoverage coverage = (glyph + (uint)step) % 2 == 0 ? GlyphCoverage.Gray : GlyphCoverage.Rgb;
                requests.Add(new GlyphRequest(new GlyphKey(font, glyph, glyph == 50 ? 375 : 24, step, coverage), Ink));
            }
        }

        // 300 requests: the 240 above, the space, and glyphs 62 to 120 in Gray at step 0.
        requests.Add(new GlyphRequest(new GlyphKey(font, TestRasterizer.Space, 24, 0), Ink));
        for (uint glyph = 62; glyph < 121; glyph++)
        {
            requests.Add(new GlyphRequest(new GlyphKey(font, glyph, 24, 0), Ink));
        }

        return [.. requests];
    }

    private static TestRasterizer NewRasterizer(List<TestRasterizer>? made = null)
    {
        var rasterizer = new TestRasterizer();
        rasterizer.ColorGlyphs[40] = false;
        rasterizer.ColorGlyphs[41] = true;
        made?.Add(rasterizer);
        return rasterizer;
    }

    private static (AtlasGlyph[] Glyphs, bool[] Found) GetMany(GlyphAtlas atlas, GlyphRequest[] requests)
    {
        var glyphs = new AtlasGlyph[requests.Length];
        bool[] found = new bool[requests.Length];
        atlas.GetMany(requests, glyphs, found);
        return (glyphs, found);
    }

    // The same pages of every kind, byte for byte.
    private static void AssertSamePages(GlyphAtlas expected, GlyphAtlas actual)
    {
        foreach (GlyphCoverage coverage in (GlyphCoverage[])[GlyphCoverage.Gray, GlyphCoverage.Rgb, GlyphCoverage.Color])
        {
            int pages = expected.GetPageCount(coverage);
            Assert.Equal(pages, actual.GetPageCount(coverage));
            for (int page = 0; page < pages; page++)
            {
                Assert.True(
                    expected.GetPagePixels(coverage, page).SequenceEqual(actual.GetPagePixels(coverage, page)),
                    $"{coverage} page {page} differs."
                );
            }
        }
    }
}
