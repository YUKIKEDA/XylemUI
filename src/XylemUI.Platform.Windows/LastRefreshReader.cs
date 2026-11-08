namespace XylemUI.Platform.Windows;

/// <summary>
/// Reads the last refresh that showed a frame of a swap chain (for D3D11, <c>IDXGISwapChain::GetFrameStatistics</c>), so that the
/// frame clock can start frames late (design §2.11, "Frames that start late").
/// </summary>
/// <param name="qpcTime">The performance counter at that refresh (<c>SyncQPCTime</c>).</param>
/// <param name="refreshCount">The refresh's number (<c>SyncRefreshCount</c>).</param>
/// <returns><see langword="false"/> when there are no statistics yet, or they could not be read.</returns>
public delegate bool LastRefreshReader(out long qpcTime, out uint refreshCount);
