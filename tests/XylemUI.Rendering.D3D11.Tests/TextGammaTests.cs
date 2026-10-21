namespace XylemUI.Rendering.D3D11.Tests;

// The gamma ratios of the glyph shader (T4, design §5.0.3), against the values Windows Terminal documents.
public sealed class TextGammaTests
{
    /// <summary>
    /// The ratios of gamma 1.8, Windows' default, are the constants Windows Terminal gives for it; gamma 1.0 corrects nothing;
    /// a gamma outside 1.0..2.2 takes the nearest end.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - None.
    ///
    /// Steps:
    /// - Take the ratios of 1.8, 1.0, 0.5, 2.2, and 3.0.
    ///
    /// Expected:
    /// - 1.8: 0.148054421, −0.894594550, 1.47590804, −0.324668258, each within 1e−6 (Windows Terminal's
    ///   <c>src/renderer/atlas/dwrite.hlsl</c>, the comment of <c>DWrite_GrayscaleBlend</c>).
    /// - 1.0: all 0. 0.5 is 1.0, and 3.0 is 2.2.
    /// </remarks>
    [Fact]
    public void RatiosOf_Gamma_MatchWindowsTerminal()
    {
        System.Numerics.Vector4 ratios = TextGamma.RatiosOf(1.8f);

        Assert.Equal(0.148054421f, ratios.X, 1e-6f);
        Assert.Equal(-0.894594550f, ratios.Y, 1e-6f);
        Assert.Equal(1.47590804f, ratios.Z, 1e-6f);
        Assert.Equal(-0.324668258f, ratios.W, 1e-6f);
        Assert.Equal(System.Numerics.Vector4.Zero, TextGamma.RatiosOf(1.0f));
        Assert.Equal(TextGamma.RatiosOf(1.0f), TextGamma.RatiosOf(0.5f));
        Assert.Equal(TextGamma.RatiosOf(2.2f), TextGamma.RatiosOf(3.0f));
    }
}
