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

    internal static async Task<Dictionary<int, string>> GetTagNamesAsync()
    {
        string json = await Http.GetStringAsync(
            "https://api.steampowered.com/IStoreService/GetTagList/v1/?language=french");

        List<TagName> tags = JsonSerializer.Deserialize<TagListResponse>(json)?.Response?.Tags
            ?? new List<TagName>();

        return tags.ToDictionary(tag => tag.TagId, tag => tag.Name);
    }
}