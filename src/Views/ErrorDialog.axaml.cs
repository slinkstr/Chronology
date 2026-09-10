using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Chronology.Views;

public partial class ErrorDialog : Window
{
    public ErrorDialog()
    {
        InitializeComponent();
    }

    public ErrorDialog(string message) : this()
    {
        MessageText.Text = message;
    }

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
