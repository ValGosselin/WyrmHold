using System.Diagnostics;
using Wyrmhold.Core;

namespace WyrmHold.App;

/// <summary>
/// Ouvre une adresse dans le navigateur par défaut de Windows.
/// Même code que MainWindow.OpenInBrowser, mais utilisable depuis n'importe quel fichier.
/// </summary>
public static class BrowserHelper
{
    public static void Open(string url)
    {
        try
        {
            // UseShellExecute = true : Windows choisit le programme associé (ici, le navigateur).
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Logger.Log($"Impossible d'ouvrir {url} : {ex.Message}");
        }
    }
}
