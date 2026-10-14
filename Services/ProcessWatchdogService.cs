using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Text.RegularExpressions;
using System.Windows.Threading;
using ProcessSupervisor.Models;

namespace ProcessSupervisor.Services;

/// <summary>Avvia, ferma e monitora i processi configurati. Da usare sul thread UI.</summary>
public sealed class ProcessWatchdogService
{
    private static readonly TimeSpan RestartDelay = TimeSpan.FromSeconds(3);
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly ConfigurationStore _configuration = new();
    private readonly AppLogger _logger;
    private bool _scriptDiscoveryFailureLogged;

    public ObservableCollection<MonitoredProcess> Processes { get; } = new();

    public ProcessWatchdogService(AppLogger logger)
    {
        _logger = logger;
        _timer.Tick += (_, _) => Tick();
    }

    public void Start()
    {
        ReloadConfig();
        _timer.Start();
    }

    public List<AppProcessConfig> GetConfiguration() => _configuration.LoadCurrent();

    public List<AppProcessConfig> GetDefaultConfiguration() => _configuration.LoadDefaults();

    public void SaveConfiguration(IEnumerable<AppProcessConfig> configs)
    {
        _configuration.Save(configs);
        ReloadConfig();
    }

    public void ReloadConfig()
    {
        List<AppProcessConfig> configs;
        try
        {
            configs = _configuration.LoadCurrent();
        }
        catch (Exception ex)
        {
            _logger.Log($"Errore lettura apps_config.json: {ex.Message}");
            return;
        }

        foreach (var gone in Processes.Where(p => configs.All(c => c.Id != p.Id)).ToList())
        {
            gone.RestartPending = false;
            Processes.Remove(gone);
            _logger.Log($"{gone.Name} rimosso dalla configurazione");
        }

        foreach (var cfg in configs)
        {
            var existing = Processes.FirstOrDefault(p => p.Id == cfg.Id);
            if (existing != null)
            {
                existing.ApplyConfig(cfg);
                continue;
            }

            var monitored = new MonitoredProcess(cfg);
            Processes.Add(monitored);
            TryAttachExisting(monitored);
        }

        _logger.Log($"Configurazione caricata ({configs.Count} applicazioni)");
    }

    private void TryAttachExisting(MonitoredProcess monitored)
    {
        var extension = Path.GetExtension(monitored.ExecutablePath);
        if (extension.Equals(".bat", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".ps1", StringComparison.OrdinalIgnoreCase))
        {
            TryAttachScript(monitored);
            return;
        }

        try
        {
            var processName = Path.GetFileNameWithoutExtension(monitored.ExecutablePath);
            var candidates = Process.GetProcessesByName(processName);
            Process? selected = null;
            string? selectedPath = null;
            var configuredPath = Path.GetFullPath(monitored.ExecutablePath);

            foreach (var candidate in candidates)
            {
                string? candidatePath = null;
                try { candidatePath = candidate.MainModule?.FileName; }
                catch (System.ComponentModel.Win32Exception) { }
                catch (InvalidOperationException) { }

                if (candidatePath is null)
                {
                    candidate.Dispose();
                    continue;
                }

                if (selected is null ||
                    string.Equals(candidatePath, configuredPath, StringComparison.OrdinalIgnoreCase))
                {
                    selected?.Dispose();
                    selected = candidate;
                    selectedPath = candidatePath;
                }
                else
                {
                    candidate.Dispose();
                }
            }

            if (selected is null)
                return;

            AdoptProcess(monitored, selected, selectedPath!);
        }
        catch (Exception ex)
        {
            _logger.Log($"Rilevamento istanze di {monitored.Name} fallito: {ex.Message}");
        }
    }

    private void TryAttachScript(MonitoredProcess monitored)
    {
        try
        {
            var configuredPath = Path.GetFullPath(monitored.ExecutablePath);
            var configuredName = Path.GetFileName(configuredPath);
            using var searcher = new ManagementObjectSearcher(
                "SELECT ProcessId, CommandLine FROM Win32_Process " +
                "WHERE Name = 'cmd.exe' OR Name = 'powershell.exe' OR Name = 'pwsh.exe'");
            using var processes = searcher.Get();

            Process? selected = null;
            string? selectedPath = null;
            foreach (ManagementObject item in processes)
            {
                var commandLine = item["CommandLine"] as string;
                var scriptPath = GetScriptPath(commandLine);
                if (scriptPath is null ||
                    !Path.GetFileName(scriptPath).Equals(configuredName, StringComparison.OrdinalIgnoreCase))
                    continue;

                var processId = Convert.ToInt32(item["ProcessId"]);
                Process candidate;
                try { candidate = Process.GetProcessById(processId); }
                catch (ArgumentException) { continue; }

                if (selected is null ||
                    string.Equals(scriptPath, configuredPath, StringComparison.OrdinalIgnoreCase))
                {
                    selected?.Dispose();
                    selected = candidate;
                    selectedPath = scriptPath;
                }
                else
                {
                    candidate.Dispose();
                }
            }

            if (selected is not null)
                AdoptProcess(monitored, selected, selectedPath!);
        }
        catch (ManagementException ex)
        {
            if (!_scriptDiscoveryFailureLogged)
            {
                _scriptDiscoveryFailureLogged = true;
                _logger.Log($"Rilevamento script tramite WMI non disponibile: {ex.Message}");
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            if (!_scriptDiscoveryFailureLogged)
            {
                _scriptDiscoveryFailureLogged = true;
                _logger.Log($"Accesso al rilevamento WMI negato: {ex.Message}");
            }
        }
    }

    private static string? GetScriptPath(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
            return null;

        var match = Regex.Match(
            commandLine,
            "\"[^\"]+\\.(?:bat|cmd|ps1)\"|[^\\s\"]+\\.(?:bat|cmd|ps1)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success ? match.Value.Trim('"') : null;
    }

    private void AdoptProcess(MonitoredProcess monitored, Process process, string observedPath)
    {
        monitored.Handle = process;
        monitored.Pid = process.Id;
        try { monitored.StartTime = process.StartTime; }
        catch (System.ComponentModel.Win32Exception) { monitored.StartTime = DateTime.Now; }
        monitored.Status = ProcessStatus.Running;
        monitored.SetObservedPath(observedPath);
        _logger.Log(
            $"Rilevato {monitored.Name} già attivo (PID: {process.Id}; {monitored.OriginStatus}: {observedPath})");
    }

    public void Start(MonitoredProcess monitored)
    {
        if (monitored.IsActive)
            return;

        monitored.StopRequested = false;
        monitored.RestartPending = false;
        monitored.Status = ProcessStatus.Starting;

        try
        {
            var startInfo = CreateStartInfo(monitored);
            var process = Process.Start(startInfo)
                          ?? throw new InvalidOperationException("Process.Start ha restituito null");

            monitored.Handle = process;
            monitored.Pid = process.Id;
            monitored.StartTime = DateTime.Now;
            monitored.StopTime = null;
            monitored.SetObservedPath(monitored.ExecutablePath);
            monitored.Status = ProcessStatus.Running;
            _logger.Log($"Avviato {monitored.Name} (PID: {process.Id}; percorso configurato)");
        }
        catch (Exception ex)
        {
            monitored.Handle = null;
            monitored.Pid = null;
            monitored.Status = ProcessStatus.Stopped;
            _logger.Log($"Impossibile avviare {monitored.Name}: {ex.Message}");
        }
    }

    private static ProcessStartInfo CreateStartInfo(MonitoredProcess monitored)
    {
        var executablePath = Path.GetFullPath(monitored.ExecutablePath);
        var extension = Path.GetExtension(executablePath);
        var workingDirectory = string.IsNullOrWhiteSpace(monitored.WorkingDirectory)
            ? Path.GetDirectoryName(executablePath) ?? AppContext.BaseDirectory
            : monitored.WorkingDirectory;

        if (extension.Equals(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return new ProcessStartInfo(executablePath, monitored.Arguments)
            {
                UseShellExecute = false,
                WorkingDirectory = workingDirectory
            };
        }

        if (extension.Equals(".bat", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase))
        {
            var command = $"\"{executablePath}\" {monitored.Arguments}".TrimEnd();
            return new ProcessStartInfo(Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe")
            {
                UseShellExecute = false,
                WorkingDirectory = workingDirectory,
                Arguments = $"/d /s /c \"\"{command}\""
            };
        }

        if (extension.Equals(".ps1", StringComparison.OrdinalIgnoreCase))
        {
            return new ProcessStartInfo(
                "powershell.exe",
                $"-NoProfile -ExecutionPolicy Bypass -File \"{executablePath}\" {monitored.Arguments}".TrimEnd())
            {
                UseShellExecute = false,
                WorkingDirectory = workingDirectory
            };
        }

        throw new InvalidDataException($"Estensione non supportata: {extension}");
    }

    public async Task StopAsync(MonitoredProcess monitored)
    {
        monitored.RestartPending = false;
        var handle = monitored.Handle;
        if (!monitored.IsActive || handle is null)
        {
            if (monitored.Status == ProcessStatus.Starting)
                monitored.Status = ProcessStatus.Stopped;
            return;
        }

        monitored.StopRequested = true;
        var pid = monitored.Pid;

        try
        {
            if (!handle.HasExited)
                handle.Kill(entireProcessTree: true);
            await handle.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            _logger.Log($"Errore durante l'arresto di {monitored.Name}: {ex.Message}");
            if (!HasExited(handle))
            {
                monitored.StopRequested = false;
                return;
            }
        }

        try { monitored.LastExitCode = handle.HasExited ? handle.ExitCode : null; }
        catch (InvalidOperationException) { }

        handle.Dispose();
        monitored.Handle = null;
        monitored.Pid = null;
        monitored.StopTime = DateTime.Now;
        monitored.Status = ProcessStatus.Stopped;
        monitored.OriginStatus = "Non in esecuzione";
        monitored.StopRequested = false;
        _logger.Log($"Fermato {monitored.Name} (PID: {pid})");
    }

    public async Task RestartAsync(MonitoredProcess monitored)
    {
        await StopAsync(monitored);
        Start(monitored);
    }

    public void StartAll()
    {
        foreach (var monitored in Processes.ToList())
            Start(monitored);
    }

    public Task StopAllAsync() => Task.WhenAll(Processes.ToList().Select(StopAsync));

    public async Task RestartAllAsync()
    {
        await StopAllAsync();
        StartAll();
    }

    private void Tick()
    {
        foreach (var monitored in Processes.ToList())
        {
            if (monitored.IsActive && monitored.Handle is not null)
            {
                if (monitored.StopRequested)
                {
                    monitored.RefreshUptime();
                    continue;
                }

                if (HasExited(monitored.Handle))
                    HandleExit(monitored);
                else
                    monitored.RefreshUptime();

                continue;
            }

            if (!monitored.RestartPending)
                TryAttachExisting(monitored);
            monitored.RefreshUptime();
        }
    }

    private static bool HasExited(Process process)
    {
        try { return process.HasExited; }
        catch (InvalidOperationException) { return true; }
        catch (System.ComponentModel.Win32Exception) { return true; }
    }

    private void HandleExit(MonitoredProcess monitored)
    {
        int? exitCode = null;
        try { exitCode = monitored.Handle!.ExitCode; }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }

        monitored.Handle?.Dispose();
        monitored.Handle = null;
        monitored.LastExitCode = exitCode;
        monitored.Pid = null;
        monitored.StopTime = DateTime.Now;
        monitored.OriginStatus = "Non in esecuzione";
        monitored.CrashCount += exitCode is null or not 0 ? 1 : 0;
        monitored.Status = exitCode is null or not 0 ? ProcessStatus.Crashed : ProcessStatus.Stopped;
        monitored.RefreshUptime();

        if (monitored.Status == ProcessStatus.Crashed)
        {
            _logger.Log(
                $"Crash di {monitored.Name} (ExitCode: {(exitCode?.ToString() ?? "n/d")})");
            if (monitored.AutoRestartOnCrash)
            {
                monitored.RestartPending = true;
                _logger.Log($"Riavvio automatico di {monitored.Name} tra {RestartDelay.TotalSeconds:0} secondi...");
                _ = RestartAfterDelayAsync(monitored);
            }
        }
        else
        {
            _logger.Log($"Interruzione di {monitored.Name} (ExitCode: 0)");
        }
    }

    private async Task RestartAfterDelayAsync(MonitoredProcess monitored)
    {
        await Task.Delay(RestartDelay);
        if (monitored.RestartPending && monitored.Status == ProcessStatus.Crashed && Processes.Contains(monitored))
            Start(monitored);
    }
}
