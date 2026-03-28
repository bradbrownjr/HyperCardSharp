using Avalonia.Controls;
using Avalonia.Interactivity;
using HyperCardSharp.App.Controls;

namespace HyperCardSharp.App.Views;

public partial class AboutWindow : Window
{
    public bool ColorMode
    {
        set
        {
            var tb = this.FindControl<System7TitleBar>("TitleBar");
            if (tb != null) tb.ColorMode = value;
        }
    }

    public AboutWindow()
    {
        InitializeComponent();
    }

    private void OnTitleBarClose(object? sender, EventArgs e) => Close();

    private void OnOkClick(object? sender, RoutedEventArgs e) => Close();
}
