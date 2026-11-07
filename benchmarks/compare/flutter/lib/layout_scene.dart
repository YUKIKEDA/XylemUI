// The layout scenes of XylemUI's `layout` scenario (benchmarks/XylemUI.E2E/LayoutCost.cs, roadmap rows L1a and L1b) in Flutter's
// render tree, laid out at 2,000 x 2,000 under a PipelineOwner (the layout pass alone, no widgets and no frame): a vertical
// RenderFlex of 100 horizontal RenderFlexes of 100 boxes of 10 x 10, and a RenderWrap of 10,000 boxes (the `wrap_` figures).
// The four times of the comparison: the first layout, a layout after one box's width changes (PipelineOwner.flushLayout), a
// layout after one box is marked for layout at the same size (markNeedsLayout, as a change of its content that keeps its size;
// row L5), and a layout at a new width. The grid scene of row P02f is a RenderTable (Flutter has no CSS grid; the table is the
// nearest, design §3.3): the columns 120 px, intrinsic, flex 1, and the larger of 80 px and flex 2 (the scene's minmax(80px,
// 2fr)), and 250 rows of boxes of 10 x 10 (the `grid_` figures); the change is the width of the box in the intrinsic column of the
// middle row.
import 'dart:io';

import 'package:flutter/rendering.dart';

void runLayoutScene(String output) {
  final flex = _measure(_build);
  final wrap = _measure(_buildWrap);
  final absolute = _measureAbsolute();
  final grid = _measure(_buildTable);
  File(output).writeAsStringSync(
    '{"name":"Flutter","first_us_p50":${flex.$1},"change_us_p50":${flex.$2},"content_us_p50":${flex.$3},"width_us_p50":${flex.$4},'
    '"wrap_first_us_p50":${wrap.$1},"wrap_change_us_p50":${wrap.$2},"wrap_content_us_p50":${wrap.$3},"wrap_width_us_p50":${wrap.$4},'
    '"absolute_first_us_p50":${absolute.$1},"absolute_change_us_p50":${absolute.$2},'
    '"grid_first_us_p50":${grid.$1},"grid_change_us_p50":${grid.$2},"grid_width_us_p50":${grid.$4}}\n',
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
    final firstUs = _microseconds(watch);

    var changeUs = 0.0;
    for (var i = 0; i < 2; i++) {
      target.additionalConstraints = BoxConstraints.tightFor(width: i.isEven ? 20 : 10, height: 10);
      watch
        ..reset()
        ..start();
      owner.flushLayout();
      changeUs = _microseconds(watch);
    }

    var contentUs = 0.0;
    for (var i = 0; i < 2; i++) {
      target.markNeedsLayout();
      watch
        ..reset()
        ..start();
      owner.flushLayout();
      contentUs = _microseconds(watch);
    }

    var widthUs = 0.0;
    for (var i = 0; i < 2; i++) {
      watch
        ..reset()
        ..start();
      root.layout(BoxConstraints.tight(Size(i.isEven ? 1600 : 2000, 2000)));
      widthUs = _microseconds(watch);
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

(RenderBox, RenderConstrainedBox) _buildTable() {
  RenderConstrainedBox? target;
  final rows = <List<RenderBox>>[];
  for (var r = 0; r < 250; r++) {
    final cells = <RenderBox>[];
    for (var c = 0; c < 4; c++) {
      final box = RenderConstrainedBox(additionalConstraints: const BoxConstraints.tightFor(width: 10, height: 10));
      if (r == 125 && c == 1) {
        target = box;
      }
      cells.add(box);
    }
    rows.add(cells);
  }
  final table = RenderTable(
    columns: 4,
    columnWidths: const <int, TableColumnWidth>{
      0: FixedColumnWidth(120),
      1: IntrinsicColumnWidth(),
      2: FlexColumnWidth(1),
      3: MaxColumnWidth(FixedColumnWidth(80), FlexColumnWidth(2)),
    },
    textDirection: TextDirection.ltr,
    children: rows,
  );
  return (table, target!);
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

// The absolute scene (row L2): a Column of 100 Stacks of 2000 x 20, each with 100 Positioned boxes of 10 x 10 at left 20 each.
// The change is one box's width, 10 or 20 in turn.
(double, double) _measureAbsolute() {
  final first = <double>[];
  final change = <double>[];
  for (var s = 0; s < 18; s++) {
    final owner = PipelineOwner();
    RenderConstrainedBox? target;
    final stacks = <RenderBox>[];
    for (var r = 0; r < 100; r++) {
      final children = <RenderBox>[];
      for (var c = 0; c < 100; c++) {
        final box = RenderConstrainedBox(additionalConstraints: const BoxConstraints.tightFor(width: 10, height: 10));
        if (r == 50 && c == 50) {
          target = box;
        }
        children.add(box);
      }
      final stack = RenderStack(textDirection: TextDirection.ltr, children: children);
      var child = stack.firstChild;
      var c = 0;
      while (child != null) {
        final data = child.parentData! as StackParentData;
        data.left = c * 20.0;
        data.top = 0;
        child = data.nextSibling;
        c++;
      }
      stacks.add(RenderConstrainedBox(additionalConstraints: const BoxConstraints.tightFor(width: 2000, height: 20), child: stack));
    }
    final root = RenderFlex(direction: Axis.vertical, textDirection: TextDirection.ltr, children: stacks);
    owner.rootNode = root;
    final watch = Stopwatch()..start();
    root.layout(BoxConstraints.tight(const Size(2000, 2000)));
    final firstUs = _microseconds(watch);
    var changeUs = 0.0;
    for (var i = 0; i < 2; i++) {
      target!.additionalConstraints = BoxConstraints.tightFor(width: i.isEven ? 20 : 10, height: 10);
      watch
        ..reset()
        ..start();
      owner.flushLayout();
      changeUs = _microseconds(watch);
    }
    if (target!.size.width != 10) {
      throw StateError('The absolute scene did not follow the change.');
    }
    if (s >= 3) {
      first.add(firstUs);
      change.add(changeUs);
    }
  }
  return (_median(first), _median(change));
}

// Microseconds from the stopwatch's ticks: elapsedMicroseconds is a whole number, and a change of the absolute scene, near 2 µs,
// read as 2.0 in every round whatever it took between 2 and 3 µs (row L7). The other apps report fractions too.
double _microseconds(Stopwatch watch) => watch.elapsedTicks * 1000000 / watch.frequency;

double _median(List<double> values) {
  values.sort();
  return values[values.length ~/ 2];
}
