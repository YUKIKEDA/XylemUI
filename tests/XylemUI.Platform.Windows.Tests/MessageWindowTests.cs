using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace XylemUI.Platform.Windows.Tests;

// The host's message-only windows: the loop's wake window and the clipboard's owner (design §6.0). The window is made on the UI
// thread and destroyed there, since a window belongs to the thread that made it.
[Collection(UiThreadGroup.Name)]
public sealed class MessageWindowTests(UiThreadFixture ui)
{
    // A class is the process's: no other code registers this name, and the one test that uses it runs once in its process.
    private const string ClassName = "XylemUI.Tests.MessageWindow";

    /// <summary>
    /// A class that is registered already, as an earlier call that registered it and then failed to make its window leaves it, is
    /// used as it is: the window is made (PR #937's review).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A class name of this test's own, registered by a first call whose window is then destroyed: the class stays registered
    ///   with no window, as after a failed <c>CreateWindowExW</c>.
    ///
    /// Steps:
    /// - Make a message-only window of the same class again.
    ///
    /// Expected:
    /// - Both calls give a window and an error of 0; the first window is destroyed, and the second exists.
    /// </remarks>
    [Fact]
    public void Create_ClassRegisteredAlready_MakesTheWindow() =>
        ui.Invoke(() =>
        {
            HWND first = MessageWindow.Create(ClassName, out int firstError);
            bool destroyed = DestroyWindow(first);

            HWND second = MessageWindow.Create(ClassName, out int secondError);
            try
            {
                Assert.NotEqual(HWND.NULL, first);
                Assert.Equal(0, firstError);
                Assert.True(destroyed);
                Assert.NotEqual(HWND.NULL, second);
                Assert.Equal(0, secondError);
                Assert.True(IsWindow(second));
            }
            finally
            {
                if (second != HWND.NULL)
                {
                    _ = DestroyWindow(second);
                }
            }
        });
}
