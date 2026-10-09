namespace Wyrmhold.Core;

/// <summary>
/// Garde une trace des plantages sur le PC (dossier « crashes »), pour proposer de les signaler
/// au démarrage suivant. Rien n'est envoyé : le fichier reste local tant que l'utilisateur n'a rien décidé.
/// </summary>
public static class CrashReporter
{
    private static readonly string Folder = Path.Combine(AppPaths.DataFolder, "crashes");

    // Au-delà, les plus anciens sont supprimés (un plantage en boucle ne doit pas remplir le disque).
    private const int MaxCrashFiles = 10;

    /// <summary>
    /// Enregistre un plantage. Ne lance jamais d'erreur : on est déjà en train de planter.
    /// </summary>
    public static void Save(Exception exception, string source)
    {
        try
        {
            Directory.CreateDirectory(Folder);

            string text =
                $"Date : {DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}" +
                $"Version : {AppInfo.Version}{Environment.NewLine}" +
                $"Origine : {source}{Environment.NewLine}{Environment.NewLine}" +
                exception;   // ToString() : type, message, pile des appels et erreurs internes

            File.WriteAllText(Path.Combine(Folder, $"crash-{DateTime.Now:yyyyMMdd-HHmmss-fff}.txt"), text);
            Logger.Log($"PLANTAGE ({source}) : {exception.GetType().Name} : {exception.Message}");

            foreach (string old in GetCrashFiles().SkipLast(MaxCrashFiles))
            {
                File.Delete(old);
            }
        }
        catch
        {
            // Rien à faire de plus : on ne peut même pas écrire sur le disque.
        }
    }

    /// <summary>Le plantage le plus récent pas encore traité, ou null.</summary>
    public static string? GetPendingCrashFile()
    {
        return GetCrashFiles().LastOrDefault();
    }

    /// <summary>
    /// L'utilisateur a répondu (signaler ou non) : tous les plantages en attente sont supprimés,
    /// pour ne pas reposer la question à chaque démarrage.
    /// </summary>
    public static void ClearPending()
    {
        try
        {
            foreach (string path in GetCrashFiles())
            {
                File.Delete(path);
            }
        }
        catch (IOException ex)
        {
            Logger.Log($"Rapports de plantage impossibles à supprimer : {ex.Message}");
        }
    }

    // Du plus ancien au plus récent (le nom contient la date, donc l'ordre alphabétique suffit).
    private static List<string> GetCrashFiles()
    {
        return Directory.Exists(Folder)
            ? Directory.GetFiles(Folder, "crash-*.txt").OrderBy(path => path).ToList()
            : new List<string>();
    }
}
