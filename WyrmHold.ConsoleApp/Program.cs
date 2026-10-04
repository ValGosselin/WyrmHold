using Wyrmhold.Core;

Secrets secrets = Secrets.Load();
SteamWebApi steamApi = new SteamWebApi(secrets);

if (!steamApi.IsConfigured)
{
    Console.WriteLine("secrets.json introuvable ou incomplet.");
    return;
}

List<SteamOwnedGame> ownedGames = await steamApi.GetOwnedGamesAsync();

Console.WriteLine($"{ownedGames.Count} jeu(x) possédé(s) sur Steam.");
Console.WriteLine("Tes 10 jeux les plus joués :");

foreach (SteamOwnedGame game in ownedGames.OrderByDescending(g => g.PlaytimeMinutes).Take(10))
{
    Console.WriteLine($" - {game.Name} : {game.PlaytimeMinutes / 60} h");
}