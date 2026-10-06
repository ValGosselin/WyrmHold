namespace Wyrmhold.Core;

/// <summary>
/// Une ligne de graphique en barres : un libellé, une valeur affichée,
/// et la longueur de la barre entre 0 (vide) et 1 (pleine).
/// </summary>
public record StatBar(string Label, string ValueText, double Ratio);

/// <summary>
/// Les chiffres de la page Statistiques, calculés à partir de la bibliothèque.
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
    public List<StatBar> GamesByPlatform { get; private set; } = new List<StatBar>();
    public List<StatBar> PlaytimeByPlatform { get; private set; } = new List<StatBar>();
    public List<StatBar> PlaytimeByWeek { get; private set; } = new List<StatBar>();

    public static LibraryStatistics Compute(List<Game> games, List<(long StartedUnix, long EndedUnix)> sessions)
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

        // Top 10 des jeux les plus joués.
        List<Game> topGames = games
            .Where(g => g.PlaytimeMinutes > 0)
            .OrderByDescending(g => g.PlaytimeMinutes)
            .Take(10)
            .ToList();

        stats.TopGames = ToBars(topGames.Select(g => (g.Name, (long)g.PlaytimeMinutes)), FormatDuration);

        // Répartition par plateforme.
        List<IGrouping<string, Game>> platforms = games.GroupBy(g => g.PlatformName).ToList();

        stats.GamesByPlatform = ToBars(
            platforms.Select(group => (group.Key, (long)group.Count())).OrderByDescending(p => p.Item2),
            count => $"{count} jeu(x)");

        stats.PlaytimeByPlatform = ToBars(
            platforms.Select(group => (group.Key, group.Sum(g => (long)g.PlaytimeMinutes))).OrderByDescending(p => p.Item2),
            FormatDuration);

        // Temps de jeu par semaine, d'après les sessions enregistrées par Wyrmhold.
        stats.PlaytimeByWeek = ToBars(SumMinutesByWeek(sessions), FormatDuration);

        return stats;
    }

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

        return rest == 0 || hours >= 10 ? $"{hours} h" : $"{hours} h {rest:00}";
    }
}