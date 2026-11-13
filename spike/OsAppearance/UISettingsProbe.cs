using System.Diagnostics;
using static OsAppearance.Native;
using static OsAppearance.Report;

namespace OsAppearance;

/// <summary>
/// <c>Windows.UI.ViewManagement.UISettings</c> through <c>RoActivateInstance</c> and the vtable of <c>IUISettings3</c>
/// (windows.ui.viewmanagement.h of the Windows SDK 10.0.22621: IInspectable's 6 slots, then GetColorValue,
/// add_ColorValuesChanged, remove_ColorValuesChanged).
/// </summary>
internal static unsafe class UISettingsProbe
{
    public const int Background = 0;
    public const int Foreground = 1;

    private const string ClassName = "Windows.UI.ViewManagement.UISettings";

    // IUISettings3, MIDL_INTERFACE("03021be4-5254-4781-8194-5168f7d06d7b") in windows.ui.viewmanagement.h.
    private static readonly Guid IidUISettings3 = new("03021be4-5254-4781-8194-5168f7d06d7b");

    // The IUISettings3* the UI thread activated, or null. Released by Close.
    private static void* _settings;
    private static void* _handler;
    private static long _token;

    public static bool Available => _settings is not null;

    /// <summary>Activates UISettings on the calling thread and logs each step.</summary>
    public static void Activate()
    {
        nint id = 0;
        int hr;
        fixed (char* name = ClassName)
        {
            hr = WindowsCreateString(name, (uint)ClassName.Length, &id);
        }

        Probe("uisettings.create_string", Hr(hr));
        if (hr < 0)
        {
            return;
        }

        void* inspectable = null;
        long start = Stopwatch.GetTimestamp();
        hr = RoActivateInstance(id, &inspectable);
        Probe("uisettings.ro_activate_instance", $"{Hr(hr)} in {Ms(start)}");
        _ = WindowsDeleteString(id);
        if (hr < 0 || inspectable is null)
        {
            return;
        }

        Probe("uisettings.runtime_class_name", RuntimeClassName(inspectable));
        void* settings3 = null;
        Guid iid = IidUISettings3;
        hr = QueryInterface(inspectable, &iid, &settings3);
        Probe("uisettings.query_iuisettings3", Hr(hr));
        _ = Release(inspectable);
        if (hr >= 0 && settings3 is not null)
        {
            _settings = settings3;
        }
    }

    /// <summary>One colour of <c>GetColorValue</c> as #AARRGGBB, or why it could not be read.</summary>
    public static string ColorText(int type) => ColorText(_settings, type);

    /// <summary>The same through another UISettings pointer (the sender of the event, on the event's thread).</summary>
    public static string ColorText(void* settings3, int type)
    {
        if (settings3 is null)
        {
            return "unavailable (UISettings not activated)";
        }

        int hr = GetColorValue(settings3, type, out WinRTColor color);
        return hr < 0 ? "FAILED " + Hr(hr) : Format(color);
    }

    public static bool TryGetColor(int type, out WinRTColor color)
    {
        color = default;
        return _settings is not null && GetColorValue(_settings, type, out color) >= 0;
    }

    public static string Format(WinRTColor c) => $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";

    /// <summary>Registers <see cref="ColorHandler"/> on <c>ColorValuesChanged</c> and logs the result.</summary>
    public static void Subscribe()
    {
        if (_settings is null)
        {
            Probe("uisettings.color_values_changed.add", "skipped (UISettings not activated)");
            return;
        }

        Probe("uisettings.color_values_changed.handler_iid", ColorHandler.Iid.ToString("B"));
        _handler = ColorHandler.Create();
        long token;
        int hr = ((delegate* unmanaged<void*, void*, long*, int>)Slot(_settings, 7))(_settings, _handler, &token);
        Probe("uisettings.color_values_changed.add", $"{Hr(hr)} token={token}");
        if (hr >= 0)
        {
            _token = token;
        }
        else
        {
            _handler = null;
        }
    }

    public static void Unsubscribe()
    {
        if (_settings is null || _handler is null)
        {
            return;
        }

        int hr = ((delegate* unmanaged<void*, long, int>)Slot(_settings, 8))(_settings, _token);
        Probe("uisettings.color_values_changed.remove", Hr(hr));
        _handler = null;
    }

    public static void Close()
    {
        Unsubscribe();
        if (_settings is not null)
        {
            _ = Release(_settings);
            _settings = null;
        }
    }

    /// <summary>Reads Foreground through the event's sender (an IUISettings*), queried for IUISettings3 on this thread.</summary>
    public static string ForegroundThroughSender(void* sender)
    {
        if (sender is null)
        {
            return "sender is null";
        }

        void* settings3 = null;
        Guid iid = IidUISettings3;
        int hr = QueryInterface(sender, &iid, &settings3);
        if (hr < 0 || settings3 is null)
        {
            return "query IUISettings3 FAILED " + Hr(hr);
        }

        string text = ColorText(settings3, Foreground);
        _ = Release(settings3);
        return text;
    }

    private static int GetColorValue(void* settings3, int type, out WinRTColor color)
    {
        WinRTColor value = default;
        int hr = ((delegate* unmanaged<void*, int, WinRTColor*, int>)Slot(settings3, 6))(settings3, type, &value);
        color = value;
        return hr;
    }

    /// <summary>IInspectable::GetRuntimeClassName (slot 4), to show which object RoActivateInstance gave.</summary>
    private static string RuntimeClassName(void* inspectable)
    {
        nint name = 0;
        int hr = ((delegate* unmanaged<void*, nint*, int>)Slot(inspectable, 4))(inspectable, &name);
        if (hr < 0)
        {
            return "FAILED " + Hr(hr);
        }

        uint length;
        char* buffer = WindowsGetStringRawBuffer(name, &length);
        string text = buffer is null ? string.Empty : new string(buffer, 0, (int)length);
        _ = WindowsDeleteString(name);
        return text;
    }
}
