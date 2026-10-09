namespace Wyrmhold.Core;

/// <summary>Un succès lu dans les fichiers locaux de Steam (IconUrl = l'icône en couleur).</summary>
public record LocalAchievement(
    string Id, string Name, string Description, bool IsHidden, string? IconUrl, bool IsUnlocked, long UnlockedUnix)
{
    /// <summary>La forme commune à toutes les plateformes (notification, overlay, fenêtre des succès).</summary>
    public AchievementDetail ToDetail() => new AchievementDetail
    {
        Id = Id,
        Name = Name,
        Description = Description,
        IsHidden = IsHidden,
        IconUrl = IconUrl,
        IsUnlocked = IsUnlocked,
        UnlockedUnix = UnlockedUnix
    };
}

/// <summary>
/// Lit les succès d'un jeu Steam directement sur le disque, sans internet. Le client Steam réécrit
/// Steam\appcache\stats\UserGameStats_[compte]_[appid].bin AU MOMENT où un succès se débloque,
/// alors que la Steam Web API peut mettre plusieurs minutes à le voir (238 s mesurées le 9 octobre 2026).
///
/// Deux fichiers :
/// - UserGameStatsSchema_[appid].bin : la liste des succès. Ils sont rangés par « stat » (un numéro),
///   et dans chaque stat par « bit » (0 à 31) ; chaque bit a un nom (ex. ACH_BEER_1) et un texte par langue.
/// - UserGameStats_[compte]_[appid].bin : pour chaque stat, « data » = un nombre de 32 bits où
///   chaque bit à 1 est un succès débloqué, et « AchievementTimes » = l'heure de chaque déblocage.
/// </summary>
public class SteamLocalAchievements
{
    // Un SteamID64 = ce nombre + le numéro de compte (« 32 bits ») utilisé dans le nom des fichiers.
    private const long SteamId64Base = 76561197960265728;

    private readonly string _statsFolder;

    public SteamLocalAchievements(string steamFolder)
    {
        _statsFolder = Path.Combine(steamFolder, "appcache", "stats");
    }

    public string StatsFolder => _statsFolder;

    /// <summary>Le dossier de Steam, lu dans le registre (null si Steam n'est pas installé).</summary>
    public static SteamLocalAchievements? FromRegistry()
    {
        // Même lecture que SteamProvider.
        string? steamPath = Microsoft.Win32.Registry.GetValue(
            @"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
        return string.IsNullOrEmpty(steamPath) ? null : new SteamLocalAchievements(steamPath);
    }

    /// <summary>Le nom du fichier des succès d'un joueur pour un jeu (celui que Steam réécrit au déblocage).</summary>
    public static string UserStatsFileName(string steamId64, string appId)
    {
        long accountId = long.Parse(steamId64) - SteamId64Base;
        return $"UserGameStats_{accountId}_{appId}.bin";
    }

    /// <summary>
    /// Tous les succès du jeu (débloqués ou non), ou null si les fichiers manquent
    /// (jeu jamais lancé sur ce PC, Steam ailleurs…). Lance une erreur si un fichier est illisible.
    /// </summary>
    public List<LocalAchievement>? Read(string steamId64, string appId, string language = "french")
    {
        string schemaPath = Path.Combine(_statsFolder, $"UserGameStatsSchema_{appId}.bin");
        string statsPath = Path.Combine(_statsFolder, UserStatsFileName(steamId64, appId));

        if (!File.Exists(schemaPath) || !File.Exists(statsPath))
        {
            return null;
        }

        KeyValueNode schema = SteamBinaryKeyValues.Read(ReadShared(schemaPath));
        KeyValueNode stats = SteamBinaryKeyValues.Read(ReadShared(statsPath));

        KeyValueNode? schemaStats = schema[appId]?["stats"];
        KeyValueNode? userStats = stats["cache"];

        if (schemaStats is null)
        {
            return null;
        }

        var achievements = new List<LocalAchievement>();

        foreach (KeyValueNode stat in schemaStats.Children)
        {
            KeyValueNode? bits = stat["bits"];

            if (bits is null)
            {
                continue;   // une statistique simple (compteur), pas un groupe de succès
            }

            KeyValueNode? userStat = userStats?[stat.Name];
            int unlockedBits = userStat?["data"]?.AsInt ?? 0;
            KeyValueNode? times = userStat?["AchievementTimes"];

            foreach (KeyValueNode bit in bits.Children)
            {
                if (!int.TryParse(bit.Name, out int bitNumber) || bitNumber is < 0 or > 31)
                {
                    continue;
                }

                string id = bit["name"]?.AsString ?? "";
                KeyValueNode? display = bit["display"];
                string name = Localized(display?["name"], language) ?? id;
                string description = Localized(display?["desc"], language) ?? "";

                // « (bits >> n) & 1 » : le n-ième bit du nombre, 1 = débloqué.
                bool isUnlocked = ((unlockedBits >> bitNumber) & 1) == 1;
                long unlockedUnix = times?[bit.Name]?.AsInt ?? 0;

                achievements.Add(new LocalAchievement(
                    id, name, description,
                    IsHidden: display?["hidden"]?.AsInt == 1,
                    IconUrl: SteamWebApi.BuildIconUrl(appId, display?["icon"]?.AsString),
                    isUnlocked, unlockedUnix));
            }
        }

        return achievements;
    }

    // Le texte dans la langue voulue, sinon en anglais. Certains jeux mettent directement un texte au lieu d'une liste de langues.
    private static string? Localized(KeyValueNode? node, string language)
    {
        return node?.AsString ?? node?[language]?.AsString ?? node?["english"]?.AsString;
    }

    // Steam peut être en train d'écrire le fichier : on le lit en laissant les autres programmes y accéder.
    private static byte[] ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}
