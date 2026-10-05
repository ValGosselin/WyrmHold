using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Wyrmhold.Core;

namespace WyrmHold.App;

public static class CoverLoader
{
    private const int MaxCachedImages = 300;

    private static readonly Dictionary<string, BitmapImage> Cache = new Dictionary<string, BitmapImage>();

    public static readonly DependencyProperty PathProperty =
        DependencyProperty.RegisterAttached(
            "Path",
            typeof(string),
            typeof(CoverLoader),
            new PropertyMetadata(null, OnPathChanged));

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

        BitmapImage? loadedImage = await Task.Run(() => LoadImage(path));

        if (loadedImage is null || GetPath(image) != path)
        {
            return;
        }

        if (Cache.Count >= MaxCachedImages)
        {
            Cache.Clear();
        }

        Cache[path] = loadedImage;
        image.Source = loadedImage;
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