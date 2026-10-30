namespace XylemUI;

/// <summary>
/// A damped spring that moves an <see cref="Animation"/>'s value toward a target and carries its speed when the target changes
/// (design §2.14): Flutter's <c>SpringDescription</c> and GPUI's <c>SpringConfig</c>.
/// </summary>
/// <param name="Stiffness">How hard the spring pulls, per unit of distance; above 0.</param>
/// <param name="Damping">How much it slows, per unit of speed; 0 or more.</param>
/// <param name="Mass">The mass it moves; above 0.</param>
public readonly record struct Spring(float Stiffness, float Damping, float Mass = 1)
{
    /// <summary>
    /// Gets a spring that settles without overshooting in about a third of a second: critically damped, stiffness 300, mass 1
    /// (damping 2√300).
    /// </summary>
    public static Spring Default { get; } = new(300, 2 * MathF.Sqrt(300), 1);

    /// <summary>
    /// Moves a position and a speed by a time toward a target, in steps of at most 1/1,000 s, so that the result does not depend on
    /// the frames' rate.
    /// </summary>
    /// <param name="position">The position; moved.</param>
    /// <param name="velocity">The speed, per second; moved.</param>
    /// <param name="target">Where the spring is at rest.</param>
    /// <param name="seconds">The time, 0 or more.</param>
    internal void Step(ref float position, ref float velocity, float target, double seconds)
    {
        const double Step = 0.001;
        while (seconds > 0)
        {
            float dt = (float)Math.Min(Step, seconds);
            seconds -= dt;

            // Semi-implicit Euler: the speed first, then the position with the new speed.
            float force = (-Stiffness * (position - target)) - (Damping * velocity);
            velocity += force / Mass * dt;
            position += velocity * dt;
        }
    }

    /// <summary>
    /// Returns whether a position and a speed are at rest at a target: within 1/1,000 of it, and slower than 1/1,000 a second.
    /// </summary>
    /// <param name="position">The position.</param>
    /// <param name="velocity">The speed.</param>
    /// <param name="target">The target.</param>
    /// <returns><see langword="true"/> at rest.</returns>
    internal static bool IsSettled(float position, float velocity, float target) =>
        MathF.Abs(position - target) < 1e-3f && MathF.Abs(velocity) < 1e-3f;
}
