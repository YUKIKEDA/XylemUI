namespace XylemUI;

/// <summary>
/// Holds the function that makes the OS host the app was built with (design §6.0, "ホストの選び方"). The host's <c>Register</c>
/// method, which the app's module initializer calls (<see cref="PlatformHostAttribute"/>), sets it; <c>XylemApp.Run</c> reads it, and
/// throws <see cref="InvalidOperationException"/> naming the runtime identifier when no host set it.
/// </summary>
internal static class PlatformHostRegistry
{
    /// <summary>
    /// Gets or sets the function that makes the host; <see langword="null"/> until a host is registered.
    /// </summary>
    public static Func<IPlatformApp>? Factory { get; set; }
}
