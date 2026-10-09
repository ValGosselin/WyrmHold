using System.Globalization;
using System.Net;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using System.Text.Json;

namespace Wyrmhold.Core;

public class SteamWebApi
{
    private static readonly HttpClient Http = new HttpClient();
    public static bool TryParseLoginCookie(string? cookieValue, out string steamId, out string accessToken)
    {
        steamId = "";
        accessToken = "";

        if (string.IsNullOrEmpty(cookieValue))
        {
            return false;
        }

        string[] parts = Uri.UnescapeDataString(cookieValue).Split("||");

        if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0)
        {
            return false;
        }

        steamId = parts[0];
        accessToken = parts[1];
        return true;
    }

    private readonly Secrets _secrets;

    public SteamWebApi(Secrets secrets)
    {
        _secrets = secrets;
    }

    public bool IsConfigured =>
        !string.IsNullOrEmpty(_secrets.SteamApiKey) && !string.IsNullOrEmpty(_secrets.SteamId);

    public Task<List<SteamOwnedGame>> GetOwnedGamesAsync()
    {
        return GetGamesAsync("GetOwnedGames", "&include_appinfo=true&include_played_free_games=true");
    }

    public Task<List<SteamOwnedGame>> GetRecentlyPlayedGamesAsync()
    {
        return GetGamesAsync("GetRecentlyPlayedGames", "");
    }

    private async Task<List<SteamOwnedGame>> GetGamesAsync(string method, string extraParameters)
    {
        if (!IsConfigured)
        {
            return new List<SteamOwnedGame>();
        }

        string url = $"https://api.steampowered.com/IPlayerService/{method}/v1/"
            + $"?key={_secrets.SteamApiKey}&steamid={_secrets.SteamId}"
            + extraParameters;

        string json = await Http.GetStringAsync(url);
        SteamOwnedGamesResponse? result = JsonSerializer.Deserialize<SteamOwnedGamesResponse>(json);

        return result?.Response?.Games ?? new List<SteamOwnedGame>();
    }
    public async Task<List<SharedLibraryApp>> GetFamilyLibraryAsync(string accessToken)
    {
        string token = Uri.EscapeDataString(accessToken);

        string groupJson = await Http.GetStringAsync(
            $"https://api.steampowered.com/IFamilyGroupsService/GetFamilyGroupForUser/v1/?access_token={token}");

        FamilyGroupData? family = JsonSerializer.Deserialize<FamilyGroupResponse>(groupJson)?.Response;

        if (family is null || family.IsNotMemberOfAnyGroup || string.IsNullOrEmpty(family.FamilyGroupId))
        {
            return new List<SharedLibraryApp>();
        }

        string libraryJson = await Http.GetStringAsync(
            "https://api.steampowered.com/IFamilyGroupsService/GetSharedLibraryApps/v1/"
            + $"?access_token={token}&family_groupid={family.FamilyGroupId}&include_own=false");

        return JsonSerializer.Deserialize<SharedLibraryResponse>(libraryJson)?.Response?.Apps
            ?? new List<SharedLibraryApp>();
    }

    // ----- Succès -----

    private const string Language = "french";

    public async Task<AchievementProgress> GetAchievementProgressAsync(string appId, CancellationToken cancellationToken = default)
    {
        List<SteamPlayerAchievement> achievements = await GetPlayerAchievementsAsync(appId, null, cancellationToken);
        int unlocked = achievements.Count(a => a.Achieved == 1);
        return new AchievementProgress(unlocked, achievements.Count);
    }

    /// <summary>
    /// Les identifiants (apiname) des succès débloqués : un seul appel, pour le suivi en direct.
    /// </summary>
    public async Task<HashSet<string>> GetUnlockedAchievementIdsAsync(string appId, CancellationToken cancellationToken = default)
    {
        List<SteamPlayerAchievement> achievements = await GetPlayerAchievementsAsync(appId, null, cancellationToken);

        return achievements
            .Where(a => a.Achieved == 1 && !string.IsNullOrEmpty(a.ApiName))
            .Select(a => a.ApiName!)
            .ToHashSet();
    }

    /// <summary>
    /// La liste complète des succès, en combinant plusieurs réponses de Steam :
    /// 1. tes succès (débloqué ou non, et quand) ;
    /// 2. GetGameAchievements : nom, description même pour les cachés, rareté ;
    /// 3. GetSchemaForGame : les adresses complètes des icônes (et secours pour les noms et descriptions) ;
    /// 4. l'avancement des succès à compteur, lu sur la page de ton profil Steam.
    /// </summary>
    public async Task<List<AchievementDetail>> GetAchievementDetailsAsync(string appId, CancellationToken cancellationToken = default)
    {
        List<SteamPlayerAchievement> playerAchievements = await GetPlayerAchievementsAsync(appId, Language, cancellationToken);

        if (playerAchievements.Count == 0)
        {
            return new List<AchievementDetail>();
        }

        // Ces listes sont des « bonus » : sans elles, on affiche quand même tes succès.
        Dictionary<string, SteamGameAchievement> gameAchievements = await TryReadAsync(
            () => GetGameAchievementsAsync(appId, cancellationToken), "liste des succès", appId);

        // Toujours lu : GetGameAchievements ne donne qu'un nom de fichier pour les icônes,
        // alors que le schéma donne leur adresse complète (c'est elle qui affichait bien les icônes avant).
        Dictionary<string, SteamSchemaAchievement> schema = await TryReadAsync(
            () => GetSchemaAsync(appId, cancellationToken), "schéma des succès", appId);

        // La rareté n'est à demander à part que si GetGameAchievements n'a pas répondu.
        Dictionary<string, double> rarities = new Dictionary<string, double>();

        if (gameAchievements.Count == 0)
        {
            rarities = await TryReadAsync(() => GetGlobalPercentagesAsync(appId, cancellationToken), "rareté des succès", appId);
        }

        Dictionary<string, (double Value, double Max)> progression = await TryReadAsync(
            () => GetCommunityProgressAsync(appId, cancellationToken), "avancement des succès", appId);

        List<AchievementDetail> details = new List<AchievementDetail>();

        for (int i = 0; i < playerAchievements.Count; i++)
        {
            SteamPlayerAchievement achievement = playerAchievements[i];
            string apiName = achievement.ApiName ?? "";
            bool isUnlocked = achievement.Achieved == 1;

            // Pour chaque information, on prend la première source qui la connaît.
            gameAchievements.TryGetValue(apiName, out SteamGameAchievement? game);
            schema.TryGetValue(apiName, out SteamSchemaAchievement? info);

            string name = FirstFilled(game?.Name, info?.DisplayName, achievement.Name, apiName);

            // La page du profil ne donne que le nom du succès : c'est par lui qu'on retrouve l'avancement.
            bool hasProgress = progression.TryGetValue(NameTools.Normalize(name), out (double Value, double Max) progress);

            details.Add(new AchievementDetail
            {
                Id = apiName,
                Name = name,
                Description = FirstFilled(game?.Description, info?.Description, achievement.Description, ""),
                IsUnlocked = isUnlocked,
                UnlockedUnix = achievement.UnlockTimeUnix,
                IsHidden = game?.Hidden ?? (info?.Hidden == 1),
                // Le schéma donne des adresses à l'ancien format (404 pour les jeux récents) :
                // on n'en garde que le nom du fichier, comme pour GetGameAchievements.
                IconUrl = isUnlocked
                    ? BuildIconUrl(appId, game?.Icon) ?? BuildIconUrl(appId, FileNameOf(info?.Icon))
                    : BuildIconUrl(appId, game?.IconGray) ?? BuildIconUrl(appId, FileNameOf(info?.IconGray)),
                RarityPercent = game?.Percent ?? (rarities.TryGetValue(apiName, out double percent) ? (double?)percent : null),
                Order = i,
                ProgressValue = hasProgress ? progress.Value : null,
                ProgressMax = hasProgress ? progress.Max : null
            });
        }

        return details;
    }

    /// <summary>
    /// Tes succès pour un jeu. Liste vide si le jeu n'a pas de succès.
    /// language = null : réponse plus légère, sans les noms (suffit pour compter).
    /// </summary>
    private async Task<List<SteamPlayerAchievement>> GetPlayerAchievementsAsync(string appId, string? language, CancellationToken cancellationToken)
    {
        string url = "https://api.steampowered.com/ISteamUserStats/GetPlayerAchievements/v1/"
            + $"?key={_secrets.SteamApiKey}&steamid={_secrets.SteamId}&appid={Uri.EscapeDataString(appId)}"
            + (language is null ? "" : $"&l={language}");

        // GetAsync et pas GetStringAsync : pour un jeu sans succès, Steam répond avec une erreur 400
        // et un message, qu'on veut pouvoir lire au lieu de recevoir directement une exception.
        using HttpResponseMessage response = await Http.GetAsync(url, cancellationToken);

        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
        {
            throw new AchievementSourceUnavailableException(
                "Steam refuse l'accès : vérifie que les détails de jeu de ton profil sont publics et que ta clé API est valide.");
        }

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            throw new AchievementSourceUnavailableException("Trop d'appels à l'API Steam : on réessaiera au prochain démarrage.");
        }

        string json = await response.Content.ReadAsStringAsync(cancellationToken);
        SteamPlayerStats? stats = ReadPlayerStats(json);

        if (stats is { Success: true })
        {
            return stats.Achievements ?? new List<SteamPlayerAchievement>();
        }

        string error = stats?.Error ?? "";

        if (error.Contains("not public", StringComparison.OrdinalIgnoreCase))
        {
            throw new AchievementSourceUnavailableException("Ton profil Steam est privé : les succès ne sont pas lisibles.");
        }

        if (error.Contains("no stats", StringComparison.OrdinalIgnoreCase))
        {
            return new List<SteamPlayerAchievement>();
        }

        // On joint le code HTTP à l'exception : LibraryService s'en sert pour traiter le cas « 500 ».
        throw new HttpRequestException(
            $"Réponse inattendue de Steam ({(int)response.StatusCode}) : {(error.Length > 0 ? error : "illisible")}",
            null,
            response.StatusCode);
    }

    private static SteamPlayerStats? ReadPlayerStats(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<SteamPlayerAchievementsResponse>(json)?.PlayerStats;
        }
        catch (JsonException)
        {
            // Pas du JSON (page d'erreur HTML, par exemple).
            return null;
        }
    }

    private async Task<Dictionary<string, SteamGameAchievement>> GetGameAchievementsAsync(string appId, CancellationToken cancellationToken)
    {
        string url = "https://api.steampowered.com/IPlayerService/GetGameAchievements/v1/"
            + $"?key={_secrets.SteamApiKey}&appid={Uri.EscapeDataString(appId)}&language={Language}";

        string json = await Http.GetStringAsync(url, cancellationToken);
        List<SteamGameAchievement> achievements = JsonSerializer.Deserialize<SteamGameAchievementsResponse>(json)
            ?.Response?.Achievements ?? new List<SteamGameAchievement>();

        return achievements
            .Where(a => !string.IsNullOrEmpty(a.ApiName))
            .DistinctBy(a => a.ApiName)
            .ToDictionary(a => a.ApiName!);
    }

    // ----- Statistiques brutes -----

    /// <summary>
    /// Les compteurs que le jeu envoie à Steam (ex. étoiles ramassées), avec leur nom lisible quand il existe.
    /// Un jeu sans statistiques donne une liste vide, pas une erreur.
    /// </summary>
    public async Task<List<GameStat>> GetGameStatsAsync(string appId, CancellationToken cancellationToken = default)
    {
        string url = "https://api.steampowered.com/ISteamUserStats/GetUserStatsForGame/v2/"
            + $"?key={_secrets.SteamApiKey}&steamid={_secrets.SteamId}&appid={Uri.EscapeDataString(appId)}";

        // Comme pour les succès : un jeu sans statistiques répond par une erreur 400, ce n'est pas grave.
        using HttpResponseMessage response = await Http.GetAsync(url, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return new List<GameStat>();
        }

        string json = await response.Content.ReadAsStringAsync(cancellationToken);
        List<SteamUserStat> values;

        try
        {
            values = JsonSerializer.Deserialize<SteamUserStatsResponse>(json)?.PlayerStats?.Stats ?? new List<SteamUserStat>();
        }
        catch (JsonException)
        {
            return new List<GameStat>();
        }

        if (values.Count == 0)
        {
            return new List<GameStat>();
        }

        // Le schéma donne le nom lisible de chaque statistique (« Étoiles » plutôt que « STAT_STARS »).
        Dictionary<string, string> displayNames = new Dictionary<string, string>();

        try
        {
            SteamSchemaStats schema = await ReadSchemaAsync(appId, cancellationToken);

            foreach (SteamSchemaStat stat in schema.Stats ?? new List<SteamSchemaStat>())
            {
                if (!string.IsNullOrEmpty(stat.Name) && !string.IsNullOrWhiteSpace(stat.DisplayName))
                {
                    displayNames.TryAdd(stat.Name, stat.DisplayName);
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            Logger.Log($"Steam : noms des statistiques indisponibles pour l'appid {appId} : {ex.Message}");
        }

        return values
            .Where(v => !string.IsNullOrEmpty(v.Name))
            .Select(v => new GameStat(displayNames.GetValueOrDefault(v.Name!, v.Name!), v.Value))
            .OrderBy(stat => stat.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Les barres « 37 / 100 » ne sont dans aucune des API Steam utilisées ici : on les lit sur la page des succès
    /// de ton profil Steam (comme le fait le plugin Playnite SuccessStory). Il faut que les détails de jeu
    /// de ton profil soient publics. Si Steam change sa page, on reçoit simplement une liste vide.
    /// Résultat rangé par nom de succès normalisé (sans espaces ni ponctuation).
    /// </summary>
    private async Task<Dictionary<string, (double Value, double Max)>> GetCommunityProgressAsync(string appId, CancellationToken cancellationToken)
    {
        string url = $"https://steamcommunity.com/profiles/{_secrets.SteamId}/stats/{Uri.EscapeDataString(appId)}"
            + $"?tab=achievements&l={Language}";

        using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64)");

        using HttpResponseMessage response = await Http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        string html = await response.Content.ReadAsStringAsync(cancellationToken);

        // AngleSharp lit le HTML comme un navigateur, puis on cherche les éléments avec des sélecteurs CSS.
        IDocument document = new HtmlParser().ParseDocument(html);
        Dictionary<string, (double Value, double Max)> progression = new Dictionary<string, (double Value, double Max)>();

        foreach (IElement row in document.QuerySelectorAll("#personalAchieve div.achieveRow"))
        {
            string? name = row.QuerySelector("div.achieveTxt h3")?.TextContent;
            string? text = row.QuerySelector("div.achievementProgressBar .progressText")?.TextContent;

            if (name is null || text is null)
            {
                continue;   // succès sans compteur
            }

            string[] parts = text.Split('/');

            if (parts.Length == 2
                && TryReadWholeNumber(parts[0], out double value)
                && TryReadWholeNumber(parts[1], out double max)
                && max > 0)
            {
                progression.TryAdd(NameTools.Normalize(name), (value, max));
            }
        }

        return progression;
    }

    /// <summary>
    /// Lit « 1 000 » ou « 1,000 » en gardant seulement les chiffres.
    /// Limite connue : un compteur à virgule (rare) serait mal lu.
    /// </summary>
    private static bool TryReadWholeNumber(string text, out double number)
    {
        string digits = new string(text.Where(char.IsDigit).ToArray());
        return double.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out number);
    }

    /// <summary>
    /// Construit l'adresse d'une icône de succès à partir du nom de son fichier, au format qu'utilise
    /// aujourd'hui la page des succès de Steam, pour tous les jeux (vérifié en octobre 2026 sur Kotamon
    /// et Team Fortress 2). L'ancien format (steamcdn-a.akamaihd.net/steamcommunity/public/images/apps/…)
    /// répond « 404 » pour les jeux récents.
    /// </summary>
    private static string? FileNameOf(string? url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
            ? Path.GetFileName(uri.AbsolutePath)
            : null;
    }

    private static string? BuildIconUrl(string appId, string? icon)
    {
        if (string.IsNullOrEmpty(icon))
        {
            return null;
        }

        // Si on reçoit déjà une adresse complète, on n'en garde que le nom du fichier.
        string fileName = icon.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? FileNameOf(icon) ?? icon
            : icon;

        return $"https://shared.fastly.steamstatic.com/community_assets/images/apps/{appId}/{fileName}";
    }

    private async Task<SteamSchemaStats> ReadSchemaAsync(string appId, CancellationToken cancellationToken)
    {
        string url = "https://api.steampowered.com/ISteamUserStats/GetSchemaForGame/v2/"
            + $"?key={_secrets.SteamApiKey}&appid={Uri.EscapeDataString(appId)}&l={Language}";

        string json = await Http.GetStringAsync(url, cancellationToken);
        return JsonSerializer.Deserialize<SteamSchemaResponse>(json)?.Game?.AvailableGameStats ?? new SteamSchemaStats();
    }

    private async Task<Dictionary<string, SteamSchemaAchievement>> GetSchemaAsync(string appId, CancellationToken cancellationToken)
    {
        List<SteamSchemaAchievement> achievements = (await ReadSchemaAsync(appId, cancellationToken)).Achievements
            ?? new List<SteamSchemaAchievement>();

        // DistinctBy : par sécurité, au cas où un même identifiant apparaîtrait deux fois.
        return achievements
            .Where(a => !string.IsNullOrEmpty(a.ApiName))
            .DistinctBy(a => a.ApiName)
            .ToDictionary(a => a.ApiName);
    }

    private static async Task<Dictionary<string, double>> GetGlobalPercentagesAsync(string appId, CancellationToken cancellationToken)
    {
        string url = "https://api.steampowered.com/ISteamUserStats/GetGlobalAchievementPercentagesForApp/v2/"
            + $"?gameid={Uri.EscapeDataString(appId)}";

        string json = await Http.GetStringAsync(url, cancellationToken);
        List<SteamGlobalPercentage> percentages = JsonSerializer.Deserialize<SteamGlobalPercentagesResponse>(json)
            ?.AchievementPercentages?.Achievements ?? new List<SteamGlobalPercentage>();

        return percentages
            .Where(p => !string.IsNullOrEmpty(p.ApiName))
            .DistinctBy(p => p.ApiName)
            .ToDictionary(p => p.ApiName, p => p.Percent);
    }

    /// <summary>
    /// Lance une lecture « bonus » : si elle échoue, on le note dans le journal et on continue avec un dictionnaire vide.
    /// </summary>
    private static async Task<Dictionary<string, T>> TryReadAsync<T>(Func<Task<Dictionary<string, T>>> read, string what, string appId)
    {
        try
        {
            return await read();
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            Logger.Log($"Steam : {what} indisponible pour l'appid {appId} : {ex.Message}");
            return new Dictionary<string, T>();
        }
    }

    private static string FirstFilled(params string?[] values)
    {
        return values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "";
    }
}
