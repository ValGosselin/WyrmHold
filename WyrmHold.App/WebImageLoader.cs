using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Wyrmhold.Core;

namespace WyrmHold.App;

/// <summary>
/// Charge une image depuis une adresse web, en la téléchargeant nous-mêmes avec HttpClient
/// au lieu de laisser WPF le faire (le téléchargement intégré de WPF échoue sans rien dire).
/// Les images sont gardées sur le disque : chaque icône n'est téléchargée qu'une fois.
/// Utilisation en XAML : &lt;Image local:WebImageLoader.Url="{Binding IconUrl}"/&gt;
/// </summary>
public static class WebImageLoader
{
    private const int MaxCachedImages = 500;

    // Taille maximale du dossier d'images sur le disque. Au-delà, on supprime les images
    // les moins récemment utilisées jusqu'à redescendre à TrimmedCacheBytes (marge pour ne pas nettoyer sans arrêt).
    private const long MaxCacheBytes = 300L * 1024 * 1024;
    private const long TrimmedCacheBytes = 250L * 1024 * 1024;

    // On vérifie la taille au premier téléchargement de la session, puis tous les 100 téléchargements.
    private const int DownloadsBetweenChecks = 100;
    private static int _downloadsSinceCheck = DownloadsBetweenChecks;

    private static readonly HttpClient Http = CreateHttpClient();
    private static readonly string CacheFolder = Path.Combine(AppPaths.DataFolder, "WebImages");
    private static readonly Dictionary<string, BitmapImage> MemoryCache = new Dictionary<string, BitmapImage>();

    public static readonly DependencyProperty UrlProperty =
        DependencyProperty.RegisterAttached(
            "Url",
            typeof(string),
            typeof(WebImageLoader),
            new PropertyMetadata(null, OnUrlChanged));

    public static string? GetUrl(DependencyObject element)
    {
        return (string?)element.GetValue(UrlProperty);
    }

    public static void SetUrl(DependencyObject element, string? value)
    {
        element.SetValue(UrlProperty, value);
    }

    // Largeur (en pixels) à laquelle l'image est décodée : 96 par défaut, assez pour une icône.
    // Une capture d'écran en a besoin de plus, sinon elle serait floue.
    // Dans un DataTemplate, WPF peut donner l'adresse AVANT la largeur : quand la largeur change,
    // on recharge donc l'image (sinon elle resterait décodée en 96 pixels, puis étirée et floue).
    public static readonly DependencyProperty DecodeWidthProperty =
        DependencyProperty.RegisterAttached(
            "DecodeWidth",
            typeof(int),
            typeof(WebImageLoader),
            new PropertyMetadata(96, OnDecodeWidthChanged));

    public static int GetDecodeWidth(DependencyObject element)
    {
        return (int)element.GetValue(DecodeWidthProperty);
    }

    public static void SetDecodeWidth(DependencyObject element, int value)
    {
        element.SetValue(DecodeWidthProperty, value);
    }

    private static HttpClient CreateHttpClient()
    {
        HttpClient client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
        return client;
    }

    private static void OnUrlChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is Image image)
        {
            LoadIntoImage(image);
        }
    }

    private static void OnDecodeWidthChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        // Pas encore d'adresse : rien à recharger, la largeur sera lue quand l'adresse arrivera.
        if (element is Image image && !string.IsNullOrEmpty(GetUrl(image)))
        {
            LoadIntoImage(image);
        }
    }

    /// <summary>Affiche dans l'image l'adresse et la largeur qu'elle a en ce moment.</summary>
    private static async void LoadIntoImage(Image image)
    {
        string? url = GetUrl(image);
        image.Source = null;

        if (string.IsNullOrEmpty(url))
        {
            return;
        }

        // Une même image peut être demandée en petit et en grand : la clé de la mémoire contient la largeur.
        int decodeWidth = GetDecodeWidth(image);
        string memoryKey = $"{decodeWidth}|{url}";

        if (MemoryCache.TryGetValue(memoryKey, out BitmapImage? cachedImage))
        {
            image.Source = cachedImage;
            return;
        }

        BitmapImage? loadedImage = await LoadAsync(url, decodeWidth);

        // Pendant le téléchargement, la ligne a pu être réutilisée pour un autre succès (liste virtualisée),
        // ou la largeur a changé : un autre chargement, plus récent, s'occupe alors de l'image.
        if (loadedImage is null || GetUrl(image) != url || GetDecodeWidth(image) != decodeWidth)
        {
            return;
        }

        if (MemoryCache.Count >= MaxCachedImages)
        {
            MemoryCache.Clear();
        }

        MemoryCache[memoryKey] = loadedImage;
        image.Source = loadedImage;
    }

    private static async Task<BitmapImage?> LoadAsync(string url, int decodeWidth)
    {
        try
        {
            string path = Path.Combine(CacheFolder, GetCacheFileName(url));

            if (File.Exists(path))
            {
                byte[] cachedBytes = await File.ReadAllBytesAsync(path);
                MarkAsUsed(path);
                return await Task.Run(() => Decode(cachedBytes, decodeWidth));
            }

            byte[] bytes = await Http.GetByteArrayAsync(url);

            // On décode AVANT d'enregistrer : si ce n'est pas une image (page d'erreur…),
            // Decode lève une exception et le fichier n'est pas gardé.
            BitmapImage bitmap = await Task.Run(() => Decode(bytes, decodeWidth));

            Directory.CreateDirectory(CacheFolder);
            await File.WriteAllBytesAsync(path, bytes);

            if (++_downloadsSinceCheck >= DownloadsBetweenChecks)
            {
                _downloadsSinceCheck = 0;
                _ = Task.Run(TrimCache);   // en arrière-plan : l'image s'affiche sans attendre le ménage
            }

            return bitmap;
        }
        catch (Exception ex)
        {
            Logger.Log($"Image web impossible à charger ({url}) : {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Met la date du fichier à maintenant : l'image compte comme « utilisée récemment »
    /// et sera parmi les dernières supprimées par TrimCache.
    /// </summary>
    private static void MarkAsUsed(string path)
    {
        try
        {
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
        }
        catch (IOException)
        {
            // Pas grave : l'image risque juste d'être supprimée un peu plus tôt.
        }
    }

    /// <summary>
    /// Si le dossier d'images dépasse MaxCacheBytes, supprime les images les moins récemment utilisées
    /// jusqu'à redescendre à TrimmedCacheBytes. Une image supprimée sera simplement retéléchargée si besoin.
    /// </summary>
    private static void TrimCache()
    {
        try
        {
            List<FileInfo> files = new DirectoryInfo(CacheFolder).GetFiles()
                .OrderBy(file => file.LastWriteTimeUtc)   // les plus anciennes d'abord
                .ToList();

            long totalBytes = files.Sum(file => file.Length);

            if (totalBytes <= MaxCacheBytes)
            {
                return;
            }

            foreach (FileInfo file in files)
            {
                if (totalBytes <= TrimmedCacheBytes)
                {
                    break;
                }

                try
                {
                    long length = file.Length;
                    file.Delete();
                    totalBytes -= length;
                }
                catch (IOException)
                {
                    // Image en cours de lecture : on passe à la suivante.
                }
            }

            Logger.Log($"Cache d'images réduit à {totalBytes / (1024 * 1024)} Mo.");
        }
        catch (Exception ex)
        {
            Logger.Log($"Nettoyage du cache d'images impossible : {ex.Message}");
        }
    }

    private static BitmapImage Decode(byte[] bytes, int decodeWidth)
    {
        using MemoryStream stream = new MemoryStream(bytes);

        BitmapImage bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;   // lit tout de suite : le flux peut être fermé après
        bitmap.StreamSource = stream;
        bitmap.DecodePixelWidth = decodeWidth;
        bitmap.EndInit();
        bitmap.Freeze();                                  // utilisable depuis n'importe quel fil
        return bitmap;
    }

    /// <summary>
    /// Un nom de fichier unique par adresse : l'empreinte SHA-256 de l'adresse, plus l'extension de l'image.
    /// </summary>
    private static string GetCacheFileName(string url)
    {
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)));
        string extension = Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
            ? Path.GetExtension(uri.AbsolutePath)
            : "";

        return hash + extension;
    }
}
