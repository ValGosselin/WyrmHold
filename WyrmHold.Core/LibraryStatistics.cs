using System.Globalization;

namespace Wyrmhold.Core;

/// <summary>
/// Une ligne de graphique en barres : un libellé, une valeur affichée,
/// et la longueur de la barre entre 0 (vide) et 1 (pleine).
/// </summary>
public record StatBar(string Label, string ValueText, double Ratio)
{
    // La jaquette du jeu (Top 10 seulement ; null ailleurs).
    public string? CoverPath { get; init; }
}

/// <summary>
/// Une part d'un anneau : un libellé, sa valeur, son pourcentage du total,
/// et le nom de la couleur à chercher dans le thème (ex. « PlatformSteamBrush »).
/// </summary>
public record StatSlice(string Label, double Value, string ValueText, double Percent, string BrushKey)
{
    public string PercentText => Percent >= 0.1 || Percent == 0 ? $"{Percent:0.#} %" : "< 0,1 %";
}

/// <summary>
/// Une colonne de l'histogramme « Complétion des jeux » : une tranche de pourcentage et le nombre de jeux dedans.
/// </summary>
public record CompletionBucket(string Label, int Count, double Ratio, bool IsPerfect);

/// <summary>
/// Une colonne du graphique de rareté : un niveau (« Rare », « 5-10 % ») et combien de tes succès y sont.
/// Level : 0 = très commun … 5 = ultra rare (sert à la couleur, du bleu vers le doré).
/// </summary>
public record RarityBucket(string Name, string Range, int Count, double Ratio, int Level);

/// <summary>
/// Une ligne de « Tes succès les plus rares » : le succès, son jeu, le % de joueurs qui l'ont et sa date.
/// </summary>
public record RareUnlock(string Name, Game Game, double RarityPercent, long UnlockedUnix)
{
    public string GameName => Game.Name;
    public string? CoverPath => Game.CoverPath;
    public string SourceName => Game.SourceName;

    // « 0,4 % » ; sous 0,1 %, deux décimales pour ne pas afficher « 0 % ».
    public string RarityText => RarityPercent < 0.1
        ? $"{RarityPercent.ToString("0.0#", CultureInfo.GetCultureInfo("fr-FR"))} %"
        : $"{RarityPercent.ToString("0.#", CultureInfo.GetCultureInfo("fr-FR"))} %";

    public string DateText => UnlockedUnix > 0
        ? DateTimeOffset.FromUnixTimeSeconds(UnlockedUnix).LocalDateTime.ToString("dd/MM/yyyy")
        : "";
}

/// <summary>
/// Une ligne de la liste « Jeux récents » : un jeu et la date de son dernier succès débloqué.
/// </summary>
public record RecentAchievementGame(Game Game, DateTime LastUnlock)
{
    public string Name => Game.Name;
    public string? CoverPath => Game.CoverPath;
    public string SourceName => Game.SourceName;
    public double Ratio => Game.AchievementsTotal == 0 ? 0 : Math.Min(1, (double)Game.AchievementsUnlocked / Game.AchievementsTotal);
    public string ProgressText => $"{Game.AchievementsUnlocked} / {Game.AchievementsTotal}";
    public bool IsPerfect => Game.AchievementsTotal > 0 && Game.AchievementsUnlocked >= Game.AchievementsTotal;
    public string PlaytimeText => Game.PlaytimeMinutes > 0 ? LibraryStatistics.FormatDuration(Game.PlaytimeMinutes) : "—";
    public string LastUnlockText => LastUnlock.ToString("dd/MM/yyyy HH:mm");
}

/// <summary>
/// Les chiffres de la page Statistiques, calculés à partir de la bibliothèque.
/// Chaque copie compte pour un jeu (un jeu sur Steam et sur Epic = 2 jeux, chacun avec ses succès : choix de Val du 15 novembre 2026).
/// </summary>
public class LibraryStatistics
{
    public const int WeekCount = 12;

    public int GameCount { get; private set; }
    public int InstalledCount { get; private set; }
    public int PlayedCount { get; private set; }
    public int NeverPlayedCount { get; private set; }
    public int FavoriteCount { get; private set; }
    public string TotalPlaytimeText { get; private set; } = "";

    public List<StatBar> TopGames { get; private set; } = new List<StatBar>();
    public List<StatSlice> GamesByPlatform { get; private set; } = new List<StatSlice>();
    public List<StatSlice> PlaytimeByPlatform { get; private set; } = new List<StatSlice>();
    public List<StatBar> PlaytimeByWeek { get; private set; } = new List<StatBar>();

    // ----- Succès -----

    // Total des succès débloqués, toutes plateformes.
    public int AchievementsUnlocked { get; private set; }

    // Jeux qui ont des succès, et ce qu'on en a fait.
    public int GamesWithAchievements { get; private set; }
    public int StartedCount { get; private set; }       // au moins 1 succès
    public int InProgressCount { get; private set; }    // commencés, pas à 100 %
    public int UntouchedCount { get; private set; }     // aucun succès débloqué
    public int PerfectCount { get; private set; }       // 100 %

    // Succès qui restent à débloquer : dans les jeux commencés, dans les jeux jamais touchés.
    public int RemainingInStarted { get; private set; }
    public int RemainingInUntouched { get; private set; }

    // Complétion moyenne et médiane, en %, sur les jeux à succès lancés au moins une fois (choix de Val du 15 novembre 2026).
    public int CompletionGameCount { get; private set; }
    public double AverageCompletion { get; private set; }
    public double MedianCompletion { get; private set; }

    // L'anneau de la carte « Complétion moyenne » : la part faite et la part restante.
    public List<StatSlice> CompletionRing { get; private set; } = new List<StatSlice>();

    // Combien de jeux (les mêmes que pour la moyenne) dans chaque tranche de complétion.
    public List<CompletionBucket> CompletionBuckets { get; private set; } = new List<CompletionBucket>();

    // ----- Temps de jeu (jeux joués seulement) -----

    public string AveragePlaytimeText { get; private set; } = "";
    public string MedianPlaytimeText { get; private set; } = "";

    // Les textes du panneau de chiffres, déjà mis en forme (« 40,4 % », « 4 189 »).
    public string AchievementsUnlockedText => FormatCount(AchievementsUnlocked);
    public string AverageCompletionText => $"{AverageCompletion.ToString("0.##", French)} %";
    public string AverageCompletionRoundText => $"{Math.Floor(AverageCompletion).ToString(French)}";
    public string MedianCompletionText => $"{MedianCompletion.ToString("0.##", French)} %";
    public string RemainingInStartedText => FormatCount(RemainingInStarted);
    public string RemainingInUntouchedText => FormatCount(RemainingInUntouched);

    private static readonly System.Globalization.CultureInfo French = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");

    // Les tranches de l'histogramme : [début, fin[ en %. La dernière (100 %) est à part.
    private static readonly (double From, double To, string Label)[] Buckets =
    {
        (0, 5, "0-5"), (5, 10, "5-10"), (10, 20, "10-20"), (20, 30, "20-30"), (30, 40, "30-40"),
        (40, 50, "40-50"), (50, 60, "50-60"), (60, 70, "60-70"), (70, 80, "70-80"), (80, 90, "80-90"),
        (90, 100, "90-99")
    };

    /// <param name="mergeCopies">Réglage « regrouper les copies » : le Top 10 additionne alors les copies d'un même jeu
    /// (Rocket League Steam + Epic = une ligne, demande de Val du 15 novembre 2026). Le reste de la page compte chaque copie.</param>
    /// <param name="unlocks">L'historique des succès (voir GameDatabase.LoadAchievementUnlocks). null = pas d'historique.</param>
    /// <param name="hasDatedAchievements">Vrai si la source des succès d'un jeu donne les dates (pas les succès maison).</param>
    public static LibraryStatistics Compute(
        List<Game> games,
        List<(long StartedUnix, long EndedUnix)> sessions,
        bool mergeCopies = true,
        List<StoredUnlock>? unlocks = null,
        Func<Game, bool>? hasDatedAchievements = null)
    {
        LibraryStatistics stats = new LibraryStatistics
        {
            GameCount = games.Count,
            InstalledCount = games.Count(g => g.IsInstalled),
            PlayedCount = games.Count(g => g.PlaytimeMinutes > 0),
            NeverPlayedCount = games.Count(g => g.PlaytimeMinutes == 0 && g.LastPlayedUnix == 0),
            FavoriteCount = games.Count(g => g.IsFavorite),
            TotalPlaytimeText = FormatDuration(games.Sum(g => (long)g.PlaytimeMinutes))
        };

        // Top 10 des jeux les plus joués, avec leur jaquette. Avec le regroupement, les copies d'un même jeu
        // (même clé que dans la bibliothèque) ne font qu'une ligne : nom et jaquette de la copie la plus jouée, temps additionnés.
        List<(Game Main, long Minutes)> topGames = games
            .Where(g => g.PlaytimeMinutes > 0)
            .GroupBy(g => mergeCopies && g.GroupKey.Length > 0 ? g.GroupKey : $"{g.Platform}|{g.PlatformGameId}")
            .Select(group => (Main: group.MaxBy(g => g.PlaytimeMinutes)!, Minutes: group.Sum(g => (long)g.PlaytimeMinutes)))
            .OrderByDescending(top => top.Minutes)
            .Take(10)
            .ToList();

        List<StatBar> topBars = ToBars(topGames.Select(top => (top.Main.Name, top.Minutes)), FormatDuration);
        stats.TopGames = topBars.Select((bar, i) => bar with { CoverPath = topGames[i].Main.CoverPath }).ToList();

        // Répartition par plateforme (la console pour un jeu émulé), avec la couleur de chacune.
        List<IGrouping<string, Game>> platforms = games.GroupBy(g => g.FilterKey).ToList();

        stats.GamesByPlatform = ToSlices(
            platforms.Select(group => (group.First().SourceName, group.Key, (double)group.Count())),
            count => $"{FormatCount((int)count)} jeu(x)");

        stats.PlaytimeByPlatform = ToSlices(
            platforms.Select(group => (group.First().SourceName, group.Key, (double)group.Sum(g => (long)g.PlaytimeMinutes))),
            minutes => FormatDuration((long)minutes));

        // Temps de jeu par semaine, d'après les sessions enregistrées par Wyrmhold.
        stats.PlaytimeByWeek = ToBars(SumMinutesByWeek(sessions), FormatDuration);

        stats.ComputeAchievements(games);
        stats.ComputePlaytime(games);
        stats.ComputeHistory(games, unlocks ?? new List<StoredUnlock>(), hasDatedAchievements ?? (_ => true));
        stats.ComputeRarity(games, unlocks ?? new List<StoredUnlock>());

        return stats;
    }

    // ----- Rareté des succès (étape 3) -----

    // Les niveaux de rareté (choix de Val du 15 novembre 2026), du plus commun au plus rare : [début, fin[ en % de joueurs.
    private static readonly (double From, double To, string Name, string Range)[] RarityLevels =
    {
        (50, double.MaxValue, "Très commun", "≥ 50 %"),
        (25, 50, "Commun", "25-50 %"),
        (10, 25, "Peu commun", "10-25 %"),
        (5, 10, "Rare", "5-10 %"),
        (1, 5, "Très rare", "1-5 %"),
        (0, 1, "Ultra rare", "< 1 %")
    };

    // Le graphique de rareté : combien de tes succès dans chaque niveau.
    public List<RarityBucket> RarityBuckets { get; private set; } = new List<RarityBucket>();

    // Tes succès dont la rareté est connue, et leur rareté moyenne (% de joueurs qui les ont).
    public int RarityKnownCount { get; private set; }
    public string AverageRarityText { get; private set; } = "—";
    public string RarityKnownText => $"{FormatCount(RarityKnownCount)} succès dont la rareté est connue, selon le % de joueurs qui les ont";

    // Tes succès les plus rares.
    public List<RareUnlock> RarestUnlocks { get; private set; } = new List<RareUnlock>();

    public const int RarestCount = 10;

    private void ComputeRarity(List<Game> games, List<StoredUnlock> unlocks)
    {
        Dictionary<string, Game> gamesByKey = games
            .GroupBy(GameDatabase.GameKey)
            .ToDictionary(group => group.Key, group => group.First());

        // Les succès des jeux encore dans la bibliothèque, avec une rareté connue.
        List<(StoredUnlock Unlock, Game Game, double Rarity)> known = unlocks
            .Where(u => u.RarityPercent is not null && gamesByKey.ContainsKey(u.GameKey))
            .Select(u => (u, gamesByKey[u.GameKey], u.RarityPercent!.Value))
            .ToList();

        RarityKnownCount = known.Count;
        AverageRarityText = known.Count == 0 ? "—" : $"{known.Average(k => k.Rarity).ToString("0.#", French)} %";

        List<(string Name, string Range, int Count)> counts = RarityLevels
            .Select(level => (level.Name, level.Range, known.Count(k => k.Rarity >= level.From && k.Rarity < level.To)))
            .ToList();

        int max = counts.Count == 0 ? 0 : counts.Max(c => c.Count);
        RarityBuckets = counts
            .Select((c, index) => new RarityBucket(c.Name, c.Range, c.Count, max == 0 ? 0 : (double)c.Count / max, index))
            .ToList();

        // Les plus rares d'abord ; à rareté égale, le plus récent.
        RarestUnlocks = known
            .OrderBy(k => k.Rarity)
            .ThenByDescending(k => k.Unlock.UnlockedUnix)
            .Take(RarestCount)
            .Select(k => new RareUnlock(k.Unlock.Name ?? k.Unlock.AchievementId, k.Game, k.Rarity, k.Unlock.UnlockedUnix))
            .ToList();
    }

    // ----- Historique des succès (étape 2) -----

    // Le calendrier d'une année, une case par jour.
    public AchievementCalendar Calendar { get; private set; } = new AchievementCalendar(new Dictionary<DateTime, int>());

    // Record sur une journée, et moyenne par jour où au moins un succès a été débloqué.
    public int BestDayCount { get; private set; }
    public string BestDayText { get; private set; } = "—";
    public string DailyAverageText { get; private set; } = "—";

    // Remplissage de l'historique : jeux commencés dont on a les dates, sur ceux qui peuvent en avoir.
    public int HistoryReadCount { get; private set; }
    public int HistoryGameCount { get; private set; }
    public bool IsHistoryIncomplete => HistoryReadCount < HistoryGameCount;
    public string HistoryStatusText => $"Historique en cours de remplissage : {HistoryReadCount} jeu(x) lu(s) sur {HistoryGameCount}. "
        + "Les succès des autres apparaîtront à la prochaine lecture des succès.";

    // Les jeux du dernier succès débloqué (le plus récent en premier).
    public List<RecentAchievementGame> RecentGames { get; private set; } = new List<RecentAchievementGame>();

    public const int RecentGameCount = 10;

    private void ComputeHistory(List<Game> games, List<StoredUnlock> unlocks, Func<Game, bool> hasDatedAchievements)
    {
        // Seuls les jeux encore dans la bibliothèque comptent (un jeu masqué garde ses lignes en base).
        Dictionary<string, Game> gamesByKey = games
            .GroupBy(GameDatabase.GameKey)
            .ToDictionary(group => group.Key, group => group.First());

        List<(Game Game, DateTime When)> dated = unlocks
            .Where(u => u.UnlockedUnix > 0 && gamesByKey.ContainsKey(u.GameKey))
            .Select(u => (gamesByKey[u.GameKey], DateTimeOffset.FromUnixTimeSeconds(u.UnlockedUnix).LocalDateTime))
            .ToList();

        // Par jour (date locale) : le calendrier, le record et la moyenne.
        Dictionary<DateTime, int> perDay = dated
            .GroupBy(d => d.When.Date)
            .ToDictionary(group => group.Key, group => group.Count());

        Calendar = new AchievementCalendar(perDay);

        if (perDay.Count > 0)
        {
            KeyValuePair<DateTime, int> best = perDay.OrderByDescending(day => day.Value).ThenByDescending(day => day.Key).First();
            BestDayCount = best.Value;
            BestDayText = best.Key.ToString("dd/MM/yyyy", French);
            DailyAverageText = ((double)dated.Count / perDay.Count).ToString("0.##", French);
        }

        // Où en est le remplissage (la lecture complète unique se fait en arrière-plan).
        HashSet<string> withHistory = unlocks.Select(u => u.GameKey).ToHashSet();
        List<Game> canHaveHistory = games.Where(g => g.AchievementsUnlocked > 0 && hasDatedAchievements(g)).ToList();
        HistoryGameCount = canHaveHistory.Count;
        HistoryReadCount = canHaveHistory.Count(g => withHistory.Contains(GameDatabase.GameKey(g)));

        // Jeux récents : le dernier succès de chaque jeu, du plus récent au plus ancien.
        RecentGames = dated
            .GroupBy(d => d.Game)
            .Select(group => (Game: group.Key, Last: group.Max(d => d.When)))
            .OrderByDescending(entry => entry.Last)
            .Take(RecentGameCount)
            .Select(entry => new RecentAchievementGame(entry.Game, entry.Last))
            .ToList();
    }

    private void ComputeAchievements(List<Game> games)
    {
        List<Game> withAchievements = games.Where(g => g.AchievementsTotal > 0).ToList();

        AchievementsUnlocked = games.Sum(g => g.AchievementsUnlocked);
        GamesWithAchievements = withAchievements.Count;
        StartedCount = withAchievements.Count(g => g.AchievementsUnlocked > 0);
        PerfectCount = withAchievements.Count(IsPerfect);
        InProgressCount = StartedCount - PerfectCount;
        UntouchedCount = GamesWithAchievements - StartedCount;

        RemainingInStarted = withAchievements
            .Where(g => g.AchievementsUnlocked > 0)
            .Sum(g => Math.Max(0, g.AchievementsTotal - g.AchievementsUnlocked));
        RemainingInUntouched = withAchievements
            .Where(g => g.AchievementsUnlocked == 0)
            .Sum(g => g.AchievementsTotal);

        // La moyenne ne compte que les jeux lancés au moins une fois : un jeu jamais ouvert ne dit rien de ta façon de jouer.
        // Un succès débloqué prouve aussi que le jeu a été lancé (certaines plateformes ne donnent pas le temps de jeu).
        List<double> completions = withAchievements
            .Where(g => g.PlaytimeMinutes > 0 || g.LastPlayedUnix > 0 || g.AchievementsUnlocked > 0)
            .Select(CompletionPercent)
            .ToList();

        CompletionGameCount = completions.Count;
        AverageCompletion = completions.Count == 0 ? 0 : completions.Average();
        MedianCompletion = Median(completions);

        CompletionRing = new List<StatSlice>
        {
            new StatSlice("Fait", AverageCompletion, "", AverageCompletion, "CompleteTextBrush"),
            new StatSlice("Reste", 100 - AverageCompletion, "", 100 - AverageCompletion, "SubtleBrush")
        };

        // L'histogramme : une colonne par tranche, puis 100 %.
        List<(string Label, int Count, bool IsPerfect)> counts = Buckets
            .Select(b => (b.Label, completions.Count(c => c >= b.From && c < b.To), false))
            .ToList();
        counts.Add(("100", completions.Count(c => c >= 100), true));

        int max = counts.Count == 0 ? 0 : counts.Max(c => c.Count);
        CompletionBuckets = counts
            .Select(c => new CompletionBucket(c.Label, c.Count, max == 0 ? 0 : (double)c.Count / max, c.IsPerfect))
            .ToList();
    }

    private void ComputePlaytime(List<Game> games)
    {
        List<double> minutes = games.Where(g => g.PlaytimeMinutes > 0).Select(g => (double)g.PlaytimeMinutes).ToList();

        AveragePlaytimeText = minutes.Count == 0 ? "—" : FormatDuration((long)minutes.Average());
        MedianPlaytimeText = minutes.Count == 0 ? "—" : FormatDuration((long)Median(minutes));
    }

    private static bool IsPerfect(Game game) => game.AchievementsTotal > 0 && game.AchievementsUnlocked >= game.AchievementsTotal;

    // Un jeu ne dépasse jamais 100 %, même si la plateforme compte un succès en trop.
    private static double CompletionPercent(Game game) =>
        Math.Min(100, 100.0 * game.AchievementsUnlocked / game.AchievementsTotal);

    private static double Median(List<double> values)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        List<double> sorted = values.OrderBy(v => v).ToList();
        int middle = sorted.Count / 2;

        // Nombre pair de valeurs : la médiane est entre les deux du milieu.
        return sorted.Count % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
    }

    private static string FormatCount(int count) => count.ToString("N0", French);

    /// <summary>
    /// Transforme des triplets (libellé, plateforme, valeur) en parts d'anneau, de la plus grosse à la plus petite.
    /// Les parts à 0 sont retirées : elles ne se verraient pas.
    /// </summary>
    private static List<StatSlice> ToSlices(IEnumerable<(string Label, string FilterKey, double Value)> values, Func<double, string> formatValue)
    {
        List<(string Label, string FilterKey, double Value)> list = values.Where(v => v.Value > 0).OrderByDescending(v => v.Value).ToList();
        double total = list.Sum(v => v.Value);

        return list
            .Select(v => new StatSlice(v.Label, v.Value, formatValue(v.Value), total == 0 ? 0 : 100 * v.Value / total, PlatformBrushKey(v.FilterKey)))
            .ToList();
    }

    /// <summary>
    /// Le nom de la couleur d'une plateforme ou d'une console dans les palettes (Themes/*.xaml) : « PlatformSteamBrush ».
    /// </summary>
    public static string PlatformBrushKey(string filterKey) => $"Platform{filterKey}Brush";

    /// <summary>
    /// Transforme des couples (libellé, valeur) en barres : la plus grande valeur fait une barre pleine.
    /// </summary>
    private static List<StatBar> ToBars(IEnumerable<(string Label, long Value)> values, Func<long, string> formatValue)
    {
        List<(string Label, long Value)> list = values.ToList();
        long max = list.Count == 0 ? 0 : list.Max(v => v.Value);

        return list
            .Select(v => new StatBar(v.Label, formatValue(v.Value), max == 0 ? 0 : (double)v.Value / max))
            .ToList();
    }

    private static List<(string Label, long Minutes)> SumMinutesByWeek(List<(long StartedUnix, long EndedUnix)> sessions)
    {
        DateTime thisMonday = MondayOf(DateTime.Today);
        List<(string Label, long Minutes)> weeks = new List<(string, long)>();

        // De la plus ancienne semaine (il y a 11 semaines) à la semaine en cours.
        for (int i = WeekCount - 1; i >= 0; i--)
        {
            DateTime monday = thisMonday.AddDays(-7 * i);
            DateTime nextMonday = monday.AddDays(7);

            long minutes = sessions
                .Where(s => IsBetween(s.StartedUnix, monday, nextMonday))
                .Sum(s => (s.EndedUnix - s.StartedUnix) / 60);

            weeks.Add(($"Semaine du {monday:dd/MM}", minutes));
        }

        return weeks;
    }

    private static bool IsBetween(long unixSeconds, DateTime start, DateTime end)
    {
        DateTime date = DateTimeOffset.FromUnixTimeSeconds(unixSeconds).LocalDateTime;
        return date >= start && date < end;
    }

    private static DateTime MondayOf(DateTime date)
    {
        // DayOfWeek compte à partir du dimanche (0) : on décale pour que lundi = 0.
        int daysSinceMonday = ((int)date.DayOfWeek + 6) % 7;
        return date.Date.AddDays(-daysSinceMonday);
    }

    public static string FormatDuration(long minutes)
    {
        if (minutes < 60)
        {
            return $"{minutes} min";
        }

        long hours = minutes / 60;
        long rest = minutes % 60;

        // « 9 626 h » plutôt que « 9626 h » : espace des milliers, à la française.
        return rest == 0 || hours >= 10 ? $"{hours.ToString("N0", French)} h" : $"{hours} h {rest:00}";
    }
}