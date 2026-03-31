using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using HyperCardSharp.App.Controls;
using HyperCardSharp.Core.Containers;

namespace HyperCardSharp.App.Views;

public partial class StackPickerWindow : Window
{
    private int _selectedIndex = -1;
    private ScrollViewer? _scrollViewer;
    private bool _updatingScroll;

    public bool ColorMode
    {
        set
        {
            var tb = this.FindControl<System7TitleBar>("TitleBar");
            if (tb != null) tb.ColorMode = value;
        }
    }

    public StackPickerWindow()
    {
        InitializeComponent();
    }

    public StackPickerWindow(IReadOnlyList<StackEntry> entries) : this()
    {
        foreach (var entry in entries)
            StackList.Items.Add(entry);

        if (entries.Count > 0)
            StackList.SelectedIndex = 0;

        InfoText.Text = entries.Count == 1
            ? "1 stack"
            : $"{entries.Count} stacks";

        StackList.DoubleTapped += OnListDoubleTapped;

        // Wire our custom scrollbar once the ListBox template is applied
        StackList.Loaded += (_, _) => WireScrollBar();
    }

    /// <summary>
    /// The index selected by the user, or -1 if cancelled.
    /// </summary>
    public int SelectedIndex => _selectedIndex;

    // ── Custom scrollbar wiring ────────────────────────────────────────────

    private void WireScrollBar()
    {
        _scrollViewer = StackList.FindDescendantOfType<ScrollViewer>();
        if (_scrollViewer == null) return;

        _scrollViewer.PropertyChanged += OnScrollViewerPropertyChanged;
        ListScrollBar.ValueChanged += OnScrollBarValueChanged;
        SyncScrollBarProperties();
    }

    private void OnScrollViewerPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == ScrollViewer.OffsetProperty ||
            e.Property == ScrollViewer.ExtentProperty ||
            e.Property == ScrollViewer.ViewportProperty)
        {
            SyncScrollBarProperties();
        }
    }

    private void SyncScrollBarProperties()
    {
        if (_scrollViewer == null || _updatingScroll) return;
        _updatingScroll = true;

        double extent   = _scrollViewer.Extent.Height;
        double viewport = _scrollViewer.Viewport.Height;
        double maxScroll = Math.Max(0, extent - viewport);

        ListScrollBar.Minimum      = 0;
        ListScrollBar.Maximum      = maxScroll;
        ListScrollBar.ViewportSize = viewport;
        ListScrollBar.Value        = _scrollViewer.Offset.Y;
        ListScrollBar.SmallChange  = 16;  // one row
        ListScrollBar.LargeChange  = Math.Max(16, viewport - 16);

        _updatingScroll = false;
    }

    private void OnScrollBarValueChanged(object? sender, double newValue)
    {
        if (_scrollViewer == null || _updatingScroll) return;
        _updatingScroll = true;
        _scrollViewer.Offset = new Vector(_scrollViewer.Offset.X, newValue);
        _updatingScroll = false;
    }

    // ── Event handlers ─────────────────────────────────────────────────────

    private void OnTitleBarClose(object? sender, EventArgs e)
    {
        _selectedIndex = -1;
        Close();
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        _selectedIndex = StackList.SelectedIndex;
        Close();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        _selectedIndex = -1;
        Close();
    }

    private void OnListDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (StackList.SelectedIndex >= 0)
        {
            _selectedIndex = StackList.SelectedIndex;
            Close();
        }
    }
}
