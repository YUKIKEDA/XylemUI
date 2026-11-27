using System.Text;
using System.Text.Json;

namespace XylemUI.Tools.CldrTableGen;

/// <summary>
/// The pieces the number and the date tables are written with: a string as a length byte and its UTF-8, the index of a table among
/// the distinct ones, and a string of the json that must be there.
/// </summary>
internal static class TableBytes
{
    /// <summary>
    /// Adds a string: its UTF-8 length in one byte, and its bytes.
    /// </summary>
    /// <param name="table">The table to add to.</param>
    /// <param name="text">The string.</param>
    /// <exception cref="InvalidDataException">The string is longer than 255 bytes of UTF-8.</exception>
    public static void Add(List<byte> table, string text)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        if (bytes.Length > 255)
        {
            throw new InvalidDataException("The string " + text + " is longer than 255 bytes of UTF-8.");
        }

        table.Add((byte)bytes.Length);
        table.AddRange(bytes);
    }

    /// <summary>
    /// Returns the index of a table among the distinct ones, and adds it when it is new.
    /// </summary>
    /// <param name="table">The table.</param>
    /// <param name="tables">The distinct tables, in the order they were first used.</param>
    /// <param name="index">The index of each distinct table, by its bytes in base 64.</param>
    /// <returns>The index.</returns>
    public static int Index(byte[] table, List<byte[]> tables, Dictionary<string, int> index)
    {
        string key = Convert.ToBase64String(table);
        if (!index.TryGetValue(key, out int at))
        {
            at = tables.Count;
            tables.Add(table);
            index.Add(key, at);
        }

        return at;
    }

    /// <summary>
    /// Returns a string property of the json that is not empty.
    /// </summary>
    /// <param name="tag">The locale, for the message.</param>
    /// <param name="element">The object.</param>
    /// <param name="name">The property's name.</param>
    /// <returns>The string.</returns>
    /// <exception cref="InvalidDataException">The property is not there, not a string, or empty.</exception>
    public static string Text(string tag, JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.GetString() is { Length: > 0 } text
            ? text
            : throw new InvalidDataException(tag + " has no " + name + ".");
}
