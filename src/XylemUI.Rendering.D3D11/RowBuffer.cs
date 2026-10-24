using System.Numerics;
using System.Runtime.CompilerServices;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;

namespace XylemUI.Rendering.D3D11;

/// <summary>
/// A dynamic buffer of rows of four floats that the pixel shader reads as <c>Buffer&lt;float4&gt;</c> (roadmap row R4): the
/// values of the shapes and the stops of the gradients. A typed buffer, unlike a structured one, is read by pixel shaders at
/// feature level 10.0. It grows to twice its rows when a frame needs more, and its view is made again.
/// </summary>
internal unsafe struct RowBuffer : IDisposable
{
    private ID3D11Buffer* _buffer;
    private ID3D11ShaderResourceView* _view;
    private int _rows;

    /// <summary>
    /// Gets the view the pixel shader reads.
    /// </summary>
    public readonly ID3D11ShaderResourceView* View => _view;

    /// <summary>
    /// Returns a buffer of the given rows.
    /// </summary>
    /// <param name="device">The device.</param>
    /// <param name="rows">The rows it holds before it first grows.</param>
    /// <returns>The buffer.</returns>
    public static RowBuffer Create(ID3D11Device* device, int rows)
    {
        var buffer = default(RowBuffer);
        buffer.Allocate(device, rows);
        return buffer;
    }

    /// <summary>
    /// Maps the buffer for this frame, growing it first when it holds fewer rows than asked for.
    /// </summary>
    /// <param name="device">The device, to grow the buffer.</param>
    /// <param name="context">The immediate context.</param>
    /// <param name="rows">The rows the frame writes.</param>
    /// <returns>The rows, to write before <see cref="Unmap"/>.</returns>
    public Span<Vector4> Map(ID3D11Device* device, ID3D11DeviceContext* context, int rows)
    {
        if (rows > _rows)
        {
            Grow(device, rows);
        }

        D3D11_MAPPED_SUBRESOURCE mapped;
        HResults.ThrowIfFailed(context->Map((ID3D11Resource*)_buffer, 0, D3D11_MAP.D3D11_MAP_WRITE_DISCARD, 0, &mapped), "ID3D11DeviceContext::Map");
        return new Span<Vector4>(mapped.pData, rows);
    }

    /// <summary>
    /// Ends the writes of <see cref="Map"/>.
    /// </summary>
    /// <param name="context">The immediate context.</param>
    public readonly void Unmap(ID3D11DeviceContext* context) => context->Unmap((ID3D11Resource*)_buffer, 0);

    /// <summary>
    /// Releases the buffer and its view.
    /// </summary>
    public void Dispose()
    {
        if (_view is not null)
        {
            _ = _view->Release();
            _view = null;
        }

        if (_buffer is not null)
        {
            _ = _buffer->Release();
            _buffer = null;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Grow(ID3D11Device* device, int rows)
    {
        Dispose();
        Allocate(device, Math.Max(rows, _rows * 2));
    }

    private void Allocate(ID3D11Device* device, int rows)
    {
        var description = new D3D11_BUFFER_DESC
        {
            ByteWidth = (uint)(rows * sizeof(Vector4)),
            Usage = D3D11_USAGE.D3D11_USAGE_DYNAMIC,
            BindFlags = (uint)D3D11_BIND_FLAG.D3D11_BIND_SHADER_RESOURCE,
            CPUAccessFlags = (uint)D3D11_CPU_ACCESS_FLAG.D3D11_CPU_ACCESS_WRITE,
        };
        ID3D11Buffer* buffer;
        HResults.ThrowIfFailed(device->CreateBuffer(&description, null, &buffer), "ID3D11Device::CreateBuffer");
        var view = new D3D11_SHADER_RESOURCE_VIEW_DESC
        {
            Format = DXGI_FORMAT.DXGI_FORMAT_R32G32B32A32_FLOAT,
            ViewDimension = D3D_SRV_DIMENSION.D3D11_SRV_DIMENSION_BUFFER,
        };
        view.Buffer.FirstElement = 0;
        view.Buffer.NumElements = (uint)rows;
        ID3D11ShaderResourceView* created;
        HRESULT result = device->CreateShaderResourceView((ID3D11Resource*)buffer, &view, &created);
        if (result < 0)
        {
            _ = buffer->Release();
            HResults.ThrowIfFailed(result, "ID3D11Device::CreateShaderResourceView");
        }

        _buffer = buffer;
        _view = created;
        _rows = rows;
    }
}
