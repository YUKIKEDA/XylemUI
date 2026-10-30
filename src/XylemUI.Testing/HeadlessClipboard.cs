namespace XylemUI.Testing;

/// <summary>
/// The clipboard of a <see cref="HeadlessApp"/>: plain text in memory, which a test reads and sets (design §4.9, "The clipboard";
/// §12.4).
/// </summary>
public sealed class HeadlessClipboard : IClipboardBackend
{
    /// <summary>
    /// Gets or sets the clipboard's text; <see langword="null"/> for an empty clipboard.
    /// </summary>
    public string? Text { get; set; }

    /// <inheritdoc/>
    public string? GetText() => Text;

    /// <inheritdoc/>
    public bool SetText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Text = text;
        return true;
    }
}
