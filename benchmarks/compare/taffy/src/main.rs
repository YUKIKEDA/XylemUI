// The layout scene of XylemUI's `layout` scenario (benchmarks/XylemUI.E2E/LayoutCost.cs, roadmap row L1a) in Taffy, the layout of
// GPUI: a column of 100 rows of 100 leaves that measure 10 x 10, laid out at 2,000 x 2,000 without rounding (XylemUI does not
// round). Three scenes as in XylemUI: S6b's meaning (every flex item has a minimum of 0), CSS's defaults, and percentages with
// padding. Prints one JSON line: the medians of the first layout, a layout after one leaf's width changes, and a layout at a
// new width, and how many times a leaf was measured in a first layout and in a layout after a change.
use std::sync::atomic::{AtomicUsize, Ordering};
use std::time::Instant;

static MEASURES: AtomicUsize = AtomicUsize::new(0);
use taffy::prelude::*;

const ROWS: usize = 100;
const COLUMNS: usize = 100;

fn build(scene: &str) -> (TaffyTree<()>, NodeId, NodeId) {
    let mut tree: TaffyTree<()> = TaffyTree::new();
    // XylemUI's layout does not round; neither does this.
    tree.disable_rounding();
    let zero = Dimension::length(0.0);
    let mut target = None;
    let mut rows = Vec::new();
    for r in 0..ROWS {
        let mut leaves = Vec::new();
        for c in 0..COLUMNS {
            let mut style = Style::default();
            if scene == "s6b" {
                style.min_size.width = zero;
            }
            if scene == "percent" {
                style.size.width = percent(0.01_f32);
                style.padding = Rect {
                    left: percent(0.001_f32),
                    right: percent(0.001_f32),
                    top: percent(0.001_f32),
                    bottom: percent(0.001_f32),
                };
            }
            let leaf = tree.new_leaf_with_context(style, ()).unwrap();
            if r == ROWS / 2 && c == COLUMNS / 2 {
                target = Some(leaf);
            }
            leaves.push(leaf);
        }
        let mut row_style = Style {
            flex_direction: FlexDirection::Row,
            ..Default::default()
        };
        if scene == "s6b" {
            row_style.min_size.height = zero;
        }
        rows.push(tree.new_with_children(row_style, &leaves).unwrap());
    }
    let root_style = Style {
        flex_direction: FlexDirection::Column,
        size: Size {
            width: length(2000.0_f32),
            height: length(2000.0_f32),
        },
        ..Default::default()
    };
    let root = tree.new_with_children(root_style, &rows).unwrap();
    (tree, root, target.unwrap())
}

fn layout(tree: &mut TaffyTree<()>, root: NodeId) {
    tree.compute_layout_with_measure(
        root,
        Size::MAX_CONTENT,
        |known, _available, _node, _ctx, _style| {
            MEASURES.fetch_add(1, Ordering::Relaxed);
            Size {
                width: known.width.unwrap_or(10.0),
                height: known.height.unwrap_or(10.0),
            }
        },
    )
    .unwrap();
}

fn median(mut v: Vec<f64>) -> f64 {
    v.sort_by(|a, b| a.partial_cmp(b).unwrap());
    v[v.len() / 2]
}

fn main() {
    let mut line = String::from("{\"name\":\"Taffy\",");
    for scene in ["s6b", "auto_min", "percent"] {
        let (mut first, mut change, mut width) = (Vec::new(), Vec::new(), Vec::new());
        for s in 0..18 {
            let (mut tree, root, target) = build(scene);
            let t = Instant::now();
            layout(&mut tree, root);
            let first_us = t.elapsed().as_secs_f64() * 1e6;
            let mut style = tree.style(target).unwrap().clone();
            let mut change_us = 0.0;
            for i in 0..2 {
                style.size.width = length(if i % 2 == 0 { 20.0_f32 } else { 10.0 });
                tree.set_style(target, style.clone()).unwrap();
                let t = Instant::now();
                layout(&mut tree, root);
                change_us = t.elapsed().as_secs_f64() * 1e6;
            }
            let mut root_style = tree.style(root).unwrap().clone();
            let mut width_us = 0.0;
            for i in 0..2 {
                root_style.size.width = length(if i % 2 == 0 { 1600.0_f32 } else { 2000.0 });
                tree.set_style(root, root_style.clone()).unwrap();
                let t = Instant::now();
                layout(&mut tree, root);
                width_us = t.elapsed().as_secs_f64() * 1e6;
            }
            if s >= 3 {
                first.push(first_us);
                change.push(change_us);
                width.push(width_us);
            }
        }
        let (mut tree, root, target) = build(scene);
        MEASURES.store(0, Ordering::Relaxed);
        layout(&mut tree, root);
        let first_runs = MEASURES.load(Ordering::Relaxed);
        let mut style = tree.style(target).unwrap().clone();
        style.size.width = length(20.0_f32);
        tree.set_style(target, style).unwrap();
        MEASURES.store(0, Ordering::Relaxed);
        layout(&mut tree, root);
        let change_runs = MEASURES.load(Ordering::Relaxed);
        line.push_str(&format!(
            "\"{scene}_first_us_p50\":{:.1},\"{scene}_change_us_p50\":{:.1},\"{scene}_width_us_p50\":{:.1},\"{scene}_first_leaf_runs\":{first_runs},\"{scene}_change_leaf_runs\":{change_runs},",
            median(first),
            median(change),
            median(width)
        ));
    }
    line.pop();
    line.push('}');
    println!("{line}");
}
