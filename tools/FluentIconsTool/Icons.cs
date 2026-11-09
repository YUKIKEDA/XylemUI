using System.Text;
using System.Text.Json;

namespace XylemUI.Tools.FluentIcons;

/// <summary>
/// The icons of one Fluent font: its maps from names to code points (<c>FluentSystemIcons-Regular.json</c>), read into the
/// property names that <c>FluentIcons</c> gives them. The two fonts give one name different code points, so each is read alone
/// (design §5.0.4, "The list of names").
/// </summary>
internal sealed class Icons
{
    /// <summary>
    /// The two fonts: the style in the names and the file names.
    /// </summary>
    public static readonly string[] Styles = ["Regular", "Filled"];

    private Icons(string style, List<Icon> list)
    {
        Style = style;
        List = list;
        ByProperty = list.ToDictionary(icon => icon.Property, StringComparer.Ordinal);
    }

    /// <summary>
    /// Gets the style: Regular or Filled.
    /// </summary>
    public string Style { get; }

    /// <summary>
    /// Gets the icons in the order of the map.
    /// </summary>
    public List<Icon> List { get; }

    /// <summary>
    /// Gets the icons by property name.
    /// </summary>
    public Dictionary<string, Icon> ByProperty { get; }

    /// <summary>
    /// Reads the map of a style from the Fluent directory.
    /// </summary>
    /// <param name="directory">The directory that vendor/build.ps1 fills (vendor/out/fluent).</param>
    /// <param name="style">The style.</param>
    /// <returns>The icons.</returns>
    /// <exception cref="InvalidDataException">A name is not <c>ic_fluent_name_size_style</c>, or two give one property.</exception>
    public static Icons Read(string directory, string style)
    {
        var map = JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(MapPath(directory, style)))!;
        string suffix = "_" + style.ToLowerInvariant();
        var list = new List<Icon>(map.Count);
        var properties = new HashSet<string>(StringComparer.Ordinal);
        foreach ((string name, int codePoint) in map)
        {
            const string Prefix = "ic_fluent_";
            if (!name.StartsWith(Prefix, StringComparison.Ordinal) || !name.EndsWith(suffix, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"The name {name} is not ic_fluent_<name>_<size>{suffix}.");
            }

            string[] parts = name[Prefix.Length..].Split('_');
            if (parts.Length < 3 || !int.TryParse(parts[^2], out int size))
            {
                throw new InvalidDataException($"The name {name} has no size before its style.");
            }

            var property = new StringBuilder();
            foreach (string part in parts)
            {
                _ = property.Append(char.ToUpperInvariant(part[0])).Append(part, 1, part.Length - 1);
            }

            if (!properties.Add(property.ToString()))
            {
                throw new InvalidDataException($"Two names give the property {property}.");
            }

            list.Add(new Icon(name, property.ToString(), codePoint, size));
        }

        return new Icons(style, list);
    }

    /// <summary>
    /// Returns the path of a style's font.
    /// </summary>
    /// <param name="directory">The Fluent directory.</param>
    /// <param name="style">The style.</param>
    /// <returns>The path.</returns>
    public static string FontPath(string directory, string style) => Path.Combine(directory, $"FluentSystemIcons-{style}.ttf");

    private static string MapPath(string directory, string style) => Path.Combine(directory, $"FluentSystemIcons-{style}.json");
}

/// <summary>
/// One icon of a font.
/// </summary>
/// <param name="Name">The name in the map (<c>ic_fluent_add_24_regular</c>).</param>
/// <param name="Property">The property of <c>FluentIcons</c> (<c>Add24Regular</c>).</param>
/// <param name="CodePoint">The code point.</param>
/// <param name="Size">The size the glyph was drawn for.</param>
internal sealed record Icon(string Name, string Property, int CodePoint, int Size);
