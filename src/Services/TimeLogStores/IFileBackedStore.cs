namespace Chronology.Services.TimeLogStores;

public interface IFileBackedStore : ITimeLogStore
{
    string DataDirectory { get; }

    string Contents(DateOnly date);
    string GetFilePath(DateOnly date);
    void   OpenCurrentDayFile(DateOnly date);
    void   OpenDataDirectory();
}
