using System.Net;
using System.Text.Json;

namespace Wyrmhold.Core;

public static class SteamStoreApi
{
    private const string ImageServer = "https://shared.steamstatic.com/store_item_assets/";

    private static readonly HttpClient Http = new HttpClient();

    public static async Task<string?> GetLibraryCapsuleUrlAsync(string appId)
    {
        if (!int.TryParse(appId, out int numericAppId))
        {
            return null;
        }

        var request = new
        {
            ids = new[] { new { appid = numericAppId } },
            context = new { language = "french", country_code = "FR" },
            data_request = new { include_assets = true }
        };

        string inputJson = JsonSerializer.Serialize(request);
        string url = "https://api.steampowered.com/IStoreBrowseService/GetItems/v1/?input_json="
            + Uri.EscapeDataString(inputJson);

        string json = await Http.GetStringAsync(url);
        using JsonDocument document = JsonDocument.Parse(json);

        if (!document.RootElement.TryGetProperty("response", out JsonElement response)
            || !response.TryGetProperty("store_items", out JsonElement items)
            || items.ValueKind != JsonValueKind.Array
            || items.GetArrayLength() == 0
            || !items[0].TryGetProperty("assets", out JsonElement assets)
            || !assets.TryGetProperty("asset_url_format", out JsonElement format)
            || !assets.TryGetProperty("library_capsule", out JsonElement capsule))
        {
            return null;
        }

        string? urlFormat = format.GetString();
        string? fileName = capsule.GetString();

        if (string.IsNullOrEmpty(urlFormat) || string.IsNullOrEmpty(fileName))
        {
            return null;
        }

        return ImageServer + urlFormat.Replace("${FILENAME}", fileName);
    }
    public static async Task<string?> FindAppIdByNameAsync(string gameName)
    {
        string cleanedName = NameTools.CleanForSearch(gameName);
        string url = "https://store.steampowered.com/api/storesearch/"
            + $"?term={Uri.EscapeDataString(cleanedName)}&l=french&cc=FR";

        string json = await Http.GetStringAsync(url);
        using JsonDocument document = JsonDocument.Parse(json);

        if (!document.RootElement.TryGetProperty("items", out JsonElement items)
            || items.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        string target = NameTools.Normalize(cleanedName);

        foreach (JsonElement item in items.EnumerateArray())
        {
            if (item.TryGetProperty("name", out JsonElement name)
                && NameTools.Normalize(name.GetString() ?? "") == target
                && item.TryGetProperty("id", out JsonElement id))
            {
                return id.GetInt32().ToString();
            }
        }

        return null;
    }
    internal static async Task<List<StoreItem>> GetItemsAsync(IEnumerable<int> appIds)
    {
        var request = new
        {
            ids = appIds.Select(id => new { appid = id }).ToArray(),
            context = new { language = "french", country_code = "FR" },
            data_request = new { include_basic_info = true, include_release = true, include_tag_count = 10 }
        };

        string url = "https://api.steampowered.com/IStoreBrowseService/GetItems/v1/?input_json="
            + Uri.EscapeDataString(JsonSerializer.Serialize(request));

        string json = await Http.GetStringAsync(url);
        List<StoreItem> items = JsonSerializer.Deserialize<StoreItemsResponse>(json)?.Response?.StoreItems
            ?? new List<StoreItem>();

        return items.Where(item => item.Success == 1).ToList();
    }

    // Les vidéos ne sont pas sur le même serveur que les images (adresse vérifiée le 8 octobre 2026).
    private const string VideoServer = "https://video.fastly.steamstatic.com/store_trailers/";

    /// <summary>
    /// La description courte (en français), les captures d'écran et les bandes-annonces d'un jeu,
    /// en une seule requête à la boutique Steam. null si Steam ne connaît pas le jeu.
    /// </summary>
    public static async Task<SteamGameMedia?> GetMediaAsync(int appId)
    {
        var request = new
        {
            ids = new[] { new { appid = appId } },
            context = new { language = "french", country_code = "FR" },
            data_request = new { include_basic_info = true, include_screenshots = true, include_trailers = true }
        };

        string url = "https://api.steampowered.com/IStoreBrowseService/GetItems/v1/?input_json="
            + Uri.EscapeDataString(JsonSerializer.Serialize(request));

        string json = await Http.GetStringAsync(url);
        StoreItem? item = JsonSerializer.Deserialize<StoreItemsResponse>(json)?.Response?.StoreItems
            .FirstOrDefault(storeItem => storeItem.Success == 1);

        if (item == null)
        {
            return null;
        }

        string? description = item.BasicInfo?.ShortDescription;

        var media = new SteamGameMedia
        {
            AppId = appId,

            // Steam écrit certains caractères en code HTML (« &quot; » pour un guillemet) : on les remet en clair.
            Description = string.IsNullOrWhiteSpace(description) ? null : WebUtility.HtmlDecode(description).Trim()
        };

        foreach (StoreScreenshot screenshot in (item.Screenshots?.AllAges ?? new List<StoreScreenshot>()).OrderBy(s => s.Ordinal))
        {
            media.Screenshots.Add(new SteamScreenshot(
                ImageServer + ToThumbnailScreenshot(screenshot.FileName),
                ImageServer + screenshot.FileName));
        }

        IEnumerable<StoreTrailer> trailers = (item.Trailers?.Highlights ?? new List<StoreTrailer>())
            .Concat(item.Trailers?.Others ?? new List<StoreTrailer>());

        foreach (StoreTrailer trailer in trailers)
        {
            string? mp4 = trailer.Microtrailer.FirstOrDefault(file => file.Type == "video/mp4")?.FileName;

            // L'image pleine taille de la bande-annonce, ou à défaut la moyenne (600 × 337, floue en grand).
            string? thumbnail = trailer.ScreenshotFull ?? trailer.ScreenshotMedium;

            media.Trailers.Add(new SteamTrailer(
                trailer.Name,
                thumbnail == null ? null : ImageServer + "steam/apps/" + thumbnail,
                mp4 == null ? null : VideoServer + mp4));
        }

        return media;
    }

    /// <summary>
    /// La version 1920 × 1080 d'une capture (environ 500 Ko, contre 800 Ko pour l'originale en 2560 × 1440) :
    /// « ss_abc.jpg?t=1 » devient « ss_abc.1920x1080.jpg?t=1 ».
    /// La version 600 × 338 existe aussi, mais elle est floue une fois agrandie à l'écran (essai du 8 octobre 2026).
    /// </summary>
    private static string ToThumbnailScreenshot(string fileName)
    {
        int queryStart = fileName.IndexOf('?');
        string path = queryStart < 0 ? fileName : fileName.Substring(0, queryStart);
        string query = queryStart < 0 ? "" : fileName.Substring(queryStart);

        return path.EndsWith(".jpg")
            ? path.Substring(0, path.Length - ".jpg".Length) + ".1920x1080.jpg" + query
            : fileName;
    }

    /// <summary>Tous les tags de Steam : numéro → nom, dans la langue demandée (« french », « english »…).</summary>
    internal static async Task<Dictionary<int, string>> GetTagNamesAsync(string language = "french")
    {
        string json = await Http.GetStringAsync(
            "https://api.steampowered.com/IStoreService/GetTagList/v1/?language=" + Uri.EscapeDataString(language));

        List<TagName> tags = JsonSerializer.Deserialize<TagListResponse>(json)?.Response?.Tags
            ?? new List<TagName>();

        return tags.ToDictionary(tag => tag.TagId, tag => tag.Name);
    }
}