using Microsoft.Web.WebView2.Core;
using Wyrmhold.Core;

namespace WyrmHold.App;

/// <summary>
/// Ouvre l'historique des commandes EA dans un navigateur WebView2
/// et attend la réponse JSON du site.
/// </summary>
public class EaOrderHistoryCapture
{
    private readonly TaskCompletionSource<string> _jsonReceived = new TaskCompletionSource<string>();

    public EaOrderHistoryCapture(CoreWebView2 webView)
    {
        webView.WebResourceResponseReceived += WebView_WebResourceResponseReceived;
    }

    public async Task<string?> WaitForJsonAsync(TimeSpan timeout)
    {
        Task finished = await Task.WhenAny(_jsonReceived.Task, Task.Delay(timeout));
        return finished == _jsonReceived.Task ? await _jsonReceived.Task : null;
    }

    private async void WebView_WebResourceResponseReceived(object? sender, CoreWebView2WebResourceResponseReceivedEventArgs e)
    {
        // Tant qu'on n'est pas connecté, EA répond par une redirection (302) vers sa page de connexion :
        // on n'écoute que la vraie réponse (200).
        if (!e.Request.Uri.StartsWith(EaOrderHistory.Url, StringComparison.OrdinalIgnoreCase)
            || e.Response.StatusCode != 200)
        {
            return;
        }

        string? json = await WebViewHelpers.ReadContentAsync(e.Response, e.Request.Uri);

        if (json is not null && (json.TrimStart().StartsWith('[') || json.TrimStart().StartsWith('{')))
        {
            _jsonReceived.TrySetResult(json);
        }
    }
}