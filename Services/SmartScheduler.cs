using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace WallArt.Services;

public class SmartScheduler : IDisposable
{
    private readonly Func<Task> _action;
    private readonly ILogService _logService;
    private readonly CancellationTokenSource _cts = new CancellationTokenSource();
    
    // Configured interval
    private TimeSpan _interval;
    
    // Internal trackers
    private DateTime _nextRunTime;
    private bool _isRunning;
    private Task? _schedulerTask;

    public SmartScheduler(Func<Task> action, TimeSpan interval, ILogService logService)
    {
        _action = action;
        _interval = interval;
        _logService = logService;
        
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.TimeChanged += OnTimeChanged;
    }

    public void Start()
    {
        if (_isRunning) return;
        _isRunning = true;
        _nextRunTime = DateTime.Now.Add(_interval);

        _schedulerTask = Task.Run(RunLoopAsync, _cts.Token);
        _logService.Log($"Scheduler started. Expected next run: {_nextRunTime:HH:mm:ss}");
    }
    
    public void Stop()
    {
        if (!_isRunning) return;
        _isRunning = false;
        _cts.Cancel();
    }
    
    public void UpdateInterval(TimeSpan newInterval)
    {
        _interval = newInterval;
        _nextRunTime = DateTime.Now.Add(_interval);
        _logService.Log($"Interval updated. Next run pushed to: {_nextRunTime:HH:mm:ss}");
    }
    
    public void ManualTriggerFired()
    {
        // When manually fired, push the next scheduled run out by the full interval length so they don't overlap
        _nextRunTime = DateTime.Now.Add(_interval);
        _logService.Log($"Manual trigger run. Next background run pushed into the future: {_nextRunTime:HH:mm:ss}");
    }

    private async Task RunLoopAsync()
    {
        while (!_cts.Token.IsCancellationRequested && _isRunning)
        {
            var now = DateTime.Now;

            // Did we pass the target time?
            if (now >= _nextRunTime)
            {
                _logService.Log("Scheduled time reached. Executing...");
                
                // CRITICAL: Push the NEXT run time forward immediately BEFORE executing. 
                // This prevents a long-running execution from causing an immediate "catch-up" double-fire.
                _nextRunTime = now.Add(_interval);
                
                try 
                {
                    await _action();
                } 
                catch (Exception ex) 
                {
                    _logService.Log($"Background execution failed: {ex.Message}");
                }
            }

            // Sleep locally to prevent CPU burn, but check relatively often (e.g. 10s) 
            // incase SystemEvents missed a wakeup or the time was manually adjusted.
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(10), _cts.Token);
            }
            catch (TaskCanceledException) { break; }
        }
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Resume) return;
        
        _logService.Log("System Wake detected. Resolving missed background scheduling...");
        EvaluateMisfire();
    }

    private void OnTimeChanged(object? sender, EventArgs e)
    {
        _logService.Log("System Time change detected. Re-evaluating schedule...");
        EvaluateMisfire();
    }
    
    private void EvaluateMisfire()
    {
        var now = DateTime.Now;
        if (now >= _nextRunTime && _isRunning)
        {
            _logService.Log($"Detected a missed interval during sleep/downtime. Target was {_nextRunTime:HH:mm:ss}. Firing precisely once to catch up, and adjusting future schedule.");
            
            // Re-anchor the next run completely, obliterating any built-up debt.
            // Say you slept through 3 intervals (11:00, 12:00, 1:00) and woke at 1:30. 
            // It will run exactly once right now, and set the next interval for 2:30.
            _nextRunTime = now.Add(_interval);
            
            _ = Task.Run(async () =>
            {
                try 
                {
                    await _action();
                } 
                catch (Exception ex) 
                {
                    _logService.Log($"Misfire execution failed: {ex.Message}");
                }
            });
        }
    }

    public void Dispose()
    {
        Stop();
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.TimeChanged -= OnTimeChanged;
        _cts.Dispose();
    }
}
