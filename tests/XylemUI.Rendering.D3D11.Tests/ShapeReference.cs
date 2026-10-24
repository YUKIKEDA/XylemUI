using System.Numerics;

namespace XylemUI.Rendering.D3D11.Tests;

// The CPU side of the shape tests (roadmap row R4): what a rounded rectangle, its border, a gradient, and a shadow should leave
// in a pixel, premultiplied, at the pixel's center. The rounded rectangle and the gradient follow the geometry of design §5.0
// ("Shapes, brushes, and shadows") as the shader's do. The shadow is not the shader's formula: it is the Gaussian blur of the
// rounded rectangle integrated exactly along x and numerically along y in steps of 0.02 pixel, so the test measures the error
// of the shader's approximation (erf's polynomial and four samples along y).
internal static class ShapeReference
{
    public static float Distance(Vector2 p, Vector4 rect, Vector4 radii)
    {
        var half = new Vector2(rect.Z, rect.W) * 0.5f;
        Vector2 fromCenter = p - (new Vector2(rect.X, rect.Y) + half);
        float radius = MathF.Min(Corner(fromCenter, radii), MathF.Min(half.X, half.Y));
        Vector2 q = Vector2.Abs(fromCenter) - half + new Vector2(radius);
        return radius == 0 ? MathF.Max(q.X, q.Y) : Vector2.Max(q, Vector2.Zero).Length() + MathF.Min(0, MathF.Max(q.X, q.Y)) - radius;
    }

    public static float Coverage(Vector2 p, Vector4 rect, Vector4 radii) => Math.Clamp(0.5f - Distance(p, rect, radii), 0, 1);

    public static Vector4 Premultiply(Color color) =>
        new Vector4(color.R * color.A / 255f, color.G * color.A / 255f, color.B * color.A / 255f, color.A) / 255f;

    // A rounded rectangle filled and bordered, at p.
    public static Vector4 Shape(Vector2 p, Vector4 rect, Vector4 radii, Vector4 fill, Vector4 widths, Vector4 border)
    {
        float outer = Coverage(p, rect, radii);
        if (widths != Vector4.Zero)
        {
            var inner = new Vector4(rect.X + widths.X, rect.Y + widths.Y, rect.Z - widths.X - widths.Z, rect.W - widths.Y - widths.W);
            var pulledIn = new Vector4(
                MathF.Max(widths.X, widths.Y),
                MathF.Max(widths.Z, widths.Y),
                MathF.Max(widths.Z, widths.W),
                MathF.Max(widths.X, widths.W)
            );
            var innerRadii = Vector4.Max(Vector4.Zero, radii - pulledIn);
            float inside = inner.Z > 0 && inner.W > 0 ? Coverage(p, inner, innerRadii) : 0;
            fill = Vector4.Lerp(border, fill, inside);
        }

        return fill * outer;
    }

    // A gradient's premultiplied sRGB color at p in box, without the dither.
    public static Vector4 Gradient(GradientBrush brush, Vector2 p, Vector4 box)
    {
        var center = new Vector2(box.X + (brush.Center.X * box.Z), box.Y + (brush.Center.Y * box.W));
        Vector2 d = p - center;
        float angle = brush.Angle * MathF.PI / 180;
        float t = brush.Kind switch
        {
            GradientKind.Linear => (
                Vector2.Dot(p - new Vector2(box.X + (box.Z / 2), box.Y + (box.W / 2)), new Vector2(MathF.Sin(angle), -MathF.Cos(angle)))
                / (MathF.Abs(box.Z * MathF.Sin(angle)) + MathF.Abs(box.W * MathF.Cos(angle)))
            ) + 0.5f,
            GradientKind.Radial => d.Length() / (brush.Radius * MathF.Max(box.Z, box.W)),
            _ => Fraction((MathF.Atan2(d.X, -d.Y) - angle) / (2 * MathF.PI)),
        };
        bool oklab = brush.ColorSpace == GradientColorSpace.Oklab;
        ReadOnlySpan<GradientStop> stops = brush.Stops;
        Vector4 color = GradientColors.Premultiplied(stops[0].Color, oklab);
        if (t > stops[0].Offset)
        {
            color = GradientColors.Premultiplied(stops[^1].Color, oklab);
            for (int i = 1; i < stops.Length; i++)
            {
                if (t < stops[i].Offset)
                {
                    float f = (t - stops[i - 1].Offset) / MathF.Max(stops[i].Offset - stops[i - 1].Offset, 1e-6f);
                    color = Vector4.Lerp(
                        GradientColors.Premultiplied(stops[i - 1].Color, oklab),
                        GradientColors.Premultiplied(stops[i].Color, oklab),
                        f
                    );
                    break;
                }
            }
        }

        if (oklab && color.W > 0)
        {
            Vector3 linear = OklabToLinear(new Vector3(color.X, color.Y, color.Z) / color.W);
            color = new Vector4(
                ToSrgb(Math.Clamp(linear.X, 0, 1)) * color.W,
                ToSrgb(Math.Clamp(linear.Y, 0, 1)) * color.W,
                ToSrgb(Math.Clamp(linear.Z, 0, 1)) * color.W,
                color.W
            );
        }

        return color;
    }

    // The shadow's alpha at p: the Gaussian blur of the rounded rectangle `rect`, cut out by `element` for an outer shadow, or its
    // complement inside `element` for an inset one.
    public static float Shadow(Vector2 p, Vector4 rect, Vector4 radii, float sigma, Vector4 element, Vector4 elementRadii, bool inset)
    {
        float alpha = sigma <= 0 ? Coverage(p, rect, radii) : Blurred(p, rect, radii, sigma);
        float inside = Coverage(p, element, elementRadii);
        return inset ? (1 - alpha) * inside : alpha * (1 - inside);
    }

    // Composes a premultiplied color over an opaque background, as the 8-bit target stores it.
    public static Color Over(Vector4 color, Color under) =>
        new(Blend(color.X, color.W, under.R), Blend(color.Y, color.W, under.G), Blend(color.Z, color.W, under.B));

    private static byte Blend(float source, float alpha, byte under) =>
        (byte)Math.Clamp(MathF.Round((source + ((1 - alpha) * (under / 255f))) * 255f), 0, 255);

    private static float Blurred(Vector2 p, Vector4 rect, Vector4 radii, float sigma)
    {
        const double Step = 0.02;
        double sum = 0;
        double top = rect.Y;
        double bottom = rect.Y + rect.W;
        for (double v = Math.Max(top, p.Y - (6 * sigma)) + (Step / 2); v < Math.Min(bottom, p.Y + (6 * sigma)); v += Step)
        {
            (double left, double right) = Extent(rect, radii, v);
            if (right <= left)
            {
                continue;
            }

            double weight = Math.Exp(-((p.Y - v) * (p.Y - v)) / (2 * sigma * sigma)) / (Math.Sqrt(2 * Math.PI) * sigma);
            sum += weight * (Phi((p.X - left) / sigma) - Phi((p.X - right) / sigma)) * Step;
        }

        return (float)sum;
    }

    // The left and right of the rounded rectangle at height v.
    private static (double Left, double Right) Extent(Vector4 rect, Vector4 radii, double v)
    {
        double half = Math.Min(rect.Z, rect.W) / 2;
        double left = rect.X + Inward(Math.Min(radii.X, half), Math.Min(radii.W, half), rect.Y, rect.Y + rect.W, v);
        double right = rect.X + rect.Z - Inward(Math.Min(radii.Y, half), Math.Min(radii.Z, half), rect.Y, rect.Y + rect.W, v);
        return (left, right);
    }

    // How far the edge is pulled in at height v by the top corner of radius `top` and the bottom corner of radius `bottom`.
    private static double Inward(double top, double bottom, double y0, double y1, double v)
    {
        if (v < y0 + top)
        {
            double dy = y0 + top - v;
            return top - Math.Sqrt(Math.Max(0, (top * top) - (dy * dy)));
        }

        if (v > y1 - bottom)
        {
            double dy = v - (y1 - bottom);
            return bottom - Math.Sqrt(Math.Max(0, (bottom * bottom) - (dy * dy)));
        }

        return 0;
    }

    private static double Phi(double z) => 0.5 * (1 + Erf(z / Math.Sqrt(2)));

    // erf to about 1.2e-7 (Numerical Recipes' erfc by Chebyshev fitting).
    private static double Erf(double x)
    {
        double z = Math.Abs(x);
        double t = 1 / (1 + (0.5 * z));
        ReadOnlySpan<double> c =
        [
            -1.26551223,
            1.00002368,
            0.37409196,
            0.09678418,
            -0.18628806,
            0.27886807,
            -1.13520398,
            1.48851587,
            -0.82215223,
            0.17087277,
        ];
        double polynomial = 0;
        for (int i = c.Length - 1; i >= 0; i--)
        {
            polynomial = c[i] + (t * polynomial);
        }

        double r = t * Math.Exp(-(z * z) + polynomial);
        return x >= 0 ? 1 - r : r - 1;
    }

    private static float Corner(Vector2 fromCenter, Vector4 radii) =>
        fromCenter.X < 0 ? (fromCenter.Y < 0 ? radii.X : radii.W) : (fromCenter.Y < 0 ? radii.Y : radii.Z);

    private static float Fraction(float value) => value - MathF.Floor(value);

    private static float ToSrgb(float c) => c <= 0.0031308f ? c * 12.92f : (1.055f * MathF.Pow(c, 1 / 2.4f)) - 0.055f;

    private static Vector3 OklabToLinear(Vector3 lab)
    {
        float l = lab.X + (0.3963377774f * lab.Y) + (0.2158037573f * lab.Z);
        float m = lab.X - (0.1055613458f * lab.Y) - (0.0638541728f * lab.Z);
        float s = lab.X - (0.0894841775f * lab.Y) - (1.2914855480f * lab.Z);
        l = l * l * l;
        m = m * m * m;
        s = s * s * s;
        return new Vector3(
            (4.0767416621f * l) - (3.3077115913f * m) + (0.2309699292f * s),
            (-1.2684380046f * l) + (2.6097574011f * m) - (0.3413193965f * s),
            (-0.0041960863f * l) - (0.7034186147f * m) + (1.7076147010f * s)
        );
    }
}
