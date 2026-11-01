namespace XylemUI;

/// <summary>
/// The OS's clipboard, as plain text (design §6.0, §4.9 "The clipboard"). Synchronous: the clipboard of Windows opens on the
/// calling thread, and a call that cannot open it fails at once rather than waiting to try again on the UI thread.
/// </summary>
public interface IClipboardBackend
{
    /// <summary>
    /// Returns the clipboard's text.
    /// </summary>
    /// <returns>The text; <see langword="null"/> when the clipboard holds none or cannot be opened.</returns>
    string? GetText();

    /// <summary>
    /// Puts text on the clipboard, in place of what it held.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>Whether the text is on the clipboard: <see langword="false"/> when the clipboard cannot be opened.</returns>
    bool SetText(string text);

    /// <summary>
    /// Returns whether the clipboard holds text, without reading it: a context menu enables its paste with it when it opens (design
    /// §4.9, "The context menu").
    /// </summary>
    /// <returns>Whether the clipboard holds text.</returns>
    bool HasText();
}
