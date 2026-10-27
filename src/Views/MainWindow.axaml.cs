using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Reactive;
using Avalonia.Threading;
using Chronology.Services.TimeLogStores;
using Chronology.ViewModels;

namespace Chronology.Views;

public partial class MainWindow : Window
{
    private DispatcherTimer? _timer;
    private IDisposable[]    _inputSubscriptions = [];
    private IInputElement?   _lastFocusedElement;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closed += OnClosed;
        Activated += OnActivated;
    }

    private void OnLoaded(object? sender, EventArgs args)
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

        Dispatcher.UIThread.Post(() =>
        {
            if (FocusManager.GetFocusedElement() is null)
            {
                ActivityField?.Focus();
            }
        });


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
        AddHandler(KeyUpEvent, OnAltKeyUp, RoutingStrategies.Bubble, handledEventsToo: true);
        Deactivated += (_, _) => SetValue(AccessText.ShowAccessKeyProperty, false);
        AddHandler(GotFocusEvent, OnGotFocusTrack, RoutingStrategies.Bubble);
        MenuBar.AddHandler(KeyDownEvent, OnMenuBarKeyDown, RoutingStrategies.Tunnel);
        MenuBar.AddHandler(KeyDownEvent, OnMenuBarSpaceKeyDown, RoutingStrategies.Tunnel);
        MenuBar.AddHandler(LostFocusEvent, OnMenuBarLostFocus, RoutingStrategies.Bubble);

        foreach (var item in MenuBar.Items.OfType<MenuItem>())
        {
            item.PropertyChanged += (_, e) =>
            {
                if (e.Property == MenuItem.IsSubMenuOpenProperty && !item.IsSubMenuOpen)
                {
                    KeyboardNavigation.SetTabOnceActiveElement(item, null);
                    KeyboardNavigation.SetTabOnceActiveElement(MenuBar, item);
                }
            };
        }

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
        if (e.KeyModifiers == KeyModifiers.Alt && e.Key is Key.Left or Key.Right && DataContext is MainViewModel vm)
        {
            // Intentionally not marked as handled. The framework must see a key pressed while Alt is held, or it treats the Alt release as a bare tap and opens the menu.
            (e.Key == Key.Left ? vm.GoBackCommand : vm.GoForwardCommand).Execute(null);
        }
    }

    private void OnAltKeyUp(object? sender, KeyEventArgs e)
    {
        // Mnemonic underlines only clear on menu close or pointer press
        if (e.Key is Key.LeftAlt or Key.RightAlt && !MenuBar.IsOpen)
        {
            SetValue(AccessText.ShowAccessKeyProperty, false);
        }
    }

    private void OnGotFocusTrack(object? sender, FocusChangedEventArgs e)
    {
        if (e.Source is IInputElement el and not MenuItem)
        {
            _lastFocusedElement = el;
        }
    }

    private void OnMenuBarLostFocus(object? sender, FocusChangedEventArgs e)
    {
        if (e.NewFocusedElement is not ILogical logical || logical.FindLogicalAncestorOfType<Menu>() != MenuBar)
        {
            MenuBar.SelectedIndex = -1;
        }
    }

    private void OnMenuBarKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Tab)
        {
            return;
        }
        e.Handled = true;
        MenuBar.Close();
        var direction = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? NavigationDirection.Previous : NavigationDirection.Next;
        var anchor = e.Source as IInputElement ?? this;
        while (anchor is MenuItem { IsTopLevel: false } item && item.Parent is IInputElement parent)
        {
            anchor = parent;
        }
        for (var i = 0; i < 16; i++)
        {
            var options = new FindNextElementOptions { FocusedElement = anchor };
            var next = FocusManager.FindNextElement(direction, options);
            if (next is null || next == anchor)
            {
                break;
            }
            if (next is InputElement { Focusable: true, IsEffectivelyEnabled: true } el && el.Focus(NavigationMethod.Tab))
            {
                break;
            }
            anchor = next;
        }
    }

    private void OnMenuBarSpaceKeyDown(object? sender, KeyEventArgs e)
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

    private void OnActivated(object? sender, EventArgs e)
    {
        var focused = FocusManager.GetFocusedElement() ?? _lastFocusedElement;
        focused?.Focus(NavigationMethod.Tab);
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
