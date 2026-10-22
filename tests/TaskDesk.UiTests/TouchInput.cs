using System.Drawing;
using System.Runtime.InteropServices;

namespace TaskDesk.UiTests;

/// <summary>
/// One finger on the screen through Windows' touch injection (<c>InjectTouchInput</c>), in physical screen pixels,
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

    private static bool _initialized;

    /// <summary>
    /// Presses at <paramref name="from"/>, holds still for <paramref name="hold"/>, then slides to
    /// <paramref name="to"/> in small steps and lifts, as a person reordering by press-and-hold would.
    /// </summary>
    public static void HoldAndDrag(Point from, Point to, TimeSpan hold)
    {
        Initialize();
        Inject(from, PointerFlagDown | PointerFlagInRange | PointerFlagInContact);
        var held = System.Diagnostics.Stopwatch.StartNew();
        while (held.Elapsed < hold)
        {
            Thread.Sleep(StepInterval);
            Inject(from, PointerFlagUpdate | PointerFlagInRange | PointerFlagInContact);
        }

        const int Steps = 40;
        for (var i = 1; i <= Steps; i++)
        {
            Thread.Sleep(StepInterval);
            Inject(
                new Point(from.X + (to.X - from.X) * i / Steps, from.Y + (to.Y - from.Y) * i / Steps),
                PointerFlagUpdate | PointerFlagInRange | PointerFlagInContact);
        }

        // Rest on the target a moment, as the mouse drag does, so the last move lands before the lift.
        for (var i = 0; i < 20; i++)
        {
            Thread.Sleep(StepInterval);
            Inject(to, PointerFlagUpdate | PointerFlagInRange | PointerFlagInContact);
        }

        Inject(to, PointerFlagUp);
        Thread.Sleep(200);
    }

    private static void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        if (!InitializeTouchInjection(1, TouchFeedbackNone))
        {
            throw new InvalidOperationException($"Touch injection is unavailable (error {Marshal.GetLastWin32Error()}).");
        }

        _initialized = true;
    }

    private static void Inject(Point at, uint flags)
    {
        var location = new NativePoint { X = at.X, Y = at.Y };
        var contact = new PointerTouchInfo
        {
            PointerType = PointerTypeTouch,
            PointerFlags = flags,
            PixelLocation = location,
            Contact = new NativeRect { Left = at.X, Top = at.Y, Right = at.X, Bottom = at.Y },
        };
        if (!InjectTouchInput(1, [contact]))
        {
            throw new InvalidOperationException($"Touch injection failed at {at} (error {Marshal.GetLastWin32Error()}).");
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
