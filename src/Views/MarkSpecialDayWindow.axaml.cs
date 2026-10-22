using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Chronology.ViewModels;

namespace Chronology.Views;

public partial class MarkSpecialDayWindow : Window
{
    public MarkSpecialDayWindow()
    {
        InitializeComponent();
    }

    public MarkSpecialDayWindow(MarkSpecialDayViewModel viewModel) : this()
    {
        DataContext = viewModel;
        Opened += (_, _) => TypeField.Focus();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close(null);
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }

    private void Confirm()
    {
        if (DataContext is MarkSpecialDayViewModel vm && vm.CanConfirm)
        {
            Close(vm.BuildResult());
        }
    }

    private void OnFieldKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is ComboBox { IsDropDownOpen: true })
        {
            return;
        }
        Confirm();
        e.Handled = true;
    }

    private void OnMark(object? sender, RoutedEventArgs e)
    {
        Confirm();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}
