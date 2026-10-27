using System.IO;
using System.Windows.Threading;
using Wyrmhold.Core;

namespace WyrmHold.App;

/// <summary>
/// Surveille les dossiers où les lanceurs notent leurs jeux installés : un appmanifest_*.acf par jeu dans chaque
/// « steamapps » de Steam, un *.item par jeu dans les manifestes d'Epic. Un fichier créé, modifié, renommé ou
/// supprimé = une installation, une mise à jour ou une désinstallation → l'événement Changed, sur le fil de l'interface.
/// Debounce : Steam réécrit son manifeste pendant tout un téléchargement ; on attend 5 s sans aucun changement
/// avant de prévenir (chaque nouveau changement relance l'attente), pour ne relire qu'une fois.
/// </summary>
public sealed class LibraryFolderWatcher : IDisposable
{
    private static readonly TimeSpan QuietDelay = TimeSpan.FromSeconds(5);

    private readonly Dispatcher _dispatcher;
    private readonly List<FileSystemWatcher> _watchers = new List<FileSystemWatcher>();
    private readonly DispatcherTimer _debounce;

    // Le dernier fichier changé, pour le journal.
    private string _lastChange = "";

    // Après 5 s de calme, sur le fil de l'interface.
    public event Action<string>? Changed;

    // À créer sur le fil de l'interface (le minuteur et les événements y arrivent).
    public LibraryFolderWatcher(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _debounce = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = QuietDelay };
        _debounce.Tick += (sender, e) =>
        {
            _debounce.Stop();
            Changed?.Invoke(_lastChange);
        };
    }

    public bool IsWatching => _watchers.Count > 0;

    /// <summary>
    /// (Re)lance la surveillance des dossiers de Steam et d'Epic qui existent sur ce PC.
    /// À rappeler après une synchro complète : une nouvelle bibliothèque Steam a pu être ajoutée.
    /// </summary>
    public void Start()
    {
        Stop();

        List<(string Folder, string Filter)> targets = SteamProvider.GetSteamappsFolders()
            .Select(folder => (folder, "appmanifest_*.acf"))
            .ToList();
        targets.Add((EpicProvider.ManifestsFolder, "*.item"));

        foreach ((string folder, string filter) in targets)
        {
            if (!Directory.Exists(folder))
            {
                continue;
            }

            try
            {
                FileSystemWatcher watcher = new FileSystemWatcher(folder, filter)
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                    IncludeSubdirectories = false
                };

                watcher.Created += OnFileEvent;
                watcher.Changed += OnFileEvent;
                watcher.Deleted += OnFileEvent;
                watcher.Renamed += OnFileEvent;
                watcher.Error += (sender, e) => Logger.Log($"Surveillance de {folder} : {e.GetException().Message}");
                watcher.EnableRaisingEvents = true;
                _watchers.Add(watcher);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // Dossier illisible : on s'en passe (le bouton et la synchro régulière restent là).
                Logger.Log($"Surveillance de {folder} impossible : {ex.Message}");
            }
        }
    }

    public void Stop()
    {
        _debounce.Stop();

        foreach (FileSystemWatcher watcher in _watchers)
        {
            watcher.Dispose();
        }

        _watchers.Clear();
    }

    public void Dispose() => Stop();

    // Appelé sur un fil du système de fichiers : on repasse par le Dispatcher avant de toucher au minuteur.
    private void OnFileEvent(object sender, FileSystemEventArgs e)
    {
        string name = e.Name ?? "";

        _dispatcher.InvokeAsync(() =>
        {
            _lastChange = name;
            _debounce.Stop();
            _debounce.Start();
        });
    }
}
