namespace XylemUI.Text;

/// <summary>
/// One OpenType feature for a range of text (design §4.8, row T12): its four-letter tag and its value, as HarfBuzz's
/// <c>hb_feature_t</c> takes them for the whole range.
/// </summary>
/// <param name="Tag">The tag, its four ASCII letters packed big-endian (<see cref="Create"/> packs them).</param>
/// <param name="Value">The value: 0 turns the feature off, 1 on, and a larger value picks an alternate.</param>
public readonly record struct FontFeature(uint Tag, uint Value)
{
    /// <summary>
    /// Makes a feature from its tag's letters.
    /// </summary>
    /// <param name="tag">The tag: four ASCII letters, such as <c>liga</c> or <c>smcp</c>.</param>
    /// <param name="value">The value.</param>
    /// <returns>The feature.</returns>
    /// <exception cref="ArgumentException">The tag is not four ASCII letters.</exception>
    public static FontFeature Create(string tag, uint value = 1)
    {
        ArgumentNullException.ThrowIfNull(tag);

        // OpenType's Tag: four characters from space to tilde (the spec's "Data types").
        if (tag.Length != 4 || tag.AsSpan().ContainsAnyExceptInRange(' ', '~'))
        {
            throw new ArgumentException("An OpenType feature's tag is four printable ASCII characters.", nameof(tag));
        }

        return new FontFeature(((uint)tag[0] << 24) | ((uint)tag[1] << 16) | ((uint)tag[2] << 8) | tag[3], value);
    }
}

/// <summary>
/// The OpenType features of a range of text (design §4.8, row T12). An app makes one and uses it again in each write, so that a
/// write allocates nothing; two ranges with the same instance share their shaping runs.
/// </summary>
public sealed class FontFeatures
{
    private readonly FontFeature[] _features;

    /// <summary>
    /// Initializes a new instance of the <see cref="FontFeatures"/> class.
    /// </summary>
    /// <param name="features">The features, applied in order.</param>
    public FontFeatures(params ReadOnlySpan<FontFeature> features) => _features = features.ToArray();

    /// <summary>
    /// Gets the features.
    /// </summary>
    public ReadOnlySpan<FontFeature> Features => _features;
}
