using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace MacProbe;

// Probe 1 (runtime side), probe 7 (static linking) and the AccessKit half of probe 7.
internal static unsafe class HostProbes
{
    private static readonly (string Name, char Kind)[] Sysctls =
    [
        ("kern.osproductversion", 's'),
        ("kern.osversion", 's'),
        ("hw.machine", 's'),
        ("hw.model", 's'),
        ("machdep.cpu.brand_string", 's'),
        ("hw.optional.arm64", 'i'),
        ("sysctl.proc_translated", 'i'),
        ("hw.ncpu", 'i'),
        ("hw.memsize", 'l'),
    ];

    public static void Runtime()
    {
        Report.Step(
            "runtime",
            () =>
            {
                Report.Value("os_description", RuntimeInformation.OSDescription);
                Report.Value("os_arch", RuntimeInformation.OSArchitecture.ToString());
                Report.Value("process_arch", RuntimeInformation.ProcessArchitecture.ToString());
                Report.Value("framework", RuntimeInformation.FrameworkDescription);
                Report.Value(
                    "native_aot",
                    $"jit_compiled_methods={System.Runtime.JitInfo.GetCompiledMethodCount()} dynamic_code_supported={RuntimeFeature.IsDynamicCodeSupported}"
                );
                Report.Value("main_thread", Libc.pthread_main_np() == 1 ? "yes (Main runs on the process's main thread)" : "no");
                string path = Environment.ProcessPath ?? "?";
                Report.Value("executable", $"{path} {(File.Exists(path) ? new FileInfo(path).Length : -1)} bytes");
            }
        );
        Report.Step(
            "sysctl",
            () =>
            {
                foreach ((string name, char kind) in Sysctls)
                {
                    Report.Value($"sysctl.{name}", Sysctl(name, kind));
                }
            }
        );
    }

    public static void StaticLink()
    {
        Report.Step(
            "call",
            () =>
            {
                int sum = ProbeNative.probe_add(2, 3);
                Report.Value("call", sum == 5 ? "ok probe_add(2, 3) = 5 through DirectPInvoke" : $"FAILED probe_add(2, 3) = {sum}");
            }
        );
        Report.Step(
            "built_with",
            () =>
                Report.Value(
                    "built_with",
                    $"{ObjC.Utf8(ProbeNative.probe_compiler())}; arch {ObjC.Utf8(ProbeNative.probe_arch())}; min macOS {ProbeNative.probe_min_macos()}"
                )
        );
    }

    public static void AccessKit()
    {
#if PROBE_ACCESSKIT
        Report.Step(
            "node",
            () =>
            {
                nint node = AccessKitApi.accesskit_node_new(0);
                if (node == 0)
                {
                    throw new ProbeException("accesskit_node_new returned NULL");
                }
                fixed (byte* label = ObjC.Utf8z("probe"))
                {
                    AccessKitApi.accesskit_node_set_label(node, label);
                }
                byte* back = AccessKitApi.accesskit_node_label(node);
                string text = ObjC.Utf8(back);
                if (back != null)
                {
                    AccessKitApi.accesskit_string_free(back);
                }
                byte role = AccessKitApi.accesskit_node_role(node);
                AccessKitApi.accesskit_node_free(node);
                Report.Value("node", text == "probe" && role == 0 ? $"ok role={role} label={text}" : $"FAILED role={role} label={text}");
            }
        );

        // Never true: keeps accesskit_macos_adapter_new reachable so that the linker keeps the macOS adapter.
        if (Environment.GetEnvironmentVariable("PROBE_NEVER_SET_THIS") == "call-the-adapter-with-no-view")
        {
            AccessKitApi.accesskit_macos_adapter_new(0, 0, 0, 0);
        }
#else
        Report.Value("node", "SKIPPED this executable has no AccessKit (publish with -p:ProbeAccessKit=true)");
#endif
    }

    private static string Sysctl(string name, char kind)
    {
        fixed (byte* key = ObjC.Utf8z(name))
        {
            nuint size = 0;
            if (Libc.sysctlbyname(key, null, &size, null, 0) != 0)
            {
                return $"absent (errno {Marshal.GetLastPInvokeError()})";
            }
            var buffer = new byte[Math.Max((int)size, 8)];
            fixed (byte* b = buffer)
            {
                if (Libc.sysctlbyname(key, b, &size, null, 0) != 0)
                {
                    return $"FAILED errno {Marshal.GetLastPInvokeError()}";
                }
            }
            return kind switch
            {
                'i' => BitConverter.ToInt32(buffer, 0).ToString(),
                'l' => BitConverter.ToInt64(buffer, 0).ToString(),
                _ => Encoding.UTF8.GetString(buffer, 0, (int)size).TrimEnd('\0'),
            };
        }
    }
}
