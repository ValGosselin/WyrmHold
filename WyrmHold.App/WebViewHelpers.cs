using System.IO;
using Microsoft.Web.WebView2.Core;
using Wyrmhold.Core;

namespace WyrmHold.App;

public static class WebViewHelpers
{
    // uri : l'adresse de la requête, notée dans le journal si la lecture échoue.
    public static async Task<string?> ReadContentAsync(CoreWebView2WebResourceResponseView response, string? uri = null)
    {
        try
        {
            using Stream? content = await response.GetContentAsync();

            if (content is null)
            {
                return null;
            }

            using StreamReader reader = new StreamReader(content);
            return await reader.ReadToEndAsync();
        }
        catch (Exception ex)
        {
            // L'adresse (sans ses paramètres, qui peuvent contenir des identifiants) dit d'où vient l'erreur.
            Logger.Log($"Lecture d'une réponse web impossible ({response.StatusCode}{(uri is null ? "" : ", " + StripQuery(uri))}) : {ex.Message}");
            return null;
        }
    }

    // « https://site/chemin?id=…&token=… » → « https://site/chemin » : les paramètres restent hors du journal.
    private static string StripQuery(string uri)
    {
        int question = uri.IndexOf('?');
        return question < 0 ? uri : uri[..question];
    }
}