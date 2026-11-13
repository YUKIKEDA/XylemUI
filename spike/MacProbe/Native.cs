using System.Runtime.InteropServices;

namespace MacProbe;

// Every system library is bound lazily by path (dlopen at the first call), so `otool -L` of the executable lists only what
// NativeAOT itself links, and the `dyld_images` line of each group lists what was loaded at run time. The only libraries linked at
// build time are the static ones of probe 7 (libprobe.a, and libaccesskit.a in the AccessKit variant).
internal static class Paths
{
    public const string ObjC = "/usr/lib/libobjc.A.dylib";
    public const string LibSystem = "/usr/lib/libSystem.B.dylib";
    public const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    public const string Foundation = "/System/Library/Frameworks/Foundation.framework/Foundation";
    public const string AppKit = "/System/Library/Frameworks/AppKit.framework/AppKit";
    public const string QuartzCore = "/System/Library/Frameworks/QuartzCore.framework/QuartzCore";
    public const string Metal = "/System/Library/Frameworks/Metal.framework/Metal";
    public const string CoreText = "/System/Library/Frameworks/CoreText.framework/CoreText";
    public const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    public const string CoreVideo = "/System/Library/Frameworks/CoreVideo.framework/CoreVideo";
}

[StructLayout(LayoutKind.Sequential)]
internal struct CFRange
{
    public nint Location;
    public nint Length;

    public CFRange(nint location, nint length)
    {
        Location = location;
        Length = length;
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct CGSize
{
    public double Width;
    public double Height;
}

[StructLayout(LayoutKind.Sequential)]
internal struct CGPoint
{
    public double X;
    public double Y;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MTLClearColor
{
    public double Red;
    public double Green;
    public double Blue;
    public double Alpha;
}

// MTLRegion = { MTLOrigin { x, y, z }, MTLSize { width, height, depth } }, six NSUInteger.
[StructLayout(LayoutKind.Sequential)]
internal struct MTLRegion
{
    public nuint X;
    public nuint Y;
    public nuint Z;
    public nuint Width;
    public nuint Height;
    public nuint Depth;
}

[StructLayout(LayoutKind.Sequential)]
internal struct CVTime
{
    public long TimeValue;
    public int TimeScale;
    public int Flags;
}

// CFRunLoopSourceContext, version 0: ten pointer-sized fields.
[StructLayout(LayoutKind.Sequential)]
internal struct CFRunLoopSourceContext
{
    public nint Version;
    public nint Info;
    public nint Retain;
    public nint Release;
    public nint CopyDescription;
    public nint Equal;
    public nint Hash;
    public nint Schedule;
    public nint Cancel;
    public nint Perform;
}

internal static unsafe partial class ObjCRuntime
{
    [LibraryImport(Paths.ObjC)]
    public static partial nint objc_getClass(byte* name);

    [LibraryImport(Paths.ObjC)]
    public static partial nint sel_registerName(byte* name);

    [LibraryImport(Paths.ObjC)]
    public static partial nint object_getClass(nint obj);

    [LibraryImport(Paths.ObjC)]
    public static partial byte* class_getName(nint cls);

    [LibraryImport(Paths.ObjC)]
    public static partial byte class_respondsToSelector(nint cls, nint sel);

    [LibraryImport(Paths.ObjC)]
    public static partial nint class_getInstanceMethod(nint cls, nint sel);

    [LibraryImport(Paths.ObjC)]
    public static partial nint objc_allocateClassPair(nint superclass, byte* name, nuint extraBytes);

    [LibraryImport(Paths.ObjC)]
    public static partial void objc_registerClassPair(nint cls);

    [LibraryImport(Paths.ObjC)]
    public static partial byte class_addMethod(nint cls, nint sel, nint imp, byte* types);

    // alignment is log2 of the alignment in bytes (3 for 8 bytes).
    [LibraryImport(Paths.ObjC)]
    public static partial byte class_addIvar(nint cls, byte* name, nuint size, byte alignment, byte* types);

    [LibraryImport(Paths.ObjC)]
    public static partial nint class_getInstanceVariable(nint cls, byte* name);

    [LibraryImport(Paths.ObjC)]
    public static partial nint ivar_getOffset(nint ivar);

    [LibraryImport(Paths.ObjC)]
    public static partial nint objc_getProtocol(byte* name);

    [LibraryImport(Paths.ObjC)]
    public static partial byte class_addProtocol(nint cls, nint protocol);

    [LibraryImport(Paths.ObjC)]
    public static partial byte class_conformsToProtocol(nint cls, nint protocol);

    [LibraryImport(Paths.ObjC)]
    public static partial nint objc_autoreleasePoolPush();

    [LibraryImport(Paths.ObjC)]
    public static partial void objc_autoreleasePoolPop(nint pool);
}

// objc_msgSend declared once per call shape. On arm64 objc_msgSend is called with the C ABI of the method itself, and there is no
// _stret or _fpret variant. On x86_64 a struct return larger than 16 bytes would need objc_msgSend_stret; no shape below returns a
// struct, and a double return goes through plain objc_msgSend there (only long double needs _fpret). Struct ARGUMENTS
// (MTLClearColor, CGSize, MTLRegion) are passed by the platform ABI like any C call.
internal static unsafe partial class Msg
{
    private const string Send = "objc_msgSend";

    [LibraryImport(Paths.ObjC, EntryPoint = Send)]
    public static partial nint Id(nint self, nint op);

    [LibraryImport(Paths.ObjC, EntryPoint = Send)]
    public static partial nint IdId(nint self, nint op, nint a);

    [LibraryImport(Paths.ObjC, EntryPoint = Send)]
    public static partial nint IdUInt(nint self, nint op, nuint a);

    [LibraryImport(Paths.ObjC, EntryPoint = Send)]
    public static partial nint IdPtr(nint self, nint op, byte* a);

    [LibraryImport(Paths.ObjC, EntryPoint = Send)]
    public static partial nint IdCharsLength(nint self, nint op, char* characters, nuint length);

    [LibraryImport(Paths.ObjC, EntryPoint = Send)]
    public static partial nint IdIdError(nint self, nint op, nint a, nint* error);

    [LibraryImport(Paths.ObjC, EntryPoint = Send)]
    public static partial nint IdIdIdError(nint self, nint op, nint a, nint b, nint* error);

    [LibraryImport(Paths.ObjC, EntryPoint = Send)]
    public static partial void Void(nint self, nint op);

    [LibraryImport(Paths.ObjC, EntryPoint = Send)]
    public static partial void VoidId(nint self, nint op, nint a);

    [LibraryImport(Paths.ObjC, EntryPoint = Send)]
    public static partial void VoidUInt(nint self, nint op, nuint a);

    [LibraryImport(Paths.ObjC, EntryPoint = Send)]
    public static partial void VoidIdIdBool(nint self, nint op, nint a, nint b, byte c);

    [LibraryImport(Paths.ObjC, EntryPoint = Send)]
    public static partial void VoidClearColor(nint self, nint op, MTLClearColor color);

    [LibraryImport(Paths.ObjC, EntryPoint = Send)]
    public static partial void VoidSize(nint self, nint op, CGSize size);

    [LibraryImport(Paths.ObjC, EntryPoint = Send)]
    public static partial void VoidUIntUIntUInt(nint self, nint op, nuint a, nuint b, nuint c);

    [LibraryImport(Paths.ObjC, EntryPoint = Send)]
    public static partial void VoidGetBytes(nint self, nint op, byte* bytes, nuint bytesPerRow, MTLRegion region, nuint mipmapLevel);

    [LibraryImport(Paths.ObjC, EntryPoint = Send)]
    public static partial nuint UInt(nint self, nint op);

    [LibraryImport(Paths.ObjC, EntryPoint = Send)]
    public static partial nint Int(nint self, nint op);

    [LibraryImport(Paths.ObjC, EntryPoint = Send)]
    public static partial ulong UInt64(nint self, nint op);

    [LibraryImport(Paths.ObjC, EntryPoint = Send)]
    public static partial double Double(nint self, nint op);

    // BOOL is `bool` on arm64 and `signed char` on x86_64; one byte either way.
    [LibraryImport(Paths.ObjC, EntryPoint = Send)]
    public static partial byte Bool(nint self, nint op);

    [LibraryImport(Paths.ObjC, EntryPoint = Send)]
    public static partial byte BoolId(nint self, nint op, nint a);

    [LibraryImport(Paths.ObjC, EntryPoint = Send)]
    public static partial byte BoolInt(nint self, nint op, nint a);

    [LibraryImport(Paths.ObjC, EntryPoint = Send)]
    public static partial long LongLong(nint self, nint op, long a);
}

internal static unsafe partial class Libc
{
    [LibraryImport(Paths.LibSystem, SetLastError = true)]
    public static partial int sysctlbyname(byte* name, void* oldp, nuint* oldlenp, void* newp, nuint newlen);

    [LibraryImport(Paths.LibSystem)]
    public static partial int pthread_main_np();

    [LibraryImport(Paths.LibSystem)]
    public static partial void _exit(int status);

    [LibraryImport(Paths.LibSystem)]
    public static partial uint _dyld_image_count();

    [LibraryImport(Paths.LibSystem)]
    public static partial byte* _dyld_get_image_name(uint index);

    [LibraryImport(Paths.LibSystem)]
    public static partial nint dispatch_queue_create(byte* label, nint attr);

    [LibraryImport(Paths.LibSystem)]
    public static partial void dispatch_sync(nint queue, nint block);

    [LibraryImport(Paths.LibSystem)]
    public static partial void dispatch_async(nint queue, nint block);

    [LibraryImport(Paths.LibSystem)]
    public static partial void dispatch_sync_f(nint queue, nint context, nint work);

    [LibraryImport(Paths.LibSystem)]
    public static partial void dispatch_async_f(nint queue, nint context, nint work);

    [LibraryImport(Paths.LibSystem)]
    public static partial nint _Block_copy(nint block);

    [LibraryImport(Paths.LibSystem)]
    public static partial void _Block_release(nint block);
}

internal static unsafe partial class CF
{
    [LibraryImport(Paths.CoreFoundation)]
    public static partial nint CFStringCreateWithCharacters(nint allocator, char* characters, nint length);

    [LibraryImport(Paths.CoreFoundation)]
    public static partial nint CFStringGetLength(nint str);

    [LibraryImport(Paths.CoreFoundation)]
    public static partial void CFStringGetCharacters(nint str, CFRange range, char* buffer);

    [LibraryImport(Paths.CoreFoundation)]
    public static partial void CFRelease(nint obj);

    [LibraryImport(Paths.CoreFoundation)]
    public static partial nint CFArrayGetCount(nint array);

    [LibraryImport(Paths.CoreFoundation)]
    public static partial nint CFArrayGetValueAtIndex(nint array, nint index);

    // style 0 = kCFURLPOSIXPathStyle.
    [LibraryImport(Paths.CoreFoundation)]
    public static partial nint CFURLCopyFileSystemPath(nint url, nint style);

    [LibraryImport(Paths.CoreFoundation)]
    public static partial nint CFRunLoopGetMain();

    [LibraryImport(Paths.CoreFoundation)]
    public static partial nint CFRunLoopGetCurrent();

    // Returns 1 finished, 2 stopped, 3 timed out, 4 handled a source.
    [LibraryImport(Paths.CoreFoundation)]
    public static partial int CFRunLoopRunInMode(nint mode, double seconds, byte returnAfterSourceHandled);

    [LibraryImport(Paths.CoreFoundation)]
    public static partial nint CFRunLoopSourceCreate(nint allocator, nint order, CFRunLoopSourceContext* context);

    [LibraryImport(Paths.CoreFoundation)]
    public static partial void CFRunLoopAddSource(nint runLoop, nint source, nint mode);

    [LibraryImport(Paths.CoreFoundation)]
    public static partial void CFRunLoopSourceSignal(nint source);

    [LibraryImport(Paths.CoreFoundation)]
    public static partial void CFRunLoopWakeUp(nint runLoop);
}

internal static unsafe partial class CT
{
    [LibraryImport(Paths.CoreText)]
    public static partial nint CTFontCreateWithName(nint name, double size, nint matrix);

    [LibraryImport(Paths.CoreText)]
    public static partial nint CTFontCreateUIFontForLanguage(uint uiType, double size, nint language);

    [LibraryImport(Paths.CoreText)]
    public static partial nint CTFontCopyFamilyName(nint font);

    [LibraryImport(Paths.CoreText)]
    public static partial nint CTFontCopyPostScriptName(nint font);

    [LibraryImport(Paths.CoreText)]
    public static partial nint CTFontCopyAttribute(nint font, nint attribute);

    [LibraryImport(Paths.CoreText)]
    public static partial nint CTFontCreateForString(nint currentFont, nint str, CFRange range);

    [LibraryImport(Paths.CoreText)]
    public static partial byte CTFontGetGlyphsForCharacters(nint font, char* characters, ushort* glyphs, nint count);

    [LibraryImport(Paths.CoreText)]
    public static partial void CTFontDrawGlyphs(nint font, ushort* glyphs, CGPoint* positions, nuint count, nint context);
}

internal static unsafe partial class CG
{
    [LibraryImport(Paths.CoreGraphics)]
    public static partial nint CGColorSpaceCreateDeviceGray();

    [LibraryImport(Paths.CoreGraphics)]
    public static partial nint CGBitmapContextCreate(
        void* data,
        nuint width,
        nuint height,
        nuint bitsPerComponent,
        nuint bytesPerRow,
        nint space,
        uint bitmapInfo
    );

    [LibraryImport(Paths.CoreGraphics)]
    public static partial void CGContextSetGrayFillColor(nint context, double gray, double alpha);

    [LibraryImport(Paths.CoreGraphics)]
    public static partial int CGGetActiveDisplayList(uint maxDisplays, uint* displays, uint* count);

    [LibraryImport(Paths.CoreGraphics)]
    public static partial int CGGetOnlineDisplayList(uint maxDisplays, uint* displays, uint* count);

    [LibraryImport(Paths.CoreGraphics)]
    public static partial uint CGMainDisplayID();
}

internal static unsafe partial class CV
{
    [LibraryImport(Paths.CoreVideo)]
    public static partial int CVDisplayLinkCreateWithActiveCGDisplays(nint* link);

    [LibraryImport(Paths.CoreVideo)]
    public static partial int CVDisplayLinkSetOutputCallback(nint link, nint callback, nint userInfo);

    [LibraryImport(Paths.CoreVideo)]
    public static partial int CVDisplayLinkStart(nint link);

    [LibraryImport(Paths.CoreVideo)]
    public static partial int CVDisplayLinkStop(nint link);

    // CVTime is 16 bytes: returned in registers on both arm64 and x86_64.
    [LibraryImport(Paths.CoreVideo)]
    public static partial CVTime CVDisplayLinkGetNominalOutputVideoRefreshPeriod(nint link);
}

internal static partial class MetalApi
{
    [LibraryImport(Paths.Metal)]
    public static partial nint MTLCreateSystemDefaultDevice();

    [LibraryImport(Paths.Metal)]
    public static partial nint MTLCopyAllDevices();
}

// native/probe.c, linked statically (DirectPInvoke "probe" in MacProbe.csproj).
internal static unsafe partial class ProbeNative
{
    private const string Lib = "probe";

    [LibraryImport(Lib)]
    public static partial int probe_add(int a, int b);

    [LibraryImport(Lib)]
    public static partial byte* probe_compiler();

    [LibraryImport(Lib)]
    public static partial byte* probe_arch();

    [LibraryImport(Lib)]
    public static partial int probe_min_macos();

    [LibraryImport(Lib)]
    public static partial nint probe_global_block();

    [LibraryImport(Lib)]
    public static partial int probe_global_block_runs();

    [LibraryImport(Lib)]
    public static partial void probe_invoke_block(nint block);

    [LibraryImport(Lib)]
    public static partial int probe_copy_call_release(nint block, nint* isaAfterCopy);
}

#if PROBE_ACCESSKIT
// include/accesskit.h of accesskit-c 0.23.1 (checked in the archive vendor/build.ps1 extracts on Windows).
internal static unsafe partial class AccessKitApi
{
    private const string Lib = "accesskit";

    [LibraryImport(Lib)]
    public static partial nint accesskit_node_new(byte role);

    [LibraryImport(Lib)]
    public static partial void accesskit_node_free(nint node);

    [LibraryImport(Lib)]
    public static partial byte accesskit_node_role(nint node);

    [LibraryImport(Lib)]
    public static partial void accesskit_node_set_label(nint node, byte* value);

    [LibraryImport(Lib)]
    public static partial byte* accesskit_node_label(nint node);

    [LibraryImport(Lib)]
    public static partial void accesskit_string_free(byte* value);

    // Never called (see HostProbes.AccessKit): referenced so that the macOS adapter stays in the link and the size growth is that
    // of a real use, not of the node functions alone.
    [LibraryImport(Lib)]
    public static partial nint accesskit_macos_adapter_new(nint view, byte isViewFocused, nint actionHandler, nint userdata);
}
#endif
