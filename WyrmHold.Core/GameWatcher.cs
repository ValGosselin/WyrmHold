using System.Diagnostics;

namespace Wyrmhold.Core;

/// <summary>
/// Un jeu repéré en train de tourner, et depuis quand.
/// </summary>
public class RunningGame
{
    public RunningGame(Game game, DateTimeOffset start)
    {
        Game = game;
        Start = start;
    }

    // Remplacé par la nouvelle copie du jeu quand la bibliothèque est rechargée (voir SetGames).
    public Game Game { get; internal set; }

    public DateTimeOffset Start { get; }

    public TimeSpan Elapsed => DateTimeOffset.Now - Start;
}

/// <summary>
/// Repère les jeux en cours, quel que soit le lanceur, même lancés hors de Wyrmhold :
/// toutes les 5 secondes, il regarde quels programmes tournent et si l'un d'eux est rangé
/// dans le dossier d'installation d'un jeu de la bibliothèque.
/// Il ne fait que regarder : il ne touche ni aux fichiers ni à la mémoire des jeux.
/// À démarrer depuis le fil de l'interface : ses événements arrivent alors sur ce fil.
/// </summary>
public class GameWatcher
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    // Un jeu absent 2 fois de suite (10 s) est considéré comme fermé. Une seule absence ne suffit pas :
    // certains jeux ferment leur petit programme de démarrage puis ouvrent le vrai jeu.
    private const int MissesBeforeStop = 2;

    // Un dossier de jeu installé et le jeu qui va avec.
    private record GameFolder(string Folder, Game Game);

    // Les dossiers des jeux installés, du plus long au plus court : le plus précis gagne.
    private List<GameFolder> _folders = new List<GameFolder>();

    // Les jeux en cours, par clé (voir KeyOf), et combien de fois de suite chacun a manqué à l'appel.
    private readonly Dictionary<string, RunningGame> _running = new Dictionary<string, RunningGame>();
    private readonly Dictionary<string, int> _misses = new Dictionary<string, int>();

    // null = surveillance arrêtée.
    private CancellationTokenSource? _stop;

    public event Action<RunningGame>? GameStarted;

    // Le jeu qui vient de se fermer, et l'heure de fin retenue pour sa session.
    public event Action<RunningGame, DateTimeOffset>? GameStopped;

    // Après chaque passage (toutes les 5 s), qu'un jeu ait changé ou non.
    public event Action? Scanned;

    public bool IsWatching => _stop is not null;

    // Le jeu en cours, ou le dernier lancé s'il y en a plusieurs. null = aucun jeu.
    public RunningGame? Current => _running.Values.MaxBy(running => running.Start);

    public bool IsRunning(Game game) => _running.ContainsKey(KeyOf(game));

    // La même clé que pour les sessions : plateforme + identifiant du jeu sur cette plateforme.
    public static string KeyOf(Game game) => $"{game.Platform}_{game.PlatformGameId}";

    /// <summary>
    /// Donne la liste des jeux à reconnaître (seuls les jeux installés, avec un dossier, comptent).
    /// À rappeler à chaque rechargement de la bibliothèque.
    /// </summary>
    public void SetGames(IEnumerable<Game> games)
    {
        List<GameFolder> all = new List<GameFolder>();

        foreach (Game game in games)
        {
            if (game.IsInstalled && TryGetFolder(game.InstallPath, out string folder))
            {
                all.Add(new GameFolder(folder, game));
            }
        }

        // Un dossier qui contient le dossier d'un AUTRE jeu est une bibliothèque, pas un jeu
        // (ex. « steamapps\common » enregistré par erreur) : tous ses programmes passeraient pour ce jeu.
        _folders = all
            .Where(outer => !all.Any(inner => inner.Folder.Length > outer.Folder.Length
                && inner.Folder.StartsWith(outer.Folder, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(entry => entry.Folder.Length)
            .ToList();

        // Les jeux déjà en cours pointent maintenant vers les nouvelles copies (temps de jeu à jour…).
        foreach (GameFolder entry in _folders)
        {
            if (_running.TryGetValue(KeyOf(entry.Game), out RunningGame? running))
            {
                running.Game = entry.Game;
            }
        }
    }

    public void Start()
    {
        if (_stop is not null)
        {
            return;
        }

        _stop = new CancellationTokenSource();
        _ = WatchLoopAsync(_stop.Token);
    }

    /// <summary>
    /// Arrête la surveillance. Les jeux encore en cours sont annoncés comme fermés maintenant :
    /// sans surveillance, on ne saura pas quand ils se ferment vraiment.
    /// </summary>
    public void Stop()
    {
        if (_stop is null)
        {
            return;
        }

        _stop.Cancel();
        _stop = null;

        List<RunningGame> stopped = _running.Values.ToList();
        _running.Clear();
        _misses.Clear();

        foreach (RunningGame running in stopped)
        {
            GameStopped?.Invoke(running, DateTimeOffset.Now);
        }
    }

    private async Task WatchLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                List<GameFolder> folders = _folders;

                // La lecture des programmes se fait à côté (Task.Run) pour ne pas figer la fenêtre ;
                // la suite reprend sur le fil de l'interface.
                Dictionary<string, Game> found = await Task.Run(() => FindRunningGames(folders));

                if (token.IsCancellationRequested)
                {
                    return;
                }

                Update(found);
                await Task.Delay(PollInterval, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                // Une erreur ne doit pas arrêter la surveillance : on la note et on réessaie au prochain tour.
                Logger.Log($"Détecteur de jeu : {ex}");
                await Task.Delay(PollInterval, CancellationToken.None);
            }
        }
    }

    /// <summary>
    /// Compare les jeux trouvés à ce passage avec ceux du passage d'avant.
    /// </summary>
    private void Update(Dictionary<string, Game> found)
    {
        DateTimeOffset now = DateTimeOffset.Now;

        foreach ((string key, Game game) in found)
        {
            _misses.Remove(key);

            if (!_running.ContainsKey(key))
            {
                RunningGame running = new RunningGame(game, now);
                _running[key] = running;
                GameStarted?.Invoke(running);
            }
        }

        foreach (string key in _running.Keys.Where(key => !found.ContainsKey(key)).ToList())
        {
            int misses = _misses.GetValueOrDefault(key) + 1;

            if (misses < MissesBeforeStop)
            {
                _misses[key] = misses;
                continue;
            }

            _misses.Remove(key);

            // Un événement précédent a pu arrêter la surveillance (et vider la liste) : rien à faire alors.
            if (!_running.Remove(key, out RunningGame? stopped))
            {
                continue;
            }

            // Le jeu s'est fermé avant le premier passage où il manquait : on retient ce moment-là.
            GameStopped?.Invoke(stopped, now - PollInterval * (MissesBeforeStop - 1));
        }

        if (IsWatching)
        {
            Scanned?.Invoke();
        }
    }

    /// <summary>
    /// Les jeux dont au moins un programme tourne. Appelé à côté du fil de l'interface.
    /// </summary>
    private static Dictionary<string, Game> FindRunningGames(List<GameFolder> folders)
    {
        Dictionary<string, Game> found = new Dictionary<string, Game>();

        if (folders.Count == 0)
        {
            return found;
        }

        Process[] processes = Process.GetProcesses();

        try
        {
            foreach (Process process in processes)
            {
                string? exePath = NativeProcesses.GetExecutablePath(process.Id);

                if (exePath is null)
                {
                    continue;
                }

                GameFolder? match = folders.FirstOrDefault(entry =>
                    exePath.StartsWith(entry.Folder, StringComparison.OrdinalIgnoreCase));

                if (match is not null)
                {
                    found.TryAdd(KeyOf(match.Game), match.Game);
                }
            }
        }
        finally
        {
            foreach (Process process in processes)
            {
                process.Dispose();
            }
        }

        return found;
    }

    /// <summary>
    /// Le dossier d'installation sous la forme « D:\Jeux\Hades\ » (avec la barre à la fin,
    /// pour que « D:\Jeux\Hades » ne reconnaisse pas « D:\Jeux\Hades II »).
    /// </summary>
    private static bool TryGetFolder(string? installPath, out string folder)
    {
        folder = "";

        if (string.IsNullOrWhiteSpace(installPath) || !Path.IsPathFullyQualified(installPath))
        {
            return false;
        }

        try
        {
            string trimmed = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installPath));

            // Jamais la racine d'un disque (« D:\ ») : tous les programmes du disque passeraient pour ce jeu.
            if (Path.GetDirectoryName(trimmed) is null)
            {
                return false;
            }

            folder = trimmed + Path.DirectorySeparatorChar;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}
