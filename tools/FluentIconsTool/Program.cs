using XylemUI.Tools.FluentIcons;

// The build tool of XylemUI.Theme.Fluent (design §5.0.4, "Icons"):
//   keep <assembly> <references file> <fluent directory> <keep all: true or false>
//   generate <fluent directory> <output .cs file>
if (args.Length == 3 && args[0] == "generate")
{
    Generator.Write(args[1], args[2]);
    return 0;
}

if (args.Length == 5 && args[0] == "keep")
{
    // The subset needs the build machine's HarfBuzz, which vendor/build.ps1 makes; without it the first call would end in a
    // DllNotFoundException that names neither (PR #726's review).
    bool keepAll = bool.Parse(args[4]);
    if (!keepAll && !File.Exists(Path.Combine(AppContext.BaseDirectory, "harfbuzz-subset.dll")))
    {
        Console.Error.WriteLine(
            "FluentIconsTool: harfbuzz-subset.dll is not next to the tool. Run ./vendor/build.ps1, which makes it in vendor/out/win-x64/tool, and build again."
        );
        return 1;
    }

    string[] references = File.ReadAllLines(args[2]).Where(line => line.Length > 0).ToArray();
    Keeper.Run(args[1], references, args[3], keepAll);
    return 0;
}

Console.Error.WriteLine("usage: FluentIconsTool keep <assembly> <references file> <fluent directory> <true|false>");
Console.Error.WriteLine("       FluentIconsTool generate <fluent directory> <output .cs file>");
return 2;
