using System.Text.Json.Serialization;

namespace Wyrmhold.Core;

public class SharedLibraryApp
{
    [JsonPropertyName("appid")]
    public int AppId { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("owner_steamids")]
    public List<string> OwnerSteamIds { get; set; } = new List<string>();

    [JsonPropertyName("exclude_reason")]
    public int ExcludeReason { get; set; }

    [JsonPropertyName("app_type")]
    public int AppType { get; set; }

    [JsonPropertyName("rt_playtime")]
    public int PlaytimeMinutes { get; set; }

    [JsonPropertyName("rt_last_played")]
    public long LastPlayedUnix { get; set; }
}

internal class FamilyGroupResponse
{
    [JsonPropertyName("response")]
    public FamilyGroupData? Response { get; set; }
}

internal class FamilyGroupData
{
    [JsonPropertyName("family_groupid")]
    public string? FamilyGroupId { get; set; }

    [JsonPropertyName("is_not_member_of_any_group")]
    public bool IsNotMemberOfAnyGroup { get; set; }
}

internal class SharedLibraryResponse
{
    [JsonPropertyName("response")]
    public SharedLibraryData? Response { get; set; }
}

internal class SharedLibraryData
{
    [JsonPropertyName("apps")]
    public List<SharedLibraryApp> Apps { get; set; } = new List<SharedLibraryApp>();
}