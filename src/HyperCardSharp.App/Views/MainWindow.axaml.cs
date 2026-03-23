using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using HyperCardSharp.App.ViewModels;
using HyperCardSharp.Core.Containers;

namespace HyperCardSharp.App.Views;

/// <summary>
/// View-model for a single row in the stack picker list.
/// </summary>
public class StackListItem
{
    public string Name { get; init; } = "";
    public string Cards { get; init; } = "";
    public string Size { get; init; } = "";
    public string Resolution { get; init; } = "";
    public int Index { get; init; }

    public static StackListItem FromStackData(string name, byte[] data, int index)
    {
        int cardCount = 0;
        short cardW = 0, cardH = 0;

        try
        {
            var span = data.AsSpan();
            int stkBlockSize = BinaryPrimitives.ReadInt32BigEndian(span.Slice(0, 4));
            if (stkBlockSize >= 0x1BC && data.Length >= 0x1BC)
            {
                cardH = BinaryPrimitives.ReadInt16BigEndian(span.Slice(0x1B8, 2));
                cardW = BinaryPrimitives.ReadInt16BigEndian(span.Slice(0x1BA, 2));
            }
            int pos = 0;
            while (pos + 8 <= data.Length)
            {
                int sz = BinaryPrimitives.ReadInt32BigEndian(span.Slice(pos, 4));
                if (sz < 16 || pos + sz > data.Length) break;
                if (data[pos + 4] == 'C' && data[pos + 5] == 'A' &&
                    data[pos + 6] == 'R' && data[pos + 7] == 'D')
                    cardCount++;
                pos += sz;
            }
        }
        catch { }

        string sizeStr = data.Length >= 1024 * 1024
            ? $"{data.Length / (1024.0 * 1024.0):F1} MB"
            : $"{data.Length / 1024}K";
        string resStr = cardW > 0 && cardH > 0 ? $"{cardW}\u00D7{cardH}" : "\u2014";

        return new StackListItem
        {
            Name = name,
            Cards = cardCount > 0 ? cardCount.ToString() : "\u2014",
            Size = sizeStr,
            Resolution = resStr,
            Index = index
        };
    }
}

public partial class MainWindow : Window
{
    private readonly StackViewModel _viewModel = new();

    private const double StatusBarHeight = 28;
    private const double WindowChromeHeight = 32;
    private const double CardPadding = 8;

    private string? _currentOpenFileName;
    private List<(string Name, byte[] Data)>? _currentStacks;
    private TaskCompletionSource<int>? _pickerTcs;

    /// <summary>Platform-aware modifier key label: ⌘ on macOS, Ctrl on others.</summary>
    private static readonly string Mod = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "\u2318" : "Ctrl";
    private static readonly KeyModifiers PlatformMod =
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? KeyModifiers.Meta : KeyModifiers.Control;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        KeyDown += OnKeyDown;
        _viewModel.ShowAnswerDialog += OnShowAnswerDialog;
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

    private bool HasMod(KeyEventArgs e) => e.KeyModifiers.HasFlag(PlatformMod);

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        // ── Help overlay ──
        if (HelpOverlay.IsVisible)
        {
            HelpOverlay.IsVisible = false;
            e.Handled = true;
            return;
        }

        // ── Picker overlay: let arrow keys through for list navigation ──
        if (PickerOverlay.IsVisible)
        {
            if (e.Key == Key.Escape)
            {
                ClosePickerWithResult(-1);
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                ClosePickerWithResult(PickerList.SelectedIndex);
                e.Handled = true;
            }
            // Don't handle Up/Down/Home/End — let them navigate the ListBox
            return;
        }

        // ── Main key handling ──
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
            case Key.O when HasMod(e):
                _ = OpenFileAsync();
                e.Handled = true;
                break;
            case Key.M when HasMod(e):
                _ = SwitchStackAsync();
                e.Handled = true;
                break;
            case Key.H when HasMod(e):
                ShowHelp();
                e.Handled = true;
                break;
            // Zoom presets
            case Key.D1 when HasMod(e):
                ResizeToScale(1.0);
                e.Handled = true;
                break;
            case Key.D2 when HasMod(e):
                ResizeToScale(1.5);
                e.Handled = true;
                break;
            case Key.D3 when HasMod(e):
                ResizeToScale(2.0);
                e.Handled = true;
                break;
            case Key.D4 when HasMod(e):
                ResizeToScale(4.0);
                e.Handled = true;
                break;
            // Zoom +/-
            case Key.OemPlus when HasMod(e):
            case Key.Add when HasMod(e):
                ZoomStep(+1);
                e.Handled = true;
                break;
            case Key.OemMinus when HasMod(e):
            case Key.Subtract when HasMod(e):
                ZoomStep(-1);
                e.Handled = true;
                break;
        }
    }

    // ── Help overlay ────────────────────────────────────────────────────

    private void ShowHelp()
    {
        // Build help text with platform-correct modifier key
        HelpContent.Text =
            $"  {Mod}+O          Open file\n" +
            $"  {Mod}+M          Switch stack\n" +
            $"  {Mod}+H          This help\n" +
            $"\n" +
            $"  {Mod}+1          Zoom 1\u00D7\n" +
            $"  {Mod}+2          Zoom 1.5\u00D7\n" +
            $"  {Mod}+3          Zoom 2\u00D7\n" +
            $"  {Mod}+4          Zoom 4\u00D7\n" +
            $"  {Mod}++          Zoom in\n" +
            $"  {Mod}+\u2212          Zoom out\n" +
            $"\n" +
            $"  \u2190 \u2192            Previous / next card\n" +
            $"  Home / End     First / last card\n" +
            $"  Space          Next card\n" +
            $"\n" +
            $"  Escape         Close dialog";
        HelpOverlay.IsVisible = true;
    }

    private void OnHelpClose(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        HelpOverlay.IsVisible = false;
    }

    // ── Zoom ────────────────────────────────────────────────────────────

    private double _currentScale = 1.0;
    private static readonly double[] ZoomLevels = { 0.5, 0.75, 1.0, 1.5, 2.0, 3.0, 4.0 };

    private void ZoomStep(int direction)
    {
        // Find current position in zoom levels and step
        int idx = 0;
        double minDist = double.MaxValue;
        for (int i = 0; i < ZoomLevels.Length; i++)
        {
            double dist = Math.Abs(ZoomLevels[i] - _currentScale);
            if (dist < minDist) { minDist = dist; idx = i; }
        }
        idx = Math.Clamp(idx + direction, 0, ZoomLevels.Length - 1);
        _currentScale = ZoomLevels[idx];
        ResizeToScale(_currentScale);
    }

    // ── Picker overlay ──────────────────────────────────────────────────

    private Task<int> ShowPickerOverlay(List<(string Name, byte[] Data)> stacks)
    {
        PickerList.Items.Clear();
        for (int i = 0; i < stacks.Count; i++)
        {
            var item = StackListItem.FromStackData(stacks[i].Name, stacks[i].Data, i);
            PickerList.Items.Add(item);
        }

        PickerInfoText.Text = stacks.Count == 1 ? "1 stack" : $"{stacks.Count} stacks";
        if (_currentOpenFileName != null)
            PickerTitleText.Text = _currentOpenFileName;

        if (stacks.Count > 0)
            PickerList.SelectedIndex = 0;

        PickerOverlay.IsVisible = true;
        PickerList.Focus();

        _pickerTcs = new TaskCompletionSource<int>();
        return _pickerTcs.Task;
    }

    private void ClosePickerWithResult(int index)
    {
        PickerOverlay.IsVisible = false;
        _pickerTcs?.TrySetResult(index);
        _pickerTcs = null;
    }

    private void OnPickerOk(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => ClosePickerWithResult(PickerList.SelectedIndex);

    private void OnPickerCancel(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => ClosePickerWithResult(-1);

    private void OnPickerDoubleTap(object? sender, TappedEventArgs e)
    {
        if (PickerList.SelectedIndex >= 0)
            ClosePickerWithResult(PickerList.SelectedIndex);
    }

    // ── Window resize ───────────────────────────────────────────────────

    private void ResizeToScale(double scale)
    {
        _currentScale = scale;
        double cardW = _viewModel.CardWidth * scale;
        double cardH = _viewModel.CardHeight * scale;
        double targetW = cardW + CardPadding;
        double targetH = cardH + CardPadding + StatusBarHeight + WindowChromeHeight;

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

    // ── File open / stack selection ─────────────────────────────────────

    private async Task OpenFileAsync()
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

        if (files.Count == 0) return;

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

            _currentOpenFileName = file.Name;
            _currentStacks = stacks;
            await PickAndLoadStack(file.Name, stacks);
        }
        catch (Exception ex)
        {
            _viewModel.StatusText = $"Error opening \"{file.Name}\": {ex.Message}";
        }
    }

    private async Task SwitchStackAsync()
    {
        if (_currentStacks == null || _currentStacks.Count < 2 || _currentOpenFileName == null)
        {
            _viewModel.StatusText = "No multi-stack file loaded. Ctrl+O to open a file.";
            return;
        }
        await PickAndLoadStack(_currentOpenFileName, _currentStacks);
    }

    private async Task PickAndLoadStack(string fileName, List<(string Name, byte[] Data)> stacks)
    {
        byte[] data;
        string? stackName = null;

        if (stacks.Count > 1)
        {
            int selectedIndex = await ShowPickerOverlay(stacks);
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
}
