// The comparison scene in GPUI (XylemUI roadmap row M1a; the same as benchmarks/XylemUI.E2E/Scene.cs): an 800 x 600 window
// cleared to #2A2A2A, the marker #3366CC at (0, 0, 40, 40), 1,000 rectangles that move with the frame, and one line of Japanese
// text at 24 px near (60, 30) in the test font. Animated by request_animation_frame; with --static it draws once and stays idle.
// The space key switches the marker to #CC6633 and back (the input measurement, row R3).
#![windows_subsystem = "windows"]

use std::borrow::Cow;

use gpui::{
    canvas, div, fill, point, prelude::*, px, rgb, size, App, Application, Bounds, Context, FocusHandle, KeyDownEvent, Window,
    WindowBounds, WindowOptions,
};

const FONT: &[u8] = include_bytes!("../../../../src/XylemUI.Testing/Fonts/XylemUITestSans.ttf");

struct Scene {
    frame: u32,
    animate: bool,
    pressed: bool,
    focus: FocusHandle,
}

impl Render for Scene {
    fn render(&mut self, window: &mut Window, cx: &mut Context<Self>) -> impl IntoElement {
        let frame = self.frame;
        if self.animate {
            self.frame += 1;
            window.request_animation_frame();
        }

        let marker = if self.pressed { rgb(0xCC6633) } else { rgb(0x3366CC) };
        div()
            .size_full()
            .bg(rgb(0x2A2A2A))
            // The space key switches the marker's color (the input measurement, XylemUI roadmap row R3).
            .track_focus(&self.focus)
            .on_key_down(cx.listener(|this, event: &KeyDownEvent, _, cx| {
                if event.keystroke.key == "space" {
                    this.pressed = !this.pressed;
                    cx.notify();
                }
            }))
            .child(
                canvas(
                    |_, _, _| {},
                    move |bounds, _, window, _| {
                        let origin = bounds.origin;
                        let width = f32::from(bounds.size.width);
                        let height = f32::from(bounds.size.height);
                        window.paint_quad(fill(Bounds::new(origin, size(px(40.), px(40.))), marker));
                        for i in 0..1000u32 {
                            let x = ((i * 37 + frame * 3) as f32) % (width - 20.).max(1.);
                            let y = 50. + ((i * 11) as f32) % (height - 70.).max(1.);
                            let color = if i & 1 == 0 { rgb(0xCC3333) } else { rgb(0x33AA66) };
                            let at = point(origin.x + px(x), origin.y + px(y));
                            window.paint_quad(fill(Bounds::new(at, size(px(16.), px(16.))), color));
                        }
                    },
                )
                .absolute()
                .size_full(),
            )
            .child(
                div()
                    .absolute()
                    .left(px(60.))
                    .top(px(30. - 24.))
                    .font_family("Noto Sans JP Thin")
                    .text_size(px(24.))
                    .text_color(rgb(0xE8E8E8))
                    .child("Hello, World! 日本語のテキスト"),
            )
    }
}

fn main() {
    let animate = !std::env::args().any(|a| a == "--static");
    Application::new().run(move |cx: &mut App| {
        cx.text_system().add_fonts(vec![Cow::Borrowed(FONT)]).expect("the test font loads");
        let bounds = Bounds::centered(None, size(px(800.), px(600.)), cx);
        cx.open_window(
            WindowOptions { window_bounds: Some(WindowBounds::Windowed(bounds)), ..Default::default() },
            |window, cx| {
                cx.new(|cx| {
                    let focus = cx.focus_handle();
                    focus.focus(window);
                    Scene { frame: 0, animate, pressed: false, focus }
                })
            },
        )
        .expect("the window opens");
        cx.activate(true);
    });
}
