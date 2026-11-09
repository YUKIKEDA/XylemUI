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
    string[] references = File.ReadAllLines(args[2]).Where(line => line.Length > 0).ToArray();
    Keeper.Run(args[1], references, args[3], bool.Parse(args[4]));
    return 0;
}

Console.Error.WriteLine("usage: FluentIconsTool keep <assembly> <references file> <fluent directory> <true|false>");
Console.Error.WriteLine("       FluentIconsTool generate <fluent directory> <output .cs file>");
return 2;
