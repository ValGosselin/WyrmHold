using System.Web;
using Microsoft.Web.WebView2.Core;
using Wyrmhold.Core;

namespace WyrmHold.App;

/// <summary>
/// Écoute un navigateur WebView2 qui affiche la page « Games activity » d'Ubisoft,
/// et garde au passage les réponses des API appelées par cette page.
/// </summary>
public class UbisoftPageCapture
{
    public const string GamesActivityUrl = "https://www.ubisoft.com/en-gb/account/games-activity";

    private const string ApiServer = "https://public-ubiservices.ubi.com/";

    private readonly UbisoftCapture _capture = new UbisoftCapture();
    private readonly TaskCompletionSource<bool> _catalogReceived = new TaskCompletionSource<bool>();

    public UbisoftPageCapture(CoreWebView2 webView)
    {
        webView.WebResourceResponseReceived += WebView_WebResourceResponseReceived;
    }

    public async Task<UbisoftCapture?> WaitForDataAsync(TimeSpan timeout)
    {
        Task finished = await Task.WhenAny(_catalogReceived.Task, Task.Delay(timeout));

        if (finished != _catalogReceived.Task || _capture.GamesPlayedJson is null)
        {
            return null;
        }

        await Task.Delay(TimeSpan.FromSeconds(5));
        return _capture;
    }

    private async void WebView_WebResourceResponseReceived(object? sender, CoreWebView2WebResourceResponseReceivedEventArgs e)
    {
        string uri = e.Request.Uri;

        // Seules les vraies réponses (GET réussi) ont un contenu à lire. Les autres (requêtes OPTIONS que le
        // navigateur envoie avant chaque appel à un autre site, erreurs, redirections) n'en ont pas :
        // les lire remplissait le journal de « Lecture d'une réponse web impossible » (9 octobre 2026).
        if (!uri.StartsWith(ApiServer, StringComparison.OrdinalIgnoreCase)
            || e.Request.Method != "GET"
            || e.Response.StatusCode != 200)
        {
            return;
        }

        if (uri.Contains("/gamesplayed", StringComparison.OrdinalIgnoreCase))
        {
            _capture.GamesPlayedJson = await WebViewHelpers.ReadContentAsync(e.Response, uri);
        }
        else if (uri.Contains("/catalog?", StringComparison.OrdinalIgnoreCase))
        {
            string? json = await WebViewHelpers.ReadContentAsync(e.Response, uri);

            if (json is not null)
            {
                _capture.CatalogJsons.Add(json);
                _catalogReceived.TrySetResult(true);
            }
        }
        else if (uri.Contains("/stats?", StringComparison.OrdinalIgnoreCase))
        {
            string? spaceId = HttpUtility.ParseQueryString(new Uri(uri).Query)["spaceId"];
            string? json = await WebViewHelpers.ReadContentAsync(e.Response, uri);

            if (spaceId is not null && json is not null)
            {
                _capture.StatsJsonBySpaceId[spaceId] = json;
            }
        }
    }
}