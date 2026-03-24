using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using HyperCardSharp.App.ViewModels;
using HyperCardSharp.Core.Containers;

namespace HyperCardSharp.App.Views;

public partial class MainWindow : Window
{
    private readonly StackViewModel _viewModel = new();

    // Menu bar height (~20px) + 2px outer border
    private const double ChromeHeight = 22;

    private static readonly double[] ZoomLevels = { 1.0, 1.5, 2.0, 4.0 };
    private int _currentScaleIndex = 0;

    // Retained for Ctrl+L "switch stack" within the same file
    private string? _currentOpenFileName;
    private List<(string Name, byte[] Data)>? _currentStacks;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        KeyDown += OnKeyDown;
        _viewModel.ShowAnswerDialog  += OnShowAnswerDialog;

        // Make the menu bar border draggable (it is the top chrome)
        var menuBarBorder = this.FindControl<Avalonia.Controls.Border>("MenuBarBorder");
        if (menuBarBorder != null)
        {
            menuBarBorder.PointerPressed += (_, e) =>
            {
                if (e.GetCurrentPoint(menuBarBorder).Properties.IsLeftButtonPressed)
                    BeginMoveDrag(e);
            };
        }
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        var cardDisplay = this.FindControl<HyperCardSharp.App.Controls.SkiaBitmapControl>("CardDisplay");
        if (cardDisplay != null)
            cardDisplay.CardPointerReleased += (x, y) => _viewModel.HandleCardClick(x, y);
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
            case Key.O when e.KeyModifiers.HasFlag(KeyModifiers.Control):
                _ = OpenFileAsync();
                e.Handled = true;
                break;
            // Switch stack within the current multi-stack file
            case Key.M when e.KeyModifiers.HasFlag(KeyModifiers.Control):
                _ = SwitchStackAsync();
                e.Handled = true;
                break;
            // Show help dialog
            case Key.H when e.KeyModifiers.HasFlag(KeyModifiers.Control):
                _ = ShowHelpAsync();
                e.Handled = true;
                break;
            // Zoom presets
            case Key.D1 when e.KeyModifiers.HasFlag(KeyModifiers.Control):
                _currentScaleIndex = 0;
                ResizeToScale(ZoomLevels[0]);
                e.Handled = true;
                break;
            case Key.D2 when e.KeyModifiers.HasFlag(KeyModifiers.Control):
                _currentScaleIndex = 1;
                ResizeToScale(ZoomLevels[1]);
                e.Handled = true;
                break;
            case Key.D3 when e.KeyModifiers.HasFlag(KeyModifiers.Control):
                _currentScaleIndex = 2;
                ResizeToScale(ZoomLevels[2]);
                e.Handled = true;
                break;
            case Key.D4 when e.KeyModifiers.HasFlag(KeyModifiers.Control):
                _currentScaleIndex = 3;
                ResizeToScale(ZoomLevels[3]);
                e.Handled = true;
                break;
            // Zoom in/out step (Ctrl+= and Ctrl+-)
            case Key.OemPlus when e.KeyModifiers.HasFlag(KeyModifiers.Control):
            case Key.Add when e.KeyModifiers.HasFlag(KeyModifiers.Control):
                ZoomIn();
                e.Handled = true;
                break;
            case Key.OemMinus when e.KeyModifiers.HasFlag(KeyModifiers.Control):
            case Key.Subtract when e.KeyModifiers.HasFlag(KeyModifiers.Control):
                ZoomOut();
                e.Handled = true;
                break;
            // Quit
            case Key.Q when e.KeyModifiers.HasFlag(KeyModifiers.Control):
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
        try
        {
            await using var stream = await file.OpenReadAsync();
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            var raw = ms.ToArray();

            var logLines = new List<string>();
            var stacks = ContainerPipeline.UnwrapMultiple(raw, msg => logLines.Add(msg));

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
        string fileName, List<(string Name, byte[] Data)> stacks)
    {
        byte[] data;
        string? stackName = null;

        if (stacks.Count > 1)
        {
            var names = stacks.Select(s => s.Name).ToList();
            var picker = new StackPickerWindow(names);
            var result = await picker.ShowDialog<int?>(this);
            int selectedIndex = result ?? -1;

            if (selectedIndex < 0 || selectedIndex >= stacks.Count)
            {
                _viewModel.StatusText = "Stack selection cancelled.";
                return;
            }

            data = stacks[selectedIndex].Data;
            stackName = stacks[selectedIndex].Name;
        }
        else
        {
            data = stacks[0].Data;
        }

        _viewModel.LoadStack(data, fileName, stackName);
    }

    // ── Menu event handlers ────────────────────────────────────────────────────

    private void OnMenuOpen(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => _ = OpenFileAsync();

    private void OnMenuSwitchStack(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => _ = SwitchStackAsync();

    private void OnMenuToggleRenderMode(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => _viewModel.ToggleRenderMode();

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
        => _ = ShowHelpAsync();

    private void OnMenuQuit(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => Close();
}
