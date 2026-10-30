using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using TerraFX.Interop.DirectX;

namespace XylemUI.Rendering.D3D11;

/// <summary>
/// Turns a failed <c>HRESULT</c> into an exception, out of line so that the callers stay small (.cursor/rules/perf.mdc).
/// </summary>
internal static class HResults
{
    /// <summary>
    /// Returns a value indicating whether <paramref name="result"/> means the device is gone, so the renderer cannot go on.
    /// </summary>
    /// <param name="result">The <c>HRESULT</c>.</param>
    /// <returns><see langword="true"/> when the device was removed, reset, or hung, or the driver had an internal error.</returns>
    public static bool IsDeviceLost(int result) =>
        result
            is DXGI.DXGI_ERROR_DEVICE_REMOVED
                or DXGI.DXGI_ERROR_DEVICE_RESET
                or DXGI.DXGI_ERROR_DEVICE_HUNG
                or DXGI.DXGI_ERROR_DRIVER_INTERNAL_ERROR;

    /// <summary>
    /// Throws for a failed result: <see cref="GpuDeviceLostException"/> when the device is gone, otherwise
    /// <see cref="InvalidOperationException"/> with the result in <see cref="Exception.HResult"/>.
    /// </summary>
    /// <param name="result">The <c>HRESULT</c>; a negative value is a failure.</param>
    /// <param name="operation">The call that returned <paramref name="result"/>, for the exception's message.</param>
    /// <param name="removedReason">The device's removed reason, for <see cref="GpuDeviceLostException.RemovedReason"/>; 0 when it was not asked.</param>
    /// <exception cref="GpuDeviceLostException"><paramref name="result"/> means the device is gone.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="result"/> is any other failure.</exception>
    public static void ThrowIfFailed(int result, string operation, int removedReason = 0)
    {
        if (result < 0)
        {
            Throw(result, operation, removedReason);
        }
    }

    [DoesNotReturn]
    [StackTraceHidden]
    private static void Throw(int result, string operation, int removedReason)
    {
        if (IsDeviceLost(result))
        {
            throw new GpuDeviceLostException(operation, result, removedReason);
        }

        throw new InvalidOperationException($"{operation} failed (0x{result:X8}).") { HResult = result };
    }
}
