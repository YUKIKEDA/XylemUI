using TerraFX.Interop.DirectX;

namespace XylemUI.Rendering.D3D11.Tests;

// How a failed HRESULT becomes an exception (S7). Losing the device cannot be caused on demand, so the mapping is tested on the
// codes themselves; recovering from it is roadmap row R1.
public sealed class HResultsTests
{
    /// <summary>
    /// The three codes of a lost device become <see cref="GpuDeviceLostException"/> with the code and the reason.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - None.
    ///
    /// Steps:
    /// - ThrowIfFailed with DEVICE_REMOVED, DEVICE_RESET, and DEVICE_HUNG, and a removed reason of 0x887A0006.
    ///
    /// Expected:
    /// - <see cref="GpuDeviceLostException"/> each time, with the code in HResult and the reason in RemovedReason.
    /// </remarks>
    [Theory]
    [InlineData(DXGI.DXGI_ERROR_DEVICE_REMOVED)]
    [InlineData(DXGI.DXGI_ERROR_DEVICE_RESET)]
    [InlineData(DXGI.DXGI_ERROR_DEVICE_HUNG)]
    public void DeviceLostCodes_ThrowGpuDeviceLostException(int code)
    {
        GpuDeviceLostException lost = Assert.Throws<GpuDeviceLostException>(() =>
            HResults.ThrowIfFailed(code, "Present", DXGI.DXGI_ERROR_DEVICE_HUNG)
        );

        Assert.Equal(code, lost.HResult);
        Assert.Equal(DXGI.DXGI_ERROR_DEVICE_HUNG, lost.RemovedReason);
    }

    /// <summary>
    /// Another failure becomes <see cref="InvalidOperationException"/> with the code; success and positive status codes do not
    /// throw.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - None.
    ///
    /// Steps:
    /// - ThrowIfFailed with DXGI_ERROR_INVALID_CALL, with S_OK, and with DXGI_STATUS_OCCLUDED.
    ///
    /// Expected:
    /// - <see cref="InvalidOperationException"/> with the code for the first; nothing for the others.
    /// </remarks>
    [Fact]
    public void OtherResults_ThrowOnlyWhenFailed()
    {
        InvalidOperationException failed = Assert.Throws<InvalidOperationException>(() =>
            HResults.ThrowIfFailed(DXGI.DXGI_ERROR_INVALID_CALL, "ResizeBuffers")
        );
        Assert.Equal(DXGI.DXGI_ERROR_INVALID_CALL, failed.HResult);

        HResults.ThrowIfFailed(0, "Draw");
        HResults.ThrowIfFailed(DXGI.DXGI_STATUS_OCCLUDED, "Present");
    }
}
