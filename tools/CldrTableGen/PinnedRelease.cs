using System.Security.Cryptography;

namespace XylemUI.Tools.CldrTableGen;

/// <summary>
/// The release of cldr-json that the cldr-json line of vendor/versions.lock pins: its version, the SHA-256 of its json-full zip, and
/// the URLs to download it from (design §4.11, "The version of CLDR").
/// </summary>
internal sealed class PinnedRelease
{
    private const string LineName = "cldr-json";

    private PinnedRelease(string version, string sha256, IReadOnlyList<string> urls)
    {
        Version = version;
        Sha256 = sha256;
        Urls = urls;
    }

    /// <summary>
    /// Gets the version of CLDR, such as 49.0.0.
    /// </summary>
    public string Version { get; }

    /// <summary>
    /// Gets the SHA-256 of the zip, in lower case.
    /// </summary>
    public string Sha256 { get; }

    /// <summary>
    /// Gets the URLs of the zip, to be tried in order.
    /// </summary>
    public IReadOnlyList<string> Urls { get; }

    /// <summary>
    /// Reads the cldr-json line of a lock file.
    /// </summary>
    /// <param name="path">The lock file.</param>
    /// <returns>The release the line pins.</returns>
    public static PinnedRelease Read(string path)
    {
        foreach (string line in File.ReadLines(path))
        {
            string[] fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length >= 4 && fields[0] == LineName)
            {
                return new PinnedRelease(fields[1], fields[2].ToLowerInvariant(), fields[3..]);
            }
        }

        throw new InvalidDataException(path + " has no " + LineName + " line.");
    }

    /// <summary>
    /// Returns a value indicating whether the bytes of a zip have the pinned SHA-256.
    /// </summary>
    /// <param name="zip">The bytes of the zip.</param>
    /// <returns><see langword="true"/> when the SHA-256 is the pinned one.</returns>
    public bool Matches(byte[] zip) => Convert.ToHexStringLower(SHA256.HashData(zip)) == Sha256;

    /// <summary>
    /// Stops the tool when the bytes of a zip do not have the pinned SHA-256.
    /// </summary>
    /// <param name="zip">The bytes of the zip.</param>
    /// <exception cref="InvalidDataException">The SHA-256 differs.</exception>
    public void Check(byte[] zip)
    {
        string actual = Convert.ToHexStringLower(SHA256.HashData(zip));
        if (actual != Sha256)
        {
            throw new InvalidDataException("The zip has SHA-256 " + actual + ", not " + Sha256 + " (the " + LineName + " line).");
        }
    }

    /// <summary>
    /// Stops the tool when the version the zip states (cldr-core/package.json) is not the pinned one, so that the version the
    /// generated files name is the version of their data.
    /// </summary>
    /// <param name="cldr">The zip.</param>
    /// <exception cref="InvalidDataException">The versions differ.</exception>
    public void CheckVersion(CldrZip cldr)
    {
        string stated = cldr.Json("cldr-core/package.json").GetProperty("version").GetString() ?? string.Empty;
        if (stated != Version)
        {
            throw new InvalidDataException("The zip is of version " + stated + ", not " + Version + " (the " + LineName + " line).");
        }
    }
}
