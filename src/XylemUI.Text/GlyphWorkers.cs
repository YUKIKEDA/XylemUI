using System.Runtime.ExceptionServices;

namespace XylemUI.Text;

/// <summary>
/// Resident threads that run one piece of work each, beside the calling thread, and are waited for (design §5.0.3, "Glyphs seen
/// for the first time", roadmap row T5). The caller is worker 0; the others are background threads made when the workers are, so
/// they never keep the process alive, and they wait on a semaphore between pieces of work.
/// </summary>
/// <remarks>
/// <see cref="Run"/> is called from one thread at a time. An exception a worker throws is thrown again from <see cref="Run"/> after
/// every worker has finished: the one of the lowest worker index, and the others are dropped, since the atlas's rasterizers throw
/// for causes all threads share (a disposed font, a failed DirectWrite call).
/// </remarks>
internal sealed class GlyphWorkers : IDisposable
{
    private readonly Thread[] _threads;
    private readonly SemaphoreSlim[] _go;
    private readonly CountdownEvent _done;
    private readonly ExceptionDispatchInfo?[] _failures;
    private Action<int>? _work;
    private volatile bool _stopping;

    /// <summary>
    /// Initializes a new instance of the <see cref="GlyphWorkers"/> class and starts its threads.
    /// </summary>
    /// <param name="count">The number of workers, the calling thread included: at least 2.</param>
    /// <exception cref="OutOfMemoryException">There was not enough memory to start a thread; the threads already started are stopped.</exception>
    /// <exception cref="ThreadStartException">The OS could not start a thread; the threads already started are stopped.</exception>
    public GlyphWorkers(int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 2);
        Count = count;
        _threads = new Thread[count - 1];
        _go = new SemaphoreSlim[count - 1];
        _failures = new ExceptionDispatchInfo?[count];
        _done = new CountdownEvent(count - 1);
        int started = 0;
        try
        {
            for (int i = 0; i < count - 1; i++)
            {
                _go[i] = new SemaphoreSlim(0);
                int index = i + 1;
                _threads[i] = new Thread(() => Loop(index)) { IsBackground = true, Name = $"XylemUI glyph worker {index}" };
                _threads[i].Start();
                started++;
            }
        }
        catch
        {
            Stop(started);
            throw;
        }
    }

    /// <summary>
    /// Gets the number of workers, the calling thread included.
    /// </summary>
    public int Count { get; }

    /// <summary>
    /// Gets the worker threads, the calling thread not included.
    /// </summary>
    public IReadOnlyList<Thread> Threads => _threads;

    /// <summary>
    /// Runs <paramref name="work"/> once on each worker, with its index (0 on the calling thread), and waits for all of them.
    /// </summary>
    /// <param name="work">The work; it is given the worker's index.</param>
    public void Run(Action<int> work)
    {
        ObjectDisposedException.ThrowIf(_stopping, this);
        _work = work;
        _done.Reset(Count - 1);
        Array.Clear(_failures);
        foreach (SemaphoreSlim go in _go)
        {
            _ = go.Release();
        }

        try
        {
            work(0);
        }
        catch (Exception exception)
        {
            _failures[0] = ExceptionDispatchInfo.Capture(exception);
        }

        _done.Wait();
        _work = null;
        foreach (ExceptionDispatchInfo? failure in _failures)
        {
            failure?.Throw();
        }
    }

    /// <summary>
    /// Stops the threads and waits for them to end.
    /// </summary>
    public void Dispose()
    {
        if (!_stopping)
        {
            Stop(_threads.Length);
        }
    }

    private void Stop(int started)
    {
        _stopping = true;
        for (int i = 0; i < started; i++)
        {
            _ = _go[i].Release();
        }

        for (int i = 0; i < started; i++)
        {
            _threads[i].Join();
        }

        foreach (SemaphoreSlim? go in _go)
        {
            go?.Dispose();
        }

        _done.Dispose();
    }

    private void Loop(int index)
    {
        SemaphoreSlim go = _go[index - 1];
        while (true)
        {
            go.Wait();
            if (_stopping)
            {
                return;
            }

            try
            {
                _work!(index);
            }
            catch (Exception exception)
            {
                _failures[index] = ExceptionDispatchInfo.Capture(exception);
            }

            _ = _done.Signal();
        }
    }
}
