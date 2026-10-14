using System.IO;
using System.Windows;
using ProcessSupervisor.Services;

namespace ProcessSupervisor;

public partial class SchedulerLogWindow : Window
{
    private readonly AppLogger _logger;
    private readonly string _schedulerLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MortiDiSonno", "logs", "scheduler.log");

    public SchedulerLogWindow(AppLogger logger)
    {
        InitializeComponent();
        _logger = logger;
        RefreshLog();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        _logger.Log("Utente: aggiornamento log dello scheduler");
        RefreshLog();
    }

    private void RefreshLog()
    {
        try
        {
            LogText.Text = File.Exists(_schedulerLogPath)
                ? File.ReadAllText(_schedulerLogPath)
                : $"File non trovato:{Environment.NewLine}{_schedulerLogPath}";
            LogText.CaretIndex = LogText.Text.Length;
            LogText.ScrollToEnd();
        }
        catch (Exception ex)
        {
            _logger.Log($"Errore lettura log scheduler: {ex.Message}");
            LogText.Text = $"Impossibile leggere il file:{Environment.NewLine}{_schedulerLogPath}{Environment.NewLine}{ex.Message}";
        }
    }
}
