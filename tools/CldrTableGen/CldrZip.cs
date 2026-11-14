using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace XylemUI.Tools.CldrTableGen;

/// <summary>
/// Reads the files of a json-full zip of cldr-json without extracting it (design §4.11, "How the tables are made").
/// </summary>
internal sealed class CldrZip : IDisposable
{
    private readonly ZipArchive _zip;
    private readonly Dictionary<string, JsonDocument> _documents = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="CldrZip"/> class.
    /// </summary>
    /// <param name="bytes">The bytes of the zip.</param>
    public CldrZip(byte[] bytes)
    {
        _zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
    }

    /// <summary>
    /// Returns a value indicating whether the zip has a file.
    /// </summary>
    /// <param name="path">The path in the zip, such as <c>cldr-numbers-full/main/ja/numbers.json</c>.</param>
    /// <returns><see langword="true"/> when the file is there.</returns>
    public bool Has(string path) => _zip.GetEntry(path) is not null;

    /// <summary>
    /// Returns a value indicating whether the zip has the number and the Gregorian date files of a locale: the locales of the set
    /// have both (design §4.11, "The set of locales").
    /// </summary>
    /// <param name="tag">The locale's tag, as cldr-json names its folder.</param>
    /// <returns><see langword="true"/> when both files are there.</returns>
    public bool HasNumbersAndDates(string tag) =>
        Has("cldr-numbers-full/main/" + tag + "/numbers.json") && Has("cldr-dates-full/main/" + tag + "/ca-gregorian.json");

    /// <summary>
    /// Returns the text of a file.
    /// </summary>
    /// <param name="path">The path in the zip.</param>
    /// <returns>The text, with its line ends as LF.</returns>
    public string Text(string path)
    {
        ZipArchiveEntry entry = _zip.GetEntry(path) ?? throw new InvalidDataException("The zip has no " + path + ".");
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        return reader.ReadToEnd().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    /// <summary>
    /// Returns the root of a JSON file, read once.
    /// </summary>
    /// <param name="path">The path in the zip.</param>
    /// <returns>The root element.</returns>
    public JsonElement Json(string path)
    {
        if (!_documents.TryGetValue(path, out JsonDocument? document))
        {
            ZipArchiveEntry entry = _zip.GetEntry(path) ?? throw new InvalidDataException("The zip has no " + path + ".");
            using Stream stream = entry.Open();
            document = JsonDocument.Parse(stream);
            _documents.Add(path, document);
        }

        return document.RootElement;
    }

    /// <summary>
    /// Returns the <c>supplemental</c> object of a file of <c>cldr-core/supplemental</c>.
    /// </summary>
    /// <param name="name">The file's name without <c>.json</c>, such as <c>weekData</c>.</param>
    /// <returns>The object.</returns>
    public JsonElement Supplemental(string name) => Json("cldr-core/supplemental/" + name + ".json").GetProperty("supplemental");

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (JsonDocument document in _documents.Values)
        {
            document.Dispose();
        }

        _zip.Dispose();
    }
}
