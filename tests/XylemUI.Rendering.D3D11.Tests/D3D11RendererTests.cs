using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;
using XylemUI.Platform.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace XylemUI.Rendering.D3D11.Tests;

// The D3D11 renderer (S7, design §2.11.1, §5.0.1, §5.2). The pixels are read back from the back buffer before the present, on
// the WARP rasterizer, so the tests do not depend on a GPU or on looking at the screen.
[Collection(UiThreadGroup.Name)]
[Trait("Category", "Gpu")]
public sealed unsafe class D3D11RendererTests(UiThreadFixture ui)
{
    private static readonly Color Clear = Color.FromRgb(0x2A2A2A);
    private static readonly Color Red = Color.FromRgb(0xCC3333);
    private static readonly Color Blue = Color.FromRgb(0x3366CC);

    /// <summary>
    /// Solid rectangles from a display list are drawn with their colors, edges included, over the clear color.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A hidden window whose client area is at least 160 x 100 (the outer size is larger: borders and the title bar), and a
    ///   WARP renderer.
    ///
    /// Steps:
    /// - Draw red (10, 10, 50, 30) and blue (100, 20, 40, 40) over a dark clear color; read pixels back before the present.
    ///
    /// Expected:
    /// - Inside red at (10, 10) and (59, 39); clear at (60, 10) and (9, 10) (the right and left edge are half open).
    /// - Inside blue at (120, 40); clear at (5, 90).
    /// </remarks>
    [Fact]
    public void Draw_FillRects_PixelsHaveTheirColors() =>
        ui.Invoke(() =>
        {
            using var window = new Win32Window("draw", 400, 300);
            (int width, int height) = window.ClientSize;
            Assert.True(width >= 160 && height >= 100);
            using var renderer = D3D11Renderer.Create(window.Handle, width, height, useWarp: true);
            var list = new DisplayList();
            list.FillRect(10, 10, 50, 30, Red);
            list.FillRect(100, 20, 40, 40, Blue);

            renderer.Draw(list, Clear);

            Assert.True(renderer.IsWarp);
            Assert.Equal(Red, renderer.ReadPixel(10, 10));
            Assert.Equal(Red, renderer.ReadPixel(59, 39));
            Assert.Equal(Clear, renderer.ReadPixel(60, 10));
            Assert.Equal(Clear, renderer.ReadPixel(9, 10));
            Assert.Equal(Blue, renderer.ReadPixel(120, 40));
            Assert.Equal(Clear, renderer.ReadPixel(5, 90));
            renderer.Present();
        });

    /// <summary>
    /// Reading a pixel outside the back buffer throws instead of reading past the mapped texture.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A WARP renderer whose back buffer is resized to 50 x 40.
    ///
    /// Steps:
    /// - Read (50, 0), (0, 40), and (-1, 0).
    ///
    /// Expected:
    /// - <see cref="ArgumentOutOfRangeException"/> for each; (49, 39) reads.
    /// </remarks>
    [Fact]
    public void ReadPixel_OutsideTheBackBuffer_Throws() =>
        ui.Invoke(() =>
        {
            using var window = new Win32Window("bounds", 200, 100);
            (int width, int height) = window.ClientSize;
            using var renderer = D3D11Renderer.Create(window.Handle, width, height, useWarp: true);
            renderer.Resize(50, 40);
            renderer.Draw(new DisplayList(), Clear);

            Assert.Throws<ArgumentOutOfRangeException>(() => renderer.ReadPixel(50, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => renderer.ReadPixel(0, 40));
            Assert.Throws<ArgumentOutOfRangeException>(() => renderer.ReadPixel(-1, 0));
            Assert.Equal(Clear, renderer.ReadPixel(49, 39));
            renderer.Present();
        });

    /// <summary>
    /// The default device (hardware, or WARP when there is none) draws the same pixels.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A hidden window and a renderer created without asking for WARP.
    ///
    /// Steps:
    /// - Draw one blue rectangle; read a pixel inside and one outside.
    ///
    /// Expected:
    /// - Blue inside, the clear color outside; the test output says which device was used.
    /// </remarks>
    [Fact]
    public void Create_DefaultDevice_Draws()
    {
        ITestOutputHelper? output = TestContext.Current.TestOutputHelper;
        ui.Invoke(() =>
        {
            using var window = new Win32Window("default", 200, 100);
            (int width, int height) = window.ClientSize;
            using var renderer = D3D11Renderer.Create(window.Handle, width, height);
            var list = new DisplayList();
            list.FillRect(0, 0, 20, 20, Blue);

            renderer.Draw(list, Clear);

            output?.WriteLine($"device={(renderer.IsWarp ? "warp" : "hardware")}");
            Assert.Equal(Blue, renderer.ReadPixel(5, 5));
            Assert.Equal(Clear, renderer.ReadPixel(30, 30));
            renderer.Present();
        });
    }

    /// <summary>
    /// A steady-state frame — write the display list, upload, draw, present — allocates nothing.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A WARP renderer; a display list rewritten each frame with 1,000 rectangles. Warmed up with 20 frames.
    ///
    /// Steps:
    /// - Run 100 frames, measuring the bytes allocated on this thread.
    ///
    /// Expected:
    /// - 0 bytes, and the display list never grew.
    /// </remarks>
    [Fact]
    public void SteadyState_Frame_AllocatesNothing() =>
        ui.Invoke(() =>
        {
            using var window = new Win32Window("steady", 200, 100);
            (int width, int height) = window.ClientSize;
            using var renderer = D3D11Renderer.Create(window.Handle, width, height, useWarp: true);
            var list = new DisplayList();
            for (int i = 0; i < 20; i++)
            {
                Frame(renderer, list, i);
            }

            int growth = list.GrowthCount;
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++)
            {
                Frame(renderer, list, i);
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.Equal(0, allocated);
            Assert.Equal(growth, list.GrowthCount);
        });

    /// <summary>
    /// After a resize in the contract's order, the renderer draws at the new size.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A WARP renderer for a 200 x 100 client area.
    ///
    /// Steps:
    /// - Resize to 320 x 160; draw a red rectangle at (280, 130, 20, 20); read it back.
    ///
    /// Expected:
    /// - The renderer reports 320 x 160, and (285, 135) is red: the back buffer is the new size.
    /// </remarks>
    [Fact]
    public void Resize_InTheContractOrder_DrawsAtTheNewSize() =>
        ui.Invoke(() =>
        {
            using var window = new Win32Window("resize", 200, 100);
            (int width, int height) = window.ClientSize;
            using var renderer = D3D11Renderer.Create(window.Handle, width, height, useWarp: true);
            var list = new DisplayList();
            list.FillRect(280, 130, 20, 20, Red);

            renderer.Resize(320, 160);
            renderer.Draw(list, Clear);

            Assert.Equal((320, 160), (renderer.Width, renderer.Height));
            Assert.Equal(Red, renderer.ReadPixel(285, 135));
            renderer.Present();
        });

    /// <summary>
    /// A resize right after a draw, before any present, still succeeds: the view the draw bound is unbound and released first.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A WARP renderer that has drawn a frame and not presented it (its render target view is bound to the context).
    ///
    /// Steps:
    /// - Resize to 300 x 200; draw a red rectangle at (250, 150, 20, 20); read it back.
    ///
    /// Expected:
    /// - No exception; (255, 155) is red.
    /// </remarks>
    [Fact]
    public void Resize_AfterADrawWithoutPresent_Succeeds() =>
        ui.Invoke(() =>
        {
            using var window = new Win32Window("bound", 200, 100);
            (int width, int height) = window.ClientSize;
            using var renderer = D3D11Renderer.Create(window.Handle, width, height, useWarp: true);
            var list = new DisplayList();
            renderer.Draw(list, Clear);

            renderer.Resize(300, 200);
            list.FillRect(250, 150, 20, 20, Red);
            renderer.Draw(list, Clear);

            Assert.Equal(Red, renderer.ReadPixel(255, 155));
            renderer.Present();
        });

    /// <summary>
    /// ResizeBuffers fails while a view of a back buffer is alive, which is why the order is a contract.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A WARP renderer, whose render target view is alive and bound by a draw.
    ///
    /// Steps:
    /// - Call ResizeBuffers on its swap chain directly, without releasing the view.
    ///
    /// Expected:
    /// - DXGI_ERROR_INVALID_CALL.
    /// </remarks>
    [Fact]
    public void ResizeBuffers_WithAViewAlive_Fails() =>
        ui.Invoke(() =>
        {
            using var window = new Win32Window("view", 200, 100);
            (int width, int height) = window.ClientSize;
            using var renderer = D3D11Renderer.Create(window.Handle, width, height, useWarp: true);
            renderer.Draw(new DisplayList(), Clear);

            int result = renderer.SwapChain->ResizeBuffers(
                0,
                400,
                300,
                DXGI_FORMAT.DXGI_FORMAT_UNKNOWN,
                (uint)DXGI_SWAP_CHAIN_FLAG.DXGI_SWAP_CHAIN_FLAG_FRAME_LATENCY_WAITABLE_OBJECT
            );

            Assert.Equal(DXGI.DXGI_ERROR_INVALID_CALL, result);
            renderer.Present();
        });

    /// <summary>
    /// The synchronous render path of WM_SIZE (S4) can resize the swap chain and draw at the new size on the spot.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A hidden window with a WARP renderer; its synchronous render resizes the renderer, draws a blue rectangle at the bottom
    ///   right corner, and reads it back.
    ///
    /// Steps:
    /// - Change the window's size with SetWindowPos.
    ///
    /// Expected:
    /// - The render ran with the new client size; the renderer has that size; the corner pixel is blue.
    /// </remarks>
    [Fact]
    public void SynchronousRender_ResizesAndDrawsInsideWmSize() =>
        ui.Invoke(() =>
        {
            using var window = new Win32Window("sync", 200, 100);
            (int width, int height) = window.ClientSize;
            using var renderer = D3D11Renderer.Create(window.Handle, width, height, useWarp: true);
            var list = new DisplayList();
            (int Width, int Height) rendered = default;
            Color corner = default;
            window.SynchronousRender = (_, newWidth, newHeight) =>
            {
                rendered = (newWidth, newHeight);
                renderer.Resize(newWidth, newHeight);
                list.Reset();
                list.FillRect(newWidth - 10, newHeight - 10, 10, 10, Blue);
                renderer.Draw(list, Clear);
                corner = renderer.ReadPixel(newWidth - 1, newHeight - 1);
                renderer.Present();
            };

            Assert.True(SetWindowPos((HWND)window.Handle, HWND.NULL, 0, 0, 360, 240, (uint)(SWP.SWP_NOMOVE | SWP.SWP_NOZORDER | SWP.SWP_NOACTIVATE)));

            Assert.Equal(window.ClientSize, rendered);
            Assert.Equal(rendered, (renderer.Width, renderer.Height));
            Assert.Equal(Blue, corner);
        });

    private static void Frame(D3D11Renderer renderer, DisplayList list, int frame)
    {
        list.Reset();
        for (int i = 0; i < 1_000; i++)
        {
            list.FillRect(((i * 7) + frame) % 190, i % 90, 10, 10, (i & 1) == 0 ? Red : Blue);
        }

        renderer.Render(list, Clear);
    }
}
