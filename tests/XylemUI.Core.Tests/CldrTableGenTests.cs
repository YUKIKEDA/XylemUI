using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace XylemUI.Tests;

// The generator of the CLDR tables, tools/CldrTableGen (LC2, design §4.11, "How the tables are made" and "Tests"), run on a small
// zip of the json-full shape that the tests make: two runs write the same bytes, a locale takes the plural rules of its plural parent
// or of its truncated tag, a zip whose SHA-256 is not the lock's stops it, and so does a language of row LC4 with neither data nor
// an alias. The tables it wrote from the real zip are checked in
// LocaleDataTests and PluralRulesTests.
public sealed class CldrTableGenTests
{
    // The 82 languages of row LC4: Flutter 3.47.8's kMaterialSupportedLanguages
    // (packages/flutter_localizations/lib/src/l10n/generated_material_localizations.dart, lines 46518 to 46601).
    private static readonly string[] Lc4Languages = (
        "af am ar as az be bg bn bo bs ca cs cy da de el en es et eu fa fi fil fr ga gl gsw gu he hi hr hu hy id is it ja ka kk km kn ko ky "
        + "lo lt lv mk ml mn mr ms my nb ne nl no or pa pl ps pt ro ru si sk sl sq sr sv sw ta te th tl tr ug uk ur uz vi zh zu"
    ).Split(' ');

    /// <summary>
    /// Two runs of the generator from the same zip write the same files, byte for byte.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A zip of the json-full shape: the languages of row LC4 as modern locales with number and date files, but bo, which has the
    ///   files and no coverage level, and tl, which has neither and an alias to fil; ca-ES-valencia, modern with a variant; root.
    /// - A lock file whose cldr-json line has the zip's SHA-256.
    ///
    /// Steps:
    /// - Run the tool into two empty directories.
    ///
    /// Expected:
    /// - Both runs end with 0, and write the same paths with the same bytes.
    /// - The entries hold bo (added from its data) and tl (the alias), and not ca-ES-valencia.
    /// </remarks>
    [Fact]
    public void Tool_SameZipTwice_WritesTheSameBytes()
    {
        string directory = Directory.CreateTempSubdirectory("xylemui-cldr-").FullName;
        try
        {
            string zip = WriteZip(directory, withBo: true);
            string lockFile = WriteLock(directory, Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(zip))));
            string first = Path.Combine(directory, "first");
            string second = Path.Combine(directory, "second");

            (int firstExit, string firstOutput) = RunTool("--zip", zip, "--lock", lockFile, "--out", first);
            (int secondExit, string secondOutput) = RunTool("--zip", zip, "--lock", lockFile, "--out", second);

            Assert.True(firstExit == 0, firstOutput);
            Assert.True(secondExit == 0, secondOutput);
            string[] firstFiles = Files(first);
            Assert.Equal(firstFiles, Files(second));
            Assert.Contains("src/XylemUI.Core/Localization/Cldr/CldrTables.g.cs", firstFiles);
            Assert.Contains("tests/XylemUI.Core.Tests/CldrPluralSamples.g.cs", firstFiles);
            foreach (string file in firstFiles)
            {
                Assert.True(File.ReadAllBytes(Path.Combine(first, file)).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(second, file))), file);
            }

            string locales = File.ReadAllText(Path.Combine(first, "src/XylemUI.Core/Localization/Cldr/CldrLocales.g.cs"));
            Assert.Contains("public static LocaleData Bo => CldrBo.Instance;", locales, StringComparison.Ordinal);
            Assert.Contains("public static LocaleData Tl => CldrTl.Instance;", locales, StringComparison.Ordinal);
            Assert.DoesNotContain("CaESValencia", locales, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// A locale takes the plural rules of the parent that the plurals component of parentLocales.json names, and with none it
    /// truncates its tag; the main parent locales are not read (UTS #35 part 1, "Parent Locales": component parents stand alone).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The zip of the first test: pt-AO has no rules of its own, plurals.json has pt and pt-PT, and the main parent of pt-AO is
    ///   pt-PT. The plurals component's parents are the row's JSON object.
    ///
    /// Steps:
    /// - Run the tool, and read the line of pt-AO in the table of samples it wrote for the tests.
    ///
    /// Expected:
    /// - With no plural parents, pt-AO takes pt's rules (its tag truncated), not pt-PT's; with pt-PT as its plural parent, pt-PT's;
    ///   with root, und's. Its ordinal rules are und's in every row (ordinals.json has only und).
    /// </remarks>
    /// <param name="pluralParents">The plurals component's parent locales, as a JSON object.</param>
    /// <param name="key">The key of plurals.json whose rules pt-AO takes.</param>
    [Theory]
    [InlineData("{}", "pt")]
    [InlineData("{ \"pt-AO\": \"pt-PT\" }", "pt-PT")]
    [InlineData("{ \"pt-AO\": \"root\" }", "und")]
    public void Tool_PluralParents_AreFollowed_AndTruncatedWithout(string pluralParents, string key)
    {
        string directory = Directory.CreateTempSubdirectory("xylemui-cldr-").FullName;
        try
        {
            string zip = WriteZip(directory, withBo: true, pluralParents);
            string lockFile = WriteLock(directory, Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(zip))));
            string output = Path.Combine(directory, "out");

            (int exit, string text) = RunTool("--zip", zip, "--lock", lockFile, "--out", output);

            Assert.True(exit == 0, text);
            string samples = File.ReadAllText(Path.Combine(output, "tests/XylemUI.Core.Tests/CldrPluralSamples.g.cs"));
            string line = "        (\"pt-AO\", \"" + key + "\", \"und\"),";
            Assert.Contains(line, samples, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// A zip whose SHA-256 is not the one the lock pins stops the generator before it writes anything.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The zip of the test above, and a lock file whose cldr-json line names another SHA-256 (the zip's with its last digit
    ///   changed).
    ///
    /// Steps:
    /// - Run the tool into an empty directory.
    ///
    /// Expected:
    /// - It ends with 1, says the zip's SHA-256 is not the lock's, and the directory stays empty.
    /// </remarks>
    [Fact]
    public void Tool_ZipOfAnotherSha256_Stops()
    {
        string directory = Directory.CreateTempSubdirectory("xylemui-cldr-").FullName;
        try
        {
            string zip = WriteZip(directory, withBo: true);
            string actual = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(zip)));
            string other = actual[..^1] + (actual[^1] == '0' ? '1' : '0');
            string lockFile = WriteLock(directory, other);
            string output = Path.Combine(directory, "out");

            (int exit, string text) = RunTool("--zip", zip, "--lock", lockFile, "--out", output);

            Assert.Equal(1, exit);
            Assert.Contains("SHA-256 " + actual + ", not " + other, text, StringComparison.Ordinal);
            Assert.False(Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// A language of row LC4 that has neither number and date files nor an alias to a locale of the set stops the generator: the
    /// widgets' translations would have a language with no tables (design §4.11, "The set of locales").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The zip of the first test without bo's files, and a lock with its SHA-256.
    ///
    /// Steps:
    /// - Run the tool into an empty directory.
    ///
    /// Expected:
    /// - It ends with 1 and names bo.
    /// </remarks>
    [Fact]
    public void Tool_Lc4LanguageWithoutData_Stops()
    {
        string directory = Directory.CreateTempSubdirectory("xylemui-cldr-").FullName;
        try
        {
            string zip = WriteZip(directory, withBo: false);
            string lockFile = WriteLock(directory, Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(zip))));

            (int exit, string text) = RunTool("--zip", zip, "--lock", lockFile, "--out", Path.Combine(directory, "out"));

            Assert.Equal(1, exit);
            Assert.Contains("The language bo of row LC4", text, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    // A zip of the json-full shape, with the files the generator reads and nothing more. The number and date files are empty
    // objects: the generator reads only that they are there. pt-AO is a modern locale with no plural rules of its own; plurals.json
    // has rules for pt and for pt-PT, and the parent locales of the plurals component are the given JSON object.
    private static string WriteZip(string directory, bool withBo, string pluralParents = "{}")
    {
        string path = Path.Combine(directory, "cldr-test-json-full.zip");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            Add(zip, "LICENSE", "Test notice.\n");
            Add(zip, "cldr-core/package.json", "{ \"version\": \"49.0.0\" }");
            var levels = new StringBuilder(
                "{ \"effectiveCoverageLevels\": { \"und\": \"modern\", \"ca-ES-valencia\": \"modern\", \"pt-AO\": \"modern\""
            );
            foreach (string language in Lc4Languages.Where(language => language is not ("tl" or "bo")))
            {
                levels.Append(", \"").Append(language).Append("\": \"modern\"");
            }

            levels.Append(" } }");
            Add(zip, "cldr-core/coverageLevels.json", levels.ToString());
            foreach (
                string tag in Lc4Languages
                    .Where(language => language != "tl" && (withBo || language != "bo"))
                    .Append("und")
                    .Append("ca-ES-valencia")
                    .Append("pt-AO")
            )
            {
                Add(zip, "cldr-numbers-full/main/" + tag + "/numbers.json", "{}");
                Add(zip, "cldr-dates-full/main/" + tag + "/ca-gregorian.json", "{}");
            }

            Add(
                zip,
                "cldr-core/supplemental/aliases.json",
                "{ \"supplemental\": { \"metadata\": { \"alias\": { \"languageAlias\": { \"tl\": { \"_reason\": \"legacy\", \"_replacement\": \"fil\" } } } } } }"
            );
            Add(zip, "cldr-core/supplemental/likelySubtags.json", "{ \"supplemental\": { \"likelySubtags\": { \"ja\": \"ja-Jpan-JP\" } } }");
            const string Weeks =
                "{ \"supplemental\": { \"weekData\": { \"minDays\": { \"001\": \"1\" }, \"firstDay\": { \"001\": \"mon\", \"JP\": \"sun\" }, "
                + "\"weekendStart\": { \"001\": \"sat\" }, \"weekendEnd\": { \"001\": \"sun\" } } } }";
            Add(zip, "cldr-core/supplemental/weekData.json", Weeks);
            const string Cardinals =
                "{ \"supplemental\": { \"plurals-type-cardinal\": { \"und\": { \"pluralRule-count-other\": \" @integer 0~15\" }, "
                + "\"en\": { \"pluralRule-count-one\": \"i = 1 and v = 0 @integer 1\", \"pluralRule-count-other\": \" @integer 0, 2~16\" }, "
                + "\"pt\": { \"pluralRule-count-one\": \"i = 0..1 @integer 0, 1\", \"pluralRule-count-other\": \" @integer 2~17\" }, "
                + "\"pt-PT\": { \"pluralRule-count-one\": \"i = 1 and v = 0 @integer 1\", \"pluralRule-count-other\": \" @integer 0, 2~16\" } } } }";
            Add(zip, "cldr-core/supplemental/plurals.json", Cardinals);
            string parents =
                "{ \"supplemental\": { \"parentLocales\": { \"parentLocale\": { \"pt-AO\": \"pt-PT\" }, \"plurals\": " + pluralParents + " } } }";
            Add(zip, "cldr-core/supplemental/parentLocales.json", parents);
            Add(
                zip,
                "cldr-core/supplemental/ordinals.json",
                "{ \"supplemental\": { \"plurals-type-ordinal\": { \"und\": { \"pluralRule-count-other\": \" @integer 0~15\" } } } }"
            );
        }

        return path;
    }

    private static void Add(ZipArchive zip, string name, string text)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
        writer.Write(text);
    }

    private static string WriteLock(string directory, string sha256)
    {
        string path = Path.Combine(directory, "versions.lock");
        File.WriteAllText(path, "# test\ncldr-json  49.0.0  " + sha256 + "  https://example.invalid/cldr-test-json-full.zip\n");
        return path;
    }

    private static string[] Files(string root) =>
        [
            .. Directory
                .EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Select(file => Path.GetRelativePath(root, file).Replace('\\', '/'))
                .Order(StringComparer.Ordinal),
        ];

    private static (int ExitCode, string Output) RunTool(params string[] arguments)
    {
#if DEBUG
        const string Configuration = "Debug";
#else
        const string Configuration = "Release";
#endif
        string tool = Path.Combine(RepositoryRoot(), "tools", "CldrTableGen", "bin", Configuration, "net10.0", "CldrTableGen.dll");
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("exec");
        start.ArgumentList.Add(tool);
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start)!;
        Task<string> error = process.StandardError.ReadToEndAsync();
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output + error.Result);
    }

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? at = new(AppContext.BaseDirectory); at is not null; at = at.Parent)
        {
            if (File.Exists(Path.Combine(at.FullName, "XylemUI.slnx")))
            {
                return at.FullName;
            }
        }

        throw new InvalidOperationException("The tests run outside the repository.");
    }
}
