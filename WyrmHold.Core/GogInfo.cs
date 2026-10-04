namespace Wyrmhold.Core;

internal class GogInfo
{
    public string Name { get; set; } = "";
    public string GameId { get; set; } = "";
    public string? RootGameId { get; set; }
    public List<GogPlayTask> PlayTasks { get; set; } = new List<GogPlayTask>();
}

internal class GogPlayTask
{
    public bool IsPrimary { get; set; }
    public string Type { get; set; } = "";
    public string? Path { get; set; }
    public string? Arguments { get; set; }
    public string? WorkingDir { get; set; }
}