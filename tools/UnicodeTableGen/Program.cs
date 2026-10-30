// Generates the Unicode tables of XylemUI.Text from the Unicode Character Database (design §4.1, "Line breaking and grapheme
// clusters"; §8.13). The version and the SHA-256 of UCD.zip are the unicode-ucd line of vendor/versions.lock.
//
//   dotnet run --project tools/UnicodeTableGen -c Release
//
// Writes src/XylemUI.Text/Unicode/UnicodeData.g.cs and copies the conformance tests (GraphemeBreakTest.txt, LineBreakTest.txt,
// WordBreakTest.txt)
// to tests/XylemUI.Text.Tests/Unicode/. Run `dotnet csharpier format src/XylemUI.Text/Unicode` after it. The output is committed;
// the gate does not run this tool.
using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

string root = FindRoot();
(string version, string sha256, string url) = ReadLock(root);
byte[] zipBytes = await Download(root, version, sha256, url);
using var zip = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);

// The line break classes, in the order PropertyValueAliases.txt lists them.
List<string> lineBreakNames = ReadAliases(Read(zip, "PropertyValueAliases.txt"), "lb");
string[] graphemeNames =
[
    "Other",
    "CR",
    "LF",
    "Control",
    "Extend",
    "ZWJ",
    "Regional_Indicator",
    "Prepend",
    "SpacingMark",
    "L",
    "V",
    "T",
    "LV",
    "LVT",
];
string[] wordNames =
[
    "Other",
    "CR",
    "LF",
    "Newline",
    "Extend",
    "ZWJ",
    "Regional_Indicator",
    "Format",
    "Katakana",
    "Hebrew_Letter",
    "ALetter",
    "Single_Quote",
    "Double_Quote",
    "MidNumLet",
    "MidLetter",
    "MidNum",
    "Numeric",
    "ExtendNumLet",
    "WSegSpace",
];
string[] indicNames = ["None", "Linker", "Consonant", "Extend"];
string[] categoryNames = ["Other", "Pi", "Pf", "Mn", "Mc", "Cn"];
if (lineBreakNames.Count > 64)
{
    throw new InvalidOperationException($"{lineBreakNames.Count} line break classes do not fit in 6 bits.");
}

const int CodePoints = 0x110000;
byte[] lineBreak = new byte[CodePoints];
byte[] grapheme = new byte[CodePoints];
bool[] pictographic = new bool[CodePoints];
bool[] eastAsian = new bool[CodePoints];
byte[] indic = new byte[CodePoints];
byte[] category = new byte[CodePoints];
byte[] word = new byte[CodePoints];

Fill(Read(zip, "LineBreak.txt"), null, value => (byte)IndexOf(lineBreakNames, value), lineBreak);
Fill(Read(zip, "auxiliary/GraphemeBreakProperty.txt"), null, value => (byte)IndexOf(graphemeNames, value), grapheme);
Fill(Read(zip, "auxiliary/WordBreakProperty.txt"), null, value => (byte)IndexOf(wordNames, value), word);
FillFlag(Read(zip, "emoji/emoji-data.txt"), "Extended_Pictographic", pictographic);
var eastAsianValues = new byte[CodePoints];
Fill(Read(zip, "EastAsianWidth.txt"), null, value => value is "F" or "W" or "H" ? (byte)1 : (byte)0, eastAsianValues);
for (int c = 0; c < CodePoints; c++)
{
    eastAsian[c] = eastAsianValues[c] == 1;
}

Fill(Read(zip, "DerivedCoreProperties.txt"), "InCB", value => (byte)IndexOf(indicNames, value), indic);
Fill(
    Read(zip, "extracted/DerivedGeneralCategory.txt"),
    null,
    value => Array.IndexOf(categoryNames, value) is int i and > 0 ? (byte)i : (byte)0,
    category
);

// One 32-bit value a code point: the line break class (6 bits), the grapheme cluster break (4), Extended_Pictographic (1), an East
// Asian width of F, W, or H (1), Indic_Conjunct_Break (2), the general categories the rules read (3), and the word break (5).
uint[] values = new uint[CodePoints];
for (int c = 0; c < CodePoints; c++)
{
    values[c] =
        lineBreak[c]
        | ((uint)grapheme[c] << 6)
        | (pictographic[c] ? 1u << 10 : 0)
        | (eastAsian[c] ? 1u << 11 : 0)
        | ((uint)indic[c] << 12)
        | ((uint)category[c] << 14)
        | ((uint)word[c] << 17);
}

// The distinct values are few (150 in Unicode 18.0.0, with the word breaks): the second stage holds a byte, the index of a value in a palette.
uint[] palette = [.. values.Distinct().Order()];
if (palette.Length > 256)
{
    throw new InvalidOperationException($"{palette.Length} distinct values do not fit an index of a byte.");
}

var indexOf = new Dictionary<uint, byte>();
for (int i = 0; i < palette.Length; i++)
{
    indexOf[palette[i]] = (byte)i;
}

byte[] indices = [.. values.Select(v => indexOf[v])];

// Two-stage tables for each block size: their bytes, and the time to look up a code point of mixed text.
int[] sample = Sample();
Console.WriteLine($"{palette.Length} distinct values");
Console.WriteLine("shift  block  stage1  blocks  bytes      ns/lookup");
(int Shift, ushort[] Stage1, byte[] Stage2, int Bytes) best = (0, [], [], int.MaxValue);
for (int shift = 4; shift <= 10; shift++)
{
    (ushort[] stage1, byte[] stage2) = TwoStage(indices, shift);
    int bytes = (stage1.Length * 2) + stage2.Length + (palette.Length * 4);
    double ns = TimeLookups(stage1, stage2, palette, shift, sample);
    string[] row = [$"{shift}", $"{1 << shift}", $"{stage1.Length}", $"{stage2.Length >> shift}", $"{bytes:N0}", $"{ns:F2}"];
    int[] widths = [5, 5, 6, 6, 9, 9];
    Console.WriteLine(string.Join("  ", row.Select((cell, i) => cell.PadLeft(widths[i]))));
    if (bytes < best.Bytes)
    {
        best = (shift, stage1, stage2, bytes);
    }
}

Console.WriteLine($"chosen: shift {best.Shift}, {best.Bytes:N0} bytes (the smallest)");
string output = Path.Combine(root, "src", "XylemUI.Text", "Unicode", "UnicodeData.g.cs");
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
File.WriteAllText(
    output,
    Emit(version, best.Shift, best.Stage1, best.Stage2, palette, lineBreakNames, graphemeNames, indicNames, categoryNames, wordNames)
);
Console.WriteLine($"wrote {output}");

string tests = Path.Combine(root, "tests", "XylemUI.Text.Tests", "Unicode");
Directory.CreateDirectory(tests);
foreach (string name in new[] { "GraphemeBreakTest.txt", "LineBreakTest.txt", "WordBreakTest.txt" })
{
    File.WriteAllText(Path.Combine(tests, name), Read(zip, "auxiliary/" + name));
    Console.WriteLine($"copied {name}");
}

return 0;

static string FindRoot()
{
    for (string? directory = Directory.GetCurrentDirectory(); directory is not null; directory = Path.GetDirectoryName(directory))
    {
        if (File.Exists(Path.Combine(directory, "XylemUI.slnx")))
        {
            return directory;
        }
    }

    throw new InvalidOperationException("Run the tool inside the repository: no XylemUI.slnx above the current directory.");
}

static (string Version, string Sha256, string Url) ReadLock(string root)
{
    foreach (string line in File.ReadLines(Path.Combine(root, "vendor", "versions.lock")))
    {
        string[] fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length >= 4 && fields[0] == "unicode-ucd")
        {
            return (fields[1], fields[2].ToLowerInvariant(), fields[3]);
        }
    }

    throw new InvalidOperationException("vendor/versions.lock has no unicode-ucd line.");
}

static async Task<byte[]> Download(string root, string version, string sha256, string url)
{
    string cache = Path.Combine(root, "vendor", ".cache");
    Directory.CreateDirectory(cache);
    string path = Path.Combine(cache, $"unicode-ucd-{version}.zip");
    if (!File.Exists(path))
    {
        using var client = new HttpClient();
        await File.WriteAllBytesAsync(path, await client.GetByteArrayAsync(new Uri(url)));
    }

    byte[] bytes = await File.ReadAllBytesAsync(path);
    string actual = Convert.ToHexStringLower(SHA256.HashData(bytes));
    if (actual != sha256)
    {
        File.Delete(path);
        throw new InvalidOperationException($"UCD.zip has SHA-256 {actual}, not {sha256} (vendor/versions.lock).");
    }

    return bytes;
}

static string Read(ZipArchive zip, string name)
{
    ZipArchiveEntry entry = zip.GetEntry(name) ?? throw new InvalidOperationException($"UCD.zip has no {name}.");
    using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
    return reader.ReadToEnd();
}

static List<string> ReadAliases(string text, string property)
{
    var names = new List<string>();
    foreach (string raw in text.Split('\n'))
    {
        string line = raw.Split('#')[0];
        string[] fields = line.Split(';', StringSplitOptions.TrimEntries);
        if (fields.Length >= 3 && fields[0] == property)
        {
            names.Add(fields[1]);
        }
    }

    return names;
}

static int IndexOf(IReadOnlyList<string> names, string value)
{
    for (int i = 0; i < names.Count; i++)
    {
        if (names[i] == value)
        {
            return i;
        }
    }

    throw new InvalidOperationException($"The value {value} is not one the tables know; the tool needs to learn it.");
}

// A property file's values: first its @missing lines, in order, then its data lines. With `property`, only the lines of that
// property (the second field), whose value is the third field.
static void Fill(string text, string? property, Func<string, byte> map, byte[] target)
{
    var data = new List<(int First, int Last, string Value)>();
    foreach (string raw in text.Split('\n'))
    {
        string line = raw.Trim();
        bool missing = line.StartsWith("# @missing:", StringComparison.Ordinal);
        if (missing)
        {
            line = line["# @missing:".Length..];
        }
        else
        {
            line = line.Split('#')[0];
        }

        string[] fields = line.Split(';', StringSplitOptions.TrimEntries);
        if (fields.Length < 2 || fields[0].Length == 0)
        {
            continue;
        }

        string value;
        if (property is null)
        {
            value = fields[1];
        }
        else if (fields.Length >= 3 && fields[1] == property)
        {
            value = fields[2];
        }
        else
        {
            continue;
        }

        (int first, int last) = Range(fields[0]);
        if (missing)
        {
            Array.Fill(target, map(value), first, last - first + 1);
        }
        else
        {
            data.Add((first, last, value));
        }
    }

    foreach ((int first, int last, string value) in data)
    {
        Array.Fill(target, map(value), first, last - first + 1);
    }
}

static void FillFlag(string text, string property, bool[] target)
{
    foreach (string raw in text.Split('\n'))
    {
        string[] fields = raw.Split('#')[0].Split(';', StringSplitOptions.TrimEntries);
        if (fields.Length >= 2 && fields[1] == property)
        {
            (int first, int last) = Range(fields[0]);
            Array.Fill(target, true, first, last - first + 1);
        }
    }
}

static (int First, int Last) Range(string field)
{
    string[] ends = field.Split("..");
    int first = int.Parse(ends[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    int last = ends.Length > 1 ? int.Parse(ends[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture) : first;
    return (first, last);
}

static (ushort[] Stage1, byte[] Stage2) TwoStage(byte[] values, int shift)
{
    int size = 1 << shift;
    var stage1 = new ushort[values.Length >> shift];
    var stage2 = new List<byte>();
    var blocks = new Dictionary<string, ushort>();
    for (int b = 0; b < stage1.Length; b++)
    {
        ReadOnlySpan<byte> block = values.AsSpan(b << shift, size);
        string key = Convert.ToBase64String(block);
        if (!blocks.TryGetValue(key, out ushort index))
        {
            index = checked((ushort)blocks.Count);
            blocks.Add(key, index);
            stage2.AddRange(block);
        }

        stage1[b] = index;
    }

    return (stage1, [.. stage2]);
}

// Code points of the text the tables are read for: ASCII, Japanese (kana and kanji), and emoji, interleaved.
static int[] Sample()
{
    var random = new Random(1);
    var sample = new int[1 << 16];
    for (int i = 0; i < sample.Length; i++)
    {
        sample[i] = (i % 4) switch
        {
            0 => random.Next(0x20, 0x7F),
            1 => random.Next(0x3041, 0x3097),
            2 => random.Next(0x4E00, 0x9FFF),
            _ => random.Next(0x1F300, 0x1FAFF),
        };
    }

    return sample;
}

static double TimeLookups(ushort[] stage1, byte[] stage2, uint[] palette, int shift, int[] sample)
{
    int mask = (1 << shift) - 1;
    uint sum = 0;
    var times = new List<double>();
    for (int round = 0; round < 7; round++)
    {
        long start = Stopwatch.GetTimestamp();
        for (int repeat = 0; repeat < 64; repeat++)
        {
            foreach (int c in sample)
            {
                sum += palette[stage2[(stage1[c >> shift] << shift) | (c & mask)]];
            }
        }

        times.Add(Stopwatch.GetElapsedTime(start).TotalNanoseconds / (64.0 * sample.Length));
    }

    times.Sort();
    return sum == 42 ? -1 : times[times.Count / 2];
}

static string Emit(
    string version,
    int shift,
    ushort[] stage1,
    byte[] stage2,
    uint[] palette,
    List<string> lineBreakNames,
    string[] graphemeNames,
    string[] indicNames,
    string[] categoryNames,
    string[] wordNames
)
{
    var text = new StringBuilder();
    text.AppendLine("// <auto-generated>");
    text.AppendLine(
        CultureInfo.InvariantCulture,
        $"// Generated by tools/UnicodeTableGen from the Unicode Character Database {version} (vendor/versions.lock)."
    );
    text.AppendLine("// Do not edit: run the tool again.");
    text.AppendLine("// </auto-generated>");
    text.AppendLine("namespace XylemUI.Text.Unicode;");
    text.AppendLine();
    text.AppendLine("internal static partial class UnicodeData");
    text.AppendLine("{");
    text.AppendLine(CultureInfo.InvariantCulture, $"    public const string Version = \"{version}\";");
    text.AppendLine(CultureInfo.InvariantCulture, $"    private const int Shift = {shift};");

    // Every table is bytes: a span of bytes points into the executable's data in every build, where a span of a wider type is an
    // array made at each read in a Debug build. Stage1 and Palette are little-endian.
    text.Append("    private static ReadOnlySpan<byte> Stage1 => [");
    text.AppendJoin(", ", stage1.SelectMany(v => BitConverter.GetBytes(v)));
    text.AppendLine("];");
    text.Append("    private static ReadOnlySpan<byte> Stage2 => [");
    text.AppendJoin(", ", stage2);
    text.AppendLine("];");
    text.Append("    private static ReadOnlySpan<byte> Palette => [");
    text.AppendJoin(", ", palette.SelectMany(v => BitConverter.GetBytes(v)));
    text.AppendLine("];");
    text.AppendLine("}");
    AppendEnum(text, "LineBreakClass", lineBreakNames);
    AppendEnum(text, "GraphemeClusterBreak", graphemeNames);
    AppendEnum(text, "IndicConjunctBreak", indicNames);
    AppendEnum(text, "CategoryOfInterest", categoryNames);
    AppendEnum(text, "WordBreak", wordNames);
    return text.ToString();
}

static void AppendEnum(StringBuilder text, string name, IReadOnlyList<string> members)
{
    text.AppendLine();
    text.AppendLine(CultureInfo.InvariantCulture, $"internal enum {name} : byte");
    text.AppendLine("{");
    for (int i = 0; i < members.Count; i++)
    {
        text.AppendLine(CultureInfo.InvariantCulture, $"    {members[i]} = {i},");
    }

    text.AppendLine("}");
}
