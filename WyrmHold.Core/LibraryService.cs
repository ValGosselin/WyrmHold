using System.Net;

namespace Wyrmhold.Core;

public class LibraryService
{
    private const string GogTokenName = "gog";
    private const string SteamFamilyMarkerName = "steam-family";

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