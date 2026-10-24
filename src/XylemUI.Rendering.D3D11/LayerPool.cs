using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;

namespace XylemUI.Rendering.D3D11;

/// <summary>
/// The textures of the layers of opacity groups (roadmap row R4c, design §5.0, "The API of layers, clips, transforms, and
/// DrawingContext"): premultiplied RGBA8 render targets, kept from frame to frame and given out again. A layer's size is rounded
/// up to <see cref="Granularity"/>, so that a group whose bounds change by a few pixels keeps its texture, and a layer takes the
/// smallest free texture it fits in. Each layer of a frame has a texture of its own. The pool holds at most
/// <see cref="CapacityBytes"/>: to make room it releases only textures the last frame did not use either, since a steady frame
/// asks for the same layers again; a layer that still does not fit is refused and counted. A texture no frame has used for
/// <see cref="KeptFrames"/> frames is released.
/// </summary>
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

    private readonly List<Entry> _entries = [];
    private long _frame;

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
    /// Gets the number of layers refused because the pool was full.
    /// </summary>
    public int RefusedCount { get; private set; }

    /// <summary>
    /// Starts a frame: the textures the last frame used can be given out again, and those unused for <see cref="KeptFrames"/>
    /// frames are released.
    /// </summary>
    public void BeginFrame()
    {
        _frame++;
        foreach (Entry entry in _entries)
        {
            if (entry.Texture is not null && _frame - entry.UsedFrame > KeptFrames)
            {
                Release(entry);
            }
        }
    }

    /// <summary>
    /// Gives out a texture of at least <paramref name="width"/> by <paramref name="height"/> pixels for this frame.
    /// </summary>
    /// <param name="device">The device that makes a new texture.</param>
    /// <param name="width">The layer's width, in pixels.</param>
    /// <param name="height">The layer's height.</param>
    /// <returns>The texture's index, or −1 when the pool is full even after releasing the textures this frame does not use.</returns>
    public int Acquire(ID3D11Device* device, int width, int height)
    {
        int w = (width + Granularity - 1) / Granularity * Granularity;
        int h = (height + Granularity - 1) / Granularity * Granularity;
        int best = -1;
        for (int i = 0; i < _entries.Count; i++)
        {
            Entry entry = _entries[i];
            bool fits = entry.Texture is not null && entry.UsedFrame != _frame && entry.Width >= w && entry.Height >= h;
            if (fits && (best < 0 || (long)entry.Width * entry.Height < (long)_entries[best].Width * _entries[best].Height))
            {
                best = i;
            }
        }

        if (best >= 0)
        {
            _entries[best].UsedFrame = _frame;
            return best;
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
            RefusedCount++;
            return -1;
        }

        int free = _entries.FindIndex(static entry => entry.Texture is null);
        Entry made = free >= 0 ? _entries[free] : new Entry();
        Create(device, made, w, h);
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

    private void Create(ID3D11Device* device, Entry entry, int width, int height)
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
        ID3D11Texture2D* texture;
        HResults.ThrowIfFailed(device->CreateTexture2D(&description, null, &texture), "ID3D11Device::CreateTexture2D");
        ID3D11RenderTargetView* target = null;
        ID3D11ShaderResourceView* view = null;
        try
        {
            HResults.ThrowIfFailed(device->CreateRenderTargetView((ID3D11Resource*)texture, null, &target), "ID3D11Device::CreateRenderTargetView");
            HResults.ThrowIfFailed(device->CreateShaderResourceView((ID3D11Resource*)texture, null, &view), "ID3D11Device::CreateShaderResourceView");
        }
        catch
        {
            if (target is not null)
            {
                _ = target->Release();
            }

            _ = texture->Release();
            throw;
        }

        entry.Texture = texture;
        entry.Target = target;
        entry.View = view;
        entry.Width = width;
        entry.Height = height;
        entry.UsedFrame = _frame;
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

    // One texture, or a free place when Texture is null: its views, its size, and the last frame that used it.
    private sealed class Entry
    {
        public ID3D11Texture2D* Texture;
        public ID3D11RenderTargetView* Target;
        public ID3D11ShaderResourceView* View;
        public int Width;
        public int Height;
        public long UsedFrame;
    }
}
