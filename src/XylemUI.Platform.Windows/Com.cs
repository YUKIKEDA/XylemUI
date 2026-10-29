using System.Diagnostics.CodeAnalysis;
using TerraFX.Interop.Windows;

namespace XylemUI.Platform.Windows;

/// <summary>
/// The COM helpers of the DirectWrite classes: a failed <c>HRESULT</c> becomes <see cref="InvalidOperationException"/> with the
/// result in <see cref="Exception.HResult"/>, and a reference is released once (.cursor/rules/interop.mdc).
/// </summary>
internal static unsafe class Com
{
    /// <summary>
    /// Throws when <paramref name="result"/> is a failure.
    /// </summary>
    /// <param name="result">The <c>HRESULT</c>.</param>
    /// <param name="operation">The call that returned it, for the message.</param>
    /// <exception cref="InvalidOperationException">The result is a failure.</exception>
    public static void ThrowIfFailed(int result, string operation)
    {
        if (result < 0)
        {
            Throw(result, operation);
        }
    }

    /// <summary>
    /// Releases the reference and clears it; does nothing for a cleared one.
    /// </summary>
    /// <typeparam name="T">The interface.</typeparam>
    /// <param name="pointer">The reference.</param>
    public static void Release<T>(ref T* pointer)
        where T : unmanaged, IUnknown.Interface
    {
        if (pointer is not null)
        {
            _ = pointer->Release();
            pointer = null;
        }
    }

    /// <summary>
    /// Initializes COM and OLE on the calling thread, in a single-threaded apartment (design §5.0.3, "COM initialization"; row
    /// I1c): DirectManipulation needs COM, and the clipboard, drag and drop, and UI Automation will need the apartment. Each call
    /// that returns is balanced by <see cref="UninitializeUiThread"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The thread is in the multithreaded apartment, as .NET starts a thread unless its <c>Main</c> has <c>[STAThread]</c>, or OLE
    /// failed to initialize.
    /// </exception>
    public static void InitializeUiThread()
    {
        int result = TerraFX.Interop.Windows.Windows.OleInitialize(null);
        if (result == RPC.RPC_E_CHANGED_MODE)
        {
            throw new InvalidOperationException(
                "The UI thread is in COM's multithreaded apartment; put [STAThread] on the app's Main, as WPF and WinForms ask."
            )
            {
                HResult = result,
            };
        }

        ThrowIfFailed(result, "OleInitialize");
    }

    /// <summary>
    /// Ends what <see cref="InitializeUiThread"/> began on the calling thread.
    /// </summary>
    public static void UninitializeUiThread() => TerraFX.Interop.Windows.Windows.OleUninitialize();

    [DoesNotReturn]
    private static void Throw(int result, string operation) =>
        throw new InvalidOperationException($"{operation} failed with 0x{result:X8}.") { HResult = result };
}
