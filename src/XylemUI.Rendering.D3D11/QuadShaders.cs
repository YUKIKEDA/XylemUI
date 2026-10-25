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
/// shape, as CSS's. The rounded rectangle's distance is the largest of the edges' and of the arcs of the corners whose square
/// holds the pixel (GPUI's while every radius fits half the shorter side), so that a radius may pass half a side once the display
/// list has scaled the radii as CSS does; the blur still cuts each radius at half the shorter side, as GPUI's. A border lies over
/// the fill. A gradient interpolates premultiplied colors, in sRGB or in Oklab (Björn Ottosson's matrices), and is dithered
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
                float2 InverseSize; float GrayscaleContrast; float ClearTypeContrast; float4 GammaRatios; float IsBgr; float Unused; float2 TargetOrigin;
            };
            Texture2DArray<float> Atlas : register(t0);
            Texture2DArray<float4> AtlasRgb : register(t1);
            Texture2DArray<float4> AtlasColor : register(t2);
            Buffer<float4> Shapes : register(t3);
            Buffer<float4> Brushes : register(t4);
            Texture2D<float4> Layer : register(t5);
            Buffer<float4> States : register(t6);
            // A shader compiled with FADED (D3D11Renderer's faded variants) takes the opacity of a group that gave it to its children
            // (row R4c): a shape multiplies its color by the opacity in its sixth row, and a gradient on a line or text by the instance
            // color's alpha. Without FADED the source is the shaders' of the lists without groups, compiled to the same code: a bool
            // parameter folded to false instead made the compiler order PS differently, and the shapes scene's GPU time 2 % longer.
            #ifdef FADED
            #define FADE(alpha) * (alpha)
            #else
            #define FADE(alpha)
            #endif
            struct VertexIn
            {
                float4 rect : RECT; uint color : COLOR; uint atlasOrigin : ATLAS; uint page : PAGE; uint shape : SHAPE; uint vertexId : SV_VertexID;
            };
            struct VertexOut { float4 position : SV_Position; float4 color : COLOR; nointerpolation int4 atlas : ATLAS; nointerpolation uint shape : SHAPE; };
            struct ClearTypeOut { float4 foreground : SV_Target0; float4 alpha : SV_Target1; };
            // A command inside a clip or a transform (row R4d) reads its state: StateRows rows from its state's index in States, as
            // D3D11Renderer.WriteState writes them. The vertex shader transforms the quad's corners, grown by a physical pixel. The
            // pixel shaders take the command's own position (`local`) for its distances, scaled by the transform's span for the
            // antialiasing, and multiply by the coverage of the state's rectangle and of each rounded clip. The rectangle is taken by
            // the distance too: four SV_ClipDistance planes (GPUI's shaders.hlsl lines 111 to 115) made the GPU time of 100 clipped
            // cards 204 to 324 us against 118 to 174 us (frames --scopes rect). Shaders compiled without STATED read none of this:
            // the macros below expand to the code of the commands outside every state.
            struct StatedOut
            {
                float4 position : SV_Position; float4 color : COLOR; nointerpolation int4 atlas : ATLAS; nointerpolation uint shape : SHAPE;
                float2 local : LOCAL; nointerpolation float4 rect : QUAD; nointerpolation uint state : STATE; nointerpolation float texelScale : TEXEL;
            };
            #ifdef STATED
            static const uint StateRows = STATE_ROWS;
            static float StateSpan = 1;
            #define VS_OUT StatedOut
            #define AT(input) input.local
            #define AA(d) ((d) * StateSpan)
            #define STATE_BEGIN(input) float clip = BeginState(input);
            #define CLIPPED * clip
            #define GRAY_AT(input) GrayAt(input)
            #define RGB_AT(input) RgbAt(input)
            #define COLOR_AT(input) ColorAt(input)
            #define LAYER_TEXEL(input) int2(input.local - input.rect.xy)
            #else
            #define VS_OUT VertexOut
            #define AT(input) input.position.xy
            #define AA(d) (d)
            #define STATE_BEGIN(input)
            #define CLIPPED
            #define GRAY_AT(input) Atlas.Load(int4(int2(input.position.xy) + input.atlas.xy, input.atlas.z, 0))
            #define RGB_AT(input) AtlasRgb.Load(int4(int2(input.position.xy) + input.atlas.xy, input.atlas.z, 0)).rgb
            #define COLOR_AT(input) AtlasColor.Load(int4(int2(input.position.xy) + input.atlas.xy, input.atlas.z, 0))
            #define LAYER_TEXEL(input) int2(input.position.xy) + input.atlas.xy
            #endif
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
            // Where a pixel lies inside a corner's square (q > 0, q measured from the arc's center toward the corner), the distance to
            // the arc. A sharp corner (radius 0) keeps the distance to its edges.
            float CornerArc(float d, float2 q, float radius) { return all(q > 0) && radius > 0 ? max(d, length(q) - radius) : d; }
            // The signed distance to a rounded rectangle whose radii were scaled as CSS's (the two on a side never sum past it): the
            // largest of the distance to the edges and of the distances to the arcs of the corners whose square holds the pixel. The
            // two corners on a side cannot both hold it, so at most one diagonal pair (top-left and bottom-right, or top-right and
            // bottom-left) does: both of a pair can, when a radius passes half a side. While every radius fits half the shorter side,
            // this is GPUI's formula. It has no branch: a branch here, even one every pixel of a shape takes the same way, made the GPU
            // time of the shapes scene (frames --shapes) 6 to 10 % longer; this costs 5 % over the formula that cuts each radius at half.
            float RoundedRectDistance(float2 p, float4 rect, float4 radii)
            {
                float2 halfSize = rect.zw * 0.5;
                float2 fromCenter = p - (rect.xy + halfSize);
                float2 edge = abs(fromCenter) - halfSize;
                float2 topLeft = -fromCenter - halfSize + radii.x;
                float2 bottomRight = fromCenter - halfSize + radii.z;
                bool falling = all(topLeft > 0) || all(bottomRight > 0);
                float2 first = falling ? topLeft : float2(fromCenter.x, -fromCenter.y) - halfSize + radii.y;
                float2 second = falling ? bottomRight : float2(-fromCenter.x, fromCenter.y) - halfSize + radii.w;
                float d = CornerArc(max(edge.x, edge.y), first, falling ? radii.x : radii.y);
                return CornerArc(d, second, falling ? radii.z : radii.w);
            }
            float Coverage(float2 p, float4 rect, float4 radii) { return saturate(0.5 - AA(RoundedRectDistance(p, rect, radii))); }
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
            // The distance past the ends of a dash, `along` (> 0 beyond them) and `across` (from the middle of the line): a flat cap
            // ends at the end, a square one half the width past it, a round one is the disc of half the width around it.
            float CapDistance(float along, float across, float half, float cap)
            {
                float straight = max(abs(across) - half, along - (cap > 1.5 ? half : 0));
                float round = length(float2(max(along, 0), across)) - half;
                return cap > 0.5 && cap < 1.5 ? round : straight;
            }
            // The dash k of a pattern (length `on`, period, offset) along a line of length `total`: where it starts and ends, cut to
            // the line. It is empty when it lies past the line, or has no length and a flat cap, which draws nothing.
            float2 DashSpan(float k, float on, float period, float offset, float total)
            {
                float start = k * period - offset;
                return float2(max(start, 0), min(start + on, total));
            }
            bool DashDrawn(float2 span, float cap) { return span.y > span.x || (span.y == span.x && cap > 0.5); }
            // A straight line with its caps and dashes. Only the dash at or before p (p taken within the line, so that past an end
            // it is the last or first dash) and the next one can be nearest: the dashes are alike, and their distance grows with
            // the distance along the line.
            float LineDistance(float2 p, float4 ends, float4 stroke, float4 more)
            {
                float2 a = ends.xy;
                float total = more.w;
                float2 u = total > 0 ? (ends.zw - a) / total : float2(1, 0);
                float s = dot(p - a, u);
                float n = dot(p - a, float2(-u.y, u.x));
                float k = floor((clamp(s, 0, total) + more.x) / stroke.w);
                float2 first = DashSpan(k, stroke.z, stroke.w, more.x, total);
                float2 next = DashSpan(k + 1, stroke.z, stroke.w, more.x, total);
                float d1 = DashDrawn(first, stroke.y) ? CapDistance(max(first.x - s, s - first.y), n, stroke.x, stroke.y) : 1e9;
                float d2 = DashDrawn(next, stroke.y) ? CapDistance(max(next.x - s, s - next.y), n, stroke.x, stroke.y) : 1e9;
                return min(d1, d2);
            }
            // A dash of an arc between the lengths span.x and span.y from the arc's start: inside its angles, the distance across the
            // circle; past an end, the cap's distance in that end's frame (along the tangent beyond it, across along the radius).
            float ArcDashDistance(float2 p, float s, float n, float2 span, float start, float r, float4 stroke)
            {
                bool before = s < span.x;
                float at = start + (before ? span.x : span.y) / r;
                float2 radial = float2(sin(at), -cos(at));
                float2 tangent = (before ? -1 : 1) * float2(cos(at), sin(at));
                float2 fromEnd = p - r * radial;
                bool inside = s >= span.x && s <= span.y;
                float along = inside ? max(span.x - s, s - span.y) : dot(fromEnd, tangent);
                float across = inside ? n : dot(fromEnd, radial);
                return DashDrawn(span, stroke.y) ? CapDistance(along, across, stroke.x, stroke.y) : 1e9;
            }
            float ArcDash(float2 q, float s, float n, float k, float r, float4 stroke, float4 more)
            {
                return ArcDashDistance(q, s, n, DashSpan(k, stroke.z, stroke.w, more.x, more.w), more.y, r, stroke);
            }
            // An arc of a circle with its caps and dashes: p's angle from the arc's start, taken in the turn centered on the arc's
            // middle, so that a point just before the start is before it and not near the end. A flat or square cap juts out
            // straight, past the circle, so outside the circle the dash before the nearest one can be nearer: three dashes count.
            // On a whole circle the first and the last dashes meet at the seam (the start), so they are also measured with p's angle
            // taken in the turn centered on the seam: the first from its start, the last from its end, a turn on. When both reach
            // the seam they are one dash across it, with no ends there; a circle without dashes is that one dash, a ring.
            float ArcDistance(float2 p, float4 circle, float4 stroke, float4 more)
            {
                float2 q = p - circle.xy;
                float r = circle.z;
                float total = more.w;
                float relative = atan2(q.x, -q.y) - more.y;
                float fromSeam = relative - 6.28318531 * floor((relative + 3.14159265) / 6.28318531);
                float middle = more.z * 0.5;
                relative -= 6.28318531 * floor((relative - middle + 3.14159265) / 6.28318531);
                float s = relative * r;
                float n = length(q) - r;
                float k = floor((clamp(s, 0, total) + more.x) / stroke.w);
                float d = min(ArcDash(q, s, n, k - 1, r, stroke, more), min(ArcDash(q, s, n, k, r, stroke, more), ArcDash(q, s, n, k + 1, r, stroke, more)));
                float seam = fromSeam * r;
                float first = floor(more.x / stroke.w);
                float last = floor((total + more.x) / stroke.w);
                float across = min(
                    min(ArcDash(q, seam, n, first, r, stroke, more), ArcDash(q, seam, n, first + 1, r, stroke, more)),
                    min(ArcDash(q, seam + total, n, last - 1, r, stroke, more), ArcDash(q, seam + total, n, last, r, stroke, more)));
                float2 firstSpan = DashSpan(first, stroke.z, stroke.w, more.x, total);
                float2 lastSpan = DashSpan(last, stroke.z, stroke.w, more.x, total);
                bool joined = firstSpan.x <= 0 && lastSpan.y >= total && firstSpan.y > firstSpan.x && lastSpan.y > lastSpan.x;
                float2 joinedSpan = float2(lastSpan.x - total, firstSpan.y);
                across = min(across, joined ? ArcDashDistance(q, seam, n, joinedSpan, more.y, r, stroke) : 1e9);
                return more.z > 6.2831 ? min(d, across) : d;
            }
            // The signed distance to an ellipse of radii ab around the origin: Inigo Quilez's iterative solver
            // (https://iquilezles.org/articles/ellipsedist, MIT License), whose Newton steps on the angle of the nearest point need
            // no branch. Five steps, as Flutter's uber_sdf.frag takes; they converge for an ellipse 100 times as wide as it is tall
            // (CurveRenderTests.Ellipse_HundredToOne_IsAtItsTrueDistance).
            float EllipseDistance(float2 p, float2 ab)
            {
                p = abs(p);
                float2 q = ab * (p - ab);
                float2 cs = normalize(q.x < q.y ? float2(0.01, 1) : float2(1, 0.01));
                for (int i = 0; i < 5; i++)
                {
                    float2 u = ab * float2(cs.x, cs.y);
                    float2 v = ab * float2(-cs.y, cs.x);
                    float a = dot(p - u, v);
                    float c = dot(p - u, u) + dot(v, v);
                    float b = sqrt(max(c * c - a * a, 0));
                    cs = float2(cs.x * b - cs.y * a, cs.y * b + cs.x * a) / c;
                }
                float d = length(p - ab * cs);
                return dot(p / ab, p / ab) > 1 ? d : -d;
            }
            // A sector of a circle: Inigo Quilez's pie (MIT License) in the frame whose y runs through the sector's middle.
            float SectorDistance(float2 p, float4 circle, float4 more)
            {
                float middle = more.y + more.z * 0.5;
                float2 q = p - circle.xy;
                float2 local = float2(abs(dot(q, float2(cos(middle), sin(middle)))), dot(q, float2(sin(middle), -cos(middle))));
                float2 c = float2(sin(more.z * 0.5), cos(more.z * 0.5));
                float l = length(local) - circle.z;
                float m = length(local - c * clamp(dot(local, c), 0, circle.z));
                return max(l, m * sign(c.y * local.x - c.x * local.y));
            }
            #ifdef STATED
            // The state's clips at the pixel: the rectangle's coverage (a pixel of a rectangle on whole pixels is in or out), and each
            // rounded clip's, its distance taken in its own space and scaled to physical pixels. Sets the span the commands'
            // distances are scaled by.
            float BeginState(StatedOut input)
            {
                uint row = input.state * StateRows;
                float4 move = States.Load(row + 1);
                float4 rect = States.Load(row + 2);
                StateSpan = move.z;
                float2 physical = input.position.xy + TargetOrigin;
                float2 outside = max(rect.xy - physical, physical - rect.zw);
                float clip = saturate(0.5 - max(outside.x, outside.y));
                int count = int(move.w);
                for (int i = 0; i < count; i++)
                {
                    uint at = row + 3 + 4 * i;
                    float4 inverse = States.Load(at);
                    float4 more = States.Load(at + 1);
                    float2 q = float2(dot(physical, inverse.xz) + more.x, dot(physical, inverse.yw) + more.y);
                    clip *= saturate(0.5 - RoundedRectDistance(q, States.Load(at + 2), States.Load(at + 3)) * more.z);
                }
                return clip;
            }
            // A glyph's texel at the pixel, sampled between the four nearest texels (Flutter samples transformed glyphs so,
            // text_contents.cc lines 325 to 333): the pixel's place in the glyph's quad, in the text's pixels, from the glyph's
            // origin in its page. A texel outside the glyph's box reads 0, as the padding around it in the page does.
            float2 TexelAt(StatedOut input) { return (input.local - input.rect.xy) * input.texelScale - 0.5; }
            bool InGlyph(StatedOut input, int2 texel) { return all(texel >= 0) && all(float2(texel) < input.rect.zw * input.texelScale); }
            float GrayTexel(StatedOut input, int2 texel)
            {
                return InGlyph(input, texel) ? Atlas.Load(int4(texel + input.atlas.xy, input.atlas.z, 0)) : 0;
            }
            float3 RgbTexel(StatedOut input, int2 texel)
            {
                return InGlyph(input, texel) ? AtlasRgb.Load(int4(texel + input.atlas.xy, input.atlas.z, 0)).rgb : 0;
            }
            float4 ColorTexel(StatedOut input, int2 texel)
            {
                return InGlyph(input, texel) ? AtlasColor.Load(int4(texel + input.atlas.xy, input.atlas.z, 0)) : 0;
            }
            float GrayAt(StatedOut input)
            {
                float2 t = TexelAt(input);
                int2 i = int2(floor(t));
                float2 f = t - floor(t);
                float top = lerp(GrayTexel(input, i), GrayTexel(input, i + int2(1, 0)), f.x);
                float bottom = lerp(GrayTexel(input, i + int2(0, 1)), GrayTexel(input, i + int2(1, 1)), f.x);
                return lerp(top, bottom, f.y);
            }
            float3 RgbAt(StatedOut input)
            {
                float2 t = TexelAt(input);
                int2 i = int2(floor(t));
                float2 f = t - floor(t);
                float3 top = lerp(RgbTexel(input, i), RgbTexel(input, i + int2(1, 0)), f.x);
                float3 bottom = lerp(RgbTexel(input, i + int2(0, 1)), RgbTexel(input, i + int2(1, 1)), f.x);
                return lerp(top, bottom, f.y);
            }
            float4 ColorAt(StatedOut input)
            {
                float2 t = TexelAt(input);
                int2 i = int2(floor(t));
                float2 f = t - floor(t);
                float4 top = lerp(ColorTexel(input, i), ColorTexel(input, i + int2(1, 0)), f.x);
                float4 bottom = lerp(ColorTexel(input, i + int2(0, 1)), ColorTexel(input, i + int2(1, 1)), f.x);
                return lerp(top, bottom, f.y);
            }
            #endif
            // A line, an ellipse, an arc, or a sector (row R4b): six rows from the instance's shape row, as D3D11Renderer.WriteCurve
            // writes them. A pixel shader of its own, so that the shapes' and the glyphs' shader does not grow with it: inside PS,
            // it made the GPU time of the frames scene 3 to 4 µs longer and that of the shapes scene 3 %. A line thinner than a pixel
            // covers the share of the pixel it lies on (the second term takes off what is past its far side).
            float4 PSCurve(VS_OUT input) : SV_Target
            {
                STATE_BEGIN(input)
                float2 p = AT(input);
                uint row = input.shape;
                float4 straight = input.color;
                float4 kind = Shapes.Load(row + 3);
                float4 geometry = Shapes.Load(row);
                float4 stroke = Shapes.Load(row + 1);
                float4 more = Shapes.Load(row + 2);
                float4 box = Shapes.Load(row + 4);
                float d;
                if (kind.x < 4.5)
                {
                    d = LineDistance(p, geometry, stroke, more);
                }
                else if (kind.x < 6.5)
                {
                    float e = EllipseDistance(p - geometry.xy, geometry.zw);
                    d = kind.x < 5.5 ? e : abs(e) - stroke.x;
                }
                else if (kind.x < 7.5)
                {
                    d = ArcDistance(p, geometry, stroke, more);
                }
                else
                {
                    d = SectorDistance(p, geometry, more);
                }
                float alpha = saturate(0.5 - AA(d)) - saturate(0.5 - AA(d) - AA(kind.z > 0 ? kind.z : 1e9));
                float4 color = kind.y >= 0 ? GradientColor(int(kind.y), p, box) FADE(straight.a) : Premultiply(straight);
                return color * alpha CLIPPED;
            }
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
                    // A group's opacity given to its children multiplies the shape as a whole, its border over its fill (row R4c).
                    return fill * outer FADE(elementRadii.x);
                }
                float alpha = BlurredRect(p, rect, radii, kind.w);
                float element = Coverage(p, second, elementRadii);
                alpha = kind.x < 2.5 ? alpha * (1 - element) : (1 - alpha) * element;
                return Premultiply(straight) * alpha;
            }
            float4 PS(VS_OUT input) : SV_Target
            {
                STATE_BEGIN(input)
                if (input.shape != 0xFFFFFFFF)
                {
                    return ShapeColor(AT(input), input.shape, input.color) CLIPPED;
                }

                float alpha = input.color.a;
                if (input.atlas.w != 0)
                {
                    return COLOR_AT(input) * alpha CLIPPED;
                }

                if (input.atlas.z >= 0)
                {
                    float coverage = GRAY_AT(input);
                    float brightness = dot(input.color.rgb, float3(0.30, 0.59, 0.11));
                    float k = GrayscaleContrast * saturate(4.0 * (0.75 - brightness));
                    float contrasted = coverage * (k + 1.0) / (coverage * k + 1.0);
                    float4 g = GammaRatios;
                    alpha *= saturate(contrasted + contrasted * (1.0 - contrasted) * ((g.x * brightness + g.y) * contrasted + (g.z * brightness + g.w)));
                }
            #ifdef STATED
                else
                {
                    // A rectangle's edges, which a transform can turn off the pixels, are antialiased as a shape's.
                    alpha *= Coverage(input.local, input.rect, 0);
                }
                alpha *= clip;
            #endif
                return float4(input.color.rgb * alpha, alpha);
            }
            ClearTypeOut PSClearType(VS_OUT input)
            {
                STATE_BEGIN(input)
                float3 coverage = RGB_AT(input);
                coverage = IsBgr > 0.5 ? coverage.bgr : coverage;
                float3 color = input.color.rgb;
                float k = ClearTypeContrast * saturate(4.0 * (0.75 - dot(color, float3(0.30, 0.59, 0.11))));
                float3 contrasted = coverage * (k + 1.0) / (coverage * k + 1.0);
                float4 g = GammaRatios;
                float3 corrected = saturate(contrasted + contrasted * (1.0 - contrasted) * ((g.x * color + g.y) * contrasted + (g.z * color + g.w)));
                ClearTypeOut output;
                output.foreground = float4(color, 1.0);
                output.alpha = float4(input.color.a * corrected CLIPPED, 1.0);
                return output;
            }
            // The premultiplied color of brushed text at the pixel (row R4): the gradient of the run's rows over the run's box.
            float4 TextPaint(VS_OUT input)
            {
                float4 kind = Shapes.Load(input.shape + 3);
                return GradientColor(int(kind.y), AT(input), Shapes.Load(input.shape + 4)) FADE(input.color.a);
            }
            float3 Straight(float4 paint) { return paint.a > 0 ? paint.rgb / paint.a : float3(0, 0, 0); }
            // Grayscale and color glyphs with a gradient: PS's text, with the gradient's color at the pixel for the text's color.
            float4 PSText(VS_OUT input) : SV_Target
            {
                STATE_BEGIN(input)
                float4 paint = TextPaint(input);
                if (input.atlas.w != 0)
                {
                    return COLOR_AT(input) * paint.a CLIPPED;
                }

                float coverage = GRAY_AT(input);
                float brightness = dot(Straight(paint), float3(0.30, 0.59, 0.11));
                float k = GrayscaleContrast * saturate(4.0 * (0.75 - brightness));
                float contrasted = coverage * (k + 1.0) / (coverage * k + 1.0);
                float4 g = GammaRatios;
                return paint * saturate(contrasted + contrasted * (1.0 - contrasted) * ((g.x * brightness + g.y) * contrasted + (g.z * brightness + g.w))) CLIPPED;
            }
            // ClearType glyphs with a gradient: PSClearType, each channel corrected with the gradient's color at the pixel.
            ClearTypeOut PSClearTypeText(VS_OUT input)
            {
                STATE_BEGIN(input)
                float4 paint = TextPaint(input);
                float3 coverage = RGB_AT(input);
                coverage = IsBgr > 0.5 ? coverage.bgr : coverage;
                float3 color = Straight(paint);
                float k = ClearTypeContrast * saturate(4.0 * (0.75 - dot(color, float3(0.30, 0.59, 0.11))));
                float3 contrasted = coverage * (k + 1.0) / (coverage * k + 1.0);
                float4 g = GammaRatios;
                float3 corrected = saturate(contrasted + contrasted * (1.0 - contrasted) * ((g.x * color + g.y) * contrasted + (g.z * color + g.w)));
                ClearTypeOut output;
                output.foreground = float4(color, 1.0);
                output.alpha = float4(paint.a * corrected CLIPPED, 1.0);
                return output;
            }
            // A layer composited into its parent (row R4c): the layer's premultiplied pixels times the group's alpha. The quad is the
            // layer's bounds in its parent's pixels, and its atlas origin is 0, so the texel is the pixel less the bounds' corner.
            float4 PSLayer(VS_OUT input) : SV_Target
            {
                STATE_BEGIN(input)
                return Layer.Load(int3(LAYER_TEXEL(input), 0)) * input.color.a CLIPPED;
            }
            #ifdef STATED
            // The quad of a command in a state: its corners, grown by a physical pixel for the antialiasing of edges the transform
            // turns, through the state's transform. A layer's composite (texelScale 0) is in physical pixels already.
            StatedOut VSStated(VertexIn input, uint state : STATE, float texelScale : TEXELSCALE)
            {
                uint row = state * StateRows;
                float4 axes = States.Load(row);
                float4 move = States.Load(row + 1);
                bool physicalQuad = texelScale == 0;
                // A transform that flattens the plane (span 0) is not grown: its quads have no area.
                float grow = physicalQuad || move.z <= 0 ? 0 : 1 / move.z;
                float2 corner = float2(input.vertexId & 1, input.vertexId >> 1);
                float2 local = input.rect.xy - grow + corner * (input.rect.zw + 2 * grow);
                float2 pixel = physicalQuad ? local : float2(dot(local, axes.xz) + move.x, dot(local, axes.yw) + move.y);
                StatedOut output;
                float2 target = pixel - TargetOrigin;
                output.position = float4(target.x * InverseSize.x * 2 - 1, 1 - target.y * InverseSize.y * 2, 0, 1);
                uint c = input.color;
                output.color = float4(c & 0xFF, (c >> 8) & 0xFF, (c >> 16) & 0xFF, c >> 24) / 255.0;
                bool solid = input.page == 0xFFFFFFFF;
                bool color = !solid && (input.page & 0x40000000) != 0;
                output.atlas = int4(input.atlasOrigin & 0xFFFF, input.atlasOrigin >> 16, solid ? -1 : int(input.page & 0x3FFFFFFF), color ? 1 : 0);
                output.shape = input.shape;
                output.local = local;
                output.rect = input.rect;
                output.state = state;
                output.texelScale = texelScale;
                return output;
            }
            #endif
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
    /// Gets the entry point of the pixel shader of lines, ellipses, arcs, and sectors, NUL-terminated.
    /// </summary>
    public static ReadOnlySpan<byte> CurvePixelEntry => "PSCurve\0"u8;

    /// <summary>
    /// Gets the entry point of the pixel shader of grayscale and color glyphs with a gradient, NUL-terminated.
    /// </summary>
    public static ReadOnlySpan<byte> TextPixelEntry => "PSText\0"u8;

    /// <summary>
    /// Gets the entry point of the pixel shader of ClearType glyphs with a gradient, NUL-terminated.
    /// </summary>
    public static ReadOnlySpan<byte> ClearTypeTextPixelEntry => "PSClearTypeText\0"u8;

    /// <summary>
    /// Gets the entry point of the pixel shader that composites a layer into its parent, NUL-terminated.
    /// </summary>
    public static ReadOnlySpan<byte> LayerPixelEntry => "PSLayer\0"u8;

    /// <summary>
    /// Gets the entry point of the vertex shader of the commands inside a clip or a transform (row R4d), NUL-terminated; it is in
    /// the source compiled with <c>STATED</c>.
    /// </summary>
    public static ReadOnlySpan<byte> StatedVertexEntry => "VSStated\0"u8;

    /// <summary>
    /// Gets the vertex shader model, NUL-terminated (feature level 10.0 and up).
    /// </summary>
    public static ReadOnlySpan<byte> VertexTarget => "vs_4_0\0"u8;

    /// <summary>
    /// Gets the pixel shader model, NUL-terminated.
    /// </summary>
    public static ReadOnlySpan<byte> PixelTarget => "ps_4_0\0"u8;
}
