using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Chronology.ViewModels;

namespace Chronology.Views;

public partial class MainWindow : Window
{
    private DispatcherTimer? _timer;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => (DataContext as MainViewModel)?.Tick();
        _timer.Start();

        Dispatcher.UIThread.Post(() => ActivityField?.Focus());

        // When the File submenu opens via click (not via Alt mnemonic), the
        // default interaction handler selects the first item but does not
        // focus it, so keyboard input is ignored until the user hovers.
        // Explicitly focus the first child when the submenu opens.
        FileMenu.SubmenuOpened += (_, _) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                var first = FileMenu.Items.OfType<MenuItem>().FirstOrDefault();
                first?.Focus();
            });
        };

        // Spacebar activates the focused menu item in addition to Enter.
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && FocusManager?.GetFocusedElement() is MenuItem mi)
        {
            if (mi.HasSubMenu)
            {
                mi.IsSubMenuOpen = !mi.IsSubMenuOpen;
            }
            else
            {
                mi.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            }
            e.Handled = true;
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _timer?.Stop();
    }

    private void OnTimeFieldGotFocus(object? sender, RoutedEventArgs e)
    {
        (DataContext as MainViewModel)?.OnTimeFieldGotFocus();
    }

    private void OnTimeFieldLostFocus(object? sender, RoutedEventArgs e)
    {
        (DataContext as MainViewModel)?.OnTimeFieldLostFocus();
    }

    private async void OnCopyTimelogToClipboard(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
        {
            return;
        }
        var text = vm.GetTimelogText();
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard != null)
        {
            await clipboard.SetTextAsync(text);
        }
    }

    private void OnExit(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
