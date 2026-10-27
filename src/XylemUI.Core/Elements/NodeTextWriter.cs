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
    private char[] _buffer;
    private int _length;

    internal NodeTextWriter(char[] buffer)
    {
        _buffer = buffer;
        _length = 0;
    }

    /// <summary>
    /// Gets the buffer written into; a write that needs more room replaces it with a larger one.
    /// </summary>
    internal readonly char[] Buffer => _buffer;

    /// <summary>
    /// Gets the number of characters written.
    /// </summary>
    internal readonly int Length => _length;

    internal void Append(ReadOnlySpan<char> value)
    {
        Ensure(value.Length);
        value.CopyTo(_buffer.AsSpan(_length));
        _length += value.Length;
    }

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
