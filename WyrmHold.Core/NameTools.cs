using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Wyrmhold.Core;

public static class NameTools
{
    // Au-delà, un identifiant est coupé : il sert aussi de nom de fichier pour la jaquette.
    private const int MaxSlugLength = 100;

    // Un Title ID Switch : 16 chiffres hexadécimaux commençant par 01 (ex. dans « [0100187003A36000] »).
    private static readonly Regex SwitchTitleId = new Regex(
        "(?<![0-9A-Fa-f])01[0-9A-Fa-f]{14}(?![0-9A-Fa-f])", RegexOptions.Compiled);

    /// <summary>
    /// Transforme un nom de fichier de jeu en nom lisible : retire les balises « (Europe) », « [!] », « {…} »,
    /// le Title ID et la version de fin, remplace les « _ », et remet l'article devant
    /// (« Legend of Zelda, The - Minish Cap » → « The Legend of Zelda - Minish Cap »).
    /// </summary>
    public static string CleanRomName(string fileName)
    {
        string name = Regex.Replace(fileName, @"\([^)]*\)|\[[^\]]*\]|\{[^}]*\}", " ");
        name = SwitchTitleId.Replace(name, " ");
        name = name.Replace('_', ' ');
        name = Regex.Replace(name, @"\s+[vV]\d+(\.\d+)*\s*$", " ");
        name = Regex.Replace(name, @"\s+", " ").Trim(' ', '-', '.');
        name = Regex.Replace(name, @"^(.+?), (The|A|An|Le|La|Les)\b(.*)$", "$2 $1$3");

        return name.Length > 0 ? name : fileName;
    }

    /// <summary>
    /// Le Title ID Switch écrit dans un nom de fichier, en minuscules, ou null s'il n'y en a pas.
    /// </summary>
    public static string? FindSwitchTitleId(string fileName)
    {
        Match match = SwitchTitleId.Match(fileName);
        return match.Success ? match.Value.ToLowerInvariant() : null;
    }

    /// <summary>
    /// Retire les accents : « Pokémon » → « Pokemon ».
    /// </summary>
    public static string RemoveDiacritics(string text)
    {
        // FormD sépare chaque lettre de son accent, qu'il ne reste plus qu'à enlever.
        IEnumerable<char> letters = text.Normalize(NormalizationForm.FormD)
            .Where(letter => CharUnicodeInfo.GetUnicodeCategory(letter) != UnicodeCategory.NonSpacingMark);

        return new string(letters.ToArray()).Normalize(NormalizationForm.FormC);
    }

    /// <summary>
    /// Un identifiant stable tiré d'un nom : minuscules, chiffres et tirets (« pokemon-emerald-version-usa »).
    /// Il ne dépend pas du dossier : déplacer une ROM ne fait perdre ni son temps de jeu ni ses collections.
    /// </summary>
    public static string ToSlug(string name)
    {
        string slug = Regex.Replace(RemoveDiacritics(name).ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');

        return slug.Length <= MaxSlugLength ? slug : slug.Substring(0, MaxSlugLength).TrimEnd('-');
    }


    public static string CleanForSearch(string name)
    {
        string cleaned = name.Replace("™", "").Replace("®", "").Replace("©", "").Trim();

        if (cleaned.EndsWith(" Demo", StringComparison.OrdinalIgnoreCase))
        {
            cleaned = cleaned.Substring(0, cleaned.Length - " Demo".Length).Trim();
        }

        return cleaned;
    }

    public static string Normalize(string name)
    {
        return new string(name.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
    }
}