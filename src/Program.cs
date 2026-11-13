using System.Security.Cryptography;
using System.Text;
using Avalonia;

namespace Chronology;

sealed class Program
{
    internal static bool SecondInstance;

    private static Mutex? _instanceMutex;

    [STAThread]
    public static void Main(string[] args)
    {
        SecondInstance = !AcquireSingleInstance();

        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static bool AcquireSingleInstance()
    {
        var path = Path.GetFullPath(Directory.GetCurrentDirectory());
        if (OperatingSystem.IsWindows())
        {
            path = path.ToLowerInvariant();
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path)));
        _instanceMutex = new Mutex(true, $"Global\\Chronology-{hash}", out var createdNew);
        return createdNew;
    }
}
