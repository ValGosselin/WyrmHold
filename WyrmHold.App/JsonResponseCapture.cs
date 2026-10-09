using Microsoft.Web.WebView2.Core;

namespace WyrmHold.App;

/// <summary>
/// Écoute un navigateur WebView2 et attend la réponse JSON d'une adresse précise.
/// </summary>
public class JsonResponseCapture
{
    private readonly string _url;
    private readonly TaskCompletionSource<string> _jsonReceived = new TaskCompletionSource<string>();

    public JsonResponseCapture(CoreWebView2 webView, string url)
    {
        _url = url;
        webView.WebResourceResponseReceived += WebView_WebResourceResponseReceived;
    }

    public async Task<string?> WaitForJsonAsync(TimeSpan timeout)
    {
        Task finished = await Task.WhenAny(_jsonReceived.Task, Task.Delay(timeout));
        return finished == _jsonReceived.Task ? await _jsonReceived.Task : null;
    }

    private async void WebView_WebResourceResponseReceived(object? sender, CoreWebView2WebResourceResponseReceivedEventArgs e)
    {
        if (e.Request.Method != "GET"
            || !e.Request.Uri.StartsWith(_url, StringComparison.OrdinalIgnoreCase)
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