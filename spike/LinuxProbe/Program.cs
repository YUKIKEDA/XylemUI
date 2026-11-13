using System.Runtime.InteropServices;
using LinuxProbe;
using static LinuxProbe.Report;

// One section per process (ci/run-probe.sh), so that a crash in native code loses one section, not the run:
//   LinuxProbe env <label>                    the session's environment variables as this process sees them
//   LinuxProbe aot <label>                    runtime, glibc, idle memory of a NativeAOT process
//   LinuxProbe wgpu <label> [all|vulkan|gl]   wgpu-native instance, adapters, device, offscreen frames
//   LinuxProbe wayland <label>                wl_display_connect(NULL)
//   LinuxProbe x11 <label>                    XOpenDisplay(NULL) and xcb_connect(NULL)
//   LinuxProbe fontconfig                     FcInitLoadConfigAndFonts and FcFontMatch
string section = args.Length > 0 ? args[0] : string.Empty;
string label = args.Length > 1 ? args[1] : "default";

switch (section)
{
    case "env":
        Sections.Environment(label);
        break;
    case "aot":
        Sections.Aot(label);
        break;
    case "wgpu":
        Guard("wgpu." + label, () => WgpuProbe.Run(label, args.Length > 2 ? args[2] : "all"));
        break;
    case "wayland":
        Guard("wayland." + label, () => DisplayProbe.Wayland(label));
        break;
    case "x11":
        Guard("x11." + label, () => DisplayProbe.X11(label));
        break;
    case "fontconfig":
        Guard("fontconfig", FontconfigProbe.Run);
        break;
    default:
        Console.Error.WriteLine("usage: LinuxProbe env|aot|wgpu|wayland|x11|fontconfig <label> [all|vulkan|gl]");
        return 2;
}

return 0;

namespace LinuxProbe
{
    internal static unsafe class Sections
    {
        public static void Environment(string label)
        {
            string p = "env." + label;
            foreach (string name in new[] { "XDG_SESSION_TYPE", "XDG_RUNTIME_DIR", "WAYLAND_DISPLAY", "DISPLAY", "DBUS_SESSION_BUS_ADDRESS" })
            {
                Probe($"{p}.{name}", Env(name));
            }
        }

        public static void Aot(string label)
        {
            string p = "aot." + label;
            Probe(p + ".framework", RuntimeInformation.FrameworkDescription);
            Probe(p + ".rid", RuntimeInformation.RuntimeIdentifier);
            Probe(p + ".os", RuntimeInformation.OSDescription);
            Probe(p + ".process_architecture", RuntimeInformation.ProcessArchitecture.ToString());
            // Under NativeAOT nothing is compiled at run time (.cursor/rules/aot.mdc).
            long jitted = System.Runtime.JitInfo.GetCompiledMethodCount();
            Probe(p + ".native_aot", jitted == 0 ? "yes (0 methods compiled by a JIT)" : $"no ({jitted} methods compiled by a JIT)");
            Probe(p + ".process_path", System.Environment.ProcessPath ?? "(null)");
            Guard(
                p + ".glibc",
                () =>
                {
                    var version = (delegate* unmanaged<byte*>)Dl.Symbol(Dl.Open("libc.so.6"), "gnu_get_libc_version");
                    Probe(p + ".glibc", Dl.Str(version()));
                }
            );
            Probe(p + ".memory_idle", ProcStatus.Memory());
        }
    }
}
