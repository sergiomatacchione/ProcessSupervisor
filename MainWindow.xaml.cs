using System.Collections.Specialized;
using System.Windows;
using ProcessSupervisor.Services;
using ProcessSupervisor.ViewModels;

namespace ProcessSupervisor;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var vm = new MainViewModel();
        DataContext = vm;

        // Auto-scroll del log all'ultima riga
        vm.LogEntries.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Add && LogList.Items.Count > 0)
                LogList.ScrollIntoView(LogList.Items[^1]);
        };
    }

    private void OpenConfiguration_Click(object sender, RoutedEventArgs e)
    {
        var vm = (MainViewModel)DataContext;
        vm.Logger.Log("Utente: apertura schermata di configurazione");
        try
        {
            var editor = new ConfigEditorWindow(
                vm.Logger,
                vm.GetConfiguration(),
                vm.GetDefaultConfiguration(),
                vm.GetRequestQueueSettings(),
                vm.GetDefaultRequestQueueSettings(),
                vm.SaveConfiguration)
            {
                Owner = this
            };
            editor.Closed += (_, _) => vm.Logger.Log("Utente: chiusura schermata di configurazione");
            editor.ShowDialog();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Impossibile aprire la configurazione.{Environment.NewLine}{ex.Message}",
                "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenSchedulerLog_Click(object sender, RoutedEventArgs e)
    {
        var vm = (MainViewModel)DataContext;
        vm.Logger.Log("Utente: apertura log dello scheduler");
        var window = new SchedulerLogWindow(vm.Logger) { Owner = this };
        window.Closed += (_, _) => vm.Logger.Log("Utente: chiusura log dello scheduler");
        window.ShowDialog();
    }
}
