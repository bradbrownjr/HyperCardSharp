using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace HyperCardSharp.App.Controls;

/// <summary>
/// Draws an authentic System 7-style title bar: alternating 1px black/white
/// horizontal stripes, a close box on the left, and the window title centred
/// in a cleared zone. Handles window dragging and close-box clicks.
/// In ColorMode the bar uses the System 7 Platinum (gray) palette.
/// </summary>
public class System7TitleBar : Control
{
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<System7TitleBar, string>(nameof(Title), string.Empty);

    /// <summary>
    /// When true the title bar renders in the System 7 Platinum (color) palette
    /// — gray stripes on medium gray — instead of black stripes on white.
    /// </summary>
    public static readonly StyledProperty<bool> ColorModeProperty =
        AvaloniaProperty.Register<System7TitleBar, bool>(nameof(ColorMode), false);

    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public bool ColorMode
    {
        get => GetValue(ColorModeProperty);
        set => SetValue(ColorModeProperty, value);
    }

    /// <summary>Raised when the user clicks the close box.</summary>
    public event EventHandler? CloseRequested;

    // B&W palette
    private static readonly SolidColorBrush BgWhite  = new(Colors.White);
    private static readonly SolidColorBrush BgBlack  = new(Colors.Black);

    // System 7 color palette — the title bar stripes used the Window Color from
    // the Color control panel. The default was a medium-bright blue matching
    // the Mac 256-color system palette (clut 8) entry at RGB(0, 0, 204).
    private static readonly SolidColorBrush Sys7Blue = new(Color.FromRgb(0, 0, 204));

    private static readonly IPen BlackPen = new Pen(new SolidColorBrush(Colors.Black), 1);

    // Updated during Render; used for close-box hit testing.
    private Rect _closeBoxRect;

    // True while the pointer is held inside the close box — shows × inside the box.
    private bool _closeBoxHeld;

    static System7TitleBar()
    {
        AffectsRender<System7TitleBar>(TitleProperty, ColorModeProperty);
    }

    public System7TitleBar()
    {
        Height = 20;
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width;
        double h = Bounds.Height;

        bool color = ColorMode;
        var bg     = BgWhite;               // System 7: background is always white
        var stripe = color ? Sys7Blue : BgBlack;  // color → blue stripes; B&W → black

        // ── Background ────────────────────────────────────────────────────────
        ctx.DrawRectangle(bg, null, new Rect(0, 0, w, h));

        // ── Top and bottom border lines ───────────────────────────────────────
        // The real System 7 title bar has a solid 1px black line at the very top
        // and very bottom edge, framing the stripe pattern.
        ctx.FillRectangle(BgBlack, new Rect(0, 0, w, 1));       // top border
        ctx.FillRectangle(BgBlack, new Rect(0, h - 1, w, 1));   // bottom border

        // ── Horizontal stripes ────────────────────────────────────────────────
        // Fill the interior (between top and bottom border) with alternating
        // 1px stripe / 1px gap.  Interior runs from y=1 to y=h-2 inclusive.
        // Start first stripe at the top of the interior; stop when we'd
        // overlap the bottom border.
        const double stripeH = 1;
        const double gapH    = 1;
        const double insetX  = 1;
        double interiorTop    = 1;
        double interiorBottom = h - 1;

        for (double y = interiorTop; y + stripeH <= interiorBottom; y += stripeH + gapH)
        {
            ctx.FillRectangle(stripe, new Rect(insetX, y, w - insetX * 2, stripeH));
        }

        // ── Close box ─────────────────────────────────────────────────────────
        const double boxSize  = 11;
        const double clearing = 3;
        double boxTop = Math.Floor((h - boxSize) / 2);
        _closeBoxRect = new Rect(8, boxTop, boxSize, boxSize);

        // Erase stripes behind the close box.
        ctx.FillRectangle(bg, new Rect(
            _closeBoxRect.X - clearing,
            _closeBoxRect.Y - clearing,
            _closeBoxRect.Width  + clearing * 2,
            _closeBoxRect.Height + clearing * 2));

        ctx.DrawRectangle(bg, BlackPen, _closeBoxRect);

        // Draw × inside the box while the pointer is held down — classic Mac feel.
        if (_closeBoxHeld)
        {
            double x1 = _closeBoxRect.X + 2.5;
            double y1 = _closeBoxRect.Y + 2.5;
            double x2 = _closeBoxRect.Right  - 2.5;
            double y2 = _closeBoxRect.Bottom - 2.5;
            ctx.DrawLine(BlackPen, new Point(x1, y1), new Point(x2, y2));
            ctx.DrawLine(BlackPen, new Point(x2, y1), new Point(x1, y2));
        }

        // ── Title text ────────────────────────────────────────────────────────
        var typeface = new Typeface(
            "Geneva, Helvetica, Arial, sans-serif",
            FontStyle.Normal,
            FontWeight.Bold);

        var ft = new FormattedText(
            Title,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            12,
            BgBlack);

        double tx = Math.Floor((w - ft.Width) / 2);
        double ty = Math.Floor((h - ft.Height) / 2);

        // Clear stripes behind the title text.
        ctx.DrawRectangle(bg, null, new Rect(tx - 8, 0, ft.Width + 16, h));
        ctx.DrawText(ft, new Point(tx, ty));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_closeBoxRect.Contains(e.GetPosition(this)))
        {
            _closeBoxHeld = true;
            InvalidateVisual();
        }
        else if (VisualRoot is Window w)
        {
            try { w.BeginMoveDrag(e); }
            catch { /* BeginMoveDrag can fail on some platforms */ }
        }
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        bool wasHeld = _closeBoxHeld;
        _closeBoxHeld = false;
        InvalidateVisual();
        if (wasHeld && _closeBoxRect.Contains(e.GetPosition(this)))
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (_closeBoxHeld) { _closeBoxHeld = false; InvalidateVisual(); }
    }
}
