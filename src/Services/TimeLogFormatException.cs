namespace Chronology.Services;

public class TimeLogFormatException : Exception
{
    public string FilePath { get; }
    public int LineNumber { get; }

    public TimeLogFormatException(string filePath, int lineNumber, string message)
        : base($"Malformed day file {filePath} at line {lineNumber}: {message}")
    {
        FilePath = filePath;
        LineNumber = lineNumber;
    }
}
