using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using TaskDesk.Core.Shell;
using Windows.Foundation;

namespace TaskDesk.Views;

/// <summary>
/// What a press would drag: the item, and the rows its picture shows, top to bottom (the pressed row first; for an
/// open Task, its Subtask rows after it).
/// </summary>
public sealed record DragSubject(object Item, IReadOnlyList<FrameworkElement> Rows);

/// <summary>
/// The list side of a <see cref="ReorderDrag"/>. Every point is relative to <see cref="Surface"/>, in DIPs.
/// </summary>
public interface IReorderDragHost
{
    /// <summary>The list the drag runs over; a release outside it cancels.</summary>
    FrameworkElement Surface { get; }

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
/// While the pointer is near the list's top or bottom edge, a timer on <paramref name="clock"/> scrolls the list
/// (<see cref="DragRules.EdgeScrollSpeed"/>); the host calls <see cref="ListScrolled"/> when the list scrolls, so
/// the line keeps to the gap under the pointer.
/// </summary>
public sealed partial class ReorderDrag(IReorderDragHost host, TimeProvider clock)
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
    private Point _pointer;
    private ITimer? _edgeTimer;
    private long _lastEdgeFire;
    private long _pendingEdgeTime;
    private int _edgeStepQueued;

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

    /// <summary>
    /// A press at <paramref name="at"/>. Where something drags, takes its picture (completing when it is ready);
    /// the drag itself starts only on a move past the threshold.
    /// </summary>
    public async Task PressAsync(Point at)
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
            if (!DragRules.HasPassedThreshold(at.X - _press.X, at.Y - _press.Y, thresholdX, thresholdY))
            {
                return false;
            }

            IsDragging = true;
            LastPressDragged = true;
        }

        _pointer = at;
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
            host.ShowDragAt(_pointer);
        }
    }

    private double EdgeSpeed()
    {
        var surface = host.Surface;
        return DragRules.EdgeScrollSpeed(_pointer.X, _pointer.Y, surface.ActualWidth, surface.ActualHeight);
    }

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
            Interlocked.Exchange(ref _lastEdgeFire, clock.GetTimestamp());
            Interlocked.Exchange(ref _pendingEdgeTime, 0);
            var ui = host.Surface.DispatcherQueue;
            _edgeTimer = clock.CreateTimer(_ => OnEdgeTimer(ui), null, EdgeScrollInterval, EdgeScrollInterval);
        }
    }

    private void StopEdgeTimer()
    {
        _edgeTimer?.Dispose();
        _edgeTimer = null;
    }

    private void OnEdgeTimer(Microsoft.UI.Dispatching.DispatcherQueue ui)
    {
        var now = clock.GetTimestamp();
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
            (double)Interlocked.Exchange(ref _pendingEdgeTime, 0) / clock.TimestampFrequency);
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
        End();
        if (!dragging || subject is null)
        {
            drop = Task.CompletedTask;
            return false;
        }

        var surface = host.Surface;
        drop = DragRules.ReleaseAt(at.X, at.Y, surface.ActualWidth, surface.ActualHeight) == DragRelease.Drop
            ? host.DropAsync(subject.Item, at)
            : Task.CompletedTask;
        return true;
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
        _subject = null;
        _image = null;
        if (Picture is { } picture)
        {
            picture.IsOpen = false;
            picture.Child = null;
            Picture = null;
        }
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
