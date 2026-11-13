using System.Runtime.InteropServices;

namespace XylemUI.Desktop.Tests;

/// <summary>
/// A COM UI Automation client for the properties UIAutomationClient's managed classes do not read (AriaProperties, ControllerFor,
/// LiveSetting, FullDescription, Level): <c>CUIAutomation8</c> called by its vtable slots, as the Windows SDK's
/// <c>UIAutomationClient.h</c> (10.0.26100.0) lays them out. It runs on the thread that made it, an MTA thread of the test's client.
/// </summary>
internal sealed unsafe partial class UiaComClient : IDisposable
{
    // IUIAutomation (UIAutomationClient.h, IUIAutomationVtbl): ElementFromHandle is slot 6, CreatePropertyCondition slot 23.
    private const int ElementFromHandleSlot = 6;
    private const int CreatePropertyConditionSlot = 23;

    // IUIAutomationElement (IUIAutomationElementVtbl): FindFirst is slot 5, GetCurrentPropertyValue slot 10, get_CurrentName slot 23.
    private const int FindFirstSlot = 5;
    private const int GetCurrentPropertyValueSlot = 10;
    private const int CurrentNameSlot = 23;

    // IUIAutomationElementArray (IUIAutomationElementArrayVtbl): get_Length is slot 3, GetElement slot 4.
    private const int LengthSlot = 3;
    private const int GetElementSlot = 4;

    // IUnknown.
    private const int QueryInterfaceSlot = 0;
    private const int ReleaseSlot = 2;

    // TreeScope_Descendants, CLSCTX_INPROC_SERVER, COINIT_MULTITHREADED, UIA_NamePropertyId, UIA_AutomationIdPropertyId.
    private const int TreeScopeDescendants = 4;
    private const uint ClsctxInprocServer = 1;
    private const uint CoinitMultithreaded = 0;
    private const int NamePropertyId = 30005;
    private const int AutomationIdPropertyId = 30011;

    // The VARIANT types the client reads (wtypes.h).
    private const ushort VtEmpty = 0;
    private const ushort VtI4 = 3;
    private const ushort VtR8 = 5;
    private const ushort VtBstr = 8;
    private const ushort VtBool = 11;
    private const ushort VtUnknown = 13;
    private const ushort VtArray = 0x2000;

    // CLSID_CUIAutomation8, IID_IUIAutomation, and IID_IUIAutomationElement (UIAutomationClient.h).
    private static readonly Guid ClsidCuiAutomation8 = new("e22ad333-b25f-460c-83d0-0581107395c9");
    private static readonly Guid IidIuiAutomation = new("30cbe57d-d9d0-452a-ab13-7ac5ac4825ee");
    private static readonly Guid IidIuiAutomationElement = new("d22108aa-8ac5-49a5-837b-37bbb3d7591e");
    private static readonly Guid IidIuiAutomationElementArray = new("14314595-b4bc-4055-95f2-58f2e42c9855");

    private readonly bool _initialized;
    private void* _automation;
    private void* _root;

    /// <summary>
    /// Initializes a new instance of the <see cref="UiaComClient"/> class: the element of a window.
    /// </summary>
    /// <param name="hwnd">The window.</param>
    public UiaComClient(nint hwnd)
    {
        int initialized = CoInitializeEx(null, CoinitMultithreaded);
        _initialized = initialized >= 0;
        Guid clsid = ClsidCuiAutomation8;
        Guid iid = IidIuiAutomation;
        void* automation;
        Check(CoCreateInstance(&clsid, null, ClsctxInprocServer, &iid, &automation));
        _automation = automation;
        void* root;
        Check(((delegate* unmanaged<void*, nint, void**, int>)Slot(_automation, ElementFromHandleSlot))(_automation, hwnd, &root));
        _root = root;
    }

    /// <summary>
    /// Reads a property of the first descendant with a name, by the property's id.
    /// </summary>
    /// <param name="name">The descendant's name.</param>
    /// <param name="propertyId">The property's id (UIAutomationClient.h).</param>
    /// <returns>The value: null, an int, a double, a bool, a string, or the names of an array of elements.</returns>
    public object? ReadByName(string name, int propertyId) => Read(NamePropertyId, name, propertyId);

    /// <summary>
    /// Reads a property of the first descendant with an automation ID, by the property's id.
    /// </summary>
    /// <param name="automationId">The descendant's automation ID.</param>
    /// <param name="propertyId">The property's id.</param>
    /// <returns>The value, as <see cref="ReadByName"/> gives it.</returns>
    public object? ReadById(string automationId, int propertyId) => Read(AutomationIdPropertyId, automationId, propertyId);

    /// <summary>
    /// Releases the elements and the client.
    /// </summary>
    public void Dispose()
    {
        Release(_root);
        _root = null;
        Release(_automation);
        _automation = null;
        if (_initialized)
        {
            CoUninitialize();
        }
    }

    private object? Read(int keyId, string key, int propertyId)
    {
        void* element = Find(keyId, key);
        try
        {
            Variant value;
            Check(((delegate* unmanaged<void*, int, Variant*, int>)Slot(element, GetCurrentPropertyValueSlot))(element, propertyId, &value));
            try
            {
                return ValueOf(value);
            }
            finally
            {
                _ = VariantClear(&value);
            }
        }
        finally
        {
            Release(element);
        }
    }

    // The first descendant of the window whose property of an id has a text.
    private void* Find(int keyId, string key)
    {
        var match = new Variant { Vt = VtBstr, Pointer = Marshal.StringToBSTR(key) };
        void* condition = null;
        try
        {
            Check(
                ((delegate* unmanaged<void*, int, Variant, void**, int>)Slot(_automation, CreatePropertyConditionSlot))(
                    _automation,
                    keyId,
                    match,
                    &condition
                )
            );
            void* found;
            Check(((delegate* unmanaged<void*, int, void*, void**, int>)Slot(_root, FindFirstSlot))(_root, TreeScopeDescendants, condition, &found));
            return found != null ? found : throw new InvalidOperationException($"UIA found no element whose property {keyId} is \"{key}\".");
        }
        finally
        {
            Release(condition);
            Marshal.FreeBSTR(match.Pointer);
        }
    }

    private static object? ValueOf(Variant value) =>
        value.Vt switch
        {
            VtEmpty => null,
            VtI4 => value.Int,
            VtR8 => value.Double,
            VtBool => value.Bool != 0,
            VtBstr => Marshal.PtrToStringBSTR(value.Pointer),
            VtUnknown => NamesOfElementArray((void*)value.Pointer),
            VtUnknown | VtArray => NamesOf((void*)value.Pointer),
            _ => throw new InvalidOperationException($"The client reads no VARIANT of the type {value.Vt}."),
        };

    // The names of the elements of an IUnknown that is an IUIAutomationElementArray (ControllerFor comes as one), in order.
    private static string[] NamesOfElementArray(void* unknown)
    {
        Guid iid = IidIuiAutomationElementArray;
        void* array;
        if (((delegate* unmanaged<void*, Guid*, void**, int>)Slot(unknown, QueryInterfaceSlot))(unknown, &iid, &array) < 0)
        {
            throw new InvalidOperationException("The VT_UNKNOWN value is not an IUIAutomationElementArray.");
        }

        try
        {
            int length;
            Check(((delegate* unmanaged<void*, int*, int>)Slot(array, LengthSlot))(array, &length));
            var names = new string[length];
            for (int i = 0; i < length; i++)
            {
                void* element;
                Check(((delegate* unmanaged<void*, int, void**, int>)Slot(array, GetElementSlot))(array, i, &element));
                try
                {
                    names[i] = NameOf(element);
                }
                finally
                {
                    Release(element);
                }
            }

            return names;
        }
        finally
        {
            Release(array);
        }
    }

    private static string NameOf(void* element)
    {
        nint name;
        Check(((delegate* unmanaged<void*, nint*, int>)Slot(element, CurrentNameSlot))(element, &name));
        try
        {
            return Marshal.PtrToStringBSTR(name);
        }
        finally
        {
            Marshal.FreeBSTR(name);
        }
    }

    // The names of a SAFEARRAY of IUnknown that are elements, in order.
    private static string[] NamesOf(void* array)
    {
        int lower;
        int upper;
        Check(SafeArrayGetLBound(array, 1, &lower));
        Check(SafeArrayGetUBound(array, 1, &upper));
        var names = new List<string>();
        for (int i = lower; i <= upper; i++)
        {
            void* unknown;
            Check(SafeArrayGetElement(array, &i, &unknown));
            try
            {
                Guid iid = IidIuiAutomationElement;
                void* element;
                Check(((delegate* unmanaged<void*, Guid*, void**, int>)Slot(unknown, QueryInterfaceSlot))(unknown, &iid, &element));
                try
                {
                    names.Add(NameOf(element));
                }
                finally
                {
                    Release(element);
                }
            }
            finally
            {
                Release(unknown);
            }
        }

        return [.. names];
    }

    private static void* Slot(void* instance, int slot) => (*(void***)instance)[slot];

    private static void Release(void* instance)
    {
        if (instance != null)
        {
            _ = ((delegate* unmanaged<void*, uint>)Slot(instance, ReleaseSlot))(instance);
        }
    }

    private static void Check(int result) => Marshal.ThrowExceptionForHR(result);

    [LibraryImport("ole32.dll")]
    private static partial int CoInitializeEx(void* reserved, uint coInit);

    [LibraryImport("ole32.dll")]
    private static partial void CoUninitialize();

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(Guid* clsid, void* outer, uint context, Guid* iid, void** instance);

    [LibraryImport("oleaut32.dll")]
    private static partial int VariantClear(Variant* value);

    [LibraryImport("oleaut32.dll")]
    private static partial int SafeArrayGetLBound(void* array, uint dimension, int* bound);

    [LibraryImport("oleaut32.dll")]
    private static partial int SafeArrayGetUBound(void* array, uint dimension, int* bound);

    [LibraryImport("oleaut32.dll")]
    private static partial int SafeArrayGetElement(void* array, int* indices, void* value);

    // A VARIANT (oaidl.h): its type at 0, three reserved words, and the value at 8; 24 bytes on x64.
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct Variant
    {
        [FieldOffset(0)]
        public ushort Vt;

        [FieldOffset(8)]
        public nint Pointer;

        [FieldOffset(8)]
        public int Int;

        [FieldOffset(8)]
        public double Double;

        [FieldOffset(8)]
        public short Bool;
    }
}
