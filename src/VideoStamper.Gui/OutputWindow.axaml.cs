using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace VideoStamper.Gui;

public partial class OutputWindow : Window
{
    public OutputWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// Append a line of text to the output box and scroll to the end.
    /// Safe to call from background threads.
    /// </summary>
    public async Task AppendLineAsync(string line)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            // Look up the TextBox by name every time to avoid null fields
            var tb = this.FindControl<TextBox>("OutputTextBox");
            if (tb == null)
            {
                return; // nothing to append to; just bail gracefully
            }

            if (!string.IsNullOrEmpty(tb.Text))
                tb.Text += Environment.NewLine + line;
            else
                tb.Text = line;

            tb.CaretIndex = tb.Text?.Length ?? 0;
        });
    }
}

