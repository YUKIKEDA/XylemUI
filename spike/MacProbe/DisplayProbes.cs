namespace MacProbe;

// Probe 8: displays, CVDisplayLink, and whether the CADisplayLink API exists (presence only; no window is made).
internal static unsafe class DisplayProbes
{
    private static nint s_link;
    private static int s_ticks;

    private static nint Link => s_link != 0 ? s_link : throw new ProbeException("no CVDisplayLink (see cvdisplaylink_create)");

    // Runs on the CVDisplayLink thread; it only counts, and cannot throw.
    [System.Runtime.InteropServices.UnmanagedCallersOnly]
    private static int OnDisplayLink(nint link, nint now, nint outputTime, ulong flagsIn, ulong* flagsOut, nint context)
    {
        Interlocked.Increment(ref s_ticks);
        return 0;
    }

    public static void Run()
    {
        Report.Step(
            "cg_displays",
            () =>
            {
                uint active = 0;
                uint online = 0;
                int activeRc = CG.CGGetActiveDisplayList(0, null, &active);
                int onlineRc = CG.CGGetOnlineDisplayList(0, null, &online);
                Report.Value("cg_displays", $"active={active} (rc {activeRc}) online={online} (rc {onlineRc}) CGMainDisplayID=0x{CG.CGMainDisplayID():X}");
            }
        );
        Report.Step(
            "cvdisplaylink_create",
            () =>
            {
                nint link = 0;
                int rc = CV.CVDisplayLinkCreateWithActiveCGDisplays(&link);
                s_link = rc == 0 ? link : 0;
                Report.Value("cvdisplaylink_create", rc == 0 && link != 0 ? "ok" : $"FAILED CVReturn {rc}");
            }
        );
        Report.Step(
            "cvdisplaylink_period",
            () =>
            {
                CVTime period = CV.CVDisplayLinkGetNominalOutputVideoRefreshPeriod(Link);
                string value =
                    (period.Flags & 1) != 0 ? "indefinite (kCVTimeIsIndefinite)"
                    : period.TimeScale == 0 ? "timeScale 0"
                    : $"{period.TimeValue * 1000.0 / period.TimeScale:F3} ms";
                Report.Value("cvdisplaylink_period", value);
            }
        );
        Report.Step(
            "cvdisplaylink_ticks",
            () =>
            {
                delegate* unmanaged<nint, nint, nint, ulong, ulong*, nint, int> callback = &OnDisplayLink;
                int setRc = CV.CVDisplayLinkSetOutputCallback(Link, (nint)callback, 0);
                int startRc = CV.CVDisplayLinkStart(Link);
                Thread.Sleep(1000);
                int stopRc = CV.CVDisplayLinkStop(Link);
                Report.Value(
                    "cvdisplaylink_ticks",
                    $"{Volatile.Read(ref s_ticks)} callbacks in 1 s (set rc {setRc}, start rc {startRc}, stop rc {stopRc})"
                );
            }
        );
        Report.Step(
            "cadisplaylink",
            () =>
            {
                nint selector = ObjC.Sel("displayLinkWithTarget:selector:");
                string methods = string.Join(
                    " ",
                    new[] { "NSView", "NSWindow", "NSScreen" }.Select(name =>
                    {
                        nint cls = ObjC.Class(name);
                        string state =
                            cls == 0 ? "no class"
                            : ObjCRuntime.class_getInstanceMethod(cls, selector) != 0 ? "present"
                            : "absent";
                        return $"-[{name} displayLinkWithTarget:selector:]={state}";
                    })
                );
                Report.Value("cadisplaylink", $"class CADisplayLink={(ObjC.Class("CADisplayLink") != 0 ? "present" : "absent")}; {methods}");
            }
        );
        Report.Step(
            "nsscreen",
            () =>
            {
                nint screenClass = ObjC.RequireClass("NSScreen");
                nint screens = ObjC.Id(screenClass, "screens");
                nint count = screens == 0 ? 0 : CF.CFArrayGetCount(screens);
                nint main = ObjC.Id(screenClass, "mainScreen");
                string detail =
                    main == 0
                        ? "no main screen"
                        : $"backingScaleFactor={ObjC.Double(main, "backingScaleFactor")} "
                            + $"maximumFramesPerSecond={Msg.Int(main, ObjC.Check(main, "maximumFramesPerSecond"))} "
                            + $"localizedName=\"{ObjC.Text(ObjC.Id(main, "localizedName"))}\"";
                Report.Value("nsscreen", $"{count} screen(s); main: {detail}");
            }
        );
    }
}
