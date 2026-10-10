using System.ComponentModel;
using System.Windows;
using Velopack;
using Velopack.Sources;
using Wyrmhold.Core;

namespace WyrmHold.App;

/// <summary>
/// Au démarrage, avant la fenêtre principale : cherche une nouvelle version sur GitHub (Releases).
/// - pas de mise à jour, version de développement, ou pas de réponse en 5 s → la fenêtre se ferme et Wyrmhold s'ouvre ;
/// - mise à jour trouvée → téléchargement (avec le pourcentage), installation, et Wyrmhold redémarre dans la nouvelle
///   version (choix de Val du 10 octobre 2026 : rien à cliquer ; l'appli n'est pas encore utilisée, rien n'est interrompu).
/// « Passer » (ou la croix) ouvre Wyrmhold tout de suite ; la mise à jour sera cherchée au prochain démarrage.
/// Quand elle se ferme, App ouvre la fenêtre principale (voir App.OnStartup).
/// </summary>
public partial class UpdateWindow : Window
{
    // Sans réponse de GitHub au bout de ce temps (pas d'internet…), on ouvre Wyrmhold quand même.
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(5);

    // « Passer » : arrête l'attente (et le téléchargement s'il a commencé).
    private readonly CancellationTokenSource _skip = new CancellationTokenSource();

    // true pendant l'installation : la fenêtre ne doit plus se fermer (Velopack va fermer et relancer Wyrmhold).
    private bool _isInstalling;

    // La fenêtre est déjà fermée (« Passer » pendant la recherche) : ne pas la refermer.
    private bool _isClosed;

    public UpdateWindow()
    {
        InitializeComponent();
        VersionText.Text = $"Version {AppInfo.Version}";
        Closed += (sender, e) => _isClosed = true;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await CheckAndInstallAsync();

        if (!_isInstalling && !_isClosed)
        {
            Close();
        }
    }

    private async Task CheckAndInstallAsync()
    {
        try
        {
            UpdateManager updates = new UpdateManager(new GithubSource(AppInfo.RepositoryUrl, null, false));

            // Lancé depuis Visual Studio (pas installé) : rien à mettre à jour.
            if (!updates.IsInstalled)
            {
                return;
            }

            // 1. La recherche, 5 secondes au plus. CheckForUpdatesAsync ne sait pas s'arrêter :
            //    si elle traîne, on la laisse finir dans son coin et on n'utilise pas sa réponse.
            Task<UpdateInfo?> check = updates.CheckForUpdatesAsync();
            Task finished = await Task.WhenAny(check, Task.Delay(CheckTimeout, _skip.Token));

            if (finished != check)
            {
                if (!_skip.IsCancellationRequested)
                {
                    Logger.Log($"Recherche de mise à jour : pas de réponse en {CheckTimeout.TotalSeconds:0} s, Wyrmhold s'ouvre sans attendre.");
                }
                return;
            }

            UpdateInfo? available = await check;

            if (available is null)
            {
                return;   // déjà à jour
            }

            // 2. Le téléchargement, avec son pourcentage.
            string version = available.TargetFullRelease.Version.ToString();
            StatusText.Text = $"Téléchargement de la version {version}…";
            Progress.IsIndeterminate = false;
            Progress.Value = 0;

            await updates.DownloadUpdatesAsync(available, percent =>
            {
                // Velopack donne l'avancement depuis un autre fil : on revient sur celui de la fenêtre.
                Dispatcher.BeginInvoke(() =>
                {
                    Progress.Value = percent;
                    StatusText.Text = $"Téléchargement de la version {version}… {percent} %";
                });
            }, _skip.Token);

            // 3. L'installation : Velopack ferme Wyrmhold, remplace les fichiers et le relance.
            _isInstalling = true;
            SkipButton.IsEnabled = false;
            Progress.IsIndeterminate = true;
            StatusText.Text = $"Installation de la version {version}… Wyrmhold va redémarrer.";
            Logger.Log($"Mise à jour {version} téléchargée au démarrage : installation et redémarrage.");

            updates.ApplyUpdatesAndRestart(available.TargetFullRelease);
        }
        catch (OperationCanceledException)
        {
            // « Passer » pendant le téléchargement : Wyrmhold s'ouvre, on réessaiera au prochain démarrage.
            _isInstalling = false;
        }
        catch (Exception ex)
        {
            // Pas de réseau, GitHub indisponible, installation refusée… : Wyrmhold s'ouvre quand même.
            _isInstalling = false;
            Logger.Log($"Mise à jour au démarrage impossible : {ex.Message}");
        }
    }

    private void SkipButton_Click(object sender, RoutedEventArgs e)
    {
        _skip.Cancel();
        Close();
    }

    // La croix = « Passer » (sauf pendant l'installation, qui va relancer Wyrmhold elle-même).
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_isInstalling)
        {
            e.Cancel = true;
            return;
        }

        _skip.Cancel();
    }
}
