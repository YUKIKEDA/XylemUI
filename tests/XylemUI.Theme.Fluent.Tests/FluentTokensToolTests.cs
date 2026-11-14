using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;

namespace XylemUI.Theme.Fluent.Tests;

// tools/FluentTokensTool (design §13.2, "トークンの元", and its "確かめ方"): FluentTokens.g.cs is what the tool writes from the token
// files of microsoft/fluentui at the pinned commit, which vendor/build.ps1 takes into vendor/out/fluent/tokens.
public sealed partial class FluentTokensToolTests
{
    /// <summary>
    /// The tool writes, from the pinned token files, the file the repository holds.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The 17 token files of vendor/versions.lock in vendor/out/fluent/tokens; the tool this project's build built.
    ///
    /// Steps:
    /// - Run <c>FluentTokensTool generate</c> into a file of a temporary folder.
    ///
    /// Expected:
    /// - The tool exits with 0, and the file is the same text as src/XylemUI.Theme.Fluent/FluentTokens.g.cs.
    /// </remarks>
    [Fact]
    public void Generate_FromThePinnedTokens_WritesTheCommittedFile()
    {
        string root = RepositoryRoot();
        string directory = Directory.CreateTempSubdirectory("xylemui-tokens-").FullName;
        try
        {
            string output = Path.Combine(directory, "FluentTokens.g.cs");

            (int exitCode, string log) = RunTool(root, "generate", Path.Combine(root, "vendor", "out", "fluent", "tokens"), output);

            Assert.True(exitCode == 0, log);
            Assert.Equal(File.ReadAllText(Path.Combine(root, "src", "XylemUI.Theme.Fluent", "FluentTokens.g.cs")), File.ReadAllText(output));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// Each variant has the same 184 alias colors, besides the status palette and the status colors.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - FluentColors and the committed FluentTokens.g.cs.
    ///
    /// Steps:
    /// - Count FluentColors' properties whose names start with neither ColorPalette nor ColorStatus; in each of the generated Light,
    ///   Dark, and HighContrast tables, count the assignments of such a property.
    ///
    /// Expected:
    /// - 184 properties, and 184 assignments in each table, each property once.
    /// </remarks>
    [Fact]
    public void Generate_EachVariant_Has184AliasColors()
    {
        string[] alias = typeof(FluentColors)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.PropertyType == typeof(Color))
            .Select(property => property.Name)
            .Where(IsAlias)
            .ToArray();
        string generated = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "XylemUI.Theme.Fluent", "FluentTokens.g.cs"));

        Assert.Equal(184, alias.Length);
        foreach (string variant in new[] { "Light", "Dark", "HighContrast" })
        {
            int start = generated.IndexOf($"internal static FluentColors {variant}(", StringComparison.Ordinal);
            int end = generated.IndexOf("};", start, StringComparison.Ordinal);
            string[] assigned = Assignment().Matches(generated[start..end]).Select(match => match.Groups[1].Value).Where(IsAlias).ToArray();
            Assert.Equal(alias.Order(StringComparer.Ordinal), assigned.Order(StringComparer.Ordinal));
        }
    }

    /// <summary>
    /// The tool stops on an alias file that does not hold 184 colors, rather than write a theme with one missing.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A copy of the token files in a temporary folder whose alias/lightColor.ts lacks the line of colorNeutralForeground1Hover.
    ///
    /// Steps:
    /// - Run <c>FluentTokensTool generate</c> on the copy.
    ///
    /// Expected:
    /// - The tool exits with other than 0, its output says the file has 183 alias colors, not 184, and it writes no file.
    /// </remarks>
    [Fact]
    public void Generate_AliasFileWithoutOneColor_Stops()
    {
        string root = RepositoryRoot();
        string directory = Directory.CreateTempSubdirectory("xylemui-tokens-").FullName;
        try
        {
            string tokens = CopyTokens(root, directory);
            string light = Path.Combine(tokens, "alias", "lightColor.ts");
            File.WriteAllLines(
                light,
                File.ReadAllLines(light).Where(line => !line.Contains("colorNeutralForeground1Hover:", StringComparison.Ordinal))
            );
            string output = Path.Combine(directory, "FluentTokens.g.cs");

            (int exitCode, string log) = RunTool(root, "generate", tokens, output);

            Assert.NotEqual(0, exitCode);
            Assert.Contains("183 alias colors, not 184", log, StringComparison.Ordinal);
            Assert.False(File.Exists(output));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// Each reader of the tool stops on a line it does not recognise, rather than pass over a statement that would change a token.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A copy of the token files in a temporary folder, with one line added to one file. The cases cover each reader: outside the
    ///   objects of an object file (global/colors.ts) and inside a read object (global/spacings.ts, a spread), after the function of an
    ///   alias file (alias/lightColor.ts), a palette file (alias/highContrastColorPalette.ts, the <c>Object.assign(colorStatusTokens,
    ///   …)</c> of the review of PR #929), the shadows (utils/shadows.ts, a spread in the returned object), the name lists
    ///   (sharedColorNames.ts), and after the typography styles (global/typographyStyles.ts).
    ///
    /// Steps:
    /// - Run <c>FluentTokensTool generate</c> on the copy.
    ///
    /// Expected:
    /// - The tool exits with other than 0, its output names the added line as one it does not read, and it writes no file.
    /// </remarks>
    /// <param name="file">The file, under packages/tokens/src.</param>
    /// <param name="after">The line the added line follows; empty to add it at the end of the file.</param>
    /// <param name="added">The added line.</param>
    [Theory]
    [InlineData("global/colors.ts", "export const white = '#ffffff';", "Object.freeze(grey);")]
    [InlineData("global/spacings.ts", "  m: '12px',", "  ...extraSpacings,")]
    [InlineData("alias/lightColor.ts", "", "export const extraColor = grey[14];")]
    [InlineData("alias/highContrastColorPalette.ts", "", "Object.assign(colorStatusTokens, { colorStatusDangerBackground4: hcCanvas });")]
    [InlineData("utils/shadows.ts", "  return {", "    ...extraShadows,")]
    [InlineData("sharedColorNames.ts", "", "export const extraNames = statusSharedColorNames.concat(['pink']);")]
    [InlineData("global/typographyStyles.ts", "", "Object.assign(typographyStyles, {});")]
    public void Generate_FileWithAnUnknownLine_Stops(string file, string after, string added)
    {
        string root = RepositoryRoot();
        string directory = Directory.CreateTempSubdirectory("xylemui-tokens-").FullName;
        try
        {
            string tokens = CopyTokens(root, directory);
            string path = Path.Combine(tokens, file);
            var lines = File.ReadAllLines(path).ToList();
            int at = after.Length == 0 ? lines.Count : lines.IndexOf(after) + 1;
            Assert.True(at > 0, $"{file} has no line \"{after}\".");
            lines.Insert(at, added);
            File.WriteAllLines(path, lines);
            string output = Path.Combine(directory, "FluentTokens.g.cs");

            (int exitCode, string log) = RunTool(root, "generate", tokens, output);

            Assert.NotEqual(0, exitCode);
            Assert.Contains($"the line \"{added}\" is not one this tool reads", log, StringComparison.Ordinal);
            Assert.False(File.Exists(output));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    // Copies the pinned token files and vendor/versions.lock into a folder laid out as the repository's vendor folder, and returns the
    // folder of the token files.
    private static string CopyTokens(string root, string directory)
    {
        string tokens = Path.Combine(directory, "vendor", "out", "fluent", "tokens");
        string source = Path.Combine(root, "vendor", "out", "fluent", "tokens");
        foreach (string file in Directory.EnumerateFiles(source, "*.ts", SearchOption.AllDirectories))
        {
            string copy = Path.Combine(tokens, Path.GetRelativePath(source, file));
            _ = Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(file, copy);
        }

        File.Copy(Path.Combine(root, "vendor", "versions.lock"), Path.Combine(directory, "vendor", "versions.lock"));
        return tokens;
    }

    private static bool IsAlias(string name) =>
        !name.StartsWith("ColorPalette", StringComparison.Ordinal) && !name.StartsWith("ColorStatus", StringComparison.Ordinal);

    // An assignment of a color in a generated table: "            ColorNeutralForeground1 = new(...), // grey[14]".
    [GeneratedRegex(@"^ {12}(Color\w+) = ", RegexOptions.Multiline)]
    private static partial Regex Assignment();

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

    private static (int ExitCode, string Output) RunTool(string root, params string[] arguments)
    {
#if DEBUG
        const string Configuration = "Debug";
#else
        const string Configuration = "Release";
#endif
        string tool = Path.Combine(root, "tools", "FluentTokensTool", "bin", Configuration, "net10.0", "FluentTokensTool.dll");
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("exec");
        start.ArgumentList.Add(tool);
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start)!;
        Task<string> error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output + error.GetAwaiter().GetResult());
    }
}
