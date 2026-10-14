using System.IO;
using System.Text.Json;
using ProcessSupervisor.Models;

namespace ProcessSupervisor.Services;

public sealed class RequestQueueSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _settingsPath = Path.Combine(AppContext.BaseDirectory, "request_queue.json");

    public RequestQueueSettings Load()
    {
        if (!File.Exists(_settingsPath))
            return RequestQueueSettings.CreateDefaults();

        var settings = JsonSerializer.Deserialize<RequestQueueSettings>(File.ReadAllText(_settingsPath))
                       ?? throw new InvalidDataException("Il file request_queue.json non è valido.");
        Validate(settings);
        return new RequestQueueSettings
        {
            InboxDirectory = Path.GetFullPath(settings.InboxDirectory),
            ProcessedDirectory = Path.GetFullPath(settings.ProcessedDirectory)
        };
    }

    public void Save(RequestQueueSettings settings)
    {
        Validate(settings);
        var temporaryPath = $"{_settingsPath}.tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temporaryPath, _settingsPath, overwrite: true);
    }

    public static void Validate(RequestQueueSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.InboxDirectory))
            throw new InvalidDataException("Specificare la cartella delle richieste.");
        if (string.IsNullOrWhiteSpace(settings.ProcessedDirectory))
            throw new InvalidDataException("Specificare la cartella delle richieste elaborate.");

        var inbox = Path.GetFullPath(settings.InboxDirectory);
        var processed = Path.GetFullPath(settings.ProcessedDirectory);
        if (string.Equals(inbox, processed, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("La cartella delle richieste e quella delle richieste elaborate devono essere diverse.");
    }
}
