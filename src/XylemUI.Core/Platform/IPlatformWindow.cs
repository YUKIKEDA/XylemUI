using XylemUI.Text;

namespace XylemUI;

/// <summary>
/// The window tier of the platform contract (design §6.0): one OS window, with the renderer and the frame clock that the host pairs
/// with it, the OS's input method, and the accessibility adapter. The window core sees the renderer only as <see cref="Target"/>,
/// and the clock only through <see cref="FrameScheduler"/>. Sizes are in physical pixels (design §6.1.1).
/// </summary>
/// <remarks>
/// It belongs to the UI thread. The host makes it in <see cref="IPlatformApp.OpenWindow"/>; the window core reads its members once
/// in <see cref="WindowCore.Connect"/>, then calls <see cref="Attach"/>, and disposes it with itself. The OS's notices reach the core
/// through delegates the host binds once in <see cref="Attach"/>: one call for each notice, with no interface between them.
/// </remarks>
internal interface IPlatformWindow : IDisposable
{
    /// <summary>
    /// Gets the size of the client area, in physical pixels.
    /// </summary>
    (int Width, int Height) ClientSize { get; }

    /// <summary>
    /// Gets the window's scale, from logical to physical pixels.
    /// </summary>
    float ScaleFactor { get; }

    /// <summary>
    /// Gets a value indicating whether the window shows: neither hidden nor minimized (design §2.11, "Frames of hidden and
    /// minimized windows").
    /// </summary>
    bool IsShowing { get; }

    /// <summary>
    /// Gets a value indicating whether the window is the active window, which takes the keyboard.
    /// </summary>
    bool IsActive { get; }

    /// <summary>
    /// Gets how the text of the window's monitor is drawn (design §5.0.3, "ClearType").
    /// </summary>
    TextRenderingParameters TextRendering { get; }

    /// <summary>
    /// Gets what the window's frames draw on: the renderer the host paired with the window.
    /// </summary>
    IRenderTarget Target { get; }

    /// <summary>
    /// Gets the frame scheduler of the window. A Windows window returns the loop's one (design §2.11).
    /// </summary>
    IFrameScheduler FrameScheduler { get; }

    /// <summary>
    /// Gets the window's pointer platform: the cursor, the capture, the pointer's history, the touchpad, and the drags handed to the
    /// OS (design §7).
    /// </summary>
    IPointerPlatform Pointer { get; }

    /// <summary>
    /// Gets the OS's input method of the window; <see langword="null"/> when it could not be made, and the fields then take the
    /// committed characters (design §4.9).
    /// </summary>
    ITextInputPlatform? TextInput { get; }

    /// <summary>
    /// Starts to pass the OS's notices to the window core, once its frames and input are made: called once, after
    /// <see cref="WindowCore.Connect"/>.
    /// </summary>
    void Attach();

    /// <summary>
    /// Tells the window that the root function ran and the first frame was drawn: assistive technology that asked while the window
    /// showed gets the tree (design §4.4.1).
    /// </summary>
    void RootMounted();

    /// <summary>
    /// Asks the window to close, as its close button does.
    /// </summary>
    void RequestClose();
}
