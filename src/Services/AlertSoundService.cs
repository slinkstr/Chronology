using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Chronology.Services;

public sealed class AlertSoundService : IAlertSoundService
{
    private const uint SndAlias = 0x00010000;
    private const uint SndAsync = 0x00000001;
    private const uint SndFilename = 0x00020000;

    public bool Play(string? soundFile)
    {
        if (!string.IsNullOrWhiteSpace(soundFile) && File.Exists(soundFile) && PlayFile(soundFile))
        {
            return true;
        }
        return PlaySystemSound();
    }

    private static bool PlayFile(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return PlayWindowsFile(path);
        }
        if (OperatingSystem.IsMacOS())
        {
            return TryStart("/usr/bin/afplay", path);
        }
        if (OperatingSystem.IsLinux())
        {
            return PlayLinuxFile(path);
        }
        return false;
    }

    private static bool PlayLinuxFile(string path)
    {
        return TryStart("pw-play", path)
            || TryStart("paplay", path)
            || TryStart("canberra-gtk-play", "--file", path)
            || TryStart("aplay", path);
    }

    private static bool PlayLinuxSystemSound()
    {
        if (TryStart("canberra-gtk-play", "--id", "bell", "--description", "Chronology reminder"))
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
            if (TryStart("pw-play", soundFile)
                || TryStart("paplay", soundFile)
                || TryStart("canberra-gtk-play", "--file", soundFile))
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

    private static bool PlaySystemSound()
    {
        if (OperatingSystem.IsWindows())
        {
            return PlayWindowsSystemSound();
        }
        if (OperatingSystem.IsMacOS())
        {
            return PlayMacSystemSound();
        }
        if (OperatingSystem.IsLinux())
        {
            return PlayLinuxSystemSound();
        }
        return false;
    }

    private static bool PlayWindowsFile(string path)
    {
        return TryWinmmPlaySound(path, SndFilename | SndAsync);
    }

    private static bool PlayWindowsSystemSound()
    {
        return TryWinmmPlaySound("SystemAsterisk", SndAlias | SndAsync);
    }

    private static bool TryStart(string fileName, params string[] arguments)
    {
        try
        {
            var startInfo = new ProcessStartInfo(fileName)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }
            var process = Process.Start(startInfo);
            process?.Dispose();
            return process is not null;
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
