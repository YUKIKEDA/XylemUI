using XylemUI.Testing;
using XylemUI.Testing.XUnit;
using static XylemUI.Elements;
using static XylemUI.Primitives.Primitive;

namespace XylemUI.Primitives.Tests;

// The content of a popup looks a context up from where the popup was declared (row RC1, design §2.13): the popup layer is
// elsewhere in the tree, but the popup's scope is under the scope that declared it. Each test declares the part inside a value
// "inside" of a key whose default is "default", and reads the key where the popup's content is made.
public sealed class ContextPopupTests
{
    private static readonly Context<string> Key = new("default");

    /// <summary>
    /// The panel of a menu and the panel of its submenu find the value provided around the menu's anchor.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An open menu, declared inside the value, whose panel has an item with a submenu; both panels read the key.
    ///
    /// Steps:
    /// - Settle, then press Right to open the submenu.
    ///
    /// Expected:
    /// - Both panels read "inside".
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task MenuAndSubmenu_FindTheValueOfWhereTheMenuWasDeclared()
    {
        var open = Signal.Create(true);
        string? menu = null;
        string? submenu = null;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            Key.Provide(
                "inside",
                () =>
                    Box()
                        .Width(100)
                        .Height(30)
                        .Menu(
                            open,
                            () =>
                            {
                                menu = Key.Use();
                                return Column(
                                    SubmenuItem(
                                        Box().Width(200).Height(30),
                                        () =>
                                        {
                                            submenu = Key.Use();
                                            return Column(MenuItem(Box().Width(200).Height(30), static () => { }));
                                        }
                                    )
                                );
                            }
                        )
            )
        );
        _ = app.PumpAndSettle();

        _ = app.Keyboard.Press(XylemUI.Key.Right);
        _ = app.PumpAndSettle();

        Assert.Equal("inside", menu);
        Assert.Equal("inside", submenu);
    }

    /// <summary>
    /// The panel of a context menu finds the value provided around its node.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A 300 × 50 node at (0, 0) with a context menu, declared inside the value; the panel reads the key.
    ///
    /// Steps:
    /// - Click the right button at (50, 25) and settle.
    ///
    /// Expected:
    /// - The panel read "inside".
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task ContextMenu_FindsTheValueOfWhereItWasDeclared()
    {
        string? seen = null;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            Key.Provide(
                "inside",
                () =>
                    Box()
                        .Width(300)
                        .Height(50)
                        .Focusable()
                        .ContextMenu(() =>
                        {
                            seen = Key.Use();
                            return MenuItem(Box().Width(200).Height(30), static () => { });
                        })
            )
        );
        _ = app.PumpAndSettle();

        app.Pointer.Click(50, 25, PointerButton.Right);
        _ = app.PumpAndSettle();

        Assert.Equal("inside", seen);
    }

    /// <summary>
    /// The panel of a menu bar's menu finds the value provided around the bar.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A menu bar with one 40 × 20 item at (0, 0), declared inside the value; the item's panel reads the key.
    ///
    /// Steps:
    /// - Click the item at (20, 10) and settle.
    ///
    /// Expected:
    /// - The panel read "inside".
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task MenuBar_FindsTheValueOfWhereItWasDeclared()
    {
        string? seen = null;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            Key.Provide(
                "inside",
                () =>
                    Column(
                        MenuBar(
                            MenuBarItem(
                                Box().Width(40).Height(20),
                                () =>
                                {
                                    seen = Key.Use();
                                    return Column(MenuItem(Box().Width(80).Height(20), static () => { }));
                                }
                            )
                        )
                    )
            )
        );
        _ = app.PumpAndSettle();

        app.Pointer.Click(20, 10);
        _ = app.PumpAndSettle();

        Assert.Equal("inside", seen);
    }

    /// <summary>
    /// The rows of a select's list find the value provided around the select.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A focused select of two options, declared inside the value; the function that makes a row records the key each time.
    ///
    /// Steps:
    /// - Forget what was recorded so far, press F4 to open the list, and settle.
    ///
    /// Expected:
    /// - A popup is open, rows were made for it, and every one read "inside".
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Select_FindsTheValueOfWhereItWasDeclared()
    {
        var value = Signal.Create("a");
        var seen = new List<string>();
        Element select = default;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            Key.Provide(
                "inside",
                () =>
                    select = Select(
                            value,
                            ["a", "b"],
                            _ =>
                            {
                                seen.Add(Key.Use());
                                return Box().Width(60).Height(20);
                            }
                        )
                        .Width(150)
            )
        );
        _ = app.PumpAndSettle();
        _ = app.Focus(select);
        _ = app.PumpAndSettle();
        seen.Clear();

        _ = app.Keyboard.Press(XylemUI.Key.F4);
        _ = app.PumpAndSettle();

        _ = Assert.Single(app.Popups);
        Assert.NotEmpty(seen);
        Assert.All(seen, static read => Assert.Equal("inside", read));
    }

    /// <summary>
    /// The content of a tooltip finds the value provided around its anchor.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A 100 × 30 anchor at (0, 0) with a tooltip, declared inside the value; the content reads the key.
    ///
    /// Steps:
    /// - Move the pointer over the anchor and wait 400 ms.
    ///
    /// Expected:
    /// - The tooltip is open and its content read "inside".
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Tooltip_FindsTheValueOfWhereItWasDeclared()
    {
        string? seen = null;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
            Key.Provide(
                "inside",
                () =>
                    Box()
                        .Width(100)
                        .Height(30)
                        .Tooltip(() =>
                        {
                            seen = Key.Use();
                            return Box().Width(80).Height(20);
                        })
            )
        );
        _ = app.PumpAndSettle();

        app.Pointer.Move(20, 10);
        app.Pointer.Move(50, 15);
        app.Pump(TimeSpan.FromMilliseconds(400));
        _ = app.PumpAndSettle();

        _ = Assert.Single(app.Popups);
        Assert.Equal("inside", seen);
    }

    /// <summary>
    /// The content of a dialog finds the value provided around where the dialog was declared, and code outside the value still
    /// reads the default.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A dialog, declared inside the value, whose content reads the key; beside the value, the root function reads the key too.
    ///
    /// Steps:
    /// - Open the dialog and settle.
    ///
    /// Expected:
    /// - The dialog's content read "inside"; the root function read "default".
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task Dialog_FindsTheValueOfWhereItWasDeclared()
    {
        var open = Signal.Create(false);
        string? seen = null;
        string? outside = null;
        await using HeadlessApp app = await HeadlessApp.StartAsync(() =>
        {
            outside = Key.Use();
            return Column(
                Key.Provide(
                    "inside",
                    () =>
                        Dialog(
                            open,
                            () =>
                            {
                                seen = Key.Use();
                                return Box().Width(100).Height(40).Focusable();
                            }
                        )
                )
            );
        });
        _ = app.PumpAndSettle();

        open.Set(true);
        _ = app.PumpAndSettle();

        Assert.Equal("inside", seen);
        Assert.Equal("default", outside);
    }
}
