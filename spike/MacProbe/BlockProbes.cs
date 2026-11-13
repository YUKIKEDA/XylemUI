using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MacProbe;

// Probe 6: blocks built in C#, with no Obj-C shim.
//
// Layout used (Clang "Block Implementation Specification", clang/docs/Block-ABI-Apple.rst; written FROM MEMORY, not fetched):
//
//   struct Block_literal_1 {            // 64-bit offsets
//       void *isa;                      //  0  &_NSConcreteGlobalBlock, &_NSConcreteStackBlock (copied to _NSConcreteMallocBlock)
//       int flags;                      //  8
//       int reserved;                   // 12
//       void (*invoke)(void *, ...);    // 16  first argument is the block itself
//       struct Block_descriptor_1 {     // 24
//           unsigned long reserved;     //     0
//           unsigned long size;         //     sizeof the literal, captures included (what Block_copy copies)
//           // copy_helper, dispose_helper only if flags & BLOCK_HAS_COPY_DISPOSE (1 << 25)
//           const char *signature;      //     only if flags & BLOCK_HAS_SIGNATURE (1 << 30)
//       } *descriptor;
//       /* captured variables */        // 32
//   };
//   BLOCK_IS_GLOBAL = 1 << 28.
//
// The `clang_layout` step reads a block that clang itself built (native/probe.c) and prints its isa, flags, size and signature, so
// the run checks this layout against the compiler instead of against memory.
internal static unsafe class BlockProbes
{
    private const int BlockHasCopyDispose = 1 << 25;
    private const int BlockIsGlobal = 1 << 28;
    private const int BlockHasSignature = 1 << 30;

    [StructLayout(LayoutKind.Sequential)]
    private struct BlockDescriptor
    {
        public nuint Reserved;
        public nuint Size;
        public byte* Signature;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BlockLiteral
    {
        public nint Isa;
        public int Flags;
        public int Reserved;
        public nint Invoke;
        public BlockDescriptor* Descriptor;
        public nint Captured;
    }

    private static nint s_globalIsa;
    private static nint s_stackIsa;
    private static nint s_mallocIsa;
    private static byte* s_signature;
    private static BlockLiteral* s_global;
    private static BlockLiteral* s_captured;
    private static nint s_queue;

    private static int s_globalRuns;
    private static int s_capturedRuns;
    private static int s_mainRuns;
    private static int s_functionSum;
    private static nint s_seenIsa;
    private static nint s_seenCaptured;

    // Invoke functions: they only count and copy fields of the block they are given; nothing here can throw.
    [UnmanagedCallersOnly]
    private static void InvokeGlobal(BlockLiteral* block) => Interlocked.Increment(ref s_globalRuns);

    [UnmanagedCallersOnly]
    private static void InvokeMain(BlockLiteral* block) => Interlocked.Increment(ref s_mainRuns);

    [UnmanagedCallersOnly]
    private static void InvokeCaptured(BlockLiteral* block)
    {
        s_seenIsa = block->Isa;
        s_seenCaptured = block->Captured;
        Interlocked.Increment(ref s_capturedRuns);
    }

    [UnmanagedCallersOnly]
    private static void AddContext(nint context) => Interlocked.Add(ref s_functionSum, (int)context);

    public static void Run()
    {
        Report.Step(
            "runtime_symbols",
            () =>
            {
                s_globalIsa = Export("_NSConcreteGlobalBlock");
                s_stackIsa = Export("_NSConcreteStackBlock");
                s_mallocIsa = Export("_NSConcreteMallocBlock");
                byte[] signature = ObjC.Utf8z("v8@?0");
                s_signature = (byte*)NativeMemory.Alloc((nuint)signature.Length);
                signature.CopyTo(new Span<byte>(s_signature, signature.Length));
                Report.Value(
                    "runtime_symbols",
                    $"_NSConcreteGlobalBlock={ObjC.Hex(s_globalIsa)} _NSConcreteStackBlock={ObjC.Hex(s_stackIsa)} _NSConcreteMallocBlock={ObjC.Hex(s_mallocIsa)}"
                );
            }
        );
        Report.Step(
            "clang_layout",
            () =>
            {
                nint block = ProbeNative.probe_global_block();
                nint isa = *(nint*)block;
                int flags = *(int*)(block + 8);
                nuint* descriptor = *(nuint**)(block + 24);
                nuint size = descriptor[1];
                int signatureIndex = (flags & BlockHasCopyDispose) != 0 ? 4 : 2;
                string signature = (flags & BlockHasSignature) != 0 ? ObjC.Utf8((byte*)descriptor[signatureIndex]) : "none";
                ProbeNative.probe_invoke_block(block);
                string isaName = isa == s_globalIsa ? "_NSConcreteGlobalBlock" : ObjC.Hex(isa);
                Report.Value(
                    "clang_layout",
                    $"clang's global block: isa={isaName} flags=0x{flags:X8} descriptor.size={size} signature=\"{signature}\" invoke ran {ProbeNative.probe_global_block_runs()} time(s); "
                        + "this probe assumes isa=_NSConcreteGlobalBlock, flags including 0x50000000, size 32, signature v8@?0"
                );
            }
        );
        Report.Step(
            "csharp_global_called_by_clang",
            () =>
            {
                s_global = NewBlock(s_globalIsa, BlockIsGlobal | BlockHasSignature, &InvokeGlobal, 0, 32);
                ProbeNative.probe_invoke_block((nint)s_global);
                bool sameAfterCopy = Libc._Block_copy((nint)s_global) == (nint)s_global;
                Report.Value(
                    "csharp_global_called_by_clang",
                    s_globalRuns == 1
                        ? $"ok clang-compiled code called a C#-built global block; _Block_copy returned the same pointer: {sameAfterCopy}"
                        : $"FAILED invoke ran {s_globalRuns} times"
                );
            }
        );
        Report.Step(
            "csharp_stack_copied_by_runtime",
            () =>
            {
                s_captured = NewBlock(s_stackIsa, BlockHasSignature, &InvokeCaptured, 7, 40);
                nint isaAfterCopy = 0;
                int moved = ProbeNative.probe_copy_call_release((nint)s_captured, &isaAfterCopy);
                bool ok = moved == 1 && isaAfterCopy == s_mallocIsa && s_capturedRuns == 1 && s_seenCaptured == 7;
                Report.Value(
                    "csharp_stack_copied_by_runtime",
                    $"{(ok ? "ok" : "FAILED")} Block_copy moved it: {moved == 1}, copy isa is _NSConcreteMallocBlock: {isaAfterCopy == s_mallocIsa}, "
                        + $"invoke ran {s_capturedRuns} time(s) and read the captured value {s_seenCaptured} (expected 7)"
                );
            }
        );
        Report.Step(
            "dispatch_sync_private_queue",
            () =>
            {
                fixed (byte* label = ObjC.Utf8z("dev.xylemui.probe"))
                {
                    s_queue = Libc.dispatch_queue_create(label, 0);
                }
                int before = s_globalRuns;
                Libc.dispatch_sync(s_queue, (nint)s_global);
                Report.Value(
                    "dispatch_sync_private_queue",
                    s_globalRuns == before + 1 ? "ok dispatch_sync ran the C#-built global block" : "FAILED the block did not run"
                );
            }
        );
        Report.Step(
            "dispatch_async_private_queue",
            () =>
            {
                int before = s_capturedRuns;
                Libc.dispatch_async(s_queue, (nint)s_captured);
                Libc.dispatch_sync(s_queue, (nint)s_global); // a serial queue: this returns after the async block has run
                bool ran = s_capturedRuns == before + 1;
                Report.Value(
                    "dispatch_async_private_queue",
                    $"{(ran && s_seenCaptured == 7 ? "ok" : "FAILED")} dispatch_async of a C#-built stack block: ran {ran}, "
                        + $"isa seen by invoke is _NSConcreteMallocBlock: {s_seenIsa == s_mallocIsa}, captured {s_seenCaptured}; the invoke ran on a libdispatch thread"
                );
            }
        );
        Report.Step(
            "dispatch_f",
            () =>
            {
                delegate* unmanaged<nint, void> work = &AddContext;
                Libc.dispatch_sync_f(s_queue, 5, (nint)work);
                Libc.dispatch_async_f(s_queue, 6, (nint)work);
                Libc.dispatch_sync(s_queue, (nint)s_global);
                Report.Value(
                    "dispatch_f",
                    s_functionSum == 11 ? "ok dispatch_sync_f and dispatch_async_f (no block)" : $"FAILED sum {s_functionSum}, expected 11"
                );
            }
        );
        Report.Step(
            "main_queue",
            () =>
            {
                // dispatch_get_main_queue() is a macro for &_dispatch_main_q: a C# caller reads the symbol's address.
                nint mainQueue = Export("_dispatch_main_q");
                BlockLiteral* block = NewBlock(s_globalIsa, BlockIsGlobal | BlockHasSignature, &InvokeMain, 0, 32);
                Libc.dispatch_async(mainQueue, (nint)block);
                nint mode = ObjC.ReadExport(Frameworks.Handle("CoreFoundation"), "kCFRunLoopDefaultMode");
                long start = Stopwatch.GetTimestamp();
                int rc = 0;
                while (Volatile.Read(ref s_mainRuns) == 0 && Stopwatch.GetElapsedTime(start).TotalSeconds < 3)
                {
                    rc = CF.CFRunLoopRunInMode(mode, 0.1, 1);
                }
                double ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                Report.Value(
                    "main_queue",
                    s_mainRuns == 1
                        ? $"ok dispatch_async(&_dispatch_main_q, block) ran after {ms:F1} ms of CFRunLoopRunInMode on the main thread (rc {rc})"
                        : $"FAILED not run after {ms:F0} ms of CFRunLoopRunInMode (last rc {rc})"
                );
            }
        );
    }

    // In native memory, never freed: the GC cannot move it, and the probe ends with the process.
    private static BlockLiteral* NewBlock(nint isa, int flags, delegate* unmanaged<BlockLiteral*, void> invoke, nint captured, nuint size)
    {
        if (isa == 0 || s_signature == null)
        {
            throw new ProbeException("block runtime symbols missing (see runtime_symbols)");
        }
        var descriptor = (BlockDescriptor*)NativeMemory.AllocZeroed((nuint)sizeof(BlockDescriptor));
        descriptor->Size = size;
        descriptor->Signature = s_signature;
        var block = (BlockLiteral*)NativeMemory.AllocZeroed((nuint)sizeof(BlockLiteral));
        block->Isa = isa;
        block->Flags = flags;
        block->Invoke = (nint)invoke;
        block->Descriptor = descriptor;
        block->Captured = captured;
        return block;
    }

    private static nint Export(string name)
    {
        string[] libraries = [Paths.LibSystem, "/usr/lib/system/libsystem_blocks.dylib", "/usr/lib/system/libdispatch.dylib"];
        foreach (string library in libraries)
        {
            if (NativeLibrary.TryLoad(library, out nint handle) && NativeLibrary.TryGetExport(handle, name, out nint address))
            {
                Report.Info($"{name} found through {library}");
                return address;
            }
        }
        throw new ProbeException($"symbol {name} not found in {string.Join(", ", libraries)}");
    }
}
