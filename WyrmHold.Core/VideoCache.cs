using System.Security.Cryptography;
using System.Text;

namespace Wyrmhold.Core;

/// <summary>
/// Télécharge une vidéo sur le disque avant de la lire.
/// Le lecteur vidéo de WPF (MediaElement) refuse les adresses internet
/// (« Seuls les URI à en-tête pack du site d'origine sont pris en charge »), mais lit sans problème un fichier local.
/// On garde les dernières vidéos : relire un extrait déjà vu ne retélécharge rien.
/// </summary>
public static class VideoCache
{
    // Un extrait pèse environ 3 Mo : 20 extraits ≈ 60 Mo au plus sur le disque.
    private const int MaxCachedVideos = 20;

    private static readonly HttpClient Http = new HttpClient();
    private static readonly string CacheFolder = Path.Combine(AppPaths.DataFolder, "Videos");

    /// <summary>Le chemin du fichier local de la vidéo (téléchargée si besoin).</summary>
    public static async Task<string> GetLocalFileAsync(string url)
    {
        Directory.CreateDirectory(CacheFolder);

        string path = Path.Combine(CacheFolder, GetCacheFileName(url));

        if (File.Exists(path))
        {
            // On « touche » le fichier : il devient le plus récent, donc le dernier à être supprimé.
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
            return path;
        }

        byte[] bytes = await Http.GetByteArrayAsync(url);

        // On écrit d'abord dans un fichier temporaire, renommé à la fin :
        // un téléchargement interrompu ne laisse pas une vidéo à moitié écrite dans le cache.
        string temporaryPath = path + ".part";
        await File.WriteAllBytesAsync(temporaryPath, bytes);
        File.Move(temporaryPath, path, overwrite: true);

        RemoveOldVideos();
        return path;
    }

    /// <summary>Garde seulement les vidéos les plus récentes.</summary>
    private static void RemoveOldVideos()
    {
        IEnumerable<FileInfo> oldVideos = new DirectoryInfo(CacheFolder)
            .GetFiles("*.mp4")
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Skip(MaxCachedVideos);

        foreach (FileInfo video in oldVideos)
        {
            try
            {
                video.Delete();
            }
            catch (IOException)
            {
                // Vidéo en cours de lecture : on la supprimera une prochaine fois.
            }
        }
    }

    /// <summary>Un nom de fichier unique par adresse (empreinte SHA-256), comme pour le cache d'images.</summary>
    private static string GetCacheFileName(string url)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url))) + ".mp4";
    }
}
