using System.Text.Json;

namespace Wyrmhold.Core;

/// <summary>
/// Les réglages de Wyrmhold, enregistrés dans settings.json (à côté de la base).
/// Les clés API restent dans secrets.json : ce fichier-ci ne contient rien de secret.
/// </summary>
public class AppSettings
{
    private static readonly string FilePath = Path.Combine(AppPaths.DataFolder, "settings.json");

    // L'identifiant du thème choisi (« light », « dark »… et plus tard ceux de la boutique de thèmes).
    public string Theme { get; set; } = "light";

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            Logger.Log($"Réglages illisibles, valeurs par défaut utilisées : {ex.Message}");
        }

        return new AppSettings();
    }

    public void Save()
    {
        string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(FilePath, json);
    }
}
