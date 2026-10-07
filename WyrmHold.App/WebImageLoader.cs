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

    private static HttpClient CreateHttpClient()
    {
        HttpClient client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
        return client;
    }

    private static async void OnUrlChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not Image image)
        {
            return;
        }

        string? url = e.NewValue as string;
        image.Source = null;

        if (string.IsNullOrEmpty(url))
        {
            return;
        }

        if (MemoryCache.TryGetValue(url, out BitmapImage? cachedImage))
        {
            image.Source = cachedImage;
            return;
        }

        BitmapImage? loadedImage = await LoadAsync(url);

        // Pendant le téléchargement, la ligne a pu être réutilisée pour un autre succès (liste virtualisée).
        if (loadedImage is null || GetUrl(image) != url)
        {
            return;
        }

        if (MemoryCache.Count >= MaxCachedImages)
        {
            MemoryCache.Clear();
        }

        MemoryCache[url] = loadedImage;
        image.Source = loadedImage;
    }

    private static async Task<BitmapImage?> LoadAsync(string url)
    {
        try
        {
            string path = Path.Combine(CacheFolder, GetCacheFileName(url));

            if (File.Exists(path))
            {
                byte[] cachedBytes = await File.ReadAllBytesAsync(path);
                return await Task.Run(() => Decode(cachedBytes));
            }

            byte[] bytes = await Http.GetByteArrayAsync(url);

            // On décode AVANT d'enregistrer : si ce n'est pas une image (page d'erreur…),
            // Decode lève une exception et le fichier n'est pas gardé.
            BitmapImage bitmap = await Task.Run(() => Decode(bytes));

            Directory.CreateDirectory(CacheFolder);
            await File.WriteAllBytesAsync(path, bytes);
            return bitmap;
        }
        catch (Exception ex)
        {
            Logger.Log($"Image web impossible à charger ({url}) : {ex.Message}");
            return null;
        }
    }

    private static BitmapImage Decode(byte[] bytes)
    {
        using MemoryStream stream = new MemoryStream(bytes);

        BitmapImage bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;   // lit tout de suite : le flux peut être fermé après
        bitmap.StreamSource = stream;
        bitmap.DecodePixelWidth = 96;
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
