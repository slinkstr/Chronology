using Chronology.Services;

namespace Chronology.Tests;

internal sealed class FakeAlertSoundService : IAlertSoundService
{
    public List<string?> PlayedFiles { get; } = [];

    public bool Play(string? soundFile)
    {
        PlayedFiles.Add(soundFile);
        return true;
    }
}
