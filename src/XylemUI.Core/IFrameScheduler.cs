namespace XylemUI;

/// <summary>
/// Calls back on the UI thread at the platform's next frame (design §2.11).
/// </summary>
/// <remarks>
/// One callback is pending at a time: a second <see cref="RequestFrame"/> before the frame replaces the first. The callback runs
/// once; call <see cref="RequestFrame"/> again from inside it to get the next frame. Pass a delegate that is created once, so that a
/// request does not allocate.
/// </remarks>
public interface IFrameScheduler
{
    /// <summary>
    /// Asks for <paramref name="callback"/> to run once, on the UI thread, at the next frame.
    /// </summary>
    /// <param name="callback">The work to run at the frame.</param>
    /// <exception cref="InvalidOperationException">The calling thread is not the UI thread.</exception>
    void RequestFrame(Action callback);

    /// <summary>
    /// Drops the pending request, if any. The callback does not run.
    /// </summary>
    /// <exception cref="InvalidOperationException">The calling thread is not the UI thread.</exception>
    void CancelFrame();
}
