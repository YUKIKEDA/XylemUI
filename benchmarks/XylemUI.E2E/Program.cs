// The end-to-end measurements of XylemUI (design §11.3; roadmap row M1a; docs/benchmarks/m1-baseline.md). Recorded, not a
// gate (.cursor/rules/bench.mdc). Judge the numbers on a NativeAOT Release build:
//   dotnet publish benchmarks/XylemUI.E2E -c Release -r win-x64 -o <dir>, then <dir>/XylemUI.E2E.exe <scenario>.
// Each scenario prints one JSON object on a line, except scope-cost, which prints the rows ADR 0001 quotes.
//   startup [runs]             launch to the first frame on the display, launch to Main, and the executable's size
//   idle [runs] [--gray]       CPU time and memory of 10 idle seconds after the first frame (--gray: grayscale text)
//   startup-check [runs]       in one run, the desktop image with the marker against the frame statistics (the outside check)
//   frames [frames] [--warp]   CPU stages, GPU time, present latency, intervals, missed frames of the scene
//                              (--gray draws its text in grayscale instead of the window's ClearType, row T2; --shapes draws each
//                              rectangle rounded, with a gradient, a border, and a shadow, row R4a; --curves draws each as a
//                              dashed line, an ellipse, and an arc, and 1,000 glyphs with a gradient, row R4b)
//   frames-heavy N [frames]    the same with N full-window translucent rectangles (Present under GPU load, row R2)
//   event-latency [samples]    a posted message to the present call and to the display (the stand-in for input latency)
//   resize-text [passes]       relayout of 10,101 text leaves after a window resize, shaping and cached
//   layout [samples]           the four layout times of 10,101 nodes in three scenes (design §3.2, row L1a)
//   layout-loop scene kind N   one of them (kind change, fixed_row, content, or width; scene absolute: its change) repeated N times on one tree, for a profile
//   glyph-refused [requests]   a request refused at the atlas's page limit, against a cached one
//   glyph-resolve [runs]       ResolveGlyphs for a frame of 1 to 431 new glyphs, and of emoji, on 1, 2, and 4 threads (row T5)
//   glyph-resolve-child N T [--emoji] [--from F]  one sample of it: N new glyphs on T threads, to the workers from F on
//   shapes                     the shapes of row R4a, to compare with benchmarks/compare/shapes.html in a browser
//   curves                     the lines, ellipses, and arcs of row R4b, to compare with benchmarks/compare/curves.html
//   dpi-awareness              whether the process is per-monitor DPI aware (V2) when fonts and glyphs come before the window (row W1)
//   draw-text [--cold]         DrawText of a screen of 40 lines whose glyphs are in the atlas, per glyph (row T5); --cold: the
//                              frame scene's line once per frame with the caches emptied before, as a frame calls it
//   font-fallback [splits]     the font fallback of a line: the first split, a split again, one match, the bytes (row F1)
//   dependency-order [sets]    an effect that alternates the order it reads two computed chains of 1, 10, 100
//   scope-cost                 ADR 0001: four ways to hold cleanups, and scopes rebuilt each frame
//   startup-child [--idle]     the child of startup and idle
//   app [--static] [--period ms]  the scene as an app, animated (at most one frame per period) or drawn once, for `external`
//   text [--dark]              the text scene of the rendering comparison, drawn once (row T4, TextQuality.cs)
//                              (--gray in grayscale; --frames N draws N frames and reports their costs, row T2;
//                              --aliased bilevel and --scale S at scale S instead of the window's, row T8)
//   text-capture out.png app [args]  launch an app, and save its client area from the screen as a PNG (row T4)
//   external --presentmon <exe> --app <exe> [--name n] [--runs N]   the comparison of frameworks, from outside the process
using System.Globalization;
using XylemUI.E2E;

string scenario = args.Length > 0 ? args[0] : "frames";
float Scale() => Array.IndexOf(args, "--scale") is int at and >= 0 ? float.Parse(args[at + 1], System.Globalization.CultureInfo.InvariantCulture) : 0;

int Number(int index, int fallback) =>
    args.Length > index && int.TryParse(args[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : fallback;

return scenario switch
{
    "startup" => Startup.Launch(Number(1, 10)),
    "startup-child" => Startup.Child(idle: args.Contains("--idle"), gray: args.Contains("--gray")),
    "idle" => Startup.Idle(Number(1, 3), gray: args.Contains("--gray")),
    "startup-check" => Startup.Check(Number(1, 5)),
    "frames" => Frames.Run(
        Number(1, 600),
        heavy: 0,
        warp: args.Contains("--warp"),
        gray: args.Contains("--gray"),
        shapes: args.Contains("--shapes"),
        curves: args.Contains("--curves")
    ),
    "frames-heavy" => Frames.Run(Number(2, 120), heavy: Number(1, 20_000), warp: false),
    "event-latency" => EventLatency.Run(Number(1, 100)),
    "resize-text" => CoreCosts.ResizeText(Number(1, 51)),
    "layout" => LayoutCost.Run(Number(1, 15)),
    "layout-loop" => LayoutCost.Loop(args.Length > 1 ? args[1] : "s6b", args.Length > 2 ? args[2] : "change", Number(3, 10_000)),
    "glyph-refused" => CoreCosts.GlyphRefused(Number(1, 2_000)),
    "glyph-resolve" => GlyphResolve.Run(Number(1, 5)),
    "dpi-awareness" => DpiAwareness.Run(),
    "fill-rect" => GlyphResolve.FillRectCost(args.Length > 1 ? float.Parse(args[1], CultureInfo.InvariantCulture) : 1),
    "shapes" => ShapeGallery.Run(),
    "curves" => CurveGallery.Run(),
    "draw-text" => GlyphResolve.DrawTextCost(cold: args.Contains("--cold")),
    "glyph-resolve-child" => GlyphResolve.Child(
        Number(1, 431),
        Number(2, 1),
        emoji: args.Contains("--emoji"),
        from: Array.IndexOf(args, "--from") is int at and >= 0 ? Number(at + 1, 0) : 0
    ),
    "font-fallback" => CoreCosts.FontFallbackCost(Number(1, 2_000)),
    "dependency-order" => CoreCosts.DependencyOrder(Number(1, 10_000)),
    "scope-cost" => ScopeCost.Run(),
    "text" => TextQuality.Run(
        dark: args.Contains("--dark"),
        gray: args.Contains("--gray"),
        frames: Array.IndexOf(args, "--frames") is int at and >= 0 ? Number(at + 1, 0) : 0,
        aliased: args.Contains("--aliased"),
        scale: Scale()
    ),
    "text-capture" => TextCapture.Run(args[1], args[2], string.Join(' ', args.Skip(3))),
    "app" => External.App(isStatic: args.Contains("--static"), period: Array.IndexOf(args, "--period") is int at and >= 0 ? Number(at + 1, 0) : 0),
    "external" => External.Run(args),
    _ => Unknown(scenario),
};

static int Unknown(string scenario)
{
    Console.Error.WriteLine($"Unknown scenario '{scenario}'. See the comment at the top of Program.cs.");
    return 2;
}
