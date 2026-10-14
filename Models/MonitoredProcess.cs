using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ProcessSupervisor.Models;

public enum ProcessStatus { Stopped, Starting, Running, Crashed }

/// <summary>Stato a runtime di un processo monitorato.</summary>
public partial class MonitoredProcess : ObservableObject
{
    public MonitoredProcess(AppProcessConfig config, string? instanceLabel = null, bool isRequestInstance = false)
    {
        ConfigId = config.Id;
        Id = instanceLabel is null ? config.Id : $"{config.Id}:{Guid.NewGuid():N}";
        InstanceLabel = instanceLabel ?? "Monitoraggio";
        IsRequestInstance = isRequestInstance;
        ApplyConfig(config);
    }

    public string Id { get; }
    public string ConfigId { get; }
    public string InstanceLabel { get; }
    internal bool IsRequestInstance { get; }

    [ObservableProperty] private string _name = "";
    public string ExecutablePath { get; private set; } = "";
    public string Arguments { get; private set; } = "";
    public string WorkingDirectory { get; private set; } = "";
    public bool AutoRestartOnCrash { get; private set; }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsActive))]
    private ProcessStatus _status = ProcessStatus.Stopped;

    [ObservableProperty] private int? _pid;
    [ObservableProperty] private DateTime? _startTime;
    [ObservableProperty] private DateTime? _stopTime;
    [ObservableProperty] private int _crashCount;
    [ObservableProperty] private int? _lastExitCode;
    [ObservableProperty] private string _originStatus = "Non in esecuzione";
    [ObservableProperty] private string _observedPath = "";

    /// <summary>True se il processo è in avvio o in esecuzione.</summary>
    public bool IsActive => Status is ProcessStatus.Starting or ProcessStatus.Running;

    public TimeSpan Uptime =>
        IsActive && StartTime.HasValue ? DateTime.Now - StartTime.Value : TimeSpan.Zero;

    public string UptimeText =>
        !IsActive || !StartTime.HasValue ? "—"
        : Uptime.Days > 0 ? $"{Uptime.Days}g {Uptime:hh\\:mm\\:ss}"
        : $"{Uptime:hh\\:mm\\:ss}";

    public void RefreshUptime()
    {
        OnPropertyChanged(nameof(Uptime));
        OnPropertyChanged(nameof(UptimeText));
    }

    public void ApplyConfig(AppProcessConfig c)
    {
        Name = c.Name;
        ExecutablePath = c.ExecutablePath;
        Arguments = c.Arguments;
        WorkingDirectory = c.WorkingDirectory;
        AutoRestartOnCrash = c.AutoRestartOnCrash;
        if (!string.IsNullOrWhiteSpace(ObservedPath))
            SetObservedPath(ObservedPath);
    }

    // --- Stato interno usato dal servizio ---
    internal Process? Handle { get; set; }
    internal bool StopRequested { get; set; }
    internal bool RestartPending { get; set; }

    internal void SetObservedPath(string path)
    {
        ObservedPath = path;
        OriginStatus = string.Equals(
            Path.GetFullPath(path),
            Path.GetFullPath(ExecutablePath),
            StringComparison.OrdinalIgnoreCase)
            ? "Percorso configurato"
            : "Percorso diverso";
    }
}
