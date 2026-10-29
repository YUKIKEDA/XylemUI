namespace XylemUI;

/// <summary>
/// Estimates a pointer's velocity from its last positions (design §7, "Touch and pen", row I1b), as Flutter's
/// <c>VelocityTracker</c> does (<c>gestures/velocity_tracker.dart</c> lines 138 to 256): a polynomial of degree 2 fitted by
/// least squares to at most 20 positions of the last 100 ms, cut where two positions are more than 40 ms apart.
/// </summary>
/// <remarks>
/// One tracker belongs to a pointer's state and is reused for each of its presses: it allocates nothing after it is made.
/// </remarks>
internal sealed class VelocityTracker
{
    private const int HistorySize = 20;
    private const double HorizonMilliseconds = 100;
    private const double StoppedMilliseconds = 40;
    private const int MinSampleSize = 3;

    // Flutter's precisionErrorTolerance (foundation/constants.dart line 71).
    private const double PrecisionTolerance = 1e-10;

    // A ring of the positions, the newest at _index.
    private readonly double[] _x = new double[HistorySize];
    private readonly double[] _y = new double[HistorySize];
    private readonly double[] _time = new double[HistorySize];
    private int _index;
    private int _count;

    /// <summary>
    /// Forgets the positions: a new press starts.
    /// </summary>
    public void Reset()
    {
        _index = 0;
        _count = 0;
    }

    /// <summary>
    /// Adds a position.
    /// </summary>
    /// <param name="time">When the platform received the input.</param>
    /// <param name="x">The column, in logical pixels.</param>
    /// <param name="y">The row, in logical pixels.</param>
    public void Add(TimeSpan time, double x, double y)
    {
        _index = (_index + 1) % HistorySize;
        _x[_index] = x;
        _y[_index] = y;
        _time[_index] = time.TotalMilliseconds;
        _count = Math.Min(_count + 1, HistorySize);
    }

    /// <summary>
    /// Returns the velocity at the newest position, in logical pixels a second, and how far the positions it used moved, from the
    /// oldest to the newest. Both are zero when the pointer has not moved for 40 ms before <paramref name="now"/>, and the velocity
    /// is zero when fewer than 3 positions are usable.
    /// </summary>
    /// <param name="now">When the velocity is asked for: the release's time.</param>
    /// <returns>The velocity and the offset.</returns>
    public (double VelocityX, double VelocityY, double OffsetX, double OffsetY) Estimate(TimeSpan now)
    {
        if (_count == 0 || now.TotalMilliseconds - _time[_index] > StoppedMilliseconds)
        {
            return default;
        }

        Span<double> x = stackalloc double[HistorySize];
        Span<double> y = stackalloc double[HistorySize];
        Span<double> time = stackalloc double[HistorySize];
        int samples = 0;
        int index = _index;
        double newest = _time[_index];
        double previous = newest;
        int oldestIndex = _index;
        while (samples < _count)
        {
            double age = newest - _time[index];
            double delta = Math.Abs(_time[index] - previous);
            previous = _time[index];
            if (age > HorizonMilliseconds || delta > StoppedMilliseconds)
            {
                break;
            }

            oldestIndex = index;
            x[samples] = _x[index];
            y[samples] = _y[index];
            time[samples] = -age;
            samples++;
            index = (index == 0 ? HistorySize : index) - 1;
        }

        double offsetX = _x[_index] - _x[oldestIndex];
        double offsetY = _y[_index] - _y[oldestIndex];
        if (
            samples >= MinSampleSize
            && Slope(time[..samples], x[..samples], out double slopeX)
            && Slope(time[..samples], y[..samples], out double slopeY)
        )
        {
            // From pixels a millisecond to pixels a second.
            return (slopeX * 1000, slopeY * 1000, offsetX, offsetY);
        }

        return (0, 0, offsetX, offsetY);
    }

    // The coefficient of degree 1 of a polynomial of degree 2 fitted to the points by least squares, every weight 1: the QR
    // decomposition by Gram-Schmidt and the back substitution of Flutter's LeastSquaresSolver (gestures/lsq_solver.dart lines 106 to
    // 171), in the same order of operations. False when the points cannot fix a curve.
    private static bool Slope(ReadOnlySpan<double> x, ReadOnlySpan<double> y, out double slope)
    {
        const int N = 3;
        int m = x.Length;
        Span<double> a = stackalloc double[N * HistorySize];
        Span<double> q = stackalloc double[N * HistorySize];
        Span<double> r = stackalloc double[N * N];
        Span<double> coefficients = stackalloc double[N];
        for (int h = 0; h < m; h++)
        {
            a[h] = 1;
            for (int i = 1; i < N; i++)
            {
                a[(i * m) + h] = a[((i - 1) * m) + h] * x[h];
            }
        }

        for (int j = 0; j < N; j++)
        {
            for (int h = 0; h < m; h++)
            {
                q[(j * m) + h] = a[(j * m) + h];
            }

            for (int i = 0; i < j; i++)
            {
                double dot = Dot(q.Slice(j * m, m), q.Slice(i * m, m));
                for (int h = 0; h < m; h++)
                {
                    q[(j * m) + h] -= dot * q[(i * m) + h];
                }
            }

            double norm = Math.Sqrt(Dot(q.Slice(j * m, m), q.Slice(j * m, m)));
            if (norm < PrecisionTolerance)
            {
                slope = 0;
                return false;
            }

            double inverseNorm = 1.0 / norm;
            for (int h = 0; h < m; h++)
            {
                q[(j * m) + h] *= inverseNorm;
            }

            for (int i = 0; i < N; i++)
            {
                r[(j * N) + i] = i < j ? 0.0 : Dot(q.Slice(j * m, m), a.Slice(i * m, m));
            }
        }

        for (int i = N - 1; i >= 0; i--)
        {
            coefficients[i] = Dot(q.Slice(i * m, m), y);
            for (int j = N - 1; j > i; j--)
            {
                coefficients[i] -= r[(i * N) + j] * coefficients[j];
            }

            coefficients[i] /= r[(i * N) + i];
        }

        slope = coefficients[1];
        return true;
    }

    private static double Dot(ReadOnlySpan<double> left, ReadOnlySpan<double> right)
    {
        double result = 0;
        for (int i = 0; i < left.Length; i++)
        {
            result += left[i] * right[i];
        }

        return result;
    }
}

/// <summary>
/// A fling along one axis (design §7, "Touch and pen", row I1b): Flutter's <c>ClampingScrollSimulation</c>
/// (<c>widgets/scroll_simulation.dart</c> lines 165 to 266), the distance and duration of Android's scroller.
/// </summary>
internal readonly struct ClampingFling
{
    // Flutter's _kInflexion, _physicalCoeff, the default friction, and _kDecelerationRate.
    private const double Inflexion = 0.35;
    private const double PhysicalCoefficient = 9.80665 * 39.37 * 160.0 * 0.84;
    private const double Friction = 0.015;
    private static readonly double DecelerationRate = Math.Log(0.78) / Math.Log(0.9);

    /// <summary>
    /// Initializes a new instance of the <see cref="ClampingFling"/> struct.
    /// </summary>
    /// <param name="position">The position where it starts, in logical pixels.</param>
    /// <param name="velocity">The velocity, in logical pixels a second; not zero.</param>
    public ClampingFling(double position, double velocity)
    {
        double referenceVelocity = Friction * PhysicalCoefficient / Inflexion;
        double androidDuration = Math.Pow(Math.Abs(velocity) / referenceVelocity, 1 / (DecelerationRate - 1.0));
        Position = position;
        Duration = DecelerationRate * Inflexion * androidDuration;
        Distance = velocity * Duration / DecelerationRate;
    }

    /// <summary>
    /// Gets the position where it starts.
    /// </summary>
    public double Position { get; }

    /// <summary>
    /// Gets how long it runs, in seconds.
    /// </summary>
    public double Duration { get; }

    /// <summary>
    /// Gets the signed distance it travels, in logical pixels.
    /// </summary>
    public double Distance { get; }

    /// <summary>
    /// Returns the position a time after the start.
    /// </summary>
    /// <param name="seconds">The time since the start.</param>
    /// <returns>The position, in logical pixels.</returns>
    public double At(double seconds)
    {
        double t = Math.Clamp(seconds / Duration, 0.0, 1.0);
        return Position + (Distance * (1.0 - Math.Pow(1.0 - t, DecelerationRate)));
    }
}
