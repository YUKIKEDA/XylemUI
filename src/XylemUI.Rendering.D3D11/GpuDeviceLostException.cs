namespace XylemUI.Rendering.D3D11;

/// <summary>
/// Thrown when the GPU device was removed or reset (a driver update, a GPU switch, a hang, an internal driver error). The
/// renderer cannot draw again; recovering by creating a new device is roadmap row R1.
/// </summary>
public sealed class GpuDeviceLostException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GpuDeviceLostException"/> class.
    /// </summary>
    public GpuDeviceLostException()
        : base("The GPU device was lost.") { }

    /// <summary>
    /// Initializes a new instance of the <see cref="GpuDeviceLostException"/> class.
    /// </summary>
    /// <param name="message">The message.</param>
    public GpuDeviceLostException(string message)
        : base(message) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="GpuDeviceLostException"/> class.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public GpuDeviceLostException(string message, Exception innerException)
        : base(message, innerException) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="GpuDeviceLostException"/> class for a call that found the device gone.
    /// </summary>
    /// <param name="operation">The call that failed.</param>
    /// <param name="result">The <c>HRESULT</c> it returned, which becomes <see cref="Exception.HResult"/>.</param>
    /// <param name="removedReason">The reason the device gave for the removal, or 0 when it was not asked.</param>
    internal GpuDeviceLostException(string operation, int result, int removedReason)
        : base($"The GPU device was lost during {operation} (0x{result:X8}, reason 0x{removedReason:X8}).")
    {
        HResult = result;
        RemovedReason = removedReason;
    }

    /// <summary>
    /// Gets the reason the device gave for the removal (<c>GetDeviceRemovedReason</c>), or 0 when it was not asked.
    /// </summary>
    public int RemovedReason { get; }
}
