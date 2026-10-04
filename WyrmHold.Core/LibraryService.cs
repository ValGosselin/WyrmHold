namespace Wyrmhold.Core;

public class LibraryService
{
    private readonly List<ILibraryProvider> _providers = new List<ILibraryProvider>
    {
        new SteamProvider()
    };

    private readonly GameDatabase _database = new GameDatabase();

    public LibraryService()
    {
        _database.Initialize();
    }

    public List<Game> ScanAll()
    {
        List<Game> allGames = new List<Game>();

        foreach (ILibraryProvider provider in _providers)
        {
            try
            {
                List<Game> games = provider.GetInstalledGames();
                _database.SaveGames(provider.Platform, games);
                allGames.AddRange(games);
            }
            catch (Exception ex)
            {
                Logger.Log($"Erreur avec {provider.Platform} : {ex}");
            }
        }

        return allGames;
    }

    public void Launch(Game game)
    {
        ILibraryProvider? provider = _providers.FirstOrDefault(p => p.Platform == game.Platform);

        if (provider is null)
        {
            Logger.Log($"Aucun provider pour {game.Platform}");
            return;
        }

        provider.Launch(game);
    }
}