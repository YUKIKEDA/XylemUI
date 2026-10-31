using System.Runtime.InteropServices;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace XylemUI.Platform.Windows;

/// <summary>
/// A field's kind of input as TSF reads it (design §4.7, "The kinds of input"): an <c>ITfInputScope</c> with one input scope,
/// which the document gives for the attribute <c>GUID_PROP_INPUTSCOPE</c>, as Chromium does (<c>tsf_text_store.cc</c> lines 884 to
/// 950, 86c7d7a6). A hand-written vtable (Issue #535); the reference the document hands over is the caller's.
/// </summary>
internal unsafe struct TsfInputScope
{
    /// <summary>
    /// The attribute of the input scope (InputScope.h).
    /// </summary>
    public static readonly Guid Attribute = new(0x1713dd5a, 0x68e7, 0x4a5b, 0x9a, 0xf6, 0x59, 0x2a, 0x59, 0x5c, 0x77, 0x8d);

    private static readonly void** Table = CreateTable();

    private void** _table;
    private int _references;
    private InputScope _scope;

    /// <summary>
    /// Allocates an input scope with one reference, its caller's.
    /// </summary>
    /// <param name="kind">The field's kind of input.</param>
    /// <returns>The input scope, as TSF takes it.</returns>
    public static IUnknown* Create(TextInputKind kind)
    {
        var scope = (TsfInputScope*)NativeMemory.AllocZeroed((nuint)sizeof(TsfInputScope));
        scope->_table = Table;
        scope->_references = 1;
        scope->_scope = ScopeOf(kind);
        return (IUnknown*)scope;
    }

    /// <summary>
    /// Returns the input scope of a kind of input (InputScope.h).
    /// </summary>
    /// <param name="kind">The kind.</param>
    /// <returns>The input scope.</returns>
    public static InputScope ScopeOf(TextInputKind kind) =>
        kind switch
        {
            TextInputKind.Url => InputScope.IS_URL,
            TextInputKind.Email => InputScope.IS_EMAIL_SMTPEMAILADDRESS,
            TextInputKind.Number => InputScope.IS_NUMBER,
            TextInputKind.Telephone => InputScope.IS_TELEPHONE_FULLTELEPHONENUMBER,
            TextInputKind.Password => InputScope.IS_PASSWORD,
            _ => InputScope.IS_DEFAULT,
        };

    private static void** CreateTable()
    {
        // IUnknown, then ITfInputScope in InputScope.idl. One table for the process, never freed.
        void** table = (void**)NativeMemory.Alloc((nuint)(8 * sizeof(void*)));
        table[0] = (delegate* unmanaged<TsfInputScope*, Guid*, void**, int>)&QueryInterface;
        table[1] = (delegate* unmanaged<TsfInputScope*, uint>)&AddRef;
        table[2] = (delegate* unmanaged<TsfInputScope*, uint>)&Release;
        table[3] = (delegate* unmanaged<TsfInputScope*, InputScope**, uint*, int>)&GetInputScopes;
        table[4] = (delegate* unmanaged<TsfInputScope*, void**, uint*, int>)&GetPhrase;
        table[5] = (delegate* unmanaged<TsfInputScope*, void**, int>)&GetString;
        table[6] = (delegate* unmanaged<TsfInputScope*, void**, int>)&GetString;
        table[7] = (delegate* unmanaged<TsfInputScope*, void**, int>)&GetString;
        return table;
    }

    // None of these calls anything that throws, so they have no catch.
    [UnmanagedCallersOnly]
    private static int QueryInterface(TsfInputScope* self, Guid* iid, void** result)
    {
        if (*iid == IID.IID_IUnknown || *iid == __uuidof<ITfInputScope>())
        {
            *result = self;
            _ = Interlocked.Increment(ref self->_references);
            return S.S_OK;
        }

        *result = null;
        return E.E_NOINTERFACE;
    }

    [UnmanagedCallersOnly]
    private static uint AddRef(TsfInputScope* self) => (uint)Interlocked.Increment(ref self->_references);

    [UnmanagedCallersOnly]
    private static uint Release(TsfInputScope* self)
    {
        int left = Interlocked.Decrement(ref self->_references);
        if (left == 0)
        {
            NativeMemory.Free(self);
        }

        return (uint)left;
    }

    // The caller frees the array with CoTaskMemFree.
    [UnmanagedCallersOnly]
    private static int GetInputScopes(TsfInputScope* self, InputScope** scopes, uint* count)
    {
        var array = (InputScope*)CoTaskMemAlloc((nuint)sizeof(InputScope));
        if (array is null)
        {
            *scopes = null;
            *count = 0;
            return E.E_OUTOFMEMORY;
        }

        *array = self->_scope;
        *scopes = array;
        *count = 1;
        return S.S_OK;
    }

    [UnmanagedCallersOnly]
    private static int GetPhrase(TsfInputScope* self, void** phrases, uint* count)
    {
        *phrases = null;
        *count = 0;
        return E.E_NOTIMPL;
    }

    // GetRegularExpression, GetSRGS, and GetXML: the field has none.
    [UnmanagedCallersOnly]
    private static int GetString(TsfInputScope* self, void** value)
    {
        *value = null;
        return E.E_NOTIMPL;
    }
}
