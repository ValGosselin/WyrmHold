namespace Wyrmhold.Core;

/// <summary>
/// Suit les succès du jeu en cours pendant qu'on joue (overlay activé seulement) :
/// 1. au démarrage du jeu, une lecture complète sert de point de départ ;
/// 2. Steam : le fichier que le client Steam réécrit AU MOMENT du déblocage est surveillé
///    (Steam\appcache\stats, voir SteamLocalAchievements) → le succès est repéré en 1 à 2 secondes ;
/// 3. une lecture légère par internet, comparée à la précédente : toutes les 15 s pour Epic et GOG (leur
///    seule méthode), toutes les 60 s pour Steam (secours, si le format du fichier change un jour) ;
/// 4. un nouveau succès → base et badges mis à jour, et l'événement AchievementUnlocked.
/// Ses événements arrivent sur le fil de l'interface (il est piloté par le détecteur de jeu).
/// </summary>
public class LiveAchievementTracker
{
    // Lecture par internet : Steam a son fichier local (instantané), internet n'y sert que de secours ;
    // Epic et GOG n'ont qu'internet, donc on les interroge plus souvent (4 petites requêtes par minute de jeu).
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan FastPollInterval = TimeSpan.FromSeconds(15);

    private static TimeSpan OnlineInterval(Game game) =>
        game.Platform == Platform.Steam ? PollInterval : FastPollInterval;

    // Steam réécrit parfois le fichier en plusieurs fois : on attend un court instant avant de le relire.
    private static readonly TimeSpan FileSettleDelay = TimeSpan.FromMilliseconds(400);

    // Après 3 échecs de suite, on arrête pour cette partie (source en panne, jeu inconnu de la source…).
    private const int MaxFailuresInARow = 3;

    private readonly LibraryService _library;

    // Le jeu suivi en ce moment (sa clé), et de quoi arrêter son suivi.
    private string? _trackedKey;
    private CancellationTokenSource? _session;

    // Un succès vient d'être débloqué (le détail contient l'icône en couleur).
    public event Action<Game, AchievementDetail>? AchievementUnlocked;

    // Le jeu vient de passer à 100 %.
    public event Action<Game>? GameCompleted;

    public LiveAchievementTracker(LibraryService library)
    {
        _library = library;

        // Toutes les 5 s (et à la fermeture d'un jeu), on vérifie qu'on suit le bon jeu.
        _library.Watcher.Scanned += () => Follow(_library.Watcher.Current);
        _library.Watcher.GameStopped += (running, end) => Follow(_library.Watcher.Current);
    }

    /// <summary>
    /// Suit ce jeu (s'il n'est pas déjà suivi), ou arrête tout si running vaut null ou si l'overlay est désactivé.
    /// </summary>
    private void Follow(RunningGame? running)
    {
        if (!_library.Settings.OverlayEnabled)
        {
            running = null;
        }

        string? key = running is null ? null : GameWatcher.KeyOf(running.Game);

        if (key == _trackedKey)
        {
            return;
        }

        _session?.Cancel();
        _session = null;
        _trackedKey = key;

        if (running is not null)
        {
            _session = new CancellationTokenSource();
            _ = TrackAsync(running, _session.Token);
        }
    }

    private async Task TrackAsync(RunningGame running, CancellationToken token)
    {
        // Les succès connus au dernier passage (null = point de départ pas encore lu).
        HashSet<string>? known = null;
        int failures = 0;
        bool loggedNoSource = false;
        bool localReadable = true;
        DateTime lastOnlineCheck = DateTime.MinValue;

        Logger.Log($"Succès en direct : début du suivi de {running.Game.Name} ({running.Game.PlatformName}).");

        // Le fichier de Steam prévient (« Release ») ce sémaphore ; la boucle l'attend au lieu de dormir 60 s.
        using var fileChanged = new SemaphoreSlim(0);
        using FileSystemWatcher? fileWatcher = WatchSteamFile(running.Game, fileChanged);

        while (!token.IsCancellationRequested)
        {
            // running.Game peut être remplacé par une copie plus récente (rechargement de la bibliothèque).
            Game game = running.Game;

            try
            {
                // null = pas de source pour ce jeu, décochée dans les réglages, ou pas encore prête
                // (Wyrmhold vient de démarrer et n'a pas fini de lire ton compte) : on réessaie dans 60 s.
                IAchievementProvider? provider = await _library.PrepareAchievementSourceAsync(game);

                // Noté une seule fois par partie (sinon une ligne par minute).
                if (provider is null && !loggedNoSource)
                {
                    loggedNoSource = true;
                    Logger.Log($"Succès en direct : pas de source pour {game.Name} pour l'instant "
                        + "(source décochée, jeu sans succès connus, ou compte pas encore lu) ; nouvel essai chaque minute.");
                }

                if (provider is not null)
                {
                    if (known is null)
                    {
                        List<AchievementDetail> start = await provider.GetAchievementsAsync(game, token);

                        if (start.Count == 0)
                        {
                            Logger.Log($"Succès en direct : {game.Name} n'a aucun succès, suivi arrêté.");
                            return;   // jeu sans succès : rien à suivre
                        }

                        known = start.Where(a => a.IsUnlocked).Select(a => a.Id).ToHashSet();

                        // Le disque peut avoir un succès que la Web API ne voit pas encore : il fait partie du départ,
                        // sinon il serait annoncé comme nouveau alors qu'il date d'avant le lancement du jeu.
                        if (localReadable && ReadLocal(game, ref localReadable) is { } local)
                        {
                            known.UnionWith(local.Where(a => a.IsUnlocked).Select(a => a.Id));
                        }

                        lastOnlineCheck = DateTime.UtcNow;
                        Logger.Log($"Succès en direct : point de départ pour {game.Name} : {known.Count}/{start.Count} débloqués"
                            + (fileWatcher is null ? "." : " ; fichier local de Steam surveillé."));
                    }
                    else
                    {
                        // 1. Le disque (Steam) : instantané.
                        if (localReadable && ReadLocal(game, ref localReadable) is { } local)
                        {
                            List<AchievementDetail> fresh = local
                                .Where(a => a.IsUnlocked && !known.Contains(a.Id))
                                .OrderBy(a => a.UnlockedUnix)
                                .Select(a => a.ToDetail())
                                .ToList();

                            if (fresh.Count > 0)
                            {
                                Announce(game, fresh, local.Count(a => a.IsUnlocked), local.Count, known, "fichier local de Steam");
                            }
                        }

                        // 2. Internet : toutes les 15 s pour Epic et GOG (leur seule méthode), toutes les 60 s pour Steam (secours).
                        if (DateTime.UtcNow - lastOnlineCheck >= OnlineInterval(game))
                        {
                            lastOnlineCheck = DateTime.UtcNow;
                            HashSet<string> unlocked = await provider.GetUnlockedIdsAsync(game, token);

                            if (!token.IsCancellationRequested && unlocked.Except(known).Any())
                            {
                                await AnnounceFromOnlineAsync(provider, game, known, token);
                            }
                        }
                    }
                }

                failures = 0;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return;
            }
            catch (AchievementSourceUnavailableException ex)
            {
                Logger.Log($"Succès en direct arrêtés pour {game.Name} : {ex.Message}");
                return;
            }
            catch (Exception ex)
            {
                // Erreur passagère (réseau…) : on réessaie la minute suivante, 3 fois au plus.
                Logger.Log($"Succès en direct illisibles pour {game.Name} : {ex.Message}");

                if (++failures >= MaxFailuresInARow)
                {
                    return;
                }
            }

            try
            {
                // On attend 60 s (15 s pour Epic et GOG)… ou moins, si Steam réécrit son fichier entre-temps.
                if (await fileChanged.WaitAsync(OnlineInterval(running.Game), token))
                {
                    await Task.Delay(FileSettleDelay, token);

                    // Plusieurs écritures de suite = une seule lecture.
                    while (fileChanged.CurrentCount > 0)
                    {
                        fileChanged.Wait(0);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Surveille le fichier de succès du jeu dans Steam\appcache\stats (jeux Steam seulement).
    /// null si ce n'est pas possible : on garde alors la lecture par internet toutes les 60 s.
    /// </summary>
    private FileSystemWatcher? WatchSteamFile(Game game, SemaphoreSlim signal)
    {
        SteamLocalAchievements? local = _library.SteamLocalStats;

        if (game.Platform != Platform.Steam || local is null || string.IsNullOrEmpty(_library.SteamId)
            || !Directory.Exists(local.StatsFolder))
        {
            return null;
        }

        try
        {
            var watcher = new FileSystemWatcher(local.StatsFolder, SteamLocalAchievements.UserStatsFileName(_library.SteamId, game.PlatformGameId))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName
            };

            // Ces événements arrivent sur un autre fil : on se contente de réveiller la boucle.
            FileSystemEventHandler wake = (sender, e) => Wake(signal);
            watcher.Changed += wake;
            watcher.Created += wake;
            watcher.Renamed += (sender, e) => Wake(signal);   // écriture dans un fichier temporaire, puis renommage
            watcher.EnableRaisingEvents = true;
            return watcher;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or FormatException or OverflowException)
        {
            Logger.Log($"Succès en direct : surveillance du fichier Steam impossible pour {game.Name} : {ex.Message}");
            return null;
        }
    }

    private static void Wake(SemaphoreSlim signal)
    {
        try
        {
            signal.Release();
        }
        catch (ObjectDisposedException)
        {
            // Le suivi vient de s'arrêter : rien à réveiller.
        }
    }

    /// <summary>
    /// Les succès lus sur le disque (Steam), ou null. Un fichier illisible (format changé par Valve) coupe
    /// la lecture locale pour cette partie (readable = false) : la Web API prend le relais.
    /// </summary>
    private List<LocalAchievement>? ReadLocal(Game game, ref bool readable)
    {
        SteamLocalAchievements? local = _library.SteamLocalStats;

        if (game.Platform != Platform.Steam || local is null || string.IsNullOrEmpty(_library.SteamId))
        {
            return null;
        }

        try
        {
            List<LocalAchievement>? achievements = local.Read(_library.SteamId, game.PlatformGameId);
            return achievements is { Count: > 0 } ? achievements : null;
        }
        catch (IOException)
        {
            return null;   // Steam est en train d'écrire : la prochaine écriture (ou la minute suivante) le relira
        }
        catch (Exception ex)
        {
            readable = false;
            Logger.Log($"Succès en direct : fichier local de Steam illisible pour {game.Name} ({ex.Message}) ; lecture par internet seulement.");
            return null;
        }
    }

    /// <summary>
    /// La lecture par internet a vu de nouveaux succès : on relit la liste complète (une seule fois)
    /// pour avoir leur nom, leur description et leur icône en couleur.
    /// </summary>
    private async Task AnnounceFromOnlineAsync(
        IAchievementProvider provider, Game game, HashSet<string> known, CancellationToken token)
    {
        List<AchievementDetail> details = await provider.GetAchievementsAsync(game, token);

        if (token.IsCancellationRequested)
        {
            return;
        }

        List<AchievementDetail> fresh = details
            .Where(a => a.IsUnlocked && !known.Contains(a.Id))
            .OrderBy(a => a.UnlockedUnix)
            .ToList();

        known.UnionWith(details.Where(a => a.IsUnlocked).Select(a => a.Id));
        Announce(game, fresh, details.Count(a => a.IsUnlocked), details.Count, known, "internet");
    }

    /// <summary>Enregistre la nouvelle progression, puis prévient (overlay, notification, 100 %).</summary>
    private void Announce(Game game, List<AchievementDetail> fresh, int unlockedCount, int total, HashSet<string> known, string source)
    {
        bool wasComplete = game.IsAchievementsComplete;

        known.UnionWith(fresh.Select(a => a.Id));
        _library.SaveAchievementProgress(game, new AchievementProgress(unlockedCount, total));

        foreach (AchievementDetail achievement in fresh)
        {
            // Le délai entre le déblocage (selon la source) et maintenant : montre si la source répond en retard.
            // (0 = date inconnue.)
            long delay = achievement.UnlockedUnix > 0
                ? DateTimeOffset.UtcNow.ToUnixTimeSeconds() - achievement.UnlockedUnix
                : -1;
            Logger.Log($"Succès en direct : « {achievement.Name} » débloqué dans {game.Name}"
                + (delay >= 0 ? $", repéré {delay} s après le déblocage" : "")
                + $" ({source}).");

            AchievementUnlocked?.Invoke(game, achievement);
        }

        if (!wasComplete && game.IsAchievementsComplete)
        {
            GameCompleted?.Invoke(game);
        }
    }
}
