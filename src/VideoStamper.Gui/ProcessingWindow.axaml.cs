using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace VideoStamper.Gui;

public partial class ProcessingWindow : Window
{
    public event EventHandler? CancelRequested;

    public ProcessingWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void Cancel_OnClick(object? sender, RoutedEventArgs e)
        => CancelRequested?.Invoke(this, EventArgs.Empty);

    public void SetStatus(string text)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var tb = this.FindControl<TextBlock>("StatusText");
            if (tb != null) tb.Text = text;
        });
    }

    public void AppendLine(string line)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var box = this.FindControl<TextBox>("LogTextBox");
            if (box == null) return;

            if (!string.IsNullOrEmpty(box.Text))
                box.Text += Environment.NewLine;

            box.Text += line;

            // keep it scrolled to bottom
            box.CaretIndex = box.Text.Length;
        });
    }
}

