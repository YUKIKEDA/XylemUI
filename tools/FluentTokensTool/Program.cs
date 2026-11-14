using XylemUI.Tools.FluentTokens;

// The token tool of XylemUI.Theme.Fluent (design §13.2, "トークンの元"):
//   generate <tokens directory> <output .cs file>
// The tokens directory holds packages/tokens/src of microsoft/fluentui at the commit vendor/versions.lock pins, as vendor/build.ps1
// takes it (vendor/out/fluent/tokens). The output is committed (src/XylemUI.Theme.Fluent/FluentTokens.g.cs); a test of
// XylemUI.Theme.Fluent.Tests runs this tool and compares.
if (args.Length == 3 && args[0] == "generate")
{
    Generator.Write(args[1], args[2]);
    return 0;
}

Console.Error.WriteLine("usage: FluentTokensTool generate <tokens directory> <output .cs file>");
return 2;
