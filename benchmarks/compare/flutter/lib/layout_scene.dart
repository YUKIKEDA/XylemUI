// The layout scenes of XylemUI's `layout` scenario (benchmarks/XylemUI.E2E/LayoutCost.cs, roadmap rows L1a and L1b) in Flutter's
// render tree, laid out at 2,000 x 2,000 under a PipelineOwner (the layout pass alone, no widgets and no frame): a vertical
// RenderFlex of 100 horizontal RenderFlexes of 100 boxes of 10 x 10, and a RenderWrap of 10,000 boxes (the `wrap_` figures).
// The four times of the comparison: the first layout, a layout after one box's width changes (PipelineOwner.flushLayout), a
// layout after one box is marked for layout at the same size (markNeedsLayout, as a change of its content that keeps its size;
// row L5), and a layout at a new width.
import 'dart:io';

import 'package:flutter/rendering.dart';

void runLayoutScene(String output) {
  final flex = _measure(_build);
  final wrap = _measure(_buildWrap);
  File(output).writeAsStringSync(
    '{"name":"Flutter","first_us_p50":${flex.$1},"change_us_p50":${flex.$2},"content_us_p50":${flex.$3},"width_us_p50":${flex.$4},'
    '"wrap_first_us_p50":${wrap.$1},"wrap_change_us_p50":${wrap.$2},"wrap_content_us_p50":${wrap.$3},"wrap_width_us_p50":${wrap.$4}}\n',
  );
}

(double, double, double, double) _measure((RenderBox, RenderConstrainedBox) Function() build) {
  final first = <double>[];
  final change = <double>[];
  final content = <double>[];
  final width = <double>[];
  for (var s = 0; s < 18; s++) {
    final owner = PipelineOwner();
    final (root, target) = build();
    owner.rootNode = root;
    final watch = Stopwatch()..start();
    root.layout(BoxConstraints.tight(const Size(2000, 2000)));
    final firstUs = watch.elapsedMicroseconds.toDouble();

    var changeUs = 0.0;
    for (var i = 0; i < 2; i++) {
      target.additionalConstraints = BoxConstraints.tightFor(width: i.isEven ? 20 : 10, height: 10);
      watch
        ..reset()
        ..start();
      owner.flushLayout();
      changeUs = watch.elapsedMicroseconds.toDouble();
    }

    var contentUs = 0.0;
    for (var i = 0; i < 2; i++) {
      target.markNeedsLayout();
      watch
        ..reset()
        ..start();
      owner.flushLayout();
      contentUs = watch.elapsedMicroseconds.toDouble();
    }

    var widthUs = 0.0;
    for (var i = 0; i < 2; i++) {
      watch
        ..reset()
        ..start();
      root.layout(BoxConstraints.tight(Size(i.isEven ? 1600 : 2000, 2000)));
      widthUs = watch.elapsedMicroseconds.toDouble();
    }

    if (root.size.width != 2000 || target.size.width != 10) {
      throw StateError('The layout did not follow: root ${root.size}, target ${target.size}.');
    }

    if (s >= 3) {
      first.add(firstUs);
      change.add(changeUs);
      content.add(contentUs);
      width.add(widthUs);
    }
  }

  return (_median(first), _median(change), _median(content), _median(width));
}

(RenderBox, RenderConstrainedBox) _build() {
  RenderConstrainedBox? target;
  final rows = <RenderBox>[];
  for (var r = 0; r < 100; r++) {
    final leaves = <RenderBox>[];
    for (var c = 0; c < 100; c++) {
      final leaf = RenderConstrainedBox(additionalConstraints: const BoxConstraints.tightFor(width: 10, height: 10));
      if (r == 50 && c == 50) {
        target = leaf;
      }
      leaves.add(leaf);
    }
    rows.add(RenderFlex(direction: Axis.horizontal, textDirection: TextDirection.ltr, children: leaves));
  }
  final root = RenderFlex(direction: Axis.vertical, textDirection: TextDirection.ltr, children: rows);
  return (root, target!);
}

(RenderBox, RenderConstrainedBox) _buildWrap() {
  RenderConstrainedBox? target;
  final boxes = <RenderBox>[];
  for (var i = 0; i < 10000; i++) {
    final box = RenderConstrainedBox(additionalConstraints: const BoxConstraints.tightFor(width: 10, height: 10));
    if (i == 5050) {
      target = box;
    }
    boxes.add(box);
  }
  return (RenderWrap(textDirection: TextDirection.ltr, children: boxes), target!);
}

double _median(List<double> values) {
  values.sort();
  return values[values.length ~/ 2];
}
