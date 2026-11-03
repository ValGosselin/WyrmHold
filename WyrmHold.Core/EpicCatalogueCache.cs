using System.Text.Json;

namespace Wyrmhold.Core;

internal class EpicCatalogEntry
{
    public string Title { get; set; } = "";
    public bool IsGame { get; set; }
    public string? CoverUrl { get; set; }

    // Le « namespace » (ou sandboxId) du jeu chez Epic : il sert à demander la liste de ses succès.
    public string? Namespace { get; set; }

    // L'identifiant du jeu dans le catalogue Epic (catalogItemId) : avec le namespace et l'AppName, il forme
    // l'adresse qui ouvre la fenêtre d'installation d'Epic. null = pas encore lu (cache d'avant le 3 novembre 2026).
    public string? CatalogItemId { get; set; }

    // Le développeur selon Epic. null = pas encore lu ; "" = Epic n'en donne pas.
    public string? Developer { get; set; }
}

internal class EpicCatalogCache
{
    private readonly string _path = Path.Combine(AppPaths.DataFolder, "epic-catalog.json");
    private readonly Dictionary<string, EpicCatalogEntry> _entries;

    public EpicCatalogCache()
    {
        _entries = Load();
    }

    public EpicCatalogEntry? Find(string appName)
    {
        return _entries.GetValueOrDefault(appName);
    }

    public void Set(string appName, EpicCatalogEntry entry)
    {
        _entries[appName] = entry;
    }

    public void Save()
    {
        JsonSerializerOptions options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(_path, JsonSerializer.Serialize(_entries, options));
    }

    private Dictionary<string, EpicCatalogEntry> Load()
    {
        if (!File.Exists(_path))
        {
            return new Dictionary<string, EpicCatalogEntry>();
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, EpicCatalogEntry>>(File.ReadAllText(_path))
                ?? new Dictionary<string, EpicCatalogEntry>();
        }
        catch (JsonException ex)
        {
            Logger.Log($"Cache du catalogue Epic illisible, il sera reconstruit : {ex.Message}");
            return new Dictionary<string, EpicCatalogEntry>();
        }
    }
}