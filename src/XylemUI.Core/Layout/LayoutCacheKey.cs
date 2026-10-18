using System.Runtime.CompilerServices;

namespace XylemUI;

/// <summary>
/// What a cached layout result of a node depends on (design §2.4, §3.2): the input as far as it matters, and the versions of
/// the node's style, children, and descendants' layout.
/// </summary>
/// <param name="Known">The known sizes.</param>
/// <param name="KnownWidthIsDefinite">Whether the known width is definite; <see langword="true"/> when the width is not known.</param>
/// <param name="KnownHeightIsDefinite">The same, for the height.</param>
/// <param name="Parent">The parent size, on the axes where the node's own style uses a percentage of it; NaN elsewhere.</param>
/// <param name="Available">The available space, on the axes whose size is not known; a fixed value elsewhere.</param>
/// <param name="SizingMode">Whether the node's size styles applied.</param>
/// <param name="LayoutStyleVersion">The node's <see cref="NodeVersions.LayoutStyle"/>.</param>
/// <param name="ChildStructureVersion">The node's <see cref="NodeVersions.Children"/>.</param>
/// <param name="DescendantLayoutVersion">The node's <see cref="NodeVersions.DescendantLayout"/>.</param>
internal readonly record struct LayoutCacheKey(
    OptionalSize Known,
    bool KnownWidthIsDefinite,
    bool KnownHeightIsDefinite,
    OptionalSize Parent,
    AvailableSize Available,
    SizingMode SizingMode,
    ulong LayoutStyleVersion,
    ulong ChildStructureVersion,
    ulong DescendantLayoutVersion
)
{
    /// <summary>
    /// Returns the key of <paramref name="input"/> for <paramref name="node"/>. The available space of an axis whose size is
    /// known does not change the result (Taffy keys the same way), and neither does the parent size on an axis where the node
    /// uses no percentage of it (design §3.2).
    /// </summary>
    public static LayoutCacheKey Of(in UiNode node, in LayoutInput input)
    {
        OptionalSize known = input.KnownDimensions;
        bool knownWidth = !float.IsNaN(known.Width);
        bool knownHeight = !float.IsNaN(known.Height);
        return new(
            known,
            !knownWidth || input.KnownWidthIsDefinite,
            !knownHeight || input.KnownHeightIsDefinite,
            new OptionalSize(
                node.Layout.UsesPercentOfParentWidth ? input.ParentSize.Width : float.NaN,
                node.Layout.UsesPercentOfParentHeight ? input.ParentSize.Height : float.NaN
            ),
            new AvailableSize(knownWidth ? default : input.Available.Width, knownHeight ? default : input.Available.Height),
            input.SizingMode,
            node.Versions.LayoutStyle,
            node.Versions.Children,
            node.Versions.DescendantLayout
        );
    }

    /// <summary>
    /// Returns a value indicating whether both keys stand for the same input and versions. Field by field, NaN equal to NaN:
    /// the compiler's comparison went through a comparer per field and cost about 200 ns a hit in a scan of the measurement
    /// cache (row L1a's layout scenario).
    /// </summary>
    public bool Equals(LayoutCacheKey other) => Matches(in other);

    /// <summary>
    /// Returns the same as <see cref="Equals(LayoutCacheKey)"/> without copying the other key.
    /// </summary>
    public bool Matches(in LayoutCacheKey other) =>
        DescendantLayoutVersion == other.DescendantLayoutVersion
        && LayoutStyleVersion == other.LayoutStyleVersion
        && ChildStructureVersion == other.ChildStructureVersion
        && Same(Known.Width, other.Known.Width)
        && Same(Known.Height, other.Known.Height)
        && Available.Width.Kind == other.Available.Width.Kind
        && Available.Height.Kind == other.Available.Height.Kind
        && Available.Width.Value == other.Available.Width.Value
        && Available.Height.Value == other.Available.Height.Value
        && Same(Parent.Width, other.Parent.Width)
        && Same(Parent.Height, other.Parent.Height)
        && KnownWidthIsDefinite == other.KnownWidthIsDefinite
        && KnownHeightIsDefinite == other.KnownHeightIsDefinite
        && SizingMode == other.SizingMode;

    /// <summary>
    /// Returns the same as <see cref="Matches"/> with the key of <paramref name="input"/> for <paramref name="node"/>, without
    /// making that key: a parent asks it of every clean child it lays out again (row L4).
    /// </summary>
    public bool MatchesInput(in UiNode node, in LayoutInput input)
    {
        OptionalSize known = input.KnownDimensions;
        bool knownWidth = !float.IsNaN(known.Width);
        bool knownHeight = !float.IsNaN(known.Height);
        return DescendantLayoutVersion == node.Versions.DescendantLayout
            && LayoutStyleVersion == node.Versions.LayoutStyle
            && ChildStructureVersion == node.Versions.Children
            && Same(Known.Width, known.Width)
            && Same(Known.Height, known.Height)
            && (knownWidth || (Available.Width.Kind == input.Available.Width.Kind && Available.Width.Value == input.Available.Width.Value))
            && (knownHeight || (Available.Height.Kind == input.Available.Height.Kind && Available.Height.Value == input.Available.Height.Value))
            && (!node.Layout.UsesPercentOfParentWidth || Same(Parent.Width, input.ParentSize.Width))
            && (!node.Layout.UsesPercentOfParentHeight || Same(Parent.Height, input.ParentSize.Height))
            && KnownWidthIsDefinite == (!knownWidth || input.KnownWidthIsDefinite)
            && KnownHeightIsDefinite == (!knownHeight || input.KnownHeightIsDefinite)
            && SizingMode == input.SizingMode;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Written by hand with <see cref="Equals(LayoutCacheKey)"/>, which replaces the compiler's comparison for its cost; the
    /// caches compare keys and never hash them.
    /// </remarks>
    public override int GetHashCode() => HashCode.Combine(Known, Available, Parent, DescendantLayoutVersion, LayoutStyleVersion);

    private static bool Same(float a, float b) => a == b || (float.IsNaN(a) && float.IsNaN(b));
}

/// <summary>
/// The results of the measuring runs of a node (design §3.2): <see cref="Capacity"/> entries, the one not used for longest
/// overwritten first (Taffy's cache of measurements).
/// </summary>
internal struct MeasureCache
{
    /// <summary>
    /// The number of entries: 9 in L1a; 4 since a leaf's min-content and max-content widths come from its content widths
    /// (row L4: the same runs, 360 bytes a node instead of 760; with 2, a width change took three times as long).
    /// </summary>
    public const int Capacity = 4;

    /// <summary>
    /// The results of the node's base-size step as a flex item, in its container's last measuring run (row L4). A container is
    /// measured and laid out with different inputs in one pass, so each kind of run keeps its own. They sit here, apart from
    /// the node: on the node they made it 656 bytes instead of 528, and a width change 11 to 14 % slower.
    /// </summary>
    public FlexBaseMemo MeasureBase;

    /// <summary>
    /// The same, in its container's last layout run.
    /// </summary>
    public FlexBaseMemo LayoutBase;

    private Entries _entries;
    private ushort _recentlyUsed;
    private byte _next;
    private byte _count;

    // The content widths of a leaf (row L4), as Chromium keeps them: one answer for every height unless they depend on it, and
    // then the answer for the last height asked.
    private IntrinsicWidths _anyHeight;
    private IntrinsicWidths _atHeight;
    private float _height;
    private bool _hasAnyHeight;
    private bool _hasAtHeight;

    // After the flags, in the struct's padding: as a field before the entries it made the cache 464 bytes a node instead of 456,
    // and the first layout of row L1a's scene (S6b's meaning) 7 % slower in two sets of 6 rounds; here it is level with L4.
    private uint _evictedGeneration;

    /// <summary>
    /// Gets or sets the parent's <see cref="LayoutState.CacheGeneration"/> when an entry or the content widths for one height were
    /// overwritten; 0 when none was. While it matches, a value the parent read may be gone, so a change of content cannot be
    /// compared with it and goes on to the parent (design §2.4.1, row L5).
    /// </summary>
    public uint EvictedGeneration
    {
        readonly get => _evictedGeneration;
        set => _evictedGeneration = value;
    }

    /// <summary>
    /// Returns the content widths kept for a content height of <paramref name="knownHeight"/> (NaN when it is not decided).
    /// </summary>
    public readonly bool TryGetIntrinsicWidths(float knownHeight, out IntrinsicWidths widths)
    {
        if (_hasAnyHeight && (!_anyHeight.DependsOnHeight || float.IsNaN(knownHeight)))
        {
            widths = _anyHeight;
            return true;
        }

        if (_hasAtHeight && !float.IsNaN(knownHeight) && _height == knownHeight)
        {
            widths = _atHeight;
            return true;
        }

        widths = default;
        return false;
    }

    /// <summary>
    /// Keeps the content widths returned for <paramref name="knownHeight"/>. Returns <see langword="true"/> when they replace
    /// the widths kept for another height.
    /// </summary>
    public bool StoreIntrinsicWidths(float knownHeight, IntrinsicWidths widths)
    {
        if (float.IsNaN(knownHeight) || !widths.DependsOnHeight)
        {
            _anyHeight = widths;
            _hasAnyHeight = true;
            return false;
        }

        bool overwrites = _hasAtHeight && _height != knownHeight;
        _atHeight = widths;
        _height = knownHeight;
        _hasAtHeight = true;
        return overwrites;
    }

    /// <summary>
    /// Returns the content widths kept for every height, if any.
    /// </summary>
    public readonly bool TryGetAnyHeightWidths(out IntrinsicWidths widths)
    {
        widths = _anyHeight;
        return _hasAnyHeight;
    }

    /// <summary>
    /// Returns the content widths kept for one height, and that height, if any.
    /// </summary>
    public readonly bool TryGetAtHeightWidths(out float knownHeight, out IntrinsicWidths widths)
    {
        knownHeight = _height;
        widths = _atHeight;
        return _hasAtHeight;
    }

    /// <summary>
    /// Gets the number of entries.
    /// </summary>
    public readonly int Count => _count;

    /// <summary>
    /// Returns the entry at <paramref name="index"/>, below <see cref="Count"/>.
    /// </summary>
    public readonly void GetEntry(int index, out LayoutCacheKey key, out RequestedAxis axis, out Size size)
    {
        ref readonly Entry entry = ref _entries[index];
        key = entry.Key;
        axis = entry.Axis;
        size = entry.Size;
    }

    /// <summary>
    /// Returns the size cached for <paramref name="key"/>. An entry measured for both axes serves either; one measured for one
    /// axis serves only that axis, since its other axis means nothing.
    /// </summary>
    public bool TryGet(in LayoutCacheKey key, RequestedAxis axis, out Size size)
    {
        for (int i = 0; i < _count; i++)
        {
            ref Entry entry = ref _entries[i];
            if ((entry.Axis == RequestedAxis.Both || entry.Axis == axis) && entry.Key.Matches(in key))
            {
                _recentlyUsed |= (ushort)(1 << i);
                size = entry.Size;
                return true;
            }
        }

        size = default;
        return false;
    }

    /// <summary>
    /// Keeps the size of a measuring run. Returns <see langword="true"/> when it replaces the entry of another input.
    /// </summary>
    public bool Store(in LayoutCacheKey key, RequestedAxis axis, Size size)
    {
        for (int i = 0; i < _count; i++)
        {
            ref Entry existing = ref _entries[i];
            if (existing.Axis == axis && existing.Key.Matches(in key))
            {
                existing.Size = size;
                _recentlyUsed |= (ushort)(1 << i);
                return false;
            }
        }

        int index;
        bool overwrites = _count == Capacity;
        if (!overwrites)
        {
            index = _count++;
        }
        else
        {
            // The clock of Taffy's cache: skip, and clear, the entries used since the hand last passed them.
            while ((_recentlyUsed & (1 << _next)) != 0)
            {
                _recentlyUsed &= (ushort)~(1 << _next);
                _next = (byte)((_next + 1) % Capacity);
            }

            index = _next;
            _next = (byte)((_next + 1) % Capacity);
        }

        _entries[index] = new Entry
        {
            Key = key,
            Axis = axis,
            Size = size,
        };
        _recentlyUsed |= (ushort)(1 << index);
        return overwrites;
    }

    /// <summary>
    /// Drops every entry and the content widths.
    /// </summary>
    public void Clear()
    {
        _count = 0;
        _next = 0;
        _recentlyUsed = 0;
        _hasAnyHeight = false;
        _hasAtHeight = false;
        _evictedGeneration = 0;
        MeasureBase = default;
        LayoutBase = default;
    }

    /// <summary>
    /// Gives every entry the node's current descendant version: a layout run moved the children, and a measurement does not
    /// depend on where they were placed (a change of content inside drops the cache instead).
    /// </summary>
    public void Restamp(ulong descendantLayoutVersion)
    {
        for (int i = 0; i < _count; i++)
        {
            ref Entry entry = ref _entries[i];
            entry.Key = entry.Key with { DescendantLayoutVersion = descendantLayoutVersion };
        }
    }

    private struct Entry
    {
        public LayoutCacheKey Key;
        public RequestedAxis Axis;
        public Size Size;
    }

    [InlineArray(Capacity)]
    private struct Entries
    {
        private Entry _first;
    }
}
