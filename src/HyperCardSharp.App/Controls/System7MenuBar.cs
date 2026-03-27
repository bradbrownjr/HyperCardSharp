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
    private const double FontSz = 13;

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
    }

    // ── Apple logo ─────────────────────────────────────────────────────────

    // Procedural 16×16 pixel-art Apple logo.
    // Classic bitten-apple silhouette: leaf pointing up-right, rounded body,
    // concave bite on right side, smoothly tapered bottom.
    // Each string is 16 chars: '#' = filled, '.' = transparent.
    // Rainbow stripe colors top→bottom: GREEN, YELLOW, ORANGE, RED, PURPLE, BLUE.
    private static readonly string[] AppleShape =
    [
        ".......##.......",  // row  0  leaf tip
        "......##........",  // row  1  leaf
        "...####.####....",  // row  2  top body + stem dip
        "..###########...",  // row  3  upper body
        "..############..",  // row  4  widest
        "..########.###..",  // row  5  bite starts (right side)
        "..#######..###..",  // row  6  bite deeper
        "..#######..###..",  // row  7  bite
        "..########.###..",  // row  8  bite closing
        "..############..",  // row  9  full body
        "..###########...",  // row 10  narrowing
        "...#########....",  // row 11  narrow
        "....#######.....",  // row 12  more narrow
        ".....#####......",  // row 13  bottom
        "................",  // row 14
        "................",  // row 15
    ];

    // Rainbow stripe bands (row ranges, inclusive) and their ARGB colors.
    private static readonly (int FromRow, int ToRow, uint Color)[] RainbowStripes =
    [
        (0,  1,  0xFF00B300),  // GREEN   (leaf)
        (2,  3,  0xFF00B300),  // GREEN   (body top)
        (4,  5,  0xFFFFFF00),  // YELLOW
        (6,  7,  0xFFFF8000),  // ORANGE
        (8,  9,  0xFFFF0000),  // RED
        (10, 11, 0xFF8000FF),  // PURPLE
        (12, 13, 0xFF0066FF),  // BLUE
    ];

    private uint GetRainbowColor(int row)
    {
        foreach (var (from, to, color) in RainbowStripes)
            if (row >= from && row <= to) return color;
        return 0xFF000000;
    }

    /// <summary>
    /// Draw the Apple logo procedurally into the DrawingContext at the given rect.
    /// </summary>
    private void DrawAppleLogo(DrawingContext ctx, Rect destRect, bool inverted)
    {
        double pxW = destRect.Width  / 16.0;
        double pxH = destRect.Height / 16.0;

        for (int row = 0; row < 16; row++)
        {
            var line = AppleShape[row];
            for (int col = 0; col < 16; col++)
            {
                if (line[col] != '#') continue;

                IBrush brush;
                if (inverted)
                {
                    brush = Brushes.White;
                }
                else if (_useColorLogo)
                {
                    uint c = GetRainbowColor(row);
                    brush = new SolidColorBrush(Color.FromUInt32(c));
                }
                else
                {
                    brush = Brushes.Black;
                }

                double x = destRect.X + col * pxW;
                double y = destRect.Y + row * pxH;
                ctx.FillRectangle(brush, new Rect(x, y, Math.Ceiling(pxW), Math.Ceiling(pxH)));
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

        popup.Closed += (_, _) =>
        {
            LogicalChildren.Remove(popup);
            _openMenuIndex = -1;
            _activePopup   = null;
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
            LogicalChildren.Remove(_activePopup);
            _activePopup.Close();
            _activePopup    = null;
            _activeDropdown = null;
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

        var logoRect = new Rect(StartX + 2, 2, 14, 14);
        DrawAppleLogo(context, logoRect, appleOpen);

        // Menu titles
        double x = StartX + AppleW;
        for (int i = 1; i < _menus.Count; i++)
        {
            bool isOpen = _openMenuIndex == i;
            double w = TitleWidth(i);
            if (isOpen)
                context.FillRectangle(Brushes.Black, new Rect(x - 2, 0, w, BarH - 1));
            var ft = MakeFt(_menus[i].Title, isOpen ? Brushes.White : Brushes.Black);
            context.DrawText(ft, new Point(x + ItemPad, 3));
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
