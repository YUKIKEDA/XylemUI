using System.Runtime.InteropServices;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace XylemUI.Platform.Windows;

/// <summary>
/// Makes the host's message-only windows (<c>HWND_MESSAGE</c>): the loop's wake window and the clipboard's owner. Such a window is
/// never shown, takes no broadcast, and leaves every message to the default procedure.
/// </summary>
internal static unsafe class MessageWindow
{
    /// <summary>
    /// Makes a message-only window of the class <paramref name="className"/>, registering the class first. A class registered
    /// already is used as it is: an earlier call that registered it and then failed to make its window leaves it registered.
    /// </summary>
    /// <param name="className">The name of the window's class; only this method registers it.</param>
    /// <param name="error">The system's error code when no window was made; 0 otherwise.</param>
    /// <returns>The window; <see cref="HWND.NULL"/> when the class could not be registered or the window could not be made.</returns>
    internal static HWND Create(string className, out int error)
    {
        HINSTANCE instance = GetModuleHandleW(null);
        fixed (char* name = className)
        {
            var windowClass = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                lpfnWndProc = &Procedure,
                hInstance = instance,
                lpszClassName = name,
            };

            if (RegisterClassExW(&windowClass) == 0)
            {
                error = Marshal.GetLastSystemError();
                if (error != TerraFX.Interop.Windows.ERROR.ERROR_CLASS_ALREADY_EXISTS)
                {
                    return HWND.NULL;
                }
            }

            HWND window = CreateWindowExW(0, name, null, 0, 0, 0, 0, 0, HWND.HWND_MESSAGE, HMENU.NULL, instance, null);
            error = window == HWND.NULL ? Marshal.GetLastSystemError() : 0;
            return window;
        }
    }

    [UnmanagedCallersOnly]
    private static LRESULT Procedure(HWND hwnd, uint message, WPARAM wParam, LPARAM lParam) => DefWindowProcW(hwnd, message, wParam, lParam);
}
