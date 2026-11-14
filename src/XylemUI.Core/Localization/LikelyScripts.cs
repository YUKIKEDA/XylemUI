using System.Buffers.Binary;

namespace XylemUI;

/// <summary>
/// Finds the likely script of a locale (design §4.11, "Resolution", "Adding the script a region implies"), as the Add Likely Subtags
/// operation of UTS #35 part 1 ("Likely Subtags") fills it in: a script the locale writes is kept, and an empty one is looked up with
/// the language and the region, then with the language alone. Only the script is found; the region and the language are not.
/// </summary>
/// <remarks>
/// A script is returned as its 4 ASCII letters packed in a <see cref="uint"/>, the first letter in the lowest byte, so that two
/// scripts are compared without a string; 0 stands for no script found. The table is CLDR's likelySubtags cut down by
/// tools/CldrTableGen to the keys without a script (<c>CldrLikelyScripts</c>). A lookup allocates nothing.
/// </remarks>
internal static class LikelyScripts
{
    /// <summary>
    /// Returns the script a locale writes, or else the likely script of its language and region.
    /// </summary>
    /// <remarks>
    /// The script <c>Zzzz</c> and the region <c>ZZ</c> count as empty, as step 1 of Add Likely Subtags removes them. CLDR's
    /// canonicalization (aliases.json) is not applied: the locale is canonical as the IANA registry makes it (design §4.11), so a
    /// language only CLDR replaces is looked up as it is written. <c>tl</c> has a key of its own and gives <c>Latn</c>; <c>sh</c>
    /// has none and gives no script.
    /// </remarks>
    /// <param name="locale">The locale.</param>
    /// <returns>The packed script, or 0 when the locale writes none and the table has none for it.</returns>
    public static uint Infer(Locale locale)
    {
        string? script = locale.Script;
        if (script is not null && script != "Zzzz")
        {
            return Pack(script);
        }

        if (!TryPackLanguage(locale.Language, out uint language))
        {
            return 0;
        }

        // Step 2 of Add Likely Subtags looks up language_script_region, language_script, language_region, then language; with no
        // script, the first two are not looked up, and the table holds no key with a script. The region ZZ needs no case: the
        // table has no key with it (tools/CldrTableGen stops on one), so its lookup falls to the language, as with no region.
        string? region = locale.Region;
        if (region is not null && TryFind((language << 16) | PackRegion(region), out uint found))
        {
            return found;
        }

        return TryFind(language << 16, out found) ? found : 0;
    }

    /// <summary>
    /// Returns the 4 letters of a script packed in a <see cref="uint"/>, the first letter in the lowest byte.
    /// </summary>
    /// <param name="script">The script, 4 ASCII letters.</param>
    /// <returns>The packed script.</returns>
    public static uint Pack(ReadOnlySpan<char> script) => script[0] | ((uint)script[1] << 8) | ((uint)script[2] << 16) | ((uint)script[3] << 24);

    // A language of 2 or 3 lower-case letters, each 1 to 26 in base 27; the table has no longer language.
    private static bool TryPackLanguage(string language, out uint packed)
    {
        packed = 0;
        if (language.Length is < 2 or > 3)
        {
            return false;
        }

        for (int i = 0; i < 3; i++)
        {
            packed = (packed * 27) + (i < language.Length ? (uint)(language[i] - 'a' + 1) : 0);
        }

        return true;
    }

    // A region of 2 upper-case letters is 1 + 26 × first + second (A is 0); one of 3 digits is 1000 + its number.
    private static uint PackRegion(string region) =>
        region.Length == 2
            ? 1 + (uint)(((region[0] - 'A') * 26) + (region[1] - 'A'))
            : 1000 + (uint)(((region[0] - '0') * 100) + ((region[1] - '0') * 10) + (region[2] - '0'));

    private static bool TryFind(uint key, out uint script)
    {
        ReadOnlySpan<byte> keys = CldrLikelyScripts.Keys;
        int low = 0;
        int high = (keys.Length / 4) - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) / 2);
            uint at = BinaryPrimitives.ReadUInt32LittleEndian(keys.Slice(middle * 4, 4));
            if (at == key)
            {
                int index = CldrLikelyScripts.Values[middle];
                script = BinaryPrimitives.ReadUInt32LittleEndian(CldrLikelyScripts.Scripts.Slice(index * 4, 4));
                return true;
            }

            if (at < key)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        script = 0;
        return false;
    }
}
