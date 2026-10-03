using Microsoft.Win32;

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
Console.WriteLine($"Dossier de Steam : {steamPath}");

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
        string folder = parts[3].Replace(@"\\", @"\");
        libraryFolders.Add(folder);
    }
}

Console.WriteLine($"{libraryFolders.Count} dossier(s) de bibliothèque :");
foreach (string folder in libraryFolders)
{
    Console.WriteLine($" - {folder}");
}

Console.WriteLine();
Console.WriteLine("Jeux installés :");

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

        string installPath = Path.Combine(steamappsPath, "common", installDir);
        Console.WriteLine($" - [{appId}] {name} ({installPath})");
    }
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