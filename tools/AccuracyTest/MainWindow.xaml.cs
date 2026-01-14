using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;

namespace AccuracyTest;

public partial class MainWindow : Window
{
    private DispatcherTimer? _timer;
    private Stopwatch? _stopwatch;
    private int _targetSeconds;
    private bool _isRunning;

    public MainWindow()
    {
        InitializeComponent();
    }

    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isRunning)
        {
            StopTest();
            return;
        }

        if (!int.TryParse(DurationInput.Text, out _targetSeconds) || _targetSeconds <= 0)
        {
            MessageBox.Show("Please enter a valid duration in seconds.", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        StartTest();
    }

    private void StartTest()
    {
        _isRunning = true;
        StartButton.Content = "Stop Test";
        DurationInput.IsEnabled = false;
        
        StatusText.Text = $"Test in progress - keep this window focused!";
        ResultText.Text = "";
        
        _stopwatch = Stopwatch.StartNew();
        
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _timer.Tick += Timer_Tick;
        _timer.Start();
        
        // Ensure window stays focused
        Activate();
        Focus();
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (_stopwatch is null) return;
        
        var elapsed = _stopwatch.Elapsed;
        var remaining = TimeSpan.FromSeconds(_targetSeconds) - elapsed;
        
        if (remaining <= TimeSpan.Zero)
        {
            CompleteTest();
            return;
        }
        
        TimerText.Text = $"{remaining:mm\\:ss\\.f}";
    }

    private void CompleteTest()
    {
        StopTest();
        
        var actualElapsed = _stopwatch?.Elapsed ?? TimeSpan.Zero;
        
        TimerText.Text = "00:00.0";
        StatusText.Text = "Test Complete!";
        ResultText.Text = $"✓ Test completed. Actual elapsed time: {actualElapsed.TotalSeconds:F1} seconds\n\n" +
                          $"Now check the TimeRecordingAgent History window:\n" +
                          $"• Look for entry: \"Time Recording Accuracy Test\"\n" +
                          $"• Expected duration: ~{_targetSeconds} seconds (±10s for polling)\n" +
                          $"• If duration is significantly less, there may be a timing issue.";
    }

    private void StopTest()
    {
        _timer?.Stop();
        _timer = null;
        _stopwatch?.Stop();
        
        _isRunning = false;
        StartButton.Content = "Start Test";
        DurationInput.IsEnabled = true;
    }
}
