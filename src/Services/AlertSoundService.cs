using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Chronology.Services;

public sealed class AlertSoundService : IAlertSoundService
{
    private const uint SndAlias                   = 0x00010000;
    private const uint SndFilename                = 0x00020000;
    private const int  RepeatGapMilliseconds      = 500;
    private const int  ProcessTimeoutMilliseconds = 10000;

    public async Task<bool> PlayAsync(string? soundFile, int count, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        for (var i = 0; i < count; i++)
        {
            if (!await PlayOnceAsync(soundFile, cancellationToken))
            {
                return false;
            }
            if (i + 1 < count)
            {
                await Task.Delay(RepeatGapMilliseconds, cancellationToken);
            }
        }
        return true;
    }

    private static async Task<bool> PlayOnceAsync(string? soundFile, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(soundFile) && File.Exists(soundFile) && await PlayFileAsync(soundFile, cancellationToken))
        {
            return true;
        }
        return await PlaySystemSoundAsync(cancellationToken);
    }

    private static async Task<bool> PlayFileAsync(string path, CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows())
        {
            return await PlayWindowsFileAsync(path, cancellationToken);
        }
        if (OperatingSystem.IsMacOS())
        {
            return await TryStartAsync("/usr/bin/afplay", cancellationToken, path);
        }
        if (OperatingSystem.IsLinux())
        {
            return await PlayLinuxFileAsync(path, cancellationToken);
        }
        return false;
    }

    private static async Task<bool> PlayLinuxFileAsync(string path, CancellationToken cancellationToken)
    {
        return await TryStartAsync("pw-play", cancellationToken, path)
            || await TryStartAsync("paplay", cancellationToken, path)
            || await TryStartAsync("canberra-gtk-play", cancellationToken, "--file", path)
            || await TryStartAsync("aplay", cancellationToken, path);
    }

    private static async Task<bool> PlayLinuxSystemSoundAsync(CancellationToken cancellationToken)
    {
        if (await TryStartAsync("canberra-gtk-play", cancellationToken, "--id", "bell", "--description", "Chronology reminder"))
        {
            return true;
        }

        string[] soundFiles =
        [
            "/usr/share/sounds/freedesktop/stereo/bell.oga",
            "/usr/share/sounds/freedesktop/stereo/dialog-warning.oga",
            "/usr/share/sounds/freedesktop/stereo/alarm-clock-elapsed.oga",
        ];
        foreach (var soundFile in soundFiles)
        {
            if (!File.Exists(soundFile))
            {
                continue;
            }
            if (await TryStartAsync("pw-play", cancellationToken, soundFile)
             || await TryStartAsync("paplay", cancellationToken, soundFile)
             || await TryStartAsync("canberra-gtk-play", cancellationToken, "--file", soundFile))
            {
                return true;
            }
        }
        return false;
    }

    private static bool PlayMacSystemSound()
    {
        NSBeep();
        return true;
    }

    private static async Task<bool> PlaySystemSoundAsync(CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows())
        {
            return await PlayWindowsSystemSoundAsync(cancellationToken);
        }
        if (OperatingSystem.IsMacOS())
        {
            return PlayMacSystemSound();
        }
        if (OperatingSystem.IsLinux())
        {
            return await PlayLinuxSystemSoundAsync(cancellationToken);
        }
        return false;
    }

    private static async Task<bool> PlayWindowsFileAsync(string path, CancellationToken cancellationToken)
    {
        return await Task.Run(() => TryWinmmPlaySound(path, SndFilename), cancellationToken);
    }

    private static async Task<bool> PlayWindowsSystemSoundAsync(CancellationToken cancellationToken)
    {
        return await Task.Run(() => TryWinmmPlaySound("SystemAsterisk", SndAlias), cancellationToken);
    }

    private static async Task<bool> TryStartAsync(string fileName, CancellationToken cancellationToken, params string[] arguments)
    {
        try
        {
            var startInfo = new ProcessStartInfo(fileName)
            {
                UseShellExecute = false,
                CreateNoWindow  = true,
            };
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return false;
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ProcessTimeoutMilliseconds);
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException) { }

                cancellationToken.ThrowIfCancellationRequested();
                return false;
            }
            return process.ExitCode == 0;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or PlatformNotSupportedException or FileNotFoundException)
        {
            return false;
        }
    }

    private static bool TryWinmmPlaySound(string sound, uint flags)
    {
        try
        {
            return PlaySound(sound, IntPtr.Zero, flags);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return false;
        }
    }

    [DllImport("/System/Library/Frameworks/AppKit.framework/AppKit", EntryPoint = "NSBeep")]
    private static extern void NSBeep();

    [DllImport("winmm.dll", EntryPoint = "PlaySoundW", CharSet = CharSet.Unicode)]
    private static extern bool PlaySound(string sound, IntPtr module, uint flags);
}
