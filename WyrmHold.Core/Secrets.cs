using System.Text.Json;

namespace Wyrmhold.Core;

public class Secrets
{
    public string SteamApiKey { get; set; } = "";
    public string SteamId { get; set; } = "";

    public string SteamGridDbApiKey { get; set; } = "";
    public static Secrets Load()
    {
        string path = Path.Combine(AppPaths.DataFolder, "secrets.json");

        if (!File.Exists(path))
        {
            Logger.Log($"Fichier de secrets introuvable : {path}");
            return new Secrets();
        }

        return JsonSerializer.Deserialize<Secrets>(File.ReadAllText(path)) ?? new Secrets();
    }
}