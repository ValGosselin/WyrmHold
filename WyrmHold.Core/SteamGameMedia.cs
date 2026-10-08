namespace Wyrmhold.Core;

/// <summary>
/// Ce que la boutique Steam donne pour présenter un jeu : description, captures d'écran, bandes-annonces.
/// </summary>
public class SteamGameMedia
{
    public int AppId { get; set; }
    public string? Description { get; set; }
    public List<SteamScreenshot> Screenshots { get; } = new List<SteamScreenshot>();
    public List<SteamTrailer> Trailers { get; } = new List<SteamTrailer>();

    // La page du jeu sur la boutique Steam : on y regarde les bandes-annonces complètes
    // (Steam ne les donne qu'en streaming, un format que le lecteur vidéo de WPF ne sait pas lire).
    public string StoreUrl => $"https://store.steampowered.com/app/{AppId}/";
}

/// <summary>Une capture d'écran : version 1920 × 1080 pour la rangée, originale pour l'agrandissement.</summary>
public record SteamScreenshot(string SmallUrl, string FullUrl);

/// <summary>
/// Une bande-annonce : son nom, son image, et son « microtrailer »
/// (boucle courte et muette en .mp4, environ 3 Mo ; null si Steam n'en donne pas).
/// </summary>
public record SteamTrailer(string Name, string? ThumbnailUrl, string? MicrotrailerUrl);
