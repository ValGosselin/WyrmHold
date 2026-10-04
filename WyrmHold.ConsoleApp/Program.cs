using Wyrmhold.Core;

LibraryService library = new LibraryService();
List<Game> games = library.ScanAll();

List<Game> sortedGames = games.OrderBy(g => g.Name).ToList();

Console.WriteLine($"{sortedGames.Count} jeu(x) trouvé(s) :");

for (int i = 0; i < sortedGames.Count; i++)
{
    Console.WriteLine($" {i + 1}. {sortedGames[i].Name}");
}

Console.WriteLine();
Console.Write("Numéro du jeu à lancer (Entrée pour quitter) : ");
string? input = Console.ReadLine();

if (!int.TryParse(input, out int choice) || choice < 1 || choice > sortedGames.Count)
{
    Console.WriteLine("Aucun jeu lancé.");
    return;
}

Game selectedGame = sortedGames[choice - 1];


Console.WriteLine($"Lancement de {selectedGame.Name}...");
library.Launch(selectedGame);