using System.Runtime.CompilerServices;

namespace XylemUI;

/// <summary>
/// Writes the text of a text node (<see cref="Elements.Text(TextBuilder)"/>, design §2.13).
/// </summary>
/// <param name="text">The writer: each <c>Write</c> appends to the text being written.</param>
public delegate void TextBuilder(ref TextWriter text);

/// <summary>
/// Appends to the text a <see cref="TextBuilder"/> writes, in the node's own buffer, with no string made: spans, values that format
/// themselves (<see cref="ISpanFormattable"/>), and interpolated strings. The node takes the text when it differs from the one
/// there (design §2.13).
/// </summary>
/// <remarks>
/// It is a struct and not a <c>ref struct</c>: <see cref="TextWriterHandler"/> holds it in a <c>ref</c> field, and a <c>ref</c> field
/// cannot refer to a <c>ref struct</c> (CS9050). It holds no span, so nothing ties it to the stack.
/// </remarks>
public struct TextWriter
{
    private readonly TextLeaf _leaf;
    private char[] _buffer;
    private int _length;

    internal TextWriter(TextLeaf leaf)
    {
        _leaf = leaf;
        _buffer = leaf.SpareFor(0, 0);
        _length = 0;
    }

    /// <summary>
    /// Gets the number of characters written.
    /// </summary>
    internal readonly int Length => _length;

    /// <summary>
    /// Appends characters.
    /// </summary>
    /// <param name="value">The characters.</param>
    public void Write(ReadOnlySpan<char> value)
    {
        Ensure(value.Length);
        value.CopyTo(_buffer.AsSpan(_length));
        _length += value.Length;
    }

    /// <summary>
    /// Appends a value that formats itself, with no boxing and no string.
    /// </summary>
    /// <typeparam name="T">The value's type.</typeparam>
    /// <param name="value">The value.</param>
    public void Write<T>(T value)
        where T : ISpanFormattable
    {
        int written;
        while (!value.TryFormat(_buffer.AsSpan(_length), out written, default, null))
        {
            Ensure(Math.Max(16, _buffer.Length - _length + 1));
        }

        _length += written;
    }

    /// <summary>
    /// Appends an interpolated string, each part written straight into the buffer.
    /// </summary>
    /// <param name="handler">The handler the compiler builds from the interpolated string; it has written the parts already.</param>
    public readonly void Write([InterpolatedStringHandlerArgument("")] ref TextWriterHandler handler) => _ = handler;

    // Room for count more characters, keeping what was written.
    private void Ensure(int count)
    {
        if (_length + count > _buffer.Length)
        {
            _buffer = _leaf.SpareFor(_length + count, _length);
        }
    }
}

/// <summary>
/// Writes an interpolated string into a <see cref="TextWriter"/> part by part (design §2.13). It holds the writer by reference, so the
/// writer's length moves with what the parts wrote; a copy of the writer would lose it.
/// </summary>
[InterpolatedStringHandler]
public ref struct TextWriterHandler
{
    private ref TextWriter _writer;

    /// <summary>
    /// Initializes a new instance of the <see cref="TextWriterHandler"/> struct for the compiler.
    /// </summary>
    /// <param name="literalLength">The length of the literal parts.</param>
    /// <param name="formattedCount">The number of formatted parts.</param>
    /// <param name="writer">The writer the parts go to.</param>
    public TextWriterHandler(int literalLength, int formattedCount, ref TextWriter writer)
    {
        _ = literalLength;
        _ = formattedCount;
        _writer = ref writer;
    }

    /// <summary>
    /// Writes a literal part.
    /// </summary>
    /// <param name="value">The part.</param>
    public readonly void AppendLiteral(string value) => _writer.Write(value);

    /// <summary>
    /// Writes a value that formats itself.
    /// </summary>
    /// <typeparam name="T">The value's type.</typeparam>
    /// <param name="value">The value.</param>
    public readonly void AppendFormatted<T>(T value)
        where T : ISpanFormattable => _writer.Write(value);

    /// <summary>
    /// Writes characters.
    /// </summary>
    /// <param name="value">The characters.</param>
    public readonly void AppendFormatted(ReadOnlySpan<char> value) => _writer.Write(value);

    /// <summary>
    /// Writes a string; <see langword="null"/> writes nothing.
    /// </summary>
    /// <param name="value">The string.</param>
    public readonly void AppendFormatted(string? value) => _writer.Write(value);
}
