namespace Chronology.Services;

public interface IFileBackedStore : ITimeLogStore
{
    string Contents(DateOnly date);
    void   OpenCurrentDayFile(DateOnly date);
    void   OpenDataDirectory();
}
