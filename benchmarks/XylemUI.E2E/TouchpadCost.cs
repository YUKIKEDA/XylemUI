using System.Diagnostics;
using XylemUI.Platform.Windows;

namespace XylemUI.E2E;

// The cost of polling a touchpad's DirectManipulation viewport once a frame (row I1c, design §7, "Touch and pen"): a hidden window
// with DirectManipulation's own viewport, on a thread in COM's single-threaded apartment, as an app's Main with [STAThread] has;
// the window's gestures updated as a frame updates them, with no contact (DirectManipulation's Update and an empty queue). The
// queue's delivery of a pan update is `input`'s us_per_touchpad_pan_update.
internal static class TouchpadCost
{
    private const int Updates = 10_000;

    public static int Run()
    {
        int code = 0;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                code = Measure();
            }
            catch (Exception exception)
            {
                error = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null)
        {
            throw new InvalidOperationException("The touchpad scene failed.", error);
        }

        return code;
    }

    private static int Measure()
    {
        UiThread.Initialize();
        using var window = new Win32Window("touchpad", 400, 300);
        if (window.TouchpadViewport is not DirectManipulationViewport)
        {
            throw new InvalidOperationException("The window has no DirectManipulation viewport.");
        }

        using var report = new Report("touchpad");
        var platform = (IPointerPlatform)window;
        var values = new double[21];
        for (int s = 0; s < values.Length; s++)
        {
            for (int i = 0; i < Updates; i++)
            {
                _ = platform.UpdateGestures(TimeSpan.Zero);
            }

            long start = Stopwatch.GetTimestamp();
            for (int i = 0; i < Updates; i++)
            {
                if (platform.UpdateGestures(TimeSpan.Zero))
                {
                    throw new InvalidOperationException("A gesture went on with no contact.");
                }
            }

            values[s] = Stopwatch.GetElapsedTime(start).TotalMicroseconds / Updates;
        }

        Array.Sort(values);
        report.Number("us_per_direct_manipulation_update_p50", values[values.Length / 2]);
        return 0;
    }
}
