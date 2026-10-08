namespace Wyrmhold.Core;

/// <summary>Un genre préféré : son nom pour IsThereAnyDeal (anglais), son nom affiché (français), ton temps de jeu.</summary>
public record PreferredGenre(string Name, string DisplayName, int Minutes)
{
    // « Roguelike · 120 h »
    public string Label => $"{DisplayName} · {Minutes / 60} h";
}

/// <summary>
/// Recommandations « Pour toi » :
/// 1. tes genres préférés = les tags Steam de tes jeux, classés par temps de jeu cumulé ;
/// 2. les promos IsThereAnyDeal qui portent au moins un de ces genres, avec de bons avis Steam.
/// </summary>
public class GenreRecommender
{
    // Combien de genres on garde (au-delà, on retombe sur « un peu de tout »).
    public const int GenreCount = 5;

    // Tags Steam qui ne décrivent pas un genre (mode de jeu, style graphique, qualités…) : on les ignore.
    // Noms anglais de Steam. C'est un choix de Wyrmhold, à compléter si un tag inutile remonte.
    private static readonly HashSet<string> NonGenreTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Singleplayer", "Multiplayer", "Co-op", "Online Co-Op", "Local Co-Op", "Local Multiplayer",
        "PvP", "Online PvP", "PvE", "Split Screen",
        "Great Soundtrack", "Atmospheric", "Beautiful", "Colorful", "Cute", "Funny", "Stylized",
        "Realistic", "Cartoony", "Pixel Graphics", "2D", "3D", "Third Person", "First-Person",
        "Indie", "Free to Play", "Early Access", "Controller", "Moddable",
        "Difficult", "Replay Value", "Classic", "Masterpiece", "Addictive", "Family Friendly",
        "Female Protagonist", "Multiple Endings", "Choices Matter", "Character Customization",
        "Violent", "Gore", "Nudity", "Sexual Content", "Mature", "Casual",
        "Team-Based", "Competitive", "eSports", "Massively Multiplayer"
    };

    // Steam range les tags d'un jeu du plus au moins représentatif : on ne compte que les premiers,
    // sinon un jeu très joué « vote » aussi pour des tags secondaires.
    private const int TagsPerGame = 5;

    // Nom français d'un tag → nom anglais. Rempli une fois par session (deux appels à Steam).
    private Dictionary<string, string>? _frenchToEnglish;

    /// <summary>
    /// Tes genres préférés, du plus joué au moins joué (GenreCount au plus).
    /// excluded : les genres décochés par l'utilisateur (noms anglais).
    /// </summary>
    public async Task<List<PreferredGenre>> GetPreferredGenresAsync(IEnumerable<Game> games, IEnumerable<string> excluded)
    {
        Dictionary<string, string> frenchToEnglish = await GetFrenchToEnglishAsync();
        HashSet<string> excludedSet = new HashSet<string>(excluded, StringComparer.OrdinalIgnoreCase);

        // Temps de jeu cumulé par tag. Les tags sont enregistrés en français dans la base (« Rogue-like »…).
        var minutesByTag = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var frenchNameByTag = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (Game game in games.Where(g => g.PlaytimeMinutes > 0))
        {
            foreach (string frenchTag in game.TagList.Take(TagsPerGame))
            {
                // Pas de nom anglais connu : IsThereAnyDeal ne le comprendrait pas, on passe.
                if (!frenchToEnglish.TryGetValue(frenchTag, out string? englishTag)
                    || NonGenreTags.Contains(englishTag)
                    || excludedSet.Contains(englishTag))
                {
                    continue;
                }

                minutesByTag[englishTag] = minutesByTag.GetValueOrDefault(englishTag) + game.PlaytimeMinutes;
                frenchNameByTag.TryAdd(englishTag, frenchTag);
            }
        }

        return minutesByTag
            .OrderByDescending(pair => pair.Value)
            .Take(GenreCount)
            .Select(pair => new PreferredGenre(pair.Key, frenchNameByTag[pair.Key], pair.Value))
            .ToList();
    }

    /// <summary>
    /// Une page de promos qui portent au moins un des genres, filtrées par la qualité (avis Steam).
    /// Forme des filtres vérifiée en console le 8 octobre 2026 (voir taches.md) :
    /// steamPerc a besoin d'un maximum (100), sinon IsThereAnyDeal ne renvoie rien.
    /// </summary>
    public static Task<ItadDealsPage> GetDealsAsync(IsThereAnyDealApi itad, IEnumerable<string> genres,
        int minSteamPercent, int minSteamReviews, int offset, int limit, string sort)
    {
        var quality = new Dictionary<string, object>
        {
            ["type"] = new[] { 1 },                                          // 1 = jeux seulement (pas de DLC)
            ["steamPerc"] = new { min = minSteamPercent, max = 100 },
            ["steamCount"] = new { min = minSteamReviews, max = (int?)null }
        };

        return itad.GetDealsByTagsAsync(genres, matchAll: false, offset, limit, sort, extraFilter: quality);
    }

    /// <summary>
    /// Steam donne un numéro à chaque tag : on récupère sa liste en français et en anglais,
    /// et on relie les deux noms par ce numéro (pas de table de traduction écrite à la main).
    /// </summary>
    private async Task<Dictionary<string, string>> GetFrenchToEnglishAsync()
    {
        if (_frenchToEnglish != null)
        {
            return _frenchToEnglish;
        }

        Dictionary<int, string> french = await SteamStoreApi.GetTagNamesAsync("french");
        Dictionary<int, string> english = await SteamStoreApi.GetTagNamesAsync("english");

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach ((int tagId, string frenchName) in french)
        {
            if (english.TryGetValue(tagId, out string? englishName))
            {
                map.TryAdd(frenchName, englishName);
            }
        }

        _frenchToEnglish = map;
        return map;
    }
}
