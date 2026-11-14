using System.Globalization;
using System.Text.RegularExpressions;

namespace XylemUI.Tools.FluentTokens;

/// <summary>
/// Where an alias color comes from: a fixed color, a brand step, or one of the OS's eight high-contrast colors.
/// </summary>
internal abstract record ColorSource(string Origin);

/// <summary>
/// A fixed color, written as its channels.
/// </summary>
internal sealed record FixedColor(byte R, byte G, byte B, byte A, string Origin) : ColorSource(Origin);

/// <summary>
/// A brand step, 0 for step 10 to 15 for step 160.
/// </summary>
internal sealed record BrandStep(int Index, string Origin) : ColorSource(Origin);

/// <summary>
/// One of the members of <c>HighContrastColors</c>.
/// </summary>
internal sealed record SystemColor(string Member, string Origin) : ColorSource(Origin);

/// <summary>
/// A token of a size in pixels.
/// </summary>
internal sealed record PixelToken(string Name, float Value);

/// <summary>
/// A text style of typographyStyles.ts.
/// </summary>
internal sealed record TextStyleToken(string Name, float FontSize, float LineHeight, int Weight);

/// <summary>
/// One layer of a shadow: its offset, its blur, and whether its color is the ambient or the key color.
/// </summary>
internal sealed record ShadowLayer(float X, float Y, float Blur, bool IsKey);

/// <summary>
/// A shadow of shadows.ts: its name without the suffix, and its ambient and key layers.
/// </summary>
internal sealed record ShadowToken(string Name, ShadowLayer Ambient, ShadowLayer Key);

/// <summary>
/// Reads the TypeScript files of <c>@fluentui/tokens</c> (<c>packages/tokens/src</c>) that design §13.2 names under "トークンの元".
/// Each file is read whole, line by line, and every line must be one of three kinds, or the tool stops: a line it reads (an entry,
/// an alias color, a template, a patch, a shadow, a name); a line any file may hold (blank, a comment, an import); or a line of the
/// file's JavaScript scaffolding that the pinned files hold, listed by its reader. An object of an object file that the tool does
/// not read (fonts.ts's families, the brand ramps of other products, the persona palette) is passed over whole, up to its
/// <c>};</c>, and the persona template of a palette file is passed over too. So an upstream change that adds a statement, such as
/// <c>Object.assign(colorStatusTokens, …)</c>, stops the tool rather than being left out of the theme.
/// </summary>
internal sealed partial class TokenSources
{
    /// <summary>
    /// The number of alias colors of each variant (design §13.2, "トークンの元").
    /// </summary>
    public const int AliasColorCount = 184;

    // The CSS system color each high-contrast constant of global/colors.ts stands for (the comment at the end of each line of
    // alias/highContrastColor.ts), and the member of HighContrastColors that holds it (design §13.2, "ハイコントラスト").
    private static readonly Dictionary<string, (string Css, string Member)> HighContrastConstants = new(StringComparer.Ordinal)
    {
        ["hcCanvas"] = ("Canvas", "Window"),
        ["hcCanvasText"] = ("CanvasText", "WindowText"),
        ["hcHyperlink"] = ("LinkText", "Hotlight"),
        ["hcDisabled"] = ("GrayText", "GrayText"),
        ["hcHighlight"] = ("Highlight", "Highlight"),
        ["hcHighlightText"] = ("HighlightText", "HighlightText"),
        ["hcButtonFace"] = ("ButtonFace", "ButtonFace"),
        ["hcButtonText"] = ("ButtonText", "ButtonText"),
    };

    // The objects of global/fonts.ts that the typography styles name.
    private static readonly string[] FontObjects = ["fontSizes", "lineHeights", "fontWeights"];

    // The scaffolding of the palette files (alias/lightColorPalette.ts, darkColorPalette.ts, highContrastColorPalette.ts at the
    // pinned commit), trimmed: the reduce calls around the templates, and the export that joins the two palettes.
    private static readonly HashSet<string> PaletteScaffolding = new(StringComparer.Ordinal)
    {
        "const color = sharedColor.slice(0, 1).toUpperCase() + sharedColor.slice(1);",
        "const color = statusColor.slice(0, 1).toUpperCase() + statusColor.slice(1);",
        "const sharedColorTokens = {",
        "const statusColorTokens = {",
        "return Object.assign(acc, sharedColorTokens);",
        "return Object.assign(acc, statusColorTokens);",
        "};",
        "},",
        ");",
        "(acc, [statusColor, sharedColor]) => {",
        "{} as ColorStatusTokens,",
        "}, {} as StatusColorPaletteTokens);",
        "}, {} as PersonaColorPaletteTokens);",
        "export const colorPaletteTokens: ColorPaletteTokens = { ...statusColorPaletteTokens, ...personaColorPaletteTokens };",
    };

    // The scaffolding of utils/shadows.ts at the pinned commit, trimmed: the two overloads, the function, and its return.
    private static readonly HashSet<string> ShadowScaffolding = new(StringComparer.Ordinal)
    {
        "export function createShadowTokens(ambientColor: string, keyColor: string, tokenSuffix: 'Brand'): ShadowBrandTokens;",
        "export function createShadowTokens(ambientColor: string, keyColor: string): ShadowTokens;",
        "export function createShadowTokens(ambientColor: string, keyColor: string, tokenSuffix: 'Brand' | '' = '') {",
        "return {",
        "};",
        "}",
    };

    private readonly string _directory;
    private readonly Dictionary<string, string> _scalars = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<(string Key, string Value)>> _objects = new(StringComparer.Ordinal);

    private TokenSources(string directory)
    {
        _directory = directory;
        ReadGlobalColors();
    }

    /// <summary>
    /// Gets the 16 steps of <c>brandWeb</c>, step 10 first, as <c>0xRRGGBB</c>.
    /// </summary>
    public IReadOnlyList<uint> BrandWeb { get; private set; } = [];

    /// <summary>
    /// Gets the names of the color tokens in the order of the light theme: the alias colors, then the status palette, then the
    /// status colors.
    /// </summary>
    public IReadOnlyList<string> ColorNames { get; private set; } = [];

    /// <summary>
    /// Gets the colors of each variant ("Light", "Dark", "HighContrast") by token name.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, ColorSource>> Colors { get; private set; } =
        new Dictionary<string, IReadOnlyDictionary<string, ColorSource>>();

    /// <summary>
    /// Gets the spacing tokens, horizontal then vertical.
    /// </summary>
    public IReadOnlyList<PixelToken> Spacing { get; private set; } = [];

    /// <summary>
    /// Gets the stroke widths.
    /// </summary>
    public IReadOnlyList<PixelToken> StrokeWidths { get; private set; } = [];

    /// <summary>
    /// Gets the corner radii.
    /// </summary>
    public IReadOnlyList<PixelToken> Radii { get; private set; } = [];

    /// <summary>
    /// Gets the 17 text styles.
    /// </summary>
    public IReadOnlyList<TextStyleToken> TextStyles { get; private set; } = [];

    /// <summary>
    /// Gets the shadows.
    /// </summary>
    public IReadOnlyList<ShadowToken> Shadows { get; private set; } = [];

    /// <summary>
    /// Reads every file.
    /// </summary>
    /// <param name="directory">The folder that holds <c>packages/tokens/src</c>'s files at their paths.</param>
    /// <returns>The tokens.</returns>
    public static TokenSources Read(string directory)
    {
        var sources = new TokenSources(directory);
        sources.BrandWeb = sources.ReadBrandWeb();
        OrderedTokens light = sources.ReadVariant("lightColor.ts", "lightColorPalette.ts", highContrast: false);
        OrderedTokens dark = sources.ReadVariant("darkColor.ts", "darkColorPalette.ts", highContrast: false);
        OrderedTokens highContrast = sources.ReadVariant("highContrastColor.ts", "highContrastColorPalette.ts", highContrast: true);
        foreach ((string name, OrderedTokens tokens) in new[] { ("dark", dark), ("high-contrast", highContrast) })
        {
            if (!tokens.Names.ToHashSet(StringComparer.Ordinal).SetEquals(light.Names))
            {
                string[] differ = tokens.Names.Except(light.Names).Concat(light.Names.Except(tokens.Names)).ToArray();
                throw new InvalidDataException($"The {name} colors do not have the light colors' tokens: {string.Join(", ", differ)}.");
            }
        }

        sources.ColorNames = light.Names;
        sources.Colors = new Dictionary<string, IReadOnlyDictionary<string, ColorSource>>(StringComparer.Ordinal)
        {
            ["Light"] = light.Values,
            ["Dark"] = dark.Values,
            ["HighContrast"] = highContrast.Values,
        };
        sources.Spacing = sources.ReadSpacing();
        sources.StrokeWidths = sources.ReadPixels("global/strokeWidths.ts", "strokeWidths");
        sources.Radii = sources.ReadPixels("global/borderRadius.ts", "borderRadius");
        sources.TextStyles = sources.ReadTextStyles();
        sources.Shadows = sources.ReadShadows();
        return sources;
    }

    // An object literal `export const name: Type = {` or `const name: Type = {`, one `key: value,` a line, up to `};`.
    [GeneratedRegex(@"^(?:export )?const (\w+)(?::[^=]+)? = \{$")]
    private static partial Regex ObjectStart();

    [GeneratedRegex(@"^'?(\w+)'?: (.+),$")]
    private static partial Regex ObjectEntry();

    [GeneratedRegex(@"^(\w+),$")]
    private static partial Regex Shorthand();

    [GeneratedRegex(@"^export const (\w+) = '([^']*)';$")]
    private static partial Regex ScalarLine();

    // A line of an alias color file: `  colorX: source, // comment`.
    [GeneratedRegex(@"^  (color\w+): (.+?), // (.*)$")]
    private static partial Regex AliasLine();

    [GeneratedRegex(@"^\[`colorPalette\$\{color\}(\w+)`\]: (.+),$")]
    private static partial Regex PaletteTemplate();

    [GeneratedRegex(@"^\[`colorStatus\$\{color\}(\w+)`\]: (.+),$")]
    private static partial Regex StatusTemplate();

    [GeneratedRegex(@"^(statusColorPaletteTokens|colorStatusTokens|personaColorPaletteTokens)\.(\w+) = (.+);$")]
    private static partial Regex Patch();

    [GeneratedRegex(@"^(\w+)\[(\w+)\]$")]
    private static partial Regex Indexed();

    [GeneratedRegex(@"^rgba\((\d+), ?(\d+), ?(\d+), ?([0-9.]+)\)$")]
    private static partial Regex Rgba();

    [GeneratedRegex(@"^\[`(shadow\d+)\$\{tokenSuffix\}`\]: `(.+)`,$")]
    private static partial Regex ShadowLine();

    [GeneratedRegex(@"^(\S+) (\S+) (\S+) \$\{(ambientColor|keyColor)\}$")]
    private static partial Regex ShadowPart();

    // A list of names of sharedColorNames.ts: `export const name = [`, one `'name',` a line, up to `] as const;`, or on one line.
    [GeneratedRegex(@"^export const (\w+) = \[$")]
    private static partial Regex NameListStart();

    [GeneratedRegex(@"^'\w+',$")]
    private static partial Regex NameListEntry();

    [GeneratedRegex(@"^export const \w+ = \[[^\]]*\] as const;$")]
    private static partial Regex NameListOneLine();

    [GeneratedRegex(@"^import (?:type )?\{[^}]*\} from '[^']+';$")]
    private static partial Regex ImportLine();

    [GeneratedRegex(@"^import (?:type )?\{$")]
    private static partial Regex ImportStart();

    [GeneratedRegex(@"^\} from '[^']+';$")]
    private static partial Regex ImportEnd();

    private static InvalidDataException Unknown(string path, string raw) => new($"{path}: the line \"{raw}\" is not one this tool reads.");

    private static string Capitalize(string name) => char.ToUpperInvariant(name[0]) + name[1..];

    private static string Unquote(string value)
    {
        if (value.Length >= 2 && ((value[0] == '\'' && value[^1] == '\'') || (value[0] == '`' && value[^1] == '`')))
        {
            return value[1..^1];
        }

        throw new InvalidDataException($"The value {value} is not a quoted string.");
    }

    private static float Pixels(string value)
    {
        string text = Unquote(value);
        string number =
            text == "0" ? "0"
            : text.EndsWith("px", StringComparison.Ordinal) ? text[..^2]
            : throw new InvalidDataException($"The size {value} is not in pixels.");
        return float.Parse(number, NumberStyles.Integer, CultureInfo.InvariantCulture);
    }

    private static float Length(string value) =>
        value == "0" ? 0
        : value.EndsWith("px", StringComparison.Ordinal) ? float.Parse(value[..^2], NumberStyles.Integer, CultureInfo.InvariantCulture)
        : throw new InvalidDataException($"The length {value} of a shadow is not in pixels.");

    // A CSS color as Fluent writes it: #rrggbb, #rrggbbaa, rgba(r, g, b, a), or transparent. An alpha of rgba() is rounded to the
    // nearest byte, halves away from zero.
    private static FixedColor ParseColor(string text, string origin)
    {
        if (text == "transparent")
        {
            return new FixedColor(0, 0, 0, 0, origin);
        }

        if (text.Length is 7 or 9 && text[0] == '#')
        {
            byte Hex(int at) => byte.Parse(text.AsSpan(at, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return new FixedColor(Hex(1), Hex(3), Hex(5), text.Length == 9 ? Hex(7) : (byte)255, origin);
        }

        Match rgba = Rgba().Match(text);
        if (rgba.Success)
        {
            byte Channel(int group) => byte.Parse(rgba.Groups[group].Value, NumberStyles.Integer, CultureInfo.InvariantCulture);
            double alpha = double.Parse(rgba.Groups[4].Value, NumberStyles.Float, CultureInfo.InvariantCulture);
            if (alpha is < 0 or > 1)
            {
                throw new InvalidDataException($"The alpha of {text} is outside 0 to 1.");
            }

            return new FixedColor(Channel(1), Channel(2), Channel(3), (byte)Math.Round(alpha * 255, MidpointRounding.AwayFromZero), origin);
        }

        throw new InvalidDataException($"{text} is not a color this tool reads.");
    }

    // The names of an object of global/colorPalette.ts written in shorthand (`red,`): each is the constant of global/colors.ts of
    // the same name.
    private static HashSet<string> SharedNames(Dictionary<string, List<(string Key, string Value)>> palette, string name)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach ((string key, string value) in palette[name])
        {
            if (key != value)
            {
                throw new InvalidDataException($"global/colorPalette.ts: {name}'s {key} is {value}, not the color of its own name.");
            }

            _ = names.Add(key);
        }

        return names;
    }

    private string[] Lines(string path) => File.ReadAllLines(Path.Combine(_directory, path));

    // The object literals of a file, by name: all of them, or those named in `only` (the others are passed over whole, so that an
    // object this tool does not read, such as fonts.ts's families over several lines, does not stop it). A shorthand entry (`red,`)
    // is read as the key and its own name. Outside the objects, a file holds only the lines any file may hold and string constants
    // (`export const white = '#ffffff';`).
    private Dictionary<string, List<(string Key, string Value)>> Objects(string path, params string[] only)
    {
        var objects = new Dictionary<string, List<(string Key, string Value)>>(StringComparer.Ordinal);
        var structure = new Structure();
        List<(string Key, string Value)>? current = null;
        bool skipping = false;
        foreach (string raw in Lines(path))
        {
            string line = raw.Trim();
            if (current is null && !skipping)
            {
                Match start = ObjectStart().Match(line);
                if (start.Success)
                {
                    string name = start.Groups[1].Value;
                    if (only.Length > 0 && !only.Contains(name, StringComparer.Ordinal))
                    {
                        skipping = true;
                        continue;
                    }

                    current = [];
                    objects.Add(name, current);
                }
                else if (!ScalarLine().IsMatch(line) && !structure.Skips(line))
                {
                    throw Unknown(path, raw);
                }

                continue;
            }

            if (line == "};")
            {
                current = null;
                skipping = false;
                continue;
            }

            if (skipping)
            {
                continue;
            }

            Match entry = ObjectEntry().Match(line);
            Match shorthand = Shorthand().Match(line);
            if (entry.Success)
            {
                current!.Add((entry.Groups[1].Value, entry.Groups[2].Value));
            }
            else if (shorthand.Success)
            {
                current!.Add((shorthand.Groups[1].Value, shorthand.Groups[1].Value));
            }
            else if (!structure.Skips(line))
            {
                throw Unknown(path, raw);
            }
        }

        foreach (string name in only)
        {
            if (!objects.ContainsKey(name))
            {
                throw new InvalidDataException($"{path} has no {name}.");
            }
        }

        return objects;
    }

    private void ReadGlobalColors()
    {
        const string Path = "global/colors.ts";
        foreach (string raw in Lines(Path))
        {
            Match scalar = ScalarLine().Match(raw);
            if (scalar.Success)
            {
                _scalars.Add(scalar.Groups[1].Value, scalar.Groups[2].Value);
            }
        }

        foreach ((string name, List<(string Key, string Value)> entries) in Objects(Path))
        {
            _objects.Add(name, entries.Select(entry => (entry.Key, Unquote(entry.Value))).ToList());
        }

        foreach (string constant in HighContrastConstants.Keys)
        {
            if (!_scalars.ContainsKey(constant))
            {
                throw new InvalidDataException($"{Path} has no {constant}.");
            }
        }
    }

    private string GlobalColor(string name, string key)
    {
        if (!_objects.TryGetValue(name, out List<(string Key, string Value)>? entries))
        {
            throw new InvalidDataException($"global/colors.ts has no {name}.");
        }

        foreach ((string entryKey, string value) in entries)
        {
            if (entryKey == key)
            {
                return value;
            }
        }

        throw new InvalidDataException($"global/colors.ts has no {name}[{key}].");
    }

    private List<uint> ReadBrandWeb()
    {
        const string Path = "global/brandColors.ts";
        List<(string Key, string Value)> entries = Objects(Path, "brandWeb")["brandWeb"];
        if (entries.Count != 16)
        {
            throw new InvalidDataException($"{Path}: brandWeb has {entries.Count} steps, not 16.");
        }

        var steps = new List<uint>(16);
        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i].Key != ((i + 1) * 10).ToString(CultureInfo.InvariantCulture))
            {
                throw new InvalidDataException($"{Path}: brandWeb's step {i + 1} is {entries[i].Key}.");
            }

            FixedColor color = ParseColor(Unquote(entries[i].Value), "brandWeb");
            steps.Add(((uint)color.R << 16) | ((uint)color.G << 8) | color.B);
        }

        return steps;
    }

    // A source of an alias file or a palette file: a global color, a brand step, a high-contrast constant, or a quoted literal.
    private ColorSource Source(string expression, string? css)
    {
        if (expression.StartsWith('\''))
        {
            return ParseColor(Unquote(expression), expression);
        }

        if (HighContrastConstants.TryGetValue(expression, out (string Css, string Member) system))
        {
            if (css is not null && css != system.Css)
            {
                throw new InvalidDataException($"{expression} is commented as {css}, not {system.Css}.");
            }

            return new SystemColor(system.Member, $"{expression} ({system.Css})");
        }

        if (_scalars.TryGetValue(expression, out string? scalar))
        {
            return ParseColor(scalar, expression);
        }

        Match indexed = Indexed().Match(expression);
        if (indexed.Success)
        {
            string name = indexed.Groups[1].Value;
            string key = indexed.Groups[2].Value;
            if (name == "brand")
            {
                int step = int.Parse(key, NumberStyles.Integer, CultureInfo.InvariantCulture);
                if (step % 10 != 0 || step is < 10 or > 160)
                {
                    throw new InvalidDataException($"{expression} is not a brand step.");
                }

                return new BrandStep((step / 10) - 1, expression);
            }

            return ParseColor(GlobalColor(name, key), expression);
        }

        throw new InvalidDataException($"{expression} is not a color source this tool reads.");
    }

    private OrderedTokens ReadVariant(string aliasFile, string paletteFile, bool highContrast)
    {
        var tokens = new OrderedTokens();
        string aliasPath = $"alias/{aliasFile}";
        var structure = new Structure();

        // Before the function and after it, only the lines any file may hold; inside it, only alias colors.
        string part = "before";
        foreach (string raw in Lines(aliasPath))
        {
            if (
                part == "before"
                && raw.StartsWith("export const generateColorTokens = (", StringComparison.Ordinal)
                && raw.EndsWith("): ColorTokens => ({", StringComparison.Ordinal)
            )
            {
                part = "inside";
                continue;
            }

            if (part == "inside" && raw == "});")
            {
                part = "after";
                continue;
            }

            if (part != "inside")
            {
                if (!structure.Skips(raw.Trim()))
                {
                    throw Unknown(aliasPath, raw);
                }

                continue;
            }

            Match line = AliasLine().Match(raw);
            if (!line.Success)
            {
                throw new InvalidDataException($"{aliasPath}: the line \"{raw}\" is not an alias color this tool reads.");
            }

            // The comment of a high-contrast line starts with the CSS system color its source stands for.
            string? css = highContrast ? line.Groups[3].Value.Split(' ')[0] : null;
            tokens.Set(line.Groups[1].Value, Source(line.Groups[2].Value, css));
        }

        if (part != "after")
        {
            throw new InvalidDataException($"{aliasPath} has no generateColorTokens that ends with \"}});\".");
        }

        if (tokens.Names.Count != AliasColorCount)
        {
            throw new InvalidDataException($"{aliasPath} has {tokens.Names.Count} alias colors, not {AliasColorCount}.");
        }

        ReadPalette($"alias/{paletteFile}", tokens);
        return tokens;
    }

    // The status palette and the status colors of a palette file (design §13.2: statusSharedColors and mappedStatusColors). The
    // persona palette is not read.
    private void ReadPalette(string path, OrderedTokens tokens)
    {
        List<string> statusNames = ReadStatusNames();
        List<(string Status, string Color)> mapping = ReadStatusMapping();
        Dictionary<string, List<(string Key, string Value)>> palette = Objects("global/colorPalette.ts", "statusSharedColors", "mappedStatusColors");
        HashSet<string> statusShared = SharedNames(palette, "statusSharedColors");
        HashSet<string> mappedShared = SharedNames(palette, "mappedStatusColors");

        var paletteTemplate = new List<(string Suffix, string Source)>();
        var statusTemplate = new List<(string Suffix, string Source)>();
        var patches = new List<(string Target, string Name, string Source)>();
        string block = string.Empty;
        var structure = new Structure();
        foreach (string raw in Lines(path))
        {
            string line = raw.Trim();
            if (PaletteScaffolding.Contains(line) || structure.Skips(line))
            {
                continue;
            }

            if (line.StartsWith("const statusColorPaletteTokens = statusSharedColorNames.reduce(", StringComparison.Ordinal))
            {
                block = "palette";
            }
            else if (line.StartsWith("const personaColorPaletteTokens = personaSharedColorNames.reduce(", StringComparison.Ordinal))
            {
                block = "persona";
            }
            else if (
                line.StartsWith(
                    "export const colorStatusTokens: ColorStatusTokens = Object.entries(statusColorMapping).reduce(",
                    StringComparison.Ordinal
                )
            )
            {
                block = "status";
            }
            else if (line.StartsWith("[`", StringComparison.Ordinal))
            {
                Match palettePart = PaletteTemplate().Match(line);
                Match statusPart = StatusTemplate().Match(line);
                if (block == "palette" && palettePart.Success)
                {
                    paletteTemplate.Add((palettePart.Groups[1].Value, palettePart.Groups[2].Value));
                }
                else if (block == "status" && statusPart.Success)
                {
                    statusTemplate.Add((statusPart.Groups[1].Value, statusPart.Groups[2].Value));
                }
                else if (block != "persona")
                {
                    throw new InvalidDataException($"{path}: the line \"{raw}\" is not a template this tool reads.");
                }
            }
            else if (
                line.StartsWith("statusColorPaletteTokens.", StringComparison.Ordinal)
                || line.StartsWith("colorStatusTokens.", StringComparison.Ordinal)
                || line.StartsWith("personaColorPaletteTokens.", StringComparison.Ordinal)
            )
            {
                Match patch = Patch().Match(line);
                if (!patch.Success)
                {
                    throw new InvalidDataException($"{path}: the line \"{raw}\" is not a patch this tool reads.");
                }

                if (patch.Groups[1].Value != "personaColorPaletteTokens")
                {
                    patches.Add((patch.Groups[1].Value, patch.Groups[2].Value, patch.Groups[3].Value));
                }
            }
            else
            {
                throw Unknown(path, raw);
            }
        }

        if (paletteTemplate.Count == 0 || statusTemplate.Count == 0)
        {
            throw new InvalidDataException($"{path} has no status palette or no status colors.");
        }

        // statusColorPaletteTokens: each status color with the template, then the patches, as the TypeScript assigns them.
        foreach (string name in statusNames)
        {
            foreach ((string suffix, string source) in paletteTemplate)
            {
                tokens.Set($"colorPalette{Capitalize(name)}{suffix}", PaletteSource(source, name, statusShared, mappedShared, mapping));
            }
        }

        foreach ((string _, string name, string source) in patches.Where(patch => patch.Target == "statusColorPaletteTokens"))
        {
            tokens.Set(name, PaletteSource(source, null, statusShared, mappedShared, mapping));
        }

        foreach ((string status, string color) in mapping)
        {
            foreach ((string suffix, string source) in statusTemplate)
            {
                tokens.Set($"colorStatus{Capitalize(status)}{suffix}", PaletteSource(source, color, statusShared, mappedShared, mapping));
            }
        }

        foreach ((string _, string name, string source) in patches.Where(patch => patch.Target == "colorStatusTokens"))
        {
            tokens.Set(name, PaletteSource(source, null, statusShared, mappedShared, mapping));
        }
    }

    // A source of the palette: `statusSharedColors[sharedColor].v`, `statusSharedColors.name.v`, `mappedStatusColors[sharedColor].v`,
    // `mappedStatusColors[statusColorMapping.status].v`, or a high-contrast constant.
    private ColorSource PaletteSource(
        string expression,
        string? sharedColor,
        HashSet<string> statusShared,
        HashSet<string> mappedShared,
        List<(string Status, string Color)> mapping
    )
    {
        if (HighContrastConstants.ContainsKey(expression))
        {
            return Source(expression, null);
        }

        int variantAt = expression.LastIndexOf('.');
        if (variantAt < 0)
        {
            throw new InvalidDataException($"{expression} is not a palette source this tool reads.");
        }

        string variant = expression[(variantAt + 1)..];
        string reference = expression[..variantAt];
        (string table, HashSet<string> names, string color) = reference switch
        {
            "statusSharedColors[sharedColor]" => (
                "statusSharedColors",
                statusShared,
                sharedColor ?? throw new InvalidDataException($"{expression} has no color.")
            ),
            "mappedStatusColors[sharedColor]" => (
                "mappedStatusColors",
                mappedShared,
                sharedColor ?? throw new InvalidDataException($"{expression} has no color.")
            ),
            _ when reference.StartsWith("statusSharedColors.", StringComparison.Ordinal) => (
                "statusSharedColors",
                statusShared,
                reference["statusSharedColors.".Length..]
            ),
            _ when reference.StartsWith("mappedStatusColors[statusColorMapping.", StringComparison.Ordinal) && reference.EndsWith(']') => (
                "mappedStatusColors",
                mappedShared,
                mapping.Single(entry => entry.Status == reference["mappedStatusColors[statusColorMapping.".Length..^1]).Color
            ),
            _ => throw new InvalidDataException($"{expression} is not a palette source this tool reads."),
        };
        if (!names.Contains(color))
        {
            throw new InvalidDataException($"{table} has no {color}.");
        }

        return ParseColor(GlobalColor(color, variant), $"{color}.{variant}");
    }

    private List<string> ReadStatusNames() => ReadNameList("sharedColorNames.ts", "statusSharedColorNames");

    private List<string> ReadNameList(string path, string name)
    {
        // Every list of the file is read, so that a line of any of them that is not a name stops the tool; the one named is kept.
        var lists = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var structure = new Structure();
        List<string>? current = null;
        foreach (string raw in Lines(path))
        {
            string line = raw.Trim();
            Match start = NameListStart().Match(line);
            if (current is null && start.Success)
            {
                current = [];
                lists.Add(start.Groups[1].Value, current);
            }
            else if (current is not null && line == "] as const;")
            {
                current = null;
            }
            else if (current is not null && NameListEntry().IsMatch(line))
            {
                current.Add(Unquote(line.TrimEnd(',')));
            }
            else if (current is not null || !(NameListOneLine().IsMatch(line) || structure.Skips(line)))
            {
                throw Unknown(path, raw);
            }
        }

        return lists.GetValueOrDefault(name) ?? throw new InvalidDataException($"{path} has no {name}.");
    }

    private List<(string Status, string Color)> ReadStatusMapping()
    {
        const string Path = "statusColorMapping.ts";
        return Objects(Path, "statusColorMapping")["statusColorMapping"].Select(entry => (entry.Key, Unquote(entry.Value))).ToList();
    }

    private List<PixelToken> ReadSpacing()
    {
        const string Path = "global/spacings.ts";
        Dictionary<string, List<(string Key, string Value)>> objects = Objects(Path, "spacings", "horizontalSpacings", "verticalSpacings");
        Dictionary<string, float> sizes = objects["spacings"].ToDictionary(entry => entry.Key, entry => Pixels(entry.Value), StringComparer.Ordinal);
        var tokens = new List<PixelToken>();
        foreach (string name in new[] { "horizontalSpacings", "verticalSpacings" })
        {
            foreach ((string key, string value) in objects[name])
            {
                string size = value.StartsWith("spacings.", StringComparison.Ordinal)
                    ? value["spacings.".Length..]
                    : throw new InvalidDataException($"{Path}: {key} is {value}.");
                tokens.Add(new PixelToken(key, sizes[size]));
            }
        }

        return tokens;
    }

    private List<PixelToken> ReadPixels(string path, string name) =>
        Objects(path, name)[name].Select(entry => new PixelToken(entry.Key, Pixels(entry.Value))).ToList();

    // global/typographyStyles.ts: each style names the tokens of global/fonts.ts; the family (fontFamilyBase, the web's list) is not
    // read (design §13.2, "使わないもの").
    private List<TextStyleToken> ReadTextStyles()
    {
        const string FontsPath = "global/fonts.ts";
        const string Path = "global/typographyStyles.ts";
        Dictionary<string, List<(string Key, string Value)>> fonts = Objects(FontsPath, FontObjects);
        Dictionary<string, string> font = FontObjects
            .SelectMany(name => fonts.GetValueOrDefault(name) ?? throw new InvalidDataException($"{FontsPath} has no {name}."))
            .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);

        var styles = new List<TextStyleToken>();
        string? style = null;
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        var structure = new Structure();

        // Before the object and after it, only the lines any file may hold.
        string part = "before";
        foreach (string raw in Lines(Path))
        {
            string line = raw.Trim();
            if (part == "before" && line == "export const typographyStyles: TypographyStyles = {")
            {
                part = "inside";
                continue;
            }

            if (part == "inside" && style is null && line == "};")
            {
                part = "after";
                continue;
            }

            if (part != "inside")
            {
                if (!structure.Skips(line))
                {
                    throw Unknown(Path, raw);
                }

                continue;
            }

            if (line.Length == 0)
            {
                continue;
            }

            if (style is null)
            {
                style = line.EndsWith(": {", StringComparison.Ordinal)
                    ? line[..^3]
                    : throw new InvalidDataException($"{Path}: the line \"{raw}\" starts no style.");
                fields.Clear();
                continue;
            }

            if (line == "},")
            {
                string Token(string field)
                {
                    string value = fields.GetValueOrDefault(field) ?? throw new InvalidDataException($"{Path}: {style} has no {field}.");
                    string name = value.StartsWith("tokens.", StringComparison.Ordinal)
                        ? value["tokens.".Length..]
                        : throw new InvalidDataException($"{Path}: {style}'s {field} is {value}.");
                    return font.GetValueOrDefault(name) ?? throw new InvalidDataException($"{FontsPath} has no {name}.");
                }

                if (fields.GetValueOrDefault("fontFamily") != "tokens.fontFamilyBase" || fields.Count != 4)
                {
                    throw new InvalidDataException($"{Path}: {style} has other fields than the base family, a size, a weight, and a line height.");
                }

                styles.Add(
                    new TextStyleToken(
                        style,
                        Pixels(Token("fontSize")),
                        Pixels(Token("lineHeight")),
                        int.Parse(Token("fontWeight"), NumberStyles.Integer, CultureInfo.InvariantCulture)
                    )
                );
                style = null;
                continue;
            }

            Match entry = ObjectEntry().Match(line);
            if (!entry.Success)
            {
                throw new InvalidDataException($"{Path}: the line \"{raw}\" is not a field this tool reads.");
            }

            fields.Add(entry.Groups[1].Value, entry.Groups[2].Value);
        }

        if (part != "after")
        {
            throw new InvalidDataException($"{Path} has no typographyStyles that ends with \"}};\".");
        }

        return styles;
    }

    // utils/shadows.ts: each shadow is `x y blur ${ambientColor}, x y blur ${keyColor}`, written once with the suffix "Brand" and
    // once without.
    private List<ShadowToken> ReadShadows()
    {
        const string Path = "utils/shadows.ts";
        var shadows = new List<ShadowToken>();
        var structure = new Structure();
        foreach (string raw in Lines(Path))
        {
            string line = raw.Trim();
            if (ShadowScaffolding.Contains(line) || structure.Skips(line))
            {
                continue;
            }

            if (!line.StartsWith("[`shadow", StringComparison.Ordinal))
            {
                throw Unknown(Path, raw);
            }

            Match shadow = ShadowLine().Match(line);
            string[] parts = shadow.Success ? shadow.Groups[2].Value.Split(", ") : [];
            if (parts.Length != 2)
            {
                throw new InvalidDataException($"{Path}: the line \"{raw}\" is not a shadow this tool reads.");
            }

            ShadowLayer Layer(string part)
            {
                Match layer = ShadowPart().Match(part);
                return layer.Success
                    ? new ShadowLayer(
                        Length(layer.Groups[1].Value),
                        Length(layer.Groups[2].Value),
                        Length(layer.Groups[3].Value),
                        layer.Groups[4].Value == "keyColor"
                    )
                    : throw new InvalidDataException($"{Path}: \"{part}\" is not a layer of a shadow.");
            }

            ShadowLayer ambient = Layer(parts[0]);
            ShadowLayer key = Layer(parts[1]);
            if (ambient.IsKey || !key.IsKey)
            {
                throw new InvalidDataException($"{Path}: {shadow.Groups[1].Value} is not the ambient layer and then the key layer.");
            }

            shadows.Add(new ShadowToken(shadow.Groups[1].Value, ambient, key));
        }

        return shadows.Count > 0 ? shadows : throw new InvalidDataException($"{Path} has no shadow.");
    }

    /// <summary>
    /// The lines any file may hold, trimmed: blank lines, comments (<c>// …</c>, and <c>/* … */</c> on one line or over several),
    /// and imports (<c>import … from '…';</c> on one line, or <c>import {</c> / <c>import type {</c>, one name a line, and
    /// <c>} from '…';</c>). It keeps whether it is inside a comment or an import, so one instance reads one file.
    /// </summary>
    private sealed class Structure
    {
        private bool _inComment;
        private bool _inImport;

        public bool Skips(string line)
        {
            if (_inComment)
            {
                _inComment = !line.EndsWith("*/", StringComparison.Ordinal);
                return true;
            }

            if (_inImport)
            {
                if (ImportEnd().IsMatch(line))
                {
                    _inImport = false;
                    return true;
                }

                return Shorthand().IsMatch(line);
            }

            if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal) || ImportLine().IsMatch(line))
            {
                return true;
            }

            if (line.StartsWith("/*", StringComparison.Ordinal))
            {
                _inComment = !line.EndsWith("*/", StringComparison.Ordinal);
                return true;
            }

            _inImport = ImportStart().IsMatch(line);
            return _inImport;
        }
    }

    /// <summary>
    /// Tokens by name in the order they were first set: a later value of a name replaces it in place, as a property of a JavaScript
    /// object keeps its place when it is assigned again.
    /// </summary>
    private sealed class OrderedTokens
    {
        private readonly List<string> _names = [];
        private readonly Dictionary<string, ColorSource> _values = new(StringComparer.Ordinal);

        public List<string> Names => _names;

        public IReadOnlyDictionary<string, ColorSource> Values => _values;

        public void Set(string name, ColorSource source)
        {
            if (!_values.ContainsKey(name))
            {
                _names.Add(name);
            }

            _values[name] = source;
        }
    }
}
