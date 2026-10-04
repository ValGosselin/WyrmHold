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
    private readonly SteamWebApi _steamApi = new SteamWebApi(Secrets.Load());

    public LibraryService()
    {
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
                    await AddSteamOwnedGamesAsync(games);
                }

                _database.SaveGames(provider.Platform, games);
            }
            catch (Exception ex)
            {
                Logger.Log($"Erreur avec {provider.Platform} : {ex}");
            }
        }

        return _database.LoadGames();
    }

    private async Task AddSteamOwnedGamesAsync(List<Game> steamGames)
    {
        try
        {
            List<SteamOwnedGame> ownedGames = await _steamApi.GetOwnedGamesAsync();

            foreach (SteamOwnedGame owned in ownedGames)
            {
                string appId = owned.AppId.ToString();
                Game? game = steamGames.FirstOrDefault(g => g.PlatformGameId == appId);

                if (game is null)
                {
                    steamGames.Add(new Game
                    {
                        Platform = Platform.Steam,
                        PlatformGameId = appId,
                        Name = owned.Name,
                        IsInstalled = false,
                        PlaytimeMinutes = owned.PlaytimeMinutes
                    });
                }
                else
                {
                    game.PlaytimeMinutes = owned.PlaytimeMinutes;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"API Steam indisponible : {ex.Message}");
        }
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
}