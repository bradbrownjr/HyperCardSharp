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

        // ── System 7 title bar: thin stripes with white space at edges ──
        // Reference: system.css uses background-clip: content-box with padding
        // to create white border around stripe area. We replicate this by
        // insetting stripes 2px from left/right and centering vertically.
        ctx.DrawRectangle(BgWhite, null, new Rect(0, 0, w, h));

        // 6 stripes × 1px + 5 gaps × 1px = 11px of pattern.
        // Centre the pattern vertically in the bar — leaving white space at
        // top and bottom, matching authentic System 7 Finder title bars.
        const int stripeCount = 6;
        const double stripeH = 1;
        const double gapH = 1;
        double patternH = stripeCount * stripeH + (stripeCount - 1) * gapH;
        double startY = Math.Floor((h - patternH) / 2);

        // Horizontal inset — stripes don't touch left/right window edges.
        const double insetX = 1;

        for (int i = 0; i < stripeCount; i++)
        {
            double y = startY + i * (stripeH + gapH);
            ctx.FillRectangle(BgBlack, new Rect(insetX, y, w - insetX * 2, stripeH));
        }

        // ── Close box ─────────────────────────────────────────────────
        // Simple empty square with white clearing around it (System 7 style).
        const double boxSize = 11;
        const double clearing = 3;  // white space around close box
        double boxTop = Math.Floor((h - boxSize) / 2);
        _closeBoxRect = new Rect(8, boxTop, boxSize, boxSize);

        // White clearing rect — larger than the box to erase stripes around it.
        ctx.FillRectangle(BgWhite, new Rect(
            _closeBoxRect.X - clearing,
            _closeBoxRect.Y - clearing,
            _closeBoxRect.Width + clearing * 2,
            _closeBoxRect.Height + clearing * 2));

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
