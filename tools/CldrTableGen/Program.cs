// Generates the CLDR tables of XylemUI.Core from the json-full release of cldr-json (design §4.11, "How the tables are made"): the
// set of locales, one class for each locale with its week data and its plural rules, the plural rules compiled to methods, the
// entries of CldrLocales, and the samples of the plural rules that the tests select. The version, the SHA-256, and the URL of the
// zip are the cldr-json line of vendor/versions.lock.
//
//   dotnet run --project tools/CldrTableGen -c Release
//
// Downloads the zip into vendor/.cache once, and stops when its SHA-256 is not the lock's. Writes every *.g.cs of
// src/XylemUI.Core/Localization/Cldr/ (the ones there before are deleted), src/XylemUI.Core/LICENSE-CLDR.txt, and
// tests/XylemUI.Core.Tests/CldrPluralSamples.g.cs. Two runs from the same zip write the same bytes. The output is committed; the
// gate does not run this tool.
//
// The tests run it on a zip they make:
//
//   CldrTableGen --zip <zip> --lock <versions.lock> --out <directory>
//
// --zip reads that file in place of the download (its SHA-256 is checked all the same), --lock reads the cldr-json line from that
// file, and --out writes under that directory, at the same paths relative to it as under the repository.
using System.Security.Cryptography;
using XylemUI.Tools.CldrTableGen;

try
{
    return await Run(args);
}
catch (InvalidDataException exception)
{
    // A zip that is not the pinned one, or data the tool does not know how to read: the message names what was found.
    Console.Error.WriteLine("CldrTableGen: " + exception.Message);
    return 1;
}

static async Task<int> Run(string[] args)
{
    string? zipPath = null;
    string? lockPath = null;
    string? outRoot = null;
    for (int i = 0; i < args.Length; i += 2)
    {
        // The option is checked before its value is read, so that an unknown last option (--help) prints the usage.
        string option = args[i];
        if (option is not ("--zip" or "--lock" or "--out"))
        {
            throw new InvalidDataException("Usage: CldrTableGen [--zip <zip>] [--lock <versions.lock>] [--out <directory>]");
        }

        string value = i + 1 < args.Length ? args[i + 1] : throw new InvalidDataException("The option " + option + " has no value.");
        switch (option)
        {
            case "--zip":
                zipPath = value;
                break;
            case "--lock":
                lockPath = value;
                break;
            default:
                outRoot = value;
                break;
        }
    }

    string? root = zipPath is null || lockPath is null || outRoot is null ? FindRoot() : null;
    lockPath ??= Path.Combine(root!, "vendor", "versions.lock");
    outRoot ??= root!;
    PinnedRelease release = PinnedRelease.Read(lockPath);
    byte[] zipBytes = zipPath is null ? await Download(root!, release) : await File.ReadAllBytesAsync(zipPath);
    release.Check(zipBytes);

    using var cldr = new CldrZip(zipBytes);
    release.CheckVersion(cldr);
    LocaleSet locales = LocaleSet.Read(cldr);
    WeekData week = WeekData.Read(cldr);
    PluralTables plurals = PluralTables.Read(cldr, locales);
    var output = new Output(release, cldr.Text("LICENSE"), locales, week, plurals);
    output.Write(outRoot);

    Console.WriteLine(
        "CLDR "
            + release.Version
            + ": "
            + Output.Number(locales.Tables.Count)
            + " tables and root, "
            + Output.Number(locales.Entries.Count)
            + " entries of CldrLocales ("
            + string.Join(", ", locales.Aliases.Select(alias => alias.Key + " -> " + alias.Value))
            + "); left out for their variants: "
            + string.Join(", ", locales.Variants)
            + "; added for row LC4: "
            + string.Join(", ", locales.Added)
    );
    Console.WriteLine(
        "plural rule sets: "
            + Output.Number(plurals.Cardinal.Count)
            + " cardinal, "
            + Output.Number(plurals.Ordinal.Count)
            + " ordinal, "
            + Output.Number(plurals.Pairs)
            + " pairs"
    );
    Console.WriteLine("wrote " + outRoot);
    return 0;
}

static string FindRoot()
{
    for (string? directory = Directory.GetCurrentDirectory(); directory is not null; directory = Path.GetDirectoryName(directory))
    {
        if (File.Exists(Path.Combine(directory, "XylemUI.slnx")))
        {
            return directory;
        }
    }

    throw new InvalidDataException("Run the tool inside the repository: no XylemUI.slnx above the current directory.");
}

// The zip of the lock, downloaded into vendor/.cache once (as tools/UnicodeTableGen keeps UCD.zip). Each URL of the line is tried in
// turn, as vendor/build.ps1 does; a cached file whose SHA-256 is not the lock's is downloaded again.
static async Task<byte[]> Download(string root, PinnedRelease release)
{
    string cache = Path.Combine(root, "vendor", ".cache");
    Directory.CreateDirectory(cache);
    string path = Path.Combine(cache, "cldr-json-" + release.Version + ".zip");
    if (File.Exists(path))
    {
        byte[] cached = await File.ReadAllBytesAsync(path);
        if (release.Matches(cached))
        {
            return cached;
        }

        File.Delete(path);
    }

    using var client = new HttpClient();
    foreach (string url in release.Urls)
    {
        Console.WriteLine("download " + url);
        byte[] bytes;
        try
        {
            bytes = await client.GetByteArrayAsync(new Uri(url));
        }
        catch (HttpRequestException exception)
        {
            Console.WriteLine("    failed: " + exception.Message);
            continue;
        }
        catch (TaskCanceledException exception) when (exception.InnerException is TimeoutException)
        {
            // HttpClient's timeout (100 seconds by default) ends the request with a TaskCanceledException whose inner exception is a
            // TimeoutException: that is what the documentation of HttpClient.Timeout says for .NET 5 and later, not seen in a run
            // here. Nothing else cancels the request, so the next URL is tried.
            Console.WriteLine("    timed out: " + exception.Message);
            continue;
        }

        if (release.Matches(bytes))
        {
            await File.WriteAllBytesAsync(path, bytes);
            return bytes;
        }

        Console.WriteLine("    SHA-256 " + Convert.ToHexStringLower(SHA256.HashData(bytes)) + " is not the lock's");
    }

    throw new InvalidDataException("No URL of the cldr-json line gave the zip with its SHA-256.");
}
