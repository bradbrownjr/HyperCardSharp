using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace HyperCardSharp.App.Views;

public partial class StackPickerWindow : Window
{
    private int _selectedIndex = -1;

    public StackPickerWindow()
    {
        InitializeComponent();
    }

    public StackPickerWindow(IReadOnlyList<string> stackNames) : this()
    {
        foreach (var name in stackNames)
            StackList.Items.Add(name);

        if (stackNames.Count > 0)
            StackList.SelectedIndex = 0;

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
