using System.Text.Json;
using System.IO;
using ProcessSupervisor.Models;

namespace ProcessSupervisor.Services;

public sealed class ConfigurationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public string ConfigPath { get; } = Path.Combine(AppContext.BaseDirectory, "apps_config.json");
    public string DefaultsPath { get; } = Path.Combine(AppContext.BaseDirectory, "apps_config.default.json");

    public List<AppProcessConfig> LoadCurrent() => Load(ConfigPath);

    public List<AppProcessConfig> LoadDefaults() => Load(DefaultsPath);

    public void Save(IEnumerable<AppProcessConfig> configs)
    {
        var json = JsonSerializer.Serialize(configs, JsonOptions);
        var temporaryPath = $"{ConfigPath}.tmp";
        File.WriteAllText(temporaryPath, json);
        File.Move(temporaryPath, ConfigPath, overwrite: true);
    }

    private static List<AppProcessConfig> Load(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<List<AppProcessConfig>>(
                   json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
               ?? throw new InvalidDataException($"Il file {Path.GetFileName(path)} non contiene una configurazione valida.");
    }
}
