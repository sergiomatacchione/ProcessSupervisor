using System.IO;

namespace ProcessSupervisor.Models;

public sealed class RequestQueueSettings
{
    public string InboxDirectory { get; set; } = "";
    public string ProcessedDirectory { get; set; } = "";

    public static RequestQueueSettings CreateDefaults() => new()
    {
        InboxDirectory = Path.Combine(AppContext.BaseDirectory, "REQUESTS"),
        ProcessedDirectory = Path.Combine(AppContext.BaseDirectory, "REQUESTS", "ANALYZED")
    };
}
