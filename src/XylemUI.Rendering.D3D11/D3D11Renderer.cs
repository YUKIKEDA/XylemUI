using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;
using XylemUI.Text;
using static TerraFX.Interop.DirectX.D3D_DRIVER_TYPE;
using static TerraFX.Interop.DirectX.D3D_FEATURE_LEVEL;
using static TerraFX.Interop.DirectX.DirectX;
using static TerraFX.Interop.Windows.Windows;

namespace XylemUI.Rendering.D3D11;

/// <summary>
/// Draws a <see cref="DisplayList"/> into a window with Direct3D 11, through the COM vtables directly (design §5.2).
/// </summary>
/// <remarks>
/// <para>
/// The swap chain is flip-model (<c>FLIP_DISCARD</c>, two buffers) with a frame latency waitable object and a maximum latency of
/// one frame. The platform's message loop waits on <see cref="FrameLatencyWaitableObject"/> together with the message queue,
/// and calls <see cref="Render"/> when it is signaled (design §2.11).
/// </para>
/// <para>
/// COM references are released here, in order, never by the GC (design §2.9). <see cref="Resize"/> releases the render target
/// view before <c>ResizeBuffers</c> and creates it again after (design §2.11.1). All members belong to the UI thread.
/// </para>
/// <para>
/// The pages of the glyph atlas are the slices of three texture arrays: 8-bit grayscale coverage, RGBA ClearType coverage
/// (design §5.0.3, "ClearType"), and premultiplied BGRA color glyphs ("Color glyphs"). Rectangles, grayscale glyphs, and color
/// glyphs share one blend state, ClearType glyphs have the dual-source one, and the frame is drawn in the display list's order, split into draws only where the blend state changes. Before the
/// instances are written, the atlas's changes are uploaded (design §5.0.3): a page whose id changed is uploaded whole, the other
/// pages only in their dirty regions. Direct3D 11 orders these writes after the draws already issued that read the texture, so no
/// fence is needed (design §5.0.2).
/// </para>
/// <para>
/// The gamma, the contrast, and the order of the subpixels come from <see cref="TextRendering"/>, which the platform reads for
/// the window's monitor; the kind of coverage of each glyph run comes from the display list.
/// </para>
/// </remarks>
public sealed unsafe class D3D11Renderer : IDisposable
{
    // The rows of one command in the shape buffer, and the kinds QuadShaders' ShapeColor tells apart (row R4). Brushed text's rows
    // have the kind of text, which only the text shaders read.
    private const int ShapeRows = 6;
    private const float ShapeKind = 1;
    private const float OuterShadowKind = 2;
    private const float InsetShadowKind = 3;
    private const float LineKind = 4;
    private const float FillEllipseKind = 5;
    private const float StrokeEllipseKind = 6;
    private const float ArcKind = 7;
    private const float SectorKind = 8;
    private const float TextKind = 9;

    // The period of a line without dashes: one dash as long as the line, and a gap no frame reaches.
    private const float NoDashGap = 1e9f;

    private const uint SwapChainFlags = (uint)DXGI_SWAP_CHAIN_FLAG.DXGI_SWAP_CHAIN_FLAG_FRAME_LATENCY_WAITABLE_OBJECT;
    private const DXGI_FORMAT BackBufferFormat = DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM;

    private static readonly Color Opaque = Color.FromRgb(0xFFFFFF);

    private readonly ID3D11Device* _device;
    private readonly ID3D11DeviceContext* _context;
    private readonly IDXGISwapChain2* _swapChain;
    private readonly ID3D11VertexShader* _vertexShader;
    private readonly ID3D11PixelShader* _pixelShader;
    private readonly ID3D11PixelShader* _clearTypePixelShader;
    private readonly ID3D11PixelShader* _curvePixelShader;
    private readonly ID3D11PixelShader* _textPixelShader;
    private readonly ID3D11PixelShader* _clearTypeTextPixelShader;
    private readonly ID3D11PixelShader* _layerPixelShader;
    private readonly ID3D11PixelShader* _fadedPixelShader;
    private readonly ID3D11PixelShader* _fadedCurvePixelShader;
    private readonly ID3D11PixelShader* _fadedTextPixelShader;

    private readonly ID3D11InputLayout* _inputLayout;
    private readonly ID3D11Buffer* _targetConstants;
    private readonly ID3D11BlendState* _blend;
    private readonly ID3D11BlendState* _clearTypeBlend;
    private readonly HANDLE _frameLatencyWaitable;
    private readonly AtlasTextures _grayAtlas = new(GlyphCoverage.Gray, DXGI_FORMAT.DXGI_FORMAT_R8_UNORM);
    private readonly AtlasTextures _rgbAtlas = new(GlyphCoverage.Rgb, DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM);
    private readonly AtlasTextures _colorAtlas = new(GlyphCoverage.Color, DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM);

    // The layers of opacity groups (row R4c). 32 MB holds two layers of a 2,560 x 1,440 window.
    private readonly LayerPool _layers = new(32L << 20);

    private TextRenderingParameters _textRendering = TextRenderingParameters.Default;

    // The groups open while the instances are written, and the layers open while the draws run, with their bounds (row R4c).
    private GroupState[] _groupStates = new GroupState[8];
    private Vector4[] _targetStack = new Vector4[8];
    private int[] _targetLayers = new int[8];
    private DrawRange[] _draws = new DrawRange[16];

    private ID3D11RenderTargetView* _renderTarget;
    private ID3D11Buffer* _instances;
    private int _instanceCapacity;
    private RowBuffer _shapeRows;
    private RowBuffer _brushRows;
    private int[] _brushStarts = new int[16];
    private GpuFrameTimer? _gpuTimer;
    private (uint Count, long Qpc)[] _presents = [];
    private uint _lastReportedPresent;
    private bool _disposed;

    private D3D11Renderer(ID3D11Device* device, ID3D11DeviceContext* context, bool isWarp, HWND window, int width, int height)
    {
        _device = device;
        _context = context;
        IsWarp = isWarp;
        FeatureLevel = device->GetFeatureLevel();
        Width = width;
        Height = height;

        // A failure part way (a swap chain the window already has, a shader that does not compile) releases what was made, so
        // that the device is not leaked and the window can get a swap chain again.
        try
        {
            _swapChain = CreateSwapChain(device, window, width, height);
            _ = _swapChain->SetMaximumFrameLatency(1);
            _frameLatencyWaitable = _swapChain->GetFrameLatencyWaitableObject();
            CreateRenderTarget();
            _vertexShader = CreateVertexShader(device, out _inputLayout);
            _pixelShader = CreatePixelShader(device, QuadShaders.PixelEntry);
            _clearTypePixelShader = CreatePixelShader(device, QuadShaders.ClearTypePixelEntry);
            _curvePixelShader = CreatePixelShader(device, QuadShaders.CurvePixelEntry);
            _textPixelShader = CreatePixelShader(device, QuadShaders.TextPixelEntry);
            _clearTypeTextPixelShader = CreatePixelShader(device, QuadShaders.ClearTypeTextPixelEntry);
            _layerPixelShader = CreatePixelShader(device, QuadShaders.LayerPixelEntry);
            _fadedPixelShader = CreatePixelShader(device, QuadShaders.PixelEntry, faded: true);
            _fadedCurvePixelShader = CreatePixelShader(device, QuadShaders.CurvePixelEntry, faded: true);
            _fadedTextPixelShader = CreatePixelShader(device, QuadShaders.TextPixelEntry, faded: true);
            _targetConstants = CreateBuffer(device, 48, D3D11_BIND_FLAG.D3D11_BIND_CONSTANT_BUFFER);
            _blend = CreateBlendState(device);
            _clearTypeBlend = CreateClearTypeBlendState(device);
            WriteTargetConstants();
            GrowInstances(256);
            _shapeRows = RowBuffer.Create(device, 64);
            _brushRows = RowBuffer.Create(device, 64);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>
    /// Gets a value indicating whether the device is the WARP software rasterizer (no hardware device, or asked for).
    /// </summary>
    public bool IsWarp { get; }

    /// <summary>
    /// Gets or sets how text is drawn: the gamma and contrast of the correction and the order of the subpixels (design §5.0.3).
    /// Set it from the window (<c>Win32Window.TextRendering</c>) when the renderer is made and whenever the window's value changes;
    /// until then it is <see cref="TextRenderingParameters.Default"/>. Its <see cref="TextRenderingParameters.Coverage"/> is not
    /// read here: each glyph run carries its own.
    /// </summary>
    /// <exception cref="InvalidOperationException">The calling thread is not the UI thread.</exception>
    public TextRenderingParameters TextRendering
    {
        get => _textRendering;
        set => SetTextRendering(value);
    }

    /// <summary>
    /// Gets the feature level of the device.
    /// </summary>
    internal D3D_FEATURE_LEVEL FeatureLevel { get; }

    /// <summary>
    /// Gets the number of draws of the last <see cref="Draw"/>, for the tests of where the frame is split.
    /// </summary>
    internal int LastDrawCount { get; private set; }

    /// <summary>
    /// Gets the number of layer textures the renderer holds (row R4c).
    /// </summary>
    internal int LayerCount => _layers.Count;

    /// <summary>
    /// Gets the bytes the layer textures hold.
    /// </summary>
    internal long LayerBytes => _layers.Bytes;

    /// <summary>
    /// Gets the number of layers refused because the pool of layer textures was full; their children took the group's alpha.
    /// </summary>
    internal int LayersRefused => _layers.RefusedCount;

    /// <summary>
    /// Gets or sets the most bytes the layer textures may hold together.
    /// </summary>
    internal long LayerCapacityBytes
    {
        get => _layers.CapacityBytes;
        set => _layers.CapacityBytes = value;
    }

    /// <summary>
    /// Gets the width of the back buffer, in physical pixels.
    /// </summary>
    public int Width { get; private set; }

    /// <summary>
    /// Gets the height of the back buffer, in physical pixels.
    /// </summary>
    public int Height { get; private set; }

    /// <summary>
    /// Gets the swap chain's frame latency waitable object. It is signaled when the swap chain can take the next frame; the
    /// platform waits on it before it renders. The renderer owns and closes it.
    /// </summary>
    public nint FrameLatencyWaitableObject => _frameLatencyWaitable;

    /// <summary>
    /// Gets the swap chain, for the tests of the resize order.
    /// </summary>
    internal IDXGISwapChain2* SwapChain => _swapChain;

    /// <summary>
    /// Gets the render target view, for the tests of the resize; null after a resize that failed.
    /// </summary>
    internal ID3D11RenderTargetView* RenderTargetView => _renderTarget;

    /// <summary>
    /// Gets a value indicating whether frames are measured (see <see cref="StartMeasuringFrames"/>).
    /// </summary>
    internal bool MeasuresFrames => _gpuTimer is not null;

    /// <summary>
    /// Gets the number of frames the GPU timer did not measure because it was still waiting for older ones.
    /// </summary>
    internal int GpuFramesSkipped => _gpuTimer?.Skipped ?? 0;

    /// <summary>
    /// Gets the number of slices of the grayscale atlas texture array, for the tests of the atlas upload; 0 before the first glyph.
    /// </summary>
    internal int AtlasSlices => _grayAtlas.Slices;

    /// <summary>
    /// Gets the number of slices of the ClearType atlas texture array; 0 before the first ClearType glyph.
    /// </summary>
    internal int ClearTypeAtlasSlices => _rgbAtlas.Slices;

    /// <summary>
    /// Gets the number of slices of the color atlas texture array; 0 before the first color glyph.
    /// </summary>
    internal int ColorAtlasSlices => _colorAtlas.Slices;

    /// <summary>
    /// Gets the number of whole pages uploaded so far, of both kinds, for the tests of the atlas upload.
    /// </summary>
    internal int AtlasPageUploads => _grayAtlas.PageUploads + _rgbAtlas.PageUploads + _colorAtlas.PageUploads;

    /// <summary>
    /// Gets the number of dirty regions uploaded so far, of both kinds, for the tests of the atlas upload.
    /// </summary>
    internal int AtlasRegionUploads => _grayAtlas.RegionUploads + _rgbAtlas.RegionUploads + _colorAtlas.RegionUploads;

    /// <summary>
    /// Creates a renderer for the client area of <paramref name="window"/>.
    /// </summary>
    /// <param name="window">The window handle (<c>HWND</c>).</param>
    /// <param name="width">The client width, in physical pixels.</param>
    /// <param name="height">The client height, in physical pixels.</param>
    /// <param name="useWarp"><see langword="true"/> to use the WARP software rasterizer even when a GPU is present.</param>
    /// <returns>The renderer.</returns>
    /// <exception cref="InvalidOperationException">The calling thread is not the UI thread.</exception>
    /// <exception cref="InvalidOperationException">Direct3D could not create the device, the swap chain, or the shaders.</exception>
    public static D3D11Renderer Create(nint window, int width, int height, bool useWarp = false) =>
        Create(window, width, height, useWarp, featureLevel10: false);

    /// <summary>
    /// Creates a renderer, on a device of feature level 10.0 when <paramref name="featureLevel10"/> is set: the lowest level the
    /// renderer supports, for the tests that its shaders and blend states work there.
    /// </summary>
    /// <param name="window">The window handle (<c>HWND</c>).</param>
    /// <param name="width">The client width, in physical pixels.</param>
    /// <param name="height">The client height, in physical pixels.</param>
    /// <param name="useWarp"><see langword="true"/> to use the WARP software rasterizer even when a GPU is present.</param>
    /// <param name="featureLevel10"><see langword="true"/> to ask for feature level 10.0 only.</param>
    /// <returns>The renderer.</returns>
    internal static D3D11Renderer Create(nint window, int width, int height, bool useWarp, bool featureLevel10)
    {
        UiThread.VerifyAccess();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        // A hardware device first; WARP when there is none (a VM, a remote session) or when asked.
        bool isWarp = useWarp;
        if (!useWarp && TryCreateDevice(D3D_DRIVER_TYPE_HARDWARE, featureLevel10, out ID3D11Device* device, out ID3D11DeviceContext* context) >= 0)
        {
            return new D3D11Renderer(device, context, isWarp: false, (HWND)window, width, height);
        }

        isWarp = true;
        HResults.ThrowIfFailed(TryCreateDevice(D3D_DRIVER_TYPE_WARP, featureLevel10, out device, out context), "D3D11CreateDevice (WARP)");
        return new D3D11Renderer(device, context, isWarp, (HWND)window, width, height);
    }

    /// <summary>
    /// Draws the display list over a cleared back buffer and presents it, synchronized to the display's refresh.
    /// </summary>
    /// <param name="list">The commands of the frame.</param>
    /// <param name="clearColor">The color of the area no command covers.</param>
    /// <param name="atlas">The glyph atlas the glyph runs of <paramref name="list"/> were written from; null when it has none.</param>
    /// <exception cref="InvalidOperationException">The calling thread is not the UI thread.</exception>
    /// <exception cref="ArgumentException">The list has glyph runs and <paramref name="atlas"/> is null.</exception>
    /// <exception cref="GpuDeviceLostException">The device was lost.</exception>
    public void Render(DisplayList list, Color clearColor, GlyphAtlas? atlas = null)
    {
        Draw(list, clearColor, atlas);
        Present();
    }

    /// <summary>
    /// Resizes the back buffers. The render target view is released before <c>ResizeBuffers</c> and created again after, the order
    /// the swap chain requires (design §2.11.1).
    /// </summary>
    /// <param name="width">The new client width, in physical pixels.</param>
    /// <param name="height">The new client height, in physical pixels.</param>
    /// <exception cref="InvalidOperationException">The calling thread is not the UI thread.</exception>
    /// <exception cref="GpuDeviceLostException">The device was lost.</exception>
    public void Resize(int width, int height)
    {
        UiThread.VerifyAccess();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (width == Width && height == Height)
        {
            return;
        }

        // 1. Every view of a back buffer: unbind it from the context, then release it. ResizeBuffers documents that no view may
        // still be bound; WARP and the hardware tested also accept a bound one (S7), but the contract is the documentation's.
        _context->OMSetRenderTargets(0, null, null);
        _ = _renderTarget->Release();
        _renderTarget = null;

        // 2. ResizeBuffers, keeping the count, the format, and the flags.
        HRESULT result = _swapChain->ResizeBuffers(0, (uint)width, (uint)height, DXGI_FORMAT.DXGI_FORMAT_UNKNOWN, SwapChainFlags);
        HResults.ThrowIfFailed(result, "IDXGISwapChain::ResizeBuffers", RemovedReason(result));

        // 3. The view again, and the constants that depend on the size.
        Width = width;
        Height = height;
        CreateRenderTarget();
        WriteTargetConstants();
    }

    /// <summary>
    /// Releases every COM reference and closes the waitable object. It also works after a failed <see cref="Resize"/> (the
    /// render target view is gone) or a constructor that failed part way.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_context is not null)
        {
            _context->ClearState();
        }

        _gpuTimer?.Dispose();
        _grayAtlas.Dispose();
        _rgbAtlas.Dispose();
        _colorAtlas.Dispose();
        Release((IUnknown*)_instances);
        _shapeRows.Dispose();
        _brushRows.Dispose();
        Release((IUnknown*)_clearTypeBlend);
        Release((IUnknown*)_blend);
        Release((IUnknown*)_targetConstants);
        Release((IUnknown*)_inputLayout);
        _layers.Dispose();
        Release((IUnknown*)_fadedTextPixelShader);
        Release((IUnknown*)_fadedCurvePixelShader);
        Release((IUnknown*)_fadedPixelShader);
        Release((IUnknown*)_layerPixelShader);
        Release((IUnknown*)_clearTypeTextPixelShader);
        Release((IUnknown*)_textPixelShader);
        Release((IUnknown*)_curvePixelShader);
        Release((IUnknown*)_clearTypePixelShader);
        Release((IUnknown*)_pixelShader);
        Release((IUnknown*)_vertexShader);
        Release((IUnknown*)_renderTarget);
        _renderTarget = null;
        if (_frameLatencyWaitable != HANDLE.NULL)
        {
            _ = CloseHandle(_frameLatencyWaitable);
        }

        Release((IUnknown*)_swapChain);
        Release((IUnknown*)_context);
        Release((IUnknown*)_device);
    }

    /// <summary>
    /// Uploads the atlas's changes, writes the instances of the display list, and records the draw into the back buffer, without
    /// presenting.
    /// </summary>
    /// <param name="list">The commands of the frame.</param>
    /// <param name="clearColor">The color of the area no command covers.</param>
    /// <param name="atlas">
    /// The glyph atlas the glyph runs of <paramref name="list"/> were written from; null when it has none. Its dirty regions are
    /// cleared after the upload, so an atlas is drawn by one renderer only.
    /// </param>
    /// <exception cref="InvalidOperationException">The calling thread is not the UI thread.</exception>
    /// <exception cref="ArgumentException">The list has glyph runs and <paramref name="atlas"/> is null.</exception>
    public void Draw(DisplayList list, Color clearColor, GlyphAtlas? atlas = null)
    {
        ArgumentNullException.ThrowIfNull(list);
        UiThread.VerifyAccess();
        if (_renderTarget is null)
        {
            ThrowNoRenderTarget();
        }

        if (atlas is null && list.GlyphCount > 0)
        {
            throw new ArgumentException("The display list has glyph runs; pass the glyph atlas they were written from.", nameof(atlas));
        }

        // The GPU time covers all of this frame's GPU work that the renderer records: the atlas upload, the instance upload, the
        // clear, and the draw (design §11.3, GPU execution time).
        _gpuTimer?.Begin();
        if (atlas is not null)
        {
            _grayAtlas.Upload(_device, _context, atlas);
            _rgbAtlas.Upload(_device, _context, atlas);
            _colorAtlas.Upload(_device, _context, atlas);
            atlas.ClearDirtyRegions();
        }

        // A glyph run is one command and one instance per glyph, so this bounds the instances.
        int bound = list.CommandCount + list.GlyphCount;
        if (bound > _instanceCapacity)
        {
            GrowInstances(bound);
        }

        int draws = bound > 0 ? WriteInstances(list, bound) : 0;
        int drawCalls = 0;
        foreach (DrawRange range in _draws.AsSpan(0, draws))
        {
            drawCalls += range.Op == DrawOp.Draw && range.Count > 0 ? 1 : 0;
        }

        LastDrawCount = drawCalls;

        ID3D11RenderTargetView* target = _renderTarget;
        _context->OMSetRenderTargets(1, &target, null);
        var viewport = new D3D11_VIEWPORT
        {
            Width = Width,
            Height = Height,
            MaxDepth = 1,
        };
        _context->RSSetViewports(1, &viewport);
        float* clear = stackalloc float[4] { clearColor.R / 255f, clearColor.G / 255f, clearColor.B / 255f, clearColor.A / 255f };
        _context->ClearRenderTargetView(target, clear);
        if (draws == 0)
        {
            _gpuTimer?.End();
            return;
        }

        uint stride = (uint)sizeof(QuadInstance);
        uint offset = 0;
        ID3D11Buffer* instances = _instances;
        ID3D11Buffer* constants = _targetConstants;
        _context->IASetInputLayout(_inputLayout);
        _context->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY.D3D_PRIMITIVE_TOPOLOGY_TRIANGLESTRIP);
        _context->IASetVertexBuffers(0, 1, &instances, &stride, &offset);
        _context->VSSetShader(_vertexShader, null, 0);
        _context->VSSetConstantBuffers(0, 1, &constants);
        _context->PSSetConstantBuffers(0, 1, &constants);
        ID3D11ShaderResourceView** atlasViews = stackalloc ID3D11ShaderResourceView*[5]
        {
            _grayAtlas.View,
            _rgbAtlas.View,
            _colorAtlas.View,
            _shapeRows.View,
            _brushRows.View,
        };
        _context->PSSetShaderResources(0, 5, atlasViews);
        int targets = 0;
        float* transparent = stackalloc float[4];
        foreach (DrawRange draw in _draws.AsSpan(0, draws))
        {
            if (draw.Op == DrawOp.BeginLayer)
            {
                if (targets == _targetLayers.Length)
                {
                    Array.Resize(ref _targetLayers, targets * 2);
                    Array.Resize(ref _targetStack, targets * 2);
                }

                _targetStack[targets] = draw.Bounds;
                _targetLayers[targets++] = draw.Layer;
                SetTarget(_layers.Target(draw.Layer), draw.Bounds);
                _context->ClearRenderTargetView(_layers.Target(draw.Layer), transparent);
                continue;
            }

            if (draw.Op == DrawOp.EndLayer)
            {
                targets--;
                bool root = targets == 0;
                SetTarget(
                    root ? _renderTarget : _layers.Target(_targetLayers[targets - 1]),
                    root ? new Vector4(0, 0, Width, Height) : _targetStack[targets - 1]
                );
                continue;
            }

            if (draw.Count == 0)
            {
                continue;
            }

            ID3D11PixelShader* shader = draw.Pixel switch
            {
                PixelKind.ClearType => _clearTypePixelShader,
                PixelKind.Curve => _curvePixelShader,
                PixelKind.Text => _textPixelShader,
                PixelKind.ClearTypeText => _clearTypeTextPixelShader,
                PixelKind.Layer => _layerPixelShader,
                PixelKind.Faded => _fadedPixelShader,
                PixelKind.CurveFaded => _fadedCurvePixelShader,
                PixelKind.TextFaded => _fadedTextPixelShader,
                _ => _pixelShader,
            };
            bool clearType = draw.Pixel is PixelKind.ClearType or PixelKind.ClearTypeText;
            _context->PSSetShader(shader, null, 0);
            _context->OMSetBlendState(clearType ? _clearTypeBlend : _blend, null, uint.MaxValue);
            if (draw.Pixel == PixelKind.Layer)
            {
                ID3D11ShaderResourceView* layer = _layers.View(draw.Layer);
                _context->PSSetShaderResources(5, 1, &layer);
                _context->DrawInstanced(4, (uint)draw.Count, 0, (uint)draw.Start);
                layer = null;
                _context->PSSetShaderResources(5, 1, &layer);
                continue;
            }

            _context->DrawInstanced(4, (uint)draw.Count, 0, (uint)draw.Start);
        }

        _gpuTimer?.End();
    }

    // Makes `target`, whose pixels are those of `bounds` in the frame, the target of the draws that follow (row R4c).
    private void SetTarget(ID3D11RenderTargetView* target, Vector4 bounds)
    {
        _context->OMSetRenderTargets(1, &target, null);
        var viewport = new D3D11_VIEWPORT
        {
            Width = bounds.Z,
            Height = bounds.W,
            MaxDepth = 1,
        };
        _context->RSSetViewports(1, &viewport);
        WriteTargetConstants(bounds);
    }

    /// <summary>
    /// Presents the back buffer, waiting for the display's refresh.
    /// </summary>
    /// <exception cref="InvalidOperationException">The calling thread is not the UI thread.</exception>
    /// <exception cref="GpuDeviceLostException">The device was lost.</exception>
    public void Present()
    {
        UiThread.VerifyAccess();

        // The time of the call, only while frames are measured (the frame path makes no call for the measurement otherwise).
        LARGE_INTEGER called = default;
        if (_gpuTimer is not null)
        {
            _ = QueryPerformanceCounter(&called);
        }

        HRESULT result = _swapChain->Present(1, 0);
        HResults.ThrowIfFailed(result, "IDXGISwapChain::Present", RemovedReason(result));
        if (_gpuTimer is not null)
        {
            uint count;
            if (_swapChain->GetLastPresentCount(&count) >= 0)
            {
                _presents[count % (uint)_presents.Length] = (count, called.QuadPart);
            }
        }
    }

    /// <summary>
    /// Starts measuring frames: the GPU time of each draw (timestamp queries) and the time from each present call to the display
    /// (frame statistics). For the benchmarks (roadmap row M1a); frames are not measured otherwise.
    /// </summary>
    internal void StartMeasuringFrames()
    {
        if (_gpuTimer is null)
        {
            _gpuTimer = new GpuFrameTimer(_device, _context);
            _presents = new (uint, long)[8];
        }
    }

    /// <summary>
    /// Returns the GPU time of the oldest measured frame that the GPU finished (see <see cref="StartMeasuringFrames"/>).
    /// </summary>
    /// <param name="milliseconds">The time from the frame's first command to its last, on the GPU.</param>
    /// <returns><see langword="false"/> when none is ready, or frames are not measured.</returns>
    internal bool TryReadGpuMilliseconds(out double milliseconds)
    {
        milliseconds = 0;
        return _gpuTimer is not null && _gpuTimer.TryRead(out milliseconds);
    }

    /// <summary>
    /// Returns, for the last present that reached the display and was not returned before, the time from the present call to
    /// the display's refresh that showed it (frame statistics, <c>SyncQPCTime</c>; see <see cref="StartMeasuringFrames"/>).
    /// </summary>
    /// <param name="milliseconds">The time from the present call to the refresh.</param>
    /// <param name="displayedQpc">The performance counter of that refresh.</param>
    /// <returns><see langword="false"/> when no new present reached the display, or frames are not measured.</returns>
    internal bool TryReadPresentLatency(out double milliseconds, out long displayedQpc)
    {
        milliseconds = 0;
        displayedQpc = 0;
        if (_gpuTimer is null)
        {
            return false;
        }

        DXGI_FRAME_STATISTICS statistics;
        if (_swapChain->GetFrameStatistics(&statistics) < 0 || statistics.PresentCount == _lastReportedPresent)
        {
            return false;
        }

        (uint count, long qpc) = _presents[statistics.PresentCount % (uint)_presents.Length];
        if (count != statistics.PresentCount)
        {
            return false;
        }

        LARGE_INTEGER frequency;
        _ = QueryPerformanceFrequency(&frequency);
        _lastReportedPresent = count;
        displayedQpc = statistics.SyncQPCTime.QuadPart;
        milliseconds = (displayedQpc - qpc) * 1000.0 / frequency.QuadPart;
        return true;
    }

    /// <summary>
    /// Copies the back buffer to the CPU and returns the color of one pixel. Call it between <see cref="Draw"/> and
    /// <see cref="Present"/>: after a flip-model present, the back buffer's content is undefined. It creates a staging texture
    /// and waits for the GPU, so it is for tests and checks, not for every frame.
    /// </summary>
    /// <param name="x">The column, from the left.</param>
    /// <param name="y">The row, from the top.</param>
    /// <returns>The color of the pixel.</returns>
    /// <exception cref="InvalidOperationException">The calling thread is not the UI thread.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The pixel is outside the back buffer.</exception>
    public Color ReadPixel(int x, int y)
    {
        UiThread.VerifyAccess();
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(x, Width);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(y, Height);
        ID3D11Texture2D* staging = CaptureBackBuffer();
        try
        {
            return ReadCapturedPixel(staging, x, y);
        }
        finally
        {
            _ = staging->Release();
        }
    }

    /// <summary>
    /// Records a copy of the back buffer into a new staging texture, without waiting for it; the caller releases it. With
    /// <see cref="ReadCapturedPixel"/> later, a test sees what the draws recorded before the capture produced, even when later
    /// work changed what they read.
    /// </summary>
    /// <returns>The staging texture.</returns>
    internal ID3D11Texture2D* CaptureBackBuffer()
    {
        ID3D11Texture2D* backBuffer;
        HResults.ThrowIfFailed(_swapChain->GetBuffer(0, __uuidof<ID3D11Texture2D>(), (void**)&backBuffer), "IDXGISwapChain::GetBuffer");
        D3D11_TEXTURE2D_DESC description;
        backBuffer->GetDesc(&description);
        description.Usage = D3D11_USAGE.D3D11_USAGE_STAGING;
        description.BindFlags = 0;
        description.CPUAccessFlags = (uint)D3D11_CPU_ACCESS_FLAG.D3D11_CPU_ACCESS_READ;
        description.MiscFlags = 0;
        try
        {
            ID3D11Texture2D* staging;
            HResults.ThrowIfFailed(_device->CreateTexture2D(&description, null, &staging), "ID3D11Device::CreateTexture2D");
            _context->CopyResource((ID3D11Resource*)staging, (ID3D11Resource*)backBuffer);
            return staging;
        }
        finally
        {
            _ = backBuffer->Release();
        }
    }

    /// <summary>
    /// Waits for a capture of <see cref="CaptureBackBuffer"/> and returns the color of one of its pixels.
    /// </summary>
    /// <param name="staging">The capture.</param>
    /// <param name="x">The column, from the left; inside the back buffer.</param>
    /// <param name="y">The row, from the top; inside the back buffer.</param>
    /// <returns>The color of the pixel.</returns>
    internal Color ReadCapturedPixel(ID3D11Texture2D* staging, int x, int y)
    {
        D3D11_MAPPED_SUBRESOURCE mapped;
        HResults.ThrowIfFailed(_context->Map((ID3D11Resource*)staging, 0, D3D11_MAP.D3D11_MAP_READ, 0, &mapped), "ID3D11DeviceContext::Map");
        byte* pixel = (byte*)mapped.pData + (y * mapped.RowPitch) + (x * 4);
        var color = new Color(pixel[2], pixel[1], pixel[0], pixel[3]);
        _context->Unmap((ID3D11Resource*)staging, 0);
        return color;
    }

    private static HRESULT TryCreateDevice(D3D_DRIVER_TYPE driver, bool featureLevel10, out ID3D11Device* device, out ID3D11DeviceContext* context)
    {
        D3D_FEATURE_LEVEL* levels = stackalloc D3D_FEATURE_LEVEL[] { D3D_FEATURE_LEVEL_11_0, D3D_FEATURE_LEVEL_10_1, D3D_FEATURE_LEVEL_10_0 };
        uint levelCount = 3;
        if (featureLevel10)
        {
            levels[0] = D3D_FEATURE_LEVEL_10_0;
            levelCount = 1;
        }

        uint flags = (uint)D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_BGRA_SUPPORT;
        ID3D11Device* createdDevice;
        ID3D11DeviceContext* createdContext;
        HRESULT result = D3D11CreateDevice(
            null,
            driver,
            HMODULE.NULL,
            flags,
            levels,
            levelCount,
            TerraFX.Interop.DirectX.D3D11.D3D11_SDK_VERSION,
            &createdDevice,
            null,
            &createdContext
        );
        device = result >= 0 ? createdDevice : null;
        context = result >= 0 ? createdContext : null;
        return result;
    }

    private static IDXGISwapChain2* CreateSwapChain(ID3D11Device* device, HWND window, int width, int height)
    {
        IDXGIDevice* dxgiDevice;
        HResults.ThrowIfFailed(device->QueryInterface(__uuidof<IDXGIDevice>(), (void**)&dxgiDevice), "QueryInterface(IDXGIDevice)");
        IDXGIAdapter* adapter = null;
        IDXGIFactory2* factory = null;
        IDXGISwapChain1* swapChain1 = null;
        try
        {
            HResults.ThrowIfFailed(dxgiDevice->GetAdapter(&adapter), "IDXGIDevice::GetAdapter");
            HResults.ThrowIfFailed(adapter->GetParent(__uuidof<IDXGIFactory2>(), (void**)&factory), "IDXGIAdapter::GetParent");
            var description = new DXGI_SWAP_CHAIN_DESC1
            {
                Width = (uint)width,
                Height = (uint)height,
                Format = BackBufferFormat,
                SampleDesc = new DXGI_SAMPLE_DESC(1, 0),
                BufferUsage = DXGI.DXGI_USAGE_RENDER_TARGET_OUTPUT,
                BufferCount = 2,
                Scaling = DXGI_SCALING.DXGI_SCALING_STRETCH,
                SwapEffect = DXGI_SWAP_EFFECT.DXGI_SWAP_EFFECT_FLIP_DISCARD,
                AlphaMode = DXGI_ALPHA_MODE.DXGI_ALPHA_MODE_UNSPECIFIED,
                Flags = SwapChainFlags,
            };
            HResults.ThrowIfFailed(
                factory->CreateSwapChainForHwnd((IUnknown*)device, window, &description, null, null, &swapChain1),
                "IDXGIFactory2::CreateSwapChainForHwnd"
            );
            _ = factory->MakeWindowAssociation(window, DXGI.DXGI_MWA_NO_ALT_ENTER);
            IDXGISwapChain2* swapChain;
            HResults.ThrowIfFailed(swapChain1->QueryInterface(__uuidof<IDXGISwapChain2>(), (void**)&swapChain), "QueryInterface(IDXGISwapChain2)");
            return swapChain;
        }
        finally
        {
            if (swapChain1 is not null)
            {
                _ = swapChain1->Release();
            }

            if (factory is not null)
            {
                _ = factory->Release();
            }

            if (adapter is not null)
            {
                _ = adapter->Release();
            }

            _ = dxgiDevice->Release();
        }
    }

    // Compiles an entry point of QuadShaders; `faded` defines FADED, for the variants of the groups that give their opacity to their
    // children (row R4c).
    private static ID3DBlob* Compile(ReadOnlySpan<byte> entry, ReadOnlySpan<byte> target, bool faded = false)
    {
        ReadOnlySpan<byte> source = QuadShaders.Source;
        ReadOnlySpan<byte> name = "FADED\0"u8;
        ReadOnlySpan<byte> value = "1\0"u8;
        ID3DBlob* code;
        ID3DBlob* errors = null;
        HRESULT result;
        D3D_SHADER_MACRO* macros = stackalloc D3D_SHADER_MACRO[2];
        fixed (byte* sourcePointer = source)
        {
            fixed (
                byte* namePointer = name,
                    valuePointer = value,
                    entryPointer = entry,
                    targetPointer = target
            )
            {
                macros[0] = new D3D_SHADER_MACRO { Name = (sbyte*)namePointer, Definition = (sbyte*)valuePointer };
                macros[1] = default;
                result = D3DCompile(
                    sourcePointer,
                    (nuint)source.Length,
                    null,
                    faded ? macros : null,
                    null,
                    (sbyte*)entryPointer,
                    (sbyte*)targetPointer,
                    D3DCOMPILE.D3DCOMPILE_OPTIMIZATION_LEVEL3,
                    0,
                    &code,
                    &errors
                );
            }
        }

        if (result < 0)
        {
            string message = errors is null ? "no message" : Encoding.UTF8.GetString((byte*)errors->GetBufferPointer(), (int)errors->GetBufferSize());
            if (errors is not null)
            {
                _ = errors->Release();
            }

            throw new InvalidOperationException($"D3DCompile failed: {message}") { HResult = result };
        }

        if (errors is not null)
        {
            _ = errors->Release();
        }

        return code;
    }

    private static ID3D11VertexShader* CreateVertexShader(ID3D11Device* device, out ID3D11InputLayout* inputLayout)
    {
        ID3DBlob* code = Compile(QuadShaders.VertexEntry, QuadShaders.VertexTarget);
        try
        {
            ID3D11VertexShader* shader;
            HResults.ThrowIfFailed(
                device->CreateVertexShader(code->GetBufferPointer(), code->GetBufferSize(), null, &shader),
                "ID3D11Device::CreateVertexShader"
            );

            // The semantic names in one pinned literal: "RECT" at 0, "COLOR" at 5, "ATLAS" at 11, "PAGE" at 17, "SHAPE" at 22.
            fixed (byte* names = "RECT\0COLOR\0ATLAS\0PAGE\0SHAPE\0"u8)
            {
                byte* rect = names;
                byte* color = names + 5;
                D3D11_INPUT_ELEMENT_DESC* elements = stackalloc D3D11_INPUT_ELEMENT_DESC[5];
                elements[0] = new D3D11_INPUT_ELEMENT_DESC
                {
                    SemanticName = (sbyte*)rect,
                    Format = DXGI_FORMAT.DXGI_FORMAT_R32G32B32A32_FLOAT,
                    AlignedByteOffset = 0,
                    InputSlotClass = D3D11_INPUT_CLASSIFICATION.D3D11_INPUT_PER_INSTANCE_DATA,
                    InstanceDataStepRate = 1,
                };
                elements[1] = new D3D11_INPUT_ELEMENT_DESC
                {
                    SemanticName = (sbyte*)color,
                    Format = DXGI_FORMAT.DXGI_FORMAT_R32_UINT,
                    AlignedByteOffset = (uint)sizeof(Vector4),
                    InputSlotClass = D3D11_INPUT_CLASSIFICATION.D3D11_INPUT_PER_INSTANCE_DATA,
                    InstanceDataStepRate = 1,
                };
                elements[2] = new D3D11_INPUT_ELEMENT_DESC
                {
                    SemanticName = (sbyte*)(names + 11),
                    Format = DXGI_FORMAT.DXGI_FORMAT_R32_UINT,
                    AlignedByteOffset = (uint)sizeof(Vector4) + 4,
                    InputSlotClass = D3D11_INPUT_CLASSIFICATION.D3D11_INPUT_PER_INSTANCE_DATA,
                    InstanceDataStepRate = 1,
                };
                elements[3] = new D3D11_INPUT_ELEMENT_DESC
                {
                    SemanticName = (sbyte*)(names + 17),
                    Format = DXGI_FORMAT.DXGI_FORMAT_R32_UINT,
                    AlignedByteOffset = (uint)sizeof(Vector4) + 8,
                    InputSlotClass = D3D11_INPUT_CLASSIFICATION.D3D11_INPUT_PER_INSTANCE_DATA,
                    InstanceDataStepRate = 1,
                };
                elements[4] = new D3D11_INPUT_ELEMENT_DESC
                {
                    SemanticName = (sbyte*)(names + 22),
                    Format = DXGI_FORMAT.DXGI_FORMAT_R32_UINT,
                    AlignedByteOffset = (uint)sizeof(Vector4) + 12,
                    InputSlotClass = D3D11_INPUT_CLASSIFICATION.D3D11_INPUT_PER_INSTANCE_DATA,
                    InstanceDataStepRate = 1,
                };
                ID3D11InputLayout* layout;
                HResults.ThrowIfFailed(
                    device->CreateInputLayout(elements, 5, code->GetBufferPointer(), code->GetBufferSize(), &layout),
                    "ID3D11Device::CreateInputLayout"
                );
                inputLayout = layout;
                return shader;
            }
        }
        finally
        {
            _ = code->Release();
        }
    }

    private static ID3D11PixelShader* CreatePixelShader(ID3D11Device* device, ReadOnlySpan<byte> entry, bool faded = false)
    {
        ID3DBlob* code = Compile(entry, QuadShaders.PixelTarget, faded);
        try
        {
            ID3D11PixelShader* shader;
            HResults.ThrowIfFailed(
                device->CreatePixelShader(code->GetBufferPointer(), code->GetBufferSize(), null, &shader),
                "ID3D11Device::CreatePixelShader"
            );
            return shader;
        }
        finally
        {
            _ = code->Release();
        }
    }

    // Premultiplied "over": the pixel shader multiplies the color by its alpha (the color's alpha times a glyph's coverage).
    private static ID3D11BlendState* CreateBlendState(ID3D11Device* device)
    {
        var description = default(D3D11_BLEND_DESC);
        description.RenderTarget[0] = new D3D11_RENDER_TARGET_BLEND_DESC
        {
            BlendEnable = true,
            SrcBlend = D3D11_BLEND.D3D11_BLEND_ONE,
            DestBlend = D3D11_BLEND.D3D11_BLEND_INV_SRC_ALPHA,
            BlendOp = D3D11_BLEND_OP.D3D11_BLEND_OP_ADD,
            SrcBlendAlpha = D3D11_BLEND.D3D11_BLEND_ONE,
            DestBlendAlpha = D3D11_BLEND.D3D11_BLEND_INV_SRC_ALPHA,
            BlendOpAlpha = D3D11_BLEND_OP.D3D11_BLEND_OP_ADD,
            RenderTargetWriteMask = (byte)D3D11_COLOR_WRITE_ENABLE.D3D11_COLOR_WRITE_ENABLE_ALL,
        };
        ID3D11BlendState* blend;
        HResults.ThrowIfFailed(device->CreateBlendState(&description, &blend), "ID3D11Device::CreateBlendState");
        return blend;
    }

    // Dual-source: each channel of the target takes the color by its own alpha, the shader's second output (GPUI's
    // create_blend_state_for_subpixel_rendering, directx_renderer.rs, lines 1513 to 1530). The target's alpha is not written.
    private static ID3D11BlendState* CreateClearTypeBlendState(ID3D11Device* device)
    {
        var description = default(D3D11_BLEND_DESC);
        description.RenderTarget[0] = new D3D11_RENDER_TARGET_BLEND_DESC
        {
            BlendEnable = true,
            SrcBlend = D3D11_BLEND.D3D11_BLEND_SRC1_COLOR,
            DestBlend = D3D11_BLEND.D3D11_BLEND_INV_SRC1_COLOR,
            BlendOp = D3D11_BLEND_OP.D3D11_BLEND_OP_ADD,
            SrcBlendAlpha = D3D11_BLEND.D3D11_BLEND_ONE,
            DestBlendAlpha = D3D11_BLEND.D3D11_BLEND_ZERO,
            BlendOpAlpha = D3D11_BLEND_OP.D3D11_BLEND_OP_ADD,
            RenderTargetWriteMask = (byte)(
                D3D11_COLOR_WRITE_ENABLE.D3D11_COLOR_WRITE_ENABLE_ALL & ~D3D11_COLOR_WRITE_ENABLE.D3D11_COLOR_WRITE_ENABLE_ALPHA
            ),
        };
        ID3D11BlendState* blend;
        HResults.ThrowIfFailed(device->CreateBlendState(&description, &blend), "ID3D11Device::CreateBlendState");
        return blend;
    }

    private static ID3D11Buffer* CreateBuffer(ID3D11Device* device, int bytes, D3D11_BIND_FLAG bind)
    {
        var description = new D3D11_BUFFER_DESC
        {
            ByteWidth = (uint)bytes,
            Usage = D3D11_USAGE.D3D11_USAGE_DYNAMIC,
            BindFlags = (uint)bind,
            CPUAccessFlags = (uint)D3D11_CPU_ACCESS_FLAG.D3D11_CPU_ACCESS_WRITE,
        };
        ID3D11Buffer* buffer;
        HResults.ThrowIfFailed(device->CreateBuffer(&description, null, &buffer), "ID3D11Device::CreateBuffer");
        return buffer;
    }

    private int RemovedReason(HRESULT result) => HResults.IsDeviceLost(result) ? _device->GetDeviceRemovedReason() : 0;

    private void CreateRenderTarget()
    {
        ID3D11Texture2D* backBuffer;
        HResults.ThrowIfFailed(_swapChain->GetBuffer(0, __uuidof<ID3D11Texture2D>(), (void**)&backBuffer), "IDXGISwapChain::GetBuffer");
        try
        {
            ID3D11RenderTargetView* view;
            HResults.ThrowIfFailed(_device->CreateRenderTargetView((ID3D11Resource*)backBuffer, null, &view), "ID3D11Device::CreateRenderTargetView");
            _renderTarget = view;
        }
        finally
        {
            _ = backBuffer->Release();
        }
    }

    private void SetTextRendering(TextRenderingParameters value)
    {
        UiThread.VerifyAccess();
        _textRendering = value;
        WriteTargetConstants();
    }

    private void WriteTargetConstants() => WriteTargetConstants(new Vector4(0, 0, Width, Height));

    // The constants of a target of the size (bounds.Z, bounds.W): the back buffer, or a layer (row R4c), whose commands were moved
    // into its pixels as they were written (Shift).
    private void WriteTargetConstants(Vector4 bounds)
    {
        D3D11_MAPPED_SUBRESOURCE mapped;
        HResults.ThrowIfFailed(
            _context->Map((ID3D11Resource*)_targetConstants, 0, D3D11_MAP.D3D11_MAP_WRITE_DISCARD, 0, &mapped),
            "ID3D11DeviceContext::Map"
        );
        var constants = (Vector4*)mapped.pData;
        TextRenderingParameters text = _textRendering;
        constants[0] = new Vector4(1f / bounds.Z, 1f / bounds.W, text.GrayscaleContrast, text.ClearTypeContrast);
        constants[1] = TextGamma.RatiosOf(text.Gamma);
        constants[2] = new Vector4(text.IsBgr ? 1 : 0, 0, 0, 0);
        _context->Unmap((ID3D11Resource*)_targetConstants, 0);
    }

    // The instances go straight into the mapped buffer; WRITE_DISCARD gives a fresh buffer while the GPU still reads the last one:
    // one per rectangle and one per glyph. Returns the number of draws, in _draws: a new one starts where the blend state changes.
    // A list without groups takes a copy of the loop specialized for it, in which NativeAOT folds the alpha and the layer's origin
    // away: with them in the one loop, the Draw stage of frames took 2 us longer and that of frames --curves 9 us (row R4c).
    private int WriteInstances(DisplayList list, int bound) =>
        list.GroupCount > 0 ? WriteInstances<WithGroups>(list, bound) : WriteInstances<NoGroups>(list, bound);

    private int WriteInstances<TMode>(DisplayList list, int bound)
        where TMode : struct, IGroupMode
    {
        D3D11_MAPPED_SUBRESOURCE mapped;
        HResults.ThrowIfFailed(
            _context->Map((ID3D11Resource*)_instances, 0, D3D11_MAP.D3D11_MAP_WRITE_DISCARD, 0, &mapped),
            "ID3D11DeviceContext::Map"
        );
        try
        {
            var destination = new Span<QuadInstance>(mapped.pData, bound);
            DisplayListReader reader = list.GetReader();
            Span<Vector4> shapes = list.ShapeCount > 0 ? _shapeRows.Map(_device, _context, list.ShapeCount * ShapeRows) : default;
            if (reader.Brushes.Length > 0)
            {
                WriteBrushes(reader.Brushes);
            }

            _layers.BeginFrame();
            int i = 0;
            int draws = 0;
            int shape = 0;
            int depth = 0;
            int layers = 0;
            float alpha = 1;
            Vector4 move = default;
            var target = new Vector4(0, 0, Width, Height);
            while (reader.TryNext(out CommandTag tag))
            {
                // The common commands first. Inside a layer, `move` is the layer's origin, which each command subtracts from its
                // positions as it writes them, so that the shaders need no origin (row R4c). The instances and rows are written once:
                // reading them back from the mapped buffers, which are write-combined, cost 120 us a frame for 1,000 rectangles in layers.
                if (tag == CommandTag.FillRect)
                {
                    draws = AddToDraw(draws, i, PixelKind.Main);
                    FillRectCommand rect = reader.ReadFillRect();
                    destination[i] = new QuadInstance
                    {
                        Rect = Moved<TMode>(new Vector4(rect.X, rect.Y, rect.Width, rect.Height), move),
                        Color = Faded<TMode>(rect.Color, alpha),
                        Page = QuadInstance.SolidPage,
                        Shape = QuadInstance.NoShape,
                    };
                }
                else if (tag == CommandTag.Shape)
                {
                    ShapeCommand command = reader.ReadShape();
                    draws = AddToDraw(draws, i, TMode.Groups && alpha < 1 ? PixelKind.Faded : PixelKind.Main);
                    destination[i] = WriteShape<TMode>(command, shapes, shape, alpha, move);
                    shape += ShapeRows;
                }
                else if (tag == CommandTag.Shadow)
                {
                    draws = AddToDraw(draws, i, PixelKind.Main);
                    destination[i] = WriteShadow<TMode>(reader.ReadShadow(), shapes, shape, alpha, move);
                    shape += ShapeRows;
                }
                else if (tag == CommandTag.GlyphRun)
                {
                    i = WriteGlyphRun<TMode>(reader.ReadGlyphRun(), destination, i, shapes, ref shape, ref draws, alpha, move);
                    continue;
                }
                else if (!TMode.Groups || tag <= CommandTag.Sector)
                {
                    // Without groups every tag left is a curve, so a curve is compared no more times than before groups, and the
                    // command goes straight to WriteCurve: a local copy of it cost a Memmove per curve (R4c profile of the curves
                    // scene).
                    if (TMode.Groups)
                    {
                        CurveCommand command = Moved(tag, reader.ReadCurve(), move);
                        draws = AddToDraw(draws, i, alpha < 1 && command.Brush >= 0 ? PixelKind.CurveFaded : PixelKind.Curve);
                        destination[i] = WriteCurve<TMode>(tag, command, shapes, shape, alpha);
                    }
                    else
                    {
                        draws = AddToDraw(draws, i, PixelKind.Curve);
                        destination[i] = WriteCurve<TMode>(tag, reader.ReadCurve(), shapes, shape, alpha);
                    }

                    shape += ShapeRows;
                }
                else if (TMode.Groups && tag == CommandTag.PushOpacity)
                {
                    GroupCommand group = reader.ReadGroup();
                    if (depth == _groupStates.Length)
                    {
                        Array.Resize(ref _groupStates, depth * 2);
                    }

                    ref GroupState state = ref _groupStates[depth++];
                    state = new GroupState
                    {
                        SavedAlpha = alpha,
                        Alpha = group.Alpha,
                        SavedTarget = target,
                        Layer = -1,
                    };
                    if (!group.Layer)
                    {
                        alpha *= group.Alpha;
                        continue;
                    }

                    // The layer is the group's bounds cut to its target and put on whole pixels outward (design §5.0, "The API of
                    // layers, clips, transforms, and DrawingContext"); none is made when that is empty.
                    float left = MathF.Max(MathF.Floor(group.X), target.X);
                    float top = MathF.Max(MathF.Floor(group.Y), target.Y);
                    float right = MathF.Min(MathF.Ceiling(group.X + group.Width), target.X + target.Z);
                    float bottom = MathF.Min(MathF.Ceiling(group.Y + group.Height), target.Y + target.W);
                    if (group.Width <= 0 || group.Height <= 0 || right <= left || bottom <= top)
                    {
                        depth--;
                        SkipGroup(ref reader);
                        continue;
                    }

                    int layer = _layers.Acquire(_device, (int)(right - left), (int)(bottom - top));
                    if (layer < 0)
                    {
                        // The pool is full: the children take the alpha, as GPUI and Avalonia draw every group.
                        alpha *= group.Alpha;
                        continue;
                    }

                    state.Layer = layer;
                    state.Bounds = new Vector4(left, top, right - left, bottom - top);
                    draws = AddOp(draws, i, DrawOp.BeginLayer, layer, state.Bounds);
                    target = state.Bounds;
                    alpha = 1;
                    layers++;
                    move = new Vector4(target.X, target.Y, 0, 0);
                    continue;
                }
                else if (TMode.Groups)
                {
                    reader.ReadPop();
                    GroupState state = _groupStates[--depth];
                    alpha = state.SavedAlpha;
                    target = state.SavedTarget;
                    if (state.Layer < 0)
                    {
                        continue;
                    }

                    // The composite, in the pixels of the target outside the layer.
                    layers--;
                    move = layers > 0 ? new Vector4(target.X, target.Y, 0, 0) : default;
                    draws = AddOp(draws, i, DrawOp.EndLayer, state.Layer, default);
                    draws = AddToDraw(draws, i, PixelKind.Layer, state.Layer);
                    destination[i++] = new QuadInstance
                    {
                        Rect = state.Bounds - move,
                        Color = Faded<TMode>(new Color(255, 255, 255, (byte)MathF.Round(255 * state.Alpha)), alpha),
                        Page = QuadInstance.SolidPage,
                        Shape = QuadInstance.NoShape,
                    };
                    continue;
                }

                i++;
            }

            if (draws > 0 && _draws[draws - 1].Op == DrawOp.Draw)
            {
                ref DrawRange last = ref _draws[draws - 1];
                last.Count = i - last.Start;
            }

            return draws;
        }
        finally
        {
            _context->Unmap((ID3D11Resource*)_instances, 0);
            if (list.ShapeCount > 0)
            {
                _shapeRows.Unmap(_context);
            }
        }
    }

    // The six rows of a rounded rectangle, as QuadShaders' ShapeColor reads them: the radii, the border's widths, the border's
    // color, the kind with the rows of the fill's and the border's gradients, the rectangle, and nothing.
    private QuadInstance WriteShape<TMode>(in ShapeCommand command, Span<Vector4> rows, int row, float alpha, in Vector4 move)
        where TMode : struct, IGroupMode
    {
        Vector4 rect = Moved<TMode>(new Vector4(command.X, command.Y, command.Width, command.Height), move);
        BorderWidths widths = command.BorderWidths;
        rows[row] = Radii(command.Radii);
        rows[row + 1] = new Vector4(widths.Left, widths.Top, widths.Right, widths.Bottom);
        rows[row + 2] = Straight(command.BorderColor);
        rows[row + 3] = new Vector4(ShapeKind, BrushRow(command.FillBrush), BrushRow(command.BorderBrush), 0);
        rows[row + 4] = rect;

        // A group's opacity given to its children, which PSFaded multiplies the whole shape by: fading the fill and the border
        // apart would show the fill through an opaque border.
        rows[row + 5] = new Vector4(TMode.Groups ? alpha : 1, 0, 0, 0);
        return new QuadInstance
        {
            Rect = rect,
            Color = command.Fill.Packed,
            Page = QuadInstance.SolidPage,
            Shape = (uint)row,
        };
    }

    // The six rows of a shadow: the radii of the blurred rectangle, the shape's rectangle, nothing, the kind with the sigma, the
    // blurred rectangle, and the shape's radii. An outer shadow's rectangle is the shape's moved and grown by the spread, and its
    // quad is that grown by three sigmas; an inset shadow's is the hole, moved and shrunk, and its quad the shape's (GPUI's
    // paint_drop_shadows and paint_inset_shadows, window.rs lines 4472 to 4535).
    private static QuadInstance WriteShadow<TMode>(in ShadowCommand command, Span<Vector4> rows, int row, float alpha, in Vector4 move)
        where TMode : struct, IGroupMode
    {
        Vector4 element = Moved<TMode>(new Vector4(command.X, command.Y, command.Width, command.Height), move);
        float spread = command.Inset ? -command.Spread : command.Spread;
        var blurred = new Vector4(
            element.X + command.Offset.X - spread,
            element.Y + command.Offset.Y - spread,
            MathF.Max(0, command.Width + (2 * spread)),
            MathF.Max(0, command.Height + (2 * spread))
        );
        CornerRadii radii = command.Radii;
        rows[row] = new Vector4(
            Grown(radii.TopLeft, spread),
            Grown(radii.TopRight, spread),
            Grown(radii.BottomRight, spread),
            Grown(radii.BottomLeft, spread)
        );
        rows[row + 1] = element;
        rows[row + 2] = default;
        rows[row + 3] = new Vector4(command.Inset ? InsetShadowKind : OuterShadowKind, -1, -1, command.Sigma);
        rows[row + 4] = blurred;
        rows[row + 5] = Radii(radii);
        float margin = 3 * command.Sigma;
        Vector4 quad = command.Inset
            ? element
            : new Vector4(blurred.X - margin, blurred.Y - margin, blurred.Z + (2 * margin), blurred.W + (2 * margin));
        return new QuadInstance
        {
            Rect = quad,
            Color = Faded<TMode>(command.Color, alpha),
            Page = QuadInstance.SolidPage,
            Shape = (uint)row,
        };
    }

    // The six rows of a line, an ellipse, an arc, or a sector, as QuadShaders' CurveColor reads them: the geometry; half the width,
    // the cap, the dash's length, and the dash's period; the dash's offset and the arc's start and sweep; the kind with the row of
    // the gradient and the width (0 for a fill); the gradient's box; and nothing. A line without dashes is one dash as long as the
    // line. The quad is the shape's box grown by the half width, the caps, and a pixel for the antialiasing.
    private QuadInstance WriteCurve<TMode>(CommandTag tag, in CurveCommand command, Span<Vector4> rows, int row, float alpha)
        where TMode : struct, IGroupMode
    {
        float half = command.Width * 0.5f;
        float kind = tag switch
        {
            CommandTag.Line => LineKind,
            CommandTag.FillEllipse => FillEllipseKind,
            CommandTag.StrokeEllipse => StrokeEllipseKind,
            CommandTag.Arc => ArcKind,
            _ => SectorKind,
        };
        float length =
            tag == CommandTag.Line
                ? Vector2.Distance(new Vector2(command.A, command.B), new Vector2(command.C, command.D))
                : command.SweepAngle * command.C;
        Dash dash = command.Dash ?? new Dash(length, NoDashGap);
        rows[row] = new Vector4(command.A, command.B, command.C, command.D);
        rows[row + 1] = new Vector4(half, (float)command.Cap, dash.On, dash.On + dash.Off);
        rows[row + 2] = new Vector4(dash.Offset, command.StartAngle, command.SweepAngle, length);
        rows[row + 3] = new Vector4(kind, BrushRow(command.Brush), command.Width, 0);
        rows[row + 4] = command.Box;
        rows[row + 5] = default;

        // Square caps reach half the width past an end along the line and as far to the side, so √2 times it from the end.
        float margin = (half * 1.4143f) + 1;
        Vector4 quad =
            tag == CommandTag.Line
                ? new Vector4(
                    MathF.Min(command.A, command.C) - margin,
                    MathF.Min(command.B, command.D) - margin,
                    MathF.Abs(command.C - command.A) + (2 * margin),
                    MathF.Abs(command.D - command.B) + (2 * margin)
                )
                : new Vector4(
                    command.A - command.C - margin,
                    command.B - (tag is CommandTag.FillEllipse or CommandTag.StrokeEllipse ? command.D : command.C) - margin,
                    (2 * command.C) + (2 * margin),
                    (2 * (tag is CommandTag.FillEllipse or CommandTag.StrokeEllipse ? command.D : command.C)) + (2 * margin)
                );

        // Only the faded shader reads a gradient's instance color, so a list without groups writes the color as it is.
        return new QuadInstance
        {
            Rect = quad,
            Color = TMode.Groups ? Faded<TMode>(command.Brush >= 0 ? Opaque : command.Color, alpha) : command.Color.Packed,
            Page = QuadInstance.SolidPage,
            Shape = (uint)row,
        };
    }

    // A curve less `move` (a layer's origin): a line's two ends, or a center, and the gradient's box. Moving the command before
    // WriteCurve keeps WriteCurve as it was without groups: moving its four values inside it kept them in four more registers,
    // which every call saved and restored (R4c, 25 more instructions and about 3 µs in the curves scene).
    private static CurveCommand Moved(CommandTag tag, in CurveCommand command, Vector4 move) =>
        command with
        {
            A = command.A - move.X,
            B = command.B - move.Y,
            C = tag == CommandTag.Line ? command.C - move.X : command.C,
            D = tag == CommandTag.Line ? command.D - move.Y : command.D,
            Box = command.Box - move,
        };

    // A glyph run's instances and the rows of its gradient, less `move` (a layer's origin, or zero).
    // Returns the next instance. Inlined into the loop as it was before groups: the native compiler kept it as a call, which
    // the R4c profile of the curves scene showed as its own function.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int WriteGlyphRun<TMode>(
        in GlyphRunCommand run,
        Span<QuadInstance> destination,
        int i,
        Span<Vector4> shapes,
        ref int shape,
        ref int draws,
        float alpha,
        Vector4 move
    )
        where TMode : struct, IGroupMode
    {
        bool clearType = run.Coverage == GlyphCoverage.Rgb;
        uint paintRow = QuadInstance.NoShape;
        if (run.Brush >= 0)
        {
            paintRow = (uint)shape;
            shapes.Slice(shape, ShapeRows).Clear();
            shapes[shape + 3] = new Vector4(TextKind, BrushRow(run.Brush), 0, 0);
            shapes[shape + 4] = Moved<TMode>(run.Box, move);
            shape += ShapeRows;
        }

        PixelKind pixel =
            run.Brush >= 0
                ? (
                    clearType ? PixelKind.ClearTypeText
                    : TMode.Groups && alpha < 1 ? PixelKind.TextFaded
                    : PixelKind.Text
                )
                : (clearType ? PixelKind.ClearType : PixelKind.Main);
        draws = AddToDraw(draws, i, pixel);
        uint color = Faded<TMode>(run.Color, alpha);
        uint kind = run.Coverage == GlyphCoverage.Color ? QuadInstance.ColorPage : 0;
        foreach (GlyphInstance glyph in run.Glyphs)
        {
            destination[i++] = new QuadInstance
            {
                Rect = Moved<TMode>(new Vector4(glyph.X, glyph.Y, glyph.Width, glyph.Height), move),
                Color = color,
                AtlasOrigin = (uint)glyph.AtlasX | ((uint)glyph.AtlasY << 16),
                Page = (uint)glyph.Page | kind,
                Shape = paintRow,
            };
        }

        return i;
    }

    // Reads, and draws nowhere, the rest of a group whose layer lies outside its target, up to its Pop.
    private static void SkipGroup(ref DisplayListReader reader)
    {
        int open = 1;
        while (open > 0 && reader.TryNext(out CommandTag tag))
        {
            open +=
                tag == CommandTag.PushOpacity ? 1
                : tag == CommandTag.Pop ? -1
                : 0;
            Discard(ref reader, tag);
        }
    }

    // A color with its alpha times a group's opacity that was given to its children (row R4c). A gradient's instance color is
    // opaque white, so that its alpha is that opacity, which the shaders multiply the gradient by. The inlining keeps the
    // list without groups free of a call: the native compiler did not inline it on its own (R4c profile).
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint Faded<TMode>(Color color, float alpha)
        where TMode : struct, IGroupMode =>
        !TMode.Groups || alpha == 1 ? color.Packed : new Color(color.R, color.G, color.B, (byte)MathF.Round(color.A * alpha)).Packed;

    // A position less a layer's origin, in a list with groups.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector4 Moved<TMode>(Vector4 position, Vector4 move)
        where TMode : struct, IGroupMode => TMode.Groups ? position - move : position;

    // Reads a command of a layer that lies outside its target, which draws nothing.
    private static void Discard(ref DisplayListReader reader, CommandTag tag)
    {
        switch (tag)
        {
            case CommandTag.FillRect:
                _ = reader.ReadFillRect();
                break;
            case CommandTag.Shape:
                _ = reader.ReadShape();
                break;
            case CommandTag.Shadow:
                _ = reader.ReadShadow();
                break;
            case CommandTag.GlyphRun:
                _ = reader.ReadGlyphRun();
                break;
            case CommandTag.PushOpacity:
                _ = reader.ReadGroup();
                break;
            case CommandTag.Pop:
                reader.ReadPop();
                break;
            default:
                _ = reader.ReadCurve();
                break;
        }
    }

    // CSS's spread radius: a rounded corner grows with the spread (and shrinks with a negative one); a square corner stays square.
    private static float Grown(float radius, float spread) => radius > 0 ? MathF.Max(0, radius + spread) : 0;

    private static Vector4 Radii(in CornerRadii radii) => new(radii.TopLeft, radii.TopRight, radii.BottomRight, radii.BottomLeft);

    private static Vector4 Straight(Color color) => new Vector4(color.R, color.G, color.B, color.A) / 255f;

    private float BrushRow(int brush) => brush < 0 ? -1 : _brushStarts[brush];

    // Each brush's rows, as QuadShaders' GradientColor reads them: the kind, the color space, the number of stops, and the angle
    // in radians; the center and the radius; then each stop's color, premultiplied in the brush's color space, and its offset.
    private void WriteBrushes(ReadOnlySpan<GradientBrush> brushes)
    {
        int rows = 0;
        foreach (GradientBrush brush in brushes)
        {
            rows += 2 + (2 * brush.Stops.Length);
        }

        if (_brushStarts.Length < brushes.Length)
        {
            Array.Resize(ref _brushStarts, Math.Max(brushes.Length, _brushStarts.Length * 2));
        }

        Span<Vector4> destination = _brushRows.Map(_device, _context, rows);
        int row = 0;
        for (int b = 0; b < brushes.Length; b++)
        {
            GradientBrush brush = brushes[b];
            _brushStarts[b] = row;
            ReadOnlySpan<GradientStop> stops = brush.Stops;
            bool oklab = brush.ColorSpace == GradientColorSpace.Oklab;
            destination[row++] = new Vector4((float)brush.Kind, oklab ? 1 : 0, stops.Length, brush.Angle * (MathF.PI / 180));
            destination[row++] = new Vector4(brush.Center.X, brush.Center.Y, brush.Radius, 0);
            foreach (GradientStop stop in stops)
            {
                destination[row++] = GradientColors.Premultiplied(stop.Color, oklab);
                destination[row++] = new Vector4(stop.Offset, 0, 0, 0);
            }
        }

        _brushRows.Unmap(_context);
    }

    // The draw of the instance at `start`: the last one when its pixel shader (and, for a composite, its layer) is the same, else
    // a new one that ends the last.
    private int AddToDraw(int draws, int start, PixelKind pixel, int layer = -1)
    {
        // One comparison a command: the key holds the operation, the pixel shader, and a composite's layer.
        int key = DrawKey(DrawOp.Draw, pixel, layer);
        if (draws > 0 && _draws[draws - 1].Key == key)
        {
            return draws;
        }

        return AddRange(
            draws,
            start,
            new DrawRange
            {
                Start = start,
                Pixel = pixel,
                Layer = layer,
                Key = key,
            }
        );
    }

    private static int DrawKey(DrawOp op, PixelKind pixel, int layer) => (int)op | ((int)pixel << 4) | ((layer + 1) << 12);

    // The start or the end of a layer at the instance `start`, ending the draw before it.
    private int AddOp(int draws, int start, DrawOp op, int layer, Vector4 bounds) =>
        AddRange(
            draws,
            start,
            new DrawRange
            {
                Start = start,
                Op = op,
                Layer = layer,
                Bounds = bounds,
                Key = DrawKey(op, PixelKind.Main, layer),
            }
        );

    private int AddRange(int draws, int start, DrawRange range)
    {
        if (draws > 0 && _draws[draws - 1].Op == DrawOp.Draw)
        {
            ref DrawRange last = ref _draws[draws - 1];
            last.Count = start - last.Start;
        }

        if (draws == _draws.Length)
        {
            GrowDraws();
        }

        _draws[draws] = range;
        return draws + 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void GrowDraws() => Array.Resize(ref _draws, _draws.Length * 2);

    private static void Release(IUnknown* reference)
    {
        if (reference is not null)
        {
            _ = reference->Release();
        }
    }

    [DoesNotReturn]
    [StackTraceHidden]
    private static void ThrowNoRenderTarget() =>
        throw new InvalidOperationException("The renderer has no render target since a resize failed; dispose it and create another.");

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void GrowInstances(int needed)
    {
        int capacity = Math.Max(needed, _instanceCapacity * 2);
        if (_instances is not null)
        {
            _ = _instances->Release();
        }

        _instances = CreateBuffer(_device, capacity * sizeof(QuadInstance), D3D11_BIND_FLAG.D3D11_BIND_VERTEX_BUFFER);
        _instanceCapacity = capacity;
    }

    // The pixel shader of a draw: the rectangles', shapes', and grayscale and color glyphs'; ClearType glyphs'; lines', ellipses',
    // and arcs'; the two for glyphs with a gradient; a layer's composite; and the variants (Faded) that multiply a gradient by the
    // opacity a group gave its children (row R4c). Each kind added in row R4b has a shader of its own, so that the main one
    // does not grow with it (docs/benchmarks/r4b-curves.md): a draw starts where the kind changes.
    private enum PixelKind : byte
    {
        Main,
        ClearType,
        Curve,
        Text,
        ClearTypeText,
        Layer,
        Faded,
        CurveFaded,
        TextFaded,
    }

    // A range of the instance buffer drawn with one pixel shader and one blend state; or the start of a layer, which makes the
    // layer's texture the target, or its end, which makes its parent the target again (row R4c). A composite's draw names its layer.
    private struct DrawRange
    {
        public int Start;
        public int Count;
        public PixelKind Pixel;
        public DrawOp Op;
        public int Layer;
        public Vector4 Bounds;
        public int Key;
    }

    /// <summary>
    /// Whether the list being written has opacity groups: the instance loop and the writes are specialized for each (row R4c).
    /// </summary>
    private interface IGroupMode
    {
        /// <summary>
        /// Gets a value indicating whether the list has opacity groups.
        /// </summary>
        static abstract bool Groups { get; }
    }

    // A list with no opacity group: no alpha, no layer.
    private readonly struct NoGroups : IGroupMode
    {
        public static bool Groups => false;
    }

    // A list with opacity groups.
    private readonly struct WithGroups : IGroupMode
    {
        public static bool Groups => true;
    }

    // What a DrawRange does.
    private enum DrawOp : byte
    {
        Draw,
        BeginLayer,
        EndLayer,
    }

    // An opacity group open while the instances are written: the alpha outside it, its own alpha, the target outside it, and its
    // layer (-1 when its alpha is given to its children).
    private struct GroupState
    {
        public float SavedAlpha;
        public float Alpha;
        public Vector4 SavedTarget;
        public int Layer;
        public Vector4 Bounds;
    }

    // The texture array of one kind of the atlas's pages, a slice per page, and the id of the page each slice holds.
    private sealed class AtlasTextures(GlyphCoverage coverage, DXGI_FORMAT format)
    {
        private ID3D11Texture2D* _texture;
        private int _pageSize;
        private long[] _pageIds = [];

        public ID3D11ShaderResourceView* View { get; private set; }

        public int Slices { get; private set; }

        public int PageUploads { get; private set; }

        public int RegionUploads { get; private set; }

        // The kind's changes since the last upload (design §5.0.3). The array is made again, keeping the slices that are still
        // valid, when the kind has more pages than slices, or when its extra pages were dropped (so that the GPU memory goes back to
        // MaxPages pages, design §5.3).
        public void Upload(ID3D11Device* device, ID3D11DeviceContext* context, GlyphAtlas atlas)
        {
            int pages = atlas.GetPageCount(coverage);
            if (pages == 0)
            {
                return;
            }

            int pageSize = atlas.GetPageSize(coverage);
            int slices = Math.Max(pages, atlas.GetMaxPages(coverage));
            if (_texture is null || pageSize != _pageSize || pages > Slices || Slices > slices)
            {
                Resize(device, context, pageSize, _texture is null || pages > Slices ? pages : slices);
            }

            UploadPages(context, atlas, pages);
        }

        public void Dispose()
        {
            Release((IUnknown*)View);
            Release((IUnknown*)_texture);
            View = null;
            _texture = null;
        }

        private void UploadPages(ID3D11DeviceContext* context, GlyphAtlas atlas, int pages)
        {
            int rowBytes = _pageSize * GlyphAtlas.BytesPerPixel(coverage);
            int bytesPerPixel = GlyphAtlas.BytesPerPixel(coverage);

            // Whole pages first: a page whose id changed is another page (new, or dropped and added again); its regions are in it.
            ulong wholePages = 0;
            for (int page = 0; page < pages; page++)
            {
                long id = atlas.GetPageId(coverage, page);
                if (_pageIds[page] == id)
                {
                    continue;
                }

                fixed (byte* pixels = atlas.GetPagePixels(coverage, page))
                {
                    context->UpdateSubresource((ID3D11Resource*)_texture, (uint)page, null, pixels, (uint)rowBytes, 0);
                }

                _pageIds[page] = id;
                wholePages |= page < 64 ? 1UL << page : 0;
                PageUploads++;
            }

            foreach (AtlasRegion region in atlas.DirtyRegions)
            {
                if (region.Coverage != coverage || (region.Page < 64 && (wholePages & (1UL << region.Page)) != 0))
                {
                    continue;
                }

                var box = new D3D11_BOX(region.X, region.Y, 0, region.X + region.Width, region.Y + region.Height, 1);
                fixed (byte* pixels = atlas.GetPagePixels(coverage, region.Page))
                {
                    byte* first = pixels + ((long)region.Y * rowBytes) + ((long)region.X * bytesPerPixel);
                    context->UpdateSubresource((ID3D11Resource*)_texture, (uint)region.Page, &box, first, (uint)rowBytes, 0);
                }

                RegionUploads++;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void Resize(ID3D11Device* device, ID3D11DeviceContext* context, int pageSize, int slices)
        {
            var description = new D3D11_TEXTURE2D_DESC
            {
                Width = (uint)pageSize,
                Height = (uint)pageSize,
                MipLevels = 1,
                ArraySize = (uint)slices,
                Format = format,
                SampleDesc = new DXGI_SAMPLE_DESC(1, 0),
                Usage = D3D11_USAGE.D3D11_USAGE_DEFAULT,
                BindFlags = (uint)D3D11_BIND_FLAG.D3D11_BIND_SHADER_RESOURCE,
            };
            ID3D11Texture2D* texture;
            HResults.ThrowIfFailed(device->CreateTexture2D(&description, null, &texture), "ID3D11Device::CreateTexture2D");
            ID3D11ShaderResourceView* view;
            HRESULT result = device->CreateShaderResourceView((ID3D11Resource*)texture, null, &view);
            if (result < 0)
            {
                _ = texture->Release();
                HResults.ThrowIfFailed(result, "ID3D11Device::CreateShaderResourceView");
            }

            // The slices that stay valid are copied on the GPU; the others are uploaded whole by UploadPages (their id is 0).
            long[] ids = new long[slices];
            if (_texture is not null && pageSize == _pageSize)
            {
                int kept = Math.Min(slices, Slices);
                for (int slice = 0; slice < kept; slice++)
                {
                    context->CopySubresourceRegion((ID3D11Resource*)texture, (uint)slice, 0, 0, 0, (ID3D11Resource*)_texture, (uint)slice, null);
                }

                Array.Copy(_pageIds, ids, kept);
            }

            Dispose();
            _texture = texture;
            View = view;
            _pageSize = pageSize;
            Slices = slices;
            _pageIds = ids;
        }
    }
}
