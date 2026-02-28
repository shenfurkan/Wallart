using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;

namespace WallArt.Services;

public interface ILogService
{
    ObservableCollection<string> Logs { get; }
    void Log(string message);
}

public class LogService : ILogService
{
    public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();
    
    public void Log(string message)
    {
        var formattedMessage = $"[{DateTime.Now:HH:mm:ss}] {message}";
        
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(() => Logs.Add(formattedMessage));
        }
        else
        {
            Logs.Add(formattedMessage);
        }
        
        var d = Application.Current?.Dispatcher;
        if (d != null && !d.CheckAccess())
        {
            d.BeginInvoke(() => {
                while (Logs.Count > 100) Logs.RemoveAt(0);
            });
        }
        else
        {
            while (Logs.Count > 100) Logs.RemoveAt(0);
        }
    }
}
