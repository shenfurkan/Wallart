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
    public DateTime NextRunTime => _nextRunTime;
    private DateTime _nextRunTime;
    private int _started; // 0 = not started, 1 = started (Interlocked)
    private Task? _schedulerTask;

    public SmartScheduler(Func<Task> action, TimeSpan interval, ILogService logService)
    {
        _action = action;
        _interval = interval;
        _logService = logService;
        
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.TimeChanged += OnTimeChanged;
    }

    private DateTime CalculateNextRun(DateTime fromTime)
    {
        if (_interval == TimeSpan.FromTicks(-1)) // -1 ticks means Midnight
        {
            return fromTime.Date.AddDays(1);
        }
        return fromTime.Add(_interval);
    }

    public void Start()
    {
        // Only allow one start; harmless if called again
        if (Interlocked.CompareExchange(ref _started, 1, 0) != 0) return;
        _nextRunTime = CalculateNextRun(DateTime.Now);

        _schedulerTask = Task.Run(RunLoopAsync, _cts.Token);
        _logService.Log($"Scheduler started. Expected next run: {_nextRunTime:HH:mm:ss}");
    }
    
    public void Stop()
    {
        _cts.Cancel();
    }
    
    public void UpdateInterval(TimeSpan newInterval)
    {
        _interval = newInterval;
        _nextRunTime = CalculateNextRun(DateTime.Now);
        _logService.Log($"Interval updated. Next run pushed to: {_nextRunTime:HH:mm:ss}");
    }
    
    public void ManualTriggerFired()
    {
        // When manually fired, push the next scheduled run out by the full interval length so they don't overlap
        _nextRunTime = CalculateNextRun(DateTime.Now);
        _logService.Log($"Manual trigger run. Next background run pushed into the future: {_nextRunTime:HH:mm:ss}");
    }

    public void ScheduleTemporaryRetry(TimeSpan delay)
    {
        var retryTime = DateTime.Now.Add(delay);
        if (retryTime < _nextRunTime)
        {
            _nextRunTime = retryTime;
            _logService.Log($"Scheduled temporary retry for: {_nextRunTime:HH:mm:ss}");
        }
    }

    private async Task RunLoopAsync()
    {
        while (!_cts.Token.IsCancellationRequested)
        {
            var now = DateTime.Now;

            if (now >= _nextRunTime)
            {
                _logService.Log("Scheduled time reached. Executing...");
                // Push next run time BEFORE executing to prevent double-fire on slow execution.
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

            // Sleep until next run (or at most 1 minute) so we don't burn CPU on a 10 s poll.
            // Capping at 1 min keeps us responsive to UpdateInterval changes via UpdateInterval().
            var timeUntilNext = _nextRunTime - DateTime.Now;
            var sleepDuration = timeUntilNext > TimeSpan.Zero
                ? TimeSpan.FromTicks(Math.Min(timeUntilNext.Ticks, TimeSpan.FromMinutes(1).Ticks))
                : TimeSpan.FromSeconds(5);
            try
            {
                await Task.Delay(sleepDuration, _cts.Token);
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
        if (now >= _nextRunTime && !_cts.IsCancellationRequested)
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
