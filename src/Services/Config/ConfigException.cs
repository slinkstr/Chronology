namespace Chronology.Services;

public class ConfigException(string message, Exception? innerException = null) : Exception(message, innerException);
