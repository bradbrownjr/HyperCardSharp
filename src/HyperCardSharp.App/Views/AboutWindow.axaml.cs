using Avalonia.Controls;
using Avalonia.Interactivity;

namespace HyperCardSharp.App.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
    }

    private void OnTitleBarClose(object? sender, EventArgs e) => Close();

    private void OnOkClick(object? sender, RoutedEventArgs e) => Close();
}
