using System.Text.Json.Serialization;

namespace ProcessSupervisor.Models;

/// <summary>Voce di apps_config.json.</summary>
public class AppProcessConfig
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("executablePath")] public string ExecutablePath { get; set; } = "";
    [JsonPropertyName("arguments")] public string Arguments { get; set; } = "";
    [JsonPropertyName("workingDirectory")] public string WorkingDirectory { get; set; } = "";
    [JsonPropertyName("autoRestartOnCrash")] public bool AutoRestartOnCrash { get; set; }
}
