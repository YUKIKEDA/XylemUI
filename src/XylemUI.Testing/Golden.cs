using System.Globalization;
using System.Reflection;

namespace XylemUI.Testing;

/// <summary>
/// How far a screenshot may be from its golden (design §12.3, "Comparison"): a channel may differ by up to <see cref="Channel"/>,
/// and up to <see cref="Pixels"/> pixels may differ by more. The default is ±2 a channel and no pixel beyond it: WARP gives the same
/// pixels for the same version, and Zed's runner allows ±2.
/// </summary>
/// <param name="Channel">The difference allowed in each of red, green, blue, and alpha; 0 to 255.</param>
/// <param name="Pixels">The pixels allowed to differ by more; 0 or more.</param>
public readonly record struct GoldenTolerance(int Channel = 2, int Pixels = 0)
{
    /// <summary>
    /// Gets the default: ±2 a channel, and no pixel beyond it.
    /// </summary>
    public static GoldenTolerance Default { get; } = new(2, 0);
}

/// <summary>
/// Thrown when a screenshot does not match its golden, or the golden is missing (design §12.3).
/// </summary>
public sealed class GoldenMismatchException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GoldenMismatchException"/> class.
    /// </summary>
    public GoldenMismatchException() { }

    /// <summary>
    /// Initializes a new instance of the <see cref="GoldenMismatchException"/> class with what did not match.
    /// </summary>
    /// <param name="message">The golden, the counts, and where the failure's images are.</param>
    public GoldenMismatchException(string message)
        : base(message) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="GoldenMismatchException"/> class with what did not match and a cause.
    /// </summary>
    /// <param name="message">The golden, the counts, and where the failure's images are.</param>
    /// <param name="innerException">The cause.</param>
    public GoldenMismatchException(string message, Exception innerException)
        : base(message, innerException) { }
}

/// <summary>
/// Finds, compares, and updates the goldens of a test project (design §12.3).
/// </summary>
internal static class Goldens
{
    /// <summary>
    /// The name of the assembly metadata that holds the test project's folder; <c>XylemUI.Testing.targets</c> writes it.
    /// </summary>
    public const string ProjectDirectoryKey = "XylemUI.Testing.ProjectDirectory";

    /// <summary>
    /// The environment variable that writes the goldens instead of comparing.
    /// </summary>
    public const string UpdateVariable = "XYLEMUI_UPDATE_GOLDENS";

    /// <summary>
    /// Gets or sets the folder of the goldens, for the tests of the comparison; <see langword="null"/> reads it from the test
    /// assembly.
    /// </summary>
    internal static string? RootForTests { get; set; }

    /// <summary>
    /// Gets or sets whether the goldens are written, for the tests of the comparison; <see langword="null"/> reads
    /// <see cref="UpdateVariable"/>.
    /// </summary>
    internal static bool? UpdateForTests { get; set; }

    /// <summary>
    /// Gets the folder the failures' images go to: the test's output folder's <c>GoldenFailures</c>.
    /// </summary>
    public static string FailureFolder => FailureFolderForTests ?? Path.Combine(AppContext.BaseDirectory, "GoldenFailures");

    /// <summary>
    /// Gets or sets the folder the failures' images go to, for the tests of the comparison, whose failures are meant: they stay out
    /// of the folder the CI uploads (PR #469's review). <see langword="null"/> for the test's output folder's.
    /// </summary>
    internal static string? FailureFolderForTests { get; set; }

    /// <summary>
    /// Compares a screenshot with its golden, or writes the golden when asked to.
    /// </summary>
    /// <param name="name">The golden's file name under <c>Goldens/</c>, with <c>.png</c>.</param>
    /// <param name="pixels">The screenshot's colors.</param>
    /// <param name="width">Its width.</param>
    /// <param name="height">Its height.</param>
    /// <param name="tolerance">How far it may be.</param>
    /// <returns>A task that completes when the comparison or the update is done.</returns>
    /// <exception cref="GoldenMismatchException">The golden is missing or does not match.</exception>
    public static async Task MatchAsync(string name, Color[] pixels, int width, int height, GoldenTolerance tolerance)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (tolerance.Channel is < 0 or > 255 || tolerance.Pixels < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tolerance), tolerance, "The channel is 0 to 255, and the pixels 0 or more.");
        }

        string path = Path.Combine(Root(), name);
        if (UpdateForTests ?? Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, Png.Encode(pixels, width, height)).ConfigureAwait(true);
            DeleteFailures(name);
            return;
        }

        if (!File.Exists(path))
        {
            string actual = await WriteFailureAsync(name, string.Empty, Png.Encode(pixels, width, height)).ConfigureAwait(true);
            throw new GoldenMismatchException(
                $"The golden {path} is missing. The screenshot is at {actual}; set {UpdateVariable}=1 to write the goldens."
            );
        }

        byte[] file = await File.ReadAllBytesAsync(path).ConfigureAwait(true);
        Color[] golden =
            Png.Decode(file, out int goldenWidth, out int goldenHeight)
            ?? throw new GoldenMismatchException($"The golden {path} is not a PNG this harness reads.");
        if (goldenWidth != width || goldenHeight != height)
        {
            string actual = await WriteFailureAsync(name, string.Empty, Png.Encode(pixels, width, height)).ConfigureAwait(true);
            throw new GoldenMismatchException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The screenshot is {width} × {height} and the golden {path} is {goldenWidth} × {goldenHeight}. The screenshot is at {actual}."
                )
            );
        }

        (int beyond, int largest, Color[] diff) = Compare(pixels, golden, tolerance.Channel);
        if (beyond <= tolerance.Pixels)
        {
            // An image of an earlier failure of this golden is old now; left there, it would read as this run's.
            DeleteFailures(name);
            return;
        }

        string actualPath = await WriteFailureAsync(name, string.Empty, Png.Encode(pixels, width, height)).ConfigureAwait(true);
        string diffPath = await WriteFailureAsync(name, ".diff", Png.Encode(diff, width, height)).ConfigureAwait(true);
        throw new GoldenMismatchException(
            string.Create(
                CultureInfo.InvariantCulture,
                $"{beyond} pixels of {width * height} differ from the golden {path} by more than {tolerance.Channel} in a channel (at most {largest}); {tolerance.Pixels} may. The screenshot is at {actualPath}, the difference at {diffPath} (red where it is beyond)."
            )
        );
    }

    /// <summary>
    /// Counts the pixels that differ by more than <paramref name="channel"/> in a channel, and makes the image of the difference:
    /// red where a pixel is beyond, and the golden, faded, where it is not.
    /// </summary>
    /// <param name="actual">The screenshot's colors.</param>
    /// <param name="golden">The golden's colors, as many.</param>
    /// <param name="channel">The difference allowed in a channel.</param>
    /// <returns>The pixels beyond, the largest difference in a channel, and the image.</returns>
    internal static (int Beyond, int Largest, Color[] Diff) Compare(ReadOnlySpan<Color> actual, ReadOnlySpan<Color> golden, int channel)
    {
        int beyond = 0;
        int largest = 0;
        var diff = new Color[actual.Length];
        for (int i = 0; i < actual.Length; i++)
        {
            Color a = actual[i];
            Color g = golden[i];
            int d = Math.Max(Math.Max(Math.Abs(a.R - g.R), Math.Abs(a.G - g.G)), Math.Max(Math.Abs(a.B - g.B), Math.Abs(a.A - g.A)));
            largest = Math.Max(largest, d);
            if (d > channel)
            {
                beyond++;
                diff[i] = new Color(255, 0, 0);
            }
            else
            {
                diff[i] = new Color((byte)(128 + (g.R / 2)), (byte)(128 + (g.G / 2)), (byte)(128 + (g.B / 2)));
            }
        }

        return (beyond, largest, diff);
    }

    // The test project's Goldens folder, from the metadata its build wrote (design §12.3: [CallerFilePath] names no file on disk in
    // the CI's deterministic builds).
    private static string Root()
    {
        if (RootForTests is { } root)
        {
            return root;
        }

        Assembly? tests = Assembly.GetEntryAssembly();
        foreach (AssemblyMetadataAttribute metadata in tests?.GetCustomAttributes<AssemblyMetadataAttribute>() ?? [])
        {
            if (metadata.Key == ProjectDirectoryKey && metadata.Value is { Length: > 0 } folder)
            {
                return Path.Combine(folder, "Goldens");
            }
        }

        throw new InvalidOperationException(
            $"The test assembly has no {ProjectDirectoryKey} metadata: its project imports XylemUI.Testing.targets, which the package adds."
        );
    }

    private static void DeleteFailures(string name)
    {
        string stem = Path.Combine(FailureFolder, Path.ChangeExtension(name, null));
        if (!Directory.Exists(Path.GetDirectoryName(stem)))
        {
            return;
        }

        File.Delete(stem + ".png");
        File.Delete(stem + ".diff.png");
    }

    private static async Task<string> WriteFailureAsync(string name, string suffix, byte[] png)
    {
        string path = Path.Combine(FailureFolder, Path.ChangeExtension(name, null) + suffix + ".png");
        _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, png).ConfigureAwait(true);
        return path;
    }
}
