using System.IO;
using Microsoft.Web.WebView2.Core;
using Wyrmhold.Core;

namespace WyrmHold.App;

public static class WebViewHelpers
{
    public static async Task<string?> ReadContentAsync(CoreWebView2WebResourceResponseView response)
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
            Logger.Log($"Lecture d'une réponse web impossible : {ex.Message}");
            return null;
        }
    }
}