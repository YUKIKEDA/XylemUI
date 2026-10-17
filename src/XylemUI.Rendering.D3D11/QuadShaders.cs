namespace XylemUI.Rendering.D3D11;

/// <summary>
/// The HLSL source of the solid-rectangle shaders, compiled at run time by <c>D3DCompile</c> in this spike (design §5.2; compiling
/// at build time is roadmap row C0).
/// </summary>
/// <remarks>
/// Each instance is one rectangle; the vertex shader makes its four corners from <c>SV_VertexID</c> (a triangle strip of 4
/// vertices), so there is no vertex buffer, only the instance buffer.
/// </remarks>
internal static class QuadShaders
{
    /// <summary>
    /// Gets the source, UTF-8.
    /// </summary>
    public static ReadOnlySpan<byte> Source =>
        """
            cbuffer Target : register(b0) { float2 InverseSize; float2 Unused; };
            struct VertexIn { float4 rect : RECT; uint color : COLOR; uint vertexId : SV_VertexID; };
            struct VertexOut { float4 position : SV_Position; float4 color : COLOR; };
            VertexOut VS(VertexIn input)
            {
                float2 corner = float2(input.vertexId & 1, input.vertexId >> 1);
                float2 pixel = input.rect.xy + corner * input.rect.zw;
                VertexOut output;
                output.position = float4(pixel.x * InverseSize.x * 2 - 1, 1 - pixel.y * InverseSize.y * 2, 0, 1);
                uint c = input.color;
                output.color = float4(c & 0xFF, (c >> 8) & 0xFF, (c >> 16) & 0xFF, c >> 24) / 255.0;
                return output;
            }
            float4 PS(VertexOut input) : SV_Target { return input.color; }
            """u8;

    /// <summary>
    /// Gets the vertex shader's entry point, NUL-terminated.
    /// </summary>
    public static ReadOnlySpan<byte> VertexEntry => "VS\0"u8;

    /// <summary>
    /// Gets the pixel shader's entry point, NUL-terminated.
    /// </summary>
    public static ReadOnlySpan<byte> PixelEntry => "PS\0"u8;

    /// <summary>
    /// Gets the vertex shader model, NUL-terminated (feature level 10.0 and up).
    /// </summary>
    public static ReadOnlySpan<byte> VertexTarget => "vs_4_0\0"u8;

    /// <summary>
    /// Gets the pixel shader model, NUL-terminated.
    /// </summary>
    public static ReadOnlySpan<byte> PixelTarget => "ps_4_0\0"u8;
}
