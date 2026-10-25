namespace XylemUI;

// An opacity group (row R4c): its alpha, whether it is drawn into a layer, and the bounds of what it draws, in physical pixels.
// The bounds are the union of its children's, each cut to its clips; ResolveGlyphs adds the glyphs it places. A clip's layer (row
// R4d) is a group of alpha 1 that is always a layer: the rounded clips past DisplayList.MaxRoundedClips are applied when it is
// composited. A clip's layer keeps the state around it and the clip's layer around it, whose clips the hit test applies to the
// regions inside (row R4e).
internal struct GroupRecord
{
    public float Alpha;
    public bool Layer;
    public bool Clip;
    public float Left;
    public float Top;
    public float Right;
    public float Bottom;
    public int State;
    public int ClipLayer;
}
