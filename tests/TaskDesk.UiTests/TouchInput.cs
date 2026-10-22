using System.Drawing;
using System.Runtime.InteropServices;

namespace TaskDesk.UiTests;

/// <summary>
/// One finger (or two) on the screen through Windows' touch injection (<c>InjectTouchInput</c>), in physical screen pixels,
/// with the timing a person's touch has: WinUI gets a real touch pointer, its press-and-hold and its swipes.
/// </summary>
internal static class TouchInput
{
    private const int StepInterval = 15;
    private const uint PointerFlagInRange = 0x0002;
    private const uint PointerFlagInContact = 0x0004;
    private const uint PointerFlagDown = 0x10000;
    private const uint PointerFlagUpdate = 0x20000;
    private const uint PointerFlagUp = 0x40000;
    private const int PointerTypeTouch = 2;
    private const uint TouchFeedbackNone = 3;

    private const uint Moving = PointerFlagUpdate | PointerFlagInRange | PointerFlagInContact;
    private const uint First = 0;
    private const uint Second = 1;

    private static bool _initialized;

    /// <summary>
    /// Presses at <paramref name="from"/>, holds still for <paramref name="hold"/>, then slides to
    /// <paramref name="to"/> in small steps, rests there for <paramref name="rest"/> (300 ms by default) and lifts, as
    /// a person reordering by press-and-hold would. With <paramref name="secondFingerTap"/>, a second finger taps
    /// there halfway through the slide while the first stays down and keeps moving.
    /// </summary>
    public static void HoldAndDrag(Point from, Point to, TimeSpan hold, TimeSpan? rest = null, Point? secondFingerTap = null)
    {
        Initialize();
        Inject((First, from, PointerFlagDown | PointerFlagInRange | PointerFlagInContact));
        var held = System.Diagnostics.Stopwatch.StartNew();
        while (held.Elapsed < hold)
        {
            Thread.Sleep(StepInterval);
            Inject((First, from, Moving));
        }

        const int Steps = 40;
        for (var i = 1; i <= Steps; i++)
        {
            Thread.Sleep(StepInterval);
            var at = new Point(from.X + (to.X - from.X) * i / Steps, from.Y + (to.Y - from.Y) * i / Steps);
            if (i == Steps / 2 && secondFingerTap is { } tap)
            {
                TapWithSecondFinger(at, tap);
            }
            else
            {
                Inject((First, at, Moving));
            }
        }

        // Rest on the target a moment, as the mouse drag does, so the last move lands before the lift.
        var resting = System.Diagnostics.Stopwatch.StartNew();
        while (resting.Elapsed < (rest ?? TimeSpan.FromMilliseconds(300)))
        {
            Thread.Sleep(StepInterval);
            Inject((First, to, Moving));
        }

        Inject((First, to, PointerFlagUp));
        Thread.Sleep(200);
    }

    // The second finger lands, stays a few frames and lifts; the first, at firstAt, stays down in every frame.
    private static void TapWithSecondFinger(Point firstAt, Point tap)
    {
        Inject((First, firstAt, Moving), (Second, tap, PointerFlagDown | PointerFlagInRange | PointerFlagInContact));
        for (var i = 0; i < 5; i++)
        {
            Thread.Sleep(StepInterval);
            Inject((First, firstAt, Moving), (Second, tap, Moving));
        }

        Thread.Sleep(StepInterval);
        Inject((First, firstAt, Moving), (Second, tap, PointerFlagUp));
    }

    private static void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        if (!InitializeTouchInjection(2, TouchFeedbackNone))
        {
            throw new InvalidOperationException($"Touch injection is unavailable (error {Marshal.GetLastWin32Error()}).");
        }

        _initialized = true;
    }

    // One frame: every finger that is down, each by its own pointer id.
    private static void Inject(params (uint Id, Point At, uint Flags)[] fingers)
    {
        var contacts = fingers.Select(f => new PointerTouchInfo
        {
            PointerType = PointerTypeTouch,
            PointerId = f.Id,
            PointerFlags = f.Flags,
            PixelLocation = new NativePoint { X = f.At.X, Y = f.At.Y },
            Contact = new NativeRect { Left = f.At.X, Top = f.At.Y, Right = f.At.X, Bottom = f.At.Y },
        }).ToArray();
        if (!InjectTouchInput((uint)contacts.Length, contacts))
        {
            throw new InvalidOperationException(
                $"Touch injection failed at {fingers[0].At} (error {Marshal.GetLastWin32Error()}).");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    // POINTER_TOUCH_INFO: POINTER_INFO, then the touch fields.
    [StructLayout(LayoutKind.Sequential)]
    private struct PointerTouchInfo
    {
        public int PointerType;
        public uint PointerId;
        public uint FrameId;
        public uint PointerFlags;
        public IntPtr SourceDevice;
        public IntPtr HwndTarget;
        public NativePoint PixelLocation;
        public NativePoint HimetricLocation;
        public NativePoint PixelLocationRaw;
        public NativePoint HimetricLocationRaw;
        public uint Time;
        public uint HistoryCount;
        public int InputData;
        public uint KeyStates;
        public ulong PerformanceCount;
        public int ButtonChangeType;
        public uint TouchFlags;
        public uint TouchMask;
        public NativeRect Contact;
        public NativeRect ContactRaw;
        public uint Orientation;
        public uint Pressure;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InitializeTouchInjection(uint maxCount, uint feedbackMode);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InjectTouchInput(uint count, [In] PointerTouchInfo[] contacts);
}
