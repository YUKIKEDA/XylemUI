namespace XylemUI;

/// <summary>
/// Names the OS host of the assembly that carries it (design §6.0, "ホストの選び方"): a public static class with a method
/// <c>public static void Register()</c> that sets <see cref="PlatformHostRegistry.Factory"/>. In an executable that references the
/// assembly, the source generator of <c>XylemUI.Generators</c> calls that method in the app's module initializer. With two such
/// assemblies referenced, the generator reports the error XYLEM008 and registers neither; a class without the method is the error
/// XYLEM009, and is not registered.
/// </summary>
/// <param name="host">The class with the <c>Register</c> method.</param>
/// <remarks>
/// Internal: a host sees it through <c>InternalsVisibleTo</c>, and the generator reads it from the host's metadata by its name.
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly)]
internal sealed class PlatformHostAttribute(Type host) : Attribute
{
    /// <summary>
    /// Gets the class with the <c>Register</c> method.
    /// </summary>
    public Type Host { get; } = host;
}
