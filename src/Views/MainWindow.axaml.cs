using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Reactive;
using Avalonia.Threading;
using Chronology.Services.TimeLogStores;
using Chronology.ViewModels;

namespace Chronology.Views;

public partial class MainWindow : Window
{
    private DispatcherTimer? _timer;
    private IDisposable[]    _inputSubscriptions = [];

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        _timer?.Stop();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => (DataContext as MainViewModel)?.Tick();
        _timer.Start();

        if (DataContext is MainViewModel vm)
        {
            vm.ErrorOccurred += OnErrorOccurred;
            vm.FlushPendingErrors();
        }

        Dispatcher.UIThread.Post(() => ActivityField?.Focus());


        // When opening menu via click the first item is selected but not focused, causing it to ignore keyboard input.
        FileMenu.SubmenuOpened += (_, _) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                var first = FileMenu.Items.OfType<MenuItem>().FirstOrDefault();
                first?.Focus();
            });
        };

        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);

        // RoutedEvent.Raised counts interaction inside dialogs.
        foreach (var s in _inputSubscriptions) { s.Dispose(); }
        _inputSubscriptions =
        [
            KeyDownEvent.Raised.Subscribe(new AnonymousObserver<(object, RoutedEventArgs)>(OnAnyInput)),
            PointerPressedEvent.Raised.Subscribe(new AnonymousObserver<(object, RoutedEventArgs)>(OnAnyInput)),
            PointerWheelChangedEvent.Raised.Subscribe(new AnonymousObserver<(object, RoutedEventArgs)>(OnAnyInput)),
        ];
    }

    private void OnAnyInput((object, RoutedEventArgs) e)
    {
        (DataContext as MainViewModel)?.NotifyInteraction();
    }

    private async void OnErrorOccurred(string title, string message)
    {
        var dialog = new ErrorDialog(message);
        await dialog.ShowDialog(this);
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space || FocusManager.GetFocusedElement() is not MenuItem mi) { return; }

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

    private void OnClosed(object? sender, EventArgs e)
    {
        _timer?.Stop();
        foreach (var s in _inputSubscriptions) { s.Dispose(); }
        (DataContext as MainViewModel)?.NotifyClosing();
    }

    private void OnLogFieldKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is AutoCompleteBox { IsDropDownOpen: true })
        {
            return;
        }
        if (DataContext is MainViewModel vm && vm.LogCommand.CanExecute(null))
        {
            vm.LogCommand.Execute(null);
            ActivityField?.Focus();
            e.Handled = true;
        }
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

    private async void OnMarkSpecialDay(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        var dialog = new MarkSpecialDayWindow(new MarkSpecialDayViewModel(vm.ValidateActivity, vm.Today));
        var result = await dialog.ShowDialog<MarkSpecialDayResult?>(this);
        if (result is null)
        {
            return;
        }

        await vm.MarkSpecialDayAsync(result.Date, result.Activity, result.Duration, count => ConfirmAppendAsync(result, count));
    }

    private async Task<bool> ConfirmAppendAsync(MarkSpecialDayResult mark, int existingCount)
    {
        var entries = existingCount == 1 ? "entry" : "entries";
        var message = $"The log for {mark.Date:yyyy-MM-dd} already contains {existingCount} {entries}.\nAppend {mark.Activity} ({DurationText.Format(mark.Duration, DurationFormat.Hhmm)})?";
        var dialog = new ConfirmDialog("Append to existing day", message, confirmText: "Append", cancelText: "Cancel");
        return await dialog.ShowDialog<bool>(this);
    }

    private void OnExit(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void OnAbout(object? sender, RoutedEventArgs e)
    {
        var about = new AboutWindow();
        await about.ShowDialog(this);
    }
}
