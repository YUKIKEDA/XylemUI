using System.Collections.Concurrent;
using XylemUI.TestSupport;

namespace XylemUI.Tests;

// The queue of the image elements' decodes (design §5.0.4, "Decoding threads", IM1's Grill, Q3 and Q22): at most Limit at once on
// the thread pool, each result posted to the context it was asked from, and a request let go skipped.
[Collection(UiThreadGroup.Name)]
public sealed class ImageDecodeQueueTests
{
    /// <summary>
    /// Many decodes queued at once run at most <see cref="ImageDecodeQueue.Limit"/> at a time, each posts its result once to its
    /// context, and one let go before it ran is skipped.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A test decode that counts the decodes running and keeps the most, and takes 20 ms; a context that counts its posts.
    ///
    /// Steps:
    /// - Queue 4 × Limit + 1 decodes, the last let go at once; wait until none is in flight.
    ///
    /// Expected:
    /// - The most decodes running at once is from 1 to Limit, and so is the queue's own count; the context took 4 × Limit posts, each
    ///   request's once, and the request let go was not decoded.
    /// </remarks>
    [Fact]
    public void Decodes_RunAtMostTheLimitAtOnce()
    {
        int running = 0;
        int most = 0;
        var decoded = new ConcurrentBag<ImageSource>();
        ImageDecodeQueue.DecodeForTests = (source, _, _) =>
        {
            int now = Interlocked.Increment(ref running);
            int seen;
            while (now > (seen = Volatile.Read(ref most)) && Interlocked.CompareExchange(ref most, now, seen) != seen) { }

            decoded.Add(source);
            Thread.Sleep(20);
            _ = Interlocked.Decrement(ref running);
            return new ImageResult(new byte[4], 1, 1);
        };
        try
        {
            var context = new CountingContext();
            int count = (4 * ImageDecodeQueue.Limit) + 1;
            var completed = new ConcurrentBag<ImageRequest>();
            ImageRequest? last = null;
            for (int i = 0; i < count; i++)
            {
                last = new ImageRequest(ImageSource.FromBytes(new byte[] { (byte)i }), 0, 0, context, completed.Add);
                if (i == count - 1)
                {
                    Assert.True(last.TryCancel());
                }

                ImageDecodeQueue.Enqueue(last);
            }

            Assert.True(SpinWait.SpinUntil(() => ImageDecodeQueue.InFlight == 0, TimeSpan.FromSeconds(30)));

            Assert.InRange(most, 1, ImageDecodeQueue.Limit);
            Assert.InRange(ImageDecodeQueue.MostRunning, 1, ImageDecodeQueue.Limit);
            Assert.Equal(count - 1, context.Posts);
            Assert.Equal(count - 1, decoded.Count);
            Assert.DoesNotContain(decoded, source => source.Equals(last!.Source));
        }
        finally
        {
            ImageDecodeQueue.DecodeForTests = null;
        }
    }

    // Counts the posts, and runs each at once on the posting thread.
    private sealed class CountingContext : SynchronizationContext
    {
        private int _posts;

        public int Posts => Volatile.Read(ref _posts);

        public override void Post(SendOrPostCallback d, object? state)
        {
            _ = Interlocked.Increment(ref _posts);
            d(state);
        }
    }
}
