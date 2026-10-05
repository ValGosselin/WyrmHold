namespace Wyrmhold.Core;

public class LibraryService
{
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
    private readonly Secrets _secrets;
    private readonly SteamWebApi _steamApi;
    public string SteamId => _secrets.SteamId;
    private readonly CoverCache _covers = new CoverCache();
    private readonly SteamGridDbApi _steamGridDb;
    private readonly MetadataFetcher _metadata = new MetadataFetcher();

    public LibraryService()
    {
        _secrets = Secrets.Load();
        _steamApi = new SteamWebApi(_secrets);
        _steamGridDb = new SteamGridDbApi(_secrets.SteamGridDbApiKey);
        _database.Initialize();
    }

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

        List<Game> allGames = _database.LoadGames();
        _covers.AttachCovers(allGames);
        return allGames;
    }

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
    public async Task UpdateMetadataAsync(List<Game> games)
    {
        List<Game> updatedGames = await _metadata.FetchMissingAsync(games);

        if (updatedGames.Count > 0)
        {
            _database.SaveMetadata(updatedGames);
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
        return familyGames.Count;
    }
    public async Task DownloadCoversAsync(List<Game> games)
    {
        await _covers.DownloadMissingSteamCoversAsync(games);
        await _covers.DownloadMissingCoversFromSteamGridDbAsync(games, _steamGridDb);
        _covers.AttachCovers(games);
    }
}