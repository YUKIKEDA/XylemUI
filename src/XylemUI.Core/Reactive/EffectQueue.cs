using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace XylemUI;

/// <summary>
/// The effects of one stabilization cycle, in buckets by depth (design §2.2.2). Effects run from the shallowest bucket to the
/// deepest; within a bucket, in the order they were queued.
/// </summary>
/// <remarks>
/// Nothing is added to a queue while it runs: effects that become dirty during a cycle go to the next cycle's queue. Buckets
/// keep their arrays, so a steady state allocates nothing.
/// </remarks>
internal sealed class EffectQueue
{
    private List<Effect>?[] _buckets = [];
    private int _minDepth = int.MaxValue;
    private int _maxDepth = -1;

    /// <summary>
    /// Gets the number of queued effects.
    /// </summary>
    internal int Count { get; private set; }

    /// <summary>
    /// Queues an effect at its depth.
    /// </summary>
    internal void Enqueue(Effect effect, uint depth)
    {
        int index = (int)depth;
        if (index >= _buckets.Length)
        {
            Grow(index);
        }

        (_buckets[index] ??= []).Add(effect);
        Count++;
        if (index < _minDepth)
        {
            _minDepth = index;
        }

        if (index > _maxDepth)
        {
            _maxDepth = index;
        }
    }

    /// <summary>
    /// Runs every queued effect, shallowest first, and empties the queue.
    /// </summary>
    internal void RunAll()
    {
        for (int depth = _minDepth; depth <= _maxDepth; depth++)
        {
            List<Effect>? bucket = _buckets[depth];
            if (bucket is null || bucket.Count == 0)
            {
                continue;
            }

            foreach (Effect effect in CollectionsMarshal.AsSpan(bucket))
            {
                effect.RunQueued();
            }

            bucket.Clear();
        }

        Count = 0;
        _minDepth = int.MaxValue;
        _maxDepth = -1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Grow(int index) => Array.Resize(ref _buckets, Math.Max(index + 1, _buckets.Length * 2));
}
