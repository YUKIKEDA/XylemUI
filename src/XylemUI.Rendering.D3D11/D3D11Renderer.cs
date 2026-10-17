using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;
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
/// </remarks>
public sealed unsafe class D3D11Renderer : IDisposable
{
    private const uint SwapChainFlags = (uint)DXGI_SWAP_CHAIN_FLAG.DXGI_SWAP_CHAIN_FLAG_FRAME_LATENCY_WAITABLE_OBJECT;
    private const DXGI_FORMAT BackBufferFormat = DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM;

    private readonly ID3D11Device* _device;
    private readonly ID3D11DeviceContext* _context;
    private readonly IDXGISwapChain2* _swapChain;
    private readonly ID3D11VertexShader* _vertexShader;
    private readonly ID3D11PixelShader* _pixelShader;
    private readonly ID3D11InputLayout* _inputLayout;
    private readonly ID3D11Buffer* _targetConstants;
    private readonly HANDLE _frameLatencyWaitable;
    private ID3D11RenderTargetView* _renderTarget;
    private ID3D11Buffer* _instances;
    private int _instanceCapacity;
    private bool _disposed;

    private D3D11Renderer(ID3D11Device* device, ID3D11DeviceContext* context, bool isWarp, HWND window, int width, int height)
    {
        _device = device;
        _context = context;
        IsWarp = isWarp;
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
            _pixelShader = CreatePixelShader(device);
            _targetConstants = CreateBuffer(device, 16, D3D11_BIND_FLAG.D3D11_BIND_CONSTANT_BUFFER);
            WriteTargetConstants();
            GrowInstances(256);
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
    /// Creates a renderer for the client area of <paramref name="window"/>.
    /// </summary>
    /// <param name="window">The window handle (<c>HWND</c>).</param>
    /// <param name="width">The client width, in physical pixels.</param>
    /// <param name="height">The client height, in physical pixels.</param>
    /// <param name="useWarp"><see langword="true"/> to use the WARP software rasterizer even when a GPU is present.</param>
    /// <returns>The renderer.</returns>
    /// <exception cref="InvalidOperationException">The calling thread is not the UI thread.</exception>
    /// <exception cref="InvalidOperationException">Direct3D could not create the device, the swap chain, or the shaders.</exception>
    public static D3D11Renderer Create(nint window, int width, int height, bool useWarp = false)
    {
        UiThread.VerifyAccess();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        // A hardware device first; WARP when there is none (a VM, a remote session) or when asked.
        bool isWarp = useWarp;
        if (!useWarp && TryCreateDevice(D3D_DRIVER_TYPE_HARDWARE, out ID3D11Device* device, out ID3D11DeviceContext* context) >= 0)
        {
            return new D3D11Renderer(device, context, isWarp: false, (HWND)window, width, height);
        }

        isWarp = true;
        HResults.ThrowIfFailed(TryCreateDevice(D3D_DRIVER_TYPE_WARP, out device, out context), "D3D11CreateDevice (WARP)");
        return new D3D11Renderer(device, context, isWarp, (HWND)window, width, height);
    }

    /// <summary>
    /// Draws the display list over a cleared back buffer and presents it, synchronized to the display's refresh.
    /// </summary>
    /// <param name="list">The commands of the frame.</param>
    /// <param name="clearColor">The color of the area no command covers.</param>
    /// <exception cref="InvalidOperationException">The calling thread is not the UI thread.</exception>
    /// <exception cref="GpuDeviceLostException">The device was lost.</exception>
    public void Render(DisplayList list, Color clearColor)
    {
        Draw(list, clearColor);
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

        Release((IUnknown*)_instances);
        Release((IUnknown*)_targetConstants);
        Release((IUnknown*)_inputLayout);
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
    /// Writes the instances of the display list and records the draw into the back buffer, without presenting.
    /// </summary>
    /// <param name="list">The commands of the frame.</param>
    /// <param name="clearColor">The color of the area no command covers.</param>
    /// <exception cref="InvalidOperationException">The calling thread is not the UI thread.</exception>
    public void Draw(DisplayList list, Color clearColor)
    {
        ArgumentNullException.ThrowIfNull(list);
        UiThread.VerifyAccess();
        if (_renderTarget is null)
        {
            ThrowNoRenderTarget();
        }

        int count = list.CommandCount;
        if (count > _instanceCapacity)
        {
            GrowInstances(count);
        }

        if (count > 0)
        {
            WriteInstances(list, count);
        }

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
        if (count == 0)
        {
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
        _context->PSSetShader(_pixelShader, null, 0);
        _context->DrawInstanced(4, (uint)count, 0, 0);
    }

    /// <summary>
    /// Presents the back buffer, waiting for the display's refresh.
    /// </summary>
    /// <exception cref="InvalidOperationException">The calling thread is not the UI thread.</exception>
    /// <exception cref="GpuDeviceLostException">The device was lost.</exception>
    public void Present()
    {
        UiThread.VerifyAccess();
        HRESULT result = _swapChain->Present(1, 0);
        HResults.ThrowIfFailed(result, "IDXGISwapChain::Present", RemovedReason(result));
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
        ID3D11Texture2D* backBuffer;
        HResults.ThrowIfFailed(_swapChain->GetBuffer(0, __uuidof<ID3D11Texture2D>(), (void**)&backBuffer), "IDXGISwapChain::GetBuffer");
        D3D11_TEXTURE2D_DESC description;
        backBuffer->GetDesc(&description);
        description.Usage = D3D11_USAGE.D3D11_USAGE_STAGING;
        description.BindFlags = 0;
        description.CPUAccessFlags = (uint)D3D11_CPU_ACCESS_FLAG.D3D11_CPU_ACCESS_READ;
        description.MiscFlags = 0;
        ID3D11Texture2D* staging;
        HResults.ThrowIfFailed(_device->CreateTexture2D(&description, null, &staging), "ID3D11Device::CreateTexture2D");
        try
        {
            _context->CopyResource((ID3D11Resource*)staging, (ID3D11Resource*)backBuffer);
            D3D11_MAPPED_SUBRESOURCE mapped;
            HResults.ThrowIfFailed(_context->Map((ID3D11Resource*)staging, 0, D3D11_MAP.D3D11_MAP_READ, 0, &mapped), "ID3D11DeviceContext::Map");
            byte* pixel = (byte*)mapped.pData + (y * mapped.RowPitch) + (x * 4);
            var color = new Color(pixel[2], pixel[1], pixel[0], pixel[3]);
            _context->Unmap((ID3D11Resource*)staging, 0);
            return color;
        }
        finally
        {
            _ = staging->Release();
            _ = backBuffer->Release();
        }
    }

    private static HRESULT TryCreateDevice(D3D_DRIVER_TYPE driver, out ID3D11Device* device, out ID3D11DeviceContext* context)
    {
        D3D_FEATURE_LEVEL* levels = stackalloc D3D_FEATURE_LEVEL[] { D3D_FEATURE_LEVEL_11_0, D3D_FEATURE_LEVEL_10_1, D3D_FEATURE_LEVEL_10_0 };
        uint flags = (uint)D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_BGRA_SUPPORT;
        ID3D11Device* createdDevice;
        ID3D11DeviceContext* createdContext;
        HRESULT result = D3D11CreateDevice(
            null,
            driver,
            HMODULE.NULL,
            flags,
            levels,
            3,
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

    private static ID3DBlob* Compile(ReadOnlySpan<byte> entry, ReadOnlySpan<byte> target)
    {
        ReadOnlySpan<byte> source = QuadShaders.Source;
        ID3DBlob* code;
        ID3DBlob* errors = null;
        HRESULT result;
        fixed (byte* sourcePointer = source)
        {
            fixed (byte* entryPointer = entry)
            {
                fixed (byte* targetPointer = target)
                {
                    result = D3DCompile(
                        sourcePointer,
                        (nuint)source.Length,
                        null,
                        null,
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

            // Both semantic names in one pinned literal: "RECT" at 0, "COLOR" at 5.
            fixed (byte* names = "RECT\0COLOR\0"u8)
            {
                byte* rect = names;
                byte* color = names + 5;
                D3D11_INPUT_ELEMENT_DESC* elements = stackalloc D3D11_INPUT_ELEMENT_DESC[2];
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
                ID3D11InputLayout* layout;
                HResults.ThrowIfFailed(
                    device->CreateInputLayout(elements, 2, code->GetBufferPointer(), code->GetBufferSize(), &layout),
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

    private static ID3D11PixelShader* CreatePixelShader(ID3D11Device* device)
    {
        ID3DBlob* code = Compile(QuadShaders.PixelEntry, QuadShaders.PixelTarget);
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

    private void WriteTargetConstants()
    {
        D3D11_MAPPED_SUBRESOURCE mapped;
        HResults.ThrowIfFailed(
            _context->Map((ID3D11Resource*)_targetConstants, 0, D3D11_MAP.D3D11_MAP_WRITE_DISCARD, 0, &mapped),
            "ID3D11DeviceContext::Map"
        );
        *(Vector4*)mapped.pData = new Vector4(1f / Width, 1f / Height, 0, 0);
        _context->Unmap((ID3D11Resource*)_targetConstants, 0);
    }

    // The instances go straight into the mapped buffer; WRITE_DISCARD gives a fresh buffer while the GPU still reads the last one.
    private void WriteInstances(DisplayList list, int count)
    {
        D3D11_MAPPED_SUBRESOURCE mapped;
        HResults.ThrowIfFailed(
            _context->Map((ID3D11Resource*)_instances, 0, D3D11_MAP.D3D11_MAP_WRITE_DISCARD, 0, &mapped),
            "ID3D11DeviceContext::Map"
        );
        try
        {
            var destination = new Span<QuadInstance>(mapped.pData, count);
            DisplayListReader reader = list.GetReader();
            int i = 0;
            while (reader.TryNext(out CommandTag _))
            {
                FillRectCommand rect = reader.ReadFillRect();
                destination[i++] = new QuadInstance { Rect = new Vector4(rect.X, rect.Y, rect.Width, rect.Height), Color = rect.Color.Packed };
            }
        }
        finally
        {
            _context->Unmap((ID3D11Resource*)_instances, 0);
        }
    }

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
}
