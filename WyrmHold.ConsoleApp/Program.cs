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

Console.WriteLine($"Dossier de Steam : {steamPath}");