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
/// </summary>
public class System7TitleBar : Control
{
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<System7TitleBar, string>(nameof(Title), string.Empty);

    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>Raised when the user clicks the close box.</summary>
    public event EventHandler? CloseRequested;

    private static readonly SolidColorBrush BgWhite = new(Colors.White);
    private static readonly SolidColorBrush BgBlack = new(Colors.Black);
    private static readonly IPen BlackPen = new Pen(new SolidColorBrush(Colors.Black), 1);

    // Updated during Render; used for close-box hit testing.
    private Rect _closeBoxRect;

    static System7TitleBar()
    {
        AffectsRender<System7TitleBar>(TitleProperty);
    }

    public System7TitleBar()
    {
        Height = 20;
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width;
        double h = Bounds.Height;

        // ── System 7 title bar: 6 thick black stripes with white gaps ──
        // Reference: System 7 Finder windows show ~6 black bars (2px each)
        // separated by 1px white gaps, filling the bar height.
        ctx.DrawRectangle(BgWhite, null, new Rect(0, 0, w, h));

        // 6 stripes × 2px + 5 gaps × 1px = 17px of pattern.
        // Centre the pattern vertically in the bar.
        const int stripeCount = 6;
        const double stripeH = 2;
        const double gapH = 1;
        double patternH = stripeCount * stripeH + (stripeCount - 1) * gapH;
        double startY = Math.Floor((h - patternH) / 2);

        for (int i = 0; i < stripeCount; i++)
        {
            double y = startY + i * (stripeH + gapH);
            ctx.FillRectangle(BgBlack, new Rect(0, y, w, stripeH));
        }

        // ── Close box ─────────────────────────────────────────────────
        // Simple empty 11×11 square — no inner box (authentic System 7).
        const double boxSize = 11;
        double boxTop = Math.Floor((h - boxSize) / 2);
        _closeBoxRect = new Rect(8, boxTop, boxSize, boxSize);

        ctx.DrawRectangle(BgWhite, BlackPen, _closeBoxRect);

        // ── Title text ────────────────────────────────────────────────
        // Clear stripes behind title, then draw bold text centred.
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

        // White band clears the stripes so text is legible.
        ctx.DrawRectangle(BgWhite, null, new Rect(tx - 8, 0, ft.Width + 16, h));
        ctx.DrawText(ft, new Point(tx, ty));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        // Only start dragging when the click is outside the close box.
        if (!_closeBoxRect.Contains(e.GetPosition(this)) && VisualRoot is Window w)
            w.BeginMoveDrag(e);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_closeBoxRect.Contains(e.GetPosition(this)))
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
    }
}
