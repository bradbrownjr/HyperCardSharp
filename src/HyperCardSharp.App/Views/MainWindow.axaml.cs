using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using HyperCardSharp.App.ViewModels;
using HyperCardSharp.Core.Containers;

namespace HyperCardSharp.App.Views;

public partial class MainWindow : Window
{
    private readonly StackViewModel _viewModel = new();

    // Menu bar (20px) + 2px outer border
    private const double ChromeHeight = 22;

    private static readonly double[] ZoomLevels = { 1.0, 1.5, 2.0, 4.0 };
    private int _currentScaleIndex = 0;

    // Retained for Ctrl+M "switch stack" within the same file
    private string? _currentOpenFileName;
    private List<StackEntry>? _currentStacks;

    // ── Recent files ──────────────────────────────────────────────────────────
    private const int MaxRecentFiles = 8;
    private List<string> _recentFiles = new();
    private static readonly string RecentFilesPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "HyperCardSharp", "recent.json");

    // Menu item whose submenu we rebuild when the recent-files list changes
    private HyperCardSharp.App.Controls.System7MenuBar.MenuItem? _recentFilesMenuItem;

    // Held so its Title can be updated when the render mode toggles
    private HyperCardSharp.App.Controls.System7MenuBar.MenuItem? _renderModeMenuItem;

    // Platform detection: on macOS, show ⌘ instead of Ctrl and accept Meta key
    private static bool IsMac => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
    private static string Mod(string key) => IsMac ? $"\u2318{key}" : $"Ctrl+{key}";
    private static bool HasCmdOrCtrl(KeyModifiers mods) =>
        mods.HasFlag(KeyModifiers.Control) || mods.HasFlag(KeyModifiers.Meta);

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        KeyDown += OnKeyDown;
        _viewModel.ShowAnswerDialog  += OnShowAnswerDialog;
        _viewModel.CrossStackNavigationRequested += OnCrossStackNavigation;
        _viewModel.GoHomeRequested += () => _ = OpenFileAsync();

        LoadRecentFiles();

        // Set up the custom menu bar
        InitializeMenuBar();

        // Populate the "Open Recent" submenu now that _recentFilesMenuItem is assigned
        RebuildRecentFilesMenu();
    }

    private void InitializeMenuBar()
    {
        var menuBar = this.FindControl<HyperCardSharp.App.Controls.System7MenuBar>("MenuBar");
        if (menuBar == null) return;

        var menus = new List<HyperCardSharp.App.Controls.System7MenuBar.MenuDef>
        {
            // Apple menu
            new HyperCardSharp.App.Controls.System7MenuBar.MenuDef
            {
                Title = "  ",
                Items = new()
                {
                    new() { Title = "About HyperCard#…", Click = (s, e) => OnMenuAbout(s, e) }
                }
            },
            // File menu
            new HyperCardSharp.App.Controls.System7MenuBar.MenuDef
            {
                Title = "File",
                Items = new()
                {
                    new() { Title = "Open\u2026", Shortcut = Mod("O"), Click = (s, e) => OnMenuOpen(s, e) },
                    new() { Title = "Switch Stack\u2026", Shortcut = Mod("M"), Click = (s, e) => OnMenuSwitchStack(s, e) },
                    (_recentFilesMenuItem = new() { Title = "Open Recent", Click = null }),
                    new() { IsSeparator = true },
                    new() { Title = "Quit", Shortcut = Mod("Q"), Click = (s, e) => OnMenuQuit(s, e) }
                }
            },
            // View menu
            new HyperCardSharp.App.Controls.System7MenuBar.MenuDef
            {
                Title = "View",
                Items = new()
                {
                    (_renderModeMenuItem = new() { Title = _viewModel.RenderModeLabel, Click = (s, e) => OnMenuToggleRenderMode(s, e) }),
                    new() { IsSeparator = true },
                    new() { Title = "Zoom 1\u00d7", Shortcut = Mod("1"), Click = (s, e) => OnMenuZoom1(s, e) },
                    new() { Title = "Zoom 2\u00d7", Shortcut = Mod("2"), Click = (s, e) => OnMenuZoom2(s, e) },
                    new() { Title = "Zoom 3\u00d7", Shortcut = Mod("3"), Click = (s, e) => OnMenuZoom3(s, e) },
                    new() { Title = "Zoom 4\u00d7", Shortcut = Mod("4"), Click = (s, e) => OnMenuZoom4(s, e) }
                }
            },
            // Help menu
            new HyperCardSharp.App.Controls.System7MenuBar.MenuDef
            {
                Title = "Help",
                Items = new()
                {
                    new() { Title = "Keyboard Shortcuts\u2026", Shortcut = "F1", Click = (s, e) => OnMenuHelp(s, e) }
                }
            }
        };

        menuBar.Menus = menus;

        // Clicking empty menu bar space triggers window drag
        menuBar.PointerPressed += (_, e) =>
        {
            if (!e.Handled && e.GetCurrentPoint(menuBar).Properties.IsLeftButtonPressed)
            {
                try { BeginMoveDrag(e); }
                catch { /* BeginMoveDrag can fail on some platforms if pointer state is stale */ }
            }
        };
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        var cardDisplay = this.FindControl<HyperCardSharp.App.Controls.SkiaBitmapControl>("CardDisplay");
        if (cardDisplay != null)
        {
            cardDisplay.CardPointerReleased += (x, y) => _viewModel.HandleCardClick(x, y);
            cardDisplay.CardPointerMoved += (x, y) =>
            {
                cardDisplay.Cursor = _viewModel.IsOverClickableButton(x, y)
                    ? new Cursor(StandardCursorType.Hand)
                    : Cursor.Default;
            };
            cardDisplay.CardPointerExited += () =>
            {
                cardDisplay.Cursor = Cursor.Default;
            };

            // Visual effect transitions
            _viewModel.TransitionRequested += (from, to, effect, speed, dir) =>
                cardDisplay.PlayTransition(from, to, effect, speed, dir);
        }

        // Drag-and-drop: accept files dragged onto the window
        var dropTarget = this.FindControl<Border>("RootBorder") ?? (InputElement)this;
        DragDrop.SetAllowDrop(dropTarget, true);
        dropTarget.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        dropTarget.AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private void OnShowAnswerDialog(string message)
    {
        _viewModel.StatusText = $"[answer] {message}";
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Right:
            case Key.Down:
            case Key.Space:
                _viewModel.NextCard();
                e.Handled = true;
                break;
            case Key.Left:
            case Key.Up:
                _viewModel.PreviousCard();
                e.Handled = true;
                break;
            case Key.Home:
                _viewModel.FirstCard();
                e.Handled = true;
                break;
            case Key.End:
                _viewModel.LastCard();
                e.Handled = true;
                break;
            case Key.O when HasCmdOrCtrl(e.KeyModifiers):
                _ = OpenFileAsync();
                e.Handled = true;
                break;
            // Switch stack within the current multi-stack file
            case Key.M when HasCmdOrCtrl(e.KeyModifiers):
                _ = SwitchStackAsync();
                e.Handled = true;
                break;
            // Show help dialog — F1 is the cross-platform convention;
            // on classic Mac OS 7, HyperCard used Cmd+? for help.
            case Key.F1:
            case Key.H when HasCmdOrCtrl(e.KeyModifiers):
                _ = ShowHelpAsync();
                e.Handled = true;
                break;
            // Zoom presets
            case Key.D1 when HasCmdOrCtrl(e.KeyModifiers):
                _currentScaleIndex = 0;
                ResizeToScale(ZoomLevels[0]);
                e.Handled = true;
                break;
            case Key.D2 when HasCmdOrCtrl(e.KeyModifiers):
                _currentScaleIndex = 1;
                ResizeToScale(ZoomLevels[1]);
                e.Handled = true;
                break;
            case Key.D3 when HasCmdOrCtrl(e.KeyModifiers):
                _currentScaleIndex = 2;
                ResizeToScale(ZoomLevels[2]);
                e.Handled = true;
                break;
            case Key.D4 when HasCmdOrCtrl(e.KeyModifiers):
                _currentScaleIndex = 3;
                ResizeToScale(ZoomLevels[3]);
                e.Handled = true;
                break;
            // Zoom in/out step (Ctrl+= and Ctrl+-)
            case Key.OemPlus when HasCmdOrCtrl(e.KeyModifiers):
            case Key.Add when HasCmdOrCtrl(e.KeyModifiers):
                ZoomIn();
                e.Handled = true;
                break;
            case Key.OemMinus when HasCmdOrCtrl(e.KeyModifiers):
            case Key.Subtract when HasCmdOrCtrl(e.KeyModifiers):
                ZoomOut();
                e.Handled = true;
                break;
            // Quit
            case Key.Q when HasCmdOrCtrl(e.KeyModifiers):
            case Key.F4 when e.KeyModifiers.HasFlag(KeyModifiers.Alt):
                Close();
                e.Handled = true;
                break;
        }
    }

    private void ResizeToScale(double scale)
    {
        double cardW = _viewModel.CardWidth * scale;
        double cardH = _viewModel.CardHeight * scale;
        double targetW = cardW + 2; // 1px border each side
        double targetH = cardH + ChromeHeight;

        var screen = Screens.Primary ?? Screens.All.FirstOrDefault();
        if (screen != null)
        {
            var workArea = screen.WorkingArea;
            double maxW = workArea.Width / screen.Scaling;
            double maxH = workArea.Height / screen.Scaling;
            targetW = Math.Min(targetW, maxW);
            targetH = Math.Min(targetH, maxH);
        }

        Width = targetW;
        Height = targetH;
    }

    private void ZoomIn()
    {
        if (_currentScaleIndex < ZoomLevels.Length - 1)
        {
            _currentScaleIndex++;
            ResizeToScale(ZoomLevels[_currentScaleIndex]);
        }
    }

    private void ZoomOut()
    {
        if (_currentScaleIndex > 0)
        {
            _currentScaleIndex--;
            ResizeToScale(ZoomLevels[_currentScaleIndex]);
        }
    }

    private async System.Threading.Tasks.Task ShowHelpAsync()
    {
        var help = new HelpWindow();
        help.ColorMode = _viewModel.RenderMode == HyperCardSharp.Rendering.RenderMode.Color;
        await help.ShowDialog(this);
    }

    private void OnTitleBarClose(object? sender, EventArgs e)
    {
        Close();
    }

    private async System.Threading.Tasks.Task OpenFileAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open HyperCard Stack",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("HyperCard Stacks") { Patterns = new[] { "*" } },
                new FilePickerFileType("All Files") { Patterns = new[] { "*.*" } }
            }
        });

        if (files.Count == 0)
            return;

        var file = files[0];
        var localPath = file.TryGetLocalPath();
        try
        {
            await using var stream = await file.OpenReadAsync();
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            var raw = ms.ToArray();

            var logLines = new List<string>();
            var stacks = ContainerPipeline.UnwrapEntries(raw, msg => logLines.Add(msg));

            if (stacks.Count == 0)
            {
                var detail = logLines.Count > 0 ? logLines[^1] : "no stack found in container.";
                _viewModel.StatusText = $"Could not open \"{file.Name}\": {detail}";
                return;
            }

            // Sort alphabetically before storing and presenting
            stacks.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name));

            // Store for Ctrl+M re-pick
            _currentOpenFileName = file.Name;
            _currentStacks = stacks;

            if (localPath != null)
                AddToRecentFiles(localPath);

            await PickAndLoadStack(file.Name, stacks);
        }
        catch (Exception ex)
        {
            _viewModel.StatusText = $"Error opening \"{file.Name}\": {ex.Message}";
        }
    }

    /// <summary>
    /// Re-show the stack picker for the currently open multi-stack file.
    /// </summary>
    private async System.Threading.Tasks.Task SwitchStackAsync()
    {
        if (_currentStacks == null || _currentStacks.Count < 2 || _currentOpenFileName == null)
        {
            _viewModel.StatusText = "No multi-stack file loaded. Ctrl+O to open a file.";
            return;
        }

        await PickAndLoadStack(_currentOpenFileName, _currentStacks);
    }

    private async System.Threading.Tasks.Task PickAndLoadStack(
        string fileName, List<StackEntry> stacks)
    {
        StackEntry selected;

        if (stacks.Count > 1)
        {
            var picker = new StackPickerWindow(stacks);
            picker.ColorMode = _viewModel.RenderMode == HyperCardSharp.Rendering.RenderMode.Color;
            await picker.ShowDialog(this);
            int selectedIndex = picker.SelectedIndex;

            if (selectedIndex < 0 || selectedIndex >= stacks.Count)
            {
                _viewModel.StatusText = "Stack selection cancelled.";
                return;
            }

            selected = stacks[selectedIndex];
        }
        else
        {
            selected = stacks[0];
        }

        _viewModel.LoadStack(selected.Data, fileName,
            stacks.Count > 1 ? selected.Name : null,
            selected.ResourceFork);
    }

    // ── Menu event handlers ────────────────────────────────────────────────────

    private void OnMenuOpen(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => _ = OpenFileAsync();

    private void OnMenuSwitchStack(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => _ = SwitchStackAsync();

    private void OnMenuToggleRenderMode(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _viewModel.ToggleRenderMode();
        bool isColor = _viewModel.RenderMode == HyperCardSharp.Rendering.RenderMode.Color;
        // Update the menu item label to reflect the new mode
        if (_renderModeMenuItem != null)
            _renderModeMenuItem.Title = _viewModel.RenderModeLabel;
        // Sync Apple logo: rainbow in color mode, black silhouette in B&W
        var menuBar = this.FindControl<HyperCardSharp.App.Controls.System7MenuBar>("MenuBar");
        if (menuBar != null)
            menuBar.UseColorLogo = isColor;
    }

    private void OnMenuZoom1(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    { _currentScaleIndex = 0; ResizeToScale(ZoomLevels[0]); }

    private void OnMenuZoom2(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    { _currentScaleIndex = 1; ResizeToScale(ZoomLevels[1]); }

    private void OnMenuZoom3(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    { _currentScaleIndex = 2; ResizeToScale(ZoomLevels[2]); }

    private void OnMenuZoom4(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    { _currentScaleIndex = 3; ResizeToScale(ZoomLevels[3]); }

    private void OnMenuHelp(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => _ = ShowHelpAsync();

    private void OnMenuAbout(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => _ = ShowAboutAsync();

    private async System.Threading.Tasks.Task ShowAboutAsync()
    {
        var about = new AboutWindow();
        about.ColorMode = _viewModel.RenderMode == HyperCardSharp.Rendering.RenderMode.Color;
        await about.ShowDialog(this);
    }

    private void OnMenuQuit(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => Close();

    // ── Drag-and-drop ─────────────────────────────────────────────────────────

    private static void OnDragOver(object? sender, DragEventArgs e)
    {
#pragma warning disable CS0618
        e.DragEffects = e.Data.Contains(DataFormats.Files)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
#pragma warning restore CS0618
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
#pragma warning disable CS0618
        if (!e.Data.Contains(DataFormats.Files)) return;
        var files = e.Data.GetFiles();
#pragma warning restore CS0618
        if (files == null) return;
        var first = files.FirstOrDefault();
        if (first == null) return;
        e.Handled = true;
        _ = OpenFileByPathAsync(first.TryGetLocalPath() ?? "");
    }

    // ── Recent files ──────────────────────────────────────────────────────────

    private void LoadRecentFiles()
    {
        try
        {
            if (File.Exists(RecentFilesPath))
            {
                var json = File.ReadAllText(RecentFilesPath);
                _recentFiles = JsonSerializer.Deserialize<List<string>>(json) ?? new();
                // Remove paths that no longer exist
                _recentFiles = _recentFiles.Where(File.Exists).ToList();
            }
        }
        catch { _recentFiles = new(); }
        RebuildRecentFilesMenu();
    }

    private void SaveRecentFiles()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(RecentFilesPath)!);
            File.WriteAllText(RecentFilesPath,
                JsonSerializer.Serialize(_recentFiles));
        }
        catch { /* non-critical — ignore */ }
    }

    private void AddToRecentFiles(string path)
    {
        _recentFiles.Remove(path);          // move to top if already present
        _recentFiles.Insert(0, path);
        if (_recentFiles.Count > MaxRecentFiles)
            _recentFiles = _recentFiles.Take(MaxRecentFiles).ToList();
        SaveRecentFiles();
        RebuildRecentFilesMenu();
    }

    private void RebuildRecentFilesMenu()
    {
        if (_recentFilesMenuItem == null) return;
        // Note: System7MenuBar.MenuItem.SubItems would require richer menu model.
        // We instead update the Title to show the count and use a Click handler
        // that shows a file-picker pre-filtered to recent entries.
        // This is a minimal but functional implementation.
        if (_recentFiles.Count == 0)
        {
            _recentFilesMenuItem.Title = "Open Recent";
            _recentFilesMenuItem.Click = null;
        }
        else
        {
            _recentFilesMenuItem.Title = $"Open Recent ({_recentFiles.Count})";
            _recentFilesMenuItem.Click = (_, _) => _ = ShowRecentFilesPickerAsync();
        }
    }

    private async System.Threading.Tasks.Task ShowRecentFilesPickerAsync()
    {
        // Show the most recent files as a stack picker (reuse existing dialog)
        var entries = _recentFiles
            .Select(p => new StackEntry { Name = Path.GetFileName(p), Data = Array.Empty<byte>(), FullPath = p })
            .ToList();

        // Pick path
        string? selectedPath = null;
        if (entries.Count == 1)
        {
            selectedPath = entries[0].FullPath;
        }
        else
        {
            // Use StackPickerWindow to let the user choose from recent files
            var picker = new StackPickerWindow(entries);
            picker.ColorMode = _viewModel.RenderMode == HyperCardSharp.Rendering.RenderMode.Color;
            await picker.ShowDialog(this);
            int idx = picker.SelectedIndex;
            if (idx >= 0 && idx < entries.Count)
                selectedPath = entries[idx].FullPath;
        }

        if (selectedPath != null)
            await OpenFileByPathAsync(selectedPath);
    }

    /// <summary>Opens a file given its absolute path (used by drag-and-drop and recent files).</summary>
    public async System.Threading.Tasks.Task OpenFileByPathAsync(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        try
        {
            var raw = await File.ReadAllBytesAsync(path);
            var logLines = new List<string>();
            var stacks = ContainerPipeline.UnwrapEntries(raw, msg => logLines.Add(msg));

            if (stacks.Count == 0)
            {
                var detail = logLines.Count > 0 ? logLines[^1] : "no stack found in container.";
                _viewModel.StatusText = $"Could not open \"{Path.GetFileName(path)}\": {detail}";
                return;
            }

            stacks.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name));
            _currentOpenFileName = Path.GetFileName(path);
            _currentStacks = stacks;

            AddToRecentFiles(path);
            await PickAndLoadStack(Path.GetFileName(path), stacks);
        }
        catch (Exception ex)
        {
            _viewModel.StatusText = $"Error opening \"{Path.GetFileName(path)}\": {ex.Message}";
        }
    }

    /// <summary>
    /// Called when HyperTalk executes <c>go to stack "name"</c>.
    /// Looks for the stack in the same directory as the currently-open file.
    /// </summary>
    private void OnCrossStackNavigation(string stackName, string? cardName, int? cardNumber)
    {
        _ = OpenCrossStackAsync(stackName, cardName, cardNumber);
    }

    private async System.Threading.Tasks.Task OpenCrossStackAsync(
        string stackName, string? cardName, int? cardNumber)
    {
        // Resolve the stack file relative to the current file's directory
        string? baseDir = null;
        if (_currentOpenFileName != null)
        {
            // Try to find the directory from a recent file entry with this name
            var match = _recentFiles.FirstOrDefault(
                p => string.Equals(Path.GetFileName(p), _currentOpenFileName,
                     StringComparison.OrdinalIgnoreCase));
            if (match != null) baseDir = Path.GetDirectoryName(match);
        }

        string? resolvedPath = null;
        if (baseDir != null)
        {
            // Try exact name match, then with common HyperCard extensions
            foreach (var candidate in new[]
            {
                Path.Combine(baseDir, stackName),
                Path.Combine(baseDir, stackName + ".sit"),
                Path.Combine(baseDir, stackName + ".img"),
                Path.Combine(baseDir, stackName + "_HyperCard"),
            })
            {
                if (File.Exists(candidate)) { resolvedPath = candidate; break; }
            }

            // Case-insensitive fallback: search for any file whose name starts with stackName
            if (resolvedPath == null)
            {
                resolvedPath = Directory.EnumerateFiles(baseDir)
                    .FirstOrDefault(p => Path.GetFileNameWithoutExtension(p)
                        .StartsWith(stackName, StringComparison.OrdinalIgnoreCase));
            }
        }

        if (resolvedPath != null)
        {
            await OpenFileByPathAsync(resolvedPath);
            // Navigate to the specified card after loading
            if (cardNumber.HasValue)
                _viewModel.GoToCardNumber(cardNumber.Value);
            else if (cardName != null)
                _viewModel.GoToCardByName(cardName);
        }
        else
        {
            _viewModel.StatusText =
                $"Cross-stack: stack \"{stackName}\" not found in the same directory. " +
                "Use Ctrl+O to open it manually.";
        }
    }

}
