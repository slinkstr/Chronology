using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Chronology.Services.Config;
using Chronology.ViewModels;
using Chronology.Views;

namespace Chronology;

// ReSharper disable once PartialTypeWithSinglePart
public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            try
            {
                desktop.MainWindow = new MainWindow
                {
                    DataContext = new MainViewModel(),
                };
            }
            catch (ConfigException ex)
            {
                desktop.MainWindow = new ErrorDialog($"Failed to load config at {Config.FilePath}:\n{ex.Message}");
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
