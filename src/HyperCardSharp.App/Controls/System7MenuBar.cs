using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using SkiaSharp;

namespace HyperCardSharp.App.Controls;

/// <summary>
/// System 7-style custom menu bar with Apple logo, rendered entirely with SkiaSharp
/// to avoid Avalonia MenuItem rendering bugs. Handles menu clicks and dropdown rendering.
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
    private int _hoverItemIndex = -1;
    private Bitmap? _appleLogo;
    private bool _logoLoaded = false;
    private DispatcherTimer? _flashTimer;
    private int _flashCount = 0;
    private int _flashingItemIndex = -1;
    private EventHandler<RoutedEventArgs>? _pendingClick;

    public static readonly DirectProperty<System7MenuBar, List<MenuDef>> MenusProperty =
        AvaloniaProperty.RegisterDirect<System7MenuBar, List<MenuDef>>(
            nameof(Menus),
            o => o._menus,
            (o, v) => o._menus = v);

    public List<MenuDef> Menus
    {
        get => _menus;
        set => SetAndRaise(MenusProperty, ref _menus, value);
    }

    static System7MenuBar()
    {
        AffectsRender<System7MenuBar>(MenusProperty);
        AffectsMeasure<System7MenuBar>(MenusProperty);
    }

    public System7MenuBar()
    {
        Height = 20;
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        LostFocus += (_, _) => { _openMenuIndex = -1; InvalidateVisual(); };
    }

    private void LoadAppleLogo()
    {
        if (_logoLoaded) return;
        _logoLoaded = true;
        
        try
        {
            var uri = "avares://HyperCardSharp.App/Assets/apple-logo.png";
            _appleLogo = new Bitmap(uri);
        }
        catch
        {
            _appleLogo = null;
        }
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var pos = e.GetPosition(this);
        var click = GetMenuClickAt((int)pos.X, (int)pos.Y);

        if (click.MenuIndex >= 0)
        {
            if (click.MenuIndex == _openMenuIndex)
                _openMenuIndex = -1;
            else
                _openMenuIndex = click.MenuIndex;
            _hoverItemIndex = -1;
            InvalidateVisual();
            e.Handled = true;
        }
        else if (_openMenuIndex >= 0 && click.ItemIndex >= 0)
        {
            var item = _menus[_openMenuIndex].Items[click.ItemIndex];
            if (!item.IsSeparator && item.Click != null)
            {
                // Start flash animation
                _flashingItemIndex = click.ItemIndex;
                _flashCount = 0;
                _pendingClick = item.Click;
                StartFlashAnimation();
                e.Handled = true;
            }
        }
        else if (_openMenuIndex >= 0)
        {
            _openMenuIndex = -1;
            InvalidateVisual();
        }
    }

    private void StartFlashAnimation()
    {
        if (_flashTimer != null)
            _flashTimer.Stop();

        _flashTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _flashTimer.Tick += (_, _) =>
        {
            _flashCount++;
            if (_flashCount >= 4)
            {
                // Animation complete - invoke the click handler
                _flashTimer?.Stop();
                _openMenuIndex = -1;
                _flashingItemIndex = -1;
                InvalidateVisual();
                _pendingClick?.Invoke(this, new RoutedEventArgs());
                _pendingClick = null;
            }
            else
            {
                InvalidateVisual();
            }
        };
        _flashTimer.Start();
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        var pos = e.GetPosition(this);
        if (_openMenuIndex >= 0)
        {
            var click = GetMenuClickAt((int)pos.X, (int)pos.Y);
            if (click.ItemIndex != _hoverItemIndex)
            {
                _hoverItemIndex = click.ItemIndex;
                InvalidateVisual();
            }
        }
    }

    private (int MenuIndex, int ItemIndex) GetMenuClickAt(int x, int y)
    {
        if (y < 0 || y >= Height)
            return (-1, -1);

        int currX = 6;

        // Apple menu
        if (x >= currX && x < currX + 14)
            return (0, -2);
        currX += 20;

        // Top-level menus
        for (int i = 1; i < _menus.Count; i++)
        {
            int width = _menus[i].Title.Length * 8 + 12;
            if (x >= currX && x < currX + width && y < 20)
                return (i, -2);
            currX += width;
        }

        // Dropdown items
        if (_openMenuIndex >= 0)
        {
            int dropX = GetMenuDropdownX(_openMenuIndex);
            int dropY = 20;
            int dropWidth = 200;

            if (x >= dropX && x < dropX + dropWidth)
            {
                var items = _menus[_openMenuIndex].Items;
                int itemY = dropY + 4;
                for (int i = 0; i < items.Count; i++)
                {
                    if (y >= itemY && y < itemY + 16)
                        return (_openMenuIndex, i);
                    itemY += 16;
                }
            }
        }

        return (-1, -1);
    }

    private int GetMenuDropdownX(int menuIndex)
    {
        if (menuIndex == 0) return 6;
        int x = 26;
        for (int i = 1; i < menuIndex; i++)
            x += _menus[i].Title.Length * 8 + 12;
        return x;
    }

    public override void Render(DrawingContext context)
    {
        LoadAppleLogo();  // Load on first render
        
        using (var surface = SKSurface.Create(new SKImageInfo((int)Bounds.Width, (int)Height)))
        {
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.White);

            int x = 6;

            // Apple logo
            DrawAppleMenuItem(canvas, x);
            x += 20;

            // Top-level menus
            for (int i = 1; i < _menus.Count; i++)
            {
                int width = _menus[i].Title.Length * 8 + 12;
                DrawTopMenuItem(canvas, x, _menus[i].Title, _openMenuIndex == i);
                x += width;
            }

            // Dropdown menu
            if (_openMenuIndex >= 0)
            {
                DrawDropdownMenu(canvas);
            }

            // Convert SkiaSharp surface to Avalonia image
            using (var image = surface.Snapshot())
            using (var pixmap = image.PeekPixels())
            {
                var bmp = new WriteableBitmap(new PixelSize((int)Bounds.Width, (int)Height), Vector.One, PixelFormat.Rgba8888);
                using (var buf = bmp.Lock())
                {
                    unsafe
                    {
                        long copySize = (long)pixmap.Width * pixmap.Height * 4;
                        System.Buffer.MemoryCopy(
                            pixmap.GetPixels().ToPointer(),
                            buf.Address.ToPointer(),
                            copySize,
                            copySize);
                    }
                }
                context.DrawImage(bmp, new Rect(0, 0, Bounds.Width, Height));
            }
        }
    }

    private void DrawAppleMenuItem(SKCanvas canvas, int x)
    {
        if (_appleLogo != null && _openMenuIndex == 0)
        {
            var paint = new SKPaint { Color = SKColors.Black };
            canvas.DrawRect(x - 2, 2, 14, 16, paint);
        }

        // Draw ⌘ or apple outline
        var textPaint = new SKPaint
        {
            Color = _openMenuIndex == 0 ? SKColors.White : SKColors.Black,
            Typeface = SKTypeface.FromFamilyName("Geneva", SKFontStyle.Bold),
            TextSize = 13
        };
        canvas.DrawText("⌘", x + 1, 15, textPaint);
    }

    private void DrawTopMenuItem(SKCanvas canvas, int x, string title, bool isOpen)
    {
        if (isOpen)
        {
            var paint = new SKPaint { Color = SKColors.Black };
            canvas.DrawRect(x - 2, 2, title.Length * 8 + 8, 16, paint);
        }

        var textPaint = new SKPaint
        {
            Color = isOpen ? SKColors.White : SKColors.Black,
            Typeface = SKTypeface.FromFamilyName("Geneva", SKFontStyle.Bold),
            TextSize = 13
        };
        canvas.DrawText(title, x + 2, 15, textPaint);
    }

    private void DrawDropdownMenu(SKCanvas canvas)
    {
        var menu = _menus[_openMenuIndex];
        int x = GetMenuDropdownX(_openMenuIndex);
        int y = 20;
        int width = 200;
        int height = menu.Items.Count * 16 + 8;

        // Background
        var bgPaint = new SKPaint { Color = SKColors.White };
        canvas.DrawRect(x, y, width, height, bgPaint);

        // Border
        var borderPaint = new SKPaint { Color = SKColors.Black, StrokeWidth = 1, Style = SKPaintStyle.Stroke };
        canvas.DrawRect(x, y, width, height, borderPaint);

        // Items
        var textPaint = new SKPaint
        {
            Color = SKColors.Black,
            Typeface = SKTypeface.FromFamilyName("Geneva"),
            TextSize = 12
        };

        int itemY = y + 4;
        for (int i = 0; i < menu.Items.Count; i++)
        {
            var item = menu.Items[i];

            // Show highlight if hovering or flashing
            bool showHighlight = (_hoverItemIndex == i && _flashingItemIndex < 0) ||
                                 (_flashingItemIndex == i && _flashCount % 2 == 0);

            if (showHighlight && !item.IsSeparator)
            {
                var hoverPaint = new SKPaint { Color = SKColors.Black };
                canvas.DrawRect(x + 2, itemY, width - 4, 14, hoverPaint);
                textPaint.Color = SKColors.White;
            }

            if (item.IsSeparator)
            {
                var linePaint = new SKPaint { Color = new SKColor(128, 128, 128), StrokeWidth = 1 };
                canvas.DrawLine(x + 4, itemY + 6, x + width - 4, itemY + 6, linePaint);
            }
            else
            {
                canvas.DrawText(item.Title, x + 8, itemY + 11, textPaint);
                if (item.Shortcut != null)
                {
                    canvas.DrawText(item.Shortcut, x + width - 60, itemY + 11, textPaint);
                }
            }

            if (showHighlight && !item.IsSeparator)
                textPaint.Color = SKColors.Black;

            itemY += 16;
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        return new Size(availableSize.Width, 20);
    }
}

