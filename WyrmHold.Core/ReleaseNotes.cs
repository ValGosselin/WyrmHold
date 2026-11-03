namespace Wyrmhold.Core;

/// <summary>Les nouveautés d'une version : son numéro et son texte, sans le titre « ## 1.1.0 ».</summary>
public record ReleaseNote(Version Version, string Markdown);

/// <summary>
/// Lit notes-de-version.md, rangé dans la DLL : ce qu'apporte chaque version.
/// Le même fichier sert à publier.ps1, qui donne à Velopack la partie de la version publiée.
/// </summary>
public static class ReleaseNotes
{
    private const string ResourceName = "notes-de-version.md";

    // Du plus récent au plus ancien. Lu une seule fois, et seulement si quelqu'un en a besoin.
    private static readonly Lazy<List<ReleaseNote>> AllNotes = new Lazy<List<ReleaseNote>>(Load);

    private static List<ReleaseNote> Load()
    {
        using Stream? file = typeof(ReleaseNotes).Assembly.GetManifestResourceStream(ResourceName);

        if (file is null)
        {
            Logger.Log($"Notes de version introuvables dans l'appli ({ResourceName}).");
            return new List<ReleaseNote>();
        }

        using StreamReader reader = new StreamReader(file);
        return Parse(reader.ReadToEnd());
    }

    /// <summary>Découpe le fichier en parties « ## numéro ». Ce qui précède la première partie est ignoré.</summary>
    public static List<ReleaseNote> Parse(string text)
    {
        List<ReleaseNote> notes = new List<ReleaseNote>();
        Version? version = null;
        List<string> lines = new List<string>();

        void EndSection()
        {
            string markdown = string.Join("\n", lines).Trim();

            if (version is not null && markdown.Length > 0)
            {
                notes.Add(new ReleaseNote(version, markdown));
            }

            lines.Clear();
        }

        foreach (string line in text.Split('\n'))
        {
            // « ### Corrections » n'est pas concerné : son 3e caractère est un #, pas une espace.
            if (line.StartsWith("## "))
            {
                EndSection();
                version = Version.TryParse(line[3..].Trim(), out Version? parsed) ? parsed : null;
            }
            else
            {
                lines.Add(line.TrimEnd());
            }
        }

        EndSection();
        return notes.OrderByDescending(note => note.Version).ToList();
    }

    /// <summary>Toutes les notes jusqu'à la version de l'appli, pour les relire depuis « À propos ».</summary>
    public static List<ReleaseNote> UpTo(string currentVersion)
    {
        return Version.TryParse(currentVersion, out Version? current)
            ? AllNotes.Value.Where(note => note.Version <= current).ToList()
            : new List<ReleaseNote>();
    }

    /// <summary>
    /// Ce qu'une personne n'a pas encore vu, plusieurs versions si elle en a sauté.
    /// lastSeen vide = on ne sait pas d'où elle vient : seulement la version actuelle.
    /// </summary>
    public static List<ReleaseNote> Since(string? lastSeen, string currentVersion)
    {
        return Since(AllNotes.Value, lastSeen, currentVersion);
    }

    public static List<ReleaseNote> Since(List<ReleaseNote> notes, string? lastSeen, string currentVersion)
    {
        if (!Version.TryParse(currentVersion, out Version? current))
        {
            return new List<ReleaseNote>();
        }

        // Version de lastSeen illisible : traitée comme vide.
        return Version.TryParse(lastSeen, out Version? seen)
            ? notes.Where(note => note.Version > seen && note.Version <= current).ToList()
            : notes.Where(note => note.Version == current).ToList();
    }

    /// <summary>true si version est plus récente que other (un numéro illisible n'est jamais plus récent).</summary>
    public static bool IsNewer(string version, string other)
    {
        return Version.TryParse(version, out Version? first)
            && Version.TryParse(other, out Version? second)
            && first > second;
    }
}
