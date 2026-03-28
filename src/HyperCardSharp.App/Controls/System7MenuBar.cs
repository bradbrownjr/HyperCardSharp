using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using System.Globalization;

namespace HyperCardSharp.App.Controls;

/// <summary>
/// System 7-style menu bar rendered with Avalonia native DrawingContext.
/// Dropdowns are displayed via Popup controls positioned below each menu title.
/// </summary>
public class System7MenuBar : Control
{
    public class MenuItem
    {
        public string Title { get; set; } = "";
        public string? Shortcut { get; set; }
        public bool IsSeparator { get; set; }
        public EventHandler<RoutedEventArgs>? Click { get; set; }
    }

    public class MenuDef
    {
        public string Title { get; set; } = "";
        public List<MenuItem> Items { get; set; } = new();
    }

    private List<MenuDef> _menus = new();
    private int _openMenuIndex = -1;
    private Popup? _activePopup;
    private MenuDropdown? _activeDropdown;
    private bool _useColorLogo;

    private const double BarH   = 20;
    private const double StartX = 6;
    private const double AppleW = 20;
    private const double ItemPad = 8;
    // 12pt matches Chicago_12 design size (system.css reference).
    // ChicagoFLF has no Bold variant — do NOT set FontWeight.Bold or Avalonia
    // falls back to a different system font (e.g. Arial Bold) which looks wrong.
    private const double FontSz = 12;

    private static readonly Typeface ChicagoTyp = new Typeface(
        "avares://HyperCardSharp.App/Assets/Fonts#ChicagoFLF, Chicago, Geneva, Helvetica, Arial");

    public static readonly DirectProperty<System7MenuBar, List<MenuDef>> MenusProperty =
        AvaloniaProperty.RegisterDirect<System7MenuBar, List<MenuDef>>(
            nameof(Menus), o => o._menus, (o, v) => o._menus = v);

    public List<MenuDef> Menus
    {
        get => _menus;
        set => SetAndRaise(MenusProperty, ref _menus, value);
    }

    /// <summary>
    /// When true, draw the rainbow Apple logo. When false (default), draw a black silhouette.
    /// </summary>
    public bool UseColorLogo
    {
        get => _useColorLogo;
        set { _useColorLogo = value; InvalidateVisual(); }
    }

    static System7MenuBar() => AffectsRender<System7MenuBar>(MenusProperty);

    public System7MenuBar()
    {
        Height = BarH;
        PointerPressed += OnPointerPressed;
        PointerMoved   += OnPointerMoved;
    }

    // ── Apple logo ─────────────────────────────────────────────────────────

    // ── Pixel-art Apple logo ────────────────────────────────────────────────
    // Matches the system.css apple.svg: 9×11 pixel grid rendered with rects.
    // Each tuple is (x, y, width, height) in the 9-wide × 11-tall space.
    private static readonly (int X, int Y, int W, int H)[] ApplePixels =
    [
        (5, 0, 2, 1),   // stem
        (4, 1, 2, 1),   // leaf upper
        (4, 2, 1, 1),   // leaf lower
        (1, 3, 3, 1),   // body top-left  (bite gap at x=4)
        (5, 3, 3, 1),   // body top-right
        (0, 4, 9, 1),   // body full
        (0, 5, 9, 2),   // body mid
        (0, 7, 9, 2),   // body lower-mid
        (1, 9, 7, 1),   // body narrow
        (2, 10, 2, 1),  // left foot
        (5, 10, 2, 1),  // right foot
    ];

    // Rainbow color per pixel row (rows 0-10). Classic 6-stripe Apple logo.
    private static readonly uint[] RainbowRowColors =
    [
        0xFF00B300,  // row 0  — green  (stem)
        0xFF00B300,  // row 1  — green  (leaf)
        0xFFFFFF00,  // row 2  — yellow
        0xFFFFFF00,  // row 3  — yellow (top body)
        0xFFFF8000,  // row 4  — orange
        0xFFFF8000,  // row 5  — orange
        0xFFFF0000,  // row 6  — red
        0xFFFF0000,  // row 7  — red
        0xFF8000FF,  // row 8  — purple
        0xFF0066FF,  // row 9  — blue
        0xFF0066FF,  // row 10 — blue
    ];

    /// <summary>
    /// Draw the Apple logo as pixel-art rectangles scaled to destRect.
    /// Matches the system.css apple.svg (9×11 grid of rects).
    /// </summary>
    private void DrawAppleLogo(DrawingContext ctx, Rect destRect, bool inverted)
    {
        double sx = destRect.Width / 9.0;
        double sy = destRect.Height / 11.0;

        foreach (var (px, py, pw, ph) in ApplePixels)
        {
            double rx = destRect.X + px * sx;
            double ry = destRect.Y + py * sy;
            double rw = pw * sx;
            double rh = ph * sy;

            if (_useColorLogo && !inverted)
            {
                // Draw each pixel row in its rainbow color
                for (int row = py; row < py + ph; row++)
                {
                    var color = new SolidColorBrush(Color.FromUInt32(RainbowRowColors[row]));
                    double rowY = destRect.Y + row * sy;
                    ctx.FillRectangle(color, new Rect(rx, rowY, rw, sy));
                }
            }
            else
            {
                ctx.FillRectangle(
                    inverted ? Brushes.White : Brushes.Black,
                    new Rect(rx, ry, rw, rh));
            }
        }
    }

    // ── Layout helpers ───────────────────────────────────────────────────────

    private double TitleWidth(int idx)
    {
        var ft = MakeFt(_menus[idx].Title, Brushes.Black);
        return ft.Width + ItemPad * 2;
    }

    private double MenuLeft(int idx)
    {
        if (idx == 0) return StartX;
        double x = StartX + AppleW;
        for (int i = 1; i < idx; i++) x += TitleWidth(i);
        return x;
    }

    private int HitTest(double x)
    {
        if (x >= StartX && x < StartX + AppleW) return 0;
        double cur = StartX + AppleW;
        for (int i = 1; i < _menus.Count; i++)
        {
            double w = TitleWidth(i);
            if (x >= cur && x < cur + w) return i;
            cur += w;
        }
        return -1;
    }

    private FormattedText MakeFt(string text, IBrush fg) =>
        new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            ChicagoTyp, FontSz, fg);

    // ── Input ────────────────────────────────────────────────────────────────

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        int idx = HitTest(e.GetPosition(this).X);
        if (idx >= 0)
        {
            if (_openMenuIndex == idx)
                CloseMenu();
            else
                OpenMenu(idx);
            e.Handled = true;
        }
        else if (_openMenuIndex >= 0)
        {
            CloseMenu();
            e.Handled = true;  // absorb click that closes menu so we don't start drag
        }
    }

    // Mac-style fluid menu switching: when a menu is open, hovering over
    // another menu title immediately switches to that menu.
    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_openMenuIndex < 0) return;
        int idx = HitTest(e.GetPosition(this).X);
        if (idx >= 0 && idx != _openMenuIndex)
            OpenMenu(idx);
    }

    // ── Menu open / close ────────────────────────────────────────────────────

    private void OpenMenu(int idx)
    {
        CloseMenu();
        _openMenuIndex = idx;
        InvalidateVisual();

        var dropdown = new MenuDropdown(_menus[idx]);
        dropdown.ItemSelected += (_, item) =>
        {
            dropdown.BeginFlash(item, () =>
            {
                _activePopup?.Close();
                item.Click?.Invoke(this, new RoutedEventArgs());
            });
        };

        var popup = new Popup
        {
            PlacementTarget    = this,
            Placement          = PlacementMode.AnchorAndGravity,
            PlacementAnchor    = PopupAnchor.BottomLeft,
            PlacementGravity   = PopupGravity.BottomRight,
            HorizontalOffset   = MenuLeft(idx),
            IsLightDismissEnabled = true,
            Child              = dropdown
        };

        // Capture popup in a local so the closure doesn't hold a stale reference.
        // Only reset shared state if this popup is still the active one — guards
        // against a light-dismiss Closed event firing after OpenMenu() has already
        // replaced the popup with a new one.
        var cp = popup;
        popup.Closed += (_, _) =>
        {
            if (!ReferenceEquals(_activePopup, cp)) return;
            LogicalChildren.Remove(cp);
            _openMenuIndex  = -1;
            _activePopup    = null;
            _activeDropdown = null;
            InvalidateVisual();
        };

        _activePopup    = popup;
        _activeDropdown = dropdown;
        LogicalChildren.Add(popup);
        popup.Open();
    }

    private void CloseMenu()
    {
        if (_activePopup != null)
        {
            // Null _activePopup BEFORE calling Close() so the popup.Closed capture
            // guard sees a null and returns early, avoiding duplicate state resets.
            var p = _activePopup;
            _activePopup    = null;
            _activeDropdown = null;
            LogicalChildren.Remove(p);
            p.Close();
        }
        _openMenuIndex = -1;
        InvalidateVisual();
    }

    // ── Rendering ────────────────────────────────────────────────────────────

    public override void Render(DrawingContext context)
    {
        // White bar
        context.FillRectangle(Brushes.White, new Rect(0, 0, Bounds.Width, BarH));
        // Bottom border
        context.FillRectangle(Brushes.Black, new Rect(0, BarH - 1, Bounds.Width, 1));

        // Apple logo
        bool appleOpen = _openMenuIndex == 0;
        if (appleOpen)
            context.FillRectangle(Brushes.Black, new Rect(StartX - 2, 0, AppleW, BarH - 1));

        // Logo 13×14px: each 9×11 grid cell ≈ 1.4×1.3 screen pixels.
        // Fits comfortably in the 20px bar with ~3px margin top and bottom.
        var logoRect = new Rect(StartX + 3, 3, 13, 14);
        DrawAppleLogo(context, logoRect, appleOpen);

        // Menu titles — vertically centred in the bar
        double x = StartX + AppleW;
        for (int i = 1; i < _menus.Count; i++)
        {
            bool isOpen = _openMenuIndex == i;
            double w = TitleWidth(i);
            if (isOpen)
                context.FillRectangle(Brushes.Black, new Rect(x - 2, 0, w, BarH - 1));
            var ft = MakeFt(_menus[i].Title, isOpen ? Brushes.White : Brushes.Black);
            double textY = Math.Floor((BarH - 1 - ft.Height) / 2);
            context.DrawText(ft, new Point(x + ItemPad, textY));
            x += w;
        }
    }

    protected override Size MeasureOverride(Size availableSize)
        => new(availableSize.Width, BarH);
}

/// <summary>
/// The dropdown panel rendered inside a Popup. Uses native DrawingContext.
/// Supports hover highlight and classic Mac flash-on-select animation.
/// </summary>
internal class MenuDropdown : Control
{
    private readonly System7MenuBar.MenuDef _menu;
    private int _hoverIndex = -1;
    private int _flashIndex = -1;
    private bool _flashHighlit;
    private DispatcherTimer? _flashTimer;
    private Action? _afterFlash;

    private const int RowH   = 18;
    private const int PadX   = 8;
    private const double W   = 200;
    private const double FtSz = 12;

    private static readonly Typeface ItemTyp = new Typeface("Geneva, Helvetica, Arial");

    public event EventHandler<System7MenuBar.MenuItem>? ItemSelected;

    public MenuDropdown(System7MenuBar.MenuDef menu)
    {
        _menu  = menu;
        // +1 for drop shadow on right edge
        Width  = W + 1;
        // +1 for drop shadow on bottom edge
        Height = menu.Items.Count * RowH + 8 + 1;
        PointerMoved   += OnPointerMoved;
        PointerPressed += OnPointerPressed;
    }

    private int HitItem(double y)
    {
        int idx = (int)((y - 4) / RowH);
        return (idx >= 0 && idx < _menu.Items.Count) ? idx : -1;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        int idx = HitItem(e.GetPosition(this).Y);
        if (idx >= 0 && _menu.Items[idx].IsSeparator) idx = -1;
        if (idx != _hoverIndex) { _hoverIndex = idx; InvalidateVisual(); }
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        int idx = HitItem(e.GetPosition(this).Y);
        if (idx >= 0 && !_menu.Items[idx].IsSeparator)
        {
            ItemSelected?.Invoke(this, _menu.Items[idx]);
            e.Handled = true;
        }
    }

    public void BeginFlash(System7MenuBar.MenuItem item, Action onDone)
    {
        _flashIndex  = _menu.Items.IndexOf(item);
        if (_flashIndex < 0) { onDone(); return; }
        _hoverIndex  = -1;
        _afterFlash  = onDone;
        _flashHighlit = true;
        int count = 0;
        _flashTimer?.Stop();
        _flashTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _flashTimer.Tick += (_, _) =>
        {
            _flashHighlit = !_flashHighlit;
            count++;
            InvalidateVisual();
            if (count >= 4) { _flashTimer?.Stop(); _afterFlash?.Invoke(); }
        };
        InvalidateVisual();
        _flashTimer.Start();
    }

    public override void Render(DrawingContext context)
    {
        double menuW = W;
        double menuH = Bounds.Height - 1; // Exclude shadow row

        // Drop shadow — 1px black line on right and bottom edges (System 7 style)
        context.FillRectangle(Brushes.Black, new Rect(1, menuH, menuW, 1));  // bottom shadow
        context.FillRectangle(Brushes.Black, new Rect(menuW, 1, 1, menuH));  // right shadow

        // White background with 1px black border
        context.FillRectangle(Brushes.White, new Rect(0, 0, menuW, menuH));
        context.DrawRectangle(null, new Pen(Brushes.Black, 1), new Rect(0.5, 0.5, menuW - 1, menuH - 1));

        for (int i = 0; i < _menu.Items.Count; i++)
        {
            var item = _menu.Items[i];
            double y  = 4 + i * RowH;
            bool hl   = !item.IsSeparator &&
                        ((_flashIndex == i && _flashHighlit) ||
                         (_flashIndex < 0 && _hoverIndex == i));

            if (hl)
                context.FillRectangle(Brushes.Black, new Rect(2, y, W - 4, RowH - 2));

            IBrush fg = hl ? Brushes.White : Brushes.Black;

            if (item.IsSeparator)
            {
                context.FillRectangle(new SolidColorBrush(Color.FromRgb(128, 128, 128)),
                    new Rect(4, y + RowH / 2.0 - 0.5, W - 8, 1));
            }
            else
            {
                var ft = new FormattedText(item.Title, CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight, ItemTyp, FtSz, fg);
                context.DrawText(ft, new Point(PadX, y + 2));

                if (item.Shortcut != null)
                {
                    var sft = new FormattedText(item.Shortcut, CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight, ItemTyp, 11, fg);
                    context.DrawText(sft, new Point(W - 65, y + 2));
                }
            }
        }
    }
}
