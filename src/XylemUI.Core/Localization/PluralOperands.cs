namespace XylemUI;

/// <summary>
/// The operands a plural rule reads (UTS #35 part 3, "Plural Operand Meanings"; design §4.11, "Passing the number"): the absolute
/// value, its integer digits, the number of its visible fraction digits with and without trailing zeros, those digits as a whole
/// number with and without trailing zeros, and the exponent of a compact decimal.
/// </summary>
internal readonly struct PluralOperands
{
    /// <summary>
    /// The absolute value of the source number (n).
    /// </summary>
    public readonly decimal N;

    /// <summary>
    /// The integer digits of n (i).
    /// </summary>
    public readonly decimal I;

    /// <summary>
    /// The number of visible fraction digits of n, with trailing zeros (v).
    /// </summary>
    public readonly int V;

    /// <summary>
    /// The number of visible fraction digits of n, without trailing zeros (w).
    /// </summary>
    public readonly int W;

    /// <summary>
    /// The visible fraction digits of n as a whole number, with trailing zeros (f).
    /// </summary>
    public readonly decimal F;

    /// <summary>
    /// The visible fraction digits of n as a whole number, without trailing zeros (t).
    /// </summary>
    public readonly decimal T;

    /// <summary>
    /// The exponent of the power of 10 of a compact decimal (c, and its older name e); 0 for a number written in full.
    /// </summary>
    public readonly int E;

    private PluralOperands(decimal n, decimal i, int v, int w, decimal f, decimal t, int e)
    {
        N = n;
        I = i;
        V = v;
        W = w;
        F = f;
        T = t;
        E = e;
    }

    /// <summary>
    /// Returns the operands of a whole number, which has no fraction digits.
    /// </summary>
    /// <param name="value">The number.</param>
    /// <returns>The operands.</returns>
    public static PluralOperands Of(long value)
    {
        // Through decimal, so that the absolute value of long.MinValue does not overflow.
        decimal n = Math.Abs((decimal)value);
        return new PluralOperands(n, n, 0, 0, 0, 0, 0);
    }

    /// <summary>
    /// Returns the operands of a decimal, whose scale is the number of its visible fraction digits (<c>1.0m</c> has v = 1).
    /// </summary>
    /// <param name="value">The number.</param>
    /// <param name="exponent">The exponent of a compact decimal (6 for 1.2M); 0 for a number written in full.</param>
    /// <returns>The operands.</returns>
    public static PluralOperands Of(decimal value, int exponent)
    {
        decimal n = Math.Abs(value);
        decimal i = decimal.Truncate(n);
        int v = n.Scale;
        decimal power = 1;
        for (int k = 0; k < v; k++)
        {
            power *= 10;
        }

        // (n - i) is below 1 and has v digits, so the product is a whole number below 10^28, inside decimal's range.
        decimal f = decimal.Truncate((n - i) * power);
        decimal t = f;
        int w = f == 0 ? 0 : v;
        while (t != 0 && t % 10 == 0)
        {
            t /= 10;
            w--;
        }

        return new PluralOperands(n, i, v, w, f, t, exponent);
    }

    /// <summary>
    /// Returns a value indicating whether an operand is a whole number in a range of a rule: in CLDR's rules a range holds only for
    /// whole values (<c>n = 2..4</c> holds for 3, not for 2.5).
    /// </summary>
    /// <param name="value">The operand, or the operand modulo a value.</param>
    /// <param name="low">The range's first value.</param>
    /// <param name="high">The range's last value.</param>
    /// <returns><see langword="true"/> when the operand is whole and in the range, both ends included.</returns>
    public static bool InRange(decimal value, int low, int high) => value >= low && value <= high && decimal.Truncate(value) == value;
}
