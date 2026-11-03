using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wyrmhold.Core;

/// <summary>
/// Une règle de succès maison, décrite dans le fichier JSON du jeu, sans code par succès :
/// flag = la valeur nommée est allumée ; atLeast = compteur ≥ value ; all = toutes les valeurs de keys sont allumées.
/// </summary>
public record CustomRule(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("key")] string? Key,
    [property: JsonPropertyName("value")] long Value,
    [property: JsonPropertyName("keys")] List<string>? Keys)
{
    public bool IsMet(IReadOnlyDictionary<string, long> values)
    {
        long Read(string? key) => key is null ? 0 : values.GetValueOrDefault(key);

        return Type switch
        {
            "flag" => Read(Key) > 0,
            "atLeast" => Read(Key) >= Value,
            "all" => Keys?.All(key => Read(key) > 0) == true,
            _ => false
        };
    }
}

public record CustomAchievement(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("rule")] CustomRule Rule,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("hidden")] bool IsHidden,
    [property: JsonPropertyName("verified")] bool IsVerified);

public record CustomGameDefinition(
    [property: JsonPropertyName("titleId")] string TitleId,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("saveFile")] string SaveFile,
    [property: JsonPropertyName("reader")] string Reader,
    [property: JsonPropertyName("achievements")] List<CustomAchievement> Achievements);

/// <summary>
/// Les fichiers de succès maison (Data\CustomAchievements\&lt;TitleID&gt;.json), rangés dans la DLL.
/// Un jeu sans fichier n'a simplement pas de succès maison.
/// </summary>
public static class CustomAchievementCatalog
{
    private static readonly Dictionary<string, CustomGameDefinition?> Loaded = new Dictionary<string, CustomGameDefinition?>();

    public static CustomGameDefinition? Find(string titleId)
    {
        lock (Loaded)
        {
            if (!Loaded.TryGetValue(titleId, out CustomGameDefinition? definition))
            {
                definition = Load(titleId);
                Loaded[titleId] = definition;
            }

            return definition;
        }
    }

    private static CustomGameDefinition? Load(string titleId)
    {
        using Stream? file = typeof(CustomAchievementCatalog).Assembly.GetManifestResourceStream($"CustomAchievements/{titleId}.json");

        return file is null ? null : JsonSerializer.Deserialize<CustomGameDefinition>(file);
    }
}
