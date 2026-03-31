using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace HyperCardSharp.App.Controls;

/// <summary>
/// Pixel-authentic System 7 grow box (resize handle).
/// Renders the classic overlapping-squares icon at 15×15 and handles
/// drag-to-resize on its parent window.
/// </summary>
public class System7GrowBox : Control
{
    private static readonly SolidColorBrush Black = new(Colors.Black);
    private static readonly SolidColorBrush White = new(Colors.White);

    public System7GrowBox()
    {
        Width = 16;
        Height = 16;
        Cursor = new Cursor(StandardCursorType.BottomRightCorner);
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width;
        double h = Bounds.Height;

        // White background
        ctx.FillRectangle(White, new Rect(0, 0, w, h));

        // 1px black border on all sides
        ctx.FillRectangle(Black, new Rect(0, 0, w, 1));       // top
        ctx.FillRectangle(Black, new Rect(0, h - 1, w, 1));   // bottom
        ctx.FillRectangle(Black, new Rect(0, 0, 1, h));       // left
        ctx.FillRectangle(Black, new Rect(w - 1, 0, 1, h));   // right

        // The grow box icon: two overlapping outlined rectangles.
        // Larger box at bottom-right, smaller at top-left, creating
        // the classic Mac "resize handle" look.

        // Large rectangle (bottom-right, ~10×10 inset from edges)
        double lx = 5, ly = 5, lw = 9, lh = 9;
        ctx.FillRectangle(White, new Rect(lx, ly, lw, lh));
        ctx.FillRectangle(Black, new Rect(lx, ly, lw, 1));
        ctx.FillRectangle(Black, new Rect(lx, ly + lh - 1, lw, 1));
        ctx.FillRectangle(Black, new Rect(lx, ly, 1, lh));
        ctx.FillRectangle(Black, new Rect(lx + lw - 1, ly, 1, lh));

        // Small rectangle (top-left, ~6×6)
        double sx = 2, sy = 2, sw = 6, sh = 6;
        ctx.FillRectangle(White, new Rect(sx, sy, sw, sh));
        ctx.FillRectangle(Black, new Rect(sx, sy, sw, 1));
        ctx.FillRectangle(Black, new Rect(sx, sy + sh - 1, sw, 1));
        ctx.FillRectangle(Black, new Rect(sx, sy, 1, sh));
        ctx.FillRectangle(Black, new Rect(sx + sw - 1, sy, 1, sh));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (VisualRoot is Window w)
        {
            try { w.BeginResizeDrag(WindowEdge.SouthEast, e); }
            catch { /* Can fail on some platforms */ }
        }
        e.Handled = true;
    }
}
