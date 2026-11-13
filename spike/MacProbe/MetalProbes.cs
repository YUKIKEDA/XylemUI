using System.Diagnostics;

namespace MacProbe;

// Probe 3: is there a Metal device on the runner, and can it render offscreen and be read back (pixel tests on CI)?
internal static unsafe class MetalProbes
{
    private const nuint PixelFormatBgra8Unorm = 80;
    private const nuint TextureType2D = 2;
    private const nuint UsageShaderReadRenderTarget = 1 | 4;
    private const nuint StorageModeManaged = 1;
    private const nuint LoadActionClear = 2;
    private const nuint StoreActionStore = 1;
    private const nuint PrimitiveTriangle = 3;
    private const nint StatusCompleted = 4;
    private const nuint Size = 4;

    // A full-screen triangle in green, drawn over a red clear.
    private const string Shader = """
        #include <metal_stdlib>
        using namespace metal;
        struct V { float4 position [[position]]; };
        vertex V vs(uint id [[vertex_id]]) {
            float2 uv = float2((id << 1) & 2, id & 2);
            V out;
            out.position = float4(uv * 2.0 - 1.0, 0.0, 1.0);
            return out;
        }
        fragment half4 fs(V in [[stage_in]]) { return half4(0.0, 1.0, 0.0, 1.0); }
        """;

    private static readonly (string Name, nint Value)[] Families =
    [
        ("apple7", 1007),
        ("apple8", 1008),
        ("apple9", 1009),
        ("mac2", 2002),
        ("metal3", 5001),
    ];

    private static nint s_device;
    private static nint s_queue;
    private static nint s_pipeline;
    private static nint s_layer;
    private static nint s_drawable;

    private static nint Device => s_device != 0 ? s_device : throw new ProbeException("no Metal device (see default_device)");

    private static nint Queue => s_queue != 0 ? s_queue : throw new ProbeException("no command queue (see command_queue)");

    public static void Run()
    {
        Report.Step(
            "default_device",
            () =>
            {
                s_device = MetalApi.MTLCreateSystemDefaultDevice();
                Report.Value("default_device", s_device != 0 ? $"present ({ObjC.ClassName(s_device)})" : "NULL");
            }
        );
        Report.Step(
            "all_devices",
            () =>
            {
                nint all = MetalApi.MTLCopyAllDevices();
                nint count = all == 0 ? 0 : CF.CFArrayGetCount(all);
                var names = new List<string>();
                for (nint i = 0; i < count; i++)
                {
                    names.Add(ObjC.Text(ObjC.Id(CF.CFArrayGetValueAtIndex(all, i), "name")) ?? "?");
                }
                if (s_device == 0 && count > 0)
                {
                    s_device = CF.CFArrayGetValueAtIndex(all, 0);
                    Report.Info("no default device; using the first of MTLCopyAllDevices");
                }
                Report.Value("all_devices", $"{count}: {string.Join(", ", names)}");
            }
        );
        Report.Step(
            "device",
            () =>
            {
                nint d = Device;
                string families = string.Join(
                    " ",
                    Families.Select(f => $"{f.Name}={Msg.BoolInt(d, ObjC.Check(d, "supportsFamily:"), f.Value)}")
                );
                Report.Value("device.name", ObjC.Text(ObjC.Id(d, "name")) ?? "?");
                Report.Value(
                    "device.properties",
                    $"lowPower={ObjC.Bool(d, "isLowPower")} headless={ObjC.Bool(d, "isHeadless")} removable={ObjC.Bool(d, "isRemovable")} "
                        + $"unifiedMemory={ObjC.Bool(d, "hasUnifiedMemory")} registryID=0x{ObjC.U64(d, "registryID"):X} "
                        + $"recommendedMaxWorkingSetSize={ObjC.U64(d, "recommendedMaxWorkingSetSize")}"
                );
                Report.Value("device.families", families);
            }
        );
        Report.Step(
            "command_queue",
            () =>
            {
                s_queue = ObjC.Id(Device, "newCommandQueue");
                Report.Value("command_queue", s_queue != 0 ? "ok" : "FAILED newCommandQueue returned nil");
            }
        );
        Report.Step(
            "clear_pass",
            () =>
            {
                nint texture = NewTexture();
                string result = RenderPass(texture, pipeline: 0, synchronize: true);
                Report.Value("clear_pass", $"{result}; {ReadCenter(texture, expectedB: 0, expectedG: 0, expectedR: 255)}");
            }
        );
        Report.Step(
            "shader_compile",
            () =>
            {
                long start = Stopwatch.GetTimestamp();
                nint source = ObjC.NSString(Shader);
                nint error = 0;
                nint d = Device;
                nint library = Msg.IdIdIdError(d, ObjC.Check(d, "newLibraryWithSource:options:error:"), source, 0, &error);
                if (library == 0)
                {
                    throw new ProbeException($"newLibraryWithSource failed: {ObjC.ErrorText(error)}");
                }
                double compileMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                nint vertex = ObjC.Id(library, "newFunctionWithName:", ObjC.NSString("vs"));
                nint fragment = ObjC.Id(library, "newFunctionWithName:", ObjC.NSString("fs"));
                nint descriptor = ObjC.New("MTLRenderPipelineDescriptor");
                ObjC.VoidId(descriptor, "setVertexFunction:", vertex);
                ObjC.VoidId(descriptor, "setFragmentFunction:", fragment);
                nint attachment = ObjC.IdU(ObjC.Id(descriptor, "colorAttachments"), "objectAtIndexedSubscript:", 0);
                ObjC.SetU(attachment, "setPixelFormat:", PixelFormatBgra8Unorm);
                error = 0;
                s_pipeline = Msg.IdIdError(d, ObjC.Check(d, "newRenderPipelineStateWithDescriptor:error:"), descriptor, &error);
                if (s_pipeline == 0)
                {
                    throw new ProbeException($"newRenderPipelineStateWithDescriptor failed: {ObjC.ErrorText(error)}");
                }
                Report.Value(
                    "shader_compile",
                    $"ok MSL compiled at run time in {compileMs:F1} ms; pipeline built in {Stopwatch.GetElapsedTime(start).TotalMilliseconds:F1} ms in all"
                );
            }
        );
        Report.Step(
            "draw_pass",
            () =>
            {
                if (s_pipeline == 0)
                {
                    throw new ProbeException("no pipeline (see shader_compile)");
                }
                nint texture = NewTexture();
                string result = RenderPass(texture, s_pipeline, synchronize: true);
                Report.Value("draw_pass", $"{result}; {ReadCenter(texture, expectedB: 0, expectedG: 255, expectedR: 0)}");
            }
        );
        Report.Step(
            "layer_create",
            () =>
            {
                s_layer = ObjC.Id(ObjC.RequireClass("CAMetalLayer"), "layer");
                ObjC.VoidId(s_layer, "setDevice:", Device);
                ObjC.SetU(s_layer, "setPixelFormat:", PixelFormatBgra8Unorm);
                Msg.VoidSize(s_layer, ObjC.Check(s_layer, "setDrawableSize:"), new CGSize { Width = Size, Height = Size });
                Report.Value(
                    "layer_create",
                    $"ok CAMetalLayer with no window, setDevice: done; allowsNextDrawableTimeout={ObjC.Bool(s_layer, "allowsNextDrawableTimeout")}"
                );
            }
        );
        Report.Step(
            "layer_next_drawable",
            () =>
            {
                if (s_layer == 0)
                {
                    throw new ProbeException("no layer (see layer_create)");
                }
                long start = Stopwatch.GetTimestamp();
                s_drawable = ObjC.Id(s_layer, "nextDrawable");
                double ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                Report.Value(
                    "layer_next_drawable",
                    s_drawable != 0 ? $"drawable after {ms:F1} ms ({ObjC.ClassName(s_drawable)})" : $"nil after {ms:F1} ms"
                );
            }
        );
        Report.Step(
            "layer_drawable_render",
            () =>
            {
                if (s_drawable == 0)
                {
                    throw new ProbeException("no drawable (see layer_next_drawable)");
                }
                nint texture = ObjC.Id(s_drawable, "texture");
                Report.Value("layer_drawable_render", $"{RenderPass(texture, s_pipeline, synchronize: false)} (not presented)");
            }
        );
    }

    private static nint NewTexture()
    {
        nint descriptor = ObjC.New("MTLTextureDescriptor");
        ObjC.SetU(descriptor, "setTextureType:", TextureType2D);
        ObjC.SetU(descriptor, "setPixelFormat:", PixelFormatBgra8Unorm);
        ObjC.SetU(descriptor, "setWidth:", Size);
        ObjC.SetU(descriptor, "setHeight:", Size);
        ObjC.SetU(descriptor, "setUsage:", UsageShaderReadRenderTarget);
        // Managed works on Apple silicon and on Intel GPUs; the blit's synchronizeResource: makes it readable by the CPU.
        ObjC.SetU(descriptor, "setStorageMode:", StorageModeManaged);
        nint texture = ObjC.Id(Device, "newTextureWithDescriptor:", descriptor);
        return texture != 0 ? texture : throw new ProbeException("newTextureWithDescriptor: returned nil");
    }

    // Clears to red, draws with the pipeline if one is given, commits and waits. Returns the status line.
    private static string RenderPass(nint texture, nint pipeline, bool synchronize)
    {
        nint pass = ObjC.Id(ObjC.RequireClass("MTLRenderPassDescriptor"), "renderPassDescriptor");
        nint attachment = ObjC.IdU(ObjC.Id(pass, "colorAttachments"), "objectAtIndexedSubscript:", 0);
        ObjC.VoidId(attachment, "setTexture:", texture);
        ObjC.SetU(attachment, "setLoadAction:", LoadActionClear);
        ObjC.SetU(attachment, "setStoreAction:", StoreActionStore);
        Msg.VoidClearColor(attachment, ObjC.Check(attachment, "setClearColor:"), new MTLClearColor { Red = 1, Alpha = 1 });

        nint buffer = ObjC.Id(Queue, "commandBuffer");
        nint encoder = ObjC.Id(buffer, "renderCommandEncoderWithDescriptor:", pass);
        if (pipeline != 0)
        {
            ObjC.VoidId(encoder, "setRenderPipelineState:", pipeline);
            Msg.VoidUIntUIntUInt(encoder, ObjC.Check(encoder, "drawPrimitives:vertexStart:vertexCount:"), PrimitiveTriangle, 0, 3);
        }
        ObjC.Void(encoder, "endEncoding");
        if (synchronize)
        {
            nint blit = ObjC.Id(buffer, "blitCommandEncoder");
            ObjC.VoidId(blit, "synchronizeResource:", texture);
            ObjC.Void(blit, "endEncoding");
        }
        ObjC.Void(buffer, "commit");
        long start = Stopwatch.GetTimestamp();
        ObjC.Void(buffer, "waitUntilCompleted");
        double waitMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        nint status = Msg.Int(buffer, ObjC.Check(buffer, "status"));
        nint error = ObjC.Id(buffer, "error");
        double gpuMs = (ObjC.Double(buffer, "GPUEndTime") - ObjC.Double(buffer, "GPUStartTime")) * 1000;
        string state = status == StatusCompleted ? "ok completed" : $"FAILED status {status} ({ObjC.ErrorText(error)})";
        return $"{state}; waitUntilCompleted returned after {waitMs:F2} ms; GPU time {gpuMs:F3} ms";
    }

    private static string ReadCenter(nint texture, byte expectedB, byte expectedG, byte expectedR)
    {
        byte* pixels = stackalloc byte[(int)(Size * Size * 4)];
        var region = new MTLRegion { Width = Size, Height = Size, Depth = 1 };
        Msg.VoidGetBytes(texture, ObjC.Check(texture, "getBytes:bytesPerRow:fromRegion:mipmapLevel:"), pixels, Size * 4, region, 0);
        int at = (int)((2 * Size + 2) * 4);
        byte b = pixels[at];
        byte g = pixels[at + 1];
        byte r = pixels[at + 2];
        byte a = pixels[at + 3];
        bool match = b == expectedB && g == expectedG && r == expectedR && a == 255;
        return $"readback BGRA=({b},{g},{r},{a}) expected ({expectedB},{expectedG},{expectedR},255): {(match ? "match" : "MISMATCH")}";
    }
}
