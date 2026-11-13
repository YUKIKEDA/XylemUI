namespace XylemUI;

/// <summary>
/// Marks a constructor, a method, a property, or one accessor of a property that only the UI thread may call: off the UI thread it
/// throws <see cref="InvalidOperationException"/> (design §2.12). The analyzer XYLEM100 reports a call of such a member inside a
/// lambda that runs on another thread (design §2.2, "Analyzers").
/// </summary>
/// <remarks>
/// The attribute is read from the member that is called, not from a member it overrides or implements: a call through an interface
/// is reported when the interface's member carries it.
/// </remarks>
[AttributeUsage(AttributeTargets.Constructor | AttributeTargets.Method | AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
public sealed class UiThreadOnlyAttribute : Attribute;
