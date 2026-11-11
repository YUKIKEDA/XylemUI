using System.Diagnostics;

namespace XylemUI.E2E;

// The upload of an image with its mipmaps (design §5.0.4, "Passing them to the renderer"; row IM1, 1b-1): for square images of 64
// to 4,096 pixels a side, the CPU time of the Draw that uploads one (UpdateSubresource and GenerateMips, before the frame's clear),
// and the time from the Draw's start until the GPU has done the frame. The second is read on the CPU: a pixel read back right after
// the Draw waits for the GPU's work before it (ReadPixel maps a staging copy). The frames that upload nothing are measured the
// same way, so the upload's GPU work is the difference. The GPU's timestamp queries (GpuFrameTimer) were tried first and were not
// used: their medians were not in the order of the sizes (1,024 px 0.24 ms, 256 px 0.25 ms; 4,096 px 4.7 ms, 2,048 px 6.9 ms).
// The image is released after its frame, and the next frame lets its texture go; the frames are not presented.
internal static class ImageUpload
{
    private static readonly int[] Sides = [64, 256, 1_024, 2_048, 4_096];

    public static int Run(int runs)
    {
        UiThread.Initialize();
        using var scene = new Scene("XylemUI E2E image upload");
        var images = new TextureCache();
        using var report = new Report("image-upload");
        report.Text("gpu", scene.Renderer.IsWarp ? "warp" : "hardware");
        var empty = new List<double>();
        foreach (int side in Sides)
        {
            byte[] pixels = new byte[side * side * 4];
            for (int i = 0; i < pixels.Length; i += 4)
            {
                pixels[i] = (byte)(i >> 2);
                pixels[i + 1] = (byte)(i >> 10);
                pixels[i + 2] = (byte)(i >> 16);
                pixels[i + 3] = 255;
            }

            var draw = new List<double>();
            var done = new List<double>();
            var rewriteDraw = new List<double>();
            var rewritten = new List<double>();

            // Two runs warm the path for the size; they are left out.
            for (int run = -2; run < runs; run++)
            {
                // A frame that lets the last image's texture go, then one that uploads nothing, measured.
                _ = Frame(scene, images, out _);
                double emptyDone = Frame(scene, images, out _);
                ImageHandle image = images.Add(side, side, pixels);
                double uploadDone = Frame(scene, images, out double drawMs);

                // The same image with new pixels, written into its texture: an animation's next frame (row IM1's 3c).
                images.Rewrite(image, pixels);
                double rewriteDone = Frame(scene, images, out double rewriteMs);
                images.Release(image);
                if (run >= 0)
                {
                    empty.Add(emptyDone);
                    draw.Add(drawMs);
                    done.Add(uploadDone);
                    rewriteDraw.Add(rewriteMs);
                    rewritten.Add(rewriteDone);
                }
            }

            report.Series($"upload_{side}_draw_ms", draw);
            report.Series($"upload_{side}_done_ms", done);
            report.Series($"rewrite_{side}_draw_ms", rewriteDraw);
            report.Series($"rewrite_{side}_done_ms", rewritten);
        }

        report.Series("empty_done_ms", empty);
        return 0;
    }

    // A frame with the marker alone: the draw (which uploads the queued images and lets the released ones' textures go), and a
    // pixel read back, which waits for the GPU. Returns the time from the draw's start until the pixel was read.
    private static double Frame(Scene scene, TextureCache images, out double drawMs)
    {
        scene.List.Reset(scene.Window.ScaleFactor);
        scene.List.FillRect(0, 0, 40, 40, Scene.Marker);
        long start = Stopwatch.GetTimestamp();
        scene.Renderer.Draw(scene.Lists, scene.Clear, scene.Atlas, images);
        drawMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        _ = scene.Renderer.ReadPixel(0, 0);
        return Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }
}
