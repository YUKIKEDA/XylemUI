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
/// <para>
/// A color glyph (roadmap row T6, the page's bit 30 set) loads its premultiplied color from the BGRA atlas and scales it by the
/// text's alpha only, with the same blend as the rest: the glyph's own colors are not corrected (design §5.0.3, "Color glyphs").
/// </para>
/// <para>
/// A ClearType glyph (roadmap row T2) loads red, green, and blue coverage from the RGBA atlas, swapped on a BGR monitor, and
/// corrects each channel with the channel of the color: GPUI's <c>apply_contrast_and_gamma_correction3</c> with its ClearType
/// contrast, and its fragment shader (<c>shaders.hlsl</c>, <c>subpixel_sprite_fragment</c>, lines 1194 to 1206). It writes the
/// color and the per-channel alpha as two outputs for the dual-source blend <c>SRC1_COLOR, INV_SRC1_COLOR</c>, which blends each
/// channel with its own alpha and does not write the target's alpha.
/// </para>
/// <para>
/// A rounded rectangle or a shadow (roadmap row R4, design §5.0, "Shapes, brushes, and shadows") reads its values from the shape
/// buffer, six rows from the instance's shape row, and its gradients from the brush buffer. The rounded rectangle's signed distance
/// and the shadow's blur are GPUI's (Zed v1.24.3, <c>crates/gpui_windows/src/shaders.hlsl</c>, <c>quad_sdf</c> lines 265 to 315,
/// <c>erf</c>, <c>blur_along_x</c>, and <c>gaussian</c> lines 229 to 248, <c>shadow_fragment</c> lines 913 to 953; Copyright
/// Zed Industries, Inc., Apache License 2.0): the blur integrates the rounded rectangle along x in closed form with an
/// approximation of erf, and along y with four samples within three standard deviations. An outer shadow is cut out under its
/// shape, as CSS's. The rounded rectangle's distance is taken from the arc of the corner whose square holds the pixel, else from
/// the nearest edge, so that a radius may pass half a side once the display list has scaled the radii as CSS does. A border lies
/// over the fill. A gradient interpolates premultiplied colors, in sRGB or in Oklab (Björn Ottosson's matrices), and is dithered
/// with a triangular noise of one step of 8 bits.
/// </para>
/// </remarks>
internal static class QuadShaders
{
    /// <summary>
    /// Gets the source, UTF-8.
    /// </summary>
    public static ReadOnlySpan<byte> Source =>
        """
            cbuffer Target : register(b0)
            {
                float2 InverseSize; float GrayscaleContrast; float ClearTypeContrast; float4 GammaRatios; float IsBgr; float3 Unused;
            };
            Texture2DArray<float> Atlas : register(t0);
            Texture2DArray<float4> AtlasRgb : register(t1);
            Texture2DArray<float4> AtlasColor : register(t2);
            Buffer<float4> Shapes : register(t3);
            Buffer<float4> Brushes : register(t4);
            struct VertexIn
            {
                float4 rect : RECT; uint color : COLOR; uint atlasOrigin : ATLAS; uint page : PAGE; uint shape : SHAPE; uint vertexId : SV_VertexID;
            };
            struct VertexOut { float4 position : SV_Position; float4 color : COLOR; nointerpolation int4 atlas : ATLAS; nointerpolation uint shape : SHAPE; };
            struct ClearTypeOut { float4 foreground : SV_Target0; float4 alpha : SV_Target1; };
            VertexOut VS(VertexIn input)
            {
                float2 corner = float2(input.vertexId & 1, input.vertexId >> 1);
                float2 pixel = input.rect.xy + corner * input.rect.zw;
                VertexOut output;
                output.position = float4(pixel.x * InverseSize.x * 2 - 1, 1 - pixel.y * InverseSize.y * 2, 0, 1);
                uint c = input.color;
                output.color = float4(c & 0xFF, (c >> 8) & 0xFF, (c >> 16) & 0xFF, c >> 24) / 255.0;
                int2 origin = int2(input.atlasOrigin & 0xFFFF, input.atlasOrigin >> 16) - int2(input.rect.xy);
                bool solid = input.page == 0xFFFFFFFF;
                bool color = !solid && (input.page & 0x40000000) != 0;
                output.atlas = int4(origin, solid ? -1 : int(input.page & 0x3FFFFFFF), color ? 1 : 0);
                output.shape = input.shape;
                return output;
            }
            float CornerRadius(float2 fromCenter, float4 radii)
            {
                return fromCenter.x < 0 ? (fromCenter.y < 0 ? radii.x : radii.w) : (fromCenter.y < 0 ? radii.y : radii.z);
            }
            // The signed distance to a rounded rectangle whose radii were scaled as CSS's (the two on a side never sum past it): inside
            // a corner's square, the distance to its arc; elsewhere, to the nearest edge. A radius may pass half the shorter side.
            float RoundedRectDistance(float2 p, float4 rect, float4 radii)
            {
                float2 lo = rect.xy;
                float2 hi = rect.xy + rect.zw;
                if (p.x < lo.x + radii.x && p.y < lo.y + radii.x) { return length(p - (lo + radii.x)) - radii.x; }
                if (p.x > hi.x - radii.y && p.y < lo.y + radii.y) { return length(p - float2(hi.x - radii.y, lo.y + radii.y)) - radii.y; }
                if (p.x > hi.x - radii.z && p.y > hi.y - radii.z) { return length(p - (hi - radii.z)) - radii.z; }
                if (p.x < lo.x + radii.w && p.y > hi.y - radii.w) { return length(p - float2(lo.x + radii.w, hi.y - radii.w)) - radii.w; }
                return max(max(lo.x - p.x, p.x - hi.x), max(lo.y - p.y, p.y - hi.y));
            }
            float Coverage(float2 p, float4 rect, float4 radii) { return saturate(0.5 - RoundedRectDistance(p, rect, radii)); }
            float2 Erf(float2 x)
            {
                float2 s = sign(x);
                float2 a = abs(x);
                x = 1 + (0.278393 + (0.230389 + 0.078108 * (a * a)) * a) * a;
                x *= x;
                return s - s / (x * x);
            }
            float BlurAlongX(float x, float y, float sigma, float corner, float2 halfSize)
            {
                float delta = min(halfSize.y - corner - abs(y), 0);
                float curved = halfSize.x - corner + sqrt(max(0, corner * corner - delta * delta));
                float2 integral = 0.5 + 0.5 * Erf((x + float2(-curved, curved)) * (sqrt(0.5) / sigma));
                return integral.y - integral.x;
            }
            float Gaussian(float x, float sigma) { return exp(-(x * x) / (2 * sigma * sigma)) / (sqrt(2 * 3.14159265) * sigma); }
            float BlurredRect(float2 p, float4 rect, float4 radii, float sigma)
            {
                if (sigma <= 0) { return Coverage(p, rect, radii); }
                float2 halfSize = rect.zw * 0.5;
                float2 fromCenter = p - (rect.xy + halfSize);
                float corner = min(CornerRadius(fromCenter, radii), min(halfSize.x, halfSize.y));
                float low = fromCenter.y - halfSize.y;
                float high = fromCenter.y + halfSize.y;
                float start = clamp(-3 * sigma, low, high);
                float end = clamp(3 * sigma, low, high);
                float step = (end - start) / 4;
                float y = start + step * 0.5;
                float alpha = 0;
                for (int i = 0; i < 4; i++)
                {
                    alpha += BlurAlongX(fromCenter.x, fromCenter.y - y, sigma, corner, halfSize) * Gaussian(y, sigma) * step;
                    y += step;
                }
                return alpha;
            }
            float3 LinearToSrgb(float3 c) { return c <= 0.0031308 ? c * 12.92 : 1.055 * pow(max(c, 0), 1 / 2.4) - 0.055; }
            float3 OklabToLinear(float3 lab)
            {
                float l = lab.x + 0.3963377774 * lab.y + 0.2158037573 * lab.z;
                float m = lab.x - 0.1055613458 * lab.y - 0.0638541728 * lab.z;
                float s = lab.x - 0.0894841775 * lab.y - 1.2914855480 * lab.z;
                l = l * l * l; m = m * m * m; s = s * s * s;
                return float3(4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s,
                    -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s,
                    -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s);
            }
            float Noise(float2 p) { return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453); }
            // A gradient's premultiplied sRGB color at p in box: header rows (kind, space, stops, angle) and (center, radius), then a
            // premultiplied color and an offset per stop.
            float4 GradientColor(int row, float2 p, float4 box)
            {
                float4 head = Brushes.Load(row);
                float4 shape = Brushes.Load(row + 1);
                int kind = int(head.x);
                int count = int(head.z);
                float2 center = box.xy + shape.xy * box.zw;
                float2 d = p - center;
                float t;
                if (kind == 0)
                {
                    float2 direction = float2(sin(head.w), -cos(head.w));
                    float length = abs(box.z * direction.x) + abs(box.w * direction.y);
                    t = dot(p - (box.xy + 0.5 * box.zw), direction) / length + 0.5;
                }
                else if (kind == 1)
                {
                    t = sqrt(dot(d, d)) / (shape.z * max(box.z, box.w));
                }
                else
                {
                    t = frac((atan2(d.x, -d.y) - head.w) / 6.28318531);
                }
                float4 previous = Brushes.Load(row + 2);
                float before = Brushes.Load(row + 3).x;
                float4 color = previous;
                if (t > before)
                {
                    color = Brushes.Load(row + 2 * count);
                    for (int i = 1; i < count; i++)
                    {
                        float4 next = Brushes.Load(row + 2 + 2 * i);
                        float at = Brushes.Load(row + 3 + 2 * i).x;
                        if (t < at)
                        {
                            color = lerp(previous, next, (t - before) / max(at - before, 1e-6));
                            break;
                        }
                        previous = next;
                        before = at;
                    }
                }
                if (head.y > 0.5 && color.a > 0)
                {
                    color = float4(LinearToSrgb(saturate(OklabToLinear(color.rgb / color.a))) * color.a, color.a);
                }
                float dither = (Noise(p) - Noise(p + 17.3)) / 255;
                return float4(clamp(color.rgb + dither * color.a, 0, color.a), color.a);
            }
            float4 Premultiply(float4 c) { return float4(c.rgb * c.a, c.a); }
            // A rounded rectangle, its border, or a shadow (row R4): six rows from `row`, as D3D11Renderer.WriteShape writes them.
            float4 ShapeColor(float2 p, uint row, float4 straight)
            {
                float4 radii = Shapes.Load(row);
                float4 second = Shapes.Load(row + 1);
                float4 borderColor = Shapes.Load(row + 2);
                float4 kind = Shapes.Load(row + 3);
                float4 rect = Shapes.Load(row + 4);
                float4 elementRadii = Shapes.Load(row + 5);
                if (kind.x < 1.5)
                {
                    float outer = Coverage(p, rect, radii);
                    float4 fill = kind.y >= 0 ? GradientColor(int(kind.y), p, rect) : Premultiply(straight);
                    float4 widths = second;
                    if (any(widths > 0))
                    {
                        float4 inner = float4(rect.xy + widths.xy, rect.zw - widths.xy - widths.zw);
                        float4 innerRadii = max(0, radii - float4(max(widths.x, widths.y), max(widths.z, widths.y), max(widths.z, widths.w), max(widths.x, widths.w)));
                        float inside = inner.z > 0 && inner.w > 0 ? Coverage(p, inner, innerRadii) : 0;
                        float4 border = kind.z >= 0 ? GradientColor(int(kind.z), p, rect) : Premultiply(borderColor);
                        // The border lies over the fill, which covers the whole box, as CSS, Flutter, Avalonia, and GPUI draw it
                        // (GPUI's over, shaders.hlsl lines 840 to 844): a translucent border shows the fill under it.
                        fill = lerp(border + fill * (1 - border.a), fill, inside);
                    }
                    return fill * outer;
                }
                float alpha = BlurredRect(p, rect, radii, kind.w);
                float element = Coverage(p, second, elementRadii);
                alpha = kind.x < 2.5 ? alpha * (1 - element) : (1 - alpha) * element;
                return Premultiply(straight) * alpha;
            }
            float4 PS(VertexOut input) : SV_Target
            {
                if (input.shape != 0xFFFFFFFF)
                {
                    return ShapeColor(input.position.xy, input.shape, input.color);
                }

                float alpha = input.color.a;
                if (input.atlas.w != 0)
                {
                    return AtlasColor.Load(int4(int2(input.position.xy) + input.atlas.xy, input.atlas.z, 0)) * alpha;
                }

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
            ClearTypeOut PSClearType(VertexOut input)
            {
                float3 coverage = AtlasRgb.Load(int4(int2(input.position.xy) + input.atlas.xy, input.atlas.z, 0)).rgb;
                coverage = IsBgr > 0.5 ? coverage.bgr : coverage;
                float3 color = input.color.rgb;
                float k = ClearTypeContrast * saturate(4.0 * (0.75 - dot(color, float3(0.30, 0.59, 0.11))));
                float3 contrasted = coverage * (k + 1.0) / (coverage * k + 1.0);
                float4 g = GammaRatios;
                float3 corrected = saturate(contrasted + contrasted * (1.0 - contrasted) * ((g.x * color + g.y) * contrasted + (g.z * color + g.w)));
                ClearTypeOut output;
                output.foreground = float4(color, 1.0);
                output.alpha = float4(input.color.a * corrected, 1.0);
                return output;
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
    /// Gets the ClearType pixel shader's entry point, NUL-terminated.
    /// </summary>
    public static ReadOnlySpan<byte> ClearTypePixelEntry => "PSClearType\0"u8;

    /// <summary>
    /// Gets the vertex shader model, NUL-terminated (feature level 10.0 and up).
    /// </summary>
    public static ReadOnlySpan<byte> VertexTarget => "vs_4_0\0"u8;

    /// <summary>
    /// Gets the pixel shader model, NUL-terminated.
    /// </summary>
    public static ReadOnlySpan<byte> PixelTarget => "ps_4_0\0"u8;
}
