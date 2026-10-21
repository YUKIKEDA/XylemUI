// The text scene of the rendering comparison (XylemUI roadmap row T4; the same as benchmarks/XylemUI.E2E/TextQuality.cs). In
// logical pixels: the window filled with the background; for Segoe UI, then Yu Gothic UI, at 12, 14, 16, and 20 px, a sample
// line with its top at y and a probe line of 16 "l" with its top at y + 1.8 size, then y grows by 3.6 size, from y 12; every line
// starts at x 16. Black on white, or white on black with --dark. Characters a family lacks take Flutter's fallback.
import 'package:flutter/widgets.dart';

const sample = 'Hamburgefonstiv 0123 日本語のテキスト、ひらがな';
const probe = 'llllllllllllllll';
const families = ['Segoe UI', 'Yu Gothic UI'];
const sizes = [12.0, 14.0, 16.0, 20.0];

class TextScene extends StatelessWidget {
  const TextScene({super.key, required this.dark});

  final bool dark;

  @override
  Widget build(BuildContext context) {
    final background = dark ? const Color(0xFF000000) : const Color(0xFFFFFFFF);
    final ink = dark ? const Color(0xFFFFFFFF) : const Color(0xFF000000);
    final lines = <Widget>[];
    var y = 12.0;
    for (final family in families) {
      for (final size in sizes) {
        final style = TextStyle(fontFamily: family, fontSize: size, color: ink);
        lines.add(Positioned(left: 16, top: y, child: Text(sample, style: style)));
        lines.add(Positioned(left: 16, top: y + 1.8 * size, child: Text(probe, style: style)));
        y += 3.6 * size;
      }
    }

    return Directionality(
      textDirection: TextDirection.ltr,
      child: ColoredBox(color: background, child: Stack(children: lines)),
    );
  }
}
