// The end-to-end measurements of XylemUI (design §11.3; roadmap row M1a; docs/benchmarks/m1-baseline.md). Recorded, not a
// gate (.cursor/rules/bench.mdc). Judge the numbers on a NativeAOT Release build:
//   dotnet publish benchmarks/XylemUI.E2E -c Release -r win-x64 -o <dir>, then <dir>/XylemUI.E2E.exe <scenario>.
// Each scenario prints one JSON object on a line, except scope-cost, which prints the rows ADR 0001 quotes.
//   startup [runs]             launch to the first frame on the display, launch to Main, and the executable's size
//   idle [runs]                CPU time and memory of 10 idle seconds after the first frame
//   startup-check [runs]       in one run, the desktop image with the marker against the frame statistics (the outside check)
//   frames [frames] [--warp]   CPU stages, GPU time, present latency, intervals, missed frames of the scene
//   frames-heavy N [frames]    the same with N full-window translucent rectangles (Present under GPU load, row R2)
//   event-latency [samples]    a posted message to the present call and to the display (the stand-in for input latency)
//   resize-text [passes]       relayout of 10,101 text leaves after a window resize, shaping and cached
//   layout [samples]           the four layout times of 10,101 nodes in three scenes (design §3.2, row L1a)
//   layout-loop scene kind N   one of them (kind change or width) repeated N times on one tree, for a profile
//   glyph-refused [requests]   a request refused at the atlas's page limit, against a cached one
//   dependency-order [sets]    an effect that alternates the order it reads two computed chains of 1, 10, 100
//   scope-cost                 ADR 0001: four ways to hold cleanups, and scopes rebuilt each frame
//   startup-child [--idle]     the child of startup and idle
//   app [--static] [--period ms]  the scene as an app, animated (at most one frame per period) or drawn once, for `external`
//   external --presentmon <exe> --app <exe> [--name n] [--runs N]   the comparison of frameworks, from outside the process
using System.Globalization;
using XylemUI.E2E;

string scenario = args.Length > 0 ? args[0] : "frames";
int Number(int index, int fallback) =>
    args.Length > index && int.TryParse(args[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : fallback;

return scenario switch
{
    "startup" => Startup.Launch(Number(1, 10)),
    "startup-child" => Startup.Child(idle: args.Contains("--idle")),
    "idle" => Startup.Idle(Number(1, 3)),
    "startup-check" => Startup.Check(Number(1, 5)),
    "frames" => Frames.Run(Number(1, 600), heavy: 0, warp: args.Contains("--warp")),
    "frames-heavy" => Frames.Run(Number(2, 120), heavy: Number(1, 20_000), warp: false),
    "event-latency" => EventLatency.Run(Number(1, 100)),
    "resize-text" => CoreCosts.ResizeText(Number(1, 51)),
    "layout" => LayoutCost.Run(Number(1, 15)),
    "layout-loop" => LayoutCost.Loop(args.Length > 1 ? args[1] : "s6b", args.Length > 2 ? args[2] : "change", Number(3, 10_000)),
    "glyph-refused" => CoreCosts.GlyphRefused(Number(1, 2_000)),
    "dependency-order" => CoreCosts.DependencyOrder(Number(1, 10_000)),
    "scope-cost" => ScopeCost.Run(),
    "app" => External.App(isStatic: args.Contains("--static"), period: Array.IndexOf(args, "--period") is int at and >= 0 ? Number(at + 1, 0) : 0),
    "external" => External.Run(args),
    _ => Unknown(scenario),
};

static int Unknown(string scenario)
{
    Console.Error.WriteLine($"Unknown scenario '{scenario}'. See the comment at the top of Program.cs.");
    return 2;
}
