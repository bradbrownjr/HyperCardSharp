using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using HyperCardSharp.Rendering;
using SkiaSharp;

namespace HyperCardSharp.App.Controls;

/// <summary>
/// Avalonia control that renders an SKBitmap with nearest-neighbor scaling.
/// When no bitmap is loaded, displays a classic Mac startup screen:
/// 1px checkerboard background with a hardcoded pixel-art floppy disk icon.
/// Only the "?" blinks — the disk body is always visible.
/// </summary>
public class SkiaBitmapControl : Control
{
    public static readonly StyledProperty<SKBitmap?> SourceProperty =
        AvaloniaProperty.Register<SkiaBitmapControl, SKBitmap?>(nameof(Source));

    public SKBitmap? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    private WriteableBitmap? _writeableBitmap;
    private WriteableBitmap? _placeholderBitmap;
    private int _placeholderW, _placeholderH;
    private bool _questionMarkVisible = true;
    private DispatcherTimer? _blinkTimer;

    // ── Transition state ──────────────────────────────────────────────────────
    private SKBitmap?        _transitionFrom;         // old card (owned by us — must dispose)
    private SKBitmap?        _transitionTo;           // new card (alias for Source — do not dispose)
    private SKBitmap?        _transitionFrameBitmap;  // reusable composite buffer
    private string?          _transitionEffect;
    private string?          _transitionSpeed;
    private float            _transitionT;            // 0 → 1
    private float            _transitionStep;         // added to _transitionT each tick
    private DispatcherTimer? _transitionTimer;

    // Cached render transform — updated each Render() call, used for pointer hit-testing.
    private double _renderOffsetX, _renderOffsetY, _renderScale = 1.0;
    private double _renderSourceW, _renderSourceH;

    /// <summary>Fired (card X, card Y) on pointer press over a loaded card bitmap.</summary>
    public event Action<float, float>? CardPointerPressed;

    /// <summary>Fired (card X, card Y) on pointer release over a loaded card bitmap.</summary>
    public event Action<float, float>? CardPointerReleased;

    /// <summary>Fired (card X, card Y) on pointer move over a loaded card bitmap.</summary>
    public event Action<float, float>? CardPointerMoved;

    /// <summary>Fired when pointer exits the card bitmap area.</summary>
    public event Action? CardPointerExited;

    // -------------------------------------------------------------------------
    // Hardcoded 24×28 pixel-art floppy disk.  B = black, W = white.
    // Each string is exactly 28 characters — visually verifiable by inspection.
    // -------------------------------------------------------------------------
    // 3.5" floppy disk icon extracted from reference samples/floppy-question-mark-300x295.png.
    // 32x32 pixel art, scale=3 => 96x96 screen pixels.
    // Layout: rows 0-9=metal shutter (top), row 10=separator, rows 11-13=gap,
    //         rows 14-30=label area with bordered inner rectangle, row 31=bottom border.
    // Hub window (5x7) at cols 16-20, rows 2-8 (upper-right of shutter center).
    // 'T' = transparent (leave checkerboard background — chamfered corner).
    // Each string is exactly 32 characters.
    private const int DiskW = 32;
    private const int DiskH = 32;

    private static readonly string[] DiskBody =
    [
        "BBBBBBBBBBBBBBBBBBBBBBBBBBBBTTTT",  // row  0  top border, chamfer cutout (4T)
        "BWWWWWWBWWWWWWWWWWWWWWWBWWWWBTTT",  // row  1  chamfer step: col28=B border, 29-31=T
        "BWWWWWWBWWWWWWWWWBBBWWWBWWWWWBTT",  // row  2  chamfer step: col29=B border, 30-31=T
        "BWWWWWWBWWWWWWWWBWWWBWWBWWWWWWBT",  // row  3  chamfer step: col30=B border, 31=T
        "BWWWWWWBWWWWWWWWBWWWBWWBWWWWWWWB",  // row  4  hub
        "BWWWWWWBWWWWWWWWBWWWBWWBWWWWWWWB",  // row  5  hub
        "BWWWWWWBWWWWWWWWBWWWBWWBWWWWWWWB",  // row  6  hub
        "BWWWWWWBWWWWWWWWBWWWBWWBWWWWWWWB",  // row  7  hub
        "BWWWWWWBWWWWWWWWWBBBWWWBWWWWWWWB",  // row  8  hub bottom (cols 17-19)
        "BWWWWWWBWWWWWWWWWWWWWWWBWWWWWWWB",  // row  9  shutter bottom
        "BWWWWWWWBBBBBBBBBBBBBBBWWWWWWWWB",  // row 10  separator (cols 8-22)
        "BWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWB",  // row 11  white
        "BWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWB",  // row 12  white
        "BWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWB",  // row 13  white
        "BWWWWBBBBBBBBBBBBBBBBBBBBBBWWWWB",  // row 14  label top (cols 5-26 = 22B)
        "BWWWBWWWWWWWWWWWWWWWWWWWWWWBWWWB",  // row 15  label sides (cols 4,27)
        "BWWWBWWWWWWWWWWWWWWWWWWWWWWBWWWB",  // row 16  label
        "BWWWBWWWWWWWWWWWWWWWWWWWWWWBWWWB",  // row 17  label (? row 0)
        "BWWWBWWWWWWWWWWWWWWWWWWWWWWBWWWB",  // row 18  label (? row 1)
        "BWWWBWWWWWWWWWWWWWWWWWWWWWWBWWWB",  // row 19  label (? row 2)
        "BWWWBWWWWWWWWWWWWWWWWWWWWWWBWWWB",  // row 20  label (? row 3)
        "BWWWBWWWWWWWWWWWWWWWWWWWWWWBWWWB",  // row 21  label
        "BWWWBWWWWWWWWWWWWWWWWWWWWWWBWWWB",  // row 22  label
        "BWWWBWWWWWWWWWWWWWWWWWWWWWWBWWWB",  // row 23  label
        "BWWWBWWWWWWWWWWWWWWWWWWWWWWBWWWB",  // row 24  label (? stem)
        "BWWWBWWWWWWWWWWWWWWWWWWWWWWBWWWB",  // row 25  label (? stem)
        "BWWWBWWWWWWWWWWWWWWWWWWWWWWBWWWB",  // row 26  label (gap)
        "BWWWBWWWWWWWWWWWWWWWWWWWWWWBWWWB",  // row 27  label (? dot)
        "BWWWBWWWWWWWWWWWWWWWWWWWWWWBWWWB",  // row 28  label (? dot)
        "BWWWBWWWWWWWWWWWWWWWWWWWWWWBWWWB",  // row 29  label
        "BWWWBWWWWWWWWWWWWWWWWWWWWWWBWWWB",  // row 30  label bottom
        "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB",  // row 31  bottom border
    ];

    // "?" pixel positions (col, row) in disk coordinates.
    // Extracted from reference image. Large chunky "?" centered in label area.
    //   ..######..    row 17: cols 13-18
    //   .########.    row 18: cols 12-19
    //   .##....##.    row 19: cols 12-13, 18-19
    //   .##....##.    row 20: cols 12-13, 18-19
    //   ......###.    row 21: cols 17-19
    //   .....###..    row 22: cols 16-18
    //   ....###...    row 23: cols 15-17
    //   ....##....    row 24: cols 15-16
    //   ....##....    row 25: cols 15-16
    //   ..........    row 26: (gap)
    //   ....##....    row 27: cols 15-16 (dot)
    //   ....##....    row 28: cols 15-16 (dot)
    private static readonly (int Col, int Row)[] QuestionMarkDots =
    [
        (13,17),(14,17),(15,17),(16,17),(17,17),(18,17),
        (12,18),(13,18),(14,18),(15,18),(16,18),(17,18),(18,18),(19,18),
        (12,19),(13,19),                                (18,19),(19,19),
        (12,20),(13,20),                                (18,20),(19,20),
                                        (17,21),(18,21),(19,21),
                                (16,22),(17,22),(18,22),
                        (15,23),(16,23),(17,23),
                        (15,24),(16,24),
                        (15,25),(16,25),

                        (15,27),(16,27),
                        (15,28),(16,28),
    ];

    static SkiaBitmapControl()
    {
        AffectsRender<SkiaBitmapControl>(SourceProperty);
        AffectsMeasure<SkiaBitmapControl>(SourceProperty);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SourceProperty)
        {
            UpdateBitmap();
            UpdateBlinkTimer();
            InvalidateVisual();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        UpdateBlinkTimer();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _blinkTimer?.Stop();
        _blinkTimer = null;
        _transitionTimer?.Stop();
        _transitionTimer = null;
        _transitionFrom?.Dispose();
        _transitionFrom = null;
        _transitionFrameBitmap?.Dispose();
        _transitionFrameBitmap = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void UpdateBlinkTimer()
    {
        if (Source == null && _blinkTimer == null)
        {
            _blinkTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(530) };
            _blinkTimer.Tick += (_, _) =>
            {
                _questionMarkVisible = !_questionMarkVisible;
                _placeholderBitmap = null;
                InvalidateVisual();
            };
            _blinkTimer.Start();
        }
        else if (Source != null && _blinkTimer != null)
        {
            _blinkTimer.Stop();
            _blinkTimer = null;
            _placeholderBitmap = null;
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // Always fill available space — Render() handles fit-scaling internally
        if (Source == null)
            return new Size(640, 400);

        return availableSize;
    }

    private void UpdateBitmap(SKBitmap? overrideSource = null)
    {
        var source = overrideSource ?? Source;
        if (source == null || source.Width <= 0 || source.Height <= 0)
        {
            _writeableBitmap = null;
            return;
        }

        var size = new PixelSize(source.Width, source.Height);
        _writeableBitmap = new WriteableBitmap(size, new Vector(96, 96),
            Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Opaque);

        using var framebuffer = _writeableBitmap.Lock();
        var srcPixels = source.GetPixelSpan();
        unsafe
        {
            var dst = new Span<byte>((void*)framebuffer.Address,
                framebuffer.RowBytes * source.Height);
            for (int y = 0; y < source.Height; y++)
            {
                var srcRow = srcPixels.Slice(y * source.RowBytes, source.Width * 4);
                var dstRow = dst.Slice(y * framebuffer.RowBytes, source.Width * 4);
                srcRow.CopyTo(dstRow);
            }
        }
    }

    /// <summary>
    /// Build the placeholder at exactly the given pixel dimensions.
    /// Draws a 1px checkerboard, then stamps the hardcoded pixel-art floppy disk
    /// centered on screen at 2× scale. The "?" overlay is rendered separately
    /// and blinks independently via _questionMarkVisible.
    /// </summary>
    private void RebuildPlaceholder(int w, int h)
    {
        if (w <= 0 || h <= 0) return;

        var pixels = new uint[w * h];

        // 1px checkerboard background
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                pixels[y * w + x] = ((x + y) % 2 == 0) ? 0xFFFFFFFF : 0xFF000000;

        // Scale floppy at fixed 2× — 64px tall in any window size.
        // The real Macintosh startup floppy is tiny (~7% of 342px height).
        const int scale = 2;
        int renderW = DiskW * scale;
        int renderH = DiskH * scale;
        int ox = (w - renderW) / 2;
        int oy = (h - renderH) / 2;

        void Stamp(int col, int row, uint color)
        {
            for (int sy = 0; sy < scale; sy++)
            for (int sx = 0; sx < scale; sx++)
            {
                int dx = ox + col * scale + sx;
                int dy = oy + row * scale + sy;
                if ((uint)dx < (uint)w && (uint)dy < (uint)h)
                    pixels[dy * w + dx] = color;
            }
        }

        // Disk body ('T' = transparent — leave checkerboard background)
        for (int row = 0; row < DiskH; row++)
        {
            var rowStr = DiskBody[row];
            for (int col = 0; col < DiskW; col++)
            {
                char c = rowStr[col];
                if (c == 'T') continue;
                Stamp(col, row, c == 'B' ? 0xFF000000u : 0xFFFFFFFFu);
            }
        }

        // "?" overlay — only when blinking on
        if (_questionMarkVisible)
        {
            foreach (var (qCol, qRow) in QuestionMarkDots)
                Stamp(qCol, qRow, 0xFF000000u);
        }

        var pixelSize = new PixelSize(w, h);
        _placeholderBitmap = new WriteableBitmap(pixelSize, new Vector(96, 96),
            Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Opaque);
        _placeholderW = w;
        _placeholderH = h;

        using var fb = _placeholderBitmap.Lock();
        unsafe
        {
            fixed (uint* src = pixels)
            {
                var srcSpan = new ReadOnlySpan<byte>(src, w * h * 4);
                var dst = new Span<byte>((void*)fb.Address, fb.RowBytes * h);
                for (int y = 0; y < h; y++)
                {
                    var srcRow = srcSpan.Slice(y * w * 4, w * 4);
                    var dstRow = dst.Slice(y * fb.RowBytes, w * 4);
                    srcRow.CopyTo(dstRow);
                }
            }
        }
    }

    public override void Render(DrawingContext context)
    {
        // During a transition, _writeableBitmap already holds the composited frame
        // (updated by the timer tick), so normal rendering code is used unchanged.
        var bitmapToRender = _writeableBitmap;

        if (bitmapToRender == null)
        {
            int pw = (int)Bounds.Width;
            int ph = (int)Bounds.Height;
            if (pw <= 0 || ph <= 0) return;

            if (_placeholderBitmap == null || _placeholderW != pw || _placeholderH != ph)
                RebuildPlaceholder(pw, ph);

            if (_placeholderBitmap == null) return;

            RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.None);
            context.DrawImage(_placeholderBitmap,
                new Rect(0, 0, pw, ph),
                new Rect(0, 0, pw, ph));
            return;
        }

        var sourceSize = new Size(bitmapToRender.PixelSize.Width, bitmapToRender.PixelSize.Height);
        var targetSize = Bounds.Size;

        // Always fit the card to the available space (scale up or down)
        double scale = Math.Min(targetSize.Width  / sourceSize.Width,
                                targetSize.Height / sourceSize.Height);
        if (scale <= 0) scale = 1;
        double scaledWidth  = sourceSize.Width  * scale;
        double scaledHeight = sourceSize.Height * scale;

        double offsetX = (targetSize.Width  - scaledWidth)  / 2;
        double offsetY = (targetSize.Height - scaledHeight) / 2;

        // Cache for pointer coordinate transform
        _renderOffsetX = offsetX;
        _renderOffsetY = offsetY;
        _renderScale   = scale;
        _renderSourceW = sourceSize.Width;
        _renderSourceH = sourceSize.Height;

        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.None);
        context.DrawImage(bitmapToRender,
            new Rect(0, 0, sourceSize.Width, sourceSize.Height),
            new Rect(offsetX, offsetY, scaledWidth, scaledHeight));
    }

    protected override void OnPointerPressed(Avalonia.Input.PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_writeableBitmap == null || _renderScale <= 0) return;
        var pt = e.GetPosition(this);
        double cardX = (pt.X - _renderOffsetX) / _renderScale;
        double cardY = (pt.Y - _renderOffsetY) / _renderScale;
        if (cardX < 0 || cardY < 0 || cardX >= _renderSourceW || cardY >= _renderSourceH) return;
        CardPointerPressed?.Invoke((float)cardX, (float)cardY);
    }

    protected override void OnPointerReleased(Avalonia.Input.PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        // Suppress clicks while a transition is playing.
        if (_transitionTimer != null) return;
        if (_writeableBitmap == null || _renderScale <= 0) return;

        var pt = e.GetPosition(this);
        double cardX = (pt.X - _renderOffsetX) / _renderScale;
        double cardY = (pt.Y - _renderOffsetY) / _renderScale;

        if (cardX < 0 || cardY < 0 || cardX >= _renderSourceW || cardY >= _renderSourceH) return;

        CardPointerReleased?.Invoke((float)cardX, (float)cardY);
        e.Handled = true;
    }

    protected override void OnPointerMoved(Avalonia.Input.PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_writeableBitmap == null || _renderScale <= 0) return;

        var pt = e.GetPosition(this);
        double cardX = (pt.X - _renderOffsetX) / _renderScale;
        double cardY = (pt.Y - _renderOffsetY) / _renderScale;

        if (cardX < 0 || cardY < 0 || cardX >= _renderSourceW || cardY >= _renderSourceH)
        {
            CardPointerExited?.Invoke();
            return;
        }

        CardPointerMoved?.Invoke((float)cardX, (float)cardY);
    }

    protected override void OnPointerExited(Avalonia.Input.PointerEventArgs e)
    {
        base.OnPointerExited(e);
        CardPointerExited?.Invoke();
    }

    // ── Transition playback ───────────────────────────────────────────────────

    /// <summary>
    /// Begins an animated visual-effect transition between two card bitmaps.
    /// Takes ownership of <paramref name="from"/> and disposes it when the transition ends.
    /// </summary>
    public void PlayTransition(
        SKBitmap from, SKBitmap to,
        string effect, string? speed, string? direction)
    {
        // Cancel any in-progress transition.
        _transitionTimer?.Stop();
        _transitionTimer = null;
        _transitionFrom?.Dispose();

        int durationMs = speed?.Trim().ToLowerInvariant() switch
        {
            "very fast" => 150,
            "fast"      => 250,
            "slow"      => 600,
            "slowly"    => 600,
            _           => 400
        };

        // Allocate (or reuse) the composite frame buffer.
        if (_transitionFrameBitmap == null
            || _transitionFrameBitmap.Width  != from.Width
            || _transitionFrameBitmap.Height != from.Height)
        {
            _transitionFrameBitmap?.Dispose();
            _transitionFrameBitmap = new SKBitmap(
                from.Width, from.Height,
                SKColorType.Bgra8888, SKAlphaType.Opaque);
        }

        _transitionFrom   = from;
        _transitionTo     = to;
        _transitionEffect = effect;
        _transitionSpeed  = speed;
        _transitionT      = 0f;

        const double intervalMs = 33.3; // ~30 fps
        _transitionStep = (float)(intervalMs / durationMs);

        _transitionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(intervalMs) };
        _transitionTimer.Tick += OnTransitionTick;
        _transitionTimer.Start();

        // Render frame 0 immediately (shows "from" card while timer fires).
        AdvanceTransitionFrame();
    }

    private void OnTransitionTick(object? sender, EventArgs e)
    {
        _transitionT += _transitionStep;
        if (_transitionT >= 1f)
        {
            FinalizeTransition();
            return;
        }
        AdvanceTransitionFrame();
    }

    private void AdvanceTransitionFrame()
    {
        if (_transitionFrom == null || _transitionTo == null || _transitionFrameBitmap == null)
            return;

        TransitionRenderer.CompositeFrame(
            _transitionFrom, _transitionTo, _transitionFrameBitmap,
            _transitionEffect ?? "dissolve", _transitionT);

        UpdateBitmap(_transitionFrameBitmap);
        InvalidateVisual();
    }

    private void FinalizeTransition()
    {
        _transitionTimer!.Stop();
        _transitionTimer = null;
        _transitionFrom?.Dispose();
        _transitionFrom = null;
        _transitionTo   = null;

        // Let the normal Source pipeline take over for the final frame.
        UpdateBitmap();
        InvalidateVisual();
    }
}
