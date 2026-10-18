namespace XylemUI;

/// <summary>
/// The results of a flex item's base-size step (CSS Flexbox §9.2.3 and the automatic minimum of §4.5) in its container's last
/// run, with what the step read of the container: a container solved again uses them instead of asking the item again (design
/// §3.2, row L4). They sit in the item's <see cref="MeasureCache"/>, so marking the item dirty (a change of its style or its
/// content) drops them. A descendant laid out again on its own, without marking the item, is inside a boundary whose content
/// the item did not read, so the results stay.
/// </summary>
internal struct FlexBaseMemo
{
    /// <summary>
    /// The results were stored.
    /// </summary>
    public bool Valid;

    /// <summary>
    /// The container's main axis is horizontal.
    /// </summary>
    public bool Row;

    /// <summary>
    /// The step measured the item's content, so a container that uses the results marks the item as read (design §2.4.1).
    /// </summary>
    public bool ReadContent;

    /// <summary>
    /// The container's main space was a min-content constraint.
    /// </summary>
    public bool MinContentMain;

    /// <summary>
    /// The kind of the container's cross space.
    /// </summary>
    public AvailableSpaceKind CrossKind;

    /// <summary>
    /// The value of the container's cross space.
    /// </summary>
    public float CrossValue;

    /// <summary>
    /// The width of the container's content box, which an item's percentage margins and padding resolve against, where the
    /// item uses a percentage; NaN elsewhere. A row's width can be known but not definite, and then it is not in
    /// <see cref="PercentMain"/> (PR #103's review).
    /// </summary>
    public float InnerWidth;

    /// <summary>
    /// The cross size of the container's content box.
    /// </summary>
    public float CrossParent;

    /// <summary>
    /// The main size of the container's content box where it is definite, NaN elsewhere.
    /// </summary>
    public float PercentMain;

    /// <summary>
    /// The container's <see cref="NodeVersions.LayoutStyle"/> (its <c>align-items</c> gives the item's <c>align-self</c>).
    /// </summary>
    public ulong ContainerStyle;

    /// <summary>
    /// The flex base size.
    /// </summary>
    public float FlexBasis;

    /// <summary>
    /// The base size is definite (CSS Flexbox §9.8).
    /// </summary>
    public bool FlexBasisIsDefinite;

    /// <summary>
    /// The used minimum main size.
    /// </summary>
    public float ResolvedMinMain;
}
