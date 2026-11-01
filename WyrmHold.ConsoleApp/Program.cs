using System.Text.Json;
using Wyrmhold.Core;

// Phase 6 — essais IsThereAnyDeal, prix en France (euros).
// 1 = comparateur de prix (chercher un jeu, voir ses offres)
// 2 = promos filtrées par tags (préparation des recommandations par genre)
// (Le premier essai avec CheapShark reste disponible dans CheapSharkApi.)

// 7 = rapport de bug : nettoyage des données personnelles (pas besoin de clé IsThereAnyDeal).
if (args.Length > 0 && args[0] == "7")
{
    TestBugReport();
    return;
}

// 21 = émulateurs, ESSAI À BLANC : programmes et dossiers trouvés, jeux lus et commande de lancement (rien n'est lancé ni enregistré).
if (args.Length > 0 && args[0] == "21")
{
    ShowEmulators();
    return;
}

// 22 = émulateurs : essai des lecteurs de formats sur de faux fichiers, du nettoyage des noms et de la lecture des lignes de commande.
if (args.Length > 0 && args[0] == "22")
{
    await TestEmulationAsync();
    return;
}

// 9 = succès Steam lus sur le disque, comparés à la Steam Web API : « dotnet run -- 9 4294490 ».
if (args.Length > 1 && args[0] == "9")
{
    await CompareLocalAchievementsAsync(args[1]);
    return;
}

// 11 = désinstallation, ESSAI À BLANC (rien n'est lancé) : ce que ferait « Désinstaller… » pour chaque jeu installé.
if (args.Length > 0 && args[0] == "11")
{
    foreach (Game game in new LibraryService().LoadGames().Where(g => g.IsInstalled).OrderBy(g => g.Platform))
    {
        UninstallPlan? plan = GameUninstaller.FindPlan(game);
        Console.WriteLine(plan is null
            ? $"{game.PlatformName,-10} {game.Name} : AUCUNE MÉTHODE"
            : $"{game.PlatformName,-10} {game.Name} : {plan.Kind} → {plan.FileName} {plan.Arguments}");
    }
    return;
}

// 20 = un jeu installé absent de GetOwnedGames : « dotnet run -- 20 5133900 » essaie plusieurs paramètres et GetRecentlyPlayedGames.
if (args.Length > 1 && args[0] == "20")
{
    using HttpClient http = new HttpClient();
    string appId = args[1];
    string player = $"?key={Secrets.Current.SteamApiKey}&steamid={Secrets.Current.SteamId}";
    string owned = "https://api.steampowered.com/IPlayerService/GetOwnedGames/v1/" + player + "&include_appinfo=true&include_played_free_games=true";

    async Task ShowAsync(string label, string url)
    {
        using JsonDocument doc = JsonDocument.Parse(await http.GetStringAsync(url));
        List<JsonElement> list = doc.RootElement.GetProperty("response").TryGetProperty("games", out JsonElement games)
            ? games.EnumerateArray().ToList()
            : new List<JsonElement>();
        bool found = list.Any(g => g.GetProperty("appid").GetInt32().ToString() == appId);
        Console.WriteLine($"{label,-45} {list.Count,5} jeux ; {appId} {(found ? "PRÉSENT" : "absent")}");
    }

    await ShowAsync("GetOwnedGames (paramètres actuels)", owned);
    await ShowAsync("  + skip_unvetted_apps=false", owned + "&skip_unvetted_apps=false");
    await ShowAsync("  + include_free_sub=true", owned + "&include_free_sub=true");
    await ShowAsync("  + les deux", owned + "&skip_unvetted_apps=false&include_free_sub=true");
    await ShowAsync("GetRecentlyPlayedGames", "https://api.steampowered.com/IPlayerService/GetRecentlyPlayedGames/v1/" + player);

    // Les jeux que skip_unvetted_apps=false ajoute.
    async Task<Dictionary<int, string>> ReadGamesAsync(string url)
    {
        using JsonDocument doc = JsonDocument.Parse(await http.GetStringAsync(url));
        return doc.RootElement.GetProperty("response").GetProperty("games").EnumerateArray()
            .ToDictionary(g => g.GetProperty("appid").GetInt32(), g => g.TryGetProperty("name", out JsonElement n) ? n.GetString() ?? "" : "");
    }

    Dictionary<int, string> without = await ReadGamesAsync(owned);
    Dictionary<int, string> with = await ReadGamesAsync(owned + "&skip_unvetted_apps=false");

    foreach ((int id, string name) in with.Where(pair => !without.ContainsKey(pair.Key)))
    {
        Console.WriteLine($"   en plus : {id} {name}");
    }
    return;
}

// 19 = badge « Mis à jour », ESSAI À BLANC (rien n'est enregistré) : jeux dont la version a changé ces 7 jours,
// et la patch note qui allumerait le badge. « dotnet run -- 19 405640 » = règle appliquée à un appid, comme s'il venait d'être mis à jour.
if (args.Length > 0 && args[0] == "19")
{
    List<(string Name, string AppId, long DetectedUnix)> targets = args.Length > 1
        ? new List<(string, string, long)> { (args[1], args[1], DateTimeOffset.UtcNow.ToUnixTimeSeconds()) }
        : new LibraryService().LoadGames()
            .Where(g => g.IsUpdateDetectedRecently)
            .Select(g => (g.Name, g.Platform == Platform.Steam ? g.PlatformGameId : g.SteamAppId ?? "", g.LastUpdateDetectedUnix))
            .ToList();

    foreach ((string name, string appId, long detected) in targets)
    {
        Console.WriteLine($"{name} (appid {appId}, version changée le {DateTimeOffset.FromUnixTimeSeconds(detected).LocalDateTime:dd/MM HH:mm})");

        if (appId.Length == 0)
        {
            Console.WriteLine("   appid Steam inconnu : l'appli le cherchera par le nom");
            continue;
        }

        long oldest = detected - Game.RecentUpdateDays * 24 * 3600;

        foreach (SteamNewsItem item in await SteamNewsApi.GetAnnouncementsAsync(appId, 20))
        {
            string verdict = item.DateUnix < oldest ? "trop ancienne" : item.LooksLikePatchNotes ? "PATCH NOTE → badge" : "pas une patch note";
            Console.WriteLine($"   {DateTimeOffset.FromUnixTimeSeconds(item.DateUnix).LocalDateTime:dd/MM/yyyy} [{verdict}] {item.Title}");

            if (item.DateUnix < oldest)
            {
                break;
            }
        }
    }
    return;
}

// 18 = IsThereAnyDeal : « tags » (un tag) et « tagsUnion » (des genres) dans le MÊME filtre, comme « Pour toi » + un tag.
if (args.Length > 0 && args[0] == "18")
{
    IsThereAnyDealApi itad = new IsThereAnyDealApi(Secrets.Current);
    string[] genres = { "Strategy", "Simulation" };

    ItadDealsPage tagOnly = await itad.GetDealsByTagsAsync(new[] { "RPG" }, true, 0, 200, "");
    ItadDealsPage genresOnly = await itad.GetDealsByTagsAsync(genres, false, 0, 200, "");
    ItadDealsPage both = await itad.GetDealsByTagsAsync(new[] { "RPG" }, true, 0, 200, "",
        extraFilter: new Dictionary<string, object> { ["tagsUnion"] = genres });

    HashSet<string> tagIds = tagOnly.List.Select(d => d.Id).ToHashSet();
    HashSet<string> genreIds = genresOnly.List.Select(d => d.Id).ToHashSet();
    Console.WriteLine($"RPG seul : {tagOnly.List.Count} ; Strategy OU Simulation : {genresOnly.List.Count} ; les deux : {both.List.Count}");
    Console.WriteLine($"Parmi « les deux » : {both.List.Count(d => tagIds.Contains(d.Id))} aussi dans « RPG seul » (1re page), "
        + $"{both.List.Count(d => genreIds.Contains(d.Id))} aussi dans « genres » (1re page)");
    Console.WriteLine(string.Join(" ; ", both.List.Take(8).Select(d => d.Title)));
    return;
}

// 17 = GetOwnedGames avec / sans include_free_sub (démos, licences gratuites) : combien de jeux en plus, lesquels.
if (args.Length > 0 && args[0] == "17")
{
    using HttpClient http = new HttpClient();
    string baseUrl = "https://api.steampowered.com/IPlayerService/GetOwnedGames/v1/"
        + $"?key={Secrets.Current.SteamApiKey}&steamid={Secrets.Current.SteamId}&include_appinfo=true&include_played_free_games=true";

    async Task<Dictionary<int, string>> ReadAsync(string url)
    {
        using JsonDocument doc = JsonDocument.Parse(await http.GetStringAsync(url));
        return doc.RootElement.GetProperty("response").TryGetProperty("games", out JsonElement list)
            ? list.EnumerateArray().ToDictionary(g => g.GetProperty("appid").GetInt32(),
                g => g.TryGetProperty("name", out JsonElement n) ? n.GetString() ?? "" : "")
            : new Dictionary<int, string>();
    }

    Dictionary<int, string> without = await ReadAsync(baseUrl);
    Dictionary<int, string> with = await ReadAsync(baseUrl + "&include_free_sub=true");
    List<KeyValuePair<int, string>> extra = with.Where(pair => !without.ContainsKey(pair.Key)).ToList();

    Console.WriteLine($"Sans include_free_sub : {without.Count} ; avec : {with.Count} ; en plus : {extra.Count}");
    Console.WriteLine(string.Join(" ; ", extra.Take(25).Select(pair => $"{pair.Value} ({pair.Key})")));

    // Essai à blanc de la nouvelle règle (10 octobre 2026) : quels jeux Steam de la base seraient masqués ?
    List<Game> steamRows = new LibraryService().LoadGames().Where(g => g.Platform == Platform.Steam).ToList();
    HashSet<string> familyIds = new GameDatabase().LoadKnownFamilyAppIds();
    List<Game> wouldHide = steamRows
        .Where(g => !(int.TryParse(g.PlatformGameId, out int id) && with.ContainsKey(id)) && !familyIds.Contains(g.PlatformGameId))
        .ToList();
    Console.WriteLine($"\nEssai à blanc : {steamRows.Count} jeux Steam affichés, {familyIds.Count} de la famille connus ; "
        + $"ni à toi ni de la famille : {wouldHide.Count} (installés : {wouldHide.Count(g => g.IsInstalled)}, gardés s'ils sont gratuits)");
    Console.WriteLine(string.Join(" ; ", wouldHide.Take(30).Select(g => $"{g.Name} ({g.PlatformGameId}{(g.IsInstalled ? ", installé" : "")}{(g.IsFamilyShared == true ? ", marqué famille" : "")})")));

    List<Game> installedNotOwned = new LibraryService().LoadGames()
        .Where(g => g.Platform == Platform.Steam && g.IsInstalled && int.TryParse(g.PlatformGameId, out int id) && !with.ContainsKey(id))
        .ToList();
    Console.WriteLine($"\nInstallés mais absents même avec include_free_sub : {installedNotOwned.Count}");
    Console.WriteLine(string.Join(" ; ", installedNotOwned.Select(g => $"{g.Name} ({g.PlatformGameId}, famille={g.IsFamilyShared}, propriétaire={g.OwnerSteamId ?? "-"})")));
    return;
}

// 16 = jeux similaires selon Steam (sans clé) : « dotnet run -- 16 1222140 730 ».
if (args.Length > 1 && args[0] == "16")
{
    foreach (string appId in args.Skip(1))
    {
        List<SimilarGame> similar = await SteamSimilarGames.GetAsync(int.Parse(appId), new HashSet<int>(), 6);
        Console.WriteLine($"{appId} → {string.Join(" ; ", similar.Select(game => $"{game.Name} ({game.AppId})"))}");
        Console.WriteLine($"   image : {similar.FirstOrDefault()?.ImageUrl}");
    }

    return;
}

// 15 = réponse BRUTE d'IsThereAnyDeal games/lookup/v1 pour des appid Steam (la clé n'est pas affichée).
if (args.Length > 1 && args[0] == "15")
{
    using HttpClient http = new HttpClient();
    http.DefaultRequestHeaders.Add("ITAD-API-Key", Secrets.Current.IsThereAnyDealApiKey);

    foreach (string appId in args.Skip(1))
    {
        using HttpResponseMessage response = await http.GetAsync($"https://api.isthereanydeal.com/games/lookup/v1?appid={Uri.EscapeDataString(appId)}");
        string body = await response.Content.ReadAsStringAsync();
        Console.WriteLine($"{appId,-8} → {(int)response.StatusCode} : {body.Substring(0, Math.Min(220, body.Length))}");
    }

    return;
}

// 14 = réponse BRUTE de GetPlayerAchievements pour des appid (code HTTP + début du texte ; la clé n'est pas affichée).
// « dotnet run -- 14 1222140 2050650 » : sert à comprendre les refus 403 de Steam.
if (args.Length > 1 && args[0] == "14")
{
    using HttpClient http = new HttpClient();

    foreach (string appId in args.Skip(1))
    {
        string url = "https://api.steampowered.com/ISteamUserStats/GetPlayerAchievements/v1/"
            + $"?key={Secrets.Current.SteamApiKey}&steamid={Secrets.Current.SteamId}&appid={Uri.EscapeDataString(appId)}";
        using HttpResponseMessage response = await http.GetAsync(url);
        string body = await response.Content.ReadAsStringAsync();
        Console.WriteLine($"{appId,-8} → {(int)response.StatusCode} {response.StatusCode} : {body.Substring(0, Math.Min(160, body.Length))}");
    }

    return;
}

// 13 simuler <nom> = POUR TESTER (modifie la base, Wyrmhold fermé) : fait croire que le jeu avait 3 succès de moins
// et qu'il n'a pas été lu depuis longtemps. Au prochain lancement, la vérification de la semaine le relit
// et « découvre » 3 succès ajoutés. Les vrais chiffres reviennent à cette lecture.
if (args.Length > 2 && args[0] == "13" && args[1] == "simuler")
{
    string wanted = string.Join(" ", args.Skip(2));
    Game? target = new LibraryService().LoadGames()
        .Where(g => g.AchievementsTotal > 3 && g.Name.Contains(wanted, StringComparison.CurrentCultureIgnoreCase))
        .OrderBy(g => g.Name.Length)
        .FirstOrDefault();

    if (target is null)
    {
        Console.WriteLine($"Aucun jeu avec plus de 3 succès dont le nom contient « {wanted} ».");
        return;
    }

    target.AchievementsTotal -= 3;
    target.AchievementsUnlocked = Math.Min(target.AchievementsUnlocked, target.AchievementsTotal);
    target.HasNewAchievements = false;
    target.AchievementsAdded = 0;
    target.AchievementsCheckedUnix = 1;   // « lu il y a très longtemps » : premier de la vérification de la semaine
    new GameDatabase().SaveAchievements(new List<Game> { target });

    Console.WriteLine($"{target.Name} ({target.PlatformName}) : enregistré à {target.AchievementsUnlocked}/{target.AchievementsTotal}. "
        + "Lance Wyrmhold : après « lecture des succès », il doit annoncer +3.");
    return;
}

// 13 = succès ajoutés : la logique des badges (jeux fictifs en mémoire), puis, en lecture seule,
// combien de jeux de ta bibliothèque la vérification de la semaine relirait.
if (args.Length > 0 && args[0] == "13")
{
    void Check(string label, Game game, int unlocked, int total, int expectedAdded, bool expectedOrange)
    {
        int added = game.ApplyAchievementProgress(new AchievementProgress(unlocked, total), 1000);
        bool ok = added == expectedAdded && game.HasNewAchievements == expectedOrange;
        Console.WriteLine($"{(ok ? "OK " : "ERREUR")} {label} : +{added}, badge « {(game.HasAddedAchievements ? game.NewAchievementsText : "aucun")} », vignette « {game.AddedAchievementsTileText} »");
    }

    Check("1re lecture (rien de nouveau)", new Game(), 10, 50, 0, false);
    Check("en cours 37/50 → 37/60", new Game { AchievementsUnlocked = 37, AchievementsTotal = 50, AchievementsCheckedUnix = 1 }, 37, 60, 10, false);
    Check("terminé 50/50 → 50/55", new Game { AchievementsUnlocked = 50, AchievementsTotal = 50, AchievementsCheckedUnix = 1 }, 50, 55, 5, true);
    Check("total 0 (erreur 500) → 40", new Game { AchievementsTotal = 0, AchievementsCheckedUnix = 1 }, 0, 40, 0, false);

    Game seen = new Game { AchievementsUnlocked = 37, AchievementsTotal = 50, AchievementsCheckedUnix = 1 };
    seen.ApplyAchievementProgress(new AchievementProgress(37, 60), 1000);
    seen.AcknowledgeAddedAchievements();
    Console.WriteLine($"{(seen.HasAddedAchievements ? "ERREUR" : "OK ")} liste des succès ouverte : badge retiré");

    Game back = new Game { AchievementsUnlocked = 50, AchievementsTotal = 50, AchievementsCheckedUnix = 1 };
    back.ApplyAchievementProgress(new AchievementProgress(50, 55), 1000);
    back.AcknowledgeAddedAchievements();
    Console.WriteLine($"{(back.HasNewAchievements ? "OK " : "ERREUR")} jeu terminé : badge orange gardé après l'ouverture ({back.NewAchievementsText})");
    back.ApplyAchievementProgress(new AchievementProgress(55, 55), 2000);
    Console.WriteLine($"{(back.HasAddedAchievements ? "ERREUR" : "OK ")} retour à 100 % : badge retiré");

    long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    List<Game> library = new LibraryService().LoadGames();
    int withAchievements = library.Count(g => g.AchievementsTotal > 0);
    int stale = library.Count(g => g.AchievementsTotal > 0 && g.AchievementsCheckedUnix < now - 7 * 24 * 3600);
    Console.WriteLine($"\nBibliothèque : {withAchievements} jeux avec succès, {stale} lus il y a plus de 7 jours "
        + $"→ relus par lots de 150 ({Math.Ceiling(stale / 150.0)} lecture(s) pour tout parcourir).");
    return;
}

// 12 = jeux gratuits (sans clé) : démos Steam, jeux gratuits Steam, jeux offerts par Epic. « dotnet run -- 12 [nom] ».
if (args.Length > 0 && args[0] == "12")
{
    string search = string.Join(" ", args.Skip(1));

    foreach (FreeGameSource source in new[] { FreeGameSource.SteamDemo, FreeGameSource.SteamFreeToPlay })
    {
        foreach (bool newestFirst in new[] { false, true })
        {
            FreeGamesPage first = await SteamFreeCatalog.GetPageAsync(source, 0, newestFirst, search);
            FreeGamesPage second = await SteamFreeCatalog.GetPageAsync(source, first.NextStart, newestFirst, search);
            int common = first.Games.Count(game => second.Games.Any(other => other.SteamAppId == game.SteamAppId));

            Console.WriteLine($"\n{source}{(newestFirst ? " (nouveautés)" : "")} : {first.TotalCount} au total, "
                + $"page 1 = {first.Games.Count}, page 2 = {second.Games.Count}, en commun = {common}, encore = {second.HasMore}");

            foreach (FreeGame game in first.Games.Take(3))
            {
                Console.WriteLine($"  {game.SteamAppId,-8} {game.Title} | {game.SummaryLine} | {game.SteamInstallUrl}");
            }
        }
    }

    Console.WriteLine("\nOfferts sur Epic :");

    foreach (FreeGame game in await EpicFreeGames.GetAsync())
    {
        Console.WriteLine($"  {game.Title} | {game.SummaryLine} | {game.StoreUrl}");
    }

    return;
}

// 10 sans numéro = joueurs en jeu de TOUTE la bibliothèque (comme l'appli), avec le temps mis.
if (args.Length == 1 && args[0] == "10")
{
    LibraryService library = new LibraryService();
    List<Game> games = library.LoadGames();
    System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
    await library.RefreshPlayerCountsAsync(games);
    Console.WriteLine($"{games.Count} jeux, {games.Count(g => g.HasPlayersInGame)} avec un chiffre, en {watch.ElapsedMilliseconds} ms");
    foreach (IGrouping<Platform, Game> group in games.GroupBy(g => g.Platform))
    {
        Console.WriteLine($"  {group.Key} : {group.Count(g => g.HasPlayersInGame)}/{group.Count()}");
    }
    foreach (Game game in games.Where(g => g.HasPlayersInGame).OrderByDescending(g => g.PlayersInGame).Take(5))
    {
        Console.WriteLine($"  {game.Name} ({game.PlatformName}) : {game.PlayersInGameText}");
    }
    return;
}

// 10 = joueurs en jeu sur Steam (sans clé) : « dotnet run -- 10 730 1245620 999999999 ».
if (args.Length > 1 && args[0] == "10")
{
    foreach (string appId in args.Skip(1))
    {
        try
        {
            int? count = await SteamPlayerCountApi.GetCurrentPlayersAsync(appId);
            Game steam = new Game { Platform = Platform.Steam, PlayersInGame = count };
            Game epic = new Game { Platform = Platform.Epic, PlayersInGame = count };
            Console.WriteLine(count is null
                ? $"{appId} : pas de chiffre chez Steam"
                : $"{appId} : {count} → badge Steam « {steam.PlayersInGameText} », badge Epic « {epic.PlayersInGameText} »");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{appId} : erreur {ex.Message}");
        }
    }
    return;
}

// 8 = clés API : import de l'ancien secrets.json, puis essai de chaque clé enregistrée (affichées masquées).
if (args.Length > 0 && args[0] == "8")
{
    await TestApiKeysAsync();
    return;
}

try
{
    Secrets secrets = Secrets.Load();
    var itad = new IsThereAnyDealApi(secrets);

    if (!itad.IsConfigured)
    {
        Console.WriteLine("Clé IsThereAnyDeal absente : ajoute-la dans Wyrmhold (Réglages → Clés API).");
        return;
    }

    // Le mode peut aussi être donné au lancement : « dotnet run -- 2 ».
    string mode;

    if (args.Length > 0)
    {
        mode = args[0];
    }
    else
    {
        Console.Write("1 = comparateur de prix, 2 = promos par tags : ");
        mode = Console.ReadLine() ?? "";
    }

    if (mode.Trim() == "6")
    {
        await TestRecommendationsAsync(itad);
    }
    else if (mode.Trim() == "5")
    {
        await TestSortValuesAsync(itad);
    }
    else if (mode.Trim() == "4")
    {
        // « dotnet run -- 4 Hades » : la présentation du premier jeu trouvé.
        await ShowPresentationAsync(itad, string.Join(" ", args.Skip(1)));
    }
    else if (mode.Trim() == "3")
    {
        await TestQualityFiltersAsync(itad);
    }
    else if (mode.Trim() == "2")
    {
        await TestDealsByTagsAsync(itad, args.Skip(1).ToArray());
    }
    else
    {
        await ComparePricesAsync(itad);
    }

    Console.WriteLine();
    Console.WriteLine("Prix fournis par IsThereAnyDeal.");
}
catch (Exception ex)
{
    // Pas de réseau, clé refusée, limite dépassée… on affiche l'erreur au lieu de planter.
    Console.WriteLine($"Erreur : {ex.Message}");
}

static async Task ComparePricesAsync(IsThereAnyDealApi itad)
{
    Console.Write("Jeu à chercher : ");
    string title = Console.ReadLine() ?? "";

    List<ItadSearchResult> results = await itad.SearchAsync(title);

    if (results.Count == 0)
    {
        Console.WriteLine("Aucun jeu trouvé.");
        return;
    }

    for (int i = 0; i < results.Count; i++)
    {
        Console.WriteLine($"{i + 1}. {results[i].Title} [{results[i].Type}]");
    }

    Console.Write("Numéro du jeu : ");
    if (!int.TryParse(Console.ReadLine(), out int choice) || choice < 1 || choice > results.Count)
    {
        Console.WriteLine("Choix invalide.");
        return;
    }

    ItadSearchResult selected = results[choice - 1];

    // On passe une liste d'un seul identifiant : la méthode sait en traiter jusqu'à 200.
    List<ItadGamePrices> prices = await itad.GetPricesAsync(new[] { selected.Id });
    ItadGamePrices? game = prices.FirstOrDefault();

    if (game == null || game.Deals.Count == 0)
    {
        Console.WriteLine("Aucune offre pour ce jeu en France.");
        return;
    }

    Console.WriteLine();
    Console.WriteLine($"Offres pour {selected.Title} :");

    foreach (ItadDeal deal in game.Deals.OrderBy(deal => deal.Price.Amount))
    {
        string promo = deal.Cut > 0 ? $"-{deal.Cut} %, prix normal {deal.Regular.Amount:0.00} {deal.Regular.Currency}" : "pas de promo";

        Console.WriteLine($"- {deal.Shop.Name,-22} {deal.Price.Amount,7:0.00} {deal.Price.Currency}   ({promo})");
        Console.WriteLine($"  {deal.Url}");
    }

    if (game.HistoryLow?.AllTime != null)
    {
        Console.WriteLine();
        Console.WriteLine($"Plus bas historique : {game.HistoryLow.AllTime.Amount:0.00} {game.HistoryLow.AllTime.Currency}");
    }
}

// Compare le nombre de promos pour deux tags, seuls puis ensemble, avec « tags » et « tagsUnion ».
// Si « tags » donne MOINS de promos que chaque tag seul, il veut dire « tous les tags » (ET).
// Si « tagsUnion » donne PLUS, il veut dire « au moins un des tags » (OU).
// Les deux tags peuvent aussi être donnés au lancement : « dotnet run -- 2 Horror Racing ».
static async Task TestDealsByTagsAsync(IsThereAnyDealApi itad, string[] tagArgs)
{
    string tagA;
    string tagB;

    if (tagArgs.Length >= 2)
    {
        tagA = tagArgs[0];
        tagB = tagArgs[1];
    }
    else
    {
        Console.Write("Premier tag (Entrée = Roguelike) : ");
        tagA = ReadOrDefault("Roguelike");
        Console.Write("Second tag (Entrée = Deckbuilding) : ");
        tagB = ReadOrDefault("Deckbuilding");
    }

    Console.WriteLine();
    await CountAsync(itad, "Sans filtre", null, true);
    await CountAsync(itad, $"tags [{tagA}]", new[] { tagA }, true);
    await CountAsync(itad, $"tags [{tagB}]", new[] { tagB }, true);
    await CountAsync(itad, $"tags [{tagA}, {tagB}]", new[] { tagA, tagB }, true);
    await CountAsync(itad, $"tagsUnion [{tagA}, {tagB}]", new[] { tagA, tagB }, false);
}

// Les filtres de qualité, sous la forme donnée par la spécification de l'API (dist/openapi.yaml) :
// « type » = liste de numéros (1 = jeu, 2 = DLC…) ; « steamPerc » et « steamCount » = { min, max },
// les deux clés obligatoires (null = pas de limite).
static async Task TestQualityFiltersAsync(IsThereAnyDealApi itad)
{
    string[] tags = { "RPG", "Strategy" };

    var tests = new List<(string Label, Dictionary<string, object> Filter)>
    {
        ("Sans critère", new Dictionary<string, object>()),
        ("jeux seulement", new Dictionary<string, object> { ["type"] = new[] { 1 } }),
        // Essais du 8 octobre 2026 : steamPerc avec max = null renvoie 0 promo, il faut écrire max = 100 ;
        // steamCount accepte max = null.
        ("+ 80 % d'avis positifs", new Dictionary<string, object>
        {
            ["type"] = new[] { 1 },
            ["steamPerc"] = new { min = 80, max = 100 }
        }),
        ("+ 500 avis au moins", new Dictionary<string, object>
        {
            ["type"] = new[] { 1 },
            ["steamPerc"] = new { min = 80, max = 100 },
            ["steamCount"] = new { min = 500, max = (int?)null }
        }),
    };

    foreach ((string label, Dictionary<string, object> filter) in tests)
    {
        try
        {
            ItadDealsPage page = await itad.GetDealsByTagsAsync(tags, false, 0, 200, "", extraFilter: filter);
            string count = page.HasMore ? "plus de 200" : page.List.Count.ToString();
            string examples = string.Join(" | ", page.List.Take(5).Select(item => $"{item.Title} [{item.Type}]"));

            Console.WriteLine($"{label,-24} {count,12}   ex. : {examples}");
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"{label,-24} refusé : {ex.Message}");
        }
    }
}

// La documentation dit seulement que « sort » prend les mêmes valeurs que la liste des promos du site,
// sans les lister. On essaie des valeurs probables : refusée (erreur), ignorée (même ordre que sans tri) ou acceptée.
static async Task TestSortValuesAsync(IsThereAnyDealApi itad)
{
    string[] tags = { "RPG", "Strategy" };
    var quality = new Dictionary<string, object>
    {
        ["type"] = new[] { 1 },
        ["steamPerc"] = new { min = 80, max = 100 },
        ["steamCount"] = new { min = 2000, max = (int?)null }
    };

    string[] sorts =
    {
        "", "-cut", "price", "rank", "-rank", "trending", "-trending", "waitlisted", "-waitlisted",
        "collected", "-collected", "steam", "-steam", "release-date", "-release-date", "time", "-time", "popularity"
    };

    string? defaultOrder = null;

    foreach (string sort in sorts)
    {
        try
        {
            ItadDealsPage page = await itad.GetDealsByTagsAsync(tags, false, 0, 6, sort, extraFilter: quality);
            string titles = string.Join(" | ", page.List.Select(item => item.Title));
            defaultOrder ??= titles;

            string verdict = sort.Length > 0 && titles == defaultOrder ? "(même ordre que sans tri)" : "";
            Console.WriteLine($"{(sort.Length == 0 ? "(aucun)" : sort),-15} {verdict} {titles}");
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"{sort,-15} refusé : {ex.Message}");
        }
    }
}

// Les genres préférés calculés depuis ta bibliothèque (lue seulement), puis les promos recommandées.
static async Task TestRecommendationsAsync(IsThereAnyDealApi itad)
{
    List<Game> games = new GameDatabase().LoadGames();
    var recommender = new GenreRecommender();
    List<PreferredGenre> genres = await recommender.GetPreferredGenresAsync(games, Array.Empty<string>());

    Console.WriteLine($"{games.Count} jeux, {games.Count(g => g.PlaytimeMinutes > 0)} joués, {games.Count(g => g.TagList.Length > 0)} avec des tags");
    Console.WriteLine("Genres préférés :");

    foreach (PreferredGenre genre in genres)
    {
        Console.WriteLine($"  {genre.Label,-30} → {genre.Name}");
    }

    ItadDealsPage page = await GenreRecommender.GetDealsAsync(itad, genres.Select(g => g.Name), 80, 2000, 0, 15, "rank");

    Console.WriteLine();
    Console.WriteLine("Promos recommandées (tri : popularité) :");

    foreach (ItadDealListItem item in page.List)
    {
        Console.WriteLine($"  {item.Title,-45} {item.Deal.Price.Amount,7:0.00} € (-{item.Deal.Cut} %) chez {item.Deal.Shop.Name}");
    }
}

static async Task ShowPresentationAsync(IsThereAnyDealApi itad, string title)
{
    List<ItadSearchResult> results = await itad.SearchAsync(title);
    ItadSearchResult? first = results.FirstOrDefault();

    if (first == null)
    {
        Console.WriteLine("Aucun jeu trouvé.");
        return;
    }

    GamePresentation? presentation = await new GamePresentationService(itad).GetAsync(first.Id);

    if (presentation == null)
    {
        Console.WriteLine("Pas de fiche pour ce jeu.");
        return;
    }

    Console.WriteLine(first.Title);
    Console.WriteLine($"Jaquette    : {presentation.CoverUrl}");
    Console.WriteLine($"Infos       : {presentation.InfoLine}");
    Console.WriteLine($"Genres      : {presentation.TagsLine}");
    Console.WriteLine($"Avis        : {presentation.ReviewLine}");
    Console.WriteLine($"Description : {presentation.Description}");

    SteamGameMedia? media = presentation.SteamMedia;
    Console.WriteLine($"Captures    : {media?.Screenshots.Count ?? 0}  ex. {media?.Screenshots.FirstOrDefault()?.SmallUrl}");
    Console.WriteLine($"Bandes-ann. : {media?.Trailers.Count ?? 0}  ex. {media?.Trailers.FirstOrDefault()?.Name}");
    Console.WriteLine($"  image     : {media?.Trailers.FirstOrDefault()?.ThumbnailUrl}");
    Console.WriteLine($"  teaser    : {media?.Trailers.FirstOrDefault()?.MicrotrailerUrl}");
    Console.WriteLine($"Page Steam  : {media?.StoreUrl}");
}

// Phase 9.1 : d'abord des exemples inventés (chaque ligne doit ressortir masquée),
// puis un vrai rapport construit avec TON journal, vérifié avant d'être affiché.
static void TestBugReport()
{
    string[] samples =
    {
        @"at Wyrmhold.Core.SteamProvider.GetInstalledGames() in C:\Users\jdupont\source\repos\WyrmHold\SteamProvider.cs:line 11",
        "GET https://api.steampowered.com/IPlayerService/GetOwnedGames/v1/?key=0123456789ABCDEF0123456789ABCDEF&steamid=76561198000000000",
        "Authorization: Bearer abcDEF123.456-xyz",
        "Jeton : eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjMifQ.c2lnbmF0dXJl",
        "Compte : jean.dupont@example.com",
        "Clé secrète maison : MaCleSecrete2026",
        "Le dossier de jdupont est introuvable (mais « jdupontel » doit rester)",
    };

    Console.WriteLine("=== Exemples (secret connu : MaCleSecrete2026, compte Windows : jdupont) ===");

    foreach (string sample in samples)
    {
        Console.WriteLine($"avant : {sample}");
        Console.WriteLine($"après : {ReportSanitizer.Clean(sample, new[] { "MaCleSecrete2026" }, "jdupont")}");
        Console.WriteLine();
    }

    BugReport report = BugReport.Create(AppSettings.Load(), CrashReporter.GetPendingCrashFile());
    report.Description = "Essai du rapport depuis la console (mode 7).";

    string text = report.ToText();
    string url = report.GetGitHubUrl();
    int leaks = ReportSanitizer.CountRemainingSecrets(text) + ReportSanitizer.CountRemainingSecrets(Uri.UnescapeDataString(url));

    Console.WriteLine("=== Vrai rapport ===");

    if (leaks > 0)
    {
        // Sécurité : on n'affiche pas un rapport qui contient encore un secret.
        Console.WriteLine($"ÉCHEC : {leaks} secret(s) connu(s) encore présent(s). Rapport non affiché.");
        return;
    }

    Console.WriteLine(text);
    Console.WriteLine();
    Console.WriteLine($"Secrets connus restants : 0. Nom du compte Windows présent : {text.Contains(Environment.UserName, StringComparison.OrdinalIgnoreCase)}");
    Console.WriteLine($"Lien GitHub : {url.Length} caractères (maximum 4000)");
}

// Phase 9.2 : la première lecture importe secrets.json (chiffré, puis supprimé), ensuite on essaie chaque clé.
static async Task TestApiKeysAsync()
{
    string legacy = Path.Combine(AppPaths.DataFolder, "secrets.json");
    Console.WriteLine($"secrets.json avant : {(File.Exists(legacy) ? "présent" : "absent")}");

    Secrets keys = Secrets.Current;

    Console.WriteLine($"secrets.json après : {(File.Exists(legacy) ? "présent" : "absent")}");
    Console.WriteLine($"Fichier chiffré    : {(SecureStore.Exists("api-keys") ? "présent" : "absent")}");
    Console.WriteLine();
    Console.WriteLine($"Steam        {ApiKeyTester.Mask(keys.SteamApiKey),-14} {(await ApiKeyTester.TestSteamAsync(keys.SteamApiKey, keys.SteamId)).Message}");
    Console.WriteLine($"SteamGridDB  {ApiKeyTester.Mask(keys.SteamGridDbApiKey),-14} {(await ApiKeyTester.TestSteamGridDbAsync(keys.SteamGridDbApiKey)).Message}");
    Console.WriteLine($"ITAD         {ApiKeyTester.Mask(keys.IsThereAnyDealApiKey),-14} {(await ApiKeyTester.TestIsThereAnyDealAsync(keys.IsThereAnyDealApiKey)).Message}");
    Console.WriteLine($"ITAD client  {ApiKeyTester.Mask(keys.IsThereAnyDealClientId)}");

    // Une mauvaise clé doit être reconnue comme telle (et pas comme « pas de réseau »).
    Console.WriteLine();
    Console.WriteLine($"Fausse clé Steam : {(await ApiKeyTester.TestSteamAsync(new string('0', 32), keys.SteamId)).Message}");
    Console.WriteLine($"Fausse clé SGDB  : {(await ApiKeyTester.TestSteamGridDbAsync("faussecle")).Message}");
    Console.WriteLine($"Fausse clé ITAD  : {(await ApiKeyTester.TestIsThereAnyDealAsync("faussecle")).Message}");
}

// Les succès d'un jeu lus dans les fichiers de Steam, puis la même liste demandée à la Web API :
// les deux doivent dire la même chose (sauf un succès tout juste débloqué, que la Web API voit en retard).
static async Task CompareLocalAchievementsAsync(string appId)
{
    Secrets keys = Secrets.Current;
    SteamLocalAchievements? local = SteamLocalAchievements.FromRegistry();

    if (local is null || !keys.HasSteam)
    {
        Console.WriteLine("Steam introuvable ou clé Steam absente.");
        return;
    }

    List<LocalAchievement>? fromDisk = local.Read(keys.SteamId, appId);

    if (fromDisk is null)
    {
        Console.WriteLine($"Fichiers absents dans {local.StatsFolder}.");
        return;
    }

    Console.WriteLine($"Disque : {fromDisk.Count(a => a.IsUnlocked)}/{fromDisk.Count} débloqués");

    foreach (LocalAchievement achievement in fromDisk.Where(a => a.IsUnlocked).OrderByDescending(a => a.UnlockedUnix).Take(3))
    {
        Console.WriteLine($"  {DateTimeOffset.FromUnixTimeSeconds(achievement.UnlockedUnix).LocalDateTime:dd/MM HH:mm:ss}  {achievement.Name} ({achievement.Id})");
    }

    HashSet<string> fromApi = await new SteamWebApi(keys).GetUnlockedAchievementIdsAsync(appId);
    HashSet<string> diskIds = fromDisk.Where(a => a.IsUnlocked).Select(a => a.Id).ToHashSet();

    Console.WriteLine($"Web API : {fromApi.Count} débloqués");
    Console.WriteLine($"Seulement sur le disque : {string.Join(", ", diskIds.Except(fromApi))}");
    Console.WriteLine($"Seulement dans la Web API : {string.Join(", ", fromApi.Except(diskIds))}");
}

static string ReadOrDefault(string defaultValue)
{
    string text = (Console.ReadLine() ?? "").Trim();
    return text.Length == 0 ? defaultValue : text;
}

static async Task CountAsync(IsThereAnyDealApi itad, string label, string[]? tags, bool matchAll)
{
    // 3 pages de 200 au plus : de quoi comparer, sans user la limite (100 requêtes / 5 minutes).
    const int PageSize = 200;
    const int MaxPages = 3;

    List<ItadDealListItem> items = new List<ItadDealListItem>();
    int offset = 0;
    bool hasMore = true;

    for (int page = 0; page < MaxPages && hasMore; page++)
    {
        ItadDealsPage result = tags == null
            ? await itad.GetDealsAsync(offset, PageSize, "")
            : await itad.GetDealsByTagsAsync(tags, matchAll, offset, PageSize, "");

        items.AddRange(result.List);
        offset = result.NextOffset;
        hasMore = result.HasMore;
    }

    string count = hasMore ? $"plus de {items.Count}" : items.Count.ToString();
    string examples = string.Join(" | ", items.Take(4).Select(item => item.Title));

    Console.WriteLine($"{label,-40} {count,12} promo(s)   ex. : {examples}");
}

// Mode 21 : sans LibraryService, pour ne toucher ni à la base ni aux réglages.
static void ShowEmulators()
{
    AppSettings settings = AppSettings.Load();
    EmulatorProvider[] emulators =
    {
        new RetroArchProvider(settings), new DolphinProvider(settings), new RyujinxProvider(settings), new AzaharProvider(settings)
    };

    foreach (EmulatorProvider emulator in emulators)
    {
        string? executable = emulator.FindExecutable();
        List<Game> games = emulator.GetInstalledGames();

        Console.WriteLine($"=== {emulator.Name} : {executable ?? "NON TROUVÉ"}");
        Console.WriteLine($"    Dossiers de l'émulateur : {string.Join(" | ", emulator.GetConfiguredFolders())}");
        Console.WriteLine($"    Dossiers ajoutés : {string.Join(" | ", emulator.Settings.GameFolders)}");

        if (emulator is RetroArchProvider retroArch)
        {
            Console.WriteLine($"    Cœurs installés : {retroArch.GetInstalledCores().Count}");
        }

        foreach (Game game in games)
        {
            Console.WriteLine($"    {game.Name} [{game.ConsoleName}] id={game.PlatformGameId} {game.SizeText} {game.PlaytimeText}");

            try
            {
                System.Diagnostics.ProcessStartInfo start = emulator.BuildStartInfo(game, executable!);
                Console.WriteLine($"      > {start.FileName} {string.Join(" ", start.ArgumentList.Select(argument => $"\"{argument}\""))}");
            }
            catch (EmulatorLaunchException ex)
            {
                Console.WriteLine($"      > IMPOSSIBLE : {ex.Message.Split('\n')[0]}");
            }
        }
    }
}

// Mode 22 : de faux fichiers, écrits dans un dossier temporaire puis effacés.
static async Task TestEmulationAsync()
{
    string folder = Path.Combine(Path.GetTempPath(), $"wyrmhold-test-{Guid.NewGuid():N}");
    Directory.CreateDirectory(folder);
    int failures = 0;

    void Check(string label, bool isOk, object? got = null)
    {
        Console.WriteLine($"{(isOk ? "✔" : "✘")} {label}{(isOk ? "" : $" → {got}")}");
        failures += isOk ? 0 : 1;
    }

    static void Put(byte[] target, int offset, byte[] bytes) => bytes.CopyTo(target, offset);
    static byte[] Ascii(string text) => System.Text.Encoding.ASCII.GetBytes(text);
    static byte[] Utf16(string text) => System.Text.Encoding.Unicode.GetBytes(text);
    static byte[] BigEndian(uint value) => new[] { (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value };

    // Un en-tête de disque : numéro de jeu, marque de la console, nom.
    static byte[] Disc(string id, bool isWii, string name)
    {
        byte[] header = new byte[0x100];
        Put(header, 0, Ascii(id));
        Put(header, isWii ? 0x18 : 0x1C, BigEndian(isWii ? 0x5D1C9EA3 : 0xC2339F3D));
        Put(header, 0x20, Ascii(name));
        return header;
    }

    string Save(string name, byte[] bytes)
    {
        string path = Path.Combine(folder, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    try
    {
        // --- Disques ---
        byte[] gc = Disc("GALE01", false, "Super Smash Bros Melee");
        byte[] wii = Disc("RMCP01", true, "MARIO KART Wii");

        DiscImageInfo? iso = DiscImageHeader.TryRead(Save("a.iso", gc));
        Check("ISO GameCube", iso is { GameId: "GALE01", Name: "Super Smash Bros Melee", Console: GameConsole.GameCube }, iso);

        byte[] wbfs = new byte[0x400];
        Put(wbfs, 0, Ascii("WBFS"));
        wbfs[8] = 9;
        Put(wbfs, 0x200, wii);
        DiscImageInfo? wbfsInfo = DiscImageHeader.TryRead(Save("b.wbfs", wbfs));
        Check("WBFS Wii", wbfsInfo is { GameId: "RMCP01", Console: GameConsole.Wii }, wbfsInfo);

        byte[] rvz = new byte[0x200];
        Put(rvz, 0, Ascii("RVZ\u0001"));
        Put(rvz, 0x58, wii.Take(0x80).ToArray());
        DiscImageInfo? rvzInfo = DiscImageHeader.TryRead(Save("c.rvz", rvz));
        Check("RVZ Wii", rvzInfo is { GameId: "RMCP01", Name: "MARIO KART Wii" }, rvzInfo);

        byte[] ciso = new byte[0x8100];
        Put(ciso, 0, Ascii("CISO"));
        Put(ciso, 0x8000, gc);
        DiscImageInfo? cisoInfo = DiscImageHeader.TryRead(Save("d.ciso", ciso));
        Check("CISO GameCube", cisoInfo is { GameId: "GALE01" }, cisoInfo);

        // GCZ : un bloc compressé par zlib, après l'en-tête, sa position et sa somme de contrôle.
        using (MemoryStream compressed = new MemoryStream())
        {
            using (System.IO.Compression.ZLibStream zlib = new System.IO.Compression.ZLibStream(compressed, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
            {
                zlib.Write(gc.Concat(new byte[0x3F00]).ToArray());
            }

            byte[] gcz = new byte[32 + 8 + 4 + compressed.Length];
            Put(gcz, 0, BitConverter.GetBytes(0xB10BC001));
            Put(gcz, 20 + 4, BitConverter.GetBytes(0x4000));
            Put(gcz, 28, BitConverter.GetBytes(1));
            Put(gcz, 44, compressed.ToArray());
            DiscImageInfo? gczInfo = DiscImageHeader.TryRead(Save("e.gcz", gcz));
            Check("GCZ GameCube (compressé)", gczInfo is { GameId: "GALE01", Name: "Super Smash Bros Melee" }, gczInfo);
        }

        Check("Fichier qui n'est pas un disque", DiscImageHeader.TryRead(Save("f.iso", new byte[0x200])) is null);

        // --- DS ---
        byte[] nds = new byte[0x1000];
        Put(nds, 0x68, BitConverter.GetBytes(0x400));
        Put(nds, 0x400 + 0x340, Utf16("Pokemon Platinum\nVersion\nNintendo"));
        string? dsTitle = DsRomHeader.TryReadTitle(Save("g.nds", nds));
        Check("Titre DS sans l'éditeur", dsTitle == "Pokemon Platinum Version", dsTitle);

        // --- 3DS : NCSD → NCCH (0x200) → ExeFS (0x400) → icon (0x600) ---
        byte[] cci = new byte[0x4000];
        Put(cci, 0x100, Ascii("NCSD"));
        Put(cci, 0x120, BitConverter.GetBytes(1));
        Put(cci, 0x300, Ascii("NCCH"));
        Put(cci, 0x318, BitConverter.GetBytes(0x0004000000055D00UL));
        cci[0x38F] = 4;
        Put(cci, 0x3A0, BitConverter.GetBytes(1));
        Put(cci, 0x400, Ascii("icon"));
        Put(cci, 0x600, Ascii("SMDH"));
        Put(cci, 0x600 + 8 + 0x200, Utf16("Pokemon X"));
        ThreeDsRomInfo? threeDs = ThreeDsRomHeader.TryRead(Save("h.3ds", cci));
        Check("3DS déchiffré", threeDs is { TitleId: "0004000000055d00", Title: "Pokemon X" }, threeDs);

        cci[0x38F] = 0;
        ThreeDsRomInfo? encrypted = ThreeDsRomHeader.TryRead(Save("i.3ds", cci));
        Check("3DS chiffré : Title ID sans titre", encrypted is { TitleId: "0004000000055d00", Title: null }, encrypted);

        // --- Switch ---
        static byte[] Pfs0(params (string Name, byte[] Data)[] files)
        {
            byte[] names = files.SelectMany(file => Ascii(file.Name + "\0")).ToArray();
            using MemoryStream stream = new MemoryStream();
            using BinaryWriter writer = new BinaryWriter(stream);
            writer.Write(Ascii("PFS0"));
            writer.Write(files.Length);
            writer.Write(names.Length);
            writer.Write(0);

            long offset = 0;
            int nameOffset = 0;

            foreach ((string name, byte[] data) in files)
            {
                writer.Write(offset);
                writer.Write((long)data.Length);
                writer.Write(nameOffset);
                writer.Write(0);
                offset += data.Length;
                nameOffset += name.Length + 1;
            }

            writer.Write(names);

            foreach ((_, byte[] data) in files)
            {
                writer.Write(data);
            }

            return stream.ToArray();
        }

        SwitchPackageInfo? xml = SwitchPackageReader.TryRead(Save("j.nsp", Pfs0(
            ("abc.nca", new byte[64]),
            ("abc.cnmt.xml", Ascii("<ContentMeta><Type>Application</Type><Id>0x010051f0207b2000</Id></ContentMeta>")))));
        Check("NSP sans ticket (.cnmt.xml)", xml is { TitleId: "010051f0207b2000", IsBaseGame: true }, xml);

        SwitchPackageInfo? update = SwitchPackageReader.TryRead(Save("k.nsp", Pfs0(
            ("0100187003a368000000000000000007.tik", new byte[64]))));
        Check("NSP de mise à jour (.tik)", update is { TitleId: "0100187003a36800", IsBaseGame: false }, update);

        // --- Noms ---
        foreach ((string input, string expected) in new[]
        {
            ("Pokemon - Emerald Version (USA, Europe)", "Pokemon - Emerald Version"),
            ("Legend of Zelda, The - The Minish Cap (Europe) (En,Fr,De,Es,It)", "The Legend of Zelda - The Minish Cap"),
            ("Super Mario World [!]", "Super Mario World"),
            ("Pokémon Let’s Go, Eevee! [0100187003A36000][v0]", "Pokémon Let’s Go, Eevee!"),
            ("Tomodachi Life Living the Dream", "Tomodachi Life Living the Dream"),
            ("mario_kart_ds (U)", "mario kart ds"),
            ("(Europe)", "(Europe)")
        })
        {
            string cleaned = NameTools.CleanRomName(input);
            Check($"Nom : {input}", cleaned == expected, cleaned);
        }

        Check("Title ID dans un nom", NameTools.FindSwitchTitleId("Pokemon__0100187003A36800__v131072") == "0100187003a36800");
        Check("Identifiant sans accents", NameTools.ToSlug("Pokémon Émeraude (France)") == "pokemon-emeraude-france", NameTools.ToSlug("Pokémon Émeraude (France)"));

        // --- Configurations ---
        List<RetroArchProvider.PlaylistItem> items = RetroArchProvider.ParsePlaylist("""
            { "version": "1.5", "default_core_path": "", "items": [
              { "path": "D:\\GBA\\jeux.zip#Golden Sun (USA).gba", "label": "Golden Sun (USA)", "core_path": "DETECT", "core_name": "DETECT", "crc32": "DETECT", "db_name": "Nintendo - Game Boy Advance.lpl" }
            ] }
            """);
        Check("Playlist .lpl", items.Count == 1 && items[0].Path.EndsWith("#Golden Sun (USA).gba") && items[0].Label == "Golden Sun (USA)", items.Count);

        Check("retroarch.cfg", RetroArchProvider.ReadConfigValue(new[] { "a = \"b\"", "playlist_directory = \":\\playlists\"" }, "playlist_directory") == @":\playlists");

        List<string> isoPaths = DolphinProvider.ReadIsoPaths(new[]
        {
            "[Core]", "ISOPath0 = C:\\Faux", "[General]", "ISOPaths = 2", "ISOPath0 = D:\\Wii", "ISOPath1 = E:\\GC", "RecursiveISOPaths = True"
        });
        Check("Dolphin.ini", isoPaths.SequenceEqual(new[] { @"D:\Wii", @"E:\GC" }), string.Join("|", isoPaths));

        AzaharProvider.AzaharConfig config = AzaharProvider.ParseConfig(new[]
        {
            "[Data%20Storage]", "use_custom_storage=true", @"sdmc_directory=C:\\Users\\moi/Azahar/sdmc/",
            "[UI]", @"Paths\gamedirs\1\path=INSTALLED", @"Paths\gamedirs\1\deep_scan\default=true", @"Paths\gamedirs\1\deep_scan=false",
            @"Paths\gamedirs\2\path=D:\\3DS\\Jeux", @"Paths\gamedirs\2\deep_scan=true", @"Paths\gamedirs\size=2"
        });
        Check("qt-config.ini", config.GameFolders.Count == 2 && config.GameFolders[1] is { Path: @"D:\3DS\Jeux", IncludeSubfolders: true }
            && config.SdmcFolder == @"C:\Users\moi/Azahar/sdmc/", config);

        // --- Temps de jeu de bout en bout : une copie de cmd.exe appelée Ryujinx.exe joue l'émulateur ---
        string romPath = @"D:\ROMS\Jeu Test é.nsp";
        string fakeEmulator = Path.Combine(folder, RyujinxProvider.Executable);
        File.Copy(Path.Combine(Environment.SystemDirectory, "cmd.exe"), fakeEmulator);

        Game game = new Game { Platform = Platform.Ryujinx, PlatformGameId = "test", Name = "Jeu Test", IsInstalled = true, InstallPath = romPath };
        Game other = new Game { Platform = Platform.Ryujinx, PlatformGameId = "autre", Name = "Autre", IsInstalled = true, InstallPath = @"D:\ROMS\Autre.nsp" };

        using System.Diagnostics.Process emulator = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(fakeEmulator)
        {
            Arguments = $"/c ping -n 60 127.0.0.1 >nul & rem \"{romPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true
        })!;

        string? commandLine = NativeProcesses.GetCommandLine(emulator.Id);
        Check("Ligne de commande lue (accent compris)", commandLine?.Contains(romPath) == true, commandLine);

        TaskCompletionSource<string> started = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<string> stopped = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        GameWatcher watcher = new GameWatcher();
        watcher.GameStarted += running => started.TrySetResult(running.Game.Name);
        watcher.GameStopped += (running, _) => stopped.TrySetResult(running.Game.Name);
        watcher.SetGames(new[] { other, game });
        watcher.Start();

        Task firstResult = await Task.WhenAny(started.Task, Task.Delay(TimeSpan.FromSeconds(8)));
        Check("Détecteur : le bon jeu démarre", firstResult == started.Task && started.Task.Result == "Jeu Test");

        emulator.Kill(entireProcessTree: true);
        Task secondResult = await Task.WhenAny(stopped.Task, Task.Delay(TimeSpan.FromSeconds(20)));
        Check("Détecteur : le jeu s'arrête avec l'émulateur", secondResult == stopped.Task && stopped.Task.Result == "Jeu Test");
        watcher.Stop();
    }
    finally
    {
        Directory.Delete(folder, recursive: true);
    }

    Console.WriteLine(failures == 0 ? "\nTout est bon." : $"\n{failures} essai(s) raté(s).");
}
