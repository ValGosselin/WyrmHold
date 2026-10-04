using System.Text.Json.Serialization;

namespace Wyrmhold.Core;

internal class EpicManifest
{
    public string AppName { get; set; } = "";
    public string MainGameAppName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string InstallLocation { get; set; } = "";
    public List<string> AppCategories { get; set; } = new List<string>();

    [JsonPropertyName("bIsIncompleteInstall")]
    public bool IsIncompleteInstall { get; set; }
}