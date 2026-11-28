[assembly: XylemUI.PlatformHost(typeof(XylemUI.Platform.Windows.Win32Host))]

namespace XylemUI.Platform.Windows;

/// <summary>
/// Registers the Windows host (design §6.0, "ホストの選び方"). An app built for a Windows runtime identifier references this assembly,
/// and its generated module initializer calls <see cref="Register"/>, so that <c>XylemApp.Run</c> makes the host from it.
/// </summary>
public static class Win32Host
{
    /// <summary>
    /// Registers the Windows host as the one <c>XylemApp.Run</c> makes. Calling it again does nothing more.
    /// </summary>
    public static void Register() => PlatformHostRegistry.Factory = static () => new Win32PlatformApp();
}
