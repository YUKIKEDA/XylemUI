using TerraFX.Interop.DirectX;
using static TerraFX.Interop.DirectX.D3D11_QUERY;

namespace XylemUI.Rendering.D3D11;

/// <summary>
/// Measures how long the GPU takes for the work of a frame (the uploads and the draws), with timestamp queries read some frames later without waiting
/// (design §11.3, GPU execution time; roadmap row M1a). For the benchmarks; off unless the renderer is asked to measure.
/// </summary>
/// <remarks>
/// A frame brackets its draws with <see cref="Begin"/> and <see cref="End"/>. The results come back in order through
/// <see cref="TryRead"/> once the GPU has them; a frame begun while every slot still waits is not measured, so the CPU never
/// waits for the GPU here. A result whose disjoint query reports a changed clock (a power state change) is dropped.
/// </remarks>
internal sealed unsafe class GpuFrameTimer : IDisposable
{
    private const int Slots = 4;

    private readonly ID3D11DeviceContext* _context;
    private readonly nint[] _disjoint = new nint[Slots];
    private readonly nint[] _start = new nint[Slots];
    private readonly nint[] _end = new nint[Slots];
    private int _oldest;
    private int _pending;
    private bool _open;

    /// <summary>
    /// Initializes a new instance of the <see cref="GpuFrameTimer"/> class with the queries of every slot.
    /// </summary>
    /// <param name="device">The device that creates the queries.</param>
    /// <param name="context">The immediate context the frames are drawn on, which issues and reads the queries.</param>
    /// <exception cref="GpuDeviceLostException">The device was lost while a query was created.</exception>
    /// <exception cref="InvalidOperationException">A query could not be created.</exception>
    public GpuFrameTimer(ID3D11Device* device, ID3D11DeviceContext* context)
    {
        _context = context;
        for (int i = 0; i < Slots; i++)
        {
            _disjoint[i] = CreateQuery(device, D3D11_QUERY_TIMESTAMP_DISJOINT);
            _start[i] = CreateQuery(device, D3D11_QUERY_TIMESTAMP);
            _end[i] = CreateQuery(device, D3D11_QUERY_TIMESTAMP);
        }
    }

    /// <summary>
    /// Gets the number of frames not measured because every slot was still waiting for the GPU.
    /// </summary>
    public int Skipped { get; private set; }

    /// <summary>
    /// Opens the measurement of a frame, before its first command.
    /// </summary>
    public void Begin()
    {
        if (_pending == Slots)
        {
            Skipped++;
            return;
        }

        int slot = (_oldest + _pending) % Slots;
        _context->Begin((ID3D11Asynchronous*)_disjoint[slot]);
        _context->End((ID3D11Asynchronous*)_start[slot]);
        _open = true;
    }

    /// <summary>
    /// Closes the measurement opened by <see cref="Begin"/>, after the frame's last command.
    /// </summary>
    public void End()
    {
        if (!_open)
        {
            return;
        }

        int slot = (_oldest + _pending) % Slots;
        _context->End((ID3D11Asynchronous*)_end[slot]);
        _context->End((ID3D11Asynchronous*)_disjoint[slot]);
        _pending++;
        _open = false;
    }

    /// <summary>
    /// Returns the GPU time of the oldest measured frame, if the GPU has finished it.
    /// </summary>
    /// <param name="milliseconds">The time from the frame's first command to its last, on the GPU.</param>
    /// <returns><see langword="false"/> when no measured frame is finished yet.</returns>
    public bool TryRead(out double milliseconds)
    {
        milliseconds = 0;
        while (_pending > 0)
        {
            int slot = _oldest;
            D3D11_QUERY_DATA_TIMESTAMP_DISJOINT disjoint;
            if (
                _context->GetData(
                    (ID3D11Asynchronous*)_disjoint[slot],
                    &disjoint,
                    (uint)sizeof(D3D11_QUERY_DATA_TIMESTAMP_DISJOINT),
                    (uint)D3D11_ASYNC_GETDATA_FLAG.D3D11_ASYNC_GETDATA_DONOTFLUSH
                ) != 0
            )
            {
                return false;
            }

            ulong start = 0;
            ulong end = 0;
            bool ready =
                _context->GetData((ID3D11Asynchronous*)_start[slot], &start, sizeof(ulong), 0) == 0
                && _context->GetData((ID3D11Asynchronous*)_end[slot], &end, sizeof(ulong), 0) == 0;
            _oldest = (_oldest + 1) % Slots;
            _pending--;
            if (ready && !disjoint.Disjoint && disjoint.Frequency != 0)
            {
                milliseconds = (end - start) * 1000.0 / disjoint.Frequency;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Releases the queries.
    /// </summary>
    public void Dispose()
    {
        for (int i = 0; i < Slots; i++)
        {
            Release(_disjoint[i]);
            Release(_start[i]);
            Release(_end[i]);
        }
    }

    private static nint CreateQuery(ID3D11Device* device, D3D11_QUERY kind)
    {
        var description = new D3D11_QUERY_DESC { Query = kind };
        ID3D11Query* query;
        HResults.ThrowIfFailed(device->CreateQuery(&description, &query), "ID3D11Device::CreateQuery");
        return (nint)query;
    }

    private static void Release(nint query)
    {
        if (query != 0)
        {
            _ = ((ID3D11Query*)query)->Release();
        }
    }
}
