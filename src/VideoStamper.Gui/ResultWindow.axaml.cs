using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Controls.ApplicationLifetimes;

namespace VideoStamper.Gui;

public partial class ResultWindow : Window
{
    public ResultWindow()
    {
        InitializeComponent();
    }

    public ResultWindow(string message)
    {
        InitializeComponent();

        // Look up the TextBlock by name instead of using the generated field
        var tb = this.FindControl<TextBlock>("MessageTextBlock");
        if (tb != null)
        {
            tb.Text = message;
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OkButton_OnClick(object? sender, RoutedEventArgs e)
    {
        // Close this window…
        Close();

        // …and shut down the whole desktop app if possible.
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
        {
            lifetime.Shutdown();
        }
    }
}

