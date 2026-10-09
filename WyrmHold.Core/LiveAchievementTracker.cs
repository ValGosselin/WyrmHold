namespace Wyrmhold.Core;

/// <summary>
/// Suit les succès du jeu en cours pendant qu'on joue (overlay activé seulement) :
/// 1. au démarrage du jeu, une lecture complète sert de point de départ ;
/// 2. puis, toutes les 60 s, une lecture légère des succès débloqués, comparée à la précédente ;
/// 3. un nouveau succès → base et badges mis à jour, et l'événement AchievementUnlocked.
/// Ses événements arrivent sur le fil de l'interface (il est piloté par le détecteur de jeu).
/// </summary>
public class LiveAchievementTracker
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(60);

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

        while (!token.IsCancellationRequested)
        {
            // running.Game peut être remplacé par une copie plus récente (rechargement de la bibliothèque).
            Game game = running.Game;

            try
            {
                // null = pas de source pour ce jeu, décochée dans les réglages, ou pas encore prête
                // (Wyrmhold vient de démarrer et n'a pas fini de lire ton compte) : on réessaie dans 60 s.
                IAchievementProvider? provider = await _library.PrepareAchievementSourceAsync(game);

                if (provider is not null)
                {
                    if (known is null)
                    {
                        List<AchievementDetail> start = await provider.GetAchievementsAsync(game, token);

                        if (start.Count == 0)
                        {
                            return;   // jeu sans succès : rien à suivre
                        }

                        known = start.Where(a => a.IsUnlocked).Select(a => a.Id).ToHashSet();
                    }
                    else
                    {
                        HashSet<string> unlocked = await provider.GetUnlockedIdsAsync(game, token);

                        if (!token.IsCancellationRequested && unlocked.Except(known).Any())
                        {
                            await AnnounceNewAchievementsAsync(provider, game, known, token);
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
                await Task.Delay(PollInterval, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>
    /// De nouveaux succès sont débloqués : on relit la liste complète (une seule fois) pour avoir
    /// leur nom, leur description et leur icône en couleur, puis on enregistre et on prévient.
    /// </summary>
    private async Task AnnounceNewAchievementsAsync(
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

        bool wasComplete = game.IsAchievementsComplete;

        known.UnionWith(details.Where(a => a.IsUnlocked).Select(a => a.Id));
        _library.SaveAchievementProgress(game, new AchievementProgress(details.Count(a => a.IsUnlocked), details.Count));

        foreach (AchievementDetail achievement in fresh)
        {
            AchievementUnlocked?.Invoke(game, achievement);
        }

        if (!wasComplete && game.IsAchievementsComplete)
        {
            GameCompleted?.Invoke(game);
        }
    }
}
