using System.Numerics;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace XylemUI.Platform.Windows.Tests;

// The input method through TSF (row P02c, design §4.7): the document's calls on the field's client, its locks and the changes it
// holds while one is granted, the read-only status, the boxes for the candidate window, the display attributes as clauses, and
// the window's input method on and off. The document is called directly, as TSF calls it; the sink of the manager is a test's.
[Collection(UiThreadGroup.Name)]
public sealed unsafe class TsfTests(UiThreadFixture ui)
{
    private const int NoLock = unchecked((int)0x80040201);
    private const int NoLayout = unchecked((int)0x80040206);
    private const int Synchronous = unchecked((int)0x80040208);
    private const int ReadOnlyError = unchecked((int)0x80040209);
    private const int Async = 0x00040300;
    private const uint Read = 0x2;
    private const uint ReadWrite = 0x6;
    private const uint Sync = 0x1;

    /// <summary>
    /// The text, its end, and the selection are read only under a lock; under one, they are the field's.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A document of a field holding "abcdef" with "cd" selected.
    ///
    /// Steps:
    /// - Read the text and the selection without a lock; then, in a read lock, read the text from 1 to 4, the whole text with -1,
    ///   the end, and the selection.
    ///
    /// Expected:
    /// - <c>TS_E_NOLOCK</c> without a lock; "bcd", "abcdef", 6, and 2 to 4 with one.
    /// </remarks>
    [Fact]
    public void Reads_UnderALock_AreTheFieldsText() =>
        ui.Invoke(() =>
        {
            var client = new FakeClient("abcdef") { Selection = new TextSelection(2, 4) };
            (TsfDocument document, FakeSink sink) = Make(client);
            int noLock = ReadText(document, 0, -1, out _);

            string part = string.Empty;
            string whole = string.Empty;
            int end = 0;
            (int Start, int End) selected = default;
            sink.Granted = flags =>
            {
                _ = ReadText(document, 1, 4, out part);
                _ = ReadText(document, 0, -1, out whole);
                int length;
                _ = document.GetEndAcp(&length);
                end = length;
                TS_SELECTION_ACP selection;
                uint count;
                _ = document.GetSelection(1, &selection, &count);
                selected = (selection.acpStart, selection.acpEnd);
            };
            int session;
            _ = document.RequestLock(Read | Sync, &session);

            Assert.Equal(NoLock, noLock);
            Assert.Equal(("bcd", "abcdef", 6), (part, whole, end));
            Assert.Equal((2, 4), selected);
        });

    /// <summary>
    /// Under a read-write lock, committed text replaces its range, text being composed goes to the composition, the selection
    /// moves, and an insertion that only asks answers the selection; none of it is told back.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A document of an empty field.
    ///
    /// Steps:
    /// - In a read-write lock: insert "ab" at the selection; ask where an insertion would go; start a composition and set "に" at
    ///   2 to 2; select 0 to 1.
    ///
    /// Expected:
    /// - The field took a replacement of 0 to 0 with "ab", then a composition of 2 to 2 with "に"; the query answered 2 to 2; the
    ///   selection is 0 to 1; the sink was told nothing.
    /// </remarks>
    [Fact]
    public void Writes_UnderAReadWriteLock_GoToTheField_AndAreNotToldBack() =>
        ui.Invoke(() =>
        {
            var client = new FakeClient(string.Empty);
            (TsfDocument document, FakeSink sink) = Make(client);
            (int Start, int End) query = (-1, -1);
            sink.Granted = flags =>
            {
                int start;
                int end;
                TS_TEXTCHANGE change;
                fixed (char* ab = "ab")
                {
                    _ = document.InsertTextAtSelection(0, ab, 2, &start, &end, &change);
                }

                _ = document.InsertTextAtSelection(0x2, null, 0, &start, &end, null);
                query = (start, end);
                _ = document.StartComposition(null);
                fixed (char* ni = "に")
                {
                    _ = document.SetText(2, 2, ni, 1, &change);
                }

                var selection = new TS_SELECTION_ACP { acpStart = 0, acpEnd = 1 };
                _ = document.SetSelection(1, &selection);
            };
            int session;
            _ = document.RequestLock(ReadWrite | Sync, &session);

            Assert.Equal(["replace 0 0 ab", "compose 2 2 に"], client.Calls);
            Assert.Equal((2, 2), query);
            Assert.Equal(new TextSelection(0, 1), client.Selection);
            Assert.Empty(sink.Told);
        });

    /// <summary>
    /// While the manager holds a lock, a synchronous request is refused and an asynchronous one is granted after it; the changes
    /// the field tells meanwhile are told after the lock, in order (design §4.7).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A document of a field holding "abc".
    ///
    /// Steps:
    /// - In a read lock: ask for a synchronous lock and an asynchronous read-write lock; the field tells a change of the text, of
    ///   the selection, and of the layout. Outside a lock, the field tells a change of the text.
    ///
    /// Expected:
    /// - <c>TS_E_SYNCHRONOUS</c> and <c>TS_S_ASYNC</c>; the locks granted are read, then read-write; the sink was told the text
    ///   change, the selection, and the layout after the locks, then the second change at once.
    /// </remarks>
    [Fact]
    public void Locks_HeldChangesAreToldAfter_AndASynchronousRequestIsRefused() =>
        ui.Invoke(() =>
        {
            var client = new FakeClient("abc");
            (TsfDocument document, FakeSink sink) = Make(client);
            int syncResult = 0;
            int asyncResult = 0;
            var granted = new List<uint>();
            sink.Granted = flags =>
            {
                granted.Add(flags);
                if (granted.Count > 1)
                {
                    return;
                }

                int result;
                _ = document.RequestLock(ReadWrite | Sync, &result);
                syncResult = result;
                _ = document.RequestLock(ReadWrite, &result);
                asyncResult = result;
                document.TextChanged(0, 1, 2);
                document.SelectionChanged();
                document.LayoutChanged();
                Assert.Empty(sink.Told);
            };
            int session;
            _ = document.RequestLock(Read | Sync, &session);
            document.TextChanged(1, 1, 3);

            Assert.Equal((Synchronous, Async), (syncResult, asyncResult));
            Assert.Equal([Read, ReadWrite], granted);
            Assert.Equal(["text 0 1 2", "selection", "layout", "text 1 1 3"], sink.Told);
        });

    /// <summary>
    /// A read-only field's document says so in its status, and refuses text.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A document of a read-only field holding "ro".
    ///
    /// Steps:
    /// - Read the status; in a read-write lock, set "x" at 0 to 0.
    ///
    /// Expected:
    /// - The dynamic flags hold <c>TS_SD_READONLY</c>; the set returns <c>TS_E_READONLY</c>, and the field took no call.
    /// </remarks>
    [Fact]
    public void ReadOnlyField_SaysSo_AndRefusesText() =>
        ui.Invoke(() =>
        {
            var client = new FakeClient("ro") { ReadOnly = true };
            (TsfDocument document, FakeSink sink) = Make(client);
            TS_STATUS status;
            _ = document.GetStatus(&status);
            int result = 0;
            sink.Granted = flags =>
            {
                TS_TEXTCHANGE change;
                fixed (char* x = "x")
                {
                    result = document.SetText(0, 0, x, 1, &change);
                }
            };
            int session;
            _ = document.RequestLock(ReadWrite | Sync, &session);

            Assert.Equal(1u, status.dwDynamicFlags & 1u);
            Assert.Equal(ReadOnlyError, result);
            Assert.Empty(client.Calls);
        });

    /// <summary>
    /// The box of a range is the field's, turned into the screen's pixels; before the field is laid out, the answer is that there
    /// is no layout.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A document of a field whose box of a range is 10, 20, 30, 15, with a conversion to the screen that adds 100 and doubles.
    ///
    /// Steps:
    /// - In a read lock, ask for the box of 0 to 2; make the field not laid out; ask again.
    ///
    /// Expected:
    /// - 120, 140, 180, 170, not clipped; then <c>TS_E_NOLAYOUT</c>.
    /// </remarks>
    [Fact]
    public void TextExt_IsTheFieldsBoxOnTheScreen_OrNoLayout() =>
        ui.Invoke(() =>
        {
            var client = new FakeClient("abc") { Box = new Rect(10, 20, 30, 15) };
            (TsfDocument document, FakeSink sink) = Make(client);
            RECT first = default;
            int second = 0;
            sink.Granted = flags =>
            {
                RECT box;
                BOOL clipped;
                _ = document.GetTextExt(0, 2, &box, &clipped);
                first = box;
                client.Box = null;
                second = document.GetTextExt(0, 2, &box, &clipped);
            };
            int session;
            _ = document.RequestLock(Read | Sync, &session);

            Assert.Equal((120, 140, 180, 170), (first.left, first.top, first.right, first.bottom));
            Assert.Equal(NoLayout, second);
        });

    /// <summary>
    /// The position under a point of the screen is the field's under the point in the window's pixels; before the field is laid
    /// out, the answer is that there is no layout.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A document of a field laid out, whose position under a point is its x, with a conversion from the screen that halves.
    ///
    /// Steps:
    /// - In a read lock, ask for the position under the screen's 8, 40; make the field not laid out; ask again.
    ///
    /// Expected:
    /// - 4; then <c>TS_E_NOLAYOUT</c>.
    /// </remarks>
    [Fact]
    public void AcpFromPoint_IsTheFieldsPositionUnderThePoint_OrNoLayout() =>
        ui.Invoke(() =>
        {
            var client = new FakeClient("abcdef") { Box = new Rect(0, 0, 10, 10) };
            (TsfDocument document, FakeSink sink) = Make(client);
            int first = -1;
            int second = 0;
            sink.Granted = flags =>
            {
                var point = new POINT(8, 40);
                int position;
                _ = document.GetAcpFromPoint(&point, &position);
                first = position;
                client.Box = null;
                second = document.GetAcpFromPoint(&point, &position);
            };
            int session;
            _ = document.RequestLock(Read | Sync, &session);

            Assert.Equal(4, first);
            Assert.Equal(NoLayout, second);
        });

    /// <summary>
    /// A display attribute becomes the run it asks for: the shape of its line (a squiggle solid until row R5, none for no line), its
    /// bold line as thick, and its colors from a COLORREF (design §4.7).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Four attributes: dotted; bold and solid with a line of COLORREF 0x00332211; a squiggle; no line.
    ///
    /// Steps:
    /// - Turn each into a run over 1 to 3.
    ///
    /// Expected:
    /// - Dotted, thin; solid, thick, line color 11 22 33; solid; no line. Colors of type none are null.
    /// </remarks>
    [Fact]
    public void DisplayAttribute_BecomesTheRunItAsksFor()
    {
        var dotted = new TF_DISPLAYATTRIBUTE { lsStyle = TF_DA_LINESTYLE.TF_LS_DOT };
        var bold = new TF_DISPLAYATTRIBUTE { lsStyle = TF_DA_LINESTYLE.TF_LS_SOLID, fBoldLine = true };
        bold.crLine.type = TF_DA_COLORTYPE.TF_CT_COLORREF;
        bold.crLine.Anonymous.cr = 0x00332211;
        var squiggle = new TF_DISPLAYATTRIBUTE { lsStyle = TF_DA_LINESTYLE.TF_LS_SQUIGGLE };
        var none = new TF_DISPLAYATTRIBUTE { lsStyle = TF_DA_LINESTYLE.TF_LS_NONE };

        Assert.Equal(new ImeAttributeRun(1, 3, DecorationStyle.Dotted, false), TsfAttributes.RunOf(dotted, 1, 3));
        Assert.Equal(new ImeAttributeRun(1, 3, DecorationStyle.Solid, true, Color.FromRgb(0x112233)), TsfAttributes.RunOf(bold, 1, 3));
        Assert.Equal(DecorationStyle.Solid, TsfAttributes.RunOf(squiggle, 1, 3).Line);
        Assert.Null(TsfAttributes.RunOf(none, 1, 3).Line);
    }

    /// <summary>
    /// The window's input method is on while a field that takes it is attached, and off when it is detached and for a field that
    /// hides its text: no document and no IMM32 input context then (spike #510).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A hidden window with TSF's input method.
    ///
    /// Steps:
    /// - Attach a field; read the input context; detach; read it; attach a field that hides its text; read it.
    ///
    /// Expected:
    /// - A document and an input context; then neither; then neither for the hidden field.
    /// </remarks>
    [Fact]
    public void InputMethod_OnForAField_OffWhenDetachedAndForAHiddenField() =>
        ui.Invoke(() =>
        {
            using var window = new Win32Window("tsf", 200, 100);
            using TsfTextInput input = TsfTextInput.TryCreate(window) ?? throw new InvalidOperationException("TSF is not there.");

            input.Attach(new FakeClient("abc"));
            (bool, bool) on = (input.Attached is not null, HasContext(window));
            input.Detach();
            (bool, bool) off = (input.Attached is not null, HasContext(window));
            input.Attach(new FakeClient("pw") { Kind = TextInputKind.Password });
            (bool, bool) hidden = (input.Attached is not null, HasContext(window));
            input.Detach();

            Assert.Equal((true, true), on);
            Assert.Equal((false, false), off);
            Assert.Equal((false, false), hidden);
        });

    // Reads a range of the document's text into a string; the result of GetText.
    private static int ReadText(TsfDocument document, int start, int end, out string text)
    {
        char* buffer = stackalloc char[16];
        uint fetched = 0;
        uint runs;
        int next;
        TS_RUNINFO run;
        int result = document.GetText(start, end, buffer, 16, &fetched, &run, 1, &runs, &next);
        text = new string(buffer, 0, (int)fetched);
        return result;
    }

    private static bool HasContext(Win32Window window)
    {
        HIMC context = ImmGetContext((HWND)window.Handle);
        if (context == HIMC.NULL)
        {
            return false;
        }

        _ = ImmReleaseContext((HWND)window.Handle, context);
        return true;
    }

    private static (TsfDocument Document, FakeSink Sink) Make(FakeClient client)
    {
        var sink = new FakeSink();
        var document = new TsfDocument(
            client,
            box => new RECT(
                (int)((box.X + 100) * 2) - 100,
                (int)((box.Y + 100) * 2) - 100,
                (int)((box.X + box.Width + 100) * 2) - 100,
                (int)((box.Y + box.Height + 100) * 2) - 100
            ),
            () => default,
            point => new Vector2(point.x / 2f, point.y / 2f)
        )
        {
            Sink = sink,
        };
        return (document, sink);
    }

    // The manager's sink: it runs Granted in each lock, and records what it was told.
    private sealed class FakeSink : ITsfSink
    {
        public Action<uint>? Granted { get; set; }

        public List<string> Told { get; } = [];

        public int OnLockGranted(uint flags)
        {
            Granted?.Invoke(flags);
            return 0;
        }

        public void OnTextChange(int start, int oldEnd, int newEnd) => Told.Add($"text {start} {oldEnd} {newEnd}");

        public void OnSelectionChange() => Told.Add("selection");

        public void OnLayoutChange() => Told.Add("layout");
    }

    // A field's client over a string, which records the edits it takes.
    private sealed class FakeClient(string text) : ITextInputClient
    {
        private string _text = text;

        public List<string> Calls { get; } = [];

        public int Length => _text.Length;

        public TextSelection Selection { get; set; }

        public (int Start, int End)? Composition { get; private set; }

        public bool ReadOnly { get; init; }

        public TextInputKind Kind { get; init; } = TextInputKind.Text;

        public Rect? Box { get; set; }

        public void Select(TextSelection selection) => Selection = selection;

        public void CopyText(int start, Span<char> into) => _text.AsSpan(start, into.Length).CopyTo(into);

        public bool BoundsOf(int start, int end, out Rect bounds)
        {
            bounds = Box ?? default;
            return Box is not null;
        }

        // The position under a point is its x, while the field is laid out (it has a box).
        public bool PositionAt(Vector2 point, out int position)
        {
            position = (int)point.X;
            return Box is not null;
        }

        public void Replace(int start, int end, ReadOnlySpan<char> value)
        {
            Calls.Add($"replace {start} {end} {value.ToString()}");
            _text = string.Concat(_text.AsSpan(0, start), value, _text.AsSpan(end));
            Selection = TextSelection.Collapsed(start + value.Length);
        }

        public void Compose(int start, int end, ReadOnlySpan<char> value, int cursor, ReadOnlySpan<ImeAttributeRun> clauses)
        {
            Calls.Add($"compose {start} {end} {value.ToString()}");
            _text = string.Concat(_text.AsSpan(0, start), value, _text.AsSpan(end));
            Composition = (start, start + value.Length);
            Selection = TextSelection.Collapsed(start + cursor);
        }

        public void EndComposition() => Composition = null;
    }
}
