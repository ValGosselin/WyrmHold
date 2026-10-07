using System.Text.Json.Serialization;

namespace Wyrmhold.Core;

internal class EpicManifest
{
    public string AppName { get; set; } = "";
    public string MainGameAppName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string InstallLocation { get; set; } = "";
    public List<string> AppCategories { get; set; } = new List<string>();
    public long InstallSize { get; set; }

    // La version installée. Son format change d'un jeu à l'autre : on ne fait que la comparer.
    public string AppVersionString { get; set; } = "";

    [JsonPropertyName("bIsIncompleteInstall")]
    public bool IsIncompleteInstall { get; set; }
}