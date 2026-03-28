using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using HyperCardSharp.App.Controls;
using HyperCardSharp.Core.Containers;

namespace HyperCardSharp.App.Views;

public partial class StackPickerWindow : Window
{
    private int _selectedIndex = -1;

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
    }

    /// <summary>
    /// The index selected by the user, or -1 if cancelled.
    /// </summary>
    public int SelectedIndex => _selectedIndex;

    private void OnTitleBarClose(object? sender, EventArgs e)
    {
        _selectedIndex = -1;
        Close(-1);
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        _selectedIndex = StackList.SelectedIndex;
        Close(_selectedIndex);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        _selectedIndex = -1;
        Close(-1);
    }

    private void OnListDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (StackList.SelectedIndex >= 0)
        {
            _selectedIndex = StackList.SelectedIndex;
            Close(_selectedIndex);
        }
    }
}
