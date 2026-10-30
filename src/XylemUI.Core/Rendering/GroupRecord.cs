namespace XylemUI;

/// <summary>
/// An opacity group (row R4c): its alpha, whether it is drawn into a layer, and the bounds of what it draws, in physical pixels.
/// </summary>
/// <remarks>
/// The bounds are the union of its children's, each cut to its clips; ResolveGlyphs adds the glyphs it places. A clip's layer (row
/// R4d) is a group of alpha 1 that is always a layer: the rounded clips past <see cref="DisplayList.MaxRoundedClips"/> are applied
/// when it is composited. A clip's layer keeps the state around it and the clip's layer around it, whose clips the hit test applies
/// to the regions inside (row R4e).
/// </remarks>
internal struct GroupRecord
{
    /// <summary>The group's opacity; 1 for a clip's layer.</summary>
    public float Alpha;

    /// <summary>Whether the children are drawn into a layer that is blended with <see cref="Alpha"/>.</summary>
    public bool Layer;

    /// <summary>Whether the group is a clip's layer.</summary>
    public bool Clip;

    /// <summary>The left edge of the bounds of what the group draws.</summary>
    public float Left;

    /// <summary>The top edge of the bounds.</summary>
    public float Top;

    /// <summary>The right edge of the bounds.</summary>
    public float Right;

    /// <summary>The bottom edge of the bounds.</summary>
    public float Bottom;

    /// <summary>The state around the group, as an index into the list's states, or -1 for none.</summary>
    public int State;

    /// <summary>The clip's layer around the group, as an index into the list's groups, or -1 for none.</summary>
    public int ClipLayer;
}
