
namespace Wyrmhold.Core;

public class Game
{
    public Platform Platform { get; set; }
    public string PlatformGameId { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsInstalled { get; set; }
    public string? InstallPath { get; set; }
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
}