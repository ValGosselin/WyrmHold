
namespace Wyrmhold.Core;

public class Game
{
    public bool? IsFamilyShared { get; set; }
    public string? OwnerSteamId { get; set; }
    public string InstalledText => IsInstalled ? "✓" : "";
    public string OriginText => IsFamilyShared == true ? "Famille" : "";
    public Platform Platform { get; set; }
    public string PlatformGameId { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsInstalled { get; set; }
    public string? InstallPath { get; set; }
    public long LastPlayedUnix { get; set; }
    public string? Description { get; set; }
    public string? Developers { get; set; }
    public long ReleaseDateUnix { get; set; }
    public bool IsEarlyAccess { get; set; }
    public string? Tags { get; set; }
    public long MetadataUpdatedUnix { get; set; }
    public string ReleaseDateText => ReleaseDateUnix == 0
    ? ""
    : DateTimeOffset.FromUnixTimeSeconds(ReleaseDateUnix).LocalDateTime.ToString("dd/MM/yyyy");

    public string InstallStatusText => IsInstalled ? "Installé" : "Non installé";

    public string[] TagList => string.IsNullOrEmpty(Tags)
        ? Array.Empty<string>()
        : Tags.Split(", ");

    public string LastPlayedText => LastPlayedUnix == 0
        ? ""
        : DateTimeOffset.FromUnixTimeSeconds(LastPlayedUnix).LocalDateTime.ToString("dd/MM/yyyy");

    public string? CoverPath { get; set; }

    public string Subtitle => string.Join(" · ",
        new[] { PlatformName, PlaytimeText, OriginText }.Where(text => text.Length > 0));
    public string PlatformName => Platform switch
    {
        Platform.Steam => "Steam",
        Platform.Epic => "Epic Games",
        Platform.Ubisoft => "Ubisoft",
        Platform.Gog => "GOG",
        Platform.Ea => "EA",
        Platform.BattleNet => "Battle.net",
        _ => Platform.ToString()
    };

    public int PlaytimeMinutes { get; set; }

    public string PlaytimeText => PlaytimeMinutes switch
    {
        0 => "",
        < 60 => $"{PlaytimeMinutes} min",
        _ => $"{PlaytimeMinutes / 60} h"
    };

    
}