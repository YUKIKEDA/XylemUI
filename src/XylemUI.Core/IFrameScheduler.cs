namespace XylemUI;

/// <summary>
/// Calls back on the UI thread at the platform's next frame (design §2.11).
/// </summary>
/// <remarks>
/// <para>
/// One callback is pending at a time: a second <see cref="RequestFrame"/> before the frame replaces the first. The callback runs
/// once; call <see cref="RequestFrame"/> again from inside it to get the next frame. Pass a delegate that is created once, so that a
/// request does not allocate.
/// </para>
/// <para>
/// When the clock is a swap chain, a callback that presents calls <see cref="FramePresented"/>. After a callback that did not,
/// the next frame waits on a timer instead of the swap chain, which stays unsignaled until a present (design §2.11, "Frames
/// that do not present").
/// </para>
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

    /// <summary>
    /// Tells the scheduler that the callback running has presented a frame. Called from inside the callback, after the present.
    /// </summary>
    /// <remarks>
    /// The default does nothing: a scheduler whose clock does not count presents (a timer, a test's) needs nothing of it.
    /// </remarks>
    void FramePresented() { }
}
