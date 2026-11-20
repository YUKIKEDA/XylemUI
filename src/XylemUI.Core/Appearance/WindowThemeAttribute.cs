namespace XylemUI;

/// <summary>
/// Chooses the theme that wraps the root of each of an app's windows (design §13.2, "根で置くもの"): a class with a method
/// <c>public static Element Wrap(Func&lt;Element&gt; root)</c>, such as <c>FluentRoot</c>. In an assembly whose output is an
/// executable, the source generator of <c>XylemUI.Generators</c> passes that method to <see cref="WindowRoot.SetWrapper"/> in the
/// app's module initializer. An app has one; with two, the generator reports the error XYLEM010 and sets neither, and a class
/// without the method is the error XYLEM011. In an assembly that is not an executable the attribute does nothing.
/// </summary>
/// <param name="type">The class with the <c>Wrap</c> method.</param>
/// <remarks>
/// More than one is allowed so that a second is the generator's error XYLEM010, which names the classes, and not the compiler's
/// CS0579.
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class WindowThemeAttribute(Type type) : Attribute
{
    /// <summary>
    /// Gets the class with the <c>Wrap</c> method.
    /// </summary>
    public Type Type { get; } = type;
}
