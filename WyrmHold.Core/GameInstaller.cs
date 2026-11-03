namespace Wyrmhold.Core;

/// <summary>Les deux identifiants d'un jeu chez Epic qui s'ajoutent à son AppName dans l'adresse d'installation.</summary>
public record EpicGameIds(string Namespace, string CatalogItemId);

/// <summary>
/// Ce qu'il faut ouvrir pour demander l'installation : une adresse seule (« steam://… »),
/// ou un programme avec ses arguments (Battle.net.exe --game=…).
/// </summary>
public record InstallCommand(string FileName, string? Arguments = null)
{
    public override string ToString() => Arguments is null ? FileName : $"{FileName} {Arguments}";
}

/// <summary>
/// Comment demander l'installation d'un jeu à son launcher. Wyrmhold n'installe rien lui-même :
/// il ouvre le launcher, qui choisit le dossier, télécharge et garde le jeu à jour.
/// Méthodes reprises du code de Playnite (lu le 3 novembre 2026) et essayées sur le PC de Val le même jour :
/// - Steam : « steam://install/&lt;appid&gt; » ouvre la fenêtre d'installation de Steam ;
/// - Epic : « com.epicgames.launcher://apps/&lt;namespace&gt;%3A&lt;catalogItemId&gt;%3A&lt;AppName&gt;?action=install »
///   ouvre la fenêtre d'installation d'Epic (choix du dossier). Vérifié dans le journal d'Epic
///   (LogUriHandler, « AppInstallUriHandler ») : A Short Hike → « ownership confirmed=true », dossier proposé ;
///   The Escapists → « ownership confirmed=false » (Epic ne le compte pas comme possédé), rien ne s'ouvre.
///   La forme courte « apps/&lt;AppName&gt;?action=install », celle de Playnite, n'ouvre que la bibliothèque.
///   La forme longue est celle du lancement dans Playnite et dans le document « Epic Games Protocol Activation ».
///   Sans les identifiants (cache pas encore complété par une synchro Epic) : la bibliothèque Epic ;
/// - Ubisoft : « uplay://install/&lt;productId&gt; » ouvre la fenêtre d'installation d'Ubisoft Connect (Watch Dogs 2).
///   Seulement pour un numéro de produit : les jeux « joués » lus sur le site ont un spaceId (GUID) à la place ;
/// - Battle.net : « Battle.net.exe --game=&lt;code&gt; » ouvre la page du jeu, avec le bouton « Installer »
///   (Call of Duty: Modern Warfare, code ODIN). « battlenet://&lt;code&gt; » n'a rien montré d'utile ;
/// - GOG, EA : pas de méthode vérifiée, on explique quoi faire.
/// </summary>
public static class GameInstaller
{
    /// <summary>Ce qu'il faut ouvrir pour installer ce jeu, ou null si aucune méthode n'est connue.</summary>
    public static InstallCommand? FindInstallCommand(Game game, EpicGameIds? epicIds = null)
    {
        return game.Platform switch
        {
            Platform.Steam => new InstallCommand($"steam://install/{game.PlatformGameId}"),
            Platform.Epic when epicIds is not null => new InstallCommand("com.epicgames.launcher://apps/"
                + $"{Uri.EscapeDataString(epicIds.Namespace)}%3A{Uri.EscapeDataString(epicIds.CatalogItemId)}"
                + $"%3A{Uri.EscapeDataString(game.PlatformGameId)}?action=install"),
            Platform.Epic => new InstallCommand("com.epicgames.launcher://store/library"),
            Platform.Ubisoft when IsUbisoftProductId(game) => new InstallCommand($"uplay://install/{game.PlatformGameId}"),
            Platform.BattleNet when File.Exists(BattleNetProvider.GetClientPath())
                => new InstallCommand(BattleNetProvider.GetClientPath(), $"--game={game.PlatformGameId}"),
            _ => null
        };
    }

    /// <summary>
    /// Le texte de l'infobulle du bouton « Installer » : ce que fera le launcher, et ce qu'il reste à faire.
    /// </summary>
    public static string Explain(Game game, EpicGameIds? epicIds = null)
    {
        return game.Platform switch
        {
            Platform.Steam => "Steam ouvre sa fenêtre d'installation : il ne reste qu'à confirmer.",
            Platform.Epic when epicIds is not null => "Epic ouvre sa fenêtre d'installation : il ne reste qu'à confirmer.",
            Platform.Epic => "Ouvre ta bibliothèque Epic : clique sur « Installer » sous le jeu.",
            Platform.Ubisoft when IsUbisoftProductId(game) => "Ubisoft Connect ouvre sa fenêtre d'installation : il ne reste qu'à confirmer.",
            Platform.BattleNet when File.Exists(BattleNetProvider.GetClientPath()) => "Battle.net s'ouvre sur la page du jeu : clique sur « Installer ».",
            _ => $"Installe-le depuis {game.PlatformName}, puis clique sur Actualiser."
        };
    }

    // Un numéro de produit Ubisoft (« 2688 ») : les jeux lus sur le site n'ont qu'un spaceId (GUID).
    private static bool IsUbisoftProductId(Game game)
    {
        return game.PlatformGameId.Length > 0 && game.PlatformGameId.All(char.IsAsciiDigit);
    }
}
