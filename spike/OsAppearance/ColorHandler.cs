using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using static OsAppearance.Native;
using static OsAppearance.Report;

namespace OsAppearance;

/// <summary>
/// A hand-written <c>TypedEventHandler&lt;UISettings, Object&gt;</c>: a native struct whose first field points to a vtable of
/// <c>[UnmanagedCallersOnly]</c> functions (QueryInterface, AddRef, Release, Invoke; the C declaration of
/// <c>__FITypedEventHandler_2_Windows__CUI__CViewManagement__CUISettings_IInspectable</c> in windows.ui.viewmanagement.h),
/// the pattern of <c>TextAnalysisSource</c> in XylemUI.Platform.Windows. It answers <c>IAgileObject</c> so that the event
/// source can call it from any thread without marshalling, and logs every interface it is asked for.
/// </summary>
internal static unsafe class ColorHandler
{
    /// <summary>
    /// The handler's IID, derived as the WinRT type system derives a parameterized interface's IID: a UUID version 5 (SHA-1)
    /// of the namespace 11f47ad5-7b73-42c0-abae-878b1e16adee and the UTF-8 signature. TypedEventHandler`2's PIID is
    /// 9de1c534-6ae1-11e0-84e1-18a905bcc53f; UISettings's default interface is IUISettings (85361600-...); Object is
    /// <c>cinterface(IInspectable)</c>. Not in the SDK's C header (it declares the IID extern only), so the probe prints it
    /// and logs what the OS asks for.
    /// </summary>
    public static readonly Guid Iid = ParameterizedIid(
        "pinterface({9de1c534-6ae1-11e0-84e1-18a905bcc53f};"
            + "rc(Windows.UI.ViewManagement.UISettings;{85361600-1c63-4627-bcb1-3a89e0bc9c55});"
            + "cinterface(IInspectable))"
    );

    private const int MaxQueryLines = 32;

    private static readonly Guid IidUnknown = new("00000000-0000-0000-c000-000000000046");
    private static readonly Guid IidAgileObject = new("94ea2b94-e9cc-49e0-c0ff-ee64ca8f5b90");

    private static int _queries;
    private static int _invocations;

    [StructLayout(LayoutKind.Sequential)]
    private struct HandlerObject
    {
        public void** Vtbl;
        public int References;
    }

    /// <summary>Makes the handler with one reference, the caller's. It lives in native memory until the process ends.</summary>
    public static void* Create()
    {
        void** vtbl = (void**)NativeMemory.Alloc((nuint)(4 * sizeof(void*)));
        vtbl[0] = (void*)(delegate* unmanaged<HandlerObject*, Guid*, void**, int>)&QueryInterfaceImpl;
        vtbl[1] = (void*)(delegate* unmanaged<HandlerObject*, uint>)&AddRefImpl;
        vtbl[2] = (void*)(delegate* unmanaged<HandlerObject*, uint>)&ReleaseImpl;
        vtbl[3] = (void*)(delegate* unmanaged<HandlerObject*, void*, void*, int>)&InvokeImpl;
        var handler = (HandlerObject*)NativeMemory.Alloc((nuint)sizeof(HandlerObject));
        handler->Vtbl = vtbl;
        handler->References = 1;
        return handler;
    }

    [UnmanagedCallersOnly]
    private static int QueryInterfaceImpl(HandlerObject* self, Guid* iid, void** result)
    {
        *result = null;
        bool known = *iid == IidUnknown || *iid == IidAgileObject || *iid == Iid;
        if (known)
        {
            *result = self;
            _ = Interlocked.Increment(ref self->References);
        }

        try
        {
            if (Interlocked.Increment(ref _queries) <= MaxQueryLines)
            {
                Probe(
                    "colorvalueschanged.query_interface",
                    $"{iid->ToString("B")} {Name(*iid)} -> {(known ? "S_OK" : "E_NOINTERFACE")} thread={Text(GetCurrentThreadId())}"
                );
            }
        }
        catch (Exception exception)
        {
            Failed("colorvalueschanged.query_interface", exception);
        }

        return known ? 0 : unchecked((int)0x80004002);
    }

    // AddRef and Release only change the count through the pointer; they cannot throw and have no catch. The object is never
    // freed: the probe makes one per process.
    [UnmanagedCallersOnly]
    private static uint AddRefImpl(HandlerObject* self) => (uint)Interlocked.Increment(ref self->References);

    [UnmanagedCallersOnly]
    private static uint ReleaseImpl(HandlerObject* self) => (uint)Interlocked.Decrement(ref self->References);

    [UnmanagedCallersOnly]
    private static int InvokeImpl(HandlerObject* self, void* sender, void* args)
    {
        try
        {
            int n = Interlocked.Increment(ref _invocations);
            uint thread = GetCurrentThreadId();
            Probe(
                $"colorvalueschanged.{Text(n)}",
                $"{Stamp()} thread={Text(thread)} ui_thread={Text(Program.UiThread)} same={thread == Program.UiThread} "
                    + $"apartment={Apartment()} foreground_via_sender={UISettingsProbe.ForegroundThroughSender(sender)}"
            );

            // The UI thread re-reads everything and logs what changed (Program.WndProc).
            _ = PostMessageW(Program.Window, Program.WmColorValuesChanged, n, 0);
        }
        catch (Exception exception)
        {
            Failed("colorvalueschanged.invoke", exception);
        }

        return 0;
    }

    /// <summary>The calling thread's COM apartment (CoGetApartmentType): APTTYPE and APTTYPEQUALIFIER.</summary>
    public static string Apartment()
    {
        int type;
        int qualifier;
        int hr = CoGetApartmentType(&type, &qualifier);
        if (hr < 0)
        {
            return "FAILED " + Hr(hr);
        }

        string typeName = type switch
        {
            0 => "STA",
            1 => "MTA",
            2 => "NA",
            3 => "MAINSTA",
            _ => Text(type),
        };
        string qualifierName = qualifier switch
        {
            0 => "NONE",
            1 => "IMPLICIT_MTA",
            2 => "NA_ON_MTA",
            3 => "NA_ON_STA",
            4 => "NA_ON_IMPLICIT_MTA",
            5 => "NA_ON_MAINSTA",
            6 => "APPLICATION_STA",
            _ => Text(qualifier),
        };
        return $"{typeName}/{qualifierName}";
    }

    private static string Name(Guid iid) =>
        iid == IidUnknown ? "(IUnknown)"
        : iid == IidAgileObject ? "(IAgileObject)"
        : iid == Iid ? "(TypedEventHandler<UISettings,Object>)"
        : "(other)";

    private static Guid ParameterizedIid(string signature)
    {
        ReadOnlySpan<byte> wrtPinterfaceNamespace = [0x11, 0xf4, 0x7a, 0xd5, 0x7b, 0x73, 0x42, 0xc0, 0xab, 0xae, 0x87, 0x8b, 0x1e, 0x16, 0xad, 0xee];
        byte[] data = new byte[16 + Encoding.UTF8.GetByteCount(signature)];
        wrtPinterfaceNamespace.CopyTo(data);
        _ = Encoding.UTF8.GetBytes(signature, data.AsSpan(16));
        byte[] hash = SHA1.HashData(data);
        hash[6] = (byte)((hash[6] & 0x0F) | 0x50);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        return new Guid(hash.AsSpan(0, 16), bigEndian: true);
    }
}
