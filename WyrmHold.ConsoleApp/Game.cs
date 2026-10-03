namespace Wyrmhold.ConsoleApp;

public class Game
{
    public Platform Platform { get; set; }
    public string PlatformGameId { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsInstalled { get; set; }
    public string? InstallPath { get; set; }
}