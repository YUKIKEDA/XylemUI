using System.Numerics;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace XylemUI.Platform.Windows.Tests;

// A touchpad's pan through DirectManipulation (row I1c, design §7, "Touch and pen"). A fake viewport stands in for
// DirectManipulation's, and the tests call the handler's callbacks by hand, as Flutter's direct_manipulation_unittests.cc and
// window_unittests.cc do; DM_POINTERHITTEST and the wheel are real messages sent to a hidden window.
[Collection(UiThreadGroup.Name)]
public sealed unsafe class Win32WindowTouchpadTests(UiThreadFixture ui)
{
    private const uint DmPointerHitTest = 0x0250;
    private const DIRECTMANIPULATION_STATUS Ready = DIRECTMANIPULATION_STATUS.DIRECTMANIPULATION_READY;
    private const DIRECTMANIPULATION_STATUS Running = DIRECTMANIPULATION_STATUS.DIRECTMANIPULATION_RUNNING;
    private const DIRECTMANIPULATION_STATUS Inertia = DIRECTMANIPULATION_STATUS.DIRECTMANIPULATION_INERTIA;

    /// <summary>
    /// <c>DM_POINTERHITTEST</c> hands a touchpad's contact to the viewport once, and no other pointer's, nor one whose type cannot be
    /// read; it asks for no frame (Flutter's <c>TouchPadHitTest</c>, <c>PointerHitTest</c>, and
    /// <c>UnknownPointerTypeSkipsDirectManipulation</c>).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A hidden window with a fake viewport; pointer 7 a touchpad, 8 a touch, 9 of no type.
    ///
    /// Steps:
    /// - Send <c>DM_POINTERHITTEST</c> for 7, 8, and 9.
    ///
    /// Expected:
    /// - The viewport got one contact, 7's; no frame was asked for.
    /// </remarks>
    [Fact]
    public void PointerHitTest_TouchpadAlone_SetsTheContact() =>
        ui.Invoke(() =>
        {
            using var window = new Win32Window("touchpad", 200, 100);
            var viewport = new FakeViewport();
            window.UseTouchpadViewport(viewport);
            window.PointerReader = new TypeReader { Types = { [7] = POINTER_INPUT_TYPE.PT_TOUCHPAD, [8] = POINTER_INPUT_TYPE.PT_TOUCH } };
            int frames = 0;
            window.GestureFrameNeeded = () => frames++;

            foreach (uint id in new uint[] { 7, 8, 9 })
            {
                _ = SendMessageW(window.Hwnd, DmPointerHitTest, id, 0);
            }

            Assert.Equal([7u], viewport.Contacts);
            Assert.Equal(0, frames);
        });

    /// <summary>
    /// The viewport's states and translations make a pan's start, its updates in logical pixels, and its end, delivered with the
    /// frame's time when the frame updates the gestures; the OS's inertia gives no update, and its end moves the content back once,
    /// whose own changes are no pan (Flutter's <c>TestGesture</c> and <c>TestInertiaCamcelNotSentAtInertiaEnd</c>).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A hidden window, at its scale s, with a fake viewport whose content starts at 0.
    ///
    /// Steps:
    /// - READY to RUNNING; translations (0, −20 s) and (6 s, −50 s); RUNNING to INERTIA; translation (6 s, −80 s); INERTIA to READY;
    ///   then the reset's READY to RUNNING and RUNNING to READY. Update the gestures with a frame's time of 1 s.
    ///
    /// Expected:
    /// - The viewport updated once and reset once.
    /// - The inputs are a pan's start, updates of (0, −20) and (6, −30) logical pixels, and an end, each of the touchpad, the same
    ///   number, and the frame's time.
    /// </remarks>
    [Fact]
    public void StatesAndTranslations_MakeAPan_WithTheFramesTime() =>
        ui.Invoke(() =>
        {
            using var window = new Win32Window("touchpad", 200, 100);
            var viewport = new FakeViewport();
            window.UseTouchpadViewport(viewport);
            float s = window.ScaleFactor;
            var inputs = new List<PointerInput>();
            window.PointerInputReceived = (in PointerInput input) => inputs.Add(input);

            window.TouchpadStatusChanged(Running, Ready);
            window.TouchpadContentUpdated(0, -20 * s);
            window.TouchpadContentUpdated(6 * s, -50 * s);
            window.TouchpadStatusChanged(Inertia, Running);
            window.TouchpadContentUpdated(6 * s, -80 * s);
            window.TouchpadStatusChanged(Ready, Inertia);
            window.TouchpadStatusChanged(Running, Ready);
            window.TouchpadStatusChanged(Ready, Running);
            _ = ((IPointerPlatform)window).UpdateGestures(TimeSpan.FromSeconds(1));

            Assert.Equal((1, 1), (viewport.Updates, viewport.Resets));
            Assert.Equal(
                [
                    (PointerInputKind.PanStart, Vector2.Zero),
                    (PointerInputKind.PanUpdate, new Vector2(0, -20)),
                    (PointerInputKind.PanUpdate, new Vector2(6, -30)),
                    (PointerInputKind.PanEnd, Vector2.Zero),
                ],
                inputs.Select(static i => (i.Kind, i.Delta))
            );
            Assert.All(
                inputs,
                i => Assert.Equal((PointerType.Touchpad, inputs[0].PointerId, TimeSpan.FromSeconds(1)), (i.PointerType, i.PointerId, i.Timestamp))
            );
            Assert.NotEqual(0, inputs[0].PointerId);
        });

    /// <summary>
    /// The OS's inertia queues no pan input, whatever the content does meanwhile (Flutter's <c>direct_manipulation.cc</c> line
    /// 152), and the end is queued once.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The gestures of a fake viewport whose content starts at 0.
    ///
    /// Steps:
    /// - READY to RUNNING; translation (0, −10); RUNNING to INERTIA; translations (0, −30) and (0, −45).
    ///
    /// Expected:
    /// - Three inputs are queued: the start, the update, and the end.
    /// </remarks>
    [Fact]
    public void Inertia_QueuesNoInput()
    {
        var gestures = new TouchpadGestures(new FakeViewport());

        gestures.OnStatusChanged(Running, Ready);
        gestures.OnContentUpdated(0, -10);
        gestures.OnStatusChanged(Inertia, Running);
        gestures.OnContentUpdated(0, -30);
        gestures.OnContentUpdated(0, -45);

        Assert.Equal(3, gestures.Pending);
    }

    /// <summary>
    /// A content not yet back from the last gesture starts a pan from where it is: only the change goes on (Flutter's
    /// <c>TestGestureWithInitialData</c>).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A hidden window, at its scale s, with a fake viewport whose content is at (5 s, 100 s).
    ///
    /// Steps:
    /// - READY to RUNNING; translation (5 s, 80 s); update the gestures.
    ///
    /// Expected:
    /// - A start and an update of (0, −20) logical pixels.
    /// </remarks>
    [Fact]
    public void ContentNotBack_PanStartsFromWhereItIs() =>
        ui.Invoke(() =>
        {
            using var window = new Win32Window("touchpad", 200, 100);
            float s = window.ScaleFactor;
            var viewport = new FakeViewport { Translation = (5 * s, 100 * s) };
            window.UseTouchpadViewport(viewport);
            var inputs = new List<PointerInput>();
            window.PointerInputReceived = (in PointerInput input) => inputs.Add(input);

            window.TouchpadStatusChanged(Running, Ready);
            window.TouchpadContentUpdated(5 * s, 80 * s);
            _ = ((IPointerPlatform)window).UpdateGestures(TimeSpan.Zero);

            Assert.Equal(
                [(PointerInputKind.PanStart, Vector2.Zero), (PointerInputKind.PanUpdate, new Vector2(0, -20))],
                inputs.Select(static i => (i.Kind, i.Delta))
            );
        });

    /// <summary>
    /// The frames poll the viewport from an interaction's begin to its end: the begin asks for one frame, and updating the gestures
    /// says to go on until the end; a reset whose status does not come ends once the content reads 0 again.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A hidden window with a fake viewport.
    ///
    /// Steps:
    /// - Update the gestures; an interaction's begin; update; a second begin; its end; update.
    /// - A pan that runs into the OS's inertia and its end (the content at (0, −40)), with no READY after the reset; update with the
    ///   content still away; then with the content at 0.
    ///
    /// Expected:
    /// - Before the begin, no polling; the begin asked for one frame, and the second asked for none; polling until the end, none
    ///   after.
    /// - Polling while the content is away after the reset, none once it reads 0.
    /// </remarks>
    [Fact]
    public void Interaction_PollsFromItsBeginToItsEnd_AndUntilTheContentIsBack() =>
        ui.Invoke(() =>
        {
            using var window = new Win32Window("touchpad", 200, 100);
            var viewport = new FakeViewport();
            window.UseTouchpadViewport(viewport);
            var platform = (IPointerPlatform)window;
            int frames = 0;
            window.GestureFrameNeeded = () => frames++;

            bool before = platform.UpdateGestures(TimeSpan.Zero);
            window.TouchpadInteraction(begin: true);
            bool during = platform.UpdateGestures(TimeSpan.Zero);
            window.TouchpadInteraction(begin: true);
            window.TouchpadInteraction(begin: false);
            bool after = platform.UpdateGestures(TimeSpan.Zero);

            window.TouchpadStatusChanged(Running, Ready);
            window.TouchpadStatusChanged(Inertia, Running);
            viewport.Translation = (0, -40);
            window.TouchpadStatusChanged(Ready, Inertia);
            bool resetting = platform.UpdateGestures(TimeSpan.Zero);
            viewport.Translation = (0, 0);
            bool back = platform.UpdateGestures(TimeSpan.Zero);

            Assert.Equal((false, true, false, 1), (before, during, after, frames));
            Assert.Equal((true, false), (resetting, back));
        });

    /// <summary>
    /// The wheel's messages are dropped from the pan's start until the OS's inertia ends, and go on otherwise: before a pan, after
    /// it, and after a contact that never ran (design §7, "Touch and pen", I1c; PR #406's review).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A hidden window with a fake viewport; pointer 7 a touchpad.
    ///
    /// Steps:
    /// - A wheel; <c>DM_POINTERHITTEST</c> for 7 and a wheel; READY to RUNNING and a wheel; RUNNING to INERTIA and a wheel; INERTIA
    ///   to READY and a wheel.
    ///
    /// Expected:
    /// - The window delivered the first wheel, the one after the contact alone, and the last; not those while running or in inertia.
    /// </remarks>
    [Fact]
    public void Wheel_DroppedFromThePansStartToTheEndOfItsInertia() =>
        ui.Invoke(() =>
        {
            using var window = new Win32Window("touchpad", 200, 100);
            window.UseTouchpadViewport(new FakeViewport());
            window.PointerReader = new TypeReader { Types = { [7] = POINTER_INPUT_TYPE.PT_TOUCHPAD } };
            int wheels = 0;
            var counted = new List<int>();
            window.PointerInputReceived = (in PointerInput input) =>
            {
                if (input.Kind == PointerInputKind.Wheel)
                {
                    wheels++;
                }
            };

            void Wheel()
            {
                _ = SendMessageW(window.Hwnd, WM.WM_MOUSEWHEEL, (nuint)(120 << 16), 0);
                counted.Add(wheels);
            }

            Wheel();
            _ = SendMessageW(window.Hwnd, DmPointerHitTest, 7, 0);
            Wheel();
            window.TouchpadStatusChanged(Running, Ready);
            Wheel();
            window.TouchpadStatusChanged(Inertia, Running);
            Wheel();
            window.TouchpadStatusChanged(Ready, Inertia);
            Wheel();

            Assert.Equal([1, 2, 2, 2, 3], counted);
        });

    /// <summary>
    /// Updating the gestures and delivering a pan's inputs allocate nothing once warm.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A hidden window with a fake viewport, warmed by one round.
    ///
    /// Steps:
    /// - 1,000 rounds of a start, 4 translations, an end, and an update of the gestures.
    ///
    /// Expected:
    /// - No bytes were allocated, and 6,000 inputs were delivered.
    /// </remarks>
    [Fact]
    public void SteadyState_UpdateGestures_AllocatesNothing() =>
        ui.Invoke(() =>
        {
            using var window = new Win32Window("touchpad", 200, 100);
            window.UseTouchpadViewport(new FakeViewport());
            var platform = (IPointerPlatform)window;
            int inputs = 0;
            window.PointerInputReceived = (in PointerInput _) => inputs++;
            void Round()
            {
                window.TouchpadStatusChanged(Running, Ready);
                for (int i = 1; i <= 4; i++)
                {
                    window.TouchpadContentUpdated(0, -10 * i);
                }

                window.TouchpadStatusChanged(Ready, Running);
                _ = platform.UpdateGestures(TimeSpan.Zero);
            }

            Round();
            inputs = 0;
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1_000; i++)
            {
                Round();
            }

            Assert.Equal((0L, 6_000), (GC.GetAllocatedBytesForCurrentThread() - before, inputs));
        });

    /// <summary>
    /// A window on a thread in COM's single-threaded apartment makes DirectManipulation's viewport, and destroying the window tears
    /// it down; COM's initialization asks for that apartment.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The tests' UI thread, which is in the single-threaded apartment; a new thread in the multithreaded one.
    ///
    /// Steps:
    /// - Make a hidden window and dispose it. Initialize COM for the UI on the new thread, and on another thread in the
    ///   single-threaded apartment.
    ///
    /// Expected:
    /// - The window had DirectManipulation's viewport; disposing it threw nothing.
    /// - The initialization threw on the multithreaded thread, with <c>RPC_E_CHANGED_MODE</c> and a message that names
    ///   <c>[STAThread]</c>, and did not on the other.
    /// </remarks>
    [Fact]
    public void RealViewport_IsMadeAndTornDown_InASingleThreadedApartment()
    {
        bool made = false;
        ui.Invoke(() =>
        {
            var window = new Win32Window("touchpad", 200, 100);
            made = window.TouchpadViewport is DirectManipulationViewport;
            window.Dispose();
        });

        Exception? multithreaded = null;
        Exception? single = null;
        var mta = new Thread(() => multithreaded = Record(Com.InitializeUiThread));
        mta.SetApartmentState(ApartmentState.MTA);
        mta.Start();
        mta.Join();
        var sta = new Thread(() =>
            single = Record(() =>
            {
                Com.InitializeUiThread();
                Com.UninitializeUiThread();
            })
        );
        sta.SetApartmentState(ApartmentState.STA);
        sta.Start();
        sta.Join();

        Assert.True(made);
        var thrown = Assert.IsType<InvalidOperationException>(multithreaded);
        Assert.Equal(RPC.RPC_E_CHANGED_MODE, thrown.HResult);
        Assert.Contains("[STAThread]", thrown.Message, StringComparison.Ordinal);
        Assert.Null(single);
    }

    private static Exception? Record(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    // A viewport that records what the window asked and gives the translation the test sets.
    private sealed class FakeViewport : ITouchpadViewport
    {
        public List<uint> Contacts { get; } = [];

        public int Updates { get; private set; }

        public int Resets { get; private set; }

        public (float X, float Y) Translation { get; set; }

        public void SetContact(uint pointerId) => Contacts.Add(pointerId);

        public void Update() => Updates++;

        public void Resize(int width, int height) { }

        public (float X, float Y) ReadTranslation() => Translation;

        public void Reset() => Resets++;

        public void Dispose() { }
    }

    // Gives the pointer types the test sets.
    private sealed class TypeReader : IPointerReader
    {
        public Dictionary<uint, POINTER_INPUT_TYPE> Types { get; } = [];

        public bool TryGetType(uint pointerId, out POINTER_INPUT_TYPE type) => Types.TryGetValue(pointerId, out type);

        public bool TryGetPenInfo(uint pointerId, out POINTER_PEN_INFO info)
        {
            info = default;
            return false;
        }

        public bool TryGetTouchInfo(uint pointerId, out POINTER_TOUCH_INFO info)
        {
            info = default;
            return false;
        }

        public int ReadPenHistory(uint pointerId, Span<POINTER_PEN_INFO> destination) => 0;

        public int ReadTouchHistory(uint pointerId, Span<POINTER_TOUCH_INFO> destination) => 0;
    }
}
