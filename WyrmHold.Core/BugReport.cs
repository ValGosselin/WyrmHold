namespace Wyrmhold.Core;

/// <summary>
/// Un rapport de bug : la description de l'utilisateur, des infos sur le PC et l'appli,
/// les dernières lignes du journal et, après un plantage, son détail.
/// Tout est nettoyé par ReportSanitizer avant d'être montré : ce qu'on voit est exactement ce qui part.
/// </summary>
public class BugReport
{
    private const string IssuesUrl = AppInfo.RepositoryUrl + "/issues/new";

    // Vérifié le 9 octobre 2026 : GitHub accepte une adresse d'environ 6 000 caractères et la refuse
    // à 7 000 (erreur 500). On reste bien en dessous, car une personne non connectée est renvoyée
    // vers la page de connexion avec l'adresse entière dedans.
    private const int MaxUrlLength = 4000;

    private const int LogLineCount = 30;

    private readonly string _context;
    private readonly List<string> _logLines;
    private readonly string? _crashDetails;

    public string Description { get; set; } = "";

    public bool IsCrash => _crashDetails != null;

    private BugReport(string context, List<string> logLines, string? crashDetails)
    {
        _context = context;
        _logLines = logLines;
        _crashDetails = crashDetails;
    }

    /// <summary>
    /// Prépare un rapport. crashFile = le fichier d'un plantage (voir CrashReporter), ou null.
    /// </summary>
    public static BugReport Create(AppSettings settings, string? crashFile = null)
    {
        string context =
            $"- Version de Wyrmhold : {AppInfo.Version}\n" +
            $"- Système : {AppInfo.WindowsVersion}\n" +
            $"- {AppInfo.DotNetVersion}\n" +
            $"- Overlay : {(settings.OverlayEnabled ? "activé" : "désactivé")}\n" +
            $"- Sources de succès désactivées : {(settings.DisabledAchievementSources.Count == 0 ? "aucune" : string.Join(", ", settings.DisabledAchievementSources))}";

        List<string> logLines = Logger.ReadLastLines(LogLineCount)
            .Select(ReportSanitizer.Clean)
            .ToList();

        string? crash = null;

        if (crashFile != null)
        {
            try
            {
                crash = ReportSanitizer.Clean(File.ReadAllText(crashFile));
            }
            catch (IOException ex)
            {
                crash = $"(rapport de plantage illisible : {ex.Message})";
            }
        }

        return new BugReport(context, logLines, crash);
    }

    public string Title
    {
        get
        {
            // La première ligne de la description, coupée si elle est trop longue.
            string first = Description.Split('\n')[0].Trim();

            if (first.Length > 80)
            {
                first = first.Substring(0, 80) + "…";
            }

            if (first.Length == 0)
            {
                first = IsCrash ? "Plantage" : "Bug";
            }

            return $"[{AppInfo.Version}] {first}";
        }
    }

    /// <summary>Le rapport complet, en Markdown (pour « Copier le rapport » et l'aperçu).</summary>
    public string ToText()
    {
        return Build(_logLines.Count, includeCrash: true);
    }

    /// <summary>
    /// L'adresse de la page « nouveau ticket » de GitHub, déjà remplie.
    /// Si le rapport est trop long pour une adresse, on enlève des lignes du journal (les plus anciennes),
    /// puis le détail du plantage, jusqu'à ce que ça passe.
    /// </summary>
    public string GetGitHubUrl()
    {
        foreach (bool includeCrash in new[] { true, false })
        {
            // 30 lignes, puis 25, 20… jusqu'à 0.
            for (int lines = _logLines.Count; ; lines = Math.Max(lines - 5, 0))
            {
                string url = BuildUrl(Build(lines, includeCrash));

                if (url.Length <= MaxUrlLength)
                {
                    return url;
                }

                if (lines == 0)
                {
                    break;
                }
            }
        }

        // Même la description seule est trop longue : on la coupe.
        string shortBody = Description.Length > 1500 ? Description.Substring(0, 1500) + "…" : Description;
        return BuildUrl(shortBody + "\n\n" + _context);
    }

    private string BuildUrl(string body)
    {
        return $"{IssuesUrl}?title={Uri.EscapeDataString(Title)}&body={Uri.EscapeDataString(body)}";
    }

    private string Build(int logLineCount, bool includeCrash)
    {
        // Le nettoyage s'applique aussi à la description : un testeur peut y coller un chemin ou un e-mail.
        string description = ReportSanitizer.Clean(Description.Trim());

        var parts = new List<string>
        {
            "### Ce qui s'est passé",
            description.Length > 0 ? description : "(pas de description)",
            "",
            "### Contexte",
            _context
        };

        if (includeCrash && _crashDetails != null)
        {
            parts.Add("");
            parts.Add("### Plantage");
            parts.Add("```");
            parts.Add(_crashDetails.Trim());
            parts.Add("```");
        }

        if (logLineCount > 0 && _logLines.Count > 0)
        {
            parts.Add("");
            parts.Add($"### Dernières lignes du journal ({Math.Min(logLineCount, _logLines.Count)})");
            parts.Add("```");
            parts.AddRange(_logLines.TakeLast(logLineCount));
            parts.Add("```");
        }
        else if (_logLines.Count > 0)
        {
            parts.Add("");
            parts.Add("(Journal retiré : trop long pour le lien. Utiliser « Copier le rapport » pour l'avoir en entier.)");
        }

        return string.Join("\n", parts);
    }
}
