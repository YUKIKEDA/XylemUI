using System.Runtime.InteropServices;

namespace OsAppearance;

/// <summary>The Win32 and WinRT calls of the probe, all with blittable signatures (no runtime marshalling).</summary>
internal static unsafe partial class Native
{
    public const uint WmDestroy = 0x0002;
    public const uint WmSysColorChange = 0x0015;
    public const uint WmSettingChange = 0x001A;
    public const uint WmTimer = 0x0113;
    public const uint WmThemeChanged = 0x031A;
    public const uint WmDwmColorizationColorChanged = 0x0320;
    public const uint WmApp = 0x8000;

    public const uint SpiGetHighContrast = 0x0042;
    public const uint SpiSetHighContrast = 0x0043;
    public const uint HcfHighContrastOn = 0x0001;

    public const uint WsOverlappedWindow = 0x00CF0000;
    public const int CwUseDefault = unchecked((int)0x80000000);
    public const int SwShowNormal = 1;

    // HKEY_CURRENT_USER and HKEY_LOCAL_MACHINE are ((HKEY)(ULONG_PTR)((LONG)0x8000000N)) in winreg.h: sign-extended on x64.
    public static readonly nint HkeyCurrentUser = unchecked((int)0x80000001);
    public static readonly nint HkeyLocalMachine = unchecked((int)0x80000002);
    public const uint RrfRtRegSz = 0x00000002;
    public const uint RrfRtRegDword = 0x00000010;

    [StructLayout(LayoutKind.Sequential)]
    public struct WndClassExW
    {
        public uint Size;
        public uint Style;
        public delegate* unmanaged<nint, uint, nint, nint, nint> WndProc;
        public int ClassExtra;
        public int WindowExtra;
        public nint Instance;
        public nint Icon;
        public nint Cursor;
        public nint Background;
        public char* MenuName;
        public char* ClassName;
        public nint SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Msg
    {
        public nint Window;
        public uint Message;
        public nint WParam;
        public nint LParam;
        public uint Time;
        public int X;
        public int Y;
        public uint Private;
    }

    /// <summary>HIGHCONTRASTW (winuser.h): cbSize, dwFlags, lpszDefaultScheme.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct HighContrastW
    {
        public uint Size;
        public uint Flags;
        public char* DefaultScheme;
    }

    /// <summary>Windows.UI.Color (windows.ui.h): A, R, G, B in that order.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct WinRTColor
    {
        public byte A;
        public byte R;
        public byte G;
        public byte B;
    }

    // combase.dll: WinRT strings and activation (roapi.h, winstring.h).
    [LibraryImport("combase.dll")]
    public static partial int WindowsCreateString(char* sourceString, uint length, nint* hstring);

    [LibraryImport("combase.dll")]
    public static partial int WindowsDeleteString(nint hstring);

    [LibraryImport("combase.dll")]
    public static partial char* WindowsGetStringRawBuffer(nint hstring, uint* length);

    [LibraryImport("combase.dll")]
    public static partial int RoActivateInstance(nint activatableClassId, void** instance);

    // ole32.dll: the UI thread's apartment, as XylemApp.Run sets it (OleInitialize on a [STAThread] Main).
    [LibraryImport("ole32.dll")]
    public static partial int OleInitialize(void* reserved);

    [LibraryImport("ole32.dll")]
    public static partial void OleUninitialize();

    [LibraryImport("ole32.dll")]
    public static partial int CoGetApartmentType(int* aptType, int* aptQualifier);

    // user32.dll
    [LibraryImport("user32.dll")]
    public static partial uint GetSysColor(int index);

    [LibraryImport("user32.dll")]
    public static partial int SystemParametersInfoW(uint action, uint uiParam, void* pvParam, uint winIni);

    [LibraryImport("user32.dll")]
    public static partial ushort RegisterClassExW(WndClassExW* windowClass);

    [LibraryImport("user32.dll")]
    public static partial nint CreateWindowExW(
        uint exStyle,
        char* className,
        char* windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        nint parent,
        nint menu,
        nint instance,
        void* param
    );

    [LibraryImport("user32.dll")]
    public static partial nint DefWindowProcW(nint window, uint message, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    public static partial int DestroyWindow(nint window);

    [LibraryImport("user32.dll")]
    public static partial int ShowWindow(nint window, int command);

    [LibraryImport("user32.dll")]
    public static partial nint LoadCursorW(nint instance, nint name);

    [LibraryImport("user32.dll")]
    public static partial int GetMessageW(Msg* message, nint window, uint filterMin, uint filterMax);

    [LibraryImport("user32.dll")]
    public static partial int TranslateMessage(Msg* message);

    [LibraryImport("user32.dll")]
    public static partial nint DispatchMessageW(Msg* message);

    [LibraryImport("user32.dll")]
    public static partial void PostQuitMessage(int exitCode);

    [LibraryImport("user32.dll")]
    public static partial int PostMessageW(nint window, uint message, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    public static partial nuint SetTimer(nint window, nuint id, uint elapse, nint timerProc);

    [LibraryImport("user32.dll")]
    public static partial int KillTimer(nint window, nuint id);

    // kernel32.dll
    [LibraryImport("kernel32.dll")]
    public static partial nint GetModuleHandleW(char* moduleName);

    [LibraryImport("kernel32.dll")]
    public static partial uint GetCurrentThreadId();

    // dwmapi.dll
    [LibraryImport("dwmapi.dll")]
    public static partial int DwmSetWindowAttribute(nint window, uint attribute, void* value, uint size);

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmGetWindowAttribute(nint window, uint attribute, void* value, uint size);

    // advapi32.dll: the registry values beside UISettings, for comparison only (the design does not read them).
    [LibraryImport("advapi32.dll")]
    public static partial int RegGetValueW(nint key, char* subKey, char* value, uint flags, uint* type, void* data, uint* size);

    /// <summary>A COM call through slot <paramref name="slot"/> of <paramref name="obj"/>'s vtable.</summary>
    public static void* Slot(void* obj, int slot) => (*(void***)obj)[slot];

    public static int QueryInterface(void* obj, Guid* iid, void** result) =>
        ((delegate* unmanaged<void*, Guid*, void**, int>)Slot(obj, 0))(obj, iid, result);

    public static uint Release(void* obj) => ((delegate* unmanaged<void*, uint>)Slot(obj, 2))(obj);
}
