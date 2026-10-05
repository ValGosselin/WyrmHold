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

    [JsonPropertyName("is_early_access")]
    public bool IsEarlyAccess { get; set; }

    [JsonPropertyName("basic_info")]
    public StoreBasicInfo? BasicInfo { get; set; }

    [JsonPropertyName("tags")]
    public List<StoreTag> Tags { get; set; } = new List<StoreTag>();

    [JsonPropertyName("release")]
    public StoreRelease? Release { get; set; }
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