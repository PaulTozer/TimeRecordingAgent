using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using TimeRecordingAgent.App.Tray;
using TimeRecordingAgent.Core.Services;
using TimeRecordingAgent.Core.Storage;

namespace TimeRecordingAgent.App;

public partial class App : System.Windows.Application
{
    private ILoggerFactory? _loggerFactory;
    private ILogger<App>? _appLogger;
    private RecordingCoordinator? _coordinator;
    private AiClassificationService? _aiService;
    private TrayIconManager? _tray;

    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var logDir = Path.Combine(AppContext.BaseDirectory, "logs");
        Directory.CreateDirectory(logDir);
        var logFilePath = Path.Combine(logDir, $"trace-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        
        _loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Trace);
            builder.AddProvider(new FileLoggerProvider(logFilePath));
        });

        _appLogger = _loggerFactory.CreateLogger<App>();
        _appLogger.LogInformation("Starting Time Recording Agent");

        var screenState = new ScreenStateMonitor(_loggerFactory.CreateLogger<ScreenStateMonitor>());
        var outlookReader = new OutlookContextReader(_loggerFactory.CreateLogger<OutlookContextReader>());
        var screenCapture = new ScreenCaptureService(_loggerFactory.CreateLogger<ScreenCaptureService>());
        var poller = new ForegroundWindowPoller(
            TimeSpan.FromSeconds(5),
            screenState,
            outlookReader,
            _loggerFactory.CreateLogger<ForegroundWindowPoller>());

        var dataPath = Path.Combine(AppContext.BaseDirectory, "data", "time-tracking.db");
        var store = new SqliteTimeStore(dataPath, _loggerFactory.CreateLogger<SqliteTimeStore>());
        _coordinator = new RecordingCoordinator(poller, store, _loggerFactory.CreateLogger<RecordingCoordinator>());

        // Initialize AI classification service (configuration is loaded by TrayIconManager from settings.json)
        _aiService = new AiClassificationService(_loggerFactory.CreateLogger<AiClassificationService>());
        _appLogger.LogInformation("AI classification service created. Configuration will be loaded from settings.json");

        _tray = new TrayIconManager(_coordinator, _aiService, outlookReader, screenCapture, _loggerFactory.CreateLogger<TrayIconManager>(), dataPath);
        _tray.Initialize();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _coordinator?.Dispose();
        _loggerFactory?.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogUnhandledException("Dispatcher", e.Exception);
    }

    private void OnDomainUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            LogUnhandledException("AppDomain", ex);
        }
    }

    private void LogUnhandledException(string source, Exception exception)
    {
        var message = $"[{source}] {exception}";
        _appLogger?.LogError(exception, "Unhandled exception from {Source}", source);
        Console.Error.WriteLine(message);
        try
        {
            var logDir = Path.Combine(AppContext.BaseDirectory, "logs");
            Directory.CreateDirectory(logDir);
            var logPath = Path.Combine(logDir, "unhandled.log");
            File.AppendAllText(logPath, $"{DateTime.Now:O} {message}{Environment.NewLine}{exception.StackTrace}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // ignore logging failures
        }
    }
}
