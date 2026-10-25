namespace XylemUI;

// An opacity group (row R4c): its alpha, whether it is drawn into a layer, and the bounds of what it draws, in physical pixels.
// The bounds are the union of its children's; ResolveGlyphs adds the glyphs it places.
internal struct GroupRecord
{
    public float Alpha;
    public bool Layer;
    public float Left;
    public float Top;
    public float Right;
    public float Bottom;
}
