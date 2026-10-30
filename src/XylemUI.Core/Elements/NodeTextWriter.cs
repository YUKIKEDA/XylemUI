using System.Runtime.CompilerServices;

namespace XylemUI;

/// <summary>
/// Writes the text of a text node (<see cref="Elements.Text(TextBuilder)"/>, design §2.13).
/// </summary>
/// <param name="text">The writer: each <c>Write</c> (<see cref="NodeTextWriterExtensions"/>) appends to the text being written.</param>
public delegate void TextBuilder(ref NodeTextWriter text);

/// <summary>
/// The text a <see cref="TextBuilder"/> writes, in a buffer the node takes in place of its own, with no string made. The writes are the extension methods
/// of <see cref="NodeTextWriterExtensions"/>: spans, values that format themselves (<see cref="ISpanFormattable"/>), and interpolated
/// strings. The node takes the text when it differs from the one there (design §2.13).
/// </summary>
/// <remarks>
/// It is a struct and not a <c>ref struct</c>: <see cref="NodeTextWriterHandler"/> holds it in a <c>ref</c> field, and a <c>ref</c> field
/// cannot refer to a <c>ref struct</c> (CS9050). It holds no span, so nothing ties it to the stack.
/// </remarks>
public struct NodeTextWriter
{
    private readonly TextRangeWriter _ranges;
    private char[] _buffer;
    private int _length;

    /// <summary>
    /// Initializes a new instance of the <see cref="NodeTextWriter"/> struct, with nothing written.
    /// </summary>
    /// <param name="buffer">The buffer to write into.</param>
    /// <param name="ranges">The writer of the text's ranges.</param>
    internal NodeTextWriter(char[] buffer, TextRangeWriter ranges)
    {
        _buffer = buffer;
        _ranges = ranges;
        _length = 0;
    }

    /// <summary>
    /// Gets the writer of the text's ranges (design §4.8).
    /// </summary>
    internal readonly TextRangeWriter Ranges => _ranges;

    /// <summary>
    /// Opens a range of a style at what has been written.
    /// </summary>
    /// <param name="style">The range's style.</param>
    internal readonly void OpenSpan(in SpanStyle style) => _ranges.Open(_length, style, -1);

    /// <summary>
    /// Opens the range of a link at what has been written.
    /// </summary>
    /// <param name="link">The link.</param>
    internal readonly void OpenLink(in TextLink link) => _ranges.Open(_length, default, _ranges.Ranges.AddLink(link));

    /// <summary>
    /// Closes the innermost range at what has been written.
    /// </summary>
    internal readonly void CloseSpan() => _ranges.Close(_length);

    /// <summary>
    /// Writes the room of an inline object: its U+FFFC, and the child it stands for.
    /// </summary>
    /// <param name="child">The element the inline object shows.</param>
    /// <param name="alignment">How the object lines up with the text around it.</param>
    internal void AppendInline(Element child, PlaceholderAlignment alignment)
    {
        _ranges.AddInline(_length, child, alignment);
        Append("\uFFFC");
    }

    /// <summary>
    /// Ends the write.
    /// </summary>
    /// <exception cref="InvalidOperationException">A range is still open.</exception>
    internal readonly void Finish() => _ranges.Finish(_length);

    /// <summary>
    /// Gets the buffer written into; a write that needs more room replaces it with a larger one.
    /// </summary>
    internal readonly char[] Buffer => _buffer;

    /// <summary>
    /// Gets the number of characters written.
    /// </summary>
    internal readonly int Length => _length;

    /// <summary>
    /// Appends characters to the text.
    /// </summary>
    /// <param name="value">The characters.</param>
    internal void Append(ReadOnlySpan<char> value)
    {
        Ensure(value.Length);
        value.CopyTo(_buffer.AsSpan(_length));
        _length += value.Length;
    }

    /// <summary>
    /// Appends a value to the text, formatted with the current culture and no format string.
    /// </summary>
    /// <typeparam name="T">The value's type.</typeparam>
    /// <param name="value">The value.</param>
    internal void Append<T>(T value)
        where T : ISpanFormattable
    {
        int written;
        while (!value.TryFormat(_buffer.AsSpan(_length), out written, default, null))
        {
            Ensure(Math.Max(16, _buffer.Length - _length + 1));
        }

        _length += written;
    }

    // Room for count more characters, keeping what was written: the larger of what is needed and twice the buffer.
    private void Ensure(int count)
    {
        if (_length + count > _buffer.Length)
        {
            var grown = new char[Math.Max(_length + count, _buffer.Length * 2)];
            _buffer.AsSpan(0, _length).CopyTo(grown);
            _buffer = grown;
        }
    }
}

/// <summary>
/// The writes of a <see cref="NodeTextWriter"/> (design §2.13). They are extension methods on <c>ref NodeTextWriter</c> and not members of it:
/// an interpolated string handler takes the writer by reference only from a parameter that names it
/// (<c>[InterpolatedStringHandlerArgument("writer")]</c>); from the receiver of a member it takes a copy (CS1620). They are all here so
/// that an interpolated string takes the handler, which allocates nothing, and not the overload of a span, which would make a string.
/// </summary>
public static class NodeTextWriterExtensions
{
    /// <summary>
    /// Appends characters.
    /// </summary>
    /// <param name="writer">The writer.</param>
    /// <param name="value">The characters.</param>
    public static void Write(this ref NodeTextWriter writer, ReadOnlySpan<char> value) => writer.Append(value);

    /// <summary>
    /// Appends a value that formats itself, with no boxing and no string.
    /// </summary>
    /// <typeparam name="T">The value's type.</typeparam>
    /// <param name="writer">The writer.</param>
    /// <param name="value">The value.</param>
    public static void Write<T>(this ref NodeTextWriter writer, T value)
        where T : ISpanFormattable => writer.Append(value);

    /// <summary>
    /// Appends an interpolated string, each part written straight into the buffer.
    /// </summary>
    /// <param name="writer">The writer.</param>
    /// <param name="handler">The handler the compiler builds from the interpolated string; it has written the parts already.</param>
    public static void Write(this ref NodeTextWriter writer, [InterpolatedStringHandlerArgument("writer")] ref NodeTextWriterHandler handler)
    {
        _ = writer;
        _ = handler;
    }

    /// <summary>
    /// Opens a range of a style (design §4.8), closed by <c>using</c>. Ranges nest: the inner one takes the fields it leaves
    /// <see langword="null"/> from the outer one, and the outermost from the text node's own style and color.
    /// </summary>
    /// <param name="writer">The writer.</param>
    /// <param name="style">The range's style. Make it once and use it again in each write.</param>
    /// <returns>The scope that closes the range.</returns>
    public static TextSpanScope Style(this ref NodeTextWriter writer, in SpanStyle style)
    {
        writer.OpenSpan(style);
        return new TextSpanScope(ref writer);
    }

    /// <summary>
    /// Opens the range of a link (design §4.8), closed by <c>using</c>. A left click, or a touch tap, on the range calls
    /// <paramref name="onClick"/> in place of the text node's own Click; a pointer over it takes the hand cursor.
    /// </summary>
    /// <param name="writer">The writer.</param>
    /// <param name="onClick">What a click on the link calls.</param>
    /// <param name="onEnter">What a pointer that comes over the link calls.</param>
    /// <param name="onLeave">What a pointer that leaves the link calls.</param>
    /// <param name="hovered">The look of the link while a pointer is over it, over its own: only its color, background, decoration,
    /// and shadow are read.</param>
    /// <returns>The scope that closes the range.</returns>
    public static TextSpanScope Link(
        this ref NodeTextWriter writer,
        PointerHandler onClick,
        PointerHandler? onEnter = null,
        PointerHandler? onLeave = null,
        in SpanStyle hovered = default
    )
    {
        ArgumentNullException.ThrowIfNull(onClick);
        writer.OpenLink(new TextLink(onClick, onEnter, onLeave, hovered));
        return new TextSpanScope(ref writer);
    }

    /// <summary>
    /// Places an element on the text's line, as an inline object (design §4.8): the text node takes it as a child, measures it
    /// under the line's width, and leaves it the room of one character, which the lines may break before and after. It takes the
    /// pointer, the focus, and the screen reader as any child does, and over a link it takes the pointer before the link.
    /// </summary>
    /// <remarks>
    /// Make the element once, outside the write, in the component that keeps it: a write runs again when what it reads changes,
    /// and an element made inside it would be made again, losing its state. The text node lets go of an element a later write
    /// leaves out, and of all of them before it goes away; the scope that made the element removes it.
    /// </remarks>
    /// <param name="writer">The writer.</param>
    /// <param name="child">The element.</param>
    /// <param name="alignment">Where it sits on its line.</param>
    /// <exception cref="ArgumentException">The element is not of the text node's tree.</exception>
    /// <exception cref="InvalidOperationException">The element was removed, has another parent, or this write placed it
    /// already.</exception>
    public static void Inline(this ref NodeTextWriter writer, Element child, PlaceholderAlignment alignment = PlaceholderAlignment.Baseline) =>
        writer.AppendInline(child, alignment);
}

/// <summary>
/// Writes an interpolated string into a <see cref="NodeTextWriter"/> part by part (design §2.13). It holds the writer by reference, so the
/// writer's length moves with what the parts wrote; a copy of the writer would lose it.
/// </summary>
[InterpolatedStringHandler]
public ref struct NodeTextWriterHandler
{
    private ref NodeTextWriter _writer;

    /// <summary>
    /// Initializes a new instance of the <see cref="NodeTextWriterHandler"/> struct for the compiler.
    /// </summary>
    /// <param name="literalLength">The length of the literal parts.</param>
    /// <param name="formattedCount">The number of formatted parts.</param>
    /// <param name="writer">The writer the parts go to.</param>
    public NodeTextWriterHandler(int literalLength, int formattedCount, ref NodeTextWriter writer)
    {
        _ = literalLength;
        _ = formattedCount;
        _writer = ref writer;
    }

    /// <summary>
    /// Writes a literal part.
    /// </summary>
    /// <param name="value">The part.</param>
    public readonly void AppendLiteral(string value) => _writer.Append(value);

    /// <summary>
    /// Writes a value that formats itself.
    /// </summary>
    /// <typeparam name="T">The value's type.</typeparam>
    /// <param name="value">The value.</param>
    public readonly void AppendFormatted<T>(T value)
        where T : ISpanFormattable => _writer.Append(value);

    /// <summary>
    /// Writes characters.
    /// </summary>
    /// <param name="value">The characters.</param>
    public readonly void AppendFormatted(ReadOnlySpan<char> value) => _writer.Append(value);

    /// <summary>
    /// Writes a string; <see langword="null"/> writes nothing.
    /// </summary>
    /// <param name="value">The string.</param>
    public readonly void AppendFormatted(string? value) => _writer.Append(value);
}
