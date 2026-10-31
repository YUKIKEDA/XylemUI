using TerraFX.Interop.Windows;
using TerraFX.Interop.WinRT;
using static TerraFX.Interop.Windows.Windows;
using static TerraFX.Interop.WinRT.WinRT;

namespace XylemUI.Platform.Windows;

/// <summary>
/// The touch keyboard of a window (design §4.7, "The touch keyboard"): shown when a press of a finger or a pen gives a text field
/// the focus, hidden when the focus leaves the fields.
/// </summary>
internal interface ITouchKeyboard
{
    /// <summary>
    /// Shows the keyboard.
    /// </summary>
    void Show();

    /// <summary>
    /// Hides the keyboard.
    /// </summary>
    void Hide();
}

/// <summary>
/// The OS's touch keyboard of one window: the window's <c>InputPane</c>, taken through <c>IInputPaneInterop::GetForWindow</c>
/// (Windows 10 1607 and later), whose <c>TryShow</c> and <c>TryHide</c> show and hide it. Chromium shows it itself in the same way,
/// after TSF's own policy did not show and hide it in step (commit 125cf9f247).
/// </summary>
internal sealed unsafe class InputPaneKeyboard : ITouchKeyboard, IDisposable
{
    private const string ClassName = "Windows.UI.ViewManagement.InputPane";

    private IInputPane2* _pane;

    private InputPaneKeyboard(IInputPane2* pane) => _pane = pane;

    /// <summary>
    /// Takes a window's <c>InputPane</c>; <see langword="null"/> when the OS has none (before Windows 10 1607, or without the WinRT
    /// class), and the keyboard is then not shown by the app.
    /// </summary>
    /// <param name="window">The window's handle.</param>
    /// <returns>The keyboard, or <see langword="null"/>.</returns>
    public static InputPaneKeyboard? TryCreate(nint window)
    {
        IInputPaneInterop* interop = null;
        fixed (char* name = ClassName)
        {
            HSTRING_HEADER header;
            HSTRING id;
            if (WindowsCreateStringReference(name, (uint)ClassName.Length, &header, &id) < 0)
            {
                return null;
            }

            Guid iid = __uuidof<IInputPaneInterop>();
            if (RoGetActivationFactory(id, &iid, (void**)&interop) < 0 || interop is null)
            {
                return null;
            }
        }

        IInputPane2* pane = null;
        Guid paneIid = __uuidof<IInputPane2>();
        int result = interop->GetForWindow((HWND)window, &paneIid, (void**)&pane);
        _ = interop->Release();
        return result < 0 || pane is null ? null : new InputPaneKeyboard(pane);
    }

    /// <inheritdoc/>
    public void Show()
    {
        if (_pane is not null)
        {
            byte shown;
            _ = _pane->TryShow(&shown);
        }
    }

    /// <inheritdoc/>
    public void Hide()
    {
        if (_pane is not null)
        {
            byte hidden;
            _ = _pane->TryHide(&hidden);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_pane is not null)
        {
            _ = _pane->Release();
            _pane = null;
        }
    }
}
