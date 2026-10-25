using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;

namespace XylemUI.Rendering.D3D11;

/// <summary>
/// The textures of the layers of opacity groups (roadmap row R4c, design §5.0, "The API of layers, clips, transforms, and
/// DrawingContext"): premultiplied RGBA8 render targets, kept from frame to frame and given out again. A layer's size is rounded
/// up to <see cref="Granularity"/>, so that a group whose bounds change by a few pixels keeps its texture, and a layer takes the
/// smallest free texture it fits in. A layer holds its texture from its push to its pop: a texture given back with
/// <see cref="Return"/> is given out again in the same frame, after <see cref="Spacing"/> other layers, so a frame needs about as
/// many textures as layers are open at once (the depth of the nesting) and <see cref="Spacing"/> more, not one for each of its
/// layers. The pool holds at most <see cref="CapacityBytes"/>: to make room it
/// releases only textures the last frame did not use either, since a steady frame asks for the same layers again; a layer that
/// still does not fit, or whose texture the device has no memory for, is refused and counted. A texture no frame has used for
/// <see cref="KeptFrames"/> frames is released.
/// </summary>
/// <remarks>
/// A texture is larger than its layer by up to <see cref="Granularity"/> - 1 pixels on the right and at the bottom. Those texels
/// are cleared and never drawn, and the composite reads none of them: it loads one texel for each pixel of the layer's bounds,
/// with no filtering. A composite that samples with filtering must keep its texture coordinates inside the bounds.
/// </remarks>
internal sealed unsafe class LayerPool : IDisposable
{
    /// <summary>
    /// The step a layer's width and height are rounded up to, in pixels.
    /// </summary>
    public const int Granularity = 64;

    /// <summary>
    /// The number of frames a texture no frame uses is kept.
    /// </summary>
    public const int KeptFrames = 120;

    /// <summary>
    /// The number of layers given out after a texture is given back before the texture is given out again, when the pool can
    /// make another texture instead.
    /// </summary>
    /// <remarks>
    /// A layer that clears and draws the texture the composite before it has just read waits for that read: giving the texture
    /// out again at once made 50 sibling layers take 685 to 733 µs of GPU against 657 to 671 µs with a texture for each. With one
    /// layer between, two textures take turns and the 50 layers take 510 to 520 µs; two and four layers between were no faster
    /// (R4c, `frames --groups`, PR #264's review).
    /// </remarks>
    public const int Spacing = 1;

    private readonly List<Entry> _entries = [];
    private long _frame;
    private long _given;

    /// <summary>
    /// Makes a texture, as <c>ID3D11Device::CreateTexture2D</c> does.
    /// </summary>
    /// <param name="device">The device.</param>
    /// <param name="description">The texture's description.</param>
    /// <param name="texture">The texture made.</param>
    /// <returns>The HRESULT.</returns>
    internal delegate int TextureMaker(ID3D11Device* device, D3D11_TEXTURE2D_DESC* description, ID3D11Texture2D** texture);

    /// <summary>
    /// Initializes a new instance of the <see cref="LayerPool"/> class.
    /// </summary>
    /// <param name="capacityBytes">The most bytes the pool's textures may hold together.</param>
    public LayerPool(long capacityBytes) => CapacityBytes = capacityBytes;

    /// <summary>
    /// Gets or sets the most bytes the pool's textures may hold together; a smaller value takes effect when a texture is next
    /// needed.
    /// </summary>
    public long CapacityBytes { get; set; }

    /// <summary>
    /// Gets the bytes the pool's textures hold.
    /// </summary>
    public long Bytes { get; private set; }

    /// <summary>
    /// Gets the number of textures the pool holds.
    /// </summary>
    public int Count { get; private set; }

    /// <summary>
    /// Gets the number of layers refused because the pool was full or the device had no memory for their texture.
    /// </summary>
    public int RefusedCount { get; private set; }

    /// <summary>
    /// Gets or sets the function that makes a texture; tests replace it to fail as a device without memory does.
    /// </summary>
    internal TextureMaker MakeTexture { get; set; } = static (device, description, texture) => device->CreateTexture2D(description, null, texture);

    /// <summary>
    /// Starts a frame: the textures the last frame used can be given out again, and those unused for <see cref="KeptFrames"/>
    /// frames are released.
    /// </summary>
    public void BeginFrame()
    {
        _frame++;
        foreach (Entry entry in _entries)
        {
            entry.InUse = false;
            if (entry.Texture is not null && _frame - entry.UsedFrame > KeptFrames)
            {
                Release(entry);
            }
        }
    }

    /// <summary>
    /// Gives out a texture of at least <paramref name="width"/> by <paramref name="height"/> pixels, held until it is given back
    /// with <see cref="Return"/> or the frame ends.
    /// </summary>
    /// <param name="device">The device that makes a new texture.</param>
    /// <param name="width">The layer's width, in pixels.</param>
    /// <param name="height">The layer's height.</param>
    /// <returns>
    /// The texture's index, or −1 when the pool is full even after releasing the textures this frame does not use, or when the
    /// device has no memory for a new texture.
    /// </returns>
    /// <exception cref="GpuDeviceLostException">The device was lost while making the texture.</exception>
    /// <exception cref="InvalidOperationException">Making the texture failed for another reason than memory.</exception>
    public int Acquire(ID3D11Device* device, int width, int height)
    {
        int w = (width + Granularity - 1) / Granularity * Granularity;
        int h = (height + Granularity - 1) / Granularity * Granularity;
        _given++;
        int best = -1;
        int recent = -1;
        for (int i = 0; i < _entries.Count; i++)
        {
            Entry entry = _entries[i];
            if (entry.Texture is null || entry.InUse || entry.Width < w || entry.Height < h)
            {
                continue;
            }

            ref int pick = ref _given - entry.Returned > Spacing ? ref best : ref recent;
            if (pick < 0 || (long)entry.Width * entry.Height < (long)_entries[pick].Width * _entries[pick].Height)
            {
                pick = i;
            }
        }

        if (best >= 0)
        {
            return Give(best);
        }

        // Textures neither this frame nor the last used make room; released ones keep their place, so that the indices given out
        // this frame stay.
        long bytes = (long)w * h * 4;
        for (int i = _entries.Count - 1; i >= 0 && Bytes + bytes > CapacityBytes; i--)
        {
            if (_entries[i].Texture is not null && _entries[i].UsedFrame < _frame - 1)
            {
                Release(_entries[i]);
            }
        }

        if (Bytes + bytes > CapacityBytes)
        {
            return recent >= 0 ? Give(recent) : Refuse();
        }

        int free = _entries.FindIndex(static entry => entry.Texture is null);
        Entry made = free >= 0 ? _entries[free] : new Entry();
        (int result, string operation) = Create(device, made, w, h);
        if (result == E.E_OUTOFMEMORY)
        {
            // The device has no memory for the texture although the pool is under its limit: refused as when the pool is full
            // (design §5.0, "The API of layers, clips, transforms, and DrawingContext").
            return recent >= 0 ? Give(recent) : Refuse();
        }

        HResults.ThrowIfFailed(result, operation);
        if (free < 0)
        {
            _entries.Add(made);
            free = _entries.Count - 1;
        }

        Bytes += bytes;
        Count++;
        return free;
    }

    /// <summary>
    /// Gives back a texture given out this frame, so that a later layer of the frame can take it: the layer that held it was
    /// composited, and the draws that come after the composite may overwrite the texture.
    /// </summary>
    /// <param name="index">The texture's index.</param>
    public void Return(int index)
    {
        _entries[index].InUse = false;
        _entries[index].Returned = _given;
    }

    /// <summary>
    /// Returns the render target view of a texture given out this frame.
    /// </summary>
    /// <param name="index">The texture's index.</param>
    /// <returns>The view.</returns>
    public ID3D11RenderTargetView* Target(int index) => _entries[index].Target;

    /// <summary>
    /// Returns the shader resource view of a texture given out this frame.
    /// </summary>
    /// <param name="index">The texture's index.</param>
    /// <returns>The view.</returns>
    public ID3D11ShaderResourceView* View(int index) => _entries[index].View;

    /// <summary>
    /// Releases every texture.
    /// </summary>
    public void Dispose()
    {
        foreach (Entry entry in _entries)
        {
            if (entry.Texture is not null)
            {
                Release(entry);
            }
        }
    }

    private int Give(int index)
    {
        _entries[index].UsedFrame = _frame;
        _entries[index].InUse = true;
        return index;
    }

    private int Refuse()
    {
        RefusedCount++;
        return -1;
    }

    // Makes the texture and its views into `entry`. Returns the HRESULT of the first call that failed, with the call's name, and
    // leaves `entry` free when one failed.
    private (int Result, string Operation) Create(ID3D11Device* device, Entry entry, int width, int height)
    {
        var description = new D3D11_TEXTURE2D_DESC
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM,
            SampleDesc = new DXGI_SAMPLE_DESC(1, 0),
            Usage = D3D11_USAGE.D3D11_USAGE_DEFAULT,
            BindFlags = (uint)(D3D11_BIND_FLAG.D3D11_BIND_RENDER_TARGET | D3D11_BIND_FLAG.D3D11_BIND_SHADER_RESOURCE),
        };
        ID3D11Texture2D* texture = null;
        int result = MakeTexture(device, &description, &texture);
        if (result < 0)
        {
            return (result, "ID3D11Device::CreateTexture2D");
        }

        ID3D11RenderTargetView* target = null;
        result = device->CreateRenderTargetView((ID3D11Resource*)texture, null, &target);
        if (result < 0)
        {
            _ = texture->Release();
            return (result, "ID3D11Device::CreateRenderTargetView");
        }

        ID3D11ShaderResourceView* view = null;
        result = device->CreateShaderResourceView((ID3D11Resource*)texture, null, &view);
        if (result < 0)
        {
            _ = target->Release();
            _ = texture->Release();
            return (result, "ID3D11Device::CreateShaderResourceView");
        }

        entry.Texture = texture;
        entry.Target = target;
        entry.View = view;
        entry.Width = width;
        entry.Height = height;
        entry.UsedFrame = _frame;
        entry.InUse = true;
        return (0, string.Empty);
    }

    private void Release(Entry entry)
    {
        _ = entry.View->Release();
        _ = entry.Target->Release();
        _ = entry.Texture->Release();
        entry.View = null;
        entry.Target = null;
        entry.Texture = null;
        Bytes -= (long)entry.Width * entry.Height * 4;
        Count--;
    }

    // One texture, or a free place when Texture is null: its views, its size, the last frame that used it, and whether a layer
    // holds it now.
    private sealed class Entry
    {
        public ID3D11Texture2D* Texture;
        public ID3D11RenderTargetView* Target;
        public ID3D11ShaderResourceView* View;
        public int Width;
        public int Height;
        public long UsedFrame;
        public bool InUse;
        public long Returned = long.MinValue / 2;
    }
}
