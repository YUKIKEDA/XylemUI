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
            string tokens = Path.Combine(directory, "vendor", "out", "fluent", "tokens");
            string source = Path.Combine(root, "vendor", "out", "fluent", "tokens");
            foreach (string file in Directory.EnumerateFiles(source, "*.ts", SearchOption.AllDirectories))
            {
                string copy = Path.Combine(tokens, Path.GetRelativePath(source, file));
                _ = Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
                File.Copy(file, copy);
            }

            File.Copy(Path.Combine(root, "vendor", "versions.lock"), Path.Combine(directory, "vendor", "versions.lock"));
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
