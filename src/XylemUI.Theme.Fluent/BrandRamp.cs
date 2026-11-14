namespace XylemUI.Theme.Fluent;

/// <summary>
/// Makes the 16 brand steps (10 to 160) from one accent color (design §13.2, "ブランドの 16 段の作り方"): the Fluent theme
/// designer's curve through Lab, with the lightness of each step taken from <c>brandWeb</c>.
/// </summary>
internal static class BrandRamp
{
    /// <summary>
    /// The number of brand steps, 10 to 160.
    /// </summary>
    public const int Steps = 16;

    // The control points of getBrandTokensFromPalette's defaults (its line 27): darkCp 2/3 and lightCp 1/3.
    private const double DarkControl = 2.0 / 3;
    private const double LightControl = 1.0 / 3;

    // Each curve is cut into 24 parts, palettes.ts line 129 with a torsion of 0.
    private const int Divisions = 24;

    /// <summary>
    /// Returns the 16 steps of a key color, step 10 first. The key's alpha is not read.
    /// </summary>
    /// <param name="key">The accent color.</param>
    /// <returns>The 16 opaque steps.</returns>
    public static Color[] From(Color key)
    {
        // hex_to_LCH, then curvePathFromPalette's LCH_to_Lab: the key's position is the Lab that comes back from LCH.
        (double keyL, double keyC, double keyH) = CieLab.ToLch(CieLab.FromSrgb(key.R / 255.0, key.G / 255.0, key.B / 255.0));
        Lab keyLab = CieLab.FromLch(keyL, keyC, keyH);
        var black = new Lab(0, 0, 0);
        var white = new Lab(100, 0, 0);
        var dark = new Lab(keyLab.L * (1 - DarkControl), keyLab.A, keyLab.B);
        var light = new Lab(keyLab.L + ((100 - keyLab.L) * LightControl), keyLab.A, keyLab.B);
        List<Lab> points = PointsOnPath(black, dark, keyLab, light, white);

        ReadOnlySpan<double> targets = FluentBrand.Lightness;
        var steps = new Color[Steps];
        int c = 0;
        for (int i = 0; i < Steps; i++)
        {
            // palettes.ts lines 101-117: move on until the next point is as light as the target, then interpolate the two points
            // linearly at the target's lightness. The targets lie strictly between 0 and 100, so the last point (white) stops it.
            double l = targets[i];
            while (l > points[c + 1].L)
            {
                c++;
            }

            Lab p1 = points[c];
            Lab p2 = points[c + 1];
            double u = (l - p1.L) / (p2.L - p1.L);
            var shade = new Lab(p1.L + ((p2.L - p1.L) * u), p1.A + ((p2.A - p1.A) * u), p1.B + ((p2.B - p1.B) * u));
            steps[i] = ToColor(CieLab.SnapIntoGamut(shade));
        }

        return steps;
    }

    // geometry.ts's getPointsOnCurvePath: 25 points on each quadratic Bézier, black to the key and the key to white, a point the
    // same as the one before it dropped. The hue torsion of theme designer is 0, and its step that turns the hue is left out
    // (design §13.2, step 2).
    private static List<Lab> PointsOnPath(Lab black, Lab dark, Lab key, Lab light, Lab white)
    {
        var points = new List<Lab>((2 * Divisions) + 2);
        AddCurve(points, black, dark, key);
        AddCurve(points, key, light, white);
        return points;
    }

    private static void AddCurve(List<Lab> points, Lab p0, Lab p1, Lab p2)
    {
        for (int d = 0; d <= Divisions; d++)
        {
            double t = (double)d / Divisions;
            var point = new Lab(Bezier(t, p0.L, p1.L, p2.L), Bezier(t, p0.A, p1.A, p2.A), Bezier(t, p0.B, p1.B, p2.B));
            if (points.Count > 0 && points[^1] == point)
            {
                continue;
            }

            points.Add(point);
        }
    }

    // geometry.ts's QuadraticBezier, in its order of operations. geometry.ts ports it, and the dropping of a repeated point above,
    // from three.js (MIT); the notice is in LICENSE-ColorConversions.txt (design §9).
    private static double Bezier(double t, double p0, double p1, double p2)
    {
        double k = 1 - t;
        return (k * k * p0) + (2 * (1 - t) * t * p1) + (t * t * p2);
    }

    // palettes.ts's sRGB_to_hex: floor(256 x), 0 below 0 and 255 from 1.
    private static Color ToColor(Lab lab)
    {
        (double r, double g, double b) = CieLab.ToSrgb(lab);
        return new Color(Channel(r), Channel(g), Channel(b));
    }

    private static byte Channel(double value) => value < 0 ? (byte)0 : (byte)Math.Floor(value >= 1.0 ? 255 : value * 256);
}
