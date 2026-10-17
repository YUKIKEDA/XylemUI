using System.Xml.Linq;

namespace XylemUI.Tests;

// Which of Taffy's XML tests are left out, and why (design §3.2: the one place that lists them). A test is left out when it
// uses a feature of a later roadmap row, or a part of CSS the design does not take; the explicit list below names the tests
// left out for a reason the features do not show.
internal static class TaffyFixtureScope
{
    // Chrome collapses these vertical margins; XylemUI adds them (design §3.2, no margin collapsing).
    private const string MarginCollapsing = "margin collapsing (the design adds margins, §3.2)";

    private static readonly Dictionary<string, string> Explicit = new(StringComparer.Ordinal)
    {
        ["block/block_flow_root_margin_non_collapse__border_box_ltr"] = MarginCollapsing,
        ["block/block_margin_y_collapse_complex__border_box_ltr"] = MarginCollapsing,
        ["block/block_margin_y_collapse_through_blocked_by_border_bottom__border_box_ltr"] = MarginCollapsing,
        ["block/block_margin_y_collapse_through_blocked_by_border_top__border_box_ltr"] = MarginCollapsing,
        ["block/block_margin_y_collapse_through_blocked_by_height__border_box_ltr"] = MarginCollapsing,
        ["block/block_margin_y_collapse_through_blocked_by_line_box__border_box_ltr"] = MarginCollapsing,
        ["block/block_margin_y_collapse_through_blocked_by_line_box_with_height_zero__border_box_ltr"] = MarginCollapsing,
        ["block/block_margin_y_collapse_through_blocked_by_line_box_with_max_height_zero__border_box_ltr"] = MarginCollapsing,
        ["block/block_margin_y_collapse_through_blocked_by_min_height__border_box_ltr"] = MarginCollapsing,
        ["block/block_margin_y_collapse_through_blocked_by_padding_bottom__border_box_ltr"] = MarginCollapsing,
        ["block/block_margin_y_collapse_through_blocked_by_padding_top__border_box_ltr"] = MarginCollapsing,
        ["block/block_margin_y_collapse_through_blocked_by_percentage_height__border_box_ltr"] = MarginCollapsing,
        ["block/block_margin_y_collapse_through_child_blocked_by_parent_min_height__border_box_ltr"] = MarginCollapsing,
        ["block/block_margin_y_collapse_through_height_clamped_by_max_height__border_box_ltr"] = MarginCollapsing,
        ["block/block_margin_y_collapse_through_negative__border_box_ltr"] = MarginCollapsing,
        ["block/block_margin_y_collapse_through_percentage_min_height_indefinite_parent__border_box_ltr"] = MarginCollapsing,
        ["block/block_margin_y_collapse_through_positive__border_box_ltr"] = MarginCollapsing,
        ["block/block_margin_y_collapse_through_positive_and_negative__border_box_ltr"] = MarginCollapsing,
        ["block/block_margin_y_first_child_collapse_blocked_by_border_top__border_box_ltr"] = MarginCollapsing,
        ["block/block_margin_y_first_child_collapse_negative_equal__border_box_ltr"] = MarginCollapsing,
        ["block/block_margin_y_first_child_collapse_negative_parent_larger__border_box_ltr"] = MarginCollapsing,
        ["block/block_margin_y_first_child_collapse_negative_parent_smaller__border_box_ltr"] = MarginCollapsing,
        ["block/block_margin_y_first_child_collapse_not_blocked_by_border_bottom__border_box_ltr"] = MarginCollapsing,
        ["block/block_margin_y_first_child_collapse_not_blocked_by_padding_bottom__border_box_ltr"] = MarginCollapsing,
        ["block/block_margin_y_first_child_collapse_positive_and_negative__border_box_ltr"] = MarginCollapsing,
        ["block/block_margin_y_first_child_collapse_positive_equal__border_box_ltr"] = MarginCollapsing,
        ["block/block_margin_y_first_child_collapse_positive_parent_larger__border_box_ltr"] = MarginCollapsing,
        ["block/block_margin_y_first_child_collapse_positive_parent_smaller__border_box_ltr"] = MarginCollapsing,
        ["block/block_margin_y_first_granchild_collapse_positive_and_negative__border_box_ltr"] = MarginCollapsing,
        ["block/block_margin_y_first_granchild_collapse_positive_equal__border_box_ltr"] = MarginCollapsing,
        ["block/block_margin_y_last_child_collapse_blocked_by_border_bottom__border_box_ltr"] = MarginCollapsing,
        ["block/block_margin_y_last_child_collapse_negative_equal__border_box_ltr"] = MarginCollapsing,
        ["block/block_margin_y_last_child_collapse_negative_parent_larger__border_box_ltr"] = MarginCollapsing,
        ["block/block_margin_y_last_child_collapse_negative_parent_smaller__border_box_ltr"] = MarginCollapsing,
        ["block/block_margin_y_last_child_collapse_not_blocked_by_border_top__border_box_ltr"] = MarginCollapsing,
        ["block/block_margin_y_last_child_collapse_not_blocked_by_padding_top__border_box_ltr"] = MarginCollapsing,
        ["block/block_margin_y_last_child_collapse_not_blocked_by_smaller_min_height__border_box_ltr"] = MarginCollapsing,
        ["block/block_margin_y_last_child_collapse_positive_and_negative__border_box_ltr"] = MarginCollapsing,
        ["block/block_margin_y_last_child_collapse_positive_equal__border_box_ltr"] = MarginCollapsing,
        ["block/block_margin_y_last_child_collapse_positive_parent_larger__border_box_ltr"] = MarginCollapsing,
        ["block/block_margin_y_last_child_collapse_positive_parent_smaller__border_box_ltr"] = MarginCollapsing,
        ["block/block_margin_y_last_granchild_collapse_positive_equal__border_box_ltr"] = MarginCollapsing,
        ["block/block_margin_y_sibling_collapse_negative__border_box_ltr"] = MarginCollapsing,
        ["block/block_margin_y_sibling_collapse_negative_percentage__border_box_ltr"] = MarginCollapsing,
        ["block/block_margin_y_sibling_collapse_positive__border_box_ltr"] = MarginCollapsing,
        ["block/block_margin_y_sibling_collapse_positive_percentage__border_box_ltr"] = MarginCollapsing,
        ["block/block_margin_y_total_collapse__border_box_ltr"] = MarginCollapsing,
        ["block/block_margin_y_total_collapse_complex__border_box_ltr"] = MarginCollapsing,
        ["blockflex/blockflex_margin_y_collapse_through_blocked_by_flex__border_box_ltr"] = MarginCollapsing,
    };

    public static string? ExclusionOf(string name, XElement test)
    {
        if (Explicit.TryGetValue(name, out string? reason))
        {
            return reason;
        }

        XElement root = test.Element("input")!.Elements().First();
        XElement viewport = test.Element("viewport")!;
        bool definiteViewport =
            viewport.Attribute("width")?.Value.EndsWith("px", StringComparison.Ordinal) == true
            && viewport.Attribute("height")?.Value.EndsWith("px", StringComparison.Ordinal) == true;
        if (root.Attribute("position")?.Value == "absolute" && definiteViewport)
        {
            return "an absolutely positioned root in a definite viewport (row L2)";
        }

        foreach (XElement element in root.DescendantsAndSelf())
        {
            if (FeatureExclusion(element, isRoot: element == root) is { } feature)
            {
                return feature;
            }
        }

        return null;
    }

    private static string? FeatureExclusion(XElement e, bool isRoot)
    {
        foreach (XAttribute attribute in e.Attributes())
        {
            string value = attribute.Value;
            string? reason = attribute.Name.LocalName switch
            {
                "display" when value is "grid" => "Grid (row P02)",
                "display" when value is "none" => "a node that is not shown (row L2)",
                "display" when value is not ("block" or "flow-root" or "flex") => $"display {value}",
                "position" when value is "absolute" && !isRoot => "absolute positioning (row L2)",
                "top" or "left" or "bottom" or "right" when !isRoot => "an inset of relative or absolute positioning (row L2)",
                "aspect-ratio" => "aspect-ratio (row L2)",
                "flex-wrap" when value.Contains("balance", StringComparison.Ordinal) => "flex-wrap: balance (row L2)",
                "flex-line-count" => "flex-line-count with flex-wrap: balance (row L2)",
                "align-items" or "align-self" or "justify-items" or "justify-self" when value is "self-start" or "self-end" =>
                    $"{attribute.Name.LocalName}: {value}, the item's own writing direction (row P03)",
                "text-align" => "text-align moving a block's children (CSS's history; the design does not take it, §3.2)",
                "flex-basis" when value is "content" => "flex-basis: content (row L3)",
                "overflow-x" or "overflow-y" when value is not "visible" => "overflow (row V2)",
                "scrollbar-width" => "a scrollbar's width (row V2)",
                "direction" when value is "rtl" => "right-to-left (row P03)",
                "writing-mode" => "a vertical writing mode (row P03)",
                "box-sizing" when value is "content-box" => "content-box sizing (the design keeps border-box, §3.2)",
                "float" or "clear" => "floats (the design has none, §3.2)",
                "width" or "height" or "flex-basis" when value.StartsWith("fit-content(", StringComparison.Ordinal) || value is "stretch" =>
                    $"the size {value} (row L3)",
                "width"
                or "height"
                or "flex-basis"
                or "min-width"
                or "min-height"
                or "max-width"
                or "max-height" when value.StartsWith("calc(", StringComparison.Ordinal) => $"the size {value} (not in the design's lengths, §3.2)",
                "display"
                or "position"
                or "flex-direction"
                or "flex-wrap"
                or "justify-content"
                or "align-items"
                or "align-self"
                or "align-content"
                or "justify-items"
                or "justify-self"
                or "column-gap"
                or "row-gap"
                or "direction"
                or "box-sizing"
                or "overflow-x"
                or "overflow-y" => null,
                "width"
                or "height"
                or "min-width"
                or "min-height"
                or "max-width"
                or "max-height"
                or "flex-basis"
                or "flex-grow"
                or "flex-shrink"
                or "top"
                or "left"
                or "bottom"
                or "right" => null,
                string property
                    when property.StartsWith("margin-", StringComparison.Ordinal)
                        || property.StartsWith("padding-", StringComparison.Ordinal)
                        || property.StartsWith("border-", StringComparison.Ordinal) => null,
                string property => $"the property {property} (not read by these tests)",
            };
            if (reason is not null)
            {
                return reason;
            }
        }

        return null;
    }
}
