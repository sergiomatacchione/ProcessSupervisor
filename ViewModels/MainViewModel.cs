using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProcessSupervisor.Models;
using ProcessSupervisor.Services;

namespace ProcessSupervisor.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private const int MaxLogEntries = 1000;
    private readonly AppLogger _logger = new();
    private readonly ProcessWatchdogService _service;

    public ObservableCollection<MonitoredProcess> Processes => _service.Processes;
    public ObservableCollection<string> LogEntries { get; } = new();
    public AppLogger Logger => _logger;

    public MainViewModel()
    {
        _service = new ProcessWatchdogService(_logger);
        _logger.LogAdded += message =>
        {
            LogEntries.Add(message);
            while (LogEntries.Count > MaxLogEntries) LogEntries.RemoveAt(0);
        };
        _service.Start();
    }

    [RelayCommand]
    private void StartAll()
    {
        _logger.Log("Utente: avvio di tutti i programmi");
        _service.StartAll();
    }

    [RelayCommand]
    private Task StopAllAsync()
    {
        _logger.Log("Utente: arresto di tutti i programmi");
        return _service.StopAllAsync();
    }

    [RelayCommand]
    private Task RestartAllAsync()
    {
        _logger.Log("Utente: riavvio di tutti i programmi");
        return _service.RestartAllAsync();
    }

    [RelayCommand]
    private void ReloadConfig()
    {
        _logger.Log("Utente: ricaricamento della configurazione");
        _service.ReloadConfig();
    }

    public List<AppProcessConfig> GetConfiguration() => _service.GetConfiguration();

    public List<AppProcessConfig> GetDefaultConfiguration() => _service.GetDefaultConfiguration();

    public void SaveConfiguration(IEnumerable<AppProcessConfig> configs)
    {
        _logger.Log("Utente: salvataggio della configurazione");
        _service.SaveConfiguration(configs);
    }

    [RelayCommand]
    private async Task ToggleAsync(MonitoredProcess? process)
    {
        if (process is null) return;
        _logger.Log($"Utente: {(process.IsActive ? "arresto" : "avvio")} di {process.Name}");
        if (process.IsActive) await _service.StopAsync(process);
        else _service.Start(process);
    }

    [RelayCommand]
    private Task RestartAsync(MonitoredProcess? process)
    {
        if (process is null)
            return Task.CompletedTask;

        _logger.Log($"Utente: riavvio di {process.Name}");
        return _service.RestartAsync(process);
    }
}
