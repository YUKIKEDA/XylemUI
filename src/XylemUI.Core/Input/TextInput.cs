namespace XylemUI;

/// <summary>
/// One input of text as the platform hands it to Core (design §4.9, "Text input on Windows"): the entry the tests inject text
/// through (design §12.4).
/// </summary>
/// <param name="Text">The text: one or more whole code points, with no control character.</param>
/// <param name="Timestamp">When the platform received the input.</param>
internal readonly record struct TextInput(string Text, TimeSpan Timestamp);
