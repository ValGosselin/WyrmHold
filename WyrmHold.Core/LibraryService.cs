using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Microsoft.Data.Sqlite;

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
    private const string EaImportedMarkerName = "ea-imported";
    private const string BattleNetImportedMarkerName = "battlenet-imported";

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

    // Succès : une source par plateforme. Pour en ajouter une, il suffira de l'ajouter à cette liste.
    private readonly SteamAchievementProvider _steamAchievements;
    private readonly GogAchievementProvider _gogAchievements = new GogAchievementProvider();
    private readonly List<IAchievementProvider> _achievementProviders;

    // Le dernier jeton GOG reçu, et quand (il ne dure qu'environ une heure).
    private GogTokens? _gogTokens;
    private DateTimeOffset _gogTokensReceived;

    public LibraryService()
    {
        _secrets = Secrets.Load();
        _steamApi = new SteamWebApi(_secrets);
        _steamGridDb = new SteamGridDbApi(_secrets.SteamGridDbApiKey);
        _steamAchievements = new SteamAchievementProvider(_steamApi);
        _achievementProviders = new List<IAchievementProvider> { _steamAchievements, _gogAchievements };
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

    // EA et Battle.net ne gardent pas la session de leur site très longtemps :
    // ces deux propriétés disent si des jeux ont déjà été importés, même si la session a expiré depuis.
    public bool HasImportedEaGames => SecureStore.Exists(EaImportedMarkerName);

    public bool HasImportedBattleNetGames => SecureStore.Exists(BattleNetImportedMarkerName);

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
        AttachCollections(games);
        _covers.AttachCovers(games);
        return games;
    }

    /// <summary>
    /// Indique à chaque jeu les collections dont il fait partie.
    /// </summary>
    private void AttachCollections(List<Game> games)
    {
        Dictionary<string, Game> gamesByKey = new Dictionary<string, Game>();

        foreach (Game game in games)
        {
            gamesByKey.TryAdd($"{game.Platform}|{game.PlatformGameId}", game);
        }

        foreach ((long collectionId, string platform, string gameId) in _database.LoadCollectionMemberships())
        {
            if (gamesByKey.TryGetValue($"{platform}|{gameId}", out Game? game))
            {
                game.CollectionIds.Add(collectionId);
            }
        }
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

            _steamAchievements.SetAppsWithStats(ownedGames
                .Where(g => g.HasCommunityVisibleStats)
                .Select(g => g.AppId.ToString()));

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
            RememberGogTokens(null);
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
        RememberGogTokens(null);
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
        RememberGogTokens(tokens);

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

    private void RememberGogTokens(GogTokens? tokens)
    {
        _gogTokens = tokens;
        _gogTokensReceived = DateTimeOffset.UtcNow;
        _gogAchievements.SetSession(tokens);
    }

    /// <summary>
    /// Renouvelle le jeton GOG s'il a plus de 50 minutes (Wyrmhold peut rester ouvert des heures).
    /// Ne réimporte pas la bibliothèque : on veut juste un jeton valide pour lire les succès.
    /// </summary>
    private async Task EnsureFreshGogTokensAsync()
    {
        if (_gogTokens is not null && DateTimeOffset.UtcNow - _gogTokensReceived < TimeSpan.FromMinutes(50))
        {
            return;
        }

        string? refreshToken = SecureStore.Load(GogTokenName);

        if (refreshToken is null)
        {
            return;
        }

        try
        {
            GogTokens? tokens = await GogApi.RefreshAsync(refreshToken);

            if (tokens is not null)
            {
                // GOG donne un nouveau jeton de renouvellement à chaque fois : on garde le dernier.
                SecureStore.Save(GogTokenName, tokens.RefreshToken);
                RememberGogTokens(tokens);
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Jeton GOG impossible à renouveler : {ex.Message}");
        }
    }

    // ----- Epic Games -----

    public async Task<int> ImportEpicLibraryAsync(string authorizationCode)
    {
        EpicTokenResponse? tokens = await EpicApi.ExchangeCodeAsync(authorizationCode);
        string? accessToken = tokens?.AccessToken;

        if (string.IsNullOrEmpty(accessToken))
        {
            throw new InvalidOperationException("Epic n'a pas fourni de jeton d'accès.");
        }

        Dictionary<string, long> playtimes = await ReadEpicPlaytimesAsync(accessToken, tokens?.AccountId);

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
                    Name = entry.Title,
                    PlaytimeMinutes = (int)(playtimes.GetValueOrDefault(record.AppName) / 60)
                });
            }
        }

        _epicCatalog.Save();
        _database.SaveOwnedGames(Platform.Epic, games);
        SecureStore.Save(EpicMarkerName, "connected");

        return games.Count;
    }

    /// <summary>
    /// Le temps de jeu est un « bonus » : si Epic ne le donne pas, l'import continue sans lui.
    /// </summary>
    private static async Task<Dictionary<string, long>> ReadEpicPlaytimesAsync(string accessToken, string? accountId)
    {
        if (string.IsNullOrEmpty(accountId))
        {
            return new Dictionary<string, long>();
        }

        try
        {
            return await EpicApi.GetPlaytimesAsync(accessToken, accountId);
        }
        catch (Exception ex)
        {
            Logger.Log($"Temps de jeu Epic indisponible : {ex.Message}");
            return new Dictionary<string, long>();
        }
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
        SecureStore.Save(EaImportedMarkerName, "imported");

        return games.Count;
    }

    public void ForgetEaSession()
    {
        SecureStore.Delete(EaMarkerName);
    }

    public void DisconnectEa()
    {
        SecureStore.Delete(EaMarkerName);
        SecureStore.Delete(EaImportedMarkerName);
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
        SecureStore.Save(BattleNetImportedMarkerName, "imported");

        return games.Count;
    }

    public void ForgetBattleNetSession()
    {
        SecureStore.Delete(BattleNetMarkerName);
    }

    public void DisconnectBattleNet()
    {
        SecureStore.Delete(BattleNetMarkerName);
        SecureStore.Delete(BattleNetImportedMarkerName);
        _database.SaveOwnedGames(Platform.BattleNet, new List<Game>());

        string webViewFolder = AppPaths.GetWebViewFolder(Platform.BattleNet);

        if (Directory.Exists(webViewFolder))
        {
            Directory.Delete(webViewFolder, recursive: true);
        }
    }

    // ----- Patch notes -----

    private const int AnnouncementsToRead = 20;
    private const int PatchNotesToShow = 5;
    private const int AnnouncementsToShowWithoutPatchNotes = 3;

    // Annonces Steam déjà lues pendant cette session, par appid (évite de rappeler Steam à chaque clic).
    private readonly Dictionary<string, List<SteamNewsItem>> _announcementsCache = new Dictionary<string, List<SteamNewsItem>>();

    public async Task<PatchNotesResult> GetPatchNotesAsync(Game game)
    {
        string? steamAppId = await FindSteamAppIdAsync(game);

        if (steamAppId is null)
        {
            return new PatchNotesResult
            {
                Message = "Ce jeu n'a pas été trouvé sur Steam : pas de patch notes automatiques.",
                LinkUrl = "https://www.google.com/search?q=" + Uri.EscapeDataString($"{game.Name} patch notes"),
                LinkText = "Chercher ses notes de mise à jour sur le web"
            };
        }

        if (!_announcementsCache.TryGetValue(steamAppId, out List<SteamNewsItem>? announcements))
        {
            announcements = await SteamNewsApi.GetAnnouncementsAsync(steamAppId, AnnouncementsToRead);
            _announcementsCache[steamAppId] = announcements;
        }

        // 1. Les annonces étiquetées « patchnotes ».
        List<SteamNewsItem> shown = announcements.Where(a => a.IsPatchNotes).Take(PatchNotesToShow).ToList();
        List<string> messages = new List<string>();

        // 2. Beaucoup de studios n'étiquettent pas leurs notes : on montre alors les dernières annonces.
        if (shown.Count == 0)
        {
            shown = announcements.Take(AnnouncementsToShowWithoutPatchNotes).ToList();
            messages.Add(shown.Count == 0
                ? "Aucune annonce sur Steam pour ce jeu."
                : "Aucune note de patch étiquetée : voici les dernières annonces du studio.");
        }

        if (game.Platform != Platform.Steam)
        {
            messages.Add("D'après la page Steam du même jeu : la version de ton lanceur peut avoir un peu d'avance ou de retard.");
        }

        return new PatchNotesResult
        {
            Notes = shown.Select((item, index) => PatchNote.FromSteam(item, isExpanded: index == 0)).ToList(),
            Message = string.Join(" ", messages),
            LinkUrl = $"https://store.steampowered.com/news/app/{steamAppId}",
            LinkText = "Toutes les actualités sur Steam"
        };
    }

    /// <summary>
    /// L'appid Steam d'un jeu : le sien pour un jeu Steam, sinon celui du même jeu trouvé par son nom.
    /// La recherche est faite une seule fois puis enregistrée en base (même quand elle ne trouve rien).
    /// </summary>
    private async Task<string?> FindSteamAppIdAsync(Game game)
    {
        if (game.Platform == Platform.Steam)
        {
            return game.PlatformGameId;
        }

        if (game.SteamAppId is null)
        {
            string? foundAppId = await SteamStoreApi.FindAppIdByNameAsync(game.Name);
            game.SteamAppId = foundAppId ?? "";
            _database.SetSteamAppId(game, game.SteamAppId);
        }

        return game.SteamAppId.Length == 0 ? null : game.SteamAppId;
    }

    // ----- Succès -----

    // Combien de jeux on interroge en même temps.
    private const int ParallelAchievementRequests = 4;

    // Un jeu joué ces derniers jours est relu à chaque fois : tu as pu débloquer des succès
    // après la dernière lecture, pendant la même partie.
    private const int RecentlyPlayedDays = 3;

    /// <summary>
    /// Relit les succès des jeux qui en ont besoin, et renvoie le nombre de jeux mis à jour.
    /// Si une source ne répond pas, ses jeux gardent leur dernière valeur enregistrée.
    /// </summary>
    public async Task<int> RefreshAchievementsAsync(List<Game> games, IProgress<(int Done, int Total)>? progress = null)
    {
        await EnsureFreshGogTokensAsync();

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // 1. Pour chaque source, la liste des jeux à relire.
        List<(IAchievementProvider Provider, List<Game> Games)> work = _achievementProviders
            .Select(provider => (Provider: provider, Games: games
                .Where(g => g.Platform == provider.Platform
                            && provider.CanHaveAchievements(g)
                            && NeedsAchievementRefresh(g, now))
                .ToList()))
            .Where(item => item.Games.Count > 0)
            .ToList();

        int total = work.Sum(item => item.Games.Count);
        int done = 0;

        // ConcurrentBag : une liste qu'on peut remplir depuis plusieurs tâches en même temps.
        ConcurrentBag<(Game Game, AchievementProgress Progress)> results = new ConcurrentBag<(Game, AchievementProgress)>();

        // 2. On interroge chaque source, quelques jeux à la fois.
        foreach ((IAchievementProvider provider, List<Game> toRefresh) in work)
        {
            using CancellationTokenSource stopSource = new CancellationTokenSource();

            ParallelOptions options = new ParallelOptions
            {
                MaxDegreeOfParallelism = ParallelAchievementRequests,
                CancellationToken = stopSource.Token
            };

            try
            {
                await Parallel.ForEachAsync(toRefresh, options, async (game, token) =>
                {
                    try
                    {
                        AchievementProgress result = await provider.GetProgressAsync(game, token);
                        results.Add((game, result));
                    }
                    catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.InternalServerError
                                                          && game.AchievementsTotal == 0
                                                          && !token.IsCancellationRequested)
                    {
                        // Steam répond « erreur 500 » pour certains jeux, peut-être ceux qui n'ont aucun succès.
                        // On ne connaissait aucun succès à ce jeu : on le note « sans succès » (rien n'est perdu)
                        // pour ne pas le redemander à chaque démarrage. Il sera relu s'il est joué ou mis à jour.
                        results.Add((game, new AchievementProgress(0, 0)));
                    }
                    catch (AchievementSourceUnavailableException ex)
                    {
                        // Toute la source est hors service : on arrête d'interroger ses jeux.
                        Logger.Log($"Succès {provider.Platform} indisponibles : {ex.Message}");
                        stopSource.Cancel();
                    }
                    catch (Exception ex) when (!token.IsCancellationRequested)
                    {
                        // Un seul jeu en échec : on passe au suivant.
                        Logger.Log($"Succès illisibles pour {game.Name} : {ex.Message}");
                    }

                    progress?.Report((Interlocked.Increment(ref done), total));
                });
            }
            catch (OperationCanceledException)
            {
                // La source a été arrêtée : les jeux déjà lus sont gardés, les autres attendront.
            }
        }

        // 3. On applique les résultats aux jeux, puis on enregistre tout d'un coup.
        foreach ((Game game, AchievementProgress result) in results)
        {
            game.ApplyAchievementProgress(result, now);
        }

        List<Game> refreshedGames = results.Select(r => r.Game).ToList();

        if (refreshedGames.Count > 0)
        {
            _database.SaveAchievements(refreshedGames);
        }

        return refreshedGames.Count;
    }

    /// <summary>
    /// La liste complète des succès d'un jeu, pour la fenêtre des succès.
    /// On en profite pour mettre à jour sa progression (« 37/50 ») avec ces chiffres tout frais.
    /// </summary>
    public async Task<List<AchievementDetail>> GetAchievementDetailsAsync(Game game)
    {
        IAchievementProvider? provider = _achievementProviders.FirstOrDefault(p => p.Platform == game.Platform);

        if (provider is null)
        {
            return new List<AchievementDetail>();
        }

        if (game.Platform == Platform.Gog)
        {
            await EnsureFreshGogTokensAsync();
        }

        List<AchievementDetail> details = await provider.GetAchievementsAsync(game);

        AchievementProgress progress = new AchievementProgress(details.Count(d => d.IsUnlocked), details.Count);
        game.ApplyAchievementProgress(progress, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        _database.SaveAchievements(new List<Game> { game });

        return details;
    }

    /// <summary>
    /// Les statistiques brutes d'un jeu (Steam seulement : les autres plateformes n'en donnent pas).
    /// </summary>
    public async Task<List<GameStat>> GetGameStatsAsync(Game game)
    {
        if (game.Platform != Platform.Steam || !_steamApi.IsConfigured)
        {
            return new List<GameStat>();
        }

        return await _steamApi.GetGameStatsAsync(game.PlatformGameId);
    }

    private static bool NeedsAchievementRefresh(Game game, long now)
    {
        const long OneDay = 24 * 60 * 60;

        return game.AchievementsCheckedUnix == 0                              // jamais lu (premier scan)
            || game.LastPlayedUnix > game.AchievementsCheckedUnix             // joué depuis la dernière lecture
            || game.LastPlayedUnix >= now - RecentlyPlayedDays * OneDay       // joué ces derniers jours
            || game.LastUpdateDetectedUnix > game.AchievementsCheckedUnix;    // mis à jour depuis
    }

    // ----- Taille sur le disque -----

    /// <summary>
    /// Mesure le dossier des jeux installés dont on ne connaît pas encore la taille
    /// (Steam et Epic la donnent déjà dans leurs fichiers ; les autres non).
    /// Chaque taille mesurée est enregistrée : on ne mesure qu'une fois par jeu.
    /// </summary>
    public async Task UpdateMissingSizesAsync(List<Game> games)
    {
        List<Game> toMeasure = games
            .Where(g => g.IsInstalled && g.SizeOnDiskBytes <= 0 && Directory.Exists(g.InstallPath))
            .ToList();

        foreach (Game game in toMeasure)
        {
            long size = await Task.Run(() => MeasureFolder(game.InstallPath!));

            if (size > 0)
            {
                game.SizeOnDiskBytes = size;
                _database.SetSizeOnDisk(game, size);
            }
        }
    }

    private static long MeasureFolder(string folder)
    {
        EnumerationOptions options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };

        try
        {
            return new DirectoryInfo(folder)
                .EnumerateFiles("*", options)
                .Sum(file => file.Length);
        }
        catch (Exception ex)
        {
            Logger.Log($"Taille impossible à mesurer pour {folder} : {ex.Message}");
            return 0;
        }
    }

    // ----- Statistiques -----

    public LibraryStatistics ComputeStatistics(List<Game> games)
    {
        long sinceUnix = DateTimeOffset.Now.AddDays(-7 * (LibraryStatistics.WeekCount + 1)).ToUnixTimeSeconds();
        return LibraryStatistics.Compute(games, _database.LoadPlaySessions(sinceUnix));
    }

    // ----- Vues enregistrées -----

    public List<SavedView> LoadSavedViews()
    {
        List<SavedView> views = new List<SavedView>();

        foreach ((long id, string name, string filtersJson) in _database.LoadSavedViews())
        {
            try
            {
                SavedViewFilters filters = JsonSerializer.Deserialize<SavedViewFilters>(filtersJson) ?? new SavedViewFilters();
                views.Add(new SavedView(id, name, filters));
            }
            catch (JsonException ex)
            {
                Logger.Log($"Vue « {name} » illisible : {ex.Message}");
            }
        }

        return views;
    }

    /// <summary>
    /// Enregistre une vue. Si une vue porte déjà ce nom, ses réglages sont remplacés.
    /// </summary>
    public void SaveView(string name, SavedViewFilters filters)
    {
        string cleanName = name.Trim();

        if (cleanName.Length == 0)
        {
            throw new InvalidOperationException("Donne un nom à la vue.");
        }

        _database.SaveView(cleanName, JsonSerializer.Serialize(filters));
    }

    public void DeleteSavedView(SavedView view)
    {
        _database.DeleteSavedView(view.Id);
    }

    // ----- Favoris -----

    public void SetFavorite(Game game, bool isFavorite)
    {
        _database.SetFavorite(game, isFavorite);
        game.IsFavorite = isFavorite;
    }

    // ----- Collections -----

    public List<GameCollection> LoadCollections()
    {
        return _database.LoadCollections();
    }

    public GameCollection CreateCollection(string name)
    {
        string cleanName = CleanCollectionName(name);

        try
        {
            long id = _database.CreateCollection(cleanName);
            return new GameCollection(id, cleanName);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == SqliteConstraintError)
        {
            throw new InvalidOperationException($"Une collection s'appelle déjà « {cleanName} ».");
        }
    }

    public void RenameCollection(GameCollection collection, string newName)
    {
        string cleanName = CleanCollectionName(newName);

        try
        {
            _database.RenameCollection(collection.Id, cleanName);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == SqliteConstraintError)
        {
            throw new InvalidOperationException($"Une collection s'appelle déjà « {cleanName} ».");
        }
    }

    public void DeleteCollection(GameCollection collection)
    {
        _database.DeleteCollection(collection.Id);
    }

    public void SetGameInCollection(Game game, GameCollection collection, bool isMember)
    {
        _database.SetGameInCollection(collection.Id, game, isMember);

        if (isMember)
        {
            game.CollectionIds.Add(collection.Id);
        }
        else
        {
            game.CollectionIds.Remove(collection.Id);
        }
    }

    // Code d'erreur SQLite quand une contrainte n'est pas respectée (ici : deux collections du même nom).
    private const int SqliteConstraintError = 19;

    private static string CleanCollectionName(string name)
    {
        string cleanName = name.Trim();

        if (cleanName.Length == 0)
        {
            throw new InvalidOperationException("Donne un nom à la collection.");
        }

        return cleanName;
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