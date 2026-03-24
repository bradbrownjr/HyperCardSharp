using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
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
    private Bitmap? _appleLogo;
    private bool _logoLoaded;
    private bool _useColorLogo;
    private ImageBrush? _logoMaskBrush;

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

    // ── Asset loading ────────────────────────────────────────────────────────

    // PNG bytes embedded directly to avoid AssetLoader path issues on Windows.
    // 150 bytes — 16×16 RGBA rainbow Apple logo.
    // Shape: rounded body, bite on right, leaf going up-right, two bottom bumps.
    // Stripes top→bottom: GREEN, YELLOW, ORANGE, RED, PURPLE, BLUE.
    private static readonly byte[] AppleLogoPng = {
        0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 0x00, 0x00, 0x00, 0x0d,
        0x49, 0x48, 0x44, 0x52, 0x00, 0x00, 0x00, 0x10, 0x00, 0x00, 0x00, 0x10,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1f, 0xf3, 0xff, 0x61, 0x00, 0x00, 0x00,
        0x5d, 0x49, 0x44, 0x41, 0x54, 0x78, 0xda, 0x63, 0x60, 0x20, 0x00, 0x62,
        0x77, 0xbb, 0xfd, 0x07, 0x61, 0x06, 0x72, 0x00, 0x7d, 0x35, 0xc3, 0x34,
        0x20, 0x63, 0x7c, 0xe2, 0x04, 0x35, 0x13, 0xc2, 0x28, 0x06, 0xfc, 0xd9,
        0xa1, 0xfe, 0x9f, 0x1c, 0x4c, 0x3d, 0x03, 0xbe, 0x36, 0x29, 0xfc, 0x27,
        0x05, 0x63, 0x84, 0x01, 0x45, 0x9a, 0x41, 0xe0, 0x9e, 0xb5, 0xfe, 0x7f,
        0x52, 0x30, 0xc5, 0x06, 0x60, 0x18, 0x34, 0xcd, 0x76, 0xfa, 0x7f, 0x52,
        0x31, 0x86, 0x2b, 0x28, 0x36, 0x00, 0x0c, 0xe6, 0xde, 0xf9, 0x4f, 0x14,
        0xc6, 0x0b, 0xb0, 0x29, 0x26, 0xc9, 0x00, 0x12, 0x01, 0x00, 0x21, 0xc5,
        0x73, 0xd2, 0x59, 0x72, 0xb0, 0xb7, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45,
        0x4e, 0x44, 0xae, 0x42, 0x60, 0x82
    };

    private void EnsureAppleLogo()
    {
        if (_logoLoaded) return;
        _logoLoaded = true;
        try
        {
            using var ms = new System.IO.MemoryStream(AppleLogoPng);
            _appleLogo = new Bitmap(ms);
            _logoMaskBrush = new ImageBrush(_appleLogo)
            {
                Stretch = Stretch.Fill
            };
        }
        catch { _appleLogo = null; }
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
        EnsureAppleLogo();

        // White bar
        context.FillRectangle(Brushes.White, new Rect(0, 0, Bounds.Width, BarH));
        // Bottom border
        context.FillRectangle(Brushes.Black, new Rect(0, BarH - 1, Bounds.Width, 1));

        // Apple logo
        bool appleOpen = _openMenuIndex == 0;
        if (appleOpen)
            context.FillRectangle(Brushes.Black, new Rect(StartX - 2, 2, AppleW, 16));

        var logoRect = new Rect(StartX + 1, 3, 14, 14);
        if (_appleLogo != null && _logoMaskBrush != null)
        {
            if (_useColorLogo)
            {
                // Rainbow logo drawn directly
                context.DrawImage(_appleLogo, logoRect);
            }
            else
            {
                // B&W silhouette: use the logo's alpha channel as an opacity mask,
                // then fill with solid black (or white when menu is open/highlighted).
                IBrush fill = appleOpen ? Brushes.White : Brushes.Black;
                using (context.PushOpacityMask(_logoMaskBrush, logoRect))
                {
                    context.FillRectangle(fill, logoRect);
                }
            }
        }
        else
        {
            var sym = MakeFt("⌘", appleOpen ? Brushes.White : Brushes.Black);
            context.DrawText(sym, new Point(StartX, 2));
        }

        // Menu titles
        double x = StartX + AppleW;
        for (int i = 1; i < _menus.Count; i++)
        {
            bool isOpen = _openMenuIndex == i;
            double w = TitleWidth(i);
            if (isOpen)
                context.FillRectangle(Brushes.Black, new Rect(x - 2, 2, w, 16));
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
        Width  = W;
        Height = menu.Items.Count * RowH + 8;
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
        // White background with 1px black border
        context.FillRectangle(Brushes.White, new Rect(0, 0, W, Bounds.Height));
        context.DrawRectangle(null, new Pen(Brushes.Black, 1), new Rect(0.5, 0.5, W - 1, Bounds.Height - 1));

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
