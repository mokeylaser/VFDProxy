namespace VFDProxy.Models;

public enum LogLevel { Info, Warning, Error, Sent, Received, Debug }

public sealed record LogEntry(DateTime Timestamp, LogLevel Level, string Message)
{
    public string FormattedTime => Timestamp.ToString("HH:mm:ss.fff");

    public string LevelTag => Level switch
    {
        LogLevel.Info     => "[INFO]",
        LogLevel.Warning  => "[WARN]",
        LogLevel.Error    => "[ERR ]",
        LogLevel.Sent     => "[>>  ]",
        LogLevel.Received => "[<<  ]",
        LogLevel.Debug    => "[DBG ]",
        _                 => "[    ]"
    };

    public override string ToString() => $"{FormattedTime} {LevelTag} {Message}";

    public static LogEntry Info   (string msg) => new(DateTime.Now, LogLevel.Info,     msg);
    public static LogEntry Warn   (string msg) => new(DateTime.Now, LogLevel.Warning,  msg);
    public static LogEntry Error  (string msg) => new(DateTime.Now, LogLevel.Error,    msg);
    public static LogEntry Sent   (string msg) => new(DateTime.Now, LogLevel.Sent,     msg);
    public static LogEntry Received(string msg)=> new(DateTime.Now, LogLevel.Received, msg);
    public static LogEntry Debug  (string msg) => new(DateTime.Now, LogLevel.Debug,    msg);
}
