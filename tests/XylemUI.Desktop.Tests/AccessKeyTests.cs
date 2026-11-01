using XylemUI.Primitives;
using XylemUI.Testing.XUnit;
using static XylemUI.Elements;
using static XylemUI.Primitives.Primitive;

namespace XylemUI.Desktop.Tests;

// The access keys of the menus (row P02d2, design §13.1, "Access keys") in a headless app: the `_` of AccessText, when the key is
// underlined, and what a letter typed in an open menu does. The menus' items hold access texts in 200 × 24 boxes.
public sealed class AccessKeyTests
{
    /// <summary>
    /// An access text draws its text without the <c>_</c>, takes the character after the first single <c>_</c> as its key, and
    /// draws <c>__</c> as one <c>_</c>; with Windows' setting on, the key is underlined.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A headless app with <see cref="HeadlessOptions.KeyboardCues"/> on, holding the access texts "_Open", "Save __as", "E_xit",
    ///   and "plain".
    ///
    /// Steps:
    /// - Settle; read each leaf's text and its underlined ranges.
    ///
    /// Expected:
    /// - "Open" with (0, 1) underlined; "Save _as" with none; "Exit" with (1, 1); "plain" with none.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task AccessText_DrawsWithoutTheMarker_AndUnderlinesTheKey()
    {
        var texts = new Element[4];
        await using HeadlessApp app = await HeadlessApp.StartAsync(
            () =>
                Column(
                    texts[0] = AccessText("_Open"),
                    texts[1] = AccessText("Save __as"),
                    texts[2] = AccessText("E_xit"),
                    texts[3] = AccessText("plain")
                ),
            new HeadlessOptions { KeyboardCues = true }
        );
        _ = app.PumpAndSettle();

        Assert.Equal(
            [("Open", "0,1"), ("Save _as", string.Empty), ("Exit", "1,1"), ("plain", string.Empty)],
            texts.Select(t => (TextOf(app, t), Underlined(app, t))).ToArray()
        );
    }

    /// <summary>
    /// With Windows' setting off, Alt shows the access keys until a pointer's press; a menu opened from the keys shows them while it
    /// is open, and one opened with the pointer does not.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A focusable 300 × 50 target with a context menu of one item "_Open", and the access text "_File" in the window.
    ///
    /// Steps:
    /// - Read whether "_File" is underlined; press Alt; read; click at (500, 500); read. Focus the target; Shift+F10; read; Esc; read.
    ///   Click the right button on the target; read.
    ///
    /// Expected:
    /// - No, yes, no; yes, no; no.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Cues_ShowAfterAlt_AndInAMenuOpenedFromTheKeys_UntilAPress()
    {
        Element file = default;
        Element target = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            Column(
                file = AccessText("_File"),
                target = Box().Width(300).Height(50).Focusable().ContextMenu(() => MenuItem(AccessText("_Open"), () => { }))
            )
        );
        _ = app.PumpAndSettle();
        var seen = new List<bool>();
        void Read()
        {
            _ = app.PumpAndSettle();
            seen.Add(Underlined(app, file).Length > 0);
        }

        Read();
        _ = app.Keyboard.Press(Key.LeftAlt);
        Read();
        app.Pointer.Click(500, 500);
        Read();
        _ = app.Focus(target);
        _ = app.Keyboard.Press(Key.F10, KeyModifiers.Shift);
        Read();
        _ = app.Keyboard.Press(Key.Escape);
        Read();
        app.Pointer.Click(target, PointerButton.Right);
        Read();

        Assert.Equal([false, true, false, true, false, false], seen);
    }

    /// <summary>
    /// In an open menu, a letter, with Alt or without, and of either case, chooses the one enabled item whose key it is; several
    /// items of one key take the focus in turn, round the end; a disabled item is passed over; a submenu's key opens it with its
    /// first item focused; a letter of no item does nothing.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A focusable anchor with a menu of "_Open", "_Copy", "_Cut", "_Delete" (disabled), "_Duplicate", "_Print", and a submenu
    ///   "_More" of one item "_Zoom"; each chosen item records its name.
    ///
    /// Steps:
    /// - Open the menu; press O. Open it; press C three times, reading the focused item's name after each; press Z. Press Alt+D
    ///   (with "_Delete" disabled, "_Duplicate" alone is D's). Open it; press Shift+P. Open it; press M; read the popups and the focus.
    ///
    /// Expected:
    /// - O chooses "Open". C focuses "Copy", "Cut", "Copy", and Z does nothing. Alt+D chooses "Duplicate". Shift+P chooses "Print". M
    ///   opens the submenu: two popups, "Zoom" focused.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Letters_ChooseOrCycle_PastDisabled_AndOpenSubmenus()
    {
        var open = Signal.Create(false);
        var chosen = new List<string>();
        var items = new Dictionary<string, Element>();
        Element Item(string label, string name, bool enabled = true) =>
            items[name] = MenuItem(Box(AccessText(label)).Width(200).Height(24), () => chosen.Add(name), enabled);
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            Box()
                .Width(100)
                .Height(30)
                .Focusable()
                .Menu(
                    open,
                    () =>
                        Column(
                            Item("_Open", "Open"),
                            Item("_Copy", "Copy"),
                            Item("_Cut", "Cut"),
                            Item("_Delete", "Delete", enabled: false),
                            Item("_Duplicate", "Duplicate"),
                            Item("_Print", "Print"),
                            items["More"] = SubmenuItem(Box(AccessText("_More")).Width(200).Height(24), () => Item("_Zoom", "Zoom"))
                        )
                )
        );
        void Open()
        {
            open.Set(true);
            _ = app.PumpAndSettle();
        }

        string Focused() => items.FirstOrDefault(i => i.Value.Node == app.Tree.Focus.Focused).Key ?? "none";

        Open();
        _ = app.Keyboard.Press(Key.O);
        _ = app.PumpAndSettle();
        Open();
        var cycled = new List<string>();
        for (int i = 0; i < 3; i++)
        {
            _ = app.Keyboard.Press(Key.C);
            cycled.Add(Focused());
        }

        _ = app.Keyboard.Press(Key.Z);
        int afterZ = chosen.Count;
        _ = app.Keyboard.Press(Key.D, KeyModifiers.Alt);
        _ = app.PumpAndSettle();
        Open();
        _ = app.Keyboard.Press(Key.P, KeyModifiers.Shift);
        _ = app.PumpAndSettle();
        Open();
        _ = app.Keyboard.Press(Key.M);
        _ = app.PumpAndSettle();

        Assert.Equal(["Copy", "Cut", "Copy"], cycled);
        Assert.Equal(1, afterZ);
        Assert.Equal(["Open", "Duplicate", "Print"], chosen);
        Assert.Equal(2, app.Popups.Count);
        Assert.Equal("Zoom", Focused());
    }

    private static string TextOf(HeadlessApp app, Element text) => app.Tree.Nodes.GetRef(text.Node).Text!.Text.ToString();

    // The underlined ranges of a text leaf, as "start,length" joined by ";".
    private static string Underlined(HeadlessApp app, Element text)
    {
        TextLeaf leaf = app.Tree.Nodes.GetRef(text.Node).Text!;
        if (leaf.Ranges is not { } ranges)
        {
            return string.Empty;
        }

        return string.Join(
            ";",
            ranges
                .Paint.ToArray()
                .Where(run => run.Decoration is { } d && (d.Lines & DecorationLines.Underline) != 0)
                .Select(run => $"{run.Start},{run.Length}")
        );
    }
}
