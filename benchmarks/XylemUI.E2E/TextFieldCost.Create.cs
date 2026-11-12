using System.Diagnostics;
using XylemUI.Platform.Windows;
using XylemUI.Primitives;
using XylemUI.Text;
using NodeHandle = XylemUI.Handle<XylemUI.UiNode>;

namespace XylemUI.E2E;

// The cost of making a text field (row KM2b): its nodes, its state, and its handlers, of which the field has one for each of its
// actions (design §7, "The default keys and actions"). 100 fields of one line with a short text are made in a scope under a root
// and the scope is disposed; text_field_create_us and text_field_create_bytes are for one field, the median of 21 samples after
// 5 unmeasured. No frame runs: the layout and the display list of a field are in the other figures.
internal static partial class TextFieldCost
{
    private const int CreateFields = 100;
    private const int CreateSamples = 21;
    private const int CreateWarm = 5;

    private static void RunCreate(Report report)
    {
        using var fonts = new DirectWriteFontProvider();
        using var context = new TextContext(fonts);
        var tree = new UiTree(context);
        NodeHandle root = tree.CreateNode(new LayoutStyle());
        var microseconds = new double[CreateSamples];
        var bytes = new double[CreateSamples];
        for (int sample = -CreateWarm; sample < CreateSamples; sample++)
        {
            ScopeHandle scope;
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp();
            UiTree.Current = tree;
            try
            {
                scope = Scope.Create(() =>
                {
                    for (int i = 0; i < CreateFields; i++)
                    {
                        tree.AppendChild(root, Primitive.TextField(Signal.Create("text")).Width(200).Node);
                    }
                });
            }
            finally
            {
                UiTree.Current = null;
            }

            double elapsed = Stopwatch.GetElapsedTime(start).TotalMicroseconds;
            long made = GC.GetAllocatedBytesForCurrentThread() - allocated;
            if (sample >= 0)
            {
                microseconds[sample] = elapsed / CreateFields;
                bytes[sample] = (double)made / CreateFields;
            }

            Scope.Dispose(scope);
        }

        Array.Sort(microseconds);
        Array.Sort(bytes);
        report.Number("text_field_create_us_p50", microseconds[CreateSamples / 2]);
        report.Number("text_field_create_bytes_p50", bytes[CreateSamples / 2]);
    }
}
