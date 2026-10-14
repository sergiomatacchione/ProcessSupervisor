using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using ProcessSupervisor.Models;
using ProcessSupervisor.Services;

namespace ProcessSupervisor;

public partial class ConfigEditorWindow : Window
{
    private static readonly HashSet<string> SupportedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".exe", ".bat", ".cmd", ".ps1" };

    private readonly AppLogger _logger;
    private readonly IReadOnlyList<AppProcessConfig> _defaults;
    private readonly Action<IReadOnlyList<AppProcessConfig>> _save;
    private readonly ObservableCollection<AppProcessConfig> _configs;

    public ConfigEditorWindow(
        AppLogger logger,
        IEnumerable<AppProcessConfig> current,
        IEnumerable<AppProcessConfig> defaults,
        Action<IReadOnlyList<AppProcessConfig>> save)
    {
        InitializeComponent();
        _logger = logger;
        _defaults = defaults.Select(Clone).ToList();
        _save = save;
        _configs = new ObservableCollection<AppProcessConfig>(current.Select(Clone));
        DataContext = _configs;
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var config = new AppProcessConfig
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Nuovo programma"
        };
        _configs.Add(config);
        ConfigGrid.SelectedItem = config;
        ConfigGrid.ScrollIntoView(config);
        _logger.Log("Utente: aggiunta voce alla configurazione");
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (ConfigGrid.SelectedItem is not AppProcessConfig selected)
            return;

        _configs.Remove(selected);
        _logger.Log($"Utente: rimozione di {selected.Name} dalla configurazione");
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: AppProcessConfig config })
            return;

        var dialog = new OpenFileDialog
        {
            Title = "Seleziona il programma da monitorare",
            Filter = "Programmi supportati (*.exe;*.bat;*.cmd;*.ps1)|*.exe;*.bat;*.cmd;*.ps1",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true)
            return;

        config.ExecutablePath = dialog.FileName;
        if (string.IsNullOrWhiteSpace(config.WorkingDirectory))
            config.WorkingDirectory = Path.GetDirectoryName(dialog.FileName) ?? "";

        ConfigGrid.Items.Refresh();
        _logger.Log($"Utente: selezionato programma {dialog.FileName}");
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(
                this,
                "Sostituire i valori visualizzati con quelli predefiniti? Saranno salvati solo premendo Salva.",
                "Ripristino configurazione",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        _configs.Clear();
        foreach (var config in _defaults)
            _configs.Add(Clone(config));
        _logger.Log("Utente: ripristinati i valori predefiniti nell'editor");
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ConfigGrid.CommitEdit();
        ConfigGrid.CommitEdit();

        var errors = Validate();
        if (errors.Count > 0)
        {
            MessageBox.Show(this, string.Join(Environment.NewLine, errors), "Configurazione non valida",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            _logger.Log($"Configurazione non salvata: {errors.Count} errori di validazione");
            return;
        }

        try
        {
            _save(_configs.Select(Clone).ToList());
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            _logger.Log($"Errore salvataggio configurazione: {ex.Message}");
            MessageBox.Show(this, $"Impossibile salvare la configurazione.{Environment.NewLine}{ex.Message}",
                "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private List<string> Validate()
    {
        var errors = new List<string>();
        if (_configs.Count == 0)
            errors.Add("Inserire almeno un programma.");

        var duplicateIds = _configs
            .Where(config => !string.IsNullOrWhiteSpace(config.Id))
            .GroupBy(config => config.Id.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key);
        foreach (var duplicateId in duplicateIds)
            errors.Add($"ID duplicato: {duplicateId}.");

        for (var index = 0; index < _configs.Count; index++)
        {
            var config = _configs[index];
            if (string.IsNullOrWhiteSpace(config.Id))
                errors.Add($"La riga {index + 1} richiede un ID.");
            if (string.IsNullOrWhiteSpace(config.Name))
                errors.Add($"La riga {index + 1} richiede un nome.");
            if (string.IsNullOrWhiteSpace(config.ExecutablePath))
            {
                errors.Add($"La riga {index + 1} richiede il percorso del programma.");
                continue;
            }

            if (!SupportedExtensions.Contains(Path.GetExtension(config.ExecutablePath)))
                errors.Add($"La riga {index + 1} deve indicare un file .exe, .bat, .cmd o .ps1.");
        }

        return errors;
    }

    private static AppProcessConfig Clone(AppProcessConfig config) => new()
    {
        Id = config.Id,
        Name = config.Name,
        ExecutablePath = config.ExecutablePath,
        Arguments = config.Arguments,
        WorkingDirectory = config.WorkingDirectory,
        AutoRestartOnCrash = config.AutoRestartOnCrash
    };
}
