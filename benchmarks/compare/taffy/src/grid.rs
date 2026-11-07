// The grid scene of row P02f (benchmarks/XylemUI.E2E/LayoutCost.cs, MeasureGrid) in Taffy 0.15.1, the version XylemUI's grid
// is ported from (design §3.3, "The shape of P02f"): a grid of four columns (120 px, auto, 1fr, and minmax(80px, 2fr)) and 250
// auto rows, a leaf of 10 x 10 in each cell, laid out at 2,000 x 2,000 without rounding. The change is one leaf's content in
// the auto column of the middle row, 10 or 20 wide in turn (kept in the node's context, then mark_dirty); the width change is
// the grid's, 1,600 or 2,000 in turn.
use std::time::Instant;
use taffy015::prelude::*;

const ROWS: usize = 250;

fn build() -> (TaffyTree<f32>, NodeId, NodeId) {
    let mut tree: TaffyTree<f32> = TaffyTree::new();
    tree.disable_rounding();
    let mut target = None;
    let mut leaves = Vec::new();
    for r in 0..ROWS {
        for c in 0..4 {
            let leaf = tree.new_leaf_with_context(Style::default(), 10.0).unwrap();
            if r == ROWS / 2 && c == 1 {
                target = Some(leaf);
            }
            leaves.push(leaf);
        }
    }
    let root_style = Style {
        display: Display::Grid,
        grid_template_columns: vec![length(120.0), auto(), fr(1.0), minmax(length(80.0), fr(2.0))],
        size: Size {
            width: length(2000.0_f32),
            height: length(2000.0_f32),
        },
        ..Default::default()
    };
    let root = tree.new_with_children(root_style, &leaves).unwrap();
    (tree, root, target.unwrap())
}

fn layout(tree: &mut TaffyTree<f32>, root: NodeId) {
    // Taffy 0.15's measure function takes the layout input and returns the layout output.
    tree.compute_layout_with_measure(root, Size::MAX_CONTENT, |input: taffy015::LayoutInput, _node, ctx, _style| {
        taffy015::LayoutOutput::from_outer_size(Size {
            width: input.known_dimensions.width.unwrap_or(*ctx.unwrap()),
            height: input.known_dimensions.height.unwrap_or(10.0),
        })
    })
    .unwrap();
}

pub fn measure(line: &mut String, median: fn(Vec<f64>) -> f64) {
    let (mut first, mut change, mut width) = (Vec::new(), Vec::new(), Vec::new());
    for s in 0..18 {
        let (mut tree, root, target) = build();
        let t = Instant::now();
        layout(&mut tree, root);
        let first_us = t.elapsed().as_secs_f64() * 1e6;
        let mut change_us = 0.0;
        for i in 0..2 {
            *tree.get_node_context_mut(target).unwrap() = if i % 2 == 0 { 20.0 } else { 10.0 };
            tree.mark_dirty(target).unwrap();
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
        assert_eq!(tree.layout(target).unwrap().size.width, 10.0, "the grid scene did not follow the change");
        if s >= 3 {
            first.push(first_us);
            change.push(change_us);
            width.push(width_us);
        }
    }
    line.push_str(&format!(
        "\"grid_first_us_p50\":{:.1},\"grid_change_us_p50\":{:.1},\"grid_width_us_p50\":{:.1},",
        median(first),
        median(change),
        median(width)
    ));
}
