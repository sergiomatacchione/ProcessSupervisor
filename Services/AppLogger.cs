using System.Globalization;
using System.IO;

namespace ProcessSupervisor.Services;

public sealed class AppLogger
{
    private readonly object _sync = new();
    private readonly string _logDirectory = Path.Combine(AppContext.BaseDirectory, "LOG");
    private DateOnly _currentDay;
    private int _nextId;

    public event Action<string>? LogAdded;

    public void Log(string description)
    {
        var now = DateTime.Now;
        var day = DateOnly.FromDateTime(now);
        description = description.Replace('\r', ' ').Replace('\n', ' ');
        string displayEntry;

        lock (_sync)
        {
            if (_currentDay != day)
            {
                _currentDay = day;
                _nextId = ReadLastId(Path.Combine(_logDirectory, $"{now:yyyyMMdd}.log"));
            }

            var id = ++_nextId;
            displayEntry = $"{id:D6} | {now:HH:mm:ss} | {description}";
            Directory.CreateDirectory(_logDirectory);
            File.AppendAllText(
                Path.Combine(_logDirectory, $"{now:yyyyMMdd}.log"),
                $"{id:D6} | {now:yyyy-MM-dd HH:mm:ss.fff} | {description}{Environment.NewLine}");
        }

        LogAdded?.Invoke(displayEntry);
    }

    private static int ReadLastId(string path)
    {
        if (!File.Exists(path))
            return 0;

        var lastLine = File.ReadLines(path).LastOrDefault();
        if (lastLine is null)
            return 0;

        var delimiter = lastLine.IndexOf('|');
        return delimiter > 0 &&
               int.TryParse(lastLine.AsSpan(0, delimiter).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            ? id
            : 0;
    }
}
