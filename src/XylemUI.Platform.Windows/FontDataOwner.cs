using System.Runtime.InteropServices;

namespace XylemUI.Platform.Windows;

/// <summary>
/// The owner object handed to <c>IDWriteInMemoryFontFileLoader::CreateInMemoryFontFileReference</c>, so that DirectWrite reads a
/// font's bytes in place instead of copying them (it copies when the owner is null). The bytes belong to a <c>Font</c>, which
/// keeps them pinned or mapped until it is disposed (<c>Font.Data</c>), so the owner holds nothing: it only answers
/// <c>IUnknown</c>.
/// </summary>
/// <remarks>
/// One object for the process, never freed: DirectWrite's shared factory may keep a font file, and with it a reference to the
/// owner, after the rasterizer that made it is gone. The callbacks only read and write through their pointers and call nothing
/// that can throw (TerraFX's IID, <see cref="Guid"/>'s equality), so no managed exception can leave them and they have no
/// <c>catch</c> (.cursor/rules/interop.mdc).
/// </remarks>
internal unsafe struct FontDataOwner
{
    private void** _vtable;

    /// <summary>
    /// Gets the owner.
    /// </summary>
    public static TerraFX.Interop.Windows.IUnknown* Instance { get; } = Create();

    private static TerraFX.Interop.Windows.IUnknown* Create()
    {
        // The order of IUnknown. One table and one object for the process, never freed.
        void** table = (void**)NativeMemory.Alloc((nuint)(3 * sizeof(void*)));
        table[0] = (delegate* unmanaged<FontDataOwner*, Guid*, void**, int>)&QueryInterface;
        table[1] = (delegate* unmanaged<FontDataOwner*, uint>)&AddRef;
        table[2] = (delegate* unmanaged<FontDataOwner*, uint>)&Release;
        var owner = (FontDataOwner*)NativeMemory.Alloc((nuint)sizeof(FontDataOwner));
        owner->_vtable = table;
        return (TerraFX.Interop.Windows.IUnknown*)owner;
    }

    [UnmanagedCallersOnly]
    private static int QueryInterface(FontDataOwner* self, Guid* iid, void** result)
    {
        if (*iid == TerraFX.Interop.Windows.IID.IID_IUnknown)
        {
            *result = self;
            return 0;
        }

        *result = null;
        return unchecked((int)0x80004002); // E_NOINTERFACE
    }

    [UnmanagedCallersOnly]
    private static uint AddRef(FontDataOwner* self) => 1;

    [UnmanagedCallersOnly]
    private static uint Release(FontDataOwner* self) => 1;
}
