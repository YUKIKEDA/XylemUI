using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MacProbe;

// Probe 2 (plain P/Invoke into the Obj-C runtime) and probe 5 (a class registered from C#, its methods C# functions).
internal static unsafe class ObjCProbes
{
    private const string Sample = "XylemUI 日本語 ✓ 😀";

    private static nint s_pool;
    private static nint s_class;
    private static nint s_protocol;
    private static nint s_object;
    private static nint s_ivarOffset;
    private static int s_addRuns;
    private static int s_pingRuns;

    public static void Basics()
    {
        Report.Step(
            "classes",
            () =>
            {
                string[] names = ["NSObject", "NSAutoreleasePool", "NSProcessInfo", "NSString"];
                Report.Value("classes", string.Join(" ", names.Select(n => $"{n}={(ObjC.Class(n) != 0 ? "yes" : "no")}")));
            }
        );
        Report.Step(
            "autorelease_pool",
            () =>
            {
                s_pool = ObjC.New("NSAutoreleasePool");
                Report.Value("autorelease_pool", $"ok [[NSAutoreleasePool alloc] init] gave a {ObjC.ClassName(s_pool)}");
            }
        );
        Report.Step(
            "process_info",
            () =>
            {
                nint info = ObjC.Id(ObjC.RequireClass("NSProcessInfo"), "processInfo");
                string? version = ObjC.Text(ObjC.Id(info, "operatingSystemVersionString"));
                Report.Value(
                    "process_info",
                    $"operatingSystemVersionString=\"{version}\" processorCount={ObjC.U(info, "processorCount")} physicalMemory={ObjC.U64(info, "physicalMemory")}"
                );
            }
        );
        Report.Step(
            "nsstring_utf8",
            () =>
            {
                nint cls = ObjC.RequireClass("NSString");
                nint str;
                fixed (byte* p = ObjC.Utf8z(Sample))
                {
                    str = Msg.IdPtr(cls, ObjC.Check(cls, "stringWithUTF8String:"), p);
                }
                nuint length = ObjC.U(str, "length");
                string? back = ObjC.Text(str);
                Report.Value(
                    "nsstring_utf8",
                    back == Sample && length == (nuint)Sample.Length
                        ? $"ok +stringWithUTF8String: round trip, length={length} UTF-16 units"
                        : $"FAILED length={length} (expected {Sample.Length}), read back \"{back}\""
                );
            }
        );
        Report.Step(
            "nsstring_utf16",
            () =>
            {
                nint str = ObjC.NSString(Sample);
                string? back = ObjC.Text(str);
                ObjC.Void(str, "release");
                Report.Value("nsstring_utf16", back == Sample ? "ok -initWithCharacters:length: round trip" : $"FAILED read back \"{back}\"");
            }
        );
        Report.Step(
            "msgsend_cost",
            () =>
            {
                nint str = ObjC.NSString(Sample);
                nint sel = ObjC.Check(str, "length");
                nuint sink = 0;
                for (int i = 0; i < 10_000; i++)
                {
                    sink += Msg.UInt(str, sel);
                }
                const int Calls = 1_000_000;
                long allocated = GC.GetAllocatedBytesForCurrentThread();
                long start = Stopwatch.GetTimestamp();
                for (int i = 0; i < Calls; i++)
                {
                    sink += Msg.UInt(str, sel);
                }
                TimeSpan elapsed = Stopwatch.GetElapsedTime(start);
                long bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
                ObjC.Void(str, "release");
                Report.Value(
                    "msgsend_cost",
                    $"{elapsed.TotalNanoseconds / Calls:F1} ns per [NSString length] from C# ({Calls} calls, selector cached), {bytes} managed bytes allocated (sink {sink})"
                );
            }
        );
        Report.Step(
            "pool_drain",
            () =>
            {
                if (s_pool == 0)
                {
                    throw new ProbeException("no pool to drain (see autorelease_pool)");
                }
                ObjC.Void(s_pool, "drain");
                s_pool = 0;
                Report.Value("pool_drain", "ok");
            }
        );
    }

    // -(long)probeAdd:(long)value returns value + the instance's _xylemHandle ivar: the shape of an NSView subclass that finds its
    // managed owner through an id stored in an ivar (interop.mdc: native code holds a NativeHandleTable id, never an address).
    [UnmanagedCallersOnly]
    private static long ProbeAdd(nint self, nint cmd, long value)
    {
        // Reads through `self` at the offset the runtime gave for the ivar; nothing here can throw.
        Interlocked.Increment(ref s_addRuns);
        return value + *(long*)(self + s_ivarOffset);
    }

    [UnmanagedCallersOnly]
    private static void Ping(nint self, nint cmd) => Interlocked.Increment(ref s_pingRuns);

    public static void ClassRegistration()
    {
        Report.Step(
            "allocate",
            () =>
            {
                nint superclass = ObjC.RequireClass("NSObject");
                fixed (byte* name = ObjC.Utf8z("XylemProbeTarget"))
                fixed (byte* ivarName = ObjC.Utf8z("_xylemHandle"))
                fixed (byte* ivarType = ObjC.Utf8z("q"))
                fixed (byte* addTypes = ObjC.Utf8z("q@:q"))
                fixed (byte* pingTypes = ObjC.Utf8z("v@:"))
                {
                    s_class = ObjCRuntime.objc_allocateClassPair(superclass, name, 0);
                    if (s_class == 0)
                    {
                        throw new ProbeException("objc_allocateClassPair returned Nil (is the name taken?)");
                    }
                    byte ivarAdded = ObjCRuntime.class_addIvar(s_class, ivarName, 8, 3, ivarType);
                    delegate* unmanaged<nint, nint, long, long> add = &ProbeAdd;
                    delegate* unmanaged<nint, nint, void> ping = &Ping;
                    byte addAdded = ObjCRuntime.class_addMethod(s_class, ObjC.Sel("probeAdd:"), (nint)add, addTypes);
                    byte pingAdded = ObjCRuntime.class_addMethod(s_class, ObjC.Sel("probePing"), (nint)ping, pingTypes);
                    s_protocol = ObjC.Protocol("NSTextInputClient");
                    byte protocolAdded = s_protocol != 0 ? ObjCRuntime.class_addProtocol(s_class, s_protocol) : (byte)0;
                    ObjCRuntime.objc_registerClassPair(s_class);
                    s_ivarOffset = ObjCRuntime.ivar_getOffset(ObjCRuntime.class_getInstanceVariable(s_class, ivarName));
                    Report.Value(
                        "allocate",
                        $"registered XylemProbeTarget : NSObject; class_addIvar={ivarAdded} probeAdd:={addAdded} probePing={pingAdded} "
                            + $"NSTextInputClient protocol {(s_protocol != 0 ? "found" : "NOT found")}, class_addProtocol={protocolAdded}; ivar offset {s_ivarOffset}"
                    );
                }
            }
        );
        Report.Step(
            "call_through_msgsend",
            () =>
            {
                if (s_class == 0)
                {
                    throw new ProbeException("no class (see allocate)");
                }
                s_object = ObjC.Id(ObjC.Id(s_class, "alloc"), "init");
                *(long*)(s_object + s_ivarOffset) = 1000;
                long result = Msg.LongLong(s_object, ObjC.Check(s_object, "probeAdd:"), 41);
                Report.Value(
                    "call_through_msgsend",
                    result == 1041 && s_addRuns == 1
                        ? "ok objc_msgSend(obj, probeAdd:, 41) ran the [UnmanagedCallersOnly] method and read the ivar: 1041"
                        : $"FAILED result {result} (expected 1041), callback ran {s_addRuns} times"
                );
            }
        );
        Report.Step(
            "call_from_framework",
            () =>
            {
                nint ping = ObjC.Sel("probePing");
                Msg.IdId(s_object, ObjC.Check(s_object, "performSelector:"), ping);
                Msg.VoidIdIdBool(s_object, ObjC.Check(s_object, "performSelectorOnMainThread:withObject:waitUntilDone:"), ping, 0, 1);
                Report.Value(
                    "call_from_framework",
                    s_pingRuns == 2
                        ? "ok -performSelector: and -performSelectorOnMainThread:withObject:waitUntilDone:YES reached the C# method"
                        : $"FAILED probePing ran {s_pingRuns} times, expected 2"
                );
            }
        );
        Report.Step(
            "conforms",
            () =>
            {
                if (s_protocol == 0)
                {
                    throw new ProbeException("NSTextInputClient protocol not found (is AppKit loaded?)");
                }
                byte byRuntime = ObjCRuntime.class_conformsToProtocol(s_class, s_protocol);
                byte byObject = Msg.BoolId(s_object, ObjC.Check(s_object, "conformsToProtocol:"), s_protocol);
                Report.Value("conforms", $"class_conformsToProtocol={byRuntime} -conformsToProtocol:={byObject} (both 1 expected)");
            }
        );
        Report.Step(
            "callback_cost",
            () =>
            {
                nint sel = ObjC.Check(s_object, "probeAdd:");
                long sink = 0;
                for (int i = 0; i < 10_000; i++)
                {
                    sink += Msg.LongLong(s_object, sel, i);
                }
                const int Calls = 1_000_000;
                long allocated = GC.GetAllocatedBytesForCurrentThread();
                long start = Stopwatch.GetTimestamp();
                for (int i = 0; i < Calls; i++)
                {
                    sink += Msg.LongLong(s_object, sel, i);
                }
                TimeSpan elapsed = Stopwatch.GetElapsedTime(start);
                long bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
                Report.Value(
                    "callback_cost",
                    $"{elapsed.TotalNanoseconds / Calls:F1} ns per C# -> objc_msgSend -> C# round trip, {bytes} managed bytes allocated over {Calls} calls (sink {sink})"
                );
            }
        );
        Report.Step(
            "release",
            () =>
            {
                ObjC.Void(s_object, "release");
                Report.Value("release", "ok");
            }
        );
    }
}
