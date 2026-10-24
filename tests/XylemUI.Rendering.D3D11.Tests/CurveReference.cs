using System.Numerics;

namespace XylemUI.Rendering.D3D11.Tests;

// The CPU side of the line, ellipse, and arc tests (roadmap row R4b, design §5.0, "The API of lines, ellipses, arcs, and the paint
// of text"): what a shape should leave in a pixel, at the pixel's center, premultiplied. It does not take the shader's shortcuts:
// it measures every dash, each arc dash from its own middle, and the nearest point of an ellipse by sampling and refining it. A
// pixel is covered by saturate(0.5 - d), less what lies past a stroke's far side (for a stroke thinner than a pixel), as the
// shader covers it; flat and square ends measure the distance past them along the line and across it, and take the larger.
internal static class CurveReference
{
    public static float Coverage(float distance, float width) =>
        Math.Clamp(0.5f - distance, 0, 1) - (width > 0 ? Math.Clamp(0.5f - distance - width, 0, 1) : 0);

    public static float CapDistance(float along, float across, float half, LineCap cap) =>
        cap switch
        {
            LineCap.Round => new Vector2(MathF.Max(along, 0), across).Length() - half,
            LineCap.Square => MathF.Max(MathF.Abs(across) - half, along - half),
            _ => MathF.Max(MathF.Abs(across) - half, along),
        };

    // The dashes along a length, each cut to it: every one the pattern places there, a solid stroke as one. A dash that the cut
    // leaves without length is kept only with a cap other than flat; one that lies past the length is left out.
    public static List<(float Start, float End)> Dashes(float length, Dash? dash, LineCap cap)
    {
        var dashes = new List<(float, float)>();
        if (dash is not { } pattern)
        {
            if (length > 0 || cap != LineCap.Flat)
            {
                dashes.Add((0, length));
            }

            return dashes;
        }

        float period = pattern.On + pattern.Off;
        for (int k = (int)MathF.Floor(pattern.Offset / period) - 1; (k * period) - pattern.Offset <= length; k++)
        {
            float start = (k * period) - pattern.Offset;
            float end = start + pattern.On;
            (float a, float b) = (MathF.Max(start, 0), MathF.Min(end, length));
            if (b > a || (b == a && end >= 0 && start <= length && cap != LineCap.Flat))
            {
                dashes.Add((a, b));
            }
        }

        return dashes;
    }

    // A straight line from a to b, its half width, caps, and dashes in physical pixels.
    public static float Line(Vector2 p, Vector2 a, Vector2 b, float half, LineCap cap, Dash? dash)
    {
        float total = Vector2.Distance(a, b);
        Vector2 u = total > 0 ? (b - a) / total : Vector2.UnitX;
        float s = Vector2.Dot(p - a, u);
        float n = Vector2.Dot(p - a, new Vector2(-u.Y, u.X));
        float distance = float.MaxValue;
        foreach ((float start, float end) in Dashes(total, dash, cap))
        {
            distance = MathF.Min(distance, CapDistance(MathF.Max(start - s, s - end), n, half, cap));
        }

        return distance;
    }

    // An arc of the circle (center, r) from `start` (radians, clockwise from up) for `sweep` radians, clockwise when positive and
    // counterclockwise when negative, its dashes laid from the start in the arc's own direction. Inside a dash's angles, the
    // distance past its ends is measured along the arc, as along a line. On a whole circle, the last and the first dashes that
    // meet at the seam are one dash across it, and a circle without dashes is a ring with no ends.
    public static float Arc(Vector2 p, Vector2 center, float r, float start, float sweep, float half, LineCap cap, Dash? dash)
    {
        float direction = MathF.Sign(sweep);
        float span = MathF.Min(MathF.Abs(sweep), MathF.Tau);
        Vector2 q = p - center;
        float angle = MathF.Atan2(q.X, -q.Y);
        float total = span * r;
        List<(float Start, float End)> dashes = Dashes(total, dash, cap);
        if (span == MathF.Tau && dashes.Count > 0 && dashes[0].Start <= 0 && dashes[^1].End >= total)
        {
            if (dashes.Count == 1)
            {
                return MathF.Abs(q.Length() - r) - half;
            }

            dashes[0] = (dashes[^1].Start - total, dashes[0].End);
            dashes.RemoveAt(dashes.Count - 1);
        }

        float distance = float.MaxValue;
        foreach ((float s0, float s1) in dashes)
        {
            float middle = start + (direction * (s0 + s1) / (2 * r));
            float aperture = (s1 - s0) / (2 * r);
            float delta = Wrap(angle - middle);
            if (MathF.Abs(delta) <= aperture)
            {
                distance = MathF.Min(distance, CapDistance((MathF.Abs(delta) - aperture) * r, q.Length() - r, half, cap));
                continue;
            }

            float side = MathF.Sign(delta);
            float end = middle + (side * aperture);
            var radial = new Vector2(MathF.Sin(end), -MathF.Cos(end));
            Vector2 tangent = side * new Vector2(MathF.Cos(end), MathF.Sin(end));
            Vector2 fromEnd = q - (r * radial);
            distance = MathF.Min(distance, CapDistance(Vector2.Dot(fromEnd, tangent), Vector2.Dot(fromEnd, radial), half, cap));
        }

        return distance;
    }

    // The signed distance to the ellipse (center, radii): the nearest of 4,096 points on it, refined by a ternary search between
    // its neighbors; negative inside.
    public static float Ellipse(Vector2 p, Vector2 center, Vector2 radii)
    {
        Vector2 q = p - center;
        float Distance(float t) => Vector2.Distance(q, new Vector2(radii.X * MathF.Cos(t), radii.Y * MathF.Sin(t)));
        const int Samples = 4096;
        int best = 0;
        for (int i = 1; i < Samples; i++)
        {
            if (Distance(i * MathF.Tau / Samples) < Distance(best * MathF.Tau / Samples))
            {
                best = i;
            }
        }

        float low = (best - 1) * MathF.Tau / Samples;
        float high = (best + 1) * MathF.Tau / Samples;
        for (int i = 0; i < 60; i++)
        {
            float a = low + ((high - low) / 3);
            float b = high - ((high - low) / 3);
            (low, high) = Distance(a) < Distance(b) ? (low, b) : (a, high);
        }

        float distance = Distance((low + high) / 2);
        float inside = (q.X * q.X / (radii.X * radii.X)) + (q.Y * q.Y / (radii.Y * radii.Y));
        return inside > 1 ? distance : -distance;
    }

    // The signed distance to the sector of the circle (center, r) from `start` for `sweep` radians, clockwise when positive: to
    // the nearest of its two radii and its arc, negative inside.
    public static float Sector(Vector2 p, Vector2 center, float r, float start, float sweep)
    {
        (float from, float span) = sweep < 0 ? (start + sweep, -sweep) : (start, sweep);
        Vector2 q = p - center;
        float angle = Wrap(MathF.Atan2(q.X, -q.Y) - from - (span / 2));
        bool within = MathF.Abs(angle) <= span / 2;
        var a = new Vector2(r * MathF.Sin(from), -r * MathF.Cos(from));
        var b = new Vector2(r * MathF.Sin(from + span), -r * MathF.Cos(from + span));
        float arc = within ? MathF.Abs(q.Length() - r) : MathF.Min(Vector2.Distance(q, a), Vector2.Distance(q, b));
        float distance = MathF.Min(arc, MathF.Min(ToSegment(q, a), ToSegment(q, b)));
        return within && q.Length() <= r ? -distance : distance;
    }

    private static float ToSegment(Vector2 p, Vector2 end)
    {
        float t = Math.Clamp(Vector2.Dot(p, end) / end.LengthSquared(), 0, 1);
        return Vector2.Distance(p, end * t);
    }

    private static float Wrap(float angle) => angle - (MathF.Tau * MathF.Floor((angle + MathF.PI) / MathF.Tau));
}
