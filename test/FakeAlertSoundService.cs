using Chronology.Services;

namespace Chronology.Tests;

internal sealed class FakeAlertSoundService : IAlertSoundService
{
    public List<int>                   Counts        { get; } = [];
    public Exception?                  Failure       { get; set; }
    public CancellationToken           LastToken     { get; private set; }
    public TaskCompletionSource<bool>? PendingResult { get; set; }
    public List<string?>               PlayedFiles   { get; } = [];
    public bool                        Result        { get; set; } = true;

    public Task<bool> PlayAsync(string? soundFile, int count, CancellationToken cancellationToken)
    {
        PlayedFiles.Add(soundFile);
        Counts.Add(count);
        LastToken = cancellationToken;
        if (Failure is not null)
        {
            return Task.FromException<bool>(Failure);
        }
        if (PendingResult is not null)
        {
            return PendingResult.Task;
        }
        return Task.FromResult(Result);
    }
}
