using System.Text;

namespace XylemUI;

/// <summary>
/// Reads a CLDR table that tools/CldrTableGen wrote as bytes (design §4.11, "Declaration and reference"): bytes, UTF-16 code units
/// as two bytes, little-endian, strings as a length byte and that many bytes of UTF-8, and currency symbols as strings whose length
/// byte also holds two bits.
/// </summary>
internal ref struct CldrTableReader
{
    private readonly ReadOnlySpan<byte> _table;
    private int _position;

    /// <summary>
    /// Initializes a new instance of the <see cref="CldrTableReader"/> struct at the table's start.
    /// </summary>
    /// <param name="table">The table.</param>
    public CldrTableReader(ReadOnlySpan<byte> table)
    {
        _table = table;
    }

    /// <summary>
    /// Gets a value indicating whether every byte of the table has been read.
    /// </summary>
    public readonly bool End => _position >= _table.Length;

    /// <summary>
    /// Reads a byte.
    /// </summary>
    /// <returns>The byte.</returns>
    public int ReadByte() => _table[_position++];

    /// <summary>
    /// Reads a UTF-16 code unit.
    /// </summary>
    /// <returns>The code unit.</returns>
    public char ReadChar()
    {
        char value = (char)(_table[_position] | (_table[_position + 1] << 8));
        _position += 2;
        return value;
    }

    /// <summary>
    /// Reads a string.
    /// </summary>
    /// <returns>The string; empty for a length of 0.</returns>
    public string ReadString()
    {
        int length = _table[_position];
        string value = Encoding.UTF8.GetString(_table.Slice(_position + 1, length));
        _position += 1 + length;
        return value;
    }

    /// <summary>
    /// Skips a string.
    /// </summary>
    public void SkipString() => _position += 1 + _table[_position];

    /// <summary>
    /// Reads a currency symbol: a length byte whose 6 low bits are the UTF-8 length and whose 2 high bits say whether the grapheme
    /// cluster next to the digits starts with a letter (bit 7 for the last cluster, bit 6 for the first), and that many bytes of
    /// UTF-8. tools/CldrTableGen judges the clusters, so the runtime needs no grapheme break tables (Issue #906).
    /// </summary>
    /// <returns>The symbol, empty for a length of 0; whether its last grapheme cluster starts with a letter; and whether its first
    /// one does.</returns>
    public (string Symbol, bool LastIsLetter, bool FirstIsLetter) ReadCurrencySymbol()
    {
        int header = _table[_position];
        int length = header & 0x3F;
        string value = Encoding.UTF8.GetString(_table.Slice(_position + 1, length));
        _position += 1 + length;
        return (value, (header & 0x80) != 0, (header & 0x40) != 0);
    }

    /// <summary>
    /// Skips a currency symbol.
    /// </summary>
    public void SkipCurrencySymbol() => _position += 1 + (_table[_position] & 0x3F);

    /// <summary>
    /// Reads three ASCII bytes, a currency's code, and compares them with a code.
    /// </summary>
    /// <param name="code">The code, three ASCII letters.</param>
    /// <returns><see langword="true"/> when the bytes are the code.</returns>
    public bool ReadCode(string code)
    {
        bool same = _table[_position] == code[0] && _table[_position + 1] == code[1] && _table[_position + 2] == code[2];
        _position += 3;
        return same;
    }
}
