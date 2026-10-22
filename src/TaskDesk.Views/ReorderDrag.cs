using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using TaskDesk.Core.Shell;
using TaskDesk.Presentation;
using Windows.Foundation;

namespace TaskDesk.Views;

/// <summary>
/// What a press would drag: the item, and the rows its picture shows, top to bottom (the pressed row first; for an
/// open Task, its Subtask rows after it).
/// </summary>
public sealed record DragSubject(object Item, IReadOnlyList<FrameworkElement> Rows);

/// <summary>A press that would drag: its item and the pointer that pressed.</summary>
public sealed record DragPress(object Item, DragPointer Pointer);

/// <summary>A touch or pen press held for the hold time and lifted without a drag: its item and where it lifted.</summary>
public sealed record HeldLift(object Item, Point At);

/// <summary>
/// The list side of a <see cref="ReorderDrag"/>. Every point is relative to <see cref="Surface"/>, in DIPs.
/// </summary>
public interface IReorderDragHost
{
    /// <summary>The list the drag runs over; a release outside it cancels.</summary>
    FrameworkElement Surface { get; }

    /// <summary>
    /// The visible area the list scrolls in: <see cref="Surface"/> itself, or the scroll area around it when the list
    /// scrolls inside a larger one. Its top and bottom edges scroll the list, and a release outside it cancels.
    /// </summary>
    FrameworkElement Viewport { get; }

    /// <summary>The opaque colour behind the drag picture: the theme's solid window background.</summary>
    Windows.UI.Color PictureBacking { get; }

    /// <summary>What a press at <paramref name="at"/> would drag, or null where nothing drags.</summary>
    DragSubject? SubjectAt(Point at);

    /// <summary>Shows the insertion line on the gap under <paramref name="at"/>, or hides it outside the list.</summary>
    void ShowDragAt(Point at);

    /// <summary>Hides the insertion line.</summary>
    void HideDragLine();

    /// <summary>Drops <paramref name="item"/> into the gap under <paramref name="at"/>.</summary>
    Task DropAsync(object item, Point at);

    /// <summary>
    /// Scrolls the list by <paramref name="dy"/> DIPs (negative is up), stopping at its ends. Returns false when it
    /// is already at the end it would scroll towards.
    /// </summary>
    bool ScrollBy(double dy);
}

/// <summary>
/// The app's own reordering drag (ADR 0007), shared by the lists that reorder. The list's pointer events feed it a
/// press, moves, a release and cancels, as points. A press past Windows' drag threshold starts the drag: a picture
/// of the pressed rows, taken at the press, follows the pointer (also outside the window) with the press point
/// under it, and the host's insertion line follows the gaps. A release over the list drops through the host; a
/// release outside it, or <see cref="Cancel"/>, moves nothing. The rows and the selection are never touched.
/// While the pointer is near the list's top or bottom edge, a timer on <see cref="Clock"/> scrolls the list
/// (<see cref="DragRules.EdgeScrollSpeed"/>); the host calls <see cref="ListScrolled"/> when the list scrolls, so
/// the line keeps to the gap under the pointer.
/// A touch or pen press drags only once held for <see cref="DragRules.HoldTime"/>; a move past the threshold
/// before that is a swipe, which scrolls the list and never drags.
/// Both timers run through <paramref name="owner"/>'s lifetime (<see cref="MainViewModel.CreateViewTimer"/>), so after
/// its stop neither scrolls nor holds; the window cancels the drag when it closes.
/// </summary>
public sealed partial class ReorderDrag(IReorderDragHost host, MainViewModel owner)
{
    /// <summary>How often the edge-scroll timer steps.</summary>
    public static readonly TimeSpan EdgeScrollInterval = TimeSpan.FromMilliseconds(10);

    /// <summary>The picture's opacity.</summary>
    public const double PictureOpacity = 0.8;

    // One edge-scroll step covers at most this much time, so a stalled UI thread does not jump the list.
    private static readonly TimeSpan MaxEdgeStep = TimeSpan.FromMilliseconds(250);

    private DragSubject? _subject;
    private Point _press;
    private Point _pressOffset;
    private Image? _image;
    private int _pressCount;
    // The pointer relative to the viewport, which stays put while the list scrolls (the surface may scroll with it).
    private Point _pointer;
    private ITimer? _edgeTimer;
    private long _lastEdgeFire;
    private long _pendingEdgeTime;
    private int _edgeStepQueued;
    private DragPointer _pointerKind;
    private ITimer? _holdTimer;
    // The pointer whose press the drag follows; the others' moves, releases and cancels do not reach it.
    private uint _pointerId;

    /// <summary>The clock the edge-scroll and hold timers run on; replaceable so tests need not wait.</summary>
    public TimeProvider Clock { get; set; } = owner.Clock;

    /// <summary>True once a touch or pen press has been held long enough to drag, until its release or cancel.</summary>
    public bool IsHeld { get; private set; }

    /// <summary>True from the move past the threshold to the release or cancel.</summary>
    public bool IsDragging { get; private set; }

    /// <summary>
    /// True when the last press became a drag: the tap Windows may raise at its release is no click.
    /// </summary>
    public bool LastPressDragged { get; private set; }

    /// <summary>What the press or the drag would move, from the press to the release or cancel; null otherwise.</summary>
    public object? Item => _subject?.Item;

    /// <summary>The drag picture while it shows, null otherwise.</summary>
    public Popup? Picture { get; private set; }

    /// <summary>Raised when a press that would drag starts, before its hold and its picture.</summary>
    public event EventHandler<DragPress>? Pressed;

    /// <summary>
    /// Raised when a press or a drag ends, however it ends: a release anywhere, a swipe, a lost pointer, a cancel or
    /// the next press.
    /// </summary>
    public event EventHandler? Ended;

    /// <summary>Raised when a held touch or pen press lifts without having dragged.</summary>
    public event EventHandler<HeldLift>? LiftedAfterHold;

    /// <summary>
    /// A press at <paramref name="at"/> by <paramref name="pointer"/>. Where something drags, takes its picture
    /// (completing when it is ready); the drag itself starts only on a move past the threshold, for touch and pen
    /// once the press is held.
    /// </summary>
    public async Task PressAsync(Point at, DragPointer pointer = DragPointer.Mouse)
    {
        End();
        LastPressDragged = false;
        var press = ++_pressCount;
        if (host.SubjectAt(at) is not { Rows.Count: > 0 } subject)
        {
            return;
        }

        _subject = subject;
        _press = at;
        _pointerKind = pointer;
        Pressed?.Invoke(this, new DragPress(subject.Item, pointer));
        if (DragRules.StartsByHold(pointer))
        {
            StartHold(press, subject);
        }

        var top = subject.Rows[0].TransformToVisual(host.Surface).TransformPoint(new Point(0, 0));
        _pressOffset = new Point(at.X - top.X, at.Y - top.Y);

        var image = await PictureOfAsync(subject.Rows);
        if (press != _pressCount || !ReferenceEquals(_subject, subject))
        {
            return;
        }

        _image = image;
        if (IsDragging)
        {
            ShowPicture(at);
        }
    }

    /// <summary>
    /// A move to <paramref name="at"/>. Past the threshold it starts the drag; while dragging it moves the picture
    /// and the insertion line. Returns true while a drag is on.
    /// </summary>
    public bool Move(Point at)
    {
        if (_subject is null)
        {
            return false;
        }

        if (!IsDragging)
        {
            var (thresholdX, thresholdY) = DragThreshold.Of(host.Surface);
            switch (DragRules.StartOnMove(_pointerKind, IsHeld, at.X - _press.X, at.Y - _press.Y, thresholdX, thresholdY))
            {
                case DragStart.Wait:
                    return false;
                case DragStart.Abandon:
                    End(); // A swipe: the list scrolls, and this press never drags.
                    return false;
            }

            IsDragging = true;
            LastPressDragged = true;
        }

        _pointer = InViewport(at);
        ShowPicture(at);
        host.ShowDragAt(at);
        UpdateEdgeTimer();
        return true;
    }

    /// <summary>The list scrolled under the pointer: during a drag the line moves to the gap now under it.</summary>
    public void ListScrolled()
    {
        if (IsDragging)
        {
            host.ShowDragAt(ReferenceEquals(host.Viewport, host.Surface)
                ? _pointer
                : host.Viewport.TransformToVisual(host.Surface).TransformPoint(_pointer));
        }
    }

    private double EdgeSpeed()
    {
        var viewport = host.Viewport;
        return DragRules.EdgeScrollSpeed(_pointer.X, _pointer.Y, viewport.ActualWidth, viewport.ActualHeight);
    }

    // A point relative to the surface, relative to the viewport.
    private Point InViewport(Point at) =>
        ReferenceEquals(host.Viewport, host.Surface) ? at : host.Surface.TransformToVisual(host.Viewport).TransformPoint(at);

    // Runs while the pointer is in an edge band; stops outside it and when the drag ends. The clock's timer may fire
    // off the UI thread: each fire adds its time to the pending step, and one step at a time runs on the UI thread.
    private void UpdateEdgeTimer()
    {
        if (EdgeSpeed() == 0)
        {
            StopEdgeTimer();
            return;
        }

        if (_edgeTimer is null)
        {
            Interlocked.Exchange(ref _lastEdgeFire, Clock.GetTimestamp());
            Interlocked.Exchange(ref _pendingEdgeTime, 0);
            var ui = host.Surface.DispatcherQueue;
            _edgeTimer = owner.CreateViewTimer(Clock, () => OnEdgeTimer(ui), EdgeScrollInterval, EdgeScrollInterval);
        }
    }

    private void StopEdgeTimer()
    {
        _edgeTimer?.Dispose();
        _edgeTimer = null;
    }

    private void OnEdgeTimer(Microsoft.UI.Dispatching.DispatcherQueue ui)
    {
        var now = Clock.GetTimestamp();
        Interlocked.Add(ref _pendingEdgeTime, now - Interlocked.Exchange(ref _lastEdgeFire, now));
        if (Interlocked.Exchange(ref _edgeStepQueued, 1) == 0 && !ui.TryEnqueue(EdgeStep))
        {
            Interlocked.Exchange(ref _edgeStepQueued, 0);
        }
    }

    // Scrolls the list as far as the pointer's edge speed goes in the time since the last step.
    private void EdgeStep()
    {
        Interlocked.Exchange(ref _edgeStepQueued, 0);
        var elapsed = TimeSpan.FromSeconds(
            (double)Interlocked.Exchange(ref _pendingEdgeTime, 0) / Clock.TimestampFrequency);
        if (_edgeTimer is null || !IsDragging || host.Surface.XamlRoot is null)
        {
            return;
        }

        var speed = EdgeSpeed();
        if (speed != 0)
        {
            host.ScrollBy(speed * (elapsed < MaxEdgeStep ? elapsed : MaxEdgeStep).TotalSeconds);
        }
    }

    /// <summary>
    /// A release at <paramref name="at"/>. Returns false when no drag was on (the press stays a click). Otherwise
    /// ends the drag at once and returns true, with <paramref name="drop"/> the drop over the list, or a completed
    /// task when the release is outside it.
    /// </summary>
    public bool TryRelease(Point at, out Task drop)
    {
        var subject = _subject;
        var dragging = IsDragging;
        var held = IsHeld;
        End();
        if (!dragging || subject is null)
        {
            drop = Task.CompletedTask;
            if (held && subject is not null)
            {
                LiftedAfterHold?.Invoke(this, new HeldLift(subject.Item, at));
            }

            return false;
        }

        drop = IsOverList(at) ? host.DropAsync(subject.Item, at) : Task.CompletedTask;
        return true;
    }

    /// <summary>
    /// True when <paramref name="at"/> is over the part of the list that shows: inside the surface and inside the
    /// viewport. A release there drops; elsewhere it cancels.
    /// </summary>
    public bool IsOverList(Point at)
    {
        var surface = host.Surface;
        var viewport = host.Viewport;
        var inViewport = InViewport(at);
        return DragRules.ReleaseAt(at.X, at.Y, surface.ActualWidth, surface.ActualHeight) == DragRelease.Drop
            && DragRules.ReleaseAt(inViewport.X, inViewport.Y, viewport.ActualWidth, viewport.ActualHeight) == DragRelease.Drop;
    }

    /// <summary>Cancels a press or a drag: the picture and the line go at once, and nothing moves.</summary>
    public void Cancel() => End();

    private void End()
    {
        StopEdgeTimer();
        if (IsDragging)
        {
            host.HideDragLine();
        }

        IsDragging = false;
        IsHeld = false;
        _holdTimer?.Dispose();
        _holdTimer = null;
        var ended = _subject is not null;
        _subject = null;
        _image = null;
        if (Picture is { } picture)
        {
            picture.IsOpen = false;
            picture.Child = null;
            Picture = null;
        }

        if (ended)
        {
            Ended?.Invoke(this, EventArgs.Empty);
        }
    }

    // The hold runs on the clock and lands on the UI thread. Once held, the list's scrolling lets go of the
    // pointer, so the move that follows drags the row instead of panning the list.
    private void StartHold(int press, DragSubject subject)
    {
        var ui = SynchronizationContext.Current;
        _holdTimer = owner.CreateViewTimer(
            Clock,
            () =>
            {
                if (ui is null)
                {
                    Hold(press, subject);
                }
                else
                {
                    ui.Post(_ => Hold(press, subject), null);
                }
            },
            DragRules.HoldTime,
            Timeout.InfiniteTimeSpan);
    }

    private void Hold(int press, DragSubject subject)
    {
        if (press != _pressCount || !ReferenceEquals(_subject, subject))
        {
            return; // The press ended (released, cancelled or swiped) before the hold.
        }

        IsHeld = true;
        subject.Rows[0].CancelDirectManipulations();
    }

    private void ShowPicture(Point at)
    {
        if (_image is null)
        {
            return; // Still being taken; the press shows it once ready.
        }

        var inWindow = host.Surface.TransformToVisual(null).TransformPoint(at);
        Picture ??= new Popup
        {
            XamlRoot = host.Surface.XamlRoot,
            ShouldConstrainToRootBounds = false,
            IsHitTestVisible = false,
            Child = _image,
        };
        Picture.HorizontalOffset = inWindow.X - _pressOffset.X;
        Picture.VerticalOffset = inWindow.Y - _pressOffset.Y;
        Picture.IsOpen = true;
    }

    // The rows as they look in the list, over the solid backing, capped with a fade, at the picture's opacity and
    // sized in DIPs so it matches the rows at any display scale.
    private async Task<Image> PictureOfAsync(IReadOnlyList<FrameworkElement> rows)
    {
        var shown = rows.Take(DragRules.PictureRows(rows.Count)).ToList();
        var pixels = new List<RowPixels>(shown.Count);
        foreach (var row in shown)
        {
            var bitmap = new RenderTargetBitmap();
            await bitmap.RenderAsync(row);
            var buffer = await bitmap.GetPixelsAsync();
            pixels.Add(new RowPixels(buffer.ToArray(), bitmap.PixelWidth, bitmap.PixelHeight));
        }

        var backing = host.PictureBacking;
        var picture = DragPicture.Compose(
            pixels, [backing.B, backing.G, backing.R, 255], DragRules.PictureFades(rows.Count));
        var source = new WriteableBitmap(Math.Max(1, picture.PixelWidth), Math.Max(1, picture.PixelHeight));
        using (var stream = source.PixelBuffer.AsStream())
        {
            await stream.WriteAsync(picture.Pixels);
        }

        source.Invalidate();
        return new Image
        {
            Source = source,
            Stretch = Stretch.Fill,
            Width = shown.Max(r => r.ActualWidth),
            Height = shown.Sum(r => r.ActualHeight),
            Opacity = PictureOpacity,
            IsHitTestVisible = false,
        };
    }

    /// <summary>
    /// Feeds the drag from the pointer events of the host's <see cref="IReorderDragHost.Surface"/>, a list of
    /// <see cref="ListViewItem"/> rows. The mouse's left button drags, and touch and pen by press-and-hold; a press
    /// on a button inside a row (a checkbox, a chevron) does not. The surface captures the pointer once the drag
    /// starts, so the moves and the release keep coming outside it; losing the pointer, or a touch swipe that the
    /// scrolling takes over, cancels. The drag follows the pointer that pressed: another finger, a palm or the mouse
    /// neither moves, drops nor cancels it. While the drag is on, its press reaches nothing in the list either: the
    /// surface takes it from the row or button it pressed, so it ticks, clicks and opens nothing. Before the drag
    /// starts, another pointer's press replaces the press (nothing has captured the pointer yet, so a lost release
    /// must not leave a press behind that blocks every later one).
    /// </summary>
    public void ListenToPointer()
    {
        // The row containers handle the press and the release themselves, so the drag listens to handled ones too.
        var surface = host.Surface;
        surface.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnPointerPressed), true);
        surface.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(OnPointerMoved), true);
        surface.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(OnPointerReleased), true);
        surface.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(OnPointerLost), true);
        surface.AddHandler(UIElement.PointerCanceledEvent, new PointerEventHandler(OnPointerLost), true);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD100:Avoid async void methods", Justification = "XAML event handler; the signature requires void.")]
    private async void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (IsDragging && IsOtherPointer(e))
        {
            // The surface takes this pointer from the row or button it pressed, so its release clicks, ticks and
            // selects nothing there: the rows stay as they are under the drag.
            TakeFromRow(e);
            return;
        }

        _pointerId = e.Pointer.PointerId;
        var point = e.GetCurrentPoint(host.Surface);
        if (DragPointerOf(e.Pointer.PointerDeviceType) is not { } pointer
            || (pointer == DragPointer.Mouse ? !point.Properties.IsLeftButtonPressed : !point.IsInContact)
            || IsOnButton(e.OriginalSource as DependencyObject))
        {
            Cancel();
            return;
        }

        await PressAsync(point.Position, pointer);
    }

    // An element under the pointer (a checkbox, a chevron, the row) captured it at its press, and a capture never
    // moves to another element: each lets go of it, from the pressed element up to the surface, which then takes it.
    private void TakeFromRow(PointerRoutedEventArgs e)
    {
        var surface = host.Surface;
        for (var element = e.OriginalSource as DependencyObject;
             element is not null && !ReferenceEquals(element, surface);
             element = VisualTreeHelper.GetParent(element))
        {
            (element as UIElement)?.ReleasePointerCapture(e.Pointer);
        }

        surface.CapturePointer(e.Pointer);
    }

    private static DragPointer? DragPointerOf(Microsoft.UI.Input.PointerDeviceType device) => device switch
    {
        Microsoft.UI.Input.PointerDeviceType.Mouse => DragPointer.Mouse,
        Microsoft.UI.Input.PointerDeviceType.Touch => DragPointer.Touch,
        Microsoft.UI.Input.PointerDeviceType.Pen => DragPointer.Pen,
        _ => null,
    };

    private static bool IsOnButton(DependencyObject? element)
    {
        for (; element is not null and not ListViewItem; element = VisualTreeHelper.GetParent(element))
        {
            if (element is ButtonBase)
            {
                return true;
            }
        }

        return false;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (IsOtherPointer(e))
        {
            return;
        }

        var starting = !IsDragging;
        if (!Move(e.GetCurrentPoint(host.Surface).Position))
        {
            return;
        }

        if (starting)
        {
            host.Surface.CapturePointer(e.Pointer);
        }

        e.Handled = true;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD100:Avoid async void methods", Justification = "XAML event handler; the signature requires void.")]
    private async void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (IsOtherPointer(e) || !TryRelease(e.GetCurrentPoint(host.Surface).Position, out var drop))
        {
            return;
        }

        e.Handled = true;
        host.Surface.ReleasePointerCapture(e.Pointer);
        await drop;
    }

    private void OnPointerLost(object sender, PointerRoutedEventArgs e)
    {
        if (!IsOtherPointer(e))
        {
            Cancel();
        }
    }

    // True while a press or a drag is on and the event is another pointer's.
    private bool IsOtherPointer(PointerRoutedEventArgs e) => _subject is not null && e.Pointer.PointerId != _pointerId;

    // Windows' drag threshold (SM_CXDRAG, SM_CYDRAG) at the surface's display scale, in DIPs.
    private static partial class DragThreshold
    {
        private const int SmCxDrag = 68;
        private const int SmCyDrag = 69;

        public static (double X, double Y) Of(FrameworkElement surface)
        {
            var scale = surface.XamlRoot?.RasterizationScale is > 0 and var s ? s : 1.0;
            var dpi = (uint)Math.Round(96 * scale);
            return (Metric(SmCxDrag, dpi) / scale, Metric(SmCyDrag, dpi) / scale);
        }

        // 4 px, Windows' default, when the metric is unavailable.
        private static double Metric(int index, uint dpi) => GetSystemMetricsForDpi(index, dpi) is > 0 and var v ? v : 4;

        [LibraryImport("user32.dll")]
        private static partial int GetSystemMetricsForDpi(int index, uint dpi);
    }
}
