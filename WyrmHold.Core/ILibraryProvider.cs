namespace Wyrmhold.Core;

public interface ILibraryProvider
{
    Platform Platform { get; }
    List<Game> GetInstalledGames();
    void Launch(Game game);
}