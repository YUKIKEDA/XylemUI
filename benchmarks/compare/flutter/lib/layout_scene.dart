// The layout scene of XylemUI's `layout` scenario (benchmarks/XylemUI.E2E/LayoutCost.cs, roadmap row L1a) in Flutter's render
// tree: a vertical RenderFlex of 100 horizontal RenderFlexes of 100 boxes of 10 x 10, laid out at 2,000 x 2,000 under a
// PipelineOwner (the layout pass alone, no widgets and no frame). The three times of the comparison: the first layout, a layout
// after one box's width changes (PipelineOwner.flushLayout), and a layout at a new width.
import 'dart:io';

import 'package:flutter/rendering.dart';

void runLayoutScene(String output) {
  final first = <double>[];
  final change = <double>[];
  final width = <double>[];
  for (var s = 0; s < 18; s++) {
    final owner = PipelineOwner();
    final (root, target) = _build();
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
      width.add(widthUs);
    }
  }

  File(output).writeAsStringSync(
    '{"name":"Flutter","first_us_p50":${_median(first)},"change_us_p50":${_median(change)},"width_us_p50":${_median(width)}}\n',
  );
}

(RenderFlex, RenderConstrainedBox) _build() {
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

double _median(List<double> values) {
  values.sort();
  return values[values.length ~/ 2];
}
