using System.Text.Json.Serialization;

namespace Wyrmhold.Core;

internal class StoreItemsResponse
{
    [JsonPropertyName("response")]
    public StoreItemsData? Response { get; set; }
}

internal class StoreItemsData
{
    [JsonPropertyName("store_items")]
    public List<StoreItem> StoreItems { get; set; } = new List<StoreItem>();
}

internal class StoreItem
{
    [JsonPropertyName("appid")]
    public int AppId { get; set; }

    [JsonPropertyName("success")]
    public int Success { get; set; }

    // Le nom du jeu dans la langue demandée (sert aux « Jeux similaires »).
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    // Vrai pour une démo ou un jeu gratuit (vérifié le 10 octobre 2026 : Counter-Strike 2, une démo, AION 2 → true ;
    // Detroit → absent). Sert à garder un jeu gratuit installé qui n'est pas dans GetOwnedGames.
    [JsonPropertyName("is_free")]
    public bool IsFree { get; set; }

    [JsonPropertyName("is_early_access")]
    public bool IsEarlyAccess { get; set; }

    [JsonPropertyName("basic_info")]
    public StoreBasicInfo? BasicInfo { get; set; }

    [JsonPropertyName("tags")]
    public List<StoreTag> Tags { get; set; } = new List<StoreTag>();

    [JsonPropertyName("release")]
    public StoreRelease? Release { get; set; }

    // Rempli seulement si la demande contient include_screenshots / include_trailers.
    [JsonPropertyName("screenshots")]
    public StoreScreenshots? Screenshots { get; set; }

    [JsonPropertyName("trailers")]
    public StoreTrailers? Trailers { get; set; }
}

internal class StoreScreenshots
{
    // Les captures visibles par tous (Steam range à part celles réservées aux adultes : on ne les prend pas).
    [JsonPropertyName("all_ages_screenshots")]
    public List<StoreScreenshot> AllAges { get; set; } = new List<StoreScreenshot>();
}

internal class StoreScreenshot
{
    // Chemin de l'image, ex. « steam/apps/1145350/ss_….jpg?t=… », à coller derrière l'adresse du serveur d'images.
    [JsonPropertyName("filename")]
    public string FileName { get; set; } = "";

    [JsonPropertyName("ordinal")]
    public int Ordinal { get; set; }
}

internal class StoreTrailers
{
    // Les bandes-annonces mises en avant par le studio, puis les autres.
    [JsonPropertyName("highlights")]
    public List<StoreTrailer> Highlights { get; set; } = new List<StoreTrailer>();

    [JsonPropertyName("other_trailers")]
    public List<StoreTrailer> Others { get; set; } = new List<StoreTrailer>();
}

internal class StoreTrailer
{
    [JsonPropertyName("trailer_name")]
    public string Name { get; set; } = "";

    // Image de la bande-annonce (600 × 337), ex. « 257204779/…/movie_600x337.jpg ».
    [JsonPropertyName("screenshot_medium")]
    public string? ScreenshotMedium { get; set; }

    // La même image en pleine taille, ex. « 257204779/…/movie_full.jpg ».
    [JsonPropertyName("screenshot_full")]
    public string? ScreenshotFull { get; set; }

    // Le « microtrailer » : une boucle courte et muette, en .webm et en .mp4.
    [JsonPropertyName("microtrailer")]
    public List<StoreTrailerFile> Microtrailer { get; set; } = new List<StoreTrailerFile>();
}

internal class StoreTrailerFile
{
    [JsonPropertyName("filename")]
    public string FileName { get; set; } = "";

    // « video/mp4 » ou « video/webm ».
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";
}

internal class StoreBasicInfo
{
    [JsonPropertyName("short_description")]
    public string? ShortDescription { get; set; }

    [JsonPropertyName("developers")]
    public List<StoreCompany> Developers { get; set; } = new List<StoreCompany>();
}

internal class StoreCompany
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
}

internal class StoreTag
{
    [JsonPropertyName("tagid")]
    public int TagId { get; set; }
}

internal class StoreRelease
{
    [JsonPropertyName("steam_release_date")]
    public long SteamReleaseDate { get; set; }
}

internal class TagListResponse
{
    [JsonPropertyName("response")]
    public TagListData? Response { get; set; }
}

internal class TagListData
{
    [JsonPropertyName("tags")]
    public List<TagName> Tags { get; set; } = new List<TagName>();
}

internal class TagName
{
    [JsonPropertyName("tagid")]
    public int TagId { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
}