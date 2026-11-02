using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Wyrmhold.Core;

namespace WyrmHold.App;

public static class CoverLoader
{
    // Une image décodée pèse environ 0,2 Mo : 600 images, environ 130 Mo.
    private const int MaxCachedImages = 600;

    private static readonly Dictionary<string, BitmapImage> Cache = new Dictionary<string, BitmapImage>();

    // L'ordre d'arrivée dans le cache : quand il est plein, seule la plus ancienne image s'en va.
    private static readonly Queue<string> CacheOrder = new Queue<string>();

    // Pas plus de 3 images décodées à la fois : le défilement garde du processeur.
    private static readonly SemaphoreSlim DecodeSlots = new SemaphoreSlim(3);

    // Le temps qu'une tuile doit rester sur le même jeu avant qu'on décode sa jaquette.
    private static readonly TimeSpan DecodeDelay = TimeSpan.FromMilliseconds(100);

    public static readonly DependencyProperty PathProperty =
        DependencyProperty.RegisterAttached(
            "Path",
            typeof(string),
            typeof(CoverLoader),
            new PropertyMetadata(null, OnPathChanged));

    /// <summary>
    /// Retire une image de la mémoire : la prochaine fois, elle sera relue sur le disque
    /// (après « Re-télécharger la jaquette », le fichier a le même nom mais une autre image).
    /// </summary>
    public static void Forget(string? path)
    {
        if (path is not null)
        {
            Cache.Remove(path);
        }
    }

    public static string? GetPath(DependencyObject element)
    {
        return (string?)element.GetValue(PathProperty);
    }

    public static void SetPath(DependencyObject element, string? value)
    {
        element.SetValue(PathProperty, value);
    }

    private static async void OnPathChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not Image image)
        {
            return;
        }

        string? path = e.NewValue as string;
        image.Source = null;

        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        if (Cache.TryGetValue(path, out BitmapImage? cachedImage))
        {
            image.Source = cachedImage;
            return;
        }

        // Barre tirée vite : une tuile change de jeu avant ce délai, on ne décode rien pour elle.
        await Task.Delay(DecodeDelay);

        if (GetPath(image) != path)
        {
            return;
        }

        BitmapImage? loadedImage;
        await DecodeSlots.WaitAsync();

        try
        {
            // Défilement rapide : pendant l'attente, la tuile a pu passer à un autre jeu. Inutile de décoder.
            if (GetPath(image) != path)
            {
                return;
            }

            // Une autre tuile a pu décoder la même image pendant l'attente.
            loadedImage = Cache.TryGetValue(path, out cachedImage)
                ? cachedImage
                : await Task.Run(() => LoadImage(path));
        }
        finally
        {
            DecodeSlots.Release();
        }

        if (loadedImage is null)
        {
            return;
        }

        // Gardée même si la tuile a changé de jeu : revenir en arrière n'aura pas à la décoder de nouveau.
        Remember(path, loadedImage);

        if (GetPath(image) == path)
        {
            image.Source = loadedImage;
        }
    }

    private static void Remember(string path, BitmapImage bitmap)
    {
        if (!Cache.ContainsKey(path))
        {
            if (Cache.Count >= MaxCachedImages)
            {
                Cache.Remove(CacheOrder.Dequeue());
            }

            CacheOrder.Enqueue(path);
        }

        Cache[path] = bitmap;
    }

    private static BitmapImage? LoadImage(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            BitmapImage bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path);
            bitmap.DecodePixelWidth = 200;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex)
        {
            Logger.Log($"Jaquette illisible {path} : {ex.Message}");
            return null;
        }
    }
}