namespace XylemUI.Rendering.D3D11;

/// <summary>
/// The HLSL source of the quad shaders, compiled at run time by <c>D3DCompile</c> in this spike (design §5.2; compiling at build
/// time is roadmap row C0).
/// </summary>
/// <remarks>
/// <para>
/// Each instance is one quad; the vertex shader makes its four corners from <c>SV_VertexID</c> (a triangle strip of 4 vertices),
/// so there is no vertex buffer, only the instance buffer.
/// </para>
/// <para>
/// A glyph's quad lies on whole pixels and is as large as its coverage, so each pixel loads one texel of its atlas page
/// (<c>Load</c>, no filtering): the texel is the pixel's position less the quad's origin plus the glyph's origin in the page. The
/// coverage is corrected for the text's color with the gamma and contrast of Windows (<see cref="TextGamma"/>, design §5.0.3)
/// and scales the color's alpha (grayscale anti-aliasing). The output is premultiplied, for the blend state
/// <c>ONE, INV_SRC_ALPHA</c>.
/// </para>
/// <para>
/// The correction is GPUI's (Zed v1.24.3, <c>crates/gpui_windows/src/alpha_correction.hlsl</c>, function
/// <c>apply_contrast_and_gamma_correction</c>), adapted from Windows Terminal's <c>DWrite_GrayscaleBlend</c>
/// (<c>src/renderer/atlas/dwrite.hlsl</c> at commit 1283c0f; Copyright (c) Microsoft Corporation, MIT License). Windows Terminal
/// weighs the color 0.25, 0.5, 0.25 for the gamma correction; GPUI, and this shader, weigh it 0.30, 0.59, 0.11 for both steps.
/// The corrected coverage is clamped to 0..1, so that the premultiplied output stays in range.
/// </para>
/// </remarks>
internal static class QuadShaders
{
    /// <summary>
    /// Gets the source, UTF-8.
    /// </summary>
    public static ReadOnlySpan<byte> Source =>
        """
            cbuffer Target : register(b0) { float2 InverseSize; float GrayscaleContrast; float Unused; float4 GammaRatios; };
            Texture2DArray<float> Atlas : register(t0);
            struct VertexIn
            {
                float4 rect : RECT; uint color : COLOR; uint atlasOrigin : ATLAS; uint page : PAGE; uint vertexId : SV_VertexID;
            };
            struct VertexOut { float4 position : SV_Position; float4 color : COLOR; nointerpolation int3 atlas : ATLAS; };
            VertexOut VS(VertexIn input)
            {
                float2 corner = float2(input.vertexId & 1, input.vertexId >> 1);
                float2 pixel = input.rect.xy + corner * input.rect.zw;
                VertexOut output;
                output.position = float4(pixel.x * InverseSize.x * 2 - 1, 1 - pixel.y * InverseSize.y * 2, 0, 1);
                uint c = input.color;
                output.color = float4(c & 0xFF, (c >> 8) & 0xFF, (c >> 16) & 0xFF, c >> 24) / 255.0;
                int2 origin = int2(input.atlasOrigin & 0xFFFF, input.atlasOrigin >> 16) - int2(input.rect.xy);
                output.atlas = int3(origin, input.page == 0xFFFFFFFF ? -1 : int(input.page));
                return output;
            }
            float4 PS(VertexOut input) : SV_Target
            {
                float alpha = input.color.a;
                if (input.atlas.z >= 0)
                {
                    float coverage = Atlas.Load(int4(int2(input.position.xy) + input.atlas.xy, input.atlas.z, 0));
                    float brightness = dot(input.color.rgb, float3(0.30, 0.59, 0.11));
                    float k = GrayscaleContrast * saturate(4.0 * (0.75 - brightness));
                    float contrasted = coverage * (k + 1.0) / (coverage * k + 1.0);
                    float4 g = GammaRatios;
                    alpha *= saturate(contrasted + contrasted * (1.0 - contrasted) * ((g.x * brightness + g.y) * contrasted + (g.z * brightness + g.w)));
                }
                return float4(input.color.rgb * alpha, alpha);
            }
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
