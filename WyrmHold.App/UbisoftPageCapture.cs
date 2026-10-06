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

        if (!uri.StartsWith(ApiServer, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (uri.Contains("/gamesplayed", StringComparison.OrdinalIgnoreCase))
        {
            _capture.GamesPlayedJson = await WebViewHelpers.ReadContentAsync(e.Response);
        }
        else if (uri.Contains("/catalog?", StringComparison.OrdinalIgnoreCase))
        {
            string? json = await WebViewHelpers.ReadContentAsync(e.Response);

            if (json is not null)
            {
                _capture.CatalogJsons.Add(json);
                _catalogReceived.TrySetResult(true);
            }
        }
        else if (uri.Contains("/stats?", StringComparison.OrdinalIgnoreCase))
        {
            string? spaceId = HttpUtility.ParseQueryString(new Uri(uri).Query)["spaceId"];
            string? json = await WebViewHelpers.ReadContentAsync(e.Response);

            if (spaceId is not null && json is not null)
            {
                _capture.StatsJsonBySpaceId[spaceId] = json;
            }
        }
    }
}