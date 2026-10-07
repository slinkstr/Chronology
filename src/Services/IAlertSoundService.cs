namespace Chronology.Services;

public interface IAlertSoundService
{
    Task<bool> PlayAsync(string? soundFile, int count, CancellationToken cancellationToken);
}
