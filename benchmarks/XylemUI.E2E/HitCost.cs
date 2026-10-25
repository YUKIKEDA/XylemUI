using System.Diagnostics;
using System.Numerics;
using NodeHandle = XylemUI.Handle<XylemUI.UiNode>;

namespace XylemUI.E2E;

// The cost of hit regions (row R4e, design §5.0, "Hit regions and DrawingContext"): writing one, the way fill-rect times a
// rectangle, and testing a point against 1,000, in the worst case where no region holds it and every one is tried. Each is timed
// with the regions outside every scope and inside a rotation and a rounded clip, where the test inverts the state's transform and
// takes each clip's distance.
internal static class HitCost
{
    private const int Regions = 1_000;

    public static int Run()
    {
        using var report = new Report("hit-region");
        var list = new DisplayList();
        foreach (bool scoped in new[] { false, true })
        {
            string name = scoped ? "in_clip" : "plain";
            report.Number($"ns_per_region_{name}_p50", Median(21, () => TimeFrames(list, scoped, 1_000) / (1_000.0 * Regions)));

            // One frame of regions, then the test of a point none holds.
            Write(list, scoped, 0);
            NodeHandle hit = default;
            report.Number(
                $"us_per_test_of_{Regions}_{name}_p50",
                Median(
                    21,
                    () =>
                    {
                        long start = Stopwatch.GetTimestamp();
                        for (int i = 0; i < 100; i++)
                        {
                            hit = list.HitTest(new Vector2(-50 - i, -50));
                        }

                        return Stopwatch.GetElapsedTime(start).TotalMicroseconds / 100;
                    }
                )
            );
            if (hit != default)
            {
                throw new InvalidOperationException("A point outside every region hit one.");
            }
        }

        return 0;
    }

    // The nanoseconds of `frames` frames of regions, after as many unmeasured.
    private static double TimeFrames(DisplayList list, bool scoped, int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            Write(list, scoped, i);
        }

        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < frames; i++)
        {
            Write(list, scoped, i);
        }

        return Stopwatch.GetElapsedTime(start).TotalNanoseconds;
    }

    // 1,000 regions of 16 logical pixels with corners of radius 4, spread as fill-rect spreads its rectangles.
    private static void Write(DisplayList list, bool scoped, int frame)
    {
        list.Reset(1.25f);
        if (scoped)
        {
            list.PushTransform(Matrix3x2.CreateRotation(0.1f, new Vector2(400, 300)));
            list.PushClip(0, 0, 800, 600, new CornerRadii(24));
        }

        for (int i = 0; i < Regions; i++)
        {
            float x = ((i * 37) + (frame * 3)) % 780;
            float y = 50 + ((i * 11) % 530);
            list.HitRegion(new NodeHandle(i + 1, 1), x, y, 16, 16, new CornerRadii(4));
        }

        if (scoped)
        {
            list.Pop();
            list.Pop();
        }
    }

    private static double Median(int samples, Func<double> sample)
    {
        var values = new List<double>(samples);
        for (int s = 0; s < samples; s++)
        {
            values.Add(sample());
        }

        values.Sort();
        return values[samples / 2];
    }
}
