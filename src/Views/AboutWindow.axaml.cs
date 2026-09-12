using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Chronology.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        TitleText.Text = $"Chronology {version?.ToString(3) ?? "unknown"}";

        var commitDate = BuildInfo.GitCommitDate.Trim();
        if (DateTimeOffset.TryParse(commitDate, out var cd))
        {
            commitDate = cd.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
        }

        var buildDate = BuildInfo.BuildDateUtc.Trim();
        if (DateTimeOffset.TryParse(buildDate, out var bd))
        {
            buildDate = bd.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
        }

        var dotnet = RuntimeInformation.FrameworkDescription;

        InfoText.Text = $"Commit: {commitDate} {BuildInfo.GitCommitHash.Trim()}\n"
                      + $"Built:  {buildDate}\n"
                      + $"Dotnet: {dotnet}";
    }

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
