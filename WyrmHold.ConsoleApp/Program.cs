using Microsoft.Win32;
using Wyrmhold.ConsoleApp;

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

Console.WriteLine($"{games.Count} jeu(x) trouvé(s) :");

foreach (Game game in games.OrderBy(g => g.Name))
{
    Console.WriteLine($" - {game.Name} ({game.Platform}, id {game.PlatformGameId})");
}

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