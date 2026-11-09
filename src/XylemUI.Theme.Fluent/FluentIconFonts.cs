using System.Reflection;

namespace XylemUI.Theme.Fluent;

/// <summary>
/// The families of the two Fluent icon fonts, each registered with <see cref="AppFonts"/> the first time a glyph of it is read
/// (design §5.0.4, "How they are embedded"). The fonts are the ones the build kept for the app: resources that
/// XylemUI.Theme.Fluent.targets writes into the app's assembly.
/// </summary>
/// <remarks>
/// A glyph is read where elements are made, on the UI thread, so the flags need no lock; <see cref="AppFonts.Add"/> checks the
/// thread. The assembly that holds a font is looked for in the entry assembly, then in every loaded assembly, so that the fonts are
/// found under a test host and in an app another program loads.
/// </remarks>
internal static class FluentIconFonts
{
    private const string RegularFamily = "FluentSystemIcons-Regular";
    private const string FilledFamily = "FluentSystemIcons-Filled";

    private static bool _regular;
    private static bool _filled;

    /// <summary>
    /// Gets the family of the Regular font, registering it the first time.
    /// </summary>
    /// <exception cref="InvalidOperationException">The calling thread is not the UI thread, or no loaded assembly holds the font.</exception>
    public static string Regular
    {
        get
        {
            if (!_regular)
            {
                Register("Regular");
                _regular = true;
            }

            return RegularFamily;
        }
    }

    /// <summary>
    /// Gets the family of the Filled font, registering it the first time.
    /// </summary>
    /// <exception cref="InvalidOperationException">The calling thread is not the UI thread, or no loaded assembly holds the font.</exception>
    public static string Filled
    {
        get
        {
            if (!_filled)
            {
                Register("Filled");
                _filled = true;
            }

            return FilledFamily;
        }
    }

    /// <summary>
    /// Returns the name of the resource that holds a style's kept font, which tools/FluentIconsTool writes.
    /// </summary>
    /// <param name="style">Regular or Filled.</param>
    /// <returns>The name.</returns>
    internal static string ResourceName(string style) => $"XylemUI.Icons.{style}.ttf";

    /// <summary>
    /// Registers a style's kept font from the assembly that holds it.
    /// </summary>
    /// <param name="style">Regular or Filled.</param>
    /// <exception cref="InvalidOperationException">The calling thread is not the UI thread, or no loaded assembly holds the font.</exception>
    internal static void Register(string style)
    {
        UiThread.VerifyAccess();
        string name = ResourceName(style);
        byte[] data =
            Read(Assembly.GetEntryAssembly(), name)
            ?? ReadFromLoaded(name)
            ?? throw new InvalidOperationException(
                $"No loaded assembly holds the Fluent icon font {name}. The project that uses FluentIcons imports "
                    + "XylemUI.Theme.Fluent.targets, which keeps the glyphs it uses and writes them into its assembly."
            );
        AppFonts.Add(data);
    }

    private static byte[]? ReadFromLoaded(string name)
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (Read(assembly, name) is { } data)
            {
                return data;
            }
        }

        return null;
    }

    // The resource's bytes, copied once into the array AppFonts keeps (design §4.5, "Copies").
    private static byte[]? Read(Assembly? assembly, string name)
    {
        using Stream? stream = assembly?.GetManifestResourceStream(name);
        if (stream is null)
        {
            return null;
        }

        byte[] data = new byte[stream.Length];
        stream.ReadExactly(data);
        return data;
    }
}
