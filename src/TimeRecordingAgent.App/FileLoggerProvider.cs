using System;
using System.Collections.Concurrent;
using System.IO;
using Microsoft.Extensions.Logging;

namespace TimeRecordingAgent.App;

public class FileLoggerProvider : ILoggerProvider
{
    private readonly string _path;
    private readonly object _lock = new();
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new();

    public FileLoggerProvider(string path)
    {
        _path = path;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return _loggers.GetOrAdd(categoryName, name => new FileLogger(name, _path, _lock));
    }

    public void Dispose()
    {
        _loggers.Clear();
    }
}

public class FileLogger : ILogger
{
    private readonly string _category;
    private readonly string _path;
    private readonly object _lock;

    public FileLogger(string category, string path, object lockObj)
    {
        _category = category;
        _path = path;
        _lock = lockObj;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        var message = formatter(state, exception);
        var shortCategory = _category.Length > 30 ? _category.Substring(_category.Length - 30) : _category;
        var logLine = $"{DateTime.Now:HH:mm:ss.fff} [{logLevel,-5}] [{shortCategory}] {message}";
        if (exception != null)
        {
            logLine += Environment.NewLine + exception.ToString();
        }

        lock (_lock)
        {
            File.AppendAllText(_path, logLine + Environment.NewLine);
        }
    }
}
