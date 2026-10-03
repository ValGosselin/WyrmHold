using Microsoft.Win32;
using Wyrmhold.ConsoleApp;
using System.Diagnostics;

string? steamPath = Registry.GetValue(
    @"HKEY_CURRENT_USER\Software\Valve\Steam",
    "SteamPath",
    null) as string;

if (steamPath is null)
{
    Console.WriteLine("Steam n'est pas installé (valeur SteamPath introuvable).");
    return;
}

steamPath = Path.GetFullPath(steamPath);

string vdfPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");

if (!File.Exists(vdfPath))
{
    Console.WriteLine($"Fichier introuvable : {vdfPath}");
    return;
}

List<string> libraryFolders = new List<string>();

foreach (string line in File.ReadAllLines(vdfPath))
{
    string[] parts = line.Split('"');

    if (parts.Length >= 4 && parts[1] == "path")
    {
        libraryFolders.Add(parts[3].Replace(@"\\", @"\"));
    }
}

List<Game> games = new List<Game>();

foreach (string folder in libraryFolders)
{
    string steamappsPath = Path.Combine(folder, "steamapps");

    if (!Directory.Exists(steamappsPath))
    {
        continue;
    }

    foreach (string manifestPath in Directory.GetFiles(steamappsPath, "appmanifest_*.acf"))
    {
        string[] lines = File.ReadAllLines(manifestPath);

        string? appId = ReadValue(lines, "appid");
        string? name = ReadValue(lines, "name");
        string? installDir = ReadValue(lines, "installdir");

        if (appId is null || name is null || installDir is null)
        {
            continue;
        }

        games.Add(new Game
        {
            Platform = Platform.Steam,
            PlatformGameId = appId,
            Name = name,
            IsInstalled = true,
            InstallPath = Path.Combine(steamappsPath, "common", installDir)
        });
    }
}
GameDatabase database = new GameDatabase();
database.Initialize();
Console.WriteLine($"Base de données : {database.DatabasePath}");
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
string launchUrl = $"steam://rungameid/{selectedGame.PlatformGameId}";

Console.WriteLine($"Lancement de {selectedGame.Name}...");
Process.Start(new ProcessStartInfo(launchUrl) { UseShellExecute = true });

string? ReadValue(string[] lines, string key)
{
    foreach (string line in lines)
    {
        string[] parts = line.Split('"');

        if (parts.Length >= 4 && string.Equals(parts[1], key, StringComparison.OrdinalIgnoreCase))
        {
            return parts[3];
        }
    }

    return null;
}