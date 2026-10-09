using System.Text.Json;

namespace Wyrmhold.Core;

/// <summary>
/// Les réglages de Wyrmhold, enregistrés dans settings.json (à côté de la base).
/// Les clés API sont ailleurs, chiffrées (voir Secrets) : ce fichier-ci ne contient rien de secret.
/// </summary>
public class AppSettings
{
    private static readonly string FilePath = Path.Combine(AppPaths.DataFolder, "settings.json");

    // L'identifiant du thème choisi (« light », « dark »… et plus tard ceux de la boutique de thèmes).
    public string Theme { get; set; } = "light";

    // L'assistant des clés API a déjà été proposé (terminé ou fermé) : on ne le rouvre plus tout seul.
    public bool SetupWizardDone { get; set; }

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

    // Le raccourci global qui ouvre ou ferme la fenêtre Aide du jeu en cours (actif avec l'overlay).
    public string HelpHotkey { get; set; } = DefaultHelpHotkey;

    public const string DefaultHelpHotkey = "Ctrl+Shift+G";

    // Pas de notification Wyrmhold pour un succès Steam : Steam affiche déjà les siens (en bas à droite).
    // Les succès Steam restent suivis (base, badges, overlay).
    public bool MuteSteamAchievementNotifications { get; set; } = true;

    // Un petit son avec chaque notification de succès (et un plus festif pour le 100 %).
    public bool AchievementSoundEnabled { get; set; } = true;

    // Onglet « Pour toi » : critères de qualité des promos recommandées (avis Steam).
    public int RecommendationMinSteamPercent { get; set; } = 80;
    public int RecommendationMinSteamReviews { get; set; } = 2000;

    // Onglet « Pour toi » : les genres décochés (nom anglais, ex. « Roguelike »), écartés du calcul.
    public List<string> ExcludedRecommendationGenres { get; set; } = new List<string>();

    // Les lanceurs désactivés (ex. « Ea ») : plus lus, et leurs jeux masqués (pas supprimés). Vide = tous actifs.
    public List<string> DisabledLaunchers { get; set; } = new List<string>();

    public bool IsLauncherEnabled(Platform platform)
    {
        return !DisabledLaunchers.Contains(platform.ToString());
    }

    public void SetLauncherEnabled(Platform platform, bool isEnabled)
    {
        DisabledLaunchers.Remove(platform.ToString());

        if (!isEnabled)
        {
            DisabledLaunchers.Add(platform.ToString());
        }
    }

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
