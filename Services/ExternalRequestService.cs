using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using ProcessSupervisor.Models;

namespace ProcessSupervisor.Services;

public sealed class ExternalRequestService
{
    private const int MaximumRequestSize = 16 * 1024;
    private static readonly JsonDocumentOptions JsonOptions = new() { MaxDepth = 4 };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly ProcessWatchdogService _watchdog;
    private readonly AppLogger _logger;
    private readonly RequestQueueSettingsStore _settingsStore = new();
    private readonly HashSet<string> _archiveFailureLogged = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _readFailureLogged = new(StringComparer.OrdinalIgnoreCase);
    private RequestQueueSettings? _settings;
    private bool _isProcessing;

    public ExternalRequestService(ProcessWatchdogService watchdog, AppLogger logger)
    {
        _watchdog = watchdog;
        _logger = logger;
        _timer.Tick += (_, _) => ProcessPendingRequests();
    }

    public RequestQueueSettings GetSettings()
    {
        if (_settings is not null)
            return Clone(_settings);

        try
        {
            _settings = _settingsStore.Load();
            return Clone(_settings);
        }
        catch (Exception ex)
        {
            _logger.Log($"Impossibile leggere le cartelle richieste: {ex.Message}; mostra i percorsi predefiniti, da salvare per attivare la coda.");
            return RequestQueueSettings.CreateDefaults();
        }
    }

    public RequestQueueSettings GetDefaultSettings() => RequestQueueSettings.CreateDefaults();

    public void UpdateSettings(RequestQueueSettings settings)
    {
        RequestQueueSettingsStore.Validate(settings);
        var normalized = new RequestQueueSettings
        {
            InboxDirectory = Path.GetFullPath(settings.InboxDirectory),
            ProcessedDirectory = Path.GetFullPath(settings.ProcessedDirectory)
        };
        Directory.CreateDirectory(normalized.InboxDirectory);
        Directory.CreateDirectory(normalized.ProcessedDirectory);
        _settingsStore.Save(normalized);
        _settings = normalized;
        _logger.Log($"Cartelle richieste aggiornate (ingresso: {normalized.InboxDirectory}; elaborate: {normalized.ProcessedDirectory})");
    }

    public void Start()
    {
        try
        {
            _settings = _settingsStore.Load();
            Directory.CreateDirectory(_settings.InboxDirectory);
            Directory.CreateDirectory(_settings.ProcessedDirectory);
            _timer.Start();
            _logger.Log($"Monitoraggio richieste esterne attivo (ingresso: {_settings.InboxDirectory}; elaborate: {_settings.ProcessedDirectory})");
        }
        catch (Exception ex)
        {
            _settings = null;
            _logger.Log($"Impossibile avviare la coda delle richieste esterne: {ex.Message}");
        }
        _timer.Start();
    }

    private void ProcessPendingRequests()
    {
        if (_isProcessing || _settings is null)
            return;

        _isProcessing = true;
        try
        {
            foreach (var requestPath in Directory.EnumerateFiles(_settings.InboxDirectory, "*.json", SearchOption.TopDirectoryOnly))
                ProcessRequest(requestPath);
        }
        catch (Exception ex)
        {
            _logger.Log($"Errore scansione richieste esterne: {ex.Message}");
        }
        finally
        {
            _isProcessing = false;
        }
    }

    private void ProcessRequest(string requestPath)
    {
        string content;
        try
        {
            if ((File.GetAttributes(requestPath) & FileAttributes.ReparsePoint) != 0)
            {
                _logger.Log($"Richiesta rifiutata: il file è un collegamento ({Path.GetFileName(requestPath)})");
                MoveToProcessed(requestPath);
                return;
            }

            var fileInfo = new FileInfo(requestPath);
            if (fileInfo.Length > MaximumRequestSize)
            {
                _logger.Log($"Richiesta rifiutata: file troppo grande ({Path.GetFileName(requestPath)})");
                MoveToProcessed(requestPath);
                return;
            }

            content = File.ReadAllText(requestPath);
            _readFailureLogged.Remove(requestPath);
            _logger.Log($"Richiesta letta: {Path.GetFileName(requestPath)}");
        }
        catch (IOException ex)
        {
            if (File.Exists(requestPath) && _readFailureLogged.Add(requestPath))
                _logger.Log($"Richiesta non ancora leggibile, nuovo tentativo previsto ({Path.GetFileName(requestPath)}): {ex.Message}");
            return;
        }
        catch (UnauthorizedAccessException ex)
        {
            if (_readFailureLogged.Add(requestPath))
                _logger.Log($"Lettura richiesta negata ({Path.GetFileName(requestPath)}): {ex.Message}");
            return;
        }

        if (!MoveToProcessed(requestPath))
            return;

        try
        {
            var request = ParseRequest(content);
            var config = _watchdog.FindConfiguredApplication(request.Id);
            if (config is null)
            {
                _logger.Log($"Richiesta rifiutata: ID non autorizzato o non univoco ({request.Id})");
                return;
            }

            _logger.Log($"Avvio richiesto da file: {config.Name} (ID: {request.Id}; seconda istanza forzata: {request.ForceNewInstance})");
            if (!_watchdog.StartFromRequest(request.Id, request.ForceNewInstance))
                _logger.Log($"Richiesta non avviata: {config.Name}");
        }
        catch (JsonException ex)
        {
            _logger.Log($"Richiesta rifiutata: JSON non valido ({Path.GetFileName(requestPath)}): {ex.Message}");
        }
        catch (InvalidDataException ex)
        {
            _logger.Log($"Richiesta rifiutata: {ex.Message} ({Path.GetFileName(requestPath)})");
        }
        catch (Exception ex)
        {
            _logger.Log($"Errore elaborazione richiesta {Path.GetFileName(requestPath)}: {ex.Message}");
        }
    }

    private bool MoveToProcessed(string requestPath)
    {
        try
        {
            Directory.CreateDirectory(_settings!.ProcessedDirectory);
            var destination = Path.Combine(_settings.ProcessedDirectory, Path.GetFileName(requestPath));
            if (File.Exists(destination))
            {
                var name = Path.GetFileNameWithoutExtension(requestPath);
                var extension = Path.GetExtension(requestPath);
                destination = Path.Combine(
                    _settings.ProcessedDirectory,
                    $"{name}_{DateTime.Now:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}{extension}");
            }

            File.Move(requestPath, destination);
            _archiveFailureLogged.Remove(requestPath);
            _readFailureLogged.Remove(requestPath);
            _logger.Log($"Richiesta archiviata: {destination}");
            return true;
        }
        catch (Exception ex)
        {
            if (_archiveFailureLogged.Add(requestPath))
                _logger.Log($"Impossibile spostare la richiesta {Path.GetFileName(requestPath)}: {ex.Message}");
            return false;
        }
    }

    private static ExternalRequest ParseRequest(string content)
    {
        using var document = JsonDocument.Parse(content, JsonOptions);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("La richiesta deve essere un oggetto JSON.");

        string? id = null;
        var forceNewInstance = false;
        var seenId = false;
        var seenForce = false;
        foreach (var property in root.EnumerateObject())
        {
            switch (property.Name)
            {
                case "id" when !seenId && property.Value.ValueKind == JsonValueKind.String:
                    id = property.Value.GetString();
                    seenId = true;
                    break;
                case "forceNewInstance" when !seenForce &&
                                             property.Value.ValueKind is JsonValueKind.True or JsonValueKind.False:
                    forceNewInstance = property.Value.GetBoolean();
                    seenForce = true;
                    break;
                case "id":
                case "forceNewInstance":
                    throw new InvalidDataException($"Proprietà duplicata o non valida: {property.Name}.");
                default:
                    throw new InvalidDataException($"Proprietà non consentita: {property.Name}.");
            }
        }

        if (string.IsNullOrWhiteSpace(id))
            throw new InvalidDataException("La proprietà id è obbligatoria.");

        return new ExternalRequest(id.Trim(), forceNewInstance);
    }

    private static RequestQueueSettings Clone(RequestQueueSettings settings) => new()
    {
        InboxDirectory = settings.InboxDirectory,
        ProcessedDirectory = settings.ProcessedDirectory
    };

    private sealed record ExternalRequest(string Id, bool ForceNewInstance);
}
