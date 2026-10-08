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

    // Regrouper un même jeu possédé sur plusieurs plateformes en une seule tuile.
    public bool MergeDuplicates { get; set; } = true;

    // Les plateformes dont on ne lit plus les succès (ex. « Epic »). Vide = toutes actives.
    public List<string> DisabledAchievementSources { get; set; } = new List<string>();

    // Onglet Boutiques : les boutiques décochées dans le filtre (ex. « Muve »). Vide = toutes affichées.
    public List<string> HiddenShops { get; set; } = new List<string>();

    // Ce que fait la croix de la fenêtre : CloseActions.Ask (demander), Background (garder en arrière-plan) ou Quit.
    public string CloseAction { get; set; } = CloseActions.Ask;

    // Overlay en jeu : activé = Wyrmhold repère le jeu en cours (toutes les 5 s) et écoute le raccourci.
    public bool OverlayEnabled { get; set; } = true;

    // Le raccourci global qui affiche ou cache l'overlay, au format « Ctrl+Shift+W ».
    public string OverlayHotkey { get; set; } = DefaultOverlayHotkey;

    public const string DefaultOverlayHotkey = "Ctrl+Shift+W";

    // Onglet « Pour toi » : critères de qualité des promos recommandées (avis Steam).
    public int RecommendationMinSteamPercent { get; set; } = 80;
    public int RecommendationMinSteamReviews { get; set; } = 2000;

    // Onglet « Pour toi » : les genres décochés (nom anglais, ex. « Roguelike »), écartés du calcul.
    public List<string> ExcludedRecommendationGenres { get; set; } = new List<string>();

    public bool IsAchievementSourceEnabled(Platform platform)
    {
        return !DisabledAchievementSources.Contains(platform.ToString());
    }

    public void SetAchievementSourceEnabled(Platform platform, bool isEnabled)
    {
        DisabledAchievementSources.Remove(platform.ToString());

        if (!isEnabled)
        {
            DisabledAchievementSources.Add(platform.ToString());
        }
    }

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

/// <summary>
/// Les valeurs possibles du réglage CloseAction. Ce sont des textes (pas une énumération)
/// pour que settings.json reste lisible : « "CloseAction": "background" ».
/// </summary>
public static class CloseActions
{
    public const string Ask = "ask";
    public const string Background = "background";
    public const string Quit = "quit";
}
