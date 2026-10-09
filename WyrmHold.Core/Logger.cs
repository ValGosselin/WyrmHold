namespace Wyrmhold.Core;

public static class Logger
{
    private static readonly string LogPath = Path.Combine(AppPaths.DataFolder, "wyrmhold.log");

    // Quand le journal dépasse 1 Mo, il devient « wyrmhold.old.log » (l'ancien est écrasé) et on repart de zéro :
    // le journal ne grossit plus sans limite, et on garde quand même l'historique récent.
    private static readonly string OldLogPath = Path.Combine(AppPaths.DataFolder, "wyrmhold.old.log");
    private const long MaxLogSize = 1024 * 1024;

    private static readonly object WriteLock = new object();

    public static void Log(string message)
    {
        lock (WriteLock)
        {
            try
            {
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length > MaxLogSize)
                {
                    File.Move(LogPath, OldLogPath, overwrite: true);
                }

                File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
            }
            catch (IOException)
            {
                // Journal verrouillé ou disque plein : on perd la ligne, mais l'appli ne plante pas pour ça.
            }
        }
    }

    /// <summary>
    /// Les dernières lignes du journal, pour un rapport de bug.
    /// Les lignes identiques qui se suivent (même message, heure différente) sont regroupées :
    /// « Lecture impossible (×23) » au lieu de 23 lignes pareilles.
    /// </summary>
    public static List<string> ReadLastLines(int count)
    {
        string[] lines;

        lock (WriteLock)
        {
            try
            {
                lines = File.Exists(LogPath) ? File.ReadAllLines(LogPath) : Array.Empty<string>();
            }
            catch (IOException)
            {
                return new List<string>();
            }
        }

        var grouped = new List<(string Line, string Message, int Repeat)>();

        foreach (string line in lines)
        {
            // Le message = la ligne sans sa date et son heure (« 2026-10-09 01:20:45 » = 19 caractères + espace).
            string message = line.Length > 20 ? line.Substring(20) : line;

            if (grouped.Count > 0 && grouped[^1].Message == message)
            {
                // On garde la date de la DERNIÈRE répétition.
                grouped[^1] = (line, message, grouped[^1].Repeat + 1);
            }
            else
            {
                grouped.Add((line, message, 1));
            }
        }

        return grouped
            .TakeLast(count)
            .Select(item => item.Repeat > 1 ? $"{item.Line} (×{item.Repeat})" : item.Line)
            .ToList();
    }
}
