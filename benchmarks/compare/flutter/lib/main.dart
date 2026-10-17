// The comparison scene in Flutter (XylemUI roadmap row M1a; the same as benchmarks/XylemUI.E2E/Scene.cs): an 800 x 600 window
// cleared to #2A2A2A, the marker #3366CC at (0, 0, 40, 40), 1,000 rectangles that move with the frame, and one line of Japanese
// text at 24 px at (60, 30) in the test font. Animated by a ticker; with --static it draws once and stays idle. With --layout <file>
// it shows nothing: it times the layout of XylemUI's layout scene (row L1a, layout_scene.dart), writes one JSON line, and exits.
import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter/scheduler.dart';

import 'layout_scene.dart';

void main(List<String> args) {
  final layout = args.indexOf('--layout');
  if (layout >= 0) {
    runLayoutScene(args[layout + 1]);
    exit(0);
  }

  runApp(SceneApp(isStatic: args.contains('--static')));
}

class SceneApp extends StatelessWidget {
  const SceneApp({super.key, required this.isStatic});

  final bool isStatic;

  @override
  Widget build(BuildContext context) => Directionality(
        textDirection: TextDirection.ltr,
        child: Scene(isStatic: isStatic),
      );
}

class Scene extends StatefulWidget {
  const Scene({super.key, required this.isStatic});

  final bool isStatic;

  @override
  State<Scene> createState() => _SceneState();
}

class _SceneState extends State<Scene> with SingleTickerProviderStateMixin {
  Ticker? _ticker;
  int _frame = 0;

  @override
  void initState() {
    super.initState();
    if (!widget.isStatic) {
      _ticker = createTicker((_) => setState(() => _frame++))..start();
    }
  }

  @override
  void dispose() {
    _ticker?.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => CustomPaint(painter: ScenePainter(_frame), size: Size.infinite);
}

class ScenePainter extends CustomPainter {
  ScenePainter(this.frame);

  final int frame;

  static final Paint _clear = Paint()..color = const Color(0xFF2A2A2A);
  static final Paint _marker = Paint()..color = const Color(0xFF3366CC);
  static final Paint _red = Paint()..color = const Color(0xFFCC3333);
  static final Paint _green = Paint()..color = const Color(0xFF33AA66);

  @override
  void paint(Canvas canvas, Size size) {
    canvas.drawRect(Offset.zero & size, _clear);
    canvas.drawRect(const Rect.fromLTWH(0, 0, 40, 40), _marker);
    final double width = size.width;
    final double height = size.height;
    for (int i = 0; i < 1000; i++) {
      final double x = ((i * 37) + (frame * 3)) % (width - 20).clamp(1, double.infinity);
      final double y = 50 + ((i * 11) % (height - 70).clamp(1, double.infinity));
      canvas.drawRect(Rect.fromLTWH(x, y, 16, 16), (i & 1) == 0 ? _red : _green);
    }

    final TextPainter text = TextPainter(
      text: const TextSpan(
        text: 'Hello, World! 日本語のテキスト',
        style: TextStyle(fontFamily: 'XylemUITest', fontSize: 24, color: Color(0xFFE8E8E8)),
      ),
      textDirection: TextDirection.ltr,
    )..layout();
    final double baseline = text.computeDistanceToActualBaseline(TextBaseline.alphabetic);
    text.paint(canvas, Offset(60, 30 - baseline));
  }

  @override
  bool shouldRepaint(ScenePainter old) => old.frame != frame;
}
