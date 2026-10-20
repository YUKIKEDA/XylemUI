using System.Numerics;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.DirectX.DirectX;

namespace XylemUI.Rendering.D3D11;

/// <summary>
/// The gamma and contrast the glyph shader corrects coverage with (design §5.0.3, roadmap row T4): the user's setting of Windows
/// (the ClearType tuner writes it; <c>IDWriteRenderingParams1</c>), read once for the process, as GPUI and Windows Terminal read
/// it. Applications are not expected to read it again after they start (Windows Terminal, <c>src/renderer/atlas/dwrite.cpp</c>).
/// </summary>
internal readonly unsafe record struct TextGamma(Vector4 Ratios, float GrayscaleContrast)
{
    // The ratios of each gamma from 1.0 to 2.2, from Windows Terminal (src/renderer/atlas/dwrite.cpp at commit 1283c0f, function
    // DWrite_GetGammaRatios; Copyright (c) Microsoft Corporation, MIT License), as GPUI takes them (Zed v1.24.3,
    // crates/gpui/src/platform.rs, get_gamma_correction_ratios).
    private static readonly float[] TargetRatios =
    [
        0.0000f,
        0.0000f,
        0.0000f,
        0.0000f,
        0.0166f,
        -0.0807f,
        0.2227f,
        -0.0751f,
        0.0350f,
        -0.1760f,
        0.4325f,
        -0.1370f,
        0.0543f,
        -0.2821f,
        0.6302f,
        -0.1876f,
        0.0739f,
        -0.3963f,
        0.8167f,
        -0.2287f,
        0.0933f,
        -0.5161f,
        0.9926f,
        -0.2616f,
        0.1121f,
        -0.6395f,
        1.1588f,
        -0.2877f,
        0.1300f,
        -0.7649f,
        1.3159f,
        -0.3080f,
        0.1469f,
        -0.8911f,
        1.4644f,
        -0.3234f,
        0.1627f,
        -1.0170f,
        1.6051f,
        -0.3347f,
        0.1773f,
        -1.1420f,
        1.7385f,
        -0.3426f,
        0.1908f,
        -1.2652f,
        1.8650f,
        -0.3476f,
        0.2031f,
        -1.3864f,
        1.9851f,
        -0.3501f,
    ];

    private static TextGamma? _system;

    /// <summary>
    /// Gets the setting of Windows, read the first time.
    /// </summary>
    /// <exception cref="InvalidOperationException">DirectWrite could not create its factory or the rendering parameters.</exception>
    public static TextGamma System => _system ??= Read();

    /// <summary>
    /// Returns the four ratios of the shader's alpha correction for a gamma, rounded to a tenth and clamped to 1.0..2.2.
    /// </summary>
    /// <param name="gamma">The gamma.</param>
    /// <returns>The ratios.</returns>
    public static Vector4 RatiosOf(float gamma)
    {
        const float Norm13 = (float)(0x10000 / (255.0 * 255.0) * 4);
        const float Norm24 = (float)(0x100 / 255.0 * 4);
        int index = Math.Clamp((int)((gamma * 10) + 0.5f), 10, 22) - 10;
        ReadOnlySpan<float> ratios = TargetRatios.AsSpan(index * 4, 4);
        return new Vector4(ratios[0] / 4 * Norm13, ratios[1] / 4 * Norm24, ratios[2] / 4 * Norm13, ratios[3] / 4 * Norm24);
    }

    private static TextGamma Read()
    {
        IDWriteFactory* factory = null;
        IDWriteRenderingParams* parameters = null;
        IDWriteRenderingParams1* parameters1 = null;
        try
        {
            Guid factoryId = IID.IID_IDWriteFactory;
            HResults.ThrowIfFailed(
                DWriteCreateFactory(DWRITE_FACTORY_TYPE.DWRITE_FACTORY_TYPE_SHARED, &factoryId, (IUnknown**)&factory),
                "DWriteCreateFactory"
            );
            HResults.ThrowIfFailed(factory->CreateRenderingParams(&parameters), "IDWriteFactory::CreateRenderingParams");
            Guid parametersId = IID.IID_IDWriteRenderingParams1;
            HResults.ThrowIfFailed(parameters->QueryInterface(&parametersId, (void**)&parameters1), "IDWriteRenderingParams::QueryInterface");
            return new TextGamma(RatiosOf(parameters1->GetGamma()), parameters1->GetGrayscaleEnhancedContrast());
        }
        finally
        {
            if (parameters1 is not null)
            {
                _ = parameters1->Release();
            }

            if (parameters is not null)
            {
                _ = parameters->Release();
            }

            if (factory is not null)
            {
                _ = factory->Release();
            }
        }
    }
}
