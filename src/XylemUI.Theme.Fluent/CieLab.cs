namespace XylemUI.Theme.Fluent;

/// <summary>
/// Converts between gamma-encoded sRGB (D65) and CIE Lab (D50) with the matrices and constants of the Fluent theme designer's
/// <c>colors/csswg.ts</c> (microsoft/fluentui commit <c>1f59fc2</c>, lines 69–130, 359–384, 388–449, 736–774), so that the
/// lightness targets and the brand steps match the record <c>docs/benchmarks/th1-accent-ramp.md</c> (design §13.2,
/// "ブランドの 16 段の作り方"). Each step runs with the TypeScript's operations in the TypeScript's order. The last digit of a result
/// can still differ by math library: <see cref="Math.Pow(double, double)"/> and <see cref="Math.Cbrt"/> are not correctly rounded,
/// and under Node v22 seven of the sixteen lightness targets differ from Windows' in their last digit (PR #929's review).
/// </summary>
/// <remarks>
/// tools/FluentTokensTool compiles this file too: the lightness targets it writes into the generated tokens come from this
/// conversion, rounded to a fixed number of decimals so that the generated file does not depend on the math library (design §13.2,
/// step 4). A matrix of seven digits (Lindbloom's table) moves the fourth decimal of a target (PR #900's
/// review), so the matrices are csswg.ts's, digit for digit. csswg.ts comes from the sample code of CSS Color 4 (W3C Software and
/// Document License) and Lea Verou's css.land (MIT); their notices are in LICENSE-ColorConversions.txt (design §9).
/// </remarks>
internal static class CieLab
{
    // The D50 white of csswg.ts's XYZ_to_Lab and Lab_to_XYZ.
    private const double WhiteX = 0.96422;
    private const double WhiteY = 1.0;
    private const double WhiteZ = 0.82521;

    // 216 / 24389 and 24389 / 27, as csswg.ts writes them.
    private const double Epsilon = 216.0 / 24389;
    private const double Kappa = 24389.0 / 27;

    /// <summary>
    /// Returns the Lab of a gamma-encoded sRGB color (csswg.ts's <c>sRGB_to_LAB</c>).
    /// </summary>
    /// <param name="r">The red channel, 0 to 1.</param>
    /// <param name="g">The green channel, 0 to 1.</param>
    /// <param name="b">The blue channel, 0 to 1.</param>
    /// <returns>The color in Lab.</returns>
    public static Lab FromSrgb(double r, double g, double b)
    {
        double lr = Linear(r);
        double lg = Linear(g);
        double lb = Linear(b);

        // lin_sRGB_to_XYZ (D65).
        double x65 = Dot(0.41239079926595934, 0.357584339383878, 0.1804807884018343, lr, lg, lb);
        double y65 = Dot(0.21263900587151027, 0.715168678767756, 0.07219231536073371, lr, lg, lb);
        double z65 = Dot(0.01933081871559182, 0.11919477979462598, 0.9505321522496607, lr, lg, lb);

        // D65_to_D50 (Bradford).
        double x = Dot(1.0479298208405488, 0.022946793341019088, -0.05019222954313557, x65, y65, z65);
        double y = Dot(0.029627815688159344, 0.990434484573249, -0.01707382502938514, x65, y65, z65);
        double z = Dot(-0.009243058152591178, 0.015055144896577895, 0.7518742899580008, x65, y65, z65);

        // XYZ_to_Lab.
        double fx = LabF(x / WhiteX);
        double fy = LabF(y / WhiteY);
        double fz = LabF(z / WhiteZ);
        return new Lab((116 * fy) - 16, 500 * (fx - fy), 200 * (fy - fz));
    }

    /// <summary>
    /// Returns the gamma-encoded sRGB of a Lab color (csswg.ts's <c>LAB_to_sRGB</c>). A color outside sRGB has a channel below 0 or
    /// above 1.
    /// </summary>
    /// <param name="lab">The color in Lab.</param>
    /// <returns>The red, green, and blue channels.</returns>
    public static (double R, double G, double B) ToSrgb(Lab lab)
    {
        // Lab_to_XYZ (D50).
        double f1 = (lab.L + 16) / 116;
        double f0 = (lab.A / 500) + f1;
        double f2 = f1 - (lab.B / 200);
        double f0Cubed = Math.Pow(f0, 3);
        double f2Cubed = Math.Pow(f2, 3);
        double x = (f0Cubed > Epsilon ? f0Cubed : ((116 * f0) - 16) / Kappa) * WhiteX;
        double y = (lab.L > Kappa * Epsilon ? Math.Pow((lab.L + 16) / 116, 3) : lab.L / Kappa) * WhiteY;
        double z = (f2Cubed > Epsilon ? f2Cubed : ((116 * f2) - 16) / Kappa) * WhiteZ;

        // D50_to_D65 (Bradford).
        double x65 = Dot(0.9554734527042182, -0.023098536874261423, 0.0632593086610217, x, y, z);
        double y65 = Dot(-0.028369706963208136, 1.0099954580058226, 0.021041398966943008, x, y, z);
        double z65 = Dot(0.012314001688319899, -0.020507696433477912, 1.3303659366080753, x, y, z);

        // XYZ_to_lin_sRGB.
        double lr = Dot(3.2409699419045226, -1.537383177570094, -0.4986107602930034, x65, y65, z65);
        double lg = Dot(-0.9692436362808796, 1.8759675015077202, 0.04155505740717559, x65, y65, z65);
        double lb = Dot(0.05563007969699366, -0.20397695888897652, 1.0569715142428786, x65, y65, z65);
        return (Gamma(lr), Gamma(lg), Gamma(lb));
    }

    /// <summary>
    /// Returns the lightness, chroma, and hue in degrees of a Lab color (csswg.ts's <c>Lab_to_LCH</c>).
    /// </summary>
    /// <param name="lab">The color in Lab.</param>
    /// <returns>L*, C*, and the hue from 0 up to 360.</returns>
    public static (double L, double C, double H) ToLch(Lab lab)
    {
        double hue = Math.Atan2(lab.B, lab.A) * 180 / Math.PI;
        return (lab.L, Math.Sqrt(Math.Pow(lab.A, 2) + Math.Pow(lab.B, 2)), hue >= 0 ? hue : hue + 360);
    }

    /// <summary>
    /// Returns the Lab color of a lightness, chroma, and hue in degrees (csswg.ts's <c>LCH_to_Lab</c>).
    /// </summary>
    /// <param name="l">L*.</param>
    /// <param name="c">C*.</param>
    /// <param name="h">The hue in degrees.</param>
    /// <returns>The color in Lab.</returns>
    public static Lab FromLch(double l, double c, double h) => new(l, c * Math.Cos(h * Math.PI / 180), c * Math.Sin(h * Math.PI / 180));

    /// <summary>
    /// Returns a Lab color inside sRGB: the color itself when it is inside, or else the color of the same lightness and hue whose
    /// chroma a binary search to a width of 0.0001 put on the edge (csswg.ts's <c>snap_into_gamut</c>).
    /// </summary>
    /// <param name="lab">The color in Lab.</param>
    /// <returns>The color inside sRGB.</returns>
    public static Lab SnapIntoGamut(Lab lab)
    {
        const double Width = 0.0001;
        (double l, double c, double h) = ToLch(lab);
        if (IsInside(l, c, h))
        {
            return lab;
        }

        double high = c;
        double low = 0;
        c /= 2;
        while (high - low > Width)
        {
            if (IsInside(l, c, h))
            {
                low = c;
            }
            else
            {
                high = c;
            }

            c = (high + low) / 2;
        }

        return FromLch(l, c, h);
    }

    // A row of a matrix times a vector, summed as csswg.ts's multiplyMatrices does: from 0, left to right. Starting from +0 keeps the
    // sign of a zero sum the same as the TypeScript's.
    private static double Dot(double m0, double m1, double m2, double x, double y, double z) => 0.0 + (m0 * x) + (m1 * y) + (m2 * z);

    // csswg.ts's lin_sRGB, with its extension below 0.
    private static double Linear(double value)
    {
        double sign = value < 0 ? -1 : 1;
        double abs = Math.Abs(value);
        return abs < 0.04045 ? value / 12.92 : sign * Math.Pow((abs + 0.055) / 1.055, 2.4);
    }

    // csswg.ts's gam_sRGB.
    private static double Gamma(double value)
    {
        double sign = value < 0 ? -1 : 1;
        double abs = Math.Abs(value);
        return abs > 0.0031308 ? sign * ((1.055 * Math.Pow(abs, 1 / 2.4)) - 0.055) : 12.92 * value;
    }

    private static double LabF(double value) => value > Epsilon ? Math.Cbrt(value) : ((Kappa * value) + 16) / 116;

    // csswg.ts's is_LCH_inside_sRGB: each channel from -0.000005 to 1.000005.
    private static bool IsInside(double l, double c, double h)
    {
        const double Margin = 0.000005;
        (double r, double g, double b) = ToSrgb(FromLch(l, c, h));
        return r >= -Margin && r <= 1 + Margin && g >= -Margin && g <= 1 + Margin && b >= -Margin && b <= 1 + Margin;
    }
}
