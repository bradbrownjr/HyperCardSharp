using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace HyperCardSharp.App.Controls;

/// <summary>
/// Pixel-authentic System 7 vertical scrollbar. Fixed 16px wide.
/// Renders its own borders (left separator, right edge, top/bottom).
/// Handles pointer input for arrows, track clicking, and thumb dragging.
/// When there is nothing to scroll, renders as an empty bordered rectangle
/// with no arrows or thumb (authentic System 7 "inactive scrollbar" look).
/// </summary>
public class System7ScrollBar : Control
{
    // ── Styled properties ──────────────────────────────────────────────────

    public static readonly StyledProperty<double> MinimumProperty =
        AvaloniaProperty.Register<System7ScrollBar, double>(nameof(Minimum), 0);

    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<System7ScrollBar, double>(nameof(Maximum), 0);

    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<System7ScrollBar, double>(nameof(Value), 0,
            coerce: CoerceValue);

    public static readonly StyledProperty<double> ViewportSizeProperty =
        AvaloniaProperty.Register<System7ScrollBar, double>(nameof(ViewportSize), 0);

    public static readonly StyledProperty<double> SmallChangeProperty =
        AvaloniaProperty.Register<System7ScrollBar, double>(nameof(SmallChange), 16);

    public static readonly StyledProperty<double> LargeChangeProperty =
        AvaloniaProperty.Register<System7ScrollBar, double>(nameof(LargeChange), 160);

    public double Minimum     { get => GetValue(MinimumProperty);     set => SetValue(MinimumProperty, value); }
    public double Maximum     { get => GetValue(MaximumProperty);     set => SetValue(MaximumProperty, value); }
    public double Value       { get => GetValue(ValueProperty);       set => SetValue(ValueProperty, value); }
    public double ViewportSize{ get => GetValue(ViewportSizeProperty);set => SetValue(ViewportSizeProperty, value); }
    public double SmallChange { get => GetValue(SmallChangeProperty); set => SetValue(SmallChangeProperty, value); }
    public double LargeChange { get => GetValue(LargeChangeProperty); set => SetValue(LargeChangeProperty, value); }

    /// <summary>Raised when <see cref="Value"/> changes.</summary>
    public event EventHandler<double>? ValueChanged;

    private static double CoerceValue(AvaloniaObject obj, double value)
    {
        var sb = (System7ScrollBar)obj;
        return Math.Clamp(value, sb.Minimum, Math.Max(sb.Minimum, sb.Maximum));
    }

    // ── Input state ────────────────────────────────────────────────────────

    private enum HitPart { None, UpArrow, DownArrow, TrackAbove, TrackBelow, Thumb }

    private HitPart _pressedPart;
    private double _thumbDragStartY;
    private double _thumbDragStartValue;
    private DispatcherTimer? _repeatTimer;

    // ── Brushes ────────────────────────────────────────────────────────────

    private static readonly SolidColorBrush Black = new(Colors.Black);
    private static readonly SolidColorBrush White = new(Colors.White);

    // ── Layout constants ───────────────────────────────────────────────────

    private const double BarWidth = 16;
    private const double ArrowHeight = 16;
    private const double MinThumbHeight = 16;

    static System7ScrollBar()
    {
        AffectsRender<System7ScrollBar>(
            MinimumProperty, MaximumProperty, ValueProperty, ViewportSizeProperty);
    }

    public System7ScrollBar()
    {
        Width = BarWidth;
    }

    // ── Geometry helpers ───────────────────────────────────────────────────

    private bool CanScroll => Maximum > Minimum;

    private Rect TopArrowRect => new(0, 0, BarWidth, ArrowHeight);

    private Rect BottomArrowRect => new(0, Bounds.Height - ArrowHeight, BarWidth, ArrowHeight);

    private (double thumbTop, double thumbHeight) ThumbGeometry()
    {
        if (!CanScroll) return (0, 0);

        double trackY = ArrowHeight;
        double trackH = Bounds.Height - ArrowHeight * 2;
        if (trackH <= 0) return (0, 0);

        double range = Maximum - Minimum;
        double total = range + ViewportSize;

        double thumbH = Math.Max(MinThumbHeight, trackH * (ViewportSize / total));
        thumbH = Math.Min(thumbH, trackH);

        double frac = range > 0 ? (Value - Minimum) / range : 0;
        double thumbY = trackY + frac * (trackH - thumbH);

        return (thumbY, thumbH);
    }

    private Rect ThumbRect()
    {
        var (y, h) = ThumbGeometry();
        return h > 0 ? new Rect(1, y, BarWidth - 2, h) : default;
    }

    // ── Rendering ──────────────────────────────────────────────────────────

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width;
        double h = Bounds.Height;

        // White background fill
        ctx.FillRectangle(White, new Rect(0, 0, w, h));

        if (CanScroll && h >= ArrowHeight * 2)
        {
            // ── Arrow button backgrounds (pressed = inverted) ──────────
            if (_pressedPart == HitPart.UpArrow)
                ctx.FillRectangle(Black, new Rect(1, 1, w - 2, ArrowHeight - 2));
            if (_pressedPart == HitPart.DownArrow)
                ctx.FillRectangle(Black, new Rect(1, h - ArrowHeight + 1, w - 2, ArrowHeight - 2));

            // ── Arrow triangles ────────────────────────────────────────
            DrawArrowTriangle(ctx, TopArrowRect, isUp: true,
                _pressedPart == HitPart.UpArrow);
            DrawArrowTriangle(ctx, BottomArrowRect, isUp: false,
                _pressedPart == HitPart.DownArrow);

            // ── Arrow–track separator lines (full-width, edge-to-edge) ──
            ctx.FillRectangle(Black, new Rect(0, ArrowHeight, w, 1));
            ctx.FillRectangle(Black, new Rect(0, h - ArrowHeight - 1, w, 1));

            // ── Thumb ──────────────────────────────────────────────────
            var thumb = ThumbRect();
            if (thumb.Width > 0 && thumb.Height > 0)
            {
                ctx.FillRectangle(White, thumb);
                // 1px black outline around the thumb
                ctx.FillRectangle(Black, new Rect(thumb.X, thumb.Y, thumb.Width, 1));                 // top
                ctx.FillRectangle(Black, new Rect(thumb.X, thumb.Bottom - 1, thumb.Width, 1));        // bottom
                ctx.FillRectangle(Black, new Rect(thumb.X, thumb.Y, 1, thumb.Height));                // left
                ctx.FillRectangle(Black, new Rect(thumb.Right - 1, thumb.Y, 1, thumb.Height));        // right
            }
        }

        // ── Border frame (always drawn last, on top) ──────────────────
        ctx.FillRectangle(Black, new Rect(0, 0, 1, h));       // left
        ctx.FillRectangle(Black, new Rect(w - 1, 0, 1, h));   // right
        ctx.FillRectangle(Black, new Rect(0, 0, w, 1));       // top
        ctx.FillRectangle(Black, new Rect(0, h - 1, w, 1));   // bottom
    }

    /// <summary>
    /// Draws a 4-row solid triangle (up or down) centred in the given button rect.
    /// Each row is a horizontal line of pixels: 1, 3, 5, 7 px wide.
    /// </summary>
    private static void DrawArrowTriangle(DrawingContext ctx, Rect rect, bool isUp, bool pressed)
    {
        var fg = pressed ? White : Black;
        double cx = Math.Floor(rect.X + rect.Width / 2);
        double cy = Math.Floor(rect.Y + rect.Height / 2);

        if (isUp)
        {
            FillRow(ctx, fg, cx, cy - 2, 1);
            FillRow(ctx, fg, cx, cy - 1, 3);
            FillRow(ctx, fg, cx, cy,     5);
            FillRow(ctx, fg, cx, cy + 1, 7);
        }
        else
        {
            FillRow(ctx, fg, cx, cy - 1, 7);
            FillRow(ctx, fg, cx, cy,     5);
            FillRow(ctx, fg, cx, cy + 1, 3);
            FillRow(ctx, fg, cx, cy + 2, 1);
        }
    }

    /// <summary>Draw a centred horizontal row of pixels at (cx, y) with the given width.</summary>
    private static void FillRow(DrawingContext ctx, IBrush brush, double cx, double y, int width)
    {
        double x = cx - Math.Floor(width / 2.0);
        ctx.FillRectangle(brush, new Rect(x, y, width, 1));
    }

    // ── Hit testing ────────────────────────────────────────────────────────

    private HitPart HitTest(Point pt)
    {
        if (!CanScroll) return HitPart.None;
        if (Bounds.Height < ArrowHeight * 2) return HitPart.None;

        if (TopArrowRect.Contains(pt))    return HitPart.UpArrow;
        if (BottomArrowRect.Contains(pt)) return HitPart.DownArrow;

        var thumb = ThumbRect();
        if (thumb.Width > 0 && thumb.Contains(pt)) return HitPart.Thumb;

        double trackTop = ArrowHeight;
        double trackBottom = Bounds.Height - ArrowHeight;
        if (pt.Y >= trackTop && pt.Y < trackBottom)
            return pt.Y < thumb.Y ? HitPart.TrackAbove : HitPart.TrackBelow;

        return HitPart.None;
    }

    // ── Pointer input ──────────────────────────────────────────────────────

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        e.Handled = true;
        e.Pointer.Capture(this);

        var pt = e.GetPosition(this);
        _pressedPart = HitTest(pt);

        switch (_pressedPart)
        {
            case HitPart.UpArrow:
                ScrollBy(-SmallChange);
                StartRepeat(() => ScrollBy(-SmallChange));
                break;
            case HitPart.DownArrow:
                ScrollBy(SmallChange);
                StartRepeat(() => ScrollBy(SmallChange));
                break;
            case HitPart.TrackAbove:
                ScrollBy(-LargeChange);
                StartRepeat(() => ScrollBy(-LargeChange));
                break;
            case HitPart.TrackBelow:
                ScrollBy(LargeChange);
                StartRepeat(() => ScrollBy(LargeChange));
                break;
            case HitPart.Thumb:
                _thumbDragStartY = pt.Y;
                _thumbDragStartValue = Value;
                break;
        }

        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (_pressedPart == HitPart.Thumb)
        {
            var pt = e.GetPosition(this);
            double trackH = Bounds.Height - ArrowHeight * 2;
            var (_, thumbH) = ThumbGeometry();
            double range = trackH - thumbH;

            if (range > 0)
            {
                double dy = pt.Y - _thumbDragStartY;
                double dv = dy / range * (Maximum - Minimum);
                Value = Math.Clamp(_thumbDragStartValue + dv, Minimum, Maximum);
            }

            e.Handled = true;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        StopRepeat();
        _pressedPart = HitPart.None;
        e.Pointer.Capture(null);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        StopRepeat();
        _pressedPart = HitPart.None;
        InvalidateVisual();
    }

    // ── Scrolling ──────────────────────────────────────────────────────────

    private void ScrollBy(double delta)
    {
        Value = Math.Clamp(Value + delta, Minimum, Maximum);
    }

    // ── Auto-repeat for held arrows / track ────────────────────────────────

    private void StartRepeat(Action action)
    {
        StopRepeat();
        _repeatTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _repeatTimer.Tick += (_, _) =>
        {
            // After initial delay, switch to fast repeat
            _repeatTimer!.Interval = TimeSpan.FromMilliseconds(50);
            action();
        };
        _repeatTimer.Start();
    }

    private void StopRepeat()
    {
        _repeatTimer?.Stop();
        _repeatTimer = null;
    }

    // ── Property changed ───────────────────────────────────────────────────

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ValueProperty)
        {
            ValueChanged?.Invoke(this, (double)change.NewValue!);
        }
    }
}
