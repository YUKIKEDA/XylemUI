// The text scene of the rendering comparison (XylemUI roadmap row T4; the same as benchmarks/XylemUI.E2E/TextQuality.cs), in the
// GPUI of Zed v1.24.3. In logical pixels: an 800 x 600 window filled with the background; for Segoe UI, then Yu Gothic UI, at 12,
// 14, 16, and 20 px, a sample line with its top at y and a probe line of 16 "l" with its top at y + 1.8 size, then y grows by
// 3.6 size, from y 12; every line starts at x 16. Black on white, or white on black with --dark. Characters a family lacks take
// GPUI's fallback. GPUI's own defaults for text (the platform's rendering mode).
#![windows_subsystem = "windows"]

use gpui::{App, Bounds, Context, Window, WindowBounds, WindowOptions, div, point, prelude::*, px, rgb, size};
use gpui_platform::application;

const SAMPLE: &str = "Hamburgefonstiv 0123 日本語のテキスト、ひらがな";
const PROBE: &str = "llllllllllllllll";
const FAMILIES: [&str; 2] = ["Segoe UI", "Yu Gothic UI"];
const SIZES: [f32; 4] = [12.0, 14.0, 16.0, 20.0];

struct TextScene {
    dark: bool,
}

impl Render for TextScene {
    fn render(&mut self, _window: &mut Window, _cx: &mut Context<Self>) -> impl IntoElement {
        let (background, ink) = if self.dark { (0x000000, 0xffffff) } else { (0xffffff, 0x000000) };
        let mut root = div().size_full().relative().bg(rgb(background));
        let mut y = 12.0_f32;
        for family in FAMILIES {
            for size in SIZES {
                for (text, top) in [(SAMPLE, y), (PROBE, y + 1.8 * size)] {
                    root = root.child(
                        div()
                            .absolute()
                            .left(px(16.0))
                            .top(px(top))
                            .font_family(family)
                            .text_size(px(size))
                            .text_color(rgb(ink))
                            .whitespace_nowrap()
                            .child(text),
                    );
                }
                y += 3.6 * size;
            }
        }
        root
    }
}

fn main() {
    let dark = std::env::args().any(|arg| arg == "--dark");
    application().run(move |cx: &mut App| {
        let bounds = Bounds { origin: point(px(100.0), px(100.0)), size: size(px(800.0), px(600.0)) };
        cx.open_window(
            WindowOptions { window_bounds: Some(WindowBounds::Windowed(bounds)), ..Default::default() },
            move |_, cx| cx.new(|_| TextScene { dark }),
        )
        .unwrap();
        cx.activate(true);
    });
}
