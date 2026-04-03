using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using HyperCardSharp.App.Controls;

namespace HyperCardSharp.App.Views;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;

    // Callback invoked when the user presses OK so the main window can apply any
    // settings that require live state changes (FontMapper directory, render mode).
    public Action<AppSettings>? SettingsApplied { get; set; }

    public bool ColorMode
    {
        set
        {
            var tb = this.FindControl<System7TitleBar>("TitleBar");
            if (tb != null) tb.ColorMode = value;
        }
    }

    public SettingsWindow()
    {
        _settings = AppSettings.Load();
        InitializeComponent();
        BindToSettings(_settings);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void BindToSettings(AppSettings s)
    {
        var fontDirBox    = this.FindControl<TextBox>("FontDirBox");
        var diskImageBox  = this.FindControl<TextBox>("DiskImageBox");
        var colorModeCombo = this.FindControl<ComboBox>("ColorModeCombo");

        if (fontDirBox    != null) fontDirBox.Text    = s.UserFontDirectory ?? string.Empty;
        if (diskImageBox  != null) diskImageBox.Text  = s.SystemDiskImagePath ?? string.Empty;
        if (colorModeCombo != null) colorModeCombo.SelectedIndex = s.UseColorMode ? 1 : 0;
    }

    private AppSettings ReadFromDialog()
    {
        var fontDirBox    = this.FindControl<TextBox>("FontDirBox");
        var diskImageBox  = this.FindControl<TextBox>("DiskImageBox");
        var colorModeCombo = this.FindControl<ComboBox>("ColorModeCombo");

        return new AppSettings
        {
            UserFontDirectory   = string.IsNullOrWhiteSpace(fontDirBox?.Text)   ? null : fontDirBox!.Text.Trim(),
            SystemDiskImagePath = string.IsNullOrWhiteSpace(diskImageBox?.Text) ? null : diskImageBox!.Text.Trim(),
            UseColorMode        = colorModeCombo?.SelectedIndex == 1,
        };
    }

    // ── Title bar ─────────────────────────────────────────────────────────────

    private void OnTitleBarClose(object? sender, EventArgs e) => Close();

    // ── Fonts section ─────────────────────────────────────────────────────────

    private async void OnChooseFontDir(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Font Folder",
            AllowMultiple = false,
        });

        if (folders.Count == 0) return;
        var path = folders[0].TryGetLocalPath();
        if (string.IsNullOrEmpty(path)) return;

        var box = this.FindControl<TextBox>("FontDirBox");
        if (box != null) box.Text = path;
    }

    private void OnResetFontDir(object? sender, RoutedEventArgs e)
    {
        var box = this.FindControl<TextBox>("FontDirBox");
        if (box != null) box.Text = string.Empty;
    }

    // ── Disk image section ────────────────────────────────────────────────────

    private async void OnChooseDiskImage(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Mac HFS Disk Image",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Disk Images") { Patterns = new[] { "*.img", "*.dsk", "*.image" } },
                new FilePickerFileType("All Files")   { Patterns = new[] { "*.*" } },
            },
        });

        if (files.Count == 0) return;
        var path = files[0].TryGetLocalPath();
        if (string.IsNullOrEmpty(path)) return;

        var box = this.FindControl<TextBox>("DiskImageBox");
        if (box != null) box.Text = path;
    }

    private void OnClearDiskImage(object? sender, RoutedEventArgs e)
    {
        var box = this.FindControl<TextBox>("DiskImageBox");
        if (box != null) box.Text = string.Empty;
    }

    // ── Dialog buttons ────────────────────────────────────────────────────────

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close();

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        var updated = ReadFromDialog();
        updated.Save();
        SettingsApplied?.Invoke(updated);
        Close();
    }
}
