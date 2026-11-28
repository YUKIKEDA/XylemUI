using BenchmarkDotNet.Attributes;
using XylemUI.Text;

namespace XylemUI.Benchmarks;

// What a text leaf's style setter compares when a whole TextStyle is written over the style it holds (row T15, design §3.2, "The
// shape of row T15", "How to compare"). Three forms, for a value equal to the one held, a change of the color alone, and a change of
// the font (its weight, so that no string is compared):
// - WholeRecord: the record's equality on the whole style, as before row T15. It cannot tell a change of the color alone from one
//   the layout reads, so it would mark the layout for both.
// - KeyThenColor: the key's equality, then the color's, as two comparisons.
// - OneCompare: the one comparison that returns the kind of change, TextStyleKey.Compare, which src uses.
// Only OneCompare is in src; the other two live here, so that no code src does not call is kept there.
[MemoryDiagnoser]
public class TextStyleCompareBenchmarks
{
    private TextStyle _whole;
    private TextStyleKey _key;
    private Color? _color;
    private TextStyle _written;

    // The style written: 0 the one held, 1 the one held in another color, 2 the one held in a bold font.
    [Params(0, 1, 2)]
    public int Case { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var font = new FontRequest("Yu Gothic UI", FontWeight.Normal, FontSlant.Normal, FontStretch.Normal);
        var held = new TextStyle(font, 14, TextLanguage.FromTag("ja"), float.NaN, Color.FromRgb(0x202020));
        _whole = held;
        _key = TextStyleKey.Of(in held);
        _color = held.Color;
        _written = Case switch
        {
            0 => held,
            1 => held with { Color = Color.FromRgb(0xCC3333) },
            _ => held with { Font = font with { Weight = FontWeight.Bold } },
        };
    }

    // Before row T15: true when nothing changed; a change of color and one of the font alike are false.
    [Benchmark(Baseline = true)]
    public bool WholeRecord() => _written == _whole;

    // 2 when the key changed, 1 when only the color did, 0 when neither did (TextStyleChange's values).
    [Benchmark]
    public int KeyThenColor()
    {
        if (TextStyleKey.Of(in _written) != _key)
        {
            return 2;
        }

        return _written.Color != _color ? 1 : 0;
    }

    // The same values, from src's comparison.
    [Benchmark]
    public int OneCompare() => (int)TextStyleKey.Compare(in _key, _color, in _written);
}
