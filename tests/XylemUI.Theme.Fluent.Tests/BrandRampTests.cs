using System.Globalization;

namespace XylemUI.Theme.Fluent.Tests;

// The 16 brand steps made from one accent color (design §13.2, "ブランドの 16 段の作り方"). The expected values are those of the
// record docs/benchmarks/th1-accent-ramp.md, which ran the TypeScript of the Fluent theme designer at microsoft/fluentui 1f59fc2.
public sealed class BrandRampTests
{
    /// <summary>
    /// Each key gives the 16 steps the record gives for it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The keys of the record: the measured blue #0078D4, yellow #FFB900, red #E81123, teal #00B294, grey #767676, black #000000,
    ///   white #FFFFFF, brandWeb's step 80 #0F6CBD, and #A1BFDE (the one color the OS gives in high contrast).
    ///
    /// Steps:
    /// - Make the steps of each key.
    ///
    /// Expected:
    /// - The hex of the 16 steps, 10 to 160, is the record's for that key ("キーごとの 16 段" and the list of (d)); grey, black, and
    ///   white give the same steps; #0F6CBD's step 80 is the key itself.
    /// </remarks>
    /// <param name="key">The key, as #RRGGBB.</param>
    /// <param name="expected">The record's 16 steps, separated by spaces.</param>
    [Theory]
    [InlineData(
        "#0078D4",
        "#00162f #002243 #002d55 #00396b #004680 #005396 #005ea8 #006cc0 #2e85e1 #5e9cf0 #75a8f6 #89b3fa #a3c3fe #bed3ff #d5e2ff #edf3ff"
    )]
    [InlineData(
        "#FFB900",
        "#1c1300 #2b1e00 #392800 #493300 #593e00 #694a00 #765400 #886100 #a77800 #c48d00 #d49900 #e3a400 #f8b300 #ffc860 #ffdca2 #fff0d9"
    )]
    [InlineData(
        "#E81123",
        "#290b00 #440900 #5d0000 #740008 #8a000e #a20013 #b60016 #cf001b #f42b2d #ff6250 #ff7c68 #ff917e #ffaa9a #ffc3b7 #ffd8d0 #ffefeb"
    )]
    [InlineData(
        "#00B294",
        "#001913 #00261e #003228 #004034 #004d3f #005c4b #006755 #007762 #00937a #00ad8f #21ba9c #44c5a8 #6dd3b9 #96e1cc #baecdd #e1f7f0"
    )]
    [InlineData(
        "#767676",
        "#151515 #202020 #2b2b2b #373737 #434343 #505050 #5a5a5a #686868 #818181 #989898 #a5a5a5 #b1b1b1 #c1c1c1 #d2d2d2 #e1e1e1 #f2f2f2"
    )]
    [InlineData(
        "#000000",
        "#151515 #202020 #2b2b2b #373737 #434343 #505050 #5a5a5a #686868 #818181 #989898 #a5a5a5 #b1b1b1 #c1c1c1 #d2d2d2 #e1e1e1 #f2f2f2"
    )]
    [InlineData(
        "#FFFFFF",
        "#151515 #202020 #2b2b2b #373737 #434343 #505050 #5a5a5a #686868 #818181 #989898 #a5a5a5 #b1b1b1 #c1c1c1 #d2d2d2 #e1e1e1 #f2f2f2"
    )]
    [InlineData(
        "#0F6CBD",
        "#00162e #002243 #002d55 #003a6b #00467f #005395 #005ea8 #0f6cbd #4584d4 #6c9be3 #7fa7e9 #91b3ee #a9c3f4 #c1d3f8 #d7e2fb #eef3fe"
    )]
    [InlineData(
        "#A1BFDE",
        "#10161b #18222b #202d39 #293949 #324658 #3d5368 #465d74 #526b84 #6985a0 #7f9cb9 #8ba9c7 #97b5d3 #a7c5e4 #bdd5ef #d3e4f7 #ecf3fc"
    )]
    public void From_EachKeyOfTheRecord_GivesTheRecordsSteps(string key, string expected)
    {
        Color[] steps = BrandRamp.From(Parse(key));

        Assert.Equal(expected.Split(' '), steps.Select(Hex));
    }

    /// <summary>
    /// The key's alpha is not read.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The measured blue #0078D4, opaque and with an alpha of 0x40.
    ///
    /// Steps:
    /// - Make the steps of both.
    ///
    /// Expected:
    /// - The same 16 opaque steps.
    /// </remarks>
    [Fact]
    public void From_KeyWithAlpha_GivesTheOpaqueKeysSteps()
    {
        Color[] opaque = BrandRamp.From(new Color(0x00, 0x78, 0xD4));
        Color[] translucent = BrandRamp.From(new Color(0x00, 0x78, 0xD4, 0x40));

        Assert.Equal(opaque, translucent);
        Assert.All(translucent, step => Assert.Equal(255, step.A));
    }

    /// <summary>
    /// The lightness targets the tool generated are the record's, to four decimals: the tool converts with csswg.ts's matrices.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - FluentBrand.Lightness, which tools/FluentTokensTool generated from brandWeb at microsoft/fluentui 1f59fc2.
    ///
    /// Steps:
    /// - Round each of the 16 targets to four decimals.
    ///
    /// Expected:
    /// - 6.8804, 12.5995, 17.6508, 23.3234, 28.6248, 34.0902, 38.4971, 44.1490, 54.0170, 62.8844, 67.6112, 72.0205, 77.9129, 84.0472,
    ///   89.5312, 95.4287 (the record's table; Lindbloom's seven-digit matrices give 44.1486 for step 80).
    /// </remarks>
    [Fact]
    public void Lightness_OfBrandWeb_IsTheRecordsToFourDecimals()
    {
        string[] expected =
        [
            "6.8804",
            "12.5995",
            "17.6508",
            "23.3234",
            "28.6248",
            "34.0902",
            "38.4971",
            "44.1490",
            "54.0170",
            "62.8844",
            "67.6112",
            "72.0205",
            "77.9129",
            "84.0472",
            "89.5312",
            "95.4287",
        ];

        Assert.Equal(expected, FluentBrand.Lightness.ToArray().Select(target => target.ToString("F4", CultureInfo.InvariantCulture)));
    }

    /// <summary>
    /// The tool writes each lightness target with 10 decimals, so that the generated file does not depend on the last digit of the
    /// math library that ran the tool.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - FluentBrand.Lightness, which tools/FluentTokensTool generated.
    ///
    /// Steps:
    /// - Write each of the 16 targets as the shortest text that reads back as the same double.
    ///
    /// Expected:
    /// - No text has more than 10 decimals, and the 10-decimal text of step 80 is 44.1489690082 (Node v22 and Windows give
    ///   44.14896900820197 and 44.14896900820196 before rounding, PR #929's review).
    /// </remarks>
    [Fact]
    public void Lightness_IsWrittenWithTenDecimals()
    {
        double[] targets = FluentBrand.Lightness.ToArray();

        Assert.All(
            targets,
            target => Assert.InRange(target.ToString("R", CultureInfo.InvariantCulture).Split('.').ElementAtOrDefault(1)?.Length ?? 0, 0, 10)
        );
        Assert.Equal("44.1489690082", targets[7].ToString("F10", CultureInfo.InvariantCulture));
    }

    private static Color Parse(string hex) => Color.FromRgb(uint.Parse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture));

    private static string Hex(Color color) => string.Create(CultureInfo.InvariantCulture, $"#{color.R:x2}{color.G:x2}{color.B:x2}");
}
