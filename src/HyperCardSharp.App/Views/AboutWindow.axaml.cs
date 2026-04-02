using System.Reflection;
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
        SetVersionText();
    }

    private void SetVersionText()
    {
        var vb = this.FindControl<TextBlock>("VersionText");
        if (vb == null) return;
        var asm = Assembly.GetExecutingAssembly();
        var infoVersion = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                       ?? asm.GetName().Version?.ToString()
                       ?? "unknown";
        // Strip build metadata suffix (e.g. "+commit") from informational version
        var plusIdx = infoVersion.IndexOf('+');
        if (plusIdx >= 0) infoVersion = infoVersion[..plusIdx];
        vb.Text = $"Version {infoVersion}";
    }

    private void OnTitleBarClose(object? sender, EventArgs e) => Close();

    private void OnOkClick(object? sender, RoutedEventArgs e) => Close();
}
