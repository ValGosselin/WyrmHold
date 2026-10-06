using System.Net;
using System.Text.Json;

namespace Wyrmhold.Core;

public class LibraryService
{
    private const string GogTokenName = "gog";
    private const string SteamFamilyMarkerName = "steam-family";
    private const string EpicMarkerName = "epic";
    private const string UbisoftMarkerName = "ubisoft";
    private const string EaMarkerName = "ea";
    private const string EaOrderIdPrefix = "order:";
    private const string BattleNetMarkerName = "battlenet";

    private readonly List<ILibraryProvider> _providers = new List<ILibraryProvider>
    {
        new SteamProvider(),
        new EpicProvider(),
        new UbisoftProvider(),
        new BattleNetProvider(),
        new GogProvider(),
        new EaProvider()
    };

    private readonly GameDatabase _database = new GameDatabase();
    private readonly CoverCache _covers = new CoverCache();
    private readonly MetadataFetcher _metadata = new MetadataFetcher();
    private readonly PlaytimeTracker _tracker = new PlaytimeTracker();
    private readonly HashSet<string> _activeSessions = new HashSet<string>();
    private readonly EpicCatalogCache _epicCatalog = new EpicCatalogCache();
    private readonly Dictionary<string, string> _ubisoftCoverUrls = new Dictionary<string, string>();

    private readonly Secrets _secrets;
    private readonly SteamWebApi _steamApi;
    private readonly SteamGridDbApi _steamGridDb;

    public LibraryService()
    {
        _secrets = Secrets.Load();
        _steamApi = new SteamWebApi(_secrets);
        _steamGridDb = new SteamGridDbApi(_secrets.SteamGridDbApiKey);
        _database.Initialize();
    }

    // ----- Propriétés -----

    public string SteamId => _secrets.SteamId;

    public bool HasActiveSessions => _activeSessions.Count > 0;

    public bool IsGogConnected => SecureStore.Exists(GogTokenName);

    public bool IsSteamApiConfigured => _steamApi.IsConfigured;

    public bool IsSteamFamilyConnected => SecureStore.Exists(SteamFamilyMarkerName);

    public bool IsEpicConnected => SecureStore.Exists(EpicMarkerName);

    public bool IsUbisoftConnected => SecureStore.Exists(UbisoftMarkerName);

    public bool IsEaConnected => SecureStore.Exists(EaMarkerName);

    public bool IsBattleNetConnected => SecureStore.Exists(BattleNetMarkerName);

    // ----- Scan et chargement -----

    public async Task<List<Game>> ScanAllAsync()
    {
        foreach (ILibraryProvider provider in _providers)
        {
            try
            {
                List<Game> games = provider.GetInstalledGames();

                if (provider.Platform == Platform.Steam)
                {
                    await AddSteamAccountGamesAsync(games);
                }

                _database.SaveGames(provider.Platform, games);
            }
            catch (Exception ex)
            {
                Logger.Log($"Erreur avec {provider.Platform} : {ex}");
            }
        }

        try
        {
            await SyncGogAsync();
        }
        catch (Exception ex)
        {
            Logger.Log($"Synchronisation GOG impossible : {ex.Message}");
        }

        // Si le compte Ubisoft est connecté, la synchronisation silencieuse
        // fait l'import complet (cache local + site) juste après.
        if (!IsUbisoftConnected)
        {
            try
            {
                ImportUbisoftLibrary(null);
            }
            catch (Exception ex)
            {
                Logger.Log($"Import local d'Ubisoft Connect impossible : {ex.Message}");
            }
        }

        return LoadGames();
    }

    public List<Game> LoadGames()
    {
        List<Game> games = _database.LoadGames();
        _covers.AttachCovers(games);
        return games;
    }

    public async Task DownloadCoversAsync(List<Game> games)
    {
        await _covers.DownloadCoversFromUrlsAsync(
            games.Where(g => g.Platform == Platform.Epic),
            game => _epicCatalog.Find(game.PlatformGameId)?.CoverUrl);

        await _covers.DownloadCoversFromUrlsAsync(
            games.Where(g => g.Platform == Platform.Ubisoft),
            game => _ubisoftCoverUrls.GetValueOrDefault(game.PlatformGameId));

        await _covers.DownloadMissingSteamCoversAsync(games);
        await _covers.DownloadMissingCoversFromSteamGridDbAsync(games, _steamGridDb);
        _covers.AttachCovers(games);
    }

    public async Task UpdateMetadataAsync(List<Game> games)
    {
        List<Game> updatedGames = await _metadata.FetchMissingAsync(games);

        if (updatedGames.Count > 0)
        {
            _database.SaveMetadata(updatedGames);
        }
    }

    // ----- Steam : compte et famille -----

    private async Task AddSteamAccountGamesAsync(List<Game> steamGames)
    {
        try
        {
            List<SteamOwnedGame> ownedGames = await _steamApi.GetOwnedGamesAsync();

            if (ownedGames.Count == 0)
            {
                return;
            }

            HashSet<string> ownedIds = ownedGames.Select(g => g.AppId.ToString()).ToHashSet();

            foreach (Game game in steamGames)
            {
                game.IsFamilyShared = !ownedIds.Contains(game.PlatformGameId);
            }

            foreach (SteamOwnedGame owned in ownedGames)
            {
                Game game = FindOrAddSteamGame(steamGames, owned);
                game.PlaytimeMinutes = owned.PlaytimeMinutes;
                game.LastPlayedUnix = owned.LastPlayedUnix;
                game.IsFamilyShared = false;
            }

            List<SteamOwnedGame> recentGames = await _steamApi.GetRecentlyPlayedGamesAsync();

            foreach (SteamOwnedGame recent in recentGames.Where(g => !ownedIds.Contains(g.AppId.ToString())))
            {
                Game game = FindOrAddSteamGame(steamGames, recent);
                game.PlaytimeMinutes = recent.PlaytimeMinutes;
                game.IsFamilyShared = true;
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"API Steam indisponible : {ex.Message}");
        }
    }

    private static Game FindOrAddSteamGame(List<Game> steamGames, SteamOwnedGame apiGame)
    {
        string appId = apiGame.AppId.ToString();
        Game? game = steamGames.FirstOrDefault(g => g.PlatformGameId == appId);

        if (game is null)
        {
            game = new Game
            {
                Platform = Platform.Steam,
                PlatformGameId = appId,
                Name = apiGame.Name,
                IsInstalled = false
            };

            steamGames.Add(game);
        }

        return game;
    }

    public async Task<int> ImportFamilyLibraryAsync(string accessToken)
    {
        List<SharedLibraryApp> apps = await _steamApi.GetFamilyLibraryAsync(accessToken);

        List<Game> familyGames = apps
            .Where(a => a.ExcludeReason == 0
                        && a.AppType == 1
                        && !a.OwnerSteamIds.Contains(_secrets.SteamId))
            .Select(a => new Game
            {
                Platform = Platform.Steam,
                PlatformGameId = a.AppId.ToString(),
                Name = a.Name,
                PlaytimeMinutes = a.PlaytimeMinutes,
                LastPlayedUnix = a.LastPlayedUnix,
                IsFamilyShared = true,
                OwnerSteamId = a.OwnerSteamIds.FirstOrDefault()
            })
            .ToList();

        _database.SaveFamilyGames(familyGames);
        SecureStore.Save(SteamFamilyMarkerName, _secrets.SteamId);
        return familyGames.Count;
    }

    public void ForgetSteamFamilySession()
    {
        SecureStore.Delete(SteamFamilyMarkerName);
    }

    public void DisconnectSteamFamily()
    {
        SecureStore.Delete(SteamFamilyMarkerName);
        _database.RemoveFamilyGames();

        string webViewFolder = AppPaths.GetWebViewFolder(Platform.Steam);

        if (Directory.Exists(webViewFolder))
        {
            Directory.Delete(webViewFolder, recursive: true);
        }
    }

    // ----- GOG -----

    public async Task<int> ConnectGogAsync(string loginCode)
    {
        GogTokens? tokens = await GogApi.ExchangeCodeAsync(loginCode);

        if (tokens is null)
        {
            throw new InvalidOperationException("GOG n'a pas fourni de jeton d'accès.");
        }

        return await ImportGogLibraryAsync(tokens);
    }

    public async Task<int> SyncGogAsync()
    {
        string? refreshToken = SecureStore.Load(GogTokenName);

        if (refreshToken is null)
        {
            return 0;
        }

        GogTokens? tokens;

        try
        {
            tokens = await GogApi.RefreshAsync(refreshToken);
        }
        catch (HttpRequestException ex)
            when (ex.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
        {
            SecureStore.Delete(GogTokenName);
            throw new InvalidOperationException("La session GOG a expiré : reconnecte-toi dans l'onglet Comptes.");
        }

        if (tokens is null)
        {
            throw new InvalidOperationException("GOG n'a pas renouvelé le jeton d'accès.");
        }

        return await ImportGogLibraryAsync(tokens);
    }

    public void DisconnectGog()
    {
        SecureStore.Delete(GogTokenName);
        _database.SaveOwnedGames(Platform.Gog, new List<Game>());

        string webViewFolder = AppPaths.GetWebViewFolder(Platform.Gog);

        if (Directory.Exists(webViewFolder))
        {
            Directory.Delete(webViewFolder, recursive: true);
        }
    }

    private async Task<int> ImportGogLibraryAsync(GogTokens tokens)
    {
        SecureStore.Save(GogTokenName, tokens.RefreshToken);

        List<long> ownedIds = await GogApi.GetOwnedGameIdsAsync(tokens.AccessToken);
        List<GogProduct> products = await GogApi.GetProductsAsync(ownedIds);

        List<Game> games = products
            .Where(product => product.GameType == "game")
            .Select(product => new Game
            {
                Platform = Platform.Gog,
                PlatformGameId = product.Id.ToString(),
                Name = product.Title
            })
            .ToList();

        _database.SaveOwnedGames(Platform.Gog, games);
        return games.Count;
    }

    // ----- Epic Games -----

    public async Task<int> ImportEpicLibraryAsync(string authorizationCode)
    {
        string? accessToken = await EpicApi.ExchangeCodeAsync(authorizationCode);

        if (string.IsNullOrEmpty(accessToken))
        {
            throw new InvalidOperationException("Epic n'a pas fourni de jeton d'accès.");
        }

        List<EpicLibraryRecord> records = await EpicApi.GetLibraryRecordsAsync(accessToken);
        List<Game> games = new List<Game>();

        foreach (EpicLibraryRecord record in records)
        {
            if (string.IsNullOrEmpty(record.AppName)
                || record.Namespace == "ue"
                || games.Any(g => g.PlatformGameId == record.AppName))
            {
                continue;
            }

            EpicCatalogEntry? entry = _epicCatalog.Find(record.AppName);

            if (entry is null)
            {
                EpicCatalogItem? item = await EpicApi.GetCatalogItemAsync(
                    record.Namespace, record.CatalogItemId, accessToken);

                if (item is null)
                {
                    Logger.Log($"Jeu Epic introuvable dans le catalogue : {record.AppName}");
                    continue;
                }

                entry = new EpicCatalogEntry
                {
                    Title = item.Title,
                    IsGame = item.MainGameItem is null && item.Categories.Any(c => c.Path == "games"),
                    CoverUrl = FindEpicCoverUrl(item)
                };

                _epicCatalog.Set(record.AppName, entry);
            }

            if (entry.IsGame)
            {
                games.Add(new Game
                {
                    Platform = Platform.Epic,
                    PlatformGameId = record.AppName,
                    Name = entry.Title
                });
            }
        }

        _epicCatalog.Save();
        _database.SaveOwnedGames(Platform.Epic, games);
        SecureStore.Save(EpicMarkerName, "connected");

        return games.Count;
    }

    private static string? FindEpicCoverUrl(EpicCatalogItem item)
    {
        EpicKeyImage? image = item.KeyImages.FirstOrDefault(i => i.Type == "DieselGameBoxTall")
            ?? item.KeyImages.FirstOrDefault(i => i.Type == "OfferImageTall")
            ?? item.KeyImages.FirstOrDefault(i => i.Height > i.Width);

        return image?.Url;
    }

    public void ForgetEpicSession()
    {
        SecureStore.Delete(EpicMarkerName);
    }

    public void DisconnectEpic()
    {
        SecureStore.Delete(EpicMarkerName);
        _database.SaveOwnedGames(Platform.Epic, new List<Game>());

        string webViewFolder = AppPaths.GetWebViewFolder(Platform.Epic);

        if (Directory.Exists(webViewFolder))
        {
            Directory.Delete(webViewFolder, recursive: true);
        }
    }

    // ----- Ubisoft Connect -----

    /// <summary>
    /// Importe la bibliothèque Ubisoft en combinant deux sources :
    /// 1. le cache local d'Ubisoft Connect (les jeux possédés, toujours lu) ;
    /// 2. le site d'Ubisoft (les jeux joués, seulement si capture n'est pas null).
    /// </summary>
    public int ImportUbisoftLibrary(UbisoftCapture? capture)
    {
        List<UbisoftLocalGame> localGames;

        try
        {
            localGames = UbisoftLocalLibrary.ReadOwnedGames();
        }
        catch (Exception ex)
        {
            Logger.Log($"Cache local d'Ubisoft Connect illisible : {ex.Message}");

            if (capture is null)
            {
                // Rien de neuf à importer : on garde la bibliothèque déjà enregistrée.
                return 0;
            }

            localGames = new List<UbisoftLocalGame>();
        }

        List<UbisoftPlayedGame> playedGames = new List<UbisoftPlayedGame>();
        Dictionary<string, UbisoftPlayedGame> playedBySpaceId = new Dictionary<string, UbisoftPlayedGame>();
        Dictionary<string, UbisoftCatalogGame> catalog = new Dictionary<string, UbisoftCatalogGame>();

        if (capture is not null)
        {
            playedGames = ReadUbisoftPlayedGames(capture);
            catalog = ReadUbisoftCatalog(capture);
            _ubisoftCoverUrls.Clear();

            foreach (UbisoftPlayedGame playedGame in playedGames)
            {
                playedBySpaceId.TryAdd(playedGame.SpaceId, playedGame);
            }
        }

        HashSet<string> knownNames = _database.LoadGames()
            .Where(g => g.Platform != Platform.Ubisoft || g.IsInstalled)
            .Select(g => NameTools.Normalize(g.Name))
            .ToHashSet();

        HashSet<string> knownSpaceIds = new HashSet<string>();
        List<Game> games = new List<Game>();

        // 1. Les jeux possédés, lus dans le cache local.
        foreach (UbisoftLocalGame localGame in localGames)
        {
            string gameId = localGame.ProductId.ToString();

            if (games.Any(g => g.PlatformGameId == gameId))
            {
                continue;
            }

            Game game = new Game
            {
                Platform = Platform.Ubisoft,
                PlatformGameId = gameId,
                Name = NameTools.CleanForSearch(localGame.Name)
            };

            knownNames.Add(NameTools.Normalize(localGame.Name));

            if (localGame.SpaceId is not null)
            {
                knownSpaceIds.Add(localGame.SpaceId);

                if (capture is not null)
                {
                    game.PlaytimeMinutes = ReadUbisoftPlaytimeMinutes(capture, localGame.SpaceId);
                }

                if (playedBySpaceId.TryGetValue(localGame.SpaceId, out UbisoftPlayedGame? playedGame))
                {
                    game.LastPlayedUnix = playedGame.LastPlayed?.UpdatedAt.ToUnixTimeSeconds() ?? 0;
                }

                if (catalog.TryGetValue(localGame.SpaceId, out UbisoftCatalogGame? info))
                {
                    AddUbisoftCoverUrl(gameId, info);
                }
            }

            games.Add(game);
        }

        // 2. Les jeux joués, lus sur le site (ceux qui ne sont pas déjà dans la liste).
        foreach (UbisoftPlayedGame playedGame in playedGames)
        {
            if (knownSpaceIds.Contains(playedGame.SpaceId)
                || !playedGame.Applications.Any(a => a.ApplicationPlatformType == "PC")
                || !catalog.TryGetValue(playedGame.SpaceId, out UbisoftCatalogGame? info)
                || !knownNames.Add(NameTools.Normalize(info.DisplayName)))
            {
                continue;
            }

            games.Add(new Game
            {
                Platform = Platform.Ubisoft,
                PlatformGameId = playedGame.SpaceId,
                Name = NameTools.CleanForSearch(info.DisplayName),
                PlaytimeMinutes = ReadUbisoftPlaytimeMinutes(capture!, playedGame.SpaceId),
                LastPlayedUnix = playedGame.LastPlayed?.UpdatedAt.ToUnixTimeSeconds() ?? 0
            });

            AddUbisoftCoverUrl(playedGame.SpaceId, info);
        }

        _database.SaveOwnedGames(Platform.Ubisoft, games);

        if (capture is not null)
        {
            SecureStore.Save(UbisoftMarkerName, "connected");
        }

        return games.Count;
    }

    private static List<UbisoftPlayedGame> ReadUbisoftPlayedGames(UbisoftCapture capture)
    {
        if (capture.GamesPlayedJson is null)
        {
            throw new InvalidOperationException("La liste des jeux Ubisoft n'a pas été reçue.");
        }

        UbisoftGamesPlayedResponse? played = JsonSerializer.Deserialize<UbisoftGamesPlayedResponse>(capture.GamesPlayedJson);

        if (played is null)
        {
            throw new InvalidOperationException("La liste des jeux Ubisoft est illisible.");
        }

        return played.GamesPlayed;
    }

    private static Dictionary<string, UbisoftCatalogGame> ReadUbisoftCatalog(UbisoftCapture capture)
    {
        Dictionary<string, UbisoftCatalogGame> catalog = new Dictionary<string, UbisoftCatalogGame>();

        foreach (string catalogJson in capture.CatalogJsons)
        {
            UbisoftCatalogResponse? response = JsonSerializer.Deserialize<UbisoftCatalogResponse>(catalogJson);

            foreach (UbisoftCatalogGame catalogGame in response?.Games ?? new List<UbisoftCatalogGame>())
            {
                catalog[catalogGame.SpaceId] = catalogGame;
            }
        }

        return catalog;
    }

    private void AddUbisoftCoverUrl(string gameId, UbisoftCatalogGame info)
    {
        string? coverUrl = info.ImageUrls?.HighBoxArt ?? info.ImageUrls?.LowBoxArt;

        if (coverUrl is not null)
        {
            _ubisoftCoverUrls[gameId] = coverUrl;
        }
    }

    private static int ReadUbisoftPlaytimeMinutes(UbisoftCapture capture, string spaceId)
    {
        if (!capture.StatsJsonBySpaceId.TryGetValue(spaceId, out string? json))
        {
            return 0;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);

            if (document.RootElement.TryGetProperty("stats", out JsonElement stats)
                && stats.TryGetProperty("Playtime", out JsonElement playtime)
                && playtime.TryGetProperty("value", out JsonElement value)
                && long.TryParse(value.GetString(), out long seconds))
            {
                return (int)(seconds / 60);
            }
        }
        catch (JsonException)
        {
        }

        return 0;
    }

    public void ForgetUbisoftSession()
    {
        SecureStore.Delete(UbisoftMarkerName);
    }

    public void DisconnectUbisoft()
    {
        SecureStore.Delete(UbisoftMarkerName);
        _database.SaveOwnedGames(Platform.Ubisoft, new List<Game>());

        // On retire les jeux venus du site, mais on garde ceux du cache local.
        ImportUbisoftLibrary(null);

        string webViewFolder = AppPaths.GetWebViewFolder(Platform.Ubisoft);

        if (Directory.Exists(webViewFolder))
        {
            Directory.Delete(webViewFolder, recursive: true);
        }
    }

    // ----- EA app -----

    /// <summary>
    /// Importe les jeux EA possédés à partir de l'historique des commandes du compte.
    /// </summary>
    public int ImportEaLibrary(string orderHistoryJson)
    {
        List<string> ownedNames = EaOrderHistory.ReadOwnedGameNames(orderHistoryJson);

        // Les jeux EA déjà connus grâce au registre, avec leur vrai identifiant (contentID).
        Dictionary<string, string> knownContentIds = new Dictionary<string, string>();

        foreach (Game knownGame in _database.LoadGames())
        {
            if (knownGame.Platform == Platform.Ea && !knownGame.PlatformGameId.StartsWith(EaOrderIdPrefix))
            {
                knownContentIds.TryAdd(NameTools.Normalize(knownGame.Name), knownGame.PlatformGameId);
            }
        }

        List<Game> games = new List<Game>();

        foreach (string name in ownedNames)
        {
            string normalizedName = NameTools.Normalize(name);

            // L'historique ne donne pas d'identifiant de jeu : si on connaît déjà ce jeu
            // par son nom, on reprend son contentID, sinon on en fabrique un à partir du nom.
            string gameId = knownContentIds.TryGetValue(normalizedName, out string? contentId)
                ? contentId
                : EaOrderIdPrefix + normalizedName;

            if (games.Any(g => g.PlatformGameId == gameId))
            {
                continue;
            }

            games.Add(new Game
            {
                Platform = Platform.Ea,
                PlatformGameId = gameId,
                Name = name
            });
        }

        _database.SaveOwnedGames(Platform.Ea, games);
        SecureStore.Save(EaMarkerName, "connected");

        return games.Count;
    }

    public void ForgetEaSession()
    {
        SecureStore.Delete(EaMarkerName);
    }

    public void DisconnectEa()
    {
        SecureStore.Delete(EaMarkerName);
        _database.SaveOwnedGames(Platform.Ea, new List<Game>());

        string webViewFolder = AppPaths.GetWebViewFolder(Platform.Ea);

        if (Directory.Exists(webViewFolder))
        {
            Directory.Delete(webViewFolder, recursive: true);
        }
    }

    // ----- Battle.net -----

    /// <summary>
    /// Importe les jeux du compte Battle.net à partir de la réponse de account.battle.net.
    /// </summary>
    public int ImportBattleNetLibrary(string gamesJson)
    {
        List<BattleNetOwnedGame> ownedGames = BattleNetAccount.ReadOwnedGames(gamesJson);

        // Les jeux installés sont identifiés par leur uid du registre (ex. « hs_beta ») :
        // on les retrouve par leur nom pour ne pas créer de doublon.
        Dictionary<string, string> knownIds = new Dictionary<string, string>();

        foreach (Game knownGame in _database.LoadGames())
        {
            if (knownGame.Platform == Platform.BattleNet && knownGame.IsInstalled)
            {
                knownIds.TryAdd(NameTools.Normalize(knownGame.Name), knownGame.PlatformGameId);
            }
        }

        List<Game> games = new List<Game>();

        foreach (BattleNetOwnedGame owned in ownedGames)
        {
            string gameId = knownIds.TryGetValue(NameTools.Normalize(owned.Name), out string? installedId)
                ? installedId
                : owned.LaunchCode;

            if (games.Any(g => g.PlatformGameId == gameId))
            {
                continue;
            }

            games.Add(new Game
            {
                Platform = Platform.BattleNet,
                PlatformGameId = gameId,
                Name = owned.Name,
                LastPlayedUnix = owned.LastPlayedUnix
            });
        }

        _database.SaveOwnedGames(Platform.BattleNet, games);
        SecureStore.Save(BattleNetMarkerName, "connected");

        return games.Count;
    }

    public void ForgetBattleNetSession()
    {
        SecureStore.Delete(BattleNetMarkerName);
    }

    public void DisconnectBattleNet()
    {
        SecureStore.Delete(BattleNetMarkerName);
        _database.SaveOwnedGames(Platform.BattleNet, new List<Game>());

        string webViewFolder = AppPaths.GetWebViewFolder(Platform.BattleNet);

        if (Directory.Exists(webViewFolder))
        {
            Directory.Delete(webViewFolder, recursive: true);
        }
    }

    // ----- Lancement et temps de jeu -----

    public bool Launch(Game game)
    {
        ILibraryProvider? provider = _providers.FirstOrDefault(p => p.Platform == game.Platform);

        if (provider is null)
        {
            Logger.Log($"Aucun provider pour {game.Platform}");
            return false;
        }

        try
        {
            provider.Launch(game);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Log($"Impossible de lancer {game.Name} : {ex}");
            return false;
        }
    }

    public async Task TrackPlaytimeAsync(Game game)
    {
        if (game.Platform == Platform.Steam || !game.IsInstalled || string.IsNullOrEmpty(game.InstallPath))
        {
            return;
        }

        string sessionKey = $"{game.Platform}_{game.PlatformGameId}";

        if (!_activeSessions.Add(sessionKey))
        {
            return;
        }

        try
        {
            PlaySessionTimes? session = await _tracker.TrackSessionAsync(game.InstallPath);

            if (session is null)
            {
                Logger.Log($"Session de jeu non détectée pour {game.Name}");
                return;
            }

            int minutes = (int)Math.Round((session.End - session.Start).TotalMinutes);

            if (minutes == 0)
            {
                return;
            }

            long startedUnix = session.Start.ToUnixTimeSeconds();
            long endedUnix = session.End.ToUnixTimeSeconds();

            _database.AddPlaySession(game, startedUnix, endedUnix, minutes);
            game.PlaytimeMinutes += minutes;
            game.LastPlayedUnix = startedUnix;
        }
        finally
        {
            _activeSessions.Remove(sessionKey);
        }
    }
}