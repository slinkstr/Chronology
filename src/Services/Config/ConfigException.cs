namespace Chronology.Services.Config;

public class ConfigException(string message, Exception? innerException = null) : Exception(message, innerException);
