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
